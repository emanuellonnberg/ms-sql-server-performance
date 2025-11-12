using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Monitoring;
using SqlDiagnostics.Core.Reports;
using SqlDiagnostics.Core.Utilities;
using System.Linq;
using SqlDiagnostics.Core;

namespace SqlDiagnostics.UI.Wpf.ViewModels;

public sealed class FullDiagnosticsViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly DiagnosticMonitor _monitor = new();

    private string _statusMessage = "Idle.";
    private string _lastUpdatedDisplay = "Last updated: —";
    private string _healthScoreDisplay = "—";
    private string _targetDisplay = "—";
    private string _connectionSummary = "—";
    private string _performanceSummary = "—";
    private string _databaseSummary = "—";
    private string _recommendationsHeader = "No recommendations.";
    private string _reportText = "No diagnostics collected yet.";
    private bool _isMonitoring;
    private string _permissionNotice = string.Empty;
    private PermissionCheckResult? _permissionResult;
    private bool _hasServerStatePermission = true;
    private bool _hasDatabaseStatePermission = true;

    private string? _connectionString;

    public FullDiagnosticsViewModel()
    {
        TopWaits.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTopWaits));
        Recommendations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecommendations));
        _monitor.SnapshotAvailable += OnSnapshotAvailable;
        _monitor.MonitorError += OnMonitorError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> TopWaits { get; } = new();

    public ObservableCollection<string> Recommendations { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string LastUpdatedDisplay
    {
        get => _lastUpdatedDisplay;
        private set => SetField(ref _lastUpdatedDisplay, value);
    }

    public string HealthScoreDisplay
    {
        get => _healthScoreDisplay;
        private set => SetField(ref _healthScoreDisplay, value);
    }

    public string TargetDisplay
    {
        get => _targetDisplay;
        private set => SetField(ref _targetDisplay, value);
    }

    public string ConnectionSummary
    {
        get => _connectionSummary;
        private set => SetField(ref _connectionSummary, value);
    }

    public string PerformanceSummary
    {
        get => _performanceSummary;
        private set => SetField(ref _performanceSummary, value);
    }

    public string DatabaseSummary
    {
        get => _databaseSummary;
        private set => SetField(ref _databaseSummary, value);
    }

    public string RecommendationsHeader
    {
        get => _recommendationsHeader;
        private set => SetField(ref _recommendationsHeader, value);
    }

    public string ReportText
    {
        get => _reportText;
        private set => SetField(ref _reportText, value);
    }

    public bool HasTopWaits => TopWaits.Count > 0;

    public bool HasRecommendations => Recommendations.Count > 0;

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set => SetField(ref _isMonitoring, value);
    }

    public string PermissionNotice
    {
        get => _permissionNotice;
        private set
        {
            if (SetField(ref _permissionNotice, value))
            {
                OnPropertyChanged(nameof(HasPermissionNotice));
            }
        }
    }

    public bool HasPermissionNotice => !string.IsNullOrWhiteSpace(PermissionNotice);

    public async Task<PermissionCheckResult> InitializeAsync(string connectionString)
    {
        _connectionString = connectionString;

        UpdateStatus("Checking required permissions…");
        _permissionResult = await SqlPermissionChecker
            .CheckRequiredPermissionsAsync(connectionString)
            .ConfigureAwait(false);
        AnalyzePermissions(_permissionResult);

        try
        {
            UpdateStatus("Collecting full diagnostics…");
            var options = BuildOptions();
            ApplyPermissionFilters(options);
            await _monitor.StartAsync(connectionString, TimeSpan.FromSeconds(45), options).ConfigureAwait(false);
            IsMonitoring = true;
        }
        catch (Exception ex)
        {
            UpdateStatus($"Failed to start diagnostics: {ex.Message}");
            return PermissionCheckResult.Error(ex.Message);
        }

        return _permissionResult;
    }

    private void AnalyzePermissions(PermissionCheckResult result)
    {
        _hasServerStatePermission = !result.MissingPermissions.Any(p =>
            p.Contains("VIEW SERVER STATE", StringComparison.OrdinalIgnoreCase));
        _hasDatabaseStatePermission = !result.MissingPermissions.Any(p =>
            p.Contains("VIEW DATABASE STATE", StringComparison.OrdinalIgnoreCase));

        PermissionNotice = BuildPermissionNotice(result);
    }

    private string BuildPermissionNotice(PermissionCheckResult result)
    {
        if (result.Succeeded && string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            builder.AppendLine(result.ErrorMessage);
        }

        if (result.MissingPermissions.Count > 0)
        {
            builder.AppendLine("Limited diagnostics – missing permissions:");
            foreach (var permission in result.MissingPermissions.Distinct())
            {
                builder.AppendLine($" • {permission}");
            }
        }

        return builder.ToString().Trim();
    }

    private static DiagnosticOptions BuildOptions()
    {
        var builder = new DiagnosticOptionsBuilder()
            .WithTimeout(TimeSpan.FromSeconds(60))
            .WithConnectionTests(includePoolAnalysis: true, monitorStability: true, stabilityDuration: TimeSpan.FromMinutes(1))
            .WithNetworkTests(includeDns: true, includePortProbe: true, measureBandwidth: true)
            .WithQueryAnalysis(includeQueryPlans: true, detectBlocking: true, captureWaitStats: true, waitStatsScope: WaitStatsScope.Server)
            .WithServerHealth(includeConfiguration: true);

        var options = builder.Build();
        options.GenerateRecommendations = true;
        return options;
    }

    private void ApplyPermissionFilters(DiagnosticOptions options)
    {
        if (!_hasServerStatePermission)
        {
            options.Categories &= ~(DiagnosticCategories.Server | DiagnosticCategories.Database);
            options.IncludeServerConfiguration = false;
            options.CaptureWaitStatistics = false;
            options.DetectBlocking = false;
        }
        else if (!_hasDatabaseStatePermission)
        {
            options.Categories &= ~DiagnosticCategories.Database;
        }
    }

    private void OnSnapshotAvailable(object? sender, DiagnosticSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyReport(snapshot.Report, snapshot.Timestamp);
        }
        else
        {
            dispatcher.Invoke(() => ApplyReport(snapshot.Report, snapshot.Timestamp));
        }
    }

    private void OnMonitorError(object? sender, Exception ex)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            UpdateStatus($"Diagnostics error: {ex.Message}");
        }
        else
        {
            dispatcher.Invoke(() => UpdateStatus($"Diagnostics error: {ex.Message}"));
        }
    }

    private void ApplyReport(DiagnosticReport report, DateTime timestamp)
    {
        UpdateStatus("Diagnostics updated.");
        LastUpdatedDisplay = $"Last updated: {timestamp:O}";
        TargetDisplay = report.TargetDataSource ?? "Unknown target";
        HealthScoreDisplay = $"{report.GetHealthScore()}/100";

        ConnectionSummary = BuildConnectionSummary(report.Connection);
        PerformanceSummary = _hasServerStatePermission
            ? BuildServerSummary(report.Server)
            : "Server metrics unavailable (requires VIEW SERVER STATE).";
        DatabaseSummary = _hasDatabaseStatePermission && _hasServerStatePermission
            ? BuildDatabaseSummary(report.Databases)
            : !_hasDatabaseStatePermission
                ? "Database metrics unavailable (requires VIEW DATABASE STATE)."
                : "Database metrics unavailable.";
        UpdateWaits(report.Server);
        UpdateRecommendations(report);

        ReportText = BuildReportText(report);
    }

    private static string BuildConnectionSummary(ConnectionMetrics? metrics)
    {
        if (metrics is null)
        {
            return "Connection metrics unavailable.";
        }

        return $"Success rate {metrics.SuccessRate:P1} across {metrics.TotalAttempts} attempts. " +
               $"Average {FormatDuration(metrics.AverageConnectionTime)}, Min {FormatDuration(metrics.MinConnectionTime)}, Max {FormatDuration(metrics.MaxConnectionTime)}.";
    }

    private static string BuildServerSummary(ServerMetrics? metrics)
    {
        if (metrics is null)
        {
            return "Server metrics unavailable.";
        }

        var usage = metrics.ResourceUsage;
        var builder = new StringBuilder();
        builder.Append($"CPU {usage.CpuUtilizationPercent?.ToString("N1") ?? "n/a"}% (SQL {usage.SqlProcessUtilizationPercent?.ToString("N1") ?? "n/a"}%), ");
        builder.Append($"Memory {usage.AvailableMemoryMb?.ToString("N0") ?? "n/a"} MB free of {usage.TotalMemoryMb?.ToString("N0") ?? "n/a"} MB, ");
        builder.Append($"PLE {usage.PageLifeExpectancySeconds?.ToString("N0") ?? "n/a"} s, IO Stall {usage.IoStallMs?.ToString("N0") ?? "n/a"} ms.");
        return builder.ToString();
    }

    private static string BuildDatabaseSummary(DatabaseMetrics? metrics)
    {
        if (metrics is null || metrics.Databases.Count == 0)
        {
            return "Database metrics unavailable.";
        }

        var builder = new StringBuilder();
        foreach (var db in metrics.Databases.Take(5))
        {
            builder.AppendLine($"{db.Name}: Size {db.DataFileSizeMb?.ToString("N1") ?? "n/a"} MB data / {db.LogFileSizeMb?.ToString("N1") ?? "n/a"} MB log, Log used {db.LogUsedPercent?.ToString("N1") ?? "n/a"}%, Sessions {db.ActiveSessionCount?.ToString("N0") ?? "n/a"}.");
        }

        if (metrics.Databases.Count > 5)
        {
            builder.AppendLine($"(+{metrics.Databases.Count - 5} more databases)");
        }

        if (metrics.Metadata.TryGetValue("warning", out var warning) && warning is string warningText && !string.IsNullOrWhiteSpace(warningText))
        {
            builder.AppendLine($"Warning: {warningText}");
        }

        return builder.ToString().TrimEnd();
    }

    private void UpdateWaits(ServerMetrics? server)
    {
        TopWaits.Clear();
        if (_hasServerStatePermission && server?.Waits.Count > 0)
        {
            foreach (var wait in server.Waits.Take(10))
            {
                TopWaits.Add($"{wait.WaitType}: {wait.WaitTimeMs:N0} ms (signal {wait.SignalWaitTimeMs:N0} ms, tasks {wait.WaitingTasksCount:N0})");
            }
        }
    }

    private void UpdateRecommendations(DiagnosticReport report)
    {
        Recommendations.Clear();

        if (report.Recommendations.Count > 0)
        {
            foreach (var recommendation in report.Recommendations)
            {
                Recommendations.Add($"[{recommendation.Severity}] {recommendation.Category}: {recommendation.Issue} – {recommendation.RecommendationText}");
            }

            RecommendationsHeader = $"{report.Recommendations.Count} recommendation{(report.Recommendations.Count == 1 ? string.Empty : "s")}.";
        }
        else
        {
            RecommendationsHeader = "No recommendations.";
        }
    }

    private static string BuildReportText(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Generated: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Target: {report.TargetDataSource ?? "Unknown"}");
        builder.AppendLine($"Health Score: {report.GetHealthScore()}/100");
        builder.AppendLine();

        builder.AppendLine("Connection:");
        builder.AppendLine($"  Success Rate: {report.Connection?.SuccessRate:P1} ({report.Connection?.SuccessfulAttempts}/{report.Connection?.TotalAttempts})");
        builder.AppendLine($"  Avg / Min / Max: {FormatDuration(report.Connection?.AverageConnectionTime)} / {FormatDuration(report.Connection?.MinConnectionTime)} / {FormatDuration(report.Connection?.MaxConnectionTime)}");
        builder.AppendLine();

        if (report.Network is not null)
        {
            builder.AppendLine("Network:");
            builder.AppendLine($"  Avg Latency: {FormatDuration(report.Network.Average)}");
            builder.AppendLine($"  Min / Max: {FormatDuration(report.Network.Min)} / {FormatDuration(report.Network.Max)}");
            builder.AppendLine($"  Jitter: {FormatDuration(report.Network.Jitter)}");
            builder.AppendLine();
        }

        if (report.Server is not null)
        {
            builder.AppendLine("Server:");
            var usage = report.Server.ResourceUsage;
            builder.AppendLine($"  CPU: {usage.CpuUtilizationPercent?.ToString("N1") ?? "n/a"}% (SQL {usage.SqlProcessUtilizationPercent?.ToString("N1") ?? "n/a"}%)");
            builder.AppendLine($"  Memory Free: {usage.AvailableMemoryMb?.ToString("N0") ?? "n/a"} MB");
            builder.AppendLine($"  Page Life Expectancy: {usage.PageLifeExpectancySeconds?.ToString("N0") ?? "n/a"} s");
            builder.AppendLine($"  IO Stall: {usage.IoStallMs?.ToString("N0") ?? "n/a"} ms");
            builder.AppendLine();

            if (report.Server.Waits.Count > 0)
            {
                builder.AppendLine("  Top Waits:");
                foreach (var wait in report.Server.Waits.Take(5))
                {
                    builder.AppendLine($"    {wait.WaitType}: {wait.WaitTimeMs:N0} ms (signal {wait.SignalWaitTimeMs:N0} ms, tasks {wait.WaitingTasksCount:N0})");
                }
                builder.AppendLine();
            }
        }

        if (report.Databases is not null && report.Databases.Databases.Count > 0)
        {
            builder.AppendLine("Databases:");
            foreach (var db in report.Databases.Databases.Take(5))
            {
                builder.AppendLine($"  {db.Name}: Size {db.DataFileSizeMb?.ToString("N1") ?? "n/a"} MB, Log Used {db.LogUsedPercent?.ToString("N1") ?? "n/a"}%, Sessions {db.ActiveSessionCount?.ToString("N0") ?? "n/a"}");
            }
            builder.AppendLine();
        }

        if (report.Recommendations.Count > 0)
        {
            builder.AppendLine("Recommendations:");
            foreach (var recommendation in report.Recommendations)
            {
                builder.AppendLine($"  - [{recommendation.Severity}] {recommendation.Category}: {recommendation.Issue} – {recommendation.RecommendationText}");
            }
        }

        return builder.ToString();
    }

    private static string FormatDuration(TimeSpan? value) =>
        value.HasValue ? $"{value.Value.TotalMilliseconds:N0} ms" : "n/a";

    private void UpdateStatus(string message) => StatusMessage = message;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public async ValueTask DisposeAsync()
    {
        _monitor.SnapshotAvailable -= OnSnapshotAvailable;
        _monitor.MonitorError -= OnMonitorError;

        if (IsMonitoring)
        {
            await _monitor.StopAsync().ConfigureAwait(false);
            IsMonitoring = false;
        }

        await _monitor.DisposeAsync().ConfigureAwait(false);
    }
}
