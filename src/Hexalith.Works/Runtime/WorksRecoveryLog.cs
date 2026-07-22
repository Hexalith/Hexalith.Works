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

    public static void DateReminderScheduled(ILogger logger, string tenantId, string workItemId, string reminderName)
        => s_reminderScheduled(logger, reminderName, workItemId, tenantId, null);

    public static void DateResumeIssued(ILogger logger, string tenantId, string workItemId, string reminderName, string correlationId)
        => s_resumeIssued(logger, workItemId, tenantId, reminderName, correlationId, null);

    public static void DateRemindersReconciled(ILogger logger, string tenantId, int dueCount, int scheduledCount)
        => s_reconciled(logger, tenantId, dueCount, scheduledCount, null);

    public static void RecoveryStepFailed(ILogger logger, string reason, Exception? exception = null)
        => s_recoveryFailed(logger, reason, exception);

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
}
