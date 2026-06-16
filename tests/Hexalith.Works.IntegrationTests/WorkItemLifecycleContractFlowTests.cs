using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// End-to-end contract-flow tests for the Story 2.1 lifecycle: each accepted lifecycle command is
/// handled, the emitted event is driven through the real <see cref="System.Text.Json"/> serialization
/// boundary (write → persist → replay), and a separate replay <see cref="WorkItemState"/> — fed only
/// by the round-tripped events — is asserted to reach the expected status and sequence. This proves the
/// event-sourced write/replay loop survives serialization (NFR-2) with the minimal, envelope-free,
/// <c>(AggregateId, Sequence)</c>-first payload shape (AR-4). The in-memory <c>Handle</c>/<c>Apply</c>
/// matrix is covered exhaustively by the unit suite; these tests add the serialization-boundary slice.
/// </summary>
public sealed class WorkItemLifecycleContractFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly ExecutorBinding Binding = new(new PartyId("party-exec"), Channel.Mcp, AuthorityLevel.Administer);

    // Envelope / transport fields that must never leak into a persisted domain event payload.
    private static readonly string[] EnvelopeFields =
        ["messageId", "causationId", "correlationId", "userId", "metadata", "cloudEvent"];

    [Fact]
    public void Full_lifecycle_round_trips_through_serialization_to_completed()
    {
        // Write-side state issues commands (and advances as the aggregate would); the replay-side state
        // is rebuilt ONLY from round-tripped events to prove the two converge across serialization.
        var write = new WorkItemState();
        var replay = new WorkItemState();

        Advance(HandleCreate(write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.Created);

        Advance(Handle<WorkItemAssigned>(new AssignWorkItem(Tenant, Item, Binding), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.Assigned);
        replay.ExecutorBinding.ShouldBe(Binding);

        Advance(Handle<WorkItemClaimed>(new ClaimWorkItem(Tenant, Item, Binding), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.InProgress);

        Advance(Handle<WorkItemSuspended>(new SuspendWorkItem(Tenant, Item), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.Suspended);

        Advance(Handle<WorkItemResumed>(new ResumeWorkItem(Tenant, Item), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.InProgress);

        Advance(Handle<WorkItemCompleted>(new CompleteWorkItem(Tenant, Item), write), write, replay);

        // The full six-event stream replays from its serialized form to the terminal status with a
        // monotonic, gap-free sequence — identical to the authoritative write-side state.
        replay.Status.ShouldBe(WorkItemStatus.Completed);
        replay.Sequence.ShouldBe(6);
        replay.Sequence.ShouldBe(write.Sequence);
    }

    [Fact]
    public void Out_of_order_event_stream_replays_to_completed_when_resorted_by_sequence()
    {
        // AC #2: every event carries (AggregateId, Sequence) FOR ORDER-TOLERANT PROJECTIONS. Prove the
        // Sequence alone is sufficient to recover the canonical order: persist the full lifecycle stream
        // through serialization, deliver it OUT OF ORDER, and let a projection re-sort purely by Sequence
        // and replay to the identical terminal state.
        var write = new WorkItemState();
        var stream = new List<(long Sequence, IEventPayload Event)>();

        void Collect<T>(T emitted)
            where T : class
        {
            // Advancing the write state assigns write.Sequence the event's own Sequence (the field is
            // declared per concrete record, not on the IEventPayload marker), so we read the
            // authoritative sequence back from the state rather than reflecting over the payload.
            ApplyEvent(write, emitted);
            stream.Add((write.Sequence, (IEventPayload)RoundTripEvent(emitted)));
        }

        Collect(HandleCreate(write));
        Collect(Handle<WorkItemAssigned>(new AssignWorkItem(Tenant, Item, Binding), write));
        Collect(Handle<WorkItemClaimed>(new ClaimWorkItem(Tenant, Item, Binding), write));
        Collect(Handle<WorkItemSuspended>(new SuspendWorkItem(Tenant, Item), write));
        Collect(Handle<WorkItemResumed>(new ResumeWorkItem(Tenant, Item), write));
        Collect(Handle<WorkItemCompleted>(new CompleteWorkItem(Tenant, Item), write));

        stream.Count.ShouldBe(6); // Guard: never assert order-tolerance over an empty or short stream.

        // The persisted sequences are exactly the contiguous, gap-free 1..6 a projection can sort on.
        stream.Select(t => t.Sequence).OrderBy(s => s).ShouldBe(new long[] { 1, 2, 3, 4, 5, 6 });

        // Deliver the stream OUT OF ORDER (deterministic reverse — no RNG), then recover order from
        // Sequence and replay into an independent state.
        var replay = new WorkItemState();
        foreach ((long _, IEventPayload e) in stream.AsEnumerable().Reverse().OrderBy(t => t.Sequence))
        {
            ApplyEvent(replay, e);
        }

        replay.Status.ShouldBe(WorkItemStatus.Completed);
        replay.Sequence.ShouldBe(6);
        replay.Sequence.ShouldBe(write.Sequence);
    }

    [Fact]
    public void Created_branch_events_round_trip_and_replay_to_their_target_status()
    {
        // Queue, Cancel and Expire are not on the create→complete happy path; cover their serialized
        // contract from independent Created states so all nine success events cross the boundary.
        var queueReplay = new WorkItemState();
        Persist(HandleCreate(new WorkItemState()), queueReplay);
        Persist(Handle<WorkItemQueued>(new QueueWorkItem(Tenant, Item), Created()), queueReplay);
        queueReplay.Status.ShouldBe(WorkItemStatus.Queued);
        queueReplay.Sequence.ShouldBe(2);

        var cancelReplay = new WorkItemState();
        Persist(HandleCreate(new WorkItemState()), cancelReplay);
        Persist(Handle<WorkItemCancelled>(new CancelWorkItem(Tenant, Item), Created()), cancelReplay);
        cancelReplay.Status.ShouldBe(WorkItemStatus.Cancelled);

        var expireReplay = new WorkItemState();
        Persist(HandleCreate(new WorkItemState()), expireReplay);
        Persist(Handle<WorkItemExpired>(new ExpireWorkItem(Tenant, Item), Created()), expireReplay);
        expireReplay.Status.ShouldBe(WorkItemStatus.Expired);
    }

    [Fact]
    public void Reject_event_round_trips_and_the_requeue_flag_drives_the_resting_status()
    {
        // AC #5 across the serialization boundary: the requeue flag carried by WorkItemRejected must
        // survive round-trip and still steer replay to Queued (requeue) vs terminal Rejected (non-requeue).
        WorkItemRejected requeued = Handle<WorkItemRejected>(new RejectWorkItem(Tenant, Item, Requeue: true), Assigned());
        requeued.Requeue.ShouldBeTrue();
        var requeueReplay = Assigned();
        requeueReplay.Apply(RoundTripEvent(requeued));
        requeueReplay.Status.ShouldBe(WorkItemStatus.Queued);

        WorkItemRejected terminal = Handle<WorkItemRejected>(new RejectWorkItem(Tenant, Item, Requeue: false), Assigned());
        terminal.Requeue.ShouldBeFalse();
        var terminalReplay = Assigned();
        terminalReplay.Apply(RoundTripEvent(terminal));
        terminalReplay.Status.ShouldBe(WorkItemStatus.Rejected);
    }

    [Fact]
    public void Illegal_transition_serializes_a_transition_rejection_only()
    {
        // Claim from Created is illegal — the result is a rejection-only payload that is returned to the
        // caller (never appended to the stream), so it carries context but no sequence.
        var result = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, Binding), Created());

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        WorkItemTransitionRejected rejection = result.Events
            .Single()
            .ShouldBeOfType<WorkItemTransitionRejected>();

        string json = JsonSerializer.Serialize(rejection, JsonOptions);
        json.ShouldNotContain("\"sequence\"", Case.Insensitive, "Rejections are returned to the caller, not sequenced into the stream.");
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            foreach (string envelope in EnvelopeFields)
            {
                document.RootElement.TryGetProperty(envelope, out _)
                    .ShouldBeFalse($"WorkItemTransitionRejected must not serialize the '{envelope}' envelope field.");
            }
        }

        WorkItemTransitionRejected roundTripped =
            JsonSerializer.Deserialize<WorkItemTransitionRejected>(json, JsonOptions).ShouldNotBeNull();

        roundTripped.TenantId.ShouldBe(Tenant);
        roundTripped.WorkItemId.ShouldBe(Item);
        roundTripped.FromStatus.ShouldBe(WorkItemStatus.Created);
        roundTripped.AttemptedAct.ShouldBe("Claim");
    }

    [Fact]
    public void Assigned_event_round_trips_with_minimal_envelope_free_binding_payload()
    {
        // AR-4 contract guard: the binding is the only enriched field, and the (AggregateId, Sequence)
        // pair leads the serialized shape with no transport envelope attached.
        WorkItemAssigned assigned = Handle<WorkItemAssigned>(new AssignWorkItem(Tenant, Item, Binding), Created());

        string json = JsonSerializer.Serialize(assigned, JsonOptions);
        json.ShouldContain("\"aggregateId\"");
        json.ShouldContain("\"sequence\"");
        json.ShouldContain("\"party-exec\"");
        json.ShouldContain("\"channel\":\"Mcp\"");

        WorkItemAssigned roundTripped = RoundTripEvent(assigned);
        roundTripped.AggregateId.ShouldBe("work-001");
        roundTripped.Sequence.ShouldBe(2);
        roundTripped.TenantId.ShouldBe(Tenant);
        roundTripped.WorkItemId.ShouldBe(Item);
        roundTripped.Binding.ShouldBe(Binding);
    }

    private static WorkItemState Created()
    {
        var state = new WorkItemState();
        state.Apply(HandleCreate(state));
        return state;
    }

    private static WorkItemState Assigned()
    {
        WorkItemState state = Created();
        state.Apply(Handle<WorkItemAssigned>(new AssignWorkItem(Tenant, Item, Binding), state));
        return state;
    }

    private static WorkItemCreated HandleCreate(WorkItemState state)
        => WorkItemAggregate.Handle(new CreateWorkItem(Tenant, Item, "Drive the lifecycle through the contract boundary"), state)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

    private static T Handle<T>(object command, WorkItemState state)
        where T : class
    {
        var result = command switch
        {
            AssignWorkItem c => WorkItemAggregate.Handle(c, state),
            QueueWorkItem c => WorkItemAggregate.Handle(c, state),
            ClaimWorkItem c => WorkItemAggregate.Handle(c, state),
            SuspendWorkItem c => WorkItemAggregate.Handle(c, state),
            ResumeWorkItem c => WorkItemAggregate.Handle(c, state),
            CompleteWorkItem c => WorkItemAggregate.Handle(c, state),
            CancelWorkItem c => WorkItemAggregate.Handle(c, state),
            RejectWorkItem c => WorkItemAggregate.Handle(c, state),
            ExpireWorkItem c => WorkItemAggregate.Handle(c, state),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.GetType().Name, "Unhandled lifecycle command."),
        };

        result.IsSuccess.ShouldBeTrue();
        return result.Events.Single().ShouldBeOfType<T>();
    }

    // Advances the authoritative write state with the freshly emitted event, then persists the same
    // event (round-tripped through JSON) into the independent replay state.
    private static void Advance<T>(T e, WorkItemState write, WorkItemState replay)
        where T : class
    {
        ApplyEvent(write, e);
        Persist(e, replay);
    }

    // Serializes the success event, asserts the persisted payload is envelope-free and leads with the
    // (AggregateId, Sequence) pair, deserializes, and applies the round-tripped event to the replay state.
    private static void Persist<T>(T e, WorkItemState replay)
        where T : class
        => ApplyEvent(replay, RoundTripEvent(e));

    private static void ApplyEvent(WorkItemState state, object e)
    {
        switch (e)
        {
            case WorkItemCreated x: state.Apply(x); break;
            case WorkItemAssigned x: state.Apply(x); break;
            case WorkItemQueued x: state.Apply(x); break;
            case WorkItemClaimed x: state.Apply(x); break;
            case WorkItemSuspended x: state.Apply(x); break;
            case WorkItemResumed x: state.Apply(x); break;
            case WorkItemCompleted x: state.Apply(x); break;
            case WorkItemCancelled x: state.Apply(x); break;
            case WorkItemRejected x: state.Apply(x); break;
            case WorkItemExpired x: state.Apply(x); break;
            default: throw new ArgumentOutOfRangeException(nameof(e), e.GetType().Name, "Unhandled success event.");
        }
    }

    private static T RoundTripEvent<T>(T e)
        where T : class
    {
        string json = JsonSerializer.Serialize(e, JsonOptions);
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement root = document.RootElement;
            root.TryGetProperty("aggregateId", out _)
                .ShouldBeTrue($"{typeof(T).Name} must serialize its AggregateId (AR-4).");
            root.TryGetProperty("sequence", out _)
                .ShouldBeTrue($"{typeof(T).Name} must serialize its Sequence (AR-4).");
            foreach (string envelope in EnvelopeFields)
            {
                root.TryGetProperty(envelope, out _)
                    .ShouldBeFalse($"{typeof(T).Name} must not serialize the '{envelope}' envelope field.");
            }
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions).ShouldNotBeNull();
    }
}
