using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Streams;

using Hexalith.Works.Runtime;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NSubstitute;

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

    /// <summary>Authority mode acquires configured credentials and exercises the real client calls on ordinary protected routes.</summary>
    [Fact]
    public async Task RecoveryAuthorityModeAcquiresBearerForRealCommandAndStreamCalls()
    {
        var capture = new CapturingHandler();
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DAPR_HTTP_ENDPOINT"] = "http://127.0.0.1:3500",
            ["EventStore:Authentication:Authority"] = "https://authority.example/realms/hexalith",
            ["EventStore:Authentication:TokenEndpoint"] = "https://authority.example/realms/hexalith/protocol/openid-connect/token",
            ["EventStore:Authentication:GrantType"] = "password",
            ["EventStore:Authentication:ClientId"] = "hexalith-eventstore",
            ["EventStore:Authentication:Username"] = "realm-user",
            ["EventStore:Authentication:Password"] = "realm-secret",
        }, capture, new TokenHandler());
        IEventStoreGatewayClient client = provider.GetRequiredService<IEventStoreGatewayClient>();
        _ = await client.SubmitCommandAsync(new SubmitCommandRequest("message", "tenant-a", "work", "item", "ResumeWorkItem",
            JsonSerializer.SerializeToElement(new { })), TestContext.Current.CancellationToken);
        _ = await client.ReadStreamAsync(new StreamReadRequest("tenant-a", "work", "item"), TestContext.Current.CancellationToken);
        capture.Paths.ShouldBe(["/api/v1/commands", "/api/v1/streams/read"]);
        capture.Headers.ShouldNotBeNull()["Authorization"].ShouldBe(["Bearer authority-token"]);
        capture.Headers["dapr-app-id"].ShouldBe(["eventstore"]);
        capture.Headers.ShouldNotContainKey("X-Hexalith-Workload-Assertion");
    }

    /// <summary>Without an authority the workload transport signs every real command and stream call.</summary>
    [Fact]
    public async Task RecoveryWorkloadModeSignsRealCommandAndStreamCalls()
    {
        var capture = new CapturingHandler();
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DAPR_HTTP_ENDPOINT"] = "http://127.0.0.1:3500",
            ["Authentication:WorkloadIssuer:Workload"] = "works",
            ["Authentication:JwtBearer:Issuer"] = "works-routing-test-issuer",
            ["Authentication:JwtBearer:Audience"] = "hexalith-eventstore",
            ["Authentication:JwtBearer:SigningKey"] = "works-routing-test-signing-key-with-32-bytes-minimum",
            ["Authentication:JwtBearer:AllowedAlgorithms:0"] = "HS256",
        }, capture);
        IEventStoreGatewayClient client = provider.GetRequiredService<IEventStoreGatewayClient>();
        _ = await client.SubmitCommandAsync(new SubmitCommandRequest("message", "tenant-a", "work", "item", "ResumeWorkItem",
            JsonSerializer.SerializeToElement(new { })), TestContext.Current.CancellationToken);
        string[] commandAssertion = capture.Headers.ShouldNotBeNull()["X-Hexalith-Workload-Assertion"];
        _ = await client.ReadStreamAsync(new StreamReadRequest("tenant-a", "work", "item"), TestContext.Current.CancellationToken);
        string[] streamAssertion = capture.Headers["X-Hexalith-Workload-Assertion"];
        commandAssertion.Single().ShouldNotBeNullOrWhiteSpace();
        streamAssertion.Single().ShouldNotBeNullOrWhiteSpace();
        streamAssertion.ShouldNotBe(commandAssertion);
        capture.Headers.ShouldNotContainKey("Authorization");
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings, CapturingHandler capturing, HttpMessageHandler? tokenHandler = null)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        // The workload assertion issuer's symmetric signing mode is admitted outside Production only.
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        _ = services.AddSingleton(environment);
        _ = services.AddSingleton<IConfiguration>(configuration);
        _ = services.AddWorksReminderAndCascadeRecovery(configuration);

        // Terminate the gateway client's handler chain locally so the Dapr routing handler still runs but no
        // socket is opened. AddHttpClient with the same name appends to the existing named configuration.
        _ = services.AddHttpClient(GatewayClientName).ConfigurePrimaryHttpMessageHandler(() => capturing);
        if (tokenHandler is not null)
            _ = services.AddHttpClient("EventStoreGatewayAuthorityToken").ConfigurePrimaryHttpMessageHandler(() => tokenHandler);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        // Snapshot rather than retain: the HttpRequestMessage the assertions would otherwise read through is
        // disposed by the caller before they run, and IHttpClientFactory owns this handler's lifetime.
        internal Dictionary<string, string[]>? Headers { get; private set; }
        internal List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Paths.Add(request.RequestUri!.AbsolutePath);
            Headers = request.Headers.ToDictionary(
                static header => header.Key,
                static header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = Paths[^1].Contains("commands", StringComparison.Ordinal)
                    ? JsonContent.Create(new SubmitCommandResponse("correlation", MessageId: "message"))
                    : JsonContent.Create(new StreamReadPage("tenant-a", "work", "item", [], new(0, null, null, 0, 0, false, null))),
            });
        }
    }
    private sealed class TokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { access_token = "authority-token", token_type = "Bearer", expires_in = 300 }) });
    }
}
