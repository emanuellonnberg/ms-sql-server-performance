using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Interfaces;

/// <summary>
/// Contract for a diagnostics component that produces a metric payload from an opened connection.
/// </summary>
/// <typeparam name="T">Metric type.</typeparam>
public interface IMetricCollector<T>
{
    Task<T> CollectAsync(SqlConnection connection, CancellationToken cancellationToken = default);

    bool IsSupported(SqlConnection connection);
}
