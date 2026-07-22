using Hexalith.EventStore.Client.Subscriptions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Runtime.Events;

/// <summary>
/// Maps the Works-local EventStore subscription endpoint that preserves the Works Web JSON wire contract.
/// </summary>
/// <remarks>
/// Intentionally carries no additional caller authentication, matching the EventStore SDK's own generic
/// <c>EventStoreDomainEventsEndpointExtensions.MapEventStoreDomainEvents</c> (this endpoint's exemplar): the
/// <c>dapr-caller-app-id</c> header used by <c>Authentication:DaprInternal:AllowedCallers</c> is attached only
/// to Dapr <em>service-invocation</em> (app-to-app RPC) requests, not <em>pub/sub delivery</em> callbacks like
/// this one, so that check does not apply here. The protection boundary for a pub/sub subscription endpoint is
/// network/deployment topology — only the local Dapr sidecar's loopback call reaches it — not an app-level
/// header check.
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
        }).WithTopic(options.PubSubName, options.TopicName);

        return endpoints;
    }

    private static IResult MapProcessingResult(EventStoreDomainEventProcessingResult result)
    {
        return result == EventStoreDomainEventProcessingResult.RetryableInProgress
            ? Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            : Results.Ok();
    }
}
