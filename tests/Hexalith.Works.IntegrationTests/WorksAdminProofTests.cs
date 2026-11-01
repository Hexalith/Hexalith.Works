using System.Net;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Extensions.Hosting;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Finite Admin requests must retain exact source and sender ownership before any real dispatch.</summary>
public sealed class WorksAdminProofTests
{
    [Theory]
    [InlineData("unknown-kind")]
    [InlineData("foreign-source")]
    [InlineData("foreign-session")]
    [InlineData("stale")]
    [InlineData("foreign-role")]
    [InlineData("foreign-host")]
    [InlineData("cancelled")]
    public async Task InvalidRequestsRefuseBeforeAuthorityIssuerOrHttpWork(string fault)
    {
        string session = Guid.NewGuid().ToString("N"), nonce = Guid.NewGuid().ToString("N");
        var source = new ProcessSource("fixture", "work", "owned");
        var clients = Substitute.For<IHttpClientFactory>();
        var issuer = Substitute.For<IWorkloadAssertionIssuer>();
        var authority = Substitute.For<IRouteCatalogAuthority>();
        using var dapr = new DaprClientBuilder().UseHttpEndpoint("http://127.0.0.1:9").Build();
        var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns(Environments.Development);
        using var service = new WorksAdminProofService(clients, dapr, authority, issuer, Configuration(session, source, fault), environment);
        var request = new WorksAdminProofRequest(nonce, fault == "foreign-session" ? "foreign" : session,
            fault == "unknown-kind" ? "http://foreign/process" : "accepted", fault == "foreign-source" ? source with { Tenant = "foreign" } : source,
            DateTimeOffset.UtcNow.AddSeconds(fault == "stale" ? -1 : 30));
        if (fault == "cancelled")
            await Should.ThrowAsync<OperationCanceledException>(() => service.ExecuteRequestAsync(request, nonce, session, new CancellationToken(true))).ConfigureAwait(true);
        else
            await Should.ThrowAsync<InvalidOperationException>(() => service.ExecuteRequestAsync(request, nonce, session, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        clients.ReceivedCalls().ShouldBeEmpty(); issuer.ReceivedCalls().ShouldBeEmpty(); authority.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("wrong-purpose")]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-aggregate")]
    [InlineData("audit-outage")]
    public async Task FiniteKindsUseOnlyTheActualAdminIssuerAndFixedEndpointThroughAnOwnedScope(string kind)
    {
        string session = Guid.NewGuid().ToString("N"), nonce = Guid.NewGuid().ToString("N");
        var source = new ProcessSource("fixture", "work", "owned");
        var clients = Substitute.For<IHttpClientFactory>();
        var issuer = Substitute.For<IWorkloadAssertionIssuer>(); issuer.CanBindResources.Returns(true);
        WorkloadAssertionRequest? issued = null;
        issuer.IssueAsync(Arg.Do<WorkloadAssertionRequest>(request => issued = request), Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("actual-admin-issuer"));
        var authority = Substitute.For<IRouteCatalogAuthority>();
        CommittedRouteCatalogSnapshot snapshot = Snapshot();
        int releases = 0, requests = 0;
        authority.AcquireAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(new RouteCatalogDispatchLease(snapshot,
            () => { releases++; return Task.CompletedTask; })));
        authority.ReadbackAsync(Arg.Any<CancellationToken>()).Returns(Readback(snapshot));
        using var http = new HttpClient(new WorksSdkHttpEvidenceHandler(async (request, token) =>
        {
            requests++;
            request.Method.ShouldBe(HttpMethod.Post);
            request.Content!.Headers.ContentLength.ShouldNotBeNull("the existing bounded receiver probe requires an explicit wire length").ShouldBeInRange(1, 4096);
            request.RequestUri!.AbsolutePath.ShouldBe("/v1.0/invoke/works/method/work/processes/revalidate");
            request.Headers.GetValues(EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName).Single().ShouldBe("actual-admin-issuer");
            request.Headers.Contains("Authorization").ShouldBeFalse();
            request.Headers.Contains(RouteCatalogInvocationHandler.RootHeader).ShouldBeFalse("only the SDK primary transport supplies generation headers");
            request.Headers.Contains(WorksPlatformDispositionProbe.NonceHeader).ShouldBe(kind != "accepted");
            if (kind != "accepted") request.Headers.GetValues(WorksPlatformDispositionProbe.KindHeader).Single().ShouldBe(kind);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token).ConfigureAwait(false));
            body.RootElement.GetProperty("source").Deserialize<ProcessSource>(JsonSerializerOptions.Web).ShouldBe(source);
            releases.ShouldBe(0);
            await RouteCatalogRemoteCall.InvokeAsync(() => Task.CompletedTask, token).ConfigureAwait(false);
            return new HttpResponseMessage(kind == "accepted" ? HttpStatusCode.Accepted : kind == "audit-outage" ? HttpStatusCode.InternalServerError : HttpStatusCode.Forbidden)
            { Content = new StringContent("receiver-response") };
        }));
        clients.CreateClient("works-admin-disposition-proof").Returns(http);
        using var dapr = new DaprClientBuilder().UseHttpEndpoint("http://127.0.0.1:9").Build();
        var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns(Environments.Development);
        using var service = new WorksAdminProofService(clients, dapr, authority, issuer, Configuration(session, source), environment);
        var result = await service.ExecuteRequestAsync(new(nonce, session, kind, source, DateTimeOffset.UtcNow.AddSeconds(30)), nonce, session,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
        result.Status.ShouldBe(kind == "accepted" ? 202 : kind == "audit-outage" ? 500 : 403);
        requests.ShouldBe(1); releases.ShouldBe(1);
        issued.ShouldNotBeNull().Audience.ShouldBe("works"); issued.Operation.ShouldBe(WorksAdminProofProtocol.Operation);
        issued.Bindings![EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType].ShouldBe(kind == "wrong-tenant" ? "fixture-foreign" : "fixture");
        issued.Bindings[EventStoreWorkloadAuthenticationDefaults.BindingClaimPrefix + "aggregate"].ShouldBe(kind == "wrong-aggregate" ? "owned-foreign" : "owned");
        issued.Bindings[EventStoreWorkloadAuthenticationDefaults.BindingClaimPrefix + "purpose"].ShouldBe(kind == "wrong-purpose" ? "wrong-purpose" : "works.process-disposition");
        result.Generation.ShouldBe(Readback(snapshot));
    }

