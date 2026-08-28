using Hexalith.Works.Reminders;

namespace Hexalith.Works.IntegrationTests;

/// <summary>A recording <see cref="IDateReminderScheduler"/> for Story 4.8 deterministic tests.</summary>
internal sealed class Story48RecordingScheduler : IDateReminderScheduler
{
    public List<(PendingDateAwait Await, TimeSpan DueTime)> Calls { get; } = [];

    public Task ScheduleResumeReminderAsync(PendingDateAwait pendingAwait, TimeSpan dueTime, CancellationToken cancellationToken = default)
    {
        Calls.Add((pendingAwait, dueTime));
        return Task.CompletedTask;
    }
}
