using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Injects controllable read-admission and publication faults over serialized SDK state.</summary>
internal sealed class WorksProcessRecoveryFaultStore(IReadModelStore inner) : IReadModelStore
{
    /// <summary>Gets or sets the selector for refused conditional writes.</summary>
    internal Func<string, bool>? RejectWrite { get; set; }

    /// <summary>Gets or sets the factory for injected read failures.</summary>
    internal Func<string, Exception?>? ReadFailure { get; set; }

    /// <summary>Gets the number of injected read failures.</summary>
    internal int RefusedReads { get; private set; }

    /// <inheritdoc/>
    public Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default) where T : class
    {
        if (ReadFailure?.Invoke(key) is { } failure)
        {
            RefusedReads++;
            return Task.FromException<ReadModelEntry<T>>(failure);
        }
        return inner.GetAsync<T>(storeName, key, cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
        => inner.SaveAsync(storeName, key, value, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
        => RejectWrite?.Invoke(key) == true ? Task.FromResult(false) : inner.TrySaveAsync(storeName, key, value, etag, cancellationToken);
}
