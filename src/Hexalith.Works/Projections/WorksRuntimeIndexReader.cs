using System.Text.Json;

using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.Projections;

/// <summary>Reads both index formats through the SDK's generation selection and discovery admission.</summary>
internal static class WorksRuntimeIndexReader
{
    private static readonly string[] s_roots = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimePendingDateKey];

    /// <summary>Reads both indexes and optional related documents from the same selected SDK generation.</summary>
    internal static async Task<SharedProjectionReadResult<WorksRuntimeIndexView>> ReadAsync(
        SharedProjectionEpochCoordinator coordinator,
        SharedProjectionScope scope,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? relatedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> initial = await coordinator
            .ReadManyAsync<WorksRuntimeDocument>(scope, s_roots, cancellationToken).ConfigureAwait(false);
        return await ReadSelectedAsync(coordinator, scope, initial, relatedKeys ?? [], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the indexes and related documents together, requiring the previously selected roots.</summary>
    internal static Task<SharedProjectionReadResult<WorksRuntimeIndexView>> ReadRelatedAsync(
        SharedProjectionEpochCoordinator coordinator,
        SharedProjectionScope scope,
        SharedProjectionReadResult<WorksRuntimeIndexView> baseline,
        IReadOnlyList<string> relatedKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(baseline);
        return ReadSelectedAsync(coordinator, scope,
            new(baseline.Value?.Documents, baseline.Generation, baseline.IsStale, baseline.IsAvailable)
            {
                IsDegraded = baseline.IsDegraded,
            },
            relatedKeys, cancellationToken);
    }

    /// <summary>Reads both indexes inside an SDK-pinned ordinary delivery fold.</summary>
    internal static async Task<WorksRuntimeIndexes?> ReadPinnedAsync(
        SharedProjectionEpochCoordinator coordinator,
        SharedProjectionScope scope,
        long generation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        WorksRuntimeDocument? queue = await ReadAsync(WorksReadModelKeys.RuntimeQueueKey).ConfigureAwait(false);
        WorksRuntimeDocument? pending = await ReadAsync(WorksReadModelKeys.RuntimePendingDateKey).ConfigureAwait(false);

        if (queue is null && pending is null && generation == 0
            && await ReadAsync(WorksReadModelKeys.RuntimeTopologyKey).ConfigureAwait(false) is null)
        {
            // A scope with only epoch state or first-delivery parking has no initialized index format yet.
            return null;
        }

        if (queue is null || pending is null)
            throw new InvalidOperationException("The selected Works runtime generation is missing an index root.");

        var documents = new Dictionary<string, WorksRuntimeDocument?>(StringComparer.Ordinal);
        foreach (string key in WorksRuntimeIndexShards.ShardKeys(queue, pending))
            documents.Add(key, await ReadAsync(key).ConfigureAwait(false));
        return WorksRuntimeIndexShards.Reconstruct(queue, pending, documents);

        async Task<WorksRuntimeDocument?> ReadAsync(string key)
            => (await coordinator.ReadGenerationAsync<WorksRuntimeDocument>(scope, generation, key, cancellationToken)
                .ConfigureAwait(false)).Value;
    }

    /// <summary>Discovers membership only after all roots and shards pass the SDK's bounded discovery admission.</summary>
    internal static async Task<WorksRuntimeIndexes?> ReadDiscoveryAsync(
        SharedProjectionEpochCoordinator coordinator,
        SharedProjectionScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        SharedProjectionReadResult<WorksRuntimeDocument> queue = await ReadAsync(WorksReadModelKeys.RuntimeQueueKey).ConfigureAwait(false);
        if (!IsCurrent(queue))
            return null;

        SharedProjectionReadResult<WorksRuntimeDocument> pending = await ReadAsync(WorksReadModelKeys.RuntimePendingDateKey).ConfigureAwait(false);
        if (!IsCurrent(pending) || queue.Generation != pending.Generation)
            return null;

        var documents = new Dictionary<string, WorksRuntimeDocument?>(StringComparer.Ordinal);
        foreach (string key in WorksRuntimeIndexShards.ShardKeys(queue.Value!, pending.Value!))
        {
            SharedProjectionReadResult<WorksRuntimeDocument> shard = await ReadAsync(key).ConfigureAwait(false);
            if (!IsCurrent(shard) || shard.Generation != queue.Generation)
                return null;

            documents.Add(key, shard.Value);
        }

        WorksRuntimeIndexes indexes = WorksRuntimeIndexShards.Reconstruct(queue.Value!, pending.Value!, documents);
        // Discovery admission reads one key at a time. Recheck both roots to fence a promotion
        // or ordinary mutation during the scan, including mutations within the same generation.
        SharedProjectionReadResult<WorksRuntimeDocument> verifiedQueue = await ReadAsync(WorksReadModelKeys.RuntimeQueueKey).ConfigureAwait(false);
        SharedProjectionReadResult<WorksRuntimeDocument> verifiedPending = await ReadAsync(WorksReadModelKeys.RuntimePendingDateKey).ConfigureAwait(false);
        return SameRoot(queue, verifiedQueue) && SameRoot(pending, verifiedPending) ? indexes : null;

        Task<SharedProjectionReadResult<WorksRuntimeDocument>> ReadAsync(string key)
            => coordinator.ReadDiscoveryAsync<WorksRuntimeDocument>(scope, key, cancellationToken);
    }

    private static async Task<SharedProjectionReadResult<WorksRuntimeIndexView>> ReadSelectedAsync(
        SharedProjectionEpochCoordinator coordinator,
        SharedProjectionScope scope,
        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> initial,
        IReadOnlyList<string> relatedKeys,
        CancellationToken cancellationToken)
    {
        if (!initial.IsAvailable || initial.Value is null
            || initial.Value[WorksReadModelKeys.RuntimeQueueKey] is not { } queue
            || initial.Value[WorksReadModelKeys.RuntimePendingDateKey] is not { } pending)
        {
            return new(null, initial.Generation, initial.IsStale, false) { IsDegraded = initial.IsDegraded };
        }

        SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> selected = initial;
        try
        {
            IReadOnlyList<string> shards = WorksRuntimeIndexShards.ShardKeys(queue, pending);
            if (shards.Count > 0 || relatedKeys.Count > 0)
            {
                selected = await coordinator.ReadManyAsync<WorksRuntimeDocument>(
                    scope, [.. s_roots, .. shards, .. relatedKeys], cancellationToken).ConfigureAwait(false);
                if (!selected.IsAvailable || selected.Value is null)
                    return new(null, selected.Generation, selected.IsStale, false) { IsDegraded = selected.IsDegraded };

                if (selected.Generation != initial.Generation
                    || s_roots.Any(key => !SameContents(initial.Value[key], selected.Value[key])))
                {
                    return new(null, selected.Generation, true, false) { IsDegraded = selected.IsDegraded };
                }
            }

            IReadOnlyDictionary<string, WorksRuntimeDocument?> documents = selected.Value!;
            WorksRuntimeIndexes indexes = WorksRuntimeIndexShards.Reconstruct(
                documents[WorksReadModelKeys.RuntimeQueueKey]!, documents[WorksReadModelKeys.RuntimePendingDateKey]!, documents);
            return new(new WorksRuntimeIndexView(indexes, documents), selected.Generation, selected.IsStale, true)
            {
                IsDegraded = selected.IsDegraded,
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            throw new WorksRuntimeIndexReadException(selected.Generation, selected.IsStale, exception);
        }
    }

    private static bool IsCurrent(SharedProjectionReadResult<WorksRuntimeDocument> read)
        => read.IsAvailable && !read.IsDegraded && !read.IsStale && read.Value is not null;

    private static bool SameRoot(
        SharedProjectionReadResult<WorksRuntimeDocument> baseline, SharedProjectionReadResult<WorksRuntimeDocument> selected)
        => IsCurrent(selected) && baseline.Generation == selected.Generation
            && string.Equals(baseline.SelectedETag, selected.SelectedETag, StringComparison.Ordinal)
            && SameContents(baseline.Value, selected.Value);

    private static bool SameContents(WorksRuntimeDocument? first, WorksRuntimeDocument? second)
        => first is not null && second is not null && string.Equals(first.Kind, second.Kind, StringComparison.Ordinal)
            && JsonElement.DeepEquals(first.Value, second.Value);
}
