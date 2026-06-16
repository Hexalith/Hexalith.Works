using System.Globalization;
using System.Text.Json;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Story 3.5 serialization-boundary coverage for the Await-Condition value object across all three
/// kinds. The existing lifecycle/spawn contract-flow suites only drive <c>ChildCompleted</c> and
/// <c>ExternalSignal</c> conditions through the real <see cref="System.Text.Json"/> path; these tests
/// add the <c>DateReached</c> seam — proving its kind, correlation key, and normalized UTC instant
/// survive write → persist → replay so a suspend/resume parked on a date converges after round-trip
/// (NFR-12 / D5 / D6). No wall-clock is read: the instant is command-delivered data only (D5).
/// </summary>
public sealed class AwaitConditionSerializationContractFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly WorkItemId Child = new("child-001");
    private static readonly ExecutorBinding Binding = new(new PartyId("party-exec"), Channel.Mcp, AuthorityLevel.Administer);
    private static readonly DateTimeOffset Instant = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> AllThreeKinds()
    {
        yield return [AwaitCondition.ChildCompleted(Child), AwaitConditionKind.ChildCompleted, "child-001"];
        yield return [AwaitCondition.DateReached(Instant), AwaitConditionKind.DateReached, Instant.ToString("O", CultureInfo.InvariantCulture)];
        yield return [AwaitCondition.ExternalSignal("external-approval-001"), AwaitConditionKind.ExternalSignal, "external-approval-001"];
    }

    [Theory]
    [MemberData(nameof(AllThreeKinds))]
    public void Await_condition_round_trips_through_json_preserving_kind_and_correlation_key(
        AwaitCondition condition, AwaitConditionKind expectedKind, string expectedKey)
    {
        string json = JsonSerializer.Serialize(condition, JsonOptions);

        AwaitCondition roundTripped = JsonSerializer.Deserialize<AwaitCondition>(json, JsonOptions).ShouldNotBeNull();

        roundTripped.ShouldBe(condition);
        roundTripped.Kind.ShouldBe(expectedKind);
        roundTripped.CorrelationKey.ShouldBe(expectedKey);
    }

    [Fact]
    public void Date_reached_condition_serializes_a_utc_instant_and_round_trips_to_an_equal_value()
    {
        AwaitCondition condition = AwaitCondition.DateReached(Instant);

        string json = JsonSerializer.Serialize(condition, JsonOptions);
        json.ShouldContain("\"kind\":\"DateReached\"");
        json.ShouldContain("\"instant\"");
        json.ShouldNotContain("childWorkItemId");
        json.ShouldNotContain("externalCorrelationId");

        AwaitCondition roundTripped = JsonSerializer.Deserialize<AwaitCondition>(json, JsonOptions).ShouldNotBeNull();
        roundTripped.Instant.ShouldBe(Instant);
        roundTripped.ChildWorkItemId.ShouldBeNull();
        roundTripped.ExternalCorrelationId.ShouldBeNull();
        roundTripped.CorrelationKey.ShouldBe(Instant.ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Kind_aware_inequality_survives_the_serialization_boundary()
    {
        // D3: a child id and an external correlation id that share the same text are still different
        // conditions — the discriminator is carried by the persisted form, not inferred from the key.
        AwaitCondition external = JsonSerializer.Deserialize<AwaitCondition>(
            JsonSerializer.Serialize(AwaitCondition.ExternalSignal("child-001"), JsonOptions), JsonOptions).ShouldNotBeNull();
        AwaitCondition child = JsonSerializer.Deserialize<AwaitCondition>(
            JsonSerializer.Serialize(AwaitCondition.ChildCompleted(Child), JsonOptions), JsonOptions).ShouldNotBeNull();

        external.CorrelationKey.ShouldBe(child.CorrelationKey);
        external.ShouldNotBe(child);
    }

    [Fact]
    public void Suspend_and_resume_parked_on_a_date_condition_round_trip_to_in_progress()
    {
        WorkItemState write = InProgress();
        WorkItemState replay = InProgress();

        // Park on a DateReached await condition; persist the suspension through JSON into the replay state.
        WorkItemSuspended suspended = WorkItemAggregate
            .Handle(new SuspendWorkItem(Tenant, Item, [AwaitCondition.DateReached(Instant)]), write)
            .Events.Single().ShouldBeOfType<WorkItemSuspended>();
        write.Apply(suspended);

        WorkItemSuspended suspendedReplayed = RoundTrip(suspended);
        suspendedReplayed.AwaitConditions.ShouldBe([AwaitCondition.DateReached(Instant)]);
        replay.Apply(suspendedReplayed);
        replay.Status.ShouldBe(WorkItemStatus.Suspended);

        // Resume with the same instant expressed in a different offset — it normalizes to the same key and
        // matches the parked condition even after both sides cross the serialization boundary (D5).
        AwaitCondition resumeKey = AwaitCondition.DateReached(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(2)));
        WorkItemResumed resumed = WorkItemAggregate
            .Handle(new ResumeWorkItem(Tenant, Item, resumeKey), write)
            .Events.Single().ShouldBeOfType<WorkItemResumed>();
        write.Apply(resumed);

        WorkItemResumed resumedReplayed = RoundTrip(resumed);
        resumedReplayed.ConsumedAwaitCondition.ShouldBe(AwaitCondition.DateReached(Instant));
        replay.Apply(resumedReplayed);

        replay.Status.ShouldBe(WorkItemStatus.InProgress);
        replay.AwaitConditions.ShouldBeEmpty();
        replay.LastConsumedAwaitCondition.ShouldBe(AwaitCondition.DateReached(Instant));
        replay.Sequence.ShouldBe(write.Sequence);
    }

    private static WorkItemState InProgress()
    {
        var state = new WorkItemState();
        state.Apply(WorkItemAggregate.Handle(new CreateWorkItem(Tenant, Item, "Park work on a date"), state)
            .Events.Single().ShouldBeOfType<WorkItemCreated>());
        state.Apply(WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, Binding), state)
            .Events.Single().ShouldBeOfType<WorkItemAssigned>());
        state.Apply(WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, Binding), state)
            .Events.Single().ShouldBeOfType<WorkItemClaimed>());
        return state;
    }

    private static T RoundTrip<T>(T value)
        where T : class
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions).ShouldNotBeNull();
}
