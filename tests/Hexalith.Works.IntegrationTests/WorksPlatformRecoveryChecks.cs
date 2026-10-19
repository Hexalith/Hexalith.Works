using System.Net.Http.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Dapr.Actors;
using Dapr.Actors.Client;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.DomainService;

using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises published SDK concurrency and paged recovery against the target's durable store.</summary>
internal static class WorksPlatformRecoveryChecks
{
    internal static async Task<object> ReissueConcurrentlyAsync(DistributedApplication app, HttpClient sidecar,
        string tenant, string item, string reminderName, CancellationToken cancellationToken)
    {
        ResourceEvent works = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works", cancellationToken, cancellationToken).ConfigureAwait(false);
        string endpoint = works.Snapshot.EnvironmentVariables.Single(variable => variable.Name == "DAPR_HTTP_ENDPOINT").Value!;
        string actor = ReminderIdentityCodec.ComputeActorId(tenant, item);
        IReminderActor first = ActorProxy.Create<IReminderActor>(new ActorId(actor), "WorksSdkReminderActor", new ActorProxyOptions { HttpEndpoint = endpoint });
        IReminderActor second = ActorProxy.Create<IReminderActor>(new ActorId(actor), "WorksSdkReminderActor", new ActorProxyOptions { HttpEndpoint = endpoint });
        var target = new ReminderTarget(tenant, "work", item);
        ReminderConvergenceResult[] outcomes = await Task.WhenAll(first.ConvergeAsync(target), second.ConvergeAsync(target)).WaitAsync(cancellationToken).ConfigureAwait(false);
        outcomes.All(outcome => outcome.Quarantined == 0 && outcome.Unresolved == 0).ShouldBeTrue();
        using HttpResponseMessage reissued = await sidecar.GetAsync($"v1.0/actors/WorksSdkReminderActor/{actor}/reminders/{reminderName}", cancellationToken).ConfigureAwait(false);
        reissued.EnsureSuccessStatusCode();
        return new { actor, reminderName, callers = outcomes.Length, outcomes, persistedSchedulerReminder = await reissued.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken).ConfigureAwait(false) };
    }

    internal static async Task<object> RepackCandidatePagesAsync(IReadModelStore store, string tenant,
        IReadOnlyList<ProcessSource> expected, CancellationToken cancellationToken)
    {
        var checkpoints = new ProcessCheckpointStore(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System);
        ProcessIndexPage<ProcessSource> original = (await checkpoints.ReadCandidatePageAsync(tenant, 0, cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        original.NextPage.ShouldBeNull();
        original.Entries.OrderBy(source => source.CatalogKey).ShouldBe(expected.OrderBy(source => source.CatalogKey));
        int observedSources = original.Entries.Count;
        ProcessSource[][] chunks = original.Entries.Chunk(3).ToArray();
        var pages = chunks.Select((entries, index) => new ProcessIndexPage<ProcessSource>(1, index, entries, index + 1 < chunks.Length ? index + 1 : null)).ToArray();
        // Publish successors first, then the root link, then its matching tail. A concurrent reader
        // can only observe bounded incomplete evidence until the complete linked inventory is durable.
        for (int index = pages.Length - 1; index >= 0; index--)
        {
            ProcessIndexPage<ProcessSource> page = pages[index];
            _ = await ReadModelWritePolicy.UpdateAsync<ProcessIndexPage<ProcessSource>>(store, "statestore",
                ProcessStateKeys.Candidates(tenant, index), _ => page,
                maxAttempts: 8, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        _ = await ReadModelWritePolicy.UpdateAsync<ProcessIndexTail>(store, "statestore", ProcessStateKeys.CandidatesTail(tenant),
            _ => new ProcessIndexTail(1, pages.Length - 1), maxAttempts: 8, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new { expectedSources = observedSources, pages };
    }

    internal static async Task<object> VerifyPagesAndOwnershipAsync(IReadModelStore store, string tenant,
        IReadOnlyList<ProcessSource> expected, CancellationToken cancellationToken)
    {
        var options = Options.Create(new EventStoreProcessOptions());
        var first = new ProcessCheckpointStore(store, options, TimeProvider.System);
        var second = new ProcessCheckpointStore(store, options, TimeProvider.System);
        int tail = (await first.ReadCandidateTailAsync(tenant, cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        tail.ShouldBeGreaterThan(0);
        var discovered = new List<ProcessSource>();
        for (int page = 0; page <= tail; page++)
        {
            ProcessIndexPage<ProcessSource> current = (await first.ReadCandidatePageAsync(tenant, page, cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
            current.Page.ShouldBe(page);
            current.NextPage.ShouldBe(page == tail ? null : page + 1);
            discovered.AddRange(current.Entries);
        }
        discovered.OrderBy(source => source.CatalogKey).ShouldBe(expected.OrderBy(source => source.CatalogKey));
        ProcessSource source = expected[0];
        ProcessSourceState? owner = null;
        int concurrentOwners = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        while (owner is null)
        {
            ProcessSourceState?[] contenders = await Task.WhenAll(first.AcquireAsync(source, deadline.Token), second.AcquireAsync(source, deadline.Token)).ConfigureAwait(false);
            concurrentOwners = contenders.Count(candidate => candidate is not null);
            concurrentOwners.ShouldBeLessThanOrEqualTo(1);
            owner = contenders.SingleOrDefault(candidate => candidate is not null);
            if (owner is null) await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
        try
        {
            (await second.AcquireAsync(source, deadline.Token).ConfigureAwait(false)).ShouldBeNull();
            await first.ReleaseAsync(owner, deadline.Token).ConfigureAwait(false);
            ProcessSourceState next = (await second.AcquireAsync(source, deadline.Token).ConfigureAwait(false)).ShouldNotBeNull();
            try
            {
                next.Fence.ShouldBeGreaterThan(owner.Fence);
                string staleOwnerWriteRefusal = (await Should.ThrowAsync<InvalidOperationException>(() => first.UpdateOwnedAsync(owner, state => state, deadline.Token)).ConfigureAwait(false)).GetType().Name;
                await first.ReleaseAsync(owner, deadline.Token).ConfigureAwait(false);
                string? retainedOwner = (await second.GetAsync(source, deadline.Token).ConfigureAwait(false)).Owner;
                retainedOwner.ShouldBe(next.Owner);
                next.Checkpoint.ShouldBe(owner.Checkpoint);
                return new { linkedPages = tail + 1, candidates = discovered, priorFence = owner.Fence, successorFence = next.Fence,
                    concurrentOwners, staleOwnerWriteRefused = staleOwnerWriteRefusal == nameof(InvalidOperationException), staleOwnerWriteRefusal,
                    staleReleasePreservedSuccessor = retainedOwner == next.Owner, checkpoint = next.Checkpoint };
            }
            finally { await second.ReleaseAsync(next, cancellationToken).ConfigureAwait(false); }
        }
        finally { await first.ReleaseAsync(owner, cancellationToken).ConfigureAwait(false); }
    }
}
