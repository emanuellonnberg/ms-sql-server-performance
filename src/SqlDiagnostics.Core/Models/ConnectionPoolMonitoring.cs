using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Represents a snapshot of connection pool activity.
/// </summary>
public sealed class ConnectionPoolSnapshot
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public TimeSpan AcquisitionTime { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? ActiveConnections { get; set; }
    public int? FreeConnections { get; set; }
    public int? PooledConnections { get; set; }
}

/// <summary>
/// Aggregated analysis after monitoring the pool.
/// </summary>
public sealed class ConnectionPoolHealthReport
{
    public string? ConnectionStringHash { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public IReadOnlyList<ConnectionPoolSnapshot> Snapshots { get; set; } = Array.Empty<ConnectionPoolSnapshot>();
    public ConnectionPoolHealthSummary Summary { get; set; } = new();
}

/// <summary>
/// High-level summary of pool health.
/// </summary>
public sealed class ConnectionPoolHealthSummary
{
    public ConnectionPoolIssueSeverity Severity { get; set; } = ConnectionPoolIssueSeverity.Healthy;
    public double FailureRate { get; set; }
    public double AverageAcquisitionMilliseconds { get; set; }
    public IList<string> Issues { get; } = new List<string>();
    public IList<string> Recommendations { get; } = new List<string>();
}

/// <summary>
/// Severity levels for pool health analysis.
/// </summary>
public enum ConnectionPoolIssueSeverity
{
    Healthy,
    Warning,
    Critical
}
