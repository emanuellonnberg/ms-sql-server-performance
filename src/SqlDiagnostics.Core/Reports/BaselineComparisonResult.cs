using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Reports;

/// <summary>
/// Summarises drift between a new diagnostic report and its baseline.
/// </summary>
public sealed class BaselineComparisonResult
{
    public double? HealthScoreDelta { get; set; }

    public double? ConnectionSuccessRateDelta { get; set; }

    public TimeSpan? ConnectionLatencyDelta { get; set; }

    public TimeSpan? NetworkLatencyDelta { get; set; }

    public double? ServerCpuDelta { get; set; }

    public IList<string> Notes { get; } = new List<string>();

    public bool HasRegressions { get; set; }
}
