using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Aggregates dynamic management view information from the SQL Server instance.
/// </summary>
public sealed class ServerMetrics
{
    public ServerResourceUsage ResourceUsage { get; set; } = new();
    public IReadOnlyDictionary<string, object> AdditionalProperties => _additionalProperties;
    public IList<WaitStatistic> Waits { get; } = new List<WaitStatistic>();
    public IList<PerformanceCounterMetric> PerformanceCounters { get; } = new List<PerformanceCounterMetric>();

    private readonly Dictionary<string, object> _additionalProperties = new(StringComparer.OrdinalIgnoreCase);

    public void AddProperty(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Property key must be provided.", nameof(key));
        }

        _additionalProperties[key] = value;
    }
}

/// <summary>
/// Represents CPU, memory and IO usage snapshots.
/// </summary>
public sealed class ServerResourceUsage
{
    public double? CpuUtilizationPercent { get; set; }
    public double? SqlProcessUtilizationPercent { get; set; }
    public double? AvailableMemoryMb { get; set; }
    public double? TotalMemoryMb { get; set; }
    public double? PageLifeExpectancySeconds { get; set; }
    public long? IoStallMs { get; set; }
}

/// <summary>
/// Represents a wait type aggregated from sys.dm_os_wait_stats.
/// </summary>
public sealed class WaitStatistic
{
    public string WaitType { get; set; } = string.Empty;
    public long WaitTimeMs { get; set; }
    public long SignalWaitTimeMs { get; set; }
    public long WaitingTasksCount { get; set; }
}

/// <summary>
/// Represents a selected SQL Server performance counter.
/// </summary>
public sealed class PerformanceCounterMetric
{
    public string CounterName { get; set; } = string.Empty;
    public string? InstanceName { get; set; }
    public double Value { get; set; }
    public string? ObjectName { get; set; }
    public int CounterType { get; set; }
}
