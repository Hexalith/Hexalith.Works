using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Projections;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic coverage of the <c>Works:Recovery</c> options contract: the fail-fast validation chain
/// registered by <see cref="WorksRecoveryExtensions.AddWorksReminderAndCascadeRecovery"/>, the deprecated
/// <c>MaxStreamPagesPerTenant</c> configuration alias kept for the renamed per-aggregate page budget, and
/// the host wiring that binds <c>Works:Projection</c> into <c>/project</c> and registers the indexed
/// pending-date-await source.
/// </summary>
/// <remarks>
/// Without these facts the fail-fast guard could be weakened silently, turning a misconfiguration into a
/// permanently and silently disabled recovery pass, and the renamed budget could stop honouring configuration
/// that still uses the old key. A no-op <see cref="IPendingDateAwaitSource"/> registration would disable
/// AC #2/#3 while every deterministic source test stayed green.
/// </remarks>
public sealed class WorksRecoveryOptionsTests
{
    [Theory]
    [InlineData("MaxStreamPagesPerAggregate", "0")]
    [InlineData("MaxStreamPagesPerAggregate", "-1")]
    [InlineData("MaxStreamPagesPerTenant", "0")]
    [InlineData("ReminderReconciliationMaxAttempts", "0")]
    [InlineData("ReminderReconciliationRetryDelayMilliseconds", "-1")]
    public void Invalid_recovery_configuration_fails_the_validation_chain(string key, string value)
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{WorksRecoveryOptions.SectionName}:{key}"] = value,
        });

        OptionsValidationException thrown = Should.Throw<OptionsValidationException>(() => options.Value);
        thrown.Failures.ShouldNotBeEmpty();
    }

    [Fact]
    public void Valid_recovery_configuration_binds_and_passes_validation()
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{WorksRecoveryOptions.SectionName}:MaxStreamPagesPerAggregate"] = "7",
        });

        options.Value.EffectiveMaxStreamPagesPerAggregate.ShouldBe(7);
    }

    [Fact]
    public void Deprecated_per_tenant_key_still_binds_the_per_aggregate_page_budget()
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{WorksRecoveryOptions.SectionName}:MaxStreamPagesPerTenant"] = "3",
        });

        // The budget was always applied per aggregate; only the configuration key's name was wrong.
        options.Value.EffectiveMaxStreamPagesPerAggregate.ShouldBe(3);
        options.Value.MaxStreamPagesPerTenant.ShouldBe(3);
    }

    [Fact]
    public void Deprecated_alias_wins_over_the_renamed_key_so_existing_configuration_keeps_its_meaning()
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{WorksRecoveryOptions.SectionName}:MaxStreamPagesPerAggregate"] = "11",
            [$"{WorksRecoveryOptions.SectionName}:MaxStreamPagesPerTenant"] = "4",
        });

        options.Value.EffectiveMaxStreamPagesPerAggregate.ShouldBe(4);
    }

    [Fact]
    public void Unconfigured_recovery_options_use_the_documented_defaults()
    {
        IOptions<WorksRecoveryOptions> options = BuildOptions(new Dictionary<string, string?>(StringComparer.Ordinal));

        options.Value.RunReconciliationOnStartup.ShouldBeTrue();
        options.Value.MaxStreamPagesPerTenant.ShouldBeNull();
        options.Value.EffectiveMaxStreamPagesPerAggregate
            .ShouldBe(WorksRecoveryOptions.DefaultMaxStreamPagesPerAggregate);
    }

    [Fact]
    public void Built_recovery_services_resolve_the_indexed_pending_date_await_source()
    {
        using IDisposable host = BuildHost(
            ["--Works:Recovery:RunReconciliationOnStartup=false"],
            store: null,
            out WebApplication app);

        app.Services.GetRequiredService<IPendingDateAwaitSource>()
            .ShouldBeOfType<IndexedPendingDateAwaitSource>();
    }

    [Fact]
    public async Task Invalid_projection_parking_budget_fails_validate_on_start()
    {
        using IDisposable host = BuildHost(
            [
                "--Works:Recovery:RunReconciliationOnStartup=false",
                "--Works:Projection:MaxUndecodableEventDispatchesBeforeParking=0",
            ],
            store: null,
            out WebApplication app);

        OptionsValidationException thrown = await Should.ThrowAsync<OptionsValidationException>(
            () => app.StartAsync(TestContext.Current.CancellationToken));
        thrown.Failures.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Bound_projection_parking_budget_of_one_is_what_project_uses()
    {
        var store = new Story47InMemoryReadModelStore();
        using IDisposable host = BuildHost(
            [
                "--Works:Recovery:RunReconciliationOnStartup=false",
                "--Works:Projection:MaxUndecodableEventDispatchesBeforeParking=1",
            ],
            store,
            out WebApplication app);
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        string address = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            .ShouldNotBeNull()
            .Addresses
            .ShouldHaveSingleItem();
        using var client = new HttpClient { BaseAddress = new Uri(address, UriKind.Absolute) };
        var request = new ProjectionRequest(
            "tenant-alpha",
            "work",
            "work-1",
            [new ProjectionEventDto(nameof(WorkItemSuspended), "{"u8.ToArray(), "json", 1, default, "corr-1")]);

        using HttpResponseMessage response = await client
            .PostAsJsonAsync("/project", request, new JsonSerializerOptions(JsonSerializerDefaults.Web), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        app.Services.GetRequiredService<IOptions<WorksProjectionOptions>>()
            .Value
            .MaxUndecodableEventDispatchesBeforeParking
            .ShouldBe(1);
        ReadModelEntry<WorkItemProjectionParking> parking = await store
            .GetAsync<WorkItemProjectionParking>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.ProjectionParkingKey("tenant-alpha", "work-1"),
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        parking.Value.ShouldNotBeNull().Parked.ShouldBeTrue();
        parking.Value.FailureCount.ShouldBe(1);

        await app.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static IOptions<WorksRecoveryOptions> BuildOptions(Dictionary<string, string?> configurationValues)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddWorksReminderAndCascadeRecovery(configuration);
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<WorksRecoveryOptions>>();
    }

    private static IDisposable BuildHost(string[] args, Story47InMemoryReadModelStore? store, out WebApplication app)
    {
        string keyDirectory = Directory.CreateTempSubdirectory("works-recovery-options-keys").FullName;
        app = WorksHost.Build(
            args,
            static webHost => webHost.UseUrls("http://127.0.0.1:0"),
            services =>
            {
                if (store is not null)
                {
                    services.RemoveAll<IReadModelStore>();
                    _ = services.AddSingleton<IReadModelStore>(store);
                }

                foreach (ServiceDescriptor hosted in services
                    .Where(static descriptor => descriptor.ServiceType == typeof(IHostedService)
                        && (descriptor.ImplementationType == typeof(ReminderReconciliationService)
                            || descriptor.ImplementationType == typeof(CascadeRecoveryService)))
                    .ToArray())
                {
                    _ = services.Remove(hosted);
                }

                _ = services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
            });
        return new HostKeyDirectory(app, keyDirectory);
    }

    private sealed class HostKeyDirectory(WebApplication app, string keyDirectory) : IDisposable
    {
        public void Dispose()
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(keyDirectory))
            {
                Directory.Delete(keyDirectory, recursive: true);
            }
        }
    }
}
