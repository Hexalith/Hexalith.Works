using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Deterministic reminder reconciliation-on-recovery (Story 4.6, AC #3). On recovery it re-scans the
/// re-readable pending <c>DateReached</c> awaits and, per await, either reissues an idempotent
/// <c>ResumeWorkItem</c> (when the instant is already due at the adapter edge — a firing may have been lost
/// before it was recorded) or re-registers the deterministic reminder (when the instant is still in the
/// future). The "due now" decision is made here at the host edge from an injected <see cref="TimeProvider"/>;
/// the kernel stays clock-free. The whole pass is idempotent — re-registering a deterministic reminder and
/// reissuing a resume both converge to a single accepted <c>WorkItemResumed</c> — so a restart mid-scan can
/// safely repeat it.
/// </summary>
public sealed class DateReminderReconciler(
    IPendingDateAwaitSource source,
    IDateReminderScheduler scheduler,
    IWorkCommandSubmitter submitter,
    TimeProvider timeProvider,
    ILogger<DateReminderReconciler> logger)
{
    private readonly IPendingDateAwaitSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IDateReminderScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    private readonly IWorkCommandSubmitter _submitter = submitter ?? throw new ArgumentNullException(nameof(submitter));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<DateReminderReconciler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Runs one reconciliation pass and returns the reissued/rescheduled counts.</summary>
    public async Task<ReminderReconciliationOutcome> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PendingDateAwait> pending = await _source.GetPendingDateAwaitsAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        int reissued = 0;
        int rescheduled = 0;

        foreach (IGrouping<string, PendingDateAwait> byTenant in pending.GroupBy(static p => p.TenantId, StringComparer.Ordinal))
        {
            int tenantReissued = 0;
            int tenantRescheduled = 0;

            foreach (PendingDateAwait pendingAwait in byTenant)
            {
                if (pendingAwait.Instant <= now)
                {
                    WorkCommandSubmission submission = DateResume.BuildSubmission(
                        pendingAwait.TenantId,
                        pendingAwait.WorkItemId,
                        pendingAwait.Instant);
                    await _submitter.SubmitAsync(submission, cancellationToken).ConfigureAwait(false);

                    WorksRecoveryLog.DateResumeIssued(
                        _logger,
                        pendingAwait.TenantId,
                        pendingAwait.WorkItemId,
                        DateReminderName.For(pendingAwait.TenantId, pendingAwait.WorkItemId, pendingAwait.CorrelationKey),
                        submission.CorrelationId);
                    tenantReissued++;
                }
                else
                {
                    await _scheduler
                        .ScheduleResumeReminderAsync(pendingAwait, pendingAwait.Instant - now, cancellationToken)
                        .ConfigureAwait(false);

                    WorksRecoveryLog.DateReminderScheduled(
                        _logger,
                        pendingAwait.TenantId,
                        pendingAwait.WorkItemId,
                        DateReminderName.For(pendingAwait.TenantId, pendingAwait.WorkItemId, pendingAwait.CorrelationKey));
                    tenantRescheduled++;
                }
            }

            WorksRecoveryLog.DateRemindersReconciled(_logger, byTenant.Key, tenantReissued, tenantRescheduled);
            reissued += tenantReissued;
            rescheduled += tenantRescheduled;
        }

        return new ReminderReconciliationOutcome(reissued, rescheduled);
    }
}

/// <summary>The result of a reconciliation pass: how many due awaits were reissued and how many future
/// awaits were rescheduled as reminders.</summary>
public sealed record ReminderReconciliationOutcome(int Reissued, int Rescheduled);
