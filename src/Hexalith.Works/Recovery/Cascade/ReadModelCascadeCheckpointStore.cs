using Hexalith.EventStore.Client.Projections;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Production <see cref="ICascadeCheckpointStore"/> backed by the shared Dapr state store through
/// <see cref="IReadModelStore"/> (Story 4.6, AC #4/#5). The key embeds the tenant id, parent work item id,
/// and parent terminal event type so each parent-terminal cascade owns exactly one durable, re-readable
/// checkpoint that survives an AppHost restart. Writes are last-write-wins because the dispatcher is the
/// single writer per cascade and every replay is idempotent.
/// </summary>
public sealed class ReadModelCascadeCheckpointStore(
    IReadModelStore store,
    ILogger<ReadModelCascadeCheckpointStore> logger) : ICascadeCheckpointStore, ICascadeCheckpointIndex
{
    // Shared with the Story 4.5 read-model adapter and the actor state store (statestore.yaml).
    private const string StateStoreName = "statestore";
    private const string IndexKey = "projection:works:cascade-checkpoint-index";

    private readonly ILogger<ReadModelCascadeCheckpointStore> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IReadModelStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc/>
    public async Task<CascadeCheckpoint?> GetAsync(string tenantId, string parentWorkItemId, string parentTerminalEventType, CancellationToken cancellationToken = default)
    {
        ReadModelEntry<CascadeCheckpoint> entry = await _store
            .GetAsync<CascadeCheckpoint>(StateStoreName, Key(tenantId, parentWorkItemId, parentTerminalEventType), cancellationToken)
            .ConfigureAwait(false);

        return entry.Value;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(CascadeCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var identity = new CascadeCheckpointIdentity(
            checkpoint.TenantId,
            checkpoint.ParentWorkItemId,
            checkpoint.ParentTerminalEventType);

        // Add discovery before the first incomplete checkpoint write: a crash can leave a harmless dangling
        // identity, but can never leave an incomplete durable checkpoint undiscoverable.
        if (!checkpoint.Completed)
        {
            await UpdateIndexAsync(identity, add: true, cancellationToken).ConfigureAwait(false);
        }

        await _store.SaveAsync(
            StateStoreName,
            Key(checkpoint.TenantId, checkpoint.ParentWorkItemId, checkpoint.ParentTerminalEventType),
            checkpoint,
            cancellationToken).ConfigureAwait(false);

        // Remove discovery only after the completed checkpoint itself is durable.
        if (checkpoint.Completed)
        {
            await UpdateIndexAsync(identity, add: false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CascadeCheckpointIdentity>> GetIncompleteAsync(CancellationToken cancellationToken = default)
    {
        ReadModelEntry<CascadeCheckpointIndex> entry = await _store
            .GetAsync<CascadeCheckpointIndex>(StateStoreName, IndexKey, cancellationToken)
            .ConfigureAwait(false);
        return entry.Value?.Entries ?? [];
    }

    /// <inheritdoc/>
    public Task RemoveIncompleteAsync(CascadeCheckpointIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return UpdateIndexAsync(identity, add: false, cancellationToken);
    }

    private static string Key(string tenantId, string parentWorkItemId, string parentTerminalEventType)
        => $"projection:works:cascade-checkpoint:{tenantId}:{parentWorkItemId}:{parentTerminalEventType}";

    private Task UpdateIndexAsync(
        CascadeCheckpointIdentity identity,
        bool add,
        CancellationToken cancellationToken)
    {
        return ReadModelWritePolicy.UpdateAsync<CascadeCheckpointIndex>(
            _store,
            StateStoreName,
            IndexKey,
            current =>
            {
                var entries = (current?.Entries ?? [])
                    .Where(value => value != identity)
                    .ToList();
                if (add)
                {
                    entries.Add(identity);
                }

                return new CascadeCheckpointIndex
                {
                    Entries = [.. entries.OrderBy(static value => value.TenantId, StringComparer.Ordinal)
                        .ThenBy(static value => value.ParentWorkItemId, StringComparer.Ordinal)
                        .ThenBy(static value => value.ParentTerminalEventType, StringComparer.Ordinal)],
                };
            },
            new ReadModelWriteContext(
                Category: "works cascade checkpoint index",
                ProjectionType: "works-cascade-checkpoints"),
            _logger,
            cancellationToken: cancellationToken);
    }
}
