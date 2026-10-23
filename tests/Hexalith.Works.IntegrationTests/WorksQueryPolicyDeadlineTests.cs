using System.Text.Json;

using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Streams;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Security;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.Parties.Client.Abstractions;
using Hexalith.Parties.Contracts.Models;
using Hexalith.Parties.Contracts.Queries;
using Hexalith.Parties.Contracts.ValueObjects;
using Hexalith.Works.Queries;
using Hexalith.Works.Recovery;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Authority deadlines deny access before state reads without swallowing caller cancellation.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksQueryPolicyDeadlineTests
{
    /// <summary>An unresponsive authority cannot exhaust the outer invocation budget or expose state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresponsiveAuthorityIsDeniedBeforeAnyWorksRead(bool detail)
    {
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        IReadModelStore store = Substitute.For<IReadModelStore, IReadModelBatchStore, IReadModelBulkStore>();
        var pending = new TaskCompletionSource<PartyIdentityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call => { observed = call.ArgAt<CancellationToken>(2); return pending.Task; });
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        using var outerDeadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        outerDeadline.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            QueryResult result = await ExecuteAsync(detail, store, policy, outerDeadline.Token).ConfigureAwait(true);

            AssertDenied(result, store);
            outerDeadline.IsCancellationRequested.ShouldBeFalse();
            observed.IsCancellationRequested.ShouldBeTrue();
            pending.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            pending.TrySetResult(Resolved());
            await DrainProviderSlotsAsync();
        }
    }

    /// <summary>Caller cancellation propagates to the identity request and query execution.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationIsPreservedBeforeAnyWorksRead(bool detail)
    {
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        IReadModelStore store = Substitute.For<IReadModelStore, IReadModelBatchStore, IReadModelBulkStore>();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<PartyIdentityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call => { entered.TrySetResult(call.ArgAt<CancellationToken>(2)); return pending.Task; });
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<QueryResult> execution = ExecuteAsync(detail, store, policy, caller.Token);
        CancellationToken observed = await entered.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        try
        {
            await caller.CancelAsync().ConfigureAwait(true);

            OperationCanceledException error = await Should.ThrowAsync<OperationCanceledException>(() => execution).ConfigureAwait(true);
            error.CancellationToken.ShouldBe(caller.Token);
            observed.IsCancellationRequested.ShouldBeTrue();
            store.ReceivedCalls().ShouldBeEmpty();
        }
        finally
        {
            pending.TrySetResult(Resolved());
            await DrainProviderSlotsAsync();
        }
    }

    /// <summary>A provider blocking before returning its Task cannot hold the query beyond its authority budget.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SynchronouslyBlockedProviderIsDeniedBeforeAnyWorksRead(bool detail, bool cancelCaller)
    {
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        IReadModelStore store = Substitute.For<IReadModelStore, IReadModelBatchStore, IReadModelBulkStore>();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                entered.TrySetResult(call.ArgAt<CancellationToken>(2));
                release.Task.GetAwaiter().GetResult();
                return Task.FromResult(Resolved());
            });
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<QueryResult> execution = Task.Run(() => ExecuteAsync(detail, store, policy, caller.Token));
        try
        {
            CancellationToken observed = await entered.Task.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            if (cancelCaller)
            {
                await caller.CancelAsync();
                OperationCanceledException error = await Should.ThrowAsync<OperationCanceledException>(() =>
                    execution.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
                error.CancellationToken.ShouldBe(caller.Token);
                store.ReceivedCalls().ShouldBeEmpty();
            }
            else
            {
                AssertDenied(await execution.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken), store);
            }
            observed.IsCancellationRequested.ShouldBeTrue();
            release.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
            try { await execution.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken); }
            catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
            await DrainProviderSlotsAsync();
        }
    }

    /// <summary>Neither deadline expiry nor caller cancellation waits for provider cancellation callbacks.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BlockingProviderCancellationCannotHoldQuery(bool detail, bool cancelCaller)
    {
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        IReadModelStore store = Substitute.For<IReadModelStore, IReadModelBatchStore, IReadModelBulkStore>();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<PartyIdentityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                entered.TrySetResult(call.ArgAt<CancellationToken>(2));
                return pending.Task;
            });
        var policy = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<QueryResult> execution = ExecuteAsync(detail, store, policy, caller.Token);
        Task cancellation = Task.CompletedTask;
        try
        {
            CancellationToken observed = await entered.Task.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            // Register after ExecuteAsync has installed its wait, so this callback runs first on a shared token.
            registration = observed.Register(() =>
            {
                callbackEntered.TrySetResult();
                callbackRelease.Task.GetAwaiter().GetResult();
                callbackCompleted.TrySetResult();
            });
            if (cancelCaller) cancellation = caller.CancelAsync();
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);

            if (cancelCaller)
            {
                OperationCanceledException error = await Should.ThrowAsync<OperationCanceledException>(() =>
                    execution.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
                error.CancellationToken.ShouldBe(caller.Token);
            }
            else
            {
                AssertDenied(await execution.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken), store);
            }

            callbackRelease.Task.IsCompleted.ShouldBeFalse();
            pending.Task.IsCompleted.ShouldBeFalse();
            store.ReceivedCalls().ShouldBeEmpty();
        }
        finally
        {
            callbackRelease.TrySetResult();
            pending.TrySetResult(Resolved());
            await cancellation.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            await registration.DisposeAsync();
            await DrainProviderSlotsAsync();
        }
    }

    /// <summary>Abandoned providers retain the shared SDK slots across policy instances until they actually finish.</summary>
    [Fact]
    public async Task RepeatedTimeoutsKeepSharedAdmissionUntilProviderCompletion()
    {
        const int capacity = 16;
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        IReadModelStore store = Substitute.For<IReadModelStore, IReadModelBatchStore, IReadModelBulkStore>();
        var pending = Enumerable.Range(0, capacity).Select(_ =>
            new TaskCompletionSource<PartyIdentityResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource<PartyIdentityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int call = Interlocked.Increment(ref calls);
                if (call == capacity) allEntered.TrySetResult();
                if (call <= capacity) return pending[call - 1].Task;
                recoveryEntered.TrySetResult();
                return recovered.Task;
            });
        var executions = Enumerable.Range(0, capacity).Select(index => ExecuteAsync(index % 2 == 0, store,
            new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions())), TestContext.Current.CancellationToken)).ToArray();
        try
        {
            await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            foreach (QueryResult result in await Task.WhenAll(executions).WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken))
                AssertDenied(result, store);
            pending.All(task => !task.Task.IsCompleted).ShouldBeTrue();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                QueryResult denied = await ExecuteAsync(attempt % 2 == 0, store,
                    new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions())), TestContext.Current.CancellationToken);
                AssertDenied(denied, store);
                Volatile.Read(ref calls).ShouldBe(capacity);
            }

            IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
            gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
                .Returns(Story48Streams.Page("tenant-deadline", "work-deadline"));
            var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
            await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => reader.ReadAsync("tenant-deadline", "work-deadline",
                TestContext.Current.CancellationToken));
            gateway.ReceivedCalls().ShouldBeEmpty();

            // A late fault is observed by the SDK, frees just its slot, and never resumes the abandoned query.
            pending[0].SetException(new InvalidOperationException("late provider failure"));
            Task<WorksQueryAuthority>? recovery = null;
            using var releaseDeadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            releaseDeadline.CancelAfter(TimeSpan.FromSeconds(4));
            while (!recoveryEntered.Task.IsCompleted)
            {
                recovery = new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()))
                    .AuthorizeAsync(Query(false), releaseDeadline.Token);
                if (await Task.WhenAny(recovery, recoveryEntered.Task).ConfigureAwait(true) == recovery)
                {
                    (await recovery.ConfigureAwait(true)).Allowed.ShouldBeFalse();
                    await Task.Delay(10, releaseDeadline.Token);
                }
            }
            AssertDenied(await ExecuteAsync(true, store, new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions())),
                TestContext.Current.CancellationToken), store);
            Volatile.Read(ref calls).ShouldBe(capacity + 1);
            recovered.TrySetResult(Resolved());
            WorksQueryAuthority authority = await recovery!.ConfigureAwait(true);
            authority.PartyId.ShouldBe("party-deadline");
            Volatile.Read(ref calls).ShouldBe(capacity + 1);
            store.ReceivedCalls().ShouldBeEmpty();
            executions.All(task => task.Result.ErrorMessage == QueryAdapterFailureReason.Forbidden).ShouldBeTrue();
            (await reader.ReadAsync("tenant-deadline", "work-deadline", TestContext.Current.CancellationToken)).ShouldBeEmpty();
            gateway.ReceivedCalls().Count().ShouldBe(1);
        }
        finally
        {
            foreach (var operation in pending) operation.TrySetResult(Resolved());
            recovered.TrySetResult(Resolved());
            await Task.WhenAll(executions).WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            await DrainProviderSlotsAsync();
        }
    }

    /// <summary>Ordinary matching authority still succeeds, while mismatched actor evidence fails closed.</summary>
    [Theory]
    [InlineData("actor-deadline", true)]
    [InlineData("foreign-actor", false)]
    public async Task CompletedAuthorityRetainsActorBinding(string actor, bool allowed)
    {
        IPartiesIdentityClient parties = Substitute.For<IPartiesIdentityClient>();
        parties.ResolvePartyIdentityAsync(Arg.Any<string>(), Arg.Any<ResolvePartyIdentity>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Resolved(actor)));

        WorksQueryAuthority authority = await new WorksQueryPolicy(parties, Options.Create(new WorksQueryOptions()))
            .AuthorizeAsync(Query(false), TestContext.Current.CancellationToken);

        authority.Allowed.ShouldBe(allowed);
        authority.AllWork.ShouldBeFalse();
        authority.PartyId.ShouldBe(allowed ? "party-deadline" : null);
    }

    private static void AssertDenied(QueryResult result, IReadModelStore store)
    {
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe(QueryAdapterFailureReason.Forbidden);
        store.ReceivedCalls().ShouldBeEmpty();
    }

    private static async Task DrainProviderSlotsAsync()
    {
        // Acquire every slot after completing test-owned providers, so no abandoned worker leaks into another test.
        using var deadline = new AuthoritativeStreamReadDeadline(TimeSpan.FromSeconds(4), TimeProvider.System,
            CancellationToken.None, TimeProvider.System.GetTimestamp());
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = new List<Task<int>>();
        try
        {
            while (workers.Count < 16)
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<int> worker = deadline.ReadWithAdmissionAsync(_ => { entered.TrySetResult(); return release.Task; });
                if (await Task.WhenAny(worker, entered.Task).ConfigureAwait(false) == entered.Task)
                    workers.Add(worker);
                else
                {
                    try { await worker.ConfigureAwait(false); }
                    catch (AuthoritativeStreamReadAdmissionException) { await Task.Delay(10).ConfigureAwait(false); }
                }
            }
        }
        finally
        {
            release.TrySetResult(0);
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }

    private static PartyIdentityResult Resolved(string actor = "actor-deadline")
    {
        DateTimeOffset observed = DateTimeOffset.UnixEpoch.AddDays(1);
        var custody = new IdentityHistoryCustodyEvidence("policy", "party-actor-history-v1", observed.AddDays(1),
            1, "custody", true, true, true);
        var binding = new HumanActorBindingEvidence("tenant-deadline", "party-deadline", actor, 1, 1,
            observed.AddMinutes(-1), null, "source", "provenance", custody);
        return new PartyIdentityResult(PartyIdentityOutcome.Resolved,
            new PartyIdentityEvidence(1, "tenant-deadline", "party-deadline", PartyIdentityClassification.Human,
                true, false, false, 2, observed, "observation", binding));
    }

    private static Task<QueryResult> ExecuteAsync(bool detail, IReadModelStore store, WorksQueryPolicy policy, CancellationToken token)
    {
        var coordinator = new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store);
        return detail
            ? new GetWorkItemQueryHandler(policy, coordinator).ExecuteAsync(Query(detail), token)
            : new WhatsNextQueryHandler(policy, coordinator).ExecuteAsync(Query(detail), token);
    }

    private static QueryEnvelope Query(bool detail)
        => new("tenant-deadline", "work", "work-deadline",
            detail ? GetWorkItemQueryHandler.GetWorkItemQueryType : WhatsNextQueryHandler.WhatsNextQueryType,
            JsonSerializer.SerializeToUtf8Bytes(new { partyId = "party-deadline" }), "correlation-deadline", "actor-deadline",
            null, false, null, "actor-deadline", "eventstore", true, null, null);
}
