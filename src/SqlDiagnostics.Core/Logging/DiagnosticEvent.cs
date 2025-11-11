using System;
using System.Text.Json.Serialization;

namespace SqlDiagnostics.Core.Logging;

/// <summary>
/// Represents a structured diagnostics event emitted by the toolkit.
/// </summary>
public sealed class DiagnosticEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public DiagnosticEventType EventType { get; set; } = DiagnosticEventType.General;
    public EventSeverity Severity { get; set; } = EventSeverity.Info;
    public string? Message { get; set; }
    public string? Source { get; set; }
    public object? Data { get; set; }

    public string MachineName { get; set; } = Environment.MachineName;
    public string ProcessName { get; set; } = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
}

/// <summary>
/// High-level categories for diagnostics events.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiagnosticEventType
{
    General,
    Connection,
    Network,
    Query,
    Server,
    Dataset,
    Pool,
    Baseline,
    Package,
    Error
}

/// <summary>
/// Indicates the severity of a diagnostics event.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventSeverity
{
    Info,
    Warning,
    Error,
    Critical
}
