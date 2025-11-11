using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Logging;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Reports;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public sealed class DiagnosticLoggerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "sqldiag-logs", Guid.NewGuid().ToString("N"));

    public DiagnosticLoggerTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task LogReport_WritesEventsToSink()
    {
        var sink = new TestSink();
        var options = new DiagnosticLoggerOptions
        {
            LogDirectory = _tempDirectory,
            EnableConsoleSink = false
        };

        using (var logger = new DiagnosticLogger(options, new[] { sink }))
        {
            var report = new DiagnosticReport
            {
                TargetDataSource = "TestServer",
                Connection = new ConnectionMetrics
                {
                    TotalAttempts = 5,
                    SuccessfulAttempts = 5,
                    SuccessRate = 1.0
                }
            };

            logger.LogReport(report);
            await Task.Delay(TimeSpan.FromMilliseconds(200)); // allow background writer to flush
        }

        Assert.NotEmpty(sink.Events);
        Assert.Contains(sink.Events, evt => evt.EventType == DiagnosticEventType.General);
        Assert.Contains(sink.Events, evt => evt.EventType == DiagnosticEventType.Connection);
    }

    [Fact]
    public async Task CreatePackageAsync_BuildsArchiveFromLogs()
    {
        var logFile = Path.Combine(_tempDirectory, "diagnostics-20250101.jsonl");
        File.WriteAllText(logFile, "{}");

        var package = await DiagnosticLogger.CreatePackageAsync(_tempDirectory, DateTime.UtcNow.AddYears(-1), cancellationToken: CancellationToken.None);
        Assert.True(File.Exists(package));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private sealed class TestSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public Task WriteAsync(IReadOnlyCollection<DiagnosticEvent> events, CancellationToken cancellationToken)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
