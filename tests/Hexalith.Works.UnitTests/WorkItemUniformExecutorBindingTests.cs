using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Hexalith.Works.Testing;
using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>
/// AC #1–#3 — every executor (system agent, internal user, external party) is bound through the one
/// <see cref="ExecutorBinding"/> shape and flows through the same create/assign/claim handlers with no
/// branch on <see cref="Channel"/> or <see cref="AuthorityLevel"/>. The three representative cases are
/// driven through identical code paths and differ only by field values, and <see cref="AuthorityLevel"/>
/// is preserved verbatim in events and replayed state (carried, not enforced).
/// </summary>
public sealed class WorkItemUniformExecutorBindingTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");

    // Each row is a representative executor case. Only the field values differ — there is no separate
    // command, event, or model for bot / human / external executors.
    [Theory]
    [InlineData("party-system", Channel.Mcp, AuthorityLevel.Administer)]      // system / agent
    [InlineData("party-agent", Channel.Chatbot, AuthorityLevel.Coordinate)]   // system / agent (alt channel)
    [InlineData("party-internal", Channel.Cli, AuthorityLevel.Contribute)]    // internal user
    [InlineData("party-external", Channel.Email, AuthorityLevel.Read)]        // external party
    public void CreateWorkItem_carries_the_uniform_binding_through_event_and_replay(
        string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);
        var command = new CreateWorkItem(Tenant, Item, "Bind work to a uniform party executor", ExecutorBinding: binding);

        WorkItemCreated created = WorkItemAggregate.Handle(command, null)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemCreated>();

        AssertSameShape(created.ExecutorBinding, binding);

        var state = new WorkItemState();
        state.Apply(created);
        AssertSameShape(state.ExecutorBinding, binding);
    }

    [Theory]
    [InlineData("party-system", Channel.Mcp, AuthorityLevel.Administer)]
    [InlineData("party-agent", Channel.Chatbot, AuthorityLevel.Coordinate)]
    [InlineData("party-internal", Channel.Cli, AuthorityLevel.Contribute)]
    [InlineData("party-external", Channel.Email, AuthorityLevel.Read)]
    public void AssignWorkItem_carries_the_uniform_binding_through_event_and_replay(
        string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Item);

        WorkItemAssigned assigned = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, binding), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();

        AssertSameShape(assigned.Binding, binding);

        state.Apply(assigned);
        state.Status.ShouldBe(WorkItemStatus.Assigned);
        AssertSameShape(state.ExecutorBinding, binding);
    }

    [Theory]
    [InlineData("party-system", Channel.Mcp, AuthorityLevel.Administer)]
    [InlineData("party-agent", Channel.Chatbot, AuthorityLevel.Coordinate)]
    [InlineData("party-internal", Channel.Cli, AuthorityLevel.Contribute)]
    [InlineData("party-external", Channel.Email, AuthorityLevel.Read)]
    public void ClaimWorkItem_carries_the_uniform_binding_through_event_and_replay(
        string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item, binding);

        WorkItemClaimed claimed = WorkItemAggregate.Handle(new ClaimWorkItem(Tenant, Item, binding), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemClaimed>();

        AssertSameShape(claimed.Binding, binding);

        state.Apply(claimed);
        state.Status.ShouldBe(WorkItemStatus.InProgress);
        AssertSameShape(state.ExecutorBinding, binding);
    }

    // AC #2/#3 — SpawnChild is the fourth binding-carrying command/event pair, and it carries the same one
    // ExecutorBinding shape as create/assign/claim. The child binding (and its AuthorityLevel) flows through
    // ChildSpawned unchanged for every representative executor; the parent aggregate never inspects Channel
    // or AuthorityLevel to decide what to emit.
    [Theory]
    [InlineData("party-system", Channel.Mcp, AuthorityLevel.Administer)]      // system / agent
    [InlineData("party-agent", Channel.Chatbot, AuthorityLevel.Coordinate)]   // system / agent (alt channel)
    [InlineData("party-internal", Channel.Cli, AuthorityLevel.Contribute)]    // internal user
    [InlineData("party-external", Channel.Email, AuthorityLevel.Read)]        // external party
    public void SpawnChild_carries_the_uniform_child_binding_through_the_spawned_event(
        string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Item);
        var command = new SpawnChild(Tenant, Item, new WorkItemId("child-001"), "Break out child work", ExecutorBinding: binding);

        ChildSpawned spawned = WorkItemAggregate.Handle(command, state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<ChildSpawned>();

        AssertSameShape(spawned.ExecutorBinding, binding);
    }

    // AC #1/#2 — reassignment is just a second accepted AssignWorkItem; the latest binding wins on replay
    // with no dedicated handoff command and no executor-kind branch. A system-agent binding is replaced by
    // an external-party binding through the same code path.
    [Fact]
    public void Reassignment_through_assign_makes_the_latest_binding_authoritative_without_a_handoff_command()
    {
        ExecutorBinding system = Binding("party-system", Channel.Mcp, AuthorityLevel.Administer);
        ExecutorBinding external = Binding("party-external", Channel.Email, AuthorityLevel.Read);
        system.ShouldNotBe(external);

        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Item);

        WorkItemAssigned first = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, system), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
        state.Apply(first);
        AssertSameShape(state.ExecutorBinding, system);

        // Rebind while Assigned — accepted by the same AssignWorkItem path (Assigned -> Assigned).
        WorkItemAssigned second = WorkItemAggregate.Handle(new AssignWorkItem(Tenant, Item, external), state)
            .Events.ShouldHaveSingleItem().ShouldBeOfType<WorkItemAssigned>();
        second.Sequence.ShouldBe(first.Sequence + 1);
        state.Apply(second);

        AssertSameShape(state.ExecutorBinding, external);
        state.Status.ShouldBe(WorkItemStatus.Assigned);
    }

    private static ExecutorBinding Binding(string partyId, Channel channel, AuthorityLevel authorityLevel)
        => new(new PartyId(partyId), channel, authorityLevel);

    private static void AssertSameShape(ExecutorBinding? actual, ExecutorBinding expected)
    {
        ExecutorBinding binding = actual.ShouldNotBeNull();
        binding.ShouldBe(expected);
        binding.PartyId.ShouldBe(expected.PartyId);
        binding.Channel.ShouldBe(expected.Channel);
        binding.AuthorityLevel.ShouldBe(expected.AuthorityLevel);
    }
}
