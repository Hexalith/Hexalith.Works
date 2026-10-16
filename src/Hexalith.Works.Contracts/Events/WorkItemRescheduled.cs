using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

[PolymorphicSerialization]
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed partial record WorkItemRescheduled(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemSchedule Schedule,
    string? Note = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkItemExpirySchedule? Expiry = null) : IEventPayload
{
    /// <summary>Preserves the original compiled constructor contract; expiry is additive.</summary>
    public WorkItemRescheduled(
        string AggregateId,
        long Sequence,
        TenantId TenantId,
        WorkItemId WorkItemId,
        WorkItemSchedule Schedule,
        string? Note)
        : this(AggregateId, Sequence, TenantId, WorkItemId, Schedule, Note, null)
    {
    }

    /// <summary>Preserves deconstruction of the original durable contract.</summary>
    public void Deconstruct(
        out string AggregateId,
        out long Sequence,
        out TenantId TenantId,
        out WorkItemId WorkItemId,
        out WorkItemSchedule Schedule,
        out string? Note)
    {
        AggregateId = this.AggregateId;
        Sequence = this.Sequence;
        TenantId = this.TenantId;
        WorkItemId = this.WorkItemId;
        Schedule = this.Schedule;
        Note = this.Note;
    }
}
