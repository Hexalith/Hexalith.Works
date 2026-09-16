namespace Hexalith.Works.Reminders;

/// <summary>
/// Represents one completed pending-date-await discovery pass, including candidates intentionally skipped
/// because their projection is terminally parked.
/// </summary>
/// <param name="Pending">The pending date awaits discovered from authoritative aggregate streams.</param>
/// <param name="SkippedParkedCount">The number of parked candidates skipped without reading their streams.</param>
public sealed record PendingDateAwaitScanResult(
    IReadOnlyList<PendingDateAwait> Pending,
    int SkippedParkedCount = 0);
