namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Governs the subscriber dead-letter operator runbook's required safety sections.
/// </summary>
public sealed class SubscriberDeadLetterOperatorDocumentationTests
{
    /// <summary>Verifies the runbook keeps the queue distinction, workflow, recovery, and redaction rules explicit.</summary>
    [Fact]
    public void RunbookDocumentsSubscriberOperationsAndPayloadSafety()
    {
        string root = RepositoryRoot.Locate();
        string path = Path.Combine(root, "docs", "operations", "subscriber-dead-letter-operator.md");

        Assert.True(File.Exists(path), $"Required subscriber DLQ runbook is missing: {path}");
        string text = File.ReadAllText(path).ReplaceLineEndings(" ");
        Assert.Contains("## Scope and queue distinction", text, StringComparison.Ordinal);
        Assert.Contains("not EventStore command dead letters", text, StringComparison.Ordinal);
        Assert.Contains("## Alerts and retention", text, StringComparison.Ordinal);
        Assert.Contains("## Triage, fix, retry, verify, archive", text, StringComparison.Ordinal);
        Assert.Contains("## Restart and failure recovery", text, StringComparison.Ordinal);
        Assert.Contains("## Payload redaction rules", text, StringComparison.Ordinal);
        Assert.Contains("must never be blindly republished", text, StringComparison.Ordinal);
        Assert.Contains("raw bytes exist solely so", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the runbook states the loss conditions an operator cannot otherwise observe.
    /// </summary>
    /// <remarks>
    /// The workload emits no log output, so every one of these facts is reachable only from this document: the
    /// inbound retry budget is finite and a refused delivery is eventually dropped, <c>capture-failed</c> is the
    /// sole signal that this is happening, an exhausted replay is recoverable rather than terminal, and nothing
    /// purges retained bodies. A runbook that omits any of them leaves an operator acting on a false model.
    /// </remarks>
    [Fact]
    public void RunbookDocumentsCaptureLossAndRecoveryBounds()
    {
        string root = RepositoryRoot.Locate();
        string path = Path.Combine(root, "docs", "operations", "subscriber-dead-letter-operator.md");
        string text = File.ReadAllText(path).ReplaceLineEndings(" ");

        Assert.Contains("## Capture limits and non-retryable deliveries", text, StringComparison.Ordinal);
        Assert.Contains("pubsubRetryInbound", text, StringComparison.Ordinal);
        Assert.Contains("dropped with nothing retained", text, StringComparison.Ordinal);
        Assert.Contains("capture-failed", text, StringComparison.Ordinal);
        Assert.Contains("unretainable", text, StringComparison.Ordinal);
        Assert.Contains("no purge or compaction operation exists", text, StringComparison.Ordinal);
        Assert.Contains("per operator-requested replay, not per item lifetime", text, StringComparison.Ordinal);
        Assert.Contains("EventStoreOperations__MaxListItems", text, StringComparison.Ordinal);
        Assert.Contains("commanddeadletter.work.events", text, StringComparison.Ordinal);
    }
}
