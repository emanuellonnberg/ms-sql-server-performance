using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlDiagnostics.Core.Triage;

/// <summary>
/// Provides a lightweight, client-side triage workflow for SQL connectivity issues.
/// </summary>
public static class QuickTriage
{
    /// <summary>
    /// Executes the triage workflow and returns a consolidated result.
    /// </summary>
    public static async Task<TriageResult> RunAsync(
        string connectionString,
        QuickTriageOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

    var effectiveOptions = options ?? new QuickTriageOptions();
        var result = new TriageResult
        {
            StartedAtUtc = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();
        if (effectiveOptions.CustomProbes != null && effectiveOptions.CustomProbes.Count > 0)
        {
            // Run custom probes in order, store results by name
            var probeResults = new System.Collections.Generic.Dictionary<string, TestResult>();
            if (effectiveOptions.EnableParallelProbes)
            {
                var probeTasks = new System.Collections.Generic.List<Task<(string, TestResult)>>();
                foreach (var probeTuple in effectiveOptions.CustomProbes)
                {
                    var name = probeTuple.Name;
                    var probe = probeTuple.Probe;
                    probeTasks.Add(Task.Run(async () => (name, await ExecuteProbeAsync(
                        connectionString,
                        probe,
                        name,
                        progress,
                        $"Running custom probe: {name}...",
                        cancellationToken,
                        effectiveOptions).ConfigureAwait(false))));
                }
                var results = await Task.WhenAll(probeTasks);
                foreach (var (name, testResult) in results)
                {
                    probeResults[name] = testResult;
                }
            }
            else
            {
                foreach (var probeTuple in effectiveOptions.CustomProbes)
                {
                    var name = probeTuple.Name;
                    var probe = probeTuple.Probe;
                    var testResult = await ExecuteProbeAsync(
                        connectionString,
                        probe,
                        name,
                        progress,
                        $"Running custom probe: {name}...",
                        cancellationToken,
                        effectiveOptions).ConfigureAwait(false);
                    probeResults[name] = testResult;
                }
            }
            // Map known names to result fields if present
            result.Network = probeResults.TryGetValue("Network", out var n) ? n : result.Network;
            result.Connection = probeResults.TryGetValue("Connection", out var c) ? c : result.Connection;
            result.Query = probeResults.TryGetValue("Query", out var q) ? q : result.Query;
            result.Server = probeResults.TryGetValue("Server", out var s) ? s : result.Server;
            result.Blocking = probeResults.TryGetValue("Blocking", out var b) ? b : result.Blocking;
        }
        else
        {
            var networkProbe = effectiveOptions.NetworkProbe ?? NetworkProbeAsync;
            Func<string, CancellationToken, Task<TestResult>> connectionProbe =
                effectiveOptions.ConnectionProbe ?? ((cs, token) => ConnectionProbeAsync(cs, effectiveOptions, token));
            Func<string, CancellationToken, Task<TestResult>> queryProbe =
                effectiveOptions.QueryProbe ?? ((cs, token) => QueryProbeAsync(cs, effectiveOptions, token));
            var serverProbe = effectiveOptions.ServerProbe ?? ServerProbeAsync;
            var blockingProbe = effectiveOptions.BlockingProbe ?? BlockingProbeAsync;

            if (effectiveOptions.EnableParallelProbes)
            {
                var tasks = new[]
                {
                    Task.Run(() => ExecuteProbeAsync(connectionString, networkProbe, "Network", progress, "Testing network connectivity...", cancellationToken, effectiveOptions)),
                    Task.Run(() => ExecuteProbeAsync(connectionString, connectionProbe, "Connection", progress, "Testing SQL connection...", cancellationToken, effectiveOptions)),
                    Task.Run(() => ExecuteProbeAsync(connectionString, queryProbe, "Query", progress, "Running diagnostic query...", cancellationToken, effectiveOptions)),
                    Task.Run(() => ExecuteProbeAsync(connectionString, serverProbe, "Server", progress, "Checking server health...", cancellationToken, effectiveOptions)),
                    Task.Run(() => ExecuteProbeAsync(connectionString, blockingProbe, "Blocking", progress, "Inspecting for blocking sessions...", cancellationToken, effectiveOptions))
                };
                await Task.WhenAll(tasks);
                result.Network = tasks[0].Result;
                result.Connection = tasks[1].Result;
                result.Query = tasks[2].Result;
                result.Server = tasks[3].Result;
                result.Blocking = tasks[4].Result;
            }
            else
            {
                result.Network = await ExecuteProbeAsync(
                    connectionString,
                    networkProbe,
                    "Network",
                    progress,
                    "Testing network connectivity...",
                    cancellationToken,
                    effectiveOptions).ConfigureAwait(false);

                result.Connection = await ExecuteProbeAsync(
                    connectionString,
                    connectionProbe,
                    "Connection",
                    progress,
                    "Testing SQL connection...",
                    cancellationToken,
                    effectiveOptions).ConfigureAwait(false);

                result.Query = await ExecuteProbeAsync(
                    connectionString,
                    queryProbe,
                    "Query",
                    progress,
                    "Running diagnostic query...",
                    cancellationToken,
                    effectiveOptions).ConfigureAwait(false);

                result.Server = await ExecuteProbeAsync(
                    connectionString,
                    serverProbe,
                    "Server",
                    progress,
                    "Checking server health...",
                    cancellationToken,
                    effectiveOptions).ConfigureAwait(false);

                result.Blocking = await ExecuteProbeAsync(
                    connectionString,
                    blockingProbe,
                    "Blocking",
                    progress,
                    "Inspecting for blocking sessions...",
                    cancellationToken,
                    effectiveOptions).ConfigureAwait(false);
            }
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        result.CompletedAtUtc = DateTime.UtcNow;
        result.Diagnosis = Diagnose(result);

        progress?.Report($"Triage complete: {result.Diagnosis.Category}");
        return result;
    }

    private static async Task<TestResult> ExecuteProbeAsync(
    string connectionString,
    Func<string, CancellationToken, Task<TestResult>> probe,
    string testName,
    IProgress<string>? progress,
    string progressMessage,
    CancellationToken cancellationToken,
    QuickTriageOptions options)
    {
        progress?.Report(progressMessage);

        try
        {
            var start = DateTimeOffset.UtcNow;
            var result = await probe(connectionString, cancellationToken).ConfigureAwait(false);
            var end = DateTimeOffset.UtcNow;
            result.StartTimeUtc = start;
            result.EndTimeUtc = end;
            // Log slow operation if duration is available and logger is set
            if (result.Duration > TimeSpan.Zero && options.Logger != null)
            {
                var slowThreshold = testName switch
                {
                    "Connection" => options.SlowConnectionThreshold,
                    "Query" => options.SlowQueryThreshold,
                    _ => TimeSpan.FromSeconds(2)
                };
                if (result.Duration > slowThreshold)
                {
                    options.Logger.LogEvent(new SqlDiagnostics.Core.Logging.DiagnosticEvent
                    {
                        EventType = SqlDiagnostics.Core.Logging.DiagnosticEventType.General,
                        Severity = SqlDiagnostics.Core.Logging.EventSeverity.Warning,
                        Message = $"{testName} probe was slow: {result.Duration.TotalMilliseconds:N0} ms.",
                        Source = "QuickTriage",
                        Data = result
                    });
                }
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var details = ex.Message;
            if (ex.InnerException != null)
            {
                details += $" | Inner: {ex.InnerException.Message}";
            }
            options.Logger?.LogEvent(new SqlDiagnostics.Core.Logging.DiagnosticEvent
            {
                EventType = SqlDiagnostics.Core.Logging.DiagnosticEventType.Error,
                Severity = SqlDiagnostics.Core.Logging.EventSeverity.Error,
                Message = $"{testName} probe failed: {details}",
                Source = "QuickTriage",
                Data = details
            });
            return TestResult.Failure(testName, details);
        }
    }

    private static async Task<TestResult> NetworkProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var server = builder.DataSource;

        var test = new TestResult("Network");
        if (string.IsNullOrWhiteSpace(server))
        {
            test.Success = false;
            test.Details = "Unable to determine server name from connection string.";
            return test;
        }

        var host = server.Split(',')[0];
        using var ping = new Ping();
        const int attempts = 4;
        var successes = 0;
        var latencies = new List<double>();

        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            test.Details = $"Pinging {host} (attempt {i + 1} of {attempts})...";
            test.AddIssue($"Ping attempt {i + 1} started.");

            try
            {
                var reply = await ping.SendPingAsync(host, 2000).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    successes++;
                    latencies.Add(reply.RoundtripTime);
                    test.AddIssue($"Ping {i + 1}: Success ({reply.RoundtripTime} ms)");
                }
                else
                {
                    test.AddIssue($"Ping {i + 1}: {reply.Status}");
                }
            }
            catch (PingException ex)
            {
                test.AddIssue($"Ping attempt {i + 1} failed: {ex.Message}");
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        test.Success = successes > 0;
        test.Details = $"{successes}/{attempts} pings succeeded.";
        if (successes > 0)
        {
            var average = latencies.Average();
            test.Duration = TimeSpan.FromMilliseconds(average);
            if (average > 100)
            {
                test.AddIssue($"High latency detected ({average:N0} ms).");
            }
        }
        else
        {
            test.AddIssue("Server is unreachable via ICMP (firewall or network issue).");
        }

        return test;
    }

    private static async Task<TestResult> ConnectionProbeAsync(
        string connectionString,
        QuickTriageOptions options,
        CancellationToken cancellationToken)
    {
        var test = new TestResult("Connection");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            test.Details = "Opening SQL connection...";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            test.Success = true;
            test.Duration = stopwatch.Elapsed;
            test.Details = $"Connection established in {stopwatch.ElapsedMilliseconds} ms.";

            if (stopwatch.Elapsed > options.SlowConnectionThreshold)
            {
                test.AddIssue("Slow connection establishment.");
            }
        }
        catch (SqlException ex)
        {
            test.Success = false;
            var details = $"SQL error {ex.Number}: {ex.Message}";
            if (ex.InnerException != null)
            {
                details += $" | Inner: {ex.InnerException.Message}";
            }
            test.Details = details;
            test.AddIssue("Failed to open SQL connection.");
        }
        catch (Exception ex)
        {
            test.Success = false;
            var details = ex.Message;
            if (ex.InnerException != null)
            {
                details += $" | Inner: {ex.InnerException.Message}";
            }
            test.Details = details;
            test.AddIssue("Unexpected error opening SQL connection.");
        }

        return test;
    }

    private static async Task<TestResult> QueryProbeAsync(
        string connectionString,
        QuickTriageOptions options,
        CancellationToken cancellationToken)
    {
        var test = new TestResult("Query");

        try
        {
            test.Details = "Opening SQL connection for diagnostic query...";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            connection.StatisticsEnabled = true;

            var stopwatch = Stopwatch.StartNew();
            test.Details = "Executing diagnostic query...";
            await using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            test.Success = true;
            test.Duration = stopwatch.Elapsed;
            test.Details = $"Diagnostic query executed in {stopwatch.ElapsedMilliseconds} ms.";

            if (stopwatch.Elapsed > options.SlowQueryThreshold)
            {
                test.AddIssue("Diagnostic query executed slower than expected.");
            }
        }
        catch (Exception ex)
        {
            test.Success = false;
            var details = ex.Message;
            if (ex.InnerException != null)
            {
                details += $" | Inner: {ex.InnerException.Message}";
            }
            test.Details = details;
            test.AddIssue("Failed to execute diagnostic query.");
        }

        return test;
    }

    private static async Task<TestResult> ServerProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        var test = new TestResult("Server");

        try
        {
            test.Details = "Opening SQL connection for server health check...";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string Query = @"
                SELECT TOP (1) 
                    cpu_percent = record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int')
                FROM (
                    SELECT CONVERT(XML, record) AS record
                    FROM sys.dm_os_ring_buffers
                    WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
                ) AS rb
                ORDER BY cpu_percent DESC;";

            test.Details = "Querying server CPU utilisation...";
            await using var command = new SqlCommand(Query, connection);
            var cpu = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as int?;

            test.Success = true;
            test.Details = cpu.HasValue
                ? $"SQL Server process CPU utilisation: {cpu.Value}%."
                : "SQL Server process CPU utilisation unavailable.";

            if (cpu.HasValue && cpu.Value > 80)
            {
                test.AddIssue("High SQL Server CPU utilisation detected.");
            }
        }
        catch (Exception ex)
        {
            test.Success = false;
            var details = ex.Message;
            if (ex.InnerException != null)
            {
                details += $" | Inner: {ex.InnerException.Message}";
            }
            test.Details = details;
            test.AddIssue("Unable to retrieve server health information.");
        }

        return test;
    }

