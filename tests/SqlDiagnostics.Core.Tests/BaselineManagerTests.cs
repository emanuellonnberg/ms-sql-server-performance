using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Baseline;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Reports;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public sealed class BaselineManagerTests : IDisposable
{
    private readonly string _tempDirectory;

    public BaselineManagerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "sqldiag-baseline-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task CaptureBaselineAsync_ComputesExpectedAggregations()
    {
        // Arrange
        var samples = new Queue<DiagnosticReport>(new[]
        {
            CreateReport(100, 0.95, 20),
            CreateReport(140, 0.90, 25),
            CreateReport(80, 0.98, 18)
        });

        var manager = new BaselineManager(
            _tempDirectory,
            (cs, token) => Task.FromResult(samples.Dequeue()));

        // Act
        var baseline = await manager.CaptureBaselineAsync(
            "Server=(local);Database=master;",
            "baseline-dev",
            new BaselineOptions { SampleCount = 3, SampleInterval = TimeSpan.Zero },
            CancellationToken.None);

        // Assert
        Assert.Equal(3, baseline.SampleCount);
        Assert.NotNull(baseline.Connection.MedianConnectionTimeMs);
        Assert.NotNull(baseline.Connection.P95ConnectionTimeMs);
        Assert.NotNull(baseline.Connection.SuccessRate);
        Assert.NotNull(baseline.Network.MedianLatencyMs);
    }

    [Fact]
    public async Task CompareToBaselineAsync_FlagsConnectionRegression()
    {
        // Arrange baseline capture
        var captureSamples = new Queue<DiagnosticReport>(new[]
        {
            CreateReport(80, 0.98, 15),
            CreateReport(90, 0.99, 16),
            CreateReport(85, 0.97, 14)
        });

        var manager = new BaselineManager(
            _tempDirectory,
            (cs, token) => Task.FromResult(captureSamples.Dequeue()));

        await manager.CaptureBaselineAsync(
            "Server=(local);Database=master;",
            "production",
            new BaselineOptions { SampleCount = 3, SampleInterval = TimeSpan.Zero },
            CancellationToken.None);

        // Override runner for comparison
        var comparisonManager = new BaselineManager(
            _tempDirectory,
            (cs, token) => Task.FromResult(CreateReport(200, 0.6, 20)));

        // Act
        var report = await comparisonManager.CompareToBaselineAsync(
            "Server=(local);Database=master;",
            baselineName: "production",
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.True(report.Success);
        Assert.True(report.HasFindings);
        Assert.Contains(report.Findings, f => f.Category == "Connection");
        Assert.Contains(report.Findings, f => f.Category == "Reliability");
    }

    [Fact]
    public async Task CompareToBaselineAsync_ReturnsMessageWhenBaselineMissing()
    {
        var manager = new BaselineManager(_tempDirectory, (cs, token) => Task.FromResult(CreateReport(100, 1.0, 10)));

        var report = await manager.CompareToBaselineAsync(
            "Server=(local);Database=master;",
            baselineName: "missing",
            cancellationToken: CancellationToken.None);

        Assert.False(report.Success);
        Assert.False(report.HasFindings);
        Assert.Contains("Baseline not found", report.Message);
    }

    private static DiagnosticReport CreateReport(double connectionMs, double successRate, double networkMs)
    {
        return new DiagnosticReport
        {
            Connection = new ConnectionMetrics
            {
                AverageConnectionTime = TimeSpan.FromMilliseconds(connectionMs),
                SuccessRate = successRate
            },
            Network = new LatencyMetrics
            {
                Average = TimeSpan.FromMilliseconds(networkMs)
            }
        };
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
            // Swallow cleanup exceptions
        }
    }
}
