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

        ReadModelEntry<WorksWhatsNextTenantIndex> entry = await _store
            .GetAsync<WorksWhatsNextTenantIndex>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.WhatsNextIndexKey(tenantId), cancellationToken)
            .ConfigureAwait(false);

        if (entry.Value is not { } index)
        {
            // Missing/unavailable read model: bounded empty result; do not fabricate freshness.
            return Success([]);
        }

        IEnumerable<WhatsNextItem> ordered = index.Items.Values.OrderBy(static item => item, WhatsNextOrdering.Instance);
        IReadOnlyList<WhatsNextItem> authorized = WhatsNextQueryAuthorization.FilterList(tenantId, ordered);

        return Success(authorized);
    }

    private static QueryResult Success(IReadOnlyList<WhatsNextItem> items)
        => QueryResult.FromPayload(JsonSerializer.SerializeToElement(items, s_jsonOptions), WorksReadModelKeys.WhatsNextProjectionType);
}
