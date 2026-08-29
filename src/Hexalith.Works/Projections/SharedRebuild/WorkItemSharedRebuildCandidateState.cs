namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Deterministic durable candidate state for a tenant-wide Works shared rebuild.</summary>
/// <param name="Histories">The authoritative histories in EventStore inventory order.</param>
internal sealed record WorkItemSharedRebuildCandidateState(
    IReadOnlyList<WorkItemSharedRebuildAggregateHistory> Histories);
