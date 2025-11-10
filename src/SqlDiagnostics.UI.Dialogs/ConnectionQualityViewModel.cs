using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Monitoring;
using SqlDiagnostics.Core.Reports;

namespace SqlDiagnostics.UI.Dialogs;

/// <summary>
/// View model powering the connection quality dialog with live charts.
/// </summary>
public sealed class ConnectionQualityViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int MaxSamples = 120;

    private readonly DiagnosticMonitor _monitor;
    private readonly ConnectionQualityDialogOptions _options;
    private readonly bool _ownsMonitor;
    private readonly bool _shouldStartMonitor;
    private readonly Dispatcher _dispatcher;

    private bool _monitorStarted;
    private bool _initialized;
    private DateTime _lastLatencySampleTimestamp = DateTime.MinValue;
    private string _statusMessage = "Preparing monitor…";
    private string _latencySummary = "No latency samples yet.";
    private string _throughputSummary = "No throughput samples yet.";
    private string _lastUpdated = "—";

    public ConnectionQualityViewModel(ConnectionQualityDialogOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        if (options.Monitor is not null)
        {
            _monitor = options.Monitor;
            _ownsMonitor = false;
            _shouldStartMonitor = false;
            _statusMessage = "Listening to external monitor.";
        }
        else
        {
            _monitor = new DiagnosticMonitor();
            _ownsMonitor = true;
            _shouldStartMonitor = true;
            _statusMessage = "Ready to start monitoring.";
        }

        LatencyPoints = new ObservableCollection<ObservablePoint>();
        ThroughputPoints = new ObservableCollection<ObservablePoint>();

        LatencySeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Values = LatencyPoints,
                GeometryFill = null,
                GeometryStroke = null,
                Fill = null,
                Stroke = new SolidColorPaint(new SKColor(33, 150, 243), 2),
                Name = "Latency (ms)"
            }
        };

        ThroughputSeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Values = ThroughputPoints,
                GeometryFill = null,
                GeometryStroke = null,
                Fill = null,
                Stroke = new SolidColorPaint(new SKColor(76, 175, 80), 2),
                Name = "Throughput (MB/s)"
            }
        };

        LatencyXAxis = new Axis[]
        {
            new Axis
            {
                Labeler = value => DateTime.FromOADate(value).ToLocalTime().ToString("T"),
                UnitWidth = TimeSpan.FromSeconds(5).TotalDays,
                MinStep = TimeSpan.FromSeconds(5).TotalDays,
                Name = "Timestamp"
            }
        };

        LatencyYAxis = new Axis[]
        {
            new Axis
            {
                Name = "Milliseconds",
                Labeler = value => $"{value:0}"
            }
        };

        ThroughputXAxis = new Axis[]
        {
            new Axis
            {
                Labeler = value => DateTime.FromOADate(value).ToLocalTime().ToString("T"),
                UnitWidth = TimeSpan.FromSeconds(5).TotalDays,
                MinStep = TimeSpan.FromSeconds(5).TotalDays,
                Name = "Timestamp"
            }
        };

        ThroughputYAxis = new Axis[]
        {
            new Axis
            {
                Name = "Megabytes / second",
                Labeler = value => $"{value:0.0}"
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ObservablePoint> LatencyPoints { get; }

    public ObservableCollection<ObservablePoint> ThroughputPoints { get; }

    public ISeries[] LatencySeries { get; }

    public ISeries[] ThroughputSeries { get; }

    public Axis[] LatencyXAxis { get; }

    public Axis[] LatencyYAxis { get; }

    public Axis[] ThroughputXAxis { get; }

    public Axis[] ThroughputYAxis { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string LatencySummary
    {
        get => _latencySummary;
        private set => SetField(ref _latencySummary, value);
    }

    public string ThroughputSummary
    {
        get => _throughputSummary;
        private set => SetField(ref _throughputSummary, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        _monitor.SnapshotAvailable += OnSnapshotAvailable;
        _monitor.MonitorError += OnMonitorError;

        if (_shouldStartMonitor && !_monitor.IsRunning)
        {
            try
            {
                StatusMessage = "Starting connection quality monitor…";
                await _monitor.StartAsync(
                    _options.ConnectionString!,
                    _options.MonitoringInterval).ConfigureAwait(false);
                _monitorStarted = true;
                StatusMessage = "Monitoring in progress.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to start monitoring: {ex.Message}";
                throw;
            }
        }
        else
        {
            StatusMessage = "Attached to external monitor.";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _monitor.SnapshotAvailable -= OnSnapshotAvailable;
        _monitor.MonitorError -= OnMonitorError;

        if (_monitorStarted)
        {
            try
            {
                await _monitor.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore stop errors so the dialog can close cleanly.
            }
        }

        if (_ownsMonitor)
        {
            await _monitor.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnSnapshotAvailable(object? sender, DiagnosticSnapshot snapshot)
    {
        _ = _dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
    }

    private void OnMonitorError(object? sender, Exception ex)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            StatusMessage = $"Monitor error: {ex.Message}";
        });
    }

    private void ApplySnapshot(DiagnosticSnapshot snapshot)
    {
        LastUpdated = snapshot.Timestamp.ToLocalTime().ToString("T");

        var report = snapshot.Report;
        ApplyLatency(report.Network);
        ApplyThroughput(snapshot.Timestamp, report.Bandwidth);
    }

    private void ApplyLatency(LatencyMetrics? metrics)
    {
        if (metrics is null)
        {
            LatencySummary = "No latency data.";
            return;
        }

        foreach (var sample in metrics.Samples.OrderBy(s => s.Timestamp))
        {
            if (sample.Timestamp <= _lastLatencySampleTimestamp)
            {
                continue;
            }

            _lastLatencySampleTimestamp = sample.Timestamp;
            var point = new ObservablePoint(sample.Timestamp.ToOADate(), sample.Elapsed.TotalMilliseconds);
            LatencyPoints.Add(point);
        }

        TrimExcess(LatencyPoints);

        if (metrics.Average is null)
        {
            LatencySummary = "No successful ping samples yet.";
        }
        else
        {
            var avg = FormatDuration(metrics.Average);
            var min = FormatDuration(metrics.Min);
            var max = FormatDuration(metrics.Max);
            var jitter = FormatDuration(metrics.Jitter);
            LatencySummary = $"Average {avg}, range {min} – {max}, jitter {jitter}";
        }
    }

    private void ApplyThroughput(DateTime snapshotTimestamp, BandwidthMetrics? metrics)
    {
        if (metrics?.MegabytesPerSecond is double mbPerSecond)
        {
            var point = new ObservablePoint(snapshotTimestamp.ToOADate(), mbPerSecond);
            ThroughputPoints.Add(point);
            TrimExcess(ThroughputPoints);
            ThroughputSummary = $"Rate {mbPerSecond:0.00} MB/s over {metrics.Duration.TotalSeconds:0}s";
        }
        else
        {
            ThroughputSummary = "Throughput measurement unavailable.";
        }
    }

    private static void TrimExcess(ObservableCollection<ObservablePoint> collection)
    {
        while (collection.Count > MaxSamples)
        {
            collection.RemoveAt(0);
        }
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "—";
        }

        var milliseconds = duration.Value.TotalMilliseconds;
        return milliseconds < 1
            ? $"{milliseconds:0.###} ms"
            : $"{milliseconds:0} ms";
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
