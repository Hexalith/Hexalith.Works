using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Models;

/// <summary>
/// Consumer-facing read model for a single work item, returned by the <c>get-work-item</c> query. It exposes the
/// minimal facts an external module (for example <c>Hexalith.Timesheets</c>) needs to validate a Work reference
/// and read its planned-vs-actual effort, without leaking the internal roll-up's child/diagnostic shape.
/// <see cref="Found"/> is <see langword="false"/> for an unknown or not-yet-projected work item, so callers fail
/// closed instead of treating an absent read model as a valid reference.
/// </summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="WorkItemId">The work item identifier (raw inner id).</param>
/// <param name="Found">Whether a read model exists for the work item in the tenant scope.</param>
/// <param name="Status">The current lifecycle status, or <see cref="WorkItemStatus.Unknown"/> when not found.</param>
/// <param name="Estimated">The planned effort, or <see langword="null"/> when no estimate has been established.</param>
/// <param name="Done">The reported done effort, or <see langword="null"/> when no estimate has been established.</param>
/// <param name="Remaining">The derived remaining effort (<c>Estimated - Done</c>), or <see langword="null"/> when unestimated.</param>
/// <param name="Unit">The effort unit, or <see langword="null"/> when unestimated.</param>
/// <param name="Parent">The parent work item reference, when this item is a child.</param>
/// <param name="LatestAcceptedSourceSequence">The latest source sequence reflected by the read model (freshness signal).</param>
public sealed record WorkItemView(
    TenantId TenantId,
    WorkItemId WorkItemId,
    bool Found,
    WorkItemStatus Status,
    decimal? Estimated,
    decimal? Done,
    decimal? Remaining,
    Unit? Unit,
    ParentWorkItemReference? Parent,
    long LatestAcceptedSourceSequence)
{
    /// <summary>Creates the fail-closed "not found" view for a work item that has no projected read model.</summary>
    /// <param name="tenantId">The tenant the lookup was scoped to.</param>
    /// <param name="workItemId">The requested work item identifier.</param>
    /// <returns>A <see cref="WorkItemView"/> with <see cref="Found"/> set to <see langword="false"/>.</returns>
    public static WorkItemView NotFound(TenantId tenantId, WorkItemId workItemId)
        => new(tenantId, workItemId, false, WorkItemStatus.Unknown, null, null, null, null, null, 0);
}
