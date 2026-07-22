using System.Text.Json;

using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class CascadeRecoveryRuntimeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = new("tenant-alpha");
    private static readonly WorkItemId Parent = new("parent-001");
    private static readonly WorkItemId ActiveChild = new("child-active");
    private static readonly WorkItemId TerminalChild = new("child-terminal");
    private static readonly WorkItemId SecondActiveChild = new("child-active-2");

    [Fact]
    public async Task New_cancel_cascade_persists_checkpoint_and_dispatches_only_active_descendants()
    {
        var store = new InMemoryCascadeCheckpointStore();
        var source = new FakeCascadeDescendantSource(
        [
            new CascadeDescendant(Tenant, ActiveChild, IsTerminal: false),
            new CascadeDescendant(Tenant, TerminalChild, IsTerminal: true),
        ]);
        var submitter = new RecordingWorkCommandSubmitter();
        var dispatcher = CreateDispatcher(store, source, submitter);

        await dispatcher.DispatchAsync(new WorkItemCancelled(Parent.Value, 7, Tenant, Parent), TestContext.Current.CancellationToken).ConfigureAwait(true);

        source.Reads.ShouldBe(1);
        submitter.Submissions.Count.ShouldBe(1);
        WorkCommandSubmission submission = submitter.Submissions.ShouldHaveSingleItem();
        submission.CommandType.ShouldBe(nameof(CancelWorkItem));
        submission.AggregateId.ShouldBe(ActiveChild.Value);
        submission.CorrelationId.ShouldBe(CascadeCommands.CorrelationId(Tenant.Value, Parent.Value, 7, ActiveChild.Value, CascadeCheckpoint.CancelKind));
        submission.Payload.Deserialize<CancelWorkItem>(Web)!.WorkItemId.ShouldBe(ActiveChild);

        CascadeCheckpoint checkpoint = store.LastSaved.ShouldNotBeNull();
        checkpoint.Completed.ShouldBeTrue();
        checkpoint.Targets.ShouldHaveSingleItem().Status.ShouldBe(CascadeTargetStatus.Completed);
        checkpoint.Targets.ShouldHaveSingleItem().DescendantWorkItemId.ShouldBe(ActiveChild.Value);
    }

    [Fact]
    public async Task Restart_replay_reissues_attempted_target_with_same_correlation_and_completes_checkpoint()
    {
        var store = new InMemoryCascadeCheckpointStore();
        var source = new FakeCascadeDescendantSource(
        [
            new CascadeDescendant(Tenant, ActiveChild, IsTerminal: false),
            new CascadeDescendant(Tenant, SecondActiveChild, IsTerminal: false),
        ]);
        var failingSubmitter = new RecordingWorkCommandSubmitter(failOnCall: 2);
        var dispatcher = CreateDispatcher(store, source, failingSubmitter);

        await Should.ThrowAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new WorkItemExpired(Parent.Value, 9, Tenant, Parent), TestContext.Current.CancellationToken)).ConfigureAwait(true);

        CascadeCheckpoint interrupted = store.LastSaved.ShouldNotBeNull();
        interrupted.Completed.ShouldBeFalse();
        interrupted.Targets[0].Status.ShouldBe(CascadeTargetStatus.Completed);
        interrupted.Targets[1].Status.ShouldBe(CascadeTargetStatus.Attempted);

        var replaySubmitter = new RecordingWorkCommandSubmitter();
        var replayDispatcher = CreateDispatcher(store, source, replaySubmitter);

        bool replayed = await replayDispatcher
            .ReplayAsync(Tenant.Value, Parent.Value, nameof(WorkItemExpired), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        replayed.ShouldBeTrue();
        source.Reads.ShouldBe(1, "Replay must read the persisted checkpoint, not rediscover an in-memory descendant list.");
        WorkCommandSubmission replayedSubmission = replaySubmitter.Submissions.ShouldHaveSingleItem();
        replayedSubmission.AggregateId.ShouldBe(SecondActiveChild.Value);
        replayedSubmission.CorrelationId.ShouldBe(interrupted.Targets[1].CorrelationId);
        store.LastSaved!.Completed.ShouldBeTrue();
        store.LastSaved.Targets.ShouldAllBe(target => target.Status == CascadeTargetStatus.Completed);
    }

    [Fact]
    public async Task Duplicate_parent_terminal_event_reuses_checkpoint_without_rediscovering_targets()
    {
        CascadeTargetCheckpoint completedTarget = new(
            ActiveChild.Value,
            CascadeCheckpoint.CancelKind,
            CascadeTargetStatus.Completed,
            CascadeCommands.CorrelationId(Tenant.Value, Parent.Value, 7, ActiveChild.Value, CascadeCheckpoint.CancelKind));
        var checkpoint = new CascadeCheckpoint(Tenant.Value, Parent.Value, nameof(WorkItemCancelled), 7, [completedTarget], Completed: true);
        var store = new InMemoryCascadeCheckpointStore(checkpoint);
        var source = new FakeCascadeDescendantSource([new CascadeDescendant(Tenant, SecondActiveChild, IsTerminal: false)]);
        var submitter = new RecordingWorkCommandSubmitter();
        var dispatcher = CreateDispatcher(store, source, submitter);

        await dispatcher.DispatchAsync(new WorkItemCancelled(Parent.Value, 7, Tenant, Parent), TestContext.Current.CancellationToken).ConfigureAwait(true);

        source.Reads.ShouldBe(0);
        submitter.Submissions.ShouldBeEmpty();
        store.LastSaved.ShouldBe(checkpoint);
    }

    [Fact]
    public void Cascade_command_submission_is_deterministic_and_rejects_unknown_kinds()
    {
        string correlationId = CascadeCommands.CorrelationId(Tenant.Value, Parent.Value, 7, ActiveChild.Value, CascadeCheckpoint.ExpireKind);

        WorkCommandSubmission submission = CascadeCommands.BuildSubmission(Tenant.Value, ActiveChild.Value, CascadeCheckpoint.ExpireKind, correlationId);

        submission.CommandType.ShouldBe(nameof(ExpireWorkItem));
        submission.CorrelationId.ShouldBe(correlationId);
        submission.CausationId.ShouldBe(correlationId);
        ExpireWorkItem command = submission.Payload.Deserialize<ExpireWorkItem>()!;
        command.TenantId.ShouldBe(Tenant);
        command.WorkItemId.ShouldBe(ActiveChild);
        Should.Throw<ArgumentOutOfRangeException>(() => CascadeCommands.BuildSubmission(Tenant.Value, ActiveChild.Value, "Complete", correlationId));
    }

    private static CascadeDispatcher CreateDispatcher(
        ICascadeCheckpointStore store,
        ICascadeDescendantSource source,
        IWorkCommandSubmitter submitter)
        => new(store, source, submitter, NullLogger<CascadeDispatcher>.Instance);

    private sealed class InMemoryCascadeCheckpointStore(CascadeCheckpoint? initial = null) : ICascadeCheckpointStore
    {
        private readonly Dictionary<string, CascadeCheckpoint> _checkpoints = initial is null
            ? new Dictionary<string, CascadeCheckpoint>(StringComparer.Ordinal)
            : new Dictionary<string, CascadeCheckpoint>(StringComparer.Ordinal)
            {
                [Key(initial.TenantId, initial.ParentWorkItemId, initial.ParentTerminalEventType)] = initial,
            };

        public CascadeCheckpoint? LastSaved { get; private set; } = initial;

        public Task<CascadeCheckpoint?> GetAsync(string tenantId, string parentWorkItemId, string parentTerminalEventType, CancellationToken cancellationToken = default)
        {
            _checkpoints.TryGetValue(Key(tenantId, parentWorkItemId, parentTerminalEventType), out CascadeCheckpoint? checkpoint);
            return Task.FromResult(checkpoint);
        }

        public Task SaveAsync(CascadeCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            LastSaved = checkpoint;
            _checkpoints[Key(checkpoint.TenantId, checkpoint.ParentWorkItemId, checkpoint.ParentTerminalEventType)] = checkpoint;
            return Task.CompletedTask;
        }

        private static string Key(string tenantId, string parentWorkItemId, string parentTerminalEventType)
            => $"{tenantId}:{parentWorkItemId}:{parentTerminalEventType}";
    }

    private sealed class FakeCascadeDescendantSource(IReadOnlyList<CascadeDescendant> descendants) : ICascadeDescendantSource
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<CascadeDescendant>> GetDescendantsAsync(string tenantId, string parentWorkItemId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(descendants);
        }
    }

    private sealed class RecordingWorkCommandSubmitter(int? failOnCall = null) : IWorkCommandSubmitter
    {
        private int _calls;

        public List<WorkCommandSubmission> Submissions { get; } = [];

        public Task SubmitAsync(WorkCommandSubmission submission, CancellationToken cancellationToken = default)
        {
            _calls++;
            Submissions.Add(submission);
            if (failOnCall == _calls)
            {
                throw new InvalidOperationException("Injected dispatch failure.");
            }

            return Task.CompletedTask;
        }
    }
}
