using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// AC #2–#4 (end-to-end) — every executor (system agent, internal user, external party) is bound through
/// the one <see cref="ExecutorBinding"/> shape, and the carried <see cref="AuthorityLevel"/> survives the
/// real write path (<see cref="WorkItemAggregate"/> command handling) → concrete
/// <see cref="JsonSerializerDefaults.Web"/> serialization → independent replay
/// <see cref="WorkItemState"/> → <see cref="WorkItemExecutorBindingView"/> projection. Where
/// <see cref="WorkItemLifecycleContractFlowTests"/> proves the generic lifecycle survives serialization
/// with a single binding, this slice proves the executor-binding data specifically: all three
/// representative executors flow through identical handlers, reassignment to a different authority is
/// authoritative on replay, and the latest authority is what the read-model view exposes — with no
/// executor-kind branch anywhere on the path.
/// </summary>
public sealed class UniformExecutorBindingLifecycleFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");

    public static TheoryData<string, string, Channel, AuthorityLevel> RepresentativeExecutors => new()
    {
        { "system agent", "party-system", Channel.Mcp, AuthorityLevel.Administer },
        { "internal user", "party-internal", Channel.Cli, AuthorityLevel.Contribute },
        { "external party", "party-external", Channel.Email, AuthorityLevel.Read },
    };

    [Theory]
    [MemberData(nameof(RepresentativeExecutors))]
    public void Create_assign_claim_carries_each_executor_binding_end_to_end_into_the_read_model_view(
        string label, string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        _ = label;
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);

        // Write side issues commands through the real aggregate; replay side is rebuilt ONLY from
        // round-tripped events, so the two only converge if the binding survives serialization.
        var write = new WorkItemState();
        var replay = new WorkItemState();

        Advance(Handle<WorkItemCreated>(new CreateWorkItem(Tenant, Item, "Bind work to a uniform party executor", ExecutorBinding: binding), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.Created);
        replay.ExecutorBinding.ShouldBe(binding);

        Advance(Handle<WorkItemAssigned>(new AssignWorkItem(Tenant, Item, binding), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.Assigned);

        Advance(Handle<WorkItemClaimed>(new ClaimWorkItem(Tenant, Item, binding), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.InProgress);

        // The replayed binding equals the supplied one, the AuthorityLevel is preserved verbatim, and the
        // read-model view exposes the same uniform data for every executor kind.
        replay.ExecutorBinding.ShouldBe(binding);
        replay.ExecutorBinding.ShouldNotBeNull().AuthorityLevel.ShouldBe(authorityLevel);

        WorkItemExecutorBindingView view = ProjectView(replay);
        view.ExecutorBinding.ShouldBe(binding);
        view.ExecutorBinding.ShouldNotBeNull().PartyId.ShouldBe(new PartyId(partyId));
        view.ExecutorBinding.Channel.ShouldBe(channel);
        view.ExecutorBinding.AuthorityLevel.ShouldBe(authorityLevel);
    }

    [Fact]
    public void Reassigning_a_different_authority_mid_lifecycle_is_authoritative_through_claim_complete_and_the_view()
    {
        // A system agent (Administer) is created, rebound to an internal user (Contribute), then to an
        // external party (Read) — all through the same AssignWorkItem path — claimed, and completed. The
        // latest authority must win on replay and remain the value the read-model view exposes at every
        // persisted step, proving authority is carried (not enforced) and reassignment needs no handoff
        // command and no executor-kind branch.
        ExecutorBinding system = Binding("party-system", Channel.Mcp, AuthorityLevel.Administer);
        ExecutorBinding internalUser = Binding("party-internal", Channel.Cli, AuthorityLevel.Contribute);
        ExecutorBinding external = Binding("party-external", Channel.Email, AuthorityLevel.Read);

        var write = new WorkItemState();
        var replay = new WorkItemState();

        Advance(Handle<WorkItemCreated>(new CreateWorkItem(Tenant, Item, "Reassign across executor kinds", ExecutorBinding: system), write), write, replay);
        replay.ExecutorBinding.ShouldBe(system);

        Advance(Handle<WorkItemAssigned>(new AssignWorkItem(Tenant, Item, internalUser), write), write, replay);
        replay.ExecutorBinding.ShouldBe(internalUser);

        // Rebind again while Assigned — accepted through the same path; the newest binding supersedes.
        Advance(Handle<WorkItemAssigned>(new AssignWorkItem(Tenant, Item, external), write), write, replay);
        replay.ExecutorBinding.ShouldBe(external);
        ProjectView(replay).ExecutorBinding.ShouldBe(external);

        Advance(Handle<WorkItemClaimed>(new ClaimWorkItem(Tenant, Item, external), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.InProgress);

        Advance(Handle<WorkItemCompleted>(new CompleteWorkItem(Tenant, Item), write), write, replay);
        replay.Status.ShouldBe(WorkItemStatus.Completed);

        // Complete does not carry or clear a binding: the latest carried authority is still authoritative
        // in terminal state and in the view, and it is the reassigned external authority — not the
        // original system authority the work item was created with.
        replay.ExecutorBinding.ShouldBe(external);
        replay.ExecutorBinding.ShouldNotBeNull().AuthorityLevel.ShouldBe(AuthorityLevel.Read);
        replay.ExecutorBinding.AuthorityLevel.ShouldNotBe(AuthorityLevel.Administer);
        ProjectView(replay).ExecutorBinding.ShouldBe(external);

        replay.Sequence.ShouldBe(write.Sequence);
    }

    private static ExecutorBinding Binding(string partyId, Channel channel, AuthorityLevel authorityLevel)
        => new(new PartyId(partyId), channel, authorityLevel);

    private static WorkItemExecutorBindingView ProjectView(WorkItemState state)
        => new(state.TenantId.ShouldNotBeNull(), state.WorkItemId.ShouldNotBeNull(), state.ExecutorBinding);

    private static T Handle<T>(object command, WorkItemState state)
        where T : class
    {
        var result = command switch
        {
            CreateWorkItem c => WorkItemAggregate.Handle(c, state),
            AssignWorkItem c => WorkItemAggregate.Handle(c, state),
            ClaimWorkItem c => WorkItemAggregate.Handle(c, state),
            CompleteWorkItem c => WorkItemAggregate.Handle(c, state),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.GetType().Name, "Unhandled command."),
        };

        result.IsSuccess.ShouldBeTrue();
        return result.Events.Single().ShouldBeOfType<T>();
    }

    // Advances the authoritative write state with the freshly emitted event, then replays the SAME event
    // round-tripped through JSON into the independent replay state, proving the binding crosses the wire.
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
            case WorkItemCompleted x: state.Apply(x); break;
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
