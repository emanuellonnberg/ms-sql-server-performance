# SQL Diagnostics Tool - Architecture

## Architecture Overview

The SQL Diagnostics Tool follows a modular, layered architecture designed for both standalone CLI usage and library integration.

```
┌─────────────────────────────────────────────────┐
│           Consumer Applications                  │
│  (Legacy Apps, CLI Tool, Monitoring Services)    │
└─────────────────┬───────────────────────────────┘
                  │
┌─────────────────▼───────────────────────────────┐
│         SqlDiagnostics.Core (Public API)        │
│  ┌──────────────────────────────────────────┐   │
│  │      SqlDiagnosticsClient                │   │
│  │  (Main Entry Point & Orchestration)      │   │
│  └──────────────────────────────────────────┘   │
└─────────────────┬───────────────────────────────┘
                  │
    ┌─────────────┼─────────────┬──────────────┐
    │             │             │              │
┌───▼────┐  ┌────▼─────┐  ┌───▼──────┐  ┌───▼─────┐
│Connection│ │  Network │  │  Query   │  │ Server  │
│Diagnostics│ │Diagnostics│ │Diagnostics│ │Diagnostics│
└───┬────┘  └────┬─────┘  └───┬──────┘  └───┬─────┘
    │             │             │              │
    └─────────────┼─────────────┼──────────────┘
                  │             │
         ┌────────▼─────────────▼──────────┐
         │   Diagnostic Data Collectors    │
         │ (Connection Stats, DMVs, etc.)  │
         └────────┬─────────────────────────┘
                  │
         ┌────────▼─────────────────────────┐
         │    Data Sources & Adapters       │
         │  (SqlClient, Network APIs, WMI)  │
         └──────────────────────────────────┘
```

## Core Components

### 1. SqlDiagnosticsClient (Facade)

The main entry point providing a simplified interface for common diagnostic scenarios.

**Responsibilities:**
- Orchestrate multiple diagnostic modules
- Aggregate results into cohesive reports
- Provide convenience methods for common use cases
- Handle cross-cutting concerns (logging, error handling)

**Key Methods:**
```csharp
Task<DiagnosticReport> RunFullDiagnosticsAsync(string connectionString, DiagnosticOptions options)
Task<DiagnosticReport> RunQuickCheckAsync(string connectionString)
IObservable<DiagnosticSnapshot> MonitorContinuously(string connectionString, TimeSpan interval)
```

### 2. ConnectionDiagnostics

Analyzes SQL connection behavior and health.

**Responsibilities:**
- Measure connection establishment timing
- Analyze connection pool behavior
- Monitor connection stability over time
- Track authentication and TLS metrics

**Data Sources:**
- `SqlConnection` events and timing
- Connection pool performance counters
- `SqlStatistics` from connection

### 3. NetworkDiagnostics

Tests network connectivity and performance to SQL Server.

**Responsibilities:**
- Measure network latency (RTT)
- Test DNS resolution
- Verify port connectivity
- Estimate available bandwidth
- Detect packet loss

**Data Sources:**
- `System.Net.NetworkInformation.Ping`
- `System.Net.Sockets.TcpClient`
- `System.Net.Dns`
- Custom TCP probes

### 4. QueryDiagnostics

Analyzes query execution and performance.

**Responsibilities:**
- Measure execution time breakdown
- Analyze query plans
- Detect blocking and deadlocks
- Collect wait statistics
- Track data transfer metrics

**Data Sources:**
- `SqlConnection.RetrieveStatistics()`
- DMVs: `sys.dm_exec_requests`, `sys.dm_os_wait_stats`
- Query plan XML (via `SET STATISTICS XML ON`)
- Blocking queries from `sys.dm_exec_requests`

### 5. ServerDiagnostics

Monitors SQL Server health and resource utilization.

**Responsibilities:**
- Collect CPU and memory metrics
- Monitor disk I/O performance
- Track connection counts
- Analyze transaction log usage
- Review server configuration

**Data Sources:**
- DMVs: `sys.dm_os_sys_info`, `sys.dm_os_performance_counters`
- `sys.configurations`
- Performance counters (if available)

### 6. ReportGenerator

Formats diagnostic results into various output formats.

