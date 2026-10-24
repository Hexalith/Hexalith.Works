using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Captures SDK recovery diagnostics across background passes without depending on internal logger types.</summary>
internal sealed class WorksReminderRecoveryLogCapture : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<(int EventId, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties)> _entries = new();

    /// <summary>Gets a stable snapshot of the structured diagnostics.</summary>
    internal (int EventId, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Properties)[] Entries
        => [.. _entries];

    /// <summary>Gets or sets an observer invoked at a recorded diagnostic boundary.</summary>
    internal Action<int, IReadOnlyDictionary<string, object?>>? Recorded { get; set; }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => this;

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        _entries.Enqueue((eventId.Id, formatter(state, exception), exception, properties));
        Recorded?.Invoke(eventId.Id, properties);
    }

    /// <inheritdoc/>
    public void Dispose() { }
}
