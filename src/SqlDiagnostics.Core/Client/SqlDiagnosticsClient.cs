using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Core.Diagnostics.Connection;
using SqlDiagnostics.Core.Diagnostics.Database;
using SqlDiagnostics.Core.Diagnostics.Network;
using SqlDiagnostics.Core.Diagnostics.Query;
using SqlDiagnostics.Core.Diagnostics.Server;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Reports;
using SqlDiagnostics.Core.Utilities;
using Microsoft.Data.SqlClient;

namespace SqlDiagnostics.Core;

/// <summary>
/// High-level facade that orchestrates individual diagnostics modules.
/// </summary>
public sealed class SqlDiagnosticsClient : IAsyncDisposable, IDisposable
{
    private readonly ConnectionDiagnostics _connectionDiagnostics;
    private readonly NetworkDiagnostics _networkDiagnostics;
    private readonly QueryDiagnostics _queryDiagnostics;
    private readonly DatabaseDiagnostics _databaseDiagnostics;
    private readonly ServerDiagnostics _serverDiagnostics;
    private readonly ILogger? _logger;
    private readonly List<IRecommendationRule> _recommendationRules = new();
    private bool _disposed;

    public SqlDiagnosticsClient(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<SqlDiagnosticsClient>();
        _connectionDiagnostics = new ConnectionDiagnostics(loggerFactory?.CreateLogger<ConnectionDiagnostics>());
        _networkDiagnostics = new NetworkDiagnostics(loggerFactory?.CreateLogger<NetworkDiagnostics>());
        _queryDiagnostics = new QueryDiagnostics();
        _serverDiagnostics = new ServerDiagnostics(loggerFactory?.CreateLogger<ServerDiagnostics>());
        _databaseDiagnostics = new DatabaseDiagnostics(loggerFactory?.CreateLogger<DatabaseDiagnostics>());
    }

    public ConnectionDiagnostics Connection => _connectionDiagnostics;

    public NetworkDiagnostics Network => _networkDiagnostics;

    public QueryDiagnostics Query => _queryDiagnostics;

    public ServerDiagnostics Server => _serverDiagnostics;

    public DatabaseDiagnostics Database => _databaseDiagnostics;

    public void RegisterRecommendationRule(IRecommendationRule rule)
    {
        EnsureNotDisposed();

        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        _recommendationRules.Add(rule);
    }

    public Task<DiagnosticReport> RunQuickCheckAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var options = new DiagnosticOptions
        {
            Categories = DiagnosticCategories.Connection | DiagnosticCategories.Network
        };

