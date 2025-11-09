using System;
using System.Collections.Generic;
using SqlDiagnostics.Models;

namespace SqlDiagnostics.Reports;

/// <summary>
/// Aggregates results from the diagnostics subsystems in a coherent report.
/// </summary>
public sealed class DiagnosticReport
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string? TargetDataSource { get; set; }
    public ConnectionMetrics? Connection { get; set; }
    public LatencyMetrics? Network { get; set; }
    public QueryMetrics? Query { get; set; }
    public ServerMetrics? Server { get; set; }
    public IList<Recommendation> Recommendations { get; } = new List<Recommendation>();
    public IDictionary<string, object> Metadata { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Describes an actionable recommendation derived from observed metrics.
/// </summary>
public sealed class Recommendation
{
    public RecommendationSeverity Severity { get; set; } = RecommendationSeverity.Info;
    public string Category { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string RecommendationText { get; set; } = string.Empty;
    public string? ReferenceLink { get; set; }
}

public enum RecommendationSeverity
{
    Info,
    Warning,
    Critical
}
