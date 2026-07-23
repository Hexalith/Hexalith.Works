using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

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

/// <summary>A recording <see cref="IWorkCommandSubmitter"/> for Story 4.8 deterministic tests.</summary>
internal sealed class Story48RecordingSubmitter : IWorkCommandSubmitter
{
    public List<WorkCommandSubmission> Submissions { get; } = [];

    public Task SubmitAsync(WorkCommandSubmission submission, CancellationToken cancellationToken = default)
    {
        Submissions.Add(submission);
        return Task.CompletedTask;
    }
}

/// <summary>A fixed-clock <see cref="TimeProvider"/> for Story 4.8 deterministic tests.</summary>
internal sealed class Story48FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
