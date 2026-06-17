namespace Hexalith.Works.Reminders;

/// <summary>
/// Host-edge seam that registers a deterministic, one-shot Dapr actor reminder for a still-future
/// <c>DateReached</c> await (Story 4.6, AC #1/#2). Registration is idempotent: the same await always maps to
/// the same <see cref="DateReminderName"/>, so re-registering during reconciliation cannot create a second
/// firing. The production implementation drives a Dapr actor; the deterministic test lane supplies a fake.
/// </summary>
public interface IDateReminderScheduler
{
    /// <summary>
    /// Registers (or idempotently re-registers) the deterministic reminder that fires <paramref name="dueTime"/>
    /// from now for the supplied <paramref name="await"/>.
    /// </summary>
    Task ScheduleResumeReminderAsync(PendingDateAwait @await, TimeSpan dueTime, CancellationToken cancellationToken = default);
}
