using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Logging;

/// <summary>
/// Represents a destination that can persist batches of diagnostic events.
/// </summary>
public interface IDiagnosticSink
{
    Task WriteAsync(IReadOnlyCollection<DiagnosticEvent> events, CancellationToken cancellationToken);
}
