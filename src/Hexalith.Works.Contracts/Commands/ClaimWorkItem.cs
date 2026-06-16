using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// The <c>InProgress</c>-entry act (<c>Assigned</c> | <c>Queued</c> → <c>InProgress</c>), emitting the
/// catalog's single <c>WorkItemClaimed</c> event. Single-claim-wins concurrency and expected-version
/// conflict are out of scope here — this models the transition only (Story 4.3).
/// </summary>
public sealed record ClaimWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    ExecutorBinding Binding);
