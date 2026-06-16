using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Models;

public sealed record RollUpProjectionDiagnostic(
    TenantId TenantId,
    WorkItemId WorkItemId,
    string EventType,
    long Sequence);
