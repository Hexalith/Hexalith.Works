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
/// <para>Events are decoded from their persisted concrete form (<see cref="JsonSerializerDefaults.Web"/>, no
/// polymorphic <c>$type</c> discriminator — the byte-frozen golden-corpus form) keyed by
/// <see cref="ProjectionEventDto.EventTypeName"/>, mirroring the EventStore Counter/Tenants projection handlers.</para>
/// <para>Reconciliation limitation (documented in <c>docs/eventstore-api-surface-constraints.md</c>): the
/// EventStore <c>/project</c> contract delivers one aggregate's event stream per call, so the cross-aggregate
/// "rolled remaining" contribution from sibling/child work items is not available within a single dispatch.
/// Each item's own remaining effort is composed; cross-aggregate roll-up convergence is exercised only when the
/// full Aspire runtime replays every aggregate.</para>
/// <para>Logging is bounded to metadata (tenant id, work-item id, event-type names, projection type, counts) —
/// never event payloads, obligations, secrets, tokens, or full command bodies (AC #4 / NFR-6).</para>
/// </remarks>
public sealed class WorkItemProjectionDispatcher
{
    private static readonly JsonSerializerOptions s_webOptions = new(JsonSerializerDefaults.Web);

    // The persisted Works event catalog keyed by simple type name. Built once from the Contracts assembly so a
    // ProjectionEventDto's EventTypeName (short or fully qualified) maps to the concrete event type for decoding.
    private static readonly IReadOnlyDictionary<string, Type> s_eventTypesByName = typeof(WorkItemCreated).Assembly
        .GetTypes()
        .Where(type => type is { IsAbstract: false } && typeof(IEventPayload).IsAssignableFrom(type))
        .ToDictionary(type => type.Name, StringComparer.Ordinal);

    private static readonly Action<ILogger, string, string, string, int, bool, Exception?> s_projected =
        LoggerMessage.Define<string, string, string, int, bool>(
            LogLevel.Information,
            new EventId(4500, "Projected"),
            "Projected work item {WorkItemId} for tenant {TenantId} (correlation {CorrelationId}) from {EventCount} events; whatsNextChanged={Changed}.");

