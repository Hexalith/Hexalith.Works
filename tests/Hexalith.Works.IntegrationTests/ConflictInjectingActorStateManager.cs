using Dapr.Actors.Runtime;

using Hexalith.EventStore.Server.Events;
using Hexalith.EventStore.Testing.Fakes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Forwards actor state operations to an in-memory store while injecting a bounded number of
/// conflicts only when an EventStore event append is staged for the next atomic save.
/// </summary>
/// <param name="inner">The in-memory state manager that owns committed and pending actor state.</param>
/// <param name="eventBatchConflicts">The number of staged event-batch saves that should conflict.</param>
/// <param name="commitExternalWinnerAsync">Commits the competing winner when the first conflict is injected.</param>
internal sealed class ConflictInjectingActorStateManager(
    InMemoryStateManager inner,
    int eventBatchConflicts,
    Func<CancellationToken, Task> commitExternalWinnerAsync) : IActorStateManager
{
    private readonly HashSet<string> _stagedEventKeys = [];
    private int _remainingEventBatchConflicts = eventBatchConflicts >= 0
        ? eventBatchConflicts
        : throw new ArgumentOutOfRangeException(nameof(eventBatchConflicts));
    private bool _externalWinnerCommitted;

    /// <summary>Gets the underlying committed actor state.</summary>
    internal IReadOnlyDictionary<string, object> CommittedState => inner.CommittedState;

    /// <summary>Gets the number of event-batch saves observed by the decorator.</summary>
    internal int EventBatchSaveAttemptCount { get; private set; }

    /// <summary>Gets the number of conflicts injected by the decorator.</summary>
    internal int InjectedConflictCount { get; private set; }

    /// <summary>Gets the number of competing-winner commits performed by the decorator.</summary>
    internal int ExternalWinnerCommitCount { get; private set; }

    /// <inheritdoc/>
    public async Task AddStateAsync<T>(string stateName, T value, CancellationToken cancellationToken = default)
    {
        await inner.AddStateAsync(stateName, value, cancellationToken).ConfigureAwait(false);
        TrackEventAppend(stateName, value);
    }

    /// <inheritdoc/>
    public async Task AddStateAsync<T>(string stateName, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        await inner.AddStateAsync(stateName, value, ttl, cancellationToken).ConfigureAwait(false);
        TrackEventAppend(stateName, value);
    }

    /// <inheritdoc/>
    public async Task<T> AddOrUpdateStateAsync<T>(
        string stateName,
        T addValue,
        Func<string, T, T> updateValueFactory,
        CancellationToken cancellationToken = default)
    {
        T value = await inner
            .AddOrUpdateStateAsync(stateName, addValue, updateValueFactory, cancellationToken)
            .ConfigureAwait(false);
        TrackEventAppend(stateName, value);
        return value;
    }

    /// <inheritdoc/>
    public async Task<T> AddOrUpdateStateAsync<T>(
        string stateName,
        T addValue,
        Func<string, T, T> updateValueFactory,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        T value = await inner
            .AddOrUpdateStateAsync(stateName, addValue, updateValueFactory, ttl, cancellationToken)
            .ConfigureAwait(false);
        TrackEventAppend(stateName, value);
        return value;
    }

    /// <inheritdoc/>
    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        _stagedEventKeys.Clear();
        await inner.ClearCacheAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<bool> ContainsStateAsync(string stateName, CancellationToken cancellationToken = default)
        => inner.ContainsStateAsync(stateName, cancellationToken);

    /// <inheritdoc/>
    public async Task<T> GetOrAddStateAsync<T>(string stateName, T value, CancellationToken cancellationToken = default)
    {
        bool existed = await inner.ContainsStateAsync(stateName, cancellationToken).ConfigureAwait(false);
        T result = await inner.GetOrAddStateAsync(stateName, value, cancellationToken).ConfigureAwait(false);
        if (!existed)
        {
            TrackEventAppend(stateName, result);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<T> GetOrAddStateAsync<T>(
        string stateName,
        T value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        bool existed = await inner.ContainsStateAsync(stateName, cancellationToken).ConfigureAwait(false);
        T result = await inner.GetOrAddStateAsync(stateName, value, ttl, cancellationToken).ConfigureAwait(false);
        if (!existed)
        {
            TrackEventAppend(stateName, result);
        }

        return result;
    }

    /// <inheritdoc/>
    public Task<T> GetStateAsync<T>(string stateName, CancellationToken cancellationToken = default)
        => inner.GetStateAsync<T>(stateName, cancellationToken);

    /// <inheritdoc/>
    public async Task RemoveStateAsync(string stateName, CancellationToken cancellationToken = default)
    {
        _ = _stagedEventKeys.Remove(stateName);
        await inner.RemoveStateAsync(stateName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveStateAsync(CancellationToken cancellationToken = default)
    {
        if (_stagedEventKeys.Count == 0)
        {
            await inner.SaveStateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        EventBatchSaveAttemptCount++;
        _stagedEventKeys.Clear();

        if (_remainingEventBatchConflicts == 0)
        {
            await inner.SaveStateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _remainingEventBatchConflicts--;
        InjectedConflictCount++;
        await inner.ClearCacheAsync(cancellationToken).ConfigureAwait(false);

        if (!_externalWinnerCommitted)
        {
            await commitExternalWinnerAsync(cancellationToken).ConfigureAwait(false);
            _externalWinnerCommitted = true;
            ExternalWinnerCommitCount++;
        }

        throw new InvalidOperationException("Injected actor-state event-batch conflict.");
    }

    /// <inheritdoc/>
    public async Task SetStateAsync<T>(string stateName, T value, CancellationToken cancellationToken = default)
    {
        await inner.SetStateAsync(stateName, value, cancellationToken).ConfigureAwait(false);
        TrackEventAppend(stateName, value);
    }

    /// <inheritdoc/>
    public async Task SetStateAsync<T>(
        string stateName,
        T value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        await inner.SetStateAsync(stateName, value, ttl, cancellationToken).ConfigureAwait(false);
        TrackEventAppend(stateName, value);
    }

    /// <inheritdoc/>
    public async Task<bool> TryAddStateAsync<T>(string stateName, T value, CancellationToken cancellationToken = default)
    {
        bool added = await inner.TryAddStateAsync(stateName, value, cancellationToken).ConfigureAwait(false);
        if (added)
        {
            TrackEventAppend(stateName, value);
        }

        return added;
    }

    /// <inheritdoc/>
    public async Task<bool> TryAddStateAsync<T>(
        string stateName,
        T value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        bool added = await inner.TryAddStateAsync(stateName, value, ttl, cancellationToken).ConfigureAwait(false);
        if (added)
        {
            TrackEventAppend(stateName, value);
        }

        return added;
    }

    /// <inheritdoc/>
    public Task<ConditionalValue<T>> TryGetStateAsync<T>(string stateName, CancellationToken cancellationToken = default)
        => inner.TryGetStateAsync<T>(stateName, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> TryRemoveStateAsync(string stateName, CancellationToken cancellationToken = default)
    {
        _ = _stagedEventKeys.Remove(stateName);
        return await inner.TryRemoveStateAsync(stateName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UnloadStateAsync(
        string stateName,
        UnloadStateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = _stagedEventKeys.Remove(stateName);
        await inner.UnloadStateAsync(stateName, options, cancellationToken).ConfigureAwait(false);
    }

    private void TrackEventAppend<T>(string stateName, T value)
    {
        if (value is EventEnvelope)
        {
            _ = _stagedEventKeys.Add(stateName);
        }
    }
}
