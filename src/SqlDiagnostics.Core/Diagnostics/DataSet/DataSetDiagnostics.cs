using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlDiagnostics.Core.Models;

namespace SqlDiagnostics.Core.Diagnostics.Dataset;

/// <summary>
/// Provides instrumentation and structural analysis for DataSet/DataAdapter usage.
/// </summary>
public sealed class DataSetDiagnostics
{
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSetDiagnostics"/> class.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public DataSetDiagnostics(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Measures the performance of a <c>SqlDataAdapter.Fill</c> operation and returns detailed metrics.
    /// </summary>
    /// <param name="adapter">The adapter responsible for hydrating the dataset.</param>
    /// <param name="dataSet">The dataset to populate.</param>
    /// <param name="options">Optional instrumentation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Captured metrics.</returns>
    public async Task<DataSetFillMetrics> MeasureFillAsync(
        SqlDataAdapter adapter,
        System.Data.DataSet dataSet,
        DataSetDiagnosticsOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (adapter is null)
        {
            throw new ArgumentNullException(nameof(adapter));
        }

        if (dataSet is null)
        {
            throw new ArgumentNullException(nameof(dataSet));
        }

        var effectiveOptions = options ?? DataSetDiagnosticsOptions.Default;
        var metrics = new DataSetFillMetrics
        {
            OperationName = effectiveOptions.OperationName,
            StartTimeUtc = DateTime.UtcNow
        };

        SqlConnection? sqlConnection = null;
        bool statsEnabledBefore = false;
        if (effectiveOptions.CaptureConnectionStatistics &&
            adapter.SelectCommand is not null &&
            adapter.SelectCommand.Connection is SqlConnection sc)
        {
            sqlConnection = sc;
            statsEnabledBefore = sqlConnection.StatisticsEnabled;
            try
            {
                sqlConnection.StatisticsEnabled = true;
                sqlConnection.ResetStatistics();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Unable to enable connection statistics before DataSet fill.");
                metrics.AddWarning("Connection statistics could not be enabled; network/CPU breakdown unavailable.");
                sqlConnection = null;
            }
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var fillDelegate = effectiveOptions.FillOverride ?? ((System.Data.DataSet target, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return adapter.Fill(target);
            });

            var rows = await Task.Run(() => fillDelegate(dataSet, cancellationToken), cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            metrics.Duration = stopwatch.Elapsed;
            metrics.RowCount = rows;
            metrics.TableCount = dataSet.Tables.Count;

            if (sqlConnection is not null)
            {
                PopulateConnectionStatistics(sqlConnection, metrics);
            }

            if (effectiveOptions.CaptureDataSetSize)
            {
                metrics.DataSetSizeBytes = EstimateDataSetSize(dataSet);
                if (metrics.DataSetSizeBytes > effectiveOptions.LargeDataSetSizeThresholdBytes)
                {
                    metrics.AddWarning($"DataSet size {metrics.DataSetSizeBytes.Value / 1024d / 1024d:N2} MB exceeds threshold.");
                }
            }

            if (effectiveOptions.AnalyzeStructure)
            {
                metrics.Analysis = AnalyzeStructure(dataSet, effectiveOptions);
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            metrics.Duration = stopwatch.Elapsed;
            metrics.Error = new TaskCanceledException("DataSet fill operation was cancelled.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            metrics.Duration = stopwatch.Elapsed;
            metrics.Error = ex;
            _logger?.LogWarning(ex, "DataSet fill failed for operation {Operation}.", effectiveOptions.OperationName ?? "<unnamed>");
        }
        finally
        {
            if (sqlConnection is not null)
            {
                try
                {
                    sqlConnection.StatisticsEnabled = statsEnabledBefore;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to restore connection statistics flag after DataSet fill.");
                }
            }
        }

        return metrics;
    }

    /// <summary>
    /// Creates a wrapper around an existing <see cref="SqlDataAdapter"/> that automatically captures metrics.
    /// </summary>
    /// <param name="adapter">Adapter to instrument.</param>
    /// <param name="options">Instrumentation options.</param>
    /// <returns>Instrumented adapter instance.</returns>
    public InstrumentedDataAdapter Instrument(SqlDataAdapter adapter, DataSetDiagnosticsOptions? options = null)
    {
        if (adapter is null)
        {
            throw new ArgumentNullException(nameof(adapter));
        }

        return new InstrumentedDataAdapter(adapter, this, options);
    }

    private static void PopulateConnectionStatistics(SqlConnection connection, DataSetFillMetrics metrics)
    {
        try
        {
            var stats = connection.RetrieveStatistics();
            metrics.ServerRoundtrips = GetStatistic(stats, "ServerRoundtrips");
            metrics.BytesReceived = GetStatistic(stats, "BytesReceived");
            metrics.BytesSent = GetStatistic(stats, "BytesSent");

            var executionMs = GetStatistic(stats, "ExecutionTime");
            if (executionMs.HasValue)
            {
                metrics.ExecutionTime = TimeSpan.FromMilliseconds(executionMs.Value);
            }

            var networkMs = GetStatistic(stats, "NetworkServerTime");
            if (networkMs.HasValue)
            {
                metrics.NetworkTime = TimeSpan.FromMilliseconds(networkMs.Value);
            }
        }
        catch (Exception ex)
        {
            metrics.AddWarning("Failed to retrieve connection statistics after fill.");
            Debug.WriteLine($"[DataSetDiagnostics] RetrieveStatistics failed: {ex}");
        }
    }

    private static long? GetStatistic(IDictionary stats, string key)
    {
        if (stats is null || key is null)
        {
            return null;
        }

        if (!stats.Contains(key))
        {
            return null;
        }

        return stats[key] switch
        {
            long longValue => longValue,
            int intValue => intValue,
            _ => null
        };
    }

    private static long? EstimateDataSetSize(System.Data.DataSet dataSet)
    {
        try
        {
            using var stream = new MemoryStream();
            dataSet.WriteXml(stream, XmlWriteMode.WriteSchema);
            return stream.Length;
        }
        catch
        {
            return null;
        }
    }

    private DataSetStructureAnalysis AnalyzeStructure(System.Data.DataSet dataSet, DataSetDiagnosticsOptions options)
    {
        var analysis = new DataSetStructureAnalysis();
        var hasRelations = dataSet.Relations.Count > 0;

        foreach (System.Data.DataTable table in dataSet.Tables)
        {
            var tableAnalysis = new DataTableAnalysis(table.TableName ?? string.Empty)
            {
                RowCount = table.Rows.Count,
                ColumnCount = table.Columns.Count,
                PrimaryKeyColumnCount = table.PrimaryKey?.Length ?? 0,
                RelationCount = dataSet.Relations
                    .Cast<DataRelation>()
                    .Count(r => r.ChildTable == table || r.ParentTable == table)
            };

            if (tableAnalysis.PrimaryKeyColumnCount == 0)
            {
                tableAnalysis.AddIssue("No primary key defined; lookups will be O(n).");
            }

            if (tableAnalysis.RowCount > options.LargeTableRowThreshold)
            {
                tableAnalysis.AddIssue($"Large table ({tableAnalysis.RowCount:N0} rows) loaded without paging.");
            }

            if (dataSet.Tables.Count > 1 && hasRelations && tableAnalysis.RelationCount == 0)
            {
                tableAnalysis.AddIssue("No DataRelations defined; may lead to client-side joins.");
            }

            if (dataSet.GetType() == typeof(DataSet))
            {
                tableAnalysis.AddIssue("Using untyped DataSet; typed DataSets provide compile-time safety and performance.");
            }

            if (table.Columns.Cast<DataColumn>().Any(c => c.DataType == typeof(string) && c.MaxLength <= 0))
            {
                tableAnalysis.AddIssue("String columns without MaxLength; consider constraining to reduce memory usage.");
            }

            analysis.AddTable(tableAnalysis);
        }

        return analysis;
    }
}
