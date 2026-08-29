using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

using Dapr;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.EventStore.DomainService;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Runtime;
using Hexalith.Works.Runtime.Events;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Regresses the runnable Works host's route table and Dapr subscription discovery document.
/// </summary>
public sealed class WorksDomainEventSubscriptionTests
{
    /// <summary>Verifies the pinned exhaustive outcome mapping fails closed for future values.</summary>
    [Theory]
    [InlineData(EventStoreDomainEventProcessingResult.Processed, HttpStatusCode.OK)]
    [InlineData(EventStoreDomainEventProcessingResult.Duplicate, HttpStatusCode.OK)]
    [InlineData(EventStoreDomainEventProcessingResult.SkippedUnknownEventType, HttpStatusCode.OK)]
    [InlineData(EventStoreDomainEventProcessingResult.SkippedNoHandlers, HttpStatusCode.OK)]
    [InlineData(EventStoreDomainEventProcessingResult.SkippedAggregateMismatch, HttpStatusCode.OK)]
    [InlineData(EventStoreDomainEventProcessingResult.FailedInvalidPayload, HttpStatusCode.OK)]
    [InlineData(EventStoreDomainEventProcessingResult.RetryableInProgress, HttpStatusCode.InternalServerError)]
    [InlineData((EventStoreDomainEventProcessingResult)int.MaxValue, HttpStatusCode.InternalServerError)]
    public void ProcessingResultMappingIsExhaustiveAndFutureValuesFailClosed(
        EventStoreDomainEventProcessingResult result,
        HttpStatusCode expectedStatus)
    {
        IResult mapped = WorksDomainEventEndpointExtensions.MapProcessingResult(result);

        mapped.ShouldBeAssignableTo<IStatusCodeHttpResult>()
            .StatusCode.ShouldBe((int)expectedStatus);
    }

