using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Baseline;

/// <summary>
/// Summarises the comparison between a live diagnostic report and a stored baseline.
/// </summary>
public sealed class RegressionReport
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BaselineName { get; set; }
    public DateTime? BaselineCapturedAtUtc { get; set; }
    public DateTime ComparisonAtUtc { get; set; } = DateTime.UtcNow;
    public IList<RegressionFinding> Findings { get; } = new List<RegressionFinding>();

    public bool HasFindings => Findings.Count > 0;
}

/// <summary>
/// Represents a single regression found during baseline comparison.
/// </summary>
public sealed class RegressionFinding
{
    public RegressionSeverity Severity { get; set; } = RegressionSeverity.Warning;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? BaselineValue { get; set; }
    public string? CurrentValue { get; set; }
    public double? PercentageChange { get; set; }
}

public enum RegressionSeverity
{
    Info,
    Warning,
    Critical
}
