using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Dapr.Client;

using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksProofCallerTests
{
    [Theory]
    [InlineData("project/%2e%2e/query")]
    [InlineData("project/../query")]
    [InlineData("http://foreign/query")]
    [InlineData("query?other=1")]
    public async Task RawHelperRejectsAliasesBeforeUriConstruction(string raw)
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9"), Timeout = TimeSpan.FromSeconds(3) };
        await Should.ThrowAsync<InvalidOperationException>(() => WorksPlatformProofRequests.PostAsync(client, "works", raw,
            new { tenantId = "fixture" }, "unused-proof-input", TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("dapr-app-id")]
    [InlineData("dapr-api-token")]
    [InlineData("Authorization")]
    [InlineData("X-Hexalith-Route-Catalog-Root")]
    [InlineData("X-Hexalith-Route-Catalog-Epoch")]
    [InlineData("X-Arbitrary-Probe")]
    public async Task ClientCannotForwardAuthorityOrArbitraryHeaders(string header)
    {
        string directory = PrivateDirectory();
        try
        {
            using var client = new HttpClient(new WorksProofCallerTransport(directory, Guid.NewGuid().ToString("N"), new Uri("http://127.0.0.1:9")))
            { BaseAddress = new Uri("http://127.0.0.1:9"), Timeout = TimeSpan.FromSeconds(3) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1.0/invoke/works/method/query") { Content = JsonContent.Create(new { tenantId = "fixture" }) };
            request.Headers.TryAddWithoutValidation(header, "foreign");
            request.Options.Set(WorksProofCallerProtocol.UseActualSender, true);
            await Should.ThrowAsync<InvalidOperationException>(() => client.SendAsync(request, TestContext.Current.CancellationToken)).ConfigureAwait(true);
            Directory.GetFiles(directory).ShouldBeEmpty();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("/v1.0/invoke/foreign/method/query")]
    [InlineData("/v1.0/invoke/works/method/process")]
    [InlineData("/v1.0/invoke/works/method/project/%2e%2e/query")]
    [InlineData("/v1.0/invoke/works/method/query?target=foreign")]
    public async Task ClientCannotChooseAnotherResourceOrUninventoriedMethod(string path)
    {
        string directory = PrivateDirectory();
        try
        {
            using var client = new HttpClient(new WorksProofCallerTransport(directory, Guid.NewGuid().ToString("N"), new Uri("http://127.0.0.1:9")))
            { BaseAddress = new Uri("http://127.0.0.1:9"), Timeout = TimeSpan.FromSeconds(3) };
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { tenantId = "fixture" }) };
            request.Options.Set(WorksProofCallerProtocol.UseActualSender, true);
            await Should.ThrowAsync<InvalidOperationException>(() => client.SendAsync(request, TestContext.Current.CancellationToken));
            Directory.GetFiles(directory).ShouldBeEmpty();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("unknown-command")]
    [InlineData("foreign-session")]
    [InlineData("stale")]
    [InlineData("cancelled")]
    public async Task InHostDriverRefusesBeforeAnyAuthorityOrTransportWork(string fault)
    {
        string directory = PrivateDirectory();
        string session = Guid.NewGuid().ToString("N"), nonce = Guid.NewGuid().ToString("N");
        try
        {
            var clients = Substitute.For<IHttpClientFactory>();
            var authority = Substitute.For<IRouteCatalogAuthority>();
            using var dapr = new DaprClientBuilder().UseHttpEndpoint("http://127.0.0.1:9").Build();
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(Environments.Development);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [WorksProofCallerProtocol.DirectoryKey] = directory, [WorksProofCallerProtocol.SessionKey] = session,
                ["WORKS_SDK_PROOF"] = "1", ["WORKS_SDK_PROOF_ROLE"] = "eventstore", ["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] = "eventstore",
            }).Build();
            var request = new WorksProofCallerRequest(nonce, fault == "foreign-session" ? "foreign" : session,
                fault == "unknown-command" ? "http://foreign/query" : "query", JsonSerializer.SerializeToElement(new { tenantId = "fixture" }),
                DateTimeOffset.UtcNow.AddSeconds(fault == "stale" ? -1 : 60));
            await WorksProofCallerProtocol.WriteNewAsync(Path.Combine(directory, nonce + ".request.json"), request, TestContext.Current.CancellationToken).ConfigureAwait(true);
            if (fault == "cancelled") await WorksProofCallerProtocol.WriteNewAsync(Path.Combine(directory, nonce + ".cancel"), new { nonce }, TestContext.Current.CancellationToken).ConfigureAwait(true);
            using var service = new WorksProofCallerService(clients, dapr, authority, configuration, environment);
            await service.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            string responsePath = Path.Combine(directory, nonce + ".response.json");
            while (!File.Exists(responsePath)) await Task.Delay(25, deadline.Token).ConfigureAwait(true);
            var response = JsonSerializer.Deserialize<WorksProofCallerResponse>(await WorksProofCallerProtocol.ReadAsync(responsePath, deadline.Token).ConfigureAwait(true)).ShouldNotBeNull();
            response.Status.ShouldBe(0);
            response.Failure.ShouldBe(fault == "cancelled" ? "cancelled" : "caller-refused");
            response.Generation.ShouldBeNull();
            clients.ReceivedCalls().ShouldBeEmpty();
            authority.ReceivedCalls().ShouldBeEmpty();
            await service.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ClientCancellationIsAcknowledgedByTheActualInHostHttpOperation()
    {
        string directory = PrivateDirectory();
        string session = Guid.NewGuid().ToString("N");
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new BlockingTransport(entered, aborted));
            var clients = Substitute.For<IHttpClientFactory>();
            clients.CreateClient("domain-service-invocation").Returns(http);
            var authority = Substitute.For<IRouteCatalogAuthority>();
            using var dapr = new DaprClientBuilder().UseHttpEndpoint("http://127.0.0.1:9").Build();
            var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns(Environments.Development);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [WorksProofCallerProtocol.DirectoryKey] = directory, [WorksProofCallerProtocol.SessionKey] = session,
                ["WORKS_SDK_PROOF"] = "1", ["WORKS_SDK_PROOF_ROLE"] = "eventstore", ["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] = "eventstore",
            }).Build();
            using var service = new WorksProofCallerService(clients, dapr, authority, configuration, environment);
            await service.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            using var client = new HttpClient(new WorksProofCallerTransport(directory, session, new Uri("http://127.0.0.1:9")))
            { BaseAddress = new Uri("http://127.0.0.1:9") };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1.0/invoke/works/method/admin/operational-index-metadata")
            { Content = JsonContent.Create(new { domains = new[] { "work" }, appId = "works", serviceVersion = "v1" }) };
            request.Options.Set(WorksProofCallerProtocol.UseActualSender, true);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            Task<HttpResponseMessage> sending = client.SendAsync(request, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => { using var response = await sending.ConfigureAwait(true); }).ConfigureAwait(true);
            await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
            Directory.GetFiles(directory).ShouldBeEmpty();
            authority.ReceivedCalls().ShouldBeEmpty();
            await service.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string PrivateDirectory()
    {
        string directory = Directory.CreateTempSubdirectory("works-proof-caller-controls-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }

    private sealed class BlockingTransport(TaskCompletionSource entered, TaskCompletionSource aborted) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/v1.0/invoke/works/method/admin/operational-index-metadata");
            request.Headers.Contains(Hexalith.EventStore.ServiceDefaults.Authentication.EventStoreWorkloadAuthenticationDefaults.AssertionHeaderName).ShouldBeFalse("The IPC cannot copy a test-process assertion.");
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(true); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { aborted.TrySetResult(); throw; }
            return new(HttpStatusCode.NoContent);
        }
    }
}
