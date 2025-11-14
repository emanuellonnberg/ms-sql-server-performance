using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Represents a lightweight snapshot of SQL Server instance state suitable for quick health checks.
/// </summary>
public sealed class ServerStateSnapshot
{
    public string? MachineName { get; set; }
    public string? ServerName { get; set; }
    public string? Edition { get; set; }
    public string? ProductVersion { get; set; }
    public string? ProductLevel { get; set; }
    public int? EngineEdition { get; set; }
    public bool? IsClustered { get; set; }
    public bool? IsHadrEnabled { get; set; }
    public string? DefaultDataPath { get; set; }
    public string? DefaultLogPath { get; set; }
    public DateTime? SqlServerStartTimeUtc { get; set; }
    public bool PendingRestart { get; set; }

    public IList<ServerServiceState> Services { get; } = new List<ServerServiceState>();
    public IList<ServerDatabaseState> Databases { get; } = new List<ServerDatabaseState>();
    public IDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ServerDatabaseState> OfflineDatabases => Databases.Where(db => !db.IsOnline);
}

/// <summary>
/// Describes the operating system service state reported by sys.dm_server_services.
/// </summary>
public sealed class ServerServiceState
{
    public string ServiceName { get; set; } = string.Empty;
    public string? StartupType { get; set; }
    public string? Status { get; set; }
    public DateTime? LastStartTimeUtc { get; set; }
    public string? Filename { get; set; }
    public string? ServiceAccount { get; set; }
}

/// <summary>
/// Captures high-level database state with recovery information for quick assessment.
/// </summary>
public sealed class ServerDatabaseState
{
    public int DatabaseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? RecoveryModel { get; set; }
    public string? UserAccess { get; set; }
    public bool? IsReadOnly { get; set; }
    public bool? IsInStandby { get; set; }
    public bool? IsAutoCloseOn { get; set; }
    public bool? IsAutoShrinkOn { get; set; }
    public bool? IsMirroringEnabled { get; set; }

    public bool IsOnline => string.Equals(State, "ONLINE", StringComparison.OrdinalIgnoreCase);
}
