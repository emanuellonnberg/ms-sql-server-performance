using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Logging;

/// <summary>
/// Writes plain-text summaries to a rolling log file set.
/// </summary>
public sealed class RollingFileSink : IDiagnosticSink
{
    private readonly string _basePath;
    private readonly int _maxFiles;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public RollingFileSink(string basePath, int maxFiles, int maxFileSizeMegabytes)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path must be specified.", nameof(basePath));
        }

        if (maxFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles), "At least one file must be kept.");
        }

        if (maxFileSizeMegabytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeMegabytes), "File size must be positive.");
        }

        _basePath = basePath;
        _maxFiles = maxFiles;
        _maxBytes = maxFileSizeMegabytes * 1024L * 1024L;

        Directory.CreateDirectory(Path.GetDirectoryName(_basePath)!);
    }

    public Task WriteAsync(IReadOnlyCollection<DiagnosticEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            using var stream = new FileStream(_basePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, Encoding.UTF8);

            foreach (var evt in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine($"[{evt.TimestampUtc:O}] {evt.Severity,-7} {evt.EventType,-10} {evt.Message ?? "<no message>"}");
                if (evt.Data is not null)
                {
                    writer.WriteLine($"    payload: {System.Text.Json.JsonSerializer.Serialize(evt.Data)}");
                }
            }

            writer.Flush();
        }

        RotateIfNeeded();
        return Task.CompletedTask;
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_basePath);
        if (!info.Exists || info.Length <= _maxBytes)
        {
            return;
        }

        for (var index = _maxFiles - 1; index >= 1; index--)
        {
            var source = index == 1 ? _basePath : $"{_basePath}.{index - 1}";
            var destination = $"{_basePath}.{index}";

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
    }
}
