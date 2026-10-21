namespace Hexalith.Works.Runtime;

/// <summary>
/// Works contextual-read configuration and historical recovery compatibility settings. Bound from the
/// <c>Works:Recovery</c> configuration section.
/// </summary>
/// <remarks>
/// <para>The canonical SDK runtime ignores the five obsolete legacy-host settings below. Configure SDK
/// reconciliation under <c>EventStore:Reminders</c> and <c>EventStore:Processes</c>; their periodic, bounded
/// recovery does not reproduce the retired startup-attempt or per-target pacing semantics. Works contextual
/// reads remain per aggregate and use the page, byte and timeout limits in this class.</para>
/// </remarks>
public sealed class WorksRecoveryOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Works:Recovery";

    /// <summary>Requires SDK reminder/process mechanics; false is rejected at startup.</summary>
    public bool UseSdkRuntime { get; init; } = true;

    /// <summary>Historical startup switch, ignored by the SDK. Use its ReconciliationEnabled options for periodic workers.</summary>
    [Obsolete("Ignored by SDK recovery. Configure EventStore:Reminders:ReconciliationEnabled and EventStore:Processes:ReconciliationEnabled.")]
    public bool RunReconciliationOnStartup { get; init; } = true;

    /// <summary>The default per-aggregate stream-page budget.</summary>
    public const int DefaultMaxStreamPagesPerAggregate = 1000;

    /// <summary>
    /// The maximum stream pages a single scan reads <em>per aggregate</em> (a runaway backstop). Every stream read
    /// this runtime issues is per-aggregate, so this budget is spent once per work item — not once per tenant.
    /// </summary>
    public int MaxStreamPagesPerAggregate { get; init; } = DefaultMaxStreamPagesPerAggregate;

    /// <summary>
    /// Deprecated alias for <see cref="MaxStreamPagesPerAggregate"/>, kept so the previously bound
    /// <c>Works:Recovery:MaxStreamPagesPerTenant</c> configuration key keeps binding. The name was wrong: the
    /// budget was always applied per aggregate, so an operator sizing it from the old name would size it wrong by
    /// the number of work items in the tenant. When set it wins over <see cref="MaxStreamPagesPerAggregate"/>;
    /// prefer the new key in new configuration.
    /// </summary>
    public int? MaxStreamPagesPerTenant { get; init; }

    /// <summary>
    /// The per-aggregate stream-page budget actually applied: the deprecated
    /// <see cref="MaxStreamPagesPerTenant"/> alias when configuration still sets it, otherwise
    /// <see cref="MaxStreamPagesPerAggregate"/>.
    /// </summary>
    public int EffectiveMaxStreamPagesPerAggregate => MaxStreamPagesPerTenant ?? MaxStreamPagesPerAggregate;

    /// <summary>Maximum cumulative encoded evidence retained by one complete contextual stream read (default 4 MiB).</summary>
    public int MaxContextBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Whole contextual stream-read deadline, including all pages and decoding (default 500 ms).</summary>
    public TimeSpan ContextReadTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Historical startup attempt budget, ignored by the SDK; periodic reconciliation has no equivalent startup limit.</summary>
    [Obsolete("Ignored by SDK recovery. SDK periodic reconciliation has no equivalent startup attempt budget.")]
    public int ReminderReconciliationMaxAttempts { get; init; } = 5;

    /// <summary>Historical startup retry delay, ignored by the SDK. Its reconciliation cadence and reminder backoff are separate controls.</summary>
    [Obsolete("Ignored by SDK recovery. See EventStore:Reminders:ReconciliationInterval, RetryInitialDelay and RetryMaxDelay; these do not reproduce startup retries.")]
    public int ReminderReconciliationRetryDelayMilliseconds { get; init; } = 1000;

    /// <summary>
    /// Historical per-target cascade delay, ignored by the SDK. Process operation, source-quantum and fan-out
    /// budgets bound supported work; none is an equivalent per-target delay.
    /// </summary>
    [Obsolete("Ignored by SDK recovery. EventStore:Processes operation, source-quantum and effect-count budgets do not provide per-target pacing.")]
    public int CascadeTargetIntervalMilliseconds { get; init; }

    /// <summary>
    /// Historical orphan-index age, ignored by the SDK. SDK leases and readiness freshness do not authorize
    /// pruning a retained process candidate and are not equivalent age-based cleanup controls.
    /// </summary>
    [Obsolete("Ignored by SDK recovery. SDK LeaseDuration and ReadinessMaxAge are not orphan-index pruning controls.")]
    public int CascadeCheckpointIndexStaleAfterHours { get; init; } = 24;
}
