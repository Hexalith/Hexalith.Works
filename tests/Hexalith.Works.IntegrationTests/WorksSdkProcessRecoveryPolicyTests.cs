using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Qualifies bounded SDK process recovery policy with real Works translation and durable state.</summary>
public sealed class WorksSdkProcessRecoveryPolicyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A retained publication yields within the exact one- or two-unit pass scheduling bound.</summary>
    /// <param name="workBudget">The work units available per pass.</param>
    /// <param name="passBound">The pass bound including catalog, candidate and repair scheduling.</param>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    public async Task RetainedPublicationFailureServesLaterSourceWithinBudgetBound(int workBudget, int passBound)
    {
        var store = new InMemoryReadModelStore();
        var blocked = new ProcessSource("tenant-a", "work", "blocked");
        var healthy = new ProcessSource("tenant-b", "work", "healthy");
        var fault = new WorksProcessRecoveryFaultStore(store)
        { RejectWrite = key => key == ProcessStateKeys.CandidateMarker(blocked) };
        var options = Profile(workBudget);
        using (ServiceProvider first = Runtime(fault, options, Catalog(blocked, healthy)))
            _ = await first.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);
        ProcessSweepState afterFirst = await Sweeps(store, options).GetAsync(Token);
        afterFirst.CatalogAfterKey.ShouldBe(blocked.CatalogKey);
        afterFirst.PendingPublications.Values.ShouldContain(blocked);

        // The pass bound includes reconstruction: no in-memory catalog page or retry state survives.
        using ServiceProvider restarted = Runtime(fault, options, Catalog(blocked, healthy));
        for (int pass = 1; pass < passBound; pass++)
            _ = await restarted.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);

        (await Checkpoints(store, options).GetAsync(healthy, Token)).Checkpoint.ShouldBe(1);
        (await Sweeps(store, options).GetAsync(Token)).PendingPublications.Values.ShouldContain(blocked);
        (await ReadinessAsync(store, options)).Status.ShouldBe(HealthStatus.Degraded);
    }

    /// <summary>Saturation retains the rejected key while three work units still service a retained healthy source.</summary>
    /// <param name="repairAfterRestart">Whether to repair publication and verify later admission.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaturatedPublicationRetainsCatalogKeyWithoutFreezingCandidates(bool repairAfterRestart)
    {
        var store = new InMemoryReadModelStore();
        var options = Profile(3);
        var healthy = new ProcessSource("tenant-healthy", "work", "retained");
        var rejected = new ProcessSource("tenant-new", "work", "rejected");
        var later = new ProcessSource(healthy.Tenant, healthy.Domain, "later-retained");
        await Checkpoints(store, options).EnsureCandidateAsync(healthy, cancellationToken: Token);
        await Checkpoints(store, options).EnsureCandidateAsync(later, cancellationToken: Token);
        var pending = Enumerable.Range(0, 128).Select(index => new ProcessSource("tenant-stuck", "work", $"pending-{index:000}"))
            .ToDictionary(ProcessStateKeys.SourceId, source => source, StringComparer.Ordinal);
        ProcessSweepState seeded = await Sweeps(store, options).GetAsync(Token);
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Sweep, seeded with
        { PendingPublications = pending, RetainedCandidates = 130 });
        var fault = new WorksProcessRecoveryFaultStore(store)
        { RejectWrite = key => key == ProcessStateKeys.Candidates("tenant-stuck", 0) };
        using (ServiceProvider services = Runtime(fault, options, Catalog(rejected)))
            _ = await services.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);

        ProcessSweepState refused = await Sweeps(store, options).GetAsync(Token);
        refused.CatalogAfterKey.ShouldBeNull();
        refused.CatalogComplete.ShouldBeFalse();
        refused.PendingPublications.Count.ShouldBe(128);
        refused.PendingPublications.Values.ShouldNotContain(rejected);
        refused.Reasons.ShouldContainKey("candidate-publication");
        (await Checkpoints(store, options).ContainsCandidateAsync(rejected, Token)).ShouldBeFalse();
        store.SnapshotRaw(options.StateStoreName, ProcessStateKeys.Candidates(rejected.Tenant, 0)).ShouldBeNull();
        (await Checkpoints(store, options).GetAsync(healthy, Token)).Checkpoint.ShouldBe(1);
        (await ReadinessAsync(store, options)).Status.ShouldBe(HealthStatus.Degraded);
        // Publication is still fully saturated after reconstruction; the next candidate must get its turn.
        using (ServiceProvider stillSaturated = Runtime(fault, options, Catalog(rejected)))
            _ = await stillSaturated.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);
        (await Checkpoints(store, options).GetAsync(later, Token)).Checkpoint.ShouldBe(1);
        ProcessSweepState stillHeld = await Sweeps(store, options).GetAsync(Token);
        stillHeld.CatalogAfterKey.ShouldBeNull();
        stillHeld.PendingPublications.Keys.Order(StringComparer.Ordinal).ShouldBe(pending.Keys.Order(StringComparer.Ordinal));
        (await Checkpoints(store, options).ContainsCandidateAsync(rejected, Token)).ShouldBeFalse();
        if (!repairAfterRestart) return;

        fault.RejectWrite = null;
        using ServiceProvider restarted = Runtime(fault, options, Catalog(rejected));
        // One fair repair frees a publication slot; a subsequent catalog visit must accept the held key.
        for (int pass = 0; pass < 3; pass++)
            _ = await restarted.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);
        (await Checkpoints(store, options).ContainsCandidateAsync(rejected, Token)).ShouldBeTrue();
        // Admission does not erase this cycle's unresolved observation or the other 127 repair intents.
        (await Checkpoints(store, options).GetAsync(rejected, Token)).ObservedUnresolved.ShouldBeTrue();
        ProcessSweepState accepted = await Sweeps(store, options).GetAsync(Token);
        accepted.PendingPublications.Values.ShouldNotContain(rejected);
        accepted.PendingPublications.Count.ShouldBe(125);
        ProcessSource[] repaired = [.. pending.Where(pair => !accepted.PendingPublications.ContainsKey(pair.Key)).Select(pair => pair.Value)];
        repaired.Length.ShouldBe(3);
        foreach (ProcessSource repairedSource in repaired)
        {
            (await Checkpoints(store, options).ContainsCandidateAsync(repairedSource, Token)).ShouldBeTrue();
            store.Snapshot<ProcessCandidateMarker>(options.StateStoreName, ProcessStateKeys.CandidateMarker(repairedSource))
                .ShouldNotBeNull().Source.ShouldBe(repairedSource);
        }
        accepted.PendingPublications.Keys.Concat(repaired.Select(ProcessStateKeys.SourceId)).Order(StringComparer.Ordinal)
            .ShouldBe(pending.Keys.Order(StringComparer.Ordinal));
        (await ReadinessAsync(store, options)).Status.ShouldBe(HealthStatus.Degraded);
    }

    /// <summary>Read admission yields once with the exact candidate cursor and recovers after reconstruction.</summary>
    /// <param name="phase">The read boundary that refuses admission.</param>
    [Theory]
    [InlineData("tenant-page")]
    [InlineData("tenant-tail")]
    [InlineData("candidate-page")]
    [InlineData("candidate-tail")]
    [InlineData("source-state")]
    public async Task ReadAdmissionRetainsDurableCursorAndDegradedCoverage(string phase)
    {
        var store = new InMemoryReadModelStore();
        var options = Profile(16);
        var source = new ProcessSource("tenant-admission", "work", "retained");
        await Checkpoints(store, options).EnsureCandidateAsync(source, cancellationToken: Token);
        // Restore a valid linked inventory with both tenant and candidate traversal away from their heads.
        var earlierTenant = new ProcessSource("tenant-earlier", "work", "earlier-tenant-source");
        var earlierCandidate = new ProcessSource(source.Tenant, source.Domain, "earlier-candidate");
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Tenants(0), new ProcessIndexPage<string>(1, 0, [earlierTenant.Tenant], 1));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Tenants(1), new ProcessIndexPage<string>(1, 1, [source.Tenant], null));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.TenantsTail, new ProcessIndexTail(1, 1));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Candidates(source.Tenant, 0), new ProcessIndexPage<ProcessSource>(1, 0, [earlierCandidate], 1));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Candidates(source.Tenant, 1), new ProcessIndexPage<ProcessSource>(1, 1, [source], null));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.CandidatesTail(source.Tenant), new ProcessIndexTail(1, 1));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Candidates(earlierTenant.Tenant, 0), new ProcessIndexPage<ProcessSource>(1, 0, [earlierTenant], null));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.TenantMarker(earlierTenant.Tenant), new ProcessTenantMarker(1, earlierTenant.Tenant));
        foreach (ProcessSource member in new[] { earlierTenant, earlierCandidate })
            store.SeedRaw(options.StateStoreName, ProcessStateKeys.CandidateMarker(member), new ProcessCandidateMarker(1, member));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Sweep,
            (await Sweeps(store, options).GetAsync(Token)) with { RetainedCandidates = 3 });
        ProcessSweepStore sweeps = Sweeps(store, options);
        ProcessSweepState owned = (await sweeps.AcquireAsync(Token)).ShouldNotBeNull();
        owned = await sweeps.UpdateOwnedAsync(owned, current => current with
        {
            CatalogComplete = true,
            NextCatalog = false,
            TenantPage = 1,
            TenantOffset = phase == "tenant-tail" ? 1 : 0,
            CandidatePage = phase == "tenant-tail" ? 0 : 1,
            CandidateOffset = phase == "candidate-tail" ? 1 : 0,
            ExpectedTenantPage = true,
            ExpectedCandidatePage = phase != "tenant-tail",
            TenantProbeMisses = 1,
            CandidateProbeMisses = phase == "tenant-tail" ? 0 : 1,
        }, Token);
        await sweeps.ReleaseAsync(owned, Token);
        string failedKey = phase switch
        {
            "tenant-page" => ProcessStateKeys.Tenants(1),
            "tenant-tail" => ProcessStateKeys.TenantsTail,
            "candidate-page" => ProcessStateKeys.Candidates(source.Tenant, 1),
            "candidate-tail" => ProcessStateKeys.CandidatesTail(source.Tenant),
            _ => ProcessStateKeys.Source(source),
        };
        bool refuse = true;
        var fault = new WorksProcessRecoveryFaultStore(store)
        {
            ReadFailure = key =>
            {
                if (key != failedKey || !refuse) return null;
                refuse = false;
                return AdmissionFailure();
            },
        };
        using (ServiceProvider services = Runtime(fault, options, Catalog()))
        {
            ProcessReconciliationPass pass = await services.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);
            pass.CycleComplete.ShouldBeFalse();
        }
        fault.RefusedReads.ShouldBe(1);
        ProcessSweepState refused = await sweeps.GetAsync(Token);
        Cursor(refused).ShouldBe(Cursor(owned));
        refused.CandidatesComplete.ShouldBeFalse();
        refused.Reasons["read-worker-exhausted"].ShouldBe(1);
        refused.Reasons.Keys.ShouldNotContain("candidate-page-unreadable");
        refused.Reasons.Keys.ShouldNotContain("tenant-page-unreadable");
        (await ReadinessAsync(store, options)).Status.ShouldBe(HealthStatus.Degraded);

        using ServiceProvider restarted = Runtime(store, options, Catalog(source));
        Cursor(await restarted.GetRequiredService<ProcessSweepStore>().GetAsync(Token)).ShouldBe(Cursor(owned));
        // Finish the degraded cycle, then require fresh clean coverage rather than clearing reasons by hand.
        for (int pass = 0; pass < 3; pass++)
            _ = await restarted.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);
        ProcessSourceState recovered = await Checkpoints(store, options).GetAsync(source, Token);
        recovered.Checkpoint.ShouldBe(1);
        recovered.Status.ShouldBe(ProcessSourceStatus.Active);
        (await ReadinessAsync(store, options)).Status.ShouldBe(HealthStatus.Healthy);
    }

    /// <summary>A finite no-tail probe completes with unresolved evidence, then retries repaired pages next cycle.</summary>
    [Fact]
    public async Task ProbeCappedMissingPageRemainsDegradedAndRetriesAfterReconstruction()
    {
        var store = new InMemoryReadModelStore();
        var options = Profile(16);
        var source = new ProcessSource("tenant-hole", "work", "repaired");
        var earlier = new ProcessSource(source.Tenant, source.Domain, "earlier");
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Tenants(0), new ProcessIndexPage<string>(1, 0, [source.Tenant], null));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Candidates(source.Tenant, 0), new ProcessIndexPage<ProcessSource>(1, 0, [earlier], 1));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.CandidateMarker(earlier), new ProcessCandidateMarker(1, earlier));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.CandidateMarker(source), new ProcessCandidateMarker(1, source));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.TenantMarker(source.Tenant), new ProcessTenantMarker(1, source.Tenant));
        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Sweep,
            (await Sweeps(store, options).GetAsync(Token)) with { RetainedCandidates = 2 });
        using (ServiceProvider first = Runtime(store, options, Catalog()))
            _ = await first.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);
        ProcessSweepState degraded = await Sweeps(store, options).GetAsync(Token);
        degraded.Complete.ShouldBeTrue();
        degraded.Unresolved.ShouldBeGreaterThan(0);
        // The linked missing page plus four fallback probes are counted without inventing a tail.
        degraded.Reasons["candidate-page-missing"].ShouldBe(5);
        degraded.Reasons.Keys.ShouldNotContain("candidate-page-unreadable");
        store.SnapshotRaw(options.StateStoreName, ProcessStateKeys.CandidatesTail(source.Tenant)).ShouldBeNull();
        (await ReadinessAsync(store, options)).Status.ShouldBe(HealthStatus.Degraded);
        (await Checkpoints(store, options).GetAsync(earlier, Token)).Checkpoint.ShouldBe(1);
        (await Checkpoints(store, options).GetAsync(source, Token)).Checkpoint.ShouldBe(0);

        store.SeedRaw(options.StateStoreName, ProcessStateKeys.Candidates(source.Tenant, 1), new ProcessIndexPage<ProcessSource>(1, 1, [source], null));
        using ServiceProvider restarted = Runtime(store, options, Catalog());
        _ = await restarted.GetRequiredService<ProcessReconciler>().RunPassAsync(Token);
        ProcessSweepState repaired = await Sweeps(store, options).GetAsync(Token);
        repaired.Cycle.ShouldBe(degraded.Cycle + 1);
        repaired.Complete.ShouldBeTrue();
        (await Checkpoints(store, options).GetAsync(source, Token)).Checkpoint.ShouldBe(1);
        repaired.Unresolved.ShouldBe(0, string.Join(", ", repaired.Reasons.Select(pair => $"{pair.Key}={pair.Value}")));
        (await ReadinessAsync(store, options)).Status.ShouldBe(HealthStatus.Healthy);
    }

    private static EventStoreProcessOptions Profile(int workBudget)
    {
        var options = new EventStoreProcessOptions
        {
            SourcesPerPass = workBudget,
            SupportedCandidateCount = 130,
            OperationTimeout = TimeSpan.FromMilliseconds(100),
            MaxEffectsPerEnvelope = 1,
            SourceServiceQuantum = TimeSpan.FromMilliseconds(900),
            LeaseDuration = TimeSpan.FromSeconds(5),
            ReconciliationInterval = TimeSpan.FromSeconds(1),
            ReadinessMaxAge = TimeSpan.FromMinutes(20),
        };
        options.Validate().ShouldBeEmpty();
        return options;
    }

    private static (int, int, int, int, bool, bool, int, int) Cursor(ProcessSweepState state)
        => (state.TenantPage, state.TenantOffset, state.CandidatePage, state.CandidateOffset,
            state.ExpectedTenantPage, state.ExpectedCandidatePage, state.TenantProbeMisses, state.CandidateProbeMisses);

    private static ProcessSweepStore Sweeps(IReadModelStore store, EventStoreProcessOptions options)
        => new(store, Options.Create(options), TimeProvider.System);

    private static ProcessCheckpointStore Checkpoints(IReadModelStore store, EventStoreProcessOptions options)
        => new(store, Options.Create(options), TimeProvider.System);

    private static Task<HealthCheckResult> ReadinessAsync(IReadModelStore store, EventStoreProcessOptions options)
        => new ProcessReadinessHealthCheck(Sweeps(store, options), Options.Create(options), TimeProvider.System)
            .CheckHealthAsync(new HealthCheckContext(), Token);

    private static Exception AdmissionFailure()
        => (Exception)Activator.CreateInstance(typeof(ProcessReconciler).Assembly
            .GetType("Hexalith.EventStore.DomainService.ProcessReadAdmissionException", throwOnError: true)!, nonPublic: true)!;

    private static IProcessSourceCatalog Catalog(params ProcessSource[] sources)
    {
        var catalog = Substitute.For<IProcessSourceCatalog>();
        catalog.ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            ProcessSource[] remaining = [.. sources.OrderBy(source => source.CatalogKey, StringComparer.Ordinal)
                .Where(source => call.ArgAt<string?>(0) is not { } after || string.CompareOrdinal(source.CatalogKey, after) > 0)];
            ProcessSource[] page = [.. remaining.Take(call.ArgAt<int>(1))];
            return Task.FromResult(new ProcessSourcePage(page, remaining.Length > page.Length ? page[^1].CatalogKey : null));
        });
        return catalog;
    }

    private static ServiceProvider Runtime(IReadModelStore store, EventStoreProcessOptions options, IProcessSourceCatalog catalog)
    {
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            StreamReadRequest request = call.Arg<StreamReadRequest>();
            var id = new WorkItemId(request.AggregateId!);
            StreamReadPage committed = Story48Streams.Page(request.Tenant, id.Value,
                new WorkItemCreated(id.Value, 1, new TenantId(request.Tenant), id, new Obligation("process policy")));
            StreamReadEvent[] tail = [.. committed.Events.Where(fact => fact.SequenceNumber > request.FromSequence)];
            return Task.FromResult(committed with
            {
                Events = tail,
                Metadata = committed.Metadata with
                { FromSequence = request.FromSequence, EventCount = tail.Length, LastSequenceReturned = tail.LastOrDefault()?.SequenceNumber },
            });
        });
        var services = new ServiceCollection();
        services.AddSingleton(catalog);
        services.AddSingleton(Checkpoints(store, options));
        services.AddSingleton(Sweeps(store, options));
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new CheckpointedProcessRunner(gateway,
            new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()))),
            Substitute.For<IProcessDelegationProvider>(), Substitute.For<ITrustedEffectSubmitter>(),
            Checkpoints(store, options), Options.Create(options)));
        services.AddSingleton<ProcessReconciler>();
        return services.BuildServiceProvider();
    }
}
