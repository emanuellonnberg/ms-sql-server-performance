using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Models;
using Microsoft.Data.SqlClient;

namespace SqlDiagnostics.Core.Diagnostics.Query;

/// <summary>
/// Wraps query execution to capture SQL client statistics.
/// </summary>
public sealed class QueryDiagnostics
{
    public async Task<QueryMetrics> ExecuteWithDiagnosticsAsync(
        SqlConnection connection,
        string query,
        CommandType commandType = CommandType.Text,
        Func<SqlParameter[]>? parametersFactory = null,
        CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query must be provided.", nameof(query));
        }

        var shouldClose = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            shouldClose = true;
        }

        connection.StatisticsEnabled = true;
        connection.ResetStatistics();

        var stopwatch = Stopwatch.StartNew();

        using var command = new SqlCommand(query, connection)
        {
            CommandType = commandType
        };

        if (parametersFactory is not null)
        {
            var parameters = parametersFactory();
            if (parameters is { Length: > 0 })
            {
                command.Parameters.AddRange(parameters);
            }
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        var rawStats = connection.RetrieveStatistics();
        if (shouldClose)
        {
            connection.Close();
        }

        return MapMetrics(stopwatch.Elapsed, rawStats);
    }

    public async Task<QueryPlanAnalysis> AnalyzeQueryPlanAsync(
        SqlConnection connection,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query must be provided.", nameof(query));
        }

        var analysis = new QueryPlanAnalysis();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var enableShowPlan = connection.CreateCommand();
            enableShowPlan.CommandText = "SET SHOWPLAN_XML ON;";
            await enableShowPlan.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            using var planCommand = connection.CreateCommand();
            planCommand.CommandText = query;

            var stopwatch = Stopwatch.StartNew();
            var result = await planCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            analysis.CollectionTime = stopwatch.Elapsed;
            analysis.PlanXml = result?.ToString();
        }
        catch (SqlException ex)
        {
            analysis.Warnings.Add(ex.Message);
        }
        finally
        {
            try
            {
                using var disableShowPlan = connection.CreateCommand();
                disableShowPlan.CommandText = "SET SHOWPLAN_XML OFF;";
                await disableShowPlan.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Ignore failures turning off SHOWPLAN.
            }

            if (shouldClose)
            {
                connection.Close();
            }
        }

        return analysis;
    }

    public async Task<BlockingReport> DetectBlockingAsync(
        SqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        var report = new BlockingReport();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = BlockingSessionsQuery;
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var session = new BlockingSession
                {
                    SessionId = reader.GetInt32(0),
                    BlockingSessionId = reader.GetInt32(1),
                    WaitType = reader.IsDBNull(2) ? null : reader.GetString(2),
                    WaitTime = TimeSpan.FromMilliseconds(reader.IsDBNull(3) ? 0 : reader.GetInt64(3)),
                    Status = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Command = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SqlText = reader.IsDBNull(6) ? null : reader.GetString(6)
                };

                report.Sessions.Add(session);
            }
        }
        catch (SqlException ex)
        {
            report.Warnings.Add(ex.Message);
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }

        return report;
    }

    public async Task<WaitStatistics> GetWaitStatisticsAsync(
        SqlConnection connection,
        WaitStatsScope scope = WaitStatsScope.Session,
        CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        var result = new WaitStatistics
        {
            Scope = scope
        };

        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = scope == WaitStatsScope.Session ? SessionWaitStatsQuery : ServerWaitStatsQuery;
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var entry = new WaitStatisticEntry
                {
                    WaitType = reader.GetString(0),
                    WaitTimeMilliseconds = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    SignalWaitTimeMilliseconds = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    WaitingTasks = reader.IsDBNull(3) ? 0 : reader.GetInt64(3)
                };

                result.Waits.Add(entry);
            }
        }
        catch (SqlException ex)
        {
            result.Waits.Add(new WaitStatisticEntry
            {
                WaitType = $"ERROR: {ex.Message}",
                WaitTimeMilliseconds = 0,
                SignalWaitTimeMilliseconds = 0,
                WaitingTasks = 0
            });
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }

        return result;
    }

    private static QueryMetrics MapMetrics(TimeSpan elapsed, System.Collections.IDictionary rawStats)
    {
        var metrics = new QueryMetrics
        {
            TotalExecutionTime = elapsed
        };

        CopyIfPresent(rawStats, "NetworkServerTime", value => metrics.NetworkTime = TimeSpan.FromMilliseconds(Convert.ToDouble(value)));
        CopyIfPresent(rawStats, "ExecutionTime", value => metrics.ServerTime = TimeSpan.FromMilliseconds(Convert.ToDouble(value)));
        CopyIfPresent(rawStats, "BytesSent", value => metrics.BytesSent = Convert.ToInt64(value));
        CopyIfPresent(rawStats, "BytesReceived", value => metrics.BytesReceived = Convert.ToInt64(value));
        CopyIfPresent(rawStats, "SelectRows", value => metrics.RowsReturned = Convert.ToInt64(value));
        CopyIfPresent(rawStats, "ServerRoundtrips", value => metrics.ServerRoundtrips = Convert.ToInt32(value));

        foreach (System.Collections.DictionaryEntry entry in rawStats)
        {
            var key = entry.Key?.ToString();
            if (!string.IsNullOrWhiteSpace(key))
            {
                metrics.AddStatistic(key, entry.Value);
            }
        }

        return metrics;
    }

    private static void CopyIfPresent(System.Collections.IDictionary stats, string key, Action<object> setter)
    {
        if (stats.Contains(key))
        {
            setter(stats[key]);
        }
    }

    private const string BlockingSessionsQuery = """
        SELECT
            req.session_id,
            req.blocking_session_id,
            req.wait_type,
            req.wait_time,
            req.status,
            req.command,
            SUBSTRING(text.text,
                (req.statement_start_offset / 2) + 1,
                (
                    (CASE req.statement_end_offset
                        WHEN -1 THEN DATALENGTH(text.text)
                        ELSE req.statement_end_offset
                    END - req.statement_start_offset) / 2
                ) + 1) AS sql_text
        FROM sys.dm_exec_requests AS req
        OUTER APPLY sys.dm_exec_sql_text(req.sql_handle) AS text
        WHERE req.blocking_session_id <> 0
        ORDER BY req.wait_time DESC;
        """;

    private const string SessionWaitStatsQuery = """
        SELECT
            wait_type,
            wait_time_ms,
            signal_wait_time_ms,
            waiting_tasks_count
        FROM sys.dm_exec_session_wait_stats
        WHERE session_id = @@SPID
        ORDER BY wait_time_ms DESC;
        """;

    private const string ServerWaitStatsQuery = """
        SELECT
            wait_type,
            wait_time_ms,
            signal_wait_time_ms,
            waiting_tasks_count
        FROM sys.dm_os_wait_stats
        WHERE waiting_tasks_count > 0
        AND wait_type NOT IN (
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
}
