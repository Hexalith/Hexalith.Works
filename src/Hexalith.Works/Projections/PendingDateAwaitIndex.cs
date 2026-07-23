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
}

/// <summary>
/// The single well-known durable document listing every tenant that has (or has had) pending <c>DateReached</c>
/// awaits (Story 4.8). This registry is the whole answer to "reconciliation without per-tenant hand configuration":
/// recovery enumerates its tenants from durable data instead of a hand-configured <c>Works:Recovery:Tenants</c>
/// list. It is append-only — a tenant whose awaits have all cleared stays listed and costs one cheap empty-index
/// read on recovery, which is preferred over a read-modify-write that would need the full per-tenant index.
/// </summary>
/// <remarks>Plain host-edge <c>System.Text.Json</c> read model; not a durable polymorphic catalog type.</remarks>
public sealed class PendingDateAwaitTenantRegistry
{
    /// <summary>The tenant ids known to have (or have had) pending date awaits.</summary>
    public HashSet<string> Tenants { get; init; } = new(StringComparer.Ordinal);
}
