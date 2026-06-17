using Dapr.Actors;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Actor contract for the date-resume reminder. One actor instance per work item (its id is
/// <see cref="DateReminderName.ActorId(string, string)"/>) owns every <c>DateReached</c> reminder for that
/// item. Registering through the Dapr actor runtime gives the reminder a persistent callback across actor
/// deactivations and host restarts (the Dapr Scheduler retains it), which is exactly why date awaits are an
/// actor-reminder concern rather than a non-persistent timer.
/// </summary>
public interface IDateReminderActor : IActor
{
    /// <summary>Idempotently registers the deterministic one-shot reminder described by <paramref name="registration"/>.</summary>
    Task ScheduleResumeAsync(DateReminderRegistration registration);
}
