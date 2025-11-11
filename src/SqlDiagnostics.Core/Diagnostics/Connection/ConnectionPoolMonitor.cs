using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlDiagnostics.Core.Models;

namespace SqlDiagnostics.Core.Diagnostics.Connection;

/// <summary>
/// Monitors SQL connection pool health over a sampling window.
/// </summary>
public sealed class ConnectionPoolMonitor
{
    private readonly string _connectionString;
    private readonly Func<CancellationToken, Task<ConnectionPoolSnapshot>> _probe;

    public ConnectionPoolMonitor(string connectionString)
        : this(connectionString, null)
    {
    }

    public ConnectionPoolMonitor(string connectionString, Func<CancellationToken, Task<ConnectionPoolSnapshot>>? customProbe)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _probe = customProbe ?? DefaultProbeAsync;
    }

    /// <summary>
    /// Samples the pool repeatedly and returns a health report.
    /// </summary>
    public async Task<ConnectionPoolHealthReport> MonitorAsync(
        TimeSpan duration,
        TimeSpan sampleInterval,
        IProgress<ConnectionPoolSnapshot>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        }

        if (sampleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleInterval), "Sample interval must be positive.");
        }

        var snapshots = new List<ConnectionPoolSnapshot>();
        var end = DateTime.UtcNow + duration;

        while (DateTime.UtcNow <= end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConnectionPoolSnapshot snapshot;
            try
            {
                snapshot = await _probe(cancellationToken).ConfigureAwait(false);
                snapshot.TimestampUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                snapshot = new ConnectionPoolSnapshot
                {
                    TimestampUtc = DateTime.UtcNow,
                    Success = false,
                    Error = ex.Message
                };
            }

            snapshots.Add(snapshot);
            progress?.Report(snapshot);

            if (DateTime.UtcNow >= end)
            {
                break;
            }

            await Task.Delay(sampleInterval, cancellationToken).ConfigureAwait(false);
        }

        var report = new ConnectionPoolHealthReport
        {
            ConnectionStringHash = ComputeHash(_connectionString),
            StartedAtUtc = snapshots.FirstOrDefault()?.TimestampUtc ?? DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            Snapshots = snapshots,
            Summary = AnalyseSnapshots(snapshots)
        };

        return report;
    }

    private async Task<ConnectionPoolSnapshot> DefaultProbeAsync(CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(_connectionString);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT counter_name, cntr_value
            FROM sys.dm_os_performance_counters
            WHERE counter_name IN (
                'NumberOfActiveConnections',
                'NumberOfFreeConnections',
                'NumberOfPooledConnections'
            )
            AND instance_name = '';
        ";

        var snapshot = new ConnectionPoolSnapshot
        {
            Success = true,
            AcquisitionTime = stopwatch.Elapsed
        };

        using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.GetString(0);
                var value = reader.IsDBNull(1) ? (int?)null : Convert.ToInt32(reader.GetInt64(1));

                switch (name)
                {
                    case "NumberOfActiveConnections":
                        snapshot.ActiveConnections = value;
                        break;
                    case "NumberOfFreeConnections":
                        snapshot.FreeConnections = value;
                        break;
                    case "NumberOfPooledConnections":
                        snapshot.PooledConnections = value;
                        break;
                }
            }
        }

        return snapshot;
    }

    private static ConnectionPoolHealthSummary AnalyseSnapshots(IReadOnlyList<ConnectionPoolSnapshot> snapshots)
    {
        var summary = new ConnectionPoolHealthSummary();
        if (snapshots.Count == 0)
        {
            summary.Issues.Add("No samples were collected.");
            summary.Severity = ConnectionPoolIssueSeverity.Warning;
            return summary;
        }

        var failures = snapshots.Count(s => !s.Success);
        summary.FailureRate = snapshots.Count == 0 ? 0 : failures / (double)snapshots.Count;
        summary.AverageAcquisitionMilliseconds = snapshots.Where(s => s.Success).DefaultIfEmpty().Average(s => s?.AcquisitionTime.TotalMilliseconds ?? 0);

        if (summary.FailureRate > 0.1)
        {
            summary.Issues.Add($"Connection acquisition failures observed ({summary.FailureRate:P0}).");
            summary.Recommendations.Add("Inspect application code for undisposed connections.");
            summary.Severity = ConnectionPoolIssueSeverity.Critical;
        }

        if (summary.AverageAcquisitionMilliseconds > 1000)
        {
            summary.Issues.Add($"High average acquisition time {summary.AverageAcquisitionMilliseconds:N0} ms.");
            summary.Recommendations.Add("Increase Max Pool Size or optimise connection usage patterns.");
            summary.Severity = Max(summary.Severity, ConnectionPoolIssueSeverity.Warning);
        }

        if (snapshots.Any(s => s.FreeConnections.HasValue && s.FreeConnections.Value == 0 && (s.ActiveConnections ?? 0) > 0))
        {
            summary.Issues.Add("Pool reported no free connections while active sessions were present.");
            summary.Recommendations.Add("Consider using connection resiliency or adding retry logic.");
            summary.Severity = ConnectionPoolIssueSeverity.Critical;
        }

        if (summary.Issues.Count == 0)
        {
            summary.Severity = ConnectionPoolIssueSeverity.Healthy;
        }

        return summary;
    }

    private static ConnectionPoolIssueSeverity Max(ConnectionPoolIssueSeverity first, ConnectionPoolIssueSeverity second) =>
        (ConnectionPoolIssueSeverity)Math.Max((int)first, (int)second);

    private static string ComputeHash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(bytes);
    }
}
