using System;

namespace SqlDiagnostics.Core.Baseline;

/// <summary>
/// Configures how performance baselines are captured and compared.
/// </summary>
public sealed class BaselineOptions
{
    /// <summary>
    /// Gets or sets how many diagnostic samples should be collected when creating a baseline.
    /// </summary>
    public int SampleCount { get; set; } = 5;

    /// <summary>
    /// Gets or sets the delay between diagnostic samples during baseline capture.
    /// </summary>
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the acceptable percentage increase (e.g. 0.2 == 20%) in connection latency before reporting a regression.
    /// </summary>
    public double ConnectionLatencyTolerance { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets the acceptable percentage increase in network latency before reporting a regression.
    /// </summary>
    public double NetworkLatencyTolerance { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the acceptable drop in connection success rate (expressed as fraction e.g. 0.1 == 10%).
    /// </summary>
    public double SuccessRateTolerance { get; set; } = 0.1;

    /// <summary>
    /// Creates a copy of the current options.
    /// </summary>
    public BaselineOptions Clone() => new()
    {
        SampleCount = SampleCount,
        SampleInterval = SampleInterval,
        ConnectionLatencyTolerance = ConnectionLatencyTolerance,
        NetworkLatencyTolerance = NetworkLatencyTolerance,
        SuccessRateTolerance = SuccessRateTolerance
    };
}
