using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Contracts.Reminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
using Hexalith.Works.Recovery;
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
    /// <summary>New SDK catalogs retain partial evidence across conflict/parking while healthy tenants advance after reconstruction.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessCatalogReturnsHealthyMembershipAndIncompleteDegradedScopeAfterRestart(bool parking)
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "pending", Suspended(TenantA, "pending"));
        await DegradeAsync(coordinator, "pending", parking);
        await SeedAsync(coordinator, TenantB, "z-healthy", Suspended(TenantB, "z-healthy"));
        var first = await new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(store, store))
            .ListAsync(null, 1, TestContext.Current.CancellationToken);
        first.IsIncomplete.ShouldBeTrue();
        first.Sources.ShouldHaveSingleItem().Aggregate.ShouldBe("z-healthy");
        await SeedAsync(coordinator, TenantB, "a-new", Suspended(TenantB, "a-new"));
        var reconstructed = await new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(store, store))
            .ListAsync(null, 256, TestContext.Current.CancellationToken);
        reconstructed.IsIncomplete.ShouldBeTrue();
        reconstructed.Sources.Select(source => source.Aggregate).ShouldBe(["a-new", "z-healthy"]);
        var handler = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions);
        (await new SharedProjectionEpochCoordinator(store, store).GetStatusAsync(handler.CreateScope(TenantA), TestContext.Current.CancellationToken))
            .IsDegraded.ShouldBeTrue();
    }

    /// <summary>All SDK members remain discoverable, including terminal and expiry-only sources with no date awaits.</summary>
    [Fact]
    public async Task ProcessCatalogUsesRetainedMembershipAndKeysetContinuation()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "a-terminal",
            new WorkItemCancelled("a-terminal", 2, new TenantId(TenantA), new WorkItemId("a-terminal")));
        await SeedAsync(coordinator, TenantA, "b-expiry-only",
            new WorkItemRescheduled("b-expiry-only", 2, new TenantId(TenantA), new WorkItemId("b-expiry-only"), new WorkItemSchedule()));
        var catalog = new WorksProcessSourceCatalog(coordinator);
        Hexalith.EventStore.Client.Processes.ProcessSourcePage first = await catalog.ListAsync(null, 1, TestContext.Current.CancellationToken);
        first.Sources.ShouldHaveSingleItem().Aggregate.ShouldBe("a-terminal");
        first.NextKey.ShouldNotBeNull();
        var restarted = new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(store, store));
        Hexalith.EventStore.Client.Processes.ProcessSourcePage next = await restarted.ListAsync(first.NextKey, 1, TestContext.Current.CancellationToken);
        next.Sources.ShouldHaveSingleItem().Aggregate.ShouldBe("b-expiry-only");
        next.NextKey.ShouldBeNull();
    }

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

    /// <summary>Offboarding removes discovery membership and reconstructed catalogs never resurrect it.</summary>
    [Fact]
    public async Task ProcessCatalogDoesNotResurrectOffboardedScope()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "offboarded", Suspended(TenantA, "offboarded"));
        var handler = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions);
        await coordinator.OffboardTenantAsync(handler.CreateScope(TenantA), "synthetic-audit", TestContext.Current.CancellationToken);
        var page = await new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(store, store))
            .ListAsync(null, 256, TestContext.Current.CancellationToken);
        page.Sources.ShouldBeEmpty();
        page.IsIncomplete.ShouldBeFalse();
        (await new SharedProjectionEpochCoordinator(store, store).GetStatusAsync(handler.CreateScope(TenantA), TestContext.Current.CancellationToken))
            .IsOffboarded.ShouldBeTrue();
    }

    /// <summary>A tenant-local tombstone outage retains SDK incomplete evidence while a healthy later tenant progresses across restart.</summary>
    [Theory]
    [InlineData("outage")]
    [InlineData("tombstone")]
    [InlineData("epoch")]
    [InlineData("generation")]
    public async Task TombstoneOutageAllowsHealthyMembershipAndDurableRecoveryAcrossRestart(string mode)
    {
        var store = new InMemoryReadModelStore();
        var normal = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(normal, TenantA, "unavailable", Suspended(TenantA, "unavailable"));
        await SeedAsync(normal, TenantB, "healthy", Suspended(TenantB, "healthy"));
        var unavailable = new TombstoneUnavailableReadModelStore(store, TenantA);
        var family = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions).CreateScope(TenantA);
        var blocked = new DiscoveryBlockedReadModelStore(store, TenantA, family.ComputeHash(), mode);
        IReadModelStore fault = mode == "outage" ? unavailable : blocked;
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<StreamReadRequest>();
            return Task.FromResult(new StreamReadPage(request.Tenant, request.Domain, request.AggregateId!, [],
                new StreamReadMetadata(0, null, null, 0, 0, false, null)));
        });
        var registrar = Substitute.For<IReminderRegistrar>();
        registrar.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>()).Returns(new ReminderConvergenceResult(0, 0, 0, 0, 0));
        ServiceProvider Restart()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(Environments.Development);
            services.AddSingleton(environment);
            services.AddSingleton<IReadModelStore>(fault);
            services.AddSingleton(new SharedProjectionEpochCoordinator(fault, store));
            services.AddSingleton(gateway);
            services.AddSingleton(Substitute.For<IProcessDelegationProvider>());
            services.AddSingleton(Substitute.For<ITrustedEffectSubmitter>());
            services.AddSingleton(registrar);
            services.AddSingleton(Options.Create(new WorksRecoveryOptions()));
            services.AddScoped<WorksSourceStreamReader>();
            services.AddScoped<IReminderTargetCatalog, WorksProcessSourceCatalog>();
            services.AddEventStoreProcesses<WorksProcessSourceCatalog, WorksProcessTranslator>();
            services.AddEventStoreReminders<WorksReminderIntentSource>(options => { options.ActorTypeName = "TombstoneRecoveryActor"; options.Workload = "works"; });
            return services.BuildServiceProvider();
        }
        async Task RemindersAsync(ServiceProvider services)
        {
            object reconciler = services.GetServices<IHostedService>().Single(service => service.GetType().Name == "ReminderReconciler");
            await ((Task)reconciler.GetType().GetMethod("RunPassAsync")!.Invoke(reconciler, [TestContext.Current.CancellationToken])!).ConfigureAwait(false);
        }
        const string catalogKey = "eventstore:reminders:v1:TombstoneRecoveryActor:control:tenants:catalog";
        for (int restart = 0; restart < 2; restart++)
        {
            using ServiceProvider services = Restart();
            var catalog = new WorksProcessSourceCatalog(services.GetRequiredService<SharedProjectionEpochCoordinator>());
            for (int scan = 0; scan < 10; scan++)
            {
                ProcessSourcePage page = await catalog.ListAsync(null, 256, TestContext.Current.CancellationToken);
                page.IsIncomplete.ShouldBeTrue();
                page.Sources.ShouldHaveSingleItem().Tenant.ShouldBe(TenantB);
                // Discovery abandonment must never consume ordinary contextual admission.
                (await new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()))
                    .ReadAsync(TenantB, "healthy", TestContext.Current.CancellationToken)).ShouldBeEmpty();
            }
            if (mode != "outage") blocked.Invocations.ShouldBe(1);
            for (int pass = 0; pass < 4; pass++) await services.GetRequiredService<ProcessReconciler>().RunPassAsync(TestContext.Current.CancellationToken);
            await RemindersAsync(services);
            (await services.GetRequiredService<ProcessCheckpointStore>().ContainsCandidateAsync(new(TenantB, "work", "healthy"), TestContext.Current.CancellationToken)).ShouldBeTrue();
            (await services.GetRequiredService<ProcessSweepStore>().GetAsync(TestContext.Current.CancellationToken)).Unresolved.ShouldBeGreaterThan(0);
            var progress = store.Snapshot<Dictionary<string, JsonElement>>("statestore", catalogKey)!;
            progress["complete"].GetBoolean().ShouldBeFalse();
            progress["isIncomplete"].GetBoolean().ShouldBeTrue();
            var readiness = await services.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>()
                .CheckHealthAsync(TestContext.Current.CancellationToken);
            readiness.Entries["eventstore-processes-unresolved"].Status.ShouldBe(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
            ((int)readiness.Entries["eventstore-processes-unresolved"].Data["unresolved"]).ShouldBeGreaterThan(0);
            readiness.Entries.Single(entry => entry.Key.Contains("reminders", StringComparison.Ordinal)).Value.Status
                .ShouldBe(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
        }
        unavailable.Unavailable = false;
        blocked.Release();
        // Wait on observable fresh admission, without assuming a provider continuation scheduling delay.
        for (int attempt = 0; ; attempt++)
        {
            if (!(await new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(fault, store))
                .ListAsync(null, 256, TestContext.Current.CancellationToken)).IsIncomplete) break;
            attempt.ShouldBeLessThan(500);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        using (ServiceProvider services = Restart())
        {
            for (int pass = 0; pass < 8; pass++) await services.GetRequiredService<ProcessReconciler>().RunPassAsync(TestContext.Current.CancellationToken);
            await RemindersAsync(services);
            (await services.GetRequiredService<ProcessSweepStore>().GetAsync(TestContext.Current.CancellationToken)).Unresolved.ShouldBe(0);
            store.Snapshot<Dictionary<string, JsonElement>>("statestore", catalogKey)!["complete"].GetBoolean().ShouldBeTrue();
        }
        var handler = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions);
        await normal.OffboardTenantAsync(handler.CreateScope(TenantA), "synthetic-tombstone-audit", TestContext.Current.CancellationToken);
        var retained = await new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(fault, store)).ListAsync(null, 256, TestContext.Current.CancellationToken);
        retained.IsIncomplete.ShouldBeFalse();
        retained.Sources.ShouldHaveSingleItem().Tenant.ShouldBe(TenantB);
    }

    /// <summary>A late absent tombstone is discarded; a fresh read must observe an offboard fence before releasing membership.</summary>
    [Fact]
    public async Task LateTombstoneCompletionCannotServeAsFreshOffboardingEvidence()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "offboarded", Suspended(TenantA, "offboarded"));
        await SeedAsync(coordinator, TenantB, "healthy", Suspended(TenantB, "healthy"));
        var scope = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions).CreateScope(TenantA);
        var blocked = new DiscoveryBlockedReadModelStore(store, TenantA, scope.ComputeHash(), "tombstone");
        var before = await new SharedProjectionEpochCoordinator(blocked, store).GetAvailableControlIndexTenantsAsync(
            WorksReadModelKeys.StateStoreName, WorksReadModelKeys.RuntimeControlIndex, TestContext.Current.CancellationToken);
        before.IsIncomplete.ShouldBeTrue();
        before.Tenants.ShouldBe([TenantB]);
        string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
        // Persist the real tombstone shape while retaining registry membership, the crash window before pruning.
        await store.SaveAsync(WorksReadModelKeys.StateStoreName,
            "shared-control-index-tombstone:" + Hash(WorksReadModelKeys.RuntimeControlIndex) + ":" + Hash(TenantA),
            new Dictionary<string, string> { ["indexName"] = WorksReadModelKeys.RuntimeControlIndex, ["tenantId"] = TenantA, ["auditId"] = "synthetic-offboard-fence" },
            TestContext.Current.CancellationToken);
        blocked.Release();
        for (int attempt = 0; ; attempt++)
        {
            var current = await new SharedProjectionEpochCoordinator(blocked, store).GetAvailableControlIndexTenantsAsync(
                WorksReadModelKeys.StateStoreName, WorksReadModelKeys.RuntimeControlIndex, TestContext.Current.CancellationToken);
            current.Tenants.ShouldBe([TenantB]);
            if (!current.IsIncomplete) break;
            attempt.ShouldBeLessThan(500);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        blocked.Invocations.ShouldBe(1);
    }

    /// <summary>An invalid duplicate family releases no members while a healthy later family remains discoverable.</summary>
    [Fact]
    public async Task DuplicateFamilyMembershipIsRejectedAtomically()
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "invalid", Suspended(TenantA, "invalid"));
        await SeedAsync(coordinator, TenantB, "healthy", Suspended(TenantB, "healthy"));
        var scope = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions).CreateScope(TenantA);
        long generation = (await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken)).ActiveGeneration;
        string queueHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(WorksReadModelKeys.RuntimeQueueKey)));
        string key = "shared-generation:" + scope.ComputeHash() + ":" + generation + ":" + queueHash;
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, key, WorksRuntimeDocument.From(new WorksWhatsNextTenantIndex
        { SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion, MemberWorkItemIds = ["invalid", "invalid"] }), TestContext.Current.CancellationToken);
        var page = await new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(store, store)).ListAsync(null, 256, TestContext.Current.CancellationToken);
        page.IsIncomplete.ShouldBeTrue();
        page.Sources.ShouldHaveSingleItem().Tenant.ShouldBe(TenantB);
    }

    /// <summary>Retained item or sequence evidence cannot disappear from complete SDK discovery.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingFamilyMembershipRetainsIncompleteDiscovery(bool extraItem)
    {
        var store = new InMemoryReadModelStore();
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        await SeedAsync(coordinator, TenantA, "retained", Suspended(TenantA, "retained"));
        await SeedAsync(coordinator, TenantB, "healthy", Suspended(TenantB, "healthy"));
        var scope = new WorkItemSharedProjectionRebuildHandler(s_sdkOptions).CreateScope(TenantA);
        long generation = (await coordinator.GetStatusAsync(scope, TestContext.Current.CancellationToken)).ActiveGeneration;
        string queueHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(WorksReadModelKeys.RuntimeQueueKey)));
        string key = "shared-generation:" + scope.ComputeHash() + ":" + generation + ":" + queueHash;
        var missing = new WhatsNextItem(new TenantId(TenantA), new WorkItemId("missing"), WorkItemStatus.Queued,
            null, null, null, null, null, [], [], 1);
        await store.SaveAsync(WorksReadModelKeys.StateStoreName, key, WorksRuntimeDocument.From(new WorksWhatsNextTenantIndex
        {
            SchemaVersion = WorksReadModelKeys.CurrentSchemaVersion,
            Items = extraItem ? new() { ["missing"] = missing } : new(),
            LastSequences = extraItem ? new() { ["retained"] = 1 } : new() { ["retained"] = 1, ["missing"] = 1 },
            MemberWorkItemIds = ["retained"],
        }), TestContext.Current.CancellationToken);

        var page = await new WorksProcessSourceCatalog(new SharedProjectionEpochCoordinator(store, store))
            .ListAsync(null, 256, TestContext.Current.CancellationToken);
        page.IsIncomplete.ShouldBeTrue();
        page.Sources.ShouldHaveSingleItem().Tenant.ShouldBe(TenantB);
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
