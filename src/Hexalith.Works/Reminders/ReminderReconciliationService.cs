using Hexalith.Works.Runtime;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Bounded hosted service that runs one reminder reconciliation pass on Works host startup (Story 4.6 AC #3,
/// Story 4.8 auto-discovery): it re-scans the durable pending <c>DateReached</c> awaits and re-registers
/// reminders / reissues due resumes so a firing lost to an AppHost restart is recovered. Story 4.8 removed the
/// hand-configured tenant gate — the pass runs whenever <see cref="WorksRecoveryOptions.RunReconciliationOnStartup"/>
/// is set (default), discovering tenants from the durable registry. It is fail-safe — a transient scan/dispatch
/// failure is logged with bounded metadata and never crashes the host, because the underlying scan and resumes
/// are idempotent and the next restart repeats them.
/// </summary>
public sealed class ReminderReconciliationService(
    DateReminderReconciler reconciler,
    IOptions<WorksRecoveryOptions> options,
    ILogger<ReminderReconciliationService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly DateReminderReconciler _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ILogger<ReminderReconciliationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RunReconciliationOnStartup)
        {
            return;
        }

        for (int attempt = 1; attempt <= _options.ReminderReconciliationMaxAttempts; attempt++)
        {
            try
            {
                _ = await _reconciler.ReconcileAsync(stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                WorksRecoveryLog.RecoveryStepFailed(_logger, "startup-reminder-reconciliation", ex);
                if (attempt == _options.ReminderReconciliationMaxAttempts)
                {
                    return;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(_options.ReminderReconciliationRetryDelayMilliseconds),
                    _timeProvider,
                    stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
