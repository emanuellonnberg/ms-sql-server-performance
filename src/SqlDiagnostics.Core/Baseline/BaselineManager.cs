using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SqlDiagnostics.Core.Reports;
using SqlDiagnostics.Core;

namespace SqlDiagnostics.Core.Baseline;

/// <summary>
/// Handles capture and comparison of local performance baselines.
/// </summary>
public sealed class BaselineManager
{
    private const string FileExtension = ".json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storageDirectory;
    private readonly Func<string, CancellationToken, Task<DiagnosticReport>> _diagnosticsRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaselineManager"/> class.
    /// </summary>
    /// <param name="storageDirectory">Directory used to store baselines. Defaults to the user's local application data folder.</param>
    /// <param name="diagnosticsRunner">Optional delegate that generates a diagnostic report for a connection string.</param>
    public BaselineManager(
        string? storageDirectory = null,
        Func<string, CancellationToken, Task<DiagnosticReport>>? diagnosticsRunner = null)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        _storageDirectory = !string.IsNullOrWhiteSpace(storageDirectory)
            ? storageDirectory!
            : Path.Combine(root, "SqlDiagnostics", "Baselines");

        _diagnosticsRunner = diagnosticsRunner ?? DefaultDiagnosticsRunner;
    }

    /// <summary>
    /// Captures a new baseline by executing diagnostics multiple times and aggregating the results.
    /// </summary>
    public async Task<PerformanceBaseline> CaptureBaselineAsync(
        string connectionString,
        string baselineName,
        BaselineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        if (string.IsNullOrWhiteSpace(baselineName))
        {
            throw new ArgumentException("Baseline name must be provided.", nameof(baselineName));
        }

        var effectiveOptions = (options ?? new BaselineOptions()).Clone();
        if (effectiveOptions.SampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Sample count must be greater than zero.");
        }

        Directory.CreateDirectory(_storageDirectory);

        var connectionTimes = new List<double>();
        var successRates = new List<double>();
        var networkLatencies = new List<double>();

        for (var i = 0; i < effectiveOptions.SampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var report = await _diagnosticsRunner(connectionString, cancellationToken).ConfigureAwait(false);

            if (report.Connection?.AverageConnectionTime is { } average)
            {
                connectionTimes.Add(average.TotalMilliseconds);
            }

            if (report.Connection is { SuccessRate: var success })
            {
                successRates.Add(success);
            }

            if (report.Network?.Average is TimeSpan networkAverage)
            {
                networkLatencies.Add(networkAverage.TotalMilliseconds);
            }

            if (i < effectiveOptions.SampleCount - 1 && effectiveOptions.SampleInterval > TimeSpan.Zero)
            {
                await Task.Delay(effectiveOptions.SampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        var baseline = new PerformanceBaseline
        {
            Name = baselineName,
            CapturedAtUtc = DateTime.UtcNow,
            ConnectionStringHash = ComputeHash(connectionString),
            SampleCount = effectiveOptions.SampleCount,
            Connection = new ConnectionBaselineMetrics
            {
                MedianConnectionTimeMs = CalculatePercentile(connectionTimes, 0.5),
                P95ConnectionTimeMs = CalculatePercentile(connectionTimes, 0.95),
                P99ConnectionTimeMs = CalculatePercentile(connectionTimes, 0.99),
                SuccessRate = successRates.Count > 0 ? successRates.Average() : (double?)null
            },
            Network = new NetworkBaselineMetrics
            {
                MedianLatencyMs = CalculatePercentile(networkLatencies, 0.5),
                P95LatencyMs = CalculatePercentile(networkLatencies, 0.95),
                P99LatencyMs = CalculatePercentile(networkLatencies, 0.99)
            }
        };

        var fileName = $"{SanitizeFileName(baselineName)}-{baseline.CapturedAtUtc:yyyyMMddHHmmss}{FileExtension}";
        var path = Path.Combine(_storageDirectory, fileName);
        var json = JsonSerializer.Serialize(baseline, JsonOptions);
        File.WriteAllText(path, json);

        return baseline;
    }

    /// <summary>
    /// Compares the current diagnostics to a stored baseline.
    /// </summary>
    public async Task<RegressionReport> CompareToBaselineAsync(
        string connectionString,
        string? baselineName = null,
        BaselineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        var baseline = baselineName is null
            ? await LoadLatestBaselineForConnectionAsync(connectionString, cancellationToken).ConfigureAwait(false)
            : await LoadBaselineByNameAsync(baselineName, cancellationToken).ConfigureAwait(false);

        if (baseline is null)
        {
            return new RegressionReport
            {
                Success = false,
                Message = "Baseline not found. Capture a baseline before running comparisons."
            };
        }

        var effectiveOptions = (options ?? new BaselineOptions()).Clone();
        var report = await _diagnosticsRunner(connectionString, cancellationToken).ConfigureAwait(false);

        var regression = new RegressionReport
        {
            Success = true,
            BaselineName = baseline.Name,
            BaselineCapturedAtUtc = baseline.CapturedAtUtc,
            ComparisonAtUtc = DateTime.UtcNow
        };

        EvaluateConnectionRegression(baseline, report, effectiveOptions, regression);
        EvaluateNetworkRegression(baseline, report, effectiveOptions, regression);

        if (!regression.HasFindings)
        {
            regression.Message = "No regressions detected.";
        }

        return regression;
    }

    private static void EvaluateConnectionRegression(
        PerformanceBaseline baseline,
        DiagnosticReport report,
        BaselineOptions options,
        RegressionReport regression)
    {
        if (baseline.Connection.P95ConnectionTimeMs.HasValue &&
            report.Connection?.AverageConnectionTime is { } currentConnection)
        {
            var threshold = baseline.Connection.P95ConnectionTimeMs.Value * (1 + options.ConnectionLatencyTolerance);
            var currentMs = currentConnection.TotalMilliseconds;
            if (currentMs > threshold)
            {
                var percentageOver = (currentMs - threshold) / threshold;
                regression.Findings.Add(new RegressionFinding
                {
                    Severity = percentageOver > 0.5 ? RegressionSeverity.Critical : RegressionSeverity.Warning,
                    Category = "Connection",
                    Description = "Average connection time exceeds baseline.",
                    BaselineValue = $"{baseline.Connection.P95ConnectionTimeMs.Value:N0} ms (P95)",
                    CurrentValue = $"{currentMs:N0} ms",
                    PercentageChange = (currentMs - baseline.Connection.P95ConnectionTimeMs.Value) / baseline.Connection.P95ConnectionTimeMs.Value
                });
            }
        }

        if (baseline.Connection.SuccessRate.HasValue &&
            report.Connection is { SuccessRate: var currentSuccess })
        {
            var baselineSuccess = baseline.Connection.SuccessRate.Value;
            if (baselineSuccess - currentSuccess > options.SuccessRateTolerance)
            {
                regression.Findings.Add(new RegressionFinding
                {
                    Severity = RegressionSeverity.Critical,
                    Category = "Reliability",
                    Description = "Connection success rate has dropped.",
                    BaselineValue = $"{baselineSuccess:P1}",
                    CurrentValue = $"{currentSuccess:P1}",
                    PercentageChange = (currentSuccess - baselineSuccess) / baselineSuccess
                });
            }
        }
    }

    private static void EvaluateNetworkRegression(
        PerformanceBaseline baseline,
        DiagnosticReport report,
        BaselineOptions options,
        RegressionReport regression)
    {
        if (baseline.Network.P95LatencyMs.HasValue &&
            report.Network?.Average is TimeSpan currentNetwork)
        {
            var baselineP95 = baseline.Network.P95LatencyMs.Value;
            var threshold = baselineP95 * (1 + options.NetworkLatencyTolerance);
            var currentMs = currentNetwork.TotalMilliseconds;
            if (currentMs > threshold)
            {
                regression.Findings.Add(new RegressionFinding
                {
                    Severity = RegressionSeverity.Warning,
                    Category = "Network",
                    Description = "Network latency is higher than baseline.",
                    BaselineValue = $"{baselineP95:N0} ms (P95)",
                    CurrentValue = $"{currentMs:N0} ms",
                    PercentageChange = (currentMs - baselineP95) / baselineP95
                });
            }
        }
    }

    private async Task<PerformanceBaseline?> LoadLatestBaselineForConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_storageDirectory))
        {
            return null;
        }

        var hash = ComputeHash(connectionString);
        var files = Directory.GetFiles(_storageDirectory, $"*{FileExtension}", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var baseline = await TryReadBaselineAsync(file, cancellationToken).ConfigureAwait(false);
            if (baseline?.ConnectionStringHash == hash)
            {
                return baseline;
            }
        }

        return null;
    }

    private async Task<PerformanceBaseline?> LoadBaselineByNameAsync(
        string baselineName,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_storageDirectory))
        {
            return null;
        }

        var prefix = SanitizeFileName(baselineName) + "-";
        var files = Directory.GetFiles(_storageDirectory, $"*{FileExtension}", SearchOption.TopDirectoryOnly)
            .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var baseline = await TryReadBaselineAsync(file, cancellationToken).ConfigureAwait(false);
            if (baseline is not null)
            {
                return baseline;
            }
        }

        return null;
    }

    private static Task<PerformanceBaseline?> TryReadBaselineAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = File.ReadAllText(path);
            var baseline = JsonSerializer.Deserialize<PerformanceBaseline>(json, JsonOptions);
            return Task.FromResult<PerformanceBaseline?>(baseline);
        }
        catch
        {
            return Task.FromResult<PerformanceBaseline?>(null);
        }
    }

    private static double? CalculatePercentile(IList<double> values, double percentile)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var index = (int)Math.Round((sorted.Length - 1) * percentile, MidpointRounding.AwayFromZero);
        index = Math.Max(0, Math.Min(index, sorted.Length - 1));
        return sorted[index];
    }

    private static string ComputeHash(string input)
    {
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(invalidChars.Contains(c) ? '_' : c);
        }

        return builder.ToString();
    }

    private static async Task<DiagnosticReport> DefaultDiagnosticsRunner(string connectionString, CancellationToken cancellationToken)
    {
        await using var client = new SqlDiagnosticsClient();
        return await client.RunQuickCheckAsync(connectionString, cancellationToken).ConfigureAwait(false);
    }
}
