using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.Strategies;

namespace Hexalith.Works.Queries;

/// <summary>
/// Runtime query handler for the tenant "what's next" queue (domain <c>work</c>). It reads the persisted
/// tenant-scoped index written by <see cref="WorkItemProjectionDispatcher"/>, applies the pure
/// <see cref="WhatsNextOrdering"/> (Priority → earliest Due Date → identity) and the pure
/// <see cref="WhatsNextQueryAuthorization"/> tenant filter using <see cref="QueryEnvelope.TenantId"/>, and
/// returns the ordered, authorized result as payload bytes only. It is fail-closed: a missing/unavailable read
/// model returns a bounded empty result rather than fabricating freshness.
/// </summary>
public sealed class WhatsNextQueryHandler : IDomainQueryHandler
{
    /// <summary>The domain this handler serves.</summary>
    public const string DomainName = "work";

    /// <summary>The query-type discriminator this handler serves.</summary>
    public const string WhatsNextQueryType = "whats-next";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadModelStore _store;

    /// <summary>Initializes a new instance of the <see cref="WhatsNextQueryHandler"/> class.</summary>
    /// <param name="store">The persisted read-model store.</param>
    public WhatsNextQueryHandler(IReadModelStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc/>
    public string Domain => DomainName;

    /// <inheritdoc/>
    public string QueryType => WhatsNextQueryType;

    /// <inheritdoc/>
    public async Task<QueryResult> ExecuteAsync(QueryEnvelope query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        string tenantId = query.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // Fail closed: no authoritative tenant context means an empty result, never a cross-tenant read.
            return Success([]);
        }

        ReadModelEntry<WorksWhatsNextTenantIndex> currentEntry = await _store
            .GetAsync<WorksWhatsNextTenantIndex>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.CurrentWhatsNextIndexKey(tenantId), cancellationToken)
            .ConfigureAwait(false);
        if (currentEntry.Value is not null)
        {
            return WorksWhatsNextTenantIndexValidation.IsValidCurrent(currentEntry.Value)
                ? Success(CurrentItems(tenantId, currentEntry.Value))
                : Success([]);
        }

        ReadModelEntry<WorksWhatsNextTenantIndex> legacyEntry = await _store
            .GetAsync<WorksWhatsNextTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.WhatsNextIndexKey(tenantId),
                cancellationToken)
            .ConfigureAwait(false);
        if (legacyEntry.Value is not null)
        {
            return WorksWhatsNextTenantIndexValidation.IsUsableLegacy(legacyEntry.Value)
                ? Success(AuthorizedItems(tenantId, legacyEntry.Value.Items.Values))
                : Success([]);
        }

        // A rebuild can commit between the first current-schema miss and this legacy miss. Re-read the
        // authoritative key once so that the atomic legacy-delete/current-write switch cannot look empty.
        currentEntry = await _store
            .GetAsync<WorksWhatsNextTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.CurrentWhatsNextIndexKey(tenantId),
                cancellationToken)
            .ConfigureAwait(false);
        return currentEntry.Value is not null && WorksWhatsNextTenantIndexValidation.IsValidCurrent(currentEntry.Value)
            ? Success(CurrentItems(tenantId, currentEntry.Value))
            : Success([]);
    }

    private static IReadOnlyList<WhatsNextItem> CurrentItems(string tenantId, WorksWhatsNextTenantIndex index)
    {
        var members = new HashSet<string>(index.MemberWorkItemIds, StringComparer.Ordinal);
        return AuthorizedItems(
            tenantId,
            index.Items.Values.Where(item => item?.WorkItemId is not null && members.Contains(item.WorkItemId.Value)));
    }

    private static IReadOnlyList<WhatsNextItem> AuthorizedItems(string tenantId, IEnumerable<WhatsNextItem> items)
        => WhatsNextQueryAuthorization.FilterList(
            tenantId,
            items.OrderBy(static item => item, WhatsNextOrdering.Instance));

    private static QueryResult Success(IReadOnlyList<WhatsNextItem> items)
        => QueryResult.FromPayload(JsonSerializer.SerializeToElement(items, s_jsonOptions), WorksReadModelKeys.WhatsNextProjectionType);
}
