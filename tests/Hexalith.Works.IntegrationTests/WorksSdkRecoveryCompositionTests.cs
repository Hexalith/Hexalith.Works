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

    /// <summary>Every committed lifecycle event resolves its wakeup adapter from the actual host.</summary>
    [Fact]
    public async Task RecoveryWakeupInventoryCoversEverySequencedEvent()
    {
        await using var app = WorksHost.Build(["--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999"]);
        using var scope = app.Services.CreateScope();
        Type[] events = [.. typeof(WorkItemCreated).Assembly.GetTypes().Where(type => !type.IsAbstract
            && typeof(Hexalith.EventStore.Contracts.Events.IEventPayload).IsAssignableFrom(type)
            && type.GetProperty(nameof(WorkItemCreated.Sequence)) is not null)];
        events.ShouldNotBeEmpty();
        foreach (Type eventType in events)
        {
            Type service = typeof(IEventStoreDomainEventHandler<>).MakeGenericType(eventType);
            scope.ServiceProvider.GetServices(service).ShouldHaveSingleItem().ShouldNotBeNull()
                .GetType().ShouldBe(typeof(WorksRecoveryWakeupHandler<>).MakeGenericType(eventType));
        }
    }

    /// <summary>Missing, blank and relative Parties addresses are rejected by host startup validation.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative/gateway")]
    [InlineData("ftp://gateway.example.invalid")]
    [InlineData("file:///tmp/parties")]
    [InlineData("mailto:parties@example.invalid")]
    public async Task InvalidPartiesGatewayRefusesStartup(string? address)
    {
        string[] settings = address is null ? [] : [$"--Works:Query:PartiesGatewayBaseAddress={address}"];
        // SDK route discovery resolves query handlers while building, so invalid options may fail
        // even before StartAsync. Both paths refuse before any request or recovery work.
        var exception = await Should.ThrowAsync<OptionsValidationException>(async () =>
        {
            var app = WorksHost.Build(settings);
            await using (app.ConfigureAwait(false))
                await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        });
        exception.Message.ShouldContain("absolute Parties gateway address");
    }

    /// <summary>Invalid fallback schemes fail before requests, while a valid explicit Parties address takes precedence.</summary>
    [Theory]
    [InlineData("ftp://gateway.example.invalid", false)]
    [InlineData("file:///tmp/parties", false)]
    [InlineData("mailto:parties@example.invalid", false)]
    [InlineData("ftp://gateway.example.invalid", true)]
    public async Task PartiesGatewaySchemeValidationUsesEffectiveAddress(string gateway, bool validOverride)
    {
        List<string> settings = [$"--EventStore:CommandGateway:BaseAddress={gateway}"];
        if (validOverride) settings.Add("--Works:Query:PartiesGatewayBaseAddress=https://parties.example.invalid");
        if (validOverride)
        {
            await using var app = WorksHost.Build([.. settings]);
            app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("IPartiesIdentityClient")
                .BaseAddress.ShouldBe(new Uri("https://parties.example.invalid"));
        }
        else
        {
            var exception = await Should.ThrowAsync<OptionsValidationException>(async () =>
            {
                var app = WorksHost.Build([.. settings]);
                await using (app.ConfigureAwait(false))
                    await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            });
            exception.Message.ShouldContain("HTTP or HTTPS");
        }
    }

    /// <summary>
    /// The explicitly configured command gateway supplies Parties identity queries when the Works override is
    /// absent or blank, and a non-blank Works override takes precedence over it.
    /// </summary>
    /// <param name="partiesOverride">The Works Parties override, or <see langword="null"/> when absent.</param>
    /// <param name="expected">The effective Parties client base address.</param>
    [Theory]
    [InlineData(null, "https://gateway.example.invalid")]
    [InlineData("", "https://gateway.example.invalid")]
    [InlineData(" ", "https://gateway.example.invalid")]
    [InlineData("https://parties.example.invalid", "https://parties.example.invalid")]
    public async Task CommandGatewaySuppliesPartiesAddress(string? partiesOverride, string expected)
    {
        List<string> settings = ["--EventStore:CommandGateway:BaseAddress=https://gateway.example.invalid"];
        if (partiesOverride is not null) settings.Add($"--Works:Query:PartiesGatewayBaseAddress={partiesOverride}");
        await using var app = WorksHost.Build([.. settings]);
        // Resolving the validated options runs the same validation that ValidateOnStart applies at startup.
        Should.NotThrow(() => app.Services.GetRequiredService<IOptions<Hexalith.Works.Queries.WorksQueryOptions>>().Value);
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Hexalith.Parties.Client.Abstractions.IPartiesIdentityClient>().ShouldNotBeNull();
        app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("IPartiesIdentityClient").BaseAddress
            .ShouldBe(new Uri(expected));
    }

    /// <summary>Invalid page budgets and projection parking limits fail startup, naming the violated budget, before recovery runs.</summary>
    /// <param name="key">The configuration key carrying the invalid budget.</param>
    /// <param name="value">The invalid budget value.</param>
    /// <param name="violation">The validation message that names the violated budget.</param>
    [Theory]
    [InlineData("Works:Recovery:MaxStreamPagesPerAggregate", "0", "The stream page budget must be positive.")]
    [InlineData("Works:Recovery:MaxStreamPagesPerAggregate", "-1", "The stream page budget must be positive.")]
    [InlineData("Works:Recovery:MaxStreamPagesPerTenant", "0", "The stream page budget must be positive.")]
    [InlineData("Works:Recovery:MaxStreamPagesPerTenant", "-1", "The stream page budget must be positive.")]
    [InlineData("Works:Projection:MaxUndecodableEventDispatchesBeforeParking", "0", "MaxUndecodableEventDispatchesBeforeParking must be positive.")]
    public async Task InvalidRecoveryBudgetsRefuseStartup(string key, string value, string violation)
    {
        await using var app = WorksHost.Build([$"--{key}={value}", "--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999"]);
        OptionsValidationException refused = await Should.ThrowAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
        refused.Failures.ShouldContain(violation);
    }

    /// <summary>The historical alias keeps its documented precedence over the aggregate budget.</summary>
    [Theory]
    [InlineData(null, 7)]
    [InlineData(3, 3)]
    public void StreamPageBudgetAliasBindsAndTakesPrecedence(int? alias, int expected)
    {
        var values = new Dictionary<string, string?> { ["Works:Recovery:MaxStreamPagesPerAggregate"] = "7" };
        if (alias.HasValue) values["Works:Recovery:MaxStreamPagesPerTenant"] = alias.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddWorksReminderAndCascadeRecovery(configuration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<WorksRecoveryOptions>>().Value.EffectiveMaxStreamPagesPerAggregate.ShouldBe(expected);
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
        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(created, context, TestContext.Current.CancellationToken));
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
