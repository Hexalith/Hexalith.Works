using Dapr.Actors;
using Dapr.Actors.Client;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Production <see cref="IDateReminderScheduler"/> that registers the deterministic date-resume reminder by
/// invoking the per-work-item <see cref="DateReminderActor"/> through the Dapr actor runtime (Story 4.6, AC
/// #1/#2). The actor id and reminder name are pure functions of the work item / await identity, so a
/// duplicate registration targets the same actor + reminder and cannot create a second firing.
/// </summary>
public sealed class DaprDateReminderScheduler(IActorProxyFactory actorProxyFactory) : IDateReminderScheduler
{
    private readonly IActorProxyFactory _actorProxyFactory = actorProxyFactory ?? throw new ArgumentNullException(nameof(actorProxyFactory));

    /// <inheritdoc/>
    public Task ScheduleResumeReminderAsync(PendingDateAwait pendingAwait, TimeSpan dueTime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pendingAwait);

        // Honor the token at the boundary: a registration is at-least-once and idempotent, so abandoning a
        // cancelled scan before the proxy call is safe (the next reconciliation re-registers). The Dapr actor
        // remoting interface methods in this codebase do not carry a CancellationToken (mirroring
        // IAggregateActor / IPartyKeyRetryActor), so the token is observed here rather than threaded further.
        cancellationToken.ThrowIfCancellationRequested();

        var actorId = new ActorId(DateReminderName.ActorId(pendingAwait.TenantId, pendingAwait.WorkItemId));
        IDateReminderActor actor = _actorProxyFactory.CreateActorProxy<IDateReminderActor>(actorId, nameof(DateReminderActor));

        double dueTimeMs = dueTime > TimeSpan.Zero ? dueTime.TotalMilliseconds : 0;
        var registration = new DateReminderRegistration(
            pendingAwait.TenantId,
            pendingAwait.WorkItemId,
            pendingAwait.Instant,
            pendingAwait.CorrelationKey,
            dueTimeMs);

        return actor.ScheduleResumeAsync(registration);
    }
}
