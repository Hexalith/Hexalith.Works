using System.Security.Claims;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Recovery;

using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises Works' concrete fail-closed authority defaults and independent delegated purposes.</summary>
public sealed class WorksSdkProcessAuthorityTests
{
    /// <summary>Each closed effect family requests fresh authority for its exact submission and causation.</summary>
    [Theory]
    [InlineData(EffectKindCatalog.DateResume, "works.date-resume")]
    [InlineData(EffectKindCatalog.Expiry, "works.expiry")]
    [InlineData(EffectKindCatalog.CascadeCancel, "works.cascade-cancel")]
    [InlineData(EffectKindCatalog.CascadeExpire, "works.cascade-expire")]
    [InlineData(EffectKindCatalog.ChildCompletionResume, "works.child-completion-resume")]
    public async Task DelegationBindsEveryKindToItsIndependentPurpose(string kind, string expectedPurpose)
    {
        WorksEffectPurposes.ForKind(kind).ShouldBe(expectedPurpose);
        TrustedEffectSubmission submission = Submission(kind);
        var authority = Substitute.For<IReminderDelegationTokenProvider>();
        CancellationToken ct = TestContext.Current.CancellationToken;
        var expected = new ReminderDelegationRequest(submission, "works", expectedPurpose, "committed-message");
        authority.GetDelegationTokenAsync(expected, ct).Returns("synthetic-first-token", "synthetic-second-token");
        var provider = new WorksProcessDelegationProvider(authority);

        (await provider.CreateAsync(submission, "committed-message", ct)).ShouldBe(
            new TrustedEffectContext("works", expectedPurpose, "committed-message", "synthetic-first-token"));
        (await provider.CreateAsync(submission, "committed-message", ct)).ShouldBe(
            new TrustedEffectContext("works", expectedPurpose, "committed-message", "synthetic-second-token"));
        await authority.Received(2).GetDelegationTokenAsync(expected, ct);
    }

    /// <summary>Absent authority never creates a trusted context.</summary>
    [Fact]
    public async Task MissingDelegationAuthorityFailsClosed()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => new WorksProcessDelegationProvider().CreateAsync(
            Submission(EffectKindCatalog.CascadeCancel), "committed-message", TestContext.Current.CancellationToken));
    }

    /// <summary>Null, empty and whitespace credentials are refused after the authority call.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UnavailableDelegationTokenFailsClosed(string? token)
    {
        var authority = Substitute.For<IReminderDelegationTokenProvider>();
        authority.GetDelegationTokenAsync(Arg.Any<ReminderDelegationRequest>(), Arg.Any<CancellationToken>()).Returns(token);
        await Should.ThrowAsync<InvalidOperationException>(() => new WorksProcessDelegationProvider(authority).CreateAsync(
            Submission(EffectKindCatalog.CascadeCancel), "committed-message", TestContext.Current.CancellationToken));
        await authority.Received(1).GetDelegationTokenAsync(Arg.Any<ReminderDelegationRequest>(), TestContext.Current.CancellationToken);
    }

    /// <summary>An unknown family cannot ask the authority for any purpose.</summary>
    [Fact]
    public async Task UnknownEffectKindFailsBeforeDelegation()
    {
        var authority = Substitute.For<IReminderDelegationTokenProvider>();
        Should.Throw<InvalidOperationException>(() => WorksEffectPurposes.ForKind("foreign-effect"));
        await Should.ThrowAsync<InvalidOperationException>(() => new WorksProcessDelegationProvider(authority).CreateAsync(
            Submission("foreign-effect"), "committed-message", TestContext.Current.CancellationToken));
        authority.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Authentication alone never grants the default disposition authority.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultDispositionAuthorizerDeniesEveryPrincipal(bool authenticated)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "synthetic-operator")], authenticated ? "synthetic" : null));
        principal.Identity!.IsAuthenticated.ShouldBe(authenticated);
        (await new WorksProcessDispositionAuthorizer().AuthorizeAsync(principal,
            new ProcessSource("tenant-415", "work", "quarantined-source"), TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>Overriding authorization without durable audit still leaves the persisted quarantine unchanged.</summary>
    [Fact]
    public async Task DefaultDispositionAuditSinkBlocksMutationEvenWithAuthorization()
    {
        var source = new ProcessSource("tenant-415", "work", "quarantined-source");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "synthetic-operator")], "synthetic"));
        var audit = new WorksProcessDispositionAuditSink();
        await Should.ThrowAsync<InvalidOperationException>(() => audit.RecordAsync(principal, source, "repair", TestContext.Current.CancellationToken));
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var checkpoints = new ProcessCheckpointStore(store, options, TimeProvider.System);
        await checkpoints.EnsureCandidateAsync(source, cancellationToken: TestContext.Current.CancellationToken);
        ProcessSourceState owned = (await checkpoints.AcquireAsync(source, TestContext.Current.CancellationToken))!;
        owned = await checkpoints.UpdateOwnedAsync(owned, state => state with
        {
            Checkpoint = 1, Status = ProcessSourceStatus.Quarantined, FailureSequence = 2, ReasonCode = "invalid-evidence",
        }, TestContext.Current.CancellationToken);
        await checkpoints.ReleaseAsync(owned, TestContext.Current.CancellationToken);
        var before = await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), TestContext.Current.CancellationToken);
        int storedKeys = store.Count;
        var authorizer = Substitute.For<IProcessDispositionAuthorizer>();
        authorizer.AuthorizeAsync(principal, source, Arg.Any<CancellationToken>()).Returns(true);
        var disposition = new ProcessDispositionService(authorizer, audit, checkpoints,
            new ProcessSweepStore(store, options, TimeProvider.System), options);

        await Should.ThrowAsync<InvalidOperationException>(() => disposition.RevalidateAsync(principal, source, "repair", TestContext.Current.CancellationToken));

        var after = await store.GetAsync<ProcessSourceState>("statestore", ProcessStateKeys.Source(source), TestContext.Current.CancellationToken);
        after.ShouldBe(before);
        store.Count.ShouldBe(storedKeys);
    }

    private static TrustedEffectSubmission Submission(string kind)
    {
        var identity = new EffectIdentity("tenant-415", "work", "source", 2, kind, "work", "target", 0);
        return new TrustedEffectSubmission(identity, "SyntheticCommand", [123, 125], "synthetic-message", "synthetic-key");
    }
}
