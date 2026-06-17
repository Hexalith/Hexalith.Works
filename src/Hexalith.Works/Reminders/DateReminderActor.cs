using Dapr.Actors.Runtime;

using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Host-edge Dapr actor that registers and fires date-based resume reminders (Story 4.6, AC #1/#2/#3). It is
/// a thin shell: it owns no domain decision. On <see cref="ScheduleResumeAsync"/> it persists the bounded
/// registration keyed by the deterministic reminder name and registers a one-shot Dapr actor reminder; when
/// the reminder fires it rebuilds the deterministic <c>ResumeWorkItem</c> through the pure
/// <see cref="DateResume"/> factory and submits it via <see cref="IWorkCommandSubmitter"/>, so the aggregate
/// — never the actor — decides acceptance and a redelivered firing no-ops after the first resume.
/// </summary>
public sealed class DateReminderActor : Actor, IDateReminderActor, IRemindable
{
    // Dapr reminders fire once when period is negative; date awaits are one-shot deadlines.
    private static readonly TimeSpan OneShot = TimeSpan.FromMilliseconds(-1);

    private readonly IWorkCommandSubmitter _submitter;
    private readonly ILogger<DateReminderActor> _logger;

    /// <summary>Initializes a new instance of the <see cref="DateReminderActor"/> class.</summary>
    public DateReminderActor(ActorHost host, IWorkCommandSubmitter submitter, ILogger<DateReminderActor> logger)
        : base(host)
    {
        _submitter = submitter ?? throw new ArgumentNullException(nameof(submitter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task ScheduleResumeAsync(DateReminderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        string reminderName = DateReminderName.For(registration.TenantId, registration.WorkItemId, registration.CorrelationKey);
        TimeSpan dueTime = registration.DueTimeMilliseconds <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(registration.DueTimeMilliseconds);

        // Persist the target before registering so a fire always finds its registration; re-registering the
        // same deterministic name overwrites in place and cannot create a second firing (idempotent).
        await StateManager.SetStateAsync(reminderName, registration).ConfigureAwait(false);
        _ = await RegisterReminderAsync(reminderName, state: null, dueTime: dueTime, period: OneShot).ConfigureAwait(false);

        WorksRecoveryLog.DateReminderScheduled(_logger, registration.TenantId, registration.WorkItemId, reminderName);
    }

    /// <inheritdoc/>
    public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        ConditionalValue<DateReminderRegistration> stored = await StateManager
            .TryGetStateAsync<DateReminderRegistration>(reminderName)
            .ConfigureAwait(false);

        if (!stored.HasValue)
        {
            // Orphaned reminder (already resumed/cleared): drop it best-effort and stop.
            await TryUnregisterAsync(reminderName).ConfigureAwait(false);
            return;
        }

        DateReminderRegistration registration = stored.Value;
        WorkCommandSubmission submission = DateResume.BuildSubmission(registration.TenantId, registration.WorkItemId, registration.Instant);
        await _submitter.SubmitAsync(submission).ConfigureAwait(false);

        WorksRecoveryLog.DateResumeIssued(_logger, registration.TenantId, registration.WorkItemId, reminderName, submission.CorrelationId);

        // The resume is recorded; clear the one-shot reminder and its state. A redelivery before this commit
        // re-issues the same idempotent resume, which the aggregate no-ops after the first WorkItemResumed.
        _ = await StateManager.TryRemoveStateAsync(reminderName).ConfigureAwait(false);
        await TryUnregisterAsync(reminderName).ConfigureAwait(false);
    }

    private async Task TryUnregisterAsync(string reminderName)
    {
        try
        {
            await UnregisterReminderAsync(reminderName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: a failed unregister is safe because the resume is idempotent.
            WorksRecoveryLog.RecoveryStepFailed(_logger, "unregister-date-reminder", ex);
        }
    }
}
