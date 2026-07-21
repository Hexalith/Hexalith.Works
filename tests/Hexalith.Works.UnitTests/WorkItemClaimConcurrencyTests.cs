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
/// Story 4.3 (FR-18 / NFR-3 / AR-10) — claim queued work with single-claim-wins. These tests build on
/// (and do not duplicate) Story 4.1's <see cref="WorkItemUniformExecutorBindingTests"/> (uniform claim
/// binding across executor kinds) and the exhaustive (status, act) matrix in
/// <see cref="WorkItemLifecycleTests"/> (which owns every Claim cell):
/// <list type="bullet">
///   <item>single-claim-wins is proved <b>deterministically</b> as the domain outcome of an
///   expected-version collision — two claims observe the same <see cref="WorkItemState.Sequence"/> and
///   both compute a <see cref="WorkItemClaimed"/> at the same next sequence, so only one append can land;
///   the loser re-handles against the now-advanced state and is domain-rejected (AC #2/#5);</item>
///   <item>the happy-path claim emits one binding-carrying <see cref="WorkItemClaimed"/> and transitions
///   to <see cref="WorkItemStatus.InProgress"/> (AC #1);</item>
///   <item>every non-claimable status rejects <see cref="ClaimWorkItem"/> with no binding/status/sequence
///   mutation (AC #3).</item>
/// </list>
/// <para>
/// The race is modelled with <b>no threads, no <c>Task.Run</c>, no sleeps, and no shared-mutable-state
/// interleaving</b> (RR-3): <see cref="WorkItemAggregate.Handle(ClaimWorkItem, WorkItemState?)"/> is pure,
/// so handling two claims against the same observed state is exactly two racers rehydrating the same
/// snapshot. The <b>live</b> ETag append / conflict-retry / retry-exhaustion path (owned by the
/// EventStore <c>AggregateActor</c> → <c>EventPersister</c> → DAPR ETag <c>SaveStateAsync</c> pipeline) is
/// exercised under the Aspire runtime in <b>Story 4.5</b>, not here; this Tier-1 test proves the pure
/// <i>domain outcome</i> of the collision (no Dapr/Aspire/network). No new event, command, or rejection
/// type is introduced — the loser's observable rejection is the existing
/// <see cref="WorkItemTransitionRejected"/> (DC1), and claim adds nothing to the frozen v1 catalog.
/// </para>
/// </summary>
public sealed class WorkItemClaimConcurrencyTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");

    // Two distinct, valid executor bindings. Story 4.1 hardened ExecutorBinding to reject
    // AuthorityLevel.Unknown / undefined Channel, so every test binding must use a valid AuthorityLevel.
    private static readonly ExecutorBinding BindingA = new(new PartyId("party-a"), Channel.Mcp, AuthorityLevel.Administer);
    private static readonly ExecutorBinding BindingB = new(new PartyId("party-b"), Channel.Cli, AuthorityLevel.Contribute);

    // ── Task 2 (AC #2/#5) — deterministic single-claim-wins via expected-version conflict. ──────────────
    [Fact]
    public void Two_claims_at_the_same_expected_version_collide_and_exactly_one_wins_with_the_loser_domain_rejected()
    {
        BindingA.ShouldNotBe(BindingB);

        // Arrange: a Queued item observed at version N (WorkItemQueued at sequence 2 over WorkItemCreated).
        WorkItemState queued = WorkItemStateBuilder.InStatus(WorkItemStatus.Queued, Tenant, Item);
        long n = queued.Sequence;
        queued.Status.ShouldBe(WorkItemStatus.Queued);

        // Both claims are handled against the SAME observed state (version N). Handle is pure and never
        // mutates state, so this is exactly two racers rehydrating the same Queued snapshot. Each accepts
        // and emits a single WorkItemClaimed targeting sequence N+1 — the store admits only one append at
        // N+1, so this shared target IS the expected-version collision.
        DomainResult resultA = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, BindingA), queued);
        DomainResult resultB = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, BindingB), queued);

        resultA.IsSuccess.ShouldBeTrue();
        resultB.IsSuccess.ShouldBeTrue();
        WorkItemClaimed claimedA = resultA.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();
        WorkItemClaimed claimedB = resultB.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();
        claimedA.Sequence.ShouldBe(n + 1);
        claimedB.Sequence.ShouldBe(n + 1);
        claimedB.Sequence.ShouldBe(claimedA.Sequence); // same expected-version target => only one can persist.

        // Winner commits: A's WorkItemClaimed appends at N+1; replay rests InProgress at N+1, bound to A.
        // (resultB's success is the would-be conflicting append — it is never persisted; the loser's actual
        // persisted outcome is the re-handle below, exactly as the substrate's conflict-retry produces it.)
        queued.Apply(claimedA);
        queued.Status.ShouldBe(WorkItemStatus.InProgress);
        queued.Sequence.ShouldBe(n + 1);
        queued.ExecutorBinding.ShouldBe(BindingA);

        // Loser re-handles against the now-advanced state — exactly what the substrate's conflict-retry does
        // (clear cache, rehydrate fresh state, re-run Handle). B observes InProgress now, and the lifecycle
        // rejects the claim: a single observable rejection event (not an exception, not a NoOp).
        DomainResult loser = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, BindingB), queued);

        loser.IsRejection.ShouldBeTrue();
        loser.IsSuccess.ShouldBeFalse();
        loser.Events.Any(e => e is WorkItemClaimed).ShouldBeFalse();
        IEventPayload emitted = loser.Events.ShouldHaveSingleItem();
        emitted.ShouldBeAssignableTo<IRejectionEvent>(); // AC #2/#5: the loser receives an observable domain rejection.
        WorkItemTransitionRejected rejection = emitted.ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.InProgress); // "someone else got there first" (DC1).
        rejection.AttemptedAct.ShouldBe("Claim");
        rejection.TenantId.ShouldBe(Tenant);
        rejection.WorkItemId.ShouldBe(Item);

        // Applying the rejection is a no-op: the winner's claim stands, untouched (status/sequence/binding).
        queued.Apply(rejection);
        queued.Status.ShouldBe(WorkItemStatus.InProgress);
        queued.Sequence.ShouldBe(n + 1);
        queued.ExecutorBinding.ShouldBe(BindingA);

        // Exactly one accepted WorkItemClaimed (the winner) and exactly one observable IRejectionEvent.
        claimedA.Binding.ShouldBe(BindingA);
        loser.Events.ShouldHaveSingleItem().ShouldBeAssignableTo<IRejectionEvent>();
    }

    // ── Task 3 (AC #1) — happy-path claim: one WorkItemClaimed, binds the claimant, transitions to
    // InProgress. Both claimable entries (Queued and Assigned) reach InProgress through the one
    // ClaimWorkItem handler. Cross-executor-kind uniformity of claim is proved by Story 4.1's
    // WorkItemUniformExecutorBindingTests and not repeated here. ─────────────────────────────────────────
    [Theory]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Assigned)]
    public void Claim_from_a_claimable_status_emits_one_claimed_act_binding_the_claimant_and_transitions_to_in_progress(WorkItemStatus from)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(from, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, BindingA), state);

        result.IsSuccess.ShouldBeTrue();
        WorkItemClaimed claimed = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();
        claimed.Sequence.ShouldBe(sequenceBefore + 1);
        claimed.Binding.ShouldBe(BindingA);

        // The raw act must be correctly addressed, not just carry the binding: the event identifies the
        // exact aggregate/tenant/item it belongs to, so a misrouted emission can't pass on binding alone.
        claimed.AggregateId.ShouldBe(Item.Value);
        claimed.TenantId.ShouldBe(Tenant);
        claimed.WorkItemId.ShouldBe(Item);

        state.Apply(claimed);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        state.Sequence.ShouldBe(sequenceBefore + 1);
        state.ExecutorBinding.ShouldBe(BindingA);
    }

    // ── Task 3 (AC #3) — not-claimable: every non-claimable status rejects ClaimWorkItem with no mutation.
    // Claimable entries are Queued and Assigned only; every other status — Created, the active InProgress
    // and Suspended, and all four terminals — rejects Claim. WorkItemLifecycleTests owns the (status, Claim)
    // matrix cells; this is the claim-state-mutation proof: no WorkItemClaimed emitted, no binding/sequence
    // burn, and Apply(WorkItemTransitionRejected) is a no-op. Each state carries a known binding so "no
    // binding mutation" is a real assertion (not null == null). ─────────────────────────────────────────
    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Suspended)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Expired)]
    public void Claim_from_a_non_claimable_status_is_rejected_with_no_binding_status_or_sequence_mutation(WorkItemStatus from)
    {
        ExecutorBinding boundBefore = new(new PartyId("party-bound"), Channel.Email, AuthorityLevel.Read);
        WorkItemState state = NonClaimableStateCarryingBinding(from, boundBefore);
        long sequenceBefore = state.Sequence;
        state.Status.ShouldBe(from);
        state.ExecutorBinding.ShouldBe(boundBefore);

        DomainResult result = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, BindingA), state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Any(e => e is WorkItemClaimed).ShouldBeFalse();
        IEventPayload emitted = result.Events.ShouldHaveSingleItem();
        emitted.ShouldBeAssignableTo<IRejectionEvent>(); // AC #3: the command emits an IRejectionEvent.
        WorkItemTransitionRejected rejection = emitted.ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(from);
        rejection.AttemptedAct.ShouldBe("Claim");
        rejection.TenantId.ShouldBe(Tenant);
        rejection.WorkItemId.ShouldBe(Item);

        state.Apply(rejection); // Apply(WorkItemTransitionRejected) is a no-op.
        state.Status.ShouldBe(from);
        state.Sequence.ShouldBe(sequenceBefore);
        state.ExecutorBinding.ShouldBe(boundBefore);
    }

    // ── DC5 (AC #2/#5) — a duplicate claim by the CURRENT HOLDER of an InProgress item is an observable
    // domain REJECTION, never a DomainResult.NoOp. Duplicate-delivery idempotency (the same claim command
    // redelivered) is a SUBSTRATE concern — the EventStore AggregateActor dedups by CausationId/offset
    // (NFR-9/AR-11). At the kernel level a second claim against an already-InProgress item — even from the
    // executor that already holds it — is a rejection: WorkItemLifecycle.Decide returns NoOp only on the
    // terminal self-duplicate diagonals (Completed/Cancelled/Rejected/Expired), never for Claim. The AC #3
    // theory above re-claims InProgress with a DIFFERENT party; this proves the same-claimant duplicate is
    // *also* a rejection, and explicitly that it is NOT a NoOp — guarding DC5 so "fixing" duplicate claim
    // into a NoOp (silent accept) would break the build rather than silently change the contract. ─────────
    [Fact]
    public void Duplicate_claim_by_the_current_holder_of_an_in_progress_item_is_rejected_not_a_no_op()
    {
        // Arrange: an InProgress item already claimed (held) by BindingA, observed at version N.
        WorkItemState held = NonClaimableStateCarryingBinding(WorkItemStatus.InProgress, BindingA);
        long sequenceBefore = held.Sequence;
        held.Status.ShouldBe(WorkItemStatus.InProgress);
        held.ExecutorBinding.ShouldBe(BindingA);

        // Act: the SAME executor re-claims the item it already holds (a duplicate claim at the domain level).
        DomainResult duplicate = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, BindingA), held);

        // Assert: an observable rejection — explicitly NOT a no-op (DC5). A NoOp would mean "silently already
        // done"; the kernel must instead surface WorkItemTransitionRejected(InProgress, "Claim").
        duplicate.IsRejection.ShouldBeTrue();
        duplicate.IsNoOp.ShouldBeFalse();
        duplicate.IsSuccess.ShouldBeFalse();
        duplicate.Events.Any(e => e is WorkItemClaimed).ShouldBeFalse();
        WorkItemTransitionRejected rejection = duplicate.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.InProgress);
        rejection.AttemptedAct.ShouldBe("Claim");
        rejection.TenantId.ShouldBe(Tenant);
        rejection.WorkItemId.ShouldBe(Item);

        // Applying the rejection mutates nothing: the holder's claim stands (status/sequence/binding intact).
        held.Apply(rejection);
        held.Status.ShouldBe(WorkItemStatus.InProgress);
        held.Sequence.ShouldBe(sequenceBefore);
        held.ExecutorBinding.ShouldBe(BindingA);
    }

    // Arranges any non-claimable status carrying a known executor binding, so the "no binding mutation"
    // assertion on a rejected claim is real for every status (Created and the binding-less terminals
    // included). Mirrors WorkItemHandoffTests.TerminalStateCarryingBinding, extended to Created/InProgress/
    // Suspended; the binding is set on WorkItemCreated and never cleared by a later replay step.
    private static WorkItemState NonClaimableStateCarryingBinding(WorkItemStatus status, ExecutorBinding binding)
    {
        string aggregateId = Item.Value;
        var state = new WorkItemState();
        long sequence = 0;
        state.Apply(new WorkItemCreated(aggregateId, ++sequence, Tenant, Item, new Obligation("Claim test work item"), ExecutorBinding: binding));

        switch (status)
        {
            case WorkItemStatus.Created:
                break;

            case WorkItemStatus.InProgress:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, Tenant, Item, binding));
                state.Apply(new WorkItemClaimed(aggregateId, ++sequence, Tenant, Item, binding));
                break;

            case WorkItemStatus.Suspended:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, Tenant, Item, binding));
                state.Apply(new WorkItemClaimed(aggregateId, ++sequence, Tenant, Item, binding));
                state.Apply(new WorkItemSuspended(aggregateId, ++sequence, Tenant, Item, [WorkItemStateBuilder.DefaultAwaitCondition()]));
                break;

            case WorkItemStatus.Completed:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, Tenant, Item, binding));
                state.Apply(new WorkItemClaimed(aggregateId, ++sequence, Tenant, Item, binding));
                state.Apply(new WorkItemCompleted(aggregateId, ++sequence, Tenant, Item));
                break;

            case WorkItemStatus.Cancelled:
                state.Apply(new WorkItemCancelled(aggregateId, ++sequence, Tenant, Item));
                break;

            case WorkItemStatus.Rejected:
                state.Apply(new WorkItemAssigned(aggregateId, ++sequence, Tenant, Item, binding));
                state.Apply(new WorkItemRejected(aggregateId, ++sequence, Tenant, Item, Requeue: false));
                break;

            case WorkItemStatus.Expired:
                state.Apply(new WorkItemExpired(aggregateId, ++sequence, Tenant, Item));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Not a non-claimable status for this helper.");
        }

        return state;
    }
}
