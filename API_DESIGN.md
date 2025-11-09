# SQL Diagnostics Tool - API Design

## Namespace Structure

```
SqlDiagnostics.Core
├── SqlDiagnosticsClient              (Main entry point)
├── Diagnostics
│   ├── ConnectionDiagnostics
│   ├── NetworkDiagnostics
│   ├── QueryDiagnostics
│   └── ServerDiagnostics
├── Models
│   ├── Metrics (ConnectionMetrics, NetworkMetrics, etc.)
│   ├── Reports (DiagnosticReport, Recommendation)
│   └── Options (DiagnosticOptions, DiagnosticOptionsBuilder)
├── Collectors
│   ├── IMetricCollector<T>
│   └── Built-in collectors
├── Reporting
│   ├── IReportGenerator
│   └── Format implementations
└── Extensions
    └── SqlConnectionExtensions
```

## Core API

### SqlDiagnosticsClient

Main entry point for all diagnostic operations.

```csharp
namespace SqlDiagnostics.Core
{
    /// <summary>
    /// Main client for SQL Server diagnostics
    /// </summary>
    public class SqlDiagnosticsClient : IDisposable
    {
        // Constructor
        public SqlDiagnosticsClient(ILogger<SqlDiagnosticsClient> logger = null);
        
        // Quick diagnostic methods
        
        /// <summary>
        /// Performs a quick health check of SQL Server connectivity
        /// </summary>
        public Task<DiagnosticReport> RunQuickCheckAsync(
            string connectionString,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Runs comprehensive diagnostics with all available tests
        /// </summary>
        public Task<DiagnosticReport> RunFullDiagnosticsAsync(
            string connectionString,
            DiagnosticOptions options = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Runs specific diagnostic categories
        /// </summary>
        public Task<DiagnosticReport> RunDiagnosticsAsync(
            string connectionString,
            DiagnosticCategories categories,
            CancellationToken cancellationToken = default);
        
        // Continuous monitoring
        
        /// <summary>
        /// Starts continuous monitoring with periodic diagnostic snapshots
        /// </summary>
        public IObservable<DiagnosticSnapshot> MonitorContinuously(
            string connectionString,
            TimeSpan interval,
            DiagnosticOptions options = null);
        
        // Component access
        
        public ConnectionDiagnostics Connection { get; }
        public NetworkDiagnostics Network { get; }
        public QueryDiagnostics Query { get; }
        public ServerDiagnostics Server { get; }
        
        // Extension points
        
        public void RegisterCollector<T>(IMetricCollector<T> collector);
        public void RegisterRecommendationRule(IRecommendationRule rule);
        
        public void Dispose();
    }
    
    [Flags]
    public enum DiagnosticCategories
    {
        None = 0,
        Connection = 1,
        Network = 2,
        Query = 4,
        Server = 8,
        All = Connection | Network | Query | Server
    }
}
```

### ConnectionDiagnostics

```csharp
namespace SqlDiagnostics.Core.Diagnostics
{
    public class ConnectionDiagnostics
    {
        /// <summary>
        /// Measures connection establishment performance
        /// </summary>
        public Task<ConnectionMetrics> MeasureConnectionAsync(
            string connectionString,
            int attempts = 10,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Analyzes connection pool behavior and health
        /// </summary>
        public Task<ConnectionPoolMetrics> AnalyzeConnectionPoolAsync(
            string connectionString,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Monitors connection stability over time
        /// </summary>
        public Task<ConnectionStabilityReport> MonitorConnectionStabilityAsync(
            string connectionString,
            TimeSpan? duration = null,
            TimeSpan? pingInterval = null,
            CancellationToken cancellationToken = default);
    }
}
```

### NetworkDiagnostics

```csharp
namespace SqlDiagnostics.Core.Diagnostics
{
    public class NetworkDiagnostics
    {
        /// <summary>
        /// Measures network latency to SQL Server
        /// </summary>
        public Task<LatencyMetrics> MeasureLatencyAsync(
            string serverAddress,
            int attempts = 10,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Tests DNS resolution performance
        /// </summary>
        public Task<DnsMetrics> TestDnsResolutionAsync(
            string hostname,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Verifies TCP port accessibility
        /// </summary>
        public Task<PortConnectivityResult> TestPortConnectivityAsync(
            string host,
            int port = 1433,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Estimates available bandwidth to SQL Server
        /// </summary>
        public Task<BandwidthMetrics> MeasureBandwidthAsync(
            SqlConnection connection,
            int testDurationSeconds = 10,
            CancellationToken cancellationToken = default);
    }
}
```

### QueryDiagnostics

```csharp
namespace SqlDiagnostics.Core.Diagnostics
{
    public class QueryDiagnostics
    {
        /// <summary>
        /// Executes a query with detailed performance metrics
        /// </summary>
        public Task<QueryMetrics> ExecuteWithDiagnosticsAsync(
            SqlConnection connection,
            string query,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Analyzes query execution plan
        /// </summary>
        public Task<QueryPlanAnalysis> AnalyzeQueryPlanAsync(
            SqlConnection connection,
            string query,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Detects blocking sessions
        /// </summary>
        public Task<BlockingReport> DetectBlockingAsync(
            SqlConnection connection,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets wait statistics for current session or server
        /// </summary>
        public Task<WaitStatistics> GetWaitStatisticsAsync(
            SqlConnection connection,
            WaitStatsScope scope = WaitStatsScope.Session,
            CancellationToken cancellationToken = default);
    }
    
    public enum WaitStatsScope
    {
        Session,
        Server
    }
}
```

