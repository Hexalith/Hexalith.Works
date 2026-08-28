using Hexalith.Works.Reminders;

namespace Hexalith.Works.Projections;

/// <summary>
/// The persisted tenant-scoped index of pending <c>DateReached</c> awaits (Story 4.8). A single entry per tenant
/// maps a work item's raw id (<c>WorkItemId.Value</c>) to its currently-pending date awaits. The runtime
/// projection adapter (<see cref="WorkItemProjectionDispatcher"/>) upserts an aggregate's entry while it holds
/// pending date awaits and removes it once a resume/terminal event clears them, so re-applying the same replayed
/// stream is idempotent (last-write-wins per work item). This index is <em>discovery</em> only — recovery re-folds
/// each candidate's per-aggregate stream for truth (DD-3), so a stale entry can never cause a wrong reissue.
/// </summary>
/// <remarks>
/// Plain <c>System.Text.Json</c> host-edge read model — NOT a <c>[PolymorphicSerialization]</c> durable catalog
/// type (the durable catalog stays 37). Entries reuse the <see cref="PendingDateAwait"/> record.
/// </remarks>
public sealed class PendingDateAwaitTenantIndex
{
    /// <summary>The pending date awaits keyed by raw work-item id (<c>WorkItemId.Value</c>).</summary>
    public Dictionary<string, IReadOnlyList<PendingDateAwait>> Entries { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The greatest projection sequence accepted for each work item, retained even after its pending entry is
    /// cleared. These tombstone watermarks prevent an older full-stream replay from resurrecting a cleared await.
    /// </summary>
    public Dictionary<string, long> LastSequences { get; init; } = new(StringComparer.Ordinal);
}
