using System;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Reports;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class BaselineComparerTests
{
    [Fact]
    public void Compare_ProducesExpectedDeltas()
    {
        var baseline = new DiagnosticReport
        {
            Connection = new ConnectionMetrics
            {
                SuccessRate = 0.95,
                AverageConnectionTime = TimeSpan.FromMilliseconds(200)
            },
            Network = new LatencyMetrics { Average = TimeSpan.FromMilliseconds(40) },
            Server = new ServerMetrics
            {
                ResourceUsage = new ServerResourceUsage { CpuUtilizationPercent = 35 }
            }
        };

        var current = new DiagnosticReport
        {
            Connection = new ConnectionMetrics
            {
                SuccessRate = 0.80,
                AverageConnectionTime = TimeSpan.FromMilliseconds(350)
            },
            Network = new LatencyMetrics { Average = TimeSpan.FromMilliseconds(120) },
            Server = new ServerMetrics
            {
                ResourceUsage = new ServerResourceUsage { CpuUtilizationPercent = 55 }
            }
        };

        var comparison = BaselineComparer.Compare(current, baseline);

        Assert.True(comparison.HasRegressions);
        Assert.True(comparison.ConnectionSuccessRateDelta < 0);
        Assert.True(comparison.ConnectionLatencyDelta > TimeSpan.Zero);
        Assert.True(comparison.NetworkLatencyDelta > TimeSpan.Zero);
        Assert.True(comparison.ServerCpuDelta > 0);
        Assert.NotEmpty(comparison.Notes);
    }
}
