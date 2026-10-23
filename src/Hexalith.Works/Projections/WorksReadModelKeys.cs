namespace Hexalith.Works.Projections;

/// <summary>
/// Deterministic, tenant-scoped read-model keys for the Works runtime projection/query adapter. Every key
/// embeds the tenant id so cross-tenant inner-id collisions (a <c>WorkItemId.Value</c> is the raw inner id,
/// not tenant-composed) can never share a read-model entry. Keys are generation-qualified from
/// <see cref="CurrentSchemaVersion"/> onward; the historical unversioned keys (<see cref="WhatsNextIndexKey"/>,
/// <see cref="RollUpKey"/>) remain the active generation for a tenant until a shared rebuild commits its
/// current-schema manifest.
/// </summary>
internal static class WorksReadModelKeys
{
    /// <summary>The current persisted Works read-model schema generation.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The SDK-owned generation family for the complete Works runtime view.</summary>
    public const string RuntimeFamily = "works-runtime-v3";

    /// <summary>The SDK control index used to discover tenants with a runtime generation.</summary>
    public const string RuntimeControlIndex = "works-runtime-v3-tenants";

    /// <summary>The logical tenant queue key inside a runtime generation.</summary>
    public const string RuntimeQueueKey = "works-runtime-v3:queue";

    /// <summary>The logical pending-date index key inside a runtime generation.</summary>
    public const string RuntimePendingDateKey = "works-runtime-v3:pending-date";

    /// <summary>The logical topology key inside a runtime generation.</summary>
    public const string RuntimeTopologyKey = "works-runtime-v3:topology";

    /// <summary>Builds a logical per-item roll-up key inside a runtime generation.</summary>
    public static string RuntimeRollUpKey(string workItemId) => $"works-runtime-v3:rollup:{workItemId}";

    /// <summary>Builds a logical per-item source-context key inside a runtime generation.</summary>
    public static string RuntimeHistoryKey(string workItemId) => $"works-runtime-v3:history:{workItemId}";

    /// <summary>Builds one bounded logical source-context page key inside the selected generation.</summary>
    public static string RuntimeHistoryPageKey(string workItemId, int ordinal) => $"works-runtime-v3:history:{workItemId}:page:{ordinal}";

    /// <summary>Builds a logical per-item parking key inside a runtime generation.</summary>
    public static string RuntimeParkingKey(string workItemId) => $"works-runtime-v3:parking:{workItemId}";

    /// <summary>The Dapr state-store component name shared with the EventStore platform.</summary>
    public const string StateStoreName = "statestore";

    /// <summary>The stable projection token for the tenant what's-next read model.</summary>
    public const string WhatsNextProjectionType = Strategies.WhatsNextQueueProjection.ProjectionType;

    /// <summary>The stable projection token for the per-work-item consumer read model.</summary>
    public const string WorkItemViewProjectionType = "works-work-item-view";

    /// <summary>The stable projection token for the pending-date-await tenant index.</summary>
    public const string PendingDateAwaitIndexProjectionType = "works-pending-date-await-index";

    /// <summary>The stable projection token for the pending-date-await tenant registry.</summary>
    public const string PendingDateAwaitRegistryProjectionType = "works-pending-date-await-registry";

    /// <summary>The stable projection token for the per-aggregate projection-parking record.</summary>
    public const string ProjectionParkingProjectionType = "works-projection-parking";

    /// <summary>Builds the historical unversioned singleton tenant index key.</summary>
    public static string WhatsNextIndexKey(string tenantId)
        => $"projection:works:whats-next:{tenantId}";

    /// <summary>Builds the current-schema singleton tenant index key.</summary>
    public static string CurrentWhatsNextIndexKey(string tenantId)
        => $"projection:works:whats-next:v{CurrentSchemaVersion}:{tenantId}";

    /// <summary>Builds the historical unversioned per-work-item roll-up key.</summary>
    public static string RollUpKey(string tenantId, string workItemId)
        => $"projection:works:rollup:{tenantId}:{workItemId}";

    /// <summary>Builds the current-schema per-work-item roll-up key.</summary>
    public static string CurrentRollUpKey(string tenantId, string workItemId)
        => $"projection:works:rollup:v{CurrentSchemaVersion}:{tenantId}:{workItemId}";

    /// <summary>
    /// Builds the per-work-item projection parking key: the bounded-failure record that stops a permanently
    /// undecodable aggregate from being redispatched forever.
    /// </summary>
    public static string ProjectionParkingKey(string tenantId, string workItemId)
        => $"projection:works:parked:{tenantId}:{workItemId}";

    /// <summary>
    /// The one tenant id the Works host refuses. <see cref="PendingDateAwaitIndexKey"/> renders
    /// <c>projection:works:pending-date-await:tenants</c> for it, byte-identical to
    /// <see cref="PendingDateAwaitRegistryKey"/> — a tenant with this id would overwrite the registry with its
    /// own index document, which deserializes into an empty registry and silently disables date-reminder
    /// recovery for every tenant. The durable registry key is deliberately left as it is (no migration); the id
    /// is rejected where tenant ids enter the host instead.
    /// </summary>
    public const string ReservedTenantId = "tenants";

    /// <summary>Returns whether the tenant id collides with the well-known pending-date-await registry key.</summary>
    public static bool IsReservedTenantId(string? tenantId)
        => string.Equals(tenantId, ReservedTenantId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Refuses <see cref="ReservedTenantId"/> at a host ingress so its pending-date-await index key cannot
    /// overwrite the well-known tenant registry.
    /// </summary>
    /// <param name="tenantId">The tenant id being admitted.</param>
    /// <exception cref="InvalidOperationException">The tenant id is <see cref="ReservedTenantId"/>.</exception>
    public static void ThrowIfReservedTenantId(string? tenantId)
    {
        if (!IsReservedTenantId(tenantId))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Tenant id '{ReservedTenantId}' is reserved by the Works host: its "
            + "pending-date-await index key collides with the well-known pending-date-await tenant registry key.");
    }

    /// <summary>Builds the singleton-per-tenant pending-date-await index key.</summary>
    /// <exception cref="InvalidOperationException">The tenant id is <see cref="ReservedTenantId"/>.</exception>
    public static string PendingDateAwaitIndexKey(string tenantId)
        => IsReservedTenantId(tenantId)
            ? throw new InvalidOperationException(
                $"Tenant id '{ReservedTenantId}' is reserved: its pending-date-await index key would be identical to "
                + $"the well-known pending-date-await tenant registry key '{PendingDateAwaitRegistryKey}'.")
            : $"projection:works:pending-date-await:{tenantId}";

    /// <summary>
    /// The well-known singleton key of the pending-date-await tenant registry (Story 4.8). This one durable
    /// document is what lets recovery enumerate the tenants that have (or have had) pending date awaits
    /// without any per-tenant hand configuration — Dapr state stores expose no key enumeration and the
    /// gateway exposes no tenant-wide read. It is deliberately unversioned: reminder recovery reads it
    /// independently of the roll-up read-model generation.
    /// </summary>
    public const string PendingDateAwaitRegistryKey = "projection:works:pending-date-await:tenants";
}
