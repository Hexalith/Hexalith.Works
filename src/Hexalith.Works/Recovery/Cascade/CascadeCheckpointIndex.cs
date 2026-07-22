namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// Singleton read model containing the identities of currently incomplete cascade checkpoints.
/// </summary>
public sealed class CascadeCheckpointIndex
{
    /// <summary>Gets the unique incomplete checkpoint identities.</summary>
    public IReadOnlyList<CascadeCheckpointIdentity> Entries { get; init; } = [];
}
