using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;

namespace Hexalith.Works.Queries;

/// <summary>
/// Runtime query handler that returns a single work item by id (domain <c>work</c>, query type
/// <c>get-work-item</c>). It reads the persisted per-item <see cref="WorkItemRollUp"/> read model written by
/// <see cref="WorkItemProjectionDispatcher"/> under the tenant-scoped roll-up key and projects it into the
/// consumer-facing <see cref="WorkItemView"/>. It is fail-closed: a missing or unavailable read model returns a
/// <see cref="WorkItemView.NotFound"/> view rather than throwing or fabricating existence, and the tenant-scoped
/// key means a cross-tenant inner id can never resolve another tenant's work item.
/// </summary>
public sealed class GetWorkItemQueryHandler : IDomainQueryHandler
{
    /// <summary>The domain this handler serves.</summary>
    public const string DomainName = "work";

    /// <summary>The query-type discriminator this handler serves.</summary>
    public const string GetWorkItemQueryType = "get-work-item";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadModelStore _store;

    /// <summary>Initializes a new instance of the <see cref="GetWorkItemQueryHandler"/> class.</summary>
    /// <param name="store">The persisted read-model store.</param>
    public GetWorkItemQueryHandler(IReadModelStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc/>
    public string Domain => DomainName;

    /// <inheritdoc/>
    public string QueryType => GetWorkItemQueryType;

    /// <inheritdoc/>
    public async Task<QueryResult> ExecuteAsync(QueryEnvelope query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // QueryEnvelope guarantees a non-blank TenantId and AggregateId at construction; the work item id is
        // carried as the aggregate id. The read is tenant-scoped through the roll-up key, so a cross-tenant id
        // can never resolve another tenant's work item.
        var tenantId = new TenantId(query.TenantId);
        var workItemId = new WorkItemId(query.AggregateId);

        ReadModelEntry<WorkItemRollUp> entry = await _store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(query.TenantId, query.AggregateId),
                cancellationToken)
            .ConfigureAwait(false);

        WorkItemView view = entry.Value is { } rollUp
            ? ToView(rollUp)
            : WorkItemView.NotFound(tenantId, workItemId);

        return QueryResult.FromPayload(JsonSerializer.SerializeToElement(view, s_jsonOptions), WorksReadModelKeys.WorkItemViewProjectionType);
    }

    private static WorkItemView ToView(WorkItemRollUp rollUp)
        => new(
            rollUp.TenantId,
            rollUp.WorkItemId,
            true,
            rollUp.Status,
            rollUp.OwnEffort?.Estimated,
            rollUp.OwnEffort?.Done,
            rollUp.OwnEffort?.Remaining,
            rollUp.OwnEffort?.Unit,
            rollUp.Parent,
            rollUp.LatestAcceptedSourceSequence);
}
