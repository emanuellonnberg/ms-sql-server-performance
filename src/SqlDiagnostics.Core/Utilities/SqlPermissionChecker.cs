using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlDiagnostics.Core.Utilities;

/// <summary>
/// Checks whether the current principal has the minimum permissions needed to run full diagnostics.
/// </summary>
public static class SqlPermissionChecker
{
    private static readonly IReadOnlyList<PermissionRequirement> ServerRequirements = new[]
    {
        new PermissionRequirement("SERVER", "VIEW SERVER STATE", "VIEW SERVER STATE")
    };

    private static readonly IReadOnlyList<PermissionRequirement> DatabaseRequirements = new[]
    {
        new PermissionRequirement("DATABASE", "VIEW DATABASE STATE", "VIEW DATABASE STATE")
    };

    /// <summary>
    /// Validates that the connection string has sufficient rights to run the advanced diagnostics collectors.
    /// </summary>
    public static async Task<PermissionCheckResult> CheckRequiredPermissionsAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        var missing = new List<string>();

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            foreach (var requirement in ServerRequirements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await HasPermissionAsync(connection, requirement, cancellationToken).ConfigureAwait(false))
                {
                    missing.Add(requirement.DisplayName);
                }
            }

            foreach (var requirement in DatabaseRequirements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await HasPermissionAsync(connection, requirement, cancellationToken).ConfigureAwait(false))
                {
                    missing.Add($"{requirement.DisplayName} on {connection.Database}");
                }
            }

            return missing.Count == 0
                ? PermissionCheckResult.Success()
                : PermissionCheckResult.Failure(missing);
        }
        catch (Exception ex)
        {
            return PermissionCheckResult.Error(ex.Message, missing);
        }
    }

    private static async Task<bool> HasPermissionAsync(
        SqlConnection connection,
        PermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (requirement.PermissionClass.Equals("SERVER", StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = """
                SELECT ISNULL(HAS_PERMS_BY_NAME(NULL, 'SERVER', @permission), 0)
                """;
        }
        else
        {
            command.CommandText = """
                SELECT ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', @permission), 0)
                """;
        }

        command.Parameters.AddWithValue("@permission", requirement.PermissionName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null || result is DBNull)
        {
            return false;
        }

        if (result is int intValue)
        {
            return intValue != 0;
        }

        if (result is long longValue)
        {
            return longValue != 0;
        }

        if (int.TryParse(result.ToString(), out var parsed))
        {
            return parsed != 0;
        }

        return false;
    }

    private readonly record struct PermissionRequirement(
        string PermissionClass,
        string PermissionName,
        string DisplayName);
}

/// <summary>
/// Result of a permission check for diagnostics.
/// </summary>
public sealed class PermissionCheckResult
{
    private PermissionCheckResult(bool succeeded, string? errorMessage, IReadOnlyList<string> missingPermissions)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
        MissingPermissions = missingPermissions;
    }

    public bool Succeeded { get; }

    public string? ErrorMessage { get; }

    public IReadOnlyList<string> MissingPermissions { get; }

    public static PermissionCheckResult Success() =>
        new(true, null, Array.Empty<string>());

    public static PermissionCheckResult Failure(IReadOnlyList<string> missing) =>
        new(false, null, missing);

    public static PermissionCheckResult Error(string message, IReadOnlyList<string>? missing = null) =>
        new(false, message, missing ?? Array.Empty<string>());
}
