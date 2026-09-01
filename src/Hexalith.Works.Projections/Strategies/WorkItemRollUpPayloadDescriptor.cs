using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;

namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// Binds one exact roll-up payload type to its identity reader and mandatory projection effect.
/// </summary>
internal sealed class WorkItemRollUpPayloadDescriptor
{
    private static readonly FrozenDictionary<Type, WorkItemRollUpPayloadDescriptor> _catalog = new[]
    {
        ForTopologyAndFold<WorkItemCreated>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (projection, node, payload) => projection.ApplyTopology(node, payload),
            static (projection, node, payload) => projection.ApplyPayload(node, payload)),
        ForTopology<ChildSpawned>(
            static payload => payload.ChildWorkItemId is null
                ? default
                : (payload.TenantId, payload.WorkItemId),
            static (projection, node, payload) => projection.ApplyTopology(node, payload)),
        ForFold<ProgressReported>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (node.Terminal || node.OwnEffort is null)
                {
                    return;
                }

                if (node.OwnEffort.Unit != payload.Unit || payload.DoneDelta <= 0)
                {
                    node.Refuse(nameof(ProgressReported), payload.Sequence);
                    return;
                }

                node.OwnEffort = node.OwnEffort.Report(payload.DoneDelta);
            }),
        ForFold<ReEstimated>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (node.Terminal)
                {
                    return;
                }

                if ((node.OwnEffort is not null && node.OwnEffort.Unit != payload.Unit)
                    || payload.Estimated < 0)
                {
                    node.Refuse(nameof(ReEstimated), payload.Sequence);
                    return;
                }

                node.OwnEffort = node.OwnEffort is null
                    ? new WorkItemEffort(payload.Estimated, payload.Unit)
                    : node.OwnEffort.ReEstimate(payload.Estimated);
            }),
        ForFold<WorkItemCompleted>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetTerminal(node, WorkItemStatus.Completed)),
        ForFold<WorkItemCancelled>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetTerminal(node, WorkItemStatus.Cancelled)),
        ForFold<WorkItemExpired>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetTerminal(node, WorkItemStatus.Expired)),
        ForFold<WorkItemRejected>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (payload.Requeue && !node.Terminal)
                {
                    node.Status = WorkItemStatus.Queued;
                }
                else if (!payload.Requeue)
                {
                    SetTerminal(node, WorkItemStatus.Rejected);
                }
            }),
        ForFold<WorkItemAssigned>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetStatusWhenActive(node, WorkItemStatus.Assigned)),
        ForFold<WorkItemQueued>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetStatusWhenActive(node, WorkItemStatus.Queued)),
        ForFold<WorkItemClaimed>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetStatusWhenActive(node, WorkItemStatus.InProgress)),
        ForFold<WorkItemSuspended>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetStatusWhenActive(node, WorkItemStatus.Suspended)),
        ForFold<WorkItemResumed>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetStatusWhenActive(node, WorkItemStatus.InProgress)),
        ForIntentionalNoOp<WorkItemRescheduled>(static payload => (payload.TenantId, payload.WorkItemId)),
    }.ToFrozenDictionary(descriptor => descriptor.PayloadType);

    private readonly Action<WorkItemRollUpProjection, WorkItemRollUpProjection.RollUpNode, IEventPayload>? _applyFold;
    private readonly Action<WorkItemRollUpProjection, WorkItemRollUpProjection.RollUpNode, IEventPayload>? _applyTopology;
    private readonly Func<IEventPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> _readIdentity;

    private WorkItemRollUpPayloadDescriptor(
        Type payloadType,
        ProjectionPayloadEffectDisposition effectDisposition,
        Func<IEventPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity,
        Action<WorkItemRollUpProjection, WorkItemRollUpProjection.RollUpNode, IEventPayload>? applyTopology,
        Action<WorkItemRollUpProjection, WorkItemRollUpProjection.RollUpNode, IEventPayload>? applyFold)
    {
        PayloadType = payloadType;
        EffectDisposition = effectDisposition;
        _readIdentity = readIdentity;
        _applyTopology = applyTopology;
        _applyFold = applyFold;
    }

    /// <summary>
    /// Gets every exact payload descriptor accepted by the roll-up projection.
    /// </summary>
    internal static IReadOnlyCollection<WorkItemRollUpPayloadDescriptor> Catalog => _catalog.Values;

    /// <summary>
    /// Gets the exact concrete payload type represented by this descriptor.
    /// </summary>
    internal Type PayloadType { get; }

    /// <summary>
    /// Gets the descriptor's mandatory projection-effect disposition.
    /// </summary>
    internal ProjectionPayloadEffectDisposition EffectDisposition { get; }

    /// <summary>
    /// Resolves the descriptor for an exact concrete payload type.
    /// </summary>
    /// <param name="payload">The payload to resolve.</param>
    /// <param name="descriptor">The resolved descriptor.</param>
    /// <returns><see langword="true"/> when the exact payload type is accepted; otherwise, <see langword="false"/>.</returns>
    internal static bool TryResolve(
        IEventPayload? payload,
        [NotNullWhen(true)] out WorkItemRollUpPayloadDescriptor? descriptor)
        => payload is not null && _catalog.TryGetValue(payload.GetType(), out descriptor);

    /// <summary>
    /// Reads and validates the payload identity owned by this descriptor.
    /// </summary>
    /// <param name="payload">The exact payload instance.</param>
    /// <param name="tenantId">The payload tenant identifier.</param>
    /// <param name="workItemId">The payload work-item identifier.</param>
    /// <returns><see langword="true"/> when both identity values are present; otherwise, <see langword="false"/>.</returns>
    internal bool TryReadIdentity(
        IEventPayload payload,
        [NotNullWhen(true)] out TenantId? tenantId,
        [NotNullWhen(true)] out WorkItemId? workItemId)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.GetType() != PayloadType)
        {
            tenantId = null;
            workItemId = null;
            return false;
        }

        (tenantId, workItemId) = _readIdentity(payload);
        return tenantId is not null && workItemId is not null;
    }

    /// <summary>
    /// Applies the descriptor-owned one-shot topology effect, when present.
    /// </summary>
    /// <param name="projection">The target roll-up projection.</param>
    /// <param name="node">The accepted delivery's node.</param>
    /// <param name="payload">The accepted payload.</param>
    internal void ApplyTopology(
        WorkItemRollUpProjection projection,
        WorkItemRollUpProjection.RollUpNode node,
        IEventPayload payload)
        => _applyTopology?.Invoke(projection, node, payload);

    /// <summary>
    /// Applies the descriptor-owned sorted-fold effect, when present.
    /// </summary>
    /// <param name="projection">The target roll-up projection.</param>
    /// <param name="node">The node being rebuilt.</param>
    /// <param name="payload">The accepted payload.</param>
    internal void ApplyFold(
        WorkItemRollUpProjection projection,
        WorkItemRollUpProjection.RollUpNode node,
        IEventPayload payload)
        => _applyFold?.Invoke(projection, node, payload);

    private static WorkItemRollUpPayloadDescriptor ForFold<TPayload>(
        Func<TPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity,
        Action<WorkItemRollUpProjection, WorkItemRollUpProjection.RollUpNode, TPayload> applyFold)
        where TPayload : IEventPayload
        => new(
            typeof(TPayload),
            ProjectionPayloadEffectDisposition.Fold,
            payload => readIdentity((TPayload)payload),
            null,
            (projection, node, payload) => applyFold(projection, node, (TPayload)payload));

    private static WorkItemRollUpPayloadDescriptor ForTopology<TPayload>(
        Func<TPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity,
        Action<WorkItemRollUpProjection, WorkItemRollUpProjection.RollUpNode, TPayload> applyTopology)
        where TPayload : IEventPayload
        => new(
            typeof(TPayload),
            ProjectionPayloadEffectDisposition.Topology,
            payload => readIdentity((TPayload)payload),
            (projection, node, payload) => applyTopology(projection, node, (TPayload)payload),
            null);

    private static WorkItemRollUpPayloadDescriptor ForTopologyAndFold<TPayload>(
        Func<TPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity,
        Action<WorkItemRollUpProjection, WorkItemRollUpProjection.RollUpNode, TPayload> applyTopology,
        Action<WorkItemRollUpProjection, WorkItemRollUpProjection.RollUpNode, TPayload> applyFold)
        where TPayload : IEventPayload
        => new(
            typeof(TPayload),
            ProjectionPayloadEffectDisposition.TopologyAndFold,
            payload => readIdentity((TPayload)payload),
            (projection, node, payload) => applyTopology(projection, node, (TPayload)payload),
            (projection, node, payload) => applyFold(projection, node, (TPayload)payload));

    private static WorkItemRollUpPayloadDescriptor ForIntentionalNoOp<TPayload>(
        Func<TPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity)
        where TPayload : IEventPayload
        => new(
            typeof(TPayload),
            ProjectionPayloadEffectDisposition.IntentionalNoOp,
            payload => readIdentity((TPayload)payload),
            null,
            null);

    private static void SetStatusWhenActive(WorkItemRollUpProjection.RollUpNode node, WorkItemStatus status)
    {
        if (!node.Terminal)
        {
            node.Status = status;
        }
    }

    private static void SetTerminal(WorkItemRollUpProjection.RollUpNode node, WorkItemStatus status)
    {
        node.Status = status;
        node.Terminal = true;
    }
}