**Responsibilities:**
- Generate JSON, HTML, Markdown reports
- Create actionable recommendations
- Compare against baselines
- Visualize metrics (charts in HTML)

## Data Model

### Core Metrics Classes

```csharp
// Connection metrics
public class ConnectionMetrics
{
    public TimeSpan AverageConnectionTime { get; set; }
    public double SuccessRate { get; set; }
    public int PoolSize { get; set; }
    public int ActiveConnections { get; set; }
    public int IdleConnections { get; set; }
    public TimeSpan AuthenticationTime { get; set; }
    public TimeSpan TlsHandshakeTime { get; set; }
}

// Network metrics
public class NetworkMetrics
{
    public TimeSpan AverageLatency { get; set; }
    public TimeSpan DnsResolutionTime { get; set; }
    public bool PortAccessible { get; set; }
    public double PacketLossRate { get; set; }
    public double EstimatedBandwidthMbps { get; set; }
    public int NetworkHops { get; set; }
}

// Query metrics
public class QueryMetrics
{
    public TimeSpan TotalExecutionTime { get; set; }
    public TimeSpan NetworkTime { get; set; }
    public TimeSpan ServerTime { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public int RowsReturned { get; set; }
    public int ServerRoundtrips { get; set; }
}

// Server metrics
public class ServerMetrics
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageMB { get; set; }
    public double PageLifeExpectancy { get; set; }
    public int ActiveConnections { get; set; }
    public Dictionary<string, TimeSpan> WaitStats { get; set; }
}

// Consolidated report
public class DiagnosticReport
{
    public DateTime GeneratedAt { get; set; }
    public string ConnectionString { get; set; } // sanitized
    public ConnectionMetrics Connection { get; set; }
    public NetworkMetrics Network { get; set; }
    public ServerMetrics Server { get; set; }
    public List<QueryMetrics> Queries { get; set; }
    public List<Recommendation> Recommendations { get; set; }
    public DiagnosticSummary Summary { get; set; }
}

// Smart recommendations
public class Recommendation
{
    public RecommendationSeverity Severity { get; set; }
    public string Category { get; set; }
    public string Issue { get; set; }
    public string Recommendation { get; set; }
    public string Rationale { get; set; }
}

public enum RecommendationSeverity
{
    Info,
    Warning,
    Critical
}
```

## Design Patterns

### 1. Strategy Pattern (Metric Collectors)

Different collection strategies for various SQL Server versions or configurations.

```csharp
public interface IMetricCollector<T>
{
    Task<T> CollectAsync(SqlConnection connection);
    bool IsSupported(SqlConnection connection);
}

public class Sql2012WaitStatsCollector : IMetricCollector<WaitStatistics> { }
public class Sql2016WaitStatsCollector : IMetricCollector<WaitStatistics> { }
```

### 2. Builder Pattern (Diagnostic Options)

Fluent API for configuring diagnostic runs.

```csharp
var options = new DiagnosticOptionsBuilder()
    .WithConnectionTests()
    .WithNetworkTests()
    .WithQueryAnalysis(includeQueryPlans: true)
    .WithServerHealth()
    .WithTimeout(TimeSpan.FromSeconds(30))
    .Build();
```

### 3. Observer Pattern (Continuous Monitoring)

Reactive streams for continuous monitoring scenarios.

```csharp
var monitor = diagnosticsClient
    .MonitorContinuously(connectionString, TimeSpan.FromSeconds(10))
    .Subscribe(
        snapshot => Console.WriteLine($"Health: {snapshot.OverallHealth}"),
        error => Console.WriteLine($"Error: {error.Message}")
    );
```

### 4. Factory Pattern (Report Generators)

Create appropriate report generators based on format.

```csharp
public interface IReportGenerator
{
    Task<string> GenerateAsync(DiagnosticReport report);
}

public class ReportGeneratorFactory
{
    public IReportGenerator Create(ReportFormat format)
    {
        return format switch
        {
            ReportFormat.Json => new JsonReportGenerator(),
            ReportFormat.Html => new HtmlReportGenerator(),
            ReportFormat.Markdown => new MarkdownReportGenerator(),
            _ => throw new ArgumentException("Unsupported format")
        };
    }
}
```

## Error Handling Strategy

### Resilience Policies

