using System.Net;
using System.Text.Json;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Finite sender ownership, cancellation and persisted-effect controls; the full real fleet remains the integration proof.</summary>
public sealed class WorksTrustedEffectProofTests
{
    [Theory]
    [InlineData("command")]
    [InlineData("nonce")]
    [InlineData("session")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("tenant")]
    [InlineData("effect-format")]
    [InlineData("extra-url")]
    [InlineData("role")]
    [InlineData("app")]
    [InlineData("host")]
    [InlineData("duplicate-roster")]
    [InlineData("foreign-domain")]
    [InlineData("cancelled")]
    /// <summary>Checks the stated finite sender, receipt or cancellation invariant.</summary>
    public async Task InvalidRequestCannotReachAuthorityStoreIssuerOrSubmitter(string fault)
    {
        var fixture = new WorksTrustedEffectProofFixture(); var request = fixture.Request();
        var target = fixture.Target with { Source = fault == "source" ? "work-sdk415-foreign" : fixture.Target.Source,
            Target = fault == "target" ? "work-sdk415-foreign" : fixture.Target.Target,
            Tenant = fault == "tenant" ? "sdk415-foreign" : fixture.Target.Tenant,
            EffectId = fault == "effect-format" ? "../foreign" : fixture.Target.EffectId };
        request = request with { Command = fault == "command" ? "http://foreign/api/v1/trusted-effects" : request.Command,
            Nonce = fault == "nonce" ? "../foreign" : request.Nonce, Session = fault == "session" ? "foreign" : request.Session,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(fault == "expired" ? -1 : fault == "future" ? 61 : 55),
            Payload = fault == "extra-url" ? JsonSerializer.SerializeToElement(new { target.Tenant, target.Source, target.Target, target.EffectId, Url = "http://foreign", Workload = "eventstore-admin" }) : JsonSerializer.SerializeToElement(target) };
        if (fault == "role") fixture.Values["WORKS_SDK_PROOF_ROLE"] = "eventstore";
        if (fault == "app") fixture.Values["EventStore:RouteCatalog:DevelopmentBootstrap:AppId"] = "eventstore-admin";
        if (fault == "host") fixture.Values["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"] = "works-ha";
        if (fault is "duplicate-roster" or "foreign-domain")
            fixture.Values["WORKS_PLATFORM_RECOVERY_GRANTS"] = JsonSerializer.Serialize(new[] { new ProcessSource(target.Tenant, "work", target.Source),
                new(target.Tenant, "work", target.Target), new(target.Tenant, fault == "foreign-domain" ? "other" : "work", target.Source) });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (fault == "cancelled") cancellation.Cancel();
        using var service = fixture.Service();
        await Should.ThrowAsync<Exception>(() => service.DispatchAsync(request, fixture.Nonce, cancellation.Token)).ConfigureAwait(true);
        fixture.Authority.ReceivedCalls().ShouldBeEmpty(); fixture.Store.ReceivedCalls().ShouldBeEmpty();
        fixture.Delegation.ReceivedCalls().ShouldBeEmpty(); fixture.Submitter.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not-serving")]
    [InlineData("wrong-root")]
    [InlineData("missing-gateway")]
    /// <summary>Checks the stated finite sender, receipt or cancellation invariant.</summary>
    public async Task UnboundGenerationCannotReadOrSubmitThePersistedEffect(string fault)
    {
        var fixture = new WorksTrustedEffectProofFixture();
        var generation = fixture.Generation with { Serving = fault != "not-serving", RootDigest = fault == "wrong-root" ? "foreign" : fixture.Generation.RootDigest,
            RequiredHosts = fault == "missing-gateway" ? fixture.Generation.RequiredHosts.Where(host => host.HostId != "eventstore").ToArray() : fixture.Generation.RequiredHosts };
        fixture.Authority.ReadbackAsync(Arg.Any<CancellationToken>()).Returns(generation);
        using var service = fixture.Service();
        await Should.ThrowAsync<InvalidOperationException>(() => service.DispatchAsync(fixture.Request(), fixture.Nonce, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        fixture.Store.ReceivedCalls().ShouldBeEmpty(); fixture.Submitter.ReceivedCalls().ShouldBeEmpty(); fixture.Releases.ShouldBe(1);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("command")]
    [InlineData("kind")]
    [InlineData("idempotency")]
    [InlineData("message")]
    [InlineData("causation")]
    [InlineData("uncommitted")]
    [InlineData("disposition")]
    /// <summary>Checks the stated finite sender, receipt or cancellation invariant.</summary>
    public async Task AForeignOrUncommittedStoredRecordCannotReachTheIssuerOrSubmitter(string fault)
    {
        var fixture = new WorksTrustedEffectProofFixture(); var submission = fixture.Fault.Submission!;
        fixture.Fault = fixture.Fault with { FailNext = fault == "uncommitted", Disposition = fault == "disposition" ? null : fixture.Fault.Disposition,
            CausationId = fault == "causation" ? null : fixture.Fault.CausationId,
            Submission = submission with { CommandType = fault == "command" ? "CancelWorkItem" : submission.CommandType,
                Identity = submission.Identity with { SourceAggregate = fault == "identity" ? "work-sdk415-foreign" : submission.Identity.SourceAggregate,
                    EffectKind = fault == "kind" ? EffectKindCatalog.Expiry : submission.Identity.EffectKind },
                MessageId = fault == "message" ? "foreign" : submission.MessageId, IdempotencyKey = fault == "idempotency" ? "foreign" : submission.IdempotencyKey } };
        using var service = fixture.Service();
        await Should.ThrowAsync<InvalidOperationException>(() => service.DispatchAsync(fixture.Request(), fixture.Nonce, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        fixture.Delegation.ReceivedCalls().ShouldBeEmpty(); fixture.Submitter.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    /// <summary>Checks the stated finite sender, receipt or cancellation invariant.</summary>
    public async Task ActualInjectedSdkSubmitterReceivesOnlyTheStoredRequestAndFreshWorksDelegation(bool conflict)
    {
        var fixture = new WorksTrustedEffectProofFixture(); var original = fixture.Fault.Submission!;
        using var service = fixture.Service();
        var response = await service.DispatchAsync(fixture.Request(conflict), fixture.Nonce, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var call = fixture.Submitter.ReceivedCalls().ShouldHaveSingleItem();
        var submission = (TrustedEffectSubmission)call.GetArguments()[0]!; var context = (TrustedEffectContext)call.GetArguments()[1]!;
        submission.Identity.ShouldBe(original.Identity); submission.MessageId.ShouldBe(original.MessageId); submission.IdempotencyKey.ShouldBe(original.IdempotencyKey);
        if (!conflict) submission.ShouldBeSameAs(original);
        else
        {
            using var changed = JsonDocument.Parse(submission.CommandPayload); using var prior = JsonDocument.Parse(original.CommandPayload);
            changed.RootElement.GetProperty("awaitCondition").ValueKind.ShouldBe(JsonValueKind.Null);
            prior.RootElement.GetProperty("awaitCondition").ValueKind.ShouldNotBe(JsonValueKind.Null);
        }
        context.ShouldBe(new TrustedEffectContext("works", WorksTrustedEffectProofProtocol.Purpose, fixture.Fault.CausationId!, "actual-works-delegation"));
        var delegation = (ReminderDelegationRequest)fixture.Delegation.ReceivedCalls().ShouldHaveSingleItem().GetArguments()[0]!;
        delegation.Submission.ShouldBeSameAs(submission); delegation.CausationId.ShouldBe(context.CausationId);
        response.Status.ShouldBe(200); response.Generation.ShouldBe(fixture.Generation); fixture.Releases.ShouldBe(1);
    }

    [Fact]
    /// <summary>Checks the stated finite sender, receipt or cancellation invariant.</summary>
    public async Task CancelledInFlightSubmissionRetainsTheActualAmbiguityFence()
    {
        var fixture = new WorksTrustedEffectProofFixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<TrustedEffectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>()).Returns(call => { entered.TrySetResult(); return complete.Task; });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var service = fixture.Service(); var dispatch = service.DispatchAsync(fixture.Request(), fixture.Nonce, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        dispatch.IsCompleted.ShouldBeFalse(); fixture.Releases.ShouldBe(0); cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => dispatch).ConfigureAwait(true); fixture.Releases.ShouldBe(0);
        complete.SetResult(new(fixture.Target.EffectId, TrustedEffectDisposition.NoOp, true, null));
    }

    [Fact]
    /// <summary>Checks the stated finite sender, receipt or cancellation invariant.</summary>
    public async Task ActualSdkHttpRefusalRemainsARefusalWithoutLosingItsConservativeFence()
    {
        var fixture = new WorksTrustedEffectProofFixture();
        fixture.Submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TrustedEffectResult>(new HttpRequestException("private-provider-message", null, HttpStatusCode.Conflict)));
        using var service = fixture.Service();
        var response = await service.DispatchAsync(fixture.Request(conflict: true), fixture.Nonce, TestContext.Current.CancellationToken).ConfigureAwait(true);
        response.Status.ShouldBe(409); response.Failure.ShouldBeNull(); response.Body.ShouldBeEmpty(); fixture.Releases.ShouldBe(0);
    }

    [Fact]
    /// <summary>Checks the stated finite sender, receipt or cancellation invariant.</summary>
    public void GatewayOwnedProofVocabularyStillRefusesTheWorksTrustedEffectRoute()
        => Should.Throw<InvalidOperationException>(() => WorksProofCallerProtocol.Command("api/v1/trusted-effects"));
}
