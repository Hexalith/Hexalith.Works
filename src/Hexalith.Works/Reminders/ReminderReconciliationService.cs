using Hexalith.Works.Runtime;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.Reminders;

/// <summary>
/// Bounded hosted service that runs one reminder reconciliation pass on Works host startup (Story 4.6, AC
/// #3): it re-scans the re-readable pending <c>DateReached</c> awaits and re-registers reminders / reissues
/// due resumes so a firing lost to an AppHost restart is recovered. It is fail-safe — a transient
/// scan/dispatch failure is logged with bounded metadata and never crashes the host, because the underlying
/// scan and resumes are idempotent and the next restart repeats them.
/// </summary>
public sealed class ReminderReconciliationService(
    DateReminderReconciler reconciler,
    IOptions<WorksRecoveryOptions> options,
    ILogger<ReminderReconciliationService> logger) : BackgroundService
{
    private readonly DateReminderReconciler _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
    private readonly WorksRecoveryOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ILogger<ReminderReconciliationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RunReconciliationOnStartup || _options.Tenants.Count == 0)
        {
            return;
        }

        try
        {
            _ = await _reconciler.ReconcileAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host shutting down during reconciliation: safe to abandon — the scan is idempotent on restart.
        }
        catch (Exception ex)
        {
            WorksRecoveryLog.RecoveryStepFailed(_logger, "startup-reminder-reconciliation", ex);
        }
    }
}
