using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>Preserves planning rules and the scope of historical verification evidence.</summary>
public sealed class PlanningEvidenceDocumentationTests
{
    private const string Spec10Name = "spec-4-8-register-and-reconcile-date-reminders-durably-10.md";
    private const string Spec11Name = "spec-4-8-register-and-reconcile-date-reminders-durably-11.md";

    /// <summary>Runtime verification remains separate from infrastructure-free kernel tests.</summary>
    [Fact]
    public void EpicContextRequiresPersistedStateAndPlatformRecoveryEvidence()
    {
        string requirements = Section(ReadArtifact("epic-4-context.md"), "## Requirements & Constraints", "## Technical Decisions");
        requirements.ShouldContain("Tier-1 tests stay free of Dapr, network, browser, and containers.");
        requirements.ShouldContain("Runtime integration tests must assert persisted end-state");
        requirements.ShouldContain("platform-hosted lifecycle and recovery paths");
        requirements.ShouldContain("including restart and retry");
        requirements.ShouldContain("API responses and mock call counts alone do not establish runtime correctness");
    }

    /// <summary>Terminal status never silently turns a refused mutation into accepted work.</summary>
    [Fact]
    public void EpicContextRetainsTerminalRejectionAndDefinedNoOpExceptions()
    {
        string requirements = Section(ReadArtifact("epic-4-context.md"), "## Requirements & Constraints", "## Technical Decisions");
        requirements.ShouldContain("Terminal items reject mutations except the defined idempotent no-ops");
        requirements.ShouldContain("Completed, Cancelled, Rejected, and Expired");
        requirements.ShouldContain("non-requeue Reject condition");
        requirements.ShouldContain("../../docs/lifecycle-transition-matrix.md#idempotent-no-op-list");
        File.ReadAllText(RepositoryRoot.PathFromRoot("docs", "lifecycle-transition-matrix.md"))
            .ShouldContain("## Idempotent no-op list");
    }

    /// <summary>Headless feedback distinguishes unchanged work from a newly accepted action.</summary>
    [Fact]
    public void HeadlessOutcomesIncludeDistinctIdempotentNoOp()
    {
        string experience = Section(ReadArtifact("epic-4-context.md"), "## UX & Interaction Patterns", "## Cross-Story Dependencies");
        experience.ShouldContain("accepted, domain-rejected, authorization-denied, idempotent no-op, and infrastructure-unknown outcomes");
        experience.ShouldContain("A no-op acknowledges unchanged work");
    }

    /// <summary>Earlier successful live observations cannot qualify a later unexecuted checkpoint.</summary>
    [Fact]
    public void Spec10VerificationKeepsHistoricalResultsAndLinksSupersedingEvidence()
    {
        string verification = Section(ReadArtifact(Spec10Name), "## Verification", "### Review Findings");
        verification.ShouldNotContain("final reviewed tree");
        verification.ShouldContain("historical spec-10 checkpoint");
        verification.ShouldContain($"({Spec11Name})");
        string historical = Section(verification, "**Observed 2026-09-20 (historical spec-10 checkpoint):**", "**Superseding evidence");
        historical.ShouldContain("`WorksAppHostSmokeHarnessTests`: 33/33 passed, 0 skipped.");
        historical.ShouldContain("Non-smoke Integration: 526/526 passed, 0 skipped.");
        historical.ShouldContain("Story 4.8 reminder/mTLS Tier-3 aggregate: 4/4 passed, 0 skipped, 1020.742s.");
        verification.ShouldContain("41/41");
        verification.ShouldContain("534/534");
        verification.ShouldContain("preceding 0/4");
        verification.ShouldContain("no fresh live acceptance credit");
        verification.ShouldContain("Neither checkpoint qualifies\nthe current repository tree");
        string superseding = ReadArtifact(Spec11Name);
        superseding.ShouldContain("focused harness 41/41");
        superseding.ShouldContain("non-smoke Integration 534/534");
    }

    /// <summary>The exact closeout inventory includes its implementing specification.</summary>
    [Fact]
    public void Spec10DatedInventoryIncludesItsOwnSpecification()
    {
        string inventory = Section(ReadArtifact("4-8-register-and-reconcile-date-reminders-durably.md"),
            "**2026-09-20 spec-10 five-patch close-out**", "**2026-09-20 File List increment review close-out**");
        inventory.ShouldContain($"- `_bmad-output/implementation-artifacts/{Spec10Name}`");
    }

    private static string ReadArtifact(string name)
        => File.ReadAllText(RepositoryRoot.PathFromRoot("_bmad-output", "implementation-artifacts", name)).ReplaceLineEndings("\n");

    private static string Section(string document, string startMarker, string endMarker)
    {
        int start = document.IndexOf(startMarker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"Required section is absent: {startMarker}");
        int end = document.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, $"Required section boundary is absent: {endMarker}");
        return document[start..end];
    }
}