    private static async Task<TestResult> BlockingProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        var test = new TestResult("Blocking");

        try
        {
            test.Details = "Opening SQL connection for blocking check...";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string Query = @"
                SELECT COUNT(*) 
                FROM sys.dm_exec_requests 
                WHERE blocking_session_id <> 0;";

            test.Details = "Querying for blocking sessions...";
            await using var command = new SqlCommand(Query, connection);
            var blocked = (int)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            test.Success = true;
            test.Details = blocked > 0
                ? $"{blocked} blocked sessions detected."
                : "No blocking sessions detected.";

            if (blocked > 0)
            {
                test.AddIssue("Blocking detected. Investigate long-running transactions.");
            }
        }
        catch (Exception ex)
        {
            test.Success = false;
            var details = ex.Message;
            if (ex.InnerException != null)
            {
                details += $" | Inner: {ex.InnerException.Message}";
            }
            test.Details = details;
            test.AddIssue("Unable to inspect blocking state.");
        }

        return test;
    }

    private static Diagnosis Diagnose(TriageResult result)
    {
        if (result.Network.Success == false || result.Network.Issues.Any())
        {
            return new Diagnosis
            {
                Category = "Network",
                Summary = "Network connectivity issues detected.",
                Details = result.Network.Details,
                Recommendations =
                {
                    "Verify that the SQL Server host is reachable (firewall, VPN, routing).",
                    "Check network latency and packet loss.",
                    "See: https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-network-configuration"
                }
            };
        }

        if (result.Connection.Success == false)
        {
            return new Diagnosis
            {
                Category = "Connection",
                Summary = "Failed to establish SQL connection.",
                Details = result.Connection.Details,
                Recommendations =
                {
                    "Confirm credentials and database accessibility.",
                    "Ensure SQL Server is configured to accept remote connections.",
                    "See: https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/enable-or-disable-a-server-network-protocol"
                }
            };
        }

        if (result.Blocking.Issues.Any())
        {
            return new Diagnosis
            {
                Category = "Blocking",
                Summary = "Blocking sessions detected on SQL Server.",
                Details = result.Blocking.Details,
                Recommendations =
                {
                    "Identify blocking sessions and review transaction scope.",
                    "Consider collecting execution plans for blocked queries.",
                    "See: https://learn.microsoft.com/en-us/sql/relational-databases/performance-monitor/analyzing-blocking"
                }
            };
        }

        if (result.Server.Issues.Any())
        {
            return new Diagnosis
            {
                Category = "Server Resource",
                Summary = "Potential server resource pressure detected.",
                Details = result.Server.Details,
                Recommendations =
                {
                    "Inspect DMV metrics for CPU and IO utilisation.",
                    "Review workload patterns and index efficiency.",
                    "See: https://learn.microsoft.com/en-us/sql/relational-databases/performance/performance-dashboard"
                }
            };
        }

        if (result.Query.Issues.Any())
        {
            return new Diagnosis
            {
                Category = "Query Performance",
                Summary = "Diagnostic query executed slower than expected.",
                Details = result.Query.Details,
                Recommendations =
                {
                    "Capture execution plan for slow queries.",
                    "Validate statistics and consider indexing strategies.",
                    "See: https://learn.microsoft.com/en-us/sql/relational-databases/performance/query-performance-issues"
                }
            };
        }

        return new Diagnosis
        {
            Category = "Healthy",
            Summary = "No immediate issues detected by triage probes.",
            Details = "All probes completed successfully.",
            Recommendations =
            {
                "Monitor the workload for intermittent issues.",
                "Capture a performance baseline for future comparisons."
            }
        };
    }
}
