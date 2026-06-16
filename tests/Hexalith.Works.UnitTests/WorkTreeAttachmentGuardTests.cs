using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Server.Aggregates;
using Shouldly;

namespace Hexalith.Works.UnitTests;

public sealed class WorkTreeAttachmentGuardTests
{
    [Fact]
    public void Validate_accepts_root_work_item_without_parent()
    {
        WorkTreeAttachmentValidationResult result = Validate(hasProposedParent: false);

        result.IsAccepted.ShouldBeTrue();
        result.Rejection.ShouldBeNull();
    }

    [Fact]
    public void Validate_accepts_first_same_tenant_parent_reference()
    {
        WorkTreeAttachmentValidationResult result = Validate();

        result.IsAccepted.ShouldBeTrue();
        result.Rejection.ShouldBeNull();
    }

    [Fact]
    public void Validate_accepts_same_parent_idempotently()
    {
        ParentWorkItemReference parent = Parent("Tenant-Alpha", "parent-001");

        WorkTreeAttachmentValidationResult result = Validate(proposedParent: parent, currentParent: parent);

        result.IsAccepted.ShouldBeTrue();
        result.Rejection.ShouldBeNull();
    }

    [Fact]
    public void Validate_accepts_same_tenant_parent_with_different_input_casing()
    {
        WorkTreeAttachmentValidationResult result = Validate(
            tenantId: new TenantId("tenant-alpha"),
            proposedParent: Parent("Tenant-Alpha", "parent-001"));

        result.IsAccepted.ShouldBeTrue();
        result.Rejection.ShouldBeNull();
    }

    [Fact]
    public void Validate_accepts_same_tenant_ancestor_with_different_input_casing()
    {
        WorkTreeAttachmentValidationResult result = Validate(ancestorChain:
        [
            Parent("Tenant-Alpha", "grandparent-001"),
        ]);

        result.IsAccepted.ShouldBeTrue();
        result.Rejection.ShouldBeNull();
    }

    [Fact]
    public void Validate_accepts_same_parent_idempotently_with_different_tenant_casing()
    {
        WorkTreeAttachmentValidationResult result = Validate(
            proposedParent: Parent("tenant-alpha", "parent-001"),
            currentParent: Parent("Tenant-Alpha", "parent-001"));

        result.IsAccepted.ShouldBeTrue();
        result.Rejection.ShouldBeNull();
    }

    [Fact]
    public void Validate_rejects_second_parent_without_success_payload()
    {
        WorkTreeAttachmentValidationResult result = Validate(currentParent: Parent("tenant-alpha", "existing-parent"));

        result.IsAccepted.ShouldBeFalse();
        WorkItemCannotReferenceSecondParent rejection = result.Rejection.ShouldBeOfType<WorkItemCannotReferenceSecondParent>();
        rejection.ShouldBeAssignableTo<IRejectionEvent>();
        rejection.ExistingParent.WorkItemId.ShouldBe(new WorkItemId("existing-parent"));
        rejection.ProposedParent.WorkItemId.ShouldBe(new WorkItemId("parent-001"));
    }

    [Fact]
    public void Validate_rejects_self_parent_as_cycle()
    {
        WorkItemId child = new("work-001");

        WorkTreeAttachmentValidationResult result = Validate(workItemId: child, proposedParent: Parent("tenant-alpha", child.Value));

        result.IsAccepted.ShouldBeFalse();
        WorkItemTreeCycleRejected rejection = result.Rejection.ShouldBeOfType<WorkItemTreeCycleRejected>();
        rejection.ShouldBeAssignableTo<IRejectionEvent>();
        rejection.CycleWorkItemId.ShouldBe(child);
    }

    [Fact]
    public void Validate_rejects_when_child_appears_in_parent_ancestor_chain()
    {
        WorkItemId child = new("work-001");

        WorkTreeAttachmentValidationResult result = Validate(ancestorChain:
        [
            Parent("tenant-alpha", "grandparent-001"),
            Parent("tenant-alpha", child.Value),
        ]);

        result.IsAccepted.ShouldBeFalse();
        WorkItemTreeCycleRejected rejection = result.Rejection.ShouldBeOfType<WorkItemTreeCycleRejected>();
        rejection.CycleWorkItemId.ShouldBe(child);
    }

    [Fact]
    public void Validate_rejects_cross_tenant_parent_facts_fail_closed()
    {
        WorkTreeAttachmentValidationResult result = Validate(proposedParent: Parent("tenant-beta", "parent-001"));

        result.IsAccepted.ShouldBeFalse();
        WorkItemCannotReferenceParentFromAnotherTenant rejection = result.Rejection.ShouldBeOfType<WorkItemCannotReferenceParentFromAnotherTenant>();
        rejection.Parent.TenantId.ShouldBe(new TenantId("tenant-beta"));
    }

