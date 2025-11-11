using System;

namespace SqlDiagnostics.Core.Logging;

/// <summary>
/// Configures behaviour of <see cref="DiagnosticLogger"/>.
/// </summary>
public sealed class DiagnosticLoggerOptions
{
    /// <summary>
    /// Directory where log files will be written.
    /// </summary>
    public string? LogDirectory { get; set; }

    /// <summary>
    /// Maximum number of events that can be buffered before new events are dropped.
    /// </summary>
    public int MaxQueueSize { get; set; } = 8_192;

    /// <summary>
    /// Interval between background flush cycles.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets a value indicating whether events should also be written to the console.
    /// </summary>
    public bool EnableConsoleSink { get; set; } = false;

    /// <summary>
    /// Maximum log file size before a roll occurs (in megabytes).
    /// </summary>
    public int RollingFileSizeInMegabytes { get; set; } = 5;

    /// <summary>
    /// Maximum number of rolled files to keep.
    /// </summary>
    public int RollingFileCount { get; set; } = 5;
}
