using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlDiagnostics.Core.Models;

namespace SqlDiagnostics.Core.Diagnostics.Dataset;

/// <summary>
/// Wraps a <see cref="SqlDataAdapter"/> instance to automatically capture <see cref="DataSetFillMetrics"/>.
/// </summary>
public sealed class InstrumentedDataAdapter : IDisposable
{
    private readonly SqlDataAdapter _inner;
    private readonly DataSetDiagnostics _diagnostics;
    private readonly DataSetDiagnosticsOptions _baseOptions;
    private bool _disposed;

    internal InstrumentedDataAdapter(SqlDataAdapter inner, DataSetDiagnostics diagnostics, DataSetDiagnosticsOptions? options)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _baseOptions = (options ?? DataSetDiagnosticsOptions.Default).Clone();
    }

    /// <summary>
    /// Gets the most recent metrics captured by the adapter.
    /// </summary>
    public DataSetFillMetrics? LastFillMetrics { get; private set; }

    /// <summary>
    /// Gets the wrapped adapter for scenarios where direct access is required.
    /// </summary>
    public SqlDataAdapter InnerAdapter => _inner;

    /// <summary>
    /// Gets or sets the command used to select records from the data source.
    /// </summary>
    public SqlCommand? SelectCommand
    {
        get => _inner.SelectCommand;
        set => _inner.SelectCommand = value;
    }

    /// <summary>
    /// Gets or sets the command used to insert new records into the data source.
    /// </summary>
    public SqlCommand? InsertCommand
    {
        get => _inner.InsertCommand;
        set => _inner.InsertCommand = value;
    }

    /// <summary>
    /// Gets or sets the command used to update records in the data source.
    /// </summary>
    public SqlCommand? UpdateCommand
    {
        get => _inner.UpdateCommand;
        set => _inner.UpdateCommand = value;
    }

    /// <summary>
    /// Gets or sets the command used to delete records in the data source.
    /// </summary>
    public SqlCommand? DeleteCommand
    {
        get => _inner.DeleteCommand;
        set => _inner.DeleteCommand = value;
    }

    /// <summary>
    /// Gets the table mapping collection associated with the underlying adapter.
    /// </summary>
    public DataTableMappingCollection TableMappings => _inner.TableMappings;

    /// <summary>
    /// Fills the provided dataset and returns the number of rows successfully added.
    /// </summary>
    /// <param name="dataSet">Target dataset.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows added.</returns>
    public int Fill(System.Data.DataSet dataSet, CancellationToken cancellationToken = default) =>
        Fill(dataSet, null, cancellationToken);

    /// <summary>
    /// Fills the provided dataset for the specified source table name and returns the number of rows added.
    /// </summary>
    /// <param name="dataSet">Target dataset.</param>
    /// <param name="srcTable">Source table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows added.</returns>
    public int Fill(System.Data.DataSet dataSet, string? srcTable, CancellationToken cancellationToken = default)
    {
        var metrics = FillInternalAsync(
                dataSet,
                srcTable,
                cancellationToken,
                overrides: srcTable is null
                    ? null
                    : new DataSetDiagnosticsOptions
                    {
                        FillOverride = (target, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            return _inner.Fill(target, srcTable);
                        }
                    })
            .GetAwaiter()
            .GetResult();

        return metrics.RowCount;
    }

    /// <summary>
    /// Fills the provided data table and returns the number of rows successfully added.
    /// </summary>
    /// <param name="dataTable">Target data table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows added.</returns>
    public int Fill(DataTable dataTable, CancellationToken cancellationToken = default)
    {
        if (dataTable is null)
        {
            throw new ArgumentNullException(nameof(dataTable));
        }

        var owningDataSet = dataTable.DataSet ?? new System.Data.DataSet();
        var addedTemporarily = false;
        if (dataTable.DataSet is null)
        {
            owningDataSet.Tables.Add(dataTable);
            addedTemporarily = true;
        }

        try
        {
            var metrics = FillInternalAsync(
                    owningDataSet,
                    dataTable.TableName,
                    cancellationToken,
                    overrides: new DataSetDiagnosticsOptions
                    {
                        FillOverride = (_, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            return _inner.Fill(dataTable);
                        },
                        AnalyzeStructure = _baseOptions.AnalyzeStructure
                    })
                .GetAwaiter()
                .GetResult();

            return metrics.RowCount;
        }
        finally
        {
            if (addedTemporarily)
            {
                owningDataSet.Tables.Remove(dataTable);
            }
        }
    }

    /// <summary>
    /// Fills the provided dataset asynchronously and returns the captured metrics.
    /// </summary>
    /// <param name="dataSet">Target dataset.</param>
    /// <param name="operationName">Optional logical operation name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Captured metrics.</returns>
    public Task<DataSetFillMetrics> FillAsync(
        System.Data.DataSet dataSet,
        string? operationName = null,
        CancellationToken cancellationToken = default) =>
        FillInternalAsync(dataSet, operationName, cancellationToken);

    private async Task<DataSetFillMetrics> FillInternalAsync(
        System.Data.DataSet dataSet,
        string? operationName,
        CancellationToken cancellationToken,
        DataSetDiagnosticsOptions? overrides = null)
    {
        if (dataSet is null)
        {
            throw new ArgumentNullException(nameof(dataSet));
        }

        var options = MergeOptions(operationName, overrides);
        var metrics = await _diagnostics
            .MeasureFillAsync(_inner, dataSet, options, cancellationToken)
            .ConfigureAwait(false);

        LastFillMetrics = metrics;
        return metrics;
    }

    private DataSetDiagnosticsOptions MergeOptions(string? operationName, DataSetDiagnosticsOptions? overrides)
    {
        var result = _baseOptions.Clone();

        if (overrides is not null)
        {
            result.CaptureConnectionStatistics = overrides.CaptureConnectionStatistics;
            result.CaptureDataSetSize = overrides.CaptureDataSetSize;
            result.AnalyzeStructure = overrides.AnalyzeStructure;
            result.LargeTableRowThreshold = overrides.LargeTableRowThreshold;
            result.LargeDataSetSizeThresholdBytes = overrides.LargeDataSetSizeThresholdBytes;

            if (!string.IsNullOrWhiteSpace(overrides.OperationName))
            {
                result.OperationName = overrides.OperationName;
            }

            if (overrides.FillOverride is not null)
            {
                result.FillOverride = overrides.FillOverride;
            }
        }

        if (!string.IsNullOrWhiteSpace(operationName))
        {
            result.OperationName = operationName;
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.Dispose();
    }
}
