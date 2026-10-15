using Hexalith.EventStore.Client.Projections;

namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Sizes the read-model batch operation ceiling for the largest sealed epoch rebuild Works can plan.</summary>
internal static class WorksRebuildOperationBound
{
    /// <summary>Documents every epoch plan carries once: the queue, pending-date and topology indexes.</summary>
    internal const int FixedDocuments = 3;

    /// <summary>Documents per aggregate besides history pages: the history manifest, parking marker and roll-up.</summary>
    internal const int DocumentsPerAggregate = 3;

    /// <summary>
    /// Gets the worst-case operation count of one epoch plan. Each admitted aggregate holds at least one history
    /// page, so the pages cost one more document per aggregate; the tenant byte ceiling then caps the pages
    /// beyond the first at <c>MaxTenantBytes / PageBytes</c> (rounding adds at most one page per aggregate).
    /// </summary>
    internal static int MaxOperations { get; } = FixedDocuments
        + (ProjectionDispatchOptions.DefaultMaxSharedRebuildAggregateCount
            * (DocumentsPerAggregate + 1))
        + (WorkItemRuntimeHistoryStore.MaxTenantBytes / WorkItemRuntimeHistoryStore.PageBytes);
}
