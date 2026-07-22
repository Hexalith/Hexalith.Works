using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class DateReminderRecoveryRuntimeTests
{
    private static readonly DateTimeOffset DueInstant = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FutureInstant = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reminder_name_is_deterministic_and_uses_only_bounded_identity_fields()
    {
        string key = AwaitCondition.DateReached(DueInstant).CorrelationKey;

        string first = DateReminderName.For("tenant-alpha", "work-001", key);
        string second = DateReminderName.For("tenant-alpha", "work-001", key);
        string differentAwait = DateReminderName.For("tenant-alpha", "work-001", AwaitCondition.DateReached(FutureInstant).CorrelationKey);
        string actor = DateReminderName.ActorId("tenant-alpha", "work-001");

        first.ShouldBe(second);
        first.ShouldStartWith($"{DateReminderName.Prefix}-");
        first.ShouldNotBe(differentAwait);
        actor.ShouldStartWith($"{DateReminderName.Prefix}-");
        first.ShouldNotContain(DueInstant.ToString("O"));
        first.ShouldNotContain("attempt", Case.Insensitive);
    }

    [Fact]
    public void Date_resume_submission_carries_the_deterministic_date_await_condition()
    {
        WorkCommandSubmission submission = DateResume.BuildSubmission("tenant-alpha", "work-001", DueInstant);

        submission.Tenant.ShouldBe("tenant-alpha");
        submission.AggregateId.ShouldBe("work-001");
        submission.CommandType.ShouldBe(nameof(ResumeWorkItem));
        submission.CorrelationId.ShouldBe(submission.CausationId);

        ResumeWorkItem command = submission.Payload.Deserialize<ResumeWorkItem>()!;
        AwaitCondition condition = AwaitCondition.DateReached(DueInstant);
        command.TenantId.Value.ShouldBe("tenant-alpha");
        command.WorkItemId.Value.ShouldBe("work-001");
        AwaitCondition actualCondition = command.AwaitCondition.ShouldNotBeNull();
        actualCondition.ShouldBe(condition);
        actualCondition.CorrelationKey.ShouldBe(condition.CorrelationKey);
    }

    [Fact]
    public void Pending_date_projection_keeps_only_the_latest_uncleared_date_awaits()
    {
        var tenant = new TenantId("tenant-alpha");
        var workItem = new WorkItemId("work-001");
        IEventPayload[] events =
        [
            new WorkItemSuspended("work-001", 1, tenant, workItem, [AwaitCondition.DateReached(DueInstant)]),
            new WorkItemResumed("work-001", 2, tenant, workItem, AwaitCondition.DateReached(DueInstant)),
            new WorkItemSuspended("work-001", 3, tenant, workItem, [AwaitCondition.ExternalSignal("approval")]),
            new WorkItemSuspended("work-001", 4, tenant, workItem, [AwaitCondition.DateReached(FutureInstant)]),
        ];

        PendingDateAwait pending = PendingDateAwaitProjection.PendingDateAwaits(events).ShouldHaveSingleItem();

        pending.TenantId.ShouldBe("tenant-alpha");
        pending.WorkItemId.ShouldBe("work-001");
        pending.Instant.ShouldBe(FutureInstant);
        pending.CorrelationKey.ShouldBe(AwaitCondition.DateReached(FutureInstant).CorrelationKey);
    }

    [Fact]
    public void Terminal_event_clears_pending_date_awaits()
    {
        var tenant = new TenantId("tenant-alpha");
        var workItem = new WorkItemId("work-001");
        IEventPayload[] events =
        [
            new WorkItemSuspended("work-001", 1, tenant, workItem, [AwaitCondition.DateReached(DueInstant)]),
            new WorkItemCancelled("work-001", 2, tenant, workItem),
        ];

        PendingDateAwaitProjection.PendingDateAwaits(events).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reconciler_reissues_due_awaits_and_reschedules_future_awaits_idempotently()
    {
        var source = new FakePendingDateAwaitSource(
        [
            new PendingDateAwait("tenant-alpha", "due-work", DueInstant, AwaitCondition.DateReached(DueInstant).CorrelationKey),
            new PendingDateAwait("tenant-alpha", "future-work", FutureInstant, AwaitCondition.DateReached(FutureInstant).CorrelationKey),
        ]);
        var scheduler = new RecordingReminderScheduler();
        var submitter = new RecordingWorkCommandSubmitter();
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var reconciler = new DateReminderReconciler(
            source,
            scheduler,
            submitter,
            timeProvider,
            NullLogger<DateReminderReconciler>.Instance);

        ReminderReconciliationOutcome first = await reconciler.ReconcileAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        ReminderReconciliationOutcome second = await reconciler.ReconcileAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        first.ShouldBe(new ReminderReconciliationOutcome(Reissued: 1, Rescheduled: 1));
        second.ShouldBe(new ReminderReconciliationOutcome(Reissued: 1, Rescheduled: 1));
        submitter.Submissions.Count.ShouldBe(2, "Reissuing is at-least-once; the deterministic command id makes it idempotent downstream.");
        submitter.Submissions.Select(s => s.CorrelationId).Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
        scheduler.Registrations.Count.ShouldBe(1, "Duplicate reconciliation overwrites the same deterministic reminder registration.");
        scheduler.Registrations.Keys.ShouldHaveSingleItem().ShouldContain(DateReminderName.Prefix);
    }

    private sealed class FakePendingDateAwaitSource(IReadOnlyList<PendingDateAwait> awaits) : IPendingDateAwaitSource
    {
        public Task<IReadOnlyList<PendingDateAwait>> GetPendingDateAwaitsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(awaits);
    }

    private sealed class RecordingReminderScheduler : IDateReminderScheduler
    {
        public Dictionary<string, TimeSpan> Registrations { get; } = new(StringComparer.Ordinal);

        public Task ScheduleResumeReminderAsync(PendingDateAwait @await, TimeSpan dueTime, CancellationToken cancellationToken = default)
        {
            string name = DateReminderName.For(@await.TenantId, @await.WorkItemId, @await.CorrelationKey);
            Registrations[name] = dueTime;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingWorkCommandSubmitter : IWorkCommandSubmitter
    {
        public List<WorkCommandSubmission> Submissions { get; } = [];

        public Task SubmitAsync(WorkCommandSubmission submission, CancellationToken cancellationToken = default)
        {
            Submissions.Add(submission);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
