using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlDiagnostics.Core;
using SqlDiagnostics.Core.Diagnostics.Connection;
using SqlDiagnostics.Core.Diagnostics.Network;
using SqlDiagnostics.Core.Interfaces;
using SqlDiagnostics.Core.Models;
using SqlDiagnostics.Core.Utilities;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class GuardClauseTests
{
    [Fact]
    public async Task StopwatchHelper_MeasureAsync_WithNullFunc_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => StopwatchHelper.MeasureAsync<string>(null!));
    }

    [Fact]
    public async Task StopwatchHelper_MeasureAsync_NonGeneric_WithNullFunc_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => StopwatchHelper.MeasureAsync(null!));
    }

    [Fact]
    public async Task ConnectionDiagnostics_MeasureConnectionAsync_WithBlankConnection_Throws()
    {
        var diagnostics = new ConnectionDiagnostics();

        await Assert.ThrowsAsync<ArgumentException>(() => diagnostics.MeasureConnectionAsync(" "));
    }

    [Fact]
    public async Task ConnectionDiagnostics_MeasureConnectionAsync_WithNegativeDelay_Throws()
    {
        var diagnostics = new ConnectionDiagnostics();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            diagnostics.MeasureConnectionAsync("Server=(local);", delayBetweenAttempts: TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task NetworkDiagnostics_MeasureLatencyAsync_WithBlankHost_Throws()
    {
        var diagnostics = new NetworkDiagnostics();

        await Assert.ThrowsAsync<ArgumentException>(() => diagnostics.MeasureLatencyAsync(" "));
    }

    [Fact]
    public async Task NetworkDiagnostics_MeasureLatencyAsync_WithNonPositiveTimeout_Throws()
    {
        var diagnostics = new NetworkDiagnostics();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => diagnostics.MeasureLatencyAsync("localhost", timeoutMilliseconds: 0));
    }

    [Fact]
    public void ConnectionMetrics_AddFailure_WithNull_Throws()
    {
        var metrics = new ConnectionMetrics();

        Assert.Throws<ArgumentNullException>(() => metrics.AddFailure(null!));
    }

    [Fact]
    public void QueryMetrics_AddStatistic_WithBlankKey_Throws()
    {
        var metrics = new QueryMetrics();

        Assert.Throws<ArgumentException>(() => metrics.AddStatistic(" ", 1));
    }

    [Fact]
    public void ServerMetrics_AddProperty_WithBlankKey_Throws()
    {
        var metrics = new ServerMetrics();

        Assert.Throws<ArgumentException>(() => metrics.AddProperty(" ", 1));
    }

    [Fact]
    public async Task SqlDiagnosticsClient_RunQuickCheckAsync_WithBlankConnection_Throws()
    {
        await using var client = new SqlDiagnosticsClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.RunQuickCheckAsync(" "));
    }

    [Fact]
    public void SqlDiagnosticsClient_MonitorContinuously_WithBlankConnection_Throws()
    {
        using var client = new SqlDiagnosticsClient();

        Assert.Throws<ArgumentException>(() => client.MonitorContinuously(" ", TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SqlDiagnosticsClient_MonitorContinuously_WithNonPositiveInterval_Throws()
    {
        using var client = new SqlDiagnosticsClient();

        Assert.Throws<ArgumentOutOfRangeException>(() => client.MonitorContinuously("Server=.;Database=master;", TimeSpan.Zero));
    }

    [Fact]
    public void SqlDiagnosticsClient_RegisterRecommendationRule_WithNull_Throws()
    {
        using var client = new SqlDiagnosticsClient();

        Assert.Throws<ArgumentNullException>(() => client.RegisterRecommendationRule(null!));
    }

    [Fact]
    public async Task MetricCollectorBase_ExecuteWithRetryAsync_WithNullOperation_Throws()
    {
        var collector = new TestCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() => collector.InvokeExecute(null!));
    }

    [Fact]
    public async Task MetricCollectorBase_ExecuteWithRetryAsync_WithInvalidAttempts_Throws()
    {
        var collector = new TestCollector();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => collector.InvokeExecute(() => Task.FromResult(0), maxAttempts: 0));
    }

    [Fact]
    public async Task MetricCollectorBase_ExecuteWithRetryAsync_WithNegativeDelay_Throws()
    {
        var collector = new TestCollector();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            collector.InvokeExecute(() => Task.FromResult(0), delay: TimeSpan.FromMilliseconds(-1)));
    }

    private sealed class TestCollector : MetricCollectorBase<int>
    {
        public TestCollector() : base(logger: null)
        {
        }

        public override bool IsSupported(SqlConnection connection) => true;

        public override Task<int> CollectAsync(SqlConnection connection, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> InvokeExecute(Func<Task<int>> operation, int maxAttempts = 3, TimeSpan? delay = null) =>
            ExecuteWithRetryAsync(operation, maxAttempts, delay);
    }
}
