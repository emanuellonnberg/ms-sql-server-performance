using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SqlDiagnostics.Core;
using SqlDiagnostics.Core.Reports;

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
                case "full":
                    report = await client.RunFullDiagnosticsAsync(connectionString).ConfigureAwait(false);
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
            else if (args[i].StartsWith($"{ConnectionOption}=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(ConnectionOption.Length + 1)..];
            }
            else if (args[i].StartsWith($"{ConnectionShortOption}=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(ConnectionShortOption.Length + 1)..];
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
        Console.WriteLine("  sqldiag quick --connection=<connection-string>");
        Console.WriteLine("  sqldiag comprehensive --connection \"<connection-string>\"");
        Console.WriteLine("  sqldiag comprehensive --connection=<connection-string>");
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
            if (report.Server.Waits.Count > 0)
            {
                builder.AppendLine("  Top Waits:");
                foreach (var wait in report.Server.Waits.Take(5))
                {
                    builder.AppendLine($"    {wait.WaitType}: Wait={wait.WaitTimeMs:N0} ms, Signal={wait.SignalWaitTimeMs:N0} ms, Tasks={wait.WaitingTasksCount:N0}");
                }
            }
            if (report.Server.PerformanceCounters.Count > 0)
            {
                builder.AppendLine("  Performance Counters:");
                foreach (var counter in report.Server.PerformanceCounters)
                {
                    var instanceSuffix = string.IsNullOrWhiteSpace(counter.InstanceName) ? string.Empty : $" ({counter.InstanceName})";
                    var formattedValue = double.IsNaN(counter.Value)
                        ? "n/a"
                        : counter.CounterName switch
                        {
                            "Buffer cache hit ratio" => $"{counter.Value:N2}%",
                            "Page life expectancy" => $"{counter.Value:N0} sec",
                            "User Connections" => counter.Value.ToString("N0"),
                            _ => $"{counter.Value:N0} (cumulative)"
                        };
                    builder.AppendLine($"    {counter.CounterName}{instanceSuffix}: {formattedValue}");
                }
            }
            builder.AppendLine();
        }

        if (report.Databases is { Databases.Count: > 0 } dbMetrics)
        {
            builder.AppendLine("Database Metrics");
            foreach (var database in dbMetrics.Databases)
            {
                builder.AppendLine($"  {database.Name}: State={database.State ?? "?"}, Recovery={database.RecoveryModel ?? "?"}");

                var dataSize = database.DataFileSizeMb;
                var logSize = database.LogFileSizeMb;
                if (dataSize.HasValue || logSize.HasValue)
                {
                    builder.AppendLine($"    Data Size: {(dataSize.HasValue ? dataSize.Value.ToString("N1") : "n/a")} MB, Log Size: {(logSize.HasValue ? logSize.Value.ToString("N1") : "n/a")} MB");
                }

                if (database.LogUsedPercent is { } logUsed)
                {
                    builder.AppendLine($"    Log Used: {logUsed:N1}%");
                }

                var sessions = database.ActiveSessionCount;
                var requests = database.RunningRequestCount;
                if (sessions.HasValue || requests.HasValue)
                {
                    builder.AppendLine($"    Sessions: {(sessions.HasValue ? sessions.Value.ToString("N0") : "n/a")}, Active Requests: {(requests.HasValue ? requests.Value.ToString("N0") : "n/a")}");
                }
            }

            if (dbMetrics.Metadata.TryGetValue("warning", out var warning) && warning is string warningText && !string.IsNullOrWhiteSpace(warningText))
            {
                builder.AppendLine($"  Warning: {warningText}");
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
