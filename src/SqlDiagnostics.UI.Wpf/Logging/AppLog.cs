using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

namespace SqlDiagnostics.UI.Wpf.Logging;

public static class AppLog
{
    private const int MaxEntries = 2000;
    private static readonly ObservableCollection<LogEntry> _entries = new();
    public static ReadOnlyObservableCollection<LogEntry> Entries { get; } = new(_entries);

    public static void Info(string source, string message) =>
        Add(LogLevel.Info, source, message, null);

    public static void Warning(string source, string message, Exception? exception = null) =>
        Add(LogLevel.Warning, source, message, exception);

    public static void Error(string source, string message, Exception? exception = null) =>
        Add(LogLevel.Error, source, message, exception);

    private static void Add(LogLevel level, string source, string message, Exception? exception)
    {
        void Append()
        {
            if (_entries.Count >= MaxEntries)
            {
                _entries.RemoveAt(0);
            }

            _entries.Add(new LogEntry(DateTimeOffset.Now, level, source, message, exception?.ToString()));
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Append);
        }
        else
        {
            Append();
        }

        if (Debugger.IsAttached)
        {
            Debug.WriteLine($"[{level}] {source}: {message}");
            if (exception is not null)
            {
                Debug.WriteLine(exception);
            }
        }
    }
}

public readonly record struct LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Source, string Message, string? Details);

public enum LogLevel
{
    Info,
    Warning,
    Error
}
