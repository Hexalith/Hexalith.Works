using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Identity;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkItemCreateTests
{
    [Fact]
    public void CreateWorkItem_with_no_prior_state_produces_created_event_and_replayable_state()
    {
        CreateWorkItem command = CreateCommand();

        var result = WorkItemAggregate.Handle(command, null);

        result.IsSuccess.ShouldBeTrue();
        result.Events.Count.ShouldBe(1);
        WorkItemCreated created = result.Events.Single().ShouldBeOfType<WorkItemCreated>();
        created.AggregateId.ShouldBe(command.WorkItemId.Value);
        created.Sequence.ShouldBe(1);
        created.TenantId.ShouldBe(command.TenantId);
        created.WorkItemId.ShouldBe(command.WorkItemId);
        created.Obligation.Description.ShouldBe("Prepare the first tenant-scoped work item");

        var state = new WorkItemState();
        state.Apply(created);

        state.Status.ShouldBe(WorkItemStatus.Created);
        AggregateIdentity identity = state.AggregateIdentity.ShouldNotBeNull();
        identity.ToString().ShouldBe("tenant-alpha:work:work-001");
        identity.ShouldBe(new AggregateIdentity("tenant-alpha", "work", "work-001"));
    }

    [Fact]
    public void CreateWorkItem_uses_edge_supplied_work_item_id_without_generating_another_identity()
    {
        CreateWorkItem command = CreateCommand(workItemId: new WorkItemId("work-edge-042"));

        WorkItemCreated created = WorkItemAggregate.Handle(command, null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        created.AggregateId.ShouldBe("work-edge-042");
        created.WorkItemId.ShouldBe(command.WorkItemId);

        var state = new WorkItemState();
        state.Apply(created);

        state.WorkItemId.ShouldBe(command.WorkItemId);
        state.AggregateIdentity.ShouldNotBeNull().ShouldBe(new AggregateIdentity("tenant-alpha", "work", "work-edge-042"));
    }

    [Fact]
    public void CreateWorkItem_with_optional_coordination_facts_preserves_only_supplied_references()
    {
        TenantId tenantId = new("tenant-alpha");
        var effort = new WorkItemEffort(8, new Unit("hour"));
        var schedule = new WorkItemSchedule(Priority.High, new DateOnly(2026, 7, 15));
        var parent = new ParentWorkItemReference(tenantId, new WorkItemId("parent-001"));
        var executor = new ExecutorBinding(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Administer);
        var conversation = new ConversationCorrelationId("conversation-456");
        CreateWorkItem command = CreateCommand(
            tenantId: tenantId,
            initialEffort: effort,
            schedule: schedule,
            parent: parent,
            executorBinding: executor,
            conversationCorrelationId: conversation);

        WorkItemCreated created = WorkItemAggregate.Handle(command, null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        created.InitialEffort.ShouldBe(effort);
        created.Schedule.ShouldBe(schedule);
        created.Parent.ShouldBe(parent);
        created.Parent.ShouldNotBeNull().TenantId.ShouldBe(tenantId);
        created.ExecutorBinding.ShouldBe(executor);
        created.ExecutorBinding.ShouldNotBeNull().PartyId.ShouldBe(new PartyId("party-123"));
        created.ExecutorBinding.ShouldNotBeNull().Channel.ShouldBe(Channel.Mcp);
        created.ConversationCorrelationId.ShouldBe(conversation);

        var state = new WorkItemState();
        state.Apply(created);

        state.InitialEffort.ShouldBe(effort);
        state.Remaining.ShouldBe(8);
        state.Schedule.ShouldBe(schedule);
        state.Parent.ShouldBe(parent);
        state.Parent.ShouldNotBeNull().TenantId.ShouldBe(tenantId);
        state.ExecutorBinding.ShouldBe(executor);
        state.ExecutorBinding.ShouldNotBeNull().PartyId.ShouldBe(new PartyId("party-123"));
        state.ExecutorBinding.ShouldNotBeNull().Channel.ShouldBe(Channel.Mcp);
        state.ConversationCorrelationId.ShouldBe(conversation);
    }

    [Fact]
    public void CreateWorkItem_without_conversation_correlation_id_remains_valid()
    {
        WorkItemCreated created = WorkItemAggregate.Handle(CreateCommand(conversationCorrelationId: null), null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        var state = new WorkItemState();
        state.Apply(created);

        created.ConversationCorrelationId.ShouldBeNull();
        state.ConversationCorrelationId.ShouldBeNull();
        state.Status.ShouldBe(WorkItemStatus.Created);
    }

    [Fact]
    public void CreateWorkItem_with_cross_tenant_parent_reference_returns_rejection()
    {
        CreateWorkItem command = CreateCommand(
            tenantId: new TenantId("tenant-alpha"),
            parent: new ParentWorkItemReference(new TenantId("tenant-beta"), new WorkItemId("parent-001")));

        var result = WorkItemAggregate.Handle(command, null);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Count.ShouldBe(1);
        WorkItemCannotReferenceParentFromAnotherTenant rejection = result.Events
            .Single()
            .ShouldBeOfType<WorkItemCannotReferenceParentFromAnotherTenant>();

        rejection.TenantId.ShouldBe(command.TenantId);
        rejection.WorkItemId.ShouldBe(command.WorkItemId);
        rejection.Parent.ShouldBe(command.Parent);

        var state = new WorkItemState();
        state.Apply(rejection);

        state.Status.ShouldBe(WorkItemStatus.Unknown);
        state.AggregateIdentity.ShouldBeNull();
    }

    [Fact]
    public void WorkItemCreated_replay_preserves_foreign_parent_tenant_as_foreign_reference()
    {
        TenantId workTenant = new("tenant-alpha");
        var foreignParent = new ParentWorkItemReference(new TenantId("tenant-beta"), new WorkItemId("parent-001"));
        var created = new WorkItemCreated(
            "work-001",
            1,
            workTenant,
            new WorkItemId("work-001"),
            new Obligation("Replay a reference-only work item"),
            Parent: foreignParent);

        var state = new WorkItemState();
        state.Apply(created);

        state.TenantId.ShouldBe(workTenant);
        state.Parent.ShouldBe(foreignParent);
        state.Parent.ShouldNotBeNull().TenantId.ShouldBe(new TenantId("tenant-beta"));
        state.Parent.TenantId.ShouldNotBe(state.TenantId);
        state.AggregateIdentity.ShouldNotBeNull().ShouldBe(new AggregateIdentity("tenant-alpha", "work", "work-001"));
    }

    [Fact]
    public void CreateWorkItem_accepts_same_tenant_parent_supplied_with_different_casing()
    {
        // Both tenant ids normalize (lowercase) through AggregateIdentity, so a casing-divergent but
        // equivalent parent tenant must be treated as same-tenant and not rejected.
        CreateWorkItem command = CreateCommand(
            tenantId: new TenantId("tenant-alpha"),
            parent: new ParentWorkItemReference(new TenantId("Tenant-Alpha"), new WorkItemId("parent-001")));

        var result = WorkItemAggregate.Handle(command, null);

        result.IsSuccess.ShouldBeTrue();
        result.Events.Single().ShouldBeOfType<WorkItemCreated>();
    }

    [Fact]
    public void CreateWorkItem_missing_obligation_takes_precedence_over_cross_tenant_parent()
    {
        CreateWorkItem command = CreateCommand(
            obligation: null,
            tenantId: new TenantId("tenant-alpha"),
            parent: new ParentWorkItemReference(new TenantId("tenant-beta"), new WorkItemId("parent-001")));

        var result = WorkItemAggregate.Handle(command, null);

        result.IsRejection.ShouldBeTrue();
        result.Events.Count.ShouldBe(1);
        result.Events.Single().ShouldBeOfType<WorkItemCannotBeCreatedWithoutObligation>();
    }

    [Fact]
    public void CreateWorkItem_with_mixed_optional_references_replays_each_field_exactly()
    {
        TenantId tenantId = new("tenant-alpha");
        var parent = new ParentWorkItemReference(tenantId, new WorkItemId("parent-001"));
        var conversation = new ConversationCorrelationId("conversation-456");
        CreateWorkItem command = CreateCommand(
            tenantId: tenantId,
            initialEffort: null,
            schedule: null,
            parent: parent,
            executorBinding: null,
            conversationCorrelationId: conversation);

        WorkItemCreated created = WorkItemAggregate.Handle(command, null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        var state = new WorkItemState();
        state.Apply(created);

        state.Parent.ShouldBe(parent);
        state.ConversationCorrelationId.ShouldBe(conversation);
        state.InitialEffort.ShouldBeNull();
        state.Schedule.ShouldBeNull();
        state.ExecutorBinding.ShouldBeNull();
    }

    [Fact]
    public void CreateWorkItem_without_estimate_keeps_remaining_undefined_and_status_created()
    {
        WorkItemCreated created = WorkItemAggregate.Handle(CreateCommand(initialEffort: null), null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        var state = new WorkItemState();
        state.Apply(created);

        state.InitialEffort.ShouldBeNull();
        state.Remaining.ShouldBeNull();
        state.IsCompletedByRemaining.ShouldBeFalse();
        state.Status.ShouldBe(WorkItemStatus.Created);
    }

    [Fact]
    public void CreateWorkItem_initial_effort_starts_with_no_done_progress()
    {
        WorkItemEffort suppliedEffort = new(8m, new Unit("hour"), 3m);
        WorkItemCreated created = WorkItemAggregate.Handle(CreateCommand(initialEffort: suppliedEffort), null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        created.InitialEffort.ShouldNotBeNull().Done.ShouldBe(0m);
        created.InitialEffort.Remaining.ShouldBe(8m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWorkItem_without_obligation_returns_rejection_only_and_does_not_mutate_state(string? obligation)
    {
        CreateWorkItem command = CreateCommand(obligation: obligation);

        var result = WorkItemAggregate.Handle(command, null);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Count.ShouldBe(1);
        result.Events.Single().ShouldBeAssignableTo<IRejectionEvent>();
        WorkItemCannotBeCreatedWithoutObligation rejection = result.Events.Single().ShouldBeOfType<WorkItemCannotBeCreatedWithoutObligation>();
        rejection.TenantId.ShouldBe(command.TenantId);
        rejection.WorkItemId.ShouldBe(command.WorkItemId);

        var state = new WorkItemState();
        state.Apply(rejection);

        state.Status.ShouldBe(WorkItemStatus.Unknown);
        state.AggregateIdentity.ShouldBeNull();
    }

    private static CreateWorkItem CreateCommand(
        TenantId? tenantId = null,
        WorkItemId? workItemId = null,
        string? obligation = "Prepare the first tenant-scoped work item",
        WorkItemEffort? initialEffort = null,
        WorkItemSchedule? schedule = null,
        ParentWorkItemReference? parent = null,
        ExecutorBinding? executorBinding = null,
        ConversationCorrelationId? conversationCorrelationId = null)
        => new(
            tenantId ?? new TenantId("tenant-alpha"),
            workItemId ?? new WorkItemId("work-001"),
            obligation,
            initialEffort,
            schedule,
            parent,
            executorBinding,
            conversationCorrelationId);
}
