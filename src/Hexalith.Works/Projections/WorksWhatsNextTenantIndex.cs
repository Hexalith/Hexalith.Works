using Hexalith.Works.Contracts.Models;

namespace Hexalith.Works.Projections;

/// <summary>
/// The persisted tenant-scoped "what's next" index and, from
/// <see cref="WorksReadModelKeys.CurrentSchemaVersion"/> onward, the authoritative work-item membership
/// manifest. A single entry per tenant holds the latest eligible <see cref="WhatsNextItem"/> for each work
/// item, keyed by the raw work-item id. The runtime projection adapter upserts an item when it is eligible
/// (<c>Queued</c>/<c>Assigned</c>) and removes it when it leaves the eligible set. A retained per-item
/// source-sequence watermark prevents an older full replay from overwriting, deleting, or resurrecting newer
/// eligibility state, whether or not the item itself is still present. Ordering and authorization are applied
/// at query time by the pure <c>WhatsNextOrdering</c> and <c>WhatsNextQueryAuthorization</c>, not stored here.
/// </summary>
public sealed class WorksWhatsNextTenantIndex
{
    /// <summary>Gets the persisted schema generation.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets the eligible items keyed by raw work-item id.</summary>
    public Dictionary<string, WhatsNextItem> Items { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the greatest accepted EventStore envelope position persisted for each work item, retained after
    /// the item leaves the eligible set. During additive rollout, an aggregate id with no entry here falls
    /// back to the sequence on any legacy eligible <see cref="Items"/> entry for that id. Once an entry
    /// exists here it is the sole ordering authority for that id: the item's own watermark comes from a
    /// different projection and never competes with it.
    /// </summary>
    public Dictionary<string, long> LastSequences { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the ordinal authoritative membership of the tenant's current schema generation. Readers resolve
    /// no per-item document whose id is absent from this list.
    /// </summary>
    public IReadOnlyList<string> MemberWorkItemIds { get; init; } = [];
}
