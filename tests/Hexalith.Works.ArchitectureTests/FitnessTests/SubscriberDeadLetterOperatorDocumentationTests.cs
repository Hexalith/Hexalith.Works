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

    /// <summary>
    /// Verifies parked-aggregate recovery limits and each recovery warning retain an actionable operator response.
    /// </summary>
    [Fact]
    public void RunbookDocumentsParkedRecoveryLimitsAndWarningResponses()
    {
        string root = RepositoryRoot.Locate();
        string path = Path.Combine(root, "docs", "operations", "subscriber-dead-letter-operator.md");

        Assert.True(File.Exists(path), $"Required subscriber DLQ runbook is missing: {path}");
        string document = File.ReadAllText(path);
        int sectionStart = document.IndexOf("## Works date-reminder recovery warnings", StringComparison.Ordinal);
        int sectionEnd = document.IndexOf("## Payload redaction rules", StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, "The Works date-reminder recovery warning section is required.");
        Assert.True(sectionEnd > sectionStart, "The Works recovery warning section must precede payload redaction rules.");
        string section = document[sectionStart..sectionEnd];

        Assert.Contains("Operator response", section, StringComparison.Ordinal);

        string recoveryFailure = WarningRow(section, "4603");
        AssertContainsAll(
            recoveryFailure,
            "RecoveryStepFailed",
            "Reason",
            "startup-reminder-reconciliation",
            "startup-cascade-recovery",
            "one startup pass",
            "narrower",
            "cause",
            "dependency",
            "same `Reason`",
            "success",
            "shutdown",
            "exhaustion",
            "no in-process repeat",
            "no universal recovery-success event");
        Assert.DoesNotContain("same-`Reason`", recoveryFailure, StringComparison.OrdinalIgnoreCase);

        string tenantScan = WarningRow(section, "4604");
        AssertContainsAll(
            tenantScan,
            "PendingDateAwaitTenantScanFailed",
            "pending-date index",
            "state-store",
            "restore",
            "host remains running",
            "shutdown",
            "restart");

        string incompleteScan = WarningRow(section, "4605");
        AssertContainsAll(
            incompleteScan,
            "PendingDateAwaitScanIncomplete",
            "attempted",
            "non-cancellation",
            "Exact caller cancellation",
            "propagates directly",
            "without 4603",
            "later tenant boundary",
            "earlier scan evidence",
            "typed incomplete-result path",
            "unless a partial operation propagates the cancellation",
            "structured `Reason`",
            "same structured `Reason`",
            "repeated warnings",
            "successful progress",
            "shutdown",
            "retry-budget exhaustion");
        Assert.DoesNotContain("same-`Reason`", incompleteScan, StringComparison.OrdinalIgnoreCase);

        string candidateScan = WarningRow(section, "4606");
        AssertContainsAll(
            candidateScan,
            "PendingDateAwaitCandidateScanFailed",
            "EventStore",
            "stream",
            "restore",
            "host remains running",
            "shutdown",
            "restart");

        string parkedCandidate = WarningRow(section, "4607");
        AssertContainsAll(
            parkedCandidate,
            "PendingDateAwaitParkedCandidateSkipped",
            "recovery",
            "missing",
            "cannot",
            "already durable",
            "may still fire",
            "separate",
            "remediation",
            "escalate");

        string parkingLookup = WarningRow(section, "4608");
        AssertContainsAll(parkingLookup, "PendingDateAwaitParkingLookupFailed", "state-store", "restore", "reconciliation");

        string schedulingFailure = WarningRow(section, "4609");
        AssertContainsAll(
            schedulingFailure,
            "DateReminderSchedulingFailed",
            "actor",
            "placement",
            "Scheduler",
            "state-store",
            "repair",
            "verify");

        AssertContainsAll(
            section.ReplaceLineEndings(" "),
            "4603 or 4609 can originate outside startup recovery",
            "does not alone prove a startup retry will occur",
            "structured `Reason`",
            "only `startup-reminder-reconciliation`",
            "Warnings 4604, 4605, 4606, and 4608",
            "unless shutdown stops the pass");
    }

    private static void AssertContainsAll(string actual, params string[] requiredTerms)
    {
        foreach (string requiredTerm in requiredTerms)
        {
            Assert.Contains(requiredTerm, actual, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string WarningRow(string section, string eventId)
        => section
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single(line => line.StartsWith($"| {eventId} ", StringComparison.Ordinal));
}
