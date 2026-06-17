using System.Text.Json;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Story 4.2 AC #3 (end-to-end) — a human → system → human hand-off chain survives the real write path
/// (<see cref="WorkItemAggregate"/> command handling) → concrete <see cref="JsonSerializerDefaults.Web"/>
/// serialization → independent replay. Where <see cref="UniformExecutorBindingLifecycleFlowTests"/>
/// proves a single reassignment is authoritative through claim/complete, this slice proves the
/// <em>ordered raw-act history</em> specifically: three contiguous <see cref="WorkItemAssigned"/> events,
/// each with its own binding, cross the wire in order and are not collapsed; the latest binding is what
/// the replayed state exposes for the next executor act; and the most-recent party's claim binds that
/// party — all with no executor-kind branch and no dedicated hand-off command.
/// </summary>
public sealed class WorkItemHandoffChainContractFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");

    [Fact]
    public void Human_system_human_handoff_chain_round_trips_each_raw_act_in_order_and_latest_binding_claims()
    {
        ExecutorBinding human1 = Binding("party-alice", Channel.Cli, AuthorityLevel.Coordinate);
        ExecutorBinding system = Binding("party-bot", Channel.Mcp, AuthorityLevel.Administer);
        ExecutorBinding human2 = Binding("party-bob", Channel.Email, AuthorityLevel.Read);

        // The hand-off is only meaningful if the three executors are genuinely distinct.
        human1.ShouldNotBe(system);
        system.ShouldNotBe(human2);
        human1.ShouldNotBe(human2);

        // Write side issues commands through the real aggregate; replay side is rebuilt ONLY from events
        // round-tripped through JSON, so the two converge only if each raw act survives serialization.
        var write = new WorkItemState();
        var replay = new WorkItemState();
        var history = new List<WorkItemAssigned>();

        Advance(Handle<WorkItemCreated>(new CreateWorkItem(Tenant, Item, "Hand off across executor kinds"), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.Created);

        foreach (ExecutorBinding binding in new[] { human1, system, human2 })
        {
            WorkItemAssigned assigned = Handle<WorkItemAssigned>(new AssignWorkItem(Tenant, Item, binding), write);
            history.Add(assigned);
            Advance(assigned, write, replay);

            // After the round-trip, the replayed binding equals this hand-off's binding — the latest act
            // is authoritative at every step, and channel/authority survived the wire.
            replay.Status.ShouldBe(WorkItemStatus.Assigned);
            replay.ExecutorBinding.ShouldBe(binding);
        }

        // Ordered raw-act evidence survives serialization: contiguous consecutive sequences, each its own
        // binding, in order — the chain is preserved as three distinct hand-offs, not collapsed.
        history.Select(e => e.Binding).ShouldBe([human1, system, human2]);
        history[1].Sequence.ShouldBe(history[0].Sequence + 1);
        history[2].Sequence.ShouldBe(history[1].Sequence + 1);

        // The most-recent party (human2) claims; the claim binds the latest party and replay agrees.
        Advance(Handle<WorkItemClaimed>(new ClaimWorkItem(Tenant, Item, human2), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.InProgress);
        replay.ExecutorBinding.ShouldBe(human2);
        replay.Sequence.ShouldBe(write.Sequence);
    }

    private static ExecutorBinding Binding(string partyId, Channel channel, AuthorityLevel authority)
        => new(new PartyId(partyId), channel, authority);

    private static T Handle<T>(object command, WorkItemState state)
        where T : class
    {
        var result = command switch
        {
            CreateWorkItem c => WorkItemAggregate.Handle(c, state),
            AssignWorkItem c => WorkItemAggregate.Handle(c, state),
            ClaimWorkItem c => WorkItemAggregate.Handle(c, state),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.GetType().Name, "Unhandled command."),
        };

        result.IsSuccess.ShouldBeTrue();
        return result.Events.Single().ShouldBeOfType<T>();
    }

    // Advances the authoritative write state with the freshly emitted event, then replays the SAME event
    // round-tripped through JSON into the independent replay state, proving the act crosses the wire.
    private static void Advance<T>(T e, WorkItemState write, WorkItemState replay)
        where T : class
    {
        ApplyEvent(write, e);
        ApplyEvent(replay, RoundTripEvent(e));
    }

    private static void ApplyEvent(WorkItemState state, object e)
    {
        switch (e)
        {
            case WorkItemCreated x: state.Apply(x); break;
            case WorkItemAssigned x: state.Apply(x); break;
            case WorkItemClaimed x: state.Apply(x); break;
            default: throw new ArgumentOutOfRangeException(nameof(e), e.GetType().Name, "Unhandled success event.");
        }
    }

    private static T RoundTripEvent<T>(T e)
        where T : class
    {
        string json = JsonSerializer.Serialize(e, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions).ShouldNotBeNull();
    }
}
