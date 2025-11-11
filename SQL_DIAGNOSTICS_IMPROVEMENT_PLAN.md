# SQL Diagnostics Toolkit - Improvement Plan
## Modern Client-Side Monitoring for Legacy Monolithic Applications

**Version:** 2.0 Planning Document  
**Target Environment:** .NET 6/8/10 + SQL Server 2016+  
**Focus:** Client-side diagnostics with comprehensive logging  
**Date:** November 2025

---

## Executive Summary

This document outlines improvements to the SQL Diagnostics Toolkit specifically tailored for monitoring legacy monolithic Windows applications with heavy `DataSet` usage, where:

- **Deployment constraint**: All diagnostics must run client-side (no server-side components in production)
- **Target users**: Development team and customer support analyzing logs from customer sites
- **Primary goals**: 
  1. Identify root causes of "application hanging" during data retrieval
  2. Enable comprehensive logging that customers can send back for analysis
  3. Support both active debugging (dev environment) and passive monitoring (production)

### Implementation Status – November 2025

| Initiative | Status | Notes |
| --- | --- | --- |
| DataSet-specific diagnostics module | ✅ Delivered | `DataSetDiagnostics`, instrumentation wrapper, anti-pattern analysis |
| Structured diagnostic logging & packaging | ✅ Delivered | `DiagnosticLogger` with JSON/rolling sinks + zip packaging helper |
| Automated baselines & regression detection | ✅ Delivered | `BaselineManager`, `RegressionReport`, CLI `baseline` commands |
| Quick triage workflow | ✅ Delivered | `QuickTriage` probes + CLI `triage` command |
| Connection pool health monitoring | ✅ Delivered | `ConnectionPoolMonitor`, health summary models, tests |
| CLI enhancements | ✅ Delivered | `triage`, `baseline capture/compare`, `dataset-test`, `package` |
| Developer integration & NuGet packaging | ✅ Delivered | `SqlDiagnosticsIntegration` helpers, sample app refresh, README/Quick Start |
| Remaining roadmap | ▶️ Planned | Alerting, historical dashboards, anomaly detection (see Post-MVP backlog) |

---

## Problem Statement

### Current Challenges
1. **"Hanging" perception**: Users report the application feels unresponsive during data operations
2. **Multiple failure modes**: Connection issues, slow queries, network problems, blocking, pool exhaustion
3. **Delayed diagnosis**: Issues are reported days/weeks after occurrence without diagnostic data
4. **DataSet-heavy architecture**: Existing toolkit doesn't address `SqlDataAdapter` and `DataSet` performance patterns
5. **Remote debugging**: Cannot easily reproduce customer issues in dev environment

### Success Criteria
- Reduce time-to-diagnosis from hours/days to minutes
- Capture 90%+ of performance issues in logs automatically
- Enable customers to provide actionable diagnostic data
- Zero production code changes required for monitoring
- Minimal performance overhead (<2% in production)

---

## Core Improvements

### 1. DataSet-Specific Diagnostics Module

**Priority**: CRITICAL  
**Effort**: Medium (2-3 weeks)

#### Rationale
Your application uses `DataSet`/`DataAdapter` extensively, but the current toolkit focuses on `SqlConnection`/`SqlCommand`. This leaves a gap in the most relevant diagnostics.

#### Implementation

```csharp
namespace SqlDiagnostics.Core.Diagnostics
{
    /// <summary>
    /// Specialized diagnostics for DataSet/DataAdapter operations
    /// </summary>
    public class DataSetDiagnostics
    {
        /// <summary>
        /// Measures DataAdapter.Fill() performance with detailed breakdown
        /// </summary>
        public async Task<DataSetFillMetrics> MeasureFillAsync(
            SqlDataAdapter adapter,
            DataSet dataSet,
            string operationName = null)
        {
            var metrics = new DataSetFillMetrics 
            { 
                OperationName = operationName,
                StartTime = DateTime.UtcNow 
            };
            
            var sw = Stopwatch.StartNew();
            
            // Enable statistics on the connection
            adapter.SelectCommand.Connection.StatisticsEnabled = true;
            adapter.SelectCommand.Connection.ResetStatistics();
            
            try
            {
                var rowCount = await Task.Run(() => adapter.Fill(dataSet));
                sw.Stop();
                
                var stats = adapter.SelectCommand.Connection.RetrieveStatistics();
                
                metrics.Duration = sw.Elapsed;
                metrics.RowCount = rowCount;
                metrics.TableCount = dataSet.Tables.Count;
                metrics.NetworkRoundtrips = (long)stats["ServerRoundtrips"];
                metrics.BytesReceived = (long)stats["BytesReceived"];
                metrics.ExecutionTime = TimeSpan.FromMilliseconds((long)stats["ExecutionTime"]);
                metrics.NetworkTime = TimeSpan.FromMilliseconds((long)stats["NetworkServerTime"]);
                
                // Memory analysis
                using (var ms = new MemoryStream())
                {
                    dataSet.WriteXml(ms);
                    metrics.DataSetSizeBytes = ms.Length;
                }
                
                // Analyze DataSet structure
                metrics.Analysis = AnalyzeDataSetStructure(dataSet);
                
                return metrics;
            }
            catch (Exception ex)
            {
                metrics.Error = ex;
                metrics.Duration = sw.Elapsed;
                return metrics;
            }
        }
        
        /// <summary>
        /// Detects common DataSet anti-patterns
        /// </summary>
        private DataSetAnalysis AnalyzeDataSetStructure(DataSet dataSet)
        {
            var analysis = new DataSetAnalysis();
            
            foreach (DataTable table in dataSet.Tables)
            {
                var tableIssues = new List<string>();
                
                // Missing primary key
                if (table.PrimaryKey == null || table.PrimaryKey.Length == 0)
                {
                    tableIssues.Add($"No primary key defined - lookups will be O(n)");
                }
                
                // Large row count without paging
                if (table.Rows.Count > 10000)
                {
                    tableIssues.Add($"{table.Rows.Count} rows loaded - consider paging");
                }
                
                // Missing relationships
                if (dataSet.Tables.Count > 1 && dataSet.Relations.Count == 0)
                {
                    tableIssues.Add("Multiple tables without relationships - may cause client-side joins");
                }
                
                // Untyped DataSet
                if (dataSet.GetType() == typeof(DataSet))
                {
                    tableIssues.Add("Untyped DataSet - typed DataSets provide better performance");
                }
                
                if (tableIssues.Any())
                {
                    analysis.TableIssues[table.TableName] = tableIssues;
                }
            }
            
            return analysis;
        }
        
        /// <summary>
        /// Wraps existing DataAdapter for automatic diagnostics
        /// </summary>
        public InstrumentedDataAdapter Instrument(SqlDataAdapter adapter)
        {
            return new InstrumentedDataAdapter(adapter, this);
        }
    }
    
    public class DataSetFillMetrics
    {
        public string OperationName { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public TimeSpan NetworkTime { get; set; }
        public int RowCount { get; set; }
        public int TableCount { get; set; }
        public long NetworkRoundtrips { get; set; }
        public long BytesReceived { get; set; }
        public long DataSetSizeBytes { get; set; }
        public DataSetAnalysis Analysis { get; set; }
        public Exception Error { get; set; }
        
        public TimeSpan ClientProcessingTime => Duration - ExecutionTime - NetworkTime;
        
        public string GetPerformanceSummary()
        {
            if (Error != null)
                return $"Failed: {Error.Message}";
                
            var sb = new StringBuilder();
            sb.AppendLine($"Operation: {OperationName ?? "Unknown"}");
            sb.AppendLine($"Total Duration: {Duration.TotalMilliseconds:F0}ms");
            sb.AppendLine($"  - Network Time: {NetworkTime.TotalMilliseconds:F0}ms ({NetworkTime.TotalMilliseconds / Duration.TotalMilliseconds:P0})");
            sb.AppendLine($"  - SQL Execution: {ExecutionTime.TotalMilliseconds:F0}ms ({ExecutionTime.TotalMilliseconds / Duration.TotalMilliseconds:P0})");
            sb.AppendLine($"  - Client Processing: {ClientProcessingTime.TotalMilliseconds:F0}ms ({ClientProcessingTime.TotalMilliseconds / Duration.TotalMilliseconds:P0})");
            sb.AppendLine($"Data: {RowCount} rows, {DataSetSizeBytes / 1024:N0} KB, {NetworkRoundtrips} roundtrips");
            
            if (Analysis?.TableIssues.Any() == true)
            {
                sb.AppendLine("Warnings:");
                foreach (var kvp in Analysis.TableIssues)
                {
                    sb.AppendLine($"  {kvp.Key}: {string.Join(", ", kvp.Value)}");
                }
            }
            
            return sb.ToString();
        }
    }
    
    public class DataSetAnalysis
    {
        public Dictionary<string, List<string>> TableIssues { get; set; } = new();
    }
    
    /// <summary>
    /// Drop-in replacement for SqlDataAdapter that automatically collects metrics
    /// </summary>
    public class InstrumentedDataAdapter : SqlDataAdapter
    {
        private readonly SqlDataAdapter _inner;
        private readonly DataSetDiagnostics _diagnostics;
        
        public InstrumentedDataAdapter(SqlDataAdapter inner, DataSetDiagnostics diagnostics)
        {
            _inner = inner;
            _diagnostics = diagnostics;
        }
        
        public DataSetFillMetrics LastFillMetrics { get; private set; }
        
        public new int Fill(DataSet dataSet)
        {
            LastFillMetrics = _diagnostics.MeasureFillAsync(_inner, dataSet).GetAwaiter().GetResult();
            return LastFillMetrics.RowCount;
        }
    }
}
```

