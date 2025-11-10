using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Represents analysed metadata for a query execution plan.
/// </summary>
public sealed class QueryPlanAnalysis
{
    public string? PlanXml { get; set; }

    public TimeSpan? CollectionTime { get; set; }

    public IList<string> Warnings { get; } = new List<string>();
}

/// <summary>
/// Snapshot describing sessions participating in blocking chains.
/// </summary>
public sealed class BlockingReport
{
    public IList<BlockingSession> Sessions { get; } = new List<BlockingSession>();

    public bool HasBlocking => Sessions.Count > 0;

    public IList<string> Warnings { get; } = new List<string>();
}

public sealed class BlockingSession
{
    public int SessionId { get; set; }

    public int BlockingSessionId { get; set; }

    public string? WaitType { get; set; }

    public TimeSpan WaitTime { get; set; }

    public string? Status { get; set; }

    public string? Command { get; set; }

    public string? SqlText { get; set; }
}

/// <summary>
/// Aggregated wait statistics scoped to the current session or server.
/// </summary>
public sealed class WaitStatistics
{
    public WaitStatsScope Scope { get; set; }

    public IList<WaitStatisticEntry> Waits { get; } = new List<WaitStatisticEntry>();
}

public sealed class WaitStatisticEntry
{
    public string WaitType { get; set; } = string.Empty;

    public long WaitTimeMilliseconds { get; set; }

    public long SignalWaitTimeMilliseconds { get; set; }

    public long WaitingTasks { get; set; }
}

public enum WaitStatsScope
{
    Session,
    Server
}
