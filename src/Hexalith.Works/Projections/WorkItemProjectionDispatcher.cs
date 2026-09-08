using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections.Models;
using Hexalith.Works.Projections.Strategies;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Projections;

/// <summary>
/// Runtime projection adapter for the <c>work</c> domain. It consumes an EventStore <see cref="ProjectionRequest"/>
/// full-replay request for a single work item, translates the request's event sequence into the existing pure
/// <see cref="WhatsNextQueueProjection"/> and <see cref="WorkItemRollUpProjection"/> input, and persists the
/// resulting read models through <see cref="IReadModelStore"/> + <see cref="ReadModelWritePolicy"/> under
/// deterministic tenant-scoped keys. All projection/notification concerns live here at the adapter edge — the
/// pure projections stay free of <c>IReadModelStore</c>, Dapr, and logging.
/// </summary>
/// <remarks>
/// <para>Events are decoded by locally constructed Web-compatible options keyed by
/// <see cref="ProjectionEventDto.EventTypeName"/>. The case-insensitive reader accepts both EventPersister's
/// options-free PascalCase bytes and historical camelCase Web fixtures; neither concrete form carries a
/// polymorphic <c>$type</c> discriminator.</para>
/// <para>Reconciliation limitation (documented in <c>docs/eventstore-api-surface-constraints.md</c>): the
/// EventStore <c>/project</c> contract delivers one aggregate's event stream per call, so the cross-aggregate
/// "rolled remaining" contribution from sibling/child work items cannot be reconciled within a single dispatch.
/// Parent rolled totals are therefore persisted and exposed as unavailable while reliable local evidence and
/// parent/child structure are preserved. Child identities reconciled by the shared rebuild are retained across
/// later single-aggregate dispatches so an ordinary replay never republishes a single-stream substitute total
/// over a reconciled document.</para>
/// <para>Logging is bounded to metadata (tenant id, work-item id, event-type names, projection type, counts) —
/// never event payloads, obligations, secrets, tokens, or full command bodies (AC #4 / NFR-6).</para>
/// </remarks>
public sealed class WorkItemProjectionDispatcher
{
    private static readonly JsonSerializerOptions s_webOptions = new(JsonSerializerDefaults.Web);

    private static readonly Action<ILogger, string, string, long, int, int, Exception?> s_projectionDecodeFailed =
        LoggerMessage.Define<string, string, long, int, int>(
            LogLevel.Warning,
            new EventId(4501, "ProjectionDecodeFailed"),
            "Work item {WorkItemId} for tenant {TenantId} could not decode a state-affecting event at sequence "
            + "{SequenceNumber} ({FailureCount} of {MaxFailures} consecutive attempts); the dispatch fails closed and is retried.");

    private static readonly Action<ILogger, string, string, long, int, Exception?> s_projectionAggregateParked =
        LoggerMessage.Define<string, string, long, int>(
            LogLevel.Error,
            new EventId(4502, "ProjectionAggregateParked"),
            "Work item {WorkItemId} for tenant {TenantId} is parked after {FailureCount} consecutive failures to "
            + "decode the state-affecting event at sequence {SequenceNumber}; it is no longer redispatched and needs operator action.");

    private static readonly Action<ILogger, string, string, long, Exception?> s_projectionParkedDispatchSkipped =
        LoggerMessage.Define<string, string, long>(
            LogLevel.Warning,
            new EventId(4503, "ProjectionParkedDispatchSkipped"),
            "Work item {WorkItemId} for tenant {TenantId} remains parked on the undecodable event at sequence "
            + "{SequenceNumber}; this dispatch is acknowledged without projecting.");

    private static readonly Action<ILogger, string, string, string, int, bool, Exception?> s_projected =
        LoggerMessage.Define<string, string, string, int, bool>(
            LogLevel.Information,
            new EventId(4500, "Projected"),
            "Projected work item {WorkItemId} for tenant {TenantId} (correlation {CorrelationId}) from {EventCount} events; whatsNextChanged={Changed}.");

    private readonly IReadModelStore _store;
    private readonly IProjectionChangeNotifier? _notifier;
    private readonly ILogger<WorkItemProjectionDispatcher> _logger;
    private readonly WorksProjectionOptions _options;

