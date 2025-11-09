using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Models;

namespace SqlDiagnostics.Diagnostics.Network;

/// <summary>
/// Performs ICMP ping probes to assess network latency and jitter.
/// </summary>
public sealed class NetworkDiagnostics
{
    private readonly ILogger? _logger;

    public NetworkDiagnostics(ILogger? logger = null)
    {
        _logger = logger;
    }

    public async Task<LatencyMetrics> MeasureLatencyAsync(
        string host,
        int attempts = 5,
        int timeoutMilliseconds = 5_000,
        CancellationToken cancellationToken = default)
    {
        if (attempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        var metrics = new LatencyMetrics();
        var values = new double[attempts];
        var successCount = 0;

        using var ping = new Ping();

        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, timeoutMilliseconds).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    metrics.AddSample(new LatencySample(DateTime.UtcNow, TimeSpan.FromMilliseconds(reply.RoundtripTime), true));
                    values[successCount++] = reply.RoundtripTime;
                }
                else
                {
                    metrics.AddSample(new LatencySample(DateTime.UtcNow, TimeSpan.FromMilliseconds(reply.RoundtripTime), false, reply.Status.ToString()));
                    _logger?.LogWarning("Ping attempt {Attempt} to {Host} returned status {Status}", i + 1, host, reply.Status);
                }
            }
            catch (PingException ex)
            {
                metrics.AddSample(new LatencySample(DateTime.UtcNow, TimeSpan.Zero, false, ex.Message));
                _logger?.LogWarning(ex, "Ping attempt {Attempt} to {Host} failed", i + 1, host);
            }
        }

        if (successCount > 0)
        {
            var data = values.Take(successCount).ToArray();
            var average = data.Average();
            metrics.Average = TimeSpan.FromMilliseconds(average);
            metrics.Min = TimeSpan.FromMilliseconds(data.Min());
            metrics.Max = TimeSpan.FromMilliseconds(data.Max());
            var variance = data.Sum(v => Math.Pow(v - average, 2)) / data.Length;
            metrics.Jitter = TimeSpan.FromMilliseconds(Math.Sqrt(variance));
        }

        return metrics;
    }
}
