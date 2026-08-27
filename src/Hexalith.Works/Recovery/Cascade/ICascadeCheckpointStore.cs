namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Re-readable persistence seam for cascade checkpoints (Story 4.6, AC #4/#5). The production implementation
/// stores checkpoints in the Dapr state store via <c>IReadModelStore</c>; the deterministic test lane
/// supplies an in-memory double so the at-least-once dispatch and restart-replay logic is provable without a
/// running store. The key is derived from <c>(tenant, parent, parent-terminal-event-type)</c> so each
/// parent-terminal cascade has exactly one durable checkpoint.
/// </summary>
public interface ICascadeCheckpointStore
{
    /// <summary>Reads the checkpoint for a parent-terminal cascade, or <see langword="null"/> when none exists yet.</summary>
    Task<CascadeCheckpoint?> GetAsync(string tenantId, string parentWorkItemId, string parentTerminalEventType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists (creates or overwrites) the cascade checkpoint. Completion is monotonic: once a checkpoint is
    /// durably completed it cannot be overwritten by an incomplete one for the same identity.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The durable checkpoint for this identity is already completed and <paramref name="checkpoint"/> is not.
    /// </exception>
    Task SaveAsync(CascadeCheckpoint checkpoint, CancellationToken cancellationToken = default);
}
