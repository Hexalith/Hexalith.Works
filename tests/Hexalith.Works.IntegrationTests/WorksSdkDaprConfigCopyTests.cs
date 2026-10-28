using Shouldly;

using YamlDotNet.RepresentationModel;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Protects the optional live-fixture copies from YAML scope changes to committed ACL and mTLS policy.</summary>
public sealed class WorksSdkDaprConfigCopyTests
{
    /// <summary>Control-plane and workload trust domains remain distinct at every committed and copied ACL scope.</summary>
    /// <param name="fileName">The configuration selected by one Platform sidecar.</param>
    /// <param name="callers">The exact caller identities in that configuration.</param>
    [Theory]
    [InlineData("accesscontrol.yaml", "works,eventstore-admin")]
    [InlineData("accesscontrol.works.yaml", "eventstore,eventstore-operations,eventstore-admin")]
    [InlineData("accesscontrol.eventstore-admin.yaml", "")]
    [InlineData("accesscontrol.eventstore-operations.yaml", "eventstore-admin")]
    public void PlatformAndFixturePoliciesPinEveryTrustDomain(string fileName, string callers)
    {
        ArgumentNullException.ThrowIfNull(callers);
        string[] expectedCallers = callers.Split(',', StringSplitOptions.RemoveEmptyEntries);
        string[] committed = ReadCommittedLines(fileName);
        foreach (string[] lines in new[] { committed, ReadCopyLines(fileName, committed) })
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(string.Join(Environment.NewLine, lines)));
            var root = (YamlMappingNode)yaml.Documents.Single().RootNode;
            var spec = (YamlMappingNode)root.Children[new YamlScalarNode("spec")];
            var mtls = (YamlMappingNode)spec.Children[new YamlScalarNode("mtls")];
            mtls.Children[new YamlScalarNode("enabled")].ToString().ShouldBe("true", fileName);
            mtls.Children[new YamlScalarNode("controlPlaneTrustDomain")].ToString().ShouldBe("localhost", fileName);
            var acl = (YamlMappingNode)spec.Children[new YamlScalarNode("accessControl")];
            acl.Children[new YamlScalarNode("trustDomain")].ToString().ShouldBe("public", fileName);
            var policies = (YamlSequenceNode)acl.Children[new YamlScalarNode("policies")];
            policies.Children.Cast<YamlMappingNode>().Select(policy => policy.Children[new YamlScalarNode("appId")].ToString())
                .ShouldBe(expectedCallers, ignoreOrder: true, customMessage: fileName);
            foreach (YamlMappingNode policy in policies.Children.Cast<YamlMappingNode>())
            {
                policy.Children[new YamlScalarNode("trustDomain")].ToString().ShouldBe("public",
                    fileName + ": " + policy.Children[new YamlScalarNode("appId")]);
            }
        }
    }

    /// <summary>The unchanged configuration and the exact component-watch opt-out retain every original policy line.</summary>
    /// <param name="fileName">The committed configuration shared with one live sidecar.</param>
    [Theory]
    [InlineData("accesscontrol.yaml")]
    [InlineData("accesscontrol.works.yaml")]
    [InlineData("accesscontrol.eventstore-admin.yaml")]
    [InlineData("accesscontrol.eventstore-operations.yaml")]
    public void ExactHotReloadOptOutPreservesTheCommittedPolicy(string fileName)
    {
        string[] committed = ReadCommittedLines(fileName);
        WorksSdkRecoveryPipelineSmokeTests.IsProtectedDaprConfigCopy(committed, committed).ShouldBeTrue();
        WorksSdkRecoveryPipelineSmokeTests.IsProtectedDaprConfigCopy(committed, ReadCopyLines(fileName, committed)).ShouldBeTrue();
    }

    /// <summary>Dedenting the ACL produces valid YAML outside spec and must fail the fixture's equivalence guard.</summary>
    /// <param name="fileName">The committed configuration shared with one live sidecar.</param>
    [Theory]
    [InlineData("accesscontrol.yaml")]
    [InlineData("accesscontrol.works.yaml")]
    [InlineData("accesscontrol.eventstore-admin.yaml")]
    [InlineData("accesscontrol.eventstore-operations.yaml")]
    public void AclIndentationCannotEscapeTheSpec(string fileName)
    {
        string[] committed = ReadCommittedLines(fileName);
        string[] copy = ReadCopyLines(fileName, committed);
        int acl = Array.IndexOf(copy, "  accessControl:");
        acl.ShouldBeGreaterThanOrEqualTo(0);
        string[] altered = [.. copy.Select((line, index) => index >= acl && line.StartsWith("  ", StringComparison.Ordinal) ? line[2..] : line)];
        var yaml = new YamlStream();
        yaml.Load(new StringReader(string.Join(Environment.NewLine, altered)));
        var root = (YamlMappingNode)yaml.Documents.Single().RootNode;
        root.Children.ContainsKey(new YamlScalarNode("accessControl")).ShouldBeTrue();
        ((YamlMappingNode)root.Children[new YamlScalarNode("spec")]).Children.ContainsKey(new YamlScalarNode("accessControl")).ShouldBeFalse();
        WorksSdkRecoveryPipelineSmokeTests.IsProtectedDaprConfigCopy(committed, altered).ShouldBeFalse();
    }

    private static string[] ReadCopyLines(string fileName, string[] committed)
    {
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_DAPR_CONFIG_DIRECTORY") is { Length: > 0 } directory)
            return File.ReadAllLines(Path.Combine(Path.GetFullPath(directory), fileName));
        int spec = Array.IndexOf(committed, "spec:");
        spec.ShouldBeGreaterThanOrEqualTo(0);
        return [.. committed.Take(spec + 1), "  features:", "    - name: HotReload", "      enabled: false", .. committed.Skip(spec + 1)];
    }

    private static string[] ReadCommittedLines(string fileName)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hexalith.Works.slnx")))
                return File.ReadAllLines(Path.Combine(Environment.GetEnvironmentVariable("WORKS_PLATFORM_ROOT")
                    ?? Path.Combine(directory.FullName, "references", "Hexalith.Platform"), "DaprComponents", fileName));
        }
        throw new DirectoryNotFoundException("Could not locate the committed Works Dapr configurations.");
    }
}
