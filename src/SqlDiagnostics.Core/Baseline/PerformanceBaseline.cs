using System;

namespace SqlDiagnostics.Core.Baseline;

/// <summary>
/// Captures aggregated diagnostics for a known-good reference state.
/// </summary>
public sealed class PerformanceBaseline
{
    public string Name { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string MachineName { get; set; } = Environment.MachineName;
    public string? ConnectionStringHash { get; set; }
    public int SampleCount { get; set; }
    public ConnectionBaselineMetrics Connection { get; set; } = new();
    public NetworkBaselineMetrics Network { get; set; } = new();
}

/// <summary>
/// Connection-related metrics that form part of a baseline.
/// </summary>
public sealed class ConnectionBaselineMetrics
{
    public double? MedianConnectionTimeMs { get; set; }
    public double? P95ConnectionTimeMs { get; set; }
    public double? P99ConnectionTimeMs { get; set; }
    public double? SuccessRate { get; set; }
}

/// <summary>
/// Network-related metrics that form part of a baseline.
/// </summary>
public sealed class NetworkBaselineMetrics
{
    public double? MedianLatencyMs { get; set; }
    public double? P95LatencyMs { get; set; }
    public double? P99LatencyMs { get; set; }
}
