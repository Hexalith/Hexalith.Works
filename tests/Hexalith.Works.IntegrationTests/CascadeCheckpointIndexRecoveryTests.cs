using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Verifies durable incomplete-cascade discovery and restart replay convergence.
/// </summary>
public sealed class CascadeCheckpointIndexRecoveryTests
{
    private const string Tenant = "tenant-alpha";
    private const string Parent = "parent-001";
    private const string Child = "child-001";
    private const string TerminalType = "WorkItemCancelled";

    /// <summary>An incomplete save adds the identity and a completed save removes it.</summary>
    [Fact]
    public async Task Checkpoint_store_maintains_incomplete_index_lifecycle()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        CascadeCheckpoint incomplete = CreateCheckpoint(CascadeTargetStatus.Pending, completed: false);

        await store.SaveAsync(incomplete, TestContext.Current.CancellationToken);

        CascadeCheckpointIndexEntry entry = (await store.GetIncompleteAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        entry.Identity.ShouldBe(new CascadeCheckpointIdentity(Tenant, Parent, TerminalType));

        await store.SaveAsync(incomplete with { Completed = true }, TestContext.Current.CancellationToken);

        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    /// <summary>Startup recovery discovers an interrupted checkpoint from the index alone and a second pass is inert.</summary>
    [Fact]
    public async Task Startup_replay_converges_from_index_and_second_pass_is_noop()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        await store.SaveAsync(
            CreateCheckpoint(CascadeTargetStatus.Attempted, completed: false),
            TestContext.Current.CancellationToken);

        ICascadeDescendantSource source = Substitute.For<ICascadeDescendantSource>();
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var dispatcher = new CascadeDispatcher(store, source, submitter, NullLogger<CascadeDispatcher>.Instance);
        var reconciler = new CascadeRecoveryReconciler(
            store,
            dispatcher,
            TimeProvider.System,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<CascadeRecoveryReconciler>.Instance);

        int first = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);
        int second = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);

        first.ShouldBe(1);
        second.ShouldBe(0);
        await submitter.Received(1).SubmitAsync(
            Arg.Is<WorkCommandSubmission>(value => value != null && value.AggregateId == Child),
            Arg.Any<CancellationToken>());
        await source.DidNotReceiveWithAnyArgs().GetDescendantsAsync(default!, default!, Arg.Any<CancellationToken>());
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        CascadeCheckpoint recovered = (await store.GetAsync(Tenant, Parent, TerminalType, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        recovered.Completed.ShouldBeTrue();
        recovered.Targets.ShouldHaveSingleItem().Status.ShouldBe(CascadeTargetStatus.Completed);
    }

    /// <summary>An index entry with no matching checkpoint (the documented crash window) is pruned only once stale.</summary>
    [Fact]
    public async Task Stale_index_entry_with_no_checkpoint_is_pruned_after_threshold()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var timeProvider = new ManualTimeProvider();
        var store = new ReadModelCascadeCheckpointStore(readModels, timeProvider, NullLogger<ReadModelCascadeCheckpointStore>.Instance);

        // Simulate the crash window documented in ReadModelCascadeCheckpointStore.SaveAsync: the index entry
        // was added but its checkpoint was never written, by seeding the index directly without a checkpoint.
        var identity = new CascadeCheckpointIdentity(Tenant, Parent, TerminalType);
        await readModels.SaveAsync(
            "statestore",
            "projection:works:cascade-checkpoint-index",
            new CascadeCheckpointIndex { Entries = [new CascadeCheckpointIndexEntry(identity, timeProvider.GetUtcNow())] },
            TestContext.Current.CancellationToken);

        ICascadeDescendantSource source = Substitute.For<ICascadeDescendantSource>();
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var dispatcher = new CascadeDispatcher(store, source, submitter, NullLogger<CascadeDispatcher>.Instance);
        IOptions<WorksRecoveryOptions> options = Options.Create(new WorksRecoveryOptions { CascadeCheckpointIndexStaleAfterHours = 24 });
        var reconciler = new CascadeRecoveryReconciler(
            store,
            dispatcher,
            timeProvider,
            options,
            NullLogger<CascadeRecoveryReconciler>.Instance);

        int tooSoon = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);

        tooSoon.ShouldBe(0);
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        timeProvider.Advance(TimeSpan.FromHours(25));
        int afterThreshold = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);

        afterThreshold.ShouldBe(0, "pruning a stale entry is not a completed replay");
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        await submitter.DidNotReceiveWithAnyArgs().SubmitAsync(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>A huge stale-after configuration is clamped so TimeSpan.FromHours cannot overflow and abort the whole recovery pass.</summary>
    [Fact]
    public async Task Recovery_pass_survives_an_overflowing_stale_after_configuration()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var timeProvider = new ManualTimeProvider();
        var store = new ReadModelCascadeCheckpointStore(readModels, timeProvider, NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        var identity = new CascadeCheckpointIdentity(Tenant, Parent, TerminalType);
        await readModels.SaveAsync(
            "statestore",
            "projection:works:cascade-checkpoint-index",
            new CascadeCheckpointIndex { Entries = [new CascadeCheckpointIndexEntry(identity, timeProvider.GetUtcNow())] },
            TestContext.Current.CancellationToken);

        ICascadeDescendantSource source = Substitute.For<ICascadeDescendantSource>();
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var dispatcher = new CascadeDispatcher(store, source, submitter, NullLogger<CascadeDispatcher>.Instance);
        IOptions<WorksRecoveryOptions> options = Options.Create(new WorksRecoveryOptions { CascadeCheckpointIndexStaleAfterHours = int.MaxValue });
        var reconciler = new CascadeRecoveryReconciler(
            store,
            dispatcher,
            timeProvider,
            options,
            NullLogger<CascadeRecoveryReconciler>.Instance);

        // Without the clamp, TimeSpan.FromHours(int.MaxValue) throws OverflowException before the loop and aborts
        // the whole pass; with it, the pass runs and the effectively-never-prune threshold keeps the entry.
        timeProvider.Advance(TimeSpan.FromHours(1_000_000));
        int completed = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);

        completed.ShouldBe(0);
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
    }

    private static CascadeCheckpoint CreateCheckpoint(CascadeTargetStatus status, bool completed)
    {
        return new CascadeCheckpoint(
            Tenant,
            Parent,
            TerminalType,
            7,
            [new CascadeTargetCheckpoint(Child, CascadeCheckpoint.CancelKind, status, "cascade-cancel-tenant-alpha-parent-001-7-child-001")],
            completed);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
