using System;
using System.IO;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Reports;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class DiagnosticReportExtensionsTests
{
    [Fact]
    public void GetHealthScore_ReturnsValueWithinBounds()
    {
        var report = CreateSampleReport();

        var score = report.GetHealthScore();

        Assert.InRange(score, 0, 100);
    }

    [Fact]
    public void ToMarkdown_ReturnsNonEmptyContent()
    {
        var markdown = CreateSampleReport().ToMarkdown();

        Assert.False(string.IsNullOrWhiteSpace(markdown));
    }

    [Fact]
    public async Task ExportToJsonAsync_WritesFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"sqldiag-test-{Guid.NewGuid():N}.json");

        try
        {
            await CreateSampleReport().ExportToJsonAsync(filePath);

            Assert.True(File.Exists(filePath));
            var contents = await File.ReadAllTextAsync(filePath);
            Assert.Contains("\"TargetDataSource\"", contents);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static DiagnosticReport CreateSampleReport() =>
        new()
        {
            TargetDataSource = "server.test",
            Connection = new ConnectionMetrics
            {
                TotalAttempts = 10,
                SuccessfulAttempts = 9,
                SuccessRate = 0.9,
                AverageConnectionTime = TimeSpan.FromMilliseconds(250)
            },
            Network = new LatencyMetrics
            {
                Average = TimeSpan.FromMilliseconds(40),
                Jitter = TimeSpan.FromMilliseconds(5)
            }
        };
}
