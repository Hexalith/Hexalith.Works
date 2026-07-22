namespace Hexalith.Works.Recovery.Cascade;

/// <summary>
/// One incomplete-checkpoint index entry paired with when it was last (re)added, so a checkpoint that never
/// got written after a crash (a harmless dangling identity, per <see cref="ReadModelCascadeCheckpointStore"/>)
/// can eventually be pruned instead of being retried and no-oping forever.
/// </summary>
public sealed record CascadeCheckpointIndexEntry(CascadeCheckpointIdentity Identity, DateTimeOffset AddedAt);
