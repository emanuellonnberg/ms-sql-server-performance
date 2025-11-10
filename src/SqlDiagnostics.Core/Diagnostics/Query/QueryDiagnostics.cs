using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Models;
using Microsoft.Data.SqlClient;

namespace SqlDiagnostics.Diagnostics.Query;

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
}
