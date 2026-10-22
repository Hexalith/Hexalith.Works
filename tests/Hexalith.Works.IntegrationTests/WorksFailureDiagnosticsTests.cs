using Hexalith.Works.Runtime;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Checks bounded structured fields and the absence of sensitive exception details.</summary>
public sealed class WorksFailureDiagnosticsTests
{
    /// <summary>Every diagnostic field derived from external scope is bounded and safe on one log line.</summary>
    [Fact]
    public void FailureContextIsBoundedWithoutRetainingExceptionMessages()
    {
        var logger = new Story48RecordingLogger<WorksFailureDiagnosticsTests>();
        string scope = "tenant\r\n\t\u2028\u2029" + new string('x', 256);
        WorksFailureDiagnostics.QueryFailed(logger, "get-work-item", scope, scope,
            new IOException("private-provider-credential"));
        WorksFailureDiagnostics.DiscoveryIncomplete(logger, scope, "invalid-state");

        logger.Entries.Count.ShouldBe(2);
        foreach (var entry in logger.Entries)
        {
            ((string)entry.Properties["TenantId"]!).Length.ShouldBe(128);
            entry.Exception.ShouldBeNull();
            entry.Message.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029').ShouldBeFalse();
            entry.Message.ShouldNotContain("private-provider-credential");
        }
        logger.Entries[0].Properties["FailureCategory"].ShouldBe("dependency-unavailable");
    }
}