    /// <summary>Initializes a new instance of the <see cref="WorkItemProjectionDispatcher"/> class.</summary>
    /// <param name="store">The persisted read-model store.</param>
    /// <param name="notifier">The projection-change notifier, or <see langword="null"/> when none is wired.</param>
    /// <param name="logger">The bounded-metadata logger.</param>
    /// <param name="options">The projection adapter options, or <see langword="null"/> for the defaults.</param>
    public WorkItemProjectionDispatcher(
        IReadModelStore store,
        IProjectionChangeNotifier? notifier,
        ILogger<WorkItemProjectionDispatcher> logger,
        WorksProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _notifier = notifier;
        _logger = logger;
        _options = options ?? new WorksProjectionOptions();
    }

    /// <summary>
    /// Projects a single work item's replayed events into the tenant "what's next" index and the per-item
    /// roll-up read model, notifying on a real eligibility/order change, and returns the per-item state.
    /// </summary>
    /// <param name="request">The EventStore projection request for one <c>work</c> aggregate.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The projection response carrying the <c>works-whats-next</c> projection type and item state.</returns>
    public async Task<ProjectionResponse> DispatchAsync(ProjectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Domain, WorkCommandSubmission.WorkDomain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Projection request domain is not the Works domain.");
        }

        if (WorksReadModelKeys.IsReservedTenantId(request.TenantId))
        {
            // /project is one of the two places a tenant id enters this host. Fail closed here rather than
            // letting the reserved id reach the read-model keys, where its pending-date-await index key would
            // be byte-identical to the well-known pending-date-await tenant registry key and would silently
            // disable date-reminder recovery for every tenant.
            throw new InvalidOperationException(
                $"Tenant id '{WorksReadModelKeys.ReservedTenantId}' is reserved by the Works host: its "
                + "pending-date-await index key collides with the well-known pending-date-await tenant registry key.");
        }

        var tenant = new TenantId(request.TenantId);
        var workItemId = new WorkItemId(request.AggregateId);
        string correlationId = WorkItemProjectionEventDecoder.CorrelationIdOf(request.Events);

        var whatsNext = new WhatsNextQueueProjection();
        var rollUp = new WorkItemRollUpProjection();
        var decodedEvents = new List<(long Sequence, IEventPayload Payload)>();
        bool changed = false;
        bool malformedEvidence = false;
        bool stateEvidenceDelivered = false;
        int decoded = 0;
        bool childContributionMayExist = (request.Events ?? []).Any(dto => dto is not null
            && string.Equals(WorkItemProjectionEventDecoder.SimpleTypeName(dto.EventTypeName), nameof(ChildSpawned), StringComparison.Ordinal));

        foreach (ProjectionEventDto? dto in request.Events ?? [])
        {
            if (dto is null)
            {
                continue;
            }

            WorkItemProjectionEventDecodeResult decode = WorkItemProjectionEventDecoder.Decode(
                dto,
                tenant,
                workItemId,
                correlationId,
                _logger);
            if (decode.Malformed)
            {
                if (PendingDateAwaitProjection.IsStateAffectingEventType(dto.EventTypeName))
                {
                    // Fail closed — but not forever. A permanently undecodable state-affecting event would
                    // otherwise 500 this endpoint on every ProjectionPollerService pass, blocking this
                    // aggregate's roll-up and what's-next writes for good with no terminal disposition. After a
                    // bounded number of consecutive failures at the same sequence, the aggregate is parked: the
                    // dispatch is acknowledged so the poller stops redispatching it, and a distinct error log
                    // names the one visible aggregate an operator has to deal with. Transient causes still get
                    // their retries before that point.
                    if (await ParkOrRetryAsync(tenant, request.AggregateId, dto.SequenceNumber, request.Events, cancellationToken).ConfigureAwait(false))
                    {
                        return NotEligibleResponse();
                    }

                    throw new InvalidOperationException("A state-affecting Works projection event could not be decoded.");
                }

                // A known Works event that cannot be decoded is incomplete evidence, exactly as it is on the
                // shared-rebuild path: skipping it silently and then publishing an available rolled total
                // would expose a number this stream cannot prove.
                malformedEvidence = true;
            }

            IEventPayload? payload = decode.Payload;
            if (payload is null)
            {
                continue;
            }

            if (payload is not IRejectionEvent)
            {
                // A non-rejection delivery is genuine state evidence for this aggregate. It is what separates
                // "the roll-up refused real events" from "there was nothing to project" (an empty or
                // rejection-only replay), which must never touch the authoritative read models.
                stateEvidenceDelivered = true;
            }

            var delivery = new WorkItemRollUpEvent(tenant, workItemId, dto.SequenceNumber, payload);
            rollUp.Project(delivery);
            changed |= whatsNext.Project(delivery).Changed;
            decodedEvents.Add((dto.SequenceNumber, payload));
            decoded++;
        }

