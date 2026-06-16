using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Hexalith.Works.Testing;
using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkItemSpawnChildTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Parent = new("parent-001");
    private static readonly WorkItemId Child = new("child-001");
    private static readonly Unit Hour = new("hour");
    private static readonly ExecutorBinding Binding =
        new(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Coordinate);

    [Fact]
    public void SpawnChild_from_existing_parent_emits_child_spawned_and_replays_reference_only_child()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Parent);
        SpawnChild command = SpawnCommand(
            initialEffort: new WorkItemEffort(8m, Hour),
            schedule: new WorkItemSchedule(Priority.High, new DateOnly(2026, 7, 15)),
            executorBinding: Binding,
            conversationCorrelationId: new ConversationCorrelationId("conversation-456"));

        DomainResult result = WorkItemAggregate.Handle(command, state);

        result.IsSuccess.ShouldBeTrue();
        result.Events.Count.ShouldBe(1);
        ChildSpawned spawned = result.Events.Single().ShouldBeOfType<ChildSpawned>();
        spawned.AggregateId.ShouldBe(Parent.Value);
        spawned.Sequence.ShouldBe(state.Sequence + 1);
        spawned.TenantId.ShouldBe(Tenant);
        spawned.WorkItemId.ShouldBe(Parent);
        spawned.ChildWorkItemId.ShouldBe(Child);
        spawned.Obligation.Description.ShouldBe("Break out child work");
        spawned.InitialEffort.ShouldBe(command.InitialEffort);
        spawned.Schedule.ShouldBe(command.Schedule);
        spawned.ExecutorBinding.ShouldBe(command.ExecutorBinding);
        spawned.ConversationCorrelationId.ShouldBe(command.ConversationCorrelationId);
        spawned.SuspendParentUntilChildCompletes.ShouldBeFalse();

        CreateWorkItem childCreate = new(
            spawned.TenantId,
            spawned.ChildWorkItemId,
            spawned.Obligation.Description,
            spawned.InitialEffort,
            spawned.Schedule,
            new ParentWorkItemReference(spawned.TenantId, spawned.WorkItemId),
            spawned.ExecutorBinding,
            spawned.ConversationCorrelationId);
        childCreate.TenantId.ShouldBe(Tenant);
        childCreate.Parent.ShouldBe(new ParentWorkItemReference(Tenant, Parent));

        state.Apply(spawned);
        state.SpawnedChildWorkItemIds.ShouldBe([Child]);
        state.Status.ShouldBe(WorkItemStatus.Created);
        state.Sequence.ShouldBe(spawned.Sequence);
    }

    [Fact]
    public void SpawnChild_with_await_intent_from_in_progress_emits_spawn_then_suspend_and_records_child_completion_condition()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Parent, Binding);
        SpawnChild command = SpawnCommand(suspendParentUntilChildCompletes: true);

        DomainResult result = WorkItemAggregate.Handle(command, state);

        result.IsSuccess.ShouldBeTrue();
        result.Events.Count.ShouldBe(2);
        ChildSpawned spawned = result.Events[0].ShouldBeOfType<ChildSpawned>();
        WorkItemSuspended suspended = result.Events[1].ShouldBeOfType<WorkItemSuspended>();

        spawned.Sequence.ShouldBe(state.Sequence + 1);
        suspended.Sequence.ShouldBe(spawned.Sequence + 1);
        suspended.AwaitCondition.ShouldBe(new AwaitCondition(Child));

        state.Apply(spawned);
        state.Apply(suspended);

        state.Status.ShouldBe(WorkItemStatus.Suspended);
        state.SpawnedChildWorkItemIds.ShouldBe([Child]);
        state.AwaitConditions.ShouldBe([new AwaitCondition(Child)]);
        state.Sequence.ShouldBe(suspended.Sequence);
    }

    [Fact]
    public void ReportProgress_after_spawn_suspension_returns_transition_rejection()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Parent, Binding);
        DomainResult spawn = WorkItemAggregate.Handle(SpawnCommand(suspendParentUntilChildCompletes: true), state);
        foreach (IEventPayload e in spawn.Events)
        {
            ApplyEvent(state, e);
        }

        DomainResult result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Parent, 1m, Hour), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.Single().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.Suspended);
        rejection.AttemptedAct.ShouldBe(nameof(ReportProgress));
    }

    [Theory]
    [MemberData(nameof(TreeGuardRejectionCases))]
    public void SpawnChild_tree_guard_rejections_return_rejection_only(SpawnChild command, Type rejectionType)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Parent);

        DomainResult result = WorkItemAggregate.Handle(command, state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Count.ShouldBe(1);
        result.Events.Single().GetType().ShouldBe(rejectionType);
    }

    [Theory]
    [InlineData(WorkItemStatus.Unknown)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void SpawnChild_rejects_missing_or_terminal_parent_state(WorkItemStatus status)
    {
        WorkItemState? state = status == WorkItemStatus.Unknown
            ? null
            : WorkItemStateBuilder.InStatus(status, Tenant, Parent, Binding);

        DomainResult result = WorkItemAggregate.Handle(SpawnCommand(), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.Single().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe(nameof(SpawnChild));
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Suspended)]
    public void SpawnChild_with_await_intent_requires_in_progress_parent(WorkItemStatus status)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Parent, Binding);

        DomainResult result = WorkItemAggregate.Handle(SpawnCommand(suspendParentUntilChildCompletes: true), state);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.Single().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(status);
        rejection.AttemptedAct.ShouldBe(nameof(SpawnChild));
    }

    [Fact]
    public void SpawnChild_replay_reconstructs_sequence_status_child_reference_and_await_condition_deterministically()
    {
        WorkItemState write = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Parent, Binding);
        WorkItemState replay = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Parent, Binding);

        DomainResult result = WorkItemAggregate.Handle(SpawnCommand(suspendParentUntilChildCompletes: true), write);
        foreach (IEventPayload e in result.Events)
        {
            ApplyEvent(write, e);
            ApplyEvent(replay, e);
        }

        replay.Sequence.ShouldBe(write.Sequence);
        replay.Status.ShouldBe(write.Status);
        replay.SpawnedChildWorkItemIds.ShouldBe(write.SpawnedChildWorkItemIds);
        replay.AwaitConditions.ShouldBe(write.AwaitConditions);
    }

    [Fact]
    public void SpawnChild_uses_caller_supplied_child_id_without_generating_another_identity()
    {
        WorkItemId edgeSuppliedChildId = new("edge-child-042");
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Parent);

        ChildSpawned spawned = WorkItemAggregate.Handle(SpawnCommand(childWorkItemId: edgeSuppliedChildId), state)
            .Events
            .Single()
            .ShouldBeOfType<ChildSpawned>();

        spawned.ChildWorkItemId.ShouldBe(edgeSuppliedChildId);
        spawned.ChildWorkItemId.Value.ShouldBe("edge-child-042");
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    public void SpawnChild_without_suspension_is_accepted_from_every_live_status(WorkItemStatus status)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(status, Tenant, Parent, Binding);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(SpawnCommand(), state);

        result.IsSuccess.ShouldBeTrue();
        ChildSpawned spawned = result.Events.Single().ShouldBeOfType<ChildSpawned>();
        spawned.Sequence.ShouldBe(sequenceBefore + 1);
        spawned.SuspendParentUntilChildCompletes.ShouldBeFalse();

        state.Apply(spawned);
        // Spawn alone never moves the parent's lifecycle status; it only records the child reference.
        state.Status.ShouldBe(status);
        state.SpawnedChildWorkItemIds.ShouldBe([Child]);
        state.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SpawnChild_without_obligation_returns_missing_obligation_rejection_for_the_child(string? obligation)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Parent);

        DomainResult result = WorkItemAggregate.Handle(SpawnCommand() with { Obligation = obligation }, state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        // Child creation follows CreateWorkItem semantics: a missing obligation is rejected, and the
        // rejection is raised against the child id the caller supplied — never the parent.
        WorkItemCannotBeCreatedWithoutObligation rejection =
            result.Events.Single().ShouldBeOfType<WorkItemCannotBeCreatedWithoutObligation>();
        rejection.TenantId.ShouldBe(Tenant);
        rejection.WorkItemId.ShouldBe(Child);
    }

    [Fact]
    public void SpawnChild_tree_guard_rejection_is_replay_safe_and_burns_no_parent_sequence()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Parent);
        long sequenceBefore = state.Sequence;

        // A spawn that overflows the depth policy is rejected; the parent stream must be left untouched.
        DomainResult rejected = WorkItemAggregate.Handle(SpawnCommand(proposedParentDepth: 32, maxDepth: 32), state);
        rejected.IsRejection.ShouldBeTrue();
        rejected.Events.Single().ShouldBeOfType<WorkItemTreeDepthExceeded>();

        // Handle is pure: the rejected attempt mutated no state and consumed no sequence number.
        state.Sequence.ShouldBe(sequenceBefore);
        state.Status.ShouldBe(WorkItemStatus.Created);
        state.SpawnedChildWorkItemIds.ShouldBeEmpty();

        // Re-handling the same rejected command is deterministic — same rejection, still no success.
        DomainResult replayed = WorkItemAggregate.Handle(SpawnCommand(proposedParentDepth: 32, maxDepth: 32), state);
        replayed.IsRejection.ShouldBeTrue();
        replayed.Events.Single().ShouldBeOfType<WorkItemTreeDepthExceeded>();

        // A subsequent valid spawn still receives the next contiguous sequence, proving neither rejected
        // attempt advanced the parent stream.
        ChildSpawned spawned = WorkItemAggregate.Handle(SpawnCommand(), state)
            .Events
            .Single()
            .ShouldBeOfType<ChildSpawned>();
        spawned.Sequence.ShouldBe(sequenceBefore + 1);
    }

    [Fact]
    public void SpawnChild_duplicate_event_replay_is_idempotent_and_distinct_children_accumulate_in_order()
    {
        WorkItemId firstChild = new("child-001");
        WorkItemId secondChild = new("child-002");
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(Parent.Value, 1, Tenant, Parent, new Obligation("Parent work")));

        var spawnFirst = new ChildSpawned(Parent.Value, 2, Tenant, Parent, firstChild, new Obligation("First child"));
        var spawnSecond = new ChildSpawned(Parent.Value, 3, Tenant, Parent, secondChild, new Obligation("Second child"));

        state.Apply(spawnFirst);
        state.Apply(spawnFirst); // Duplicate delivery of the same event must not duplicate the reference.
        state.Apply(spawnSecond);

        state.SpawnedChildWorkItemIds.ShouldBe([firstChild, secondChild]);
        state.Sequence.ShouldBe(3);
    }

    [Fact]
    public void SpawnChild_retry_with_existing_child_parent_equal_to_proposed_parent_is_accepted()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Parent);

        // The caller is reconciling a previously-known child whose recorded parent is already this parent.
        // The tree guard treats a same-parent fact as idempotent, so the retry is accepted — not rejected
        // as a second-parent attachment (contrast with the TreeGuardRejectionCases second-parent case,
        // which supplies a different existing parent).
        DomainResult result = WorkItemAggregate.Handle(
            SpawnCommand(existingChildParent: new ParentWorkItemReference(Tenant, Parent)),
            state);

        result.IsSuccess.ShouldBeTrue();
        ChildSpawned spawned = result.Events.Single().ShouldBeOfType<ChildSpawned>();
        spawned.ChildWorkItemId.ShouldBe(Child);
        spawned.Sequence.ShouldBe(state.Sequence + 1);
    }

    public static IEnumerable<object[]> TreeGuardRejectionCases()
    {
        yield return
        [
            SpawnCommand(
                proposedParentAncestors: [new ParentWorkItemReference(new TenantId("tenant-beta"), new WorkItemId("ancestor-001"))]),
            typeof(WorkItemCannotReferenceParentFromAnotherTenant),
        ];

        yield return
        [
            SpawnCommand(childWorkItemId: Parent),
            typeof(WorkItemTreeCycleRejected),
        ];

        yield return
        [
            SpawnCommand(existingChildParent: new ParentWorkItemReference(Tenant, new WorkItemId("other-parent-001"))),
            typeof(WorkItemCannotReferenceSecondParent),
        ];

        yield return
        [
            SpawnCommand(proposedParentDepth: 32, maxDepth: 32),
            typeof(WorkItemTreeDepthExceeded),
        ];
    }

    private static SpawnChild SpawnCommand(
        WorkItemId? childWorkItemId = null,
        WorkItemEffort? initialEffort = null,
        WorkItemSchedule? schedule = null,
        ExecutorBinding? executorBinding = null,
        ConversationCorrelationId? conversationCorrelationId = null,
        bool suspendParentUntilChildCompletes = false,
        IReadOnlyList<ParentWorkItemReference>? proposedParentAncestors = null,
        int proposedParentDepth = 1,
        int maxDepth = 32,
        ParentWorkItemReference? existingChildParent = null)
        => new(
            Tenant,
            Parent,
            childWorkItemId ?? Child,
            "Break out child work",
            initialEffort,
            schedule,
            executorBinding,
            conversationCorrelationId,
            suspendParentUntilChildCompletes,
            proposedParentAncestors,
            proposedParentDepth,
            maxDepth,
            existingChildParent);

    private static void ApplyEvent(WorkItemState state, IEventPayload e)
    {
        switch (e)
        {
            case ChildSpawned childSpawned:
                state.Apply(childSpawned);
                break;
            case WorkItemSuspended workItemSuspended:
                state.Apply(workItemSuspended);
                break;
            default:
                throw new InvalidOperationException($"Unexpected event type {e.GetType().Name}.");
        }
    }
}
