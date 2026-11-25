using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Holds a reminder actor's item read until the test releases its provider.</summary>
internal sealed class WorksDelayedReminderItemRead(IReadModelStore inner, string itemKey) : IReadModelStore
{
    private readonly TaskCompletionSource<CancellationToken> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the token supplied by the reminder actor to its provider.</summary>
    internal Task<CancellationToken> Entered => _entered.Task;

    /// <summary>Completes the provider wait so the actor can leave its turn.</summary>
    internal void Release() => _released.TrySetResult();

    /// <inheritdoc/>
    public async Task<ReadModelEntry<T>> GetAsync<T>(string storeName, string key, CancellationToken cancellationToken = default)
        where T : class
    {
        if (key == itemKey)
        {
            _entered.TrySetResult(cancellationToken);
            await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await inner.GetAsync<T>(storeName, key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task SaveAsync<T>(string storeName, string key, T value, CancellationToken cancellationToken = default) where T : class
        => inner.SaveAsync(storeName, key, value, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync<T>(string storeName, string key, T value, string etag, CancellationToken cancellationToken = default) where T : class
        => inner.TrySaveAsync(storeName, key, value, etag, cancellationToken);
}