        WorkItemRollUp? projected = rollUp.Get(tenant, workItemId);
        bool useCurrentSchema = false;
        bool retainsReconciledChildren = false;
        if (projected is not null)
        {
            useCurrentSchema = await UseCurrentSchemaAsync(tenant, cancellationToken).ConfigureAwait(false);

            // A shared rebuild reconciles children this single-aggregate replay cannot observe: a child that
            // names its parent in WorkItemCreated appends nothing to the parent's stream, so folding the
            // parent's own events alone yields a childless node. Merge the persisted child identities into the
            // replayed ones and refuse both rolled shapes, rather than overwriting a reconciled document with a
            // single-stream substitute total. No Works event ever detaches a child, so retained structure can
            // only become more complete, never wrong. The merge is a union rather than a wholesale replacement
            // or a count comparison: a parent reconciled from a WorkItemCreated.Parent child can later spawn
            // its own children, and either side alone is then incomplete.
            WorkItemRollUp? persisted = await ReadPersistedRollUpAsync(tenant, workItemId, useCurrentSchema, cancellationToken).ConfigureAwait(false);
            if (persisted?.ChildWorkItemIds is { Count: > 0 } reconciled)
            {
                List<WorkItemId> merged = [.. projected.ChildWorkItemIds ?? []];
                foreach (WorkItemId child in reconciled)
                {
                    if (!string.IsNullOrWhiteSpace(child?.Value)
                        && !merged.Exists(known => string.Equals(known.Value, child.Value, StringComparison.Ordinal)))
                    {
                        merged.Add(child);
                    }
                }

                if (merged.Count > (projected.ChildWorkItemIds?.Count ?? 0))
                {
                    // Ordinal work-item id is the published child ordering key (WorkItemRollUpProjection).
                    merged.Sort(static (first, second) => StringComparer.Ordinal.Compare(first.Value, second.Value));
                    retainsReconciledChildren = true;
                    projected = projected with
                    {
                        ChildWorkItemIds = merged,
                    };
                }
            }
        }

        WorkItemRollUp? model = WorkItemProjectionBoundarySanitizer.Sanitize(
            projected,
            childContributionMayExist || malformedEvidence || retainsReconciledChildren);
        WhatsNextItem? item = whatsNext
            .WhatsNext(tenant, (lookupTenant, lookupWorkItemId) =>
                string.Equals(lookupTenant.Value, tenant.Value, StringComparison.Ordinal)
                && string.Equals(lookupWorkItemId.Value, workItemId.Value, StringComparison.Ordinal)
                    ? model
                    : null)
            .FirstOrDefault(candidate => string.Equals(candidate.WorkItemId.Value, request.AggregateId, StringComparison.Ordinal));

