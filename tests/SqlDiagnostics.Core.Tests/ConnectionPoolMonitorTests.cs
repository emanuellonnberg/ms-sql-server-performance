using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Diagnostics.Connection;
using SqlDiagnostics.Core.Models;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public sealed class ConnectionPoolMonitorTests
{
    [Fact]
    public async Task MonitorAsync_AggregatesSnapshots()
    {
        var snapshots = new Queue<ConnectionPoolSnapshot>(new[]
        {
            new ConnectionPoolSnapshot { Success = true, AcquisitionTime = TimeSpan.FromMilliseconds(100), FreeConnections = 5, ActiveConnections = 5 },
            new ConnectionPoolSnapshot { Success = false, AcquisitionTime = TimeSpan.FromMilliseconds(120), Error = "timeout" },
            new ConnectionPoolSnapshot { Success = true, AcquisitionTime = TimeSpan.FromMilliseconds(150), FreeConnections = 0, ActiveConnections = 30 }
        });

        var monitor = new ConnectionPoolMonitor("Server=(local);Database=master;", _ => Task.FromResult(snapshots.Dequeue()));

        var report = await monitor.MonitorAsync(
            duration: TimeSpan.FromMilliseconds(10),
            sampleInterval: TimeSpan.FromMilliseconds(1),
            cancellationToken: CancellationToken.None);

        Assert.True(report.Snapshots.Count > 0);
        Assert.Contains(report.Snapshots, snapshot => snapshot.Error == "timeout");
        Assert.NotNull(report.Summary);
    }
}
