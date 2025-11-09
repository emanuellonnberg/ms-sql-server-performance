using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Models;

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
