using System;
using SqlDiagnostics.Core.Reports;

namespace SqlDiagnostics.Core.Monitoring;

/// <summary>
/// Represents a timestamped diagnostic report produced by a monitoring session.
/// </summary>
public sealed class DiagnosticSnapshot
{
    public DiagnosticSnapshot(DateTime timestamp, DiagnosticReport report)
    {
        Timestamp = timestamp;
        Report = report ?? throw new ArgumentNullException(nameof(report));
    }

    /// <summary>
    /// Gets the point in time when the snapshot was captured.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets the diagnostic report associated with the snapshot.
    /// </summary>
    public DiagnosticReport Report { get; }
}
