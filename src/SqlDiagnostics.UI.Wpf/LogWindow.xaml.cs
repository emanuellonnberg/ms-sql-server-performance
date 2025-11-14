using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlDiagnostics.UI.Wpf.Logging;

namespace SqlDiagnostics.UI.Wpf;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopySelected()));
    }

    private void OnCopySelected(object sender, RoutedEventArgs e) => CopySelected();

    private void OnCopyAll(object sender, RoutedEventArgs e) => CopyAll();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void CopySelected()
    {
        if (LogGrid.SelectedItem is not LogEntry entry)
        {
            return;
        }

        Clipboard.SetText(FormatEntry(entry));
    }

    private void CopyAll()
    {
        if (AppLog.Entries.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (var entry in AppLog.Entries)
        {
            builder.AppendLine(FormatEntry(entry));
        }

        Clipboard.SetText(builder.ToString());
    }

    private static string FormatEntry(LogEntry entry)
    {
        var builder = new StringBuilder();
        builder.Append($"[{entry.Timestamp:O}] [{entry.Level}] {entry.Source}: {entry.Message}");
        if (!string.IsNullOrWhiteSpace(entry.Details))
        {
            builder.AppendLine();
            builder.Append(entry.Details);
        }
        return builder.ToString();
    }
}
