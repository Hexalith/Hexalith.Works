using Hexalith.Works.Contracts.Models;

namespace Hexalith.Works.Projections;

/// <summary>
/// Deterministic, tenant-scoped read-model keys for the Works runtime projection/query adapter. Every key
/// embeds the tenant id so cross-tenant inner-id collisions (a <c>WorkItemId.Value</c> is the raw inner id,
/// not tenant-composed) can never share a read-model entry.
/// </summary>
internal static class WorksReadModelKeys
{
    /// <summary>The Dapr state-store component name shared with the EventStore platform.</summary>
    public const string StateStoreName = "statestore";

    /// <summary>The stable projection-type token for the tenant "what's next" read model. Sourced from the
    /// Story 4.4-pinned pure constant so the adapter can never drift from the token the projection reports.</summary>
    public const string WhatsNextProjectionType = Strategies.WhatsNextQueueProjection.ProjectionType;

    /// <summary>The stable projection-type token for the per-work-item consumer read model returned by the
    /// <c>get-work-item</c> query.</summary>
    public const string WorkItemViewProjectionType = "works-work-item-view";

    /// <summary>Builds the singleton-per-tenant "what's next" index key.</summary>
    public static string WhatsNextIndexKey(string tenantId) => $"projection:works:whats-next:{tenantId}";

    /// <summary>Builds the per-work-item roll-up read-model key, derived from <c>(tenantId, workItemId)</c>.</summary>
    public static string RollUpKey(string tenantId, string workItemId)
        => $"projection:works:rollup:{tenantId}:{workItemId}";
}

/// <summary>
/// The persisted tenant-scoped "what's next" index. A single entry per tenant holds the latest eligible
/// <see cref="WhatsNextItem"/> for each work item, keyed by the raw work-item id. The runtime projection
/// adapter upserts an item when it is eligible (<c>Queued</c>/<c>Assigned</c>) and removes it when it leaves
/// the eligible set, so re-applying the same replayed events is idempotent (last-write-wins per work item,
/// never duplicating list entries). Ordering and authorization are applied at query time by the pure
/// <c>WhatsNextOrdering</c> and <c>WhatsNextQueryAuthorization</c>, not stored here.
/// </summary>
public sealed class WorksWhatsNextTenantIndex
{
    /// <summary>The eligible items keyed by raw work-item id (<c>WorkItemId.Value</c>).</summary>
    public Dictionary<string, WhatsNextItem> Items { get; init; } = new(StringComparer.Ordinal);
}
