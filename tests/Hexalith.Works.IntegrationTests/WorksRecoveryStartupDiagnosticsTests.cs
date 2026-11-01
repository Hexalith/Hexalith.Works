using System.Text.Json;
using System.Text.Json.Nodes;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksRecoveryStartupDiagnosticsTests
{
    [Fact]
    public void ProjectionKeepsControlReasonsAndCountsWhileOmittingPrivateContents()
    {
        var input = JsonNode.Parse("""{"Complete":false,"IsIncomplete":true,"ReasonCode":"catalog-unavailable","AfterKey":"private-tenant\\u0000work\\u0000private-source","TenantIds":["private-tenant"],"Journal":[{"payload":"private-payload"}],"SigningKey":"private-signing-key"}""")!.AsObject();
        string result = JsonSerializer.Serialize(WorksRecoveryStartupDiagnostics.Project(input));
        result.ShouldContain("catalog-unavailable");
        result.ShouldContain("tenantIdsCount");
        result.ShouldContain("afterKeySha256");
        result.ShouldNotContain("private-");
        result.ShouldNotContain("SigningKey");
    }

    [Fact]
    public void ActorDiagnosticRedactionKeepsFailureTypesAndRemovesOpaqueCredentials()
    {
        string raw = "ActorMethodInvocationException bearer fixture-token-material " + new string('A', 100) + " ProcessEvidenceUnavailableException";
        string safe = WorksSdkProofDiagnostics.Redact(raw);
        safe.ShouldContain("ActorMethodInvocationException");
        safe.ShouldContain("ProcessEvidenceUnavailableException");
        safe.ShouldNotContain("fixture-token-material");
        safe.ShouldNotContain(new string('A', 100));
    }

    [Fact]
    public void MissingStateIsReportedAsMissingWithoutInventingACompletedScan()
    {
        var result = WorksRecoveryStartupDiagnostics.Project(null);
        result.Count.ShouldBe(1);
        result["present"].ShouldBe(false);
    }
}
