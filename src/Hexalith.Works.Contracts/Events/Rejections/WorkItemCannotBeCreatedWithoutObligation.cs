using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Events.Rejections;

public sealed record WorkItemCannotBeCreatedWithoutObligation(
    TenantId TenantId,
    WorkItemId WorkItemId) : IRejectionEvent;
