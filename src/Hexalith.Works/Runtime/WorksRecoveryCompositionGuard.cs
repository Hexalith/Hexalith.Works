using Dapr.Actors.Runtime;

using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Reminders;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Runtime;

/// <summary>Checks resolved final hosted services before the generic host starts any writer.</summary>
internal sealed class WorksRecoveryCompositionGuard(IServiceProvider services) : IHostedLifecycleService
{
    /// <inheritdoc/>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<IOptions<ActorRuntimeOptions>>().Value.Actors
            .Any(actor => actor.Type.ImplementationType == typeof(DateReminderActor)))
            throw new InvalidOperationException("SDK recovery requires exclusive final actor writer composition.");
        if (services.GetServices<IHostedService>().Any(service => service is ReminderReconciliationService or CascadeRecoveryService))
            throw new InvalidOperationException("SDK recovery requires exclusive final hosted writer composition.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
