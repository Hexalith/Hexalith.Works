namespace Hexalith.Works.Reminders;

/// <summary>
/// Thrown by <see cref="IndexedPendingDateAwaitSource"/> when one or more tenants could not be scanned for
/// pending <c>DateReached</c> awaits. Carries the awaits that were successfully discovered from the tenants
/// that did scan cleanly, so a caller can still act on that partial evidence before signalling the overall
/// pass as incomplete for retry (Story 4.8 code-review remediation: a single unreadable tenant stream must
/// not silently starve reminder discovery for every other tenant).
/// </summary>
internal sealed class PendingDateAwaitScanIncompleteException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PendingDateAwaitScanIncompleteException"/> class.</summary>
    /// <param name="partialResults">The pending date awaits discovered from the tenants that scanned successfully.</param>
    /// <param name="failedTenantCount">The number of tenants whose scan failed.</param>
    /// <param name="innerException">The exception from the last tenant scan that failed.</param>
    public PendingDateAwaitScanIncompleteException(
        IReadOnlyList<PendingDateAwait> partialResults,
        int failedTenantCount,
        Exception? innerException)
        : base(BuildMessage(partialResults, failedTenantCount), innerException)
    {
        PartialResults = partialResults;
        FailedTenantCount = failedTenantCount;
    }

    /// <summary>Gets the pending date awaits discovered from the tenants that scanned successfully.</summary>
    public IReadOnlyList<PendingDateAwait> PartialResults { get; }

    /// <summary>Gets the number of tenants whose scan failed.</summary>
    public int FailedTenantCount { get; }

    // Validating and formatting in one helper, evaluated as a base(...) constructor argument, guarantees the
    // null check runs before any message text is built — a null partialResults throws ArgumentNullException
    // instead of silently discarding the "0 awaits" fallback text that a null-conditional would have produced.
    private static string BuildMessage(IReadOnlyList<PendingDateAwait> partialResults, int failedTenantCount)
    {
        ArgumentNullException.ThrowIfNull(partialResults);
        return $"Pending date-await discovery failed for {failedTenantCount} tenant(s); {partialResults.Count} awaits were discovered from the tenants that scanned successfully.";
    }
}
