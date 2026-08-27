using System.Reflection;
using System.Text.Json;

using Hexalith.EventStore.Client.Aggregates;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Identity;
using Hexalith.EventStore.Contracts.Replay;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.EventStore.Contracts.Serialization;
using Hexalith.EventStore.Server.Events;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Shouldly;

using ContractEventEnvelope = Hexalith.EventStore.Contracts.Events.EventEnvelope;
using StoredEventEnvelope = Hexalith.EventStore.Server.Events.EventEnvelope;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Proves that EventStore envelope positions, rather than Works payload ordinals, define persisted
/// ordering when a rejection precedes the first state-changing event.
/// </summary>
public sealed class EnvelopeCanonicalSequencingTests
{
    private const string Domain = "work";
    private const string AggregateType = "work-item";
    private const string DomainServiceVersion = "v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly AggregateIdentity Identity = new(Tenant.Value, Domain, Item.Value);
    private static readonly IReadOnlyDictionary<Type, string> RejectionShapeSignatures =
        new Dictionary<Type, string>
        {
            [typeof(WorkItemTransitionRejected)] = "{TenantId:{Value:String},WorkItemId:{Value:String},FromStatus:String,AttemptedAct:String}",
            [typeof(WorkItemProgressRejected)] = "{TenantId:{Value:String},WorkItemId:{Value:String},Reason:String}",
            [typeof(WorkItemReEstimateRejected)] = "{TenantId:{Value:String},WorkItemId:{Value:String},Reason:String}",
            [typeof(WorkItemInitialEffortRejected)] = "{TenantId:{Value:String},WorkItemId:{Value:String},Done:Number}",
            [typeof(WorkItemCannotBeCreatedWithoutObligation)] = "{TenantId:{Value:String},WorkItemId:{Value:String}}",
            [typeof(WorkItemCannotReferenceParentFromAnotherTenant)] = "{TenantId:{Value:String},WorkItemId:{Value:String},Parent:{TenantId:{Value:String},WorkItemId:{Value:String}}}",
            [typeof(WorkItemCannotReferenceSecondParent)] = "{TenantId:{Value:String},WorkItemId:{Value:String},ExistingParent:{TenantId:{Value:String},WorkItemId:{Value:String}},ProposedParent:{TenantId:{Value:String},WorkItemId:{Value:String}}}",
            [typeof(WorkItemTreeCycleRejected)] = "{TenantId:{Value:String},WorkItemId:{Value:String},ProposedParent:{TenantId:{Value:String},WorkItemId:{Value:String}},CycleWorkItemId:{Value:String}}",
            [typeof(WorkItemTreeDepthExceeded)] = "{TenantId:{Value:String},WorkItemId:{Value:String},ProposedParent:{TenantId:{Value:String},WorkItemId:{Value:String}},MaxDepth:Number,ResultingDepth:Number}",
        };

    [Theory]
    [InlineData("missing-obligation")]
    [InlineData("cross-tenant-parent")]
    public async Task CommittedRejectionThenCreateUsesEnvelopeCanonicalSequencing(string rejectionLedgerName)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var stateManager = new InMemoryStateManager();
        var persister = new EventPersister(
            stateManager,
            Substitute.For<ILogger<EventPersister>>(),
            new NoOpEventPayloadProtectionService());
        var reader = new EventStreamReader(stateManager, Substitute.For<ILogger<EventStreamReader>>());

        CreateWorkItem rejectedCommand = RejectedCreate(rejectionLedgerName);
        DomainResult rejectionResult = WorkItemAggregate.Handle(rejectedCommand, new WorkItemState());

        rejectionResult.IsRejection.ShouldBeTrue();
        IRejectionEvent rejection = rejectionResult.Events.Single().ShouldBeAssignableTo<IRejectionEvent>();
        AssertLedgerEvidence(rejectionLedgerName, rejection);

