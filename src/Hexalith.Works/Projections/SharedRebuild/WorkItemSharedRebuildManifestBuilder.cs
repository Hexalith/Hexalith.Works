using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;

namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Builds the complete deterministic current-schema replacement for one sealed tenant candidate.</summary>
internal static class WorkItemSharedRebuildManifestBuilder
{
    /// <summary>Folds all histories through one pure relationship-aware graph and creates the atomic manifest.</summary>
    public static DomainProjectionRebuildPlan Build(
        DomainSharedProjectionRebuildIdentity identity,
        WorkItemSharedRebuildCandidateState candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Histories.Count == 0)
        {
            // Committing an empty inventory would delete the legacy index and publish an authoritative
            // manifest with no members, making the whole tenant unreachable. An inventory that accumulated
            // nothing is a failed capture, not a proof that the tenant is empty.
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

        foreach (WorkItemSharedRebuildAggregateHistory history in candidate.Histories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workItemId = new WorkItemId(history.AggregateId);
            string correlationId = WorkItemProjectionEventDecoder.CorrelationIdOf(history.Events);
            foreach (ProjectionEventDto? dto in history.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (dto is null)
                {
                    relationships.MarkIncomplete(history.AggregateId);
                    continue;
                }

                WorkItemProjectionEventDecodeResult decoded = WorkItemProjectionEventDecoder.Decode(
                    dto,
                    tenantId,
                    workItemId,
                    correlationId,
                    logger: null);
                if (!decoded.KnownEventType || decoded.Malformed)
                {
                    relationships.MarkIncomplete(history.AggregateId);
                    continue;
                }

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

        List<ReadModelBatchOperation> operations =
        [
            ReadModelBatchOperation.Write(
                WorksReadModelKeys.CurrentWhatsNextIndexKey(identity.TenantId),
                index,
                ReadModelBatchConcurrency.LastWrite),
        ];
        foreach ((string aggregateId, WorkItemRollUp rollUp) in rollUps.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations.Add(ReadModelBatchOperation.Write(
                WorksReadModelKeys.CurrentRollUpKey(identity.TenantId, aggregateId),
                rollUp,
                ReadModelBatchConcurrency.LastWrite));
        }

        cancellationToken.ThrowIfCancellationRequested();
        operations.Add(ReadModelBatchOperation.Delete(
            WorksReadModelKeys.LegacyWhatsNextIndexKey(identity.TenantId),
            ReadModelBatchConcurrency.IdempotentAbsent));
        foreach (string member in members.OrderBy(static id => id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations.Add(ReadModelBatchOperation.Delete(
                WorksReadModelKeys.LegacyRollUpKey(identity.TenantId, member),
                ReadModelBatchConcurrency.IdempotentAbsent));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new DomainProjectionRebuildPlan(WorksReadModelKeys.StateStoreName, operations);
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
