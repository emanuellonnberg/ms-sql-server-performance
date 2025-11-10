using System;
using System.Threading.Tasks;
using System.Windows;

namespace SqlDiagnostics.UI.Dialogs;

/// <summary>
/// Interaction logic for ConnectionQualityDialog.xaml.
/// </summary>
public partial class ConnectionQualityDialog : Window
{
    private readonly ConnectionQualityViewModel _viewModel;

    public ConnectionQualityDialog(ConnectionQualityDialogOptions options)
    {
        InitializeComponent();
        _viewModel = new ConnectionQualityViewModel(options);
        DataContext = _viewModel;

        Loaded += OnLoadedAsync;
        Closed += OnClosedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;

        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Unable to start monitoring.\n\n{ex.Message}",
                "Connection Quality Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private async void OnClosedAsync(object? sender, EventArgs e)
    {
        Closed -= OnClosedAsync;
        await _viewModel.DisposeAsync();
    }
}
