using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

[PolymorphicSerialization]
public sealed partial record WorkItemCannotReferenceSecondParent(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ParentWorkItemReference ExistingParent,
    ParentWorkItemReference ProposedParent) : IRejectionEvent;