#### Integration Pattern

```csharp
// In your application code (minimal changes):
var adapter = new SqlDataAdapter(query, connection);
var diagnostics = new DataSetDiagnostics();
var instrumentedAdapter = diagnostics.Instrument(adapter);

var ds = new DataSet();
instrumentedAdapter.Fill(ds);

// Log metrics automatically
_logger.LogInformation(instrumentedAdapter.LastFillMetrics.GetPerformanceSummary());
```

---

### 2. Structured Diagnostic Logging System

**Priority**: CRITICAL  
**Effort**: Medium (2 weeks)

#### Rationale
Currently, diagnostics produce various report formats, but there's no standardized logging that can be:
1. Automatically captured in production
2. Sent by customers for analysis
3. Aggregated and analyzed programmatically

#### Implementation

```csharp
namespace SqlDiagnostics.Core.Logging
{
    /// <summary>
    /// Centralized logging system for all diagnostic events
    /// </summary>
    public class DiagnosticLogger : IDisposable
    {
        private readonly string _logDirectory;
        private readonly List<IDiagnosticSink> _sinks;
        private readonly ConcurrentQueue<DiagnosticEvent> _eventQueue;
        private readonly CancellationTokenSource _cts;
        
        public DiagnosticLogger(DiagnosticLoggerOptions options)
        {
            _logDirectory = options.LogDirectory 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "SqlDiagnostics", "Logs");
            
            Directory.CreateDirectory(_logDirectory);
            
            _sinks = new List<IDiagnosticSink>
            {
                new JsonFileSink(_logDirectory),
                new RollingLogSink(_logDirectory, maxFileSizeMB: 10, maxFiles: 50),
            };
            
            if (options.EnableConsoleOutput)
                _sinks.Add(new ConsoleSink());
            
            if (!string.IsNullOrEmpty(options.SqliteDbPath))
                _sinks.Add(new SqliteSink(options.SqliteDbPath));
            
            _eventQueue = new ConcurrentQueue<DiagnosticEvent>();
            _cts = new CancellationTokenSource();
            
            // Background writer
            Task.Run(() => ProcessQueueAsync(_cts.Token));
        }
        
        public void LogConnectionAttempt(ConnectionMetrics metrics)
        {
            var evt = new DiagnosticEvent
            {
                EventType = DiagnosticEventType.ConnectionAttempt,
                Timestamp = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                ApplicationName = Process.GetCurrentProcess().ProcessName,
                Data = metrics,
                Severity = metrics.SuccessRate < 0.9 ? EventSeverity.Warning : EventSeverity.Info
            };
            
            EnqueueEvent(evt);
        }
        
        public void LogDataSetFill(DataSetFillMetrics metrics)
        {
            var severity = EventSeverity.Info;
            if (metrics.Error != null)
                severity = EventSeverity.Error;
            else if (metrics.Duration > TimeSpan.FromSeconds(10))
                severity = EventSeverity.Warning;
            else if (metrics.Analysis?.TableIssues.Any() == true)
                severity = EventSeverity.Warning;
            
            var evt = new DiagnosticEvent
            {
                EventType = DiagnosticEventType.DataSetOperation,
                Timestamp = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                ApplicationName = Process.GetCurrentProcess().ProcessName,
                Data = metrics,
                Severity = severity,
                Message = metrics.GetPerformanceSummary()
            };
            
            EnqueueEvent(evt);
        }
        
        public void LogSlowOperation(string operationType, TimeSpan duration, string details)
        {
            var evt = new DiagnosticEvent
            {
                EventType = DiagnosticEventType.SlowOperation,
                Timestamp = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                ApplicationName = Process.GetCurrentProcess().ProcessName,
                Severity = EventSeverity.Warning,
                Message = $"{operationType} took {duration.TotalSeconds:F2}s",
                Data = new { OperationType = operationType, Duration = duration, Details = details }
            };
            
            EnqueueEvent(evt);
        }
        
        private void EnqueueEvent(DiagnosticEvent evt)
        {
            _eventQueue.Enqueue(evt);
        }
        
        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var batch = new List<DiagnosticEvent>();
                    while (batch.Count < 100 && _eventQueue.TryDequeue(out var evt))
                    {
                        batch.Add(evt);
                    }
                    
                    if (batch.Any())
                    {
                        foreach (var sink in _sinks)
                        {
                            await sink.WriteAsync(batch, cancellationToken);
                        }
                    }
                    
                    await Task.Delay(1000, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log to Windows Event Log as fallback
                    try
                    {
                        EventLog.WriteEntry("SqlDiagnostics", 
                            $"Error in diagnostic logging: {ex.Message}", 
                            EventLogEntryType.Warning);
                    }
                    catch { /* Best effort */ }
                }
            }
        }
        
        /// <summary>
        /// Creates a diagnostic package that customers can send
        /// </summary>
        public async Task<string> CreateDiagnosticPackageAsync(
            DateTime? fromDate = null,
            string outputPath = null)
        {
            fromDate ??= DateTime.UtcNow.AddDays(-7);
            outputPath ??= Path.Combine(_logDirectory, 
                $"diagnostic-package-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
            
            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            
            // Include all log files from date range
            var logFiles = Directory.GetFiles(_logDirectory, "*.jsonl")
                .Where(f => File.GetCreationTime(f) >= fromDate);
            
            foreach (var file in logFiles)
            {
                archive.CreateEntryFromFile(file, Path.GetFileName(file));
            }
            
            // Include system info
            var systemInfo = new
            {
                MachineName = Environment.MachineName,
                OSVersion = Environment.OSVersion.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                DotNetVersion = Environment.Version.ToString(),
                Is64BitOS = Environment.Is64BitOperatingSystem,
                PackageCreated = DateTime.UtcNow
            };
            
            var systemInfoEntry = archive.CreateEntry("system-info.json");
            await using var writer = new StreamWriter(systemInfoEntry.Open());
            await writer.WriteAsync(JsonSerializer.Serialize(systemInfo, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            }));
            
            return outputPath;
        }
        
        public void Dispose()
        {
            _cts.Cancel();
            Thread.Sleep(2000); // Allow queue to flush
            
            foreach (var sink in _sinks)
            {
                if (sink is IDisposable disposable)
                    disposable.Dispose();
            }
            
            _cts.Dispose();
        }
    }
    
    public class DiagnosticEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public DiagnosticEventType EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public string MachineName { get; set; }
        public string ApplicationName { get; set; }
        public EventSeverity Severity { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
    }
    
    public enum DiagnosticEventType
    {
        ConnectionAttempt,
        DataSetOperation,
        QueryExecution,
        SlowOperation,
        NetworkIssue,
        ServerHealth,
        BlockingDetected,
        PoolExhaustion
    }
    
    public enum EventSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
    
    public interface IDiagnosticSink
    {
        Task WriteAsync(IEnumerable<DiagnosticEvent> events, CancellationToken cancellationToken);
    }
    
    /// <summary>
    /// Writes events as newline-delimited JSON (easy to parse/grep)
    /// </summary>
    public class JsonFileSink : IDiagnosticSink
    {
        private readonly string _directory;
        
        public JsonFileSink(string directory)
        {
            _directory = directory;
        }
        
        public async Task WriteAsync(IEnumerable<DiagnosticEvent> events, CancellationToken cancellationToken)
        {
            var filename = $"diagnostics-{DateTime.UtcNow:yyyy-MM-dd}.jsonl";
            var path = Path.Combine(_directory, filename);
            
            var lines = events.Select(e => JsonSerializer.Serialize(e));
            await File.AppendAllLinesAsync(path, lines, cancellationToken);
        }
    }
    
    /// <summary>
    /// SQLite sink for queryable diagnostics
    /// </summary>
    public class SqliteSink : IDiagnosticSink, IDisposable
    {
        private readonly string _dbPath;
        private readonly SemaphoreSlim _lock = new(1);
        
        public SqliteSink(string dbPath)
        {
            _dbPath = dbPath;
            InitializeDatabase();
        }
        
        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            
            var createTable = @"
                CREATE TABLE IF NOT EXISTS DiagnosticEvents (
                    EventId TEXT PRIMARY KEY,
                    EventType TEXT NOT NULL,
                    Timestamp TEXT NOT NULL,
                    MachineName TEXT,
                    ApplicationName TEXT,
                    Severity TEXT,
                    Message TEXT,
                    DataJson TEXT
                );
                
                CREATE INDEX IF NOT EXISTS idx_timestamp ON DiagnosticEvents(Timestamp);
                CREATE INDEX IF NOT EXISTS idx_severity ON DiagnosticEvents(Severity);
            ";
            
            using var cmd = new SqliteCommand(createTable, connection);
            cmd.ExecuteNonQuery();
        }
        
        public async Task WriteAsync(IEnumerable<DiagnosticEvent> events, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync(cancellationToken);
                
                using var transaction = connection.BeginTransaction();
                
                foreach (var evt in events)
                {
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO DiagnosticEvents 
                        (EventId, EventType, Timestamp, MachineName, ApplicationName, Severity, Message, DataJson)
                        VALUES (@EventId, @EventType, @Timestamp, @MachineName, @ApplicationName, @Severity, @Message, @DataJson)
                    ";
                    
                    cmd.Parameters.AddWithValue("@EventId", evt.EventId.ToString());
                    cmd.Parameters.AddWithValue("@EventType", evt.EventType.ToString());
                    cmd.Parameters.AddWithValue("@Timestamp", evt.Timestamp.ToString("O"));
                    cmd.Parameters.AddWithValue("@MachineName", evt.MachineName ?? "");
                    cmd.Parameters.AddWithValue("@ApplicationName", evt.ApplicationName ?? "");
                    cmd.Parameters.AddWithValue("@Severity", evt.Severity.ToString());
                    cmd.Parameters.AddWithValue("@Message", evt.Message ?? "");
                    cmd.Parameters.AddWithValue("@DataJson", JsonSerializer.Serialize(evt.Data));
                    
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }
        
        public void Dispose()
        {
            _lock.Dispose();
        }
    }
}
```

