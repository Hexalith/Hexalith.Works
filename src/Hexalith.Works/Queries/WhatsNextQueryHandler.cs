using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.Strategies;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

namespace Hexalith.Works.Queries;

/// <summary>
/// Runtime query handler for the tenant "what's next" queue (domain <c>work</c>). It reads the persisted
/// tenant-scoped index written by the SDK projection delivery adapter, applies the pure
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
    private readonly SharedProjectionEpochCoordinator? _coordinator;
    private readonly WorksQueryPolicy? _policy;
    private readonly IOptions<WorksProjectionOptions>? _options;

    /// <summary>Initializes a new instance of the <see cref="WhatsNextQueryHandler"/> class.</summary>
    /// <param name="store">The persisted read-model store.</param>
    public WhatsNextQueryHandler(
        IReadModelStore store,
        IOptions<WorksProjectionOptions>? options = null,
        WorksQueryPolicy? policy = null,
        SharedProjectionEpochCoordinator? coordinator = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _options = options;
        _policy = policy;
        _coordinator = coordinator;
    }

    /// <inheritdoc/>
    public string Domain => DomainName;

    /// <inheritdoc/>
    public string QueryType => WhatsNextQueryType;

    /// <inheritdoc/>
    public async Task<QueryResult> ExecuteAsync(QueryEnvelope query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_options?.Value.UseSdkDelivery == true)
        {
            return await ExecuteSdkAsync(query, cancellationToken).ConfigureAwait(false);
        }

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

    private async Task<QueryResult> ExecuteSdkAsync(QueryEnvelope query, CancellationToken cancellationToken)
    {
        WorksQueryAuthority authority = _policy is null
            ? new WorksQueryAuthority(false, false, null)
            : await _policy.AuthorizeAsync(query, cancellationToken).ConfigureAwait(false);
        if (!authority.Allowed)
        {
            return QueryResult.Failure(QueryAdapterFailureReason.Forbidden);
        }

        if (_coordinator is null)
        {
            return Unavailable(0, false);
        }

        try
        {
            var scope = new SharedProjectionScope(
                WorksReadModelKeys.StateStoreName,
                query.TenantId,
                DomainName,
                WorksReadModelKeys.RuntimeFamily,
                ["works-runtime-v3-writer"]);
            SharedProjectionReadResult<WorksRuntimeDocument> preliminary = await _coordinator
                .ReadAsync<WorksRuntimeDocument>(scope, WorksReadModelKeys.RuntimeQueueKey, cancellationToken)
                .ConfigureAwait(false);
            if (!preliminary.IsAvailable || preliminary.Value is null)
            {
                return Unavailable(preliminary.Generation, preliminary.IsStale);
            }

            WorksWhatsNextTenantIndex initialIndex = preliminary.Value.As<WorksWhatsNextTenantIndex>();
            if (!WorksWhatsNextTenantIndexValidation.IsValidCurrent(initialIndex))
            {
                return Unavailable(preliminary.Generation, preliminary.IsStale);
            }

            string[] parkingKeys = [.. initialIndex.MemberWorkItemIds.Select(WorksReadModelKeys.RuntimeParkingKey)];
            SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> read = await _coordinator
                .ReadManyAsync<WorksRuntimeDocument>(scope,
                    [WorksReadModelKeys.RuntimeQueueKey, .. parkingKeys,
                        .. initialIndex.MemberWorkItemIds.Select(WorksReadModelKeys.RuntimeRollUpKey)], cancellationToken)
                .ConfigureAwait(false);
            if (!read.IsAvailable || read.Value is null
                || !read.Value.TryGetValue(WorksReadModelKeys.RuntimeQueueKey, out WorksRuntimeDocument? queueDocument)
                || queueDocument is null)
            {
                return Unavailable(read.Generation, read.IsStale);
            }

            SharedProjectionEpochStatus status = await _coordinator.GetStatusAsync(scope, cancellationToken)
                .ConfigureAwait(false);
            if (read.IsDegraded || status.IsDegraded || status.IsOffboarded || status.ActiveGeneration != read.Generation)
            {
                return Unavailable(read.Generation, true);
            }

            bool stale = read.IsStale || status.IsBuilding || status.PendingDeliveryCount > 0;

            WorksWhatsNextTenantIndex index = queueDocument.As<WorksWhatsNextTenantIndex>();
            if (!WorksWhatsNextTenantIndexValidation.IsValidCurrent(index))
            {
                return Unavailable(read.Generation, stale);
            }

            if (index.MemberWorkItemIds.Except(initialIndex.MemberWorkItemIds, StringComparer.Ordinal).Any())
            {
                return Unavailable(read.Generation, stale);
            }

            var parked = new HashSet<string>(StringComparer.Ordinal);
            foreach (string member in index.MemberWorkItemIds)
            {
                if (!read.Value.TryGetValue(WorksReadModelKeys.RuntimeParkingKey(member),
                        out WorksRuntimeDocument? parkingDocument) || parkingDocument is null)
                {
                    return Unavailable(read.Generation, stale);
                }

                if (parkingDocument.As<WorkItemProjectionParking>().Parked)
                {
                    _ = parked.Add(member);
                }
            }

            var members = new HashSet<string>(index.MemberWorkItemIds, StringComparer.Ordinal);
            IReadOnlyList<WhatsNextItem> items = WhatsNextQueryAuthorization.FilterList(
                query.TenantId,
                index.Items.Values
                    .Where(item => item?.WorkItemId is not null
                        && members.Contains(item.WorkItemId.Value)
                        && !parked.Contains(item.WorkItemId.Value))
                    .OrderBy(static item => item, WhatsNextOrdering.Instance),
                item => authority.AllWork
                    || item.Status == WorkItemStatus.Queued
                    || item.Status == WorkItemStatus.Assigned
                        && string.Equals(item.ExecutorBinding?.PartyId?.Value, authority.PartyId, StringComparison.Ordinal));
            var rollUps = new List<WorkItemRollUp>();
            foreach (WhatsNextItem item in items)
            {
                if (!read.Value.TryGetValue(WorksReadModelKeys.RuntimeRollUpKey(item.WorkItemId.Value), out WorksRuntimeDocument? document)
                    || document is null) return Unavailable(read.Generation, stale);
                WorkItemRollUp rollUp = document.As<WorkItemRollUp>();
                if (rollUp.TenantId?.Value != query.TenantId || rollUp.WorkItemId?.Value != item.WorkItemId.Value
                    || rollUp.Status != item.Status || rollUp.LatestAcceptedSourceSequence != item.LatestAcceptedSourceSequence)
                    return Unavailable(read.Generation, stale);
                rollUps.Add(rollUp);
            }
            return QueryResult.FromPayload(
                JsonSerializer.SerializeToElement(items, s_jsonOptions),
                WorksReadModelKeys.WhatsNextProjectionType,
                Metadata(read.Generation, stale, parked.Count > 0, rollUps));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable(0, false);
        }
    }

    private static QueryResponseMetadata Metadata(long generation, bool stale, bool parked, IReadOnlyList<WorkItemRollUp> rollUps)
    {
        bool degraded = parked || rollUps.Any(model => model.Degraded || model.ProjectionDiagnostics.Count > 0);
        string[] warnings = [.. (parked ? new[] { "works-runtime-parked" } : Array.Empty<string>()),
            .. (degraded && rollUps.Any(model => model.Degraded || model.ProjectionDiagnostics.Count > 0)
                ? new[] { "works-rollup-degraded" } : Array.Empty<string>()),
            .. rollUps.SelectMany(model => model.ProjectionDiagnostics).Select(item => "works-rollup-refused:" + item.EventType)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return new(IsStale: stale, IsDegraded: stale || degraded,
            ProjectionVersion: generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WarningCodes: warnings.Length == 0 ? null : warnings)
        {
            Provenance = QueryResponseProvenance.ProjectionBacked,
            Lifecycle = stale ? ProjectionLifecycleState.Stale
                : degraded ? ProjectionLifecycleState.Degraded : ProjectionLifecycleState.Current,
        };
    }

    private static QueryResult Unavailable(long generation, bool stale)
        => QueryResult.Failure(
            "projection-unavailable",
            new QueryResponseMetadata(IsStale: stale, IsDegraded: true,
                ProjectionVersion: generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
                WarningCodes: ["works-runtime-unavailable"])
            {
                Provenance = QueryResponseProvenance.ProjectionBacked,
                Lifecycle = ProjectionLifecycleState.Unavailable,
            });

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
