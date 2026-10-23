using System.Reflection;
using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Results;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Recovery;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Current ingress guards use canonical tenant identity without relaxing stream isolation.</summary>
public sealed class WorksIngressIdentityTests
{
    /// <summary>Every spelling of the reserved tenant is refused before keys or source evidence are acquired.</summary>
    [Theory]
    [InlineData("tenants")]
    [InlineData("TENANTS")]
    [InlineData("TeNaNtS")]
    public async Task ReservedTenantIsRefusedBeforeEvidenceAcquisition(string tenant)
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));

        WorksReadModelKeys.IsReservedTenantId(tenant).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => WorksReadModelKeys.PendingDateAwaitIndexKey(tenant));
        await Should.ThrowAsync<InvalidOperationException>(() => reader.ReadAsync(tenant, "item",
            TestContext.Current.CancellationToken));

        gateway.ReceivedCalls().ShouldBeEmpty();
        WorksReadModelKeys.IsReservedTenantId("tenants-other").ShouldBeFalse();
        WorksReadModelKeys.PendingDateAwaitIndexKey("tenants-other").ShouldEndWith(":tenants-other");
    }

    /// <summary>SDK reflection dispatch accepts canonical-equivalent tenants and retains canonical event payloads.</summary>
    [Theory]
    [InlineData(false, "tenant-ingress")]
    [InlineData(false, "TENANT-INGRESS")]
    [InlineData(false, "TeNaNt-InGrEsS")]
    [InlineData(true, "tenant-ingress")]
    [InlineData(true, "TENANT-INGRESS")]
    [InlineData(true, "TeNaNt-InGrEsS")]
    public async Task WitnessedCommandAcceptsCanonicalEnvelopeTenant(bool reschedule, string envelopeTenant)
    {
        (Polymorphic command, WorkItemState? state) = Command(reschedule);
        CommandEnvelope envelope = Envelope(command, envelopeTenant);

        DomainResult result = await new WorkItemEventStoreAggregate().ProcessAsync(envelope, state,
            TestContext.Current.CancellationToken);

        result.IsRejection.ShouldBeFalse();
        result.Events.Count.ShouldBe(1);
        if (reschedule)
        {
            WorkItemRescheduled @event = result.Events.Single().ShouldBeOfType<WorkItemRescheduled>();
            @event.TenantId.Value.ShouldBe(envelope.AggregateIdentity.TenantId);
            @event.Expiry.ShouldBe(((RescheduleWorkItem)command).Expiry);
        }
        else
        {
            WorkItemCreated @event = result.Events.Single().ShouldBeOfType<WorkItemCreated>();
            @event.TenantId.Value.ShouldBe(envelope.AggregateIdentity.TenantId);
            @event.Expiry.ShouldBe(((CreateWorkItem)command).Expiry);
        }
    }

    /// <summary>Tenant normalization never admits a foreign tenant, domain or case-sensitive aggregate.</summary>
    [Theory]
    [InlineData(false, "foreign-tenant", "work", "Item-Ingress")]
    [InlineData(false, "TENANT-INGRESS", "foreign", "Item-Ingress")]
    [InlineData(false, "TENANT-INGRESS", "work", "item-ingress")]
    [InlineData(true, "foreign-tenant", "work", "Item-Ingress")]
    [InlineData(true, "TENANT-INGRESS", "foreign", "Item-Ingress")]
    [InlineData(true, "TENANT-INGRESS", "work", "item-ingress")]
    public async Task WitnessedCommandRefusesForeignStream(bool reschedule, string tenant, string domain, string aggregate)
    {
        (Polymorphic command, WorkItemState? state) = Command(reschedule);
        CommandEnvelope envelope = Envelope(command, tenant) with { Domain = domain, AggregateId = aggregate };

        TargetInvocationException error = await Should.ThrowAsync<TargetInvocationException>(() =>
            new WorkItemEventStoreAggregate().ProcessAsync(envelope, state, TestContext.Current.CancellationToken));

        error.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("addressed stream");
    }

    private static (Polymorphic Command, WorkItemState? State) Command(bool reschedule)
    {
        var tenant = new TenantId("tenant-ingress");
        var item = new WorkItemId("Item-Ingress");
        DateTimeOffset due = DateTimeOffset.UnixEpoch.AddDays(1);
        var expiry = new WorkItemExpirySchedule(due,
            ReminderIdentityCodec.ComputeScheduleToken(tenant.Value, item.Value, due, 1), 1);
        var create = new CreateWorkItem(tenant, item, "ingress proof", Expiry: expiry);
        if (!reschedule) return (create, null);
        var state = new WorkItemState();
        state.Apply(new WorkItemCreated(item.Value, 1, tenant, item, new Obligation("ingress proof")));
        return (new RescheduleWorkItem(tenant, item, new WorkItemSchedule(), Expiry: expiry), state);
    }

    private static CommandEnvelope Envelope(Polymorphic command, string tenant)
        => new("01ARZ3NDEKTSV4RRFFQ69G5FAV", tenant, "work", "Item-Ingress", command.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(command, command.GetType()), "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            null, "actor-ingress", null);
}
