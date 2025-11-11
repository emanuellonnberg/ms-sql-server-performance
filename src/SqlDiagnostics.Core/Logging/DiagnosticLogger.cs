using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Reports;

namespace SqlDiagnostics.Core.Logging;

/// <summary>
/// Centralised logger that multiplexes diagnostic events to sinks.
/// </summary>
public sealed class DiagnosticLogger : IDisposable
{
    private readonly List<IDiagnosticSink> _sinks = new();
    private readonly ConcurrentQueue<DiagnosticEvent> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private readonly TimeSpan _flushInterval;
    private readonly int _maxQueueSize;
    private bool _disposed;

    public DiagnosticLogger(DiagnosticLoggerOptions? options = null, IEnumerable<IDiagnosticSink>? additionalSinks = null)
    {
        var effectiveOptions = options ?? new DiagnosticLoggerOptions();
        _flushInterval = effectiveOptions.FlushInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(1)
            : effectiveOptions.FlushInterval;
        _maxQueueSize = Math.Max(256, effectiveOptions.MaxQueueSize);

        var logDirectory = effectiveOptions.LogDirectory;
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlDiagnostics", "Logs");
        }
        Directory.CreateDirectory(logDirectory);

        var jsonPath = Path.Combine(logDirectory, $"diagnostics-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        _sinks.Add(new JsonFileSink(jsonPath));

        var rollingPath = Path.Combine(logDirectory, "diagnostics.log");
        _sinks.Add(new RollingFileSink(
            rollingPath,
            Math.Max(1, effectiveOptions.RollingFileCount),
            Math.Max(1, effectiveOptions.RollingFileSizeInMegabytes)));

        if (effectiveOptions.EnableConsoleSink)
        {
            _sinks.Add(new ConsoleSink());
        }

        if (additionalSinks is not null)
        {
            _sinks.AddRange(additionalSinks);
        }

        _workerTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
    }

    /// <summary>
    /// Enqueues a raw event for persistence.
    /// </summary>
    public void LogEvent(DiagnosticEvent diagnosticEvent)
    {
        if (diagnosticEvent is null)
        {
            throw new ArgumentNullException(nameof(diagnosticEvent));
        }

        if (_queue.Count >= _maxQueueSize)
        {
            _queue.TryDequeue(out _);
        }

        _queue.Enqueue(diagnosticEvent);
    }

    /// <summary>
    /// Logs metrics from a connection attempt.
    /// </summary>
    public void LogConnectionMetrics(ConnectionMetrics metrics)
    {
        if (metrics is null)
        {
            throw new ArgumentNullException(nameof(metrics));
        }

        LogEvent(new DiagnosticEvent
        {
            EventType = DiagnosticEventType.Connection,
            Severity = metrics.SuccessRate < 0.8 ? EventSeverity.Warning : EventSeverity.Info,
            Message = $"Connection success rate {metrics.SuccessRate:P1} across {metrics.TotalAttempts} attempts.",
            Data = metrics
        });
    }

    /// <summary>
    /// Logs high-level details from a completed diagnostics report.
    /// </summary>
    public void LogReport(DiagnosticReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        LogEvent(new DiagnosticEvent
        {
            EventType = DiagnosticEventType.General,
            Severity = EventSeverity.Info,
            Message = $"Diagnostics report generated with health score {report.GetHealthScore()} for {report.TargetDataSource ?? "<unknown>"}",
            Data = new
            {
                report.GeneratedAtUtc,
                report.TargetDataSource,
                HealthScore = report.GetHealthScore(),
                RecommendationCount = report.Recommendations.Count,
                BaselineDelta = report.BaselineComparison?.HealthScoreDelta
            }
        });

        if (report.Connection is not null)
        {
            LogConnectionMetrics(report.Connection);
        }

        if (report.Network is not null)
        {
            LogEvent(new DiagnosticEvent
            {
                EventType = DiagnosticEventType.Network,
                Severity = EventSeverity.Info,
                Message = $"Network latency avg {report.Network.Average?.TotalMilliseconds:N0} ms.",
                Data = report.Network
            });
        }

        if (report.ConnectionPool is not null)
        {
            LogEvent(new DiagnosticEvent
            {
                EventType = DiagnosticEventType.Pool,
                Severity = report.ConnectionPool.Notes.Count > 0 ? EventSeverity.Warning : EventSeverity.Info,
                Message = "Connection pool metrics collected.",
                Data = report.ConnectionPool
            });
        }
    }

