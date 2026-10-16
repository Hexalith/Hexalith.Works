using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Testing.Fakes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Refuses only trusted-origin audit persistence to exercise fail-closed acknowledgment.</summary>
internal sealed class OriginAuditUnavailableReadModelStore(InMemoryReadModelStore inner) : IReadModelStore
{
    /// <inheritdoc/>
    public Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default) where T : class
        => key == "eventstore:trusted-effect-origin:v1:denials"
            ? Task.FromException<ReadModelEntry<T>>(new IOException("synthetic private audit outage"))
            : inner.GetAsync<T>(storeName, key, cancellationToken);

    /// <inheritdoc/>
    public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
        => inner.SaveAsync(storeName, key, value, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
        => inner.TrySaveAsync(storeName, key, value, etag, cancellationToken);
}
