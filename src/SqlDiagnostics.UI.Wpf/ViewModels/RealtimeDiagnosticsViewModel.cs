using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Monitoring;
using SqlDiagnostics.Core.Reports;

namespace SqlDiagnostics.UI.Wpf.ViewModels;

public sealed class RealtimeDiagnosticsViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private readonly DiagnosticMonitor _monitor = new();

    private string _connectionString = Environment.GetEnvironmentVariable("SQLDIAG_CONNECTION_STRING") ?? string.Empty;
    private bool _isMonitoring;
    private string _statusMessage = "Idle.";
    private string _lastUpdatedDisplay = "Last updated: —";
    private string _successRateDisplay = "—";
    private string _attemptSummary = "0 / 0";
    private string _averageConnectionTimeDisplay = "—";
    private string _minMaxConnectionDisplay = "—";
    private string _networkLatencyDisplay = "—";
    private string _networkJitterDisplay = "—";
    private string _networkStatus = "No samples yet.";

    public RealtimeDiagnosticsViewModel()
    {
        Recommendations.CollectionChanged += RecommendationsOnCollectionChanged;
        _monitor.SnapshotAvailable += OnSnapshotAvailable;
        _monitor.MonitorError += OnMonitorError;

        StartCommand = new RelayCommand(() => _ = StartMonitoringAsync(), () => IsStartEnabled);
        StopCommand = new RelayCommand(() => _ = StopMonitoringAsync(), () => IsMonitoring);

        UpdateStatus("Ready. Provide a connection string to start monitoring.");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = new();

    public string ConnectionString
    {
        get => _connectionString;
        set
        {
            if (SetField(ref _connectionString, value))
            {
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (SetField(ref _isMonitoring, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsStartEnabled));
            }
        }
    }

    public bool IsStartEnabled => !IsMonitoring && !string.IsNullOrWhiteSpace(ConnectionString);

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

    public string SuccessRateDisplay
    {
        get => _successRateDisplay;
        private set => SetField(ref _successRateDisplay, value);
    }

    public string AttemptSummary
    {
        get => _attemptSummary;
        private set => SetField(ref _attemptSummary, value);
    }

    public string AverageConnectionTimeDisplay
    {
        get => _averageConnectionTimeDisplay;
        private set => SetField(ref _averageConnectionTimeDisplay, value);
    }

    public string MinMaxConnectionDisplay
    {
        get => _minMaxConnectionDisplay;
        private set => SetField(ref _minMaxConnectionDisplay, value);
    }

    public string NetworkLatencyDisplay
    {
        get => _networkLatencyDisplay;
        private set => SetField(ref _networkLatencyDisplay, value);
    }

    public string NetworkJitterDisplay
    {
        get => _networkJitterDisplay;
        private set => SetField(ref _networkJitterDisplay, value);
    }

    public string NetworkStatus
    {
        get => _networkStatus;
        private set => SetField(ref _networkStatus, value);
    }

    public bool HasRecommendations => Recommendations.Count > 0;

    public Visibility NoRecommendationsVisibility => HasRecommendations ? Visibility.Collapsed : Visibility.Visible;

    private async Task StartMonitoringAsync()
    {
        if (IsMonitoring)
        {
            return;
        }

        try
        {
            UpdateStatus("Starting monitoring session…");
            await _monitor.StartAsync(ConnectionString, DefaultInterval).ConfigureAwait(false);
            IsMonitoring = true;
            UpdateStatus("Monitoring in progress.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Failed to start monitoring: {ex.Message}");
        }
    }

    private async Task StopMonitoringAsync()
    {
        if (!IsMonitoring)
        {
            return;
        }

        try
        {
            UpdateStatus("Stopping…");
            await _monitor.StopAsync().ConfigureAwait(false);
            UpdateStatus("Monitoring stopped.");
        }
        finally
        {
            IsMonitoring = false;
        }
    }

    private void OnSnapshotAvailable(object? sender, DiagnosticSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyReport(snapshot);
        }
        else
        {
            dispatcher.Invoke(() => ApplyReport(snapshot));
        }
    }

    private void OnMonitorError(object? sender, Exception ex)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            UpdateStatus($"Monitor error: {ex.Message}");
        }
        else
        {
            dispatcher.Invoke(() => UpdateStatus($"Monitor error: {ex.Message}"));
        }
    }

    private void ApplyReport(DiagnosticSnapshot snapshot)
    {
        var report = snapshot.Report;
        UpdateStatus($"Monitoring {(report.TargetDataSource ?? "target")}…");
        LastUpdatedDisplay = $"Last updated: {snapshot.Timestamp:T}";

        ApplyConnectionMetrics(report.Connection);
        ApplyNetworkMetrics(report.Network);
        ApplyRecommendations(report);
    }

    private void ApplyConnectionMetrics(ConnectionMetrics? metrics)
    {
        if (metrics is null)
        {
            SuccessRateDisplay = "—";
            AttemptSummary = "0 / 0";
            AverageConnectionTimeDisplay = "—";
            MinMaxConnectionDisplay = "—";
            return;
        }

        SuccessRateDisplay = $"{metrics.SuccessRate:P1}";
        AttemptSummary = $"{metrics.SuccessfulAttempts} / {metrics.FailedAttempts} (of {metrics.TotalAttempts})";
        AverageConnectionTimeDisplay = FormatDuration(metrics.AverageConnectionTime);
        MinMaxConnectionDisplay = $"{FormatDuration(metrics.MinConnectionTime)} — {FormatDuration(metrics.MaxConnectionTime)}";
    }

    private void ApplyNetworkMetrics(LatencyMetrics? metrics)
    {
        if (metrics is null || metrics.Average is null)
        {
            NetworkLatencyDisplay = "—";
            NetworkJitterDisplay = "—";
            NetworkStatus = "No ICMP samples received.";
            return;
        }

        NetworkLatencyDisplay = FormatDuration(metrics.Average);
        NetworkJitterDisplay = FormatDuration(metrics.Jitter);
        NetworkStatus = metrics.Average.Value.TotalMilliseconds < 100
            ? "Stable"
            : metrics.Average.Value.TotalMilliseconds < 250
                ? "Moderate latency"
                : "High latency";
    }

    private void ApplyRecommendations(DiagnosticReport report)
    {
        Recommendations.Clear();

        foreach (var recommendation in report.Recommendations)
        {
            Recommendations.Add(new RecommendationItemViewModel(
                $"[{recommendation.Severity}] {recommendation.Category}",
                $"{recommendation.Issue}: {recommendation.RecommendationText}"));
        }

        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(NoRecommendationsVisibility));
    }

    private void RecommendationsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(NoRecommendationsVisibility));
    }

    private void UpdateStatus(string message) => StatusMessage = message;

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "—";
        }

        if (duration.Value.TotalMilliseconds < 1)
        {
            return $"{duration.Value.TotalMilliseconds:0.###} ms";
        }

        return $"{duration.Value.TotalMilliseconds:N0} ms";
    }

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
        Recommendations.CollectionChanged -= RecommendationsOnCollectionChanged;
        _monitor.SnapshotAvailable -= OnSnapshotAvailable;
        _monitor.MonitorError -= OnMonitorError;
        await _monitor.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class RecommendationItemViewModel
{
    public RecommendationItemViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }

    public string Message { get; }
}
