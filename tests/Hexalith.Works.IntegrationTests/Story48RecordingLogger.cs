using Microsoft.Extensions.Logging;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Records structured log calls for deterministic Story 4.8 telemetry assertions.</summary>
/// <typeparam name="T">The logger category.</typeparam>
internal sealed class Story48RecordingLogger<T> : ILogger<T>
{
    /// <summary>Gets the recorded level, event id, rendered message, exception, and structured properties.</summary>
    public List<(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties)> Entries { get; } = [];

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (KeyValuePair<string, object?> value in values)
            {
                properties[value.Key] = value.Value;
            }
        }

        Entries.Add((logLevel, eventId, formatter(state, exception), exception, properties));
    }
}
