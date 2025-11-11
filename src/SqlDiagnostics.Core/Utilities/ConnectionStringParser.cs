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
            value = value[4..];
        }
        else if (value.StartsWith("np:", StringComparison.OrdinalIgnoreCase) ||
                 value.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[3..];
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim();

        if (span.IsEmpty)
        {
            return false;
        }

        // IPv6 literal with optional port e.g. [fe80::1%3],1433
        if (span[0] == '[')
        {
            var closingBracket = span.IndexOf(']');
            if (closingBracket > 0)
            {
                var hostSpan = span.Slice(1, closingBracket - 1);
                span = span[(closingBracket + 1)..];
                host = hostSpan.ToString();

                if (!span.IsEmpty && span[0] == ',' && span.Length > 1)
                {
                    if (int.TryParse(span[1..].ToString(), out var parsedPort))
                    {
                        port = parsedPort;
                    }
                }
            }
            else
            {
                host = span.ToString();
                span = ReadOnlySpan<char>.Empty;
            }
        }

        // Standard host,port syntax.
        if (host is null)
        {
            var commaIndex = span.IndexOf(',');
            if (commaIndex >= 0)
            {
                if (int.TryParse(span[(commaIndex + 1)..].ToString(), out var parsedPort))
                {
                    port = parsedPort;
                }

                span = span[..commaIndex];
            }

            host = span.ToString();
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // Remove named instance suffix if present (e.g. localhost\SQLEXPRESS).
        var instanceSeparator = host.IndexOf('\\');
        if (instanceSeparator >= 0)
        {
            host = host[..instanceSeparator];
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

        return true;
    }
}
