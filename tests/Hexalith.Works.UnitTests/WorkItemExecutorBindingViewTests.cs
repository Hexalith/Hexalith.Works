using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Testing;
using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>
/// AC #3/#4 — the read-model view exposes the executor binding as data, and <see cref="AuthorityLevel"/>
/// survives replay into the model. The view carries only the work item identity and the uniform
/// <see cref="ExecutorBinding"/> (PartyId, Channel, AuthorityLevel) — no executor-kind discriminator and
/// no separate bot / human / external model.
/// </summary>
public sealed class WorkItemExecutorBindingViewTests
{
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Item = new("work-001");

    [Theory]
    [InlineData("party-system", Channel.Mcp, AuthorityLevel.Administer)]
    [InlineData("party-internal", Channel.Cli, AuthorityLevel.Contribute)]
    [InlineData("party-external", Channel.Email, AuthorityLevel.Read)]
    public void View_projected_from_replayed_state_preserves_the_full_binding_and_authority(
        string partyId, Channel channel, AuthorityLevel authorityLevel)
    {
        var binding = new ExecutorBinding(new PartyId(partyId), channel, authorityLevel);
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item, binding);

        WorkItemExecutorBindingView view = Project(state);

        view.TenantId.ShouldBe(Tenant);
        view.WorkItemId.ShouldBe(Item);
        view.ExecutorBinding.ShouldBe(binding);
        view.ExecutorBinding.ShouldNotBeNull().PartyId.ShouldBe(new PartyId(partyId));
        view.ExecutorBinding.Channel.ShouldBe(channel);
        view.ExecutorBinding.AuthorityLevel.ShouldBe(authorityLevel);
    }

    [Fact]
    public void View_reflects_the_latest_binding_after_reassignment()
    {
        var external = new ExecutorBinding(new PartyId("party-external"), Channel.Email, AuthorityLevel.Read);
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Assigned, Tenant, Item);
        state.Apply(new Contracts.Events.WorkItemAssigned(Item.Value, state.Sequence + 1, Tenant, Item, external));

        Project(state).ExecutorBinding.ShouldBe(external);
    }

    [Fact]
    public void View_carries_a_null_binding_when_no_executor_is_bound()
    {
        WorkItemState state = WorkItemStateBuilder.InStatus(WorkItemStatus.Created, Tenant, Item);

        Project(state).ExecutorBinding.ShouldBeNull();
    }

    [Fact]
    public void View_exposes_only_identity_and_executor_binding_with_no_kind_or_display_surface()
    {
        string[] propertyNames = [.. typeof(WorkItemExecutorBindingView).GetProperties().Select(p => p.Name)];

        propertyNames.ShouldBe(
            [nameof(WorkItemExecutorBindingView.TenantId), nameof(WorkItemExecutorBindingView.WorkItemId), nameof(WorkItemExecutorBindingView.ExecutorBinding)],
            ignoreOrder: true);

        string[] forbidden = ["Kind", "Bot", "Human", "External", "DisplayName", "PartyName", "Avatar", "Colour", "Color", "Email", "Contact"];
        foreach (string name in propertyNames)
        {
            foreach (string token in forbidden)
            {
                name.ShouldNotContain(token, Case.Insensitive);
            }
        }
    }

    private static WorkItemExecutorBindingView Project(WorkItemState state)
        => new(state.TenantId.ShouldNotBeNull(), state.WorkItemId.ShouldNotBeNull(), state.ExecutorBinding);
}
