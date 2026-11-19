using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SynapseAdmin.Tests.TestSupport;

/// <summary>One recorded log call: the message template (<c>{OriginalFormat}</c>) and the named arguments.</summary>
internal sealed record RecordedLog(LogLevel Level, string? Template, IReadOnlyDictionary<string, object?> Arguments, Exception? Exception);

/// <summary>Logger double that records every call with its template and named arguments; all levels are enabled.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<RecordedLog> _entries = new();

    public IReadOnlyList<RecordedLog> Entries => _entries.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = (state as IEnumerable<KeyValuePair<string, object?>> ?? []).ToList();
        var template = properties.FirstOrDefault(p => p.Key == "{OriginalFormat}").Value as string;
        var arguments = properties.Where(p => p.Key != "{OriginalFormat}").ToDictionary(p => p.Key, p => p.Value);
        _entries.Enqueue(new RecordedLog(logLevel, template, arguments, exception));
    }
}
