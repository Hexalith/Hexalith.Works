using System.Net.Http.Headers;

using Hexalith.EventStore.Client.Registration;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.Parties.Client;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Extensions;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Runtime;

/// <summary>Declares Works contracts, folds, query authority, reminder intents and process translations.</summary>
internal static class WorksDomainAdapters
{
    /// <summary>Registers domain adapters for the canonical SDK runtime.</summary>
    internal static IServiceCollection AddWorksDomainAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        HexalithWorksContractsSerialization.RegisterPolymorphicMappers();
        _ = services.AddHexalithWorksContractsPolymorphicMappers();
        _ = services.AddEventStoreReadModelStore(static options =>
        {
            options.MaxOperations = WorksRebuildOperationBound.MaxOperations;
            options.MaxCanonicalManifestBytes = 8 * Hexalith.EventStore.Client.Projections.ReadModelBatchOptions.DefaultMaxCanonicalManifestBytes;
        });
        _ = services.AddOptions<WorksProjectionOptions>()
            .Bind(configuration.GetSection(WorksProjectionOptions.SectionName))
            .Validate(static options => options.UseSdkDelivery, "Works requires SDK projection delivery.")
            .Validate(static options => options.MaxUndecodableEventDispatchesBeforeParking > 0,
                "MaxUndecodableEventDispatchesBeforeParking must be positive.")
            .ValidateOnStart();
        _ = services.AddOptions<WorksQueryOptions>()
            .Bind(configuration.GetSection(WorksQueryOptions.SectionName))
            .Validate(static options => !string.IsNullOrWhiteSpace(options.GatewayWorkloadId), "GatewayWorkloadId is required.")
            .Validate(options => Uri.TryCreate(PartiesGatewayAddress(options, configuration), UriKind.Absolute, out _),
                "Works requires an absolute Parties gateway address in Works:Query:PartiesGatewayBaseAddress or EventStore:CommandGateway:BaseAddress.")
            .ValidateOnStart();
        // Retain Works' gateway-only query boundary by strengthening the SDK's own policy.
        // The SDK still owns channel authentication, operation assertions and endpoint routing.
        _ = services.AddOptions<AuthorizationOptions>().PostConfigure<IOptions<WorksQueryOptions>>((authorization, query) =>
        {
            AuthorizationPolicy existing = authorization.GetPolicy(EventStoreDomainServicePolicies.Query)
                ?? throw new InvalidOperationException("The canonical SDK query policy is required.");
            authorization.AddPolicy(EventStoreDomainServicePolicies.Query, new AuthorizationPolicyBuilder(existing)
                .RequireClaim(EventStoreWorkloadAuthenticationDefaults.WorkloadClaimType, query.Value.GatewayWorkloadId).Build());
        });
        _ = services.AddTransient<WorksPartiesGatewayAuthorizationHandler>();
        _ = services.AddHttpClient<IPartiesIdentityClient, HttpPartiesIdentityClient>((provider, client) =>
        {
            WorksQueryOptions options = provider.GetRequiredService<IOptions<WorksQueryOptions>>().Value;
            client.BaseAddress = new Uri(PartiesGatewayAddress(options, configuration)!, UriKind.Absolute);
            if (!string.IsNullOrWhiteSpace(options.PartiesGatewayBearerToken))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.PartiesGatewayBearerToken);
        }).AddHttpMessageHandler<WorksPartiesGatewayAuthorizationHandler>();
        _ = services.AddScoped<WorksQueryPolicy>();
        _ = services.AddEventStoreDomainEvents(typeof(WorkItemCreated).Assembly, static options =>
        {
            options.TopicName = "work.events";
            options.SubscriptionRoute = "/work/events";
        });
        _ = services.AddDaprEventStoreDomainEventMarkerStore();
        _ = services.AddEventStoreDomainEventHandler<WorkItemCreated, WorksRecoveryWakeupHandler<WorkItemCreated>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemAssigned, WorksRecoveryWakeupHandler<WorkItemAssigned>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemQueued, WorksRecoveryWakeupHandler<WorkItemQueued>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemClaimed, WorksRecoveryWakeupHandler<WorkItemClaimed>>();
        _ = services.AddEventStoreDomainEventHandler<ProgressReported, WorksRecoveryWakeupHandler<ProgressReported>>();
        _ = services.AddEventStoreDomainEventHandler<ReEstimated, WorksRecoveryWakeupHandler<ReEstimated>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemRescheduled, WorksRecoveryWakeupHandler<WorkItemRescheduled>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemSuspended, WorksRecoveryWakeupHandler<WorkItemSuspended>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemResumed, WorksRecoveryWakeupHandler<WorkItemResumed>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemCompleted, WorksRecoveryWakeupHandler<WorkItemCompleted>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemCancelled, WorksRecoveryWakeupHandler<WorkItemCancelled>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemExpired, WorksRecoveryWakeupHandler<WorkItemExpired>>();
        _ = services.AddEventStoreDomainEventHandler<WorkItemRejected, WorksRecoveryWakeupHandler<WorkItemRejected>>();
        _ = services.AddEventStoreDomainEventHandler<ChildSpawned, WorksRecoveryWakeupHandler<ChildSpawned>>();
        _ = services.AddEventStoreDomainEventHandler<ConversationLinked, WorksRecoveryWakeupHandler<ConversationLinked>>();
        _ = services.AddWorksReminderAndCascadeRecovery(configuration);
        return services;
    }

    // A blank Works override is absent, matching the recovery gateway's handling of the same command-gateway key.
    private static string? PartiesGatewayAddress(WorksQueryOptions options, IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(options.PartiesGatewayBaseAddress) ? options.PartiesGatewayBaseAddress
            : !string.IsNullOrWhiteSpace(configuration["EventStore:CommandGateway:BaseAddress"]) ? configuration["EventStore:CommandGateway:BaseAddress"]
            : null;
}
