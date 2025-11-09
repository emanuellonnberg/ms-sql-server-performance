using System;
using System.Collections.Generic;
using System.Data.Common;

namespace SqlDiagnostics.Utilities;

/// <summary>
/// Utility to extract key attributes from SQL Server connection strings.
/// </summary>
public static class ConnectionStringParser
{
    private static readonly HashSet<string> DataSourceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data Source", "Server", "Address", "Addr", "Network Address"
    };

    public static string? TryGetDataSource(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        foreach (var key in DataSourceKeys)
        {
            if (builder.TryGetValue(key, out var value) && value is string dataSource)
            {
                return dataSource;
            }
        }

        return null;
    }
}
