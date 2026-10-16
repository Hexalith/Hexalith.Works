using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Terminal expiry: accepted from any non-terminal status (→ <c>Expired</c>). The command is the
/// adapter-fired signal — handling reads no clock (deadlines are advisory-until-fired). TTL/date
/// policy is resolved upstream. SDK scheduled signals echo the persisted current expiry witness;
/// witnessless cascade expiry requires its separate authenticated delegated purpose.
/// </summary>
[PolymorphicSerialization]
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed partial record ExpireWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkItemExpirySchedule? ExpiryWitness = null)
{
    /// <summary>Preserves the original compiled constructor contract; expiry is additive.</summary>
    public ExpireWorkItem(
        TenantId TenantId,
        WorkItemId WorkItemId)
        : this(TenantId, WorkItemId, null)
    {
    }

    /// <summary>Preserves deconstruction of the original durable contract.</summary>
    public void Deconstruct(
        out TenantId TenantId,
        out WorkItemId WorkItemId)
    {
        TenantId = this.TenantId;
        WorkItemId = this.WorkItemId;
    }
}
