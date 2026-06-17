namespace Hexalith.Works.Reminders;

/// <summary>
/// Re-readable source of every still-pending <c>DateReached</c> await across the Works streams, used by
/// reminder reconciliation-on-recovery (Story 4.6, AC #3). The production implementation reads the
/// persisted <c>work</c> event streams and applies the pure <see cref="PendingDateAwaitProjection"/>; the
/// deterministic test lane supplies a fake so the reconciliation decision logic is provable without Dapr,
/// a gateway, or a clock race.
/// </summary>
public interface IPendingDateAwaitSource
{
    /// <summary>Returns the currently-pending date awaits to reconcile.</summary>
    Task<IReadOnlyList<PendingDateAwait>> GetPendingDateAwaitsAsync(CancellationToken cancellationToken = default);
}
