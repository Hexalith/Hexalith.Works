using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

[PolymorphicSerialization]
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed partial record WorkItemExpired(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkItemExpirySchedule? ExpiryWitness = null) : IEventPayload
{
    /// <summary>Preserves the original compiled constructor contract; expiry is additive.</summary>
    public WorkItemExpired(
        string AggregateId,
        long Sequence,
        TenantId TenantId,
        WorkItemId WorkItemId)
        : this(AggregateId, Sequence, TenantId, WorkItemId, null)
    {
    }

    /// <summary>Preserves deconstruction of the original durable contract.</summary>
    public void Deconstruct(
        out string AggregateId,
        out long Sequence,
        out TenantId TenantId,
        out WorkItemId WorkItemId)
    {
        AggregateId = this.AggregateId;
        Sequence = this.Sequence;
        TenantId = this.TenantId;
        WorkItemId = this.WorkItemId;
    }
}
