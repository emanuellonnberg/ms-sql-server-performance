using System;
using SqlDiagnostics.Core;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Reports;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class DiagnosticOptionsBuilderTests
{
    [Fact]
    public void WithTimeout_WithNonPositive_Throws()
    {
        var builder = new DiagnosticOptionsBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.Zero));
    }

    [Fact]
    public void Build_WithConnectionAndNetwork_SetsExpectedCategories()
    {
        var options = new DiagnosticOptionsBuilder()
            .WithConnectionTests(includePoolAnalysis: true, monitorStability: true)
            .WithNetworkTests(includeDns: true, includePortProbe: true, measureBandwidth: true)
            .WithQueryAnalysis(includeQueryPlans: true, detectBlocking: true, captureWaitStats: true, sampleQuery: "SELECT 42")
            .WithServerHealth(includeConfiguration: true)
            .Build();

        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Connection));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Network));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Server));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Database));
        Assert.True(options.IncludeConnectionPoolAnalysis);
        Assert.True(options.MonitorConnectionStability);
        Assert.True(options.IncludeDnsResolution);
        Assert.True(options.IncludePortProbe);
        Assert.True(options.MeasureNetworkBandwidth);
        Assert.True(options.IncludeQueryPlans);
        Assert.True(options.DetectBlocking);
        Assert.True(options.CaptureWaitStatistics);
        Assert.Equal("SELECT 42", options.QueryToProfile);
        Assert.True(options.IncludeServerConfiguration);
        Assert.True(options.IncludeServerStateProbe);
    }

    [Fact]
    public void WithBaseline_SetsBaselineAndCompareFlag()
    {
        var baseline = new DiagnosticReport();

        var options = new DiagnosticOptionsBuilder()
            .WithBaseline(baseline)
            .Build();

        Assert.Same(baseline, options.Baseline);
        Assert.True(options.CompareWithBaseline);
}

    [Fact]
    public void WithConnectionTests_CanDisableServerStateProbe()
    {
        var options = new DiagnosticOptionsBuilder()
            .WithConnectionTests(includeServerStateProbe: false)
            .Build();

        Assert.False(options.IncludeServerStateProbe);
    }

    [Fact]
    public void WithServerStateProbe_SetsFlag()
    {
        var options = new DiagnosticOptionsBuilder()
            .WithServerStateProbe()
            .Build();

        Assert.True(options.IncludeServerStateProbe);
    }
}
