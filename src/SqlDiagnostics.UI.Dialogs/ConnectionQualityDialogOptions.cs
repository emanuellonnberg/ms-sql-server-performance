using System;
using SqlDiagnostics.Core.Monitoring;

namespace SqlDiagnostics.UI.Dialogs;

/// <summary>
/// Configures how the connection quality dialog attaches to diagnostics sources.
/// </summary>
public sealed class ConnectionQualityDialogOptions
{
    /// <summary>
    /// Gets or sets the connection string that should be monitored.
    /// Provide either this or <see cref="Monitor"/>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets an existing <see cref="DiagnosticMonitor"/> instance to observe.
    /// When provided, the dialog will not automatically start or stop the monitor.
    /// </summary>
    public DiagnosticMonitor? Monitor { get; set; }

    /// <summary>
    /// Gets or sets the interval between diagnostic snapshots when the dialog
    /// is responsible for running the monitor. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (Monitor is null && string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException("Either an existing monitor or a connection string must be provided.");
        }

        if (MonitoringInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MonitoringInterval), "Monitoring interval must be positive.");
        }
    }
}
