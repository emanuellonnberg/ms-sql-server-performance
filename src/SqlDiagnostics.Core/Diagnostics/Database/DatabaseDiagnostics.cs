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
            207 or 208 or 2812 => true, // Missing column/object/stored proc (older servers without dm_db_log_stats)
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
            DatabaseId = SafeGetInt(reader, 0),
            Name = SafeGetString(reader, 1),
            State = SafeGetString(reader, 2),
            RecoveryModel = SafeGetString(reader, 3),
            CompatibilityLevel = SafeGetIntNullable(reader, 4),
            Containment = SafeGetString(reader, 5),
            LogReuseWait = SafeGetString(reader, 6),
            IsReadOnly = SafeGetBoolNullable(reader, 7),
            IsAutoClose = SafeGetBoolNullable(reader, 8),
            IsAutoShrink = SafeGetBoolNullable(reader, 9),
            DataFileSizeMb = SafeGetDoubleNullable(reader, 10),
            LogFileSizeMb = SafeGetDoubleNullable(reader, 11),
            ActiveSessionCount = SafeGetLongNullable(reader, 12),
            RunningRequestCount = SafeGetLongNullable(reader, 13),
            AggregateWaitTimeMs = SafeGetDoubleNullable(reader, 14),
            TotalLogSizeMb = SafeGetDoubleNullable(reader, 15),
            ActiveLogSizeMb = SafeGetDoubleNullable(reader, 16),
            LogUsedPercent = SafeGetDoubleNullable(reader, 17),
            LogSinceLastBackupMb = SafeGetDoubleNullable(reader, 18),
            LastLogBackupTime = SafeGetDateTimeNullable(reader, 19),
            LastCheckpointTime = SafeGetDateTimeNullable(reader, 20),
            LogTruncationHoldupReason = SafeGetString(reader, 21)
        };

        snapshot.AdditionalProperties["auto_create_stats"] = SafeGetBoolNullable(reader, 22);
        snapshot.AdditionalProperties["auto_update_stats"] = SafeGetBoolNullable(reader, 23);
        snapshot.AdditionalProperties["target_recovery_time_s"] = SafeGetIntNullable(reader, 24);

        return snapshot;
    }

    private static string? SafeGetString(SqlDataReader reader, int ordinal) =>
        ordinal >= 0 && ordinal < reader.FieldCount && !reader.IsDBNull(ordinal)
            ? reader.GetString(ordinal)
            : null;

    private static int SafeGetInt(SqlDataReader reader, int ordinal) =>
        ordinal >= 0 && ordinal < reader.FieldCount && !reader.IsDBNull(ordinal)
            ? Convert.ToInt32(reader.GetValue(ordinal))
            : 0;

    private static int? SafeGetIntNullable(SqlDataReader reader, int ordinal) =>
        ordinal >= 0 && ordinal < reader.FieldCount && !reader.IsDBNull(ordinal)
            ? Convert.ToInt32(reader.GetValue(ordinal))
            : (int?)null;

    private static long? SafeGetLongNullable(SqlDataReader reader, int ordinal) =>
        ordinal >= 0 && ordinal < reader.FieldCount && !reader.IsDBNull(ordinal)
            ? Convert.ToInt64(reader.GetValue(ordinal))
            : (long?)null;

    private static double? SafeGetDoubleNullable(SqlDataReader reader, int ordinal) =>
        ordinal >= 0 && ordinal < reader.FieldCount && !reader.IsDBNull(ordinal)
            ? Convert.ToDouble(reader.GetValue(ordinal))
            : (double?)null;

    private static bool? SafeGetBoolNullable(SqlDataReader reader, int ordinal) =>
        ordinal >= 0 && ordinal < reader.FieldCount && !reader.IsDBNull(ordinal)
            ? Convert.ToBoolean(reader.GetValue(ordinal))
            : (bool?)null;

    private static DateTime? SafeGetDateTimeNullable(SqlDataReader reader, int ordinal) =>
        ordinal >= 0 && ordinal < reader.FieldCount && !reader.IsDBNull(ordinal)
            ? Convert.ToDateTime(reader.GetValue(ordinal))
            : (DateTime?)null;

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
