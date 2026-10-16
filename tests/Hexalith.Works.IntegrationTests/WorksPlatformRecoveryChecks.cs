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
        return new { actor, reminderName, callers = 2, outcomes, persistedSchedulerReminder = await reissued.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken).ConfigureAwait(false) };
    }

    internal static async Task<object> RepackCandidatePagesAsync(IReadModelStore store, string tenant,
        IReadOnlyList<ProcessSource> expected, CancellationToken cancellationToken)
    {
        var checkpoints = new ProcessCheckpointStore(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System);
        ProcessIndexPage<ProcessSource> original = (await checkpoints.ReadCandidatePageAsync(tenant, 0, cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        original.NextPage.ShouldBeNull();
        original.Entries.OrderBy(source => source.CatalogKey).ShouldBe(expected.OrderBy(source => source.CatalogKey));
        ProcessSource[][] chunks = original.Entries.Chunk(3).ToArray();
        var pages = chunks.Select((entries, index) => new ProcessIndexPage<ProcessSource>(1, index, entries, index + 1 < chunks.Length ? index + 1 : null)).ToArray();
        // Publish successors first, then the root link, then its matching tail. A concurrent reader
        // can only observe bounded incomplete evidence until the complete linked inventory is durable.
        for (int index = pages.Length - 1; index >= 0; index--)
            await store.SaveAsync("statestore", ProcessStateKeys.Candidates(tenant, index), pages[index], cancellationToken).ConfigureAwait(false);
        await store.SaveAsync("statestore", ProcessStateKeys.CandidatesTail(tenant), new ProcessIndexTail(1, pages.Length - 1), cancellationToken).ConfigureAwait(false);
        return new { expectedSources = expected.Count, pages };
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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        while (owner is null)
        {
            ProcessSourceState?[] contenders = await Task.WhenAll(first.AcquireAsync(source, deadline.Token), second.AcquireAsync(source, deadline.Token)).ConfigureAwait(false);
            contenders.Count(candidate => candidate is not null).ShouldBeLessThanOrEqualTo(1);
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
                await Should.ThrowAsync<InvalidOperationException>(() => first.UpdateOwnedAsync(owner, state => state, deadline.Token)).ConfigureAwait(false);
                await first.ReleaseAsync(owner, deadline.Token).ConfigureAwait(false);
                (await second.GetAsync(source, deadline.Token).ConfigureAwait(false)).Owner.ShouldBe(next.Owner);
                next.Checkpoint.ShouldBe(owner.Checkpoint);
                return new { linkedPages = tail + 1, candidates = discovered, priorFence = owner.Fence, successorFence = next.Fence,
                    concurrentOwners = 1, staleOwnerWriteRefused = true, staleReleasePreservedSuccessor = true, checkpoint = next.Checkpoint };
            }
            finally { await second.ReleaseAsync(next, cancellationToken).ConfigureAwait(false); }
        }
        finally { await first.ReleaseAsync(owner, cancellationToken).ConfigureAwait(false); }
    }
}
