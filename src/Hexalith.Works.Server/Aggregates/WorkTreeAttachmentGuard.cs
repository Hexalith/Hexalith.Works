using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Server.Aggregates;

public static class WorkTreeAttachmentGuard
{
    public static WorkTreeAttachmentValidationResult Validate(WorkTreeAttachmentFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.TenantId);
        ArgumentNullException.ThrowIfNull(facts.WorkItemId);
        ArgumentNullException.ThrowIfNull(facts.ProposedParentAncestors);

        if (facts.ProposedParent is null)
        {
            return WorkTreeAttachmentValidationResult.Accepted(resultingDepth: 1);
        }

        ParentWorkItemReference proposedParent = facts.ProposedParent;
        int resultingDepth = facts.ProposedParentDepth + 1;

        if (proposedParent.TenantId != facts.TenantId)
        {
            return WorkTreeAttachmentValidationResult.Rejected(
                new WorkItemCannotReferenceParentFromAnotherTenant(facts.TenantId, facts.WorkItemId, proposedParent),
                resultingDepth);
        }

        ParentWorkItemReference? foreignAncestor = facts.ProposedParentAncestors
            .FirstOrDefault(ancestor => ancestor.TenantId != facts.TenantId);
        if (foreignAncestor is not null)
        {
            return WorkTreeAttachmentValidationResult.Rejected(
                new WorkItemCannotReferenceParentFromAnotherTenant(facts.TenantId, facts.WorkItemId, foreignAncestor),
                resultingDepth);
        }

        if (facts.CurrentParent is not null && facts.CurrentParent != proposedParent)
        {
            return WorkTreeAttachmentValidationResult.Rejected(
                new WorkItemCannotReferenceSecondParent(facts.TenantId, facts.WorkItemId, facts.CurrentParent, proposedParent),
                resultingDepth);
        }

        if (proposedParent.WorkItemId == facts.WorkItemId)
        {
            return WorkTreeAttachmentValidationResult.Rejected(
                new WorkItemTreeCycleRejected(facts.TenantId, facts.WorkItemId, proposedParent, facts.WorkItemId),
                resultingDepth);
        }

        ParentWorkItemReference? cycleAncestor = facts.ProposedParentAncestors
            .FirstOrDefault(ancestor => ancestor.WorkItemId == facts.WorkItemId);
        if (cycleAncestor is not null)
        {
            return WorkTreeAttachmentValidationResult.Rejected(
                new WorkItemTreeCycleRejected(facts.TenantId, facts.WorkItemId, proposedParent, cycleAncestor.WorkItemId),
                resultingDepth);
        }

        if (resultingDepth > facts.MaxDepth)
        {
            return WorkTreeAttachmentValidationResult.Rejected(
                new WorkItemTreeDepthExceeded(facts.TenantId, facts.WorkItemId, proposedParent, facts.MaxDepth, resultingDepth),
                resultingDepth);
        }

        return WorkTreeAttachmentValidationResult.Accepted(resultingDepth);
    }
}
