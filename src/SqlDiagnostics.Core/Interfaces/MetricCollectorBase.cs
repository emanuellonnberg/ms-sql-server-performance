using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace SqlDiagnostics.Core.Interfaces;

/// <summary>
/// Provides retry-aware scaffolding for collectors that execute SQL against a target instance.
/// </summary>
public abstract class MetricCollectorBase<T> : IMetricCollector<T>
{
    private readonly ILogger? _logger;

    protected MetricCollectorBase(ILogger? logger = null)
    {
        _logger = logger;
    }

    public abstract bool IsSupported(SqlConnection connection);

    public abstract Task<T> CollectAsync(SqlConnection connection, CancellationToken cancellationToken = default);

    protected Task<TResult> ExecuteWithRetryAsync<TResult>(
        Func<Task<TResult>> operation,
        int maxAttempts = 3,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        if (delay.HasValue && delay.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        return ExecuteWithRetryInternalAsync(operation, maxAttempts, delay ?? TimeSpan.FromMilliseconds(150), cancellationToken);
    }

    private async Task<TResult> ExecuteWithRetryInternalAsync<TResult>(
        Func<Task<TResult>> operation,
        int maxAttempts,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (SqlException ex) when (IsTransient(ex))
            {
                lastException = ex;
                _logger?.LogWarning(ex, "Transient SQL exception on attempt {Attempt}", attempt);
            }
            catch (TimeoutException ex)
            {
                lastException = ex;
                _logger?.LogWarning(ex, "Timeout on attempt {Attempt}", attempt);
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException ?? new InvalidOperationException("Operation failed without exception.");
    }

    protected virtual bool IsTransient(SqlException ex) =>
        ex.Number is 4060 or 40197 or 40501 or 40613 || ex.Class >= 20;
}
