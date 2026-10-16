using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Runtime.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>SDK recovery is one opt-in runtime and keeps all mechanical ownership in EventStore.</summary>
public sealed class WorksSdkRecoveryCompositionTests
{
    /// <summary>Blank addresses resolve identically for ordinary and trusted clients; SDK capacity stays finite and validated.</summary>
    [Theory]
    [InlineData("", "", "http://eventstore")]
    [InlineData(" ", "http://direct", "http://direct")]
    [InlineData("http://sidecar:3500", " ", "http://sidecar:3500")]
    public void SdkTransportsShareValidatedEffectiveAddress(string dapr, string gateway, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Works:Recovery:UseSdkRuntime"] = "true", ["Works:Projection:UseSdkDelivery"] = "true",
            ["DAPR_HTTP_ENDPOINT"] = dapr, ["EventStore:CommandGateway:BaseAddress"] = gateway,
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddWorksReminderAndCascadeRecovery(configuration);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        factory.CreateClient("IEventStoreGatewayClient").BaseAddress.ShouldBe(new Uri(expected));
        factory.CreateClient("ITrustedEffectSubmitter").BaseAddress.ShouldBe(new Uri(expected));
        var profile = provider.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value;
        profile.MaxEffectsPerEnvelope.ShouldBe(5);
        profile.SourceServiceQuantum.ShouldBe(TimeSpan.FromSeconds(15));
        profile.Validate().ShouldBeEmpty();
    }

