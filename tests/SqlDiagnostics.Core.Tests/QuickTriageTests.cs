using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Triage;
using Xunit;

namespace SqlDiagnostics.Core.Tests;

public class QuickTriageTests
{
    [Fact]
    public async Task RunAsync_HealthyResults_ProducesHealthyDiagnosis()
    {
        var options = new QuickTriageOptions
        {
            NetworkProbe = (_, _) => Task.FromResult(new TestResult("Network")
            {
                Success = true,
                Details = "Network OK"
            }),
            ConnectionProbe = (_, _) => Task.FromResult(new TestResult("Connection")
            {
                Success = true,
                Details = "Connected fast"
            }),
            QueryProbe = (_, _) => Task.FromResult(new TestResult("Query")
            {
                Success = true,
                Details = "Query fast"
            }),
            ServerProbe = (_, _) => Task.FromResult(new TestResult("Server")
            {
                Success = true,
                Details = "CPU 30%"
            }),
            BlockingProbe = (_, _) => Task.FromResult(new TestResult("Blocking")
            {
                Success = true,
                Details = "No blocking"
            })
        };

        var result = await QuickTriage.RunAsync("Server=(local);Database=master;", options, cancellationToken: CancellationToken.None);

        Assert.Equal("Healthy", result.Diagnosis.Category);
        Assert.True(result.Network.Success);
        Assert.True(result.Connection.Success);
    }

    [Fact]
    public async Task RunAsync_FailingNetwork_ReturnsNetworkDiagnosis()
    {
        var options = new QuickTriageOptions
        {
            NetworkProbe = (_, _) =>
            {
                var test = new TestResult("Network")
                {
                    Success = false,
                    Details = "Ping failed"
                };
                test.AddIssue("No response from host.");
                return Task.FromResult(test);
            },
            ConnectionProbe = (_, _) => Task.FromResult(new TestResult("Connection")
            {
                Success = true,
                Details = "Connected"
            }),
            QueryProbe = (_, _) => Task.FromResult(new TestResult("Query")
            {
                Success = true,
                Details = "Query OK"
            }),
            ServerProbe = (_, _) => Task.FromResult(new TestResult("Server")
            {
                Success = true,
                Details = "Server OK"
            }),
            BlockingProbe = (_, _) => Task.FromResult(new TestResult("Blocking")
            {
                Success = true,
                Details = "No blocking"
            })
        };

        var result = await QuickTriage.RunAsync("Server=(local);Database=master;", options, cancellationToken: CancellationToken.None);

        Assert.Equal("Network", result.Diagnosis.Category);
        Assert.Contains("Network connectivity issues detected", result.Diagnosis.Summary);
    }
}
