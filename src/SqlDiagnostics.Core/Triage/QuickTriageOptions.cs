using System;
using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Triage;

/// <summary>
/// Allows customising the probes executed by <see cref="QuickTriage"/>.
/// </summary>
public sealed class QuickTriageOptions
{
    public Func<string, CancellationToken, Task<TestResult>>? NetworkProbe { get; set; }
    public Func<string, CancellationToken, Task<TestResult>>? ConnectionProbe { get; set; }
    public Func<string, CancellationToken, Task<TestResult>>? QueryProbe { get; set; }
    public Func<string, CancellationToken, Task<TestResult>>? ServerProbe { get; set; }
    public Func<string, CancellationToken, Task<TestResult>>? BlockingProbe { get; set; }

    public TimeSpan SlowConnectionThreshold { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan SlowQueryThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
}