    /// <summary>Operator overrides cannot conceal an operation/quantum or freshness mismatch.</summary>
    [Fact]
    public void SdkCapacityOverrideRequiresCoupledBudgets()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Works:Recovery:UseSdkRuntime"] = "true", ["Works:Projection:UseSdkDelivery"] = "true",
            ["EventStore:Processes:MaxEffectsPerEnvelope"] = "6",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddWorksReminderAndCascadeRecovery(configuration);
        using var provider = services.BuildServiceProvider();
        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EventStoreProcessOptions>>().Value);
    }

    /// <summary>Active SDK composition has one trusted effect transport and no bespoke effect writers.</summary>
    [Fact]
    public void SdkModeRegistersOnlySdkMechanics()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Works:Recovery:UseSdkRuntime"] = "true", ["Works:Projection:UseSdkDelivery"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddWorksReminderAndCascadeRecovery(configuration);

        services.Count(service => service.ServiceType == typeof(IProcessTranslator)).ShouldBe(1);
        services.Count(service => service.ServiceType == typeof(IReminderIntentSource)).ShouldBe(1);
        services.Count(service => service.ServiceType == typeof(IReminderTargetCatalog)).ShouldBe(1);
        services.Count(service => service.ServiceType == typeof(ITrustedEffectSubmitter)).ShouldBe(1);
        services.Any(service => service.ServiceType == typeof(DateReminderReconciler)
            || service.ServiceType == typeof(CascadeDispatcher)
            || service.ServiceType == typeof(IDateReminderScheduler)).ShouldBeFalse();
    }

    /// <summary>Contradictory projection mode or a previously registered legacy writer is refused.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingRuntimeRegistrationIsRejected(bool legacyWriter)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Works:Recovery:UseSdkRuntime"] = "true", ["Works:Projection:UseSdkDelivery"] = legacyWriter ? "true" : "false",
        }).Build();
        var services = new ServiceCollection();
        if (legacyWriter) services.AddSingleton<IDateReminderScheduler, DaprDateReminderScheduler>();
        Should.Throw<InvalidOperationException>(() => services.AddWorksReminderAndCascadeRecovery(configuration));
    }
    /// <summary>Final composition catches legacy writers added after SDK registration and in the host customization callback.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalCompositionRefusesLateLegacyWriter(bool callback)
    {
        if (callback)
        {
            Should.Throw<InvalidOperationException>(() => WorksHost.Build([
                "--Works:Recovery:UseSdkRuntime=true", "--Works:Projection:UseSdkDelivery=true",
                "--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999"],
                configureServices: services => services.AddSingleton<IDateReminderScheduler, DaprDateReminderScheduler>()));
            return;
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Works:Recovery:UseSdkRuntime"] = "true", ["Works:Projection:UseSdkDelivery"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddWorksReminderAndCascadeRecovery(configuration);
        services.AddSingleton<IDateReminderScheduler, DaprDateReminderScheduler>();
        Should.Throw<InvalidOperationException>(() => services.ValidateWorksRecoveryComposition(configuration));
    }

    /// <summary>Resolved instance/factory services are refused before any hosted StartAsync, including earlier services.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task ActualHostedWritersAreRefusedBeforeAnyStart(bool cascade, bool factory, bool legacyFirst)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Works:Recovery:UseSdkRuntime"] = "true", ["Works:Projection:UseSdkDelivery"] = "true" });
        IHostedService sentinel = Substitute.For<IHostedService>();
        builder.Services.AddSingleton(sentinel);
        IHostedService legacy = LegacyHostedWriter(cascade);
        void AddLegacy()
        {
            if (factory) builder.Services.AddSingleton<IHostedService>(_ => legacy);
            else builder.Services.AddSingleton(legacy);
        }
        if (legacyFirst) AddLegacy();
        if (legacyFirst && !factory)
        {
            Should.Throw<InvalidOperationException>(() => builder.Services.AddWorksReminderAndCascadeRecovery(builder.Configuration));
            sentinel.ReceivedCalls().ShouldBeEmpty();
            return;
        }
        var originalHosted = builder.Services.Where(service => service.ServiceType == typeof(IHostedService)).ToHashSet();
        builder.Services.AddWorksReminderAndCascadeRecovery(builder.Configuration);
        // Exercise the production lifecycle guard without starting unrelated SDK infrastructure in this focused host.
        foreach (ServiceDescriptor descriptor in builder.Services.Where(service => service.ServiceType == typeof(IHostedService)
            && !originalHosted.Contains(service) && service.ImplementationType != typeof(WorksRecoveryCompositionGuard)).ToArray())
            builder.Services.Remove(descriptor);
        if (!legacyFirst) AddLegacy();
        using IHost host = builder.Build();
        Exception refusal = await Should.ThrowAsync<Exception>(() => host.StartAsync(TestContext.Current.CancellationToken));
        refusal.Message.ShouldContain("exclusive");
        if (factory) refusal.Message.ShouldBe("SDK recovery requires exclusive final hosted writer composition.");
        await sentinel.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        if (legacy is BackgroundService background) background.ExecuteTask.ShouldBeNull();
    }

    /// <summary>The supported WebApplication customization callback cannot hide a legacy hosted factory.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCallbackFactoryIsValidatedBeforeStart(bool cascade)
    {
        IHostedService sentinel = Substitute.For<IHostedService>();
        IHostedService legacy = LegacyHostedWriter(cascade);
        await using var app = WorksHost.Build(["--environment=Development", "--Works:Recovery:UseSdkRuntime=true",
            "--Works:Projection:UseSdkDelivery=true", "--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999"],
            configureServices: services =>
            {
                foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType != typeof(WorksRecoveryCompositionGuard)).ToArray()) services.Remove(descriptor);
                services.Insert(0, ServiceDescriptor.Singleton(sentinel));
                services.AddSingleton<IHostedService>(_ => legacy);
            });
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
        refusal.Message.ShouldBe("SDK recovery requires exclusive final hosted writer composition.");
        await sentinel.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        ((BackgroundService)legacy).ExecuteTask.ShouldBeNull();
    }

    /// <summary>The final guard permits a valid generic SDK host and the default legacy runtime.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidGenericHostCompositionStarts(bool sdk)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Works:Recovery:UseSdkRuntime"] = sdk.ToString(), ["Works:Projection:UseSdkDelivery"] = sdk.ToString(),
            ["Works:Recovery:RunReconciliationOnStartup"] = "false" });
        builder.Services.AddWorksReminderAndCascadeRecovery(builder.Configuration);
        foreach (ServiceDescriptor descriptor in builder.Services.Where(service => service.ServiceType == typeof(IHostedService)
            && service.ImplementationType != typeof(WorksRecoveryCompositionGuard)).ToArray()) builder.Services.Remove(descriptor);
        IHostedService sentinel = Substitute.For<IHostedService>();
        builder.Services.AddSingleton(sentinel);
        using IHost host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await sentinel.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private static IHostedService LegacyHostedWriter(bool cascade)
    {
        if (!cascade)
        {
            var reconciler = new DateReminderReconciler(Substitute.For<IPendingDateAwaitSource>(), Substitute.For<IDateReminderScheduler>(),
                Substitute.For<IWorkCommandSubmitter>(), TimeProvider.System, NullLogger<DateReminderReconciler>.Instance);
            return new ReminderReconciliationService(reconciler, Options.Create(new WorksRecoveryOptions()),
                NullLogger<ReminderReconciliationService>.Instance, TimeProvider.System);
        }
        var dispatcher = new CascadeDispatcher(Substitute.For<ICascadeCheckpointStore>(), Substitute.For<ICascadeDescendantSource>(),
            Substitute.For<IWorkCommandSubmitter>(), NullLogger<CascadeDispatcher>.Instance);
        return new CascadeRecoveryService(new CascadeRecoveryReconciler(Substitute.For<ICascadeCheckpointIndex>(),
            Substitute.For<ICascadeCheckpointStore>(), dispatcher, TimeProvider.System, Options.Create(new WorksRecoveryOptions()),
            NullLogger<CascadeRecoveryReconciler>.Instance), NullLogger<CascadeRecoveryService>.Instance);
    }

    /// <summary>Legacy actor registration itself is an active writer and is refused in either order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyActorCannotJoinSdkComposition(bool legacyFirst)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Works:Recovery:UseSdkRuntime"] = "true", ["Works:Projection:UseSdkDelivery"] = "true",
        }).Build();
        var services = new ServiceCollection();
        if (legacyFirst)
        {
            services.AddWorksDateReminderActors();
            Should.Throw<InvalidOperationException>(() => services.AddWorksReminderAndCascadeRecovery(configuration));
        }
        else
        {
            services.AddWorksReminderAndCascadeRecovery(configuration);
            services.AddWorksDateReminderActors();
            Should.Throw<InvalidOperationException>(() => services.ValidateWorksRecoveryComposition(configuration));
        }
    }

    /// <summary>Malformed callbacks cannot acquire legacy markers or erase an already durable SDK quarantine.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedDeliveryCannotEraseSdkUnresolvedState(bool invalidMetadata)
    {
        var store = new InMemoryReadModelStore();
        var checkpoints = new ProcessCheckpointStore(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System);
        var source = new ProcessSource("tenant-415", "work", "quarantined-source");
        await checkpoints.EnsureCandidateAsync(source, cancellationToken: TestContext.Current.CancellationToken);
        ProcessSourceState owned = (await checkpoints.AcquireAsync(source, TestContext.Current.CancellationToken))!;
        owned = await checkpoints.UpdateOwnedAsync(owned, state => state with
        {
            Checkpoint = 1, Status = ProcessSourceStatus.Quarantined, FailureSequence = 2, ReasonCode = "invalid-evidence",
        }, TestContext.Current.CancellationToken);
        await checkpoints.ReleaseAsync(owned, TestContext.Current.CancellationToken);
        ProcessSourceState before = await checkpoints.GetAsync(source, TestContext.Current.CancellationToken);
        IReminderRegistrar registrar = Substitute.For<IReminderRegistrar>();
        registrar.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>())
            .Returns(new ReminderConvergenceResult(0, 0, 0, 1, 0));
        var services = new ServiceCollection();
        services.AddSingleton(checkpoints);
        services.AddSingleton(Options.Create(new WorksRecoveryOptions { UseSdkRuntime = true }));
        services.AddSingleton(registrar);
        services.AddScoped<WorksReminderEventHandler>();
        using ServiceProvider provider = services.BuildServiceProvider();
        IEventStoreDomainEventMarkerStore markers = Substitute.For<IEventStoreDomainEventMarkerStore>();
        var processor = new WorksDomainEventProcessor(provider.GetRequiredService<IServiceScopeFactory>(), markers,
            NullLogger<WorksDomainEventProcessor>.Instance);
        var envelope = new EventStoreDomainEventEnvelope("01ARZ3NDEKTSV4RRFFQ69G5FAV", source.Aggregate,
            source.Tenant, "UnknownWorksFact", 2, DateTimeOffset.UnixEpoch, "synthetic-correlation", "json", [123])
            { Domain = invalidMetadata ? "foreign" : "work" };
        EventStoreDomainEventProcessingResult result = await processor.ProcessAsync(envelope, TestContext.Current.CancellationToken);
        result.ShouldBe(invalidMetadata ? EventStoreDomainEventProcessingResult.RetryableInProgress : EventStoreDomainEventProcessingResult.Processed);
        (await checkpoints.GetAsync(source, TestContext.Current.CancellationToken)).ShouldBe(before);
        markers.ReceivedCalls().ShouldBeEmpty();
    }

}
