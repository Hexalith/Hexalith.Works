using Hexalith.EventStore.Contracts.Events;
using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

[PolymorphicSerialization]
public sealed partial record WorkItemTreeDepthExceeded(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ParentWorkItemReference ProposedParent,
    int MaxDepth,
    int ResultingDepth) : IRejectionEvent;
