using System.Text.Json;

using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Runtime.Events;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Characterizes the EventStore subscription payload boundary and the Works-local case-insensitive JSON decoder.
/// </summary>
public class WorksDomainEventProcessorTests
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

    private sealed class DispatchThenCompletionFailsOnceMarkerStore : IEventStoreDomainEventMarkerStore
    {
        private EventStoreDomainEventMarkerAcquisitionResult _acquisition = EventStoreDomainEventMarkerAcquisitionResult.Acquired;

        public int CompletionCount { get; private set; }

        public int DispatchCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public Task<EventStoreDomainEventMarkerAcquisitionResult> TryAcquireAsync(
            string messageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_acquisition);

        public Task<bool> MarkDispatchedAsync(string messageId, CancellationToken cancellationToken = default)
        {
            DispatchCount++;
            _acquisition = EventStoreDomainEventMarkerAcquisitionResult.CompletionPending;
            return Task.FromResult(true);
        }

        public Task MarkCompletedAsync(string messageId, CancellationToken cancellationToken = default)
        {
            CompletionCount++;
            if (CompletionCount == 1)
            {
                throw new InvalidOperationException("synthetic first completion failure");
            }

            _acquisition = EventStoreDomainEventMarkerAcquisitionResult.Completed;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string messageId, CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CompletionPendingFailingMarkerStore : IEventStoreDomainEventMarkerStore
    {
        public int CompletionCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public Task<EventStoreDomainEventMarkerAcquisitionResult> TryAcquireAsync(
            string messageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EventStoreDomainEventMarkerAcquisitionResult.CompletionPending);

        public Task<bool> MarkDispatchedAsync(string messageId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("dispatch must not run");

        public Task MarkCompletedAsync(string messageId, CancellationToken cancellationToken = default)
        {
            CompletionCount++;
            throw new InvalidOperationException("synthetic persistent completion failure");
        }

        public Task ReleaseAsync(string messageId, CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class DispatchFailingMarkerStore : IEventStoreDomainEventMarkerStore
    {
        public int ReleaseCount { get; private set; }

        public Task<EventStoreDomainEventMarkerAcquisitionResult> TryAcquireAsync(
            string messageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EventStoreDomainEventMarkerAcquisitionResult.Acquired);

        public Task<bool> MarkDispatchedAsync(string messageId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("synthetic dispatch marker failure");

        public Task MarkCompletedAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ReleaseAsync(string messageId, CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<IReadOnlyDictionary<string, object?>> StructuredEntries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                StructuredEntries.Add(values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            }
        }
    }

    /// <summary>Proves the host's by-type singleton registration can activate the local processor.</summary>
    [Fact]
    public void Works_processor_is_activatable_by_the_default_service_provider()
    {
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton<IEventStoreDomainEventMarkerStore>(new InMemoryEventStoreDomainEventMarkerStore());
        registrations.AddSingleton<WorksDomainEventProcessor>();
        using ServiceProvider services = registrations.BuildServiceProvider();

        services.GetRequiredService<WorksDomainEventProcessor>().ShouldNotBeNull();
    }

    /// <summary>
    /// Proves the checked-out generic SDK processor accepts a camelCase Web compatibility payload.
    /// </summary>
    [Fact]
    public async Task GenericSdkProcessorBindsCamelCaseWebCompatibilityPayload()
    {
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        IEventStoreDomainEventHandler<WorkItemCancelled> handler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCancelled>>();
        WorkItemCancelled? decoded = null;
        handler
            .When(value => value.HandleAsync(
                Arg.Any<WorkItemCancelled>(),
                Arg.Any<EventStoreDomainEventContext>(),
                Arg.Any<CancellationToken>()))
            .Do(call => decoded = call.ArgAt<WorkItemCancelled>(0));
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => handler);
        using ServiceProvider services = registrations.BuildServiceProvider();
        var processor = new EventStoreDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                [typeof(WorkItemCancelled).FullName!] = typeof(WorkItemCancelled),
            },
            new InMemoryEventStoreDomainEventMarkerStore(),
            NullLogger<EventStoreDomainEventProcessor>.Instance);

        EventStoreDomainEventProcessingResult result = await processor.ProcessAsync(
            CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FAV", webCompatibility: true),
            TestContext.Current.CancellationToken);

        result.ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        decoded.ShouldNotBeNull();
        decoded.ShouldBe(@event);
    }

    /// <summary>
    /// Proves the Works-local processor accepts a camelCase Web compatibility payload and dispatches it once.
    /// </summary>
    [Fact]
    public async Task WorksProcessorBindsCamelCaseWebCompatibilityPayloadOnce()
    {
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        IEventStoreDomainEventHandler<WorkItemCancelled> handler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCancelled>>();
        WorkItemCancelled? decoded = null;
        handler
            .When(value => value.HandleAsync(
                Arg.Any<WorkItemCancelled>(),
                Arg.Any<EventStoreDomainEventContext>(),
                Arg.Any<CancellationToken>()))
            .Do(call => decoded = call.ArgAt<WorkItemCancelled>(0));
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => handler);
        using ServiceProvider services = registrations.BuildServiceProvider();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryEventStoreDomainEventMarkerStore(),
            NullLogger<WorksDomainEventProcessor>.Instance);

        EventStoreDomainEventProcessingResult result = await processor.ProcessAsync(
            CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB8", webCompatibility: true),
            TestContext.Current.CancellationToken);

        result.ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        decoded.ShouldBe(@event);
        await handler.Received(1).HandleAsync(
            Arg.Is<WorkItemCancelled>(value => value == @event),
            Arg.Any<EventStoreDomainEventContext>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Proves each consumed event binds from the options-free EventPersister form and reaches its handler.
    /// </summary>
    [Fact]
    public async Task WorksProcessorDispatchesEveryConsumedPersistedEventOnce()
    {
        IEventStoreDomainEventHandler<WorkItemCancelled> cancelledHandler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCancelled>>();
        IEventStoreDomainEventHandler<WorkItemExpired> expiredHandler = Substitute.For<IEventStoreDomainEventHandler<WorkItemExpired>>();
        IEventStoreDomainEventHandler<WorkItemCompleted> completedHandler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCompleted>>();
        IEventStoreDomainEventHandler<WorkItemSuspended> suspendedHandler = Substitute.For<IEventStoreDomainEventHandler<WorkItemSuspended>>();

        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => cancelledHandler);
        registrations.AddScoped(_ => expiredHandler);
        registrations.AddScoped(_ => completedHandler);
        registrations.AddScoped(_ => suspendedHandler);
        using ServiceProvider services = registrations.BuildServiceProvider();
        var markerStore = new InMemoryEventStoreDomainEventMarkerStore();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            markerStore,
            NullLogger<WorksDomainEventProcessor>.Instance);

        WorkItemCancelled cancelled = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        WorkItemExpired expired = WorkItemV1Catalog.All.OfType<WorkItemExpired>().Single();
        WorkItemCompleted completed = WorkItemV1Catalog.All.OfType<WorkItemCompleted>().Single();
        WorkItemSuspended suspended = WorkItemV1Catalog.All.OfType<WorkItemSuspended>().Single();

        (await processor.ProcessAsync(CreateEnvelope(cancelled, "01ARZ3NDEKTSV4RRFFQ69G5FAV"), TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        (await processor.ProcessAsync(CreateEnvelope(expired, "01ARZ3NDEKTSV4RRFFQ69G5FAW"), TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        EventStoreDomainEventEnvelope completedEnvelope = CreateEnvelope(completed, "01ARZ3NDEKTSV4RRFFQ69G5FAX");
        (await processor.ProcessAsync(completedEnvelope, TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        (await processor.ProcessAsync(completedEnvelope, TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
        (await processor.ProcessAsync(CreateEnvelope(suspended, "01ARZ3NDEKTSV4RRFFQ69G5FB4"), TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Processed);

        await cancelledHandler.Received(1).HandleAsync(
            Arg.Is<WorkItemCancelled>(value => value == cancelled),
            Arg.Any<EventStoreDomainEventContext>(),
            Arg.Any<CancellationToken>());
        await expiredHandler.Received(1).HandleAsync(
            Arg.Is<WorkItemExpired>(value => value == expired),
            Arg.Any<EventStoreDomainEventContext>(),
            Arg.Any<CancellationToken>());
        await completedHandler.Received(1).HandleAsync(
            Arg.Is<WorkItemCompleted>(value => value == completed),
            Arg.Any<EventStoreDomainEventContext>(),
            Arg.Any<CancellationToken>());
        await suspendedHandler.Received(1).HandleAsync(
            Arg.Is<WorkItemSuspended>(value => value == suspended),
            Arg.Any<EventStoreDomainEventContext>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Malformed known-event bytes are terminally acknowledged and cannot poison the retry loop.</summary>
    [Fact]
    public async Task Works_processor_acknowledges_undecodable_payload_and_marks_it_complete()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryEventStoreDomainEventMarkerStore(),
            NullLogger<WorksDomainEventProcessor>.Instance);
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        EventStoreDomainEventEnvelope envelope = CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FAY") with
        {
            Payload = "{"u8.ToArray(),
        };

        EventStoreDomainEventProcessingResult first = await processor.ProcessAsync(
            envelope,
            TestContext.Current.CancellationToken);
        EventStoreDomainEventProcessingResult duplicate = await processor.ProcessAsync(
            envelope,
            TestContext.Current.CancellationToken);

        first.ShouldBe(EventStoreDomainEventProcessingResult.FailedInvalidPayload);
        duplicate.ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
    }

    /// <summary>An envelope whose identity disagrees with its decoded event is terminally skipped, never dispatched.</summary>
    [Fact]
    public async Task Works_processor_skips_envelope_with_identity_mismatch()
    {
        IEventStoreDomainEventHandler<WorkItemCancelled> handler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCancelled>>();
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => handler);
        using ServiceProvider services = registrations.BuildServiceProvider();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryEventStoreDomainEventMarkerStore(),
            NullLogger<WorksDomainEventProcessor>.Instance);
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        EventStoreDomainEventEnvelope mismatched = CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB0") with
        {
            AggregateId = "work-item-unrelated",
        };

        EventStoreDomainEventProcessingResult result = await processor.ProcessAsync(
            mismatched,
            TestContext.Current.CancellationToken);

        result.ShouldBe(EventStoreDomainEventProcessingResult.SkippedAggregateMismatch);
        await handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default!, Arg.Any<CancellationToken>());
        (await processor.ProcessAsync(mismatched, TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
    }

    /// <summary>A marker already owned by another in-flight attempt yields a retryable result and no dispatch or completion.</summary>
    [Fact]
    public async Task Works_processor_returns_retryable_when_marker_in_progress()
    {
        IEventStoreDomainEventMarkerStore markerStore = Substitute.For<IEventStoreDomainEventMarkerStore>();
        markerStore
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EventStoreDomainEventMarkerAcquisitionResult.InProgress);
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            markerStore,
            NullLogger<WorksDomainEventProcessor>.Instance);
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();

        EventStoreDomainEventProcessingResult result = await processor.ProcessAsync(
            CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB1"),
            TestContext.Current.CancellationToken);

        result.ShouldBe(EventStoreDomainEventProcessingResult.RetryableInProgress);
        await markerStore.DidNotReceiveWithAnyArgs().MarkCompletedAsync(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>An envelope with an invalid message id is rejected before the marker is even acquired.</summary>
    [Fact]
    public async Task Works_processor_rejects_invalid_envelope_before_marker_acquisition()
    {
        IEventStoreDomainEventMarkerStore markerStore = Substitute.For<IEventStoreDomainEventMarkerStore>();
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            markerStore,
            NullLogger<WorksDomainEventProcessor>.Instance);
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        EventStoreDomainEventEnvelope invalid = CreateEnvelope(@event, "not-a-valid-unique-id");

        EventStoreDomainEventProcessingResult result = await processor.ProcessAsync(
            invalid,
            TestContext.Current.CancellationToken);

        result.ShouldBe(EventStoreDomainEventProcessingResult.FailedInvalidPayload);
        await markerStore.DidNotReceiveWithAnyArgs().TryAcquireAsync(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>A foreign or differently-cased domain is rejected before any marker access.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Work")]
    [InlineData("party")]
    public async Task Works_processor_rejects_non_exact_work_domain_before_marker_acquisition(string? domain)
    {
        IEventStoreDomainEventMarkerStore markerStore = Substitute.For<IEventStoreDomainEventMarkerStore>();
        IEventStoreDomainEventHandler<WorkItemCancelled> handler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCancelled>>();
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => handler);
        using ServiceProvider services = registrations.BuildServiceProvider();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            markerStore,
            NullLogger<WorksDomainEventProcessor>.Instance);
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        EventStoreDomainEventEnvelope envelope = CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB5") with
        {
            Domain = domain,
        };

        EventStoreDomainEventProcessingResult result = await processor.ProcessAsync(
            envelope,
            TestContext.Current.CancellationToken);

        result.ShouldBe(EventStoreDomainEventProcessingResult.FailedInvalidPayload);
        await markerStore.DidNotReceiveWithAnyArgs().TryAcquireAsync(default!, Arg.Any<CancellationToken>());
        await handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    /// <summary>A failed final completion redelivery completes only and never decodes or redispatches.</summary>
    [Fact]
    public async Task Works_processor_completion_failure_redelivery_completes_without_redispatch()
    {
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        IEventStoreDomainEventHandler<WorkItemCancelled> handler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCancelled>>();
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => handler);
        using ServiceProvider services = registrations.BuildServiceProvider();
        var markerStore = new DispatchThenCompletionFailsOnceMarkerStore();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            markerStore,
            NullLogger<WorksDomainEventProcessor>.Instance);
        EventStoreDomainEventEnvelope envelope = CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB6");

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => processor.ProcessAsync(envelope, TestContext.Current.CancellationToken));
        EventStoreDomainEventProcessingResult redelivery = await processor.ProcessAsync(
            envelope with
            {
                AggregateId = " ",
                EventTypeName = " ",
                SerializationFormat = " ",
                Payload = [],
            },
            TestContext.Current.CancellationToken);

        redelivery.ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
        await handler.Received(1).HandleAsync(
            Arg.Is<WorkItemCancelled>(value => value == @event),
            Arg.Any<EventStoreDomainEventContext>(),
            Arg.Any<CancellationToken>());
        markerStore.DispatchCount.ShouldBe(1);
        markerStore.CompletionCount.ShouldBe(2);
        markerStore.ReleaseCount.ShouldBe(0);
    }

    /// <summary>A persistent completion-only failure escapes and never falls back to dispatch or release.</summary>
    [Fact]
    public async Task Works_processor_completion_pending_persistent_failure_remains_retryable()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var markerStore = new CompletionPendingFailingMarkerStore();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            markerStore,
            NullLogger<WorksDomainEventProcessor>.Instance);
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        EventStoreDomainEventEnvelope envelope = CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB7") with
        {
            AggregateId = " ",
            EventTypeName = " ",
            SerializationFormat = " ",
            Payload = [],
        };

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => processor.ProcessAsync(envelope, TestContext.Current.CancellationToken));

        markerStore.CompletionCount.ShouldBe(1);
        markerStore.ReleaseCount.ShouldBe(0);
    }

    /// <summary>A failed durable dispatch marker is logged, escapes, and cannot release after handlers ran.</summary>
    [Fact]
    public async Task Works_processor_dispatch_marker_failure_escapes_without_release_and_logs_context()
    {
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        IEventStoreDomainEventHandler<WorkItemCancelled> handler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCancelled>>();
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => handler);
        using ServiceProvider services = registrations.BuildServiceProvider();
        var markerStore = new DispatchFailingMarkerStore();
        var logger = new CapturingLogger<WorksDomainEventProcessor>();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            markerStore,
            logger);

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => processor.ProcessAsync(
                CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB9"),
                TestContext.Current.CancellationToken));

        await handler.Received(1).HandleAsync(
            Arg.Is<WorkItemCancelled>(value => value == @event),
            Arg.Any<EventStoreDomainEventContext>(),
            Arg.Any<CancellationToken>());
        markerStore.ReleaseCount.ShouldBe(0);
        logger.StructuredEntries
            .Any(entry => entry.ContainsKey("ReasonCode")
                && Convert.ToString(entry["ReasonCode"])!.StartsWith("dispatch-", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    /// <summary>An envelope with a non-JSON serialization format is terminally acknowledged, not left to a retry loop.</summary>
    [Fact]
    public async Task Works_processor_terminally_acknowledges_unsupported_serialization_format()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryEventStoreDomainEventMarkerStore(),
            NullLogger<WorksDomainEventProcessor>.Instance);
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        EventStoreDomainEventEnvelope envelope = CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB2") with
        {
            SerializationFormat = "protobuf",
        };

        EventStoreDomainEventProcessingResult first = await processor.ProcessAsync(envelope, TestContext.Current.CancellationToken);
        EventStoreDomainEventProcessingResult duplicate = await processor.ProcessAsync(envelope, TestContext.Current.CancellationToken);

        first.ShouldBe(EventStoreDomainEventProcessingResult.FailedInvalidPayload);
        duplicate.ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);
    }

    /// <summary>A consumed event type with no registered handler is terminally acknowledged as skipped.</summary>
    [Fact]
    public async Task Works_processor_skips_consumed_event_with_no_registered_handler()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryEventStoreDomainEventMarkerStore(),
            NullLogger<WorksDomainEventProcessor>.Instance);
        WorkItemCancelled @event = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();

        EventStoreDomainEventProcessingResult result = await processor.ProcessAsync(
            CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FB3"),
            TestContext.Current.CancellationToken);

        result.ShouldBe(EventStoreDomainEventProcessingResult.SkippedNoHandlers);
    }

    private static EventStoreDomainEventEnvelope CreateEnvelope(
        IEventPayload @event,
        string messageId,
        bool webCompatibility = false)
    {
        (string aggregateId, string tenantId, long sequence) = @event switch
        {
            WorkItemCancelled value => (value.AggregateId, value.TenantId.Value, value.Sequence),
            WorkItemExpired value => (value.AggregateId, value.TenantId.Value, value.Sequence),
            WorkItemCompleted value => (value.AggregateId, value.TenantId.Value, value.Sequence),
            WorkItemSuspended value => (value.AggregateId, value.TenantId.Value, value.Sequence),
            _ => throw new ArgumentOutOfRangeException(nameof(@event)),
        };

        return new EventStoreDomainEventEnvelope(
            messageId,
            aggregateId,
            tenantId,
            @event.GetType().FullName!,
            sequence,
            new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero),
            $"story-4-7-{sequence}",
            "json",
            webCompatibility
                ? JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), s_web)
                : JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType()))
        {
            Domain = "work",
        };
    }
}