    /// <summary>Verifies the host-owned delivery route and SDK-owned discovery route are each unique.</summary>
    [Fact]
    public async Task WorksHostExposesOneDeliveryRouteAndOneDiscoveryRoute()
    {
        string keyDirectory = Directory.CreateTempSubdirectory("works-subscription-keys").FullName;
        IEventStoreDomainEventMarkerStore markerStore = Substitute.For<IEventStoreDomainEventMarkerStore>();
        markerStore
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EventStoreDomainEventMarkerAcquisitionResult.InProgress);
        try
        {
            await using WebApplication app = WorksHost.Build(
                ["--Works:Recovery:RunReconciliationOnStartup=false"],
                static webHost => webHost.UseUrls("http://127.0.0.1:0"),
                services =>
                {
                    services.RemoveAll<IEventStoreDomainEventMarkerStore>();
                    _ = services.AddSingleton(markerStore);
                    ServiceDescriptor cascadeRecoveryService = services.Single(static descriptor =>
                        descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType == typeof(CascadeRecoveryService));
                    _ = services.Remove(cascadeRecoveryService);

                    // Keep the real host's Data Protection keys out of the developer's profile
                    // (~/.aspnet/DataProtection-Keys) — this test only exercises routing.
                    _ = services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
                });
            await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            string address = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()
                .ShouldNotBeNull()
                .Addresses
                .ShouldHaveSingleItem();
            using var client = new HttpClient { BaseAddress = new Uri(address, UriKind.Absolute) };

            RouteEndpoint delivery = Route(app, "/work/events", HttpMethods.Post);
            Route(app, "dapr/subscribe", HttpMethods.Get);

            // Mapping the host's routes before UseEventStoreDomainService relies on the SDK yielding an
            // already-mapped route. Prove that yielding leaves exactly one of each and does not suppress the
            // canonical endpoints the SDK still owns.
            Route(app, "/project", HttpMethods.Post).Metadata.GetMetadata<ITopicMetadata>().ShouldBeNull();
            Route(app, "/project/rebuild/shared/v1", HttpMethods.Post);
            Route(app, "/process", HttpMethods.Post);
            Route(app, "/query", HttpMethods.Post);
            Route(app, "/replay-state", HttpMethods.Post);

            using (IServiceScope scope = app.Services.CreateScope())
            {
                scope.ServiceProvider
                    .GetServices<IAsyncDomainProjectionHandler>()
                    .OfType<WorkItemSharedProjectionRebuildHandler>()
                    .ShouldHaveSingleItem();
                ReadModelBatchOptions batchOptions = scope.ServiceProvider
                    .GetRequiredService<IOptions<ReadModelBatchOptions>>()
                    .Value;
                batchOptions.MaxOperations.ShouldBe(
                    (ProjectionDispatchOptions.DefaultMaxSharedRebuildAggregateCount * 2) + 2);
                batchOptions.MaxCanonicalManifestBytes.ShouldBe(
                    4 * ReadModelBatchOptions.DefaultMaxCanonicalManifestBytes);
            }

            ITopicMetadata topic = delivery.Metadata.GetMetadata<ITopicMetadata>().ShouldNotBeNull();
            topic.PubsubName.ShouldBe("pubsub");
            topic.Name.ShouldBe("work.events");
            delivery.Metadata.GetMetadata<IDeadLetterTopicMetadata>()
                .ShouldNotBeNull()
                .DeadLetterTopic.ShouldBe("deadletter.work.events");

            MethodInfo handler = delivery.Metadata.GetMetadata<MethodInfo>().ShouldNotBeNull();
            handler.GetParameters()
                .Select(static parameter => parameter.ParameterType)
                .ShouldContain(typeof(WorksDomainEventProcessor));

            using HttpResponseMessage response = await client
                .GetAsync("/dapr/subscribe", TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            string json = await response.Content
                .ReadAsStringAsync(TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement subscription = document.RootElement.EnumerateArray().ShouldHaveSingleItem();
            subscription.GetProperty("pubsubName").GetString().ShouldBe("pubsub");
            subscription.GetProperty("topic").GetString().ShouldBe("work.events");
            subscription.GetProperty("route").GetString().ShouldBe("work/events");
            subscription.GetProperty("deadLetterTopic").GetString().ShouldBe("deadletter.work.events");

            using HttpResponseMessage bindableInvalid = await client
                .PostAsync(
                    "/work/events",
                    JsonContent(CreateEnvelope("not-a-valid-message-id")),
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            bindableInvalid.StatusCode.ShouldBe(HttpStatusCode.OK);
            await markerStore.DidNotReceiveWithAnyArgs()
                .TryAcquireAsync(default!, TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            using HttpResponseMessage malformed = await client
                .PostAsync(
                    "/work/events",
                    new StringContent("{", Encoding.UTF8, "application/json"),
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            malformed.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

            string cloudEvent = JsonSerializer.Serialize(new
            {
                specversion = "1.0",
                id = "01ARZ3NDEKTSV4RRFFQ69G5FB4",
                source = "/eventstore/work",
                type = "work.event",
                datacontenttype = "application/json",
                data = CreateEnvelope("01ARZ3NDEKTSV4RRFFQ69G5FB4"),
            });
            using HttpResponseMessage retryable = await client
                .PostAsync(
                    "/work/events",
                    new StringContent(cloudEvent, Encoding.UTF8, "application/cloudevents+json"),
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            retryable.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

            await app.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        finally
        {
            // A failing assertion must not leak the Data Protection key directory into the temp volume.
            Directory.Delete(keyDirectory, recursive: true);
        }
    }

    private static RouteEndpoint Route(WebApplication app, string pattern, string method)
    {
        RouteEndpoint[] matches =
        [
            .. app.Services
                .GetServices<EndpointDataSource>()
                .SelectMany(static source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(endpoint => string.Equals(
                    endpoint.RoutePattern.RawText?.TrimStart('/'),
                    pattern.TrimStart('/'),
                    StringComparison.OrdinalIgnoreCase))
                .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() is not { } methods
                    || methods.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase)),
        ];
        return matches.ShouldHaveSingleItem($"Expected exactly one {method} route for '{pattern}'.");
    }

    private static EventStoreDomainEventEnvelope CreateEnvelope(string messageId)
        => new(
            messageId,
            "work-item-1",
            "tenant-1",
            "Unknown.Event",
            1,
            new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero),
            "subscription-test",
            "json",
            "{}"u8.ToArray())
        {
            Domain = "work",
        };

    private static StringContent JsonContent<T>(T value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
