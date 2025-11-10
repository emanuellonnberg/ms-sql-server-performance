using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Represents aggregated information about the SQL client connection pool.
/// </summary>
public sealed class ConnectionPoolMetrics
{
    public bool PoolingEnabled { get; set; }

    public int? MinPoolSize { get; set; }

    public int? MaxPoolSize { get; set; }

    public int? PooledConnectionCount { get; set; }

    public int? ActiveConnectionCount { get; set; }

    public int? FreeConnectionCount { get; set; }

    public int? ReclaimedConnectionCount { get; set; }

    public TimeSpan? SampleDuration { get; set; }

    public IList<string> Notes { get; } = new List<string>();
}
