using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SqlDiagnostics.UI.Wpf.ViewModels;
using SqlDiagnostics.Core.Utilities;

namespace SqlDiagnostics.UI.Wpf;

public partial class FullDiagnosticsWindow : Window
{
    private readonly string _connectionString;
    private bool _disposed;

    public FullDiagnosticsWindow(string connectionString)
    {
        InitializeComponent();
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FullDiagnosticsViewModel vm)
        {
            return;
        }

        var result = await vm.InitializeAsync(_connectionString).ConfigureAwait(true);
        if (!result.Succeeded && result.ErrorMessage is not null && result.MissingPermissions.Count == 0)
        {
            var message = BuildFailureMessage(result);
            _ = MessageBox.Show(this, message, "Full Diagnostics Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            await vm.DisposeAsync().ConfigureAwait(true);
            Close();
        }
    }

    private static string BuildFailureMessage(PermissionCheckResult result)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            builder.AppendLine(result.ErrorMessage);
        }

        if (result.MissingPermissions.Count > 0)
        {
            builder.AppendLine("Missing permissions:");
            foreach (var permission in result.MissingPermissions.Distinct())
            {
                builder.AppendLine($" • {permission}");
            }
        }

        if (builder.Length == 0)
        {
            builder.AppendLine("Full diagnostics could not start due to insufficient permissions.");
        }

        return builder.ToString();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (DataContext is FullDiagnosticsViewModel vm)
        {
            await vm.DisposeAsync().ConfigureAwait(true);
        }
    }
}
