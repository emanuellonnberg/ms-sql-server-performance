using System;
using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Reports;

public sealed class JsonReportGenerator : IReportGenerator
{
    public Task<string> GenerateAsync(DiagnosticReport report, CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return Task.FromResult(DiagnosticReportExtensions.BuildJson(report));
    }
}

public sealed class HtmlReportGenerator : IReportGenerator
{
    public Task<string> GenerateAsync(DiagnosticReport report, CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return Task.FromResult(DiagnosticReportExtensions.BuildHtml(report));
    }
}

public sealed class MarkdownReportGenerator : IReportGenerator
{
    public Task<string> GenerateAsync(DiagnosticReport report, CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return Task.FromResult(DiagnosticReportExtensions.BuildMarkdown(report));
    }
}

public sealed class ReportGeneratorFactory
{
    public IReportGenerator Create(ReportFormat format) =>
        format switch
        {
            ReportFormat.Json => new JsonReportGenerator(),
            ReportFormat.Html => new HtmlReportGenerator(),
            ReportFormat.Markdown => new MarkdownReportGenerator(),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported report format.")
        };
}
