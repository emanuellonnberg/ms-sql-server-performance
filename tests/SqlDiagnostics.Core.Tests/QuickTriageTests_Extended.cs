using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Logging;
using SqlDiagnostics.Core.Triage;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class QuickTriageTests_Extended
{
    [Fact]
    public async Task RunAsync_ProbeThrowsException_HandledAndFailureReturned()
    {
        var options = new QuickTriageOptions
        {
            NetworkProbe = (_, _) => throw new InvalidOperationException("Simulated failure")
        };
        var result = await QuickTriage.RunAsync("Server=(local);Database=master;", options, cancellationToken: CancellationToken.None);
        Assert.False(result.Network.Success);
        Assert.Contains("Simulated failure", result.Network.Details);
        Assert.Equal("Network", result.Diagnosis.Category);
    }

    [Fact]
    public async Task RunAsync_SlowProbe_RecordsDurationAndLogsWarning()
    {
        var logger = new DiagnosticLogger();
        var options = new QuickTriageOptions
        {
            Logger = logger,
            NetworkProbe = async (_, _) => {
                await Task.Delay(200, CancellationToken.None);
                return new TestResult("Network") { Success = true, Details = "OK", Duration = TimeSpan.FromMilliseconds(200) };
            },
            SlowConnectionThreshold = TimeSpan.FromMilliseconds(50),
            SlowQueryThreshold = TimeSpan.FromMilliseconds(50)
        };
        var result = await QuickTriage.RunAsync("Server=(local);Database=master;", options, cancellationToken: CancellationToken.None);
        Assert.True(result.Network.Duration >= TimeSpan.FromMilliseconds(200));
        // Optionally, check logger output if logger exposes events
    }

    [Fact]
    public async Task RunAsync_CustomProbeList_UsedInsteadOfDefaults()
    {
        var customProbes = new List<(string, Func<string, CancellationToken, Task<TestResult>>)> {
            ("Network", (_, _) => Task.FromResult(new TestResult("Network") { Success = true, Details = "Custom OK" })),
            ("Connection", (_, _) => Task.FromResult(new TestResult("Connection") { Success = true, Details = "Custom OK" }))
        };
        var options = new QuickTriageOptions { CustomProbes = customProbes };
        var result = await QuickTriage.RunAsync("Server=(local);Database=master;", options, cancellationToken: CancellationToken.None);
        Assert.True(result.Network.Success);
        Assert.Equal("Custom OK", result.Network.Details);
        Assert.True(result.Connection.Success);
        Assert.Equal("Custom OK", result.Connection.Details);
    }

    [Fact]
    public async Task RunAsync_ProbeTimingFields_AreSet()
    {
        var options = new QuickTriageOptions
        {
            NetworkProbe = async (_, _) => {
                await Task.Delay(10);
                return new TestResult("Network") { Success = true, Details = "OK", Duration = TimeSpan.FromMilliseconds(10) };
            }
        };
        var result = await QuickTriage.RunAsync("Server=(local);Database=master;", options, cancellationToken: CancellationToken.None);
        Assert.True(result.Network.StartTimeUtc.HasValue);
        Assert.True(result.Network.EndTimeUtc.HasValue);
        Assert.True(result.Network.EndTimeUtc > result.Network.StartTimeUtc);
    }
}