        return RunDiagnosticsInternalAsync(connectionString, options, cancellationToken);
    }

    public Task<DiagnosticReport> RunDiagnosticsAsync(
        string connectionString,
        DiagnosticCategories categories,
        CancellationToken cancellationToken = default)
    {
        var options = new DiagnosticOptions
        {
            Categories = categories
        };

        return RunDiagnosticsInternalAsync(connectionString, options, cancellationToken);
    }

    public Task<DiagnosticReport> RunFullDiagnosticsAsync(
        string connectionString,
        DiagnosticOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunDiagnosticsInternalAsync(connectionString, options ?? new DiagnosticOptions(), cancellationToken);

    public IObservable<DiagnosticSnapshot> MonitorContinuously(
        string connectionString,
        TimeSpan interval,
        DiagnosticOptions? options = null)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
        }

        return new DiagnosticsObservable(this, connectionString, interval, options?.Clone());
    }

    [Obsolete("Use RunFullDiagnosticsAsync moving forward.")]
    public Task<DiagnosticReport> RunComprehensiveAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        RunFullDiagnosticsAsync(connectionString, cancellationToken: cancellationToken);

    private async Task<DiagnosticReport> RunDiagnosticsInternalAsync(
        string connectionString,
        DiagnosticOptions options,
        CancellationToken cancellationToken)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var effectiveOptions = options.Clone();
        if (effectiveOptions.Categories == DiagnosticCategories.None)
        {
            effectiveOptions.Categories = DiagnosticCategories.All;
        }

        var report = new DiagnosticReport
        {
            TargetDataSource = ConnectionStringParser.TryGetDataSource(connectionString)
        };

        _logger?.LogInformation(
            "Running diagnostics for {Target} with categories {Categories}",
            report.TargetDataSource ?? "<unknown>",
            effectiveOptions.Categories);

        report.Metadata["categories"] = effectiveOptions.Categories.ToString();
        report.Metadata["timeout_seconds"] = effectiveOptions.Timeout.TotalSeconds;
        report.Metadata["include_query_plans"] = effectiveOptions.IncludeQueryPlans;
        report.Metadata["generate_recommendations"] = effectiveOptions.GenerateRecommendations;

        if (effectiveOptions.CompareWithBaseline && effectiveOptions.Baseline is not null)
        {
            report.Metadata["baseline_target"] = effectiveOptions.Baseline.TargetDataSource ?? "<unknown>";
        }

        if (effectiveOptions.Categories.HasFlag(DiagnosticCategories.Connection))
        {
            report.Connection = await _connectionDiagnostics
                .MeasureConnectionAsync(connectionString, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (effectiveOptions.Categories.HasFlag(DiagnosticCategories.Network))
        {
            var host = report.TargetDataSource;
            if (!string.IsNullOrWhiteSpace(host))
            {
                report.Network = await _networkDiagnostics
                    .MeasureLatencyAsync(host, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _logger?.LogWarning("Skipping network diagnostics; unable to determine target host.");
                report.Metadata["network_skipped"] = "Missing data source";
            }
        }

        if (NeedsSqlConnection(effectiveOptions.Categories))
        {
            using var connection = new SqlConnection(connectionString);

            if (effectiveOptions.Categories.HasFlag(DiagnosticCategories.Query))
            {
                try
                {
                    var queryMetrics = await _queryDiagnostics
                        .ExecuteWithDiagnosticsAsync(connection, "SELECT 1", cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    queryMetrics.AddStatistic("QueryText", "SELECT 1");
                    report.Query = queryMetrics;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Query diagnostics failed.");
                    report.Metadata["query_skipped"] = ex.Message;
                }
            }

            if (effectiveOptions.Categories.HasFlag(DiagnosticCategories.Server))
            {
                try
                {
                    report.Server = await _serverDiagnostics
                        .CollectAsync(connection, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Server diagnostics failed.");
                    report.Metadata["server_skipped"] = ex.Message;
                }
            }

            if (effectiveOptions.Categories.HasFlag(DiagnosticCategories.Database))
            {
                try
                {
                    report.Databases = await _databaseDiagnostics
                        .CollectAsync(connection, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Database diagnostics failed.");
                    report.Metadata["database_skipped"] = ex.Message;
                }
            }
        }

        if (effectiveOptions.GenerateRecommendations)
        {
            GenerateRecommendations(report);
        }

        return report;
    }

    private static bool NeedsSqlConnection(DiagnosticCategories categories) =>
        (categories & (DiagnosticCategories.Query | DiagnosticCategories.Server | DiagnosticCategories.Database)) != 0;

    private sealed class DiagnosticsObservable : IObservable<DiagnosticSnapshot>
    {
        private readonly SqlDiagnosticsClient _client;
        private readonly string _connectionString;
        private readonly TimeSpan _interval;
        private readonly DiagnosticOptions? _options;

        public DiagnosticsObservable(
            SqlDiagnosticsClient client,
            string connectionString,
            TimeSpan interval,
            DiagnosticOptions? options)
        {
            _client = client;
            _connectionString = connectionString;
            _interval = interval;
            _options = options;
        }

        public IDisposable Subscribe(IObserver<DiagnosticSnapshot> observer)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            var subscription = new DiagnosticsSubscription(_client, _connectionString, _interval, _options?.Clone(), observer);
            subscription.Start();
            return subscription;
        }

        private sealed class DiagnosticsSubscription : IDisposable
        {
            private readonly SqlDiagnosticsClient _client;
            private readonly string _connectionString;
            private readonly TimeSpan _interval;
            private readonly DiagnosticOptions? _options;
            private readonly IObserver<DiagnosticSnapshot> _observer;
            private readonly CancellationTokenSource _cts = new();
            private readonly object _gate = new();
            private Task? _loop;
            private bool _disposed;
            private bool _cleanedUp;

            public DiagnosticsSubscription(
                SqlDiagnosticsClient client,
                string connectionString,
                TimeSpan interval,
                DiagnosticOptions? options,
                IObserver<DiagnosticSnapshot> observer)
            {
                _client = client;
                _connectionString = connectionString;
                _interval = interval;
                _options = options;
                _observer = observer;
            }

            public void Start()
            {
                _loop = Task.Run(ExecuteAsync);
            }

            private async Task ExecuteAsync()
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        DiagnosticReport report;
                        try
                        {
                            if (_options is null)
                            {
                                report = await _client.RunQuickCheckAsync(_connectionString, _cts.Token).ConfigureAwait(false);
                            }
                            else
                            {
                                report = await _client.RunFullDiagnosticsAsync(_connectionString, _options, _cts.Token).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _observer.OnError(ex);
                            return;
                        }

                        _observer.OnNext(new DiagnosticSnapshot(DateTime.UtcNow, report));

                        try
                        {
                            await Task.Delay(_interval, _cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }

                    _observer.OnCompleted();
                    Cleanup();
                }
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                }

                _cts.Cancel();
                var loop = _loop;
                if (loop is not null)
                {
                    try
                    {
                        loop.GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Swallow exceptions when tearing down the loop.
                    }
                }

                Cleanup();
            }

            private void Cleanup()
            {
                lock (_gate)
                {
                    if (_cleanedUp)
                    {
                        return;
                    }

                    _cleanedUp = true;
                }

                _cts.Dispose();
            }
        }
    }

    private void GenerateRecommendations(DiagnosticReport report)
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

        foreach (var rule in _recommendationRules)
        {
            try
            {
                if (!rule.Applies(report))
                {
                    continue;
                }

                var recommendation = rule.Generate(report);
                if (recommendation is not null)
                {
                    report.Recommendations.Add(recommendation);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Recommendation rule {Rule} execution failed.", rule.GetType().Name);
            }
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
