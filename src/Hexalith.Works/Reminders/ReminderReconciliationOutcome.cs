namespace Hexalith.Works.Reminders;

/// <summary>
/// Represents the result of a reconciliation pass: due awaits reissued, future awaits rescheduled, and parked
/// candidates intentionally skipped without consuming retry budget.
/// </summary>
/// <param name="Reissued">The number of due awaits reissued as resume commands.</param>
/// <param name="Rescheduled">The number of future awaits rescheduled as reminders.</param>
/// <param name="SkippedParkedCount">The number of parked candidates skipped without a stream read.</param>
public sealed record ReminderReconciliationOutcome(
    int Reissued,
    int Rescheduled,
    int SkippedParkedCount = 0);
