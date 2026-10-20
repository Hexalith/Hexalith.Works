using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>SDK-only composition and committed Works wakeups replace the retired local runtime.</summary>
public sealed class WorksSdkRecoveryCompositionTests
{
    /// <summary>Blank addresses resolve identically for ordinary and trusted clients; SDK capacity stays finite and validated.</summary>
    [Theory]
    [InlineData("", "", "http://eventstore")]
    [InlineData(" ", "http://direct", "http://direct")]
    [InlineData("http://sidecar:3500", " ", "http://sidecar:3500")]
    [InlineData("http://sidecar:3500", "http://direct", "http://sidecar:3500")]
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


    /// <summary>Absence of historical opt-in flags always selects the SDK runtime.</summary>
    [Fact]
    public void DefaultCompositionSelectsSdkProjectionAndRecovery()
    {
        new WorksProjectionOptions().UseSdkDelivery.ShouldBeTrue();
        new WorksRecoveryOptions().UseSdkRuntime.ShouldBeTrue();
        var services = new ServiceCollection();
        services.AddWorksReminderAndCascadeRecovery(new ConfigurationBuilder().Build());
        services.ShouldContain(service => service.ServiceType == typeof(IReminderRegistrar));
        services.ShouldContain(service => service.ServiceType == typeof(ProcessCheckpointStore));
        string[] removed = ["DateReminderActor", "ReminderReconciliationService", "CascadeRecoveryService", "WorksDomainEventProcessor"];
        typeof(WorksProjectionOptions).Assembly.GetTypes().Select(type => type.Name)
            .ShouldNotContain(name => removed.Contains(name, StringComparer.Ordinal));
    }

    /// <summary>Old switches fail closed rather than activate an unqualified writer.</summary>
    [Theory]
    [InlineData("Works:Recovery:UseSdkRuntime")]
    [InlineData("Works:Projection:UseSdkDelivery")]
    public void LegacyRuntimeSelectionIsRefused(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = "false" }).Build();
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddWorksReminderAndCascadeRecovery(configuration));
    }

    /// <summary>Inconsistent wakeup metadata never discovers a source or schedules an effect.</summary>
    [Theory]
    [InlineData("foreign-domain")]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-aggregate")]
    [InlineData("reserved-tenant")]
    public async Task ForeignWakeupIdentityIsRefusedBeforeDiscovery(string scenario)
    {
        var store = new InMemoryReadModelStore();
        var processes = new ProcessCheckpointStore(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System);
        IReminderRegistrar reminders = Substitute.For<IReminderRegistrar>();
        var handler = new WorksRecoveryWakeupHandler<WorkItemCreated>(processes, reminders);
        string tenant = scenario == "reserved-tenant" ? "tenants" : "tenant-49";
        var created = new WorkItemCreated("work-49", 1, new TenantId(tenant), new WorkItemId("work-49"), new Obligation("synthetic"));
        var context = new EventStoreDomainEventContext(scenario == "foreign-tenant" ? "foreign-tenant" : tenant,
            scenario == "foreign-aggregate" ? "other-work" : "work-49", "01ARZ3NDEKTSV4RRFFQ69G5FAV", 1,
            DateTimeOffset.UnixEpoch, "correlation-49") { Domain = scenario == "foreign-domain" ? "foreign" : "work" };
        await Should.ThrowAsync<Exception>(() => handler.HandleAsync(created, context, TestContext.Current.CancellationToken));
        store.Count.ShouldBe(0);
        reminders.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Redelivery never clears a durable SDK quarantine or advances its checkpoint.</summary>
    [Fact]
    public async Task WakeupPreservesSdkQuarantineAndCheckpoint()
    {
        var store = new InMemoryReadModelStore();
        var processes = new ProcessCheckpointStore(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System);
        var source = new ProcessSource("tenant-49", "work", "work-49");
        await processes.EnsureCandidateAsync(source, cancellationToken: TestContext.Current.CancellationToken);
        ProcessSourceState owned = (await processes.AcquireAsync(source, TestContext.Current.CancellationToken))!;
        owned = await processes.UpdateOwnedAsync(owned, state => state with
        { Checkpoint = 1, Status = ProcessSourceStatus.Quarantined, FailureSequence = 2, ReasonCode = "invalid-evidence" }, TestContext.Current.CancellationToken);
        await processes.ReleaseAsync(owned, TestContext.Current.CancellationToken);
        ProcessSourceState before = await processes.GetAsync(source, TestContext.Current.CancellationToken);
        IReminderRegistrar reminders = Substitute.For<IReminderRegistrar>();
        reminders.ConvergeAsync(Arg.Any<ReminderTarget>(), Arg.Any<CancellationToken>()).Returns(new ReminderConvergenceResult(0, 0, 0, 1, 0));
        var handler = new WorksRecoveryWakeupHandler<WorkItemCreated>(processes, reminders);
        var created = new WorkItemCreated(source.Aggregate, 1, new TenantId(source.Tenant), new WorkItemId(source.Aggregate), new Obligation("synthetic"));
        var context = new EventStoreDomainEventContext(source.Tenant, source.Aggregate, "01ARZ3NDEKTSV4RRFFQ69G5FAV", 2,
            DateTimeOffset.UnixEpoch, "correlation-49") { Domain = "work" };
        await handler.HandleAsync(created, context, TestContext.Current.CancellationToken);
        await handler.HandleAsync(created, context, TestContext.Current.CancellationToken);
        (await processes.GetAsync(source, TestContext.Current.CancellationToken)).ShouldBe(before);
        await reminders.Received(2).ConvergeAsync(new ReminderTarget(source.Tenant, "work", source.Aggregate), Arg.Any<CancellationToken>());
    }
}
