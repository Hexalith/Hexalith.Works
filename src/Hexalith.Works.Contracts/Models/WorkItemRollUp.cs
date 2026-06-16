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
    long LatestAcceptedSourceSequence);
