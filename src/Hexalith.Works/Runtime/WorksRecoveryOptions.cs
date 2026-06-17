namespace Hexalith.Works.Runtime;

/// <summary>
/// Host-edge configuration for the Story 4.6 reminder/cascade recovery runtime. Bound from the
/// <c>Works:Recovery</c> configuration section.
/// </summary>
/// <remarks>
/// <para>Substrate limitation (documented, not faked): neither the EventStore stream-read surface nor the
/// Dapr state store exposes a cross-tenant enumeration, so reminder reconciliation re-scans the
/// <see cref="Tenants"/> it is told about. A single-tenant or known-tenant-set deployment (the proof's
/// shape) reconciles fully; broad multi-tenant discovery would need an EventStore capability not present at
/// implementation time.</para>
/// </remarks>
public sealed class WorksRecoveryOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Works:Recovery";

    /// <summary>The tenant scope reminder reconciliation re-scans on recovery. Empty disables the scan.</summary>
    public IReadOnlyList<string> Tenants { get; init; } = [];

    /// <summary>Whether the startup reconciliation pass runs. Defaults to enabled.</summary>
    public bool RunReconciliationOnStartup { get; init; } = true;

    /// <summary>The maximum stream pages a single reconciliation scan reads per tenant (a runaway backstop).</summary>
    public int MaxStreamPagesPerTenant { get; init; } = 1000;
}
