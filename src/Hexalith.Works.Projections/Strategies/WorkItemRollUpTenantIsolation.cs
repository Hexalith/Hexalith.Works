using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;

namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// Defines the six independently configurable tenant-isolation decisions used by the work-item roll-up projection.
/// </summary>
internal sealed class WorkItemRollUpTenantIsolation
{
    private readonly bool _enforceContribution;
    private readonly bool _enforceDegradation;
    private readonly bool _enforceDelivery;
    private readonly bool _enforceDiagnostic;
    private readonly bool _enforceEdge;
    private readonly bool _enforceOutput;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItemRollUpTenantIsolation"/> class.
    /// </summary>
    /// <param name="enforceDelivery">Whether delivery payload and header identity must match exactly.</param>
    /// <param name="enforceEdge">Whether parent-child edges and their refusal diagnostics must be tenant-closed.</param>
    /// <param name="enforceOutput">Whether child identifiers exposed by a parent must be tenant-closed.</param>
    /// <param name="enforceContribution">Whether rolled child contributions must be tenant-closed.</param>
    /// <param name="enforceDiagnostic">Whether propagated child diagnostics must be tenant-closed.</param>
    /// <param name="enforceDegradation">Whether propagated child degradation must be tenant-closed.</param>
    internal WorkItemRollUpTenantIsolation(
        bool enforceDelivery = true,
        bool enforceEdge = true,
        bool enforceOutput = true,
        bool enforceContribution = true,
        bool enforceDiagnostic = true,
        bool enforceDegradation = true)
    {
        _enforceDelivery = enforceDelivery;
        _enforceEdge = enforceEdge;
        _enforceOutput = enforceOutput;
        _enforceContribution = enforceContribution;
        _enforceDiagnostic = enforceDiagnostic;
        _enforceDegradation = enforceDegradation;
    }

    /// <summary>
    /// Determines whether a delivery may be accepted into a sequence slot.
    /// </summary>
    /// <remarks>
    /// Well-formedness fails closed unconditionally and is not governed by the delivery enforcement flag: a delivery
    /// missing a tenant, work item, or payload, carrying a payload type outside the supported allowlist, or carrying
    /// a payload that omits an identity the projection dereferences, cannot prove its payload agrees with its header
    /// and is always refused. Only the tenant and work-item identity comparison is governed by the flag.
    /// </remarks>
    /// <param name="delivery">The projection delivery to inspect.</param>
    /// <param name="descriptor">The exact payload descriptor resolved before allocation.</param>
    /// <returns><see langword="true"/> when the delivery may be accepted; otherwise, <see langword="false"/>.</returns>
    internal bool AllowsDelivery(WorkItemRollUpEvent delivery, WorkItemRollUpPayloadDescriptor? descriptor)
    {
        if (delivery is null
            || delivery.TenantId is null
            || delivery.WorkItemId is null
            || delivery.Payload is null
            || descriptor is null
            || !descriptor.TryReadIdentity(delivery.Payload, out TenantId? payloadTenantId, out WorkItemId? payloadWorkItemId))
        {
            return false;
        }

        return !_enforceDelivery || SameIdentity(delivery, payloadTenantId, payloadWorkItemId);
    }

    /// <summary>
    /// Determines whether a parent-child edge may be admitted and treated as valid for diagnostics.
    /// </summary>
    /// <param name="parentTenantId">The parent tenant identifier.</param>
    /// <param name="childTenantId">The child tenant identifier.</param>
    /// <returns><see langword="true"/> when the edge may be admitted; otherwise, <see langword="false"/>.</returns>
    internal bool AllowsEdge(TenantId parentTenantId, TenantId childTenantId)
        => !_enforceEdge || SameTenant(parentTenantId, childTenantId);

    /// <summary>
    /// Determines whether a parent-child edge may be admitted, comparing raw tenant key values.
    /// </summary>
    /// <remarks>
    /// Node keys already hold validated, normalized tenant values, so this overload compares them directly instead of
    /// re-running value-object validation on the edge-refusal path. It fails closed on a missing key exactly as the
    /// <see cref="TenantId"/> overload does, so two absent tenants are never read as the same tenant.
    /// </remarks>
    /// <param name="parentTenantId">The parent tenant key value.</param>
    /// <param name="childTenantId">The child tenant key value.</param>
    /// <returns><see langword="true"/> when the edge may be admitted; otherwise, <see langword="false"/>.</returns>
    internal bool AllowsEdge(string parentTenantId, string childTenantId)
        => !_enforceEdge
            || (!string.IsNullOrEmpty(parentTenantId)
                && string.Equals(parentTenantId, childTenantId, StringComparison.Ordinal));

    /// <summary>
    /// Determines whether a child may be exposed in a parent's output.
    /// </summary>
    /// <param name="parentTenantId">The parent tenant identifier.</param>
    /// <param name="childTenantId">The child tenant identifier.</param>
    /// <returns><see langword="true"/> when the child may be exposed; otherwise, <see langword="false"/>.</returns>
    internal bool AllowsOutput(TenantId parentTenantId, TenantId childTenantId)
        => !_enforceOutput || SameTenant(parentTenantId, childTenantId);

    /// <summary>
    /// Determines whether a child's rolled effort may contribute to a parent.
    /// </summary>
    /// <param name="parentTenantId">The parent tenant identifier.</param>
    /// <param name="childTenantId">The child tenant identifier.</param>
    /// <returns><see langword="true"/> when the child may contribute; otherwise, <see langword="false"/>.</returns>
    internal bool AllowsContribution(TenantId parentTenantId, TenantId childTenantId)
        => !_enforceContribution || SameTenant(parentTenantId, childTenantId);

    /// <summary>
    /// Determines whether a child's diagnostics may propagate to a parent.
    /// </summary>
    /// <param name="parentTenantId">The parent tenant identifier.</param>
    /// <param name="childTenantId">The child tenant identifier.</param>
    /// <returns><see langword="true"/> when diagnostics may propagate; otherwise, <see langword="false"/>.</returns>
    internal bool AllowsDiagnostic(TenantId parentTenantId, TenantId childTenantId)
        => !_enforceDiagnostic || SameTenant(parentTenantId, childTenantId);

    /// <summary>
    /// Determines whether a child's degraded state may propagate to a parent.
    /// </summary>
    /// <param name="parentTenantId">The parent tenant identifier.</param>
    /// <param name="childTenantId">The child tenant identifier.</param>
    /// <returns><see langword="true"/> when degradation may propagate; otherwise, <see langword="false"/>.</returns>
    internal bool AllowsDegradation(TenantId parentTenantId, TenantId childTenantId)
        => !_enforceDegradation || SameTenant(parentTenantId, childTenantId);

    private static bool SameIdentity(WorkItemRollUpEvent delivery, TenantId tenantId, WorkItemId workItemId)
        => SameTenant(delivery.TenantId, tenantId)
            && string.Equals(delivery.WorkItemId.Value, workItemId.Value, StringComparison.Ordinal);

    private static bool SameTenant(TenantId? first, TenantId? second)
        => first is not null
            && second is not null
            && string.Equals(first.Value, second.Value, StringComparison.Ordinal);
}
