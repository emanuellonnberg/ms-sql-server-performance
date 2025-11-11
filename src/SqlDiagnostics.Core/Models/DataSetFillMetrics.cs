using System;
using System.Collections.Generic;
using System.Text;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Metrics captured during a SqlDataAdapter fill operation.
/// </summary>
public sealed class DataSetFillMetrics
{
    private readonly List<string> _warnings = new();

    /// <summary>
    /// Gets or sets the logical name of the operation being measured.
    /// </summary>
    public string? OperationName { get; set; }

    /// <summary>
    /// Gets or sets the timestamp (UTC) at which the fill operation started.
    /// </summary>
    public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the total duration of the fill operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the time spent executing the SQL command on the server.
    /// </summary>
    public TimeSpan? ExecutionTime { get; set; }

    /// <summary>
    /// Gets or sets the time spent transferring data between client and server.
    /// </summary>
    public TimeSpan? NetworkTime { get; set; }

    /// <summary>
    /// Gets the calculated client processing time (Duration - ExecutionTime - NetworkTime) when all components are available.
    /// </summary>
    public TimeSpan? ClientProcessingTime
    {
        get
        {
            if (!ExecutionTime.HasValue || !NetworkTime.HasValue || Duration == TimeSpan.Zero)
            {
                return null;
            }

            var clientTime = Duration - ExecutionTime.Value - NetworkTime.Value;
            return clientTime < TimeSpan.Zero ? TimeSpan.Zero : clientTime;
        }
    }

    /// <summary>
    /// Gets or sets the number of rows returned across all tables.
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// Gets or sets the number of tables populated during the fill operation.
    /// </summary>
    public int TableCount { get; set; }

    /// <summary>
    /// Gets or sets the number of server round-trips performed.
    /// </summary>
    public long? ServerRoundtrips { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes received from the server.
    /// </summary>
    public long? BytesReceived { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes sent to the server.
    /// </summary>
    public long? BytesSent { get; set; }

    /// <summary>
    /// Gets or sets the estimated size of the populated <see cref="System.Data.DataSet"/> in bytes.
    /// </summary>
    public long? DataSetSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the structural analysis captured for the dataset.
    /// </summary>
    public DataSetStructureAnalysis? Analysis { get; set; }

    /// <summary>
    /// Gets or sets the error that occurred during the fill operation, if any.
    /// </summary>
    public Exception? Error { get; set; }

    /// <summary>
    /// Gets the collection of warnings detected during the fill operation.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Adds an informational warning to the metrics.
    /// </summary>
    /// <param name="message">Warning message to add.</param>
    public void AddWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _warnings.Add(message);
    }

    /// <summary>
    /// Formats the metrics as a multi-line summary suitable for logging.
    /// </summary>
    /// <returns>Human-readable summary of the captured metrics.</returns>
    public string GetPerformanceSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Operation: {OperationName ?? "DataSet Fill"}");
        builder.AppendLine($"Start: {StartTimeUtc:O}");

        if (Error is not null)
        {
            builder.AppendLine($"Result: FAILED after {Duration.TotalMilliseconds:F0} ms");
            builder.AppendLine($"Error: {Error.GetType().Name}: {Error.Message}");
            return builder.ToString();
        }

        builder.AppendLine($"Duration: {Duration.TotalMilliseconds:F0} ms");
        builder.AppendLine($"Tables: {TableCount}, Rows: {RowCount}");

        if (ExecutionTime.HasValue || NetworkTime.HasValue || ClientProcessingTime.HasValue)
        {
            builder.AppendLine("Breakdown:");
            if (ExecutionTime.HasValue)
            {
                builder.AppendLine($"  - SQL Execution: {ExecutionTime.Value.TotalMilliseconds:F0} ms");
            }

            if (NetworkTime.HasValue)
            {
                builder.AppendLine($"  - Network: {NetworkTime.Value.TotalMilliseconds:F0} ms");
            }

            if (ClientProcessingTime.HasValue)
            {
                builder.AppendLine($"  - Client Processing: {ClientProcessingTime.Value.TotalMilliseconds:F0} ms");
            }
        }

        if (BytesReceived.HasValue || BytesSent.HasValue)
        {
            builder.AppendLine("Network:");
            if (BytesSent.HasValue)
            {
                builder.AppendLine($"  - Bytes Sent: {BytesSent.Value:N0}");
            }

            if (BytesReceived.HasValue)
            {
                builder.AppendLine($"  - Bytes Received: {BytesReceived.Value:N0}");
            }
        }

        if (ServerRoundtrips.HasValue)
        {
            builder.AppendLine($"Server Roundtrips: {ServerRoundtrips.Value}");
        }

        if (DataSetSizeBytes.HasValue)
        {
            builder.AppendLine($"DataSet Size: {DataSetSizeBytes.Value / 1024d:N1} KB");
        }

        if (_warnings.Count > 0)
        {
            builder.AppendLine("Warnings:");
            foreach (var warning in _warnings)
            {
                builder.AppendLine($"  - {warning}");
            }
        }

        if (Analysis is { HasIssues: true })
        {
            builder.AppendLine("Analysis:");
            foreach (var table in Analysis.Tables)
            {
                if (table.Issues.Count == 0)
                {
                    continue;
                }

                builder.AppendLine($"  {table.TableName}:");
                foreach (var issue in table.Issues)
                {
                    builder.AppendLine($"    - {issue}");
                }
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// Options that control how dataset instrumentation is performed.
/// </summary>
public sealed class DataSetDiagnosticsOptions
{
    /// <summary>
    /// Gets an instance of <see cref="DataSetDiagnosticsOptions"/> with default values.
    /// </summary>
    public static DataSetDiagnosticsOptions Default => new();

    /// <summary>
    /// Gets or sets the logical name of the operation being instrumented.
    /// </summary>
    public string? OperationName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether connection statistics should be captured (requires <see cref="Microsoft.Data.SqlClient.SqlConnection.StatisticsEnabled"/>).
    /// </summary>
    public bool CaptureConnectionStatistics { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the populated dataset should be measured by serializing to XML.
    /// </summary>
    public bool CaptureDataSetSize { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether structural analysis should be performed on the resulting dataset.
    /// </summary>
    public bool AnalyzeStructure { get; set; } = true;

    /// <summary>
    /// Gets or sets the threshold above which a table is considered "large" for warning purposes.
    /// </summary>
    public int LargeTableRowThreshold { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the threshold above which a dataset is considered large (in bytes) when <see cref="CaptureDataSetSize"/> is enabled.
    /// </summary>
    public long LargeDataSetSizeThresholdBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Gets or sets a delegate used to override the fill operation (primarily for testing).
    /// </summary>
    public Func<System.Data.DataSet, System.Threading.CancellationToken, int>? FillOverride { get; set; }

    /// <summary>
    /// Creates a copy of the current options.
    /// </summary>
    /// <returns>Cloned options instance.</returns>
    public DataSetDiagnosticsOptions Clone() => new()
    {
        OperationName = OperationName,
        CaptureConnectionStatistics = CaptureConnectionStatistics,
        CaptureDataSetSize = CaptureDataSetSize,
        AnalyzeStructure = AnalyzeStructure,
        LargeTableRowThreshold = LargeTableRowThreshold,
        LargeDataSetSizeThresholdBytes = LargeDataSetSizeThresholdBytes,
        FillOverride = FillOverride
    };
}
