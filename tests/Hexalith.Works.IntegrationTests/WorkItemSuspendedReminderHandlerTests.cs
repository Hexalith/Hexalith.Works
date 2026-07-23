using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic proof of Story 4.8's steady-state suspend trigger (AC #1): the subscription handler registers a
/// durable date reminder for each currently-pending <c>DateReached</c> await, derived from the folded stream (not
/// the raw event — DD-1), and registers nothing for non-date suspensions or an item that has since resumed. No
/// Docker/Dapr/network — the gateway is an NSubstitute fake and the scheduler records.
/// </summary>
public sealed class WorkItemSuspendedReminderHandlerTests
{
    private static readonly TenantId s_tenant = new("tenant-alpha");
    private static readonly WorkItemId s_workItem = new("work-1");
    private static readonly DateTimeOffset s_now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_future = new(2026, 7, 22, 12, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_past = new(2026, 7, 22, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Registers_a_reminder_for_a_pending_future_date_await()
    {
        var scheduler = new Story48RecordingScheduler();
        IEventStoreGatewayClient gateway = GatewayReturning(Story48Streams.Page(
            s_tenant.Value,
            s_workItem.Value,
            Created(),
            SuspendedOnDate(s_future)));

        await NewHandler(gateway, scheduler).HandleAsync(
            new WorkItemSuspended(s_workItem.Value, 2, s_tenant, s_workItem, [AwaitCondition.DateReached(s_future)]),
            Context(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        (PendingDateAwait Await, TimeSpan DueTime) call = scheduler.Calls.ShouldHaveSingleItem();
        call.Await.WorkItemId.ShouldBe(s_workItem.Value);
        call.Await.Instant.ShouldBe(s_future);
        call.DueTime.ShouldBe(s_future - s_now);
    }

    [Fact]
    public async Task Registers_with_zero_due_time_for_an_already_due_await()
    {
        var scheduler = new Story48RecordingScheduler();
        IEventStoreGatewayClient gateway = GatewayReturning(Story48Streams.Page(
            s_tenant.Value,
            s_workItem.Value,
            Created(),
            SuspendedOnDate(s_past)));

        await NewHandler(gateway, scheduler).HandleAsync(
            new WorkItemSuspended(s_workItem.Value, 2, s_tenant, s_workItem, [AwaitCondition.DateReached(s_past)]),
            Context(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        scheduler.Calls.ShouldHaveSingleItem().DueTime.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Registers_nothing_for_a_non_date_suspension()
    {
        var scheduler = new Story48RecordingScheduler();
        IEventStoreGatewayClient gateway = GatewayReturning(Story48Streams.Page(
            s_tenant.Value,
            s_workItem.Value,
            Created(),
            new WorkItemSuspended(s_workItem.Value, 2, s_tenant, s_workItem, [AwaitCondition.ExternalSignal("approval")])));

        await NewHandler(gateway, scheduler).HandleAsync(
            new WorkItemSuspended(s_workItem.Value, 2, s_tenant, s_workItem, [AwaitCondition.ExternalSignal("approval")]),
            Context(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        scheduler.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Registers_nothing_when_the_item_has_since_resumed()
    {
        var scheduler = new Story48RecordingScheduler();
        IEventStoreGatewayClient gateway = GatewayReturning(Story48Streams.Page(
            s_tenant.Value,
            s_workItem.Value,
            Created(),
            SuspendedOnDate(s_future),
            new WorkItemResumed(s_workItem.Value, 3, s_tenant, s_workItem, AwaitCondition.DateReached(s_future))));

        // A stale/replayed WorkItemSuspended arrives, but the folded stream shows it already resumed: register nothing.
        await NewHandler(gateway, scheduler).HandleAsync(
            new WorkItemSuspended(s_workItem.Value, 2, s_tenant, s_workItem, [AwaitCondition.DateReached(s_future)]),
            Context(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        scheduler.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reads_only_the_suspended_aggregate_with_a_non_null_aggregate_id()
    {
        var scheduler = new Story48RecordingScheduler();
        var requests = new List<StreamReadRequest>();
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requests.Add(call.ArgAt<StreamReadRequest>(0));
                return Task.FromResult(Story48Streams.Page(s_tenant.Value, s_workItem.Value, Created(), SuspendedOnDate(s_future)));
            });

        await NewHandler(gateway, scheduler).HandleAsync(
            new WorkItemSuspended(s_workItem.Value, 2, s_tenant, s_workItem, [AwaitCondition.DateReached(s_future)]),
            Context(),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        requests.ShouldNotBeEmpty();
        requests.ShouldAllBe(request => !string.IsNullOrWhiteSpace(request.AggregateId));
        requests.ShouldAllBe(request => request.AggregateId == s_workItem.Value);
    }

    [Fact]
    public async Task Propagates_gateway_failure_so_the_subscription_marker_stays_retryable()
    {
        var scheduler = new Story48RecordingScheduler();
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamReadPage>(new HttpRequestException("gateway unavailable")));

        _ = await Should.ThrowAsync<HttpRequestException>(() => NewHandler(gateway, scheduler).HandleAsync(
            new WorkItemSuspended(s_workItem.Value, 2, s_tenant, s_workItem, [AwaitCondition.DateReached(s_future)]),
            Context(),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);
        scheduler.Calls.ShouldBeEmpty();
    }

    private static WorkItemSuspendedReminderHandler NewHandler(IEventStoreGatewayClient gateway, IDateReminderScheduler scheduler)
        => new(
            gateway,
            scheduler,
            new Story48FixedTimeProvider(s_now),
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<WorkItemSuspendedReminderHandler>.Instance);

    private static IEventStoreGatewayClient GatewayReturning(StreamReadPage page)
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page));
        return gateway;
    }

    private static WorkItemCreated Created()
        => new(s_workItem.Value, 1, s_tenant, s_workItem, new Obligation("Do the thing"));

    private static WorkItemSuspended SuspendedOnDate(DateTimeOffset instant)
        => new(s_workItem.Value, 2, s_tenant, s_workItem, [AwaitCondition.DateReached(instant)]);

    private static EventStoreDomainEventContext Context()
        => new(s_tenant.Value, s_workItem.Value, "01JBACKMESSAGEID0000000000", 2, s_now, "correlation-1");
}
