using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Asks a parent Work Item to spawn child work. The child creation facts mirror
/// <see cref="CreateWorkItem"/> (obligation, optional effort/schedule/binding/conversation) so the
/// command pipeline can build an equivalent child <see cref="CreateWorkItem"/> with a
/// <c>ParentWorkItemReference</c>. The child id is caller-supplied; the aggregate never generates it.
/// The tree facts (<paramref name="ProposedParentAncestors"/>, <paramref name="ProposedParentDepth"/>,
/// <paramref name="MaxDepth"/>, <paramref name="ExistingChildParent"/>) are caller-fed so the aggregate
/// can reuse the pure work-tree guard without reading EventStore or projections.
/// <paramref name="MaxDepth"/> and <paramref name="ProposedParentDepth"/> default to the domain's
/// <c>WorkTreeDepthPolicy.DefaultMaxDepth</c> (32) / root-depth (1); the literals are duplicated here
/// because Contracts must not depend on the Server-side policy constant, and must stay in sync with it.
/// </summary>
[PolymorphicSerialization]
public sealed partial record SpawnChild(
    TenantId TenantId,
    WorkItemId WorkItemId,
    WorkItemId ChildWorkItemId,
    string? Obligation,
    WorkItemEffort? InitialEffort = null,
    WorkItemSchedule? Schedule = null,
    ExecutorBinding? ExecutorBinding = null,
    ConversationCorrelationId? ConversationCorrelationId = null,
    bool SuspendParentUntilChildCompletes = false,
    IReadOnlyList<ParentWorkItemReference>? ProposedParentAncestors = null,
    int ProposedParentDepth = 1,
    int MaxDepth = 32,
    ParentWorkItemReference? ExistingChildParent = null);
