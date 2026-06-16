namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record ParentWorkItemReference
{
    public ParentWorkItemReference(WorkItemId workItemId)
    {
        ArgumentNullException.ThrowIfNull(workItemId);
        WorkItemId = workItemId;
    }

    public WorkItemId WorkItemId { get; }
}