        _ = await persister.PersistEventsAsync(
            Identity,
            AggregateType,
            CommandFor(rejectedCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAV"),
            rejectionResult,
            DomainServiceVersion,
            cancellationToken);
        await stateManager.SaveStateAsync(cancellationToken);

        stateManager.CommittedState.ShouldContainKey($"{Identity.EventStreamKeyPrefix}1");
        RehydrationResult rejectionOnlyStream = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        rejectionOnlyStream.Events.Select(static envelope => envelope.SequenceNumber).ShouldBe([1L]);

        AggregateReconstructionResult rejectionOnlyReplay = Replay(rejectionOnlyStream, includeTimeline: true);
        rejectionOnlyReplay.Status.ShouldBe(AggregateReconstructionStatus.Succeeded);
        rejectionOnlyReplay.LastAppliedSequenceNumber.ShouldBe(1);
        IReadOnlyList<AggregateReconstructionTimelineEntry> rejectionTimeline = rejectionOnlyReplay.Timeline.ShouldNotBeNull();
        rejectionTimeline.Count.ShouldBe(1);
        rejectionTimeline[0].SequenceNumber.ShouldBe(1);
        AssertUnknownStateIsUnchanged(rejectionTimeline[0].StateJson);
        AssertUnknownStateIsUnchanged(rejectionOnlyReplay.StateJson);

        var acceptedCommand = new CreateWorkItem(Tenant, Item, "Create after the persisted rejection");
        var aggregate = new WorkItemEventStoreAggregate();
        DomainResult createResult = await aggregate.ProcessAsync(
            CommandFor(acceptedCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAW"),
            ToDomainServiceCurrentState(rejectionOnlyStream));
        WorkItemCreated created = createResult.Events.Single().ShouldBeOfType<WorkItemCreated>();
        created.Sequence.ShouldBe(1);

        _ = await persister.PersistEventsAsync(
            Identity,
            AggregateType,
            CommandFor(acceptedCommand, "01ARZ3NDEKTSV4RRFFQ69G5FAW"),
            createResult,
            DomainServiceVersion,
            cancellationToken);
        await stateManager.SaveStateAsync(cancellationToken);

        stateManager.CommittedState.ShouldContainKey($"{Identity.EventStreamKeyPrefix}2");
        RehydrationResult committedStream = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        committedStream.Events.Select(static envelope => envelope.SequenceNumber).ShouldBe([1L, 2L]);

        StoredEventEnvelope persistedRejection = committedStream.Events[0];
        StoredEventEnvelope persistedCreate = committedStream.Events[1];
        persistedRejection.EventTypeName.ShouldContain(rejection.GetType().Name);
        persistedCreate.EventTypeName.ShouldContain(nameof(WorkItemCreated));
        byte[] expectedRejectionPayload = JsonSerializer.SerializeToUtf8Bytes(rejection, rejection.GetType());
        persistedRejection.Payload.ShouldBe(expectedRejectionPayload);
        IRejectionEvent persistedRejectionPayload = JsonSerializer
            .Deserialize(persistedRejection.Payload, rejection.GetType(), JsonOptions)
            .ShouldNotBeNull()
            .ShouldBeAssignableTo<IRejectionEvent>();
        persistedRejectionPayload.ShouldBe(rejection);
        AssertLedgerEvidence(rejectionLedgerName, persistedRejectionPayload);
        using (JsonDocument persistedRejectionDocument = JsonDocument.Parse(persistedRejection.Payload))
        {
            RejectionShapeSignatures.ShouldContainKey(rejection.GetType());
            ShapeOf(persistedRejectionDocument.RootElement)
                .ShouldBe(RejectionShapeSignatures[rejection.GetType()]);
        }
        JsonSerializer.Deserialize<WorkItemCreated>(persistedCreate.Payload, JsonOptions)
            .ShouldNotBeNull()
            .Sequence
            .ShouldBe(1);

        AggregateReconstructionResult completedReplay = Replay(committedStream, includeTimeline: true);
        completedReplay.Status.ShouldBe(AggregateReconstructionStatus.Succeeded);
        completedReplay.LastAppliedSequenceNumber.ShouldBe(2);
        IReadOnlyList<AggregateReconstructionTimelineEntry> completedTimeline = completedReplay.Timeline.ShouldNotBeNull();
        completedTimeline.Select(static entry => entry.SequenceNumber).ShouldBe([1L, 2L]);
        AssertState(completedTimeline[0].StateJson, WorkItemStatus.Unknown, expectedPayloadOrdinal: 0);
        AssertState(completedTimeline[1].StateJson, WorkItemStatus.Created, expectedPayloadOrdinal: 1);
        AssertState(completedReplay.StateJson, WorkItemStatus.Created, expectedPayloadOrdinal: 1);
    }

    [Fact]
    public void FrozenV1RejectionPayloadsRetainTheirExactSerializedShapes()
    {
        IRejectionEvent[] rejections = WorkItemV1Catalog.All.OfType<IRejectionEvent>().ToArray();
        rejections.Length.ShouldBe(9, "The frozen v1 catalog must still hold exactly 9 rejection payloads.");

        // Freeze the table in both directions: every catalog rejection has a signature (below) AND no
        // signature outlives the type it froze, so a retired or renamed rejection cannot leave dead cover.
        RejectionShapeSignatures.Keys.ShouldBe(
            rejections.Select(static rejection => rejection.GetType()),
            ignoreOrder: true,
            customMessage: "The recorded rejection shape signatures must match the frozen v1 rejection catalog exactly.");

        foreach (IRejectionEvent rejection in rejections)
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(rejection, rejection.GetType());
            using JsonDocument document = JsonDocument.Parse(payload);
            RejectionShapeSignatures.ShouldContainKey(
                rejection.GetType(),
                $"Frozen v1 rejection '{rejection.GetType().Name}' has no recorded serialized shape signature.");
            ShapeOf(document.RootElement).ShouldBe(RejectionShapeSignatures[rejection.GetType()]);
        }
    }

