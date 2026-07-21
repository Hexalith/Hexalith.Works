using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.Contracts.Commands;

/// <summary>
/// Creates a Work Item from the pre-creation state; create is the only entry point out of the
/// <c>Unknown</c> sentinel. For a parented create the tree facts
/// (<paramref name="ProposedParentAncestors"/>, <paramref name="ProposedParentDepth"/>,
/// <paramref name="MaxDepth"/>) are caller-fed — mirroring <see cref="SpawnChild"/> — so the aggregate
/// can reuse the pure work-tree guard without reading EventStore or projections; a root create leaves
/// them at their defaults. <paramref name="MaxDepth"/> and <paramref name="ProposedParentDepth"/>
/// default to the domain's <c>WorkTreeDepthPolicy.DefaultMaxDepth</c> (32) / root-depth (1); the
/// literals are duplicated here because Contracts must not depend on the Server-side policy constant,
/// and must stay in sync with it.
/// </summary>
[PolymorphicSerialization]
public sealed partial record CreateWorkItem(
    TenantId TenantId,
    WorkItemId WorkItemId,
    string? Obligation,
    WorkItemEffort? InitialEffort = null,
    WorkItemSchedule? Schedule = null,
    ParentWorkItemReference? Parent = null,
    ExecutorBinding? ExecutorBinding = null,
    ConversationCorrelationId? ConversationCorrelationId = null,
    IReadOnlyList<ParentWorkItemReference>? ProposedParentAncestors = null,
    int ProposedParentDepth = 1,
    int MaxDepth = 32);
