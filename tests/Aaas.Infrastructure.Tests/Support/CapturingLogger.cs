using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Aaas.Infrastructure.Tests.Support;

public sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception)
{
    /// <summary>Tout ce qu'un fournisseur de journaux pourrait écrire : message, puis exception (message + pile).</summary>
    public string FullText => Exception is null ? Message : $"{Message} {Exception}";
}

/// <summary>Journal en mémoire, thread-safe (le job écrit depuis un thread du pool).</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
}