Use Polly for retry and circuit breaker patterns:

```csharp
// Retry transient SQL errors
var retryPolicy = Policy
    .Handle<SqlException>(ex => IsTransient(ex))
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

// Circuit breaker for network tests
var circuitBreaker = Policy
    .Handle<NetworkException>()
    .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
```

### Graceful Degradation

If certain diagnostics fail, continue with others:

```csharp
public async Task<DiagnosticReport> RunFullDiagnosticsAsync(...)
{
    var report = new DiagnosticReport();
    
    try 
    { 
        report.Connection = await connectionDiag.CollectAsync(); 
    }
    catch (Exception ex) 
    { 
        report.Errors.Add(new DiagnosticError("Connection", ex)); 
    }
    
    // Continue with other diagnostics...
    
    return report;
}
```

## Performance Considerations

### Minimize Overhead

1. **Lazy Collection**: Only collect metrics when explicitly requested
2. **Sampling**: For continuous monitoring, use sampling intervals (default 10s)
3. **Async Throughout**: All I/O operations are async to prevent blocking
4. **Connection Pooling**: Reuse connections when possible
5. **Batch DMV Queries**: Combine multiple DMV queries into single round-trip

### Memory Management

1. **Streaming Results**: For large result sets, use streaming APIs
2. **Dispose Pattern**: Properly dispose all connections and resources
3. **Memory Pools**: Use `ArrayPool<T>` for temporary buffers
4. **WeakReferences**: For baseline storage in long-running monitoring

## Security Considerations

### Credential Protection

1. **Never log connection strings**: Sanitize before storing/displaying
2. **Secure string handling**: Use `SecureString` for sensitive data
3. **Principle of least privilege**: Require minimal SQL permissions

### Required SQL Permissions

Minimum permissions for full diagnostics:
```sql
-- View server state for DMVs
GRANT VIEW SERVER STATE TO [DiagnosticUser];

-- View database state for database-specific queries
GRANT VIEW DATABASE STATE TO [DiagnosticUser];

-- Connect permission
GRANT CONNECT SQL TO [DiagnosticUser];
```

For read-only mode (basic diagnostics):
```sql
-- Only connection testing, no DMV access
GRANT CONNECT SQL TO [DiagnosticUser];
```

## Extensibility Points

### Custom Metric Collectors

Users can implement custom collectors:

```csharp
public class CustomBusinessMetricCollector : IMetricCollector<CustomMetrics>
{
    public async Task<CustomMetrics> CollectAsync(SqlConnection connection)
    {
        // Custom logic
    }
}

// Register custom collector
diagnosticsClient.RegisterCollector<CustomMetrics>(
    new CustomBusinessMetricCollector());
```

### Custom Recommendations

Implement recommendation rules:

```csharp
public interface IRecommendationRule
{
    bool Applies(DiagnosticReport report);
    Recommendation Generate(DiagnosticReport report);
}

public class CustomConnectionPoolRule : IRecommendationRule
{
    public bool Applies(DiagnosticReport report)
    {
        return report.Connection.PoolExhaustionEvents > 5;
    }
    
    public Recommendation Generate(DiagnosticReport report)
    {
        return new Recommendation
        {
            Severity = RecommendationSeverity.Critical,
            Category = "Connection Pooling",
            Issue = $"Pool exhausted {report.Connection.PoolExhaustionEvents} times",
            Recommendation = "Increase Max Pool Size or reduce connection lifetime"
        };
    }
}
```

## Dependencies

### Required NuGet Packages

```xml
<!-- Core functionality -->
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.1.*" />
<PackageReference Include="System.Reactive" Version="6.0.*" />
<PackageReference Include="Polly" Version="8.0.*" />

<!-- CLI tool -->
<PackageReference Include="Spectre.Console" Version="0.48.*" />

<!-- Testing -->
<PackageReference Include="xUnit" Version="2.6.*" />
<PackageReference Include="Moq" Version="4.20.*" />
<PackageReference Include="FluentAssertions" Version="6.12.*" />
```

### Target Frameworks

```xml
<TargetFrameworks>net472;net6.0;net8.0</TargetFrameworks>
```

Multi-targeting to support legacy .NET Framework applications while enabling modern .NET features.
