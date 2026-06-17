using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Production <see cref="ICascadeCheckpointStore"/> backed by the shared Dapr state store through
/// <see cref="IReadModelStore"/> (Story 4.6, AC #4/#5). The key embeds the tenant id, parent work item id,
/// and parent terminal event type so each parent-terminal cascade owns exactly one durable, re-readable
/// checkpoint that survives an AppHost restart. Writes are last-write-wins because the dispatcher is the
/// single writer per cascade and every replay is idempotent.
/// </summary>
public sealed class ReadModelCascadeCheckpointStore(IReadModelStore store) : ICascadeCheckpointStore
{
    // Shared with the Story 4.5 read-model adapter and the actor state store (statestore.yaml).
    private const string StateStoreName = "statestore";

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
    public Task SaveAsync(CascadeCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        return _store.SaveAsync(
            StateStoreName,
            Key(checkpoint.TenantId, checkpoint.ParentWorkItemId, checkpoint.ParentTerminalEventType),
            checkpoint,
            cancellationToken);
    }

    private static string Key(string tenantId, string parentWorkItemId, string parentTerminalEventType)
        => $"projection:works:cascade-checkpoint:{tenantId}:{parentWorkItemId}:{parentTerminalEventType}";
}
