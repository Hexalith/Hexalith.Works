namespace Hexalith.Works.Projections;

/// <summary>The complete queue and date-await evidence in a single selected SDK generation.</summary>
/// <param name="Queue">The queue items and retained work item membership and watermarks.</param>
/// <param name="PendingDate">The pending date awaits and their retained cleared-await watermarks.</param>
/// <param name="IsSharded">Whether the selected generation uses the sharded representation.</param>
internal sealed record WorksRuntimeIndexes(
    WorksWhatsNextTenantIndex Queue,
    PendingDateAwaitTenantIndex PendingDate,
    bool IsSharded);