---

### 3. Automated Performance Baseline System

**Priority**: HIGH  
**Effort**: Medium (2 weeks)

#### Rationale
"It used to be faster" is a common complaint. Without baselines, you can't objectively measure regression.

#### Implementation

```csharp
namespace SqlDiagnostics.Core.Baseline
{
    /// <summary>
    /// Manages performance baselines for regression detection
    /// </summary>
    public class BaselineManager
    {
        private readonly string _storageDirectory;
        
        public BaselineManager(string storageDirectory = null)
        {
            _storageDirectory = storageDirectory 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "SqlDiagnostics", "Baselines");
            Directory.CreateDirectory(_storageDirectory);
        }
        
        /// <summary>
        /// Captures a performance baseline (run during known-good state)
        /// </summary>
        public async Task<PerformanceBaseline> CaptureBaselineAsync(
            string connectionString,
            string baselineName,
            BaselineOptions options = null)
        {
            options ??= BaselineOptions.Default;
            
            var baseline = new PerformanceBaseline
            {
                Name = baselineName,
                CaptureDate = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                ConnectionStringHash = ComputeHash(connectionString)
            };
            
            using var client = new SqlDiagnosticsClient();
            
            // Multiple samples for statistical accuracy
            var samples = new List<DiagnosticReport>();
            for (int i = 0; i < options.SampleCount; i++)
            {
                var report = await client.RunQuickCheckAsync(connectionString);
                samples.Add(report);
                
                if (i < options.SampleCount - 1)
                    await Task.Delay(options.SampleInterval);
            }
            
            // Calculate percentiles
            baseline.Connection = new ConnectionBaseline
            {
                P50 = Percentile(samples.Select(s => s.Connection.AverageConnectionTime.TotalMilliseconds), 0.5),
                P95 = Percentile(samples.Select(s => s.Connection.AverageConnectionTime.TotalMilliseconds), 0.95),
                P99 = Percentile(samples.Select(s => s.Connection.AverageConnectionTime.TotalMilliseconds), 0.99),
                SuccessRate = samples.Average(s => s.Connection.SuccessRate)
            };
            
            baseline.Network = new NetworkBaseline
            {
                P50 = Percentile(samples.Select(s => s.Network.AverageLatency.TotalMilliseconds), 0.5),
                P95 = Percentile(samples.Select(s => s.Network.AverageLatency.TotalMilliseconds), 0.95),
                P99 = Percentile(samples.Select(s => s.Network.AverageLatency.TotalMilliseconds), 0.99)
            };
            
            // Save baseline
            var filename = $"{SanitizeFilename(baselineName)}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            var path = Path.Combine(_storageDirectory, filename);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(baseline, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            
            return baseline;
        }
        
        /// <summary>
        /// Compares current performance against baseline
        /// </summary>
        public async Task<RegressionReport> CompareToBaselineAsync(
            string connectionString,
            string baselineName = null)
        {
            var baseline = baselineName != null 
                ? await LoadBaselineAsync(baselineName)
                : await LoadLatestBaselineAsync(connectionString);
            
            if (baseline == null)
            {
                return new RegressionReport
                {
                    Success = false,
                    Message = "No baseline found. Capture a baseline first using CaptureBaselineAsync()."
                };
            }
            
            using var client = new SqlDiagnosticsClient();
            var current = await client.RunQuickCheckAsync(connectionString);
            
            var report = new RegressionReport
            {
                Success = true,
                BaselineName = baseline.Name,
                BaselineDate = baseline.CaptureDate,
                ComparisonDate = DateTime.UtcNow
            };
            
            // Connection regression
            var currentConnTime = current.Connection.AverageConnectionTime.TotalMilliseconds;
            if (currentConnTime > baseline.Connection.P95 * 1.2) // 20% worse than P95
            {
                report.Regressions.Add(new Regression
                {
                    Category = "Connection",
                    Severity = RegressionSeverity.High,
                    Current = $"{currentConnTime:F0}ms",
                    Baseline = $"{baseline.Connection.P95:F0}ms (P95)",
                    PercentChange = ((currentConnTime - baseline.Connection.P95) / baseline.Connection.P95) * 100,
                    Message = "Connection time significantly worse than baseline"
                });
            }
            
            // Network regression
            var currentLatency = current.Network.AverageLatency.TotalMilliseconds;
            if (currentLatency > baseline.Network.P95 * 1.5) // 50% worse than P95
            {
                report.Regressions.Add(new Regression
                {
                    Category = "Network",
                    Severity = RegressionSeverity.High,
                    Current = $"{currentLatency:F0}ms",
                    Baseline = $"{baseline.Network.P95:F0}ms (P95)",
                    PercentChange = ((currentLatency - baseline.Network.P95) / baseline.Network.P95) * 100,
                    Message = "Network latency significantly worse than baseline"
                });
            }
            
            // Success rate regression
            if (current.Connection.SuccessRate < baseline.Connection.SuccessRate - 0.1)
            {
                report.Regressions.Add(new Regression
                {
                    Category = "Reliability",
                    Severity = RegressionSeverity.Critical,
                    Current = $"{current.Connection.SuccessRate:P1}",
                    Baseline = $"{baseline.Connection.SuccessRate:P1}",
                    PercentChange = ((current.Connection.SuccessRate - baseline.Connection.SuccessRate) / baseline.Connection.SuccessRate) * 100,
                    Message = "Connection success rate has degraded"
                });
            }
            
            return report;
        }
        
        private async Task<PerformanceBaseline> LoadBaselineAsync(string name)
        {
            var files = Directory.GetFiles(_storageDirectory, $"{SanitizeFilename(name)}-*.json");
            if (!files.Any()) return null;
            
            var latest = files.OrderByDescending(f => f).First();
            var json = await File.ReadAllTextAsync(latest);
            return JsonSerializer.Deserialize<PerformanceBaseline>(json);
        }
        
        private async Task<PerformanceBaseline> LoadLatestBaselineAsync(string connectionString)
        {
            var hash = ComputeHash(connectionString);
            var allFiles = Directory.GetFiles(_storageDirectory, "*.json");
            
            PerformanceBaseline latestBaseline = null;
            foreach (var file in allFiles.OrderByDescending(f => f))
            {
                var json = await File.ReadAllTextAsync(file);
                var baseline = JsonSerializer.Deserialize<PerformanceBaseline>(json);
                
                if (baseline.ConnectionStringHash == hash)
                {
                    latestBaseline = baseline;
                    break;
                }
            }
            
            return latestBaseline;
        }
        
        private static double Percentile(IEnumerable<double> values, double percentile)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Length - 1))];
        }
        
        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes).Substring(0, 16);
        }
        
        private static string SanitizeFilename(string filename)
        {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }
    }
    
    public class PerformanceBaseline
    {
        public string Name { get; set; }
        public DateTime CaptureDate { get; set; }
        public string MachineName { get; set; }
        public string ConnectionStringHash { get; set; }
        public ConnectionBaseline Connection { get; set; }
        public NetworkBaseline Network { get; set; }
    }
    
    public class ConnectionBaseline
    {
        public double P50 { get; set; } // median
        public double P95 { get; set; }
        public double P99 { get; set; }
        public double SuccessRate { get; set; }
    }
    
    public class NetworkBaseline
    {
        public double P50 { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }
    }
    
    public class RegressionReport
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string BaselineName { get; set; }
        public DateTime BaselineDate { get; set; }
        public DateTime ComparisonDate { get; set; }
        public List<Regression> Regressions { get; set; } = new();
        
        public bool HasRegressions => Regressions.Any();
        public bool HasCriticalRegressions => Regressions.Any(r => r.Severity == RegressionSeverity.Critical);
    }
    
    public class Regression
    {
        public string Category { get; set; }
        public RegressionSeverity Severity { get; set; }
        public string Current { get; set; }
        public string Baseline { get; set; }
        public double PercentChange { get; set; }
        public string Message { get; set; }
    }
    
    public enum RegressionSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
    
    public class BaselineOptions
    {
        public int SampleCount { get; set; } = 10;
        public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(5);
        
        public static BaselineOptions Default => new();
    }
}
```

