using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>
/// Story 4.4 — the pure tenant what's-next projection, ordering comparator, query-side authorization
/// filter, and notifier change-signal. Mirrors the <see cref="WorkItemRollUpProjectionTests"/> fixtures
/// and event-delivery harness. Proofs are deterministic; cross-tenant negatives are mutation-validated.
/// </summary>
public sealed class WhatsNextQueueProjectionTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly TenantId OtherTenant = new("tenant-beta");
    private static readonly Unit Hour = new("hour");
    private static readonly Unit Point = new("point");
    private static readonly ExecutorBinding Binding =
        new(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Coordinate);

    private static readonly ExecutorBinding OtherBinding =
        new(new PartyId("party-456"), Channel.Cli, AuthorityLevel.Contribute);

    private static readonly WorkItemId Work1 = new("work-001");
    private static readonly WorkItemId Work2 = new("work-002");

    // ----- Task 2: WhatsNextItem read-model contract (AC #3) -----

    [Fact]
    public void Read_model_keeps_own_and_rolled_remaining_as_distinct_types()
    {
        OwnRemaining own = new(5m, Hour);
        RolledRemaining rolled = new(8m, Hour);

        own.GetType().ShouldNotBe(rolled.GetType());

        WhatsNextItem item = new(
            Tenant,
            Work1,
            WorkItemStatus.Queued,
            Priority.High,
            new DateOnly(2026, 7, 1),
            Binding,
            own,
            rolled,
            [rolled],
            [AwaitCondition.ExternalSignal("await-x")],
            3);

        item.OwnRemaining.ShouldBe(own);
        item.RolledRemaining.ShouldBe(rolled);
        item.OwnRemaining.ShouldBeOfType<OwnRemaining>();
        item.RolledRemaining.ShouldBeOfType<RolledRemaining>();
        item.ExecutorBinding.ShouldBe(Binding);
        item.AwaitConditions.ShouldHaveSingleItem().Kind.ShouldBe(AwaitConditionKind.ExternalSignal);
    }

    [Fact]
    public void Read_model_exposes_only_owned_data_with_no_ui_or_executor_kind_surface()
    {
        string[] propertyNames = [.. typeof(WhatsNextItem).GetProperties().Select(p => p.Name)];

        string[] forbidden =
            ["Bot", "Human", "DisplayName", "Colour", "Color", "Glyph", "Label", "DataGrid", "Avatar"];
        foreach (string name in propertyNames)
        {
            foreach (string token in forbidden)
            {
                name.ShouldNotContain(token, Case.Insensitive);
            }
        }
    }

    // ----- Task 3: eligibility, binding, isolation, rolled composition (AC #1, #3, #5) -----

    [Fact]
    public void What_s_next_returns_only_queued_and_assigned_items_for_the_tenant()
    {
        WhatsNextQueueProjection projection = new();

        // Assigned (eligible).
        WorkItemId assigned = new("item-assigned");
        Deliver(projection, Created(assigned, 1));
        Deliver(projection, new WorkItemAssigned(assigned.Value, 2, Tenant, assigned, Binding));

        // Queued (eligible).
        WorkItemId queued = new("item-queued");
        Deliver(projection, Created(queued, 1));
        Deliver(projection, new WorkItemQueued(queued.Value, 2, Tenant, queued));

        // Created (not eligible).
        WorkItemId created = new("item-created");
        Deliver(projection, Created(created, 1));

        // InProgress (not eligible).
        WorkItemId inProgress = new("item-inprogress");
        Deliver(projection, Created(inProgress, 1));
        Deliver(projection, new WorkItemClaimed(inProgress.Value, 2, Tenant, inProgress, Binding));

        // Suspended (not eligible).
        WorkItemId suspended = new("item-suspended");
        Deliver(projection, Created(suspended, 1));
        Deliver(projection, new WorkItemClaimed(suspended.Value, 2, Tenant, suspended, Binding));
        Deliver(projection, new WorkItemSuspended(suspended.Value, 3, Tenant, suspended, [AwaitCondition.ExternalSignal("await")]));

        // Completed terminal (not eligible).
        WorkItemId completed = new("item-completed");
        Deliver(projection, Created(completed, 1));
        Deliver(projection, new WorkItemCompleted(completed.Value, 2, Tenant, completed));

        IReadOnlyList<WhatsNextItem> result = projection.WhatsNext(Tenant);

        result.Select(i => i.WorkItemId).ShouldBe([assigned, queued], ignoreOrder: true);
        result.ShouldAllBe(i => i.Status == WorkItemStatus.Queued || i.Status == WorkItemStatus.Assigned);
    }

    [Fact]
    public void Requeued_rejection_is_eligible_but_a_non_requeue_rejection_is_not()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1));
        Deliver(projection, new WorkItemRejected(Work1.Value, 2, Tenant, Work1, Requeue: true));

        Deliver(projection, Created(Work2, 1));
        Deliver(projection, new WorkItemRejected(Work2.Value, 2, Tenant, Work2, Requeue: false));

        IReadOnlyList<WhatsNextItem> result = projection.WhatsNext(Tenant);
        result.ShouldHaveSingleItem().WorkItemId.ShouldBe(Work1);
        result[0].Status.ShouldBe(WorkItemStatus.Queued);
    }

    [Fact]
    public void Queued_item_keeps_its_last_executor_binding()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1));
        Deliver(projection, new WorkItemAssigned(Work1.Value, 2, Tenant, Work1, Binding));
        Deliver(projection, new WorkItemQueued(Work1.Value, 3, Tenant, Work1));

        WhatsNextItem item = projection.WhatsNext(Tenant).ShouldHaveSingleItem();
        item.Status.ShouldBe(WorkItemStatus.Queued);
        item.ExecutorBinding.ShouldBe(Binding);
    }

    [Fact]
    public void Own_remaining_is_derived_and_rolled_remaining_is_composed_only_where_a_roll_up_is_supplied()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1, effort: 5m));
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        Deliver(projection, new ProgressReported(Work1.Value, 3, Tenant, Work1, 2m, Hour));

        WhatsNextItem withoutRollUp = projection.WhatsNext(Tenant).ShouldHaveSingleItem();
        withoutRollUp.OwnRemaining.ShouldBe(new OwnRemaining(3m, Hour));
        withoutRollUp.RolledRemaining.ShouldBeNull();
        withoutRollUp.RolledRemainingByUnit.ShouldBeEmpty();

        WorkItemRollUp rollUp = new(
            Tenant,
            Work1,
            WorkItemStatus.Queued,
            null,
            new OwnRemaining(3m, Hour),
            new RolledRemaining(9m, Hour),
            [new RolledRemaining(9m, Hour)],
            [],
            0,
            3);

        WhatsNextItem withRollUp = projection
            .WhatsNext(Tenant, (_, id) => id == Work1 ? rollUp : null)
            .ShouldHaveSingleItem();
        withRollUp.OwnRemaining.ShouldBe(new OwnRemaining(3m, Hour));
        withRollUp.RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));
        withRollUp.RolledRemainingByUnit.ShouldBe([new RolledRemaining(9m, Hour)]);
        withRollUp.OwnRemaining.ShouldNotBeNull().GetType()
            .ShouldNotBe(withRollUp.RolledRemaining.ShouldNotBeNull().GetType());
    }

    [Fact]
    public void Mismatched_unit_progress_retains_the_last_valid_own_remaining()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1, effort: 4m));
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        Deliver(projection, new ProgressReported(Work1.Value, 3, Tenant, Work1, 1m, Hour));   // 4 -> 3
        Deliver(projection, new ProgressReported(Work1.Value, 4, Tenant, Work1, 2m, Point));  // refused, retained
        Deliver(projection, new ProgressReported(Work1.Value, 5, Tenant, Work1, 1m, Hour));   // 3 -> 2

        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBe(new OwnRemaining(2m, Hour));
    }

    [Fact]
    public void Poisoned_non_positive_done_delta_retains_own_remaining_and_a_later_valid_fact_still_applies()
    {
        // Read-side defense against a corrupted stream: the write side validates DoneDelta > 0, but a
        // persisted non-positive delta must be refused (retain the last valid value, mirroring the
        // unit-mismatch path) instead of throwing inside WorkItemEffort.Report and wedging every rebuild.
        WhatsNextQueueProjection projection = new();
        Deliver(projection, Created(Work1, 1, effort: 4m));
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        Deliver(projection, new ProgressReported(Work1.Value, 3, Tenant, Work1, 1m, Hour));   // 4 -> 3

        WorkItemRollUpEvent poisoned = Envelope(new ProgressReported(Work1.Value, 4, Tenant, Work1, 0m, Hour));
        _ = projection.Project(poisoned);
        _ = projection.Project(poisoned);                                                    // idempotent redelivery
        Deliver(projection, new ProgressReported(Work1.Value, 5, Tenant, Work1, 1m, Hour));  // 3 -> 2 (still applied)

        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBe(new OwnRemaining(2m, Hour));
    }

    [Fact]
    public void Poisoned_negative_estimate_is_refused_and_never_establishes_or_updates_own_remaining()
    {
        // A persisted negative estimate would throw inside the WorkItemEffort constructor (establish
        // path) or ReEstimate (update path); both are refused, and later valid facts still apply.
        WhatsNextQueueProjection projection = new();
        Deliver(projection, Created(Work1, 1));   // no initial effort
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));

        Deliver(projection, new ReEstimated(Work1.Value, 3, Tenant, Work1, -5m, Hour));   // refused (establish path)
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBeNull();

        Deliver(projection, new ReEstimated(Work1.Value, 4, Tenant, Work1, 7m, Hour));    // valid still applies
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBe(new OwnRemaining(7m, Hour));

        Deliver(projection, new ReEstimated(Work1.Value, 5, Tenant, Work1, -1m, Hour));   // refused (update path)
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBe(new OwnRemaining(7m, Hour));
    }

    [Fact]
    public void Colliding_inner_ids_across_tenants_with_similar_schedules_never_cross_and_flip_with_the_queried_tenant()
    {
        WhatsNextQueueProjection projection = new();
        WorkItemId colliding = new("work-001");
        WorkItemSchedule schedule = new(Priority.High, new DateOnly(2026, 7, 1));

        Deliver(projection, CreatedFor(Tenant, colliding, 1, schedule));
        Deliver(projection, new WorkItemQueued(colliding.Value, 2, Tenant, colliding));

        Deliver(projection, CreatedFor(OtherTenant, colliding, 1, schedule));
        Deliver(projection, new WorkItemQueued(colliding.Value, 2, OtherTenant, colliding));

        IReadOnlyList<WhatsNextItem> tenantQueue = projection.WhatsNext(Tenant);
        IReadOnlyList<WhatsNextItem> otherQueue = projection.WhatsNext(OtherTenant);

        tenantQueue.ShouldHaveSingleItem().TenantId.ShouldBe(Tenant);
        otherQueue.ShouldHaveSingleItem().TenantId.ShouldBe(OtherTenant);
        tenantQueue.ShouldAllBe(i => i.TenantId == Tenant);
        otherQueue.ShouldAllBe(i => i.TenantId == OtherTenant);
    }

    [Fact]
    public void Tenant_or_id_mismatched_payloads_and_non_positive_sequences_are_ignored()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1));
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));

        // Envelope tenant matches but the payload carries a foreign tenant — rejected.
        _ = projection.Project(new WorkItemRollUpEvent(Tenant, Work1, 3, new WorkItemAssigned(Work1.Value, 3, OtherTenant, Work1, Binding)));

        // Non-positive sequence — ignored.
        _ = projection.Project(new WorkItemRollUpEvent(Tenant, Work1, 0, new WorkItemClaimed(Work1.Value, 0, Tenant, Work1, Binding)));

        projection.WhatsNext(Tenant).ShouldHaveSingleItem().Status.ShouldBe(WorkItemStatus.Queued);
        projection.WhatsNext(OtherTenant).ShouldBeEmpty();
    }

    [Fact]
    public void Tenant_mismatched_child_spawned_payload_is_refused_and_does_not_poison_its_sequence_slot()
    {
        WhatsNextQueueProjection projection = new();
        Deliver(projection, Created(Work1, 1));

        // The envelope header matches (Tenant, Work1) but the payload carries a foreign tenant —
        // fail-closed, so the sequence-2 slot stays free for the legitimate queue fact below.
        _ = projection.Project(new WorkItemRollUpEvent(Tenant, Work1, 2, new ChildSpawned(
            Work1.Value, 2, OtherTenant, Work1, Work2, new Obligation("spawned child work"))));

        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().Status.ShouldBe(WorkItemStatus.Queued);
    }

    [Fact]
    public void Matching_child_spawned_fact_is_accepted_and_reports_no_queue_change()
    {
        // ChildSpawned is part of the fourteen-event delivery stream this projection shares with the
        // roll-up: a header-consistent ChildSpawned must pass the fail-closed match (it owns its
        // aggregate-local sequence slot) even though it never affects eligibility or ordering.
        WhatsNextQueueProjection projection = new();
        Deliver(projection, Created(Work1, 1));
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));

        WhatsNextProjectionChange change = projection.Project(new WorkItemRollUpEvent(Tenant, Work1, 3, new ChildSpawned(
            Work1.Value, 3, Tenant, Work1, Work2, new Obligation("spawned child work"))));

        change.Changed.ShouldBeFalse();
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().Status.ShouldBe(WorkItemStatus.Queued);
    }

    [Fact]
    public void Out_of_order_and_duplicate_delivery_converge_to_the_same_queue_and_ordering()
    {
        WorkItemId work3 = new("work-003");
        WorkItemRollUpEvent[] events =
        [
            Envelope(CreatedFor(Tenant, Work1, 1, new WorkItemSchedule(Priority.Normal, new DateOnly(2026, 8, 1)))),
            Envelope(new WorkItemQueued(Work1.Value, 2, Tenant, Work1)),
            Envelope(CreatedFor(Tenant, Work2, 1, new WorkItemSchedule(Priority.Critical))),
            Envelope(new WorkItemAssigned(Work2.Value, 2, Tenant, Work2, Binding)),
            Envelope(CreatedFor(Tenant, work3, 1, new WorkItemSchedule(Priority.Normal, new DateOnly(2026, 7, 1)))),
            Envelope(new WorkItemQueued(work3.Value, 2, Tenant, work3)),
            Envelope(new WorkItemClaimed(work3.Value, 3, Tenant, work3, Binding)),  // work3 leaves the pool
        ];

        WhatsNextQueueProjection natural = Replay(events);
        WhatsNextQueueProjection shuffled = Replay([.. events.Reverse(), .. events]);

        SameQueue(natural, shuffled);
        natural.WhatsNext(Tenant).Select(i => i.WorkItemId).ShouldBe([Work2, Work1]);
    }

    // ----- Task 4: ordering comparator (AC #2) -----

    [Fact]
    public void Orders_by_priority_rank_critical_high_normal_low()
    {
        WhatsNextQueueProjection projection = new();
        WorkItemId low = new("p-low");
        WorkItemId critical = new("p-critical");
        WorkItemId normal = new("p-normal");
        WorkItemId high = new("p-high");

        QueueWith(projection, low, Priority.Low, null);
        QueueWith(projection, critical, Priority.Critical, null);
        QueueWith(projection, normal, Priority.Normal, null);
        QueueWith(projection, high, Priority.High, null);

        projection.WhatsNext(Tenant).Select(i => i.WorkItemId).ShouldBe([critical, high, normal, low]);
    }

    [Fact]
    public void Within_one_priority_orders_by_earliest_due_date()
    {
        WhatsNextQueueProjection projection = new();
        WorkItemId late = new("d-late");
        WorkItemId early = new("d-early");
        WorkItemId middle = new("d-middle");

        QueueWith(projection, late, Priority.Normal, new DateOnly(2026, 9, 1));
        QueueWith(projection, early, Priority.Normal, new DateOnly(2026, 7, 1));
        QueueWith(projection, middle, Priority.Normal, new DateOnly(2026, 8, 1));

        projection.WhatsNext(Tenant).Select(i => i.WorkItemId).ShouldBe([early, middle, late]);
    }

    [Fact]
    public void Within_identical_priority_and_due_date_orders_by_work_item_id_ordinal()
    {
        WhatsNextQueueProjection projection = new();
        DateOnly due = new(2026, 7, 1);
        WorkItemId third = new("tie-003");
        WorkItemId first = new("tie-001");
        WorkItemId second = new("tie-002");

        QueueWith(projection, third, Priority.High, due);
        QueueWith(projection, first, Priority.High, due);
        QueueWith(projection, second, Priority.High, due);

        projection.WhatsNext(Tenant).Select(i => i.WorkItemId).ShouldBe([first, second, third]);
    }

    [Fact]
    public void Item_with_only_a_priority_sorts_before_an_item_with_only_a_due_date()
    {
        WhatsNextQueueProjection projection = new();
        WorkItemId priorityOnly = new("only-priority");
        WorkItemId dueOnly = new("only-due");

        QueueWith(projection, dueOnly, null, new DateOnly(2026, 1, 1));
        QueueWith(projection, priorityOnly, Priority.Low, null);

        projection.WhatsNext(Tenant).Select(i => i.WorkItemId).ShouldBe([priorityOnly, dueOnly]);
    }

    [Fact]
    public void Item_with_neither_priority_nor_due_date_sorts_last()
    {
        WhatsNextQueueProjection projection = new();
        WorkItemId lowPriority = new("low");
        WorkItemId noPriorityButDue = new("due-only");
        WorkItemId neither = new("neither");

        QueueWith(projection, lowPriority, Priority.Low, null);
        QueueWith(projection, noPriorityButDue, null, new DateOnly(2026, 1, 1));
        QueueWith(projection, neither, null, null);

        projection.WhatsNext(Tenant).Select(i => i.WorkItemId).ShouldBe([lowPriority, noPriorityButDue, neither]);
    }

    [Fact]
    public void Unknown_priority_ranks_last_like_an_absent_priority()
    {
        WhatsNextOrdering.PriorityRank(null).ShouldBe(WhatsNextOrdering.AbsentPriorityRank);
        WhatsNextOrdering.PriorityRank(Priority.Unknown).ShouldBe(WhatsNextOrdering.AbsentPriorityRank);
        WhatsNextOrdering.PriorityRank(Priority.Critical).ShouldBe(0);
        WhatsNextOrdering.PriorityRank(Priority.Low).ShouldBe(3);
    }

    // ----- Task 5: query-side authorization filter (AC #1) -----

    [Fact]
    public void Authorization_filter_drops_items_from_another_tenant()
    {
        WhatsNextItem mine = ReadModel(Tenant, Work1);
        WhatsNextItem foreign = ReadModel(OtherTenant, Work2);

        WhatsNextQueryAuthorization.FilterList(Tenant.Value, [mine, foreign]).ShouldBe([mine]);
    }

    [Fact]
    public void Authorization_predicate_removes_only_the_rejected_item()
    {
        WhatsNextItem allowed = ReadModel(Tenant, Work1);
        WhatsNextItem rejected = ReadModel(Tenant, Work2);

        WhatsNextQueryAuthorization
            .FilterList(Tenant.Value, [allowed, rejected], item => item.WorkItemId == Work1)
            .ShouldBe([allowed]);
    }

    [Fact]
    public void Authorization_filter_is_fail_closed_for_a_null_or_empty_authoritative_tenant()
    {
        WhatsNextItem mine = ReadModel(Tenant, Work1);

        WhatsNextQueryAuthorization.FilterList(null, [mine]).ShouldBeEmpty();
        WhatsNextQueryAuthorization.FilterList("   ", [mine]).ShouldBeEmpty();
        WhatsNextQueryAuthorization.Filter(null, mine).ShouldBeNull();
        WhatsNextQueryAuthorization.Filter(Tenant.Value, mine).ShouldBe(mine);
        WhatsNextQueryAuthorization.Filter(OtherTenant.Value, mine).ShouldBeNull();
    }

    // ----- Task 6: notifier change-signal (AC #4) -----

    [Fact]
    public void Projection_type_token_is_the_stable_kebab_case_value()
        => WhatsNextQueueProjection.ProjectionType.ShouldBe("works-whats-next");

    [Fact]
    public void Project_reports_a_change_when_an_item_enters_the_pool()
    {
        WhatsNextQueueProjection projection = new();

        Project(projection, Created(Work1, 1)).Changed.ShouldBeFalse();

        WhatsNextProjectionChange queued = Project(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        queued.Changed.ShouldBeTrue();
        queued.TenantId.ShouldBe(Tenant);
    }

    [Fact]
    public void Project_reports_a_change_when_an_eligible_item_leaves_the_pool()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1));
        Project(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1)).Changed.ShouldBeTrue();
        Project(projection, new WorkItemClaimed(Work1.Value, 3, Tenant, Work1, Binding)).Changed.ShouldBeTrue();
    }

    [Fact]
    public void Project_reports_no_change_for_binding_or_remaining_only_updates_on_an_eligible_item()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1, effort: 5m));
        Project(projection, new WorkItemAssigned(Work1.Value, 2, Tenant, Work1, Binding)).Changed.ShouldBeTrue();

        Project(projection, new WorkItemAssigned(Work1.Value, 3, Tenant, Work1, OtherBinding)).Changed.ShouldBeFalse();
        Project(projection, new ProgressReported(Work1.Value, 4, Tenant, Work1, 1m, Hour)).Changed.ShouldBeFalse();
    }

    [Fact]
    public void Project_reports_a_change_when_an_eligible_item_is_rescheduled_to_a_new_priority()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, CreatedFor(Tenant, Work1, 1, new WorkItemSchedule(Priority.Normal)));
        Project(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1)).Changed.ShouldBeTrue();

        Project(projection, new WorkItemRescheduled(Work1.Value, 3, Tenant, Work1, new WorkItemSchedule(Priority.Critical)))
            .Changed.ShouldBeTrue();
    }

    [Fact]
    public void Project_reports_no_change_for_a_duplicate_sequence_delivery()
    {
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1));
        Project(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1)).Changed.ShouldBeTrue();
        Project(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1)).Changed.ShouldBeFalse();
    }

    // ----- QA gap-fill pass (bmad-qa-generate-e2e-tests): AC #1–#5 / DC1/DC4/DC5/DC7 -----

    [Fact]
    public void Query_side_authorization_composes_with_tenant_scoping_as_a_distinct_control()
    {
        // AC #1 verbatim: "query-side authorization/result filtering is applied *in addition to* tenant
        // scoping." The dev-story pass tested the two controls in isolation; this pins their composition.
        WhatsNextQueueProjection projection = new();

        // Tenant A: two eligible items (High before Normal at the same due date).
        QueueWith(projection, Work1, Priority.High, new DateOnly(2026, 7, 1));
        QueueWith(projection, Work2, Priority.Normal, new DateOnly(2026, 7, 1));

        // Tenant B: an eligible item with a *colliding* inner id.
        Deliver(projection, CreatedFor(OtherTenant, Work1, 1, new WorkItemSchedule(Priority.Critical)));
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, OtherTenant, Work1));

        // Control 1 — the projection's own tenant key-scoping.
        IReadOnlyList<WhatsNextItem> scoped = projection.WhatsNext(Tenant);
        scoped.Select(i => i.WorkItemId).ShouldBe([Work1, Work2]);
        scoped.ShouldAllBe(i => i.TenantId == Tenant);

        // Control 2 — the query-side authorization filter, applied *in addition*. Even if a foreign item
        // somehow reached the result, the authoritative-tenant check drops it and preserves survivor order.
        WhatsNextItem foreign = projection.WhatsNext(OtherTenant).ShouldHaveSingleItem();
        foreign.TenantId.ShouldBe(OtherTenant);
        IReadOnlyList<WhatsNextItem> tampered = [foreign, .. scoped];

        WhatsNextQueryAuthorization.FilterList(Tenant.Value, tampered)
            .ShouldBe(scoped);

        // The optional caller predicate (the seam a future IDomainQueryHandler fills from
        // QueryEnvelope.UserId) narrows further, independently of tenant scoping.
        WhatsNextQueryAuthorization
            .FilterList(Tenant.Value, tampered, item => item.WorkItemId == Work2)
            .ShouldHaveSingleItem().WorkItemId.ShouldBe(Work2);
    }

    [Fact]
    public void A_suspended_then_resumed_item_stays_out_of_the_pool_until_it_is_re_queued()
    {
        // WorkItemResumed had no projection coverage. Suspend/resume both leave the item InProgress-or-
        // Suspended (ineligible); resume is not terminal, so a later assignment legitimately re-admits it.
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1));
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        projection.WhatsNext(Tenant).ShouldHaveSingleItem();

        Deliver(projection, new WorkItemClaimed(Work1.Value, 3, Tenant, Work1, Binding));   // InProgress
        Deliver(projection, new WorkItemSuspended(Work1.Value, 4, Tenant, Work1, [AwaitCondition.ExternalSignal("await")]));
        projection.WhatsNext(Tenant).ShouldBeEmpty();

        Deliver(projection, new WorkItemResumed(Work1.Value, 5, Tenant, Work1));            // back to InProgress
        projection.WhatsNext(Tenant).ShouldBeEmpty();

        Deliver(projection, new WorkItemAssigned(Work1.Value, 6, Tenant, Work1, Binding));  // re-admitted
        WhatsNextItem back = projection.WhatsNext(Tenant).ShouldHaveSingleItem();
        back.Status.ShouldBe(WorkItemStatus.Assigned);
        back.AwaitConditions.ShouldBeEmpty();   // resume cleared the await conditions
    }

    [Fact]
    public void Re_estimate_establishes_then_updates_own_remaining_and_a_unit_mismatch_retains_it()
    {
        // ReEstimated own-remaining derivation was untested in this projection (only ProgressReported's
        // mismatch path was). Covers establish-from-nothing, matching re-estimate, and refuse-don't-coerce.
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1));   // no initial effort
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBeNull();

        Deliver(projection, new ReEstimated(Work1.Value, 3, Tenant, Work1, 7m, Hour));    // establishes
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBe(new OwnRemaining(7m, Hour));

        Deliver(projection, new ReEstimated(Work1.Value, 4, Tenant, Work1, 10m, Hour));   // re-estimates
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));

        Deliver(projection, new ReEstimated(Work1.Value, 5, Tenant, Work1, 99m, Point));  // unit mismatch -> retained
        projection.WhatsNext(Tenant).ShouldHaveSingleItem().OwnRemaining.ShouldBe(new OwnRemaining(10m, Hour));
    }

    [Fact]
    public void A_terminal_item_never_re_enters_the_pool_even_under_a_later_requeue_or_assign()
    {
        // AC #1/#5 safety invariant: a closed item must never reappear in the claimable pool. The terminal
        // guard absorbs later lifecycle facts (incl. a requeue rejection) and reports no queue change.
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1));
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        Deliver(projection, new WorkItemCompleted(Work1.Value, 3, Tenant, Work1));   // terminal
        projection.WhatsNext(Tenant).ShouldBeEmpty();

        Project(projection, new WorkItemRejected(Work1.Value, 4, Tenant, Work1, Requeue: true)).Changed.ShouldBeFalse();
        Project(projection, new WorkItemAssigned(Work1.Value, 5, Tenant, Work1, Binding)).Changed.ShouldBeFalse();
        projection.WhatsNext(Tenant).ShouldBeEmpty();
    }

    [Fact]
    public void Project_reports_a_change_when_an_eligible_item_is_rescheduled_to_a_new_due_date()
    {
        // AC #4 "change ... ordering" along the due-date dimension (the priority dimension was covered).
        WhatsNextQueueProjection projection = new();

        Deliver(projection, CreatedFor(Tenant, Work1, 1, new WorkItemSchedule(Priority.Normal, new DateOnly(2026, 9, 1))));
        Project(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1)).Changed.ShouldBeTrue();

        Project(projection, new WorkItemRescheduled(Work1.Value, 3, Tenant, Work1, new WorkItemSchedule(Priority.Normal, new DateOnly(2026, 7, 1))))
            .Changed.ShouldBeTrue();
    }

    [Fact]
    public void Project_reports_no_change_when_an_ineligible_item_is_rescheduled()
    {
        // AC #4 boundary: the change-signal tracks the *eligible* set/order only — rescheduling a Created
        // (ineligible) item must not signal a queue change.
        WhatsNextQueueProjection projection = new();

        Deliver(projection, CreatedFor(Tenant, Work1, 1, new WorkItemSchedule(Priority.Normal)));

        Project(projection, new WorkItemRescheduled(Work1.Value, 2, Tenant, Work1, new WorkItemSchedule(Priority.Critical, new DateOnly(2026, 1, 1))))
            .Changed.ShouldBeFalse();
        projection.WhatsNext(Tenant).ShouldBeEmpty();
    }

    [Fact]
    public void Latest_accepted_source_sequence_reflects_the_highest_accepted_sequence_under_out_of_order_delivery()
    {
        // AC #3 freshness watermark: surfaced on the read model and order-tolerant (max accepted sequence).
        WhatsNextQueueProjection projection = new();

        Deliver(projection, Created(Work1, 1, effort: 5m));
        Deliver(projection, new ReEstimated(Work1.Value, 4, Tenant, Work1, 8m, Hour));   // arrives before 2 and 3
        Deliver(projection, new WorkItemQueued(Work1.Value, 2, Tenant, Work1));
        Deliver(projection, new ProgressReported(Work1.Value, 3, Tenant, Work1, 1m, Hour));

        WhatsNextItem item = projection.WhatsNext(Tenant).ShouldHaveSingleItem();
        item.Status.ShouldBe(WorkItemStatus.Queued);
        item.LatestAcceptedSourceSequence.ShouldBe(4);
    }

    [Fact]
    public void Rolled_remaining_is_composed_per_item_only_where_a_roll_up_is_available()
    {
        // DC7 "where available" is *per item*, not all-or-nothing: a lookup that resolves one item leaves
        // the other's rolled remaining null/empty.
        WhatsNextQueueProjection projection = new();
        QueueWith(projection, Work1, Priority.High, null);
        QueueWith(projection, Work2, Priority.Normal, null);

        WorkItemRollUp rollUp = new(
            Tenant, Work1, WorkItemStatus.Queued, null,
            new OwnRemaining(2m, Hour), new RolledRemaining(9m, Hour),
            [new RolledRemaining(9m, Hour)], [], 0, 2);

        IReadOnlyList<WhatsNextItem> result =
            projection.WhatsNext(Tenant, (_, id) => id == Work1 ? rollUp : null);

        WhatsNextItem withRollUp = result.Single(i => i.WorkItemId == Work1);
        WhatsNextItem withoutRollUp = result.Single(i => i.WorkItemId == Work2);

        withRollUp.RolledRemaining.ShouldBe(new RolledRemaining(9m, Hour));
        withRollUp.RolledRemainingByUnit.ShouldBe([new RolledRemaining(9m, Hour)]);
        withoutRollUp.RolledRemaining.ShouldBeNull();
        withoutRollUp.RolledRemainingByUnit.ShouldBeEmpty();
    }

    [Fact]
    public void Authorization_filter_preserves_order_trims_the_tenant_and_rejects_a_null_result_set()
    {
        WhatsNextItem a = ReadModel(Tenant, new WorkItemId("a"));
        WhatsNextItem foreign = ReadModel(OtherTenant, new WorkItemId("b"));
        WhatsNextItem c = ReadModel(Tenant, new WorkItemId("c"));

        // Survivor order is preserved when the foreign middle item is dropped.
        WhatsNextQueryAuthorization.FilterList(Tenant.Value, [a, foreign, c]).ShouldBe([a, c]);

        // The authoritative tenant id is trimmed before the ordinal comparison.
        WhatsNextQueryAuthorization.FilterList($"  {Tenant.Value}  ", [a]).ShouldBe([a]);
        WhatsNextQueryAuthorization.Filter($"  {Tenant.Value}  ", a).ShouldBe(a);

        // A null result set is a programming error (fail-fast), distinct from the fail-closed empty-tenant path.
        Should.Throw<ArgumentNullException>(() => WhatsNextQueryAuthorization.FilterList(Tenant.Value, null!));
    }

    // ----- Helpers -----

    private static WorkItemCreated Created(WorkItemId workItemId, long sequence, decimal? effort = null)
        => CreatedFor(Tenant, workItemId, sequence, null, effort);

    private static WorkItemCreated CreatedFor(
        TenantId tenant,
        WorkItemId workItemId,
        long sequence,
        WorkItemSchedule? schedule = null,
        decimal? effort = null)
        => new(
            workItemId.Value,
            sequence,
            tenant,
            workItemId,
            new Obligation($"obligation-{workItemId.Value}"),
            effort is null ? null : new WorkItemEffort(effort.Value, Hour),
            schedule);

    private static WhatsNextItem ReadModel(TenantId tenant, WorkItemId workItemId)
        => new(tenant, workItemId, WorkItemStatus.Queued, null, null, null, null, null, [], [], 0);

    private static void QueueWith(WhatsNextQueueProjection projection, WorkItemId workItemId, Priority? priority, DateOnly? dueDate)
    {
        Deliver(projection, CreatedFor(Tenant, workItemId, 1, new WorkItemSchedule(priority, dueDate)));
        Deliver(projection, new WorkItemQueued(workItemId.Value, 2, Tenant, workItemId));
    }

    private static void Deliver(WhatsNextQueueProjection projection, IEventPayload payload)
        => _ = projection.Project(Envelope(payload));

    private static WhatsNextProjectionChange Project(WhatsNextQueueProjection projection, IEventPayload payload)
        => projection.Project(Envelope(payload));

    private static WhatsNextQueueProjection Replay(IEnumerable<WorkItemRollUpEvent> events)
    {
        WhatsNextQueueProjection projection = new();
        foreach (WorkItemRollUpEvent e in events)
        {
            _ = projection.Project(e);
        }

        return projection;
    }

    private static void SameQueue(WhatsNextQueueProjection left, WhatsNextQueueProjection right)
    {
        IReadOnlyList<WhatsNextItem> a = left.WhatsNext(Tenant);
        IReadOnlyList<WhatsNextItem> b = right.WhatsNext(Tenant);

        a.Select(i => i.WorkItemId).ShouldBe(b.Select(i => i.WorkItemId));
        a.Select(i => i.Status).ShouldBe(b.Select(i => i.Status));
        a.Select(i => i.Priority).ShouldBe(b.Select(i => i.Priority));
        a.Select(i => i.DueDate).ShouldBe(b.Select(i => i.DueDate));
        a.Select(i => i.OwnRemaining).ShouldBe(b.Select(i => i.OwnRemaining));
    }

    private static WorkItemRollUpEvent Envelope(IEventPayload payload)
        => payload switch
        {
            WorkItemCreated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemAssigned e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemQueued e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemClaimed e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemSuspended e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemResumed e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemRescheduled e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            ProgressReported e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            ReEstimated e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemCompleted e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemCancelled e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemExpired e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            WorkItemRejected e => new WorkItemRollUpEvent(e.TenantId, e.WorkItemId, e.Sequence, e),
            _ => throw new ArgumentOutOfRangeException(nameof(payload)),
        };
}
