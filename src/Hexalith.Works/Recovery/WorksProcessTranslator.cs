using System.Text.Json;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Processes;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Hexalith.Works.Runtime;

namespace Hexalith.Works.Recovery;

/// <summary>Acquires authoritative read-only relationship context around the pure Works Reactor translations.</summary>
internal sealed class WorksProcessTranslator(WorksSourceStreamReader streams) : IProcessTranslator
{
    private readonly WorksSourceStreamReader _streams = streams ?? throw new ArgumentNullException(nameof(streams));

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ProcessEffect>> TranslateAsync(ProcessSource source, StreamReadEvent streamEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(streamEvent);
        source.Validate();
        if (source.Domain != "work" || streamEvent.MetadataVersion != 1 || streamEvent.PayloadVersion is not (null or 1)
            || !string.Equals(streamEvent.SerializationFormat, "json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Works process evidence has invalid scope or encoding.");
        IEventPayload payload = WorksRecoveryEventDecoder.Decode(streamEvent.EventTypeName, streamEvent.Payload);
        if (!WorksEventIdentity.Matches(payload, source.Tenant, source.Aggregate))
            throw new InvalidOperationException("Works process payload differs from its source identity.");
        WorksExpiryEvidence.Validate(payload, source.Tenant, source.Aggregate);
        if (payload is not (WorkItemCancelled or WorkItemExpired or WorkItemCompleted or WorkItemRescheduled)) return [];

        IReadOnlyList<WorksCommittedEvent> history = await _streams.ReadAsync(source.Tenant, source.Aggregate, cancellationToken).ConfigureAwait(false);
        WorksCommittedEvent committed = history.SingleOrDefault(fact => fact.Envelope.SequenceNumber == streamEvent.SequenceNumber)
            ?? throw new InvalidOperationException("The process event is absent from its committed source stream.");
        if (committed.Envelope.MessageId != streamEvent.MessageId
            || committed.Envelope.EventTypeName != streamEvent.EventTypeName
            || !committed.Envelope.Payload.AsSpan().SequenceEqual(streamEvent.Payload))
            throw new InvalidOperationException("Process event semantics differ from the committed source.");

        if (payload is WorkItemRescheduled) return [];

        if (payload is WorkItemCompleted completed)
            return await ChildResumeAsync(source, streamEvent, completed, history, cancellationToken).ConfigureAwait(false);

        ChildSpawned[] children = [.. history.Where(fact => fact.Envelope.SequenceNumber < streamEvent.SequenceNumber)
            .Select(fact => fact.Payload).OfType<ChildSpawned>()
            .DistinctBy(child => child.ChildWorkItemId.Value).OrderBy(child => child.ChildWorkItemId.Value, StringComparer.Ordinal)];
        var descendants = new List<CascadeDescendant>();
        foreach (ChildSpawned child in children)
        {
            IReadOnlyList<WorksCommittedEvent> related;
            try
            {
                related = await _streams.ReadAsync(source.Tenant, child.ChildWorkItemId.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (EventStoreGatewayException exception) when (exception.StatusCode == 404
                && (exception.Code == StreamReplayReasonCodes.MissingStream || exception.ReasonCode == StreamReplayReasonCodes.MissingStream))
            {
                // SpawnChild commits the parent relationship before the separate child CreateWorkItem.
                // Only this known related-child read can interpret canonical MissingStream as a creation gap.
                throw new ProcessEvidenceUnavailableException(exception);
            }
            WorkItemCreated? creation = related.Select(fact => fact.Payload).OfType<WorkItemCreated>().SingleOrDefault();
            if (creation is null) throw new ProcessEvidenceUnavailableException();
            if (creation.Parent is null || creation.Parent.TenantId.Value != source.Tenant
                || creation.Parent.WorkItemId.Value != source.Aggregate)
                throw new InvalidOperationException("Works cascade relationship evidence is inconsistent.");
            // Source membership is immutable. Current terminal state cannot remove a planned effect after a
            // target commit: all direct targets receive the same intent and their aggregate returns a receipt
            // for success, rejection, or no-op. This preserves receipt replay before source checkpoint advance.
            descendants.Add(new CascadeDescendant(child.TenantId, child.ChildWorkItemId, IsTerminal: false));
        }

        return payload switch
        {
            WorkItemCancelled cancelled => [.. TerminalCascadeTranslator.ToCascadeCommands(cancelled, descendants)
                .Select(command => Effect(EffectKindCatalog.CascadeCancel, command.WorkItemId.Value, nameof(CancelWorkItem), command))],
            WorkItemExpired expired => [.. TerminalCascadeTranslator.ToCascadeCommands(expired, descendants)
                .Select(command => Effect(EffectKindCatalog.CascadeExpire, command.WorkItemId.Value, nameof(ExpireWorkItem), command))],
            _ => [],
        };
    }

    private async Task<IReadOnlyList<ProcessEffect>> ChildResumeAsync(ProcessSource source, StreamReadEvent envelope,
        WorkItemCompleted completed, IReadOnlyList<WorksCommittedEvent> history, CancellationToken cancellationToken)
    {
        ParentWorkItemReference? parent = history.Select(fact => fact.Payload).OfType<WorkItemCreated>().SingleOrDefault()?.Parent;
        if (parent is null) return [];
        if (parent.TenantId.Value != source.Tenant)
            throw new InvalidOperationException("Works child-completion parent is outside the source tenant.");
        IReadOnlyList<WorksCommittedEvent> related = await _streams.ReadAsync(source.Tenant, parent.WorkItemId.Value, cancellationToken).ConfigureAwait(false);
        if (!related.Select(fact => fact.Payload).OfType<ChildSpawned>().Any(child => child.ChildWorkItemId == completed.WorkItemId))
            throw new InvalidOperationException("Works child-completion parent has no matching child relationship.");
        IReadOnlyList<AwaitCondition> conditions = [];
        foreach (WorksCommittedEvent fact in related)
        {
            switch (fact.Payload)
            {
                case WorkItemSuspended suspended: conditions = suspended.AwaitConditions; break;
                case WorkItemResumed or WorkItemCancelled or WorkItemExpired or WorkItemCompleted or WorkItemRejected:
                    conditions = []; break;
            }
        }

        return [.. ChildCompletionResumeTranslator.ToResumeCommands(completed,
            [new AwaitingParent(parent.TenantId, parent.WorkItemId, conditions)])
            .Select(command => Effect(EffectKindCatalog.ChildCompletionResume, command.WorkItemId.Value, nameof(ResumeWorkItem), command))];
    }

    private static ProcessEffect Effect<TCommand>(string kind, string target, string type, TCommand command)
        => new(kind, "work", target, EffectKindCatalog.PrimaryTargetOrdinal, type, JsonSerializer.SerializeToUtf8Bytes(command));
}
