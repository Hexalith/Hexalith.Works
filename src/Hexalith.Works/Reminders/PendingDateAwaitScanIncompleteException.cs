namespace Hexalith.Works.Reminders;

/// <summary>
/// Thrown by <see cref="IndexedPendingDateAwaitSource"/> when one or more tenants — or one or more candidate
/// aggregates inside an otherwise readable tenant — could not be scanned for pending <c>DateReached</c> awaits.
/// Carries the awaits that were successfully discovered from everything that did scan cleanly, so a caller can
/// still act on that partial evidence before signalling the overall pass as incomplete for retry (Story 4.8
/// code-review remediation: a single unreadable stream must not silently starve reminder discovery for every
/// other tenant, nor for the other candidates in its own tenant).
/// </summary>
internal sealed class PendingDateAwaitScanIncompleteException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PendingDateAwaitScanIncompleteException"/> class.</summary>
    /// <param name="partialResults">The pending date awaits discovered from everything that scanned successfully.</param>
    /// <param name="failedTenantCount">The number of tenants whose index/registry read failed outright.</param>
    /// <param name="failedCandidateCount">The number of individual candidate aggregate streams that failed.</param>
    /// <param name="innerException">The exception from the last scan step that failed.</param>
    public PendingDateAwaitScanIncompleteException(
        IReadOnlyList<PendingDateAwait> partialResults,
        int failedTenantCount,
        int failedCandidateCount,
        Exception? innerException)
        : base(BuildMessage(partialResults, failedTenantCount, failedCandidateCount), innerException)
    {
        PartialResults = partialResults;
        FailedTenantCount = failedTenantCount;
        FailedCandidateCount = failedCandidateCount;
    }

    /// <summary>Gets the pending date awaits discovered from everything that scanned successfully.</summary>
    public IReadOnlyList<PendingDateAwait> PartialResults { get; }

    /// <summary>
    /// Gets the number of tenants whose scan failed outright — the registry entry could not be turned into a
    /// candidate list at all. Unchanged in meaning by the per-candidate isolation added alongside
    /// <see cref="FailedCandidateCount"/>: a tenant whose individual candidates failed is still scanned.
    /// </summary>
    public int FailedTenantCount { get; }

    /// <summary>
    /// Gets the number of individual candidate aggregate streams that could not be read or folded. Recovery
    /// degrades to the candidates that did read cleanly rather than collapsing for the whole tenant.
    /// </summary>
    public int FailedCandidateCount { get; }

    // Validating and formatting in one helper, evaluated as a base(...) constructor argument, guarantees the
    // null check runs before any message text is built — a null partialResults throws ArgumentNullException
    // instead of silently discarding the "0 awaits" fallback text that a null-conditional would have produced.
    private static string BuildMessage(IReadOnlyList<PendingDateAwait> partialResults, int failedTenantCount, int failedCandidateCount)
    {
        ArgumentNullException.ThrowIfNull(partialResults);
        return $"Pending date-await discovery was incomplete for {failedTenantCount} tenant(s) and "
            + $"{failedCandidateCount} candidate aggregate(s); {partialResults.Count} awaits were discovered from "
            + "everything that scanned successfully.";
    }
}
