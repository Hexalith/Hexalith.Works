using System.Text.Json;

using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// AC #1–#3 — the one <see cref="ExecutorBinding"/> shape survives concrete <see cref="JsonSerializerDefaults.Web"/>
/// serialization (the EventStore-persisted form, no <c>$type</c>) for system-agent, internal-user, and
/// external-party executors. <c>authorityLevel</c> is the field most at risk of being dropped, so each
/// representative binding is asserted present in the JSON and equal after round-trip on the three
/// binding-carrying events and on replayed <see cref="WorkItemState"/>.
/// </summary>
public sealed class UniformExecutorBindingSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
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
    public void WorkItemCreated_round_trips_the_uniform_binding_with_authority(
        string label, string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        _ = label;
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);
        var created = new WorkItemCreated("work-001", 1, Tenant, Item, new Obligation("Bind to a party executor"), ExecutorBinding: binding);

        WorkItemCreated roundTripped = RoundTripWithAuthorityAssertion(created, channel, authorityLevel);
        roundTripped.ExecutorBinding.ShouldBe(binding);

        var state = new WorkItemState();
        state.Apply(roundTripped);
        state.ExecutorBinding.ShouldBe(binding);
        state.ExecutorBinding.ShouldNotBeNull().AuthorityLevel.ShouldBe(authorityLevel);
    }

    [Theory]
    [MemberData(nameof(RepresentativeExecutors))]
    public void WorkItemAssigned_round_trips_the_uniform_binding_with_authority(
        string label, string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        _ = label;
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);
        var assigned = new WorkItemAssigned("work-001", 2, Tenant, Item, binding);

        WorkItemAssigned roundTripped = RoundTripWithAuthorityAssertion(assigned, channel, authorityLevel);
        roundTripped.Binding.ShouldBe(binding);
        roundTripped.Binding.AuthorityLevel.ShouldBe(authorityLevel);
    }

    [Theory]
    [MemberData(nameof(RepresentativeExecutors))]
    public void WorkItemClaimed_round_trips_the_uniform_binding_with_authority(
        string label, string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        _ = label;
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);
        var claimed = new WorkItemClaimed("work-001", 4, Tenant, Item, binding);

        WorkItemClaimed roundTripped = RoundTripWithAuthorityAssertion(claimed, channel, authorityLevel);
        roundTripped.Binding.ShouldBe(binding);
        roundTripped.Binding.AuthorityLevel.ShouldBe(authorityLevel);
    }

    [Theory]
    [MemberData(nameof(RepresentativeExecutors))]
    public void ChildSpawned_round_trips_the_uniform_child_binding_with_authority(
        string label, string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        // SpawnChild/ChildSpawned is the fourth binding-carrying pair; the child binding is nested but its
        // authorityLevel must survive the same EventStore-persisted form as the create/assign/claim events.
        _ = label;
        ExecutorBinding binding = Binding(partyId, channel, authorityLevel);
        var spawned = new ChildSpawned("work-001", 3, Tenant, Item, new WorkItemId("child-001"), new Obligation("Break out child work"), ExecutorBinding: binding);

        ChildSpawned roundTripped = RoundTripWithAuthorityAssertion(spawned, channel, authorityLevel);
        roundTripped.ExecutorBinding.ShouldBe(binding);
        roundTripped.ExecutorBinding.ShouldNotBeNull().AuthorityLevel.ShouldBe(authorityLevel);
    }

    private static ExecutorBinding Binding(string partyId, Channel channel, AuthorityLevel authorityLevel)
        => new(new PartyId(partyId), channel, authorityLevel);

    private static T RoundTripWithAuthorityAssertion<T>(T payload, Channel channel, AuthorityLevel authorityLevel)
        where T : class
    {
        string json = JsonSerializer.Serialize(payload, Options);
        json.ShouldContain($"\"channel\":\"{channel}\"");
        json.ShouldContain($"\"authorityLevel\":\"{authorityLevel}\"");

        T deserialized = JsonSerializer.Deserialize<T>(json, Options).ShouldNotBeNull();
        deserialized.ShouldBe(payload);
        return deserialized;
    }
}
