using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.Works.Queries;
using Hexalith.Works.Runtime;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises the real SDK-mode HTTP authentication pipeline.</summary>
public sealed class WorksSdkProtectedHttpTests
{
    private const string AppId = "works-sdk-http-test";
    private const string Issuer = "works-sdk-http-issuer";
    private const string ChannelToken = "works-sdk-http-channel-token";
    private const string SigningKey = "works-sdk-http-signing-key-with-32-bytes-minimum";

    [Fact]
    public async Task Query_and_named_projection_require_channel_and_exact_operation_assertions()
    {
        await using WebApplication app = WorksHost.Build(
            [
                "--environment=Development",
                "--Logging:LogLevel:Default=Error",
                "--Works:Projection:UseSdkDelivery=true",
                "--Works:Query:PartiesGatewayBaseAddress=http://127.0.0.1:8999",
                "--Works:Recovery:RunReconciliationOnStartup=false",
                $"--{DaprAppChannelToken.ConfigurationKey}={ChannelToken}",
                $"--EventStore:DomainService:AppId={AppId}",
                $"--Authentication:JwtBearer:Issuer={Issuer}",
                "--Authentication:JwtBearer:Audience=hexalith-eventstore",
                $"--Authentication:JwtBearer:SigningKey={SigningKey}",
                $"--Authentication:JwtBearer:AllowedAlgorithms:0={SecurityAlgorithms.HmacSha256}",
                "--Authentication:JwtBearer:RequireHttpsMetadata=false",
            ], webHost => webHost.UseUrls("http://127.0.0.1:0"));
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        string address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>().ShouldNotBeNull().Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        QueryEnvelope query = new("tenant-sdk", "work", "work-sdk", WhatsNextQueryHandler.WhatsNextQueryType,
            [], "corr-http", "untrusted-user", null, false, null, null, "eventstore", false, null, null);

        foreach (string route in new[] { "/query", "/project/v2" })
        {
            using HttpResponseMessage missing = await client.SendAsync(Request(route, query),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            missing.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            using HttpResponseMessage channelOnly = await client.SendAsync(Request(route, query, channel: true),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            channelOnly.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            using HttpResponseMessage wrongOperation = await client.SendAsync(Request(route, query,
                channel: true, assertion: Assertion("domain-service:process")),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            wrongOperation.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using HttpResponseMessage validQuery = await client.SendAsync(Request("/query", query,
            channel: true, assertion: Assertion("domain-service:query")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        validQuery.StatusCode.ShouldBe(HttpStatusCode.OK);
        QueryResult? denied = await validQuery.Content.ReadFromJsonAsync<QueryResult>(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        denied.ShouldNotBeNull().Success.ShouldBeFalse();
        denied.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);

        using HttpResponseMessage validNamed = await client.SendAsync(Request("/project/v2", query,
            channel: true, assertion: Assertion("domain-service:project")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        validNamed.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        validNamed.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);

        using HttpResponseMessage legacyConflict = await client.SendAsync(Request("/project", query,
            channel: true, assertion: Assertion("domain-service:project")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        legacyConflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using HttpResponseMessage foreignOrigin = await client.SendAsync(Request("/query", query,
            channel: true, assertion: Assertion("domain-service:query", "other-caller")),
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        foreignOrigin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static HttpRequestMessage Request(string route, QueryEnvelope query, bool channel = false, string? assertion = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(query, options: JsonSerializerOptions.Web),
        };
        if (channel)
        {
            request.Headers.Add(DaprAppChannelToken.HeaderName, ChannelToken);
        }

        if (assertion is not null)
        {
            request.Headers.Add(EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName, assertion);
        }

        return request;
    }

    private static string Assertion(string operation, string caller = "eventstore")
    {
        DateTime now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = AppId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(2),
            Claims = new Dictionary<string, object>
            {
                [EventStoreWorkloadAuthenticationDefaults.CallerClaimType] = caller,
                [EventStoreWorkloadAuthenticationDefaults.OperationClaimType] = new[] { operation },
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
