using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Core.Interfaces;
using SqlDiagnostics.Core.Models;

namespace SqlDiagnostics.Core.Diagnostics.Server;

/// <summary>
/// Executes lightweight DMV queries to surface instance state information for quick health checks.
/// </summary>
public sealed class ServerStateProbe : MetricCollectorBase<ServerStateSnapshot>
{
    public ServerStateProbe(ILogger? logger = null)
        : base(logger)
    {
    }

    public override bool IsSupported(SqlConnection connection) => connection != null;

    public override async Task<ServerStateSnapshot> CollectAsync(SqlConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        var shouldClose = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            shouldClose = true;
        }

        try
        {
            var snapshot = new ServerStateSnapshot();

            var propertiesTask = QueryServerPropertiesAsync(connection, cancellationToken);
            var servicesTask = QueryServiceStatesAsync(connection, cancellationToken);
            var databasesTask = QueryDatabaseStatesAsync(connection, cancellationToken);
            var uptimeTask = QueryServerUptimeAsync(connection, cancellationToken);

            await Task.WhenAll(propertiesTask, servicesTask, databasesTask, uptimeTask).ConfigureAwait(false);

            ApplyProperties(snapshot, propertiesTask.Result);
            ApplyServices(snapshot, servicesTask.Result);
            ApplyDatabases(snapshot, databasesTask.Result);
            snapshot.SqlServerStartTimeUtc = uptimeTask.Result;
            snapshot.PendingRestart = HasPendingRestart(snapshot.Services);

            return snapshot;
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private async Task<Dictionary<string, object?>> QueryServerPropertiesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = ServerPropertyQuery;
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new Dictionary<string, object?>();
            }

            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["MachineName"] = reader.IsDBNull(0) ? null : reader.GetString(0),
                ["ServerName"] = reader.IsDBNull(1) ? null : reader.GetString(1),
                ["Edition"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                ["ProductVersion"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                ["ProductLevel"] = reader.IsDBNull(4) ? null : reader.GetString(4),
                ["EngineEdition"] = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                ["IsClustered"] = reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                ["IsHadrEnabled"] = reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                ["DefaultDataPath"] = reader.IsDBNull(8) ? null : reader.GetString(8),
                ["DefaultLogPath"] = reader.IsDBNull(9) ? null : reader.GetString(9)
            };

            return result;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ServerServiceState>> QueryServiceStatesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = ServerServicesQuery;
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            var services = new List<ServerServiceState>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var state = new ServerServiceState
                {
                    ServiceName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    StartupType = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Status = reader.IsDBNull(2) ? null : reader.GetString(2),
                    LastStartTimeUtc = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    Filename = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ServiceAccount = reader.IsDBNull(5) ? null : reader.GetString(5)
                };
                services.Add(state);
            }

            return (IReadOnlyList<ServerServiceState>)services;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ServerDatabaseState>> QueryDatabaseStatesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = DatabaseStateQuery;
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            var databases = new List<ServerDatabaseState>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var state = new ServerDatabaseState
                {
                    DatabaseId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    State = reader.IsDBNull(2) ? null : reader.GetString(2),
                    RecoveryModel = reader.IsDBNull(3) ? null : reader.GetString(3),
                    UserAccess = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsReadOnly = reader.IsDBNull(5) ? null : reader.GetBoolean(5),
                    IsInStandby = reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                    IsAutoCloseOn = reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                    IsAutoShrinkOn = reader.IsDBNull(8) ? null : reader.GetBoolean(8),
                    IsMirroringEnabled = reader.IsDBNull(9) ? null : reader.GetBoolean(9)
                };

