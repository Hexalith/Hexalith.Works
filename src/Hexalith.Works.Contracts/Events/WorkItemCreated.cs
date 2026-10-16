using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events;

[PolymorphicSerialization]
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed partial record WorkItemCreated(
    string AggregateId,
    long Sequence,
    TenantId TenantId,
    WorkItemId WorkItemId,
    Obligation Obligation,
    WorkItemEffort? InitialEffort = null,
    WorkItemSchedule? Schedule = null,
    ParentWorkItemReference? Parent = null,
    ExecutorBinding? ExecutorBinding = null,
    ConversationCorrelationId? ConversationCorrelationId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkItemExpirySchedule? Expiry = null) : IEventPayload
{
    /// <summary>Preserves the original compiled constructor contract; expiry is additive.</summary>
    public WorkItemCreated(
        string AggregateId,
        long Sequence,
        TenantId TenantId,
        WorkItemId WorkItemId,
        Obligation Obligation,
        WorkItemEffort? InitialEffort,
        WorkItemSchedule? Schedule,
        ParentWorkItemReference? Parent,
        ExecutorBinding? ExecutorBinding,
        ConversationCorrelationId? ConversationCorrelationId)
        : this(AggregateId, Sequence, TenantId, WorkItemId, Obligation, InitialEffort, Schedule, Parent, ExecutorBinding, ConversationCorrelationId, null)
    {
    }

    /// <summary>Preserves deconstruction of the original durable contract.</summary>
    public void Deconstruct(
        out string AggregateId,
        out long Sequence,
        out TenantId TenantId,
        out WorkItemId WorkItemId,
        out Obligation Obligation,
        out WorkItemEffort? InitialEffort,
        out WorkItemSchedule? Schedule,
        out ParentWorkItemReference? Parent,
        out ExecutorBinding? ExecutorBinding,
        out ConversationCorrelationId? ConversationCorrelationId)
    {
        AggregateId = this.AggregateId;
        Sequence = this.Sequence;
        TenantId = this.TenantId;
        WorkItemId = this.WorkItemId;
        Obligation = this.Obligation;
        InitialEffort = this.InitialEffort;
        Schedule = this.Schedule;
        Parent = this.Parent;
        ExecutorBinding = this.ExecutorBinding;
        ConversationCorrelationId = this.ConversationCorrelationId;
    }
}
