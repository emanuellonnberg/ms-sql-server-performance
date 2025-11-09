using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Interfaces;
using SqlDiagnostics.Models;

namespace SqlDiagnostics.Diagnostics.Server;

/// <summary>
/// Executes lightweight DMV queries to capture server level metrics.
/// </summary>
public sealed class ServerDiagnostics : MetricCollectorBase<ServerMetrics>
{
    public ServerDiagnostics(ILogger? logger = null)
        : base(logger)
    {
    }

    public override bool IsSupported(SqlConnection connection) => connection != null;

    public override async Task<ServerMetrics> CollectAsync(SqlConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        var shouldClose = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            shouldClose = true;
        }

        try
        {
            var metrics = new ServerMetrics
            {
                ResourceUsage = await GatherResourceUsageAsync(connection, cancellationToken).ConfigureAwait(false)
            };

            var version = await ExecuteScalarAsync<string>(connection, "SELECT @@VERSION", cancellationToken).ConfigureAwait(false);
            metrics.AddProperty("sql_version_string", version ?? string.Empty);

            return metrics;
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private async Task<ServerResourceUsage> GatherResourceUsageAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var cpuTask = ExecuteReaderAsync(connection, CpuQuery, MapCpuUsageAsync, cancellationToken);
        var memoryTask = ExecuteReaderAsync(connection, MemoryQuery, MapMemoryUsageAsync, cancellationToken);
        var pleTask = ExecuteScalarAsync<double?>(connection, PleQuery, cancellationToken);
        var ioTask = ExecuteScalarAsync<long?>(connection, IoQuery, cancellationToken);

        await Task.WhenAll(cpuTask, memoryTask, pleTask, ioTask).ConfigureAwait(false);

        var usage = new ServerResourceUsage();

        if (cpuTask.Result != null)
        {
            usage.CpuUtilizationPercent = cpuTask.Result.Value.cpu;
            usage.SqlProcessUtilizationPercent = cpuTask.Result.Value.sqlCpu;
        }

        if (memoryTask.Result != null)
        {
            usage.TotalMemoryMb = memoryTask.Result.Value.total;
            usage.AvailableMemoryMb = memoryTask.Result.Value.available;
        }

        usage.PageLifeExpectancySeconds = pleTask.Result;
        usage.IoStallMs = ioTask.Result;

        return usage;
    }

    private static async Task<(double cpu, double sqlCpu)?> MapCpuUsageAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return (reader.GetDouble(0), reader.FieldCount > 1 ? reader.GetDouble(1) : 0d);
    }

    private static async Task<(double total, double available)?> MapMemoryUsageAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var total = Convert.ToDouble(reader["total_memory_mb"]);
        var available = Convert.ToDouble(reader["available_memory_mb"]);
        return (total, available);
    }

    private async Task<T?> ExecuteScalarAsync<T>(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result == null || result == DBNull.Value)
            {
                return default;
            }

            return (T)Convert.ChangeType(result, typeof(T))!;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult?> ExecuteReaderAsync<TResult>(
        SqlConnection connection,
        string sql,
        Func<SqlDataReader, CancellationToken, Task<TResult?>> map,
        CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            return await map(reader, cancellationToken).ConfigureAwait(false);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private const string CpuQuery = """
        SELECT
            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS cpu_usage,
            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/SQLProcessUtilization)[1]', 'int') AS sql_cpu_usage
        FROM (
            SELECT TOP 1 CONVERT(xml, record) AS record
            FROM sys.dm_os_ring_buffers
            WHERE ring_buffer_type = 'RING_BUFFER_SCHEDULER_MONITOR'
            ORDER BY timestamp DESC
        ) AS x;
        """;

    private const string MemoryQuery = """
        SELECT
            (total_physical_memory_kb / 1024.0) AS total_memory_mb,
            (available_physical_memory_kb / 1024.0) AS available_memory_mb
        FROM sys.dm_os_sys_memory;
        """;

    private const string PleQuery = """
        SELECT
            CAST(value_in_use AS float)
        FROM sys.configurations
        WHERE name = 'page life expectancy';
        """;

    private const string IoQuery = """
        SELECT SUM(io_stall) AS io_stall
        FROM sys.dm_io_virtual_file_stats(NULL, NULL);
        """;
}
