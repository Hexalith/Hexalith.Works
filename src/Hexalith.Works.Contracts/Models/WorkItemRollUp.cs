using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Models;

/// <summary>Represents the accepted state-changing roll-up projection for one work item.</summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="WorkItemId">The projected work item.</param>
/// <param name="Status">The projected lifecycle status.</param>
/// <param name="Parent">The projected parent reference, when present.</param>
/// <param name="OwnRemaining">The work item's own remaining effort.</param>
/// <param name="RolledRemaining">
/// The single-unit subtree remainder when locally available; <see langword="null"/> also represents a
/// runtime-unavailable child-dependent total that a per-aggregate replay cannot reconcile.
/// </param>
/// <param name="RolledRemainingByUnit">
/// The subtree remainder grouped by unit, or an empty list when no numeric contribution exists or the runtime
/// cannot reconcile child-dependent totals from separate aggregate replays.
/// </param>
/// <param name="ChildWorkItemIds">
/// The accepted child identities, emitted in ordinal <see cref="WorkItemId.Value"/> order so that permuted
/// or duplicated deliveries of the same child facts produce an identical public sequence.
/// </param>
/// <param name="LatestAcceptedSourceSequence">
/// The EventStore envelope position of the latest accepted state-changing delivery. Filtered rejection
/// positions do not advance it, so it is not the full persisted stream high-watermark. A spawn-derived
/// child that has no <c>WorkItemCreated</c> delivery of its own carries a synthetic floor of <c>1</c>
/// until a real delivery raises it.
/// </param>
public sealed record WorkItemRollUp(
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemStatus Status,
    ParentWorkItemReference? Parent,
    OwnRemaining? OwnRemaining,
    RolledRemaining? RolledRemaining,
    IReadOnlyList<RolledRemaining> RolledRemainingByUnit,
    IReadOnlyList<WorkItemId> ChildWorkItemIds,
    long LatestAcceptedSourceSequence)
{
    /// <summary>
    /// Gets the number of child identities derived from <see cref="ChildWorkItemIds"/>, treating a missing or
    /// <see langword="null"/> deserialized list as empty.
    /// </summary>
    public int ExposedChildCount => ChildWorkItemIds?.Count ?? 0;

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
