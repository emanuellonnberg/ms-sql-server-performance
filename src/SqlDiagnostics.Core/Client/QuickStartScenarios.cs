using System;
using SqlDiagnostics.Core.Models;

namespace SqlDiagnostics.Core.Client;

/// <summary>
/// Opinionated presets that make it easy to get started with <see cref="SqlDiagnosticsClient"/>.
/// </summary>
public static class QuickStartScenarios
{
    /// <summary>
    /// Creates an options preset that focuses on connection reliability and network reachability.
    /// </summary>
    /// <param name="timeout">
    /// Optional overall timeout for the diagnostic run. Defaults to the library default if omitted.
    /// </param>
    /// <param name="includePoolAnalysis">
    /// When true, gathers connection pool metrics in addition to connection timing.
    /// </param>
    /// <param name="monitorStability">
    /// When true, performs a short connection stability probe to surface intermittent failures.
    /// </param>
    /// <returns>A configured <see cref="DiagnosticOptions"/> instance.</returns>
    public static DiagnosticOptions ConnectionHealth(
        TimeSpan? timeout = null,
        bool includePoolAnalysis = true,
        bool monitorStability = true)
    {
        var builder = new DiagnosticOptionsBuilder();

        if (timeout.HasValue)
        {
            builder.WithTimeout(timeout.Value);
        }

        builder.WithConnectionTests(
                includePoolAnalysis: includePoolAnalysis,
                monitorStability: monitorStability,
                stabilityDuration: TimeSpan.FromMinutes(2),
                stabilityInterval: TimeSpan.FromSeconds(10))
            .WithNetworkTests(includeDns: true, includePortProbe: true, measureBandwidth: false);

        return builder.Build();
    }

    /// <summary>
    /// Creates an options preset for analysing a specific workload, including query and server diagnostics.
    /// </summary>
    /// <param name="queryText">The query or stored procedure to profile.</param>
    /// <param name="includePlans">When true, captures the execution plan XML.</param>
    /// <param name="includeNetworkTests">When true, retains lightweight network diagnostics to correlate latencies.</param>
    /// <returns>A configured <see cref="DiagnosticOptions"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="queryText"/> is null or whitespace.</exception>
    public static DiagnosticOptions PerformanceDeepDive(
        string queryText,
        bool includePlans = true,
        bool includeNetworkTests = true)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            throw new ArgumentException("A query or stored procedure name must be provided.", nameof(queryText));
        }

        var builder = new DiagnosticOptionsBuilder()
            .WithTimeout(TimeSpan.FromMinutes(2))
            .WithConnectionTests(includePoolAnalysis: true, monitorStability: false)
            .WithQueryAnalysis(
                includeQueryPlans: includePlans,
                detectBlocking: true,
                captureWaitStats: true,
                waitStatsScope: WaitStatsScope.Session,
                sampleQuery: queryText)
            .WithServerHealth(includeConfiguration: true);

        if (includeNetworkTests)
        {
            builder.WithNetworkTests(includeDns: true, includePortProbe: true, measureBandwidth: true);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates an options preset optimised for comparing live diagnostics to an established baseline.
    /// </summary>
    /// <param name="baseline">The baseline report to compare against.</param>
    /// <param name="includeServerInsights">When true, includes server-level diagnostics in addition to connection and network checks.</param>
    /// <returns>A configured <see cref="DiagnosticOptions"/> instance that will emit baseline comparison data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseline"/> is null.</exception>
    public static DiagnosticOptions BaselineRegression(
        DiagnosticReport baseline,
        bool includeServerInsights = true)
    {
        if (baseline is null)
        {
            throw new ArgumentNullException(nameof(baseline));
        }

        var builder = new DiagnosticOptionsBuilder()
            .WithConnectionTests(includePoolAnalysis: true, monitorStability: true)
            .WithNetworkTests(includeDns: true, includePortProbe: false, measureBandwidth: false)
            .WithBaseline(baseline);

        if (includeServerInsights)
        {
            builder.WithServerHealth(includeConfiguration: true);
        }

        return builder.Build();
    }
}