    [Fact]
    public void Validate_rejects_cross_tenant_ancestor_facts_fail_closed()
    {
        WorkTreeAttachmentValidationResult result = Validate(ancestorChain:
        [
            Parent("tenant-alpha", "grandparent-001"),
            Parent("tenant-beta", "foreign-ancestor"),
        ]);

        result.IsAccepted.ShouldBeFalse();
        WorkItemCannotReferenceParentFromAnotherTenant rejection = result.Rejection.ShouldBeOfType<WorkItemCannotReferenceParentFromAnotherTenant>();
        rejection.Parent.TenantId.ShouldBe(new TenantId("tenant-beta"));
        rejection.Parent.WorkItemId.ShouldBe(new WorkItemId("foreign-ancestor"));
    }

    [Fact]
    public void Validate_accepts_resulting_depth_at_default_limit()
    {
        WorkTreeAttachmentValidationResult result = Validate(proposedParentDepth: WorkTreeDepthPolicy.DefaultMaxDepth - 1);

        result.IsAccepted.ShouldBeTrue();
        result.ResultingDepth.ShouldBe(WorkTreeDepthPolicy.DefaultMaxDepth);
    }

    [Fact]
    public void Validate_rejects_resulting_depth_over_default_limit()
    {
        WorkTreeAttachmentValidationResult result = Validate(proposedParentDepth: WorkTreeDepthPolicy.DefaultMaxDepth);

        result.IsAccepted.ShouldBeFalse();
        WorkItemTreeDepthExceeded rejection = result.Rejection.ShouldBeOfType<WorkItemTreeDepthExceeded>();
        rejection.MaxDepth.ShouldBe(WorkTreeDepthPolicy.DefaultMaxDepth);
        rejection.ResultingDepth.ShouldBe(WorkTreeDepthPolicy.DefaultMaxDepth + 1);
    }

    [Fact]
    public void Validate_accepts_resulting_depth_over_default_when_policy_override_allows_it()
    {
        WorkTreeAttachmentValidationResult result = Validate(
            proposedParentDepth: WorkTreeDepthPolicy.DefaultMaxDepth,
            maxDepth: WorkTreeDepthPolicy.DefaultMaxDepth + 1);

        result.IsAccepted.ShouldBeTrue();
        result.ResultingDepth.ShouldBe(WorkTreeDepthPolicy.DefaultMaxDepth + 1);
        result.Rejection.ShouldBeNull();
    }

    [Fact]
    public void Validate_rejects_resulting_depth_over_smaller_policy_override()
    {
        WorkTreeAttachmentValidationResult result = Validate(proposedParentDepth: 4, maxDepth: 4);

        result.IsAccepted.ShouldBeFalse();
        WorkItemTreeDepthExceeded rejection = result.Rejection.ShouldBeOfType<WorkItemTreeDepthExceeded>();
        rejection.MaxDepth.ShouldBe(4);
        rejection.ResultingDepth.ShouldBe(5);
    }

    [Fact]
    public void Validate_has_no_breadth_cap_for_different_children_sharing_same_parent()
    {
        ParentWorkItemReference parent = Parent("tenant-alpha", "parent-001");

        WorkTreeAttachmentValidationResult first = Validate(workItemId: new WorkItemId("child-001"), proposedParent: parent);
        WorkTreeAttachmentValidationResult second = Validate(workItemId: new WorkItemId("child-002"), proposedParent: parent);

        first.IsAccepted.ShouldBeTrue();
        second.IsAccepted.ShouldBeTrue();
    }

    private static WorkTreeAttachmentValidationResult Validate(
        TenantId? tenantId = null,
        WorkItemId? workItemId = null,
        ParentWorkItemReference? proposedParent = null,
        bool hasProposedParent = true,
        ParentWorkItemReference? currentParent = null,
        IReadOnlyList<ParentWorkItemReference>? ancestorChain = null,
        int proposedParentDepth = 1,
        int maxDepth = WorkTreeDepthPolicy.DefaultMaxDepth)
        => WorkTreeAttachmentGuard.Validate(new WorkTreeAttachmentFacts(
            tenantId ?? new TenantId("tenant-alpha"),
            workItemId ?? new WorkItemId("work-001"),
            hasProposedParent ? proposedParent ?? Parent("tenant-alpha", "parent-001") : null,
            currentParent,
            ancestorChain ?? [],
            proposedParentDepth,
            maxDepth));

    private static ParentWorkItemReference Parent(string tenantId, string workItemId)
        => new(new TenantId(tenantId), new WorkItemId(workItemId));
}
