using System;
using System.Windows;
using SqlDiagnostics.UI.Dialogs;
using SqlDiagnostics.UI.Wpf.ViewModels;

namespace SqlDiagnostics.UI.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is RealtimeDiagnosticsViewModel vm)
        {
            await vm.DisposeAsync();
        }
    }

    private void OnShowConnectionQuality(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RealtimeDiagnosticsViewModel vm)
        {
            return;
        }

        var options = new ConnectionQualityDialogOptions
        {
            MonitoringInterval = TimeSpan.FromSeconds(5)
        };

        if (vm.IsMonitoring)
        {
            options.Monitor = vm.Monitor;
        }
        else if (!string.IsNullOrWhiteSpace(vm.ConnectionString))
        {
            options.ConnectionString = vm.ConnectionString;
        }
        else
        {
            MessageBox.Show(this,
                "Provide a connection string or start monitoring before opening the connection quality view.",
                "Connection Quality Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ConnectionQualityDialogLauncher.Show(options, this);
    }

    private void OnShowQuickTriage(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RealtimeDiagnosticsViewModel vm)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.ConnectionString))
        {
            MessageBox.Show(this,
                "Provide a connection string before running quick triage.",
                "Quick Triage",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new QuickTriageWindow(vm.ConnectionString)
        {
            Owner = this
        };

        window.Show();
    }

    private void OnShowFullDiagnostics(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RealtimeDiagnosticsViewModel vm)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.ConnectionString))
        {
            MessageBox.Show(this, "Provide a connection string before opening full diagnostics.", "Full Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new FullDiagnosticsWindow(vm.ConnectionString)
        {
            Owner = this
        };

        window.Show();
    }

    private void OnShowLogs(object sender, RoutedEventArgs e)
    {
        var window = new LogWindow
        {
            Owner = this
        };
        window.Show();
    }
}
