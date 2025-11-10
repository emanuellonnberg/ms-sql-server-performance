using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Reports;

/// <summary>
/// Defines a contract for formatting diagnostic reports.
/// </summary>
public interface IReportGenerator
{
    /// <summary>
    /// Generates a formatted representation of the report.
    /// </summary>
    Task<string> GenerateAsync(DiagnosticReport report, CancellationToken cancellationToken = default);
}