                databases.Add(state);
            }

            return (IReadOnlyList<ServerDatabaseState>)databases;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private Task<DateTime?> QueryServerUptimeAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        return ExecuteWithRetryAsync(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = ServerUptimeQuery;
            command.CommandType = CommandType.Text;

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null || result == DBNull.Value)
            {
                return (DateTime?)null;
            }

            return Convert.ToDateTime(result).ToUniversalTime();
        }, cancellationToken: cancellationToken);
    }

    private static void ApplyProperties(ServerStateSnapshot snapshot, IReadOnlyDictionary<string, object?> properties)
    {
        if (properties.TryGetValue("MachineName", out var machineName))
        {
            snapshot.MachineName = machineName as string;
        }

        if (properties.TryGetValue("ServerName", out var serverName))
        {
            snapshot.ServerName = serverName as string;
        }

        if (properties.TryGetValue("Edition", out var edition))
        {
            snapshot.Edition = edition as string;
        }

        if (properties.TryGetValue("ProductVersion", out var productVersion))
        {
            snapshot.ProductVersion = productVersion as string;
        }

        if (properties.TryGetValue("ProductLevel", out var productLevel))
        {
            snapshot.ProductLevel = productLevel as string;
        }

        if (properties.TryGetValue("EngineEdition", out var engineEdition) && engineEdition is int engineEditionValue)
        {
            snapshot.EngineEdition = engineEditionValue;
        }

        if (properties.TryGetValue("IsClustered", out var isClustered) && isClustered is bool isClusteredBool)
        {
            snapshot.IsClustered = isClusteredBool;
        }

        if (properties.TryGetValue("IsHadrEnabled", out var isHadrEnabled) && isHadrEnabled is bool isHadr)
        {
            snapshot.IsHadrEnabled = isHadr;
        }

        if (properties.TryGetValue("DefaultDataPath", out var dataPath) && dataPath is string dataPathString)
        {
            snapshot.DefaultDataPath = dataPathString;
        }

        if (properties.TryGetValue("DefaultLogPath", out var logPath) && logPath is string logPathString)
        {
            snapshot.DefaultLogPath = logPathString;
        }

        foreach (var kvp in properties)
        {
            snapshot.AdditionalProperties[kvp.Key] = kvp.Value ?? string.Empty;
        }
    }

    private static void ApplyServices(ServerStateSnapshot snapshot, IReadOnlyList<ServerServiceState> services)
    {
        foreach (var service in services)
        {
            snapshot.Services.Add(service);
        }
    }

    private static void ApplyDatabases(ServerStateSnapshot snapshot, IReadOnlyList<ServerDatabaseState> databases)
    {
        foreach (var database in databases)
        {
            snapshot.Databases.Add(database);
        }
    }

    private static bool HasPendingRestart(IEnumerable<ServerServiceState> services)
    {
        foreach (var service in services)
        {
            if (service.Status is null)
            {
                continue;
            }

            if (service.Status.IndexOf("PENDING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                service.Status.IndexOf("RESTART", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private const string ServerPropertyQuery = """
SELECT
    CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) AS MachineName,
    CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) AS ServerName,
    CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS Edition,
    CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion,
    CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(128)) AS ProductLevel,
    CAST(SERVERPROPERTY('EngineEdition') AS int) AS EngineEdition,
    CAST(SERVERPROPERTY('IsClustered') AS bit) AS IsClustered,
    CAST(SERVERPROPERTY('IsHadrEnabled') AS bit) AS IsHadrEnabled,
    CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS DefaultDataPath,
    CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000)) AS DefaultLogPath;
""";

    private const string ServerServicesQuery = """
SELECT
    service_name,
    startup_type_desc,
    status_desc,
    last_startup_time,
    filename,
    service_account
FROM sys.dm_server_services
ORDER BY service_name;
""";

    private const string DatabaseStateQuery = """
SELECT
    d.database_id,
    d.name,
    d.state_desc,
    d.recovery_model_desc,
    d.user_access_desc,
    d.is_read_only,
    d.is_in_standby,
    d.is_auto_close_on,
    d.is_auto_shrink_on,
    d.is_mirroring_enabled
FROM sys.databases AS d
ORDER BY d.database_id;
""";

    private const string ServerUptimeQuery = "SELECT sqlserver_start_time FROM sys.dm_os_sys_info;";
}
