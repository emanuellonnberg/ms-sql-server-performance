using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Models;

/// <summary>
/// Aggregates dynamic management view information from the SQL Server instance.
/// </summary>
public sealed class ServerMetrics
{
    public ServerResourceUsage ResourceUsage { get; set; } = new();
    public IReadOnlyDictionary<string, object> AdditionalProperties => _additionalProperties;

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
