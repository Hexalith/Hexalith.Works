using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
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

        PendingDateAwaitScanResult scan = await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        PendingDateAwait single = scan.Pending.ShouldHaveSingleItem();
        single.WorkItemId.ShouldBe(WorkFuture);
        single.Instant.ShouldBe(s_future);
        scan.SkippedParkedCount.ShouldBe(0);
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

        PendingDateAwaitScanResult scan = await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        scan.Pending.ShouldBeEmpty();
        scan.SkippedParkedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Empty_registry_returns_nothing_without_reading_any_stream()
    {
        var store = new Story47InMemoryReadModelStore();
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();

        PendingDateAwaitScanResult scan = await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        scan.Pending.ShouldBeEmpty();
        scan.SkippedParkedCount.ShouldBe(0);
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
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true)).Pending.ShouldHaveSingleItem();

        result.WorkItemId.ShouldBe(WorkFuture);
        // FromSequence is an EXCLUSIVE lower bound, so the next page starts at the last sequence returned —
        // last + 1 would skip the event at that sequence entirely.
        requests.Select(static request => request.FromSequence).ShouldBe([0L, 1L]);
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

        // A per-aggregate fail-closed guard is isolated to that candidate: the tenant itself scanned, so the
        // failure is reported as a failed candidate rather than propagating as a raw InvalidOperationException.
        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => source.GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);
        thrown.FailedTenantCount.ShouldBe(0);
        thrown.FailedCandidateCount.ShouldBe(1);
        thrown.InnerException.ShouldBeOfType<InvalidOperationException>();
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

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway)
                .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);
        thrown.FailedCandidateCount.ShouldBe(1);
        thrown.InnerException.ShouldBeOfType<InvalidOperationException>();
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

        (await Should.ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, wrongDomainGateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true))
            .InnerException.ShouldBeOfType<InvalidOperationException>();
        (await Should.ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, wrongPayloadGateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true))
            .InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Fails_closed_immediately_when_a_truncated_page_reports_no_last_sequence_instead_of_stalling_the_cursor()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkFuture, s_future)).ConfigureAwait(true);
        int callCount = 0;
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return Task.FromResult(Story48Streams.PageAt(TenantA, WorkFuture, 1, isTruncated: true));
            });
        var options = new WorksRecoveryOptions { MaxStreamPagesPerTenant = 5 };
        var source = new IndexedPendingDateAwaitSource(
            store,
            gateway,
            Options.Create(options),
            NullLogger<IndexedPendingDateAwaitSource>.Instance);

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => source.GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.InnerException.ShouldNotBeNull().Message.ShouldContain("no last sequence returned");
        callCount.ShouldBe(1, "The reader must fail closed on the first truncated-with-no-cursor page rather than looping until the page budget is exhausted.");
    }

    [Fact]
    public async Task Isolates_a_single_candidate_scan_failure_and_still_returns_partial_results_from_the_others()
    {
        const string TenantB = "tenant-beta";
        var store = new Story47InMemoryReadModelStore();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, TenantB } };
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey, registry, TestContext.Current.CancellationToken).ConfigureAwait(true);

        var indexA = new PendingDateAwaitTenantIndex();
        indexA.Entries[WorkFuture] = [new PendingDateAwait(TenantA, WorkFuture, s_future, AwaitCondition.DateReached(s_future).CorrelationKey)];
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(TenantA), indexA, TestContext.Current.CancellationToken).ConfigureAwait(true);

        var indexB = new PendingDateAwaitTenantIndex();
        indexB.Entries[WorkDue] = [new PendingDateAwait(TenantB, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey)];
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(TenantB), indexB, TestContext.Current.CancellationToken).ConfigureAwait(true);

        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                return string.Equals(request.Tenant, TenantB, StringComparison.Ordinal)
                    ? Task.FromException<StreamReadPage>(new InvalidOperationException("simulated gateway failure"))
                    : Task.FromResult(Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture), SuspendedOnDate(WorkFuture, s_future)));
            });

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway).GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.FailedCandidateCount.ShouldBe(1);
        PendingDateAwait onlyResult = thrown.PartialResults.ShouldHaveSingleItem();
        onlyResult.TenantId.ShouldBe(TenantA);
        onlyResult.WorkItemId.ShouldBe(WorkFuture);
    }

    [Fact]
    public async Task Isolates_a_single_tenant_index_read_failure_and_still_scans_the_other_tenants()
    {
        const string TenantB = "tenant-beta";
        var store = new Story47InMemoryReadModelStore();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, TenantB } };
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey, registry, TestContext.Current.CancellationToken).ConfigureAwait(true);

        var indexA = new PendingDateAwaitTenantIndex();
        indexA.Entries[WorkFuture] = [new PendingDateAwait(TenantA, WorkFuture, s_future, AwaitCondition.DateReached(s_future).CorrelationKey)];
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(TenantA), indexA, TestContext.Current.CancellationToken).ConfigureAwait(true);

        // TenantB's own index document cannot be read at all: the tenant, not one candidate, fails.
        store.FailNextGets(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(TenantB));

        IEventStoreGatewayClient gateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [WorkFuture] = Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture), SuspendedOnDate(WorkFuture, s_future)),
        });

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway).GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(1);
        thrown.FailedCandidateCount.ShouldBe(0);
        thrown.PartialResults.ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task Keeps_the_readable_candidates_of_a_tenant_when_one_candidate_stream_is_unreadable()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                return string.Equals(request.AggregateId, WorkDue, StringComparison.Ordinal)
                    ? Task.FromException<StreamReadPage>(new InvalidOperationException("simulated wedged aggregate"))
                    : Task.FromResult(Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture), SuspendedOnDate(WorkFuture, s_future)));
            });

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway).GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        // One wedged work item must not collapse recovery for the rest of its own tenant.
        thrown.FailedTenantCount.ShouldBe(0);
        thrown.FailedCandidateCount.ShouldBe(1);
        thrown.PartialResults.ShouldHaveSingleItem().WorkItemId.ShouldBe(WorkFuture);
    }

    [Fact]
    public async Task Skips_a_parked_candidate_without_reading_its_stream_or_marking_the_scan_incomplete()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        await store
            .SaveAsync(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkDue),
                new WorkItemProjectionParking { FailedSequence = 2, FailureCount = 5, Parked = true },
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        var requests = new List<StreamReadRequest>();
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                requests.Add(request);
                return string.Equals(request.AggregateId, WorkDue, StringComparison.Ordinal)
                    ? Task.FromException<StreamReadPage>(new InvalidOperationException("parked stream must not be read"))
                    : Task.FromResult(Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture), SuspendedOnDate(WorkFuture, s_future)));
            });

        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();
        PendingDateAwaitScanResult scan = await NewSource(store, gateway, logger)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        scan.Pending.ShouldHaveSingleItem().WorkItemId.ShouldBe(WorkFuture);
        scan.SkippedParkedCount.ShouldBe(1);
        requests.ShouldAllBe(request => request.AggregateId == WorkFuture);
        var parkedLog = logger.Entries
            .Where(entry => entry.EventId.Id == 4607)
            .ShouldHaveSingleItem();
        parkedLog.Level.ShouldBe(LogLevel.Warning);
        parkedLog.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task Aggregates_multiple_parked_candidates_into_the_clean_scan_count()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        foreach (string workItemId in new[] { WorkDue, WorkFuture })
        {
            await store
                .SaveAsync(
                    WorksReadModelKeys.StateStoreName,
                    WorksReadModelKeys.ProjectionParkingKey(TenantA, workItemId),
                    new WorkItemProjectionParking { FailedSequence = 2, FailureCount = 5, Parked = true },
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
        }

        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();

        PendingDateAwaitScanResult scan = await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        scan.Pending.ShouldBeEmpty();
        scan.SkippedParkedCount.ShouldBe(2);
        await gateway.DidNotReceive().ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aggregates_parked_candidates_across_tenants_into_the_clean_scan_count()
    {
        const string tenantB = "tenant-beta";
        var store = new Story47InMemoryReadModelStore();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB } };
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitRegistryKey,
            registry,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        foreach (string tenant in new[] { TenantA, tenantB })
        {
            var index = new PendingDateAwaitTenantIndex();
            index.Entries[WorkDue] =
            [
                new PendingDateAwait(tenant, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey),
            ];
            await store.SaveAsync(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(tenant),
                index,
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            await store.SaveAsync(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(tenant, WorkDue),
                new WorkItemProjectionParking { FailedSequence = 2, FailureCount = 5, Parked = true },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();

        PendingDateAwaitScanResult scan = await NewSource(store, gateway)
            .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        scan.Pending.ShouldBeEmpty();
        scan.SkippedParkedCount.ShouldBe(2);
        await gateway.DidNotReceive().ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Carries_the_cross_tenant_parked_count_when_another_candidate_makes_the_scan_incomplete()
    {
        const string tenantB = "tenant-beta";
        var store = new Story47InMemoryReadModelStore();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB } };
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitRegistryKey,
            registry,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var indexA = new PendingDateAwaitTenantIndex();
        indexA.Entries[WorkDue] =
        [
            new PendingDateAwait(TenantA, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey),
        ];
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(TenantA),
            indexA,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var indexB = new PendingDateAwaitTenantIndex();
        indexB.Entries[WorkDue] =
        [
            new PendingDateAwait(tenantB, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey),
        ];
        indexB.Entries[WorkFuture] =
        [
            new PendingDateAwait(tenantB, WorkFuture, s_future, AwaitCondition.DateReached(s_future).CorrelationKey),
        ];
        await store.SaveAsync(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(tenantB),
            indexB,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        foreach (string tenant in new[] { TenantA, tenantB })
        {
            await store.SaveAsync(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(tenant, WorkDue),
                new WorkItemProjectionParking { FailedSequence = 2, FailureCount = 5, Parked = true },
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamReadPage>(new InvalidOperationException("simulated stream failure")));

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway)
                .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.SkippedParkedCount.ShouldBe(2);
        thrown.FailedCandidateCount.ShouldBe(1);
        await gateway.Received(1).ReadStreamAsync(
            Arg.Is<StreamReadRequest>(request => request.Tenant == tenantB && request.AggregateId == WorkFuture),
            Arg.Any<CancellationToken>());
        await gateway.DidNotReceive().ReadStreamAsync(
            Arg.Is<StreamReadRequest>(request => request.AggregateId == WorkDue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Carries_the_parked_skip_count_when_another_candidate_makes_the_scan_incomplete()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        await store
            .SaveAsync(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkDue),
                new WorkItemProjectionParking { FailedSequence = 2, FailureCount = 5, Parked = true },
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamReadPage>(new InvalidOperationException("simulated stream failure")));

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway)
                .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.SkippedParkedCount.ShouldBe(1);
        thrown.FailedCandidateCount.ShouldBe(1);
        await gateway.Received(1).ReadStreamAsync(
            Arg.Is<StreamReadRequest>(request => request.AggregateId == WorkFuture),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Parking_lookup_failure_is_classified_while_later_candidates_remain_partial_results()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        store.FailNextGets(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkDue));
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                return string.Equals(request.AggregateId, WorkDue, StringComparison.Ordinal)
                    ? Task.FromException<StreamReadPage>(new InvalidOperationException("failed parking candidate stream must not be read"))
                    : Task.FromResult(Story48Streams.Page(
                        TenantA,
                        WorkFuture,
                        Created(WorkFuture),
                        SuspendedOnDate(WorkFuture, s_future)));
            });
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway, logger)
                .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(0);
        thrown.FailedCandidateCount.ShouldBe(1);
        thrown.SkippedParkedCount.ShouldBe(0);
        thrown.PartialResults.ShouldHaveSingleItem().WorkItemId.ShouldBe(WorkFuture);
        await gateway.DidNotReceive().ReadStreamAsync(
            Arg.Is<StreamReadRequest>(request => request.AggregateId == WorkDue),
            Arg.Any<CancellationToken>());
        await gateway.Received(1).ReadStreamAsync(
            Arg.Is<StreamReadRequest>(request => request.AggregateId == WorkFuture),
            Arg.Any<CancellationToken>());
        var parkingLog = logger.Entries
            .Where(entry => entry.EventId.Id == 4608)
            .ShouldHaveSingleItem();
        parkingLog.Level.ShouldBe(LogLevel.Warning);
        parkingLog.Message.ShouldContain(nameof(InvalidOperationException));
        parkingLog.Message.ShouldNotContain("Injected read failure");
        parkingLog.Exception.ShouldBeOfType<InvalidOperationException>();
        parkingLog.Properties["TenantId"].ShouldBe(TenantA);
        parkingLog.Properties["WorkItemId"].ShouldBe(WorkDue);
        parkingLog.Properties["Reason"].ShouldBe(nameof(InvalidOperationException));
        logger.Entries.ShouldNotContain(entry => entry.EventId.Id == 4606);
    }

    [Fact]
    public async Task Clean_shutdown_between_tenants_preserves_the_exact_caller_cancellation()
    {
        const string tenantB = "tenant-beta";
        using var callerCancellation = new CancellationTokenSource();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB } };
        string scannedTenant = registry.Tenants.First();
        string unreachedTenant = registry.Tenants.Single(tenant => tenant != scannedTenant);
        store.GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantRegistry>(registry, "1")));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(scannedTenant),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callerCancellation.Cancel();
                return Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantIndex>(null, null));
            });

        OperationCanceledException thrown = await Should
            .ThrowAsync<OperationCanceledException>(() => NewSource(
                store,
                Substitute.For<IEventStoreGatewayClient>()).GetPendingDateAwaitsAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.CancellationToken.ShouldBe(callerCancellation.Token);
        await store.DidNotReceive().GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clean_exact_caller_cancellation_from_the_final_tenant_index_propagates_without_warning()
    {
        const string tenantB = "tenant-beta";
        using var callerCancellation = new CancellationTokenSource();
        CancellationToken callerToken = callerCancellation.Token;
        var expected = new OperationCanceledException(callerToken);
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB } };
        string[] orderedTenants = [.. registry.Tenants];
        string emptyTenant = orderedTenants[0];
        string cancellationTenant = orderedTenants[1];
        store.GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                Arg.Is<CancellationToken>(token => token == callerToken))
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantRegistry>(registry, "1")));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(emptyTenant),
                Arg.Is<CancellationToken>(token => token == callerToken))
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantIndex>(null, null)));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(cancellationTenant),
                Arg.Is<CancellationToken>(token => token == callerToken))
            .Returns(_ =>
            {
                callerCancellation.Cancel();
                return Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(expected);
            });
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        OperationCanceledException? caught = null;
        try
        {
            _ = await NewSource(
                store,
                Substitute.For<IEventStoreGatewayClient>(),
                logger).GetPendingDateAwaitsAsync(callerToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            caught = ex;
        }

        OperationCanceledException thrown = caught.ShouldNotBeNull();
        thrown.ShouldBeSameAs(expected);
        thrown.CancellationToken.ShouldBe(callerToken);
        await store.Received(1).GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(cancellationTenant),
            Arg.Is<CancellationToken>(token => token == callerToken));
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Shutdown_after_a_recorded_tenant_failure_preserves_typed_evidence_and_stops_before_the_next_tenant()
    {
        const string tenantB = "tenant-beta";
        const string tenantC = "tenant-charlie";
        using var callerCancellation = new CancellationTokenSource();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB, tenantC } };
        string[] orderedTenants = [.. registry.Tenants];
        string successfulTenant = orderedTenants[0];
        string failingTenant = orderedTenants[1];
        string unreachedTenant = orderedTenants[2];
        var successfulIndex = new PendingDateAwaitTenantIndex();
        successfulIndex.Entries[WorkDue] =
        [
            new PendingDateAwait(successfulTenant, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey),
        ];
        store.GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantRegistry>(registry, "1")));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(successfulTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantIndex>(successfulIndex, "1")));
        store.GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(successfulTenant, WorkDue),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<WorkItemProjectionParking>(null, null)));
        var expected = new InvalidOperationException("simulated tenant-index failure");
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(failingTenant),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callerCancellation.Cancel();
                return Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(expected);
            });
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(
                new InvalidOperationException("later tenant must not be read")));
        IEventStoreGatewayClient gateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [WorkDue] = Story48Streams.Page(
                successfulTenant,
                WorkDue,
                new WorkItemCreated(
                    WorkDue,
                    1,
                    new TenantId(successfulTenant),
                    new WorkItemId(WorkDue),
                    new Obligation("Preserve this partial result")),
                new WorkItemSuspended(
                    WorkDue,
                    2,
                    new TenantId(successfulTenant),
                    new WorkItemId(WorkDue),
                    [AwaitCondition.DateReached(s_past)])),
        });
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(
                store,
                gateway,
                logger).GetPendingDateAwaitsAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(1);
        thrown.FailedCandidateCount.ShouldBe(0);
        PendingDateAwait partial = thrown.PartialResults.ShouldHaveSingleItem();
        partial.TenantId.ShouldBe(successfulTenant);
        partial.WorkItemId.ShouldBe(WorkDue);
        thrown.InnerException.ShouldBeSameAs(expected);
        var tenantLog = logger.Entries.Where(entry => entry.EventId.Id == 4604).ShouldHaveSingleItem();
        tenantLog.Exception.ShouldBeSameAs(expected);
        tenantLog.Message.ShouldContain("unless shutdown stops the scan");
        await store.DidNotReceive().GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exact_caller_cancellation_from_the_next_tenant_index_preserves_earlier_cross_tenant_evidence()
    {
        const string tenantB = "tenant-beta";
        const string tenantC = "tenant-charlie";
        const string tenantD = "tenant-delta";
        using var callerCancellation = new CancellationTokenSource();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB, tenantC, tenantD } };
        string[] orderedTenants = [.. registry.Tenants];
        string successfulTenant = orderedTenants[0];
        string failingTenant = orderedTenants[1];
        string cancellationTenant = orderedTenants[2];
        string unreachedTenant = orderedTenants[3];
        var successfulIndex = new PendingDateAwaitTenantIndex();
        successfulIndex.Entries[WorkDue] =
        [
            new PendingDateAwait(successfulTenant, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey),
        ];
        store.GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantRegistry>(registry, "1")));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(successfulTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantIndex>(successfulIndex, "1")));
        store.GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(successfulTenant, WorkDue),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<WorkItemProjectionParking>(null, null)));
        var expected = new InvalidOperationException("simulated tenant-index failure");
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(failingTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(expected));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(cancellationTenant),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callerCancellation.Cancel();
                return Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(
                    new OperationCanceledException(callerCancellation.Token));
            });
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(
                new InvalidOperationException("later tenant must not be read")));
        IEventStoreGatewayClient gateway = GatewayFor(new Dictionary<string, StreamReadPage>(StringComparer.Ordinal)
        {
            [WorkDue] = Story48Streams.Page(
                successfulTenant,
                WorkDue,
                new WorkItemCreated(
                    WorkDue,
                    1,
                    new TenantId(successfulTenant),
                    new WorkItemId(WorkDue),
                    new Obligation("Preserve this partial result")),
                new WorkItemSuspended(
                    WorkDue,
                    2,
                    new TenantId(successfulTenant),
                    new WorkItemId(WorkDue),
                    [AwaitCondition.DateReached(s_past)])),
        });
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway, logger)
                .GetPendingDateAwaitsAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(1);
        thrown.FailedCandidateCount.ShouldBe(0);
        PendingDateAwait partial = thrown.PartialResults.ShouldHaveSingleItem();
        partial.TenantId.ShouldBe(successfulTenant);
        partial.WorkItemId.ShouldBe(WorkDue);
        thrown.InnerException.ShouldBeSameAs(expected);
        logger.Entries.Where(entry => entry.EventId.Id == 4604).ShouldHaveSingleItem().Exception.ShouldBeSameAs(expected);
        await store.Received(1).GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(cancellationTenant),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exact_caller_cancellation_from_the_next_tenant_index_preserves_earlier_candidate_failure_evidence()
    {
        const string tenantB = "tenant-beta";
        const string tenantC = "tenant-charlie";
        using var callerCancellation = new CancellationTokenSource();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB, tenantC } };
        string[] orderedTenants = [.. registry.Tenants];
        string scannedTenant = orderedTenants[0];
        string cancellationTenant = orderedTenants[1];
        string unreachedTenant = orderedTenants[2];
        var index = new PendingDateAwaitTenantIndex();
        index.Entries[WorkDue] =
        [
            new PendingDateAwait(scannedTenant, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey),
        ];
        index.Entries[WorkFuture] =
        [
            new PendingDateAwait(scannedTenant, WorkFuture, s_future, AwaitCondition.DateReached(s_future).CorrelationKey),
        ];
        store.GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantRegistry>(registry, "1")));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(scannedTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantIndex>(index, "1")));
        store.GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<WorkItemProjectionParking>(null, null)));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(cancellationTenant),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callerCancellation.Cancel();
                return Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(
                    new OperationCanceledException(callerCancellation.Token));
            });
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(
                new InvalidOperationException("later tenant must not be read")));
        var expected = new InvalidOperationException("simulated candidate-stream failure");
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                return string.Equals(request.AggregateId, WorkFuture, StringComparison.Ordinal)
                    ? Task.FromException<StreamReadPage>(expected)
                    : Task.FromResult(Story48Streams.Page(
                        scannedTenant,
                        WorkDue,
                        new WorkItemCreated(
                            WorkDue,
                            1,
                            new TenantId(scannedTenant),
                            new WorkItemId(WorkDue),
                            new Obligation("Preserve this candidate")),
                        new WorkItemSuspended(
                            WorkDue,
                            2,
                            new TenantId(scannedTenant),
                            new WorkItemId(WorkDue),
                            [AwaitCondition.DateReached(s_past)])));
            });
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway, logger)
                .GetPendingDateAwaitsAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(0);
        thrown.FailedCandidateCount.ShouldBe(1);
        PendingDateAwait partial = thrown.PartialResults.ShouldHaveSingleItem();
        partial.TenantId.ShouldBe(scannedTenant);
        partial.WorkItemId.ShouldBe(WorkDue);
        thrown.InnerException.ShouldBeSameAs(expected);
        logger.Entries.Where(entry => entry.EventId.Id == 4604).ShouldBeEmpty();
        logger.Entries.Where(entry => entry.EventId.Id == 4606).ShouldHaveSingleItem().Exception.ShouldBeSameAs(expected);
        await store.Received(1).GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(cancellationTenant),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Shutdown_after_a_candidate_failure_preserves_partial_results_and_stops_before_the_next_tenant()
    {
        const string tenantB = "tenant-beta";
        using var callerCancellation = new CancellationTokenSource();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB } };
        string scannedTenant = registry.Tenants.First();
        string unreachedTenant = registry.Tenants.Single(tenant => tenant != scannedTenant);
        var index = new PendingDateAwaitTenantIndex();
        index.Entries[WorkDue] =
        [
            new PendingDateAwait(scannedTenant, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey),
        ];
        index.Entries[WorkFuture] =
        [
            new PendingDateAwait(scannedTenant, WorkFuture, s_future, AwaitCondition.DateReached(s_future).CorrelationKey),
        ];
        store.GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantRegistry>(registry, "1")));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(scannedTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantIndex>(index, "1")));
        store.GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<WorkItemProjectionParking>(null, null)));
        var expected = new InvalidOperationException("simulated candidate-stream failure");
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                if (string.Equals(request.AggregateId, WorkFuture, StringComparison.Ordinal))
                {
                    callerCancellation.Cancel();
                    return Task.FromException<StreamReadPage>(expected);
                }

                return Task.FromResult(Story48Streams.Page(
                    scannedTenant,
                    WorkDue,
                    new WorkItemCreated(
                        WorkDue,
                        1,
                        new TenantId(scannedTenant),
                        new WorkItemId(WorkDue),
                        new Obligation("Preserve this candidate")),
                    new WorkItemSuspended(
                        WorkDue,
                        2,
                        new TenantId(scannedTenant),
                        new WorkItemId(WorkDue),
                        [AwaitCondition.DateReached(s_past)])));
            });
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway, logger)
                .GetPendingDateAwaitsAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(0);
        thrown.FailedCandidateCount.ShouldBe(1);
        PendingDateAwait partial = thrown.PartialResults.ShouldHaveSingleItem();
        partial.TenantId.ShouldBe(scannedTenant);
        partial.WorkItemId.ShouldBe(WorkDue);
        thrown.InnerException.ShouldBeSameAs(expected);
        var candidateLog = logger.Entries.Where(entry => entry.EventId.Id == 4606).ShouldHaveSingleItem();
        candidateLog.Exception.ShouldBeSameAs(expected);
        candidateLog.Message.ShouldContain("unless shutdown stops the scan");
        await store.DidNotReceive().GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_tenant_index_cancellation_is_classified_when_the_caller_becomes_canceled()
    {
        const string tenantB = "tenant-beta";
        using var callerCancellation = new CancellationTokenSource();
        using var dependencyCancellation = new CancellationTokenSource();
        dependencyCancellation.Cancel();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA, tenantB } };
        string failingTenant = registry.Tenants.First();
        string unreachedTenant = registry.Tenants.Single(tenant => tenant != failingTenant);
        store.GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantRegistry>(registry, "1")));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(failingTenant),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callerCancellation.Cancel();
                return Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(
                    new OperationCanceledException(dependencyCancellation.Token));
            });
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReadModelEntry<PendingDateAwaitTenantIndex>>(
                new OperationCanceledException(callerCancellation.Token)));
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(
                store,
                Substitute.For<IEventStoreGatewayClient>(),
                logger).GetPendingDateAwaitsAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(1);
        thrown.FailedCandidateCount.ShouldBe(0);
        OperationCanceledException cause = thrown.InnerException.ShouldBeOfType<OperationCanceledException>();
        cause.CancellationToken.ShouldBe(dependencyCancellation.Token);
        logger.Entries.Where(entry => entry.EventId.Id == 4604).ShouldHaveSingleItem().Exception.ShouldBeSameAs(cause);
        await store.DidNotReceive().GetAsync<PendingDateAwaitTenantIndex>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(unreachedTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_parking_lookup_cancellation_is_classified_when_the_caller_becomes_canceled()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var dependencyCancellation = new CancellationTokenSource();
        dependencyCancellation.Cancel();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var registry = new PendingDateAwaitTenantRegistry { Tenants = { TenantA } };
        var index = new PendingDateAwaitTenantIndex();
        index.Entries[WorkDue] =
        [
            new PendingDateAwait(TenantA, WorkDue, s_past, AwaitCondition.DateReached(s_past).CorrelationKey),
        ];
        index.Entries[WorkFuture] =
        [
            new PendingDateAwait(TenantA, WorkFuture, s_future, AwaitCondition.DateReached(s_future).CorrelationKey),
        ];
        store.GetAsync<PendingDateAwaitTenantRegistry>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitRegistryKey,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantRegistry>(registry, "1")));
        store.GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(TenantA),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReadModelEntry<PendingDateAwaitTenantIndex>(index, "1")));
        store.GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkDue),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callerCancellation.Cancel();
                return Task.FromException<ReadModelEntry<WorkItemProjectionParking>>(
                    new OperationCanceledException(dependencyCancellation.Token));
            });
        store.GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkFuture),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReadModelEntry<WorkItemProjectionParking>>(
                new OperationCanceledException(callerCancellation.Token)));
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway, logger)
                .GetPendingDateAwaitsAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(0);
        thrown.FailedCandidateCount.ShouldBe(1);
        OperationCanceledException cause = thrown.InnerException.ShouldBeOfType<OperationCanceledException>();
        cause.CancellationToken.ShouldBe(dependencyCancellation.Token);
        logger.Entries.Where(entry => entry.EventId.Id == 4608).ShouldHaveSingleItem().Exception.ShouldBeSameAs(cause);
        await store.DidNotReceive().GetAsync<WorkItemProjectionParking>(
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkFuture),
            Arg.Any<CancellationToken>());
        await gateway.DidNotReceive().ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_stream_cancellation_is_classified_when_the_caller_becomes_canceled()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var dependencyCancellation = new CancellationTokenSource();
        dependencyCancellation.Cancel();
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                if (request.AggregateId == WorkDue)
                {
                    callerCancellation.Cancel();
                    return Task.FromException<StreamReadPage>(new OperationCanceledException(dependencyCancellation.Token));
                }

                return Task.FromException<StreamReadPage>(new OperationCanceledException(callerCancellation.Token));
            });
        var logger = new Story48RecordingLogger<IndexedPendingDateAwaitSource>();

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway, logger)
                .GetPendingDateAwaitsAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(0);
        thrown.FailedCandidateCount.ShouldBe(1);
        OperationCanceledException cause = thrown.InnerException.ShouldBeOfType<OperationCanceledException>();
        cause.CancellationToken.ShouldBe(dependencyCancellation.Token);
        logger.Entries.Where(entry => entry.EventId.Id == 4606).ShouldHaveSingleItem().Exception.ShouldBeSameAs(cause);
        await gateway.DidNotReceive().ReadStreamAsync(
            Arg.Is<StreamReadRequest>(request => request.AggregateId == WorkFuture),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Still_fails_a_candidate_that_has_parking_history_but_is_not_yet_parked()
    {
        var store = new Story47InMemoryReadModelStore();
        await SeedAsync(store, TenantA, (WorkDue, s_past), (WorkFuture, s_future)).ConfigureAwait(true);
        await store
            .SaveAsync(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey(TenantA, WorkDue),
                new WorkItemProjectionParking { FailedSequence = 2, FailureCount = 2, Parked = false },
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                return string.Equals(request.AggregateId, WorkDue, StringComparison.Ordinal)
                    ? Task.FromException<StreamReadPage>(new InvalidOperationException("simulated transient decode failure"))
                    : Task.FromResult(Story48Streams.Page(TenantA, WorkFuture, Created(WorkFuture), SuspendedOnDate(WorkFuture, s_future)));
            });

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => NewSource(store, gateway)
                .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.FailedCandidateCount.ShouldBe(1);
        thrown.PartialResults.ShouldHaveSingleItem().WorkItemId.ShouldBe(WorkFuture);
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

    private static IndexedPendingDateAwaitSource NewSource(
        IReadModelStore store,
        IEventStoreGatewayClient gateway,
        ILogger<IndexedPendingDateAwaitSource>? logger = null)
        => new(
            store,
            gateway,
            Options.Create(new WorksRecoveryOptions()),
            logger ?? NullLogger<IndexedPendingDateAwaitSource>.Instance);

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