---

### 4. Quick Triage System

**Priority**: HIGH  
**Effort**: Small (3-4 days)

#### Rationale
When customers report issues, you need to quickly determine: Is it network? SQL Server? The query? This provides a 30-second diagnostic.

#### Implementation

```csharp
namespace SqlDiagnostics.Core.Triage
{
    /// <summary>
    /// Rapid diagnostic triage for identifying root cause category
    /// </summary>
    public static class QuickTriage
    {
        /// <summary>
        /// Runs a 30-second triage to identify the likely problem area
        /// </summary>
        public static async Task<TriageResult> RunAsync(
            string connectionString,
            IProgress<string> progress = null)
        {
            var result = new TriageResult
            {
                StartTime = DateTime.UtcNow
            };
            
            var sw = Stopwatch.StartNew();
            
            try
            {
                // Parse connection string
                var builder = new SqlConnectionStringBuilder(connectionString);
                var server = builder.DataSource;
                
                // Test 1: Network Connectivity (5 seconds)
                progress?.Report("Testing network connectivity...");
                result.NetworkTest = await TestNetworkAsync(server);
                
                // Test 2: Connection Establishment (5 seconds)
                progress?.Report("Testing connection establishment...");
                result.ConnectionTest = await TestConnectionAsync(connectionString);
                
                // Test 3: Trivial Query (5 seconds)
                progress?.Report("Testing SQL execution...");
                result.QueryTest = await TestQueryExecutionAsync(connectionString);
                
                // Test 4: Server Health Check (5 seconds)
                progress?.Report("Checking server health...");
                result.ServerTest = await TestServerHealthAsync(connectionString);
                
                // Test 5: Blocking Detection (5 seconds)
                progress?.Report("Checking for blocking...");
                result.BlockingTest = await TestBlockingAsync(connectionString);
                
                sw.Stop();
                result.Duration = sw.Elapsed;
                
                // Determine root cause
                result.Diagnosis = DetermineRootCause(result);
                
                progress?.Report($"Triage complete: {result.Diagnosis.Category}");
            }
            catch (Exception ex)
            {
                result.Diagnosis = new Diagnosis
                {
                    Category = "Error",
                    Confidence = 1.0,
                    Summary = $"Triage failed: {ex.Message}",
                    Details = ex.ToString()
                };
            }
            
            return result;
        }
        
        private static async Task<TestResult> TestNetworkAsync(string server)
        {
            var result = new TestResult { TestName = "Network" };
            
            try
            {
                // Extract hostname
                var host = server.Split(',')[0];
                
                using var ping = new Ping();
                var attempts = 5;
                var successful = 0;
                var latencies = new List<long>();
                
                for (int i = 0; i < attempts; i++)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(host, 2000);
                        if (reply.Status == IPStatus.Success)
                        {
                            successful++;
                            latencies.Add(reply.RoundtripTime);
                        }
                    }
                    catch { }
                    
                    await Task.Delay(200);
                }
                
                result.Success = successful > 0;
                result.Duration = latencies.Any() 
                    ? TimeSpan.FromMilliseconds(latencies.Average()) 
                    : TimeSpan.Zero;
                result.Details = $"{successful}/{attempts} pings succeeded, avg latency: {latencies.DefaultIfEmpty(0).Average():F0}ms";
                
                if (successful == 0)
                {
                    result.Issues.Add("Cannot reach server via ICMP - network issue or firewall blocking");
                }
                else if (latencies.Average() > 100)
                {
                    result.Issues.Add($"High network latency: {latencies.Average():F0}ms");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Details = ex.Message;
            }
            
            return result;
        }
        
        private static async Task<TestResult> TestConnectionAsync(string connectionString)
        {
            var result = new TestResult { TestName = "Connection" };
            
            try
            {
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                sw.Stop();
                
                result.Success = true;
                result.Duration = sw.Elapsed;
                result.Details = $"Connected in {sw.ElapsedMilliseconds}ms";
                
                if (sw.ElapsedMilliseconds > 1000)
                {
                    result.Issues.Add($"Slow connection establishment: {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (SqlException ex)
            {
                result.Success = false;
                result.Details = $"SQL Error {ex.Number}: {ex.Message}";
                result.Issues.Add(ex.Number switch
                {
                    -1 => "Connection timeout - server may be overloaded or network issue",
                    18456 => "Authentication failed - check credentials",
                    4060 => "Database not found or not accessible",
                    _ => $"SQL Error {ex.Number}"
                });
            }
            
            return result;
        }
        
        private static async Task<TestResult> TestQueryExecutionAsync(string connectionString)
        {
            var result = new TestResult { TestName = "Query Execution" };
            
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                
                connection.StatisticsEnabled = true;
                
                var sw = Stopwatch.StartNew();
                using var cmd = new SqlCommand("SELECT @@VERSION", connection);
                await cmd.ExecuteScalarAsync();
                sw.Stop();
                
                var stats = connection.RetrieveStatistics();
                
                result.Success = true;
                result.Duration = sw.Elapsed;
                result.Details = $"Query executed in {sw.ElapsedMilliseconds}ms " +
                                $"(SQL: {stats["ExecutionTime"]}ms, Network: {stats["NetworkServerTime"]}ms)";
                
                if (sw.ElapsedMilliseconds > 500)
                {
                    result.Issues.Add($"Slow query execution: {sw.ElapsedMilliseconds}ms for trivial query");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Details = ex.Message;
            }
            
            return result;
        }
        
        private static async Task<TestResult> TestServerHealthAsync(string connectionString)
        {
            var result = new TestResult { TestName = "Server Health" };
            
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                
                // Check CPU
                var cpuQuery = @"
                    SELECT TOP 1 
                        SQLProcessUtilization AS SqlCpu
                    FROM (
                        SELECT 
                            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS SQLProcessUtilization
                        FROM (
                            SELECT CONVERT(XML, record) AS record
                            FROM sys.dm_os_ring_buffers
                            WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
                        ) AS x
                    ) AS y
                    ORDER BY SQLProcessUtilization DESC";
                
                using var cmd = new SqlCommand(cpuQuery, connection);
                var cpuUsage = (int?)await cmd.ExecuteScalarAsync();
                
                result.Success = true;
                result.Details = $"SQL Server CPU: {cpuUsage ?? 0}%";
                
                if (cpuUsage > 80)
                {
                    result.Issues.Add($"High SQL Server CPU usage: {cpuUsage}%");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Details = ex.Message;
            }
            
            return result;
        }
        
        private static async Task<TestResult> TestBlockingAsync(string connectionString)
        {
            var result = new TestResult { TestName = "Blocking Detection" };
            
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                
                var blockingQuery = @"
                    SELECT COUNT(*) 
                    FROM sys.dm_exec_requests 
                    WHERE blocking_session_id != 0";
                
                using var cmd = new SqlCommand(blockingQuery, connection);
                var blockedCount = (int)await cmd.ExecuteScalarAsync();
                
                result.Success = true;
                result.Details = $"{blockedCount} blocked sessions detected";
                
                if (blockedCount > 0)
                {
                    result.Issues.Add($"{blockedCount} sessions are currently blocked");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Details = ex.Message;
            }
            
            return result;
        }
        
        private static Diagnosis DetermineRootCause(TriageResult triage)
        {
            var diagnosis = new Diagnosis();
            
            // Network issues
            if (!triage.NetworkTest.Success || triage.NetworkTest.Issues.Any(i => i.Contains("latency")))
            {
                diagnosis.Category = "Network";
                diagnosis.Confidence = 0.9;
                diagnosis.Summary = "Network connectivity or latency issues detected";
                diagnosis.Details = triage.NetworkTest.Details;
                diagnosis.Recommendations.Add("Check network connectivity to SQL Server");
                diagnosis.Recommendations.Add("Verify firewall rules and VPN connection");
                diagnosis.Recommendations.Add("Run traceroute to identify network hops");
                return diagnosis;
            }
            
            // Connection issues
            if (!triage.ConnectionTest.Success)
            {
                diagnosis.Category = "Connection";
                diagnosis.Confidence = 0.95;
                diagnosis.Summary = "Cannot establish SQL Server connection";
                diagnosis.Details = triage.ConnectionTest.Details;
                diagnosis.Recommendations.Add("Verify connection string");
                diagnosis.Recommendations.Add("Check SQL Server service is running");
                diagnosis.Recommendations.Add("Verify authentication credentials");
                return diagnosis;
            }
            
            // Blocking
            if (triage.BlockingTest.Issues.Any(i => i.Contains("blocked")))
            {
                diagnosis.Category = "Blocking";
                diagnosis.Confidence = 0.85;
                diagnosis.Summary = "Active blocking detected on SQL Server";
                diagnosis.Details = triage.BlockingTest.Details;
                diagnosis.Recommendations.Add("Identify blocking session using sp_who2 or sp_whoisactive");
                diagnosis.Recommendations.Add("Consider killing blocking session if appropriate");
                diagnosis.Recommendations.Add("Review query patterns for long-running transactions");
                return diagnosis;
            }
            
            // Server performance
            if (triage.ServerTest.Issues.Any(i => i.Contains("CPU")))
            {
                diagnosis.Category = "Server Performance";
                diagnosis.Confidence = 0.8;
                diagnosis.Summary = "SQL Server resource constraints detected";
                diagnosis.Details = triage.ServerTest.Details;
                diagnosis.Recommendations.Add("Identify expensive queries using DMVs");
                diagnosis.Recommendations.Add("Check for missing indexes");
                diagnosis.Recommendations.Add("Consider server resource upgrade");
                return diagnosis;
            }
            
            // Slow queries
            if (triage.QueryTest.Issues.Any())
            {
                diagnosis.Category = "Query Performance";
                diagnosis.Confidence = 0.75;
                diagnosis.Summary = "Query execution is slow";
                diagnosis.Details = triage.QueryTest.Details;
                diagnosis.Recommendations.Add("Review query execution plans");
                diagnosis.Recommendations.Add("Check for missing indexes");
                diagnosis.Recommendations.Add("Verify statistics are up to date");
                return diagnosis;
            }
            
            // All tests passed
            diagnosis.Category = "Healthy";
            diagnosis.Confidence = 0.7;
            diagnosis.Summary = "No obvious issues detected";
            diagnosis.Details = "All basic tests completed successfully";
            diagnosis.Recommendations.Add("Issue may be intermittent - enable continuous monitoring");
            diagnosis.Recommendations.Add("Capture baseline for comparison");
            
            return diagnosis;
        }
    }
    
    public class TriageResult
    {
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public TestResult NetworkTest { get; set; }
        public TestResult ConnectionTest { get; set; }
        public TestResult QueryTest { get; set; }
        public TestResult ServerTest { get; set; }
        public TestResult BlockingTest { get; set; }
        public Diagnosis Diagnosis { get; set; }
        
        public string GetSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== SQL Diagnostics Triage ===");
            sb.AppendLine($"Completed in {Duration.TotalSeconds:F1}s");
            sb.AppendLine();
            sb.AppendLine($"DIAGNOSIS: {Diagnosis.Category} (confidence: {Diagnosis.Confidence:P0})");
            sb.AppendLine($"{Diagnosis.Summary}");
            sb.AppendLine();
            sb.AppendLine("Test Results:");
            sb.AppendLine($"  Network:    {GetStatusIcon(NetworkTest)} {NetworkTest.Details}");
            sb.AppendLine($"  Connection: {GetStatusIcon(ConnectionTest)} {ConnectionTest.Details}");
            sb.AppendLine($"  Query:      {GetStatusIcon(QueryTest)} {QueryTest.Details}");
            sb.AppendLine($"  Server:     {GetStatusIcon(ServerTest)} {ServerTest.Details}");
            sb.AppendLine($"  Blocking:   {GetStatusIcon(BlockingTest)} {BlockingTest.Details}");
            
            if (Diagnosis.Recommendations.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Recommendations:");
                foreach (var rec in Diagnosis.Recommendations)
                {
                    sb.AppendLine($"  - {rec}");
                }
            }
            
            return sb.ToString();
        }
        
        private string GetStatusIcon(TestResult test)
        {
            if (test == null) return "?";
            return test.Success && !test.Issues.Any() ? "✓" : "⚠";
        }
    }
    
    public class TestResult
    {
        public string TestName { get; set; }
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
        public string Details { get; set; }
        public List<string> Issues { get; set; } = new();
    }
    
    public class Diagnosis
    {
        public string Category { get; set; }
        public double Confidence { get; set; }
        public string Summary { get; set; }
        public string Details { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }
}
```

