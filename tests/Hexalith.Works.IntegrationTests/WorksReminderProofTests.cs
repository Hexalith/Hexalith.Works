using System.Text.Json;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Routing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Unit controls for the private fixture driver; these do not qualify distributed activation.</summary>
public sealed class WorksReminderProofTests
{
    [Theory]
    [InlineData("command")]
    [InlineData("path")]
    [InlineData("session")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("source")]
    [InlineData("domain")]
    [InlineData("extra-field")]
    [InlineData("role")]
    [InlineData("app")]
    [InlineData("host")]
    [InlineData("cancelled")]
    public async Task InvalidRequestCannotReachAuthorityOrRegistrar(string fault)
    {
        string nonce = Guid.NewGuid().ToString("N"), session = Guid.NewGuid().ToString("N");
        var target = new ReminderTarget("fixture", "work", "source");
        var registrar = Substitute.For<IReminderRegistrar>();
        var authority = Substitute.For<IRouteCatalogAuthority>();
        var values = Configuration(session, target);
        if (fault == "role") values["WORKS_SDK_PROOF_ROLE"] = "eventstore";
        if (fault == "app") values["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] = "eventstore-admin";
        if (fault == "host") values["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] = "works-ha";
        using var service = Service(registrar, authority, values);
        var request = Request(nonce, session, target) with
        {
            Nonce = fault == "path" ? "../foreign" : nonce,
            Session = fault == "session" ? "foreign" : session,
            Command = fault == "command" ? "http://foreign/process" : "converge-reminder-twice",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(fault == "stale" ? -1 : fault == "future" ? 61 : 55),
            Payload = fault == "extra-field" ? JsonSerializer.SerializeToElement(new { target.Tenant, target.Domain, target.Aggregate, AppId = "foreign" })
                : JsonSerializer.SerializeToElement(target with { Domain = fault == "domain" ? "other" : "work", Aggregate = fault == "source" ? "foreign" : "source" }),
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (fault == "cancelled") cancellation.Cancel();
        await Should.ThrowAsync<Exception>(() => service.DispatchAsync(request, nonce, cancellation.Token)).ConfigureAwait(true);
        authority.ReceivedCalls().ShouldBeEmpty();
        registrar.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task UnservingOrMismatchedGenerationCannotDispatch(bool serving, bool wrongRoot)
    {
        string nonce = Guid.NewGuid().ToString("N"), session = Guid.NewGuid().ToString("N");
        var target = new ReminderTarget("fixture", "work", "source");
        var registrar = Substitute.For<IReminderRegistrar>();
        var authority = Substitute.For<IRouteCatalogAuthority>();
        var snapshot = Snapshot();
        authority.AcquireAsync(Arg.Any<CancellationToken>()).Returns(new RouteCatalogDispatchLease(snapshot, () => Task.CompletedTask));
        authority.ReadbackAsync(Arg.Any<CancellationToken>()).Returns(Readback(snapshot) with { Serving = serving, RootDigest = wrongRoot ? "foreign" : snapshot.RootDigest });
        using var service = Service(registrar, authority, Configuration(session, target));
        await Should.ThrowAsync<InvalidOperationException>(() => service.DispatchAsync(Request(nonce, session, target), nonce, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        registrar.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ActualInjectedRegistrarRunsTwiceConcurrentlyUnderOneMatchingLease()
    {
        string nonce = Guid.NewGuid().ToString("N"), session = Guid.NewGuid().ToString("N");
        var target = new ReminderTarget("fixture", "work", "source");
        var registrar = Substitute.For<IReminderRegistrar>();
        var authority = Substitute.For<IRouteCatalogAuthority>();
        var snapshot = Snapshot();
        int releases = 0, entered = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<ReminderConvergenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        registrar.ConvergeAsync(target, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref entered) == 2) both.TrySetResult();
            return complete.Task;
        });
        authority.AcquireAsync(Arg.Any<CancellationToken>()).Returns(new RouteCatalogDispatchLease(snapshot, () => { releases++; return Task.CompletedTask; }));
        authority.ReadbackAsync(Arg.Any<CancellationToken>()).Returns(Readback(snapshot));
        using var service = Service(registrar, authority, Configuration(session, target));
        Task<WorksReminderProofResult> dispatch = service.DispatchAsync(Request(nonce, session, target), nonce, TestContext.Current.CancellationToken);
        await both.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        dispatch.IsCompleted.ShouldBeFalse();
        releases.ShouldBe(0);
        complete.SetResult(new(1, 0, 0, 0, 0));
        var result = await dispatch.ConfigureAwait(true);
        result.Outcomes.Length.ShouldBe(2);
        result.Outcomes.All(outcome => outcome.Unresolved == 0 && outcome.Quarantined == 0).ShouldBeTrue();
        result.Generation.RootDigest.ShouldBe(snapshot.RootDigest);
        releases.ShouldBe(1);
    }

    [Fact]
    public async Task CancellationOfAnAmbiguousRegistrarCallRetainsItsDurableLease()
    {
        string nonce = Guid.NewGuid().ToString("N"), session = Guid.NewGuid().ToString("N");
        var target = new ReminderTarget("fixture", "work", "source");
        var registrar = Substitute.For<IReminderRegistrar>();
        var authority = Substitute.For<IRouteCatalogAuthority>();
        var snapshot = Snapshot();
        int releases = 0;
        var complete = new TaskCompletionSource<ReminderConvergenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        registrar.ConvergeAsync(target, Arg.Any<CancellationToken>()).Returns(complete.Task);
        authority.AcquireAsync(Arg.Any<CancellationToken>()).Returns(new RouteCatalogDispatchLease(snapshot, () => { releases++; return Task.CompletedTask; }));
        authority.ReadbackAsync(Arg.Any<CancellationToken>()).Returns(Readback(snapshot));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var service = Service(registrar, authority, Configuration(session, target));
        Task<WorksReminderProofResult> dispatch = service.DispatchAsync(Request(nonce, session, target), nonce, cancellation.Token);
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => dispatch).ConfigureAwait(true);
        releases.ShouldBe(0);
        complete.SetResult(new(1, 0, 0, 0, 0));
    }

    [Fact]
    public void PrivateFailureProjectionRetainsOnlyBoundedTypesAndMethods()
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new[] { new { type = typeof(IOException).FullName,
            methods = new[] { "Hexalith.Works.IntegrationTests.WorksReminderProofTests.PrivateFailureProjectionRetainsOnlyBoundedTypesAndMethods" },
            message = "private-bearer-payload-canary" } });
        string projected = WorksReminderProofRequests.DescribeFailure(body);
        projected.ShouldContain(nameof(IOException));
        projected.ShouldContain(nameof(PrivateFailureProjectionRetainsOnlyBoundedTypesAndMethods));
        projected.ShouldNotContain("private-bearer-payload-canary");
        WorksReminderProofRequests.DescribeFailure(new byte[32 * 1024 + 1]).ShouldBe("unavailable");
        WorksReminderProofRequests.DescribeFailure("{bad-json"u8.ToArray()).ShouldBe("unavailable");
    }

    private static WorksProofCallerRequest Request(string nonce, string session, ReminderTarget target)
        => new(nonce, session, "converge-reminder-twice", JsonSerializer.SerializeToElement(target), DateTimeOffset.UtcNow.AddSeconds(55));

    private static Dictionary<string, string?> Configuration(string session, ReminderTarget target) => new()
    {
        [WorksReminderProofProtocol.SessionKey] = session,
        ["WORKS_SDK_PROOF"] = "1", ["WORKS_SDK_PROOF_ROLE"] = "works",
        ["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] = "works", ["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] = "works",
        ["WORKS_PLATFORM_RECOVERY_GRANTS"] = JsonSerializer.Serialize(new[] { new ProcessSource(target.Tenant, target.Domain, target.Aggregate) }),
    };

    private static WorksReminderProofService Service(IReminderRegistrar registrar, IRouteCatalogAuthority authority, Dictionary<string, string?> values)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        return new(registrar, authority, new ConfigurationBuilder().AddInMemoryCollection(values).Build(), environment);
    }

    private static CommittedRouteCatalogSnapshot Snapshot()
    {
        var declaration = WorksIdentityReaderCatalog.Declaration;
        var operation = WorksIdentityReaderCatalog.Operation;
        var envelope = RouteCatalogCodec.Seal(new(3, 1, "Development", false, [declaration],
            [new(declaration.RouteId, 1, RouteCatalogCodec.ComputeDeclarationDigest(declaration), "parties", "query", "v1",
                declaration.CredentialKind, declaration.Operation, false, "fixture", operation.Digest)], [], string.Empty, string.Empty, [operation], []));
        return new(RouteCatalogCodec.Encode(envelope), RouteCatalogCodec.ComputeRootDigest(envelope), 1, localAppId: "works");
    }

    private static RouteCatalogGenerationReadback Readback(CommittedRouteCatalogSnapshot snapshot) => new(1, 1, snapshot.RootDigest,
        snapshot.Envelope.RouteFacetDigest, snapshot.Envelope.IdempotencyFacetDigest, "Development",
        [.. new[] { "eventstore", "eventstore-admin", "eventstore-operations", "works" }.Select(host => new RouteCatalogHostIdentity(host, host, "fixture", "fixture", "fixture"))], true);
}
