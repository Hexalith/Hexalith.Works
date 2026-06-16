using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.Ports;
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
    public void CreateWorkItem_command_round_trips_with_reference_only_payload()
    {
        TenantId tenantId = new("tenant-alpha");
        var command = CreateCommand(
            tenantId: tenantId,
            initialEffort: new WorkItemEffort(5m, new Unit("point")),
            schedule: new WorkItemSchedule(Priority.Normal, new DateOnly(2026, 8, 20)),
            parent: new ParentWorkItemReference(tenantId, new WorkItemId("parent-001")),
            executorBinding: new ExecutorBinding(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Coordinate),
            conversationCorrelationId: new ConversationCorrelationId("conversation-456"));

        string json = JsonSerializer.Serialize(command, JsonOptions);
        json.ShouldContain("\"tenantId\"");
        json.ShouldContain("\"workItemId\"");
        json.ShouldContain("\"parent\"");
        json.ShouldContain("\"conversationCorrelationId\"");
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement executorBinding = document.RootElement.GetProperty("executorBinding");
            executorBinding.GetProperty("partyId").GetProperty("value").GetString().ShouldBe("party-123");
            executorBinding.GetProperty("channel").GetString().ShouldBe("Mcp");
        }

        json.ShouldNotContain("executorId");
        json.ShouldNotContain("partyName");
        json.ShouldNotContain("displayName");
        json.ShouldNotContain("emailAddress");
        json.ShouldNotContain("phoneNumber");
        json.ShouldNotContain("tenantName");
        json.ShouldNotContain("tenantProfile");
        json.ShouldNotContain("conversationTitle");
        json.ShouldNotContain("conversationMessage");
        json.ShouldNotContain("commentBody");
        json.ShouldNotContain("messageId");
        json.ShouldNotContain("causationId");
        json.ShouldNotContain("metadata");
        json.ShouldNotContain("UniqueIdHelper");
        json.ShouldNotContain("Guid");

        CreateWorkItem roundTripped = JsonSerializer.Deserialize<CreateWorkItem>(json, JsonOptions)
            .ShouldNotBeNull();

        roundTripped.TenantId.ShouldBe(command.TenantId);
        roundTripped.WorkItemId.ShouldBe(command.WorkItemId);
        roundTripped.Parent.ShouldBe(command.Parent);
        roundTripped.ExecutorBinding.ShouldBe(command.ExecutorBinding);
        roundTripped.ConversationCorrelationId.ShouldBe(command.ConversationCorrelationId);
    }

    [Fact]
    public void CreateWorkItem_with_optional_coordination_facts_round_trips_without_copying_sibling_data()
    {
        var effort = new WorkItemEffort(13m, new Unit("point"));
        var schedule = new WorkItemSchedule(Priority.High, new DateOnly(2026, 7, 15));
        TenantId tenantId = new("tenant-alpha");
        var parent = new ParentWorkItemReference(tenantId, new WorkItemId("parent-001"));
        var executor = new ExecutorBinding(new PartyId("party-123"), Channel.Email, AuthorityLevel.Contribute);
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

        string json = JsonSerializer.Serialize(created, JsonOptions);
        json.ShouldContain("\"partyId\"");
        json.ShouldContain("\"party-123\"");
        json.ShouldContain("\"channel\":\"Email\"");
        json.ShouldNotContain("executorId");
        json.ShouldNotContain("partyName");
        json.ShouldNotContain("displayName");
        json.ShouldNotContain("emailAddress");
        json.ShouldNotContain("phoneNumber");
        json.ShouldNotContain("tenantName");
        json.ShouldNotContain("tenantProfile");
        json.ShouldNotContain("conversationTitle");
        json.ShouldNotContain("conversationMessage");
        json.ShouldNotContain("commentBody");
        json.ShouldNotContain("messageId");
        json.ShouldNotContain("causationId");
        json.ShouldNotContain("metadata");
        json.ShouldNotContain("UniqueIdHelper");
        json.ShouldNotContain("Guid");

        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions)
            .ShouldNotBeNull();
        var state = new WorkItemState();
        state.Apply(roundTripped);

        state.InitialEffort.ShouldBe(effort);
        state.Remaining.ShouldBe(13m);
        state.Schedule.ShouldBe(schedule);
        state.Parent.ShouldBe(parent);
        state.Parent.ShouldNotBeNull().TenantId.ShouldBe(tenantId);
        state.ExecutorBinding.ShouldBe(executor);
        state.ExecutorBinding.ShouldNotBeNull().PartyId.ShouldBe(new PartyId("party-123"));
        state.ExecutorBinding.ShouldNotBeNull().Channel.ShouldBe(Channel.Email);
        state.ConversationCorrelationId.ShouldBe(conversation);
    }

    [Fact]
    public void CreateWorkItem_without_conversation_correlation_id_round_trips_without_comment_storage()
    {
        WorkItemCreated created = WorkItemAggregate.Handle(CreateCommand(conversationCorrelationId: null), null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        string json = JsonSerializer.Serialize(created, JsonOptions);
        json.ShouldNotContain("conversationMessages");
        json.ShouldNotContain("comments");
        json.ShouldNotContain("commentStore");

        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions)
            .ShouldNotBeNull();
        var state = new WorkItemState();
        state.Apply(roundTripped);

        roundTripped.ConversationCorrelationId.ShouldBeNull();
        state.ConversationCorrelationId.ShouldBeNull();
        state.Status.ShouldBe(WorkItemStatus.Created);
    }

    [Fact]
    public void CreateWorkItem_with_cross_tenant_parent_reference_serializes_rejection_only()
    {
        CreateWorkItem command = CreateCommand(
            tenantId: new TenantId("tenant-alpha"),
            parent: new ParentWorkItemReference(new TenantId("tenant-beta"), new WorkItemId("parent-001")));

        var result = WorkItemAggregate.Handle(command, null);

        result.IsRejection.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        WorkItemCannotReferenceParentFromAnotherTenant rejection = result.Events
            .Single()
            .ShouldBeOfType<WorkItemCannotReferenceParentFromAnotherTenant>();

        string json = JsonSerializer.Serialize(rejection, JsonOptions);
        WorkItemCannotReferenceParentFromAnotherTenant roundTripped =
            JsonSerializer.Deserialize<WorkItemCannotReferenceParentFromAnotherTenant>(json, JsonOptions)
                .ShouldNotBeNull();

        roundTripped.TenantId.ShouldBe(command.TenantId);
        roundTripped.WorkItemId.ShouldBe(command.WorkItemId);
        roundTripped.Parent.TenantId.ShouldBe(new TenantId("tenant-beta"));
        roundTripped.Parent.WorkItemId.ShouldBe(new WorkItemId("parent-001"));
    }

    [Fact]
    public void CreateWorkItem_with_invalid_tree_shape_serializes_specific_rejection_payloads_without_envelope()
    {
        TenantId tenantId = new("tenant-alpha");
        ParentWorkItemReference existingParent = new(tenantId, new WorkItemId("parent-001"));
        ParentWorkItemReference proposedParent = new(tenantId, new WorkItemId("parent-002"));
        IEventPayload[] rejections =
        [
            new WorkItemCannotReferenceSecondParent(tenantId, new WorkItemId("work-001"), existingParent, proposedParent),
            new WorkItemTreeCycleRejected(tenantId, new WorkItemId("work-001"), proposedParent, new WorkItemId("work-001")),
            new WorkItemTreeDepthExceeded(tenantId, new WorkItemId("work-001"), proposedParent, 32, 33),
        ];

        foreach (IEventPayload rejection in rejections)
        {
            string json = JsonSerializer.Serialize(rejection, rejection.GetType(), JsonOptions);
            using JsonDocument document = JsonDocument.Parse(json);

            foreach (string envelopeField in WorkItemV1Catalog.EnvelopeFields)
            {
                document.RootElement.TryGetProperty(envelopeField, out _)
                    .ShouldBeFalse($"{rejection.GetType().Name} must not embed EventStore envelope metadata.");
            }

            IEventPayload roundTripped = (IEventPayload)JsonSerializer.Deserialize(json, rejection.GetType(), JsonOptions)
                .ShouldNotBeNull();
            roundTripped.ShouldBe(rejection);
        }
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

    [Fact]
    public void CreateWorkItem_trims_padded_obligation_on_replay()
    {
        WorkItemCreated created = WorkItemAggregate.Handle(CreateCommand(obligation: "  Prepare the padded obligation  "), null)
            .Events
            .Single()
            .ShouldBeOfType<WorkItemCreated>();

        string json = JsonSerializer.Serialize(created, JsonOptions);
        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions)
            .ShouldNotBeNull();
        var state = new WorkItemState();
        state.Apply(roundTripped);

        state.Obligation.ShouldNotBeNull().Description.ShouldBe("Prepare the padded obligation");
    }

    [Fact]
    public void WorkItemCreated_carries_expectation_reference_and_never_an_interpreted_expectation()
    {
        var reference = new ExpectationReference("expectation-ref-001");
        var created = new WorkItemCreated(
            "work-001",
            1,
            new TenantId("tenant-alpha"),
            new WorkItemId("work-001"),
            new Obligation("Prepare the first tenant-scoped work item", reference));

        string json = JsonSerializer.Serialize(created, JsonOptions);

        // The stable expectation reference is carried on the obligation...
        json.ShouldContain("\"reference\"");
        json.ShouldContain("\"value\":\"expectation-ref-001\"");

        // ...but the interpreted-on-demand Expectation (InterpretedValue) is never serialized into the event.
        json.ShouldNotContain("interpretedValue");

        WorkItemCreated roundTripped = JsonSerializer.Deserialize<WorkItemCreated>(json, JsonOptions)
            .ShouldNotBeNull();
        var state = new WorkItemState();
        state.Apply(roundTripped);

        Obligation obligation = state.Obligation.ShouldNotBeNull();
        obligation.Reference.ShouldBe(reference);
        obligation.Reference.ShouldNotBeNull().Value.ShouldBe("expectation-ref-001");
    }

    [Fact]
    public void Obligation_payload_without_expectation_reference_field_deserializes_as_reference_only()
    {
        // A WorkItemCreated/Obligation payload written before the optional expectation reference
        // existed must still deserialize: the additive, nullable field reconstructs as a null reference.
        const string legacyObligationJson = "{\"description\":\"Prepare the first tenant-scoped work item\"}";

        Obligation obligation = JsonSerializer.Deserialize<Obligation>(legacyObligationJson, JsonOptions)
            .ShouldNotBeNull();

        obligation.Description.ShouldBe("Prepare the first tenant-scoped work item");
        obligation.Reference.ShouldBeNull();
    }

    private static CreateWorkItem CreateCommand(
        TenantId? tenantId = null,
        WorkItemEffort? initialEffort = null,
        WorkItemSchedule? schedule = null,
        ParentWorkItemReference? parent = null,
        ExecutorBinding? executorBinding = null,
        ConversationCorrelationId? conversationCorrelationId = null,
        string? obligation = "Prepare the first tenant-scoped work item")
        => new(
            tenantId ?? new TenantId("tenant-alpha"),
            new WorkItemId("work-001"),
            obligation,
            initialEffort,
            schedule,
            parent,
            executorBinding,
            conversationCorrelationId);
}
