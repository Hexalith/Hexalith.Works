using Hexalith.EventStore.Client.Projections;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
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
    private const string SecondChild = "child-002";
    private const string TerminalType = "WorkItemCancelled";
    private const string StateStoreName = "statestore";
    private const string IndexKey = "projection:works:cascade-checkpoint-index";

    /// <summary>Only absent-to-incomplete and incomplete-to-completed saves rewrite discovery.</summary>
    [Fact]
    public async Task Checkpoint_store_writes_index_only_at_lifecycle_transitions()
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
        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(1);
        readModels.GetSuccessfulWriteCount(StateStoreName, CheckpointKey()).ShouldBe(1);
        readModels.SuccessfulWriteKeys.ShouldBe(
        [
            ScopedKey(IndexKey),
            ScopedKey(CheckpointKey()),
        ]);

        readModels.ResetSuccessfulWriteObservation();
        CascadeCheckpoint attempted = incomplete with
        {
            Targets = [incomplete.Targets[0] with { Status = CascadeTargetStatus.Attempted }],
        };

        await store.SaveAsync(attempted, TestContext.Current.CancellationToken);

        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(0);
        readModels.GetSuccessfulWrites<CascadeCheckpoint>(StateStoreName, CheckpointKey())
            .ShouldHaveSingleItem()
            .Targets.ShouldHaveSingleItem().Status.ShouldBe(CascadeTargetStatus.Attempted);

        readModels.ResetSuccessfulWriteObservation();
        CascadeCheckpoint completed = attempted with
        {
            Targets = [attempted.Targets[0] with { Status = CascadeTargetStatus.Completed }],
            Completed = true,
        };

        await store.SaveAsync(completed, TestContext.Current.CancellationToken);

        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(1);
        readModels.GetSuccessfulWriteCount(StateStoreName, CheckpointKey()).ShouldBe(1);
        readModels.SuccessfulWriteKeys.ShouldBe(
        [
            ScopedKey(CheckpointKey()),
            ScopedKey(IndexKey),
        ]);
    }

    /// <summary>A failed first checkpoint write leaves its earlier discovery publication intact.</summary>
    [Fact]
    public async Task First_incomplete_checkpoint_failure_retains_discovery_for_pruning()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        readModels.FailNextSaves(StateStoreName, CheckpointKey());

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => store.SaveAsync(CreateCheckpoint(CascadeTargetStatus.Pending, completed: false), TestContext.Current.CancellationToken));

        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .Identity.ShouldBe(new CascadeCheckpointIdentity(Tenant, Parent, TerminalType));
        (await store.GetAsync(Tenant, Parent, TerminalType, TestContext.Current.CancellationToken)).ShouldBeNull();
        readModels.SuccessfulWriteKeys.ShouldBe([ScopedKey(IndexKey)]);
    }

    /// <summary>A failed intermediate progress save propagates to the caller and never touches discovery.</summary>
    [Fact]
    public async Task Intermediate_progress_failure_propagates_and_leaves_discovery_intact()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        CascadeCheckpoint incomplete = CreateCheckpoint(CascadeTargetStatus.Pending, completed: false);
        await store.SaveAsync(incomplete, TestContext.Current.CancellationToken);
        readModels.ResetSuccessfulWriteObservation();
        readModels.FailNextSaves(StateStoreName, CheckpointKey());

        // Intent matrix, intermediate progress: the checkpoint failure must reach the dispatcher so delivery can
        // retry, and the still-incomplete cascade must stay discoverable without rewriting the global index.
        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => store.SaveAsync(
                incomplete with { Targets = [incomplete.Targets[0] with { Status = CascadeTargetStatus.Attempted }] },
                TestContext.Current.CancellationToken));

        readModels.SuccessfulWriteKeys.ShouldBeEmpty();
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .Identity.ShouldBe(new CascadeCheckpointIdentity(Tenant, Parent, TerminalType));
        CascadeCheckpoint durable = (await store.GetAsync(Tenant, Parent, TerminalType, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        durable.Targets.ShouldHaveSingleItem().Status.ShouldBe(CascadeTargetStatus.Pending);
    }

    /// <summary>A failed index removal cannot roll back durable completion and a recovery pass clears it later.</summary>
    [Fact]
    public async Task Completion_removal_failure_keeps_durable_completion_reconcilable()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        CascadeCheckpoint incomplete = CreateCheckpoint(CascadeTargetStatus.Completed, completed: false);
        await store.SaveAsync(incomplete, TestContext.Current.CancellationToken);
        readModels.ResetSuccessfulWriteObservation();
        readModels.RejectNextTrySaves(StateStoreName, IndexKey, ReadModelWritePolicy.DefaultMaxAttempts);

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => store.SaveAsync(incomplete with { Completed = true }, TestContext.Current.CancellationToken));

        CascadeCheckpoint durable = (await store.GetAsync(Tenant, Parent, TerminalType, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        durable.Completed.ShouldBeTrue();
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        readModels.SuccessfulWriteKeys.ShouldBe([ScopedKey(CheckpointKey())]);

        var dispatcher = new CascadeDispatcher(
            store,
            Substitute.For<ICascadeDescendantSource>(),
            Substitute.For<IWorkCommandSubmitter>(),
            NullLogger<CascadeDispatcher>.Instance);
        var reconciler = new CascadeRecoveryReconciler(
            store,
            store,
            dispatcher,
            TimeProvider.System,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<CascadeRecoveryReconciler>.Instance);

        (await reconciler.RecoverAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(1);
    }

    /// <summary>A durable completed checkpoint cannot regress to incomplete or restore discovery.</summary>
    [Fact]
    public async Task Completed_checkpoint_rejects_incomplete_regression_without_persisting()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        CascadeCheckpoint incomplete = CreateCheckpoint(CascadeTargetStatus.Completed, completed: false);
        CascadeCheckpoint completed = incomplete with { Completed = true };
        await store.SaveAsync(incomplete, TestContext.Current.CancellationToken);
        await store.SaveAsync(completed, TestContext.Current.CancellationToken);
        readModels.ResetSuccessfulWriteObservation();

        _ = await Should.ThrowAsync<InvalidOperationException>(
            () => store.SaveAsync(incomplete, TestContext.Current.CancellationToken));

        (await store.GetAsync(Tenant, Parent, TerminalType, TestContext.Current.CancellationToken))
            .ShouldBe(completed);
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        readModels.SuccessfulWriteKeys.ShouldBeEmpty();
    }

    /// <summary>A real multi-target dispatch performs one discovery add and one removal around all progress saves.</summary>
    [Fact]
    public async Task Multi_target_dispatch_persists_progress_without_index_amplification()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        ICascadeDescendantSource source = Substitute.For<ICascadeDescendantSource>();
        source.GetDescendantsAsync(Tenant, Parent, Arg.Any<CancellationToken>())
            .Returns(
            [
                new CascadeDescendant(new TenantId(Tenant), new WorkItemId(Child), IsTerminal: false),
                new CascadeDescendant(new TenantId(Tenant), new WorkItemId(SecondChild), IsTerminal: false),
            ]);
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var dispatcher = new CascadeDispatcher(store, source, submitter, NullLogger<CascadeDispatcher>.Instance);

        await dispatcher.DispatchAsync(
            new WorkItemCancelled(Parent, 7, new TenantId(Tenant), new WorkItemId(Parent)),
            TestContext.Current.CancellationToken);

        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(2);
        IReadOnlyList<CascadeCheckpoint> checkpoints = readModels
            .GetSuccessfulWrites<CascadeCheckpoint>(StateStoreName, CheckpointKey());
        checkpoints.Count.ShouldBe(6);
        checkpoints[0].Completed.ShouldBeFalse();
        checkpoints[0].Targets.Select(static value => value.Status).ShouldBe(
            [CascadeTargetStatus.Pending, CascadeTargetStatus.Pending]);
        checkpoints[1].Targets.Select(static value => value.Status).ShouldBe(
            [CascadeTargetStatus.Attempted, CascadeTargetStatus.Pending]);
        checkpoints[2].Targets.Select(static value => value.Status).ShouldBe(
            [CascadeTargetStatus.Completed, CascadeTargetStatus.Pending]);
        checkpoints[3].Targets.Select(static value => value.Status).ShouldBe(
            [CascadeTargetStatus.Completed, CascadeTargetStatus.Attempted]);
        checkpoints[4].Targets.Select(static value => value.Status).ShouldBe(
            [CascadeTargetStatus.Completed, CascadeTargetStatus.Completed]);
        checkpoints[4].Completed.ShouldBeFalse();
        checkpoints[5].Targets.Select(static value => value.Status).ShouldBe(
            [CascadeTargetStatus.Completed, CascadeTargetStatus.Completed]);
        checkpoints[5].Completed.ShouldBeTrue();
        readModels.SuccessfulWriteKeys.First().ShouldBe(ScopedKey(IndexKey));
        readModels.SuccessfulWriteKeys.Last().ShouldBe(ScopedKey(IndexKey));
        await submitter.Received(2).SubmitAsync(Arg.Any<WorkCommandSubmission>(), Arg.Any<CancellationToken>());
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    /// <summary>A target-free cascade persists completed immediately without ever publishing discovery.</summary>
    [Fact]
    public async Task Empty_cascade_completion_does_not_write_discovery_index()
    {
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        ICascadeDescendantSource source = Substitute.For<ICascadeDescendantSource>();
        source.GetDescendantsAsync(Tenant, Parent, Arg.Any<CancellationToken>()).Returns([]);
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var dispatcher = new CascadeDispatcher(store, source, submitter, NullLogger<CascadeDispatcher>.Instance);

        await dispatcher.DispatchAsync(
            new WorkItemCancelled(Parent, 7, new TenantId(Tenant), new WorkItemId(Parent)),
            TestContext.Current.CancellationToken);

        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(0);
        CascadeCheckpoint completed = readModels
            .GetSuccessfulWrites<CascadeCheckpoint>(StateStoreName, CheckpointKey())
            .ShouldHaveSingleItem();
        completed.Completed.ShouldBeTrue();
        completed.Targets.ShouldBeEmpty();
        await submitter.DidNotReceiveWithAnyArgs().SubmitAsync(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>Concurrent first saves retry and merge distinct tenant identities into the singleton index.</summary>
    [Fact]
    public async Task Concurrent_cross_tenant_first_saves_preserve_both_identities()
    {
        const string otherTenant = "tenant-beta";
        const string otherParent = "parent-002";
        var readModels = new Story47InMemoryReadModelStore();
        var firstStore = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        var secondStore = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        readModels.CoordinateFirstTrySaveConflict(StateStoreName, IndexKey);

        await Task.WhenAll(
            firstStore.SaveAsync(
                CreateCheckpoint(CascadeTargetStatus.Pending, completed: false),
                TestContext.Current.CancellationToken),
            secondStore.SaveAsync(
                CreateCheckpoint(
                    CascadeTargetStatus.Pending,
                    completed: false,
                    tenantId: otherTenant,
                    parentWorkItemId: otherParent,
                    childWorkItemId: SecondChild),
                TestContext.Current.CancellationToken));

        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(2);
        IReadOnlyList<CascadeCheckpointIndexEntry> entries = await firstStore
            .GetIncompleteAsync(TestContext.Current.CancellationToken);
        entries.Select(static value => value.Identity).ShouldBe(
        [
            new CascadeCheckpointIdentity(Tenant, Parent, TerminalType),
            new CascadeCheckpointIdentity(otherTenant, otherParent, TerminalType),
        ]);
    }

    /// <summary>Multi-entry restart recovery removes each identity once and leaves its second pass inert.</summary>
    [Fact]
    public async Task Multi_entry_recovery_performs_one_lifecycle_removal_per_identity()
    {
        const string otherTenant = "tenant-beta";
        const string otherParent = "parent-002";
        var readModels = new Story47InMemoryReadModelStore();
        var store = new ReadModelCascadeCheckpointStore(
            readModels,
            TimeProvider.System,
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        await store.SaveAsync(
            CreateCheckpoint(CascadeTargetStatus.Attempted, completed: false),
            TestContext.Current.CancellationToken);
        await store.SaveAsync(
            CreateCheckpoint(
                CascadeTargetStatus.Attempted,
                completed: false,
                tenantId: otherTenant,
                parentWorkItemId: otherParent,
                childWorkItemId: SecondChild),
            TestContext.Current.CancellationToken);
        readModels.ResetSuccessfulWriteObservation();

        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var dispatcher = new CascadeDispatcher(
            store,
            Substitute.For<ICascadeDescendantSource>(),
            submitter,
            NullLogger<CascadeDispatcher>.Instance);
        var reconciler = new CascadeRecoveryReconciler(
            store,
            store,
            dispatcher,
            TimeProvider.System,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<CascadeRecoveryReconciler>.Instance);

        int first = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);
        int checkpointWritesAfterFirstPass = readModels.SuccessfulWriteKeys
            .Count(static value => value.Contains("cascade-checkpoint:", StringComparison.Ordinal));
        int indexWritesAfterFirstPass = readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey);
        int second = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);

        first.ShouldBe(2);
        second.ShouldBe(0);
        checkpointWritesAfterFirstPass.ShouldBe(6);
        indexWritesAfterFirstPass.ShouldBe(2);
        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(indexWritesAfterFirstPass);
        IReadOnlyList<CascadeCheckpointIndex> indexWrites = readModels
            .GetSuccessfulWrites<CascadeCheckpointIndex>(StateStoreName, IndexKey);
        indexWrites[0].Entries.ShouldHaveSingleItem().Identity.ShouldBe(
            new CascadeCheckpointIdentity(otherTenant, otherParent, TerminalType));
        indexWrites[1].Entries.ShouldBeEmpty();
        (await store.GetAsync(Tenant, Parent, TerminalType, TestContext.Current.CancellationToken))!
            .Completed.ShouldBeTrue();
        (await store.GetAsync(otherTenant, otherParent, TerminalType, TestContext.Current.CancellationToken))!
            .Completed.ShouldBeTrue();
        await submitter.Received(2).SubmitAsync(Arg.Any<WorkCommandSubmission>(), Arg.Any<CancellationToken>());
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
        readModels.ResetSuccessfulWriteObservation();

        ICascadeDescendantSource source = Substitute.For<ICascadeDescendantSource>();
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var dispatcher = new CascadeDispatcher(store, source, submitter, NullLogger<CascadeDispatcher>.Instance);
        var reconciler = new CascadeRecoveryReconciler(
            store,
            store,
            dispatcher,
            TimeProvider.System,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<CascadeRecoveryReconciler>.Instance);

        int first = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);
        int checkpointWritesAfterFirstPass = readModels.GetSuccessfulWriteCount(StateStoreName, CheckpointKey());
        int indexWritesAfterFirstPass = readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey);
        int second = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);

        first.ShouldBe(1);
        second.ShouldBe(0);
        checkpointWritesAfterFirstPass.ShouldBe(3);
        indexWritesAfterFirstPass.ShouldBe(1);
        readModels.GetSuccessfulWriteCount(StateStoreName, CheckpointKey()).ShouldBe(checkpointWritesAfterFirstPass);
        readModels.GetSuccessfulWriteCount(StateStoreName, IndexKey).ShouldBe(indexWritesAfterFirstPass);
        int durableCompletionPosition = readModels.SuccessfulWriteKeys
            .Select((value, index) => (value, index))
            .Last(static value => value.value.EndsWith(CheckpointKey(), StringComparison.Ordinal))
            .index;
        int firstRemovalPosition = readModels.SuccessfulWriteKeys
            .Select((value, index) => (value, index))
            .First(static value => value.value.EndsWith(IndexKey, StringComparison.Ordinal))
            .index;
        durableCompletionPosition.ShouldBeLessThan(firstRemovalPosition);
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
            store,
            dispatcher,
            timeProvider,
            options,
            NullLogger<CascadeRecoveryReconciler>.Instance);

        int tooSoon = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);

        tooSoon.ShouldBe(0);
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        timeProvider.Advance(TimeSpan.FromHours(24));
        int atThreshold = await reconciler.RecoverAsync(TestContext.Current.CancellationToken);

        atThreshold.ShouldBe(0);
        (await store.GetIncompleteAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        timeProvider.Advance(TimeSpan.FromHours(1));
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

    /// <summary>The deterministic dedup correlation id keeps its exact persisted format.</summary>
    [Fact]
    public void Cascade_target_correlation_id_keeps_its_deterministic_format()
    {
        // Pin the literal wire format, not the production helper: replay and redelivery only stay no-ops at the
        // aggregate while a restarted or redeployed host rebuilds byte-identical correlation ids.
        CreateCheckpoint(CascadeTargetStatus.Pending, completed: false)
            .Targets.ShouldHaveSingleItem()
            .CorrelationId.ShouldBe("cascade-Cancel-tenant-alpha-parent-001-7-child-001");
    }

    private static CascadeCheckpoint CreateCheckpoint(
        CascadeTargetStatus status,
        bool completed,
        string tenantId = Tenant,
        string parentWorkItemId = Parent,
        string childWorkItemId = Child)
    {
        return new CascadeCheckpoint(
            tenantId,
            parentWorkItemId,
            TerminalType,
            7,
            [new CascadeTargetCheckpoint(
                childWorkItemId,
                CascadeCheckpoint.CancelKind,
                status,
                CascadeCommands.CorrelationId(tenantId, parentWorkItemId, 7, childWorkItemId, CascadeCheckpoint.CancelKind))],
            completed);
    }

    private static string CheckpointKey(string tenantId = Tenant, string parentWorkItemId = Parent)
        => $"projection:works:cascade-checkpoint:{tenantId}:{parentWorkItemId}:{TerminalType}";

    private static string ScopedKey(string key) => $"{StateStoreName}:{key}";

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
