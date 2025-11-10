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
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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
        await File.WriteAllTextAsync(filePath, payload, cancellationToken).ConfigureAwait(false);
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
        await File.WriteAllTextAsync(filePath, builder, cancellationToken).ConfigureAwait(false);
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
            var successRate = Math.Clamp(report.Connection.SuccessRate, 0, 1);
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

        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }

    internal static string BuildJson(DiagnosticReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    internal static string BuildHtml(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"UTF-8\" />");
        builder.AppendLine("<title>SQL Diagnostics Report</title>");
        builder.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:2rem;}h1{font-size:1.5rem;}table{border-collapse:collapse;width:100%;margin-bottom:1rem;}th,td{border:1px solid #ccc;padding:0.5rem;text-align:left;}th{background:#f5f5f5;}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine("<h1>SQL Diagnostics Report</h1>");
        builder.AppendLine($"<p><strong>Generated:</strong> {report.GeneratedAtUtc:O}<br/>");
        builder.AppendLine($"<strong>Target:</strong> {Escape(report.TargetDataSource ?? "Unknown")}</p>");

        if (report.Connection is not null)
        {
            builder.AppendLine("<h2>Connection Metrics</h2>");
            builder.AppendLine("<table><tbody>");
            builder.AppendLine(Row("Attempts", report.Connection.TotalAttempts.ToString()));
            builder.AppendLine(Row("Success Rate", $"{report.Connection.SuccessRate:P1}"));
            if (report.Connection.AverageConnectionTime is { } avg)
            {
                builder.AppendLine(Row("Average Time (ms)", avg.TotalMilliseconds.ToString("N0")));
                builder.AppendLine(Row("Minimum Time (ms)", report.Connection.MinConnectionTime?.TotalMilliseconds.ToString("N0") ?? "—"));
                builder.AppendLine(Row("Maximum Time (ms)", report.Connection.MaxConnectionTime?.TotalMilliseconds.ToString("N0") ?? "—"));
            }
            builder.AppendLine("</tbody></table>");
        }

        if (report.Network is not null)
        {
            builder.AppendLine("<h2>Network Metrics</h2>");
            builder.AppendLine("<table><tbody>");
            builder.AppendLine(Row("Average RTT (ms)", report.Network.Average?.TotalMilliseconds.ToString("N0") ?? "—"));
            builder.AppendLine(Row("Jitter (ms)", report.Network.Jitter?.TotalMilliseconds.ToString("N0") ?? "—"));
            builder.AppendLine("</tbody></table>");
        }

        if (report.Recommendations.Count > 0)
        {
            builder.AppendLine("<h2>Recommendations</h2><ul>");
            foreach (var recommendation in report.Recommendations)
            {
                builder.AppendLine($"<li><strong>[{recommendation.Severity}] {Escape(recommendation.Category)}</strong>: {Escape(recommendation.Issue)} – {Escape(recommendation.RecommendationText)}</li>");
            }
            builder.AppendLine("</ul>");
        }

        builder.AppendLine("</body></html>");

        return builder.ToString();
    }

    internal static string BuildMarkdown(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SQL Diagnostics Report");
        builder.AppendLine();
        builder.AppendLine($"- **Generated:** {report.GeneratedAtUtc:O}");
        builder.AppendLine($"- **Target:** {report.TargetDataSource ?? "Unknown"}");
        builder.AppendLine();

        if (report.Connection is not null)
        {
            builder.AppendLine("## Connection Metrics");
            builder.AppendLine();
            builder.AppendLine($"- Attempts: {report.Connection.TotalAttempts}");
            builder.AppendLine($"- Success Rate: {report.Connection.SuccessRate:P1}");
            if (report.Connection.AverageConnectionTime is { } avg)
            {
                builder.AppendLine($"- Average Time: {avg.TotalMilliseconds:N0} ms");
                builder.AppendLine($"- Minimum Time: {report.Connection.MinConnectionTime?.TotalMilliseconds:N0} ms");
                builder.AppendLine($"- Maximum Time: {report.Connection.MaxConnectionTime?.TotalMilliseconds:N0} ms");
            }
            builder.AppendLine();
        }

        if (report.Network is not null)
        {
            builder.AppendLine("## Network Metrics");
            builder.AppendLine();
            builder.AppendLine($"- Average RTT: {report.Network.Average?.TotalMilliseconds:N0} ms");
            builder.AppendLine($"- Jitter: {report.Network.Jitter?.TotalMilliseconds:N0} ms");
            builder.AppendLine();
        }

        if (report.Recommendations.Count > 0)
        {
            builder.AppendLine("## Recommendations");
            builder.AppendLine();
            foreach (var recommendation in report.Recommendations)
            {
                builder.AppendLine($"- **[{recommendation.Severity}] {recommendation.Category}:** {recommendation.Issue} – {recommendation.RecommendationText}");
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

    private static string Row(string header, string? value) =>
        $"<tr><th>{Escape(header)}</th><td>{Escape(value ?? "—")}</td></tr>";

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);
}
