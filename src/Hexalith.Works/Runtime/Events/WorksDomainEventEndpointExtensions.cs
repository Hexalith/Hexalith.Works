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
