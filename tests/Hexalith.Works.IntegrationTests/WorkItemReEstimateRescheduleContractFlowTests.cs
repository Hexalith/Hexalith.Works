using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorkItemReEstimateRescheduleContractFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");
    private static readonly Unit Hour = new("hour");
    private static readonly ExecutorBinding Binding = new(new PartyId("party-exec"), Channel.Mcp, AuthorityLevel.Administer);

    [Fact]
    public void ReEstimate_command_to_aggregate_json_to_replay_converges_effort_without_envelope_fields()
    {
        var write = new WorkItemState();
        var replay = new WorkItemState();

        Persist(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Serialize re-estimate"), new WorkItemEffort(8m, Hour, 3m)), write, replay);
        Persist(new WorkItemAssigned(Item.Value, 2, Tenant, Item, Binding), write, replay);
        Persist(new WorkItemClaimed(Item.Value, 3, Tenant, Item, Binding), write, replay);

        var result = WorkItemAggregate.Handle(new ReEstimate(Tenant, Item, 13m, Hour, "scope grew"), write);

        result.IsSuccess.ShouldBeTrue();
        ReEstimated reEstimated = result.Events.ShouldHaveSingleItem().ShouldBeOfType<ReEstimated>();
        ReEstimated persisted = RoundTrip(reEstimated);

        write.Apply(persisted);
        replay.Apply(persisted);

        replay.InitialEffort.ShouldNotBeNull().Estimated.ShouldBe(13m);
        replay.InitialEffort.ShouldNotBeNull().Done.ShouldBe(3m);
        replay.InitialEffort.ShouldNotBeNull().Unit.ShouldBe(Hour);
        replay.Remaining.ShouldBe(10m);
        replay.Status.ShouldBe(WorkItemStatus.InProgress);
        replay.Sequence.ShouldBe(4);
        replay.Sequence.ShouldBe(write.Sequence);
    }

    [Fact]
    public void Reschedule_command_to_aggregate_json_to_replay_converges_schedule_without_envelope_fields()
    {
        var write = new WorkItemState();
        var replay = new WorkItemState();
        var schedule = new WorkItemSchedule(Priority.High, new DateOnly(2026, 7, 15));

        Persist(new WorkItemCreated(Item.Value, 1, Tenant, Item, new Obligation("Serialize reschedule")), write, replay);
        Persist(new WorkItemAssigned(Item.Value, 2, Tenant, Item, Binding), write, replay);

        var result = WorkItemAggregate.Handle(new RescheduleWorkItem(Tenant, Item, schedule, "deadline moved"), write);

        result.IsSuccess.ShouldBeTrue();
        WorkItemRescheduled rescheduled = result.Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemRescheduled>();
        WorkItemRescheduled persisted = RoundTrip(rescheduled);

        write.Apply(persisted);
        replay.Apply(persisted);

        replay.Schedule.ShouldBe(schedule);
        replay.Schedule.ShouldNotBeNull().Priority.ShouldBe(Priority.High);
        replay.Schedule.ShouldNotBeNull().DueDate.ShouldBe(new DateOnly(2026, 7, 15));
        replay.Status.ShouldBe(WorkItemStatus.Assigned);
        replay.Sequence.ShouldBe(3);
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

            foreach (string envelopeField in WorkItemV1Catalog.EnvelopeFields)
            {
                root.TryGetProperty(envelopeField, out _).ShouldBeFalse();
            }
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
            case ReEstimated x: state.Apply(x); break;
            case WorkItemRescheduled x: state.Apply(x); break;
            default: throw new ArgumentOutOfRangeException(nameof(e), e.GetType().Name, "Unhandled re-estimate/reschedule event.");
        }
    }
}
