using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SqlDiagnostics.Core.Triage;
using System.Text.Json;

namespace SqlDiagnostics.UI.Wpf.ViewModels;

public sealed class QuickTriageViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<string> _progressMessages = new();
    private readonly ObservableCollection<TriageTestViewModel> _tests = new();
    private TriageResult? _result;
    private bool _isRunning;
    private string _statusMessage = "Ready to run triage.";
    private string _summary = "No triage run yet.";
    private string _durationText = "—";
    private string _category = "Unknown";
    private string _diagnosis = "Not run.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<string> ProgressMessages { get; }
    public ReadOnlyObservableCollection<TriageTestViewModel> Tests { get; }

    public QuickTriageViewModel()
    {
        ProgressMessages = new ReadOnlyObservableCollection<string>(_progressMessages);
        Tests = new ReadOnlyObservableCollection<TriageTestViewModel>(_tests);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanRun));
            }
        }
    }

    public bool CanRun => !IsRunning;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    public string DurationText
    {
        get => _durationText;
        private set => SetField(ref _durationText, value);
    }

    public string Category
    {
        get => _category;
        private set => SetField(ref _category, value);
    }

    public string Diagnosis
    {
        get => _diagnosis;
        private set => SetField(ref _diagnosis, value);
    }

    public string RecommendationsText => _result is not null && _result.Diagnosis.Recommendations.Count > 0
        ? string.Join(Environment.NewLine, _result.Diagnosis.Recommendations)
        : "No recommendations.";

    public bool HasResult => _result is not null;

    public async Task RunAsync(string connectionString)
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
            }

            IsRunning = true;
            StatusMessage = "Running quick triage…";
            _progressMessages.Clear();

            var progress = new Progress<string>(message =>
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.CheckAccess())
                {
                    _progressMessages.Add(message);
                }
                else
                {
                    dispatcher.Invoke(() => _progressMessages.Add(message));
                }
            });

            var result = await QuickTriage.RunAsync(connectionString, options: null, progress, default).ConfigureAwait(false);
            _result = result;

            UpdateFromResult(result);
            StatusMessage = "Quick triage completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Triage failed: {ex.Message}";
            _result = null;
            Summary = "Triage failed.";
            Category = "Unknown";
            Diagnosis = ex.Message;
            DurationText = "—";
            _tests.Clear();
        }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(nameof(RecommendationsText));
            OnPropertyChanged(nameof(HasResult));
        }
    }

    private void UpdateFromResult(TriageResult result)
    {
        DurationText = $"{result.Duration.TotalSeconds:N1} seconds";
        Category = result.Diagnosis.Category;
        Diagnosis = result.Diagnosis.Summary;

        var builder = new StringBuilder();
        builder.AppendLine($"Connection: {(result.Connection.Success ? "OK" : "FAIL")} - {result.Connection.Details}");
        builder.AppendLine($"Network: {(result.Network.Success ? "OK" : "FAIL")} - {result.Network.Details}");
        builder.AppendLine($"Query: {(result.Query.Success ? "OK" : "FAIL")} - {result.Query.Details}");
        builder.AppendLine($"Server: {(result.Server.Success ? "OK" : "FAIL")} - {result.Server.Details}");
        builder.AppendLine($"Blocking: {(result.Blocking.Success ? "OK" : "FAIL")} - {result.Blocking.Details}");

        Summary = builder.ToString();
        UpdateTests(result);
        OnPropertyChanged(nameof(RecommendationsText));
        OnPropertyChanged(nameof(HasResult));
    }

    private void UpdateTests(TriageResult result)
    {
        _tests.Clear();
        _tests.Add(CreateTestViewModel(result.Network));
        _tests.Add(CreateTestViewModel(result.Connection));
        _tests.Add(CreateTestViewModel(result.Query));
        _tests.Add(CreateTestViewModel(result.Server));
        _tests.Add(CreateTestViewModel(result.Blocking));
    }

    private static TriageTestViewModel CreateTestViewModel(TestResult result) =>
        new(result.Name, result.Success, result.Details, result.Duration, result.Issues);

    public string BuildJsonReport()
    {
        if (_result is null)
        {
            throw new InvalidOperationException("Quick triage has not been run yet.");
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        return JsonSerializer.Serialize(_result, options);
    }

    public string BuildMarkdownReport()
    {
        if (_result is null)
        {
            throw new InvalidOperationException("Quick triage has not been run yet.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Quick Triage Report");
        builder.AppendLine();
        builder.AppendLine($"- **Started:** {_result.StartedAtUtc:O}");
        builder.AppendLine($"- **Completed:** {_result.CompletedAtUtc:O}");
        builder.AppendLine($"- **Duration:** {_result.Duration.TotalSeconds:N1} seconds");
        builder.AppendLine($"- **Diagnosis:** {_result.Diagnosis.Category} – {_result.Diagnosis.Summary}");
        builder.AppendLine();
        builder.AppendLine("## Tests");

        IEnumerable<TestResult> tests = new[]
        {
            _result.Network,
            _result.Connection,
            _result.Query,
            _result.Server,
            _result.Blocking
        };

        foreach (var test in tests)
        {
            builder.AppendLine($"### {test.Name}");
            builder.AppendLine($"- Status: {(test.Success ? "Pass" : "Fail")}");
            builder.AppendLine($"- Details: {test.Details}");
            builder.AppendLine($"- Duration: {(test.Duration > TimeSpan.Zero ? $"{test.Duration.TotalMilliseconds:N0} ms" : "n/a")}");
            if (test.Issues.Count > 0)
            {
                builder.AppendLine("- Issues:");
                foreach (var issue in test.Issues)
                {
                    builder.AppendLine($"  - {issue}");
                }
            }
            else
            {
                builder.AppendLine("- Issues: None");
            }
            builder.AppendLine();
        }

        if (_result.Diagnosis.Recommendations.Count > 0)
        {
            builder.AppendLine("## Recommendations");
            foreach (var recommendation in _result.Diagnosis.Recommendations)
            {
                builder.AppendLine($"- {recommendation}");
            }
        }

        return builder.ToString();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TriageTestViewModel
{
    public TriageTestViewModel(string name, bool success, string details, TimeSpan duration, IReadOnlyList<string> issues)
    {
        Name = name;
        Success = success;
        Details = details;
        Duration = duration;
        Issues = issues;
    }

    public string Name { get; }
    public bool Success { get; }
    public string Details { get; }
    public TimeSpan Duration { get; }
    public IReadOnlyList<string> Issues { get; }

    public string StatusText => Success ? "Pass" : "Fail";

    public string DurationDisplay => Duration > TimeSpan.Zero
        ? $"{Duration.TotalMilliseconds:N0} ms"
        : "n/a";

    public bool HasIssues => Issues.Count > 0;
}
