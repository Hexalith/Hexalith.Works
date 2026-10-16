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
[method: System.Text.Json.Serialization.JsonConstructor]
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
    int MaxDepth = 32,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkItemExpirySchedule? Expiry = null)
{
    /// <summary>Preserves the original compiled constructor contract; expiry is additive.</summary>
    public CreateWorkItem(
        TenantId TenantId,
        WorkItemId WorkItemId,
        string? Obligation,
        WorkItemEffort? InitialEffort,
        WorkItemSchedule? Schedule,
        ParentWorkItemReference? Parent,
        ExecutorBinding? ExecutorBinding,
        ConversationCorrelationId? ConversationCorrelationId,
        IReadOnlyList<ParentWorkItemReference>? ProposedParentAncestors,
        int ProposedParentDepth,
        int MaxDepth)
        : this(TenantId, WorkItemId, Obligation, InitialEffort, Schedule, Parent, ExecutorBinding, ConversationCorrelationId, ProposedParentAncestors, ProposedParentDepth, MaxDepth, null)
    {
    }

    /// <summary>Preserves deconstruction of the original durable contract.</summary>
    public void Deconstruct(
        out TenantId TenantId,
        out WorkItemId WorkItemId,
        out string? Obligation,
        out WorkItemEffort? InitialEffort,
        out WorkItemSchedule? Schedule,
        out ParentWorkItemReference? Parent,
        out ExecutorBinding? ExecutorBinding,
        out ConversationCorrelationId? ConversationCorrelationId,
        out IReadOnlyList<ParentWorkItemReference>? ProposedParentAncestors,
        out int ProposedParentDepth,
        out int MaxDepth)
    {
        TenantId = this.TenantId;
        WorkItemId = this.WorkItemId;
        Obligation = this.Obligation;
        InitialEffort = this.InitialEffort;
        Schedule = this.Schedule;
        Parent = this.Parent;
        ExecutorBinding = this.ExecutorBinding;
        ConversationCorrelationId = this.ConversationCorrelationId;
        ProposedParentAncestors = this.ProposedParentAncestors;
        ProposedParentDepth = this.ProposedParentDepth;
        MaxDepth = this.MaxDepth;
    }
}
