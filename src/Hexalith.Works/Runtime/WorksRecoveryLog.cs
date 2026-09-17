using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Runtime;

/// <summary>
/// Compile-time <see cref="LoggerMessage"/> definitions for the Story 4.6 reminder and cascade recovery
/// runtime. Every template carries only bounded metadata — tenant id, work item id, reminder name, a
/// correlation id, counts, and reason/kind codes — never an obligation, command/event payload, token,
/// secret, or full JSON body (AC #1/#4, NFR-6). Placeholder names deliberately avoid command, payload,
/// obligation, body, and JSON vocabulary because architecture tests scan for those leak-prone names.
/// </summary>
internal static class WorksRecoveryLog
{
    private static readonly Action<ILogger, string, string, string, Exception?> s_reminderScheduled =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(4600, "DateReminderScheduled"),
            "Scheduled date-resume reminder {ReminderName} for work item {WorkItemId} in tenant {TenantId}.");

    private static readonly Action<ILogger, string, string, string, string, Exception?> s_resumeIssued =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Information,
            new EventId(4601, "DateResumeIssued"),
            "Issued date-resume for work item {WorkItemId} in tenant {TenantId} (reminder {ReminderName}, correlation {CorrelationId}).");

    private static readonly Action<ILogger, string, int, int, Exception?> s_reconciled =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Information,
            new EventId(4602, "DateReminderReconciled"),
            "Reconciled pending date awaits for tenant {TenantId}: {DueCount} reissued, {ScheduledCount} rescheduled.");

    private static readonly Action<ILogger, string, Exception?> s_recoveryFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4603, "RecoveryStepFailed"),
            "Recovery step did not complete; reason {Reason}. It will be retried at-least-once and remains idempotent.");

    private static readonly Action<ILogger, string, Exception?> s_pendingDateAwaitTenantScanFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4604, "PendingDateAwaitTenantScanFailed"),
            "Pending date-await scan failed for tenant {TenantId}; the overall pass is signalled incomplete for retry, and later tenants remain eligible unless shutdown stops the scan.");

    private static readonly Action<ILogger, int, int, int, Exception?> s_pendingDateAwaitScanIncomplete =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Warning,
            new EventId(4605, "PendingDateAwaitScanIncomplete"),
            "Pending date-await scan was incomplete for {FailedTenantCount} tenant(s) and {FailedCandidateCount} candidate aggregate(s), with {SkippedParkedCount} parked candidate(s) skipped; reconciliation still acts on the partial results, and the incomplete scan remains eligible for retry under the configured recovery policy.");

    private static readonly Action<ILogger, string, string, Exception?> s_pendingDateAwaitCandidateScanFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4606, "PendingDateAwaitCandidateScanFailed"),
            "Pending date-await candidate stream {WorkItemId} in tenant {TenantId} could not be read; the overall pass is signalled incomplete for retry, and later candidates remain eligible unless shutdown stops the scan.");

    private static readonly Action<ILogger, string, string, Exception?> s_pendingDateAwaitParkedCandidateSkipped =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4607, "PendingDateAwaitParkedCandidateSkipped"),
            "Pending date-await candidate {WorkItemId} in tenant {TenantId} is parked; it is skipped without a stream read and does not mark the scan incomplete.");

    private static readonly Action<ILogger, string, string, string, Exception?> s_pendingDateAwaitParkingLookupFailed =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(4608, "PendingDateAwaitParkingLookupFailed"),
            "Pending date-await parking lookup failed for candidate {WorkItemId} in tenant {TenantId}; reason {Reason}. The stream is not read and the overall pass is signalled incomplete for retry.");

    private static readonly Action<ILogger, string, string, string, string, Exception?> s_dateReminderSchedulingFailed =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Warning,
            new EventId(4609, "DateReminderSchedulingFailed"),
            "Date-resume reminder {ReminderName} could not be scheduled for work item {WorkItemId} in tenant {TenantId}; reason {Reason}. The delivery remains retryable.");

    private static readonly Action<ILogger, string, string, int, Exception?> s_cascadeCheckpointed =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information,
            new EventId(4700, "CascadeCheckpointed"),
            "Persisted cascade checkpoint for parent {ParentWorkItemId} in tenant {TenantId} with {TargetCount} descendant targets.");

    private static readonly Action<ILogger, string, string, string, string, Exception?> s_cascadeTargetDispatched =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Information,
            new EventId(4701, "CascadeTargetDispatched"),
            "Dispatched {Kind} to descendant {DescendantId} for parent {ParentWorkItemId} in tenant {TenantId}.");

    private static readonly Action<ILogger, string, string, int, Exception?> s_cascadeReplayResumed =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information,
            new EventId(4702, "CascadeReplayResumed"),
            "Replayed cascade checkpoint for parent {ParentWorkItemId} in tenant {TenantId}; {OutstandingCount} outstanding descendants remain.");

    private static readonly Action<ILogger, int, int, Exception?> s_cascadeTargetIntervalClamped =
        LoggerMessage.Define<int, int>(
            LogLevel.Warning,
            new EventId(4703, "CascadeTargetIntervalClamped"),
            "Configured CascadeTargetIntervalMilliseconds {ConfiguredMilliseconds} is out of the supported range; clamped to {ClampedMilliseconds}.");

    private static readonly Action<ILogger, string, string, Exception?> s_cascadeIndexEntryPruned =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4704, "CascadeIndexEntryPruned"),
            "Pruned a stale incomplete-cascade-checkpoint index entry for parent {ParentWorkItemId} in tenant {TenantId}; no checkpoint was ever written for it.");

    private static readonly Action<ILogger, string, string, Exception?> s_cascadeIndexEntryStranded =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4705, "CascadeIndexEntryStranded"),
            "Removed a stranded incomplete-cascade-checkpoint index entry for parent {ParentWorkItemId} in tenant {TenantId}; its checkpoint is already durably completed, so no incomplete work was lost.");

    public static void DateReminderScheduled(ILogger logger, string tenantId, string workItemId, string reminderName)
        => s_reminderScheduled(logger, reminderName, workItemId, tenantId, null);

    public static void DateResumeIssued(ILogger logger, string tenantId, string workItemId, string reminderName, string correlationId)
        => s_resumeIssued(logger, workItemId, tenantId, reminderName, correlationId, null);

    public static void DateRemindersReconciled(ILogger logger, string tenantId, int dueCount, int scheduledCount)
        => s_reconciled(logger, tenantId, dueCount, scheduledCount, null);

    public static void RecoveryStepFailed(ILogger logger, string reason, Exception? exception = null)
        => s_recoveryFailed(logger, reason, exception);

    public static void PendingDateAwaitTenantScanFailed(ILogger logger, string tenantId, Exception exception)
        => s_pendingDateAwaitTenantScanFailed(logger, tenantId, exception);

    /// <summary>Logs an incomplete pending-date scan that carries partial evidence and remains retry-eligible.</summary>
    /// <remarks>
    /// Counts and identity fields remain bounded. The structured exception slot carries the incomplete-scan
    /// wrapper, whose exception chain retains the final recorded dependency cause; neither is interpolated into
    /// the message template.
    /// </remarks>
    /// <param name="logger">The recovery logger.</param>
    /// <param name="failedTenantCount">The number of tenant-index reads that failed.</param>
    /// <param name="failedCandidateCount">The number of parking or candidate-stream reads that failed.</param>
    /// <param name="skippedParkedCount">The number of terminally parked candidates skipped cleanly.</param>
    /// <param name="exception">The incomplete-scan wrapper whose exception chain carries the final recorded dependency cause.</param>
    public static void PendingDateAwaitScanIncomplete(
        ILogger logger,
        int failedTenantCount,
        int failedCandidateCount,
        int skippedParkedCount,
        Exception? exception)
        => s_pendingDateAwaitScanIncomplete(logger, failedTenantCount, failedCandidateCount, skippedParkedCount, exception);

    public static void PendingDateAwaitCandidateScanFailed(ILogger logger, string tenantId, string workItemId, Exception exception)
        => s_pendingDateAwaitCandidateScanFailed(logger, workItemId, tenantId, exception);

    /// <summary>Logs that a parked pending-date-await candidate was skipped without a stream read.</summary>
    /// <param name="logger">The recovery logger.</param>
    /// <param name="tenantId">The tenant of the parked candidate.</param>
    /// <param name="workItemId">The parked work item.</param>
    public static void PendingDateAwaitParkedCandidateSkipped(ILogger logger, string tenantId, string workItemId)
        => s_pendingDateAwaitParkedCandidateSkipped(logger, workItemId, tenantId, null);

    /// <summary>Logs that the parking document could not be read.</summary>
    /// <remarks>
    /// The <c>Reason</c> placeholder carries the exception type name only — a bounded discriminator that never
    /// contains payload. The exception itself rides in the structured exception slot, never in the template.
    /// </remarks>
    /// <param name="logger">The recovery logger.</param>
    /// <param name="tenantId">The candidate tenant.</param>
    /// <param name="workItemId">The candidate work item.</param>
    /// <param name="exception">The caught parking-store failure.</param>
    public static void PendingDateAwaitParkingLookupFailed(ILogger logger, string tenantId, string workItemId, Exception exception)
        => s_pendingDateAwaitParkingLookupFailed(logger, workItemId, tenantId, exception.GetType().Name, exception);

    /// <summary>Logs a retryable reminder-scheduling failure.</summary>
    /// <remarks>
    /// The <c>Reason</c> placeholder carries the exception type name only — a bounded discriminator that never
    /// contains payload. The exception itself rides in the structured exception slot, never in the template.
    /// </remarks>
    /// <param name="logger">The recovery logger.</param>
    /// <param name="tenantId">The reminder tenant.</param>
    /// <param name="workItemId">The reminder work item.</param>
    /// <param name="reminderName">The deterministic reminder name.</param>
    /// <param name="exception">The caught scheduler failure.</param>
    public static void DateReminderSchedulingFailed(
        ILogger logger,
        string tenantId,
        string workItemId,
        string reminderName,
        Exception exception)
        => s_dateReminderSchedulingFailed(logger, reminderName, workItemId, tenantId, exception.GetType().Name, exception);

    public static void CascadeCheckpointed(ILogger logger, string tenantId, string parentWorkItemId, int targetCount)
        => s_cascadeCheckpointed(logger, parentWorkItemId, tenantId, targetCount, null);

    public static void CascadeTargetDispatched(ILogger logger, string tenantId, string parentWorkItemId, string descendantId, string kind)
        => s_cascadeTargetDispatched(logger, kind, descendantId, parentWorkItemId, tenantId, null);

    public static void CascadeReplayResumed(ILogger logger, string tenantId, string parentWorkItemId, int outstandingCount)
        => s_cascadeReplayResumed(logger, parentWorkItemId, tenantId, outstandingCount, null);

    public static void CascadeTargetIntervalClamped(ILogger logger, int configuredMilliseconds, int clampedMilliseconds)
        => s_cascadeTargetIntervalClamped(logger, configuredMilliseconds, clampedMilliseconds, null);

    public static void CascadeIndexEntryPruned(ILogger logger, string tenantId, string parentWorkItemId)
        => s_cascadeIndexEntryPruned(logger, parentWorkItemId, tenantId, null);

    public static void CascadeIndexEntryStranded(ILogger logger, string tenantId, string parentWorkItemId)
        => s_cascadeIndexEntryStranded(logger, parentWorkItemId, tenantId, null);
}
