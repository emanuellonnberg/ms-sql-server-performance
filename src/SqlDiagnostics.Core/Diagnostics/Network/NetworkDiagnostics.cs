using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using SqlDiagnostics.Core.Models;

namespace SqlDiagnostics.Core.Diagnostics.Network;

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
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host must be provided.", nameof(host));
        }

        if (attempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        if (timeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
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

    public async Task<DnsMetrics> TestDnsResolutionAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new ArgumentException("Hostname must be provided.", nameof(hostname));
        }

        var metrics = new DnsMetrics();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await Dns.GetHostEntryAsync(hostname).ConfigureAwait(false);
            stopwatch.Stop();
            metrics.ResolutionTime = stopwatch.Elapsed;

            foreach (var address in entry.AddressList)
            {
                metrics.AddAddress(address.ToString());
            }
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            stopwatch.Stop();
            _logger?.LogWarning(ex, "DNS resolution for {Host} failed.", hostname);
            metrics.ResolutionTime = stopwatch.Elapsed;
            metrics.AddAddress($"Error: {ex.Message}");
        }

        return metrics;
    }

    public async Task<PortConnectivityResult> TestPortConnectivityAsync(
        string host,
        int port = 1433,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host must be provided.", nameof(host));
        }

        if (port <= 0 || port > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var timeoutValue = timeout.GetValueOrDefault(TimeSpan.FromSeconds(3));

        var result = new PortConnectivityResult();
        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connectTask = client.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(timeoutValue, cancellationToken);

            var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
            if (completed != connectTask)
            {
                stopwatch.Stop();
                result.IsAccessible = false;
                result.RoundtripTime = stopwatch.Elapsed;
                result.FailureReason = $"Timed out after {timeoutValue.TotalSeconds:N1}s";
                return result;
            }

            await connectTask.ConfigureAwait(false);
            stopwatch.Stop();
            result.IsAccessible = true;
            result.RoundtripTime = stopwatch.Elapsed;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException)
        {
            stopwatch.Stop();
            result.IsAccessible = false;
            result.RoundtripTime = stopwatch.Elapsed;
            result.FailureReason = ex.Message;
            _logger?.LogWarning(ex, "Port probe to {Host}:{Port} failed.", host, port);
        }

        return result;
    }

    public async Task<BandwidthMetrics> MeasureBandwidthAsync(
        SqlConnection connection,
        int testDurationSeconds = 10,
        CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (testDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(testDurationSeconds));
        }

        var metrics = new BandwidthMetrics();
        var duration = TimeSpan.FromSeconds(testDurationSeconds);

        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            connection.StatisticsEnabled = true;
            connection.ResetStatistics();

            var stopwatch = Stopwatch.StartNew();
            var iterations = 0;

            while (stopwatch.Elapsed < duration && !cancellationToken.IsCancellationRequested)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT TOP (1) name FROM sys.databases";
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                iterations++;
            }

            stopwatch.Stop();

            var stats = connection.RetrieveStatistics();
            var bytesSent = ExtractStatistic(stats, "BytesSent");
            var bytesReceived = ExtractStatistic(stats, "BytesReceived");

            metrics.Duration = stopwatch.Elapsed;
            metrics.IterationCount = iterations;
            metrics.BytesTransferred = bytesSent + bytesReceived;

            if (metrics.Duration > TimeSpan.Zero && metrics.BytesTransferred > 0)
            {
                var mb = metrics.BytesTransferred / 1024d / 1024d;
                metrics.MegabytesPerSecond = mb / metrics.Duration.TotalSeconds;
            }
        }
        catch (SqlException ex)
        {
            _logger?.LogWarning(ex, "Bandwidth measurement failed.");
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }

        return metrics;
    }

    private static long ExtractStatistic(System.Collections.IDictionary stats, string key) =>
        stats.Contains(key) && stats[key] is IConvertible convertible
            ? convertible.ToInt64(System.Globalization.CultureInfo.InvariantCulture)
            : 0;
}
