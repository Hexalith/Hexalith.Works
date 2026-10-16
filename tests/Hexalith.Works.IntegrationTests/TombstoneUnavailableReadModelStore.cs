using System.Security.Cryptography;
using System.Text;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Testing.Fakes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Fails only a selected tenant's control-index tombstone read; all retained SDK state remains real.</summary>
internal sealed class TombstoneUnavailableReadModelStore(InMemoryReadModelStore inner, string tenant) : IReadModelStore
{
    /// <summary>Whether the selected tombstone is currently unavailable.</summary>
    internal bool Unavailable { get; set; } = true;

    /// <inheritdoc/>
    public Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default) where T : class
        => Unavailable && key.StartsWith("shared-control-index-tombstone:", StringComparison.Ordinal)
            && key.EndsWith(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenant))), StringComparison.Ordinal)
            ? Task.FromException<ReadModelEntry<T>>(new IOException("synthetic tenant-local tombstone outage"))
            : inner.GetAsync<T>(storeName, key, cancellationToken);

    /// <inheritdoc/>
    public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
        => inner.SaveAsync(storeName, key, value, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
        => inner.TrySaveAsync(storeName, key, value, etag, cancellationToken);
}
