using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

[PolymorphicSerialization]
public sealed partial record WorkItemCannotBeCreatedWithoutObligation(
    TenantId TenantId,
    WorkItemId WorkItemId) : IRejectionEvent;
