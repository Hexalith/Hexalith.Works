using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Reactor;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises recovery sources against the SDK-owned runtime family and control index.</summary>
public sealed class WorksSdkRecoverySourceTests
{
    private const string TenantA = "tenant-sdk-recovery-a";
    private const string TenantB = "tenant-sdk-recovery-b";
    private static readonly DateTimeOffset s_instant = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private static readonly IOptions<WorksProjectionOptions> s_sdkOptions =
        Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });

    [Fact]
    public async Task Pending_date_source_discovers_sdk_tenants_and_uses_stream_truth_over_pinned_index()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "work-pending", Suspended(TenantA, "work-pending"));
        await SeedAsync(coordinator, TenantB, "work-resumed", Suspended(TenantB, "work-resumed"));
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey,
            new PendingDateAwaitTenantRegistry { Tenants = { "tenant-legacy-only" } },
            TestContext.Current.CancellationToken);

        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                StreamReadRequest request = call.ArgAt<StreamReadRequest>(0);
                return Task.FromResult(request.Tenant switch
                {
                    TenantA => Story48Streams.Page(TenantA, "work-pending",
                        Created(TenantA, "work-pending"), Suspended(TenantA, "work-pending")),
                    TenantB => Story48Streams.Page(TenantB, "work-resumed",
                        Created(TenantB, "work-resumed"), Suspended(TenantB, "work-resumed"),
                        new WorkItemResumed("work-resumed", 3, new TenantId(TenantB),
                            new WorkItemId("work-resumed"), AwaitCondition.DateReached(s_instant))),
                    _ => throw new InvalidOperationException("An unregistered tenant was scanned."),
                });
            });
        var source = NewPendingSource(store, gateway, coordinator);

        PendingDateAwaitScanResult scan = await source.GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken);

        PendingDateAwait pending = scan.Pending.ShouldHaveSingleItem();
        pending.TenantId.ShouldBe(TenantA);
        pending.WorkItemId.ShouldBe("work-pending");
        await gateway.Received(2).ReadStreamAsync(
            Arg.Is<StreamReadRequest>(request => request.AggregateId != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pending_date_source_reports_partial_sdk_candidate_failure_without_losing_healthy_stream()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "work-good", Suspended(TenantA, "work-good"));
        await SeedAsync(coordinator, TenantA, "work-unreadable", Suspended(TenantA, "work-unreadable"));
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<StreamReadRequest>(0).AggregateId == "work-good"
                ? Task.FromResult(Story48Streams.Page(TenantA, "work-good",
                    Created(TenantA, "work-good"), Suspended(TenantA, "work-good")))
                : Task.FromException<StreamReadPage>(new HttpRequestException("stream unavailable")));

        PendingDateAwaitScanIncompleteException error = await Should.ThrowAsync<PendingDateAwaitScanIncompleteException>(
            () => NewPendingSource(store, gateway, coordinator)
                .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken));

        error.FailedTenantCount.ShouldBe(0);
        error.FailedCandidateCount.ShouldBe(1);
        error.PartialResults.ShouldHaveSingleItem().WorkItemId.ShouldBe("work-good");
    }

    [Fact]
    public async Task Pending_date_source_does_not_borrow_parking_from_an_older_generation()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "work-good", Suspended(TenantA, "work-good"));
        await SeedAsync(coordinator, TenantA, "work-missing-parking", Suspended(TenantA, "work-missing-parking"));
        await PromoteWithoutAsync(coordinator, TenantA,
            ["work-good", "work-missing-parking"],
            workItemId => [Created(TenantA, workItemId), Suspended(TenantA, workItemId)],
            WorksReadModelKeys.RuntimeParkingKey("work-missing-parking"));

        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Story48Streams.Page(TenantA,
                call.ArgAt<StreamReadRequest>(0).AggregateId!,
                Created(TenantA, call.ArgAt<StreamReadRequest>(0).AggregateId!),
                Suspended(TenantA, call.ArgAt<StreamReadRequest>(0).AggregateId!))));

        PendingDateAwaitScanIncompleteException error = await Should.ThrowAsync<PendingDateAwaitScanIncompleteException>(
            () => NewPendingSource(store, gateway, coordinator)
                .GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken));

        error.FailedCandidateCount.ShouldBe(1);
        error.PartialResults.ShouldHaveSingleItem().WorkItemId.ShouldBe("work-good");
        await gateway.DidNotReceive().ReadStreamAsync(Arg.Is<StreamReadRequest>(request =>
            request.AggregateId == "work-missing-parking"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cascade_source_uses_parent_stream_and_one_sdk_generation_for_child_terminality()
    {
        const string parent = "parent-sdk";
        const string terminal = "child-terminal";
        const string missing = "child-untracked";
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, terminal,
            new WorkItemCancelled(terminal, 2, new TenantId(TenantA), new WorkItemId(terminal)));
        await store.SaveAsync(WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.RollUpKey(TenantA, terminal),
            new WorkItemRollUp(new TenantId(TenantA), new WorkItemId(terminal), WorkItemStatus.InProgress,
                null, null, null, [], [], 1), TestContext.Current.CancellationToken);

        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(ParentPage(parent, terminal, missing));
        var source = new StreamReadingCascadeDescendantSource(gateway, store,
            Options.Create(new WorksRecoveryOptions()), NullLogger<StreamReadingCascadeDescendantSource>.Instance,
            s_sdkOptions, coordinator);

        IReadOnlyList<CascadeDescendant> descendants = await source.GetDescendantsAsync(
            TenantA, parent, TestContext.Current.CancellationToken);

        descendants.Count.ShouldBe(2);
        descendants.Single(child => child.WorkItemId.Value == terminal).IsTerminal.ShouldBeTrue();
        descendants.Single(child => child.WorkItemId.Value == missing).IsTerminal.ShouldBeFalse();
        await gateway.Received(1).ReadStreamAsync(Arg.Is<StreamReadRequest>(request =>
            request.Tenant == TenantA && request.AggregateId == parent), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cascade_source_treats_missing_current_rollup_as_active_even_when_old_generation_was_terminal()
    {
        const string parent = "parent-sdk";
        const string child = "child-terminal";
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, child,
            new WorkItemCancelled(child, 2, new TenantId(TenantA), new WorkItemId(child)));
        await PromoteWithoutAsync(coordinator, TenantA, [child],
            workItemId => [Created(TenantA, workItemId),
                new WorkItemCancelled(workItemId, 2, new TenantId(TenantA), new WorkItemId(workItemId))],
            WorksReadModelKeys.RuntimeRollUpKey(child));

        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(ParentPage(parent, child));
        var source = new StreamReadingCascadeDescendantSource(gateway, store,
            Options.Create(new WorksRecoveryOptions()), NullLogger<StreamReadingCascadeDescendantSource>.Instance,
            s_sdkOptions, coordinator);

        IReadOnlyList<CascadeDescendant> descendants = await source.GetDescendantsAsync(
            TenantA, parent, TestContext.Current.CancellationToken);

        descendants.ShouldHaveSingleItem().IsTerminal.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_date_source_reports_incomplete_when_restarted_sdk_family_has_conflict_or_orphan_parking(bool parking)
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "pending", Suspended(TenantA, "pending"));
        await DegradeAsync(coordinator, "pending", parking);
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        var error = await Should.ThrowAsync<PendingDateAwaitScanIncompleteException>(() =>
            NewPendingSource(store, gateway, restarted).GetPendingDateAwaitsAsync(TestContext.Current.CancellationToken));
        error.FailedTenantCount.ShouldBe(1);
        error.PartialResults.ShouldBeEmpty();
        await gateway.DidNotReceive().ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cascade_source_keeps_terminal_child_active_when_restarted_sdk_family_has_conflict_or_orphan_parking(bool parking)
    {
        const string child = "terminal";
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, child, new WorkItemCancelled(child, 2, new TenantId(TenantA), new WorkItemId(child)));
        await DegradeAsync(coordinator, child, parking);
        var restarted = new SharedProjectionEpochCoordinator(store, store);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(ParentPage("parent", child));
        var source = new StreamReadingCascadeDescendantSource(gateway, store, Options.Create(new WorksRecoveryOptions()),
            NullLogger<StreamReadingCascadeDescendantSource>.Instance, s_sdkOptions, restarted);
        (await source.GetDescendantsAsync(TenantA, "parent", TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().IsTerminal.ShouldBeFalse();
    }

    private static async Task DegradeAsync(SharedProjectionEpochCoordinator coordinator, string source, bool parking)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions
        { UseSdkDelivery = true, MaxUndecodableEventDispatchesBeforeParking = 1 }));
        var scope = handler.CreateScope(TenantA);
        var lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, TestContext.Current.CancellationToken).ConfigureAwait(false);
        var delivery = new SharedProjectionDelivery(parking ? "first-parked-source" : source, parking ? 1 : 2,
            [0xff], [], new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
        var result = await coordinator.JournalAsync(scope, lease, delivery, TestContext.Current.CancellationToken).ConfigureAwait(false);
        if (parking)
        {
            result.ShouldBe(SharedProjectionJournalResult.Journaled);
            _ = await coordinator.CatchUpAsync(scope, (item, generation, token) => handler.FoldCatchUpAsync(item, generation, coordinator, token),
                TestContext.Current.CancellationToken).ConfigureAwait(false);
            (await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken).ConfigureAwait(false)).ParkingEvidence.ShouldHaveSingleItem();
        }
        else result.ShouldBe(SharedProjectionJournalResult.IdentityConflict);
    }

    private static IndexedPendingDateAwaitSource NewPendingSource(
        IReadModelStore store, IEventStoreGatewayClient gateway, SharedProjectionEpochCoordinator coordinator)
        => new(store, gateway, Options.Create(new WorksRecoveryOptions()),
            NullLogger<IndexedPendingDateAwaitSource>.Instance, s_sdkOptions, coordinator);

    private static async Task SeedAsync(SharedProjectionEpochCoordinator coordinator, string tenantId,
        string workItemId, IEventPayload secondEvent)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions);
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        SharedProjectionLease lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        IEventPayload[] events = [Created(tenantId, workItemId), secondEvent];
        for (int index = 0; index < events.Length; index++)
        {
            long sequence = index + 1;
            var dto = new ProjectionEventDto(events[index].GetType().Name,
                JsonSerializer.SerializeToUtf8Bytes(events[index], events[index].GetType()),
                "json", sequence, DateTimeOffset.UnixEpoch, "corr-sdk-recovery", GlobalPosition: sequence);
            var delivery = new SharedProjectionDelivery(workItemId, sequence,
                JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(tenantId, "work", workItemId, [dto]), s_web),
                JsonSerializer.SerializeToUtf8Bytes(sequence),
                new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
            (await coordinator.JournalAsync(scope, lease, delivery, TestContext.Current.CancellationToken)
                .ConfigureAwait(false))
                .ShouldBe(SharedProjectionJournalResult.Journaled);
            _ = await coordinator.CatchUpAsync(scope,
                (item, generation, token) => handler.FoldCatchUpAsync(item, generation, coordinator, token),
                TestContext.Current.CancellationToken).ConfigureAwait(false);
            lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId,
                TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PromoteWithoutAsync(
        SharedProjectionEpochCoordinator coordinator,
        string tenantId,
        IReadOnlyList<string> workItemIds,
        Func<string, IEventPayload[]> history,
        string omittedKey)
    {
        var handler = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions);
        SharedProjectionScope scope = handler.CreateScope(tenantId);
        string operation = "sdk-recovery-rebuild-" + Guid.NewGuid().ToString("N");
        const string inventory = "sdk-recovery-inventory";
        _ = await coordinator.BeginAsync(scope, operation, inventory,
            workItemIds.ToDictionary(static id => id, static _ => 2L, StringComparer.Ordinal),
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        var identity = new DomainSharedProjectionRebuildIdentity(tenantId, "work", WorksReadModelKeys.RuntimeFamily,
            operation, inventory);
        DomainSharedProjectionRebuildCandidate candidate = await handler.CreateEmptyCandidateAsync(identity,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        foreach (string workItemId in workItemIds)
        {
            ProjectionEventDto[] events = [.. history(workItemId).Select((payload, index) =>
                new ProjectionEventDto(payload.GetType().Name,
                    JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType()), "json", index + 1,
                    DateTimeOffset.UnixEpoch, "corr-sdk-recovery", GlobalPosition: index + 1))];
            candidate = await handler.AccumulateAsync(identity, candidate,
                new ProjectionRequest(tenantId, "work", workItemId, events),
                TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        DomainProjectionRebuildPlan plan = await handler.FinalizeAsync(identity, candidate,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        _ = await coordinator.StageAsync(scope, operation,
            [.. plan.Operations.Where(item => item.Key != omittedKey)],
            TestContext.Current.CancellationToken,
            new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex)).ConfigureAwait(false);
        await coordinator.CommitAsync(scope, operation, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static WorkItemCreated Created(string tenantId, string workItemId)
        => new(workItemId, 1, new TenantId(tenantId), new WorkItemId(workItemId), new Obligation("Recovery proof"));

    private static WorkItemSuspended Suspended(string tenantId, string workItemId)
        => new(workItemId, 2, new TenantId(tenantId), new WorkItemId(workItemId),
            [AwaitCondition.DateReached(s_instant)]);

    private static StreamReadPage ParentPage(string parentId, params string[] children)
    {
        StreamReadEvent[] events = [.. children.Select((child, index) =>
        {
            long sequence = index + 1;
            var spawned = new ChildSpawned(parentId, sequence, new TenantId(TenantA),
                new WorkItemId(parentId), new WorkItemId(child), new Obligation("Child proof"));
            return new StreamReadEvent(sequence, typeof(ChildSpawned).FullName!,
                JsonSerializer.SerializeToUtf8Bytes(spawned, s_web), "json", 1,
                $"message-{sequence}", $"correlation-{sequence}", null, s_instant, null);
        })];
        return new StreamReadPage(TenantA, "work", parentId, events,
            new StreamReadMetadata(0, null, events.Length, events.Length, events.Length,
                IsTruncated: false, NextContinuationToken: null));
    }
}
