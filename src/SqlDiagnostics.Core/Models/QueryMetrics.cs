using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Represents execution statistics captured for a query.
/// </summary>
public sealed class QueryMetrics
{
    public TimeSpan TotalExecutionTime { get; set; }
    public TimeSpan? NetworkTime { get; set; }
    public TimeSpan? ServerTime { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public long RowsReturned { get; set; }
    public int ServerRoundtrips { get; set; }
    public IReadOnlyDictionary<string, object> RawStatistics => _rawStatistics;

    private readonly Dictionary<string, object> _rawStatistics = new(StringComparer.OrdinalIgnoreCase);

    public void AddStatistic(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Statistic key must be provided.", nameof(key));
        }

        _rawStatistics[key] = value;
    }
}
