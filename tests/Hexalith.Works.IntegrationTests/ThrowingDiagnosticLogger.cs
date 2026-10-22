using Microsoft.Extensions.Logging;

namespace Hexalith.Works.IntegrationTests;

/// <summary>A provider that fails while exporting an otherwise enabled diagnostic.</summary>
/// <typeparam name="T">The logger category.</typeparam>
internal sealed class ThrowingDiagnosticLogger<T> : ILogger<T>
{
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => throw new IOException("synthetic diagnostic provider outage");
}
