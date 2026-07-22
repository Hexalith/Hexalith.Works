using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;

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
            NullLogger<ReadModelCascadeCheckpointStore>.Instance);
        CascadeCheckpoint incomplete = CreateCheckpoint(CascadeTargetStatus.Pending, completed: false);

        await store.SaveAsync(incomplete, TestContext.Current.CancellationToken);

        CascadeCheckpointIdentity identity = (await store.GetIncompleteAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        identity.ShouldBe(new CascadeCheckpointIdentity(Tenant, Parent, TerminalType));

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
}
