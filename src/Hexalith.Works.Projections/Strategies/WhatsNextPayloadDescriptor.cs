using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;

namespace Hexalith.Works.Projections.Strategies;

/// <summary>
/// Binds one exact what's-next payload type to its identity reader and mandatory projection effect.
/// </summary>
internal sealed class WhatsNextPayloadDescriptor
{
    private static readonly FrozenDictionary<Type, WhatsNextPayloadDescriptor> _catalog = new[]
    {
        ForFold<WorkItemCreated>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                node.Status = WorkItemStatus.Created;
                node.Schedule = payload.Schedule;
                node.ExecutorBinding = payload.ExecutorBinding;
                node.OwnEffort = payload.InitialEffort;
                node.AwaitConditions.Clear();
                node.Terminal = false;
            }),
        ForIntentionalNoOp<ChildSpawned>(static payload => (payload.TenantId, payload.WorkItemId)),
        ForFold<WorkItemAssigned>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (!node.Terminal)
                {
                    node.Status = WorkItemStatus.Assigned;
                    node.ExecutorBinding = payload.Binding;
                }
            }),
        ForFold<WorkItemQueued>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) => SetStatusWhenActive(node, WorkItemStatus.Queued)),
        ForFold<WorkItemClaimed>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (!node.Terminal)
                {
                    node.Status = WorkItemStatus.InProgress;
                    node.ExecutorBinding = payload.Binding;
                }
            }),
        ForFold<WorkItemSuspended>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (!node.Terminal)
                {
                    node.Status = WorkItemStatus.Suspended;
                    node.AwaitConditions.Clear();
                    node.AwaitConditions.AddRange(payload.AwaitConditions);
                }
            }),
        ForFold<WorkItemResumed>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, _) =>
            {
                if (!node.Terminal)
                {
                    node.Status = WorkItemStatus.InProgress;
                    node.AwaitConditions.Clear();
                }
            }),
        ForFold<WorkItemRescheduled>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (!node.Terminal)
                {
                    node.Schedule = payload.Schedule;
                }
            }),
        ForFold<ProgressReported>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (!node.Terminal
                    && node.OwnEffort is { } reported
                    && reported.Unit == payload.Unit
                    && payload.DoneDelta > 0)
                {
                    node.OwnEffort = reported.Report(payload.DoneDelta);
                }
            }),
        ForFold<ReEstimated>(
            static payload => (payload.TenantId, payload.WorkItemId),
            static (_, node, payload) =>
            {
                if (node.Terminal || payload.Estimated < 0)
                {
                    return;
                }

                if (node.OwnEffort is { } estimated)
                {
                    if (estimated.Unit == payload.Unit)
                    {
                        node.OwnEffort = estimated.ReEstimate(payload.Estimated);
                    }
                }
                else
                {
                    node.OwnEffort = new WorkItemEffort(payload.Estimated, payload.Unit);
                }
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
    }.ToFrozenDictionary(descriptor => descriptor.PayloadType);

    private readonly Action<WhatsNextQueueProjection, WhatsNextQueueProjection.ItemNode, IEventPayload>? _applyFold;
    private readonly Func<IEventPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> _readIdentity;

    private WhatsNextPayloadDescriptor(
        Type payloadType,
        ProjectionPayloadEffectDisposition effectDisposition,
        Func<IEventPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity,
        Action<WhatsNextQueueProjection, WhatsNextQueueProjection.ItemNode, IEventPayload>? applyFold)
    {
        PayloadType = payloadType;
        EffectDisposition = effectDisposition;
        _readIdentity = readIdentity;
        _applyFold = applyFold;
    }

    /// <summary>
    /// Gets every exact payload descriptor accepted by the what's-next projection.
    /// </summary>
    internal static IReadOnlyCollection<WhatsNextPayloadDescriptor> Catalog => _catalog.Values;

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
    internal static bool TryResolve(IEventPayload? payload, [NotNullWhen(true)] out WhatsNextPayloadDescriptor? descriptor)
        => payload is not null && _catalog.TryGetValue(payload.GetType(), out descriptor);

    /// <summary>
    /// Determines whether this descriptor's payload identity matches the delivery header exactly.
    /// </summary>
    /// <param name="delivery">The projection delivery to validate.</param>
    /// <returns><see langword="true"/> when the payload and header identities match; otherwise, <see langword="false"/>.</returns>
    internal bool MatchesIdentity(WorkItemRollUpEvent delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.TenantId is null
            || delivery.WorkItemId is null
            || delivery.Payload is null
            || delivery.Payload.GetType() != PayloadType)
        {
            return false;
        }

        (TenantId? tenantId, WorkItemId? workItemId) = _readIdentity(delivery.Payload);
        return tenantId is not null
            && workItemId is not null
            && string.Equals(tenantId.Value, delivery.TenantId.Value, StringComparison.Ordinal)
            && string.Equals(workItemId.Value, delivery.WorkItemId.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies the descriptor-owned sorted-fold effect, when present.
    /// </summary>
    /// <param name="projection">The target what's-next projection.</param>
    /// <param name="node">The node being rebuilt.</param>
    /// <param name="payload">The accepted payload.</param>
    internal void ApplyFold(
        WhatsNextQueueProjection projection,
        WhatsNextQueueProjection.ItemNode node,
        IEventPayload payload)
        => _applyFold?.Invoke(projection, node, payload);

    private static WhatsNextPayloadDescriptor ForFold<TPayload>(
        Func<TPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity,
        Action<WhatsNextQueueProjection, WhatsNextQueueProjection.ItemNode, TPayload> applyFold)
        where TPayload : IEventPayload
        => new(
            typeof(TPayload),
            ProjectionPayloadEffectDisposition.Fold,
            payload => readIdentity((TPayload)payload),
            (projection, node, payload) => applyFold(projection, node, (TPayload)payload));

    private static WhatsNextPayloadDescriptor ForIntentionalNoOp<TPayload>(
        Func<TPayload, (TenantId? TenantId, WorkItemId? WorkItemId)> readIdentity)
        where TPayload : IEventPayload
        => new(
            typeof(TPayload),
            ProjectionPayloadEffectDisposition.IntentionalNoOp,
            payload => readIdentity((TPayload)payload),
            null);

    private static void SetStatusWhenActive(WhatsNextQueueProjection.ItemNode node, WorkItemStatus status)
    {
        if (!node.Terminal)
        {
            node.Status = status;
        }
    }

    private static void SetTerminal(WhatsNextQueueProjection.ItemNode node, WorkItemStatus status)
    {
        node.Status = status;
        node.Terminal = true;
    }
}
