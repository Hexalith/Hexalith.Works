namespace Hexalith.Works.Reminders;

/// <summary>
/// Thrown by <see cref="IndexedPendingDateAwaitSource"/> when one or more tenants could not be scanned for
/// pending <c>DateReached</c> awaits. Carries the awaits that were successfully discovered from the tenants
/// that did scan cleanly, so a caller can still act on that partial evidence before signalling the overall
/// pass as incomplete for retry (Story 4.8 code-review remediation: a single unreadable tenant stream must
/// not silently starve reminder discovery for every other tenant).
/// </summary>
public sealed class PendingDateAwaitScanIncompleteException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PendingDateAwaitScanIncompleteException"/> class.</summary>
    /// <param name="partialResults">The pending date awaits discovered from the tenants that scanned successfully.</param>
    /// <param name="failedTenantCount">The number of tenants whose scan failed.</param>
    /// <param name="innerException">The exception from the last tenant scan that failed.</param>
    public PendingDateAwaitScanIncompleteException(
        IReadOnlyList<PendingDateAwait> partialResults,
        int failedTenantCount,
        Exception? innerException)
        : base(
            $"Pending date-await discovery failed for {failedTenantCount} tenant(s); {partialResults?.Count ?? 0} awaits were discovered from the tenants that scanned successfully.",
            innerException)
    {
        ArgumentNullException.ThrowIfNull(partialResults);
        PartialResults = partialResults;
        FailedTenantCount = failedTenantCount;
    }

    /// <summary>Gets the pending date awaits discovered from the tenants that scanned successfully.</summary>
    public IReadOnlyList<PendingDateAwait> PartialResults { get; }

    /// <summary>Gets the number of tenants whose scan failed.</summary>
    public int FailedTenantCount { get; }
}
