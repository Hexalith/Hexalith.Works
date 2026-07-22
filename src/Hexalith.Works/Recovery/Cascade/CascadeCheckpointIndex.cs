namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Singleton read model containing the identities of currently incomplete cascade checkpoints.
/// </summary>
public sealed class CascadeCheckpointIndex
{
    /// <summary>Gets the unique incomplete checkpoint entries.</summary>
    public IReadOnlyList<CascadeCheckpointIndexEntry> Entries { get; init; } = [];
}
