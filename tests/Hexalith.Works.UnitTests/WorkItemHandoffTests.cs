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

/// <summary>
/// Story 4.2 (FR-17 / FR-18) — assign, reassign, and hand off work through one uniform operation.
/// These are the 4.2-specific behavioral proofs that build on (and do not duplicate) Story 4.1's
/// <see cref="WorkItemUniformExecutorBindingTests"/> and the exhaustive matrix in
/// <see cref="WorkItemLifecycleTests"/>:
/// <list type="bullet">
///   <item>reassignment and human↔system hand-off are the same <see cref="AssignWorkItem"/> path,
///   differing only by <see cref="ExecutorBinding"/> field values (no kind branch, no handoff command);</item>
///   <item>the ordered event history preserves every hand-off as a distinct raw act (not collapsed),
///   and the latest binding is authoritative for the next executor act (AC #3);</item>
///   <item>requeue (<see cref="QueueWorkItem"/>) returns work to the shared pool so a different
///   executor can claim it, and the requeued item keeps its last binding in replayed state (D2/AC #4);</item>
///   <item>assignment (and requeue) from any terminal status is a <see cref="WorkItemTransitionRejected"/>
///   with no binding mutation and no sequence burn (D5/AC #5).</item>
/// </list>
/// </summary>
public sealed class WorkItemHandoffTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");

    // The three representative executors for the symmetric reassignment Theory (AC #2): a system agent,
    // an internal user, and an external party. They differ only by ExecutorBinding field values — there
    // is no per-kind command, event, or branch.
    public static TheoryData<string, string, Channel, AuthorityLevel> RepresentativeReassignTargets => new()
    {
        { "system agent", "party-bot", Channel.Mcp, AuthorityLevel.Administer },
        { "internal user", "party-user", Channel.Cli, AuthorityLevel.Contribute },
        { "external party", "party-ext", Channel.Email, AuthorityLevel.Read },
    };

    // ── Task 3 (D2; AC #3/#4) — requeue does not mutate the in-state binding. ───────────────────────
    [Fact]
    public void Requeue_keeps_the_last_executor_binding_in_replayed_state()
    {
        ExecutorBinding bindingA = Binding("party-system", Channel.Mcp, AuthorityLevel.Coordinate);
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item, bindingA);
        state.ExecutorBinding.ShouldBe(bindingA);
        long sequenceBefore = state.Sequence;

        WorkItemQueued queued = WorkItemAggregate.Handle(new QueueWorkItem(Tenant, Item), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemQueued>();
        queued.Sequence.ShouldBe(sequenceBefore + 1);
        state.Apply(queued);

        // Queueing advances the stream by exactly one and rests at Queued, but it is NOT an executor-
        // binding act: WorkItemQueued carries no binding and the last executor act (bindingA) remains
        // the in-state value. "Who owns a Queued item" is a Story 4.4 projection concern, not a mutation.
        state.Status.ShouldBe(WorkItemStatus.Queued);
        state.Sequence.ShouldBe(sequenceBefore + 1);
        state.ExecutorBinding.ShouldBe(bindingA);
    }

    // ── Task 4 (AC #1) — assign from an assignable status emits one binding-carrying act. ────────────
    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Queued)]
    public void Assign_from_an_assignable_status_emits_one_assigned_act_carrying_the_supplied_binding(WorkItemStatus from)
    {
        ExecutorBinding binding = Binding("party-exec", Channel.Mcp, AuthorityLevel.Administer);
        WorkItemState state = WorkItemStateBuilder.InStatus(from, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, binding), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemAssigned assigned = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
        assigned.Sequence.ShouldBe(sequenceBefore + 1);
        assigned.Binding.ShouldBe(binding);

        // The raw act must be correctly addressed, not just carry the binding: the event identifies the
        // exact aggregate/tenant/item it belongs to, so a misrouted emission can't pass on binding alone.
        assigned.AggregateId.ShouldBe(Item.Value);
        assigned.TenantId.ShouldBe(Tenant);
        assigned.WorkItemId.ShouldBe(Item);

        state.Apply(assigned);
        state.Status.ShouldBe(WorkItemStatus.Assigned);
        state.ExecutorBinding.ShouldBe(binding);
    }

    // ── Task 4 (AC #2) — reassignment is a second accepted AssignWorkItem; latest wins, no NoOp. ─────
    [Theory]
    [MemberData(nameof(RepresentativeReassignTargets))]
    public void Reassign_from_assigned_with_a_different_binding_is_a_fresh_act_through_the_same_handler_and_latest_wins(
        string label, string partyId, Channel channel, AuthorityLevel authority)
    {
        _ = label;
        ExecutorBinding original = Binding("party-original", Channel.Cli, AuthorityLevel.Read);
        ExecutorBinding reassigned = Binding(partyId, channel, authority);
        reassigned.ShouldNotBe(original);

        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item, original);
        state.ExecutorBinding.ShouldBe(original);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, reassigned), state);

        // Assigned → Assign is Accept(Assigned), never NoOp: a fresh raw act is emitted (D3) so the
        // ordered history records the hand-off — even though the same command path handles reassignment.
        result.IsSuccess.ShouldBeTrue();
        result.IsNoOp.ShouldBeFalse();
        WorkItemAssigned assigned = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
        assigned.Sequence.ShouldBe(sequenceBefore + 1);
        assigned.Binding.ShouldBe(reassigned);

        state.Apply(assigned);
        state.Status.ShouldBe(WorkItemStatus.Assigned);
        state.ExecutorBinding.ShouldBe(reassigned);
    }

    // ── QA gap (AC #2 / D3) — re-asserting the SAME binding is still a fresh raw act, never a NoOp. ───
    // The different-binding theory above proves latest-wins, but D3's stronger claim is that Assigned →
    // Assign is Accept(Assigned) regardless of whether the binding changed: a redundant re-assignment to
    // the identical executor still emits a new WorkItemAssigned at the next sequence so the ordered audit
    // history records every hand-off attempt, even no-change ones. A future "collapse identical rebinds
    // into a NoOp" optimization would be a visible, intentional break of this lock.
    [Fact]
    public void Reassign_to_the_same_binding_from_assigned_is_still_a_fresh_raw_act_not_a_noop()
    {
        ExecutorBinding binding = Binding("party-steady", Channel.Mcp, AuthorityLevel.Coordinate);
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item, binding);
        long sequenceBefore = state.Sequence;
        state.ExecutorBinding.ShouldBe(binding);

        DomainResult result = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, binding), state);

        result.IsSuccess.ShouldBeTrue();
        result.IsNoOp.ShouldBeFalse();
        WorkItemAssigned assigned = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
        assigned.Sequence.ShouldBe(sequenceBefore + 1);
        assigned.Binding.ShouldBe(binding);

        state.Apply(assigned);
        state.Status.ShouldBe(WorkItemStatus.Assigned);
        state.Sequence.ShouldBe(sequenceBefore + 1); // the act advanced the stream — it was not collapsed.
        state.ExecutorBinding.ShouldBe(binding);
    }

    // ── QA gap (FR-18 push/pull; D2 × AC #3) — a push assignment from the pool overrides the binding D2
    // intentionally left in state across a requeue, and the pushed binding becomes authoritative. This
    // proves the "push" half of FR-18 coexists with "pull" (claim) AND that latest-act-wins holds even
    // when the in-state value is a stale leftover, not a freshly cleared field. ─────────────────────────
    [Fact]
    public void Assign_from_the_pool_overrides_the_binding_requeue_left_in_state_and_the_pushed_binding_wins()
    {
        ExecutorBinding bindingA = Binding("party-first", Channel.Cli, AuthorityLevel.Contribute);
        ExecutorBinding bindingC = Binding("party-pushed", Channel.Email, AuthorityLevel.Read);
        bindingA.ShouldNotBe(bindingC);

        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item, bindingA);

        // Requeue: D2 keeps bindingA as the in-state value even though the item is now in the shared pool.
        WorkItemQueued queued = WorkItemAggregate.Handle(new QueueWorkItem(Tenant, Item), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemQueued>();
        state.Apply(queued);
        state.Status.ShouldBe(WorkItemStatus.Queued);
        state.ExecutorBinding.ShouldBe(bindingA); // stale-by-design carryover (D2).

        // Push from the pool (Queued → Assign): a coordinator assigns a different executor without a claim.
        DomainResult result = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, bindingC), state);
        result.IsSuccess.ShouldBeTrue();
        WorkItemAssigned assigned = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
        assigned.Binding.ShouldBe(bindingC);
        state.Apply(assigned);

        // The pushed binding is authoritative — it overrides the stale carryover, not the other way round.
        state.Status.ShouldBe(WorkItemStatus.Assigned);
        state.ExecutorBinding.ShouldBe(bindingC);
    }

    // ── Task 4 (AC #3) — the novel assertion: a hand-off chain preserves each act as ordered raw-act
    // evidence, and the latest binding is authoritative for the next executor act. Covered both ways
    // (human → system → human and system → human → system) per FR-17's human↔system symmetry. ─────────
    [Fact]
    public void Human_to_system_to_human_handoff_preserves_each_act_in_order_and_latest_binds_the_next_claim()
    {
        ExecutorBinding human1 = Binding("party-alice", Channel.Cli, AuthorityLevel.Coordinate);
        ExecutorBinding system = Binding("party-bot", Channel.Mcp, AuthorityLevel.Administer);
        ExecutorBinding human2 = Binding("party-bob", Channel.Cli, AuthorityLevel.Contribute);

        AssertHandoffChainPreservesOrderedRawActsAndLatestBindsNextClaim(human1, system, human2);
    }

    [Fact]
    public void System_to_human_to_system_handoff_preserves_each_act_in_order_and_latest_binds_the_next_claim()
    {
        ExecutorBinding system1 = Binding("party-bot", Channel.Mcp, AuthorityLevel.Administer);
        ExecutorBinding human = Binding("party-alice", Channel.Cli, AuthorityLevel.Coordinate);
        ExecutorBinding system2 = Binding("party-bot2", Channel.Chatbot, AuthorityLevel.Coordinate);

        AssertHandoffChainPreservesOrderedRawActsAndLatestBindsNextClaim(system1, human, system2);
    }

    // ── Task 5 (AC #4) — requeue returns work to the shared pool; a DIFFERENT executor can claim it.
    // This is the QueueWorkItem requeue path (D6), distinct from Story 2.5's RejectWorkItem decline. ──
    [Fact]
    public void Requeue_returns_work_to_the_pool_so_a_different_executor_can_claim_it()
    {
        ExecutorBinding bindingA = Binding("party-first", Channel.Mcp, AuthorityLevel.Coordinate);
        ExecutorBinding bindingB = Binding("party-second", Channel.Email, AuthorityLevel.Read);
        bindingA.ShouldNotBe(bindingB);

        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item, bindingA);

        WorkItemQueued queued = WorkItemAggregate.Handle(new QueueWorkItem(Tenant, Item), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemQueued>();
        state.Apply(queued);
        state.Status.ShouldBe(WorkItemStatus.Queued);

        // The item is now claimable per the lifecycle table, and a different party (bindingB) claims it —
        // proving it returned to the shared pool rather than staying bound to the original executor.
        WorkItemClaimed claimed = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, bindingB), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();
        claimed.Binding.ShouldBe(bindingB);
        state.Apply(claimed);

        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.ExecutorBinding.ShouldBe(bindingB);
    }

    // ── Task 5 (AC #5) — assignment from any terminal status is rejected; no binding mutation, no
    // sequence burn, and Apply(WorkItemTransitionRejected) is a no-op. Theory over all four terminals. ─
    [Theory]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Assign_from_each_terminal_status_is_rejected_with_no_binding_mutation_and_no_sequence_burn(WorkItemStatus terminal)
    {
        ExecutorBinding boundBefore = Binding("party-bound", Channel.Mcp, AuthorityLevel.Coordinate);
        ExecutorBinding attempted = Binding("party-attempted", Channel.Email, AuthorityLevel.Read);
        WorkItemState state = TerminalStateCarryingBinding(terminal, boundBefore);
        long sequenceBefore = state.Sequence;
        state.Status.ShouldBe(terminal);
        state.ExecutorBinding.ShouldBe(boundBefore);

        DomainResult result = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, attempted), state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Any(e => e is WorkItemAssigned).ShouldBeFalse();
        IEventPayload emitted = result.Events.ShouldHaveSingleItem();
        emitted.ShouldBeAssignableTo<IRejectionEvent>(); // AC#5: the command emits an IRejectionEvent.
        WorkItemTransitionRejected rejection = emitted.ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(terminal);
        rejection.AttemptedAct.ShouldBe("Assign");
        rejection.TenantId.ShouldBe(Tenant);
        rejection.WorkItemId.ShouldBe(Item);

        state.Apply(rejection); // Apply(WorkItemTransitionRejected) is a no-op.
        state.Status.ShouldBe(terminal);
        state.Sequence.ShouldBe(sequenceBefore);
        state.ExecutorBinding.ShouldBe(boundBefore);
    }

    // ── Task 5 (AC #5) — requeue from any terminal status is rejected, so the shared-pool path cannot
    // reopen a closed item. Theory over all four terminals. ─────────────────────────────────────────
    [Theory]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Queue_from_each_terminal_status_is_rejected_so_the_shared_pool_cannot_reopen_a_closed_item(WorkItemStatus terminal)
    {
        ExecutorBinding boundBefore = Binding("party-bound", Channel.Mcp, AuthorityLevel.Coordinate);
        WorkItemState state = TerminalStateCarryingBinding(terminal, boundBefore);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new QueueWorkItem(Tenant, Item), state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Any(e => e is WorkItemQueued).ShouldBeFalse();
        IEventPayload emitted = result.Events.ShouldHaveSingleItem();
        emitted.ShouldBeAssignableTo<IRejectionEvent>(); // AC#5: the command emits an IRejectionEvent.
        WorkItemTransitionRejected rejection = emitted.ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(terminal);
        rejection.AttemptedAct.ShouldBe("Queue");

        state.Apply(rejection);
        state.Status.ShouldBe(terminal);
        state.Sequence.ShouldBe(sequenceBefore);
        state.ExecutorBinding.ShouldBe(boundBefore);
    }

    private static void AssertHandoffChainPreservesOrderedRawActsAndLatestBindsNextClaim(params ExecutorBinding[] chain)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Item);
        long expectedSequence = state.Sequence;
        var history = new List<WorkItemAssigned>();

        foreach (ExecutorBinding binding in chain)
        {
            WorkItemAssigned assigned = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, binding), state)
                .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
            assigned.Sequence.ShouldBe(++expectedSequence);
            history.Add(assigned);
            state.Apply(assigned);
        }

        // Ordered raw-act evidence: one WorkItemAssigned per hand-off, contiguous consecutive sequences,
        // each carrying its OWN binding in order — the history is not collapsed into a single latest act.
        history.Count.ShouldBe(chain.Length);
        for (int i = 0; i < chain.Length; i++)
        {
            history[i].Binding.ShouldBe(chain[i]);
            if (i > 0)
            {
                history[i].Sequence.ShouldBe(history[i - 1].Sequence + 1);
            }
        }

        // The latest binding is authoritative for the next executor act: the most-recent party claims,
        // and the claim binds that party (WorkItemClaimed carries it; replay agrees).
        ExecutorBinding latest = chain[^1];
        state.ExecutorBinding.ShouldBe(latest);

        WorkItemClaimed claimed = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, latest), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();
        claimed.Binding.ShouldBe(latest);
        state.Apply(claimed);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.ExecutorBinding.ShouldBe(latest);
    }

    // Arranges a terminal state that carries a known binding for all four terminals (the shared builder's
    // shortest path to Cancelled/Expired binds nothing), so the "no binding mutation" assertion is real.
    private static WorkItemState TerminalStateCarryingBinding(WorkItemStatus terminal, ExecutorBinding binding)
    {
        string aggregateId = Item.Value;
        var state = new WorkItemState();
        long sequence = 0;
        state.Apply(new WorkItemCreated(aggregateId, ++sequence, Tenant, Item, new Obligation("Terminal hand-off work item")));
        state.Apply(new WorkItemAssigned(aggregateId, ++sequence, Tenant, Item, binding));

        switch (terminal)
        {
            case WorkItemStatus.Completed:
                state.Apply(new WorkItemClaimed(aggregateId, ++sequence, Tenant, Item, binding));
                state.Apply(new WorkItemCompleted(aggregateId, ++sequence, Tenant, Item));
                break;

            case WorkItemStatus.Cancelled:
                state.Apply(new WorkItemCancelled(aggregateId, ++sequence, Tenant, Item));
                break;

            case WorkItemStatus.Rejected:
                state.Apply(new WorkItemRejected(aggregateId, ++sequence, Tenant, Item, Requeue: false));
                break;

            case WorkItemStatus.Expired:
                state.Apply(new WorkItemExpired(aggregateId, ++sequence, Tenant, Item));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(terminal), terminal, "Not a terminal status.");
        }

        return state;
    }

    private static ExecutorBinding Binding(string partyId, Channel channel, AuthorityLevel authority)
        => new(new PartyId(partyId), channel, authority);
}
