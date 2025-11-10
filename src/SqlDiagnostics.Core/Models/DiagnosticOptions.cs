using System;
using SqlDiagnostics.Core.Reports;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Configurable options that control how diagnostics are executed.
/// </summary>
public sealed class DiagnosticOptions
{
    /// <summary>
    /// Gets or sets the overall timeout applied to long-running diagnostic operations.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the categories to execute.
    /// </summary>
    public DiagnosticCategories Categories { get; set; } = DiagnosticCategories.All;

    /// <summary>
    /// Gets or sets whether detailed query plans should be captured when executing query diagnostics.
    /// </summary>
    public bool IncludeQueryPlans { get; set; }

    /// <summary>
    /// Gets or sets whether recommendation rules should be evaluated after diagnostics complete.
    /// </summary>
    public bool GenerateRecommendations { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the resulting report should be compared against a baseline.
    /// </summary>
    public bool CompareWithBaseline { get; set; }

    /// <summary>
    /// Gets or sets the baseline report that subsequent diagnostics will be compared against.
    /// </summary>
    public DiagnosticReport? Baseline { get; set; }

    /// <summary>
    /// Creates a deep copy of the options instance.
    /// </summary>
    public DiagnosticOptions Clone() => new()
    {
        Timeout = Timeout,
        Categories = Categories,
        IncludeQueryPlans = IncludeQueryPlans,
        GenerateRecommendations = GenerateRecommendations,
        CompareWithBaseline = CompareWithBaseline,
        Baseline = Baseline
    };
}

/// <summary>
/// Fluent builder for <see cref="DiagnosticOptions"/>.
/// </summary>
public sealed class DiagnosticOptionsBuilder
{
    private readonly DiagnosticOptions _options = new();

    public DiagnosticOptionsBuilder WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        _options.Timeout = timeout;
        return this;
    }

    public DiagnosticOptionsBuilder WithCategories(DiagnosticCategories categories)
    {
        _options.Categories = categories;
        return this;
    }

    public DiagnosticOptionsBuilder WithConnectionTests()
    {
        _options.Categories |= DiagnosticCategories.Connection;
        return this;
    }

    public DiagnosticOptionsBuilder WithNetworkTests()
    {
        _options.Categories |= DiagnosticCategories.Network;
        return this;
    }

    public DiagnosticOptionsBuilder WithQueryAnalysis(bool includeQueryPlans = false)
    {
        _options.Categories |= DiagnosticCategories.Query;
        _options.IncludeQueryPlans = includeQueryPlans;
        return this;
    }

    public DiagnosticOptionsBuilder WithServerHealth()
    {
        _options.Categories |= DiagnosticCategories.Server;
        _options.Categories |= DiagnosticCategories.Database;
        return this;
    }

    public DiagnosticOptionsBuilder WithBaseline(DiagnosticReport baseline)
    {
        _options.Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _options.CompareWithBaseline = true;
        return this;
    }

    public DiagnosticOptionsBuilder WithoutRecommendations()
    {
        _options.GenerateRecommendations = false;
        return this;
    }

    public DiagnosticOptions Build() => _options.Clone();
}
