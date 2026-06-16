using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

public sealed record WorkItemCannotReferenceParentFromAnotherTenant(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ParentWorkItemReference Parent) : IRejectionEvent;
