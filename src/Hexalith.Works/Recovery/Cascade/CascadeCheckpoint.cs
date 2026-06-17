namespace Hexalith.Works.Recovery.Cascade;

/// <summary>The dispatch status of a single cascade target descendant.</summary>
public enum CascadeTargetStatus
{
    /// <summary>The target terminal command has not been attempted yet.</summary>
    Pending = 0,

    /// <summary>The target terminal command attempt has been recorded but not yet confirmed dispatched.</summary>
    Attempted = 1,

    /// <summary>The target terminal command has been dispatched into the EventStore command path.</summary>
    Completed = 2,
}

/// <summary>
/// A single descendant target within a cascade checkpoint. It carries only bounded identifiers — the
/// descendant work item id, the terminal command kind (<c>Cancel</c>/<c>Expire</c>), the dispatch status,
/// and the deterministic correlation id used for at-least-once dedup — never any obligation or payload.
/// </summary>
public sealed record CascadeTargetCheckpoint(
    string DescendantWorkItemId,
    string Kind,
    CascadeTargetStatus Status,
    string CorrelationId);

/// <summary>
/// The persisted, re-readable record of one parent-terminal cascade (Story 4.6, AC #4/#5). It survives a
/// process restart so checkpoint replay can discover the descendants still requiring termination from this
/// projection — never from an in-memory list — and skip the descendants already dispatched. Bounded fields
/// only: tenant id, parent work item id, the parent terminal event type and sequence, the per-target dispatch
/// state, and a completion marker.
/// </summary>
public sealed record CascadeCheckpoint(
    string TenantId,
    string ParentWorkItemId,
    string ParentTerminalEventType,
    long ParentTerminalSequence,
    IReadOnlyList<CascadeTargetCheckpoint> Targets,
    bool Completed)
{
    /// <summary>The cascade kind for a parent <c>WorkItemCancelled</c> — descendants receive <c>CancelWorkItem</c>.</summary>
    public const string CancelKind = "Cancel";

    /// <summary>The cascade kind for a parent <c>WorkItemExpired</c> — descendants receive <c>ExpireWorkItem</c>.</summary>
    public const string ExpireKind = "Expire";
}
