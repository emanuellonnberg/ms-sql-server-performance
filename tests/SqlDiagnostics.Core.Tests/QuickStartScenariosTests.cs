using System;
using SqlDiagnostics.Core.Client;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Reports;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class QuickStartScenariosTests
{
    [Fact]
    public void ConnectionHealth_Defaults_EnableConnectionAndNetwork()
    {
        var options = QuickStartScenarios.ConnectionHealth();

        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Connection));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Network));
        Assert.True(options.IncludeConnectionPoolAnalysis);
        Assert.True(options.MonitorConnectionStability);
        Assert.True(options.IncludeDnsResolution);
        Assert.True(options.IncludePortProbe);
        Assert.False(options.MeasureNetworkBandwidth);
    }

    [Fact]
    public void ConnectionHealth_WithTimeout_OverridesTimeout()
    {
        var timeout = TimeSpan.FromSeconds(90);

        var options = QuickStartScenarios.ConnectionHealth(timeout: timeout);

        Assert.Equal(timeout, options.Timeout);
    }

    [Fact]
    public void PerformanceDeepDive_RequiresQuery()
    {
        Assert.Throws<ArgumentException>(() => QuickStartScenarios.PerformanceDeepDive(string.Empty));
    }

    [Fact]
    public void PerformanceDeepDive_SetsExpectedFlags()
    {
        var options = QuickStartScenarios.PerformanceDeepDive("SELECT 1", includePlans: true);

        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Connection));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Query));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Server));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Database));
        Assert.True(options.IncludeQueryPlans);
        Assert.True(options.DetectBlocking);
        Assert.True(options.CaptureWaitStatistics);
        Assert.Equal("SELECT 1", options.QueryToProfile);
        Assert.True(options.IncludeServerConfiguration);
    }

    [Fact]
    public void BaselineRegression_SetsBaselineAndCompareFlag()
    {
        var baseline = new DiagnosticReport();

        var options = QuickStartScenarios.BaselineRegression(baseline);

        Assert.Same(baseline, options.Baseline);
        Assert.True(options.CompareWithBaseline);
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Connection));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Network));
    }
}
