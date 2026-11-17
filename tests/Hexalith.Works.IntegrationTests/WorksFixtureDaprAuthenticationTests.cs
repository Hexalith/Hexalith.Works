using System.Net;
using System.Security.Cryptography;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Dapr;
using Dapr.Client;

using Grpc.Core;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises selected-resource credentials on the actual HTTP and gRPC client transports.</summary>
public sealed class WorksFixtureDaprAuthenticationTests
{
    [Fact]
    public async Task SelectedResourceCredentialAuthenticatesBothTransportsWithoutTrustingMaskedSnapshotsAsync()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string credential = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        WebApplication http = await ServerAsync(credential, grpc: false, token).ConfigureAwait(true);
        await using var httpLifetime = http.ConfigureAwait(true);
        WebApplication grpc = await ServerAsync(credential, grpc: true, token).ConfigureAwait(true);
        await using var grpcLifetime = grpc.ConfigureAwait(true);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var owner = builder.AddResource(new ProjectResource("eventstore"));
        var secret = builder.AddParameter("owner-sidecar-token", () => credential, secret: true);
        owner.WithEnvironment("DAPR_API_TOKEN", secret);
        var foreign = builder.AddResource(new ProjectResource("foreign"))
            .WithEnvironment("DAPR_API_TOKEN", "foreign-credential");
        var tokenless = builder.AddResource(new ProjectResource("tokenless"));
        foreach (var selectedResource in new[] { owner, foreign, tokenless })
            selectedResource.WithEnvironment("DAPR_HTTP_ENDPOINT", http.Urls.Single()).WithEnvironment("DAPR_GRPC_ENDPOINT", grpc.Urls.Single());
        using var app = builder.Build();
        ResourceEvent Snapshot(ProjectResource resource) => new(resource, resource.Name, new CustomResourceSnapshot
        {
            ResourceType = "project", Properties = [], EnvironmentVariables = [
                new("DAPR_HTTP_ENDPOINT", "http://127.0.0.1:9", true), new("DAPR_GRPC_ENDPOINT", "http://127.0.0.1:10", true),
                new("DAPR_API_TOKEN", "[masked]", true)],
        });
        ResourceEvent selected = Snapshot(owner.Resource);
        var unselected = new ResourceEvent(new ProjectResource("eventstore"), "eventstore", selected.Snapshot);
        await Should.ThrowAsync<InvalidOperationException>(() => WorksAppHostTestReadiness.CreateDaprClientAsync(app, unselected, "Other", token)).ConfigureAwait(true);
        await Should.ThrowAsync<InvalidOperationException>(() => WorksAppHostTestReadiness.CreateSdkDaprClientAsync(app, unselected, token)).ConfigureAwait(true);
        using HttpClient client = await WorksAppHostTestReadiness.CreateDaprClientAsync(app, selected, "Owner", token).ConfigureAwait(true);
        using (HttpResponseMessage response = await client.GetAsync("/v1.0/metadata", token).ConfigureAwait(true))
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var invalid = new HttpRequestMessage(HttpMethod.Get, "/v1.0/metadata"))
        {
            invalid.Headers.Add("dapr-api-token", "incorrect-credential");
            using HttpResponseMessage response = await client.SendAsync(invalid, token).ConfigureAwait(true);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        await Should.ThrowAsync<InvalidOperationException>(() => client.GetAsync("http://127.0.0.1:9/v1.0/metadata", token)).ConfigureAwait(true);
        using DaprClient sdk = await WorksAppHostTestReadiness.CreateSdkDaprClientAsync(app, selected, token).ConfigureAwait(true);
        await sdk.SaveStateAsync("statestore", "owned", "proof", cancellationToken: token).ConfigureAwait(true);
        foreach (ProjectResource refused in new[] { foreign.Resource, tokenless.Resource })
        {
            ResourceEvent resource = Snapshot(refused);
            using HttpClient wrongHttp = await WorksAppHostTestReadiness.CreateDaprClientAsync(app, resource, "Other", token).ConfigureAwait(true);
            using HttpResponseMessage denied = await wrongHttp.GetAsync("/v1.0/metadata", token).ConfigureAwait(true);
            denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            using DaprClient wrongSdk = await WorksAppHostTestReadiness.CreateSdkDaprClientAsync(app, resource, token).ConfigureAwait(true);
            DaprException failure = await Should.ThrowAsync<DaprException>(() => wrongSdk.SaveStateAsync("statestore", "owned", "proof", cancellationToken: token)).ConfigureAwait(true);
            failure.InnerException.ShouldBeOfType<RpcException>().StatusCode.ShouldBe(StatusCode.Unauthenticated);
        }
    }

    [Fact]
    public async Task FailedCredentialResolutionRefusesBothFactoriesAsync()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var owner = builder.AddResource(new ProjectResource("eventstore"))
            .WithEnvironment(context => context.EnvironmentVariables["DAPR_API_TOKEN"] = new BrokenCredential());
        using var app = builder.Build();
        var resource = new ResourceEvent(owner.Resource, owner.Resource.Name, new CustomResourceSnapshot
        {
            ResourceType = "project", Properties = [], EnvironmentVariables = [
                new("DAPR_HTTP_ENDPOINT", "http://127.0.0.1:9", true), new("DAPR_GRPC_ENDPOINT", "http://127.0.0.1:10", true)],
        });
        await Should.ThrowAsync<InvalidOperationException>(() => WorksAppHostTestReadiness.CreateDaprClientAsync(app, resource, "Owner", TestContext.Current.CancellationToken)).ConfigureAwait(true);
        await Should.ThrowAsync<InvalidOperationException>(() => WorksAppHostTestReadiness.CreateSdkDaprClientAsync(app, resource, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }



    [Fact]
    public async Task TokenlessSdkRefusesValidAmbientCredentialInAnIsolatedProcessAsync()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string credential = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        WebApplication http = await ServerAsync(credential, grpc: false, token).ConfigureAwait(true);
        await using var httpLifetime = http.ConfigureAwait(true);
        WebApplication grpc = await ServerAsync(credential, grpc: true, token).ConfigureAwait(true);
        await using var grpcLifetime = grpc.ConfigureAwait(true);
        using WorksIsolatedFixtureProcess fixture = await WorksIsolatedFixtureProcess.CreateAsync(token).ConfigureAwait(true);
        var start = fixture.StartInfo("ambient", http.Urls.Single(), grpc.Urls.Single());
        start.Environment["DAPR_API_TOKEN"] = credential;
        var result = await WorksIsolatedFixtureProcess.RunAsync(start, token).ConfigureAwait(true);
        result.ExitCode.ShouldBe(0, result.Output);
        result.Output.ShouldContain("ambient-token-refused");
        result.Output.ShouldNotContain(credential);
    }

    [Fact]
    public async Task SidecarRedirectIsReturnedWithoutSendingAnyRequestToItsDestinationAsync()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        int destinationRequests = 0;
        var destinationBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        destinationBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication destination = destinationBuilder.Build();
        await using var destinationLifetime = destination.ConfigureAwait(true);
        destination.Run(context => { Interlocked.Increment(ref destinationRequests); return Task.CompletedTask; });
        await destination.StartAsync(token).ConfigureAwait(true);
        var sourceBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        sourceBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication source = sourceBuilder.Build();
        await using var sourceLifetime = source.ConfigureAwait(true);
        string target = destination.Urls.Single() + "/destination";
        source.Run(context => { context.Response.Redirect(target); return Task.CompletedTask; });
        await source.StartAsync(token).ConfigureAwait(true);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var owner = builder.AddResource(new ProjectResource("eventstore"))
            .WithEnvironment("DAPR_HTTP_ENDPOINT", source.Urls.Single()).WithEnvironment("DAPR_API_TOKEN", "owned-token");
        using var app = builder.Build();
        var resource = new ResourceEvent(owner.Resource, owner.Resource.Name, new CustomResourceSnapshot { ResourceType = "project", Properties = [] });
        using HttpClient client = await WorksAppHostTestReadiness.CreateDaprClientAsync(app, resource, "Owner", token).ConfigureAwait(true);
        using HttpResponseMessage response = await client.GetAsync("/redirect", token).ConfigureAwait(true);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsoluteUri.ShouldBe(target);
        Volatile.Read(ref destinationRequests).ShouldBe(0);
    }

    private static async Task<WebApplication> ServerAsync(string credential, bool grpc, CancellationToken token)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
            endpoint => endpoint.Protocols = grpc ? HttpProtocols.Http2 : HttpProtocols.Http1));
        var app = builder.Build();
        app.Run(async context =>
        {
            bool authorized = context.Request.Headers["dapr-api-token"] == credential;
            if (grpc)
            {
                await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted).ConfigureAwait(false);
                context.Response.ContentType = "application/grpc";
                if (authorized)
                {
                    context.Response.DeclareTrailer("grpc-status");
                    await context.Response.Body.WriteAsync(new byte[5], context.RequestAborted).ConfigureAwait(false);
                    context.Response.AppendTrailer("grpc-status", "0");
                }
                else context.Response.Headers["grpc-status"] = "16";
            }
            else context.Response.StatusCode = authorized ? StatusCodes.Status200OK : StatusCodes.Status401Unauthorized;
        });
        await app.StartAsync(token).ConfigureAwait(false);
        return app;
    }
}
