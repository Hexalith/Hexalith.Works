using Hexalith.EventStore.Contracts.Projections;

namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>One complete authoritative aggregate history retained in a shared rebuild candidate.</summary>
/// <param name="AggregateId">The aggregate identity.</param>
/// <param name="Events">The complete persisted event prefix.</param>
internal sealed record WorkItemSharedRebuildAggregateHistory(
    string AggregateId,
    ProjectionEventDto[] Events);
