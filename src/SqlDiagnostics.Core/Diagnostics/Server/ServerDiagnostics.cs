using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Core.Interfaces;
using SqlDiagnostics.Core.Models;
using Microsoft.Data.SqlClient;

namespace SqlDiagnostics.Core.Diagnostics.Server;

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

            var waits = await GatherTopWaitStatsAsync(connection, cancellationToken).ConfigureAwait(false);
            foreach (var wait in waits)
            {
                metrics.Waits.Add(wait);
            }

            var counters = await GatherPerformanceCountersAsync(connection, cancellationToken).ConfigureAwait(false);
            foreach (var counter in counters)
            {
                metrics.PerformanceCounters.Add(counter);
            }

            var configuration = await GatherConfigurationAsync(connection, cancellationToken).ConfigureAwait(false);
            foreach (var setting in configuration)
            {
                metrics.Configuration.Add(setting);
            }

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
            var usage = new ServerResourceUsage();

            var cpuResult = await ExecuteReaderAsync(connection, CpuQuery, MapCpuUsageAsync, cancellationToken).ConfigureAwait(false);
            if (cpuResult != null)
            {
                usage.CpuUtilizationPercent = cpuResult.Value.cpu;
                usage.SqlProcessUtilizationPercent = cpuResult.Value.sqlCpu;
            }

            var memoryResult = await ExecuteReaderAsync(connection, MemoryQuery, MapMemoryUsageAsync, cancellationToken).ConfigureAwait(false);
            if (memoryResult != null)
            {
                usage.TotalMemoryMb = memoryResult.Value.total;
                usage.AvailableMemoryMb = memoryResult.Value.available;
            }

            usage.PageLifeExpectancySeconds = await ExecuteScalarAsync<double?>(connection, PleQuery, cancellationToken).ConfigureAwait(false);
            usage.IoStallMs = await ExecuteScalarAsync<long?>(connection, IoQuery, cancellationToken).ConfigureAwait(false);

            return usage;
        }

    private async Task<IReadOnlyList<WaitStatistic>> GatherTopWaitStatsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var result = await ExecuteReaderAsync(connection, WaitStatsQuery, MapWaitStatsAsync, cancellationToken).ConfigureAwait(false);
        return result ?? Array.Empty<WaitStatistic>();
    }

    private static async Task<IReadOnlyList<WaitStatistic>?> MapWaitStatsAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        var waits = new List<WaitStatistic>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var wait = new WaitStatistic
            {
                WaitType = reader.GetString(0),
                WaitTimeMs = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                SignalWaitTimeMs = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                WaitingTasksCount = reader.IsDBNull(3) ? 0 : reader.GetInt64(3)
            };
            waits.Add(wait);
        }

        return waits;
    }

    private async Task<IReadOnlyList<PerformanceCounterMetric>> GatherPerformanceCountersAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var result = await ExecuteReaderAsync(connection, PerformanceCountersQuery, MapPerformanceCountersAsync, cancellationToken).ConfigureAwait(false);
        return result ?? Array.Empty<PerformanceCounterMetric>();
    }

    private async Task<IReadOnlyList<ServerConfigurationSetting>> GatherConfigurationAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var result = await ExecuteReaderAsync(connection, ConfigurationQuery, MapConfigurationAsync, cancellationToken).ConfigureAwait(false);
        return result ?? Array.Empty<ServerConfigurationSetting>();
    }

    private static async Task<IReadOnlyList<PerformanceCounterMetric>?> MapPerformanceCountersAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        var counters = new List<PerformanceCounterMetric>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var counter = new PerformanceCounterMetric
            {
                ObjectName = reader.IsDBNull(0) ? null : reader.GetString(0),
                CounterName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                InstanceName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Value = reader.IsDBNull(3) ? double.NaN : Convert.ToDouble(reader.GetValue(3)),
                CounterType = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            };

            counters.Add(counter);
        }

        return counters;
    }

    private static async Task<IReadOnlyList<ServerConfigurationSetting>?> MapConfigurationAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        var settings = new List<ServerConfigurationSetting>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var setting = new ServerConfigurationSetting
            {
                Name = reader.GetString(0),
                Description = reader.IsDBNull(1) ? null : reader.GetString(1),
                Value = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                ValueInUse = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                IsAdvanced = !reader.IsDBNull(4) && reader.GetBoolean(4)
            };

            settings.Add(setting);
        }

        return settings;
    }

    private static async Task<(double cpu, double sqlCpu)?> MapCpuUsageAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var cpu = reader.IsDBNull(0) ? 0d : Convert.ToDouble(reader.GetValue(0));
        var sqlCpu = reader.FieldCount > 1 && !reader.IsDBNull(1)
            ? Convert.ToDouble(reader.GetValue(1))
            : 0d;
        return (cpu, sqlCpu);
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

    private const string WaitStatsQuery = """
        SELECT TOP (10)
            wait_type,
            wait_time_ms,
            signal_wait_time_ms,
            waiting_tasks_count
        FROM sys.dm_os_wait_stats
        WHERE wait_type NOT IN (
            'SLEEP_TASK',
            'SLEEP_SYSTEMTASK',
            'LAZYWRITER_SLEEP',
            'RESOURCE_QUEUE',
            'XE_TIMER_EVENT',
            'XE_DISPATCHER_WAIT',
            'FT_IFTS_SCHEDULER_IDLE_WAIT',
            'BROKER_TASK_STOP',
            'BROKER_TO_FLUSH',
            'SQLTRACE_BUFFER_FLUSH',
            'CLR_AUTO_EVENT',
            'CLR_MANUAL_EVENT',
            'REQUEST_FOR_DEADLOCK_SEARCH'
        )
        ORDER BY wait_time_ms DESC;
        """;

    private const string PerformanceCountersQuery = """
        WITH counters AS (
            SELECT
                object_name,
                counter_name,
                instance_name,
                cntr_value,
                cntr_type
            FROM sys.dm_os_performance_counters
            WHERE (
                object_name LIKE '%SQL Statistics%'
                AND counter_name IN ('Batch Requests/sec', 'SQL Compilations/sec', 'SQL Re-Compilations/sec')
            )
            OR (
                object_name LIKE '%Buffer Manager%'
                AND counter_name IN ('Page life expectancy', 'Page lookups/sec', 'Buffer cache hit ratio', 'Buffer cache hit ratio base')
            )
            OR (
                object_name LIKE '%General Statistics%'
                AND counter_name IN ('User Connections')
            )
        )
        SELECT
            c.object_name,
            c.counter_name,
            c.instance_name,
            CASE
                WHEN c.counter_name = 'Buffer cache hit ratio'
                    THEN CASE
                        WHEN b.cntr_value IS NULL OR b.cntr_value = 0 THEN NULL
                        ELSE (c.cntr_value * 100.0) / b.cntr_value
                    END
                ELSE CAST(c.cntr_value AS float)
            END AS cntr_value,
            c.cntr_type
        FROM counters AS c
        LEFT JOIN counters AS b
            ON c.counter_name = 'Buffer cache hit ratio'
            AND b.counter_name = 'Buffer cache hit ratio base'
            AND c.object_name = b.object_name
            AND ISNULL(c.instance_name, '') = ISNULL(b.instance_name, '')
        WHERE c.counter_name IN (
            'Batch Requests/sec',
            'SQL Compilations/sec',
            'SQL Re-Compilations/sec',
            'Page life expectancy',
            'Page lookups/sec',
            'Buffer cache hit ratio',
            'User Connections'
        );
        """;

    private const string ConfigurationQuery = """
        SELECT
            name,
            description,
            CAST(value AS float) AS configured_value,
            CAST(value_in_use AS float) AS value_in_use,
            is_advanced
        FROM sys.configurations
        WHERE value <> value_in_use
            OR name IN ('cost threshold for parallelism', 'max degree of parallelism', 'optimize for ad hoc workloads')
        ORDER BY name;
        """;
}
