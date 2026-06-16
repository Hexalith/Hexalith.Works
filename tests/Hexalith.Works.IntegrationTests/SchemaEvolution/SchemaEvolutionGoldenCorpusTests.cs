using System.IO;
using System.Text.Json;

using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Shouldly;

namespace Hexalith.Works.IntegrationTests.SchemaEvolution;

/// <summary>
/// RR-6 / NFR-12 schema-evolution golden-corpus tests (AC #5): the frozen, concrete-type
/// (<see cref="JsonSerializerDefaults.Web"/>) serialized samples of Works v1 events deserialize from
/// the checked-in bytes via the production <see cref="System.Text.Json"/> path and round-trip — proving
/// additive, backward-compatible deserialization with no <c>V2</c> event types. Injecting an unknown
/// field proves additive tolerance. These freeze the EventStore-persisted form (no <c>$type</c>); the
/// polymorphic-resolution capability is proven separately in
/// <see cref="WorkItemSerializationRegistrationTests"/>. Line endings are normalized before reading.
/// Pure Tier-1 (reads copied-to-output fixtures only).
/// </summary>
public sealed class SchemaEvolutionGoldenCorpusTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly string GoldenDirectory =
        Path.Combine(AppContext.BaseDirectory, "SchemaEvolution", "Golden");

    [Fact]
    public void WorkItemCreated_DeserializesFromFrozenBytesAndRoundTrips()
    {
        string frozen = ReadGolden("WorkItemCreated.v1.json");

        WorkItemCreated deserialized = JsonSerializer.Deserialize<WorkItemCreated>(frozen, Options).ShouldNotBeNull();

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(1);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");
        deserialized.Obligation.Description.ShouldBe("Prepare the first tenant-scoped work item");
        deserialized.Obligation.Reference.ShouldNotBeNull().Value.ShouldBe("expectation-ref-001");
        deserialized.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(13m);
        deserialized.InitialEffort.ShouldNotBeNull().Unit.Value.ShouldBe("point");
        deserialized.InitialEffort.ShouldNotBeNull().Remaining.ShouldBe(13m);
        deserialized.Schedule.ShouldNotBeNull().Priority.ShouldBe(Priority.High);
        deserialized.Schedule.ShouldNotBeNull().DueDate.ShouldBe(new DateOnly(2026, 7, 15));
        deserialized.Parent.ShouldNotBeNull().TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.Parent.ShouldNotBeNull().WorkItemId.Value.ShouldBe("parent-001");
        deserialized.ExecutorBinding.ShouldNotBeNull().PartyId.Value.ShouldBe("party-123");
        deserialized.ExecutorBinding.ShouldNotBeNull().Channel.ShouldBe(Channel.Mcp);
        deserialized.ExecutorBinding.ShouldNotBeNull().AuthorityLevel.ShouldBe(AuthorityLevel.Coordinate);
        deserialized.ConversationCorrelationId.ShouldNotBeNull().Value.ShouldBe("conversation-456");

        // Round-trip: re-serializing then deserializing yields an equal event.
        string reserialized = JsonSerializer.Serialize(deserialized, Options);
        JsonSerializer.Deserialize<WorkItemCreated>(reserialized, Options).ShouldBe(deserialized);
    }

    [Fact]
    public void WorkItemAssigned_DeserializesFromFrozenBytesAndRoundTrips()
    {
        string frozen = ReadGolden("WorkItemAssigned.v1.json");

        WorkItemAssigned deserialized = JsonSerializer.Deserialize<WorkItemAssigned>(frozen, Options).ShouldNotBeNull();

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(2);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");
        deserialized.Binding.PartyId.Value.ShouldBe("party-123");
        deserialized.Binding.Channel.ShouldBe(Channel.Mcp);
        deserialized.Binding.AuthorityLevel.ShouldBe(AuthorityLevel.Coordinate);

        string reserialized = JsonSerializer.Serialize(deserialized, Options);
        JsonSerializer.Deserialize<WorkItemAssigned>(reserialized, Options).ShouldBe(deserialized);
    }

    [Fact]
    public void WorkItemCompleted_DeserializesFromFrozenBytesAndRoundTrips()
    {
        string frozen = ReadGolden("WorkItemCompleted.v1.json");

        WorkItemCompleted deserialized = JsonSerializer.Deserialize<WorkItemCompleted>(frozen, Options).ShouldNotBeNull();

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(7);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");

        string reserialized = JsonSerializer.Serialize(deserialized, Options);
        JsonSerializer.Deserialize<WorkItemCompleted>(reserialized, Options).ShouldBe(deserialized);
    }

    [Fact]
    public void ProgressReported_DeserializesFromFrozenBytesAndRoundTrips()
    {
        string frozen = ReadGolden("ProgressReported.v1.json");

        ProgressReported deserialized = JsonSerializer.Deserialize<ProgressReported>(frozen, Options).ShouldNotBeNull();

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(7);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");
        deserialized.DoneDelta.ShouldBe(3m);
        deserialized.Unit.Value.ShouldBe("point");
        deserialized.Note.ShouldBe("first progress");

        string reserialized = JsonSerializer.Serialize(deserialized, Options);
        JsonSerializer.Deserialize<ProgressReported>(reserialized, Options).ShouldBe(deserialized);
    }

    [Fact]
    public void ReEstimated_DeserializesFromFrozenBytesAndRoundTrips()
    {
        ReEstimated deserialized = RoundTrip<ReEstimated>("ReEstimated.v1.json");

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(8);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");
        deserialized.Estimated.ShouldBe(13m);
        deserialized.Unit.Value.ShouldBe("point");
        deserialized.Note.ShouldBe("new estimate");
    }

    [Fact]
    public void WorkItemRescheduled_DeserializesFromFrozenBytesAndRoundTrips()
    {
        WorkItemRescheduled deserialized = RoundTrip<WorkItemRescheduled>("WorkItemRescheduled.v1.json");

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(9);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");
        deserialized.Schedule.Priority.ShouldBe(Priority.High);
        deserialized.Schedule.DueDate.ShouldBe(new DateOnly(2026, 7, 15));
        deserialized.Note.ShouldBe("deadline moved");
    }

    [Fact]
    public void WorkItemClaimed_DeserializesFromFrozenBytesAndRoundTrips()
    {
        WorkItemClaimed deserialized = RoundTrip<WorkItemClaimed>("WorkItemClaimed.v1.json");

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(4);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");

        // The claim carries the executor binding verbatim — the only enriched field on the event.
        deserialized.Binding.PartyId.Value.ShouldBe("party-123");
        deserialized.Binding.Channel.ShouldBe(Channel.Mcp);
        deserialized.Binding.AuthorityLevel.ShouldBe(AuthorityLevel.Coordinate);
    }

    [Fact]
    public void WorkItemRejected_DeserializesFromFrozenBytesAndRoundTrips()
    {
        WorkItemRejected deserialized = RoundTrip<WorkItemRejected>("WorkItemRejected.v1.json");

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(9);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");

        // The requeue flag is the resting-status discriminator and must survive the freeze exactly.
        deserialized.Requeue.ShouldBeFalse();
    }

    [Fact]
    public void Base_shape_lifecycle_events_deserialize_from_frozen_bytes_and_round_trip()
    {
        // The five lifecycle events whose v1 payload is exactly (AggregateId, Sequence, TenantId,
        // WorkItemId). Each is frozen independently so a future per-event field addition is still gated
        // by its own corpus entry — RR-6 requires every event ever produced to remain deserializable,
        // not just a representative sample.
        WorkItemQueued queued = RoundTrip<WorkItemQueued>("WorkItemQueued.v1.json");
        AssertCommonShape(queued.AggregateId, queued.Sequence, queued.TenantId, queued.WorkItemId, 3);

        WorkItemSuspended suspended = RoundTrip<WorkItemSuspended>("WorkItemSuspended.v1.json");
        AssertCommonShape(suspended.AggregateId, suspended.Sequence, suspended.TenantId, suspended.WorkItemId, 5);
        suspended.AwaitConditions.ShouldBe([new AwaitCondition(new WorkItemId("child-001"))]);

        WorkItemResumed resumed = RoundTrip<WorkItemResumed>("WorkItemResumed.v1.json");
        AssertCommonShape(resumed.AggregateId, resumed.Sequence, resumed.TenantId, resumed.WorkItemId, 6);
        resumed.ConsumedAwaitCondition.ShouldBe(new AwaitCondition(new WorkItemId("child-001")));

        WorkItemCancelled cancelled = RoundTrip<WorkItemCancelled>("WorkItemCancelled.v1.json");
        AssertCommonShape(cancelled.AggregateId, cancelled.Sequence, cancelled.TenantId, cancelled.WorkItemId, 8);

        WorkItemExpired expired = RoundTrip<WorkItemExpired>("WorkItemExpired.v1.json");
        AssertCommonShape(expired.AggregateId, expired.Sequence, expired.TenantId, expired.WorkItemId, 10);
    }

    [Fact]
    public void Legacy_suspend_resume_payloads_without_story35_fields_deserialize_tolerantly()
    {
        const string suspendedJson = """
            {
              "aggregateId": "work-001",
              "sequence": 5,
              "tenantId": { "value": "tenant-alpha" },
              "workItemId": { "value": "work-001" }
            }
            """;
        const string resumedJson = """
            {
              "aggregateId": "work-001",
              "sequence": 6,
              "tenantId": { "value": "tenant-alpha" },
              "workItemId": { "value": "work-001" }
            }
            """;

        WorkItemSuspended suspended = JsonSerializer.Deserialize<WorkItemSuspended>(suspendedJson, Options).ShouldNotBeNull();
        WorkItemResumed resumed = JsonSerializer.Deserialize<WorkItemResumed>(resumedJson, Options).ShouldNotBeNull();

        suspended.AwaitConditions.ShouldBeEmpty();
        resumed.ConsumedAwaitCondition.ShouldBeNull();
    }

    [Fact]
    public void WorkItemClaimed_ToleratesAdditiveUnknownField()
    {
        string frozen = ReadGolden("WorkItemClaimed.v1.json");
        string withFutureField = frozen.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": \"ignored\"\n}";

        WorkItemClaimed deserialized = JsonSerializer.Deserialize<WorkItemClaimed>(withFutureField, Options).ShouldNotBeNull();
        deserialized.Binding.PartyId.Value.ShouldBe("party-123");
    }

    [Fact]
    public void WorkItemRejected_ToleratesAdditiveUnknownField()
    {
        string frozen = ReadGolden("WorkItemRejected.v1.json");
        string withFutureField = frozen.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": \"ignored\"\n}";

        WorkItemRejected deserialized = JsonSerializer.Deserialize<WorkItemRejected>(withFutureField, Options).ShouldNotBeNull();
        deserialized.Requeue.ShouldBeFalse();
    }

    [Fact]
    public void WorkItemCreated_ToleratesAdditiveUnknownField()
    {
        // Additive/tolerant: an unknown field added by a future writer must not break deserialization.
        string frozen = ReadGolden("WorkItemCreated.v1.json");
        string withFutureField = frozen.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": \"ignored\"\n}";

        WorkItemCreated deserialized = JsonSerializer.Deserialize<WorkItemCreated>(withFutureField, Options).ShouldNotBeNull();
        deserialized.WorkItemId.Value.ShouldBe("work-001");
        deserialized.Obligation.Description.ShouldBe("Prepare the first tenant-scoped work item");
    }

    [Fact]
    public void WorkItemCompleted_ToleratesAdditiveUnknownField()
    {
        string frozen = ReadGolden("WorkItemCompleted.v1.json");
        string withFutureField = frozen.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": \"ignored\"\n}";

        WorkItemCompleted deserialized = JsonSerializer.Deserialize<WorkItemCompleted>(withFutureField, Options).ShouldNotBeNull();
        deserialized.Sequence.ShouldBe(7);
    }

    [Fact]
    public void ProgressReported_ToleratesAdditiveUnknownField()
    {
        string frozen = ReadGolden("ProgressReported.v1.json");
        string withFutureField = frozen.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": \"ignored\"\n}";

        ProgressReported deserialized = JsonSerializer.Deserialize<ProgressReported>(withFutureField, Options).ShouldNotBeNull();
        deserialized.DoneDelta.ShouldBe(3m);
        deserialized.Unit.Value.ShouldBe("point");
    }

    [Fact]
    public void ReEstimated_ToleratesAdditiveUnknownField()
    {
        string frozen = ReadGolden("ReEstimated.v1.json");
        string withFutureField = frozen.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": \"ignored\"\n}";

        ReEstimated deserialized = JsonSerializer.Deserialize<ReEstimated>(withFutureField, Options).ShouldNotBeNull();
        deserialized.Estimated.ShouldBe(13m);
        deserialized.Unit.Value.ShouldBe("point");
    }

    [Fact]
    public void WorkItemRescheduled_ToleratesAdditiveUnknownField()
    {
        string frozen = ReadGolden("WorkItemRescheduled.v1.json");
        string withFutureField = frozen.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": \"ignored\"\n}";

        WorkItemRescheduled deserialized = JsonSerializer.Deserialize<WorkItemRescheduled>(withFutureField, Options).ShouldNotBeNull();
        deserialized.Schedule.Priority.ShouldBe(Priority.High);
        deserialized.Schedule.DueDate.ShouldBe(new DateOnly(2026, 7, 15));
    }

    [Fact]
    public void ChildSpawned_DeserializesFromFrozenBytesAndRoundTrips()
    {
        ChildSpawned deserialized = RoundTrip<ChildSpawned>("ChildSpawned.v1.json");

        deserialized.AggregateId.ShouldBe("work-001");
        deserialized.Sequence.ShouldBe(14);
        deserialized.TenantId.Value.ShouldBe("tenant-alpha");
        deserialized.WorkItemId.Value.ShouldBe("work-001");
        deserialized.ChildWorkItemId.Value.ShouldBe("child-001");
        deserialized.Obligation.Description.ShouldBe("Break out child work");
        deserialized.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(5m);
        deserialized.InitialEffort.ShouldNotBeNull().Unit.Value.ShouldBe("point");
        deserialized.Schedule.ShouldNotBeNull().Priority.ShouldBe(Priority.Normal);
        deserialized.Schedule.ShouldNotBeNull().DueDate.ShouldBe(new DateOnly(2026, 8, 20));
        deserialized.ExecutorBinding.ShouldNotBeNull().PartyId.Value.ShouldBe("party-123");
        deserialized.ExecutorBinding.ShouldNotBeNull().Channel.ShouldBe(Channel.Mcp);
        deserialized.ExecutorBinding.ShouldNotBeNull().AuthorityLevel.ShouldBe(AuthorityLevel.Coordinate);
        deserialized.ConversationCorrelationId.ShouldNotBeNull().Value.ShouldBe("conversation-456");
        deserialized.SuspendParentUntilChildCompletes.ShouldBeTrue();
    }

    [Fact]
    public void ChildSpawned_ToleratesAdditiveUnknownField()
    {
        string frozen = ReadGolden("ChildSpawned.v1.json");
        string withFutureField = frozen.TrimEnd().TrimEnd('}') + ",\n  \"futureField\": \"ignored\"\n}";

        ChildSpawned deserialized = JsonSerializer.Deserialize<ChildSpawned>(withFutureField, Options).ShouldNotBeNull();
        deserialized.ChildWorkItemId.Value.ShouldBe("child-001");
        deserialized.SuspendParentUntilChildCompletes.ShouldBeTrue();
    }

    // Deserializes a frozen file via the production path and proves it re-serializes and round-trips to
    // an equal record. The File.Exists vacuous-pass guard lives in ReadGolden, so a missing fixture is
    // reported as the root cause rather than a downstream null.
    private static T RoundTrip<T>(string fileName)
        where T : class
    {
        string frozen = ReadGolden(fileName);

        T deserialized = JsonSerializer.Deserialize<T>(frozen, Options).ShouldNotBeNull();

        string reserialized = JsonSerializer.Serialize(deserialized, Options);
        JsonSerializer.Deserialize<T>(reserialized, Options).ShouldBe(deserialized);

        return deserialized;
    }

    private static void AssertCommonShape(
        string aggregateId, long sequence, TenantId tenantId, WorkItemId workItemId, long expectedSequence)
    {
        aggregateId.ShouldBe("work-001");
        sequence.ShouldBe(expectedSequence);
        tenantId.Value.ShouldBe("tenant-alpha");
        workItemId.Value.ShouldBe("work-001");
    }

    private static string ReadGolden(string fileName)
    {
        string path = Path.Combine(GoldenDirectory, fileName);
        File.Exists(path).ShouldBeTrue(path);

        // Normalize line endings before any comparison (golden files may be checked in CRLF or LF).
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
