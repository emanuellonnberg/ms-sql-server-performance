using System;
using System.Threading.Tasks;
using System.Windows;
using SqlDiagnostics.UI.Wpf.ViewModels;
using Microsoft.Win32;
using System.IO;
using SqlDiagnostics.UI.Dialogs;

namespace SqlDiagnostics.UI.Wpf;

public partial class QuickTriageWindow : Window
    private void OnRecommendationLinkClick(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to open link: {ex.Message}", "Quick Triage", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }
{
    private readonly string _connectionString;
    private bool _initialized;

    public QuickTriageWindow(string connectionString)
    {
        InitializeComponent();
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        if (DataContext is QuickTriageViewModel vm)
        {
            await RunTriageAsync(vm).ConfigureAwait(true);
        }
    }

    private async void OnRunAgainClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is QuickTriageViewModel vm)
        {
            await RunTriageAsync(vm).ConfigureAwait(true);
        }
    }

    private async Task RunTriageAsync(QuickTriageViewModel vm)
    {
        try
        {
            await vm.RunAsync(_connectionString).ConfigureAwait(true);
        }
        catch
        {
            // The view model already reports the error in the UI.
        }
    }

    private void OnOpenConnectionQualityClick(object sender, RoutedEventArgs e)
    {
        var options = new ConnectionQualityDialogOptions
        {
            ConnectionString = _connectionString,
            MonitoringInterval = TimeSpan.FromSeconds(5)
        };

        ConnectionQualityDialogLauncher.Show(options, this);
    }

    private void OnOpenFullDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var window = new FullDiagnosticsWindow(_connectionString)
        {
            Owner = this
        };

        window.Show();
    }

    private void OnExportJsonClick(object sender, RoutedEventArgs e) =>
        ExportReport(vm => vm.BuildJsonReport(), "JSON Files (*.json)|*.json|All Files (*.*)|*.*", ".json");

    private void OnExportMarkdownClick(object sender, RoutedEventArgs e) =>
        ExportReport(vm => vm.BuildMarkdownReport(), "Markdown Files (*.md)|*.md|Text Files (*.txt)|*.txt|All Files (*.*)|*.*", ".md");

    private void ExportReport(Func<QuickTriageViewModel, string> reportBuilder, string filter, string defaultExtension)
    {
        if (DataContext is not QuickTriageViewModel vm)
        {
            return;
        }

        if (!vm.HasResult)
        {
            _ = MessageBox.Show(this,
                "Run quick triage before exporting a report.",
                "Quick Triage",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
            FileName = $"quick-triage-{DateTime.UtcNow:yyyyMMdd-HHmmss}{defaultExtension}"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var content = reportBuilder(vm);
            File.WriteAllText(dialog.FileName, content);
            _ = MessageBox.Show(this, $"Report saved to {dialog.FileName}", "Quick Triage", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _ = MessageBox.Show(this, $"Failed to save report: {ex.Message}", "Quick Triage", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
