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
    private readonly SharedProjectionEpochCoordinator? _coordinator;
    private readonly WorksQueryPolicy? _policy;
    private readonly IOptions<WorksProjectionOptions>? _options;

    /// <summary>Initializes a new instance of the <see cref="GetWorkItemQueryHandler"/> class.</summary>
    /// <param name="store">The persisted read-model store.</param>
    public GetWorkItemQueryHandler(
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

        if (_options?.Value.UseSdkDelivery == true)
        {
            return await ExecuteSdkAsync(query, tenantId, workItemId, cancellationToken).ConfigureAwait(false);
        }

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

    private async Task<QueryResult> ExecuteSdkAsync(
        QueryEnvelope query,
        TenantId tenantId,
        WorkItemId workItemId,
        CancellationToken cancellationToken)
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
            return SdkUnavailable(0, false);
        }

        try
        {
            var scope = new SharedProjectionScope(
                WorksReadModelKeys.StateStoreName,
                query.TenantId,
                DomainName,
                WorksReadModelKeys.RuntimeFamily,
                ["works-runtime-v3-writer"]);
            string rollUpKey = WorksReadModelKeys.RuntimeRollUpKey(query.AggregateId);
            string parkingKey = WorksReadModelKeys.RuntimeParkingKey(query.AggregateId);
            SharedProjectionReadResult<IReadOnlyDictionary<string, WorksRuntimeDocument?>> read = await _coordinator
                .ReadManyAsync<WorksRuntimeDocument>(
                    scope,
                    [WorksReadModelKeys.RuntimeQueueKey, rollUpKey, parkingKey],
                    cancellationToken).ConfigureAwait(false);
            if (!read.IsAvailable || read.Value is null
                || !read.Value.TryGetValue(WorksReadModelKeys.RuntimeQueueKey, out WorksRuntimeDocument? queueDocument)
                || queueDocument is null)
            {
                return SdkUnavailable(read.Generation, read.IsStale);
            }

            SharedProjectionEpochStatus status = await _coordinator.GetStatusAsync(scope, cancellationToken)
                .ConfigureAwait(false);
            if (status.IsOffboarded || status.ActiveGeneration != read.Generation)
            {
                return SdkUnavailable(read.Generation, true);
            }

            bool stale = read.IsStale || status.IsBuilding || status.PendingDeliveryCount > 0;

            WorksWhatsNextTenantIndex index = queueDocument.As<WorksWhatsNextTenantIndex>();
            if (!WorksWhatsNextTenantIndexValidation.IsValidCurrent(index))
            {
                return SdkUnavailable(read.Generation, stale);
            }

            QueryResponseMetadata metadata = SdkMetadata(read.Generation, stale);
            if (!index.MemberWorkItemIds.Contains(query.AggregateId, StringComparer.Ordinal))
            {
                return NotFound(tenantId, workItemId, metadata);
            }

            if (!authority.AllWork
                && (!index.Items.TryGetValue(query.AggregateId, out WhatsNextItem? item)
                    || WhatsNextQueryAuthorization.Filter(query.TenantId, item, candidate =>
                        string.Equals(candidate.WorkItemId?.Value, query.AggregateId, StringComparison.Ordinal)
                        && (candidate.Status == WorkItemStatus.Queued
                            || candidate.Status == WorkItemStatus.Assigned
                                && string.Equals(candidate.ExecutorBinding?.PartyId?.Value, authority.PartyId,
                                    StringComparison.Ordinal))) is null))
            {
                return NotFound(tenantId, workItemId, metadata);
            }

            if (!read.Value.TryGetValue(parkingKey, out WorksRuntimeDocument? parkingDocument)
                || parkingDocument is null
                || parkingDocument.As<WorkItemProjectionParking>().Parked)
            {
                return SdkUnavailable(read.Generation, stale);
            }

            if (!read.Value.TryGetValue(rollUpKey, out WorksRuntimeDocument? rollUpDocument)
                || rollUpDocument is null)
            {
                return SdkUnavailable(read.Generation, stale);
            }

            WorkItemRollUp rollUp = rollUpDocument.As<WorkItemRollUp>();
            return MatchesIdentity(rollUp, query.TenantId, query.AggregateId)
                ? Found(rollUp, metadata)
                : NotFound(tenantId, workItemId, metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return SdkUnavailable(0, false);
        }
    }

    private static QueryResponseMetadata SdkMetadata(long generation, bool stale)
        => new(IsStale: stale, IsDegraded: stale,
            ProjectionVersion: generation.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            Provenance = QueryResponseProvenance.ProjectionBacked,
            Lifecycle = stale ? ProjectionLifecycleState.Stale : ProjectionLifecycleState.Current,
        };

    private static QueryResult SdkUnavailable(long generation, bool stale)
        => QueryResult.Failure("projection-unavailable",
            new QueryResponseMetadata(IsStale: stale, IsDegraded: true,
                ProjectionVersion: generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
                WarningCodes: ["works-runtime-unavailable"])
            {
                Provenance = QueryResponseProvenance.ProjectionBacked,
                Lifecycle = ProjectionLifecycleState.Unavailable,
            });

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

    private static QueryResult Found(WorkItemRollUp rollUp, QueryResponseMetadata? metadata = null)
        => QueryResult.FromPayload(JsonSerializer.SerializeToElement(ToView(rollUp), s_jsonOptions), WorksReadModelKeys.WorkItemViewProjectionType, metadata);

    private static QueryResult NotFound(TenantId tenantId, WorkItemId workItemId, QueryResponseMetadata? metadata = null)
        => QueryResult.FromPayload(
            JsonSerializer.SerializeToElement(WorkItemView.NotFound(tenantId, workItemId), s_jsonOptions),
            WorksReadModelKeys.WorkItemViewProjectionType,
            metadata);

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
            rollUp.LatestAcceptedSourceSequence)
        {
            ConversationCorrelationId = rollUp.ConversationCorrelationId,
        };
}
