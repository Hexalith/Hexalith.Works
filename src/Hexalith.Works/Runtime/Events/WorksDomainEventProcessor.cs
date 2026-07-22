using Hexalith.Commons.UniqueIds;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hexalith.Works.Runtime.Events;

/// <summary>
/// Processes Works domain-event envelopes with the Web JSON contract used by the Works event stream.
/// </summary>
internal sealed class WorksDomainEventProcessor
{
    private readonly ILogger<WorksDomainEventProcessor> _logger;
    private readonly IEventStoreDomainEventMarkerStore _markerStore;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    /// <summary>Initializes a new instance of the <see cref="WorksDomainEventProcessor"/> class.</summary>
    public WorksDomainEventProcessor(
        IServiceScopeFactory serviceScopeFactory,
        IEventStoreDomainEventMarkerStore markerStore,
        ILogger<WorksDomainEventProcessor> logger)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _markerStore = markerStore ?? throw new ArgumentNullException(nameof(markerStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Deduplicates, decodes, validates, and dispatches one published Works event.</summary>
    internal async Task<EventStoreDomainEventProcessingResult> ProcessAsync(
        EventStoreDomainEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!ValidateEnvelope(envelope))
        {
            WorksDomainEventLog.InvalidEnvelope(_logger, "invalid-envelope-metadata");
            return EventStoreDomainEventProcessingResult.FailedInvalidPayload;
        }

        EventStoreDomainEventMarkerAcquisitionResult acquisition = await _markerStore
            .TryAcquireAsync(envelope.MessageId, cancellationToken)
            .ConfigureAwait(false);
        if (acquisition == EventStoreDomainEventMarkerAcquisitionResult.Completed)
        {
            WorksDomainEventLog.Duplicate(
                _logger,
                envelope.EventTypeName,
                envelope.TenantId,
                envelope.AggregateId,
                envelope.CorrelationId);
            return EventStoreDomainEventProcessingResult.Duplicate;
        }

        if (acquisition != EventStoreDomainEventMarkerAcquisitionResult.Acquired)
        {
            return EventStoreDomainEventProcessingResult.RetryableInProgress;
        }

        bool releaseMarkerOnFailure = true;
        try
        {
            if (!string.Equals(envelope.SerializationFormat, "json", StringComparison.OrdinalIgnoreCase))
            {
                await CompleteSkippedAsync(envelope, "unsupported-serialization-format").ConfigureAwait(false);
                return EventStoreDomainEventProcessingResult.FailedInvalidPayload;
            }

            IEventPayload? @event = WorksEventDecoder.Decode(envelope.EventTypeName, envelope.Payload);
            if (@event is null)
            {
                await CompleteSkippedAsync(envelope, "undecodable-event").ConfigureAwait(false);
                return EventStoreDomainEventProcessingResult.FailedInvalidPayload;
            }

            if (!TryGetConsumedIdentity(@event, out string? tenantId, out string? workItemId, out string? aggregateId))
            {
                await CompleteSkippedAsync(envelope, "no-registered-handler").ConfigureAwait(false);
                return EventStoreDomainEventProcessingResult.SkippedNoHandlers;
            }

            // workItemId (the strongly-typed WorkItemId) and aggregateId (the record's raw AggregateId string)
            // are definitionally equal for every consumed event today, but are independent constructor
            // parameters at the type level — checking both is defense-in-depth against a future consumed event
            // whose AggregateId is not WorkItemId-keyed, not redundant copy-paste.
            if (!string.Equals(envelope.TenantId, tenantId, StringComparison.Ordinal)
                || !string.Equals(envelope.AggregateId, workItemId, StringComparison.Ordinal)
                || !string.Equals(envelope.AggregateId, aggregateId, StringComparison.Ordinal))
            {
                await CompleteSkippedAsync(envelope, "envelope-identity-mismatch").ConfigureAwait(false);
                return EventStoreDomainEventProcessingResult.SkippedAggregateMismatch;
            }

            var context = new EventStoreDomainEventContext(
                envelope.TenantId,
                envelope.AggregateId,
                envelope.MessageId,
                envelope.SequenceNumber,
                envelope.Timestamp,
                envelope.CorrelationId)
            {
                Domain = envelope.Domain,
                GlobalPosition = envelope.GlobalPosition,
                CausationId = envelope.CausationId,
                UserId = envelope.UserId,
            };

            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            int handlerCount = await DispatchAsync(scope.ServiceProvider, @event, context, cancellationToken).ConfigureAwait(false);
            if (handlerCount == 0)
            {
                await CompleteSkippedAsync(envelope, "no-registered-handler").ConfigureAwait(false);
                return EventStoreDomainEventProcessingResult.SkippedNoHandlers;
            }

            releaseMarkerOnFailure = false;
            await MarkCompletedSafelyAsync(envelope).ConfigureAwait(false);
            return EventStoreDomainEventProcessingResult.Processed;
        }
        catch
        {
            if (releaseMarkerOnFailure)
            {
                await ReleaseSafelyAsync(envelope).ConfigureAwait(false);
            }

            throw;
        }
    }

    // Single source of truth for which event types this processor consumes: both identity extraction and
    // dispatch key off this one table, keyed by concrete event type, so adding a consumed event type (e.g.
    // Story 4.8's WorkItemSuspended) only requires one new entry instead of two independently-maintained
    // switch statements that can silently drift out of sync.
    private static readonly IReadOnlyDictionary<Type, ConsumedEventDescriptor> s_consumedEvents =
        new Dictionary<Type, ConsumedEventDescriptor>
        {
            [typeof(WorkItemCancelled)] = ConsumedEventDescriptor.For<WorkItemCancelled>(
                value => (value.TenantId.Value, value.WorkItemId.Value, value.AggregateId)),
            [typeof(WorkItemExpired)] = ConsumedEventDescriptor.For<WorkItemExpired>(
                value => (value.TenantId.Value, value.WorkItemId.Value, value.AggregateId)),
            [typeof(WorkItemCompleted)] = ConsumedEventDescriptor.For<WorkItemCompleted>(
                value => (value.TenantId.Value, value.WorkItemId.Value, value.AggregateId)),
        };

    private static Task<int> DispatchAsync(
        IServiceProvider serviceProvider,
        IEventPayload @event,
        EventStoreDomainEventContext context,
        CancellationToken cancellationToken)
    {
        return s_consumedEvents.TryGetValue(@event.GetType(), out ConsumedEventDescriptor? descriptor)
            ? descriptor.DispatchAsync(serviceProvider, @event, context, cancellationToken)
            : Task.FromResult(0);
    }

    private static async Task<int> DispatchAsync<TEvent>(
        IServiceProvider serviceProvider,
        TEvent @event,
        EventStoreDomainEventContext context,
        CancellationToken cancellationToken)
        where TEvent : IEventPayload
    {
        IEventStoreDomainEventHandler<TEvent>[] handlers = serviceProvider
            .GetServices<IEventStoreDomainEventHandler<TEvent>>()
            .ToArray();
        foreach (IEventStoreDomainEventHandler<TEvent> handler in handlers)
        {
            await handler.HandleAsync(@event, context, cancellationToken).ConfigureAwait(false);
        }

        return handlers.Length;
    }

    private static bool TryGetConsumedIdentity(
        IEventPayload @event,
        out string? tenantId,
        out string? workItemId,
        out string? aggregateId)
    {
        if (s_consumedEvents.TryGetValue(@event.GetType(), out ConsumedEventDescriptor? descriptor))
        {
            (tenantId, workItemId, aggregateId) = descriptor.GetIdentity(@event);
            return true;
        }

        (tenantId, workItemId, aggregateId) = (null, null, null);
        return false;
    }

    /// <summary>Binds one consumed event type's identity extraction and handler dispatch together.</summary>
    private sealed class ConsumedEventDescriptor
    {
        private readonly Func<IEventPayload, (string TenantId, string WorkItemId, string AggregateId)> _identity;
        private readonly Func<IServiceProvider, IEventPayload, EventStoreDomainEventContext, CancellationToken, Task<int>> _dispatch;

        private ConsumedEventDescriptor(
            Func<IEventPayload, (string TenantId, string WorkItemId, string AggregateId)> identity,
            Func<IServiceProvider, IEventPayload, EventStoreDomainEventContext, CancellationToken, Task<int>> dispatch)
        {
            _identity = identity;
            _dispatch = dispatch;
        }

        public static ConsumedEventDescriptor For<TEvent>(Func<TEvent, (string TenantId, string WorkItemId, string AggregateId)> identity)
            where TEvent : IEventPayload
            => new(
                @event => identity((TEvent)@event),
                (serviceProvider, @event, context, cancellationToken) =>
                    WorksDomainEventProcessor.DispatchAsync(serviceProvider, (TEvent)@event, context, cancellationToken));

        public (string TenantId, string WorkItemId, string AggregateId) GetIdentity(IEventPayload @event) => _identity(@event);

        public Task<int> DispatchAsync(
            IServiceProvider serviceProvider,
            IEventPayload @event,
            EventStoreDomainEventContext context,
            CancellationToken cancellationToken)
            => _dispatch(serviceProvider, @event, context, cancellationToken);
    }

    private static bool ValidateEnvelope(EventStoreDomainEventEnvelope envelope)
    {
        return !string.IsNullOrWhiteSpace(envelope.MessageId)
            && IsValidUniqueId(envelope.MessageId)
            && !string.IsNullOrWhiteSpace(envelope.AggregateId)
            && !string.IsNullOrWhiteSpace(envelope.TenantId)
            && !string.IsNullOrWhiteSpace(envelope.EventTypeName)
            && !string.IsNullOrWhiteSpace(envelope.CorrelationId)
            && !string.IsNullOrWhiteSpace(envelope.SerializationFormat)
            && envelope.Payload is { Length: > 0 };
    }

    private static bool IsValidUniqueId(string value)
    {
        try
        {
            _ = UniqueIdHelper.ToGuid(value);
            return true;
        }
        catch (Exception)
        {
            // This is a pure syntactic-validity check: any failure to parse the id, regardless of exception
            // type, means the id is invalid. A narrower type filter here would be fragile against unhandled
            // exception types escaping this validation and defeating poison-message protection.
            return false;
        }
    }

    private async Task CompleteSkippedAsync(EventStoreDomainEventEnvelope envelope, string reasonCode)
    {
        WorksDomainEventLog.Skipped(
            _logger,
            envelope.EventTypeName,
            envelope.TenantId,
            envelope.AggregateId,
            envelope.CorrelationId,
            reasonCode);
        await MarkCompletedSafelyAsync(envelope).ConfigureAwait(false);
    }

    private async Task MarkCompletedSafelyAsync(EventStoreDomainEventEnvelope envelope)
    {
        try
        {
            await _markerStore.MarkCompletedAsync(envelope.MessageId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WorksDomainEventLog.MarkerFailure(
                _logger,
                envelope.EventTypeName,
                envelope.TenantId,
                envelope.AggregateId,
                envelope.CorrelationId,
                $"complete-{exception.GetType().Name}");
        }
    }

    private async Task ReleaseSafelyAsync(EventStoreDomainEventEnvelope envelope)
    {
        try
        {
            await _markerStore.ReleaseAsync(envelope.MessageId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WorksDomainEventLog.MarkerFailure(
                _logger,
                envelope.EventTypeName,
                envelope.TenantId,
                envelope.AggregateId,
                envelope.CorrelationId,
                $"release-{exception.GetType().Name}");
        }
    }
}
