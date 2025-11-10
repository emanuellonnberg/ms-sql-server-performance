using System;
using System.Threading.Tasks;
using SqlDiagnostics.Core;

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

        using var client = new SqlDiagnosticsClient();
        var report = await client.RunQuickCheckAsync(connectionString);

        Console.WriteLine($"Quick check complete. Success rate: {report.Connection?.SuccessRate:P1}");
    }
}