---

### 5. Connection Pool Health Monitor

**Priority**: MEDIUM  
**Effort**: Small (3-4 days)

#### Rationale
DataSet-heavy apps often leak connections. Pool exhaustion causes "hanging" but isn't obvious.

#### Implementation

```csharp
namespace SqlDiagnostics.Core.Diagnostics
{
    /// <summary>
    /// Monitors connection pool health and detects leaks/exhaustion
    /// </summary>
    public class ConnectionPoolMonitor
    {
        private readonly string _connectionString;
        private readonly PerformanceCounter _poolCounter;
        
        public ConnectionPoolMonitor(string connectionString)
        {
            _connectionString = connectionString;
            
            // Try to create performance counter (may not work on all systems)
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                var counterName = $"{builder.DataSource}_{builder.InitialCatalog}";
                
                _poolCounter = new PerformanceCounter(
                    ".NET Data Provider for SqlServer",
                    "NumberOfActiveConnectionPools",
                    counterName,
                    readOnly: true);
            }
            catch
            {
                // Performance counters may not be available
            }
        }
        
        /// <summary>
        /// Monitors pool health over time
        /// </summary>
        public async Task<PoolHealthReport> MonitorAsync(
            TimeSpan duration,
            IProgress<PoolSnapshot> progress = null)
        {
            var report = new PoolHealthReport
            {
                ConnectionString = SanitizeConnectionString(_connectionString),
                StartTime = DateTime.UtcNow,
                Duration = duration
            };
            
            var snapshots = new List<PoolSnapshot>();
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < duration)
            {
                var snapshot = await CaptureSnapshotAsync();
                snapshots.Add(snapshot);
                progress?.Report(snapshot);
                
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            
            report.Snapshots = snapshots;
            report.Analysis = AnalyzeSnapshots(snapshots);
            
            return report;
        }
        
        private async Task<PoolSnapshot> CaptureSnapshotAsync()
        {
            var snapshot = new PoolSnapshot
            {
                Timestamp = DateTime.UtcNow
            };
            
            // Test connection acquisition time
            var sw = Stopwatch.StartNew();
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                sw.Stop();
                
                snapshot.AcquisitionTime = sw.Elapsed;
                snapshot.Success = true;
                
                // Query pool info if possible
                if (_poolCounter != null)
                {
                    snapshot.PoolCount = (int)_poolCounter.NextValue();
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                snapshot.AcquisitionTime = sw.Elapsed;
                snapshot.Success = false;
                snapshot.Error = ex.Message;
            }
            
            return snapshot;
        }
        
        private PoolAnalysis AnalyzeSnapshots(List<PoolSnapshot> snapshots)
        {
            var analysis = new PoolAnalysis();
            
            // Check for increasing acquisition times (pool pressure)
            var recentAcquisitionTimes = snapshots
                .TakeLast(10)
                .Select(s => s.AcquisitionTime.TotalMilliseconds)
                .ToList();
            
            if (recentAcquisitionTimes.Average() > 1000)
            {
                analysis.Issues.Add("High connection acquisition time - possible pool exhaustion");
                analysis.Severity = PoolHealthSeverity.Warning;
            }
            
            // Check for failures
            var failureRate = snapshots.Count(s => !s.Success) / (double)snapshots.Count;
            if (failureRate > 0.1)
            {
                analysis.Issues.Add($"High failure rate: {failureRate:P0}");
                analysis.Severity = PoolHealthSeverity.Critical;
            }
            
            // Check for timeout errors
            var timeouts = snapshots.Count(s => s.Error?.Contains("timeout") == true);
            if (timeouts > 0)
            {
                analysis.Issues.Add($"{timeouts} timeout errors - likely pool exhaustion");
                analysis.Severity = PoolHealthSeverity.Critical;
                analysis.Recommendations.Add("Increase Max Pool Size in connection string");
                analysis.Recommendations.Add("Audit code for undisposed connections");
                analysis.Recommendations.Add("Consider reducing Connection Lifetime");
            }
            
            if (analysis.Issues.Count == 0)
            {
                analysis.Severity = PoolHealthSeverity.Healthy;
                analysis.Summary = "Connection pool is healthy";
            }
            else
            {
                analysis.Summary = $"{analysis.Issues.Count} pool health issues detected";
            }
            
            return analysis;
        }
        
        private string SanitizeConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            builder.Password = "***";
            return builder.ToString();
        }
    }
    
    public class PoolHealthReport
    {
        public string ConnectionString { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public List<PoolSnapshot> Snapshots { get; set; }
        public PoolAnalysis Analysis { get; set; }
    }
    
    public class PoolSnapshot
    {
        public DateTime Timestamp { get; set; }
        public TimeSpan AcquisitionTime { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public int? PoolCount { get; set; }
    }
    
    public class PoolAnalysis
    {
        public PoolHealthSeverity Severity { get; set; }
        public string Summary { get; set; }
        public List<string> Issues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }
    
    public enum PoolHealthSeverity
    {
        Healthy,
        Warning,
        Critical
    }
}
```

