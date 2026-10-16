using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

[PolymorphicSerialization]
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed partial record RescheduleWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemSchedule Schedule,
    string? Note = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkItemExpirySchedule? Expiry = null)
{
    /// <summary>Preserves the original compiled constructor contract; expiry is additive.</summary>
    public RescheduleWorkItem(
        TenantId TenantId,
        WorkItemId WorkItemId,
        WorkItemSchedule Schedule,
        string? Note)
        : this(TenantId, WorkItemId, Schedule, Note, null)
    {
    }

    /// <summary>Preserves deconstruction of the original durable contract.</summary>
    public void Deconstruct(
        out TenantId TenantId,
        out WorkItemId WorkItemId,
        out WorkItemSchedule Schedule,
        out string? Note)
    {
        TenantId = this.TenantId;
        WorkItemId = this.WorkItemId;
        Schedule = this.Schedule;
        Note = this.Note;
    }
}
