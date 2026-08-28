using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic proof of Story 4.8's index-driven recovery discovery (AC #2/#3): the source enumerates the
/// durable registry, reads each tenant's index, and re-folds every candidate's per-aggregate stream for truth
/// (DD-3) — so a stale entry whose stream cleared is skipped — and it never issues the tenant-wide null-
/// <c>AggregateId</c> read the gateway 400-rejects. Also proves the unchanged <see cref="DateReminderReconciler"/>
/// stays idempotent over the new source. No Docker/Dapr/network.
/// </summary>
public sealed class IndexedPendingDateAwaitSourceTests
{
    private const string TenantA = "tenant-alpha";
    private const string WorkDue = "work-due";
    private const string WorkFuture = "work-future";

    private static readonly DateTimeOffset s_now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_future = new(2026, 7, 22, 12, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_past = new(2026, 7, 22, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Discovers_pending_awaits_from_registry_index_and_per_aggregate_refold()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkFuture, s_future)).ConfigureAwait(true);
        IEventStoreGatewayClient gateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [WorkFuture] = Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture), SuspendedOnDate(WorkFuture, s_future)),
        });

        IReadOnlyList<PendingDateAwait> pending = await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwait single = pending.ShouldHaveSingleItem();
        single.WorkItemId.ShouldBe(WorkFuture);
        single.Instant.ShouldBe(s_future);
    }

    [Fact]
    public async Task Skips_a_stale_index_entry_whose_stream_shows_the_await_cleared()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkFuture, s_future)).ConfigureAwait(true);
        IEventStoreGatewayClient gateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            // The index still lists WorkFuture, but the authoritative stream shows it already resumed.
            [WorkFuture] = Story48Streams.Page(
                TenantA,
                WorkFuture,
                Created(WorkFuture),
                SuspendedOnDate(WorkFuture, s_future),
                new WorkItemResumed(WorkFuture, 3, new TenantId(TenantA), new WorkItemId(WorkFuture), AwaitCondition.DateReached(s_future))),
        });

        IReadOnlyList<PendingDateAwait> pending = await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task Empty_registry_returns_nothing_without_reading_any_stream()
    {
        var store = new Story47InMemoryReadModelStore();
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();

        IReadOnlyList<PendingDateAwait> pending = await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        pending.ShouldBeEmpty();
        await gateway.DidNotReceive().ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Never_constructs_a_null_aggregate_stream_read()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        var requests = new List<StreamReadRequest>();
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                requests.Add(request);
                return Task.FromResult(request.AggregateId == WorkDue
                    ? Story48Streams.Page(TenantA, WorkDue, Created(WorkDue), SuspendedOnDate(WorkDue, s_past))
                    : Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture), SuspendedOnDate(WorkFuture, s_future)));
            });

        _ = await NewSource(store, gateway).GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        requests.Count.ShouldBe(2);
        requests.ShouldAllBe(request => !string.IsNullOrWhiteSpace(request.AggregateId));
        requests.Select(request => request.AggregateId).ShouldBe([WorkDue, WorkFuture], ignoreOrder: true);
    }

    [Fact]
    public async Task Advances_to_the_next_page_and_folds_the_complete_stream()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkFuture, s_future)).ConfigureAwait(true);
        var requests = new List<StreamReadRequest>();
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
            requests.Add(request);
            return Task.FromResult(request.FromSequence == 0
                ? Story48Streams.PageAt(TenantA, WorkFuture, 1, isTruncated: true, Created(WorkFuture))
                : Story48Streams.PageAt(TenantA, WorkFuture, 2, isTruncated: false, SuspendedOnDate(WorkFuture, s_future)));
        });

        PendingDateAwait result = (await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldHaveSingleItem();

        result.WorkItemId.ShouldBe(WorkFuture);
        requests.Select(static request => request.FromSequence).ShouldBe([0L, 2L]);
    }

    [Fact]
    public async Task Fails_closed_when_the_page_budget_ends_on_a_truncated_page()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkFuture, s_future)).ConfigureAwait(true);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Story48Streams.PageAt(TenantA, WorkFuture, 1, isTruncated: true, Created(WorkFuture)));
        var options = new WorksRecoveryOptions { MaxStreamPagesPerTenant = 1 };
        var source = new IndexedPendingDateAwaitSource(
            store,
            gateway,
            Options.Create(options),
            NullLogger<IndexedPendingDateAwaitSource>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => source
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task Fails_closed_when_a_known_lifecycle_event_is_malformed()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkFuture, s_future)).ConfigureAwait(true);
        StreamReadPage page = Story48Streams.Page(TenantA, WorkFuture, SuspendedOnDate(WorkFuture, s_future));
        StreamReadEvent malformed = page.Events[0] with { Payload = "{"u8.ToArray() };
        IEventStoreGatewayClient gateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [WorkFuture] = page with { Events = [malformed] },
        });

        await Should.ThrowAsync<InvalidOperationException>(() => NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task Rejects_a_page_or_payload_outside_the_requested_stream_identity()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkFuture, s_future)).ConfigureAwait(true);
        IEventStoreGatewayClient wrongDomainGateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [WorkFuture] = Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture)) with { Domain = "other" },
        });
        IEventStoreGatewayClient wrongPayloadGateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [WorkFuture] = Story48Streams.Page(TenantA, WorkFuture, Created("work-other")),
        });

        await Should.ThrowAsync<InvalidOperationException>(() => NewSource(store, wrongDomainGateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
        await Should.ThrowAsync<InvalidOperationException>(() => NewSource(store, wrongPayloadGateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task Reconciler_over_the_indexed_source_reissues_due_and_reschedules_future_idempotently()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        IEventStoreGatewayClient gateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [WorkDue] = Story48Streams.Page(TenantA, WorkDue, Created(WorkDue), SuspendedOnDate(WorkDue, s_past)),
            [WorkFuture] = Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture), SuspendedOnDate(WorkFuture, s_future)),
        });

        var scheduler = new Story48RecordingScheduler();
        var submitter = new Story48RecordingSubmitter();
        var reconciler = new DateReminderReconciler(
            NewSource(store, gateway),
            scheduler,
            submitter,
            new Story48FixedTimeProvider(s_now),
            NullLogger<DateReminderReconciler>.Instance);

        _ = await reconciler.ReconcileAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await reconciler.ReconcileAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Due await reissued at-least-once across the two passes, but always the same deterministic resume command.
        submitter.Submissions.ShouldNotBeEmpty();
        submitter.Submissions.Select(submission => submission.CorrelationId).Distinct().Count().ShouldBe(1);

        // Future await rescheduled deterministically (one distinct reminder name across the passes).
        scheduler.Calls.ShouldNotBeEmpty();
        scheduler.Calls
            .Select(call => DateReminderName.For(call.Await.TenantId, call.Await.WorkItemId, call.Await.CorrelationKey))
            .Distinct()
            .Count()
            .ShouldBe(1);
        scheduler.Calls.ShouldAllBe(call => call.Await.WorkItemId == WorkFuture);
    }

    private static IndexedPendingDateAwaitSource NewSource(IReadModelStore store, IEventStoreGatewayClient gateway)
        => new(store, gateway, Options.Create(new WorksRecoveryOptions()), NullLogger<IndexedPendingDateAwaitSource>.Instance);

    private static async Task SeedAsync(IReadModelStore store, string tenant, params (string WorkItemId, DateTimeOffset Instant)[] entries)
    {
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { tenant } };
        var index = new PendingDateAwaitTenantIndex();
        foreach ((string workItemId, DateTimeOffset instant) in entries)
        {
            index.Entries[workItemId] = [new PendingDateAwait(tenant, workItemId, instant, AwaitCondition.DateReached(instant).CorrelationKey)];
        }

        await store.SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey, registry, TestContext.Current.CancellationToken).ConfigureAwait(false);
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(tenant), index, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static IEventStoreGatewayClient GatewayFor(IReadOnlyDictionary<string, StreamReadPage> pagesByAggregate)
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string? aggregateId = call.ArgAt<StreamReadRequest>(0).AggregateId;
                return Task.FromResult(pagesByAggregate[aggregateId!]);
            });
        return gateway;
    }

    private static WorkItemCreated Created(string workId)
        => new(workId, 1, new TenantId(TenantA), new WorkItemId(workId), new Obligation("Do the thing"));

    private static WorkItemSuspended SuspendedOnDate(string workId, DateTimeOffset instant)
        => new(workId, 2, new TenantId(TenantA), new WorkItemId(workId), [AwaitCondition.DateReached(instant)]);
}
