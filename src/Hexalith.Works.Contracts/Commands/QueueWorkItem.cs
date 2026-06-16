using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Places a work item into the shared pool (<c>Created</c> → <c>Queued</c>, or <c>Assigned</c> →
/// <c>Queued</c> as a requeue). Single-claim-wins concurrency is out of scope here (Story 4.3).
/// </summary>
public sealed record QueueWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId);
