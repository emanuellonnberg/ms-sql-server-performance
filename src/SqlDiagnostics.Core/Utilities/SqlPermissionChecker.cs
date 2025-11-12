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
        var statuses = new List<PermissionStatus>();

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
                    statuses.Add(new PermissionStatus(requirement.DisplayName, false, requirement.PermissionClass));
                }
                else
                {
                    statuses.Add(new PermissionStatus(requirement.DisplayName, true, requirement.PermissionClass));
                }
            }

            foreach (var requirement in DatabaseRequirements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var display = $"{requirement.DisplayName} ({connection.Database})";
                if (!await HasPermissionAsync(connection, requirement with { DisplayName = display }, cancellationToken).ConfigureAwait(false))
                {
                    missing.Add(display);
                    statuses.Add(new PermissionStatus(display, false, requirement.PermissionClass));
                }
                else
                {
                    statuses.Add(new PermissionStatus(display, true, requirement.PermissionClass));
                }
            }

            return missing.Count == 0
                ? PermissionCheckResult.Success(statuses)
                : PermissionCheckResult.Failure(missing, statuses);
        }
        catch (Exception ex)
        {
            return PermissionCheckResult.Error(ex.Message, statuses, missing);
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
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM sys.fn_my_permissions(NULL, 'SERVER')
                    WHERE permission_name = @permission
                ) THEN 1 ELSE 0 END
                """;
        }
        else
        {
            command.CommandText = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM sys.fn_my_permissions(NULL, 'DATABASE')
                    WHERE permission_name = @permission
                ) THEN 1 ELSE 0 END
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
    private PermissionCheckResult(bool succeeded, string? errorMessage, IReadOnlyList<string> missingPermissions, IReadOnlyList<PermissionStatus> statuses)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
        MissingPermissions = missingPermissions;
        Statuses = statuses;
    }

    public bool Succeeded { get; }

    public string? ErrorMessage { get; }

    public IReadOnlyList<string> MissingPermissions { get; }

    public IReadOnlyList<PermissionStatus> Statuses { get; }

    public static PermissionCheckResult Success(IReadOnlyList<PermissionStatus> statuses) =>
        new(true, null, Array.Empty<string>(), statuses);

    public static PermissionCheckResult Failure(IReadOnlyList<string> missing, IReadOnlyList<PermissionStatus> statuses) =>
        new(false, null, missing, statuses);

    public static PermissionCheckResult Error(string message, IReadOnlyList<PermissionStatus> statuses, IReadOnlyList<string>? missing = null) =>
        new(false, message, missing ?? Array.Empty<string>(), statuses);
}

public readonly record struct PermissionStatus(string Name, bool Granted, string Scope);