---

## Implementation Plan

### Phase 1: Core Diagnostics (Weeks 1-3)

**Goal**: Establish foundation for DataSet monitoring and logging

#### Week 1: DataSet Diagnostics
- [ ] Implement `DataSetDiagnostics` class
- [ ] Implement `DataSetFillMetrics` model
- [ ] Implement `InstrumentedDataAdapter`
- [ ] Add DataSet structure analysis
- [ ] Unit tests for DataSet diagnostics
- [ ] Sample application demonstrating usage

**Deliverables**:
- Working DataSet diagnostics module
- Documentation with code examples
- Sample app showing before/after metrics

#### Week 2: Diagnostic Logging System
- [ ] Implement `DiagnosticLogger` with queue-based architecture
- [ ] Implement JSON file sink
- [ ] Implement SQLite sink
- [ ] Implement rolling log file management
- [ ] Implement diagnostic package export
- [ ] Integration with existing diagnostic modules

**Deliverables**:
- Centralized logging infrastructure
- Diagnostic package format specification
- Log analysis tools/scripts

#### Week 3: Integration & Testing
- [ ] Update `SqlDiagnosticsClient` to use new logger
- [ ] Add automatic logging for all diagnostic operations
- [ ] Performance testing (ensure <2% overhead)
- [ ] Integration tests
- [ ] Documentation updates

