using System;
using System.Windows;
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
}
