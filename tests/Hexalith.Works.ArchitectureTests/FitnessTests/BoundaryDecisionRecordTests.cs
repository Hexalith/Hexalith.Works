using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class BoundaryDecisionRecordTests
{
    private static readonly string[] RequiredModuleNames =
    [
        "Parties",
        "Conversations",
        "EventStore",
        "Tenants",
        "Commons",
        "PolymorphicSerializations",
    ];

    private static readonly string[] RequiredDeferredSeamMarkers =
    [
        "IExpectationResolver", // (a) AI-inferred expectations seam
        "IExecutorRouter",      // (b) executor routing/selection seam
        "cost",                 // (c) cost meter / spend governance seam
        "security",             // (d) trust / security hardening seam
    ];

    [Fact]
    public void BoundaryDecisionRecord_existsAndEnumeratesEveryModuleAndDeferredSeam()
    {
        string path = RepositoryRoot.PathFromRoot("docs", "boundary-decision-record.md");
        File.Exists(path).ShouldBeTrue($"The boundary decision record must exist at '{path}' (FR-23 / AR-19).");

        string content = File.ReadAllText(path);
        content.ShouldNotBeNullOrWhiteSpace("The boundary decision record must not be empty.");

        foreach (string module in RequiredModuleNames)
        {
            content.ShouldContain(
                module,
                Case.Sensitive,
                $"The boundary decision record must enumerate owns-vs-references for the '{module}' module (AC #4).");
        }

        foreach (string marker in RequiredDeferredSeamMarkers)
        {
            content.ShouldContain(
                marker,
                Case.Insensitive,
                $"The boundary decision record must record the deferred seam marker '{marker}' as not-v1 behavior (AC #5).");
        }
    }
}
