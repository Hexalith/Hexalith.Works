using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Synchronizes the first publication writes against the same sweep ETag.</summary>
internal sealed class WorksProcessSweepContentionStore(IReadModelStore inner, string initialETag, int participants) : IReadModelStore
{
    private readonly TaskCompletionSource _firstWave = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _conflicts;

    /// <summary>Gets the number of conditional sweep writes actually refused by the store.</summary>
    internal int Conflicts => _conflicts;

    /// <inheritdoc/>
    public Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default) where T : class
        => inner.GetAsync<T>(storeName, key, cancellationToken);

    /// <inheritdoc/>
    public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
        => inner.SaveAsync(storeName, key, value, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
    {
        bool sweep = string.Equals(key, ProcessStateKeys.Sweep, StringComparison.Ordinal);
        if (sweep && string.Equals(etag, initialETag, StringComparison.Ordinal))
        {
            if (Interlocked.Increment(ref _started) == participants)
                _firstWave.TrySetResult();
            await _firstWave.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        bool saved = await inner.TrySaveAsync(storeName, key, value, etag, cancellationToken).ConfigureAwait(false);
        if (sweep && !saved)
            Interlocked.Increment(ref _conflicts);
        return saved;
    }
}
