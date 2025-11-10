using System;

namespace SqlDiagnostics.Core.Reports;

/// <summary>
/// Defines a rule that can produce actionable recommendations based on a diagnostic report.
/// </summary>
public interface IRecommendationRule
{
    /// <summary>
    /// Determines whether the rule applies to the provided report.
    /// </summary>
    bool Applies(DiagnosticReport report);

    /// <summary>
    /// Generates a recommendation to append to the report.
    /// </summary>
    Recommendation Generate(DiagnosticReport report);
}
