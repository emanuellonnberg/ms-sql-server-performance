using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Captures host reachability and latency information.
/// </summary>
public sealed class LatencyMetrics
{
    public TimeSpan? Average { get; set; }
    public TimeSpan? Min { get; set; }
    public TimeSpan? Max { get; set; }
    public TimeSpan? Jitter { get; set; }
    public IReadOnlyList<LatencySample> Samples => _samples;

    private readonly List<LatencySample> _samples = new();

    public void AddSample(LatencySample sample) => _samples.Add(sample);
}

/// <summary>
/// Represents an individual latency measurement.
/// </summary>
public readonly struct LatencySample
{
    public LatencySample(DateTime timestamp, TimeSpan elapsed, bool successful, string? error = null)
    {
        Timestamp = timestamp;
        Elapsed = elapsed;
        Successful = successful;
        Error = error;
    }

    public DateTime Timestamp { get; }
    public TimeSpan Elapsed { get; }
    public bool Successful { get; }
    public string? Error { get; }
}

/// <summary>
/// Represents the outcome of a DNS resolution attempt.
/// </summary>
public sealed class DnsMetrics
{
    public TimeSpan? ResolutionTime { get; set; }

    public IReadOnlyList<string> Addresses => _addresses;

    private readonly List<string> _addresses = new();

    public void AddAddress(string address)
    {
        if (!string.IsNullOrWhiteSpace(address))
        {
            _addresses.Add(address);
        }
    }
}

/// <summary>
/// Represents the result of probing a TCP port for connectivity.
/// </summary>
public sealed class PortConnectivityResult
{
    public bool IsAccessible { get; set; }

    public TimeSpan? RoundtripTime { get; set; }

    public string? FailureReason { get; set; }
}

/// <summary>
/// Represents measured throughput against a SQL Server endpoint.
/// </summary>
public sealed class BandwidthMetrics
{
    public TimeSpan Duration { get; set; }

    public long BytesTransferred { get; set; }

    public double? MegabytesPerSecond { get; set; }

    public int IterationCount { get; set; }
}
