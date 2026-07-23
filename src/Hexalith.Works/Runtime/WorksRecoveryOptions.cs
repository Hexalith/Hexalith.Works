namespace Hexalith.Works.Runtime;

/// <summary>
/// Host-edge configuration for the Story 4.6 reminder/cascade recovery runtime. Bound from the
/// <c>Works:Recovery</c> configuration section.
/// </summary>
/// <remarks>
/// <para>Story 4.8 removed the hand-configured <c>Tenants</c> gate: reminder reconciliation discovers the tenants
/// with pending date awaits from the durable pending-date-await registry (maintained by the <c>/project</c>
/// dispatcher), not from configuration. Neither the EventStore stream-read surface nor the Dapr state store
/// exposes a cross-tenant enumeration — the durable registry is the substrate-compatible answer, and every
/// discovery read is per-aggregate (the tenant-wide null-<c>AggregateId</c> read is gateway-rejected).</para>
/// </remarks>
public sealed class WorksRecoveryOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Works:Recovery";

    /// <summary>Whether the startup reconciliation pass runs. Defaults to enabled.</summary>
    public bool RunReconciliationOnStartup { get; init; } = true;

    /// <summary>The maximum stream pages a single reconciliation scan reads per aggregate (a runaway backstop).</summary>
    public int MaxStreamPagesPerTenant { get; init; } = 1000;

    /// <summary>
    /// Optional pacing interval between cascade targets. Zero dispatches immediately; a positive value lets
    /// operators bound burst pressure and lets the live recovery lane stop at a deterministic checkpoint boundary.
    /// Clamped to a supported maximum by <c>CascadeDispatcher</c> so a misconfigured value cannot block a
    /// dispatch indefinitely.
    /// </summary>
    public int CascadeTargetIntervalMilliseconds { get; init; }

    /// <summary>
    /// How long an incomplete-cascade-checkpoint index entry with no matching checkpoint (the documented
    /// crash window between index-add and checkpoint-write) is retried before it is pruned as abandoned.
    /// </summary>
    public int CascadeCheckpointIndexStaleAfterHours { get; init; } = 24;
}
