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
            .WithConnectionTests()
            .WithNetworkTests()
            .WithServerHealth()
            .Build();

        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Connection));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Network));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Server));
        Assert.True(options.Categories.HasFlag(DiagnosticCategories.Database));
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
}
