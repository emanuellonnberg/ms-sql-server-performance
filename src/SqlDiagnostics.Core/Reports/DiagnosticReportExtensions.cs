using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Reports;

/// <summary>
/// Helper methods for working with diagnostic reports.
/// </summary>
public static class DiagnosticReportExtensions
{
    internal static readonly JsonSerializerOptions JsonOptions =
#if NETSTANDARD2_0
        new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
#else
        new(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
#endif

    public static async Task ExportToJsonAsync(
        this DiagnosticReport report,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must be provided.", nameof(filePath));
        }

        EnsureDirectoryExists(filePath);

        var payload = BuildJson(report);
        await WriteTextAsync(filePath, payload, cancellationToken).ConfigureAwait(false);
    }

    public static async Task ExportToHtmlAsync(
        this DiagnosticReport report,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must be provided.", nameof(filePath));
        }

        EnsureDirectoryExists(filePath);

        var builder = BuildHtml(report);
        await WriteTextAsync(filePath, builder, cancellationToken).ConfigureAwait(false);
    }

    public static string ToMarkdown(this DiagnosticReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return BuildMarkdown(report);
    }

    public static int GetHealthScore(this DiagnosticReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        double score = 100;

        if (report.Connection is not null)
        {
        var successRate = Clamp(report.Connection.SuccessRate, 0d, 1d);
            score -= (1 - successRate) * 40;

            if (report.Connection.AverageConnectionTime is { } avg)
            {
                var penalty = Math.Min(avg.TotalMilliseconds / 1000.0, 1.0) * 15;
                score -= penalty;
            }
        }

        if (report.Network is not null && report.Network.Average is { } latency)
        {
            var latencyPenalty = Math.Min(latency.TotalMilliseconds / 200.0, 1.0) * 20;
            score -= latencyPenalty;

            if (report.Network.Jitter is { } jitter)
            {
                score -= Math.Min(jitter.TotalMilliseconds / 100.0, 1.0) * 10;
            }
        }

        if (report.Recommendations.Count > 0)
        {
            var criticalPenalty = report.Recommendations.Count(r => r.Severity == RecommendationSeverity.Critical) * 10;
            var warningPenalty = report.Recommendations.Count(r => r.Severity == RecommendationSeverity.Warning) * 5;
            score -= criticalPenalty + warningPenalty;
        }

        return (int)Math.Max(0, Math.Min(100, Math.Round(score)));
    }

    internal static string BuildJson(DiagnosticReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    internal static string BuildHtml(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        var healthScore = report.GetHealthScore();
        var baseline = report.BaselineComparison;

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"UTF-8\" />");
        builder.AppendLine("<title>SQL Diagnostics Report</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:'Segoe UI',Arial,sans-serif;margin:2rem;background:#f7f8fb;color:#1b1d21;}");
        builder.AppendLine("h1{font-size:2rem;margin-bottom:0.25rem;} h2{margin-top:2rem;color:#2c3a4d;} h3{margin-top:1.25rem;color:#34455d;}");
        builder.AppendLine("header{border-bottom:1px solid #d9dde7;padding-bottom:1rem;margin-bottom:2rem;}");
        builder.AppendLine(".summary ul{list-style:disc;padding-left:1.5rem;} .summary li{margin-bottom:0.35rem;}");
        builder.AppendLine(".pill{display:inline-block;padding:0.4rem 1rem;border-radius:999px;background:#1e88e5;color:#fff;font-weight:600;margin-right:1rem;}");
        builder.AppendLine(".delta{font-weight:600;margin-left:0.5rem;} .delta.positive{color:#1b9e51;} .delta.negative{color:#d13b3b;}");
        builder.AppendLine(".panel{background:#fff;border-radius:12px;padding:1.25rem;margin-bottom:1.5rem;box-shadow:0 6px 18px rgba(27,30,35,0.08);}");
        builder.AppendLine("table.metric-table{border-collapse:collapse;width:100%;margin-top:0.75rem;} .metric-table th,.metric-table td{border:1px solid #e1e5ec;padding:0.55rem 0.7rem;text-align:left;}");
        builder.AppendLine(".metric-table th{background:#f2f5fb;width:26%;font-weight:600;} .badge{display:inline-block;padding:0.25rem 0.6rem;border-radius:6px;background:#eef2fa;color:#3b4c68;font-size:0.85rem;margin-right:0.35rem;}");
        builder.AppendLine("details{margin-top:0.5rem;} pre{background:#111317;color:#f5f5f5;padding:0.75rem;border-radius:8px;overflow:auto;max-height:320px;}");
        builder.AppendLine(".note-list{padding-left:1.4rem;} .note-list li{margin-bottom:0.35rem;}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head><body>");

        builder.AppendLine("<header>");
        builder.AppendLine("<h1>SQL Diagnostics Report</h1>");
        builder.AppendLine($"<p><strong>Generated:</strong> {report.GeneratedAtUtc:O}<br/>");
        builder.AppendLine($"<strong>Target:</strong> {Escape(report.TargetDataSource ?? "Unknown")}</p>");
        builder.AppendLine("<div>");
        builder.AppendLine($"  <span class=\"pill\">Health Score: {healthScore}/100</span>");
        if (baseline?.HealthScoreDelta is double healthDelta)
        {
            builder.AppendLine($"  <span class=\"delta {GetDeltaClass(healthDelta)}\">{FormatDeltaHtml(healthDelta, "pts")}</span>");
        }
        builder.AppendLine("</div>");
        builder.AppendLine("</header>");

        builder.AppendLine("<section class=\"summary\">");
        builder.AppendLine("<h2>Summary</h2>");
        builder.AppendLine("<ul>");
        var connection = report.Connection;
        if (connection is not null)
        {
            builder.AppendLine($"  <li><strong>Connection Success:</strong> {connection.SuccessRate:P1}");
            if (baseline?.ConnectionSuccessRateDelta is double successDelta)
            {
                builder.AppendLine($" <span class=\"delta {GetDeltaClass(successDelta)}\">{FormatDeltaHtml(successDelta * 100, "%")}</span>");
            }
            builder.AppendLine("</li>");
        }
        if (report.Connection?.AverageConnectionTime is { } connLatency)
        {
            builder.AppendLine($"  <li><strong>Connection Latency:</strong> {connLatency.TotalMilliseconds:N0} ms{FormatOptionalDeltaHtml(baseline?.ConnectionLatencyDelta)}</li>");
        }
        if (report.Network?.Average is { } networkLatency)
        {
            builder.AppendLine($"  <li><strong>Network Latency:</strong> {networkLatency.TotalMilliseconds:N0} ms{FormatOptionalDeltaHtml(baseline?.NetworkLatencyDelta)}</li>");
        }
        if (report.Server?.ResourceUsage.CpuUtilizationPercent is { } cpuUsage)
        {
            builder.AppendLine($"  <li><strong>Server CPU:</strong> {cpuUsage:N1}%{FormatOptionalDeltaHtml(baseline?.ServerCpuDelta, "%")}</li>");
        }
        builder.AppendLine("</ul>");
        if (baseline?.Notes.Count > 0)
        {
            builder.AppendLine("<h3>Baseline Alerts</h3>");
            builder.AppendLine("<ul class=\"note-list\">");
            foreach (var note in baseline.Notes)
            {
                builder.AppendLine($"  <li>{Escape(note)}</li>");
            }
            builder.AppendLine("</ul>");
        }
        builder.AppendLine("</section>");

        if (connection is not null || report.ConnectionPool is not null || report.ConnectionStability is { Samples.Count: > 0 })
        {
            builder.AppendLine("<section class=\"panel\">");
            builder.AppendLine("<h2>Connection</h2>");
            if (connection is not null)
            {
                builder.AppendLine("<table class=\"metric-table\"><tbody>");
                AppendTableRow(builder, "Attempts", connection.TotalAttempts.ToString("N0"));
                AppendTableRow(builder, "Success Rate", connection.SuccessRate.ToString("P1"));
                AppendTableRow(builder, "Average Time", FormatMilliseconds(connection.AverageConnectionTime));
                AppendTableRow(builder, "Minimum Time", FormatMilliseconds(connection.MinConnectionTime));
                AppendTableRow(builder, "Maximum Time", FormatMilliseconds(connection.MaxConnectionTime));
                builder.AppendLine("</tbody></table>");
            }
            if (report.ConnectionPool is { } pool)
            {
                builder.AppendLine("<h3>Connection Pool</h3>");
                builder.AppendLine("<table class=\"metric-table\"><tbody>");
                AppendTableRow(builder, "Pooling Enabled", FormatBoolean(pool.PoolingEnabled));
                AppendTableRow(builder, "Min Pool Size", pool.MinPoolSize?.ToString("N0") ?? "n/a");
                AppendTableRow(builder, "Max Pool Size", pool.MaxPoolSize?.ToString("N0") ?? "n/a");
                AppendTableRow(builder, "Pooled Connections", pool.PooledConnectionCount?.ToString("N0") ?? "n/a");
                AppendTableRow(builder, "Active Connections", pool.ActiveConnectionCount?.ToString("N0") ?? "n/a");
                AppendTableRow(builder, "Free Connections", pool.FreeConnectionCount?.ToString("N0") ?? "n/a");
                if (pool.ReclaimedConnectionCount.HasValue)
                {
                    AppendTableRow(builder, "Reclaimed Connections", pool.ReclaimedConnectionCount.Value.ToString("N0"));
                }
                builder.AppendLine("</tbody></table>");
                if (pool.Notes.Count > 0)
                {
                    builder.AppendLine("<ul class=\"note-list\">");
                    foreach (var note in pool.Notes)
                    {
                        builder.AppendLine($"  <li>{Escape(note)}</li>");
                    }
                    builder.AppendLine("</ul>");
                }
            }
            if (report.ConnectionStability is { Samples.Count: > 0 } stability)
            {
                builder.AppendLine("<h3>Connection Stability</h3>");
                builder.AppendLine("<table class=\"metric-table\"><tbody>");
                AppendTableRow(builder, "Observation Window", FormatDuration(stability.StartedAtUtc, stability.CompletedAtUtc));
                AppendTableRow(builder, "Samples", stability.Samples.Count.ToString("N0"));
                AppendTableRow(builder, "Success / Failure", $"{stability.SuccessCount:N0} / {stability.FailureCount:N0}");
                AppendTableRow(builder, "Success Rate", stability.SuccessRate.ToString("P1"));
                AppendTableRow(builder, "Average Latency", FormatMilliseconds(stability.AverageLatency));
                AppendTableRow(builder, "Latency Range", $"{FormatMilliseconds(stability.MinimumLatency)} – {FormatMilliseconds(stability.MaximumLatency)}");
                builder.AppendLine("</tbody></table>");
            }
            builder.AppendLine("</section>");
        }

        if (report.Network is not null || report.Dns is not null || report.PortConnectivity is not null || report.Bandwidth is not null)
        {
            builder.AppendLine("<section class=\"panel\">");
            builder.AppendLine("<h2>Network</h2>");
            if (report.Network is { } network)
            {
                builder.AppendLine("<table class=\"metric-table\"><tbody>");
                AppendTableRow(builder, "Average Latency", FormatMilliseconds(network.Average));
                AppendTableRow(builder, "Minimum Latency", FormatMilliseconds(network.Min));
                AppendTableRow(builder, "Maximum Latency", FormatMilliseconds(network.Max));
                AppendTableRow(builder, "Jitter", FormatMilliseconds(network.Jitter));
                builder.AppendLine("</tbody></table>");
            }
            if (report.Dns is { } dns)
            {
                builder.AppendLine("<h3>DNS Resolution</h3>");
                builder.AppendLine("<p>");
                builder.AppendLine($"<strong>Resolution Time:</strong> {FormatMilliseconds(dns.ResolutionTime)}");
                builder.AppendLine("</p>");
                if (dns.Addresses.Count > 0)
                {
                    builder.AppendLine("<ul class=\"note-list\">");
                    foreach (var address in dns.Addresses)
                    {
                        builder.AppendLine($"  <li>{Escape(address)}</li>");
                    }
                    builder.AppendLine("</ul>");
                }
            }
            if (report.PortConnectivity is { } port)
            {
                builder.AppendLine("<h3>TCP Port Probe</h3>");
                builder.AppendLine("<table class=\"metric-table\"><tbody>");
                AppendTableRow(builder, "Accessible", FormatBoolean(port.IsAccessible));
                AppendTableRow(builder, "Roundtrip Time", FormatMilliseconds(port.RoundtripTime));
                if (!string.IsNullOrWhiteSpace(port.FailureReason))
                {
                    AppendTableRow(builder, "Failure Reason", port.FailureReason ?? string.Empty);
                }
                builder.AppendLine("</tbody></table>");
            }
            if (report.Bandwidth is { } bandwidth)
            {
                builder.AppendLine("<h3>Bandwidth Estimate</h3>");
                builder.AppendLine("<table class=\"metric-table\"><tbody>");
                AppendTableRow(builder, "Duration", bandwidth.Duration.TotalSeconds.ToString("N1") + " s");
                AppendTableRow(builder, "Bytes Transferred", bandwidth.BytesTransferred.ToString("N0"));
                AppendTableRow(builder, "Iterations", bandwidth.IterationCount.ToString("N0"));
                AppendTableRow(builder, "Throughput", bandwidth.MegabytesPerSecond.HasValue ? $"{bandwidth.MegabytesPerSecond.Value:N2} MB/s" : "n/a");
                builder.AppendLine("</tbody></table>");
            }
            builder.AppendLine("</section>");
        }

        if (report.Query is not null || report.QueryPlan is not null || (report.Blocking?.HasBlocking ?? false) || (report.WaitStatistics?.Waits.Count > 0))
        {
            builder.AppendLine("<section class=\"panel\">");
            builder.AppendLine("<h2>Query Diagnostics</h2>");
            if (report.Query is { } query)
            {
                builder.AppendLine("<table class=\"metric-table\"><tbody>");
                AppendTableRow(builder, "Total Execution Time", FormatMilliseconds(query.TotalExecutionTime));
                AppendTableRow(builder, "Server Time", FormatMilliseconds(query.ServerTime));
                AppendTableRow(builder, "Network Time", FormatMilliseconds(query.NetworkTime));
                AppendTableRow(builder, "Rows Returned", query.RowsReturned.ToString("N0"));
                AppendTableRow(builder, "Bytes Sent / Received", $"{query.BytesSent:N0} / {query.BytesReceived:N0}");
                AppendTableRow(builder, "Server Round Trips", query.ServerRoundtrips.ToString("N0"));
                builder.AppendLine("</tbody></table>");
            }
            if (report.QueryPlan is { PlanXml: { Length: > 0 } planXml })
            {
                builder.AppendLine("<h3>Execution Plan</h3>");
                var truncatedPlan = Truncate(planXml, 4000);
                builder.AppendLine("<details><summary>View execution plan (XML)</summary>");
                builder.AppendLine($"<pre>{Escape(truncatedPlan)}</pre>");
                builder.AppendLine("</details>");
                if (report.QueryPlan.Warnings.Count > 0)
                {
                    builder.AppendLine("<ul class=\"note-list\">");
                    foreach (var warning in report.QueryPlan.Warnings)
                    {
                        builder.AppendLine($"  <li>{Escape(warning)}</li>");
                    }
                    builder.AppendLine("</ul>");
                }
            }
            if (report.Blocking is { HasBlocking: true } blocking)
            {
                builder.AppendLine("<h3>Blocking Sessions</h3>");
                builder.AppendLine("<table class=\"metric-table\"><thead><tr><th>Session</th><th>Blocked By</th><th>Wait Type</th><th>Wait (ms)</th><th>Status</th><th>Command</th></tr></thead><tbody>");
            foreach (var session in blocking.Sessions)
            {
                builder.Append("<tr><td>").Append(session.SessionId).Append("</td>");
                builder.Append("<td>").Append(session.BlockingSessionId).Append("</td>");
                builder.Append("<td>").Append(Escape(session.WaitType ?? "—")).Append("</td>");
                builder.Append("<td>").Append(session.WaitTime.TotalMilliseconds.ToString("N0")).Append("</td>");
                builder.Append("<td>").Append(Escape(session.Status ?? "—")).Append("</td>");
                builder.Append("<td>").Append(Escape(session.Command ?? "—")).Append("</td></tr>");
            }
                builder.AppendLine("</tbody></table>");
                if (blocking.Warnings.Count > 0)
                {
                    builder.AppendLine("<ul class=\"note-list\">");
                    foreach (var warning in blocking.Warnings)
                    {
                        builder.AppendLine($"  <li>{Escape(warning)}</li>");
                    }
                    builder.AppendLine("</ul>");
                }
            }
            if (report.WaitStatistics is { Waits.Count: > 0 } waitStats)
            {
                builder.AppendLine($"<h3>Wait Statistics ({Escape(waitStats.Scope.ToString())})</h3>");
                builder.AppendLine("<table class=\"metric-table\"><thead><tr><th>Wait Type</th><th>Wait (ms)</th><th>Signal (ms)</th><th>Tasks</th></tr></thead><tbody>");
                foreach (var wait in waitStats.Waits.Take(10))
                {
                    builder.Append("<tr><td>").Append(Escape(wait.WaitType)).Append("</td>");
                    builder.Append("<td>").Append(wait.WaitTimeMilliseconds.ToString("N0")).Append("</td>");
                    builder.Append("<td>").Append(wait.SignalWaitTimeMilliseconds.ToString("N0")).Append("</td>");
                    builder.Append("<td>").Append(wait.WaitingTasks.ToString("N0")).Append("</td></tr>");
                }
                builder.AppendLine("</tbody></table>");
            }
            builder.AppendLine("</section>");
        }

        if (report.Server is not null)
        {
            builder.AppendLine("<section class=\"panel\">");
            builder.AppendLine("<h2>Server</h2>");
            var usage = report.Server.ResourceUsage;
            builder.AppendLine("<table class=\"metric-table\"><tbody>");
            AppendTableRow(builder, "CPU Utilisation", usage.CpuUtilizationPercent?.ToString("N1") ?? "n/a");
            AppendTableRow(builder, "SQL CPU Utilisation", usage.SqlProcessUtilizationPercent?.ToString("N1") ?? "n/a");
            AppendTableRow(builder, "Memory Available", usage.AvailableMemoryMb.HasValue && usage.TotalMemoryMb.HasValue
                ? $"{usage.AvailableMemoryMb.Value:N0} MB of {usage.TotalMemoryMb.Value:N0} MB"
                : "n/a");
            AppendTableRow(builder, "Page Life Expectancy", usage.PageLifeExpectancySeconds?.ToString("N0") ?? "n/a");
            AppendTableRow(builder, "IO Stall (ms)", usage.IoStallMs?.ToString("N0") ?? "n/a");
            builder.AppendLine("</tbody></table>");

            if (report.Server.Waits.Count > 0)
            {
                builder.AppendLine("<h3>Top Waits</h3>");
                builder.AppendLine("<table class=\"metric-table\"><thead><tr><th>Wait Type</th><th>Wait (ms)</th><th>Signal (ms)</th><th>Tasks</th></tr></thead><tbody>");
                foreach (var wait in report.Server.Waits)
                {
                    builder.Append("<tr><td>").Append(Escape(wait.WaitType)).Append("</td>");
                    builder.Append("<td>").Append(wait.WaitTimeMs.ToString("N0")).Append("</td>");
                    builder.Append("<td>").Append(wait.SignalWaitTimeMs.ToString("N0")).Append("</td>");
                    builder.Append("<td>").Append(wait.WaitingTasksCount.ToString("N0")).Append("</td></tr>");
                }
                builder.AppendLine("</tbody></table>");
            }

            if (report.Server.PerformanceCounters.Count > 0)
            {
                builder.AppendLine("<h3>Performance Counters</h3>");
                builder.AppendLine("<table class=\"metric-table\"><thead><tr><th>Counter</th><th>Instance</th><th>Value</th></tr></thead><tbody>");
                foreach (var counter in report.Server.PerformanceCounters)
                {
                    builder.Append("<tr><td>").Append(Escape(counter.CounterName)).Append("</td>");
                    builder.Append("<td>").Append(Escape(counter.InstanceName ?? "—")).Append("</td>");
                    builder.Append("<td>").Append(double.IsNaN(counter.Value) ? "n/a" : counter.Value.ToString("N2")).Append("</td></tr>");
                }
                builder.AppendLine("</tbody></table>");
            }

            if (report.Server.Configuration.Count > 0)
            {
                builder.AppendLine("<h3>Configuration Drift</h3>");
                builder.AppendLine("<table class=\"metric-table\"><thead><tr><th>Setting</th><th>Configured</th><th>In Use</th><th>Advanced</th></tr></thead><tbody>");
                foreach (var setting in report.Server.Configuration.Take(15))
                {
                    builder.Append("<tr><td>").Append(Escape(setting.Name)).Append("</td>");
                    builder.Append("<td>").Append(setting.Value?.ToString("N2") ?? "n/a").Append("</td>");
                    builder.Append("<td>").Append(setting.ValueInUse?.ToString("N2") ?? "n/a").Append("</td>");
                    builder.Append("<td>").Append(FormatBoolean(setting.IsAdvanced)).Append("</td></tr>");
                }
                builder.AppendLine("</tbody></table>");
            }
            builder.AppendLine("</section>");
        }

        if (baseline is not null)
        {
            builder.AppendLine("<section class=\"panel\">");
            builder.AppendLine("<h2>Baseline Comparison</h2>");
            builder.AppendLine("<table class=\"metric-table\"><tbody>");
            AppendTableRow(builder, "Health Score Δ", baseline.HealthScoreDelta.HasValue ? FormatDeltaString(baseline.HealthScoreDelta.Value, "pts") : "n/a");
            AppendTableRow(builder, "Connection Success Rate Δ", baseline.ConnectionSuccessRateDelta.HasValue ? FormatDeltaString(baseline.ConnectionSuccessRateDelta.Value * 100, "%") : "n/a");
            AppendTableRow(builder, "Connection Latency Δ", FormatOptionalDelta(baseline.ConnectionLatencyDelta));
            AppendTableRow(builder, "Network Latency Δ", FormatOptionalDelta(baseline.NetworkLatencyDelta));
            AppendTableRow(builder, "Server CPU Δ", baseline.ServerCpuDelta.HasValue ? FormatDeltaString(baseline.ServerCpuDelta.Value, "%") : "n/a");
            builder.AppendLine("</tbody></table>");
            if (baseline.Notes.Count > 0)
            {
                builder.AppendLine("<ul class=\"note-list\">");
                foreach (var note in baseline.Notes)
                {
                    builder.AppendLine($"  <li>{Escape(note)}</li>");
                }
                builder.AppendLine("</ul>");
            }
            builder.AppendLine("</section>");
        }

        if (report.Recommendations.Count > 0)
        {
            builder.AppendLine("<section class=\"panel\">");
            builder.AppendLine("<h2>Recommendations</h2>");
            builder.AppendLine("<ul class=\"note-list\">");
            foreach (var recommendation in report.Recommendations)
            {
                builder.Append("<li><span class=\"badge\">");
                builder.Append(Escape(recommendation.Severity.ToString()));
                builder.Append("</span> <strong>");
                builder.Append(Escape(recommendation.Category));
                builder.Append(":</strong> ");
                builder.Append(Escape(recommendation.Issue));
                builder.Append(" – ");
                builder.Append(Escape(recommendation.RecommendationText));
                if (!string.IsNullOrWhiteSpace(recommendation.ReferenceLink))
                {
                    builder.Append(" (<a href=\"");
                    builder.Append(Escape(recommendation.ReferenceLink));
                    builder.Append("\">more info</a>)");
                }
                builder.AppendLine("</li>");
            }
            builder.AppendLine("</ul>");
            builder.AppendLine("</section>");
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    internal static string BuildMarkdown(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        var healthScore = report.GetHealthScore();
        var baseline = report.BaselineComparison;

        builder.AppendLine("# SQL Diagnostics Report");
        builder.AppendLine();
        builder.AppendLine($"- **Generated:** {report.GeneratedAtUtc:O}");
        builder.AppendLine($"- **Target:** {report.TargetDataSource ?? "Unknown"}");
        builder.AppendLine($"- **Health Score:** {healthScore}/100{FormatDeltaMarkdown(baseline?.HealthScoreDelta, " pts")}");
        builder.AppendLine();

        if (baseline?.Notes.Count > 0)
        {
            builder.AppendLine("### Baseline Alerts");
            foreach (var note in baseline.Notes)
            {
                builder.AppendLine($"- {note}");
            }
            builder.AppendLine();
        }

        if (report.Connection is { } connection)
        {
            builder.AppendLine("## Connection");
            builder.AppendLine($"- Attempts: {connection.TotalAttempts}");
            builder.AppendLine($"- Success Rate: {connection.SuccessRate:P1}{FormatDeltaMarkdown(baseline?.ConnectionSuccessRateDelta, "%", asPercent:true)}");
            builder.AppendLine($"- Average Time: {FormatMilliseconds(connection.AverageConnectionTime)}{FormatDeltaMarkdown(baseline?.ConnectionLatencyDelta)}");
            builder.AppendLine($"- Min / Max Time: {FormatMilliseconds(connection.MinConnectionTime)} / {FormatMilliseconds(connection.MaxConnectionTime)}");
            builder.AppendLine();
        }

        if (report.ConnectionPool is { } pool)
        {
            builder.AppendLine("### Connection Pool");
            builder.AppendLine($"- Pooling Enabled: {FormatBoolean(pool.PoolingEnabled)}");
            builder.AppendLine($"- Pool Size: {pool.MinPoolSize?.ToString("N0") ?? "n/a"} – {pool.MaxPoolSize?.ToString("N0") ?? "n/a"}");
            builder.AppendLine($"- Active / Free: {pool.ActiveConnectionCount?.ToString("N0") ?? "n/a"} / {pool.FreeConnectionCount?.ToString("N0") ?? "n/a"}");
            if (pool.ReclaimedConnectionCount.HasValue)
            {
                builder.AppendLine($"- Reclaimed Connections: {pool.ReclaimedConnectionCount.Value:N0}");
            }
            foreach (var note in pool.Notes)
            {
                builder.AppendLine($"  - {note}");
            }
            builder.AppendLine();
        }

        if (report.ConnectionStability is { Samples.Count: > 0 } stability)
        {
            builder.AppendLine("### Connection Stability");
            builder.AppendLine($"- Observations: {stability.Samples.Count} (Success {stability.SuccessCount} / Failure {stability.FailureCount})");
            builder.AppendLine($"- Success Rate: {stability.SuccessRate:P1}");
            builder.AppendLine($"- Average Latency: {FormatMilliseconds(stability.AverageLatency)}");
            builder.AppendLine($"- Latency Range: {FormatMilliseconds(stability.MinimumLatency)} – {FormatMilliseconds(stability.MaximumLatency)}");
            builder.AppendLine();
        }

        if (report.Network is not null || report.Dns is not null || report.PortConnectivity is not null || report.Bandwidth is not null)
        {
            builder.AppendLine("## Network");
            if (report.Network is { } network)
            {
                builder.AppendLine($"- Average Latency: {FormatMilliseconds(network.Average)}{FormatDeltaMarkdown(baseline?.NetworkLatencyDelta)}");
                builder.AppendLine($"- Min / Max Latency: {FormatMilliseconds(network.Min)} / {FormatMilliseconds(network.Max)}");
                builder.AppendLine($"- Jitter: {FormatMilliseconds(network.Jitter)}");
            }
            if (report.Dns is { } dns)
            {
                builder.AppendLine($"- DNS Resolution: {FormatMilliseconds(dns.ResolutionTime)}");
                if (dns.Addresses.Count > 0)
                {
                    builder.AppendLine("  - Addresses:");
                    foreach (var address in dns.Addresses)
                    {
                        builder.AppendLine($"    - {address}");
                    }
                }
            }
            if (report.PortConnectivity is { } port)
            {
                builder.AppendLine($"- Port Connectivity: {(port.IsAccessible ? "Accessible" : "Blocked")}, RTT {FormatMilliseconds(port.RoundtripTime)}");
                if (!string.IsNullOrWhiteSpace(port.FailureReason))
                {
                    builder.AppendLine($"  - Failure Reason: {port.FailureReason}");
                }
            }
            if (report.Bandwidth is { } bandwidth)
            {
                builder.AppendLine($"- Bandwidth: {(bandwidth.MegabytesPerSecond.HasValue ? bandwidth.MegabytesPerSecond.Value.ToString("N2") + " MB/s" : "n/a")}, Bytes {bandwidth.BytesTransferred:N0}, Iterations {bandwidth.IterationCount}");
            }
            builder.AppendLine();
        }

        if (report.Query is not null || report.QueryPlan is not null || (report.Blocking?.HasBlocking ?? false) || (report.WaitStatistics?.Waits.Count > 0))
        {
            builder.AppendLine("## Query Diagnostics");
            if (report.Query is { } query)
            {
                builder.AppendLine($"- Total Execution Time: {FormatMilliseconds(query.TotalExecutionTime)}");
                builder.AppendLine($"- Server / Network Time: {FormatMilliseconds(query.ServerTime)} / {FormatMilliseconds(query.NetworkTime)}");
                builder.AppendLine($"- Rows Returned: {query.RowsReturned:N0}");
                builder.AppendLine($"- Bytes Sent / Received: {query.BytesSent:N0} / {query.BytesReceived:N0}");
                builder.AppendLine($"- Server Round Trips: {query.ServerRoundtrips:N0}");
            }
            if (report.QueryPlan is { PlanXml: { Length: > 0 } planXml })
            {
                builder.AppendLine("- Execution Plan (XML excerpt):");
                builder.AppendLine("```xml");
                builder.AppendLine(Truncate(planXml, 2000));
                builder.AppendLine("```");
            }
            if (report.Blocking is { HasBlocking: true } blocking)
            {
                builder.AppendLine("- Blocking Sessions:");
                foreach (var session in blocking.Sessions)
                {
                    builder.AppendLine($"  - SPID {session.SessionId} blocked by {session.BlockingSessionId} ({session.WaitType ?? "?"}, {session.WaitTime.TotalMilliseconds:N0} ms)");
                }
            }
            if (report.WaitStatistics is { Waits.Count: > 0 } waitStats)
            {
                builder.AppendLine($"- Wait Statistics ({waitStats.Scope}):");
                foreach (var wait in waitStats.Waits.Take(10))
                {
                    builder.AppendLine($"  - {wait.WaitType}: Wait {wait.WaitTimeMilliseconds:N0} ms, Signal {wait.SignalWaitTimeMilliseconds:N0} ms, Tasks {wait.WaitingTasks:N0}");
                }
            }
            builder.AppendLine();
        }

        if (report.Server is not null)
        {
            builder.AppendLine("## Server");
            var usage = report.Server.ResourceUsage;
            builder.AppendLine($"- CPU Utilisation: {usage.CpuUtilizationPercent?.ToString("N1") ?? "n/a"}%");
            builder.AppendLine($"- SQL CPU Utilisation: {usage.SqlProcessUtilizationPercent?.ToString("N1") ?? "n/a"}%");
            builder.AppendLine($"- Memory Available: {(usage.AvailableMemoryMb.HasValue && usage.TotalMemoryMb.HasValue ? $"{usage.AvailableMemoryMb.Value:N0} MB / {usage.TotalMemoryMb.Value:N0} MB" : "n/a")}");
            builder.AppendLine($"- Page Life Expectancy: {usage.PageLifeExpectancySeconds?.ToString("N0") ?? "n/a"} s");
            builder.AppendLine($"- IO Stall: {usage.IoStallMs?.ToString("N0") ?? "n/a"} ms");
            if (report.Server.Waits.Count > 0)
            {
                builder.AppendLine("- Waits:");
                foreach (var wait in report.Server.Waits.Take(10))
                {
                    builder.AppendLine($"  - {wait.WaitType}: {wait.WaitTimeMs:N0} ms (signal {wait.SignalWaitTimeMs:N0} ms, tasks {wait.WaitingTasksCount:N0})");
                }
            }
            if (report.Server.PerformanceCounters.Count > 0)
            {
                builder.AppendLine("- Performance Counters:");
                foreach (var counter in report.Server.PerformanceCounters)
                {
                    builder.AppendLine($"  - {counter.CounterName}{(string.IsNullOrWhiteSpace(counter.InstanceName) ? string.Empty : $" ({counter.InstanceName})")}: {counter.Value:N2}");
                }
            }
            if (report.Server.Configuration.Count > 0)
            {
                builder.AppendLine("- Configuration Drift:");
                foreach (var setting in report.Server.Configuration.Take(10))
                {
                    builder.AppendLine($"  - {setting.Name}: configured {setting.Value?.ToString("N2") ?? "n/a"}, in use {setting.ValueInUse?.ToString("N2") ?? "n/a"}");
                }
            }
            builder.AppendLine();
        }

        if (baseline is not null)
        {
            builder.AppendLine("## Baseline Comparison");
            builder.AppendLine($"- Health Score Δ: {baseline.HealthScoreDelta?.ToString("+0.0;-0.0;0") ?? "n/a"} pts");
            builder.AppendLine($"- Connection Success Rate Δ: {baseline.ConnectionSuccessRateDelta?.ToString("+0.0%;-0.0%;0.0%") ?? "n/a"}");
            builder.AppendLine($"- Connection Latency Δ: {FormatDeltaMarkdown(baseline.ConnectionLatencyDelta)}");
            builder.AppendLine($"- Network Latency Δ: {FormatDeltaMarkdown(baseline.NetworkLatencyDelta)}");
            builder.AppendLine($"- Server CPU Δ: {baseline.ServerCpuDelta?.ToString("+0.0;-0.0;0.0") ?? "n/a"}%");
            if (baseline.Notes.Count > 0)
            {
                foreach (var note in baseline.Notes)
                {
                    builder.AppendLine($"  - {note}");
                }
            }
            builder.AppendLine();
        }

        if (report.Recommendations.Count > 0)
        {
            builder.AppendLine("## Recommendations");
            foreach (var recommendation in report.Recommendations)
            {
                builder.AppendLine($"- **[{recommendation.Severity}] {recommendation.Category}:** {recommendation.Issue} – {recommendation.RecommendationText}");
                if (!string.IsNullOrWhiteSpace(recommendation.ReferenceLink))
                {
                    builder.AppendLine($"  - More info: {recommendation.ReferenceLink}");
                }
            }
        }

        return builder.ToString();
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void AppendTableRow(StringBuilder builder, string header, string value)
    {
        builder.Append("<tr><th>");
        builder.Append(Escape(header));
        builder.Append("</th><td>");
        builder.Append(Escape(string.IsNullOrWhiteSpace(value) ? "—" : value));
        builder.AppendLine("</td></tr>");
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static string FormatMilliseconds(TimeSpan value) => $"{value.TotalMilliseconds:N0} ms";

    private static string FormatMilliseconds(TimeSpan? value) =>
        value.HasValue ? FormatMilliseconds(value.Value) : "n/a";

    private static string FormatBoolean(bool value) => value ? "Yes" : "No";

    private static string FormatDuration(DateTime start, DateTime end)
    {
        var duration = end - start;
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return $"{start:HH:mm:ss} → {end:HH:mm:ss} ({duration.TotalSeconds:N1} s)";
    }

    private static string FormatOptionalDeltaHtml(TimeSpan? delta) =>
        delta.HasValue && Math.Abs(delta.Value.TotalMilliseconds) > 0.5
            ? $" <span class=\"delta {GetDeltaClass(delta.Value.TotalMilliseconds)}\">{FormatDeltaString(delta.Value.TotalMilliseconds, " ms", "N0")}</span>"
            : string.Empty;

    private static string FormatOptionalDeltaHtml(double? delta, string suffix = "") =>
        delta.HasValue && Math.Abs(delta.Value) > 0.0001
            ? $" <span class=\"delta {GetDeltaClass(delta.Value)}\">{FormatDeltaString(delta.Value, suffix)}</span>"
            : string.Empty;

    private static string FormatOptionalDelta(TimeSpan? delta) =>
        delta.HasValue ? FormatDeltaString(delta.Value.TotalMilliseconds, " ms", "N0") : "n/a";

    private static string FormatDeltaString(double delta, string suffix, string format = "N1")
    {
        var sign = delta >= 0 ? "+" : "-";
        return $"{sign}{Math.Abs(delta).ToString(format)}{suffix}";
    }

    private static string FormatDeltaHtml(double delta, string suffix) =>
        FormatDeltaString(delta, suffix);

    private static string FormatDeltaMarkdown(TimeSpan? delta)
    {
        if (!delta.HasValue || Math.Abs(delta.Value.TotalMilliseconds) < 0.5)
        {
            return string.Empty;
        }

        return $" (Δ {FormatDeltaString(delta.Value.TotalMilliseconds, " ms", "N0")})";
    }

    private static string FormatDeltaMarkdown(double? delta, string suffix = "", bool asPercent = false)
    {
        if (!delta.HasValue)
        {
            return string.Empty;
        }

        var value = asPercent ? delta.Value * 100 : delta.Value;
        if (Math.Abs(value) < 0.0001)
        {
            return string.Empty;
        }

        var format = asPercent ? "N1" : "N1";
        return $" (Δ {FormatDeltaString(value, suffix, format)})";
    }

    private static string GetDeltaClass(double delta) => delta >= 0 ? "positive" : "negative";

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength) + "...";
    }

    private static async Task WriteTextAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if NETSTANDARD2_0
        using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content).ConfigureAwait(false);
#else
        await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
#endif
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
