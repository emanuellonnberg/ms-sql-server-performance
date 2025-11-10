using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Summarises the outcome of repeated connection attempts over time.
/// </summary>
public sealed class ConnectionStabilityReport
{
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

    public IList<ConnectionStabilitySample> Samples { get; } = new List<ConnectionStabilitySample>();

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public double SuccessRate => Samples.Count == 0
        ? 0d
        : SuccessCount / (double)Samples.Count;

    public TimeSpan? AverageLatency { get; set; }

    public TimeSpan? MinimumLatency { get; set; }

    public TimeSpan? MaximumLatency { get; set; }
}

/// <summary>
/// Represents an observation captured during a stability probe.
/// </summary>
public sealed class ConnectionStabilitySample
{
    public ConnectionStabilitySample(DateTime timestamp, bool succeeded, TimeSpan latency, string? errorMessage = null)
    {
        Timestamp = timestamp;
        Succeeded = succeeded;
        Latency = latency;
        ErrorMessage = errorMessage;
    }

    public DateTime Timestamp { get; }

    public bool Succeeded { get; }

    public TimeSpan Latency { get; }

    public string? ErrorMessage { get; }
}
