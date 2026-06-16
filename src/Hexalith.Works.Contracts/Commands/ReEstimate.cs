using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

[PolymorphicSerialization]
public sealed partial record ReEstimate(
    TenantId TenantId,
    WorkItemId WorkItemId,
    decimal Estimated,
    Unit Unit,
    string? Note = null);
