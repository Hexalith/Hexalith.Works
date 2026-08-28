using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;

namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// Defines the six independently configurable tenant-isolation decisions used by the work-item roll-up projection.
/// </summary>
internal sealed class WorkItemRollUpTenantIsolation
{
    private static readonly FrozenDictionary<Type, Func<IEventPayload, (TenantId? TenantId, WorkItemId? WorkItemId)>> _payloadIdentityRegistry
        = new KeyValuePair<Type, Func<IEventPayload, (TenantId? TenantId, WorkItemId? WorkItemId)>>[]
        {
            For<WorkItemCreated>(static value => (value.TenantId, value.WorkItemId)),
            For<ChildSpawned>(static value => value.ChildWorkItemId is null
                ? default
                : (value.TenantId, value.WorkItemId)),
            For<ProgressReported>(static value => (value.TenantId, value.WorkItemId)),
            For<ReEstimated>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemCompleted>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemCancelled>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemExpired>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemRejected>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemAssigned>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemQueued>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemClaimed>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemSuspended>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemResumed>(static value => (value.TenantId, value.WorkItemId)),
            For<WorkItemRescheduled>(static value => (value.TenantId, value.WorkItemId)),
        }.ToFrozenDictionary();

    private readonly bool _enforceContribution;
    private readonly bool _enforceDegradation;
    private readonly bool _enforceDelivery;
    private readonly bool _enforceDiagnostic;
    private readonly bool _enforceEdge;
    private readonly bool _enforceOutput;

    /// <summary>
    /// Gets the exact concrete payload types whose identities the runtime projection can validate.
    /// </summary>
    internal static IReadOnlySet<Type> SupportedPayloadTypes { get; } = _payloadIdentityRegistry.Keys.ToFrozenSet();

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
    /// <returns><see langword="true"/> when the delivery may be accepted; otherwise, <see langword="false"/>.</returns>
    internal bool AllowsDelivery(WorkItemRollUpEvent delivery)
    {
        if (delivery is null
            || delivery.TenantId is null
            || delivery.WorkItemId is null
            || delivery.Payload is null
            || !TryGetPayloadIdentity(delivery.Payload, out TenantId? payloadTenantId, out WorkItemId? payloadWorkItemId))
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

    /// <summary>
    /// Creates one registry entry whose dictionary key and payload cast both come from the same type argument,
    /// so a key can never disagree with the cast performed by the reader stored under it.
    /// </summary>
    /// <typeparam name="TPayload">The exact concrete payload type this entry reads.</typeparam>
    /// <param name="readIdentity">Reads the tenant and work item identity carried by the payload.</param>
    /// <returns>The registry entry for <typeparamref name="TPayload"/>.</returns>
    private static KeyValuePair<Type, Func<IEventPayload, (TenantId? TenantId, WorkItemId? WorkItemId)>> For<TPayload>(
        Func<TPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity)
        where TPayload : IEventPayload
        => new(typeof(TPayload), payload => readIdentity((TPayload)payload));

    private static bool TryGetPayloadIdentity(
        IEventPayload payload,
        [NotNullWhen(true)] out TenantId? tenantId,
        [NotNullWhen(true)] out WorkItemId? workItemId)
    {
        if (!_payloadIdentityRegistry.TryGetValue(
                payload.GetType(),
                out Func<IEventPayload, (TenantId? TenantId, WorkItemId? WorkItemId)>? getIdentity))
        {
            tenantId = null;
            workItemId = null;
            return false;
        }

        (tenantId, workItemId) = getIdentity(payload);

        return tenantId is not null && workItemId is not null;
    }

    private static bool SameIdentity(WorkItemRollUpEvent delivery, TenantId tenantId, WorkItemId workItemId)
        => SameTenant(delivery.TenantId, tenantId)
            && string.Equals(delivery.WorkItemId.Value, workItemId.Value, StringComparison.Ordinal);

    private static bool SameTenant(TenantId? first, TenantId? second)
        => first is not null
            && second is not null
            && string.Equals(first.Value, second.Value, StringComparison.Ordinal);
}
