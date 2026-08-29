namespace Hexalith.Works.Projections;

/// <summary>
/// Deterministic, tenant-scoped read-model keys for the Works runtime projection/query adapter. Every key
/// embeds the tenant id so cross-tenant inner-id collisions (a <c>WorkItemId.Value</c> is the raw inner id,
/// not tenant-composed) can never share a read-model entry. Keys are generation-qualified from
/// <see cref="CurrentSchemaVersion"/> onward; the unversioned <c>Legacy*</c> keys remain the active
/// generation for a tenant until a shared rebuild commits its current-schema manifest.
/// </summary>
internal static class WorksReadModelKeys
{
    /// <summary>The current persisted Works read-model schema generation.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The Dapr state-store component name shared with the EventStore platform.</summary>
    public const string StateStoreName = "statestore";

    /// <summary>The stable projection token for the tenant what's-next read model.</summary>
    public const string WhatsNextProjectionType = Strategies.WhatsNextQueueProjection.ProjectionType;

    /// <summary>The stable projection token for the per-work-item consumer read model.</summary>
    public const string WorkItemViewProjectionType = "works-work-item-view";

    /// <summary>Builds the historical unversioned singleton tenant index key.</summary>
    public static string WhatsNextIndexKey(string tenantId)
        => $"projection:works:whats-next:{tenantId}";

    /// <summary>Builds the historical unversioned tenant index key.</summary>
    public static string LegacyWhatsNextIndexKey(string tenantId)
        => WhatsNextIndexKey(tenantId);

    /// <summary>Builds the current-schema singleton tenant index key.</summary>
    public static string CurrentWhatsNextIndexKey(string tenantId)
        => $"projection:works:whats-next:v{CurrentSchemaVersion}:{tenantId}";

    /// <summary>Builds the historical unversioned per-work-item roll-up key.</summary>
    public static string RollUpKey(string tenantId, string workItemId)
        => $"projection:works:rollup:{tenantId}:{workItemId}";

    /// <summary>Builds the historical unversioned per-work-item roll-up key.</summary>
    public static string LegacyRollUpKey(string tenantId, string workItemId)
        => RollUpKey(tenantId, workItemId);

    /// <summary>Builds the current-schema per-work-item roll-up key.</summary>
    public static string CurrentRollUpKey(string tenantId, string workItemId)
        => $"projection:works:rollup:v{CurrentSchemaVersion}:{tenantId}:{workItemId}";

    /// <summary>Builds the singleton-per-tenant pending-date-await index key.</summary>
    public static string PendingDateAwaitIndexKey(string tenantId)
        => $"projection:works:pending-date-await:{tenantId}";

    /// <summary>
    /// The well-known singleton key of the pending-date-await tenant registry (Story 4.8). This one durable
    /// document is what lets recovery enumerate the tenants that have (or have had) pending date awaits
    /// without any per-tenant hand configuration — Dapr state stores expose no key enumeration and the
    /// gateway exposes no tenant-wide read. It is deliberately unversioned: reminder recovery reads it
    /// independently of the roll-up read-model generation.
    /// </summary>
    public const string PendingDateAwaitRegistryKey = "projection:works:pending-date-await:tenants";
}
