using System;
using System.Collections.Generic;

namespace SqlDiagnostics.Core.Models;

/// <summary>
/// Captures timing and reliability statistics for SQL connection attempts.
/// </summary>
public sealed class ConnectionMetrics
{
    public int TotalAttempts { get; set; }
    public int SuccessfulAttempts { get; set; }
    public int FailedAttempts { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan? AverageConnectionTime { get; set; }
    public TimeSpan? MinConnectionTime { get; set; }
    public TimeSpan? MaxConnectionTime { get; set; }
    public IReadOnlyList<ConnectionFailure> Failures => _failures;

    private readonly List<ConnectionFailure> _failures = new();

    public void AddFailure(ConnectionFailure failure)
    {
        if (failure is null)
        {
            throw new ArgumentNullException(nameof(failure));
        }

        _failures.Add(failure);
    }
}

/// <summary>
/// Represents a failed connection attempt.
/// </summary>
public sealed class ConnectionFailure
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int ErrorNumber { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Severity { get; set; }
    public string? ServerName { get; set; }
    public TimeSpan? Duration { get; set; }
}
