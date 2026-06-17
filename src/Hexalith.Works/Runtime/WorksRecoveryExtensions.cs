using Hexalith.EventStore.Client.Registration;
using Hexalith.Works.Recovery.Cascade;
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
        _ = services.AddEventStoreGatewayClient(options =>
        {
            string? baseAddress = configuration["EventStore:CommandGateway:BaseAddress"];
            options.BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseAddress) ? "http://eventstore" : baseAddress);
        });
        services.TryAddSingleton<IWorkCommandSubmitter, EventStoreGatewayWorkCommandSubmitter>();

        // Date-reminder reconciliation-on-recovery.
        services.TryAddSingleton<IPendingDateAwaitSource, StreamReadingPendingDateAwaitSource>();
        services.TryAddSingleton<IDateReminderScheduler, DaprDateReminderScheduler>();
        services.TryAddSingleton<DateReminderReconciler>();
        _ = services.AddHostedService<ReminderReconciliationService>();

        // Terminal-cascade dispatch, checkpoints, and replay.
        services.TryAddSingleton<ICascadeCheckpointStore, ReadModelCascadeCheckpointStore>();
        services.TryAddSingleton<ICascadeDescendantSource, StreamReadingCascadeDescendantSource>();
        services.TryAddSingleton<CascadeDispatcher>();

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
