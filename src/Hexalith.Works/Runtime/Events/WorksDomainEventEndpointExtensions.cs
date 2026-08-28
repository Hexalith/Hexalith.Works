using Hexalith.EventStore.Client.Subscriptions;

using Dapr;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Runtime.Events;

/// <summary>
/// Maps the Works-local EventStore subscription endpoint with case-insensitive Works payload binding.
/// </summary>
/// <remarks>
/// Intentionally carries no additional caller authentication, matching the EventStore SDK's own generic
/// <c>EventStoreDomainEventsEndpointExtensions.MapEventStoreDomainEvents</c> (this endpoint's exemplar): the
/// <c>dapr-caller-app-id</c> header used by <c>Authentication:DaprInternal:AllowedCallers</c> is attached only
/// to Dapr <em>service-invocation</em> (app-to-app RPC) requests, not <em>pub/sub delivery</em> callbacks like
/// this one, so that check does not apply here. The protection boundary for a pub/sub subscription endpoint is
/// network/deployment topology, not an app-level header check.
/// <para>
/// This route is no longer reached by pub/sub delivery alone: the <c>eventstore-operations</c> workload replays
/// a captured dead letter to it through Dapr <em>service invocation</em>. That caller is admitted by the Dapr
/// access-control policy in <c>accesscontrol.works.yaml</c>, which denies by default and grants
/// <c>eventstore-operations</c> exactly <c>POST /work/events</c> — so the boundary is still topology, but it is
/// now the sidecar's caller policy rather than loopback alone, and it must stay deny-by-default for that to hold.
/// </para>
/// </remarks>
internal static class WorksDomainEventEndpointExtensions
{
    /// <summary>Maps the configured Dapr pub/sub subscription to the Works event processor.</summary>
    internal static IEndpointRouteBuilder MapWorksDomainEvents(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        EventStoreDomainEventsOptions options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<EventStoreDomainEventsOptions>>()
            .Value;
        _ = endpoints.MapPost(options.SubscriptionRoute, async (
            EventStoreDomainEventEnvelope envelope,
            WorksDomainEventProcessor processor,
            CancellationToken cancellationToken) =>
        {
            EventStoreDomainEventProcessingResult result = await processor
                .ProcessAsync(envelope, cancellationToken)
                .ConfigureAwait(false);
            return MapProcessingResult(result);
        }).WithTopic(new TopicOptions
        {
            PubsubName = options.PubSubName,
            Name = options.TopicName,
            // Derived from the subscribed topic so the poison destination cannot drift away from the topic it
            // drains. With the host's configured "work.events" this resolves to "deadletter.work.events".
            DeadLetterTopic = DeadLetterTopicName(options.TopicName),
        });

        return endpoints;
    }

    /// <summary>Builds the poison destination for a subscribed topic.</summary>
    private static string DeadLetterTopicName(string topicName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        return "deadletter." + topicName;
    }

    /// <summary>Maps every pinned processing outcome and fails retryably for future values.</summary>
    internal static IResult MapProcessingResult(EventStoreDomainEventProcessingResult result)
        => result switch
        {
            EventStoreDomainEventProcessingResult.Processed => Results.Ok(),
            EventStoreDomainEventProcessingResult.Duplicate => Results.Ok(),
            EventStoreDomainEventProcessingResult.SkippedUnknownEventType => Results.Ok(),
            EventStoreDomainEventProcessingResult.SkippedNoHandlers => Results.Ok(),
            EventStoreDomainEventProcessingResult.SkippedAggregateMismatch => Results.Ok(),
            EventStoreDomainEventProcessingResult.FailedInvalidPayload => Results.Ok(),
            EventStoreDomainEventProcessingResult.RetryableInProgress => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
}
