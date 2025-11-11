using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlDiagnostics.Core;
using SqlDiagnostics.Core.Baseline;
using SqlDiagnostics.Core.Diagnostics.Dataset;
using SqlDiagnostics.Core.Logging;
using SqlDiagnostics.Core.Reports;
using SqlDiagnostics.Core.Triage;

namespace SqlDiagnostics.Cli;

internal static class Program
{
    private const string ConnectionOption = "--connection";
    private const string ConnectionShortOption = "-c";
    private const string FormatOption = "--format";
    private const string FormatShortOption = "-f";
    private const string OutputOption = "--output";
    private const string OutputShortOption = "-o";
    private const string LogDirectoryOption = "--log-dir";
    private const string LogDirectoryShortOption = "-l";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var logDirectory = GetOptionValue(args, LogDirectoryOption, LogDirectoryShortOption);

        DiagnosticLogger? diagnosticLogger = null;
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            diagnosticLogger = new DiagnosticLogger(new DiagnosticLoggerOptions
            {
                LogDirectory = logDirectory
            });
        }

        try
        {
            return command switch
            {
                "quick" => await ExecuteDiagnosticsAsync(DiagnosticsMode.Quick, args, diagnosticLogger).ConfigureAwait(false),
                "comprehensive" or "full" => await ExecuteDiagnosticsAsync(DiagnosticsMode.Full, args, diagnosticLogger).ConfigureAwait(false),
                "triage" => await ExecuteTriageAsync(args, diagnosticLogger).ConfigureAwait(false),
                "baseline" => await ExecuteBaselineAsync(args).ConfigureAwait(false),
                "package" => await ExecutePackageAsync(args, logDirectory).ConfigureAwait(false),
                "dataset-test" => await ExecuteDatasetTestAsync(args, diagnosticLogger).ConfigureAwait(false),
                _ => UnknownCommand(command)
            };
        }
        finally
        {
            diagnosticLogger?.Dispose();
        }
    }

    private static async Task<int> ExecuteDiagnosticsAsync(DiagnosticsMode mode, string[] args, DiagnosticLogger? diagnosticLogger)
    {
        var connectionString = ResolveConnectionString(args);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("A connection string must be provided using --connection or SQLDIAG_CONNECTION_STRING.");
            return 1;
        }

        (var exportFormat, var exportPath) = ResolveExportOptions(args);

        try
        {
            using var client = new SqlDiagnosticsClient(diagnosticLogger: diagnosticLogger);
            DiagnosticReport report = mode switch
            {
                DiagnosticsMode.Quick => await client.RunQuickCheckAsync(connectionString).ConfigureAwait(false),
                _ => await client.RunFullDiagnosticsAsync(connectionString).ConfigureAwait(false)
            };

            var textReport = BuildTextReport(report);

            if (exportFormat is null)
            {
                Console.WriteLine(textReport);
                if (!string.IsNullOrWhiteSpace(exportPath))
                {
                    await File.WriteAllTextAsync(exportPath!, textReport).ConfigureAwait(false);
                    Console.WriteLine($"Report written to {exportPath}");
                }
            }
            else
            {
                var actualPath = string.IsNullOrWhiteSpace(exportPath)
                    ? GetDefaultExportPath(exportFormat.Value)
                    : exportPath!;

                var generator = new ReportGeneratorFactory().Create(exportFormat.Value);
                var content = await generator.GenerateAsync(report).ConfigureAwait(false);
                await File.WriteAllTextAsync(actualPath, content).ConfigureAwait(false);
                Console.WriteLine($"Report written to {actualPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Diagnostics failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteTriageAsync(string[] args, DiagnosticLogger? logger)
    {
        var connectionString = ResolveConnectionString(args);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("A connection string must be provided for triage.");
            return 1;
        }

        var progress = new Progress<string>(message => Console.WriteLine(message));
        var triage = await QuickTriage.RunAsync(connectionString, options: null, progress, CancellationToken.None).ConfigureAwait(false);

        logger?.LogEvent(new DiagnosticEvent
        {
            EventType = DiagnosticEventType.General,
            Severity = EventSeverity.Info,
            Message = $"Triage completed with category {triage.Diagnosis.Category}",
            Data = triage
        });

        Console.WriteLine();
        Console.WriteLine("=== Quick Triage Summary ===");
        Console.WriteLine($"Result: {triage.Diagnosis.Category} - {triage.Diagnosis.Summary}");
        Console.WriteLine($"Duration: {triage.Duration.TotalSeconds:N1} seconds");
        Console.WriteLine();

        PrintTestResult(triage.Network);
        PrintTestResult(triage.Connection);
        PrintTestResult(triage.Query);
        PrintTestResult(triage.Server);
        PrintTestResult(triage.Blocking);

        if (triage.Diagnosis.Recommendations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Recommendations:");
            foreach (var recommendation in triage.Diagnosis.Recommendations)
            {
                Console.WriteLine($"  - {recommendation}");
            }
        }

        return 0;
    }

    private static async Task<int> ExecuteBaselineAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Baseline command requires a subcommand: capture or compare.");
            return 1;
        }

        var subcommand = args[1].ToLowerInvariant();
        var connectionString = ResolveConnectionString(args);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("A connection string must be provided for baseline operations.");
            return 1;
        }

        var baselineName = GetOptionValue(args, "--name", "-n");
        var storeDirectory = GetOptionValue(args, "--baseline-dir", "-bd") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlDiagnostics", "Baselines");

        var manager = new BaselineManager(storeDirectory);

        if (subcommand == "capture")
        {
            if (string.IsNullOrWhiteSpace(baselineName))
            {
                Console.Error.WriteLine("Baseline capture requires --name <baseline-name>.");
                return 1;
            }

            var sampleCount = ParseIntOption(args, "--samples", "-s") ?? 5;
            var intervalSeconds = ParseIntOption(args, "--interval", "-i") ?? 2;

            var options = new BaselineOptions
            {
                SampleCount = sampleCount,
                SampleInterval = TimeSpan.FromSeconds(intervalSeconds)
            };

            Console.WriteLine($"Capturing baseline '{baselineName}' with {sampleCount} samples...");
            var baseline = await manager.CaptureBaselineAsync(connectionString, baselineName, options, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"Captured baseline '{baseline.Name}' at {baseline.CapturedAtUtc:O}.");
            return 0;
        }

        if (subcommand == "compare")
        {
            var report = await manager.CompareToBaselineAsync(connectionString, baselineName, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            if (!report.Success)
            {
                Console.WriteLine(report.Message);
                return 1;
            }

            Console.WriteLine($"Baseline comparison against '{report.BaselineName}' captured {report.BaselineCapturedAtUtc:O}");
            if (!report.HasFindings)
            {
                Console.WriteLine(report.Message);
                return 0;
            }

            foreach (var finding in report.Findings)
            {
                Console.WriteLine($"[{finding.Severity}] {finding.Category}: {finding.Description}");
                if (!string.IsNullOrWhiteSpace(finding.BaselineValue) || !string.IsNullOrWhiteSpace(finding.CurrentValue))
                {
                    Console.WriteLine($"  Baseline: {finding.BaselineValue ?? "n/a"}, Current: {finding.CurrentValue ?? "n/a"}");
                }
            }

            return report.HasFindings ? 2 : 0;
        }

        Console.Error.WriteLine($"Unknown baseline subcommand '{subcommand}'.");
        return 1;
    }

    private static async Task<int> ExecutePackageAsync(string[] args, string? logDirectory)
    {
        var fromDays = ParseIntOption(args, "--days", "-d") ?? 7;
        logDirectory ??= GetOptionValue(args, LogDirectoryOption, LogDirectoryShortOption) ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlDiagnostics", "Logs");

        Console.WriteLine($"Creating diagnostic package from '{logDirectory}' (last {fromDays} days)...");
        try
        {
            var packagePath = await DiagnosticLogger.CreatePackageAsync(
                logDirectory,
                DateTime.UtcNow.AddDays(-fromDays),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"Package created: {packagePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to create package: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteDatasetTestAsync(string[] args, DiagnosticLogger? logger)
    {
        var connectionString = ResolveConnectionString(args);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("A connection string must be provided for dataset-test.");
            return 1;
        }

        var query = GetOptionValue(args, "--query", "-q") ?? "SELECT TOP 50 * FROM sys.objects";

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var command = new SqlCommand(query, connection);
            using var adapter = new SqlDataAdapter(command);

            var diagnostics = new DataSetDiagnostics();
            using var instrumented = diagnostics.Instrument(adapter);
            var dataSet = new DataSet();
            var metrics = await instrumented.FillAsync(dataSet, operationName: "dataset-test").ConfigureAwait(false);

            logger?.LogEvent(new DiagnosticEvent
            {
                EventType = DiagnosticEventType.Dataset,
                Severity = metrics.Error is null ? EventSeverity.Info : EventSeverity.Error,
                Message = $"Dataset instrumentation captured {metrics.RowCount} rows across {metrics.TableCount} tables.",
                Data = metrics
            });

            Console.WriteLine(metrics.GetPerformanceSummary());
            return metrics.Error is null ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Dataset instrumentation failed: {ex.Message}");
            return 1;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        return 1;
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

    private static (ReportFormat? format, string? outputPath) ResolveExportOptions(string[] args)
    {
        ReportFormat? format = null;
        string? output = null;

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (TryConsumeOption(current, FormatOption, FormatShortOption, args, ref i, out var value))
            {
                switch (value.Trim().ToLowerInvariant())
                {
                    case "text":
                        format = null;
                        break;
                    case "json":
                        format = ReportFormat.Json;
                        break;
                    case "html":
                        format = ReportFormat.Html;
                        break;
                    case "markdown":
                    case "md":
                        format = ReportFormat.Markdown;
                        break;
                    default:
                        throw new ArgumentException("Unsupported format. Choose text, json, html, or markdown.");
                }
            }
            else if (TryConsumeOption(current, OutputOption, OutputShortOption, args, ref i, out var path))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new ArgumentException("Output option requires a file path.");
                }

                output = path;
            }
        }

        return (format, output);
    }

    private static bool TryConsumeOption(string current, string option, string shortOption, string[] args, ref int index, out string value)
    {
        if (current.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = current[(option.Length + 1)..];
            return true;
        }

        if (current.StartsWith(shortOption + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = current[(shortOption.Length + 1)..];
            return true;
        }

        if (current.Equals(option, StringComparison.OrdinalIgnoreCase) ||
            current.Equals(shortOption, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Option '{option}' requires a value.");
            }

            value = args[++index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? GetOptionValue(string[] args, string option, string? shortOption = null)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (current.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                return current[(option.Length + 1)..];
            }

            if (!string.IsNullOrWhiteSpace(shortOption) &&
                current.StartsWith(shortOption + "=", StringComparison.OrdinalIgnoreCase))
            {
                return current[(shortOption.Length + 1)..];
            }

            if (current.Equals(option, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(shortOption) && current.Equals(shortOption, StringComparison.OrdinalIgnoreCase)))
            {
                if (i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
        }

        return null;
    }

    private static int? ParseIntOption(string[] args, string option, string? shortOption = null)
    {
        var value = GetOptionValue(args, option, shortOption);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("SQL Diagnostics CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  sqldiag quick --connection \"<connection-string>\" [--format text|json|html|markdown] [--output path]");
        Console.WriteLine("  sqldiag comprehensive --connection \"<connection-string>\"");
        Console.WriteLine("  sqldiag triage --connection \"<connection-string>\"");
        Console.WriteLine("  sqldiag baseline capture --connection \"<connection-string>\" --name <baseline-name> [--samples N] [--interval seconds] [--baseline-dir path]");
        Console.WriteLine("  sqldiag baseline compare --connection \"<connection-string>\" [--name baseline-name] [--baseline-dir path]");
        Console.WriteLine("  sqldiag dataset-test --connection \"<connection-string>\" [--query \"SQL\"]");
        Console.WriteLine("  sqldiag package [--log-dir path] [--days N]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --connection, -c <string>          SQL connection string (or set SQLDIAG_CONNECTION_STRING).");
        Console.WriteLine("  --log-dir, -l <path>               Directory where diagnostics logs are written.");
        Console.WriteLine("  --format, -f <format>              Output format for quick/comprehensive commands.");
        Console.WriteLine("  --output, -o <path>                Write diagnostics report to file.");
        Console.WriteLine("  --name, -n <name>                  Baseline name for capture/compare.");
        Console.WriteLine("  --samples, -s <number>             Number of samples collected during baseline capture.");
        Console.WriteLine("  --interval, -i <seconds>           Delay between baseline samples (seconds).");
        Console.WriteLine("  --baseline-dir, -bd <path>         Directory where baselines are stored.");
        Console.WriteLine("  --days, -d <number>                Number of days of logs to include in package.");
        Console.WriteLine();
        Console.WriteLine("Environment variables:");
        Console.WriteLine("  SQLDIAG_CONNECTION_STRING   Default connection string when --connection is omitted.");
    }

    private static void PrintTestResult(TestResult result)
    {
        Console.WriteLine($"{result.Name,-10}: {(result.Success ? "OK" : "FAIL")} - {result.Details}");
        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"   * {issue}");
        }
    }

    private enum DiagnosticsMode
    {
        Quick,
        Full
    }

    private static string BuildTextReport(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        var baseline = report.BaselineComparison;

        builder.AppendLine($"Report generated at {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Target: {report.TargetDataSource ?? "<unknown>"}");
        builder.AppendLine($"Health Score: {report.GetHealthScore()}/100");
        if (baseline?.HealthScoreDelta is double healthDelta)
        {
            builder.AppendLine($"Baseline Δ: {FormatDelta(healthDelta, " pts")}");
        }
        builder.AppendLine();

        if (baseline?.Notes.Count > 0)
        {
            builder.AppendLine("Baseline Alerts:");
            foreach (var note in baseline.Notes)
            {
                builder.AppendLine($"  - {note}");
            }
            builder.AppendLine();
        }

        if (report.Connection is { } connection)
        {
            builder.AppendLine("Connection");
            builder.AppendLine($"  Attempts: {connection.TotalAttempts}");
            builder.AppendLine($"  Success Rate: {connection.SuccessRate:P1}");
            if (baseline?.ConnectionSuccessRateDelta is double successDelta)
            {
                builder.AppendLine($"  Success Rate Δ: {FormatDelta(successDelta * 100, " %")}");
            }
            builder.AppendLine($"  Average Time: {FormatMilliseconds(connection.AverageConnectionTime)}");
            if (baseline?.ConnectionLatencyDelta is { } connectionLatency)
            {
                builder.AppendLine($"  Average Time Δ: {FormatDelta(connectionLatency.TotalMilliseconds, " ms", "N0")}");
            }
            builder.AppendLine($"  Min / Max: {FormatMilliseconds(connection.MinConnectionTime)} / {FormatMilliseconds(connection.MaxConnectionTime)}");
            builder.AppendLine();
        }

        if (report.ConnectionPool is { } pool)
        {
            builder.AppendLine("Connection Pool");
            builder.AppendLine($"  Pooling Enabled: {FormatBoolean(pool.PoolingEnabled)}");
            builder.AppendLine($"  Pool Size: {pool.MinPoolSize?.ToString("N0") ?? "n/a"} – {pool.MaxPoolSize?.ToString("N0") ?? "n/a"}");
            builder.AppendLine($"  Active / Free: {pool.ActiveConnectionCount?.ToString("N0") ?? "n/a"} / {pool.FreeConnectionCount?.ToString("N0") ?? "n/a"}");
            if (pool.ReclaimedConnectionCount.HasValue)
            {
                builder.AppendLine($"  Reclaimed Connections: {pool.ReclaimedConnectionCount.Value:N0}");
            }
            foreach (var note in pool.Notes)
            {
                builder.AppendLine($"    * {note}");
            }
            builder.AppendLine();
        }

        if (report.ConnectionStability is { Samples.Count: > 0 } stability)
        {
            builder.AppendLine("Connection Stability");
            builder.AppendLine($"  Observations: {stability.Samples.Count:N0} (success {stability.SuccessCount:N0} / failure {stability.FailureCount:N0})");
            builder.AppendLine($"  Success Rate: {stability.SuccessRate:P1}");
            builder.AppendLine($"  Average Latency: {FormatMilliseconds(stability.AverageLatency)}");
            builder.AppendLine($"  Latency Range: {FormatMilliseconds(stability.MinimumLatency)} – {FormatMilliseconds(stability.MaximumLatency)}");
            builder.AppendLine();
        }

        if (report.Network is { } network)
        {
            builder.AppendLine("Network");
            builder.AppendLine($"  Average Latency: {FormatMilliseconds(network.Average)}");
            if (baseline?.NetworkLatencyDelta is { } networkDelta)
            {
                builder.AppendLine($"  Latency Δ: {FormatDelta(networkDelta.TotalMilliseconds, " ms", "N0")}");
            }
            builder.AppendLine($"  Min / Max: {FormatMilliseconds(network.Min)} / {FormatMilliseconds(network.Max)}");
            builder.AppendLine($"  Jitter: {FormatMilliseconds(network.Jitter)}");
        }

        if (report.Dns is { } dns)
        {
            builder.AppendLine($"  DNS Resolution: {FormatMilliseconds(dns.ResolutionTime)}");
            if (dns.Addresses.Count > 0)
            {
                builder.AppendLine("  Addresses:");
                foreach (var address in dns.Addresses)
                {
                    builder.AppendLine($"    - {address}");
                }
            }
        }

        if (report.PortConnectivity is { } port)
        {
            builder.AppendLine($"  Port Connectivity: {(port.IsAccessible ? "Accessible" : "Blocked")} ({FormatMilliseconds(port.RoundtripTime)})");
            if (!string.IsNullOrWhiteSpace(port.FailureReason))
            {
                builder.AppendLine($"    Reason: {port.FailureReason}");
            }
        }

        if (report.Bandwidth is { } bandwidth)
        {
            builder.AppendLine($"  Bandwidth: {(bandwidth.MegabytesPerSecond.HasValue ? bandwidth.MegabytesPerSecond.Value.ToString("N2") + " MB/s" : "n/a")}, Bytes {bandwidth.BytesTransferred:N0}, Iterations {bandwidth.IterationCount:N0}");
        }

        if (report.Network is not null || report.Dns is not null || report.PortConnectivity is not null || report.Bandwidth is not null)
        {
            builder.AppendLine();
        }

        if (report.Query is { } query)
        {
            builder.AppendLine("Query");
            builder.AppendLine($"  Total Execution Time: {FormatMilliseconds(query.TotalExecutionTime)}");
            builder.AppendLine($"  Server / Network Time: {FormatMilliseconds(query.ServerTime)} / {FormatMilliseconds(query.NetworkTime)}");
            builder.AppendLine($"  Rows Returned: {query.RowsReturned:N0}");
            builder.AppendLine($"  Bytes Sent / Received: {query.BytesSent:N0} / {query.BytesReceived:N0}");
            builder.AppendLine($"  Server Round Trips: {query.ServerRoundtrips:N0}");
        }

        if (report.QueryPlan is { PlanXml: { Length: > 0 } planXml })
        {
            var excerpt = Truncate(planXml.ReplaceLineEndings(" "), 400);
            builder.AppendLine("  Execution Plan (excerpt):");
            builder.AppendLine($"    {excerpt}");
        }

        if (report.Blocking is { HasBlocking: true } blocking)
        {
            builder.AppendLine("  Blocking Sessions:");
            foreach (var session in blocking.Sessions.Take(10))
            {
                builder.AppendLine($"    SPID {session.SessionId} blocked by {session.BlockingSessionId} ({session.WaitType ?? "?"}, {session.WaitTime.TotalMilliseconds:N0} ms)");
            }
        }

        if (report.WaitStatistics is { Waits.Count: > 0 } waitStats)
        {
            builder.AppendLine($"  Wait Statistics ({waitStats.Scope}):");
            foreach (var wait in waitStats.Waits.Take(10))
            {
                builder.AppendLine($"    {wait.WaitType}: Wait {wait.WaitTimeMilliseconds:N0} ms, Signal {wait.SignalWaitTimeMilliseconds:N0} ms, Tasks {wait.WaitingTasks:N0}");
            }
        }

        if (report.Query is not null || report.QueryPlan is not null || (report.Blocking?.HasBlocking ?? false) || (report.WaitStatistics?.Waits.Count > 0))
        {
            builder.AppendLine();
        }

        if (report.Server is { } server)
        {
            builder.AppendLine("Server");
            var usage = server.ResourceUsage;
            builder.AppendLine($"  CPU Utilisation: {usage.CpuUtilizationPercent?.ToString("N1") ?? "n/a"}%");
            if (baseline?.ServerCpuDelta is double cpuDelta)
            {
                builder.AppendLine($"  CPU Δ vs baseline: {FormatDelta(cpuDelta, " %")}");
            }
            builder.AppendLine($"  SQL CPU Utilisation: {usage.SqlProcessUtilizationPercent?.ToString("N1") ?? "n/a"}%");
            builder.AppendLine($"  Memory Available: {(usage.AvailableMemoryMb.HasValue && usage.TotalMemoryMb.HasValue ? $"{usage.AvailableMemoryMb.Value:N0} MB of {usage.TotalMemoryMb.Value:N0} MB" : "n/a")}");
            builder.AppendLine($"  Page Life Expectancy: {usage.PageLifeExpectancySeconds?.ToString("N0") ?? "n/a"} s");
            builder.AppendLine($"  IO Stall: {usage.IoStallMs?.ToString("N0") ?? "n/a"} ms");

            if (server.Waits.Count > 0)
            {
                builder.AppendLine("  Top Waits:");
                foreach (var wait in server.Waits.Take(5))
                {
                    builder.AppendLine($"    {wait.WaitType}: {wait.WaitTimeMs:N0} ms (signal {wait.SignalWaitTimeMs:N0} ms, tasks {wait.WaitingTasksCount:N0})");
                }
            }

            if (server.PerformanceCounters.Count > 0)
            {
                builder.AppendLine("  Performance Counters:");
                foreach (var counter in server.PerformanceCounters.Take(10))
                {
                    var instanceSuffix = string.IsNullOrWhiteSpace(counter.InstanceName) ? string.Empty : $" ({counter.InstanceName})";
                    builder.AppendLine($"    {counter.CounterName}{instanceSuffix}: {counter.Value:N2}");
                }
            }

            if (server.Configuration.Count > 0)
            {
                builder.AppendLine("  Configuration Drift:");
                foreach (var setting in server.Configuration.Take(10))
                {
                    builder.AppendLine($"    {setting.Name}: configured {setting.Value?.ToString("N2") ?? "n/a"}, in use {setting.ValueInUse?.ToString("N2") ?? "n/a"}");
                }
            }

            builder.AppendLine();
        }

        if (report.Databases is { Databases.Count: > 0 } dbMetrics)
        {
            builder.AppendLine("Databases");
            foreach (var database in dbMetrics.Databases.Take(10))
            {
                builder.AppendLine($"  {database.Name}: State={database.State ?? "?"}, Recovery={database.RecoveryModel ?? "?"}");
                builder.AppendLine($"    Size (Data / Log): {database.DataFileSizeMb?.ToString("N1") ?? "n/a"} MB / {database.LogFileSizeMb?.ToString("N1") ?? "n/a"} MB");
                if (database.LogUsedPercent.HasValue)
                {
                    builder.AppendLine($"    Log Used: {database.LogUsedPercent.Value:N1}%");
                }
                if (database.ActiveSessionCount.HasValue || database.RunningRequestCount.HasValue)
                {
                    builder.AppendLine($"    Sessions: {database.ActiveSessionCount?.ToString("N0") ?? "n/a"}, Active Requests: {database.RunningRequestCount?.ToString("N0") ?? "n/a"}");
                }
            }
            if (dbMetrics.Metadata.TryGetValue("warning", out var warning) && warning is string warningText && !string.IsNullOrWhiteSpace(warningText))
            {
                builder.AppendLine($"  Warning: {warningText}");
            }
            builder.AppendLine();
        }

        if (baseline is not null)
        {
            builder.AppendLine("Baseline Comparison");
            builder.AppendLine($"  Health Score Δ: {FormatDelta(baseline.HealthScoreDelta ?? 0, " pts")}");
            builder.AppendLine($"  Connection Success Rate Δ: {FormatDelta((baseline.ConnectionSuccessRateDelta ?? 0) * 100, " %")}");
            builder.AppendLine($"  Connection Latency Δ: {FormatDelta((baseline.ConnectionLatencyDelta?.TotalMilliseconds) ?? 0, " ms", "N0")}");
            builder.AppendLine($"  Network Latency Δ: {FormatDelta((baseline.NetworkLatencyDelta?.TotalMilliseconds) ?? 0, " ms", "N0")}");
            builder.AppendLine($"  Server CPU Δ: {FormatDelta(baseline.ServerCpuDelta ?? 0, " %")}");
            builder.AppendLine();
        }

        if (report.Recommendations.Count > 0)
        {
            builder.AppendLine("Recommendations");
            foreach (var recommendation in report.Recommendations)
            {
                builder.AppendLine($"  - [{recommendation.Severity}] {recommendation.Category}: {recommendation.Issue}");
                builder.AppendLine($"    {recommendation.RecommendationText}");
                if (!string.IsNullOrWhiteSpace(recommendation.ReferenceLink))
                {
                    builder.AppendLine($"    More info: {recommendation.ReferenceLink}");
                }
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatMilliseconds(TimeSpan value) =>
        $"{value.TotalMilliseconds:N0} ms";

    private static string FormatMilliseconds(TimeSpan? value) =>
        value.HasValue ? FormatMilliseconds(value.Value) : "n/a";

    private static string FormatBoolean(bool value) => value ? "Yes" : "No";

    private static string FormatDelta(double value, string suffix = "", string format = "N1")
    {
        if (Math.Abs(value) < 0.0001)
        {
            return "0" + suffix;
        }

        var sign = value >= 0 ? "+" : "-";
        return $"{sign}{Math.Abs(value).ToString(format, CultureInfo.InvariantCulture)}{suffix}";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    private static string GetDefaultExportPath(ReportFormat format)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return $"diagnostic-report-{timestamp}.{GetExtension(format)}";
    }

    private static string GetExtension(ReportFormat format) =>
        format switch
        {
            ReportFormat.Json => "json",
            ReportFormat.Html => "html",
            ReportFormat.Markdown => "md",
            _ => "txt"
        };
}
