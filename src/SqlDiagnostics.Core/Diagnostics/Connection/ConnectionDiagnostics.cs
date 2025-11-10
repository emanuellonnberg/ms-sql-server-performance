using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Utilities;

namespace SqlDiagnostics.Core.Diagnostics.Connection;

/// <summary>
/// Performs repeated connection attempts to capture latency and reliability data.
/// </summary>
public sealed class ConnectionDiagnostics
{
    private readonly ILogger? _logger;

    public ConnectionDiagnostics(ILogger? logger = null)
    {
        _logger = logger;
    }

    public async Task<ConnectionMetrics> MeasureConnectionAsync(
        string connectionString,
        int attempts = 5,
        TimeSpan? delayBetweenAttempts = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        if (attempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        if (delayBetweenAttempts.HasValue && delayBetweenAttempts.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delayBetweenAttempts), "Delay between attempts must not be negative.");
        }

        var metrics = new ConnectionMetrics { TotalAttempts = attempts };
        var timings = new List<TimeSpan>(attempts);

        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var elapsed = await StopwatchHelper.MeasureAsync(async () =>
                {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                timings.Add(elapsed);
                metrics.SuccessfulAttempts++;
            }
            catch (SqlException ex)
            {
                metrics.FailedAttempts++;
                metrics.AddFailure(new ConnectionFailure
                {
                    Timestamp = DateTime.UtcNow,
                    ErrorMessage = ex.Message,
                    ErrorNumber = ex.Number,
                    Severity = ex.Class.ToString(),
                    ServerName = ex.Server
                });
                _logger?.LogWarning(ex, "Connection attempt {Attempt} failed with SQL error {Number}", i + 1, ex.Number);
            }
            catch (Exception ex)
            {
                metrics.FailedAttempts++;
                metrics.AddFailure(new ConnectionFailure
                {
                    Timestamp = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                });
                _logger?.LogError(ex, "Connection attempt {Attempt} failed with unexpected error", i + 1);
            }

            if (delayBetweenAttempts.HasValue && i < attempts - 1)
            {
                await Task.Delay(delayBetweenAttempts.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        if (timings.Count > 0)
        {
            metrics.AverageConnectionTime = TimeSpan.FromTicks((long)timings.Average(t => t.Ticks));
            metrics.MinConnectionTime = timings.Min();
            metrics.MaxConnectionTime = timings.Max();
        }

        metrics.SuccessRate = metrics.TotalAttempts > 0
            ? (double)metrics.SuccessfulAttempts / metrics.TotalAttempts
            : 0d;

        return metrics;
    }

    public Task<ConnectionPoolMetrics> AnalyzeConnectionPoolAsync(
        string connectionString,
        TimeSpan? sampleDuration = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        return AnalyzeConnectionPoolInternalAsync(connectionString, sampleDuration, cancellationToken);
    }

    public Task<ConnectionStabilityReport> MonitorConnectionStabilityAsync(
        string connectionString,
        TimeSpan? duration = null,
        TimeSpan? probeInterval = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        return MonitorConnectionStabilityInternalAsync(connectionString, duration, probeInterval, cancellationToken);
    }

    private async Task<ConnectionPoolMetrics> AnalyzeConnectionPoolInternalAsync(
        string connectionString,
        TimeSpan? sampleDuration,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var metrics = new ConnectionPoolMetrics
        {
            PoolingEnabled = builder.Pooling,
            MinPoolSize = builder.ContainsKey("Min Pool Size") ? builder.MinPoolSize : null,
            MaxPoolSize = builder.ContainsKey("Max Pool Size") ? builder.MaxPoolSize : null
        };

        if (!builder.Pooling)
        {
            metrics.Notes.Add("Connection pooling disabled.");
            return metrics;
        }

        var sampleWindow = sampleDuration.GetValueOrDefault(TimeSpan.FromSeconds(5));
        metrics.SampleDuration = sampleWindow;

        using var connection = new SqlConnection(builder.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = PoolingCountersQuery;

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var counterName = reader.GetString(0);
                var value = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);

                switch (counterName)
                {
                    case "NumberOfPooledConnections":
                        metrics.PooledConnectionCount = (int?)value;
                        break;
                    case "NumberOfActiveConnections":
                        metrics.ActiveConnectionCount = (int?)value;
                        break;
                    case "NumberOfFreeConnections":
                        metrics.FreeConnectionCount = (int?)value;
                        break;
                    case "NumberOfStasisConnections":
                        metrics.ReclaimedConnectionCount = (int?)value;
                        break;
                }
            }

            if (metrics.PooledConnectionCount is null &&
                metrics.ActiveConnectionCount is null &&
                metrics.FreeConnectionCount is null)
            {
                metrics.Notes.Add("Performance counter data unavailable. Ensure VIEW SERVER STATE permission is granted.");
            }
        }
        catch (SqlException ex)
        {
            _logger?.LogWarning(ex, "Unable to collect pool metrics.");
            metrics.Notes.Add($"Pool analysis unavailable: {ex.Message}");
        }

        return metrics;
    }

    private async Task<ConnectionStabilityReport> MonitorConnectionStabilityInternalAsync(
        string connectionString,
        TimeSpan? duration,
        TimeSpan? probeInterval,
        CancellationToken cancellationToken)
    {
        var interval = probeInterval.GetValueOrDefault(TimeSpan.FromSeconds(5));
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(probeInterval), "Probe interval must be positive.");
        }

        var report = new ConnectionStabilityReport
        {
            StartedAtUtc = DateTime.UtcNow
        };

        var endTime = duration.HasValue
            ? DateTime.UtcNow + duration.Value
            : DateTime.UtcNow + TimeSpan.FromMinutes(1);

        while (DateTime.UtcNow <= endTime)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timestamp = DateTime.UtcNow;
            try
            {
                var latency = await StopwatchHelper.MeasureAsync(async () =>
                {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                report.Samples.Add(new ConnectionStabilitySample(timestamp, true, latency));
                report.SuccessCount++;
            }
            catch (SqlException ex)
            {
                report.Samples.Add(new ConnectionStabilitySample(timestamp, false, TimeSpan.Zero, ex.Message));
                report.FailureCount++;
                _logger?.LogWarning(ex, "Stability probe failed with SQL error {Number}", ex.Number);
            }
            catch (Exception ex)
            {
                report.Samples.Add(new ConnectionStabilitySample(timestamp, false, TimeSpan.Zero, ex.Message));
                report.FailureCount++;
                _logger?.LogError(ex, "Stability probe failed with unexpected error.");
            }

            if (DateTime.UtcNow > endTime)
            {
                break;
            }

            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        report.CompletedAtUtc = DateTime.UtcNow;

        var successfulSamples = report.Samples.Where(s => s.Succeeded).Select(s => s.Latency).ToArray();
        if (successfulSamples.Length > 0)
        {
            report.AverageLatency = TimeSpan.FromTicks((long)successfulSamples.Average(s => s.Ticks));
            report.MinimumLatency = successfulSamples.Min();
            report.MaximumLatency = successfulSamples.Max();
        }

        return report;
    }

    private const string PoolingCountersQuery = """
        SELECT
            counter_name,
            cntr_value
        FROM sys.dm_os_performance_counters
        WHERE counter_name IN (
            'NumberOfPooledConnections',
            'NumberOfActiveConnections',
            'NumberOfFreeConnections',
            'NumberOfStasisConnections'
        )
        AND instance_name = ''
        """;
}