    private static readonly Action<ILogger, string, string, string, string, Exception?> s_skippedEvent =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Warning,
            new EventId(4501, "SkippedEvent"),
            "Skipped undecodable projection event {EventType} for work item {WorkItemId} (tenant {TenantId}, correlation {CorrelationId}).");

    private readonly IReadModelStore _store;
    private readonly IProjectionChangeNotifier? _notifier;
    private readonly ILogger<WorkItemProjectionDispatcher> _logger;

    /// <summary>Initializes a new instance of the <see cref="WorkItemProjectionDispatcher"/> class.</summary>
    /// <param name="store">The persisted read-model store.</param>
    /// <param name="notifier">The projection-change notifier, or <see langword="null"/> when none is wired.</param>
    /// <param name="logger">The bounded-metadata logger.</param>
    public WorkItemProjectionDispatcher(
        IReadModelStore store,
        IProjectionChangeNotifier? notifier,
        ILogger<WorkItemProjectionDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _notifier = notifier;
        _logger = logger;
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

        var tenant = new TenantId(request.TenantId);
        var workItemId = new WorkItemId(request.AggregateId);
        string correlationId = CorrelationIdOf(request.Events);

        var whatsNext = new WhatsNextQueueProjection();
        var rollUp = new WorkItemRollUpProjection();
        var decodedEvents = new List<(long Sequence, IEventPayload Payload)>();
        bool changed = false;
        int decoded = 0;

        foreach (ProjectionEventDto? dto in request.Events ?? [])
        {
            if (dto is null)
            {
                continue;
            }

            IEventPayload? payload = Decode(dto, tenant, workItemId, correlationId);
            if (payload is null)
            {
                continue;
            }

            var delivery = new WorkItemRollUpEvent(tenant, workItemId, dto.SequenceNumber, payload);
            rollUp.Project(delivery);
            changed |= whatsNext.Project(delivery).Changed;
            decodedEvents.Add((dto.SequenceNumber, payload));
            decoded++;
        }

        WhatsNextItem? item = whatsNext
            .WhatsNext(tenant, rollUp.Get)
            .FirstOrDefault(candidate => string.Equals(candidate.WorkItemId.Value, request.AggregateId, StringComparison.Ordinal));

        await UpsertTenantIndexAsync(tenant, request.AggregateId, item, request.Events, cancellationToken).ConfigureAwait(false);
        await PersistRollUpAsync(tenant, workItemId, rollUp, cancellationToken).ConfigureAwait(false);
        await MaintainPendingDateAwaitIndexAsync(tenant, request.AggregateId, decodedEvents, request.Events, cancellationToken).ConfigureAwait(false);

        if (changed && _notifier is not null)
        {
            await _notifier
                .NotifyProjectionChangedAsync(WorksReadModelKeys.WhatsNextProjectionType, tenant.Value, entityId: null, cancellationToken)
                .ConfigureAwait(false);
        }

        s_projected(_logger, request.AggregateId, tenant.Value, correlationId, decoded, changed, null);

        JsonElement state = item is not null
            ? JsonSerializer.SerializeToElement(item, s_webOptions)
            : JsonSerializer.SerializeToElement(new WhatsNextProjectionState(false), s_webOptions);

        return new ProjectionResponse(WorksReadModelKeys.WhatsNextProjectionType, state);
    }

    private async Task UpsertTenantIndexAsync(
        TenantId tenant,
        string aggregateId,
        WhatsNextItem? item,
        IReadOnlyList<ProjectionEventDto>? events,
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

        _ = await ReadModelWritePolicy.UpdateAsync<WorksWhatsNextTenantIndex>(
            _store,
            WorksReadModelKeys.StateStoreName,
            WorksReadModelKeys.WhatsNextIndexKey(tenant.Value),
            current =>
            {
                WorksWhatsNextTenantIndex index = current ?? new WorksWhatsNextTenantIndex();
                if (item is not null)
                {
                    index.Items[aggregateId] = item;
                }
                else
                {
                    _ = index.Items.Remove(aggregateId);
                }

                return index;
            },
            context,
            _logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistRollUpAsync(
        TenantId tenant,
        WorkItemId workItemId,
        WorkItemRollUpProjection rollUp,
        CancellationToken cancellationToken)
    {
        WorkItemRollUp? model = rollUp.Get(tenant, workItemId);
        if (model is not null)
        {
            await _store
                .SaveAsync(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.RollUpKey(tenant.Value, workItemId.Value), model, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task MaintainPendingDateAwaitIndexAsync(
        TenantId tenant,
        string aggregateId,
        IReadOnlyList<(long Sequence, IEventPayload Payload)> decodedEvents,
        IReadOnlyList<ProjectionEventDto>? events,
        CancellationToken cancellationToken)
    {
        // Fold the full replayed stream (Story 4.8, DD-1/DD-3): the durable index records the aggregate's
        // *current* pending DateReached awaits, never a raw suspend event in isolation, so a stream that has
        // since resumed clears the entry. Reuse the same pure fold the recovery source uses — never a second one.
        IReadOnlyList<IEventPayload> ordered = [.. decodedEvents.OrderBy(static value => value.Sequence).Select(static value => value.Payload)];
        IReadOnlyList<PendingDateAwait> pending = PendingDateAwaitProjection.PendingDateAwaits(ordered);

        if (pending.Count == 0)
        {
            // Date awaits are sparse but /project fires for every aggregate dispatch: only touch the index when
            // this aggregate actually has an entry to clear, so the common no-date-await dispatch does one cheap
            // read and no write (never creating an empty index document for a tenant that never used a date await).
            ReadModelEntry<PendingDateAwaitTenantIndex> existing = await _store
                .GetAsync<PendingDateAwaitTenantIndex>(WorksReadModelKeys.StateStoreName, WorksReadModelKeys.PendingDateAwaitIndexKey(tenant.Value), cancellationToken)
                .ConfigureAwait(false);
            if (existing.Value is null || !existing.Value.Entries.ContainsKey(aggregateId))
            {
                return;
            }
        }
        else
        {
            // Registry BEFORE index: a crash after the registry write but before the index write leaves a registered
            // tenant with an empty index (recovery pays one cheap empty read — safe). The reverse ordering could
            // strand index entries under a tenant recovery never enumerates. The registry is append-only.
            await EnsureTenantRegisteredAsync(tenant.Value, events, cancellationToken).ConfigureAwait(false);
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
                PendingDateAwaitTenantIndex index = current ?? new PendingDateAwaitTenantIndex();
                if (pending.Count > 0)
                {
                    index.Entries[aggregateId] = pending;
                }
                else
                {
                    _ = index.Entries.Remove(aggregateId);
                }

                return index;
            },
            context,
            _logger,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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

    private IEventPayload? Decode(ProjectionEventDto dto, TenantId tenant, WorkItemId workItemId, string correlationId)
    {
        string simpleName = SimpleTypeName(dto.EventTypeName);
        if (!s_eventTypesByName.TryGetValue(simpleName, out Type? eventType))
        {
            s_skippedEvent(_logger, dto.EventTypeName, workItemId.Value, tenant.Value, correlationId, null);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(dto.Payload, eventType, s_webOptions) as IEventPayload;
        }
        catch (JsonException)
        {
            s_skippedEvent(_logger, dto.EventTypeName, workItemId.Value, tenant.Value, correlationId, null);
            return null;
        }
    }

    private static string CorrelationIdOf(IReadOnlyList<ProjectionEventDto>? events)
        => events?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e?.CorrelationId))?.CorrelationId ?? string.Empty;

    private static string SimpleTypeName(string eventTypeName)
    {
        if (string.IsNullOrEmpty(eventTypeName))
        {
            return eventTypeName;
        }

        int lastDot = eventTypeName.LastIndexOf('.');
        return lastDot >= 0 ? eventTypeName[(lastDot + 1)..] : eventTypeName;
    }

    /// <summary>Minimal state echoed back when a work item is not in the eligible "what's next" set.</summary>
    private sealed record WhatsNextProjectionState(bool Eligible);
}
