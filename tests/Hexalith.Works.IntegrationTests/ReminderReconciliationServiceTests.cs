using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Deterministic startup-retry coverage for reminder reconciliation.</summary>
public sealed class ReminderReconciliationServiceTests
{
    [Fact]
    public async Task Incomplete_first_scan_is_retried_and_the_complete_scan_succeeds()
    {
        IPendingDateAwaitSource source = Substitute.For<IPendingDateAwaitSource>();
        var completeScanReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        source.GetPendingDateAwaitsAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                return Task.FromException<PendingDateAwaitScanResult>(new InvalidOperationException("incomplete scan"));
            }

            completeScanReached.TrySetResult(true);
            return Task.FromResult(new PendingDateAwaitScanResult([]));
        });
        var reconciler = new DateReminderReconciler(
            source,
            Substitute.For<IDateReminderScheduler>(),
            Substitute.For<IWorkCommandSubmitter>(),
            TimeProvider.System,
            NullLogger<DateReminderReconciler>.Instance);
        var logger = new Story48RecordingLogger<ReminderReconciliationService>();
        using var service = new ReminderReconciliationService(
            reconciler,
            Options.Create(new WorksRecoveryOptions
            {
                ReminderReconciliationMaxAttempts = 3,
                ReminderReconciliationRetryDelayMilliseconds = 0,
            }),
            logger,
            TimeProvider.System);

        await service.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await completeScanReached.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await service.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await source.Received(2).GetPendingDateAwaitsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Permanent_failure_finishes_naturally_after_exactly_the_configured_maximum_attempts()
    {
        const int maxAttempts = 3;
        IPendingDateAwaitSource source = Substitute.For<IPendingDateAwaitSource>();
        source.GetPendingDateAwaitsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PendingDateAwaitScanResult>(new InvalidOperationException("persistent failure")));
        var reconciler = new DateReminderReconciler(
            source,
            Substitute.For<IDateReminderScheduler>(),
            Substitute.For<IWorkCommandSubmitter>(),
            TimeProvider.System,
            NullLogger<DateReminderReconciler>.Instance);
        var logger = new Story48RecordingLogger<ReminderReconciliationService>();
        using var service = new ReminderReconciliationService(
            reconciler,
            Options.Create(new WorksRecoveryOptions
            {
                ReminderReconciliationMaxAttempts = maxAttempts,
                ReminderReconciliationRetryDelayMilliseconds = 0,
            }),
            logger,
            TimeProvider.System);

        await service.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await service.ExecuteTask!.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await source.Received(maxAttempts).GetPendingDateAwaitsAsync(Arg.Any<CancellationToken>());
        await source.DidNotReceive().GetPendingDateAwaitsAsync(Arg.Is<CancellationToken>(token => token.IsCancellationRequested));
        var failureLogs = logger.Entries.Where(entry => entry.EventId.Id == 4603).ToArray();
        failureLogs.Length.ShouldBe(maxAttempts);
        failureLogs.ShouldAllBe(entry => entry.Level == LogLevel.Warning);
        failureLogs.ShouldAllBe(entry => entry.Message.Contains("startup-reminder-reconciliation", StringComparison.Ordinal));
        failureLogs.ShouldAllBe(entry => !entry.Message.Contains("persistent failure", StringComparison.Ordinal));
        failureLogs.ShouldAllBe(entry => entry.Exception == null);
    }
}
