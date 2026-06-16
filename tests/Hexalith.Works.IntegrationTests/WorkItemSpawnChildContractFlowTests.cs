using System.Text.Json;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorkItemSpawnChildContractFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Parent = new("parent-001");
    private static readonly WorkItemId Child = new("child-001");

    [Fact]
    public void SpawnChild_command_round_trips_with_caller_fed_tree_facts_and_child_create_shape()
    {
        var command = new SpawnChild(
            Tenant,
            Parent,
            Child,
            "Break out child work",
            new WorkItemEffort(5m, new Unit("point")),
            new WorkItemSchedule(Priority.Normal, new DateOnly(2026, 8, 20)),
            new ExecutorBinding(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Coordinate),
            new ConversationCorrelationId("conversation-456"),
            SuspendParentUntilChildCompletes: true,
            ProposedParentAncestors: [new ParentWorkItemReference(Tenant, new WorkItemId("root-001"))],
            ProposedParentDepth: 2,
            MaxDepth: 32,
            ExistingChildParent: new ParentWorkItemReference(Tenant, Parent));

        string json = JsonSerializer.Serialize(command, JsonOptions);
        json.ShouldContain("\"childWorkItemId\"");
        json.ShouldContain("\"suspendParentUntilChildCompletes\":true");
        json.ShouldContain("\"proposedParentAncestors\"");
        json.ShouldContain("\"proposedParentDepth\":2");
        json.ShouldContain("\"maxDepth\":32");
        json.ShouldContain("\"existingChildParent\"");
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (string envelopeField in WorkItemV1Catalog.EnvelopeFields)
        {
            document.RootElement.TryGetProperty(envelopeField, out _)
                .ShouldBeFalse($"{nameof(SpawnChild)} must not embed EventStore envelope metadata.");
        }
        json.ShouldNotContain("Guid");
        json.ShouldNotContain("UniqueIdHelper");

        SpawnChild roundTripped = JsonSerializer.Deserialize<SpawnChild>(json, JsonOptions).ShouldNotBeNull();

        roundTripped.TenantId.ShouldBe(command.TenantId);
        roundTripped.WorkItemId.ShouldBe(command.WorkItemId);
        roundTripped.ChildWorkItemId.ShouldBe(command.ChildWorkItemId);
        roundTripped.Obligation.ShouldBe(command.Obligation);
        roundTripped.InitialEffort.ShouldBe(command.InitialEffort);
        roundTripped.Schedule.ShouldBe(command.Schedule);
        roundTripped.ExecutorBinding.ShouldBe(command.ExecutorBinding);
        roundTripped.ConversationCorrelationId.ShouldBe(command.ConversationCorrelationId);
        roundTripped.SuspendParentUntilChildCompletes.ShouldBeTrue();
        roundTripped.ProposedParentAncestors.ShouldBe(command.ProposedParentAncestors);
        roundTripped.ProposedParentDepth.ShouldBe(command.ProposedParentDepth);
        roundTripped.MaxDepth.ShouldBe(command.MaxDepth);
        roundTripped.ExistingChildParent.ShouldBe(command.ExistingChildParent);
    }

    [Fact]
    public void ChildSpawned_round_trips_without_envelope_and_replays_parent_child_reference()
    {
        WorkItemState state = CreatedParent();
        ChildSpawned spawned = WorkItemAggregate.Handle(new SpawnChild(Tenant, Parent, Child, "Break out child work"), state)
            .Events
            .Single()
            .ShouldBeOfType<ChildSpawned>();

        string json = JsonSerializer.Serialize(spawned, JsonOptions);
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (string envelopeField in WorkItemV1Catalog.EnvelopeFields)
        {
            document.RootElement.TryGetProperty(envelopeField, out _)
                .ShouldBeFalse($"{nameof(ChildSpawned)} must not embed EventStore envelope metadata.");
        }

        ChildSpawned roundTripped = JsonSerializer.Deserialize<ChildSpawned>(json, JsonOptions).ShouldNotBeNull();
        var replay = new WorkItemState();
        replay.Apply(new WorkItemCreated(Parent.Value, 1, Tenant, Parent, new Obligation("Parent work")));
        replay.Apply(roundTripped);

        replay.SpawnedChildWorkItemIds.ShouldBe([Child]);
        replay.Status.ShouldBe(WorkItemStatus.Created);
        replay.Sequence.ShouldBe(roundTripped.Sequence);
    }

    [Fact]
    public void SpawnChild_with_await_round_trips_child_completion_condition_on_suspension()
    {
        WorkItemState state = InProgressParent();
        WorkItemSuspended suspended = WorkItemAggregate.Handle(
                new SpawnChild(Tenant, Parent, Child, "Break out child work", SuspendParentUntilChildCompletes: true),
                state)
            .Events[1]
            .ShouldBeOfType<WorkItemSuspended>();

        string json = JsonSerializer.Serialize(suspended, JsonOptions);
        json.ShouldContain("\"awaitConditions\"");
        json.ShouldContain("\"childWorkItemId\"");
        foreach (string envelopeField in WorkItemV1Catalog.EnvelopeFields)
        {
            json.ShouldNotContain(envelopeField);
        }

        WorkItemSuspended roundTripped = JsonSerializer.Deserialize<WorkItemSuspended>(json, JsonOptions).ShouldNotBeNull();
        roundTripped.AwaitConditions.ShouldBe([new AwaitCondition(Child)]);

        state.Apply(roundTripped);
        state.Status.ShouldBe(WorkItemStatus.Suspended);
        state.AwaitConditions.ShouldBe([new AwaitCondition(Child)]);
    }

    [Fact]
    public void WorkItemSuspended_legacy_payload_without_await_condition_deserializes_as_null()
    {
        const string legacyJson = """
            {
              "aggregateId": "parent-001",
              "sequence": 5,
              "tenantId": { "value": "tenant-alpha" },
              "workItemId": { "value": "parent-001" }
            }
            """;

        WorkItemSuspended suspended = JsonSerializer.Deserialize<WorkItemSuspended>(legacyJson, JsonOptions)
            .ShouldNotBeNull();

        suspended.AwaitCondition.ShouldBeNull();
        suspended.AwaitConditions.ShouldBeEmpty();
    }

    [Fact]
    public void WorkItemSuspended_legacy_single_await_condition_payload_deserializes_to_condition_set()
    {
        const string legacyJson = """
            {
              "aggregateId": "parent-001",
              "sequence": 5,
              "tenantId": { "value": "tenant-alpha" },
              "workItemId": { "value": "parent-001" },
              "awaitCondition": {
                "childWorkItemId": { "value": "child-001" }
              }
            }
            """;

        WorkItemSuspended suspended = JsonSerializer.Deserialize<WorkItemSuspended>(legacyJson, JsonOptions)
            .ShouldNotBeNull();

        suspended.AwaitConditions.ShouldBe([new AwaitCondition(Child)]);
    }

    private static WorkItemState CreatedParent()
    {
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(Parent.Value, 1, Tenant, Parent, new Obligation("Parent work")));
        return state;
    }

    private static WorkItemState InProgressParent()
    {
        var state = CreatedParent();
        var binding = new ExecutorBinding(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Coordinate);
        state.Apply(new WorkItemAssigned(Parent.Value, 2, Tenant, Parent, binding));
        state.Apply(new WorkItemClaimed(Parent.Value, 3, Tenant, Parent, binding));
        return state;
    }
}
