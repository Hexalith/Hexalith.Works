using System.Text.Json;

using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.State;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;

using Shouldly;

namespace Hexalith.Works.UnitTests;

/// <summary>Expiry is a persisted upstream decision with exact clock-free schedule validation.</summary>
public sealed class WorkItemExpiryWitnessTests
{
    private static readonly TenantId s_tenant = new("tenant-expiry");
    private static readonly WorkItemId s_item = new("item-expiry");
    private static readonly DateTimeOffset s_due = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Create, reschedule, replay, and expiry retain the exact supplied witness.</summary>
    [Fact]
    public void CurrentWitnessExpiresAndStaleWitnessCannotExpireRescheduledWork()
    {
        WorkItemExpirySchedule first = Witness(1);
        WorkItemCreated created = (WorkItemCreated)WorkItemAggregate.Handle(
            new CreateWorkItem(s_tenant, s_item, "proof", Expiry: first), null).Events.Single();
        var state = new WorkItemState();
        state.Apply(created);
        state.Expiry.ShouldBe(first);
        var schedule = new WorkItemSchedule();
        WorkItemExpirySchedule second = Witness(2);
        WorkItemRescheduled rescheduled = (WorkItemRescheduled)WorkItemAggregate.Handle(
            new RescheduleWorkItem(s_tenant, s_item, schedule, Expiry: second), state).Events.Single();
        state.Apply(rescheduled);
        state.Expiry.ShouldBe(second);

        WorkItemAggregate.Handle(new ExpireWorkItem(s_tenant, s_item, first), state).IsRejection.ShouldBeTrue();
        WorkItemAggregate.Handle(new ExpireWorkItem(s_tenant, s_item), state).IsRejection.ShouldBeTrue();
        WorkItemExpired expired = (WorkItemExpired)WorkItemAggregate.Handle(
            new ExpireWorkItem(s_tenant, s_item, second), state).Events.Single();
        expired.ExpiryWitness.ShouldBe(second);
        state.Apply(expired);
        state.Status.ShouldBe(WorkItemStatus.Expired);
    }

    /// <summary>Mixed-case caller tenants normalize before the SDK's canonical lowercase schedule codec.</summary>
    [Fact]
    public void MixedCaseTenantCreatesReplaysAndExpiresWithCanonicalWitness()
    {
        var tenant = new TenantId("Tenant-Expiry");
        tenant.Value.ShouldBe("tenant-expiry");
        Should.Throw<ArgumentException>(() => ReminderIdentityCodec.ComputeScheduleToken("Tenant-Expiry", s_item.Value, s_due, 1));
        var witness = new WorkItemExpirySchedule(s_due,
            ReminderIdentityCodec.ComputeScheduleToken("tenant-expiry", s_item.Value, s_due, 1), 1);
        witness.IsValidFor(tenant, s_item).ShouldBeTrue();
        WorkItemCreated created = (WorkItemCreated)WorkItemAggregate.Handle(
            new CreateWorkItem(tenant, s_item, "mixed-case proof", Expiry: witness), null).Events.Single();
        var state = new WorkItemState();
        state.Apply(JsonSerializer.Deserialize<WorkItemCreated>(JsonSerializer.Serialize(created))!);
        state.TenantId.ShouldBe(tenant);
        state.Expiry.ShouldBe(witness);
        WorkItemExpired expired = (WorkItemExpired)WorkItemAggregate.Handle(
            new ExpireWorkItem(new TenantId("TENANT-EXPIRY"), s_item, witness), state).Events.Single();
        expired.ExpiryWitness.ShouldBe(witness);
        state.Apply(JsonSerializer.Deserialize<WorkItemExpired>(JsonSerializer.Serialize(expired))!);
        state.Status.ShouldBe(WorkItemStatus.Expired);
        state.Expiry.ShouldBe(witness);
    }

    /// <summary>A wrong token or identity cannot introduce a witnessed schedule.</summary>
    [Theory]
    [InlineData("token")]
    [InlineData("tenant")]
    [InlineData("revision")]
    public void InvalidWitnessIsRejectedBeforeCreate(string failure)
    {
        WorkItemExpirySchedule witness = Witness(1);
        witness = failure switch
        {
            "token" => witness with { ScheduleToken = "altered" },
            "tenant" => witness with { ScheduleToken = ReminderIdentityCodec.ComputeScheduleToken("other-tenant", s_item.Value, s_due, 1) },
            _ => witness with { Revision = 0 },
        };
        WorkItemAggregate.Handle(new CreateWorkItem(s_tenant, s_item, "proof", Expiry: witness), null)
            .IsRejection.ShouldBeTrue();
    }

    /// <summary>Historical creates infer no due-date or TTL expiry and keep historical command serialization.</summary>
    [Fact]
    public void HistoricalPayloadsRemainUnwitnessedWithoutInventedExpiry()
    {
        var command = new CreateWorkItem(s_tenant, s_item, "historical");
        JsonSerializer.Serialize(command).ShouldNotContain("\"Expiry\":");
        WorkItemCreated created = (WorkItemCreated)WorkItemAggregate.Handle(command, null).Events.Single();
        JsonSerializer.Serialize(created).ShouldNotContain("\"Expiry\":");
        var state = new WorkItemState();
        state.Apply(created);
        state.Expiry.ShouldBeNull();
        WorkItemAggregate.Handle(new ExpireWorkItem(s_tenant, s_item), state).IsSuccess.ShouldBeTrue();
    }

    /// <summary>An established expiry cannot be dropped or replaced by an equal or earlier revision.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RescheduleCannotDropOrRewindEstablishedWitness(int revision)
    {
        var state = new WorkItemState();
        WorkItemExpirySchedule original = Witness(2);
        state.Apply((WorkItemCreated)WorkItemAggregate.Handle(new CreateWorkItem(s_tenant, s_item, "proof", Expiry: original), null).Events.Single());
        var command = new RescheduleWorkItem(s_tenant, s_item, new WorkItemSchedule(), Expiry: revision == 0 ? null : Witness(revision));
        var result = WorkItemAggregate.Handle(command, state);
        result.IsRejection.ShouldBeTrue();
        result.Events.ShouldNotContain(fact => fact is WorkItemRescheduled);
        state.Expiry.ShouldBe(original);
        state.Sequence.ShouldBe(1);
    }

    private static WorkItemExpirySchedule Witness(long revision)
        => new(s_due, ReminderIdentityCodec.ComputeScheduleToken(s_tenant.Value, s_item.Value, s_due, revision), revision);
}