    [Fact]
    public void EveryV1RejectionApplyAndReplayPathIsANoOpForUnknownAndPopulatedState()
    {
        IRejectionEvent[] rejections = WorkItemV1Catalog.All.OfType<IRejectionEvent>().ToArray();
        rejections.Length.ShouldBe(9, "The frozen v1 catalog must still hold exactly 9 rejection payloads.");

        foreach (IRejectionEvent rejection in rejections)
        {
            var unknown = new WorkItemState();
            string unknownBefore = SerializeState(unknown);
            ApplyByConvention(unknown, rejection);
            SerializeState(unknown).ShouldBe(unknownBefore);
            unknown.AggregateIdentity.ShouldBeNull();

            WorkItemState populated = PopulatedState();
            string populatedBefore = SerializeState(populated);
            ApplyByConvention(populated, rejection);
            SerializeState(populated).ShouldBe(populatedBefore);

            AggregateReconstructionResult unknownReplay = ReplayPayloads([rejection]);
            unknownReplay.Status.ShouldBe(AggregateReconstructionStatus.Succeeded);
            unknownReplay.LastAppliedSequenceNumber.ShouldBe(1);
            AssertUnknownStateIsUnchanged(unknownReplay.StateJson);

            AggregateReconstructionResult populatedReplay = ReplayPayloads([CreatedPayload(), rejection]);
            populatedReplay.Status.ShouldBe(AggregateReconstructionStatus.Succeeded);
            populatedReplay.LastAppliedSequenceNumber.ShouldBe(2);
            IReadOnlyList<AggregateReconstructionTimelineEntry> timeline = populatedReplay.Timeline.ShouldNotBeNull();
            timeline.Count.ShouldBe(2);
            timeline[1].StateJson.ShouldBe(timeline[0].StateJson);
            AssertState(populatedReplay.StateJson, WorkItemStatus.Created, expectedPayloadOrdinal: 1);
        }
    }

    private static CreateWorkItem RejectedCreate(string rejectionLedgerName)
        => rejectionLedgerName switch
        {
            "missing-obligation" => new CreateWorkItem(Tenant, Item, Obligation: null),
            "cross-tenant-parent" => new CreateWorkItem(
                Tenant,
                Item,
                "Rejected foreign parent",
                Parent: new ParentWorkItemReference(new TenantId("tenant-beta"), new WorkItemId("parent-001"))),
            _ => throw new ArgumentOutOfRangeException(nameof(rejectionLedgerName), rejectionLedgerName, "Unknown rejection ledger name."),
        };

    private static void AssertLedgerEvidence(string rejectionLedgerName, IRejectionEvent rejection)
    {
        switch (rejectionLedgerName)
        {
            case "missing-obligation":
                _ = rejection.ShouldBeOfType<WorkItemCannotBeCreatedWithoutObligation>();
                break;
            case "cross-tenant-parent":
                WorkItemCannotReferenceParentFromAnotherTenant crossTenant =
                    rejection.ShouldBeOfType<WorkItemCannotReferenceParentFromAnotherTenant>();
                crossTenant.Parent.TenantId.ShouldBe(new TenantId("tenant-beta"));
                crossTenant.Parent.WorkItemId.ShouldBe(new WorkItemId("parent-001"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rejectionLedgerName), rejectionLedgerName, "Unknown rejection ledger name.");
        }
    }

    private static CommandEnvelope CommandFor(CreateWorkItem command, string messageId)
        => new(
            MessageId: messageId,
            TenantId: command.TenantId.Value,
            Domain: Domain,
            AggregateId: command.WorkItemId.Value,
            CommandType: typeof(CreateWorkItem).FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(command, command.GetType()),
            CorrelationId: messageId,
            CausationId: null,
            UserId: "test-user",
            Extensions: null);

