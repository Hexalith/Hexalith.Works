using System.Security.Cryptography;
using System.Text;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Testing.Fakes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Retains a genuinely noncooperative tenant-local read, including its original stale result.</summary>
internal sealed class DiscoveryBlockedReadModelStore(InMemoryReadModelStore inner, string tenant, string scopeHash, string mode) : IReadModelStore
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _invocations;

    /// <summary>Counts actual blocked provider invocations rather than abandoned caller attempts.</summary>
    internal int Invocations => Volatile.Read(ref _invocations);

    /// <summary>Repairs the provider; late results remain those captured before the repair.</summary>
    internal void Release() => _release.TrySetResult();

    /// <inheritdoc/>
    public async Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default) where T : class
    {
        bool matches = mode switch
        {
            "epoch" => key == "shared-epoch:" + scopeHash,
            "generation" => key.StartsWith("shared-generation:" + scopeHash + ":", StringComparison.Ordinal),
            _ => key.StartsWith("shared-control-index-tombstone:", StringComparison.Ordinal)
                && key.EndsWith(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenant))), StringComparison.Ordinal),
        };
        ReadModelEntry<T> captured = await inner.GetAsync<T>(storeName, key, cancellationToken).ConfigureAwait(false);
        if (matches && !_release.Task.IsCompleted)
        {
            Interlocked.Increment(ref _invocations);
            await _release.Task.ConfigureAwait(false); // Deliberately ignores cancellation and returns its old captured evidence.
        }
        return captured;
    }

    /// <inheritdoc/>
    public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
        => inner.SaveAsync(storeName, key, value, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
        => inner.TrySaveAsync(storeName, key, value, etag, cancellationToken);
}
