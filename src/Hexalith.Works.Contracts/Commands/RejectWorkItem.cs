using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// A bound executor declines an assignment. With <paramref name="Requeue"/> = true (the default) the
/// item rests at <c>Queued</c>; with <c>false</c> it reaches the terminal <c>Rejected</c> status.
/// This <c>RejectWorkItem</c> command is distinct from a <c>WorkItemTransitionRejected</c> rejection
/// event (an illegal transition refused with no state change).
/// </summary>
[PolymorphicSerialization]
public sealed partial record RejectWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    bool Requeue = true);
