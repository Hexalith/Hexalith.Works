using Hexalith.EventStore.Client.Registration;
using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Recovery;
using Hexalith.Works.Recovery.ChildCompletion;
using Hexalith.Works.Reminders;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hexalith.Works.Runtime;

/// <summary>
/// Selects the legacy-default or exclusive SDK reminder/process runtime at the runnable Works host edge.
/// EventStore owns SDK mechanics; Works supplies read-only domain adapters and current delegation.
/// The pure kernel (Contracts/Server/Projections/Reactor) takes none of these runtime dependencies.
/// </summary>
public static class WorksRecoveryExtensions
{
    /// <summary>
    /// Registers the reminder + cascade recovery services and the EventStore command gateway client they
    /// dispatch through. Requires a registered <c>IReadModelStore</c> (via <c>AddEventStoreReadModelStore</c>)
    /// and the Dapr actor runtime (via <see cref="AddWorksDateReminderActors"/>).
    /// </summary>
    public static IServiceCollection AddWorksReminderAndCascadeRecovery(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services.AddOptions<WorksRecoveryOptions>()
            .Bind(configuration.GetSection(WorksRecoveryOptions.SectionName))
            .Validate(static options => options.EffectiveMaxStreamPagesPerAggregate > 0, "MaxStreamPagesPerAggregate must be greater than zero (the deprecated MaxStreamPagesPerTenant alias binds to the same budget).")
            .Validate(static options => options.MaxContextBytes is >= 1024 and <= 16 * 1024 * 1024, "MaxContextBytes must be 1 KiB..16 MiB.")
            .Validate(static options => options.ContextReadTimeout >= TimeSpan.FromMilliseconds(100) && options.ContextReadTimeout <= TimeSpan.FromSeconds(30), "ContextReadTimeout must be 100 ms..30 seconds.")
            .Validate(static options => options.ReminderReconciliationMaxAttempts > 0, "ReminderReconciliationMaxAttempts must be greater than zero.")
            .Validate(static options => options.ReminderReconciliationRetryDelayMilliseconds >= 0, "ReminderReconciliationRetryDelayMilliseconds cannot be negative.")
            .Validate(static options => options.CascadeCheckpointIndexStaleAfterHours > 0, "CascadeCheckpointIndexStaleAfterHours must be greater than zero.")
            .Validate(_ => !configuration.GetValue<bool>("Works:Recovery:UseSdkRuntime") || HasExclusiveSdkComposition(services, configuration),
                "SDK recovery requires SDK projection delivery and exclusive final runtime composition.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        // The command path back into the EventStore gateway that Story 4.5 proved (POST /api/v1/commands).
        // In the AppHost, route gateway traffic through the local Dapr sidecar and the SDK's workload-only
        // transport. Independently configured signed assertions bind each request to its tenant and work domain;
        // Dapr's attribution header alone never grants access. The direct address remains available to other hosts.
        string? daprHttpEndpoint = configuration["DAPR_HTTP_ENDPOINT"];
        string? baseAddress = configuration["EventStore:CommandGateway:BaseAddress"];
        (string effectiveAddress, string effectiveAddressSource) = !string.IsNullOrWhiteSpace(daprHttpEndpoint)
            ? (daprHttpEndpoint, "DAPR_HTTP_ENDPOINT")
            : string.IsNullOrWhiteSpace(baseAddress)
                ? ("http://eventstore", "EventStore:CommandGateway:BaseAddress default")
                : (baseAddress, "EventStore:CommandGateway:BaseAddress");
        if (!Uri.TryCreate(effectiveAddress, UriKind.Absolute, out Uri? gatewayBaseAddress))
        {
            throw new InvalidOperationException(
                $"Configuration value '{effectiveAddressSource}' is not a valid absolute URI: '{effectiveAddress}'.");
        }

        IHttpClientBuilder gatewayClient = services.AddEventStoreGatewayClient(options => options.BaseAddress = gatewayBaseAddress);
        if (!string.IsNullOrWhiteSpace(configuration["EventStore:Authentication:Authority"]))
            _ = gatewayClient.AddEventStoreGatewayAuthorityTransport(configuration);
        else
            _ = gatewayClient.AddEventStoreGatewayWorkloadTransport(WorkCommandSubmission.WorkDomain);
        if (!string.IsNullOrWhiteSpace(daprHttpEndpoint))
        {
            _ = gatewayClient.AddEventStoreDaprServiceInvocation(
                "eventstore",
                configuration["DAPR_API_TOKEN"]);
        }

        services.TryAddSingleton<IWorkCommandSubmitter, EventStoreGatewayWorkCommandSubmitter>();

        if (configuration.GetValue<bool>("Works:Recovery:UseSdkRuntime"))
        {
            if (!HasExclusiveSdkComposition(services, configuration))
                throw new InvalidOperationException("SDK recovery requires SDK projection delivery and exclusive runtime registration.");
            _ = services.AddHostedService<WorksRecoveryCompositionGuard>();
            services.TryAddScoped<WorksSourceStreamReader>();
            services.TryAddScoped<WorksReminderEventHandler>();
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

        // Date-reminder reconciliation-on-recovery. Story 4.8: discovery is the durable pending-date-await index
        // (registry + per-tenant index maintained by the /project dispatcher) re-folded per aggregate — never the
        // retired tenant-wide, null-AggregateId scan the gateway 400-rejects, and never a hand-configured tenant list.
        services.TryAddSingleton<IPendingDateAwaitSource, IndexedPendingDateAwaitSource>();
        services.TryAddSingleton<IDateReminderScheduler, DaprDateReminderScheduler>();
        services.TryAddSingleton<DateReminderReconciler>();
        _ = services.AddHostedService<ReminderReconciliationService>();

        // Terminal-cascade dispatch, checkpoints, and replay.
        services.TryAddSingleton<ReadModelCascadeCheckpointStore>();
        services.TryAddSingleton<ICascadeCheckpointStore>(static services => services.GetRequiredService<ReadModelCascadeCheckpointStore>());
        services.TryAddSingleton<ICascadeCheckpointIndex>(static services => services.GetRequiredService<ReadModelCascadeCheckpointStore>());
        services.TryAddSingleton<ICascadeDescendantSource, StreamReadingCascadeDescendantSource>();
        services.TryAddSingleton<CascadeDispatcher>();
        services.TryAddSingleton<CascadeRecoveryReconciler>();
        _ = services.AddHostedService<CascadeRecoveryService>();

        // Re-readable child-to-parent lookup for live child-completion resume translation.
        services.TryAddSingleton<IChildCompletionAwaitingParentSource, StreamReadingChildCompletionAwaitingParentSource>();

        return services;
    }

    /// <summary>Validates the final service graph before any hosted recovery writer is instantiated.</summary>
    internal static void ValidateWorksRecoveryComposition(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Works:Recovery:UseSdkRuntime") && !HasExclusiveSdkComposition(services, configuration))
            throw new InvalidOperationException("SDK recovery requires SDK projection delivery and exclusive runtime registration.");
    }

    private static bool HasExclusiveSdkComposition(IServiceCollection services, IConfiguration configuration)
        => configuration.GetValue<bool>("Works:Projection:UseSdkDelivery")
            && !services.Any(service => service.ServiceType == typeof(WorksLegacyReminderActorRegistration)
                || service.ServiceType == typeof(IDateReminderScheduler)
                || service.ServiceType == typeof(CascadeDispatcher)
                || service.ImplementationType == typeof(ReminderReconciliationService)
                || service.ImplementationType == typeof(CascadeRecoveryService)
                || service.ImplementationInstance is ReminderReconciliationService or CascadeRecoveryService
                || service.ServiceType == typeof(DateReminderReconciler)
                || service.ServiceType == typeof(CascadeRecoveryReconciler));

    /// <summary>Registers the Dapr date-resume reminder actor on the Works host's actor runtime.</summary>
    public static IServiceCollection AddWorksDateReminderActors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<WorksLegacyReminderActorRegistration>();
        services.AddActors(options => options.Actors.RegisterActor<DateReminderActor>());
        return services;
    }
}
