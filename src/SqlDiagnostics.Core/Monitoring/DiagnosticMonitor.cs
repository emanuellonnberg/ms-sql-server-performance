using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Client;
using SqlDiagnostics.Reports;

namespace SqlDiagnostics.Monitoring;

/// <summary>
/// Periodically executes diagnostics and emits snapshot events that can be consumed by UI layers.
/// </summary>
public sealed class DiagnosticMonitor : IAsyncDisposable, IDisposable
{
    private readonly SqlDiagnosticsClient _client;
    private readonly bool _clientProvided;
    private readonly ILogger? _logger;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private readonly object _gate = new();

    public DiagnosticMonitor(SqlDiagnosticsClient? client = null, ILogger? logger = null)
    {
        _clientProvided = client is not null;
        _client = client ?? new SqlDiagnosticsClient();
        _logger = logger;
    }

    /// <summary>
    /// Raised whenever a new snapshot is produced.
    /// </summary>
    public event EventHandler<DiagnosticReport>? SnapshotAvailable;

    /// <summary>
    /// Raised when an exception occurs while collecting diagnostics.
    /// </summary>
    public event EventHandler<Exception>? MonitorError;

    public bool IsRunning
    {
        get
        {
            var task = _monitorTask;
            return task is not null && !task.IsCompleted;
        }
    }

    public Task StartAsync(
        string connectionString,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        lock (_gate)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("Monitor is already running.");
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitorTask = Task.Run(() => MonitorLoopAsync(connectionString, interval, _cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? task;
        lock (_gate)
        {
            if (_cts is null)
            {
                return;
            }

            _cts.Cancel();
            task = _monitorTask;
            _cts.Dispose();
            _cts = null;
            _monitorTask = null;
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation.
            }
        }
    }

    private async Task MonitorLoopAsync(string connectionString, TimeSpan interval, CancellationToken cancellationToken)
    {
        var timerDelay = interval;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var report = await _client.RunQuickCheckAsync(connectionString, cancellationToken).ConfigureAwait(false);
                SnapshotAvailable?.Invoke(this, report);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Diagnostic monitor encountered an error.");
                MonitorError?.Invoke(this, ex);
            }

            try
            {
                await Task.Delay(timerDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (!_clientProvided)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!_clientProvided)
        {
            _client.Dispose();
        }
    }
}
