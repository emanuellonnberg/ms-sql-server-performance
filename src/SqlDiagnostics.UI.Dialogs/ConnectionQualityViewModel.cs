using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Monitoring;
using SqlDiagnostics.Core.Reports;

namespace SqlDiagnostics.UI.Dialogs;

/// <summary>
/// View model powering the connection quality dialog with OxyPlot charts.
/// </summary>
public sealed class ConnectionQualityViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int MaxSamples = 120;

    private readonly DiagnosticMonitor _monitor;
    private readonly ConnectionQualityDialogOptions _options;
    private readonly bool _ownsMonitor;
    private readonly bool _shouldStartMonitor;
    private readonly Dispatcher _dispatcher;
    private readonly LineSeries _latencySeries;
    private readonly LineSeries _throughputSeries;

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

        LatencyPlot = CreateLatencyPlot(out _latencySeries);
        ThroughputPlot = CreateThroughputPlot(out _throughputSeries);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlotModel LatencyPlot { get; }

    public PlotModel ThroughputPlot { get; }

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

        foreach (var sample in metrics.Samples)
        {
            if (sample.Timestamp <= _lastLatencySampleTimestamp)
            {
                continue;
            }

            _lastLatencySampleTimestamp = sample.Timestamp;
            var point = DateTimeAxis.CreateDataPoint(sample.Timestamp, sample.Elapsed.TotalMilliseconds);
            _latencySeries.Points.Add(point);
        }

        TrimSeries(_latencySeries);
        LatencyPlot.InvalidatePlot(true);

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
            var point = DateTimeAxis.CreateDataPoint(snapshotTimestamp, mbPerSecond);
            _throughputSeries.Points.Add(point);
            TrimSeries(_throughputSeries);
            ThroughputPlot.InvalidatePlot(true);
            ThroughputSummary = $"Rate {mbPerSecond:0.00} MB/s over {metrics.Duration.TotalSeconds:0}s";
        }
        else
        {
            ThroughputSummary = "Throughput measurement unavailable.";
        }
    }

    private static void TrimSeries(LineSeries series)
    {
        while (series.Points.Count > MaxSamples)
        {
            series.Points.RemoveAt(0);
        }
    }

    private static PlotModel CreateLatencyPlot(out LineSeries series) =>
        CreatePlotModel("Latency (ms)", "Milliseconds", OxyColor.FromRgb(33, 150, 243), out series);

    private static PlotModel CreateThroughputPlot(out LineSeries series) =>
        CreatePlotModel("Throughput (MB/s)", "Megabytes per second", OxyColor.FromRgb(76, 175, 80), out series);

    private static PlotModel CreatePlotModel(
        string seriesTitle,
        string yAxisTitle,
        OxyColor strokeColor,
        out LineSeries series)
    {
        var model = new PlotModel
        {
            PlotMargins = new OxyThickness(double.NaN)
        };

        var dateAxis = new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            StringFormat = "HH:mm:ss",
            MinorIntervalType = DateTimeIntervalType.Seconds,
            IntervalType = DateTimeIntervalType.Seconds,
            IsPanEnabled = false,
            IsZoomEnabled = false,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            AxislineStyle = LineStyle.Solid,
            Angle = 0
        };

        var valueAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = yAxisTitle,
            IsPanEnabled = false,
            IsZoomEnabled = false,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            AxislineStyle = LineStyle.Solid
        };

        series = new LineSeries
        {
            Title = seriesTitle,
            Color = strokeColor,
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
            CanTrackerInterpolatePoints = false,
            LineJoin = LineJoin.Round
        };

        model.Axes.Add(dateAxis);
        model.Axes.Add(valueAxis);
        model.Series.Add(series);

        return model;
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