    [Fact]
    public async Task AmbiguousTransportFailureRetainsTheActualAdminFence()
    {
        string session = Guid.NewGuid().ToString("N"), nonce = Guid.NewGuid().ToString("N");
        var source = new ProcessSource("fixture", "work", "owned");
        var clients = Substitute.For<IHttpClientFactory>();
        var issuer = Substitute.For<IWorkloadAssertionIssuer>(); issuer.CanBindResources.Returns(true);
        issuer.IssueAsync(Arg.Any<WorkloadAssertionRequest>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("actual-admin-issuer"));
        var authority = Substitute.For<IRouteCatalogAuthority>();
        int releases = 0;
        authority.AcquireAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new RouteCatalogDispatchLease(Snapshot(), () => { releases++; return Task.CompletedTask; })));
        var failure = new IOException("private-uncertain-response");
        using var http = new HttpClient(new WorksSdkHttpEvidenceHandler((_, _) => Task.FromException<HttpResponseMessage>(failure)));
        clients.CreateClient("works-admin-disposition-proof").Returns(http);
        using var dapr = new DaprClientBuilder().UseHttpEndpoint("http://127.0.0.1:9").Build();
        var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns(Environments.Development);
        using var service = new WorksAdminProofService(clients, dapr, authority, issuer, Configuration(session, source), environment);
        (await Should.ThrowAsync<IOException>(() => service.ExecuteRequestAsync(new(nonce, session, "accepted", source, DateTimeOffset.UtcNow.AddSeconds(30)),
            nonce, session, TestContext.Current.CancellationToken)).ConfigureAwait(true)).ShouldBeSameAs(failure);
        releases.ShouldBe(0); authority.ReceivedCalls().Count().ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualSdkIssuerUsesOnlyAdminIdentityAndRefusesAConflictingOwner(bool conflicting)
    {
        var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns(Environments.Development);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:JwtBearer:Authority"] = string.Empty,
            ["Authentication:JwtBearer:Issuer"] = "hexalith-dev",
            ["Authentication:JwtBearer:Audience"] = "hexalith-eventstore",
            ["Authentication:JwtBearer:SigningKey"] = WorksAppHostSmokeHarness.DevSigningKey,
            ["Authentication:JwtBearer:RequireHttpsMetadata"] = "false",
            ["Authentication:WorkloadIssuer:Workload"] = conflicting ? "eventstore" : null,
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton(configuration).AddSingleton(environment);
        WorksAdminProofService.RegisterIssuer(services, configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var issuer = provider.GetRequiredService<IWorkloadAssertionIssuer>();
        if (conflicting)
        {
            await Should.ThrowAsync<InvalidOperationException>(async () =>
                _ = await issuer.IssueAsync(new("works", WorksAdminProofProtocol.Operation), TestContext.Current.CancellationToken).ConfigureAwait(true)).ConfigureAwait(true);
            return;
        }
        var source = new ProcessSource("fixture", "work", "owned");
        var request = new WorksAdminProofRequest(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "accepted", source, DateTimeOffset.UtcNow.AddSeconds(30));
        string token = (await issuer.IssueAsync(new("works", WorksAdminProofProtocol.Operation, WorksAdminProofProtocol.Bindings(request)),
            TestContext.Current.CancellationToken).ConfigureAwait(true)).ShouldNotBeNull();
        var parsed = new JsonWebToken(token);
        parsed.Claims.Single(claim => claim.Type == EventStoreWorkloadAuthenticationDefaults.CallerClaimType).Value.ShouldBe("eventstore-admin");
        parsed.Claims.Single(claim => claim.Type == RouteCatalogSurfacePolicy.ClaimType).Value.ShouldBe("service");
        parsed.Claims.Single(claim => claim.Type == EventStoreWorkloadAuthenticationDefaults.OperationClaimType).Value.ShouldBe(WorksAdminProofProtocol.Operation);
        parsed.Audiences.ShouldBe(["works"]);
        parsed.Claims.Single(claim => claim.Type == EventStoreWorkloadAuthenticationDefaults.TenantBindingClaimType).Value.ShouldBe(source.Tenant);
    }

    private static IConfiguration Configuration(string session, ProcessSource source, string fault = "") => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["WORKS_SDK_PROOF"] = "1", ["WORKS_SDK_PROOF_ROLE"] = fault == "foreign-role" ? "eventstore" : "eventstore-admin",
        ["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] = "eventstore-admin",
        ["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] = fault == "foreign-host" ? "foreign" : "eventstore-admin",
        [WorksAdminProofProtocol.SessionKey] = session, ["WORKS_PLATFORM_RECOVERY_GRANTS"] = JsonSerializer.Serialize(new[] { source }),
    }).Build();

    private static CommittedRouteCatalogSnapshot Snapshot()
    {
        // Strict isolated lease fixture only; the native fact separately qualifies the actual distributed Admin.
        var declaration = WorksIdentityReaderCatalog.Declaration;
        var operation = WorksIdentityReaderCatalog.Operation;
        RouteCatalogEnvelope envelope = RouteCatalogCodec.Seal(new(3, 1, "Development", false, [declaration],
            [new(declaration.RouteId, 1, RouteCatalogCodec.ComputeDeclarationDigest(declaration), "parties", "query", "v1",
                declaration.CredentialKind, declaration.Operation, false, "fixture", operation.Digest)], [], string.Empty, string.Empty, [operation], []));
        return new(RouteCatalogCodec.Encode(envelope), RouteCatalogCodec.ComputeRootDigest(envelope), 1, localAppId: "eventstore-admin");
    }

    private static RouteCatalogGenerationReadback Readback(CommittedRouteCatalogSnapshot snapshot) => new(1, 1, snapshot.RootDigest,
        snapshot.Envelope.RouteFacetDigest, snapshot.Envelope.IdempotencyFacetDigest, "Development", [], true);
}
