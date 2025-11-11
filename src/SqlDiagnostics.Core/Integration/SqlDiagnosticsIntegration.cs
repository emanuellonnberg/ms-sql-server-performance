using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Core.Baseline;
using SqlDiagnostics.Core.Logging;
using SqlDiagnostics.Core.Triage;

namespace SqlDiagnostics.Core.Integration;

/// <summary>
/// Convenience helpers that streamline common SqlDiagnostics workflows.
/// </summary>
public static class SqlDiagnosticsIntegration
{
    /// <summary>
    /// Creates a diagnostics logger configured with sensible defaults.
    /// </summary>
    public static DiagnosticLogger CreateDefaultLogger(string? logDirectory = null, bool enableConsole = false) =>
        new(new DiagnosticLoggerOptions
        {
            LogDirectory = logDirectory,
            EnableConsoleSink = enableConsole
        });

    /// <summary>
    /// Instantiates a <see cref="SqlDiagnosticsClient"/> wired to the supplied logger factory and diagnostics logger.
    /// </summary>
    public static SqlDiagnosticsClient CreateClient(DiagnosticLogger? diagnosticLogger = null, ILoggerFactory? loggerFactory = null) =>
        new(loggerFactory, diagnosticLogger);

    /// <summary>
    /// Captures a baseline using the default storage location.
    /// </summary>
    public static Task<PerformanceBaseline> CaptureBaselineAsync(
        string connectionString,
        string baselineName,
        BaselineOptions? options = null,
        CancellationToken cancellationToken = default) =>
        new BaselineManager().CaptureBaselineAsync(connectionString, baselineName, options, cancellationToken);

    /// <summary>
    /// Compares the current diagnostics to the most recent baseline (or specified named baseline).
    /// </summary>
    public static Task<RegressionReport> CompareToBaselineAsync(
        string connectionString,
        string? baselineName = null,
        BaselineOptions? options = null,
        CancellationToken cancellationToken = default) =>
        new BaselineManager().CompareToBaselineAsync(connectionString, baselineName, options, cancellationToken);

    /// <summary>
    /// Runs the quick triage workflow and returns the consolidated result.
    /// </summary>
    public static Task<TriageResult> RunQuickTriageAsync(
        string connectionString,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        QuickTriage.RunAsync(connectionString, options: null, progress, cancellationToken);
}