    /// <summary>
    /// Creates a compressed archive containing collected logs.
    /// </summary>
    public Task<string> CreateDiagnosticPackageAsync(DateTime? fromDateUtc = null, string? outputPath = null, CancellationToken cancellationToken = default)
    {
        var logDirectory = Path.GetDirectoryName(GetPrimaryLogPath())!;
        return CreatePackageAsync(logDirectory, fromDateUtc, outputPath, cancellationToken);
    }

    /// <summary>
    /// Creates a compressed archive from an existing log directory.
    /// </summary>
    public static async Task<string> CreatePackageAsync(string logDirectory, DateTime? fromDateUtc = null, string? outputPath = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("Log directory must be provided.", nameof(logDirectory));
        }

        if (!Directory.Exists(logDirectory))
        {
            throw new DirectoryNotFoundException($"Log directory '{logDirectory}' was not found.");
        }

        var fromDate = fromDateUtc ?? DateTime.UtcNow.AddDays(-7);
        var files = Directory.GetFiles(logDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var creation = File.GetCreationTimeUtc(f);
                var write = File.GetLastWriteTimeUtc(f);
                return creation >= fromDate || write >= fromDate;
            })
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException("No log files were found in the specified directory.");
        }

        var packagePath = outputPath ?? Path.Combine(
            logDirectory,
            $"diagnostic-package-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");

        using (var fileStream = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await sourceStream.CopyToAsync(entryStream, 81920, cancellationToken).ConfigureAwait(false);
            }
        }

        return packagePath;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            _workerTask.GetAwaiter().GetResult();
        }
        catch
        {
            // swallow shutdown exceptions
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private string GetPrimaryLogPath()
    {
        foreach (var sink in _sinks)
        {
            if (sink is JsonFileSink jsonSink)
            {
                var field = typeof(JsonFileSink).GetField("_filePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(jsonSink) is string file)
                {
                    return file;
                }
            }
        }

        return Path.Combine(Path.GetTempPath(), "sqldiag.log");
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        var batch = new List<DiagnosticEvent>(256);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_queue.IsEmpty)
                {
                    await Task.Delay(_flushInterval, cancellationToken).ConfigureAwait(false);
                }

                batch.Clear();
                while (batch.Count < 256 && _queue.TryDequeue(out var evt))
                {
                    batch.Add(evt);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                foreach (var sink in _sinks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await sink.WriteAsync(batch, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }

        // final flush
        var remaining = new List<DiagnosticEvent>();
        while (_queue.TryDequeue(out var evtFinal))
        {
            remaining.Add(evtFinal);
        }

        if (remaining.Count > 0)
        {
            foreach (var sink in _sinks)
            {
                try
                {
                    sink.WriteAsync(remaining, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch
                {
                    // ignore final flush failure
                }
            }
        }
    }

    private sealed class ConsoleSink : IDiagnosticSink
    {
        public Task WriteAsync(IReadOnlyCollection<DiagnosticEvent> events, CancellationToken cancellationToken)
        {
            foreach (var evt in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var color = evt.Severity switch
                {
                    EventSeverity.Critical => ConsoleColor.Red,
                    EventSeverity.Error => ConsoleColor.Red,
                    EventSeverity.Warning => ConsoleColor.Yellow,
                    _ => ConsoleColor.Gray
                };

                var original = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine($"[{evt.TimestampUtc:O}] {evt.EventType}: {evt.Message}");
                Console.ForegroundColor = original;
            }

            return Task.CompletedTask;
        }
    }
}
