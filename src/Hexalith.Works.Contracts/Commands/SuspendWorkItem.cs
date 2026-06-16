using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Suspends in-flight work (<c>InProgress</c> → <c>Suspended</c>) until one of the supplied await
/// conditions is later presented by <see cref="ResumeWorkItem"/>.
/// </summary>
[PolymorphicSerialization]
public sealed partial record SuspendWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    IReadOnlyList<AwaitCondition>? AwaitConditions = null)
{
    public bool Equals(SuspendWorkItem? other)
        => other is not null
            && TenantId == other.TenantId
            && WorkItemId == other.WorkItemId
            && Normalize(AwaitConditions).SequenceEqual(Normalize(other.AwaitConditions));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TenantId);
        hash.Add(WorkItemId);
        foreach (AwaitCondition condition in Normalize(AwaitConditions))
        {
            hash.Add(condition);
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyList<AwaitCondition> Normalize(IReadOnlyList<AwaitCondition>? conditions)
        => conditions ?? [];
}
