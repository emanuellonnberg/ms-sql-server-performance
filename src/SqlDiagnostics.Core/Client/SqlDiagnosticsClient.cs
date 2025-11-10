using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Diagnostics.Connection;
using SqlDiagnostics.Diagnostics.Network;
using SqlDiagnostics.Diagnostics.Query;
using SqlDiagnostics.Diagnostics.Server;
using SqlDiagnostics.Models;
using SqlDiagnostics.Reports;
using SqlDiagnostics.Utilities;
using Microsoft.Data.SqlClient;

namespace SqlDiagnostics.Client;

/// <summary>
/// High-level facade that orchestrates individual diagnostics modules.
/// </summary>
public sealed class SqlDiagnosticsClient : IAsyncDisposable, IDisposable
{
    private readonly ConnectionDiagnostics _connectionDiagnostics;
    private readonly NetworkDiagnostics _networkDiagnostics;
    private readonly QueryDiagnostics _queryDiagnostics;
    private readonly ServerDiagnostics _serverDiagnostics;
    private readonly ILogger? _logger;
    private bool _disposed;

    public SqlDiagnosticsClient(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<SqlDiagnosticsClient>();
        _connectionDiagnostics = new ConnectionDiagnostics(loggerFactory?.CreateLogger<ConnectionDiagnostics>());
        _networkDiagnostics = new NetworkDiagnostics(loggerFactory?.CreateLogger<NetworkDiagnostics>());
        _queryDiagnostics = new QueryDiagnostics();
        _serverDiagnostics = new ServerDiagnostics(loggerFactory?.CreateLogger<ServerDiagnostics>());
    }

    public ConnectionDiagnostics Connection => _connectionDiagnostics;

    public NetworkDiagnostics Network => _networkDiagnostics;

    public QueryDiagnostics Query => _queryDiagnostics;

    public ServerDiagnostics Server => _serverDiagnostics;

    public async Task<DiagnosticReport> RunQuickCheckAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        var report = new DiagnosticReport
        {
            TargetDataSource = ConnectionStringParser.TryGetDataSource(connectionString)
        };

        _logger?.LogInformation("Running quick check against {Target}", report.TargetDataSource ?? "<unknown>");

        report.Connection = await _connectionDiagnostics.MeasureConnectionAsync(connectionString, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var dataSource = report.TargetDataSource;
        if (!string.IsNullOrWhiteSpace(dataSource))
        {
            report.Network = await _networkDiagnostics.MeasureLatencyAsync(dataSource, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        GenerateRecommendations(report);

        return report;
    }

    public async Task<DiagnosticReport> RunComprehensiveAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        var report = await RunQuickCheckAsync(connectionString, cancellationToken).ConfigureAwait(false);
        using var connection = new SqlConnection(connectionString);

        report.Server = await _serverDiagnostics.CollectAsync(connection, cancellationToken).ConfigureAwait(false);
        report.Metadata["collection_mode"] = "comprehensive";

        return report;
    }

    private static void GenerateRecommendations(DiagnosticReport report)
    {
        if (report.Connection is { AverageConnectionTime: { } average } && average > TimeSpan.FromMilliseconds(500))
        {
            report.Recommendations.Add(new Recommendation
            {
                Severity = RecommendationSeverity.Warning,
                Category = "Connection",
                Issue = "High connection latency",
                RecommendationText = "Consider enabling connection pooling or reviewing network latency."
            });
        }

        if (report.Connection is { SuccessRate: < 0.8 })
        {
            report.Recommendations.Add(new Recommendation
            {
                Severity = RecommendationSeverity.Warning,
                Category = "Connectivity",
                Issue = "Low success rate",
                RecommendationText = "Inspect SQL Server error logs and network stability."
            });
        }

        if (report.Network is { Jitter: { } jitter } && jitter > TimeSpan.FromMilliseconds(50))
        {
            report.Recommendations.Add(new Recommendation
            {
                Severity = RecommendationSeverity.Info,
                Category = "Network",
                Issue = "High jitter detected",
                RecommendationText = "Investigate network congestion or wireless links along the path."
            });
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqlDiagnosticsClient));
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
#if NETSTANDARD2_0
        return new ValueTask();
#else
        return ValueTask.CompletedTask;
#endif
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
