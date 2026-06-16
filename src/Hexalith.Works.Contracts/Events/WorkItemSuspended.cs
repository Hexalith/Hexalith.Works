using System.Text.Json.Serialization;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

[PolymorphicSerialization]
public sealed partial record WorkItemSuspended : IEventPayload
{
    [JsonConstructor]
    public WorkItemSuspended(
        string aggregateId,
        long sequence,
        TenantId tenantId,
        WorkItemId workItemId,
        IReadOnlyList<AwaitCondition>? awaitConditions = null,
        AwaitCondition? awaitCondition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(workItemId);

        AggregateId = aggregateId;
        Sequence = sequence;
        TenantId = tenantId;
        WorkItemId = workItemId;
        AwaitConditions = Normalize(awaitConditions, awaitCondition);
        AwaitCondition = awaitCondition;
    }

    public string AggregateId { get; }

    public long Sequence { get; }

    public TenantId TenantId { get; }

    public WorkItemId WorkItemId { get; }

    public IReadOnlyList<AwaitCondition> AwaitConditions { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AwaitCondition? AwaitCondition { get; }

    public bool Equals(WorkItemSuspended? other)
        => other is not null
            && StringComparer.Ordinal.Equals(AggregateId, other.AggregateId)
            && Sequence == other.Sequence
            && TenantId == other.TenantId
            && WorkItemId == other.WorkItemId
            && AwaitConditions.SequenceEqual(other.AwaitConditions)
            && AwaitCondition == other.AwaitCondition;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AggregateId, StringComparer.Ordinal);
        hash.Add(Sequence);
        hash.Add(TenantId);
        hash.Add(WorkItemId);
        foreach (AwaitCondition condition in AwaitConditions)
        {
            hash.Add(condition);
        }

        hash.Add(AwaitCondition);
        return hash.ToHashCode();
    }

    private static IReadOnlyList<AwaitCondition> Normalize(
        IReadOnlyList<AwaitCondition>? awaitConditions,
        AwaitCondition? awaitCondition)
    {
        AwaitCondition[] conditions = awaitConditions is { Count: > 0 }
            ? [.. awaitConditions]
            : awaitCondition is null ? [] : [awaitCondition];

        if (conditions.Any(static condition => condition is null))
        {
            throw new ArgumentException("Await condition sets cannot contain null entries.", nameof(awaitConditions));
        }

        return conditions;
    }
}
