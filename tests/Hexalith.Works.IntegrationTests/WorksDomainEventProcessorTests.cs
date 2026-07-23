using System.Text.Json;

using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Runtime.Events;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Characterizes the EventStore subscription payload boundary and the Works-local Web JSON decoder.
/// </summary>
public class WorksDomainEventProcessorTests
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

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
    /// Proves the checked-out generic SDK processor silently misbinds the camel-case Works wire payload.
    /// </summary>
    [Fact]
    public async Task Generic_sdk_processor_silently_misbinds_real_web_json_works_payload()
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
            CreateEnvelope(@event, "01ARZ3NDEKTSV4RRFFQ69G5FAV"),
            TestContext.Current.CancellationToken);

        result.ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        decoded.ShouldNotBeNull();
        decoded.ShouldNotBe(@event);
    }

    /// <summary>
    /// Proves each event consumed by Story 4.7 binds from its real Web JSON catalog form and reaches its handler.
    /// </summary>
    [Fact]
    public async Task Works_processor_dispatches_every_consumed_web_json_event_once()
    {
        IEventStoreDomainEventHandler<WorkItemCancelled> cancelledHandler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCancelled>>();
        IEventStoreDomainEventHandler<WorkItemExpired> expiredHandler = Substitute.For<IEventStoreDomainEventHandler<WorkItemExpired>>();
        IEventStoreDomainEventHandler<WorkItemCompleted> completedHandler = Substitute.For<IEventStoreDomainEventHandler<WorkItemCompleted>>();

        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => cancelledHandler);
        registrations.AddScoped(_ => expiredHandler);
        registrations.AddScoped(_ => completedHandler);
        using ServiceProvider services = registrations.BuildServiceProvider();
        var markerStore = new InMemoryEventStoreDomainEventMarkerStore();
        var processor = new WorksDomainEventProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            markerStore,
            NullLogger<WorksDomainEventProcessor>.Instance);

        WorkItemCancelled cancelled = WorkItemV1Catalog.All.OfType<WorkItemCancelled>().Single();
        WorkItemExpired expired = WorkItemV1Catalog.All.OfType<WorkItemExpired>().Single();
        WorkItemCompleted completed = WorkItemV1Catalog.All.OfType<WorkItemCompleted>().Single();

        (await processor.ProcessAsync(CreateEnvelope(cancelled, "01ARZ3NDEKTSV4RRFFQ69G5FAV"), TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        (await processor.ProcessAsync(CreateEnvelope(expired, "01ARZ3NDEKTSV4RRFFQ69G5FAW"), TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        EventStoreDomainEventEnvelope completedEnvelope = CreateEnvelope(completed, "01ARZ3NDEKTSV4RRFFQ69G5FAX");
        (await processor.ProcessAsync(completedEnvelope, TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Processed);
        (await processor.ProcessAsync(completedEnvelope, TestContext.Current.CancellationToken))
            .ShouldBe(EventStoreDomainEventProcessingResult.Duplicate);

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

    private static EventStoreDomainEventEnvelope CreateEnvelope(IEventPayload @event, string messageId)
    {
        (string aggregateId, string tenantId, long sequence) = @event switch
        {
            WorkItemCancelled value => (value.AggregateId, value.TenantId.Value, value.Sequence),
            WorkItemExpired value => (value.AggregateId, value.TenantId.Value, value.Sequence),
            WorkItemCompleted value => (value.AggregateId, value.TenantId.Value, value.Sequence),
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
            JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), s_web))
        {
            Domain = "work",
        };
    }
}
