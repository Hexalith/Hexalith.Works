using Hexalith.Works.Reminders;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Deterministic proof of <see cref="PendingDateAwaitScanIncompleteException"/>'s constructor contract (Story
/// 4.8 code-review remediation): a null <c>partialResults</c> must throw <see cref="ArgumentNullException"/>
/// before any message text is built (message construction must not silently tolerate the null with a
/// null-conditional fallback), and a valid instance must expose the count actually passed in.
/// </summary>
public sealed class PendingDateAwaitScanIncompleteExceptionTests
{
    [Fact]
    public void Constructor_throws_argument_null_for_a_null_partial_results_list()
        => Should.Throw<ArgumentNullException>(() => new PendingDateAwaitScanIncompleteException(null!, 1, 0, null));

    [Fact]
    public void Constructor_exposes_the_partial_results_and_failed_tenant_and_candidate_counts()
    {
        var partialResults = new List<PendingDateAwait>
        {
            new("tenant-alpha", "work-001", new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero), "correlation-key"),
        };
        var inner = new InvalidOperationException("simulated tenant scan failure");

        var exception = new PendingDateAwaitScanIncompleteException(partialResults, 2, 3, inner);

        exception.PartialResults.ShouldBeSameAs(partialResults);
        exception.FailedTenantCount.ShouldBe(2);
        exception.FailedCandidateCount.ShouldBe(3);
        exception.InnerException.ShouldBeSameAs(inner);
        exception.Message.ShouldContain("2 tenant");
        exception.Message.ShouldContain("3 candidate");
        exception.Message.ShouldContain("1 awaits");
    }
}