### ServerDiagnostics

```csharp
namespace SqlDiagnostics.Core.Diagnostics
{
    public class ServerDiagnostics
    {
        /// <summary>
        /// Gets comprehensive server health snapshot
        /// </summary>
        public Task<ServerHealthMetrics> GetServerHealthAsync(
            SqlConnection connection,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets current resource utilization
        /// </summary>
        public Task<ResourceMetrics> GetResourceUtilizationAsync(
            SqlConnection connection,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Analyzes SQL Server configuration
        /// </summary>
        public Task<ConfigurationReport> AnalyzeConfigurationAsync(
            SqlConnection connection,
            CancellationToken cancellationToken = default);
    }
}
```

## Configuration & Options

### DiagnosticOptions

```csharp
namespace SqlDiagnostics.Core.Models
{
    public class DiagnosticOptions
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public DiagnosticCategories Categories { get; set; } = DiagnosticCategories.All;
        public bool IncludeQueryPlans { get; set; } = false;
        public bool GenerateRecommendations { get; set; } = true;
        public bool CompareWithBaseline { get; set; } = false;
        public DiagnosticReport Baseline { get; set; }
    }
    
    public class DiagnosticOptionsBuilder
    {
        public DiagnosticOptionsBuilder WithTimeout(TimeSpan timeout);
        public DiagnosticOptionsBuilder WithCategories(DiagnosticCategories categories);
        public DiagnosticOptionsBuilder WithConnectionTests();
        public DiagnosticOptionsBuilder WithNetworkTests();
        public DiagnosticOptionsBuilder WithQueryAnalysis(bool includeQueryPlans = false);
        public DiagnosticOptionsBuilder WithServerHealth();
        public DiagnosticOptionsBuilder WithBaseline(DiagnosticReport baseline);
        public DiagnosticOptions Build();
    }
}
```

## Extension Methods

```csharp
namespace SqlDiagnostics.Core.Extensions
{
    public static class SqlConnectionExtensions
    {
        /// <summary>
        /// Runs quick diagnostics on this connection
        /// </summary>
        public static Task<ConnectionMetrics> DiagnoseAsync(
            this SqlConnection connection,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets connection statistics as metrics
        /// </summary>
        public static ConnectionMetrics GetConnectionMetrics(this SqlConnection connection);
        
        /// <summary>
        /// Tests if connection is healthy
        /// </summary>
        public static Task<bool> IsHealthyAsync(
            this SqlConnection connection,
            CancellationToken cancellationToken = default);
    }
    
    public static class DiagnosticReportExtensions
    {
        public static Task ExportToJsonAsync(
            this DiagnosticReport report,
            string filePath,
            CancellationToken cancellationToken = default);
        
        public static Task ExportToHtmlAsync(
            this DiagnosticReport report,
            string filePath,
            CancellationToken cancellationToken = default);
        
        public static string ToMarkdown(this DiagnosticReport report);
        public static int GetHealthScore(this DiagnosticReport report);
    }
}
```

## Usage Examples

### Basic Usage

```csharp
using SqlDiagnostics.Core;

var client = new SqlDiagnosticsClient();

// Quick check
var quickReport = await client.RunQuickCheckAsync(connectionString);
Console.WriteLine($"Health Score: {quickReport.GetHealthScore()}");

// Full diagnostics
var fullReport = await client.RunFullDiagnosticsAsync(connectionString);
await fullReport.ExportToHtmlAsync("diagnostic-report.html");
```

### Advanced Configuration

```csharp
var options = new DiagnosticOptionsBuilder()
    .WithTimeout(TimeSpan.FromMinutes(2))
    .WithConnectionTests()
    .WithNetworkTests()
    .WithQueryAnalysis(includeQueryPlans: true)
    .Build();

var report = await client.RunFullDiagnosticsAsync(connectionString, options);
```

### Continuous Monitoring

```csharp
var monitor = client
    .MonitorContinuously(
        connectionString, 
        TimeSpan.FromSeconds(10))
    .Subscribe(
        snapshot => 
        {
            Console.WriteLine($"[{snapshot.Timestamp}] Health: {snapshot.OverallHealth}");
        },
        error => Console.WriteLine($"Error: {error}"),
        () => Console.WriteLine("Monitoring completed")
    );

// Run for 5 minutes
await Task.Delay(TimeSpan.FromMinutes(5));
monitor.Dispose();
```

### Component-Level Access

```csharp
// Use individual components
var connectionDiag = client.Connection;
var metrics = await connectionDiag.MeasureConnectionAsync(connectionString, attempts: 20);

Console.WriteLine($"Avg Connection Time: {metrics.AverageConnectionTime.TotalMilliseconds}ms");
Console.WriteLine($"Success Rate: {metrics.SuccessRate:P}");
```
