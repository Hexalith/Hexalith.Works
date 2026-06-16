using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>
/// Story 3.6 reactor coverage (AC #1/#4/#5/#6): the pure <see cref="TerminalCascadeTranslator"/> turns a
/// parent terminal event plus caller-supplied descendant candidates into same-kind terminal command
/// intents. It is mechanical — tenant equality fails closed, explicitly-terminal candidates are skipped,
/// input order is preserved, and it makes no aggregate acceptance decision.
/// </summary>
public sealed class TerminalCascadeTranslatorTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly TenantId OtherTenant = new("tenant-beta");
    private static readonly WorkItemId Parent = new("parent-001");
    private static readonly WorkItemId ChildA = new("child-001");
    private static readonly WorkItemId ChildB = new("child-002");

    [Fact]
    public void Cancelled_parent_emits_cancel_intents_for_same_tenant_active_descendants_in_input_order()
    {
        WorkItemCancelled parentCancelled = new(Parent.Value, 5, Tenant, Parent);
        CascadeDescendant[] descendants =
        [
            new(Tenant, ChildA, IsTerminal: false),
            new(Tenant, ChildB, IsTerminal: false),
        ];

        IReadOnlyList<CancelWorkItem> commands = TerminalCascadeTranslator.ToCascadeCommands(parentCancelled, descendants);

        commands.Select(c => c.WorkItemId).ShouldBe([ChildA, ChildB]);
        commands.ShouldAllBe(c => c.TenantId == Tenant);
    }

    [Fact]
    public void Expired_parent_emits_expire_intents_for_same_tenant_active_descendants_in_input_order()
    {
        WorkItemExpired parentExpired = new(Parent.Value, 5, Tenant, Parent);
        CascadeDescendant[] descendants =
        [
            new(Tenant, ChildA, IsTerminal: false),
            new(Tenant, ChildB, IsTerminal: false),
        ];

        IReadOnlyList<ExpireWorkItem> commands = TerminalCascadeTranslator.ToCascadeCommands(parentExpired, descendants);

        commands.Select(c => c.WorkItemId).ShouldBe([ChildA, ChildB]);
        commands.ShouldAllBe(c => c.TenantId == Tenant);
    }

    [Fact]
    public void Already_terminal_descendants_are_skipped_so_no_duplicate_terminal_intent_is_emitted()
    {
        WorkItemCancelled parentCancelled = new(Parent.Value, 5, Tenant, Parent);
        CascadeDescendant[] descendants =
        [
            new(Tenant, ChildA, IsTerminal: true),   // already terminal -> skipped, no duplicate cancel
            new(Tenant, ChildB, IsTerminal: false),  // still active -> cascade target
        ];

        IReadOnlyList<CancelWorkItem> commands = TerminalCascadeTranslator.ToCascadeCommands(parentCancelled, descendants);

        commands.ShouldHaveSingleItem().WorkItemId.ShouldBe(ChildB);
    }

    [Fact]
    public void Expired_parent_skips_already_terminal_candidates_too()
    {
        WorkItemExpired parentExpired = new(Parent.Value, 5, Tenant, Parent);
        CascadeDescendant[] descendants =
        [
            new(Tenant, ChildA, IsTerminal: true),
            new(Tenant, ChildB, IsTerminal: false),
        ];

        IReadOnlyList<ExpireWorkItem> commands = TerminalCascadeTranslator.ToCascadeCommands(parentExpired, descendants);

        commands.ShouldHaveSingleItem().WorkItemId.ShouldBe(ChildB);
    }

    [Fact]
    public void Cross_tenant_descendants_are_ignored_even_when_work_item_ids_collide()
    {
        WorkItemExpired parentExpired = new(Parent.Value, 5, Tenant, Parent);
        WorkItemId colliding = new("child-001"); // same id text as ChildA but a different tenant

        CascadeDescendant[] descendants =
        [
            new(OtherTenant, colliding, IsTerminal: false),  // foreign tenant -> fail closed
            new(Tenant, ChildA, IsTerminal: false),
        ];

        IReadOnlyList<ExpireWorkItem> commands = TerminalCascadeTranslator.ToCascadeCommands(parentExpired, descendants);

        ExpireWorkItem command = commands.ShouldHaveSingleItem();
        command.TenantId.ShouldBe(Tenant);
        command.WorkItemId.ShouldBe(ChildA);
    }

    [Fact]
    public void Cancelled_parent_also_ignores_cross_tenant_descendants_with_colliding_ids()
    {
        WorkItemCancelled parentCancelled = new(Parent.Value, 5, Tenant, Parent);
        WorkItemId colliding = new("child-001");

        CascadeDescendant[] descendants =
        [
            new(OtherTenant, colliding, IsTerminal: false),
            new(Tenant, ChildA, IsTerminal: false),
        ];

        IReadOnlyList<CancelWorkItem> commands = TerminalCascadeTranslator.ToCascadeCommands(parentCancelled, descendants);

        commands.ShouldHaveSingleItem().TenantId.ShouldBe(Tenant);
    }

    [Fact]
    public void Empty_descendant_input_produces_no_cascade_commands()
    {
        TerminalCascadeTranslator.ToCascadeCommands(new WorkItemCancelled(Parent.Value, 5, Tenant, Parent), []).ShouldBeEmpty();
        TerminalCascadeTranslator.ToCascadeCommands(new WorkItemExpired(Parent.Value, 5, Tenant, Parent), []).ShouldBeEmpty();
    }

    [Fact]
    public void Translator_carries_no_status_acceptance_decision_beyond_active_terminal_filtering()
    {
        // D1/D4: the cascade input model carries no parent/descendant status decision. Selection is only
        // tenant equality plus an explicit terminal skip; acceptance, rejection and no-op stay owned by
        // WorkItemAggregate.Handle. A redelivered descendant therefore yields a command each time — the
        // translator does NOT dedup; redelivery safety is the target aggregate's idempotency.
        typeof(CascadeDescendant).GetProperty("Status")
            .ShouldBeNull("The cascade input must not carry a status acceptance decision.");

        WorkItemCancelled parentCancelled = new(Parent.Value, 5, Tenant, Parent);
        CascadeDescendant[] redelivered =
        [
            new(Tenant, ChildA, IsTerminal: false),
            new(Tenant, ChildA, IsTerminal: false),
        ];

        IReadOnlyList<CancelWorkItem> commands = TerminalCascadeTranslator.ToCascadeCommands(parentCancelled, redelivered);

        commands.Count.ShouldBe(2);
        commands.ShouldAllBe(c => c.WorkItemId == ChildA);
    }

    [Fact]
    public void Mixed_batch_preserves_active_target_order_while_skipping_terminal_and_cross_tenant_candidates()
    {
        // Order preservation must survive interleaved filtering: active targets keep their relative input
        // order even when terminal and foreign-tenant candidates sit between them (AC #1 order + AC #6
        // fail-closed in one batch), not only when the actives are contiguous.
        WorkItemCancelled parentCancelled = new(Parent.Value, 5, Tenant, Parent);
        WorkItemId childC = new("child-003");
        CascadeDescendant[] descendants =
        [
            new(Tenant, ChildA, IsTerminal: false),                            // target 1
            new(Tenant, ChildB, IsTerminal: true),                             // already terminal -> skipped
            new(OtherTenant, new WorkItemId("child-003"), IsTerminal: false),  // foreign tenant (colliding id) -> skipped
            new(Tenant, childC, IsTerminal: false),                            // target 2
        ];

        IReadOnlyList<CancelWorkItem> commands = TerminalCascadeTranslator.ToCascadeCommands(parentCancelled, descendants);

        commands.Select(c => c.WorkItemId).ShouldBe([ChildA, childC]);
        commands.ShouldAllBe(c => c.TenantId == Tenant);
    }

    [Fact]
    public void Null_parent_terminal_event_or_null_descendant_list_throws_for_both_cascade_kinds()
    {
        // AC #4 / defensive contract: the pure translator is a mechanical mapping with explicit guards;
        // a null trigger event or null candidate list is a caller contract violation, not a silent no-op.
        CascadeDescendant[] descendants = [new(Tenant, ChildA, IsTerminal: false)];

        Should.Throw<ArgumentNullException>(() => TerminalCascadeTranslator.ToCascadeCommands((WorkItemCancelled)null!, descendants));
        Should.Throw<ArgumentNullException>(() => TerminalCascadeTranslator.ToCascadeCommands((WorkItemExpired)null!, descendants));
        Should.Throw<ArgumentNullException>(() => TerminalCascadeTranslator.ToCascadeCommands(new WorkItemCancelled(Parent.Value, 5, Tenant, Parent), null!));
        Should.Throw<ArgumentNullException>(() => TerminalCascadeTranslator.ToCascadeCommands(new WorkItemExpired(Parent.Value, 5, Tenant, Parent), null!));
    }

    [Fact]
    public void Null_descendant_element_fails_closed_with_argument_null_exception()
    {
        // A null candidate inside the caller-supplied list fails closed: the translator materializes
        // eagerly and throws rather than emitting a partial cascade that silently drops the bad element.
        CascadeDescendant[] withNullElement = [null!, new(Tenant, ChildA, IsTerminal: false)];

        Should.Throw<ArgumentNullException>(() => TerminalCascadeTranslator.ToCascadeCommands(new WorkItemCancelled(Parent.Value, 5, Tenant, Parent), withNullElement));
        Should.Throw<ArgumentNullException>(() => TerminalCascadeTranslator.ToCascadeCommands(new WorkItemExpired(Parent.Value, 5, Tenant, Parent), withNullElement));
    }
}
