using Shouldly;

using YamlDotNet.RepresentationModel;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Protects the optional live-fixture copies from YAML scope changes to committed ACL and mTLS policy.</summary>
public sealed class WorksSdkDaprConfigCopyTests
{
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