        bool indexAccepted = false;
        if (model is not null)
        {
            WorkItemRollUp persistedRollUp = await PersistRollUpAsync(
                tenant,
                workItemId,
                model,
                request.Events,
                useCurrentSchema,
                cancellationToken).ConfigureAwait(false);

            // The roll-up is written first and acts as this aggregate's own ordering guard. If a concurrent
            // newer replay already won that key, this stale dispatch must not attempt the independent tenant-
            // index write. The two keys are intentionally non-atomic and converge through their own watermarks.
            if (persistedRollUp.LatestAcceptedSourceSequence <= model.LatestAcceptedSourceSequence)
            {
                indexAccepted = await UpsertTenantIndexAsync(
                    tenant,
                    request.AggregateId,
                    item,
                    model.LatestAcceptedSourceSequence,
                    request.Events,
                    useCurrentSchema,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else if (stateEvidenceDelivered && MaxSequence(request.Events) is { } deliveredLastSequence)
        {
            // The roll-up yielded no model even though this replay carried real (non-rejection) state
            // evidence, so there is no model to guard the write with — but index removal must stay reachable:
            // otherwise a stale eligible entry and its LastSequences watermark are retained forever. The
            // delivered stream's own last sequence is the incoming watermark, and UpsertTenantIndexAsync's
            // internal monotonic guard compares it against the persisted LastSequences entry (never against
            // `model`), so a stale replay still loses. An empty or rejection-only replay is deliberately NOT
            // this case: it proves nothing about the item and must not mutate the authoritative models.
            useCurrentSchema = await UseCurrentSchemaAsync(tenant, cancellationToken).ConfigureAwait(false);
            indexAccepted = await UpsertTenantIndexAsync(
                tenant,
                request.AggregateId,
                item,
                deliveredLastSequence,
                request.Events,
                useCurrentSchema,
                cancellationToken).ConfigureAwait(false);
        }

        await MaintainPendingDateAwaitIndexAsync(tenant, request.AggregateId, decodedEvents, request.Events, cancellationToken).ConfigureAwait(false);

        if (changed && indexAccepted && _notifier is not null)
        {
            await _notifier
                .NotifyProjectionChangedAsync(WorksReadModelKeys.WhatsNextProjectionType, tenant.Value, entityId: null, cancellationToken)
                .ConfigureAwait(false);
        }

        s_projected(_logger, request.AggregateId, tenant.Value, correlationId, decoded, changed, null);

        return item is not null
            ? new ProjectionResponse(
                WorksReadModelKeys.WhatsNextProjectionType,
                JsonSerializer.SerializeToElement(item, s_webOptions))
            : NotEligibleResponse();
    }

    /// <summary>The response for a work item that is not in the eligible "what's next" set.</summary>
    private static ProjectionResponse NotEligibleResponse()
        => new(
            WorksReadModelKeys.WhatsNextProjectionType,
            JsonSerializer.SerializeToElement(new WhatsNextProjectionState(false), s_webOptions));

    /// <summary>The greatest delivered source sequence in a projection request, or null when none was delivered.</summary>
    private static long? MaxSequence(IReadOnlyList<ProjectionEventDto>? events)
        => (events ?? [])
            .Where(static value => value is not null)
            .Select(static value => (long?)value.SequenceNumber)
            .Max();

    private async Task<bool> UpsertTenantIndexAsync(
        TenantId tenant,
        string aggregateId,
        WhatsNextItem? item,
        long incomingLastSequence,
        IReadOnlyList<ProjectionEventDto>? events,
        bool useCurrentSchema,
        CancellationToken cancellationToken)
    {
        // Carry tenant + correlation context and a bounded event-type summary into the read-model write so a
        // write conflict/exhaustion surfaces with correlation and tenant context (AC #4 / NFR-6). The platform
        // helper derives the correlation id from the events and caps the event-type field, so a full replay of
        // many events cannot bloat conflict/exhaustion log lines.
        ReadModelWriteContext context = new ReadModelWriteContext(
            Category: "works what's-next index",
            ProjectionType: WorksReadModelKeys.WhatsNextProjectionType)
            .WithEventDiagnostics(events ?? []);

        bool incomingAccepted = false;
        _ = await ReadModelWritePolicy.UpdateAsync<WorksWhatsNextTenantIndex>(
            _store,
            WorksReadModelKeys.StateStoreName,
            useCurrentSchema
                ? WorksReadModelKeys.CurrentWhatsNextIndexKey(tenant.Value)
                : WorksReadModelKeys.WhatsNextIndexKey(tenant.Value),
            current =>
            {
                if (useCurrentSchema && !WorksWhatsNextTenantIndexValidation.IsValidCurrent(current))
                {
                    throw new InvalidOperationException("The current Works tenant manifest is missing or uses an unsupported schema.");
                }

                if (!useCurrentSchema && current is not null && !WorksWhatsNextTenantIndexValidation.IsUsableLegacy(current))
                {
                    throw new InvalidOperationException("The legacy Works tenant index is malformed.");
                }

                var items = current is null
                    ? new Dictionary<string, WhatsNextItem>(StringComparer.Ordinal)
                    : new Dictionary<string, WhatsNextItem>(current.Items, StringComparer.Ordinal);
                var lastSequences = current is null
                    ? new Dictionary<string, long>(StringComparer.Ordinal)
                    : new Dictionary<string, long>(current.LastSequences, StringComparer.Ordinal);
                var members = current is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(current.MemberWorkItemIds, StringComparer.Ordinal);

                // Additive-rollout compatibility: when this aggregate id has no LastSequences entry, an
                // eligible legacy item still carries ordering authority on its own accepted-source watermark.
                // The item is a fallback for a missing entry, never a competing maximum. The two watermarks are
                // produced by different projections whose accept filters can disagree on a delivery (the
                // what's-next projection accepts a ChildSpawned that the roll-up's identity registry refuses),
                // so a stored item can sit permanently ahead of the roll-up watermark this guard compares
                // against. Maximising over both would then refuse every later replay of that stream and freeze
                // the item's index entry and its notifications until an event both projections accept caught
                // the roll-up up.
                long storedLastSequence;
                if (lastSequences.TryGetValue(aggregateId, out long tombstoneSequence))
                {
                    storedLastSequence = tombstoneSequence;
                }
                else if (items.TryGetValue(aggregateId, out WhatsNextItem? storedItem))
                {
                    storedLastSequence = storedItem.LatestAcceptedSourceSequence;
                }
                else
                {
                    storedLastSequence = long.MinValue;
                }

                if (storedLastSequence > incomingLastSequence)
                {
                    incomingAccepted = false;
                    return new WorksWhatsNextTenantIndex
                    {
                        SchemaVersion = useCurrentSchema ? WorksReadModelKeys.CurrentSchemaVersion : 0,
                        Items = items,
                        LastSequences = lastSequences,
                        MemberWorkItemIds = [.. members.Order(StringComparer.Ordinal)],
                    };
                }

                incomingAccepted = true;
                _ = members.Add(aggregateId);
                lastSequences[aggregateId] = incomingLastSequence;
                if (item is not null)
                {
                    items[aggregateId] = item;
                }
                else
                {
                    _ = items.Remove(aggregateId);
                }

                return new WorksWhatsNextTenantIndex
                {
                    SchemaVersion = useCurrentSchema ? WorksReadModelKeys.CurrentSchemaVersion : 0,
                    Items = items,
                    LastSequences = lastSequences,
                    MemberWorkItemIds = [.. members.Order(StringComparer.Ordinal)],
                };
            },
            context,
            _logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return incomingAccepted;
    }

    private async Task<WorkItemRollUp> PersistRollUpAsync(
        TenantId tenant,
        WorkItemId workItemId,
        WorkItemRollUp model,
        IReadOnlyList<ProjectionEventDto>? events,
        bool useCurrentSchema,
        CancellationToken cancellationToken)
    {
        ReadModelWriteContext context = new ReadModelWriteContext(
            Category: "works work-item roll-up",
            ProjectionType: WorksReadModelKeys.WorkItemViewProjectionType)
            .WithEventDiagnostics(events ?? []);

        return await ReadModelWritePolicy.UpdateAsync<WorkItemRollUp>(
            _store,
            WorksReadModelKeys.StateStoreName,
            useCurrentSchema
                ? WorksReadModelKeys.CurrentRollUpKey(tenant.Value, workItemId.Value)
                : WorksReadModelKeys.RollUpKey(tenant.Value, workItemId.Value),
            current => current is not null && current.LatestAcceptedSourceSequence > model.LatestAcceptedSourceSequence
                ? current
                : model,
            context,
            _logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads this aggregate's persisted roll-up for the active generation, fail-closed on identity.</summary>
    private async Task<WorkItemRollUp?> ReadPersistedRollUpAsync(
        TenantId tenant,
        WorkItemId workItemId,
        bool useCurrentSchema,
        CancellationToken cancellationToken)
    {
        ReadModelEntry<WorkItemRollUp> entry = await _store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                useCurrentSchema
                    ? WorksReadModelKeys.CurrentRollUpKey(tenant.Value, workItemId.Value)
                    : WorksReadModelKeys.RollUpKey(tenant.Value, workItemId.Value),
                cancellationToken)
            .ConfigureAwait(false);
        WorkItemRollUp? persisted = entry.Value;
        return persisted is not null
            && string.Equals(persisted.TenantId?.Value, tenant.Value, StringComparison.Ordinal)
            && string.Equals(persisted.WorkItemId?.Value, workItemId.Value, StringComparison.Ordinal)
                ? persisted
                : null;
    }

    private async Task<bool> UseCurrentSchemaAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        ReadModelEntry<WorksWhatsNextTenantIndex> current = await _store
            .GetAsync<WorksWhatsNextTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.CurrentWhatsNextIndexKey(tenant.Value),
                cancellationToken)
            .ConfigureAwait(false);
        if (current.Value is not null)
        {
            if (!WorksWhatsNextTenantIndexValidation.IsValidCurrent(current.Value))
            {
                throw new InvalidOperationException("The current Works tenant manifest is missing required collections or uses an unsupported schema.");
            }

            return true;
        }

        return false;
    }

    private async Task MaintainPendingDateAwaitIndexAsync(
        TenantId tenant,
        string aggregateId,
        IReadOnlyList<(long Sequence, IEventPayload Payload)> decodedEvents,
        IReadOnlyList<ProjectionEventDto>? events,
        CancellationToken cancellationToken)
    {
        if (events is null || events.Count == 0)
        {
            return;
        }

        // Fold the full replayed stream (Story 4.8, DD-1/DD-3): the durable index records the aggregate's
        // *current* pending DateReached awaits, never a raw suspend event in isolation, so a stream that has
        // since resumed clears the entry. Reuse the same pure fold the recovery source uses — never a second one.
        IReadOnlyList<IEventPayload> ordered = [.. decodedEvents.OrderBy(static value => value.Sequence).Select(static value => value.Payload)];
        IReadOnlyList<PendingDateAwait> pending = PendingDateAwaitProjection.PendingDateAwaits(ordered);

        if (pending.Count > 0)
        {
            // Registry BEFORE index: a crash after the registry write but before the index write leaves a registered
            // tenant with an empty index (recovery pays one cheap empty read — safe). The reverse ordering could
            // strand index entries under a tenant recovery never enumerates. The registry is append-only.
            await EnsureTenantRegisteredAsync(tenant.Value, events, cancellationToken).ConfigureAwait(false);
        }

        long? incomingLastSequence = MaxSequence(events);
        if (incomingLastSequence is null)
        {
            return;
        }

        if (pending.Count == 0 && !await HasPendingDateAwaitHistoryAsync(tenant, aggregateId, cancellationToken).ConfigureAwait(false))
        {
            // This aggregate has never held a date await, so there is nothing to record or tombstone. Writing
            // anyway would make every /project dispatch in the tenant — not only date-await items — contend on
            // the one singleton index key, where ReadModelWritePolicy's bounded retry can exhaust and turn an
            // ordinary projection dispatch into a 500 plus a poller retry. It would also grow the document by
            // one permanent LastSequences entry per work item ever dispatched. Items that ever held an await
            // keep their tombstone: the guard below only skips aggregates the index has never heard of.
            return;
        }

        ReadModelWriteContext context = new ReadModelWriteContext(
            Category: "works pending-date-await index",
            ProjectionType: WorksReadModelKeys.WhatsNextProjectionType)
            .WithEventDiagnostics(events ?? []);

        _ = await ReadModelWritePolicy.UpdateAsync<PendingDateAwaitTenantIndex>(
            _store,
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitIndexKey(tenant.Value),
            current =>
            {
                // Build replacement dictionaries rather than mutating the store's instance: the update func
                // must be idempotent because ReadModelWritePolicy can run it again on every ETag retry, and an
                // in-place mutation of `current` would leak this attempt's edits into the next one's baseline.
                PendingDateAwaitTenantIndex index = current ?? new PendingDateAwaitTenantIndex();
                var lastSequences = new Dictionary<string, long>(index.LastSequences, StringComparer.Ordinal);
                var entries = new Dictionary<string, IReadOnlyList<PendingDateAwait>>(index.Entries, StringComparer.Ordinal);
                if (lastSequences.TryGetValue(aggregateId, out long storedLastSequence)
                    && storedLastSequence >= incomingLastSequence.Value)
                {
                    return new PendingDateAwaitTenantIndex { Entries = entries, LastSequences = lastSequences };
                }

                lastSequences[aggregateId] = incomingLastSequence.Value;
                if (pending.Count > 0)
                {
                    entries[aggregateId] = pending;
                }
                else
                {
                    _ = entries.Remove(aggregateId);
                }

                return new PendingDateAwaitTenantIndex { Entries = entries, LastSequences = lastSequences };
            },
            context,
            _logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records one consecutive failure to decode a state-affecting event at <paramref name="sequenceNumber"/> and
    /// returns whether this aggregate is now parked (and the dispatch must be acknowledged instead of retried).
    /// </summary>
    /// <remarks>
    /// The counter is keyed by the failing sequence, so it counts <em>consecutive failures on the same poisoned
    /// event</em>: a failure at a different sequence restarts the count, and a healthy aggregate never
    /// accumulates one. Nothing is read or written on the healthy dispatch path.
    /// </remarks>
    private async Task<bool> ParkOrRetryAsync(
        TenantId tenant,
        string aggregateId,
        long sequenceNumber,
        IReadOnlyList<ProjectionEventDto>? events,
        CancellationToken cancellationToken)
    {
        int maxFailures = _options.MaxUndecodableEventDispatchesBeforeParking;
        ReadModelWriteContext context = new ReadModelWriteContext(
            Category: "works projection parking",
            ProjectionType: WorksReadModelKeys.WhatsNextProjectionType)
            .WithEventDiagnostics(events ?? []);

        WorkItemProjectionParking parking = await ReadModelWritePolicy.UpdateAsync<WorkItemProjectionParking>(
            _store,
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.ProjectionParkingKey(tenant.Value, aggregateId),
            current =>
            {
                if (current is not null && current.FailedSequence == sequenceNumber && current.Parked)
                {
                    return current;
                }

                int failureCount = current is not null && current.FailedSequence == sequenceNumber
                    ? current.FailureCount + 1
                    : 1;
                return new WorkItemProjectionParking
                {
                    FailedSequence = sequenceNumber,
                    FailureCount = failureCount,
                    Parked = failureCount >= maxFailures,
                };
            },
            context,
            _logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!parking.Parked)
        {
            s_projectionDecodeFailed(_logger, aggregateId, tenant.Value, sequenceNumber, parking.FailureCount, maxFailures, null);
            return false;
        }

        if (parking.FailureCount == maxFailures)
        {
            s_projectionAggregateParked(_logger, aggregateId, tenant.Value, sequenceNumber, parking.FailureCount, null);
        }
        else
        {
            s_projectionParkedDispatchSkipped(_logger, aggregateId, tenant.Value, sequenceNumber, null);
        }

        return true;
    }

    /// <summary>Returns whether the tenant index already carries an entry or tombstone for this aggregate.</summary>
    private async Task<bool> HasPendingDateAwaitHistoryAsync(TenantId tenant, string aggregateId, CancellationToken cancellationToken)
    {
        ReadModelEntry<PendingDateAwaitTenantIndex> entry = await _store
            .GetAsync<PendingDateAwaitTenantIndex>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.PendingDateAwaitIndexKey(tenant.Value),
                cancellationToken)
            .ConfigureAwait(false);
        return entry.Value is { } index
            && (index.Entries.ContainsKey(aggregateId) || index.LastSequences.ContainsKey(aggregateId));
    }

    private async Task EnsureTenantRegisteredAsync(
        string tenantId,
        IReadOnlyList<ProjectionEventDto>? events,
        CancellationToken cancellationToken)
    {
        // Read first, write only on a genuine addition: most suspended-item dispatches are for a tenant already
        // in the registry, so the common path is a single cheap read with no write churn on the singleton doc.
        ReadModelEntry<PendingDateAwaitTenantRegistry> current = await _store
            .GetAsync<PendingDateAwaitTenantRegistry>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitRegistryKey, cancellationToken)
            .ConfigureAwait(false);
        if (current.Value is not null && current.Value.Tenants.Contains(tenantId))
        {
            return;
        }

        ReadModelWriteContext context = new ReadModelWriteContext(
            Category: "works pending-date-await tenant registry",
            ProjectionType: WorksReadModelKeys.WhatsNextProjectionType)
            .WithEventDiagnostics(events ?? []);

        _ = await ReadModelWritePolicy.UpdateAsync<PendingDateAwaitTenantRegistry>(
            _store,
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.PendingDateAwaitRegistryKey,
            existing =>
            {
                PendingDateAwaitTenantRegistry registry = existing ?? new PendingDateAwaitTenantRegistry();
                _ = registry.Tenants.Add(tenantId);
                return registry;
            },
            context,
            _logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Minimal state echoed back when a work item is not in the eligible "what's next" set.</summary>
    private sealed record WhatsNextProjectionState(bool Eligible);
}
