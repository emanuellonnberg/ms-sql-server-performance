using System;
using System.Collections.Generic;
using System.Data.Common;

namespace SqlDiagnostics.Core.Utilities;

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

    /// <summary>
    /// Attempts to normalise a data source string into a pingable host and optional TCP port.
    /// </summary>
    public static bool TryGetNetworkEndpoint(string? dataSource, out string? host, out int? port)
    {
        host = null;
        port = null;

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return false;
        }

        var value = dataSource.Trim();

        // Strip common protocol prefixes used in connection strings (e.g. tcp:).
        if (value.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(4);
        }
        else if (value.StartsWith("np:", StringComparison.OrdinalIgnoreCase) ||
                 value.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(3);
        }

        value = value.Trim();

        if (value.Length == 0)
        {
            return false;
        }

        string portText = string.Empty;

        // IPv6 literal with optional port e.g. [fe80::1%3],1433
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracket = value.IndexOf(']');
            if (closingBracket > 0)
            {
                host = value.Substring(1, closingBracket - 1);

                if (closingBracket + 1 < value.Length && value[closingBracket + 1] == ',' &&
                    closingBracket + 2 < value.Length)
                {
                    portText = value.Substring(closingBracket + 2);
                }
            }
            else
            {
                host = value;
            }
        }

        // Standard host,port syntax.
        if (host is null)
        {
            var commaIndex = value.IndexOf(',');
            if (commaIndex >= 0)
            {
                host = value.Substring(0, commaIndex);
                if (commaIndex + 1 < value.Length)
                {
                    portText = value.Substring(commaIndex + 1);
                }
            }
            else
            {
                host = value;
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // Remove named instance suffix if present (e.g. localhost\SQLEXPRESS).
        var instanceSeparator = host.IndexOf('\\');
        if (instanceSeparator >= 0)
        {
            host = host.Substring(0, instanceSeparator);
        }

        host = host.Trim();
        if (host.Length == 0)
        {
            return false;
        }

        // Map special aliases commonly used in SQL connection strings.
        if (host is "." or "(local)" or "(localdb)")
        {
            host = "localhost";
        }
        else if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            host = "localhost";
        }

        if (!string.IsNullOrWhiteSpace(portText) && int.TryParse(portText, out var parsedPort))
        {
            port = parsedPort;
        }

        return true;
    }
}
