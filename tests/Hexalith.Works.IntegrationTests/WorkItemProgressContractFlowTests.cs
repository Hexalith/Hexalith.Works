using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorkItemProgressContractFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly Unit Hour = new("hour");
    private static readonly ExecutorBinding Binding = new(new PartyId("party-exec"), Channel.Mcp, AuthorityLevel.Administer);

    [Fact]
    public void Progress_command_to_aggregate_json_to_replay_reduces_remaining_and_auto_completes()
    {
        var write = new WorkItemState();
        var replay = new WorkItemState();

        Persist(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Serialize progress"), new WorkItemEffort(8m, Hour)), write, replay);
        Persist(new WorkItemAssigned(Item.Value, 2, Tenant, Item, Binding), write, replay);
        Persist(new WorkItemClaimed(Item.Value, 3, Tenant, Item, Binding), write, replay);

        var result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 8m, Hour, "done"), write);

        result.IsSuccess.ShouldBeTrue();
        result.Events.Count.ShouldBe(2);

        ProgressReported reported = result.Events[0].ShouldBeOfType<ProgressReported>();
        WorkItemCompleted completed = result.Events[1].ShouldBeOfType<WorkItemCompleted>();

        ProgressReported persistedProgress = RoundTrip(reported);
        WorkItemCompleted persistedCompletion = RoundTrip(completed);

        write.Apply(persistedProgress);
        write.Apply(persistedCompletion);
        replay.Apply(persistedProgress);
        replay.Apply(persistedCompletion);

        replay.InitialEffort.ShouldNotBeNull().Done.ShouldBe(8m);
        replay.Remaining.ShouldBe(0m);
        replay.Status.ShouldBe(WorkItemStatus.Completed);
        replay.Sequence.ShouldBe(5);
        replay.Sequence.ShouldBe(write.Sequence);
    }

    [Fact]
    public void Partial_progress_round_trips_through_json_and_burns_down_without_completing()
    {
        // AC #1/#2: a single sub-completion report survives concrete EventStore serialization and replays to
        // an accumulated Done / derived Remaining while staying InProgress — the non-completing burn-down path.
        var write = new WorkItemState();
        var replay = new WorkItemState();

        Persist(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Serialize progress"), new WorkItemEffort(8m, Hour)), write, replay);
        Persist(new WorkItemAssigned(Item.Value, 2, Tenant, Item, Binding), write, replay);
        Persist(new WorkItemClaimed(Item.Value, 3, Tenant, Item, Binding), write, replay);

        var result = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 3m, Hour, "first pass"), write);

        result.IsSuccess.ShouldBeTrue();
        ProgressReported reported = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ProgressReported>();

        ProgressReported persisted = RoundTrip(reported);
        persisted.DoneDelta.ShouldBe(3m);
        persisted.Note.ShouldBe("first pass");

        write.Apply(persisted);
        replay.Apply(persisted);

        replay.InitialEffort.ShouldNotBeNull().Done.ShouldBe(3m);
        replay.Remaining.ShouldBe(5m);
        replay.Status.ShouldBe(WorkItemStatus.InProgress);
        replay.Sequence.ShouldBe(4);
        replay.Sequence.ShouldBe(write.Sequence);
    }

    [Fact]
    public void Repeated_progress_round_trips_through_json_and_accumulates_then_completes()
    {
        // AC #2/#3: two reports delivered through concrete JSON accumulate Done across reports, and the
        // report that lands Remaining on zero round-trips with its paired completion to a Completed replay.
        var write = new WorkItemState();
        var replay = new WorkItemState();

        Persist(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Serialize progress"), new WorkItemEffort(8m, Hour)), write, replay);
        Persist(new WorkItemAssigned(Item.Value, 2, Tenant, Item, Binding), write, replay);
        Persist(new WorkItemClaimed(Item.Value, 3, Tenant, Item, Binding), write, replay);

        ProgressReported firstReported = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 3m, Hour), write)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<ProgressReported>();
        ProgressReported firstPersisted = RoundTrip(firstReported);
        write.Apply(firstPersisted);
        replay.Apply(firstPersisted);
        replay.Remaining.ShouldBe(5m);

        var secondResult = WorkItemAggregate.Handle(new ReportProgress(Tenant, Item, 5m, Hour), write);
        secondResult.Events.Count.ShouldBe(2);
        ProgressReported secondReported = secondResult.Events[0].ShouldBeOfType<ProgressReported>();
        WorkItemCompleted completed = secondResult.Events[1].ShouldBeOfType<WorkItemCompleted>();

        ProgressReported secondPersisted = RoundTrip(secondReported);
        WorkItemCompleted completedPersisted = RoundTrip(completed);
        write.Apply(secondPersisted);
        write.Apply(completedPersisted);
        replay.Apply(secondPersisted);
        replay.Apply(completedPersisted);

        replay.InitialEffort.ShouldNotBeNull().Done.ShouldBe(8m);
        replay.Remaining.ShouldBe(0m);
        replay.Status.ShouldBe(WorkItemStatus.Completed);
        replay.Sequence.ShouldBe(6);
        replay.Sequence.ShouldBe(write.Sequence);
    }

    private static void Persist<T>(T e, WorkItemState write, WorkItemState replay)
        where T : class, IEventPayload
    {
        T persisted = RoundTrip(e);
        Apply(write, persisted);
        Apply(replay, persisted);
    }

    private static T RoundTrip<T>(T e)
        where T : class, IEventPayload
    {
        string json = JsonSerializer.Serialize(e, JsonOptions);
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement root = document.RootElement;
            root.TryGetProperty("aggregateId", out _).ShouldBeTrue();
            root.TryGetProperty("sequence", out _).ShouldBeTrue();
            root.TryGetProperty("$type", out _).ShouldBeFalse();
            root.TryGetProperty("messageId", out _).ShouldBeFalse();
            root.TryGetProperty("metadata", out _).ShouldBeFalse();
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions).ShouldNotBeNull();
    }

    private static void Apply(WorkItemState state, IEventPayload e)
    {
        switch (e)
        {
            case WorkItemCreated x: state.Apply(x); break;
            case WorkItemAssigned x: state.Apply(x); break;
            case WorkItemClaimed x: state.Apply(x); break;
            case ProgressReported x: state.Apply(x); break;
            case WorkItemCompleted x: state.Apply(x); break;
            default: throw new ArgumentOutOfRangeException(nameof(e), e.GetType().Name, "Unhandled progress event.");
        }
    }
}
