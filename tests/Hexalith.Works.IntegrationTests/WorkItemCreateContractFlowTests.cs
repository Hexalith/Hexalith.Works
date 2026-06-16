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

public sealed class WorkItemCreateContractFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateWorkItem_contract_round_trip_replays_to_created_tenant_scoped_state()
    {
        CreateWorkItem command = CreateCommand();

        WorkItemCreated created = WorkItemAggregate.Handle(command, null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        string json = JsonSerializer.Serialize(created, JsonOptions);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        root.TryGetProperty("messageId", out _).ShouldBeFalse();
        root.TryGetProperty("causationId", out _).ShouldBeFalse();
        root.TryGetProperty("userId", out _).ShouldBeFalse();
        root.TryGetProperty("metadata", out _).ShouldBeFalse();
        root.TryGetProperty("cloudEvent", out _).ShouldBeFalse();

        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions)
            .ShouldNotBeNull();

        var state = new WorkItemState();
        state.Apply(roundTripped);

        state.Status.ShouldBe(WorkItemStatus.Created);
        state.AggregateIdentity.ShouldNotBeNull().ToString().ShouldBe("tenant-alpha:work:work-001");
        state.Obligation.ShouldNotBeNull().Description.ShouldBe("Prepare the first tenant-scoped work item");
    }

    [Fact]
    public void CreateWorkItem_with_optional_coordination_facts_round_trips_without_copying_sibling_data()
    {
        var effort = new WorkItemEffort(13m, new Unit("point"));
        var schedule = new WorkItemSchedule(Priority.High, new DateOnly(2026, 7, 15));
        var parent = new ParentWorkItemReference(new WorkItemId("parent-001"));
        var executor = new ExecutorBinding("party-123", AuthorityLevel.Contribute);
        var conversation = new ConversationCorrelationId("conversation-456");
        CreateWorkItem command = CreateCommand(
            initialEffort: effort,
            schedule: schedule,
            parent: parent,
            executorBinding: executor,
            conversationCorrelationId: conversation);

        WorkItemCreated created = WorkItemAggregate.Handle(command, null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        string json = JsonSerializer.Serialize(created, JsonOptions);
        json.ShouldContain("\"executorId\":\"party-123\"");
        json.ShouldNotContain("partyName");
        json.ShouldNotContain("tenantName");
        json.ShouldNotContain("conversationTitle");

        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions)
            .ShouldNotBeNull();
        var state = new WorkItemState();
        state.Apply(roundTripped);

        state.InitialEffort.ShouldBe(effort);
        state.Remaining.ShouldBe(13m);
        state.Schedule.ShouldBe(schedule);
        state.Parent.ShouldBe(parent);
        state.ExecutorBinding.ShouldBe(executor);
        state.ConversationCorrelationId.ShouldBe(conversation);
    }

    [Fact]
    public void CreateWorkItem_without_estimate_round_trips_without_materializing_remaining()
    {
        WorkItemCreated created = WorkItemAggregate.Handle(CreateCommand(initialEffort: null), null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        string json = JsonSerializer.Serialize(created, JsonOptions);
        json.ShouldNotContain("remaining");

        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions)
            .ShouldNotBeNull();
        var state = new WorkItemState();
        state.Apply(roundTripped);

        state.InitialEffort.ShouldBeNull();
        state.Remaining.ShouldBeNull();
        state.IsCompletedByRemaining.ShouldBeFalse();
        state.Status.ShouldBe(WorkItemStatus.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWorkItem_without_obligation_returns_serializable_rejection_only(string? obligation)
    {
        CreateWorkItem command = CreateCommand(obligation: obligation);

        var result = WorkItemAggregate.Handle(command, null);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Events.Count.ShouldBe(1);
        result.Events.Single().ShouldBeAssignableTo<IRejectionEvent>();
        WorkItemCannotBeCreatedWithoutObligation rejection = result.Events
            .Single()
            .ShouldBeOfType<WorkItemCannotBeCreatedWithoutObligation>();

        string json = JsonSerializer.Serialize(rejection, JsonOptions);
        WorkItemCannotBeCreatedWithoutObligation roundTripped =
            JsonSerializer.Deserialize<WorkItemCannotBeCreatedWithoutObligation>(json, JsonOptions)
                .ShouldNotBeNull();

        var state = new WorkItemState();
        state.Apply(roundTripped);

        roundTripped.TenantId.ShouldBe(command.TenantId);
        roundTripped.WorkItemId.ShouldBe(command.WorkItemId);
        state.Status.ShouldBe(WorkItemStatus.Unknown);
        state.AggregateIdentity.ShouldBeNull();
    }

    private static CreateWorkItem CreateCommand(
        WorkItemEffort? initialEffort = null,
        WorkItemSchedule? schedule = null,
        ParentWorkItemReference? parent = null,
        ExecutorBinding? executorBinding = null,
        ConversationCorrelationId? conversationCorrelationId = null,
        string? obligation = "Prepare the first tenant-scoped work item")
        => new(
            new TenantId("tenant-alpha"),
            new WorkItemId("work-001"),
            obligation,
            initialEffort,
            schedule,
            parent,
            executorBinding,
            conversationCorrelationId);
}
