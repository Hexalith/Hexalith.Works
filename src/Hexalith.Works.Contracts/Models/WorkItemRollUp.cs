using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Models;

public sealed record WorkItemRollUp(
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemStatus Status,
    ParentWorkItemReference? Parent,
    OwnRemaining? OwnRemaining,
    RolledRemaining? RolledRemaining,
    IReadOnlyList<RolledRemaining> RolledRemainingByUnit,
    IReadOnlyList<WorkItemId> ChildWorkItemIds,
    int ChildContributionCount,
    long LatestAcceptedSourceSequence)
{
    public bool Degraded { get; init; }

    public IReadOnlyList<RollUpProjectionDiagnostic> ProjectionDiagnostics { get; init; } = [];

    /// <summary>
    /// Gets the work item's own planned effort — estimated, done, derived remaining, and unit — when an estimate
    /// has been established, or <see langword="null"/> when the item carries no own effort. Exposed additively so
    /// external consumers (for example planned-vs-actual reporting) can read the planned <c>Estimated</c> figure,
    /// which <see cref="OwnRemaining"/> alone does not convey.
    /// </summary>
    public WorkItemEffort? OwnEffort { get; init; }
}
