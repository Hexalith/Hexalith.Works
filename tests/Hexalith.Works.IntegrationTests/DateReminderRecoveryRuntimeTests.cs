using System.Text.Json;

using Hexalith.EventStore.Contracts.Events;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task Reconciler_acts_on_partial_results_then_rethrows_when_the_source_scan_is_incomplete()
    {
        // A source reporting a partial, incomplete scan (Story 4.8 code-review remediation: one unreadable
        // tenant must not block reissue/reschedule for the tenants that did scan cleanly) must still have its
        // good-tenant results acted upon, and the reconciler must still surface the pass as incomplete so the
        // caller (ReminderReconciliationService) retries it.
        var partialResults = new List<PendingDateAwait>
        {
            new("tenant-alpha", "due-work", DueInstant, AwaitCondition.DateReached(DueInstant).CorrelationKey),
        };
        var source = new IncompleteScanPendingDateAwaitSource(
            partialResults,
            failedTenantCount: 1,
            skippedParkedCount: 2);
        var scheduler = new RecordingReminderScheduler();
        var submitter = new RecordingWorkCommandSubmitter();
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var logger = new Story48RecordingLogger<DateReminderReconciler>();
        var reconciler = new DateReminderReconciler(
            source,
            scheduler,
            submitter,
            timeProvider,
            logger);

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => reconciler.ReconcileAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(1);
        thrown.SkippedParkedCount.ShouldBe(2);
        submitter.Submissions.ShouldHaveSingleItem(
            "The tenant that scanned cleanly must still be acted upon even though the overall pass is incomplete.");
        var incompleteLog = logger.Entries
            .Where(entry => entry.EventId.Id == 4605)
            .ShouldHaveSingleItem();
        incompleteLog.Level.ShouldBe(LogLevel.Warning);
        incompleteLog.Message.ShouldContain("2 parked");
        incompleteLog.Exception.ShouldBeSameAs(thrown);
    }

    [Fact]
    public async Task Reconciler_rewrap_retains_scan_counts_and_both_causes_when_partial_submission_fails()
    {
        var scanCause = new InvalidOperationException("simulated candidate scan failure");
        var source = new IncompleteScanPendingDateAwaitSource(
            [new PendingDateAwait("tenant-alpha", "due-work", DueInstant, AwaitCondition.DateReached(DueInstant).CorrelationKey)],
            failedTenantCount: 2,
            skippedParkedCount: 4,
            failedCandidateCount: 3,
            innerException: scanCause);
        var processingCause = new InvalidOperationException("simulated resume submission failure");
        var reconciler = new DateReminderReconciler(
            source,
            new RecordingReminderScheduler(),
            new ThrowingWorkCommandSubmitter(processingCause),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<DateReminderReconciler>.Instance);

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => reconciler.ReconcileAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(2);
        thrown.FailedCandidateCount.ShouldBe(3);
        thrown.SkippedParkedCount.ShouldBe(4);
        thrown.PartialResults.ShouldBeSameAs(source.ScanException.PartialResults);
        AggregateException aggregate = thrown.InnerException.ShouldBeOfType<AggregateException>();
        aggregate.InnerExceptions.Count.ShouldBe(2);
        aggregate.InnerExceptions[0].ShouldBeSameAs(source.ScanException);
        source.ScanException.InnerException.ShouldBeSameAs(scanCause);
        aggregate.InnerExceptions[1].ShouldBeSameAs(processingCause);
    }

    [Fact]
    public async Task Incomplete_scan_exact_caller_scheduling_cancellation_escapes_unwrapped_and_warning_free()
    {
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        var expected = new OperationCanceledException(callerCancellation.Token);
        var source = new IncompleteScanPendingDateAwaitSource(
            [new PendingDateAwait("tenant-alpha", "future-work", FutureInstant, AwaitCondition.DateReached(FutureInstant).CorrelationKey)],
            failedTenantCount: 2,
            skippedParkedCount: 4,
            failedCandidateCount: 3);
        var logger = new Story48RecordingLogger<DateReminderReconciler>();
        var reconciler = new DateReminderReconciler(
            source,
            new ThrowingReminderScheduler(expected),
            new RecordingWorkCommandSubmitter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)),
            logger);

        OperationCanceledException thrown = await Should
            .ThrowAsync<OperationCanceledException>(() => reconciler.ReconcileAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.CancellationToken.ShouldBe(callerCancellation.Token);
        thrown.ShouldNotBeOfType<PendingDateAwaitScanIncompleteException>();
        logger.Entries.Where(entry => entry.EventId.Id == 4605).ShouldHaveSingleItem();
        logger.Entries.ShouldNotContain(entry => entry.EventId.Id == 4609);
    }

    [Fact]
    public async Task Incomplete_scan_foreign_scheduling_cancellation_is_rewrapped_with_counts_and_both_causes()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var schedulerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        schedulerCancellation.Cancel();
        var scanCause = new InvalidOperationException("simulated candidate scan failure");
        var source = new IncompleteScanPendingDateAwaitSource(
            [new PendingDateAwait("tenant-alpha", "future-work", FutureInstant, AwaitCondition.DateReached(FutureInstant).CorrelationKey)],
            failedTenantCount: 2,
            skippedParkedCount: 4,
            failedCandidateCount: 3,
            innerException: scanCause);
        var schedulingCause = new OperationCanceledException(schedulerCancellation.Token);
        var logger = new Story48RecordingLogger<DateReminderReconciler>();
        var reconciler = new DateReminderReconciler(
            source,
            new ThrowingReminderScheduler(schedulingCause),
            new RecordingWorkCommandSubmitter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)),
            logger);

        PendingDateAwaitScanIncompleteException thrown = await Should
            .ThrowAsync<PendingDateAwaitScanIncompleteException>(() => reconciler.ReconcileAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.FailedTenantCount.ShouldBe(2);
        thrown.FailedCandidateCount.ShouldBe(3);
        thrown.SkippedParkedCount.ShouldBe(4);
        thrown.PartialResults.ShouldBeSameAs(source.ScanException.PartialResults);
        AggregateException aggregate = thrown.InnerException.ShouldBeOfType<AggregateException>();
        aggregate.InnerExceptions.Count.ShouldBe(2);
        aggregate.InnerExceptions[0].ShouldBeSameAs(source.ScanException);
        source.ScanException.InnerException.ShouldBeSameAs(scanCause);
        OperationCanceledException processingCause = aggregate.InnerExceptions[1].ShouldBeAssignableTo<OperationCanceledException>();
        processingCause.CancellationToken.ShouldBe(schedulerCancellation.Token);
        OperationCanceledException logged = logger.Entries
            .Where(entry => entry.EventId.Id == 4609)
            .ShouldHaveSingleItem()
            .Exception.ShouldBeOfType<OperationCanceledException>();
        logged.ShouldBeSameAs(schedulingCause);
    }

    [Fact]
    public async Task Reconciler_exposes_parked_skips_from_a_clean_scan_without_making_them_retryable()
    {
        var reconciler = new DateReminderReconciler(
            new FakePendingDateAwaitSource([], skippedParkedCount: 1),
            new RecordingReminderScheduler(),
            new RecordingWorkCommandSubmitter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<DateReminderReconciler>.Instance);

        ReminderReconciliationOutcome outcome = await reconciler
            .ReconcileAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        outcome.ShouldBe(new ReminderReconciliationOutcome(Reissued: 0, Rescheduled: 0, SkippedParkedCount: 1));
    }

    [Fact]
    public async Task Recovery_scheduler_failure_emits_bounded_warning_and_rethrows_the_original_exception()
    {
        var pending = new PendingDateAwait(
            "tenant-alpha",
            "future-work",
            FutureInstant,
            AwaitCondition.DateReached(FutureInstant).CorrelationKey);
        var expected = new InvalidOperationException("sensitive scheduler detail");
        var logger = new Story48RecordingLogger<DateReminderReconciler>();
        var reconciler = new DateReminderReconciler(
            new FakePendingDateAwaitSource([pending]),
            new ThrowingReminderScheduler(expected),
            new RecordingWorkCommandSubmitter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)),
            logger);

        InvalidOperationException thrown = await Should
            .ThrowAsync<InvalidOperationException>(() => reconciler.ReconcileAsync(TestContext.Current.CancellationToken))
            .ConfigureAwait(true);

        thrown.ShouldBeSameAs(expected);
        var failureLog = logger.Entries
            .Where(entry => entry.EventId.Id == 4609)
            .ShouldHaveSingleItem();
        failureLog.Level.ShouldBe(LogLevel.Warning);
        failureLog.Message.ShouldContain(nameof(InvalidOperationException));
        failureLog.Message.ShouldContain(DateReminderName.For(
            pending.TenantId,
            pending.WorkItemId,
            pending.CorrelationKey));
        failureLog.Message.ShouldNotContain(expected.Message);
        failureLog.Exception.ShouldBeSameAs(expected);
        failureLog.Properties["TenantId"].ShouldBe(pending.TenantId);
        failureLog.Properties["WorkItemId"].ShouldBe(pending.WorkItemId);
        failureLog.Properties["ReminderName"].ShouldBe(DateReminderName.For(
            pending.TenantId,
            pending.WorkItemId,
            pending.CorrelationKey));
        failureLog.Properties["Reason"].ShouldBe(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Recovery_caller_cancellation_rethrows_without_a_scheduling_failure_warning()
    {
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        var expected = new OperationCanceledException(callerCancellation.Token);
        var logger = new Story48RecordingLogger<DateReminderReconciler>();
        var reconciler = new DateReminderReconciler(
            new FakePendingDateAwaitSource(
            [
                new PendingDateAwait(
                    "tenant-alpha",
                    "future-work",
                    FutureInstant,
                    AwaitCondition.DateReached(FutureInstant).CorrelationKey),
            ]),
            new ThrowingReminderScheduler(expected),
            new RecordingWorkCommandSubmitter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)),
            logger);

        OperationCanceledException thrown = await Should
            .ThrowAsync<OperationCanceledException>(() => reconciler.ReconcileAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.CancellationToken.ShouldBe(callerCancellation.Token);
        logger.Entries.ShouldNotContain(entry => entry.EventId.Id == 4609);
    }

    [Fact]
    public async Task Recovery_foreign_cancellation_warns_when_the_caller_token_is_also_canceled()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var schedulerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        schedulerCancellation.Cancel();
        var expected = new OperationCanceledException(schedulerCancellation.Token);
        var logger = new Story48RecordingLogger<DateReminderReconciler>();
        var reconciler = new DateReminderReconciler(
            new FakePendingDateAwaitSource(
            [
                new PendingDateAwait(
                    "tenant-alpha",
                    "future-work",
                    FutureInstant,
                    AwaitCondition.DateReached(FutureInstant).CorrelationKey),
            ]),
            new ThrowingReminderScheduler(expected),
            new RecordingWorkCommandSubmitter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)),
            logger);

        OperationCanceledException thrown = await Should
            .ThrowAsync<OperationCanceledException>(() => reconciler.ReconcileAsync(callerCancellation.Token))
            .ConfigureAwait(true);

        thrown.CancellationToken.ShouldBe(schedulerCancellation.Token);
        OperationCanceledException logged = logger.Entries
            .Where(entry => entry.EventId.Id == 4609)
            .ShouldHaveSingleItem()
            .Exception.ShouldBeOfType<OperationCanceledException>();
        logged.ShouldBeSameAs(expected);
        logged.CancellationToken.ShouldBe(schedulerCancellation.Token);
    }

    private sealed class FakePendingDateAwaitSource(
        IReadOnlyList<PendingDateAwait> awaits,
        int skippedParkedCount = 0) : IPendingDateAwaitSource
    {
        public Task<PendingDateAwaitScanResult> GetPendingDateAwaitsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PendingDateAwaitScanResult(awaits, skippedParkedCount));
    }

    private sealed class IncompleteScanPendingDateAwaitSource : IPendingDateAwaitSource
    {
        public IncompleteScanPendingDateAwaitSource(
            IReadOnlyList<PendingDateAwait> partialResults,
            int failedTenantCount,
            int skippedParkedCount,
            int failedCandidateCount = 0,
            Exception? innerException = null)
        {
            ScanException = new PendingDateAwaitScanIncompleteException(
                partialResults,
                failedTenantCount,
                failedCandidateCount,
                skippedParkedCount,
                innerException ?? new InvalidOperationException("simulated tenant scan failure"));
        }

        public PendingDateAwaitScanIncompleteException ScanException { get; }

        public Task<PendingDateAwaitScanResult> GetPendingDateAwaitsAsync(CancellationToken cancellationToken = default)
            => throw ScanException;
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

    private sealed class ThrowingReminderScheduler(Exception exception) : IDateReminderScheduler
    {
        public Task ScheduleResumeReminderAsync(
            PendingDateAwait @await,
            TimeSpan dueTime,
            CancellationToken cancellationToken = default)
            => Task.FromException(exception);
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

    private sealed class ThrowingWorkCommandSubmitter(Exception exception) : IWorkCommandSubmitter
    {
        public Task SubmitAsync(WorkCommandSubmission submission, CancellationToken cancellationToken = default)
            => Task.FromException(exception);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
