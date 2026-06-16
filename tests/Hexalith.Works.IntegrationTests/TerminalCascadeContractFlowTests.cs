using System.Text.Json;

using Hexalith.EventStore.Contracts.Results;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Hexalith.Works.Server.Aggregates;
using Hexalith.Works.Testing;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Story 3.6 cascade contract-flow slice (AC #1/#2/#3/#6). A parent terminal event crosses the real
/// <see cref="System.Text.Json"/> boundary (write → persist → replay), the pure
/// <see cref="TerminalCascadeTranslator"/> turns it into same-kind descendant command intents, those
/// intents cross the boundary too, and each is handled by an independent descendant aggregate that
/// applies its own lifecycle transition. Proves the cascade trigger events and target commands reuse the
/// existing v1 catalog (no new durable type), active same-tenant descendants reach the matching terminal
/// status, redelivery of a cascade command is an idempotent no-op, and already-terminal or cross-tenant
/// descendants are never targeted. No runtime dispatch, checkpoint, Dapr, clock, or reminder is involved.
/// </summary>
public sealed class TerminalCascadeContractFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly TenantId OtherTenant = new("tenant-beta");
    private static readonly WorkItemId Parent = new("parent-001");
    private static readonly WorkItemId ActiveChild = new("child-active");
    private static readonly WorkItemId TerminalChild = new("child-terminal");

    [Fact]
    public void Parent_cancel_cascades_through_serialization_to_active_descendant_and_skips_terminal_and_foreign()
    {
        // Parent terminates (InProgress -> Cancelled); the trigger event is persisted/replayed through JSON.
        WorkItemState parent = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Parent);
        WorkItemCancelled parentCancelled = WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Parent), parent)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemCancelled>();
        WorkItemCancelled persisted = RoundTrip(parentCancelled);

        // The reactor selects only the same-tenant, still-active descendant: the already-terminal child is
        // skipped and the foreign-tenant child (colliding id text) fails the tenant-equality check.
        CascadeDescendant[] descendants =
        [
            new(Tenant, ActiveChild, IsTerminal: false),
            new(Tenant, TerminalChild, IsTerminal: true),
            new(OtherTenant, new WorkItemId("child-active"), IsTerminal: false),
        ];
        IReadOnlyList<CancelWorkItem> intents = TerminalCascadeTranslator.ToCascadeCommands(persisted, descendants);

        CancelWorkItem intent = intents.ShouldHaveSingleItem();
        intent.TenantId.ShouldBe(Tenant);
        intent.WorkItemId.ShouldBe(ActiveChild);

        // The intent crosses the boundary and the descendant aggregate applies its own terminal transition.
        CancelWorkItem deliveredIntent = RoundTrip(intent);
        WorkItemState child = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, ActiveChild);
        WorkItemCancelled childCancelled = WorkItemAggregate.Handle(deliveredIntent, child)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemCancelled>();
        child.Apply(RoundTrip(childCancelled));
        child.Status.ShouldBe(WorkItemStatus.Cancelled);
        long sequenceAfterCancel = child.Sequence;

        // Redelivery of the same cascade command to the now-cancelled descendant is an idempotent no-op:
        // no duplicate WorkItemCancelled, no sequence burn.
        DomainResult redelivered = WorkItemAggregate.Handle(deliveredIntent, child);
        redelivered.IsNoOp.ShouldBeTrue();
        redelivered.Events.ShouldBeEmpty();
        child.Status.ShouldBe(WorkItemStatus.Cancelled);
        child.Sequence.ShouldBe(sequenceAfterCancel);
    }

    [Fact]
    public void Parent_expire_cascades_through_serialization_to_active_descendant_with_idempotent_redelivery()
    {
        WorkItemState parent = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Parent);
        WorkItemExpired parentExpired = WorkItemAggregate.Handle(new ExpireWorkItem(Tenant, Parent), parent)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemExpired>();
        WorkItemExpired persisted = RoundTrip(parentExpired);

        CascadeDescendant[] descendants =
        [
            new(Tenant, ActiveChild, IsTerminal: false),
            new(OtherTenant, new WorkItemId("child-active"), IsTerminal: false),
        ];
        IReadOnlyList<ExpireWorkItem> intents = TerminalCascadeTranslator.ToCascadeCommands(persisted, descendants);

        ExpireWorkItem intent = intents.ShouldHaveSingleItem();
        intent.TenantId.ShouldBe(Tenant);
        intent.WorkItemId.ShouldBe(ActiveChild);

        // A Suspended descendant still expires through its own lifecycle table.
        ExpireWorkItem deliveredIntent = RoundTrip(intent);
        WorkItemState child = WorkItemStateBuilder.InStatus(WorkItemStatus.Suspended, Tenant, ActiveChild);
        WorkItemExpired childExpired = WorkItemAggregate.Handle(deliveredIntent, child)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemExpired>();
        child.Apply(RoundTrip(childExpired));
        child.Status.ShouldBe(WorkItemStatus.Expired);
        long sequenceAfterExpire = child.Sequence;

        DomainResult redelivered = WorkItemAggregate.Handle(deliveredIntent, child);
        redelivered.IsNoOp.ShouldBeTrue();
        redelivered.Events.ShouldBeEmpty();
        child.Sequence.ShouldBe(sequenceAfterExpire);
    }

    [Fact]
    public void Parent_cancel_cascade_reaching_an_already_expired_descendant_rejects_through_serialization_without_a_duplicate_terminal_event()
    {
        // AC #3 at the integration boundary: cascade discovery is supplied and can be stale. The caller
        // marks a descendant still active, so the translator emits a CancelWorkItem intent for it, but by
        // delivery time the descendant has already Expired on its own. The cross-terminal cascade command
        // crosses the JSON boundary and must reject (not re-terminalize), emit no duplicate terminal event,
        // and leave the persisted descendant end-state (status + sequence) untouched — the redelivery
        // safety that lets cascade lean on target-aggregate idempotency instead of an out-of-band dedup.
        WorkItemState parent = WorkItemStateBuilder.InStatus(WorkItemStatus.InProgress, Tenant, Parent);
        WorkItemCancelled parentCancelled = WorkItemAggregate.Handle(new CancelWorkItem(Tenant, Parent), parent)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemCancelled>();
        WorkItemCancelled persisted = RoundTrip(parentCancelled);

        // Stale snapshot: caller still believes the descendant is active, so it is a cascade target.
        CascadeDescendant[] descendants = [new(Tenant, ActiveChild, IsTerminal: false)];
        CancelWorkItem intent = TerminalCascadeTranslator.ToCascadeCommands(persisted, descendants).ShouldHaveSingleItem();
        CancelWorkItem deliveredIntent = RoundTrip(intent);

        // The descendant has actually already Expired; the cascade Cancel is cross-terminal.
        WorkItemState child = WorkItemStateBuilder.InStatus(WorkItemStatus.Expired, Tenant, ActiveChild);
        long sequenceBefore = child.Sequence;

        DomainResult result = WorkItemAggregate.Handle(deliveredIntent, child);

        result.IsRejection.ShouldBeTrue();
        WorkItemTransitionRejected rejection = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemTransitionRejected>();
        rejection.FromStatus.ShouldBe(WorkItemStatus.Expired);
        rejection.AttemptedAct.ShouldBe("Cancel");
        result.Events.Any(e => e is WorkItemCancelled or WorkItemExpired).ShouldBeFalse();
        child.Status.ShouldBe(WorkItemStatus.Expired);
        child.Sequence.ShouldBe(sequenceBefore);
    }

    private static T RoundTrip<T>(T value)
        where T : class
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions).ShouldNotBeNull();
}
