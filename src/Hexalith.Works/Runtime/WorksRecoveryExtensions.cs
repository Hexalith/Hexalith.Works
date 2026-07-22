using Hexalith.EventStore.Client.Registration;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Recovery.ChildCompletion;
using Hexalith.Works.Reminders;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hexalith.Works.Runtime;

/// <summary>
/// Host-edge composition for the Story 4.6 reminder and reactor recovery runtime. Everything registered here
/// lives only in the runnable Works host (<c>src/Hexalith.Works</c>): the EventStore command gateway client,
/// the date-resume reminder reconciler + its hosted startup pass, and the terminal-cascade dispatcher with
/// its checkpoint store. The pure kernel (Contracts/Server/Projections/Reactor) takes none of these
/// dependencies (fitness-asserted).
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

        _ = services.Configure<WorksRecoveryOptions>(configuration.GetSection(WorksRecoveryOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        // The command path back into the EventStore gateway that Story 4.5 proved (POST /api/v1/commands).
        // In the AppHost, route all gateway traffic through the local Dapr sidecar. EventStore then receives
        // the sidecar-authenticated dapr-caller-app-id=works identity and applies its explicit internal-caller
        // allow-list. The direct address remains a fallback for hosts that compose recovery without Dapr.
        string? daprHttpEndpoint = configuration["DAPR_HTTP_ENDPOINT"];
        IHttpClientBuilder gatewayClient = services.AddEventStoreGatewayClient(options =>
        {
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

            options.BaseAddress = gatewayBaseAddress;
        });
        if (!string.IsNullOrWhiteSpace(daprHttpEndpoint))
        {
            _ = gatewayClient.AddEventStoreDaprServiceInvocation(
                "eventstore",
                configuration["DAPR_API_TOKEN"]);
        }

        services.TryAddSingleton<IWorkCommandSubmitter, EventStoreGatewayWorkCommandSubmitter>();

        // Date-reminder reconciliation-on-recovery.
        services.TryAddSingleton<IPendingDateAwaitSource, StreamReadingPendingDateAwaitSource>();
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

    /// <summary>Registers the Dapr date-resume reminder actor on the Works host's actor runtime.</summary>
    public static IServiceCollection AddWorksDateReminderActors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddActors(options => options.Actors.RegisterActor<DateReminderActor>());
        return services;
    }
}
