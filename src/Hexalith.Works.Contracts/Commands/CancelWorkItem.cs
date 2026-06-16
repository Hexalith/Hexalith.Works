using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Terminal cancel: accepted from any non-terminal status (→ <c>Cancelled</c>); a duplicate cancel of
/// an already-<c>Cancelled</c> item is an idempotent no-op, and cancel of any other terminal item is
/// rejected. Cascade through active descendants is out of scope here (Story 3.6).
/// </summary>
[PolymorphicSerialization]
public sealed partial record CancelWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId);
