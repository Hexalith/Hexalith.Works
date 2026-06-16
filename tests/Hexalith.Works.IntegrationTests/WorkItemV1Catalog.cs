using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Events.Rejections;
using Hexalith.Works.Contracts.ValueObjects;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Shared, frozen sample of the v1 catalog (established in Story 2.2, extended by Story 2.3): one
/// instance of every decorated success event,
/// command, and rejection event. Used by both the polymorphic-resolution test (AC #5) and the
/// concrete-shape additivity guard (AC #1/#2/#3) so the two views cannot drift apart.
/// </summary>
internal static class WorkItemV1Catalog
{
    /// <summary>11 success events + 11 commands + 4 rejection events.</summary>
    internal const int Count = 26;

    /// <summary>
    /// Envelope / transport fields owned by EventStore that must never leak into a domain payload
    /// (NFR-2 / NFR-8) — asserted absent from the concrete serialized form.
    /// </summary>
    internal static readonly string[] EnvelopeFields =
        ["messageId", "causationId", "correlationId", "userId", "metadata", "cloudEvent"];

    internal static TenantId Tenant { get; } = new("tenant-alpha");

    internal static WorkItemId Item { get; } = new("work-001");

    internal static ExecutorBinding Binding { get; } =
        new(new PartyId("party-123"), Channel.Mcp, AuthorityLevel.Coordinate);

    internal static Obligation Obligation { get; } = new("Prepare the first tenant-scoped work item");

    internal static ParentWorkItemReference Parent { get; } = new(Tenant, new WorkItemId("parent-001"));

    /// <summary>The full v1 catalog as base-typed payloads.</summary>
    internal static IReadOnlyList<Polymorphic> All =>
    [
        // 11 success events.
        new WorkItemCreated("work-001", 1, Tenant, Item, Obligation),
        new WorkItemAssigned("work-001", 2, Tenant, Item, Binding),
        new WorkItemQueued("work-001", 3, Tenant, Item),
        new WorkItemClaimed("work-001", 4, Tenant, Item, Binding),
        new WorkItemSuspended("work-001", 5, Tenant, Item),
        new WorkItemResumed("work-001", 6, Tenant, Item),
        new ProgressReported("work-001", 7, Tenant, Item, 3m, new Unit("point"), "first progress"),
        new WorkItemCompleted("work-001", 8, Tenant, Item),
        new WorkItemCancelled("work-001", 9, Tenant, Item),
        new WorkItemRejected("work-001", 10, Tenant, Item, Requeue: false),
        new WorkItemExpired("work-001", 11, Tenant, Item),

        // 11 commands.
        new CreateWorkItem(Tenant, Item, "Prepare the first tenant-scoped work item"),
        new AssignWorkItem(Tenant, Item, Binding),
        new QueueWorkItem(Tenant, Item),
        new ClaimWorkItem(Tenant, Item, Binding),
        new SuspendWorkItem(Tenant, Item),
        new ResumeWorkItem(Tenant, Item),
        new ReportProgress(Tenant, Item, 3m, new Unit("point"), "first progress"),
        new CompleteWorkItem(Tenant, Item),
        new CancelWorkItem(Tenant, Item),
        new RejectWorkItem(Tenant, Item),
        new ExpireWorkItem(Tenant, Item),

        // 4 rejection events.
        new WorkItemTransitionRejected(Tenant, Item, WorkItemStatus.Created, "Assign"),
        new WorkItemProgressRejected(Tenant, Item, "Progress unit must match the established effort unit."),
        new WorkItemCannotBeCreatedWithoutObligation(Tenant, Item),
        new WorkItemCannotReferenceParentFromAnotherTenant(Tenant, Item, Parent),
    ];
}