**Deliverables**:
- Fully integrated logging system
- Performance benchmarks
- Updated API documentation

---

### Phase 2: Analysis Tools (Weeks 4-5)

**Goal**: Enable regression detection and rapid triage

#### Week 4: Baseline & Triage
- [ ] Implement `BaselineManager`
- [ ] Implement baseline capture with statistical sampling
- [ ] Implement regression detection
- [ ] Implement `QuickTriage` system
- [ ] Unit tests for baseline and triage

**Deliverables**:
- Baseline capture/compare functionality
- 30-second triage tool
- CLI commands for baseline management

#### Week 5: Connection Pool Monitoring
- [ ] Implement `ConnectionPoolMonitor`
- [ ] Implement pool health analysis
- [ ] Add pool metrics to logging system
- [ ] Integration tests with heavy connection load
- [ ] Documentation

**Deliverables**:
- Pool monitoring capability
- Pool health dashboard (WPF)
- Troubleshooting guide for pool issues

---

### Phase 3: Developer Experience (Weeks 6-7)

**Goal**: Make it easy to use in dev and production

#### Week 6: CLI Enhancements
- [ ] Add `triage` command
- [ ] Add `baseline capture` command
- [ ] Add `baseline compare` command
- [ ] Add `dataset-test` command
- [ ] Add `package` command for creating diagnostic packages
- [ ] Improve output formatting with Spectre.Console

**Deliverables**:
- Enhanced CLI with new commands
- User-friendly progress indicators
- Colorized output

#### Week 7: Integration Helpers
- [ ] Create drop-in wrapper classes for common scenarios
- [ ] Create Visual Studio code snippets
- [ ] Create NuGet package with all features
- [ ] Sample projects for different scenarios
- [ ] Video tutorials

**Deliverables**:
- Easy integration patterns
- NuGet package published
- Complete sample applications
- Video walkthrough

---

### Phase 4: Production Readiness (Week 8)

**Goal**: Polish and prepare for production deployment

#### Week 8: Production Hardening
- [ ] Error handling review
- [ ] Security audit (credential handling)
- [ ] Performance optimization
- [ ] Memory leak testing
- [ ] Thread safety review
- [ ] Exception handling standardization

**Deliverables**:
- Production-ready codebase
- Security review report
- Performance test results

---

## Usage Patterns

### Pattern 1: Development-Time Debugging

```csharp
// In your development environment
var logger = new DiagnosticLogger(new DiagnosticLoggerOptions
{
    EnableConsoleOutput = true,
    LogDirectory = @"C:\Logs\SqlDiag"
});

// Instrument your DataAdapter
var adapter = new SqlDataAdapter(query, connection);
var diagnostics = new DataSetDiagnostics();
var instrumented = diagnostics.Instrument(adapter);

var ds = new DataSet();
instrumented.Fill(ds);

// Automatically logged with performance breakdown
Console.WriteLine(instrumented.LastFillMetrics.GetPerformanceSummary());
```

### Pattern 2: Production Monitoring

```csharp
// At application startup
public class Application
{
    private static DiagnosticLogger _diagnosticLogger;
    
    static void Main()
    {
        // Initialize logging (runs in background)
        _diagnosticLogger = new DiagnosticLogger(new DiagnosticLoggerOptions
        {
            EnableConsoleOutput = false,
            LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "YourApp", "Diagnostics"),
            SqliteDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "YourApp", "diagnostics.db")
        });
        
        // Your application code...
    }
    
    public static void LogSlowDataLoad(string operationName, TimeSpan duration)
    {
        if (duration > TimeSpan.FromSeconds(5))
        {
            _diagnosticLogger.LogSlowOperation(operationName, duration, 
                $"DataSet load exceeded threshold");
        }
    }
}
```

### Pattern 3: Customer Support Investigation

```csharp
// When customer reports issue, have them run:
// YourApp.exe /diagnostic-package

static async Task CreateSupportPackageAsync()
{
    using var logger = new DiagnosticLogger(new DiagnosticLoggerOptions
    {
        LogDirectory = GetLogDirectory()
    });
    
    Console.WriteLine("Creating diagnostic package...");
    var packagePath = await logger.CreateDiagnosticPackageAsync(
        fromDate: DateTime.UtcNow.AddDays(-7));
    
    Console.WriteLine($"Package created: {packagePath}");
    Console.WriteLine("Please send this file to support@yourcompany.com");
}
```

### Pattern 4: Quick Triage During Incident

