using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Hexalith.Works.Reminders;

namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Builds the complete deterministic current-schema replacement for one sealed tenant candidate.</summary>
internal static class WorkItemSharedRebuildManifestBuilder
{
    /// <summary>Builds the complete SDK generation without touching recoverable legacy keys.</summary>
    public static DomainProjectionRebuildPlan BuildEpoch(
        DomainSharedProjectionRebuildIdentity identity,
        WorkItemSharedRebuildCandidateState candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Histories.Count == 0)
        {
            // An inventory that accumulated nothing is a failed capture, not a proof that the tenant is empty.
            // Never promote it over the last committed readable generation.
            throw new InvalidOperationException("The Works shared rebuild candidate carries no authoritative tenant history.");
        }

        var tenantId = new TenantId(identity.TenantId);
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkItemSharedRebuildAggregateHistory history in candidate.Histories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = members.Add(history.AggregateId);
        }

        var relationships = new WorkItemSharedRebuildRelationshipGraph();
        var rollUpProjection = new WorkItemRollUpProjection();
        var queueProjection = new WhatsNextQueueProjection();
        var pendingEntries = new Dictionary<string, IReadOnlyList<PendingDateAwait>>(StringComparer.Ordinal);
        var pendingLastSequences = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (WorkItemSharedRebuildAggregateHistory history in candidate.Histories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateHistory(history);

            var workItemId = new WorkItemId(history.AggregateId);
            string correlationId = WorkItemProjectionEventDecoder.CorrelationIdOf(history.Events);
            var decodedPayloads = new List<IEventPayload>();
            // ValidateHistory has already refused null, gapped or reordered envelopes.
            foreach (ProjectionEventDto dto in history.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WorkItemProjectionEventDecodeResult decoded = WorkItemProjectionEventDecoder.Decode(
                    dto,
                    tenantId,
                    workItemId,
                    correlationId,
                    logger: null);
                if (!decoded.KnownEventType || decoded.Malformed)
                    throw new InvalidOperationException("The Works runtime history contains undecodable evidence.");

                if (decoded.Payload is not { } payload
                    || PrepareRelationshipPayload(
                        identity.TenantId,
                        history.AggregateId,
                        payload,
                        relationships) is not { } safePayload)
                {
                    continue;
                }

                var delivery = new WorkItemRollUpEvent(tenantId, workItemId, dto.SequenceNumber, safePayload);
                rollUpProjection.Project(delivery);
                _ = queueProjection.Project(delivery);
                decodedPayloads.Add(safePayload);
            }

            IReadOnlyList<PendingDateAwait> pending = PendingDateAwaitProjection.PendingDateAwaits(decodedPayloads);
            if (pending.Count > 0)
            {
                pendingEntries.Add(history.AggregateId, pending);
            }

            if (decodedPayloads.OfType<WorkItemSuspended>().Any(static suspended =>
                    suspended.AwaitConditions.Any(static condition => condition.Kind == AwaitConditionKind.DateReached)))
            {
                pendingLastSequences.Add(history.AggregateId, history.Events[^1].SequenceNumber);
            }
        }

        var rollUps = new Dictionary<string, WorkItemRollUp>(StringComparer.Ordinal);
        foreach (string aggregateId in members.OrderBy(static id => id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkItemRollUp? model = WorkItemProjectionBoundarySanitizer.Sanitize(
                rollUpProjection.Get(tenantId, new WorkItemId(aggregateId)),
                relationships.IsRolledTotalUnavailable(aggregateId, members));
            if (model is not null)
            {
                rollUps.Add(aggregateId, model);
            }
        }

        var items = new Dictionary<string, WhatsNextItem>(StringComparer.Ordinal);
        foreach (WhatsNextItem item in queueProjection.WhatsNext(
            tenantId,
            (_, workItemId) => rollUps.GetValueOrDefault(workItemId.Value)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items[item.WorkItemId.Value] = item;
        }

        var lastSequences = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string member in members.OrderBy(static id => id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastSequences[member] = rollUps.TryGetValue(member, out WorkItemRollUp? model)
                ? model.LatestAcceptedSourceSequence
                : 0;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var index = new WorksWhatsNextTenantIndex
        {
            SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
            Items = items,
            LastSequences = lastSequences,
            MemberWorkItemIds = [.. members.OrderBy(static id => id, StringComparer.Ordinal)],
        };

        List<ReadModelBatchOperation> generationOperations =
        [
            ReadModelBatchOperation.Write(
                WorksReadModelKeys.RuntimeQueueKey,
                WorksRuntimeDocument.From(index),
                ReadModelBatchConcurrency.LastWrite),
            ReadModelBatchOperation.Write(
                WorksReadModelKeys.RuntimePendingDateKey,
                WorksRuntimeDocument.From(new PendingDateAwaitTenantIndex
                {
                    Entries = pendingEntries,
                    LastSequences = pendingLastSequences,
                }),
                ReadModelBatchConcurrency.LastWrite),
            ReadModelBatchOperation.Write(
                WorksReadModelKeys.RuntimeTopologyKey,
                WorksRuntimeDocument.From(new WorksRuntimeTopology(relationships.ChildrenByParent())),
                ReadModelBatchConcurrency.LastWrite),
        ];

        foreach (WorkItemSharedRebuildAggregateHistory history in candidate.Histories.OrderBy(static item => item.AggregateId, StringComparer.Ordinal))
        {
            generationOperations.AddRange(WorkItemRuntimeHistoryStore.CreateOperations(history));
            generationOperations.Add(ReadModelBatchOperation.Write(
                WorksReadModelKeys.RuntimeParkingKey(history.AggregateId),
                WorksRuntimeDocument.From(new WorkItemProjectionParking()),
                ReadModelBatchConcurrency.LastWrite));
        }

        foreach ((string aggregateId, WorkItemRollUp rollUp) in rollUps.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            generationOperations.Add(ReadModelBatchOperation.Write(
                WorksReadModelKeys.RuntimeRollUpKey(aggregateId),
                WorksRuntimeDocument.From(rollUp),
                ReadModelBatchConcurrency.LastWrite));
        }

        return new DomainProjectionRebuildPlan(WorksReadModelKeys.StateStoreName, generationOperations);
    }

    private static void ValidateHistory(WorkItemSharedRebuildAggregateHistory history)
    {
        if (history.Events.Length == 0)
            throw new InvalidOperationException("The Works runtime aggregate history contains no committed evidence.");
        long expected = 1;
        foreach (ProjectionEventDto? envelope in history.Events)
        {
            if (envelope is null || envelope.SequenceNumber != expected)
            {
                throw new InvalidOperationException("The Works runtime history is gapped, reordered, or corrupt.");
            }

            expected++;
        }
    }

    private static IEventPayload? PrepareRelationshipPayload(
        string tenantId,
        string aggregateId,
        IEventPayload payload,
        WorkItemSharedRebuildRelationshipGraph relationships)
    {
        switch (payload)
        {
            case ChildSpawned spawned when spawned.ChildWorkItemId is null
                || string.IsNullOrWhiteSpace(spawned.ChildWorkItemId.Value):
                relationships.MarkIncomplete(aggregateId);
                return null;
            case ChildSpawned spawned:
                relationships.AddEdge(aggregateId, spawned.ChildWorkItemId.Value);
                return spawned;
            case WorkItemCreated { Parent: { } parent } created
                when parent.TenantId is null
                    || string.IsNullOrWhiteSpace(parent.TenantId.Value)
                    || parent.WorkItemId is null
                    || string.IsNullOrWhiteSpace(parent.WorkItemId.Value):
                relationships.MarkIncomplete(aggregateId);
                return created with { Parent = null };
            case WorkItemCreated { Parent: { } parent }:
                if (string.Equals(parent.TenantId.Value, tenantId, StringComparison.Ordinal))
                {
                    relationships.AddEdge(parent.WorkItemId.Value, aggregateId);
                }
                else
                {
                    relationships.MarkIncomplete(aggregateId);
                }

                return payload;
            default:
                return payload;
        }
    }
}
