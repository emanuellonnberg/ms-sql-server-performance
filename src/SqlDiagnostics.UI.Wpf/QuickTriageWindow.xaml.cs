using System;
using System.Threading.Tasks;
using System.Windows;
using SqlDiagnostics.UI.Wpf.ViewModels;

namespace SqlDiagnostics.UI.Wpf;

public partial class QuickTriageWindow : Window
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
        catch (Exception ex)
        {
            _ = MessageBox.Show(this, $"Quick triage failed: {ex.Message}", "Quick Triage", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
