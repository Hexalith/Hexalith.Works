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
/// Story 3.6 AC #2/#3, asserted at the transition table the cascade depends on. A terminal cascade may
/// deliver a terminal command to the same descendant more than once, so target-aggregate idempotency is
/// the safety mechanism (no out-of-band dedup store). These focused tests lock the cascade-delivery
/// contract directly: a duplicate self-terminal command is a <see cref="DomainResult.NoOp"/> with no
/// terminal event, no rejection event, and no sequence burn; a cross-terminal command is a
/// <see cref="WorkItemTransitionRejected"/> with no terminal success event and no state change. They
/// complement the exhaustive per-cell matrix in <see cref="WorkItemLifecycleTests"/>.
/// </summary>
public sealed class WorkItemTerminalCascadeIdempotencyTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("descendant-001");

    [Fact]
    public void Duplicate_cancel_against_cancelled_descendant_is_noop_with_no_event_and_no_sequence_burn()
    {
        WorkItemState cancelled = WorkItemStateBuilder.InStatus(WorkItemStatus.Cancelled, Tenant, Item);
        long sequenceBefore = cancelled.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Item), cancelled);

        result.IsNoOp.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.IsRejection.ShouldBeFalse();
        result.Events.ShouldBeEmpty();
        result.Events.OfType<WorkItemCancelled>().ShouldBeEmpty();
        cancelled.Status.ShouldBe(WorkItemStatus.Cancelled);
        cancelled.Sequence.ShouldBe(sequenceBefore);
    }

    [Fact]
    public void Duplicate_expire_against_expired_descendant_is_noop_with_no_event_and_no_sequence_burn()
    {
        WorkItemState expired = WorkItemStateBuilder.InStatus(WorkItemStatus.Expired, Tenant, Item);
        long sequenceBefore = expired.Sequence;

        DomainResult result = WorkItemAggregate.Handle(new ExpireWorkItem(Tenant, Item), expired);

        result.IsNoOp.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.IsRejection.ShouldBeFalse();
        result.Events.ShouldBeEmpty();
        result.Events.OfType<WorkItemExpired>().ShouldBeEmpty();
        expired.Status.ShouldBe(WorkItemStatus.Expired);
        expired.Sequence.ShouldBe(sequenceBefore);
    }

    // Cross-terminal cascade delivery: a cancel reaching an already-Expired/Completed/Rejected descendant,
    // or an expire reaching an already-Cancelled/Completed/Rejected descendant, must reject — never
    // re-terminalize and never emit a duplicate terminal event — and leave status and sequence untouched.
    [Theory]
    [InlineData(WorkItemStatus.Expired, "cancel", "Cancel")]
    [InlineData(WorkItemStatus.Completed, "cancel", "Cancel")]
    [InlineData(WorkItemStatus.Rejected, "cancel", "Cancel")]
    [InlineData(WorkItemStatus.Cancelled, "expire", "Expire")]
    [InlineData(WorkItemStatus.Completed, "expire", "Expire")]
    [InlineData(WorkItemStatus.Rejected, "expire", "Expire")]
    public void Cross_terminal_cascade_command_is_rejected_with_no_terminal_event_and_no_state_change(
        WorkItemStatus from, string command, string attemptedAct)
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(from, Tenant, Item);
        long sequenceBefore = state.Sequence;

        DomainResult result = command == "cancel"
            ? WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Item), state)
            : WorkItemAggregate.Handle(new ExpireWorkItem(Tenant, Item), state);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.IsNoOp.ShouldBeFalse();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(from);
        rejection.AttemptedAct.ShouldBe(attemptedAct);
        result.Events.Any(e => e is WorkItemCancelled or WorkItemExpired).ShouldBeFalse();
        state.Status.ShouldBe(from);
        state.Sequence.ShouldBe(sequenceBefore);
    }
}
