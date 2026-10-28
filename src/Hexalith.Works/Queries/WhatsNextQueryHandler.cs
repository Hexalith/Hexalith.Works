using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.Strategies;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hexalith.Works.Queries;

/// <summary>
/// Runtime query handler for the tenant "what's next" queue (domain <c>work</c>). It reads the persisted
/// tenant-scoped index written by the SDK projection delivery adapter, applies the pure
/// <see cref="WhatsNextOrdering"/> (Priority → earliest Due Date → identity) and the pure
/// <see cref="WhatsNextQueryAuthorization"/> tenant filter using <see cref="QueryEnvelope.TenantId"/>, and
/// returns the ordered, authorized result as payload bytes with projection metadata. An available projection
/// with no eligible authorized items returns an empty queue. Missing, invalid, or unavailable required
/// projection documents instead return a <c>projection-unavailable</c> failure with unavailable metadata.
/// Recorded parked members are omitted from a successful degraded queue carrying <c>works-runtime-parked</c>.
/// </summary>
public sealed class WhatsNextQueryHandler : IDomainQueryHandler
{
    /// <summary>The domain this handler serves.</summary>
    public const string DomainName = "work";

    /// <summary>The query-type discriminator this handler serves.</summary>
    public const string WhatsNextQueryType = "whats-next";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SharedProjectionEpochCoordinator? _coordinator;
    private readonly WorksQueryPolicy? _policy;
    private readonly ILogger<WhatsNextQueryHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="WhatsNextQueryHandler"/> class.</summary>
    /// <param name="policy">The Works query authority policy; when absent every query is refused.</param>
    /// <param name="coordinator">The SDK shared-projection reader; when absent every query reports the projection unavailable.</param>
    public WhatsNextQueryHandler(
        WorksQueryPolicy? policy = null,
        SharedProjectionEpochCoordinator? coordinator = null)
        : this(policy, coordinator, NullLogger<WhatsNextQueryHandler>.Instance)
    {
    }

    /// <summary>Initializes the query adapter with safe operational diagnostics.</summary>
    /// <param name="policy">The Works query authority policy.</param>
    /// <param name="coordinator">The SDK shared-projection reader.</param>
    /// <param name="logger">The diagnostic logger.</param>
    public WhatsNextQueryHandler(WorksQueryPolicy? policy, SharedProjectionEpochCoordinator? coordinator,
        ILogger<WhatsNextQueryHandler> logger)
    {
        _policy = policy;
        _coordinator = coordinator;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string Domain => DomainName;

    /// <inheritdoc/>
    public string QueryType => WhatsNextQueryType;

    /// <inheritdoc/>
    public async Task<QueryResult> ExecuteAsync(QueryEnvelope query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await ExecuteSdkAsync(query, cancellationToken).ConfigureAwait(false);
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
                WorksFailureDiagnostics.QueryUnavailable(_logger, WhatsNextQueryType, query.TenantId, query.CorrelationId, "invalid-membership");
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
                WorksFailureDiagnostics.QueryUnavailable(_logger, WhatsNextQueryType, query.TenantId, query.CorrelationId, "invalid-membership");
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
                {
                    WorksFailureDiagnostics.QueryUnavailable(_logger, WhatsNextQueryType, query.TenantId, query.CorrelationId, "invalid-rollup");
                    return Unavailable(read.Generation, stale);
                }
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
        catch (Exception exception)
        {
            WorksFailureDiagnostics.QueryFailed(_logger, WhatsNextQueryType, query.TenantId, query.CorrelationId, exception);
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

}
