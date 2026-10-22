using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.DomainService;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Parties.Contracts.Queries;
using Hexalith.Works.Queries;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Authority deadlines deny access before state reads without swallowing caller cancellation.</summary>
public sealed class WorksQueryPolicyDeadlineTests
{
    /// <summary>An unresponsive authority cannot exhaust the outer invocation budget or expose state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresponsiveAuthorityIsDeniedBeforeAnyWorksRead(bool detail)
    {
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var pending = new TaskCompletionSource<PartyIdentityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call => { observed = call.ArgAt<CancellationToken>(2); return pending.Task; });
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        using var outerDeadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        outerDeadline.CancelAfter(TimeSpan.FromSeconds(5));

        QueryResult result = await ExecuteAsync(detail, store, policy, outerDeadline.Token).ConfigureAwait(true);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        outerDeadline.IsCancellationRequested.ShouldBeFalse();
        observed.IsCancellationRequested.ShouldBeTrue();
        pending.Task.IsCompleted.ShouldBeFalse();
        store.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Caller cancellation propagates to the identity request and query execution.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationIsPreservedBeforeAnyWorksRead(bool detail)
    {
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        IReadModelStore store = Substitute.For<IReadModelStore>();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<PartyIdentityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call => { entered.TrySetResult(call.ArgAt<CancellationToken>(2)); return pending.Task; });
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<QueryResult> execution = ExecuteAsync(detail, store, policy, caller.Token);
        CancellationToken observed = await entered.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await caller.CancelAsync().ConfigureAwait(true);

        await Should.ThrowAsync<OperationCanceledException>(() => execution).ConfigureAwait(true);
        observed.IsCancellationRequested.ShouldBeTrue();
        store.ReceivedCalls().ShouldBeEmpty();
    }

    private static Task<QueryResult> ExecuteAsync(bool detail, IReadModelStore store, WorksQueryPolicy policy, CancellationToken token)
    {
        var options = Options.Create(new WorksProjectionOptions { UseSdkDelivery = true });
        QueryEnvelope query = new("tenant-deadline", "work", "work-deadline",
            detail ? GetWorkItemQueryHandler.GetWorkItemQueryType : WhatsNextQueryHandler.WhatsNextQueryType,
            JsonSerializer.SerializeToUtf8Bytes(new { partyId = "party-deadline" }), "correlation-deadline", "actor-deadline",
            null, false, null, "actor-deadline", "eventstore", true, null, null);
        return detail
            ? new GetWorkItemQueryHandler(policy).ExecuteAsync(query, token)
            : new WhatsNextQueryHandler(policy).ExecuteAsync(query, token);
    }
}
