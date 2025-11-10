using System;
using System.Collections.Generic;
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
}
