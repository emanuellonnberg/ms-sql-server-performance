using System;
using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Triage;

/// <summary>
/// Allows customising the probes executed by <see cref="QuickTriage"/>.
/// </summary>
public sealed class QuickTriageOptions
{
    /// <summary>
    /// Optional logger for probe failures and slow operations.
    /// </summary>
    public SqlDiagnostics.Core.Logging.DiagnosticLogger? Logger { get; set; }

    /// <summary>
    /// Optional custom probe list. If set, these will be run in order instead of the default probes.
    /// </summary>
    public System.Collections.Generic.IList<(string Name, Func<string, CancellationToken, Task<TestResult>> Probe)>? CustomProbes { get; set; }

    public Func<string, CancellationToken, Task<TestResult>>? NetworkProbe { get; set; }
    public Func<string, CancellationToken, Task<TestResult>>? ConnectionProbe { get; set; }
    public Func<string, CancellationToken, Task<TestResult>>? QueryProbe { get; set; }
    public Func<string, CancellationToken, Task<TestResult>>? ServerProbe { get; set; }
    public Func<string, CancellationToken, Task<TestResult>>? BlockingProbe { get; set; }

    public TimeSpan SlowConnectionThreshold { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan SlowQueryThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
    /// <summary>
    /// If true, run independent probes in parallel to reduce triage time.
    /// </summary>
    public bool EnableParallelProbes { get; set; } = false;
}
