namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record AwaitCondition
{
    public AwaitCondition(WorkItemId childWorkItemId)
    {
        ArgumentNullException.ThrowIfNull(childWorkItemId);
        ChildWorkItemId = childWorkItemId;
    }

    public WorkItemId ChildWorkItemId { get; }
}
