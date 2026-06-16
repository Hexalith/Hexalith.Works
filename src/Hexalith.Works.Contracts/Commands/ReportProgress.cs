using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

[PolymorphicSerialization]
public sealed partial record ReportProgress(
    TenantId TenantId,
    WorkItemId WorkItemId,
    decimal DoneDelta,
    Unit Unit,
    string? Note = null);
