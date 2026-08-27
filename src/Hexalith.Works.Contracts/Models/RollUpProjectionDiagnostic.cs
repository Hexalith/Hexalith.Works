using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Models;

/// <summary>Describes a state-changing Works payload refused by roll-up validation.</summary>
/// <param name="TenantId">The payload tenant.</param>
/// <param name="WorkItemId">The payload work item.</param>
/// <param name="EventType">The refused Works payload type.</param>
/// <param name="Sequence">
/// The refused payload's state-changing <c>Sequence</c> ordinal, not its EventStore envelope position.
/// </param>
public sealed record RollUpProjectionDiagnostic(
    TenantId TenantId,
    WorkItemId WorkItemId,
    string EventType,
    long Sequence);