```csharp
// Run from immediate window or separate console app
var connectionString = "Server=...;Database=...";

var triage = await QuickTriage.RunAsync(
    connectionString,
    progress: new Progress<string>(msg => Console.WriteLine(msg)));

Console.WriteLine(triage.GetSummary());

// Output:
// === SQL Diagnostics Triage ===
// Completed in 28.3s
//
// DIAGNOSIS: Network (confidence: 90%)
// Network connectivity or latency issues detected
//
// Test Results:
//   Network:    ⚠ 2/5 pings succeeded, avg latency: 350ms
//   Connection: ✓ Connected in 1850ms
//   Query:      ✓ Query executed in 120ms (SQL: 45ms, Network: 75ms)
//   Server:     ✓ SQL Server CPU: 35%
//   Blocking:   ✓ 0 blocked sessions detected
//
// Recommendations:
//   - Check network connectivity to SQL Server
//   - Verify firewall rules and VPN connection
//   - Run traceroute to identify network hops
```

---

## Project Structure Updates

Add these new projects to the solution:

```
SqlDiagnostics.sln
├── src/
│   ├── SqlDiagnostics.Core/
│   │   ├── Diagnostics/
│   │   │   ├── DataSetDiagnostics.cs      [NEW]
│   │   │   ├── ConnectionPoolMonitor.cs    [NEW]
│   │   │   └── (existing files)
│   │   ├── Logging/                        [NEW]
│   │   │   ├── DiagnosticLogger.cs
│   │   │   ├── IDiagnosticSink.cs
│   │   │   ├── JsonFileSink.cs
│   │   │   ├── SqliteSink.cs
│   │   │   └── RollingLogSink.cs
│   │   ├── Baseline/                       [NEW]
│   │   │   ├── BaselineManager.cs
│   │   │   ├── PerformanceBaseline.cs
│   │   │   └── RegressionReport.cs
│   │   ├── Triage/                         [NEW]
│   │   │   ├── QuickTriage.cs
│   │   │   ├── TriageResult.cs
│   │   │   └── Diagnosis.cs
│   │   └── Models/
│   │       ├── DataSetFillMetrics.cs       [NEW]
│   │       ├── DiagnosticEvent.cs          [NEW]
│   │       └── (existing files)
│   └── SqlDiagnostics.Cli/
│       ├── Commands/
│       │   ├── TriageCommand.cs            [NEW]
│       │   ├── BaselineCommand.cs          [NEW]
│       │   ├── PackageCommand.cs           [NEW]
│       │   └── (existing commands)
```

---

## Dependencies Update

Add to `SqlDiagnostics.Core.csproj`:

```xml
<ItemGroup>
  <!-- Existing -->
  <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.*" />
  <PackageReference Include="System.Reactive" Version="6.0.*" />
  <PackageReference Include="Polly" Version="8.0.*" />
  
  <!-- New for Phase 1-2 -->
  <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.*" />
  <PackageReference Include="System.IO.Compression" Version="8.0.*" />
  
  <!-- Conditional for connection pool performance counters -->
  <PackageReference Include="System.Diagnostics.PerformanceCounter" Version="8.0.*" 
                    Condition="'$(TargetFramework)' == 'net8.0'" />
</ItemGroup>
```

---

## Success Metrics

### Quantitative Goals

| Metric | Current | Target (3 months) |
|--------|---------|-------------------|
| Time to diagnose issue | 2-4 hours | <15 minutes |
| Customer-reported logs with diagnostics | 0% | 80% |
| False positive issue reports | ~40% | <10% |
| Developer time spent on "can't reproduce" | 30% | <5% |
| Production incidents with complete diagnostic data | 10% | 90% |

### Qualitative Goals

- [ ] Developers can quickly identify root cause without customer access
- [ ] Customer support can provide initial diagnosis without developer involvement
- [ ] New developers can understand performance issues using logs
- [ ] Diagnostic data is actionable and specific
- [ ] Zero production deployments required for monitoring

---

## Testing Strategy

### Unit Tests (>80% coverage)
- All diagnostic modules
- Logging sinks
- Baseline calculation
- Triage logic

### Integration Tests
- Live SQL Server 2016/2019/2022
- Various connection string configurations
- Network latency simulation
- Pool exhaustion scenarios
- Heavy DataSet loads

### Performance Tests
- Overhead measurement (<2% target)
- Memory leak detection
- Long-running monitoring (24+ hours)
- High-frequency logging

### User Acceptance Tests
- Developer using for debugging
- Customer creating diagnostic package
- Support analyzing customer logs
- Production monitoring scenarios

---

## Documentation Deliverables

1. **Quick Start Guide** (2 pages)
   - Install NuGet package
   - Add 3 lines of code
   - View first diagnostic output

2. **Troubleshooting Playbook** (10 pages)
   - Symptom → Triage → Resolution flowcharts
   - Common scenarios with solutions
   - Example log analysis

3. **API Reference** (auto-generated from XML docs)
   - All public APIs
   - Code examples for each class
   - Best practices

4. **Integration Guide** (15 pages)
   - Development-time usage
   - Production deployment
   - CI/CD integration
   - Log aggregation

5. **Video Tutorials** (3 videos, 5-10 min each)
   - Getting started
   - Debugging slow DataSet loads
   - Analyzing customer diagnostic packages

---

## Risk Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Performance overhead in production | High | Medium | Extensive benchmarking; make logging async; sampling |
| Customer resistance to new logging | Medium | Low | Emphasize privacy (no data logged); opt-in; clear benefits |
| SQLite compatibility issues | Medium | Low | Fallback to JSON files; test on target environments |
| Complex integration for developers | High | Medium | Provide wrapper classes; code snippets; samples |
| Log file size explosion | Medium | Medium | Rolling logs; automatic cleanup; compression |

---

## Next Steps

### Immediate Actions (This Week)

1. **Review & Approve Plan**
   - Stakeholder review of priorities
   - Confirm timeline and resources
   - Sign-off on scope

2. **Environment Setup**
   - Create feature branches
   - Set up test SQL Server instances
   - Configure CI/CD for new projects

3. **Spike: DataSet Instrumentation**
   - 2-day spike to validate approach
   - Measure overhead
   - Confirm SqlConnection statistics work as expected

### Week 1 Kickoff

1. Begin Phase 1, Week 1 tasks
2. Daily standups to track progress
3. Early feedback from team on DataSet diagnostics design

---

## Questions for Discussion

1. **Log Retention**: How long should diagnostic logs be retained by default?
2. **Privacy**: Are there any data privacy concerns with logging query patterns?
3. **Alerting**: Should we add real-time alerting (email/Slack) or just logging?
4. **Cloud Storage**: Should diagnostic packages support upload to cloud storage?
5. **Licensing**: Any concerns with dependencies (SQLite, System.Reactive)?

---

## Conclusion

This plan transforms the SQL Diagnostics Toolkit from a generic database diagnostic tool into a specialized solution for your legacy application's specific needs:

- **DataSet-focused**: Directly addresses your DataAdapter-heavy architecture
- **Client-side only**: No server-side components required
- **Logging-first**: Built for async analysis, not just real-time debugging
- **Customer-friendly**: Easy for non-technical users to create diagnostic packages
- **Developer-friendly**: Minimal code changes, maximum insight

**Estimated Total Effort**: 8 weeks for MVP  
**Team Size**: 1-2 developers  
**ROI**: Dramatic reduction in support time and faster issue resolution

Next step: Review this plan and schedule kickoff meeting to begin Phase 1.
