using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlDiagnostics.Core.Diagnostics.Dataset;
using SqlDiagnostics.Core.Models;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class DataSetDiagnosticsTests
{
    [Fact]
    public async Task MeasureFillAsync_PopulatesMetrics_WhenFillCompletes()
    {
        var diagnostics = new DataSetDiagnostics();
        using var adapter = new SqlDataAdapter();
        using var dataSet = new DataSet();

        var options = new DataSetDiagnosticsOptions
        {
            CaptureConnectionStatistics = false,
            CaptureDataSetSize = true,
            AnalyzeStructure = true,
            FillOverride = (target, _) =>
            {
                var table = target.Tables.Add("Customers");
                table.Columns.Add("Id", typeof(int));
                table.Columns.Add("Name", typeof(string));
                table.Rows.Add(1, "Alice");
                table.Rows.Add(2, "Bob");
                return table.Rows.Count;
            }
        };

        var metrics = await diagnostics.MeasureFillAsync(adapter, dataSet, options);

        Assert.Equal(2, metrics.RowCount);
        Assert.Equal(1, metrics.TableCount);
        Assert.Null(metrics.Error);
        Assert.NotNull(metrics.Analysis);
        Assert.True(metrics.Analysis!.HasIssues);
        Assert.Contains(metrics.Analysis.Tables, t => t.TableName == "Customers");
    }

    [Fact]
    public void InstrumentedAdapter_Fill_CapturesMetrics()
    {
        var diagnostics = new DataSetDiagnostics();
        using var adapter = new SqlDataAdapter();
        using var dataSet = new DataSet();

        var options = new DataSetDiagnosticsOptions
        {
            CaptureConnectionStatistics = false,
            CaptureDataSetSize = false,
            AnalyzeStructure = false,
            FillOverride = (target, _) =>
            {
                var table = target.Tables.Add("Orders");
                table.Columns.Add("Id", typeof(int));
                table.Rows.Add(1);
                table.Rows.Add(2);
                return table.Rows.Count;
            }
        };

        using var instrumented = diagnostics.Instrument(adapter, options);

        var rows = instrumented.Fill(dataSet);

        Assert.Equal(2, rows);
        Assert.NotNull(instrumented.LastFillMetrics);
        Assert.Equal(2, instrumented.LastFillMetrics!.RowCount);
    }

    [Fact]
    public async Task MeasureFillAsync_AddsWarningForLargeDataSet()
    {
        var diagnostics = new DataSetDiagnostics();
        using var adapter = new SqlDataAdapter();
        using var dataSet = new DataSet();

        var options = new DataSetDiagnosticsOptions
        {
            CaptureConnectionStatistics = false,
            CaptureDataSetSize = true,
            LargeDataSetSizeThresholdBytes = 1,
            AnalyzeStructure = false,
            FillOverride = (target, _) =>
            {
                var table = target.Tables.Add("Payload");
                table.Columns.Add("Id", typeof(int));
                table.Rows.Add(1);
                return table.Rows.Count;
            }
        };

        var metrics = await diagnostics.MeasureFillAsync(adapter, dataSet, options);

        Assert.NotNull(metrics.DataSetSizeBytes);
        Assert.Contains(metrics.Warnings, warning => warning.Contains("DataSet size"));
    }
}
