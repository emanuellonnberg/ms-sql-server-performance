using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Core.Interfaces;
using SqlDiagnostics.Core.Models;

namespace SqlDiagnostics.Core.Diagnostics.Database;

/// <summary>
/// Collects per-database state and usage metrics from SQL Server dynamic management views.
/// </summary>
public sealed class DatabaseDiagnostics : MetricCollectorBase<DatabaseMetrics>
{
    private readonly ILogger? _logger;

    public DatabaseDiagnostics(ILogger? logger = null)
        : base(logger)
    {
        _logger = logger;
    }

    public override bool IsSupported(SqlConnection connection) => connection != null;

    public override async Task<DatabaseMetrics> CollectAsync(SqlConnection connection, CancellationToken cancellationToken = default)
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
            var metrics = new DatabaseMetrics();
            var snapshots = await QueryDatabaseSnapshotsAsync(connection, cancellationToken).ConfigureAwait(false);
            foreach (var snapshot in snapshots)
            {
                metrics.Databases.Add(snapshot);
            }

            if (metrics.Databases.Count == 0)
            {
                metrics.Metadata["warning"] = "No databases returned by DMV queries. Ensure the login has VIEW SERVER STATE permission.";
            }

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

    private async Task<IList<DatabaseSnapshot>> QueryDatabaseSnapshotsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteSummaryQueryAsync(connection, DatabaseSummaryQueryWithLogStats, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex) when (IsLogStatsUnavailable(ex))
        {
            _logger?.LogDebug(ex, "sys.dm_db_log_stats unavailable; using reduced database metrics query.");
            return await ExecuteSummaryQueryAsync(connection, DatabaseSummaryQueryWithoutLogStats, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsLogStatsUnavailable(SqlException ex) =>
        ex.Number switch
        {
            208 or 2812 => true, // Invalid object or stored procedure (older servers without dm_db_log_stats)
            _ => false
        };

    private async Task<IList<DatabaseSnapshot>> ExecuteSummaryQueryAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var list = new List<DatabaseSnapshot>();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var snapshot = MapSnapshot(reader);
                list.Add(snapshot);
            }

            return (IList<DatabaseSnapshot>)list;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static DatabaseSnapshot MapSnapshot(SqlDataReader reader)
    {
        var snapshot = new DatabaseSnapshot
        {
            DatabaseId = reader.GetInt32(0),
            Name = reader.GetString(1),
            State = reader.IsDBNull(2) ? null : reader.GetString(2),
            RecoveryModel = reader.IsDBNull(3) ? null : reader.GetString(3),
            CompatibilityLevel = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Containment = reader.IsDBNull(5) ? null : reader.GetString(5),
            LogReuseWait = reader.IsDBNull(6) ? null : reader.GetString(6),
            IsReadOnly = reader.IsDBNull(7) ? null : reader.GetBoolean(7),
            IsAutoClose = reader.IsDBNull(8) ? null : reader.GetBoolean(8),
            IsAutoShrink = reader.IsDBNull(9) ? null : reader.GetBoolean(9),
            DataFileSizeMb = reader.IsDBNull(10) ? null : reader.GetDouble(10),
            LogFileSizeMb = reader.IsDBNull(11) ? null : reader.GetDouble(11),
            ActiveSessionCount = reader.IsDBNull(12) ? null : reader.GetInt64(12),
            RunningRequestCount = reader.IsDBNull(13) ? null : reader.GetInt64(13),
            AggregateWaitTimeMs = reader.IsDBNull(14) ? null : Convert.ToDouble(reader[14]),
            TotalLogSizeMb = reader.IsDBNull(15) ? null : reader.GetDouble(15),
            ActiveLogSizeMb = reader.IsDBNull(16) ? null : reader.GetDouble(16),
            LogUsedPercent = reader.IsDBNull(17) ? null : reader.GetDouble(17),
            LogSinceLastBackupMb = reader.IsDBNull(18) ? null : reader.GetDouble(18),
            LastLogBackupTime = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
            LastCheckpointTime = reader.IsDBNull(20) ? null : reader.GetDateTime(20),
            LogTruncationHoldupReason = reader.IsDBNull(21) ? null : reader.GetString(21)
        };

        snapshot.AdditionalProperties["auto_create_stats"] = reader.IsDBNull(22) ? null : reader.GetBoolean(22);
        snapshot.AdditionalProperties["auto_update_stats"] = reader.IsDBNull(23) ? null : reader.GetBoolean(23);
        snapshot.AdditionalProperties["target_recovery_time_s"] = reader.IsDBNull(24) ? null : reader.GetInt32(24);

        return snapshot;
    }

    private const string DatabaseSummaryQueryWithLogStats = """
WITH file_sizes AS (
    SELECT
        database_id,
        SUM(CASE WHEN type_desc = 'ROWS' THEN size END) * 8.0 / 1024 AS data_file_size_mb,
        SUM(CASE WHEN type_desc = 'LOG' THEN size END) * 8.0 / 1024 AS log_file_size_mb
    FROM sys.master_files
    GROUP BY database_id
),
session_counts AS (
    SELECT
        COALESCE(database_id, 0) AS database_id,
        COUNT_BIG(*) AS session_count
    FROM sys.dm_exec_sessions
    WHERE is_user_process = 1
    GROUP BY COALESCE(database_id, 0)
),
 request_counts AS (
    SELECT
        COALESCE(database_id, 0) AS database_id,
        COUNT_BIG(*) AS request_count,
        SUM(CAST(wait_time AS bigint)) AS total_wait_ms
    FROM sys.dm_exec_requests
    GROUP BY COALESCE(database_id, 0)
)
SELECT
    d.database_id,
    d.name,
    d.state_desc,
    d.recovery_model_desc,
    d.compatibility_level,
    d.containment_desc,
    d.log_reuse_wait_desc,
    d.is_read_only,
    d.is_auto_close_on,
    d.is_auto_shrink_on,
    fs.data_file_size_mb,
    fs.log_file_size_mb,
    sc.session_count,
    rc.request_count,
    rc.total_wait_ms,
    ls.total_log_size_mb,
    ls.active_log_size_mb,
    ls.percent_log_used,
    ls.log_since_last_log_backup_mb,
    ls.log_backup_time,
    ls.log_checkpoint_time,
    ls.log_truncation_holdup_reason,
    d.is_auto_create_stats_on,
    d.is_auto_update_stats_on,
    d.target_recovery_time_in_seconds
FROM sys.databases AS d
LEFT JOIN file_sizes AS fs ON fs.database_id = d.database_id
LEFT JOIN session_counts AS sc ON sc.database_id = d.database_id
LEFT JOIN request_counts AS rc ON rc.database_id = d.database_id
OUTER APPLY sys.dm_db_log_stats(d.database_id) AS ls
ORDER BY d.name;
""";

    private const string DatabaseSummaryQueryWithoutLogStats = """
WITH file_sizes AS (
    SELECT
        database_id,
        SUM(CASE WHEN type_desc = 'ROWS' THEN size END) * 8.0 / 1024 AS data_file_size_mb,
        SUM(CASE WHEN type_desc = 'LOG' THEN size END) * 8.0 / 1024 AS log_file_size_mb
    FROM sys.master_files
    GROUP BY database_id
),
session_counts AS (
    SELECT
        COALESCE(database_id, 0) AS database_id,
        COUNT_BIG(*) AS session_count
    FROM sys.dm_exec_sessions
    WHERE is_user_process = 1
    GROUP BY COALESCE(database_id, 0)
),
 request_counts AS (
    SELECT
        COALESCE(database_id, 0) AS database_id,
        COUNT_BIG(*) AS request_count,
        SUM(CAST(wait_time AS bigint)) AS total_wait_ms
    FROM sys.dm_exec_requests
    GROUP BY COALESCE(database_id, 0)
)
SELECT
    d.database_id,
    d.name,
    d.state_desc,
    d.recovery_model_desc,
    d.compatibility_level,
    d.containment_desc,
    d.log_reuse_wait_desc,
    d.is_read_only,
    d.is_auto_close_on,
    d.is_auto_shrink_on,
    fs.data_file_size_mb,
    fs.log_file_size_mb,
    sc.session_count,
    rc.request_count,
    rc.total_wait_ms,
    CAST(NULL AS decimal(18,2)) AS total_log_size_mb,
    CAST(NULL AS decimal(18,2)) AS active_log_size_mb,
    CAST(NULL AS decimal(18,2)) AS percent_log_used,
    CAST(NULL AS decimal(18,2)) AS log_since_last_log_backup_mb,
    CAST(NULL AS datetime2(3)) AS log_backup_time,
    CAST(NULL AS datetime2(3)) AS log_checkpoint_time,
    CAST(NULL AS nvarchar(256)) AS log_truncation_holdup_reason,
    d.is_auto_create_stats_on,
    d.is_auto_update_stats_on,
    d.target_recovery_time_in_seconds
FROM sys.databases AS d
LEFT JOIN file_sizes AS fs ON fs.database_id = d.database_id
LEFT JOIN session_counts AS sc ON sc.database_id = d.database_id
LEFT JOIN request_counts AS rc ON rc.database_id = d.database_id
ORDER BY d.name;
""";
}
