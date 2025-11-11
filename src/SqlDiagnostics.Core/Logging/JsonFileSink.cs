using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SqlDiagnostics.Core.Logging;

/// <summary>
/// Persists diagnostic events as newline-delimited JSON.
/// </summary>
public sealed class JsonFileSink : IDiagnosticSink
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonFileSink(string filePath, JsonSerializerOptions? serializerOptions = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must be specified.", nameof(filePath));
        }

        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        _serializerOptions = serializerOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }

    public async Task WriteAsync(IReadOnlyCollection<DiagnosticEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        var lines = new List<string>(events.Count);
        foreach (var evt in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add(JsonSerializer.Serialize(evt, _serializerOptions));
        }

        lock (_gate)
        {
            using var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            foreach (var line in lines)
            {
                writer.WriteLine(line);
            }
            writer.Flush();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
