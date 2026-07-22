namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Durable discovery seam for incomplete cascade checkpoints in a state store without key enumeration.
/// </summary>
public interface ICascadeCheckpointIndex
{
    /// <summary>Reads the current incomplete checkpoint entries.</summary>
    Task<IReadOnlyList<CascadeCheckpointIndexEntry>> GetIncompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes a completed or otherwise reconciled identity idempotently.</summary>
    Task RemoveIncompleteAsync(CascadeCheckpointIdentity identity, CancellationToken cancellationToken = default);
}
