using System.Net;

using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Regresses how the Story 4.6 recovery runtime reaches the EventStore command gateway. The topology proof used
/// to assert this by searching <c>WorksRecoveryExtensions.cs</c> for <c>DAPR_HTTP_ENDPOINT</c> and
/// <c>AddEventStoreDaprServiceInvocation</c>; these tests observe the composed <see cref="HttpClient"/> instead,
/// so a comment or a broken call site cannot satisfy them.
/// </summary>
public sealed class WorksRecoveryGatewayRoutingTests
{
    private const string GatewayClientName = "IEventStoreGatewayClient";

    /// <summary>The sidecar endpoint wins over the direct address and carries the EventStore routing headers.</summary>
    [Fact]
    public async Task RecoveryGatewayClientRoutesThroughTheSidecarWithTheEventStoreAppId()
    {
        var capturing = new CapturingHandler();
        using ServiceProvider provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["DAPR_HTTP_ENDPOINT"] = "http://127.0.0.1:3500",
                ["DAPR_API_TOKEN"] = "gateway-token",
                ["EventStore:CommandGateway:BaseAddress"] = "http://eventstore-direct",
            },
            capturing);

        using HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GatewayClientName);
        client.BaseAddress.ShouldBe(new Uri("http://127.0.0.1:3500"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands");
        using HttpResponseMessage response = await client
            .SendAsync(request, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        Dictionary<string, string[]> sent = capturing.Headers.ShouldNotBeNull();
        sent["dapr-app-id"].ShouldBe(["eventstore"]);
        sent["dapr-api-token"].ShouldBe(["gateway-token"]);
    }

    /// <summary>The composed AppHost sets no API token, so the sidecar route must work without one.</summary>
    [Fact]
    public async Task RecoveryGatewayClientRoutesThroughTheSidecarWithoutAnApiToken()
    {
        var capturing = new CapturingHandler();
        using ServiceProvider provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["DAPR_HTTP_ENDPOINT"] = "http://127.0.0.1:3500",
                ["EventStore:CommandGateway:BaseAddress"] = "http://eventstore-direct",
            },
            capturing);

        using HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GatewayClientName);
        client.BaseAddress.ShouldBe(new Uri("http://127.0.0.1:3500"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands");
        using HttpResponseMessage response = await client
            .SendAsync(request, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        Dictionary<string, string[]> sent = capturing.Headers.ShouldNotBeNull();
        sent["dapr-app-id"].ShouldBe(["eventstore"]);
        sent.ShouldNotContainKey("dapr-api-token");
    }

    /// <summary>Without a sidecar the client falls back to the configured direct address and adds no Dapr headers.</summary>
    [Fact]
    public async Task RecoveryGatewayClientFallsBackToTheDirectAddressWithoutASidecar()
    {
        var capturing = new CapturingHandler();
        using ServiceProvider provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["EventStore:CommandGateway:BaseAddress"] = "http://eventstore-direct",
            },
            capturing);

        using HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GatewayClientName);
        client.BaseAddress.ShouldBe(new Uri("http://eventstore-direct"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/commands");
        using HttpResponseMessage response = await client
            .SendAsync(request, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        capturing.Headers.ShouldNotBeNull().ShouldNotContainKey("dapr-app-id");
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings, CapturingHandler capturing)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddWorksReminderAndCascadeRecovery(configuration);

        // Terminate the gateway client's handler chain locally so the Dapr routing handler still runs but no
        // socket is opened. AddHttpClient with the same name appends to the existing named configuration.
        _ = services.AddHttpClient(GatewayClientName).ConfigurePrimaryHttpMessageHandler(() => capturing);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        // Snapshot rather than retain: the HttpRequestMessage the assertions would otherwise read through is
        // disposed by the caller before they run, and IHttpClientFactory owns this handler's lifetime.
        internal Dictionary<string, string[]>? Headers { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Headers = request.Headers.ToDictionary(
                static header => header.Key,
                static header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }
    }
}
