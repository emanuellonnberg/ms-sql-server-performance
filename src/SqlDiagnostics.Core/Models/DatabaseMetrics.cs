using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Models;

/// <summary>
/// Represents a snapshot of database-level health information across the SQL Server instance.
/// </summary>
public sealed class DatabaseMetrics
{
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;

    public IList<DatabaseSnapshot> Databases { get; } = new List<DatabaseSnapshot>();

    public IDictionary<string, object> Metadata { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Captures key state and usage indicators for a single database.
/// </summary>
public sealed class DatabaseSnapshot
{
    public int DatabaseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? RecoveryModel { get; set; }
    public int? CompatibilityLevel { get; set; }
    public string? Containment { get; set; }
    public string? LogReuseWait { get; set; }
    public bool? IsReadOnly { get; set; }
    public bool? IsAutoClose { get; set; }
    public bool? IsAutoShrink { get; set; }
    public double? DataFileSizeMb { get; set; }
    public double? LogFileSizeMb { get; set; }
    public double? TotalLogSizeMb { get; set; }
    public double? ActiveLogSizeMb { get; set; }
    public double? LogUsedPercent { get; set; }
    public double? LogSinceLastBackupMb { get; set; }
    public DateTime? LastLogBackupTime { get; set; }
    public DateTime? LastCheckpointTime { get; set; }
    public string? LogTruncationHoldupReason { get; set; }
    public long? ActiveSessionCount { get; set; }
    public long? RunningRequestCount { get; set; }
    public double? AggregateWaitTimeMs { get; set; }

    public IDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
}
