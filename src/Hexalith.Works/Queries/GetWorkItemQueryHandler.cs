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

        ReadModelEntry<WorksWhatsNextTenantIndex> currentIndexEntry = await _store
            .GetAsync<WorksWhatsNextTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.CurrentWhatsNextIndexKey(query.TenantId),
                cancellationToken)
            .ConfigureAwait(false);
        if (currentIndexEntry.Value is not null)
        {
            return await ReadCurrentAsync(
                query.TenantId,
                query.AggregateId,
                tenantId,
                workItemId,
                currentIndexEntry.Value,
                cancellationToken).ConfigureAwait(false);
        }

        ReadModelEntry<WorkItemRollUp> legacyEntry = await _store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(query.TenantId, query.AggregateId),
                cancellationToken)
            .ConfigureAwait(false);
        if (MatchesIdentity(legacyEntry.Value, query.TenantId, query.AggregateId))
        {
            return Found(legacyEntry.Value!);
        }

        // A rebuild can atomically delete the legacy roll-up and publish the current manifest between the
        // two reads above. Re-read the current manifest once to close that commit window.
        currentIndexEntry = await _store
            .GetAsync<WorksWhatsNextTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.CurrentWhatsNextIndexKey(query.TenantId),
                cancellationToken)
            .ConfigureAwait(false);
        return currentIndexEntry.Value is not null
            ? await ReadCurrentAsync(
                query.TenantId,
                query.AggregateId,
                tenantId,
                workItemId,
                currentIndexEntry.Value,
                cancellationToken).ConfigureAwait(false)
            : NotFound(tenantId, workItemId);
    }

    private async Task<QueryResult> ReadCurrentAsync(
        string tenantIdValue,
        string workItemIdValue,
        TenantId tenantId,
        WorkItemId workItemId,
        WorksWhatsNextTenantIndex index,
        CancellationToken cancellationToken)
    {
        if (!WorksWhatsNextTenantIndexValidation.IsValidCurrent(index)
            || !index.MemberWorkItemIds.Contains(workItemIdValue, StringComparer.Ordinal))
        {
            return NotFound(tenantId, workItemId);
        }

        ReadModelEntry<WorkItemRollUp> entry = await _store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.CurrentRollUpKey(tenantIdValue, workItemIdValue),
                cancellationToken)
            .ConfigureAwait(false);
        return MatchesIdentity(entry.Value, tenantIdValue, workItemIdValue)
            ? Found(entry.Value!)
            : NotFound(tenantId, workItemId);
    }

    private static bool MatchesIdentity(WorkItemRollUp? rollUp, string tenantId, string workItemId)
        => rollUp is not null
            && string.Equals(rollUp.TenantId?.Value, tenantId, StringComparison.Ordinal)
            && string.Equals(rollUp.WorkItemId?.Value, workItemId, StringComparison.Ordinal);

    private static QueryResult Found(WorkItemRollUp rollUp)
        => QueryResult.FromPayload(JsonSerializer.SerializeToElement(ToView(rollUp), s_jsonOptions), WorksReadModelKeys.WorkItemViewProjectionType);

    private static QueryResult NotFound(TenantId tenantId, WorkItemId workItemId)
        => QueryResult.FromPayload(
            JsonSerializer.SerializeToElement(WorkItemView.NotFound(tenantId, workItemId), s_jsonOptions),
            WorksReadModelKeys.WorkItemViewProjectionType);

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
