using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Fails actual SDK reads without depending on the SDK's private persisted state types.</summary>
internal sealed class DiagnosticUnavailableReadModelStore(Exception failure) : IReadModelStore
{
    private int _reads;

    /// <summary>Gets the number of attempted SDK reads.</summary>
    internal int ReadCount => Volatile.Read(ref _reads);

    /// <inheritdoc/>
    public Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default) where T : class
    {
        Interlocked.Increment(ref _reads);
        return Task.FromException<ReadModelEntry<T>>(failure);
    }

    /// <inheritdoc/>
    public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
        => throw new InvalidOperationException("A diagnostic query must not write state.");

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
        => throw new InvalidOperationException("A diagnostic query must not write state.");
}
