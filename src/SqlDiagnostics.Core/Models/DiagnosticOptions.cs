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

    public bool IncludeConnectionPoolAnalysis { get; set; }

    public bool MonitorConnectionStability { get; set; }

    public TimeSpan ConnectionStabilityDuration { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan ConnectionStabilityProbeInterval { get; set; } = TimeSpan.FromSeconds(5);

    public bool IncludeDnsResolution { get; set; }

    public bool IncludePortProbe { get; set; }

    public bool MeasureNetworkBandwidth { get; set; }

    public bool DetectBlocking { get; set; }

    public bool CaptureWaitStatistics { get; set; }

    public WaitStatsScope WaitStatsScope { get; set; } = WaitStatsScope.Session;

    public string QueryToProfile { get; set; } = "SELECT 1";

    public bool IncludeServerConfiguration { get; set; }

    public bool IncludeServerStateProbe { get; set; }

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
        Baseline = Baseline,
        IncludeConnectionPoolAnalysis = IncludeConnectionPoolAnalysis,
        MonitorConnectionStability = MonitorConnectionStability,
        ConnectionStabilityDuration = ConnectionStabilityDuration,
        ConnectionStabilityProbeInterval = ConnectionStabilityProbeInterval,
        IncludeDnsResolution = IncludeDnsResolution,
        IncludePortProbe = IncludePortProbe,
        MeasureNetworkBandwidth = MeasureNetworkBandwidth,
        DetectBlocking = DetectBlocking,
        CaptureWaitStatistics = CaptureWaitStatistics,
        WaitStatsScope = WaitStatsScope,
        QueryToProfile = QueryToProfile,
        IncludeServerConfiguration = IncludeServerConfiguration,
        IncludeServerStateProbe = IncludeServerStateProbe
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

    public DiagnosticOptionsBuilder WithConnectionTests(
        bool includePoolAnalysis = false,
        bool monitorStability = false,
        TimeSpan? stabilityDuration = null,
        TimeSpan? stabilityInterval = null,
        bool includeServerStateProbe = true)
    {
        _options.Categories |= DiagnosticCategories.Connection;
        _options.IncludeConnectionPoolAnalysis = includePoolAnalysis;
        _options.MonitorConnectionStability = monitorStability;
        if (includeServerStateProbe)
        {
            _options.IncludeServerStateProbe = true;
        }
        if (stabilityDuration.HasValue)
        {
            _options.ConnectionStabilityDuration = stabilityDuration.Value;
        }

        if (stabilityInterval.HasValue)
        {
            _options.ConnectionStabilityProbeInterval = stabilityInterval.Value;
        }

        return this;
    }

    public DiagnosticOptionsBuilder WithNetworkTests(
        bool includeDns = true,
        bool includePortProbe = true,
        bool measureBandwidth = false)
    {
        _options.Categories |= DiagnosticCategories.Network;
        _options.IncludeDnsResolution = includeDns;
        _options.IncludePortProbe = includePortProbe;
        _options.MeasureNetworkBandwidth = measureBandwidth;
        return this;
    }

    public DiagnosticOptionsBuilder WithQueryAnalysis(
        bool includeQueryPlans = false,
        bool detectBlocking = true,
        bool captureWaitStats = true,
        WaitStatsScope waitStatsScope = WaitStatsScope.Session,
        string? sampleQuery = null)
    {
        _options.Categories |= DiagnosticCategories.Query;
        _options.IncludeQueryPlans = includeQueryPlans;
        _options.DetectBlocking = detectBlocking;
        _options.CaptureWaitStatistics = captureWaitStats;
        _options.WaitStatsScope = waitStatsScope;
        if (!string.IsNullOrWhiteSpace(sampleQuery))
        {
            _options.QueryToProfile = sampleQuery!;
        }
        return this;
    }

    public DiagnosticOptionsBuilder WithServerHealth(bool includeConfiguration = true)
    {
        _options.Categories |= DiagnosticCategories.Server;
        _options.Categories |= DiagnosticCategories.Database;
        _options.IncludeServerConfiguration = includeConfiguration;
        _options.IncludeServerStateProbe = true;
        return this;
    }

    public DiagnosticOptionsBuilder WithServerStateProbe()
    {
        _options.IncludeServerStateProbe = true;
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
