using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

[PolymorphicSerialization]
public sealed partial record RescheduleWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemSchedule Schedule,
    string? Note = null);
