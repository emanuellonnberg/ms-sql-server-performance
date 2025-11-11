using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SqlDiagnostics.Core.Triage;

namespace SqlDiagnostics.UI.Wpf.ViewModels;

public sealed class QuickTriageViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<string> _progressMessages = new();
    private TriageResult? _result;
    private bool _isRunning;
    private string _statusMessage = "Ready to run triage.";
    private string _summary = "No triage run yet.";
    private string _durationText = "—";
    private string _category = "Unknown";
    private string _diagnosis = "Not run.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<string> ProgressMessages { get; }

    public QuickTriageViewModel()
    {
        ProgressMessages = new ReadOnlyObservableCollection<string>(_progressMessages);
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

    public async Task RunAsync(string connectionString)
    {
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        try
        {
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
        }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(nameof(RecommendationsText));
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
