using System;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Baseline;
using SqlDiagnostics.Core.Integration;
using SqlDiagnostics.Core.Reports;

namespace SqlDiagnostics.Samples;

public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("SQL Diagnostics Sample");

        var connectionString = Environment.GetEnvironmentVariable("SQLDIAG_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("Set SQLDIAG_CONNECTION_STRING to run the sample.");
            return;
        }

        using var logger = SqlDiagnosticsIntegration.CreateDefaultLogger(enableConsole: true);
        using var client = SqlDiagnosticsIntegration.CreateClient(logger);

        var report = await client.RunQuickCheckAsync(connectionString);
        Console.WriteLine($"Quick check complete. Health score: {report.GetHealthScore()}/100");

        var triage = await SqlDiagnosticsIntegration.RunQuickTriageAsync(
            connectionString,
            progress: new Progress<string>(message => Console.WriteLine($"  {message}")));

        Console.WriteLine($"Triage diagnosis: {triage.Diagnosis.Category} - {triage.Diagnosis.Summary}");

        try
        {
            var baseline = await SqlDiagnosticsIntegration.CaptureBaselineAsync(
                connectionString,
                baselineName: "developer-baseline",
                options: new BaselineOptions { SampleCount = 3, SampleInterval = TimeSpan.FromSeconds(1) });

            Console.WriteLine($"Captured baseline '{baseline.Name}' with {baseline.SampleCount} samples.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Baseline capture failed: {ex.Message}");
        }
    }
}
