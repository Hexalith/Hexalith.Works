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
/// ordering across rejection-position snapshots, mid-stream rejections, and repeated rejections before create.
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
    private static readonly DateTimeOffset SnapshotCreatedAt = new(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);

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
    public async Task SnapshotAfterRejectionRehydratesAtCurrentSequenceWithoutTail()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var stateManager = new InMemoryStateManager();
        var persister = new EventPersister(
            stateManager,
            Substitute.For<ILogger<EventPersister>>(),
            new NoOpEventPayloadProtectionService());
        var reader = new EventStreamReader(stateManager, Substitute.For<ILogger<EventStreamReader>>());

        var rejectedCommand = new CreateWorkItem(Tenant, Item, Obligation: null);
        DomainResult rejectionResult = await ProcessAsync(
            rejectedCommand,
            "01ARZ3NDEKTSV4RRFFQ69G5FB0",
            currentStream: null);
        rejectionResult.IsRejection.ShouldBeTrue();
        await CommitAsync(
            stateManager,
            persister,
            rejectedCommand,
            "01ARZ3NDEKTSV4RRFFQ69G5FB0",
            rejectionResult,
            cancellationToken);

        RehydrationResult rejectionStream = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        AggregateReconstructionResult rejectionReplay = Replay(rejectionStream, includeTimeline: true);
        AssertUnknownStateIsUnchanged(rejectionReplay.StateJson);

        var snapshotState = new WorkItemState();
        ApplyByConvention(snapshotState, rejectionResult.Events.Single().ShouldBeAssignableTo<IRejectionEvent>());
        SerializeState(snapshotState).ShouldBe(rejectionReplay.StateJson);
        var rejectionPositionSnapshot = new SnapshotRecord(
            SequenceNumber: 1,
            State: snapshotState,
            CreatedAt: SnapshotCreatedAt,
            Domain: Domain,
            AggregateId: Item.Value,
            TenantId: Tenant.Value);

        RehydrationResult snapshotBacked = (await reader.RehydrateAsync(Identity, rejectionPositionSnapshot)).ShouldNotBeNull();
        snapshotBacked.Events.ShouldBeEmpty();
        snapshotBacked.LastSnapshotSequence.ShouldBe(1);
        snapshotBacked.CurrentSequence.ShouldBe(1);
        snapshotBacked.SnapshotState.ShouldBeSameAs(snapshotState);

        var acceptedCommand = new CreateWorkItem(Tenant, Item, "Create through a rejection-position snapshot");
        DomainResult createResult = await ProcessAsync(
            acceptedCommand,
            "01ARZ3NDEKTSV4RRFFQ69G5FB1",
            snapshotBacked);
        createResult.Events.Single().ShouldBeOfType<WorkItemCreated>().Sequence.ShouldBe(1);
        await CommitAsync(
            stateManager,
            persister,
            acceptedCommand,
            "01ARZ3NDEKTSV4RRFFQ69G5FB1",
            createResult,
            cancellationToken);

        RehydrationResult committed = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        committed.Events.Select(static envelope => envelope.SequenceNumber).ShouldBe([1L, 2L]);
        AggregateReconstructionResult completedReplay = Replay(committed, includeTimeline: true);
        AssertState(completedReplay.StateJson, WorkItemStatus.Created, expectedPayloadOrdinal: 1);

        // Everything above is also satisfied by a rehydration that DROPS the snapshot: the
        // rejection-position snapshot state is byte-identical to a default WorkItemState, and the
        // pinned rehydrator maps a missing snapshot with no tail to a null state, from which the
        // aggregate reads the same Unknown status and the same next ordinal 1. One more command is
        // therefore driven through a snapshot carrying ESTABLISHED state, where dropping the snapshot
        // flips the outcome from rejection to success.
        var createPositionSnapshot = new SnapshotRecord(
            SequenceNumber: 2,
            State: PopulatedState(),
            CreatedAt: SnapshotCreatedAt,
            Domain: Domain,
            AggregateId: Item.Value,
            TenantId: Tenant.Value);

        RehydrationResult establishedSnapshot =
            (await reader.RehydrateAsync(Identity, createPositionSnapshot)).ShouldNotBeNull();
        establishedSnapshot.Events.ShouldBeEmpty();
        establishedSnapshot.LastSnapshotSequence.ShouldBe(2);
        establishedSnapshot.CurrentSequence.ShouldBe(2);

        DomainResult duplicateCreate = await ProcessAsync(
            new CreateWorkItem(Tenant, Item, "Duplicate create through an established-state snapshot"),
            "01ARZ3NDEKTSV4RRFFQ69G5FB8",
            establishedSnapshot);
        duplicateCreate.IsRejection.ShouldBeTrue(
            "A create handled through a Created-state snapshot must reject; accepting it would mean the "
            + "snapshot state never reached the aggregate and the snapshot-backed path proves nothing.");
        duplicateCreate.Events
            .Single()
            .ShouldBeOfType<WorkItemTransitionRejected>()
            .FromStatus.ShouldBe(WorkItemStatus.Created);
    }

    [Fact]
    public async Task MidStreamRejectionAdvancesEnvelopeButNotPayloadOrdinal()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var stateManager = new InMemoryStateManager();
        var persister = new EventPersister(
            stateManager,
            Substitute.For<ILogger<EventPersister>>(),
            new NoOpEventPayloadProtectionService());
        var reader = new EventStreamReader(stateManager, Substitute.For<ILogger<EventStreamReader>>());

        var create = new CreateWorkItem(Tenant, Item, "Create before a rejected transition");
        DomainResult createResult = await ProcessAsync(create, "01ARZ3NDEKTSV4RRFFQ69G5FB2", currentStream: null);
        await CommitAsync(stateManager, persister, create, "01ARZ3NDEKTSV4RRFFQ69G5FB2", createResult, cancellationToken);

        RehydrationResult afterCreate = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        var illegalComplete = new CompleteWorkItem(Tenant, Item);
        DomainResult rejectionResult = await ProcessAsync(illegalComplete, "01ARZ3NDEKTSV4RRFFQ69G5FB3", afterCreate);
        rejectionResult.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected expectedRejection = rejectionResult.Events.Single()
            .ShouldBeOfType<WorkItemTransitionRejected>();
        await CommitAsync(
            stateManager,
            persister,
            illegalComplete,
            "01ARZ3NDEKTSV4RRFFQ69G5FB3",
            rejectionResult,
            cancellationToken);

        RehydrationResult afterRejection = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        var assign = new AssignWorkItem(Tenant, Item, WorkItemV1Catalog.Binding);
        DomainResult assignResult = await ProcessAsync(assign, "01ARZ3NDEKTSV4RRFFQ69G5FB4", afterRejection);
        assignResult.Events.Single().ShouldBeOfType<WorkItemAssigned>().Sequence.ShouldBe(2);
        await CommitAsync(stateManager, persister, assign, "01ARZ3NDEKTSV4RRFFQ69G5FB4", assignResult, cancellationToken);

        RehydrationResult committed = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        committed.Events.Select(static envelope => envelope.SequenceNumber).ShouldBe([1L, 2L, 3L]);
        JsonSerializer.Deserialize<WorkItemCreated>(committed.Events[0].Payload, JsonOptions)
            .ShouldNotBeNull().Sequence.ShouldBe(1);
        committed.Events[1].EventTypeName.ShouldContain(nameof(WorkItemTransitionRejected));
        WorkItemTransitionRejected persistedRejection = JsonSerializer
            .Deserialize<WorkItemTransitionRejected>(committed.Events[1].Payload, JsonOptions)
            .ShouldNotBeNull();
        persistedRejection.ShouldBe(expectedRejection);
        persistedRejection.FromStatus.ShouldBe(expectedRejection.FromStatus);
        persistedRejection.AttemptedAct.ShouldBe(expectedRejection.AttemptedAct);
        JsonSerializer.Deserialize<WorkItemAssigned>(committed.Events[2].Payload, JsonOptions)
            .ShouldNotBeNull().Sequence.ShouldBe(2);

        AggregateReconstructionResult replay = Replay(committed, includeTimeline: true);
        IReadOnlyList<AggregateReconstructionTimelineEntry> timeline = replay.Timeline.ShouldNotBeNull();
        timeline.Select(static entry => entry.SequenceNumber).ShouldBe([1L, 2L, 3L]);
        AssertState(timeline[0].StateJson, WorkItemStatus.Created, expectedPayloadOrdinal: 1);
        AssertState(timeline[1].StateJson, WorkItemStatus.Created, expectedPayloadOrdinal: 1);
        AssertState(timeline[2].StateJson, WorkItemStatus.Assigned, expectedPayloadOrdinal: 2);
    }

    [Fact]
    public async Task RepeatedPreCreateRejectionsPreserveIndependentEvidenceBeforeCreate()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var stateManager = new InMemoryStateManager();
        var persister = new EventPersister(
            stateManager,
            Substitute.For<ILogger<EventPersister>>(),
            new NoOpEventPayloadProtectionService());
        var reader = new EventStreamReader(stateManager, Substitute.For<ILogger<EventStreamReader>>());

        CreateWorkItem missingObligation = RejectedCreate("missing-obligation");
        DomainResult firstRejection = await ProcessAsync(missingObligation, "01ARZ3NDEKTSV4RRFFQ69G5FB5", currentStream: null);
        await CommitAsync(
            stateManager,
            persister,
            missingObligation,
            "01ARZ3NDEKTSV4RRFFQ69G5FB5",
            firstRejection,
            cancellationToken);

        RehydrationResult afterFirst = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        CreateWorkItem crossTenantParent = RejectedCreate("cross-tenant-parent");
        DomainResult secondRejection = await ProcessAsync(crossTenantParent, "01ARZ3NDEKTSV4RRFFQ69G5FB6", afterFirst);
        await CommitAsync(
            stateManager,
            persister,
            crossTenantParent,
            "01ARZ3NDEKTSV4RRFFQ69G5FB6",
            secondRejection,
            cancellationToken);

        RehydrationResult afterSecond = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        var create = new CreateWorkItem(Tenant, Item, "Create after two persisted rejections");
        DomainResult createResult = await ProcessAsync(create, "01ARZ3NDEKTSV4RRFFQ69G5FB7", afterSecond);
        createResult.Events.Single().ShouldBeOfType<WorkItemCreated>().Sequence.ShouldBe(1);
        await CommitAsync(stateManager, persister, create, "01ARZ3NDEKTSV4RRFFQ69G5FB7", createResult, cancellationToken);

        RehydrationResult committed = (await reader.RehydrateAsync(Identity)).ShouldNotBeNull();
        committed.Events.Select(static envelope => envelope.SequenceNumber).ShouldBe([1L, 2L, 3L]);
        WorkItemCannotBeCreatedWithoutObligation persistedFirst = JsonSerializer
            .Deserialize<WorkItemCannotBeCreatedWithoutObligation>(committed.Events[0].Payload, JsonOptions)
            .ShouldNotBeNull();
        persistedFirst.ShouldBe(firstRejection.Events.Single());
        WorkItemCannotReferenceParentFromAnotherTenant persistedSecond = JsonSerializer
            .Deserialize<WorkItemCannotReferenceParentFromAnotherTenant>(committed.Events[1].Payload, JsonOptions)
            .ShouldNotBeNull();
        persistedSecond.ShouldBe(secondRejection.Events.Single());
        persistedSecond.Parent.ShouldBe(new ParentWorkItemReference(new TenantId("tenant-beta"), new WorkItemId("parent-001")));

        AggregateReconstructionResult replay = Replay(committed, includeTimeline: true);
        IReadOnlyList<AggregateReconstructionTimelineEntry> timeline = replay.Timeline.ShouldNotBeNull();
        AssertState(timeline[0].StateJson, WorkItemStatus.Unknown, expectedPayloadOrdinal: 0);
        AssertState(timeline[1].StateJson, WorkItemStatus.Unknown, expectedPayloadOrdinal: 0);
        AssertState(timeline[2].StateJson, WorkItemStatus.Created, expectedPayloadOrdinal: 1);
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

    private static CommandEnvelope CommandFor(object command, string messageId)
    {
        // Every Works command carries TenantId/WorkItemId but they share no common interface, so the
        // envelope identity is read off the command by name rather than pinned to the fixture constants:
        // a command aimed at another tenant or work item must not be silently re-addressed to this
        // aggregate by the harness that is supposed to prove addressing.
        PropertyInfo tenantProperty = command.GetType()
            .GetProperty(nameof(CreateWorkItem.TenantId))
            .ShouldNotBeNull();
        PropertyInfo workItemProperty = command.GetType()
            .GetProperty(nameof(CreateWorkItem.WorkItemId))
            .ShouldNotBeNull();

        return new(
            MessageId: messageId,
            TenantId: tenantProperty.GetValue(command).ShouldBeOfType<TenantId>().Value,
            Domain: Domain,
            AggregateId: workItemProperty.GetValue(command).ShouldBeOfType<WorkItemId>().Value,
            CommandType: command.GetType().FullName!,
            Payload: JsonSerializer.SerializeToUtf8Bytes(command, command.GetType()),
            CorrelationId: messageId,
            CausationId: null,
            UserId: "test-user",
            Extensions: null);
    }

    private static Task<DomainResult> ProcessAsync(object command, string messageId, RehydrationResult? currentStream)
        => new WorkItemEventStoreAggregate().ProcessAsync(
            CommandFor(command, messageId),
            currentStream is null ? null : ToDomainServiceCurrentState(currentStream));

    private static async Task CommitAsync(
        InMemoryStateManager stateManager,
        EventPersister persister,
        object command,
        string messageId,
        DomainResult result,
        CancellationToken cancellationToken)
    {
        _ = await persister.PersistEventsAsync(
            Identity,
            AggregateType,
            CommandFor(command, messageId),
            result,
            DomainServiceVersion,
            cancellationToken).ConfigureAwait(false);
        await stateManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);
    }

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

}
