namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record ParentWorkItemReference
{
    public ParentWorkItemReference(TenantId tenantId, WorkItemId workItemId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(workItemId);
        TenantId = tenantId;
        WorkItemId = workItemId;
    }

    public TenantId TenantId { get; }

    public WorkItemId WorkItemId { get; }
}
