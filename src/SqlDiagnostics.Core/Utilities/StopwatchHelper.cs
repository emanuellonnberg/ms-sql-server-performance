using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Utilities;

/// <summary>
/// Helper for measuring elapsed time of asynchronous operations.
/// </summary>
public static class StopwatchHelper
{
    public static async Task<(T result, TimeSpan elapsed)> MeasureAsync<T>(Func<Task<T>> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var sw = Stopwatch.StartNew();
        var result = await action().ConfigureAwait(false);
        sw.Stop();
        return (result, sw.Elapsed);
    }

    public static async Task<TimeSpan> MeasureAsync(Func<Task> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var sw = Stopwatch.StartNew();
        await action().ConfigureAwait(false);
        sw.Stop();
        return sw.Elapsed;
    }
}
