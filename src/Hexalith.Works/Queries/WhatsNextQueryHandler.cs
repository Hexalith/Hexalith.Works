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
/// with no eligible authorized items returns an empty queue. Parking and roll-up documents are required only
/// for the authorized eligible candidates and for members named by SDK parking evidence; missing, invalid, or
/// unavailable required documents return a <c>projection-unavailable</c> failure with unavailable metadata.
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
        long startedAt = TimeProvider.System.GetTimestamp();
        WorksQueryAuthority authority = _policy is null
            ? new WorksQueryAuthority(false, false, null)
            : await _policy.AuthorizeAsync(query, cancellationToken).ConfigureAwait(false);
        if (!authority.Allowed)
        {
            return QueryResult.Failure(QueryAdapterFailureReason.Forbidden);
        }

        if (_coordinator is not { } coordinator)
        {
            return Unavailable(0, false);
        }

        try
        {
            return await WorksProjectionReadBudget.ReadAsync(
                token => ReadSdkAsync(coordinator, query, authority, token), startedAt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (WorksRuntimeIndexReadException exception)
        {
            WorksFailureDiagnostics.QueryFailed(_logger, WhatsNextQueryType, query.TenantId, query.CorrelationId, exception.InnerException!);
            return Unavailable(exception.Generation, exception.IsStale);
        }
        catch (Exception exception)
        {
            WorksFailureDiagnostics.QueryFailed(_logger, WhatsNextQueryType, query.TenantId, query.CorrelationId, exception);
            return Unavailable(0, false);
        }
    }

    private async Task<QueryResult> ReadSdkAsync(SharedProjectionEpochCoordinator coordinator,
        QueryEnvelope query, WorksQueryAuthority authority, CancellationToken cancellationToken)
    {
        var scope = new SharedProjectionScope(
            WorksReadModelKeys.StateStoreName,
            query.TenantId,
            DomainName,
            WorksReadModelKeys.RuntimeFamily,
            ["works-runtime-v3-writer"]);
        SharedProjectionReadResult<WorksRuntimeIndexView> preliminary = await WorksRuntimeIndexReader
            .ReadAsync(coordinator, scope, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!preliminary.IsAvailable || preliminary.Value is null)
        {
            return Unavailable(preliminary.Generation, preliminary.IsStale);
        }

        // Related documents are read only for the authorized eligible candidates and for members named by SDK
        // parking evidence, so retained terminal membership does not cost two reads per member. Evidence matters
        // even when resolved: re-parking at an already recorded position writes Parked without new evidence.
        WorksWhatsNextTenantIndex index = preliminary.Value.Indexes.Queue;
        var members = new HashSet<string>(index.MemberWorkItemIds, StringComparer.Ordinal);
        if (index.Items.Any(static pair => pair.Value?.WorkItemId?.Value is { } id && !string.Equals(id, pair.Key, StringComparison.Ordinal)))
        {
            // A contradictory index could alias two entries to one work item's related documents.
            WorksFailureDiagnostics.QueryUnavailable(_logger, WhatsNextQueryType, query.TenantId, query.CorrelationId, "invalid-index");
            return Unavailable(preliminary.Generation, true);
        }
        IReadOnlyList<WhatsNextItem> candidates = WhatsNextQueryAuthorization.FilterList(
            query.TenantId,
            index.Items.Values
                .Where(item => item?.WorkItemId is not null && members.Contains(item.WorkItemId.Value))
                .OrderBy(static item => item, WhatsNextOrdering.Instance),
            item => authority.AllWork
                || item.Status == WorkItemStatus.Queued
                || item.Status == WorkItemStatus.Assigned
                    && string.Equals(item.ExecutorBinding?.PartyId?.Value, authority.PartyId, StringComparison.Ordinal));
        SharedProjectionEpochStatus selected = await coordinator.GetStatusAsync(scope, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.IsDegraded || selected.IsOffboarded || selected.ActiveGeneration != preliminary.Generation)
        {
            return Unavailable(preliminary.Generation, true);
        }

        HashSet<string> evidenced = ParkingEvidenceMembers(selected, members);
        string[] parkingMembers = [.. candidates.Select(static item => item.WorkItemId.Value)
            .Concat(evidenced).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        SharedProjectionReadResult<WorksRuntimeIndexView> read = await WorksRuntimeIndexReader
            .ReadRelatedAsync(coordinator, scope, preliminary,
                [.. parkingMembers.Select(WorksReadModelKeys.RuntimeParkingKey),
                    .. candidates.Select(static item => WorksReadModelKeys.RuntimeRollUpKey(item.WorkItemId.Value))], cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!read.IsAvailable || read.Value is null)
        {
            return Unavailable(read.Generation, read.IsStale);
        }

        SharedProjectionEpochStatus status = await coordinator.GetStatusAsync(scope, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (read.IsDegraded || status.IsDegraded || status.IsOffboarded || status.ActiveGeneration != read.Generation
            || !ParkingEvidenceMembers(status, members).IsSubsetOf(parkingMembers))
        {
            // Defense in depth: new unresolved evidence already degrades the status, but a parking document read
            // must always cover every member the SDK records as parked.
            return Unavailable(read.Generation, true);
        }

        bool stale = read.IsStale || status.IsBuilding || status.PendingDeliveryCount > 0;

        var parked = new HashSet<string>(StringComparer.Ordinal);
        foreach (string member in parkingMembers)
        {
            if (!read.Value.Documents.TryGetValue(WorksReadModelKeys.RuntimeParkingKey(member),
                    out WorksRuntimeDocument? parkingDocument) || parkingDocument is null)
            {
                return Unavailable(read.Generation, stale);
            }

            if (parkingDocument.As<WorkItemProjectionParking>().Parked)
            {
                _ = parked.Add(member);
            }
        }

        IReadOnlyList<WhatsNextItem> items = [.. candidates.Where(item => !parked.Contains(item.WorkItemId.Value))];
        var rollUps = new List<WorkItemRollUp>();
        foreach (WhatsNextItem item in items)
        {
            if (!read.Value.Documents.TryGetValue(WorksReadModelKeys.RuntimeRollUpKey(item.WorkItemId.Value), out WorksRuntimeDocument? document)
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

    private static HashSet<string> ParkingEvidenceMembers(SharedProjectionEpochStatus status, HashSet<string> members)
        => new(status.ParkingEvidence.Select(static evidence => evidence.SourceStream).Where(members.Contains), StringComparer.Ordinal);

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
