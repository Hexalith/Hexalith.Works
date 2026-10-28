using System.Reflection;
using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Qualifies the SDK replacement for legacy startup-only reminder recovery and its operator evidence.</summary>
public sealed class WorksSdkReminderRecoveryPolicyTests
{
    private const string Tenant = "tenant-reminder-policy";
    private const string ActorType = "WorksSdkReminderActor";
    private const string Prefix = "eventstore:reminders:v1:" + ActorType;
    private static readonly DateTimeOffset s_now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Actual SDK convergence refuses an initial suspension before any durable reminder state or scheduler operation.</summary>
    [Fact]
    public async Task SuspensionWithoutCreationCannotPersistOrScheduleAReminder()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string item = "corrupt-initial-suspension";
        var store = new InMemoryReadModelStore();
        var page = Story48Streams.Page(Tenant, item, new WorkItemSuspended(item, 1,
            new TenantId(Tenant), new WorkItemId(item), [AwaitCondition.DateReached(s_now.AddDays(1))]));
        var scheduler = (WorksReminderSchedulerProxy)DispatchProxy.Create(SdkType("IReminderScheduler"), typeof(WorksReminderSchedulerProxy));
        using ServiceProvider services = Services(store, Substitute.For<IReminderRegistrar>(), new(), page);
        using IServiceScope scope = services.CreateScope();
        string actorId = ReminderIdentityCodec.ComputeActorId(Tenant, item);
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => InvokeAsync(
            scope.ServiceProvider.GetRequiredService(SdkType("ReminderCoordinator")), "ConvergeAsync",
            actorId, new ReminderTarget(Tenant, "work", item), scheduler, ct));
        exception.Message.ShouldContain("before creation");
        store.SnapshotRaw("statestore", Prefix + ":item:" + actorId).ShouldBeNull();
        store.SnapshotRaw("statestore", Prefix + ":tenant:" + Tenant + ":candidates").ShouldBeNull();
        store.SnapshotRaw("statestore", Prefix + ":control:tenants").ShouldBeNull();
        scheduler.Arms.ShouldBeEmpty();
        scheduler.Held.ShouldBeEmpty();
    }

    /// <summary>Six failed passes do not exhaust recovery; the same hosted worker recovers without a restart.</summary>
    [Fact]
    public async Task PeriodicRecoveryContinuesPastLegacyAttemptBudgetAndRecoversReadiness()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var store = new InMemoryReadModelStore();
        var logs = new WorksReminderRecoveryLogCapture();
        // Stop at the completed-pass boundary before another BeginPass can mark readiness pending.
        logs.Recorded = (id, properties) =>
        {
            if (id == 200210 && (int)properties["Armed"]! == 1 && (int)properties["Incomplete"]! == 0)
                lifetime.Cancel();
        };
        IReminderRegistrar registrar = Substitute.For<IReminderRegistrar>();
        int repaired = 0;
        registrar.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>()).Returns(_ =>
            Volatile.Read(ref repaired) == 0
                ? Task.FromException<ReminderConvergenceResult>(new IOException("private-dependency-secret"))
                : Task.FromResult(new ReminderConvergenceResult(1, 0, 0, 0, 0)));
        using ServiceProvider services = Services(store, registrar, logs);
        await IndexAsync(services, new(Tenant, "work", "retry-item"), ct);
        var worker = (BackgroundService)services.GetRequiredService(SdkType("ReminderReconciler"));
        services.GetServices<IHostedService>().ShouldContain(worker);
        await worker.StartAsync(lifetime.Token);
        try
        {
            await WaitAsync(() => logs.Entries.Count(entry => entry.EventId == 200210) >= 6, ct);
            (await HealthAsync(services, ct)).Status.ShouldBe(HealthStatus.Degraded);
            Discovery(store, "indexed").GetProperty("isIncomplete").GetBoolean().ShouldBeTrue();
            logs.Entries.Count(entry => entry.EventId == 200211).ShouldBeGreaterThanOrEqualTo(6);
            Volatile.Write(ref repaired, 1);
            await WaitAsync(() => logs.Entries.Any(entry => entry.EventId == 200210
                && (int)entry.Properties["Armed"]! == 1 && (int)entry.Properties["Incomplete"]! == 0), ct);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
        (await HealthAsync(services, ct)).Status.ShouldBe(HealthStatus.Healthy);
        Discovery(store, "indexed").GetProperty("complete").GetBoolean().ShouldBeTrue();
        AssertSafeDiagnostics(logs);
    }

    /// <summary>Provider cancellation and ordinary outages preserve healthy counts, durable incompleteness and later service.</summary>
    /// <param name="foreignCancellation">Whether the failed dependency throws cancellation without cancelling the caller.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCandidateRetainsEarlierAndLaterEvidenceAcrossReconstruction(bool foreignCancellation)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        var logs = new WorksReminderRecoveryLogCapture();
        ReminderTarget[] targets = [.. new[] { "first", "middle", "last" }.Select(item => new ReminderTarget(Tenant, "work", item))
            .OrderBy(target => ReminderIdentityCodec.ComputeActorId(target.Tenant, target.Aggregate), StringComparer.Ordinal)];
        var attempted = new List<ReminderTarget>();
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        IReminderRegistrar registrar = Substitute.For<IReminderRegistrar>();
        registrar.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            ReminderTarget target = call.Arg<ReminderTarget>();
            attempted.Add(target);
            return target == targets[1]
                ? Task.FromException<ReminderConvergenceResult>(foreignCancellation
                    ? new OperationCanceledException("private-dependency-secret", foreign.Token)
                    : new IOException("private-dependency-secret"))
                : Task.FromResult(new ReminderConvergenceResult(1, 1, 0, 0, 0));
        });
        byte[] originalTenantIndex;
        const string unavailableTenant = "aaa-unavailable-tenant";
        string unavailableKey = Prefix + ":tenant:" + unavailableTenant + ":candidates";
        using (ServiceProvider services = Services(store, registrar, logs))
        {
            await IndexAsync(services, new(unavailableTenant, "work", "unreadable-index"), ct);
            originalTenantIndex = store.SnapshotRaw("statestore", unavailableKey).ShouldNotBeNull();
            // An earlier tenant is unreadable before the middle healthy-tenant candidate fails.
            await store.SaveAsync("statestore", unavailableKey,
                new Dictionary<string, object?> { ["tenant"] = unavailableTenant, ["candidates"] = null }, ct);
            foreach (ReminderTarget target in targets) await IndexAsync(services, target, ct);
            object pass = await RunPassAsync(services, ct);
            Property<int>(pass, "Tenants").ShouldBe(2);
            Property<int>(pass, "Candidates").ShouldBe(3);
            Property<int>(pass, "Armed").ShouldBe(2);
            Property<int>(pass, "Submitted").ShouldBe(2);
            Property<int>(pass, "Incomplete").ShouldBe(2);
            attempted.ShouldBe(targets);
            ct.IsCancellationRequested.ShouldBeFalse();
            (await HealthAsync(services, ct)).Status.ShouldBe(HealthStatus.Degraded);
        }
        JsonElement failed = Discovery(store, "indexed");
        failed.GetProperty("isIncomplete").GetBoolean().ShouldBeTrue();
        failed.GetProperty("complete").GetBoolean().ShouldBeFalse();
        var warning = logs.Entries.Single(entry => entry.EventId == 200211);
        warning.Properties["ActorId"].ShouldBe(ReminderIdentityCodec.ComputeActorId(Tenant, targets[1].Aggregate));
        warning.Properties["ExceptionType"].ShouldBe(foreignCancellation ? "OperationCanceledException" : "IOException");
        var completed = logs.Entries.Single(entry => entry.EventId == 200210);
        var scanFailure = logs.Entries.Single(entry => entry.EventId == 200212);
        scanFailure.Properties["Scope"].ShouldBe("tenant-candidates");
        completed.Properties["Armed"].ShouldBe(2);
        completed.Properties["Submitted"].ShouldBe(2);
        completed.Properties["Incomplete"].ShouldBe(2);
        AssertSafeDiagnostics(logs);
        // Restore the exact valid tenant index; reconstruct over the original retained candidates.
        using (JsonDocument restored = JsonDocument.Parse(originalTenantIndex))
            await store.SaveAsync("statestore", unavailableKey, restored, ct);

        IReminderRegistrar healthy = Substitute.For<IReminderRegistrar>();
        healthy.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>())
            .Returns(new ReminderConvergenceResult(1, 0, 0, 0, 0));
        using ServiceProvider restarted = Services(store, healthy, new());
        (await HealthAsync(restarted, ct)).Status.ShouldBe(HealthStatus.Degraded);
        object recovered = await RunPassAsync(restarted, ct);
        Property<int>(recovered, "Candidates").ShouldBe(4);
        Property<int>(recovered, "Incomplete").ShouldBe(0);
        Discovery(store, "indexed").GetProperty("isIncomplete").GetBoolean().ShouldBeFalse();
        (await HealthAsync(restarted, ct)).Status.ShouldBe(HealthStatus.Healthy);
        using JsonDocument candidates = JsonDocument.Parse(store.SnapshotRaw("statestore", Prefix + ":tenant:" + Tenant + ":candidates").ShouldNotBeNull());
        candidates.RootElement.GetProperty("candidates").GetArrayLength().ShouldBe(3);
    }

    /// <summary>Host shutdown can interrupt a pass despite a different provider token, without publishing success or losing the durable cursor.</summary>
    [Fact]
    public async Task HostCancellationKeepsIncompletePassAndResumesRetainedCursorAfterRestart()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var store = new InMemoryReadModelStore();
        var logs = new WorksReminderRecoveryLogCapture();
        ReminderTarget[] targets = [.. new[] { "shutdown-a", "shutdown-b", "shutdown-c" }.Select(item => new ReminderTarget(Tenant, "work", item))
            .OrderBy(target => ReminderIdentityCodec.ComputeActorId(target.Tenant, target.Aggregate), StringComparer.Ordinal)];
        IReminderRegistrar registrar = Substitute.For<IReminderRegistrar>();
        var attempted = new List<ReminderTarget>();
        registrar.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            ReminderTarget target = call.Arg<ReminderTarget>();
            attempted.Add(target);
            if (target != targets[1]) return Task.FromResult(new ReminderConvergenceResult(1, 0, 0, 0, 0));
            caller.Cancel();
            return Task.FromException<ReminderConvergenceResult>(new OperationCanceledException("private-dependency-secret", CancellationToken.None));
        });
        using (ServiceProvider services = Services(store, registrar, logs))
        {
            foreach (ReminderTarget target in targets) await IndexAsync(services, target, ct);
            await Should.ThrowAsync<OperationCanceledException>(() => RunPassAsync(services, caller.Token));
            attempted.ShouldBe(targets[..2]);
            HealthCheckResult health = await HealthAsync(services, ct);
            health.Status.ShouldBe(HealthStatus.Degraded);
            health.Data["passPending"].ShouldBe(true);
            logs.Entries.ShouldNotContain(entry => entry.EventId == 200210 || entry.EventId == 200211);
        }
        JsonElement retained = Discovery(store, "indexed");
        retained.GetProperty("complete").GetBoolean().ShouldBeFalse();
        retained.GetProperty("afterKey").GetString().ShouldBe(Tenant + "\0" + ReminderIdentityCodec.ComputeActorId(Tenant, targets[0].Aggregate));
        IReminderRegistrar healthy = Substitute.For<IReminderRegistrar>();
        healthy.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>())
            .Returns(new ReminderConvergenceResult(1, 0, 0, 0, 0));
        using ServiceProvider restarted = Services(store, healthy, new());
        Property<int>(await RunPassAsync(restarted, ct), "Candidates").ShouldBe(2);
        _ = await healthy.DidNotReceive().ConvergeAsync(targets[0], Arg.Any<CancellationToken>());
        Discovery(store, "indexed").GetProperty("complete").GetBoolean().ShouldBeTrue();
        (await HealthAsync(restarted, ct)).Status.ShouldBe(HealthStatus.Healthy);
    }

    /// <summary>An initially empty sweep cannot strand a suspension whose projection membership arrives later.</summary>
    [Fact]
    public async Task EmptyRegistryIsRevisitedAfterProjectionBackfillWithoutEventWakeup()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var store = new InMemoryReadModelStore();
        const string item = "backfilled-suspension";
        AwaitCondition date = AwaitCondition.DateReached(s_now.AddDays(1));
        IEventPayload[] facts = [new WorkItemCreated(item, 1, new TenantId(Tenant), new WorkItemId(item), new Obligation("backfill recovery")),
            new WorkItemSuspended(item, 2, new TenantId(Tenant), new WorkItemId(item), [date])];
        StreamReadPage page = Story48Streams.Page(Tenant, item, facts);
        var scheduler = (WorksReminderSchedulerProxy)DispatchProxy.Create(SdkType("IReminderScheduler"), typeof(WorksReminderSchedulerProxy));
        IReminderRegistrar registrar = Substitute.For<IReminderRegistrar>();
        ServiceProvider? current = null;
        registrar.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            using IServiceScope scope = current!.CreateScope();
            return (ReminderConvergenceResult)await InvokeAsync(scope.ServiceProvider.GetRequiredService(SdkType("ReminderCoordinator")),
                "ConvergeAsync", ReminderIdentityCodec.ComputeActorId(Tenant, item), call.Arg<ReminderTarget>(), scheduler, call.Arg<CancellationToken>())
                .ConfigureAwait(false);
        });
        using (ServiceProvider empty = Services(store, registrar, new(), page))
        {
            current = empty;
            Property<int>(await RunPassAsync(empty, ct), "Candidates").ShouldBe(0);
            Discovery(store, "catalog").GetProperty("complete").GetBoolean().ShouldBeTrue();
            store.SnapshotRaw("statestore", Prefix + ":control:tenants").ShouldBeNull();
            await BackfillAsync(store, item, facts, ct);
            // Keep the same worker and catalog registration: no restart or event wakeup triggers discovery.
            Property<int>(await RunPassAsync(empty, ct), "Incomplete").ShouldBe(0);
        }
        scheduler.Arms.ShouldHaveSingleItem().DueTime.ShouldBe(TimeSpan.FromDays(1));
        string key = Prefix + ":item:" + ReminderIdentityCodec.ComputeActorId(Tenant, item);
        byte[] witness = store.SnapshotRaw("statestore", key).ShouldNotBeNull();
        using JsonDocument state = JsonDocument.Parse(witness);
        state.RootElement.GetProperty("tenant").GetString().ShouldBe(Tenant);
        state.RootElement.GetProperty("domain").GetString().ShouldBe("work");
        state.RootElement.GetProperty("aggregate").GetString().ShouldBe(item);
        JsonElement entry = state.RootElement.GetProperty("entries").EnumerateArray().ShouldHaveSingleItem();
        entry.GetProperty("dueUtc").GetDateTimeOffset().ShouldBe(date.Instant!.Value);
        entry.GetProperty("status").GetString().ShouldBe("Armed");
        entry.GetProperty("sourceSequence").GetInt64().ShouldBe(2);
        entry.GetProperty("scheduleRevision").GetInt64().ShouldBe(2);
        entry.GetProperty("reminderName").GetString().ShouldBe(scheduler.Arms[0].Name);
        using JsonDocument registry = JsonDocument.Parse(store.SnapshotRaw("statestore", Prefix + ":control:tenants").ShouldNotBeNull());
        registry.RootElement.GetProperty("tenants").EnumerateArray().Select(value => value.GetString()).ShouldBe([Tenant]);
        using JsonDocument candidates = JsonDocument.Parse(store.SnapshotRaw("statestore", Prefix + ":tenant:" + Tenant + ":candidates").ShouldNotBeNull());
        candidates.RootElement.GetProperty("tenant").GetString().ShouldBe(Tenant);
        JsonElement candidate = candidates.RootElement.GetProperty("candidates").EnumerateArray().ShouldHaveSingleItem();
        candidate.GetProperty("domain").GetString().ShouldBe("work");
        candidate.GetProperty("aggregate").GetString().ShouldBe(item);
        candidate.GetProperty("actorId").GetString().ShouldBe(ReminderIdentityCodec.ComputeActorId(Tenant, item));
        scheduler.Held.Clear();
        using (ServiceProvider again = Services(store, registrar, new(), page))
        {
            current = again;
            Property<int>(await RunPassAsync(again, ct), "Candidates").ShouldBeGreaterThanOrEqualTo(1);
        }
        scheduler.Arms.Count.ShouldBe(2);
        store.SnapshotRaw("statestore", key).ShouldBe(witness);
    }

    /// <summary>Disabling the periodic worker is explicit and leaves readiness degraded without claiming a completed pass.</summary>
    [Fact]
    public async Task DisabledRecoveryEmitsExplicitDiagnosticAndStaysDegraded()
    {
        var logs = new WorksReminderRecoveryLogCapture();
        using ServiceProvider services = Services(new(), Substitute.For<IReminderRegistrar>(), logs, enabled: false);
        var worker = (BackgroundService)services.GetRequiredService(SdkType("ReminderReconciler"));
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        logs.Entries.ShouldContain(entry => entry.EventId == 200213);
        logs.Entries.ShouldNotContain(entry => entry.EventId == 200210);
        typeof(WorksHost).Assembly.GetTypes().ShouldNotContain(type => type.Name == "WorksRecoveryLog"
            || type.Name == "ReminderReconciliationService" || type.Name == "DateReminderReconciler" || type.Name == "IndexedPendingDateAwaitSource");
        HealthCheckResult health = await HealthAsync(services, TestContext.Current.CancellationToken);
        health.Status.ShouldBe(HealthStatus.Degraded);
        health.Data["passCompleted"].ShouldBe(false);
        await worker.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider Services(InMemoryReadModelStore store, IReminderRegistrar registrar,
        WorksReminderRecoveryLogCapture logs, StreamReadPage? page = null, bool enabled = true)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APP_API_TOKEN"] = "synthetic-callback-token",
            ["EventStore:Reminders:ReconciliationEnabled"] = enabled.ToString(),
            ["EventStore:Reminders:ReconciliationInterval"] = "00:00:00.050",
            ["EventStore:Reminders:RetryInitialDelay"] = "00:00:00.010",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        services.AddSingleton(environment);
        services.AddLogging(builder => builder.ClearProviders().SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        services.AddSingleton<IReadModelStore>(store);
        services.AddSingleton<IReadModelConditionalEraser>(store);
        services.AddSingleton<TimeProvider>(new Story48FixedTimeProvider(s_now));
        services.AddWorksReminderAndCascadeRecovery(configuration);
        services.RemoveAll<IReminderRegistrar>();
        services.AddSingleton(registrar);
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(_ => page!);
        services.RemoveAll<IEventStoreGatewayClient>();
        services.AddSingleton(gateway);
        ServiceProvider provider = services.BuildServiceProvider();
        EventStoreReminderOptions options = provider.GetRequiredService<IOptions<EventStoreReminderOptions>>().Value;
        options.ActorTypeName.ShouldBe(ActorType);
        options.Workload.ShouldBe("works");
        return provider;
    }

    private static async Task BackfillAsync(InMemoryReadModelStore store, string item, IEventPayload[] facts, CancellationToken ct)
    {
        var coordinator = new SharedProjectionEpochCoordinator(store, store);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions()));
        var scope = handler.CreateScope(Tenant);
        var lease = await coordinator.RegisterWriterAsync(scope, handler.OrdinaryWriterId, ct).ConfigureAwait(false);
        for (int index = 0; index < facts.Length; index++)
        {
            long sequence = index + 1;
            var dto = new ProjectionEventDto(facts[index].GetType().Name, JsonSerializer.SerializeToUtf8Bytes(facts[index], facts[index].GetType()),
                "json", sequence, s_now, "backfill-policy", GlobalPosition: sequence);
            var delivery = new SharedProjectionDelivery(item, sequence,
                JsonSerializer.SerializeToUtf8Bytes(new ProjectionRequest(Tenant, "work", item, [dto]), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                JsonSerializer.SerializeToUtf8Bytes(sequence), new SharedProjectionControlIndexIntent(WorksReadModelKeys.RuntimeControlIndex));
            (await coordinator.JournalAsync(scope, lease, delivery, ct).ConfigureAwait(false)).ShouldBe(SharedProjectionJournalResult.Journaled);
            (await coordinator.CatchUpAsync(scope, (entry, generation, token) => handler.FoldCatchUpAsync(entry, generation, coordinator, token), ct)
                .ConfigureAwait(false)).ShouldBe(1);
            lease = await coordinator.RefreshLeaseAsync(scope, handler.OrdinaryWriterId, ct).ConfigureAwait(false);
        }
    }

    private static async Task IndexAsync(IServiceProvider services, ReminderTarget target, CancellationToken ct)
        => _ = await InvokeAsync(services.GetRequiredService(SdkType("ReminderIntentIndex")), "EnsureCandidateAsync", target,
            ReminderIdentityCodec.ComputeActorId(target.Tenant, target.Aggregate), ct).ConfigureAwait(false);

    private static Task<object> RunPassAsync(IServiceProvider services, CancellationToken ct)
        => InvokeAsync(services.GetRequiredService(SdkType("ReminderReconciler")), "RunPassAsync", ct);

    private static Task<HealthCheckResult> HealthAsync(IServiceProvider services, CancellationToken ct)
        => services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(registration => registration.Name == "eventstore-reminders-unresolved").Factory(services)
            .CheckHealthAsync(new HealthCheckContext(), ct);

    private static JsonElement Discovery(InMemoryReadModelStore store, string phase)
    {
        using JsonDocument document = JsonDocument.Parse(store.SnapshotRaw("statestore", Prefix + ":control:tenants:" + phase).ShouldNotBeNull());
        return document.RootElement.Clone();
    }

    private static void AssertSafeDiagnostics(WorksReminderRecoveryLogCapture logs)
    {
        foreach (var entry in logs.Entries.Where(entry => entry.EventId is >= 200200 and <= 200219))
        {
            entry.Exception.ShouldBeNull();
            entry.Message.ShouldNotContain("private-dependency-secret");
            entry.Message.ShouldNotContain(Tenant);
            entry.Message.ShouldNotContain("will be retried");
        }
    }

    private static async Task WaitAsync(Func<bool> condition, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(5, timeout.Token).ConfigureAwait(false);
    }

    private static async Task<object> InvokeAsync(object instance, string method, params object[] arguments)
    {
        var operation = (Task)instance.GetType().GetMethod(method)!.Invoke(instance, arguments)!;
        await operation.ConfigureAwait(false);
        return operation.GetType().GetProperty("Result")?.GetValue(operation) ?? new object();
    }

    private static Type SdkType(string name)
        => typeof(EventStoreReminderOptions).Assembly.GetType("Hexalith.EventStore.DomainService." + name, throwOnError: true)!;

    private static T Property<T>(object instance, string name) => (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;
}
