using System;

namespace SqlDiagnostics.Core.Reports;

/// <summary>
/// Computes drift between diagnostic runs.
/// </summary>
public static class BaselineComparer
{
    private static readonly TimeSpan ConnectionLatencyRegressionThreshold = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan NetworkLatencyRegressionThreshold = TimeSpan.FromMilliseconds(50);
    private const double SuccessRateRegressionThreshold = 0.05;
    private const double CpuRegressionThreshold = 10.0;
    private const double HealthScoreRegressionThreshold = 5.0;

    public static BaselineComparisonResult Compare(DiagnosticReport current, DiagnosticReport baseline)
    {
        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        if (baseline is null)
        {
            throw new ArgumentNullException(nameof(baseline));
        }

        var result = new BaselineComparisonResult();

        if (current.Connection is { } currentConnection && baseline.Connection is { } baselineConnection)
        {
            result.ConnectionSuccessRateDelta = currentConnection.SuccessRate - baselineConnection.SuccessRate;

            if (currentConnection.AverageConnectionTime is { } currentAvg &&
                baselineConnection.AverageConnectionTime is { } baselineAvg)
            {
                result.ConnectionLatencyDelta = currentAvg - baselineAvg;
            }
        }

        if (current.Network is { Average: not null } currentNetwork &&
            baseline.Network is { Average: not null } baselineNetwork)
        {
            result.NetworkLatencyDelta = currentNetwork.Average.Value - baselineNetwork.Average.Value;
        }

        if (current.Server?.ResourceUsage is { CpuUtilizationPercent: { } currentCpu } &&
            baseline.Server?.ResourceUsage is { CpuUtilizationPercent: { } baselineCpu })
        {
            result.ServerCpuDelta = currentCpu - baselineCpu;
        }

        var currentScore = DiagnosticReportExtensions.GetHealthScore(current);
        var baselineScore = DiagnosticReportExtensions.GetHealthScore(baseline);
        result.HealthScoreDelta = currentScore - baselineScore;

        EvaluateRegression(result);

        return result;
    }

    private static void EvaluateRegression(BaselineComparisonResult result)
    {
        if (result.ConnectionSuccessRateDelta.HasValue &&
            result.ConnectionSuccessRateDelta.Value < -SuccessRateRegressionThreshold)
        {
            result.HasRegressions = true;
            result.Notes.Add($"Connection success rate dropped by {result.ConnectionSuccessRateDelta.Value:P1}.");
        }

        if (result.ConnectionLatencyDelta.HasValue &&
            result.ConnectionLatencyDelta.Value > ConnectionLatencyRegressionThreshold)
        {
            result.HasRegressions = true;
            result.Notes.Add($"Average connection time increased by {result.ConnectionLatencyDelta.Value.TotalMilliseconds:N0} ms.");
        }

        if (result.NetworkLatencyDelta.HasValue &&
            result.NetworkLatencyDelta.Value > NetworkLatencyRegressionThreshold)
        {
            result.HasRegressions = true;
            result.Notes.Add($"Network latency increased by {result.NetworkLatencyDelta.Value.TotalMilliseconds:N0} ms.");
        }

        if (result.ServerCpuDelta.HasValue &&
            result.ServerCpuDelta.Value > CpuRegressionThreshold)
        {
            result.HasRegressions = true;
            result.Notes.Add($"Server CPU utilisation increased by {result.ServerCpuDelta.Value:N1}%.");
        }

        if (result.HealthScoreDelta.HasValue &&
            result.HealthScoreDelta.Value < -HealthScoreRegressionThreshold)
        {
            result.HasRegressions = true;
            result.Notes.Add($"Overall health score dropped by {Math.Abs(result.HealthScoreDelta.Value):N0} points.");
        }
    }
}
