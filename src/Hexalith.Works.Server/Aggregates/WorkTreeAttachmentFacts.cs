using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Server.Aggregates;

public sealed record WorkTreeAttachmentFacts(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ParentWorkItemReference? ProposedParent,
    ParentWorkItemReference? CurrentParent,
    IReadOnlyList<ParentWorkItemReference> ProposedParentAncestors,
    int ProposedParentDepth = 1,
    int MaxDepth = WorkTreeDepthPolicy.DefaultMaxDepth);
