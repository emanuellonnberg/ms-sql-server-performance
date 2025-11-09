using System;
using System.Text;
using System.Threading.Tasks;
using SqlDiagnostics.Client;
using SqlDiagnostics.Reports;

namespace SqlDiagnostics.Cli;

internal static class Program
{
    private const string ConnectionOption = "--connection";
    private const string ConnectionShortOption = "-c";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var connectionString = ResolveConnectionString(args);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("A connection string must be provided using --connection or SQLDIAG_CONNECTION_STRING.");
            return 1;
        }

        try
        {
            using var client = new SqlDiagnosticsClient();
            DiagnosticReport report;

            switch (command)
            {
                case "quick":
                    report = await client.RunQuickCheckAsync(connectionString).ConfigureAwait(false);
                    break;
                case "comprehensive":
                    report = await client.RunComprehensiveAsync(connectionString).ConfigureAwait(false);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown command '{command}'.");
                    PrintHelp();
                    return 1;
            }

            PrintReport(report);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Diagnostics failed: {ex.Message}");
            return 1;
        }
    }

    private static string? ResolveConnectionString(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(ConnectionOption, StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals(ConnectionShortOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
        }

        return Environment.GetEnvironmentVariable("SQLDIAG_CONNECTION_STRING");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("SQL Diagnostics CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  sqldiag quick --connection \"<connection-string>\"");
        Console.WriteLine("  sqldiag comprehensive --connection \"<connection-string>\"");
        Console.WriteLine();
        Console.WriteLine("Environment variables:");
        Console.WriteLine("  SQLDIAG_CONNECTION_STRING   Default connection string when --connection is omitted.");
    }

    private static void PrintReport(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Report generated at {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Target: {report.TargetDataSource ?? "<unknown>"}");
        builder.AppendLine();

        if (report.Connection is not null)
        {
            builder.AppendLine("Connection Metrics");
            builder.AppendLine($"  Attempts: {report.Connection.TotalAttempts}");
            builder.AppendLine($"  Success Rate: {report.Connection.SuccessRate:P1}");
            if (report.Connection.AverageConnectionTime is { } avg)
            {
                builder.AppendLine($"  Avg Time: {avg.TotalMilliseconds:N0} ms");
                builder.AppendLine($"  Min Time: {report.Connection.MinConnectionTime?.TotalMilliseconds:N0} ms");
                builder.AppendLine($"  Max Time: {report.Connection.MaxConnectionTime?.TotalMilliseconds:N0} ms");
            }
            builder.AppendLine();
        }

        if (report.Network is not null)
        {
            builder.AppendLine("Network Metrics");
            if (report.Network.Average is { } avg)
            {
                builder.AppendLine($"  Avg RTT: {avg.TotalMilliseconds:N0} ms");
                builder.AppendLine($"  Min RTT: {report.Network.Min?.TotalMilliseconds:N0} ms");
                builder.AppendLine($"  Max RTT: {report.Network.Max?.TotalMilliseconds:N0} ms");
                builder.AppendLine($"  Jitter: {report.Network.Jitter?.TotalMilliseconds:N0} ms");
            }
            builder.AppendLine();
        }

        if (report.Server is not null)
        {
            builder.AppendLine("Server Metrics");
            var usage = report.Server.ResourceUsage;
            if (usage.CpuUtilizationPercent is { } cpu)
            {
                builder.AppendLine($"  CPU Utilization: {cpu:N1}%");
            }
            if (usage.SqlProcessUtilizationPercent is { } sqlCpu)
            {
                builder.AppendLine($"  SQL CPU Utilization: {sqlCpu:N1}%");
            }
            if (usage.TotalMemoryMb is { } totalMem && usage.AvailableMemoryMb is { } availableMem)
            {
                builder.AppendLine($"  Memory: {availableMem:N0} MB free of {totalMem:N0} MB");
            }
            if (usage.PageLifeExpectancySeconds is { } ple)
            {
                builder.AppendLine($"  Page Life Expectancy: {ple:N0} seconds");
            }
            builder.AppendLine();
        }

        if (report.Recommendations.Count > 0)
        {
            builder.AppendLine("Recommendations");
            foreach (var recommendation in report.Recommendations)
            {
                builder.AppendLine($"  - [{recommendation.Severity}] {recommendation.Category}: {recommendation.Issue}");
                builder.AppendLine($"    {recommendation.RecommendationText}");
                if (!string.IsNullOrEmpty(recommendation.ReferenceLink))
                {
                    builder.AppendLine($"    More info: {recommendation.ReferenceLink}");
                }
            }
            builder.AppendLine();
        }

        Console.WriteLine(builder.ToString());
    }
}