    private static AggregateReconstructionResult Replay(RehydrationResult stream, bool includeTimeline)
        => AggregateReplayer.Replay<WorkItemState>(new AggregateReconstructionRequest(
            TenantId: Tenant.Value,
            Domain: Domain,
            AggregateType: AggregateType,
            AggregateId: Item.Value,
            UpToSequence: stream.CurrentSequence,
            // Deliberately handed to the replayer in DESCENDING envelope order: canonical ordering must come
            // from AggregateReplayer's own sort over envelope SequenceNumber, never from the caller's input
            // order. A single-envelope stream makes the reversal a no-op, so the multi-envelope committed
            // stream is what actually exercises it.
            Events: stream.Events
                .AsEnumerable()
                .Reverse()
                .Select(static envelope => new ReplayEventEnvelope(
                    envelope.SequenceNumber,
                    envelope.EventTypeName,
                    envelope.Payload,
                    envelope.SerializationFormat,
                    envelope.MetadataVersion,
                    envelope.MessageId,
                    envelope.CorrelationId,
                    envelope.CausationId))
                .ToArray(),
            IncludeTimeline: includeTimeline,
            RequestId: "envelope-canonical-sequencing"));

    private static DomainServiceCurrentState ToDomainServiceCurrentState(RehydrationResult stream)
        => new(
            SnapshotState: stream.SnapshotState,
            Events: [.. stream.Events.Select(ToContractEnvelope)],
            LastSnapshotSequence: stream.LastSnapshotSequence,
            CurrentSequence: stream.CurrentSequence);

    private static ContractEventEnvelope ToContractEnvelope(StoredEventEnvelope envelope)
        => new(
            new EventMetadata(
                envelope.MessageId,
                envelope.AggregateId,
                envelope.AggregateType,
                envelope.TenantId,
                envelope.Domain,
                envelope.SequenceNumber,
                envelope.GlobalPosition,
                envelope.Timestamp,
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.UserId,
                envelope.DomainServiceVersion,
                envelope.EventTypeName,
                envelope.MetadataVersion,
                envelope.SerializationFormat),
            envelope.Payload,
            envelope.Extensions is null ? null : new Dictionary<string, string>(envelope.Extensions));

    private static AggregateReconstructionResult ReplayPayloads(IReadOnlyList<IEventPayload> payloads)
        => AggregateReplayer.Replay<WorkItemState>(new AggregateReconstructionRequest(
            TenantId: Tenant.Value,
            Domain: Domain,
            AggregateType: AggregateType,
            AggregateId: Item.Value,
            UpToSequence: payloads.Count,
            Events: [.. payloads.Select((payload, index) => new ReplayEventEnvelope(
                SequenceNumber: index + 1,
                EventTypeName: payload.GetType().FullName ?? payload.GetType().Name,
                Payload: JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()),
                SerializationFormat: "json",
                MetadataVersion: 1,
                MessageId: $"01ARZ3NDEKTSV4RRFFQ69G5F{index + 1:D2}",
                CorrelationId: "01ARZ3NDEKTSV4RRFFQ69G5FAV",
                CausationId: "01ARZ3NDEKTSV4RRFFQ69G5FAV"))],
            IncludeTimeline: true,
            RequestId: "all-rejection-no-op-contracts"));

    private static WorkItemCreated CreatedPayload()
        => new(Item.Value, 1, Tenant, Item, new Obligation("Populate state before applying rejection"));

    private static WorkItemState PopulatedState()
    {
        var state = new WorkItemState();
        state.Apply(CreatedPayload());
        return state;
    }

    private static void ApplyByConvention(WorkItemState state, IRejectionEvent rejection)
    {
        MethodInfo apply = typeof(WorkItemState).GetMethod(nameof(WorkItemState.Apply), [rejection.GetType()])
            .ShouldNotBeNull();
        _ = apply.Invoke(state, [rejection]);
    }

    private static string SerializeState(WorkItemState state)
        => JsonSerializer.Serialize(state, EventStorePayloadSerialization.Options);

    private static void AssertUnknownStateIsUnchanged(string? stateJson)
    {
        string actual = stateJson.ShouldNotBeNull();
        actual.ShouldBe(SerializeState(new WorkItemState()));

        using JsonDocument document = JsonDocument.Parse(actual);
        document.RootElement.GetProperty("aggregateIdentity").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("tenantId").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("workItemId").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("status").GetString().ShouldBe(nameof(WorkItemStatus.Unknown));
        document.RootElement.GetProperty("sequence").GetInt64().ShouldBe(0);
    }

    private static void AssertState(string? stateJson, WorkItemStatus expectedStatus, long expectedPayloadOrdinal)
    {
        using JsonDocument document = JsonDocument.Parse(stateJson.ShouldNotBeNull());
        document.RootElement.GetProperty("status").GetString().ShouldBe(expectedStatus.ToString());
        document.RootElement.GetProperty("sequence").GetInt64().ShouldBe(expectedPayloadOrdinal);
    }

    private static string ShapeOf(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return $"{{{string.Join(',', element.EnumerateObject().Select(property => $"{property.Name}:{ShapeOf(property.Value)}"))}}}";
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return $"[{string.Join(',', element.EnumerateArray().Select(ShapeOf))}]";
        }

        return element.ValueKind.ToString();
    }
}
