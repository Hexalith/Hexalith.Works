using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Registration;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Recovery;
using Hexalith.Works.Reminders;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hexalith.Works.Runtime;

/// <summary>Registers Works intent, translation, authority and source adapters for SDK-owned mechanics.</summary>
public static class WorksRecoveryExtensions
{
    /// <summary>Registers the Works adapters; EventStore owns reminders, processes and effect submission.</summary>
    public static IServiceCollection AddWorksReminderAndCascadeRecovery(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.GetValue<bool?>("Works:Recovery:UseSdkRuntime") == false
            || configuration.GetValue<bool?>("Works:Projection:UseSdkDelivery") == false)
            throw new InvalidOperationException("Works hosting requires SDK projection, reminder and process runtimes.");
        _ = services.AddOptions<WorksRecoveryOptions>()
            .Bind(configuration.GetSection(WorksRecoveryOptions.SectionName))
            .Validate(static options => options.UseSdkRuntime, "Works requires SDK recovery.")
            .Validate(static options => options.EffectiveMaxStreamPagesPerAggregate > 0, "The stream page budget must be positive.")
            .Validate(static options => options.MaxContextBytes is >= 1024 and <= 16 * 1024 * 1024, "MaxContextBytes must be 1 KiB..16 MiB.")
            .Validate(static options => options.ContextReadTimeout >= TimeSpan.FromMilliseconds(100) && options.ContextReadTimeout <= TimeSpan.FromSeconds(30), "ContextReadTimeout must be 100 ms..30 seconds.")
            .ValidateOnStart();
        string? daprHttpEndpoint = configuration["DAPR_HTTP_ENDPOINT"];
        string? baseAddress = configuration["EventStore:CommandGateway:BaseAddress"];
        string address = !string.IsNullOrWhiteSpace(daprHttpEndpoint) ? daprHttpEndpoint
            : !string.IsNullOrWhiteSpace(baseAddress) ? baseAddress : "http://eventstore";
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? gatewayBaseAddress))
            throw new InvalidOperationException("Works requires an absolute EventStore gateway address.");
        IHttpClientBuilder gateway = services.AddEventStoreGatewayClient(options => options.BaseAddress = gatewayBaseAddress);
        if (!string.IsNullOrWhiteSpace(configuration["EventStore:Authentication:Authority"]))
            _ = gateway.AddEventStoreGatewayAuthorityTransport(configuration);
        else
            _ = gateway.AddEventStoreGatewayWorkloadTransport("work");
        if (!string.IsNullOrWhiteSpace(daprHttpEndpoint))
            _ = gateway.AddEventStoreDaprServiceInvocation("eventstore", configuration["DAPR_API_TOKEN"]);
        services.TryAddScoped<WorksSourceStreamReader>();
        services.TryAddScoped<WorksProcessSourceCatalog>();
        services.TryAddScoped<IReminderTargetCatalog>(provider => provider.GetRequiredService<WorksProcessSourceCatalog>());
        services.TryAddScoped<IProcessDelegationProvider, WorksProcessDelegationProvider>();
        services.TryAddSingleton<IProcessDispositionAuthorizer, WorksProcessDispositionAuthorizer>();
        services.TryAddSingleton<IProcessDispositionAuditSink, WorksProcessDispositionAuditSink>();
        IHttpClientBuilder effects = services.AddHttpClient<ITrustedEffectSubmitter, HttpTrustedEffectSubmitter>(client =>
            client.BaseAddress = gatewayBaseAddress);
        _ = effects.AddEventStoreTrustedEffectWorkloadAssertion();
        if (!string.IsNullOrWhiteSpace(daprHttpEndpoint))
            _ = effects.AddEventStoreDaprServiceInvocation("eventstore", configuration["DAPR_API_TOKEN"]);
        _ = services.AddEventStoreReminders<WorksReminderIntentSource>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ActorTypeName)) options.ActorTypeName = "WorksSdkReminderActor";
            options.Workload = WorksEffectPurposes.Workload;
            options.Purposes[EffectKindCatalog.DateResume] = WorksEffectPurposes.ForKind(EffectKindCatalog.DateResume);
            options.Purposes[EffectKindCatalog.Expiry] = WorksEffectPurposes.ForKind(EffectKindCatalog.Expiry);
        });
        _ = services.AddEventStoreProcesses<WorksProcessSourceCatalog, WorksProcessTranslator>(options =>
        {
            // This finite Works profile supports five direct children. Explicit configuration
            // is preserved and the SDK validates its coupled operation/quantum/freshness limits.
            if (configuration["EventStore:Processes:MaxEffectsPerEnvelope"] is null) options.MaxEffectsPerEnvelope = 5;
            if (configuration["EventStore:Processes:SourceServiceQuantum"] is null) options.SourceServiceQuantum = TimeSpan.FromSeconds(15);
        });
        return services;
    }
}
