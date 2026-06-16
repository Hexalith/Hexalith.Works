using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Reactor;

public sealed record AwaitingParent(
    TenantId TenantId,
    WorkItemId WorkItemId,
    IReadOnlyList<AwaitCondition> AwaitConditions);
