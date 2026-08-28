using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

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
                return Task.FromException<IReadOnlyList<PendingDateAwait>>(new InvalidOperationException("incomplete scan"));
            }

            completeScanReached.TrySetResult(true);
            return Task.FromResult<IReadOnlyList<PendingDateAwait>>([]);
        });
        var reconciler = new DateReminderReconciler(
            source,
            Substitute.For<IDateReminderScheduler>(),
            Substitute.For<IWorkCommandSubmitter>(),
            TimeProvider.System,
            NullLogger<DateReminderReconciler>.Instance);
        using var service = new ReminderReconciliationService(
            reconciler,
            Options.Create(new WorksRecoveryOptions
            {
                ReminderReconciliationMaxAttempts = 3,
                ReminderReconciliationRetryDelayMilliseconds = 0,
            }),
            NullLogger<ReminderReconciliationService>.Instance,
            TimeProvider.System);

        await service.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await completeScanReached.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await service.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await source.Received(2).GetPendingDateAwaitsAsync(Arg.Any<CancellationToken>());
    }
}
