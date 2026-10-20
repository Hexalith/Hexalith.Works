using Shouldly;

using YamlDotNet.RepresentationModel;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Proves that live-start discovery isolation adds only an owned SQLite registry to each committed policy.</summary>
public sealed class WorksAppHostDiscoveryIsolationTests
{
    /// <summary>An isolated host port changes only the Sentry endpoint alongside the owned discovery block.</summary>
    [Fact]
    public void IsolatedSentryPortPreservesEveryTrustAndAccessPolicyLine()
    {
        string rendered = WorksAppHostDiscoveryIsolation.Render(Policy.ReplaceLineEndings("\n"), Registry, 61001);
        string expected = Policy.ReplaceLineEndings("\n").Replace("127.0.0.1:50001", "127.0.0.1:61001", StringComparison.Ordinal);
        rendered.Split('\n').Where(line => !IsDiscoveryLine(line)).ShouldBe(expected.Split('\n'));
        rendered.ShouldContain("enabled: true");
        rendered.ShouldContain("defaultAction: deny");
        rendered.ShouldContain("trustDomain: \"public\"");
    }

    /// <summary>Unsafe ports and an endpoint outside mTLS cannot be silently admitted.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void UnsafeSentryPortsAreRefused(int port)
        => Should.Throw<InvalidOperationException>(() => WorksAppHostDiscoveryIsolation.Render(Policy, Registry, port));

    /// <summary>The address must remain in the mTLS mapping, rather than a same-shaped unrelated policy.</summary>
    [Fact]
    public void ForeignSentryAddressMappingIsRefused()
        => Should.Throw<InvalidOperationException>(() => WorksAppHostDiscoveryIsolation.Render(
            Policy.Replace("  mtls:", "  unrelated:", StringComparison.Ordinal), Registry, 61001));

    private const string Registry = "/tmp/works-sdk416-discovery-fixture/name-resolution.db";

    private const string Policy = """
        # Local development policy.
        apiVersion: dapr.io/v1alpha1
        kind: Configuration
        metadata:
          name: accesscontrol-works
        spec:
          mtls:
            enabled: true
            sentryAddress: "127.0.0.1:50001"
          accessControl:
            defaultAction: deny
            trustDomain: "public"
            policies:
              - appId: eventstore
                defaultAction: deny
                operations:
                  - name: /process
                    httpVerb: ['POST']
                    action: allow
        """;

    /// <summary>Every committed line survives in order, and the spec gains exactly the owned SQLite resolver.</summary>
    /// <param name="newline">The committed document's line ending.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void RenderPreservesThePolicyAndSelectsTheOwnedRegistry(string newline)
    {
        string committed = Policy.ReplaceLineEndings(newline) + newline;
        string rendered = WorksAppHostDiscoveryIsolation.Render(committed, Registry);

        string[] committedLines = committed.Split(newline);
        string[] renderedLines = rendered.Split(newline);
        renderedLines.Length.ShouldBe(committedLines.Length + 8);
        renderedLines.Where(line => !IsDiscoveryLine(line)).ShouldBe(committedLines);
        if (newline == "\n") rendered.ShouldNotContain("\r");

        var yaml = new YamlStream();
        yaml.Load(new StringReader(rendered));
        var spec = (YamlMappingNode)((YamlMappingNode)yaml.Documents.Single().RootNode).Children[new YamlScalarNode("spec")];
        spec.Children.Keys.Select(key => ((YamlScalarNode)key).Value).ShouldBe(["nameResolution", "mtls", "accessControl"]);
        var resolution = (YamlMappingNode)spec.Children[new YamlScalarNode("nameResolution")];
        ((YamlScalarNode)resolution.Children[new YamlScalarNode("component")]).Value.ShouldBe("sqlite");
        ((YamlScalarNode)resolution.Children[new YamlScalarNode("version")]).Value.ShouldBe("v1");
        var configuration = (YamlMappingNode)resolution.Children[new YamlScalarNode("configuration")];
        ((YamlScalarNode)configuration.Children[new YamlScalarNode("connectionString")]).Value.ShouldBe(Registry);
        ((YamlScalarNode)configuration.Children[new YamlScalarNode("busyTimeout")]).Value.ShouldBe("5s");
        ((YamlScalarNode)configuration.Children[new YamlScalarNode("timeout")]).Value.ShouldBe("2s");
        ((YamlScalarNode)configuration.Children[new YamlScalarNode("disableWAL")]).Value.ShouldBe("true");
        configuration.Children.Count.ShouldBe(4);
        var access = (YamlMappingNode)spec.Children[new YamlScalarNode("accessControl")];
        ((YamlScalarNode)access.Children[new YamlScalarNode("defaultAction")]).Value.ShouldBe("deny");
    }

    /// <summary>Existing discovery, ambiguous or missing specs and unexpected indentation fail closed.</summary>
    /// <param name="document">A configuration the isolation must refuse.</param>
    [Theory]
    [InlineData("apiVersion: dapr.io/v1alpha1\nkind: Configuration\nspec:\n  nameResolution:\n    component: \"mdns\"\n")]
    [InlineData("apiVersion: dapr.io/v1alpha1\nkind: Configuration\nmetadata:\n  name: none\n")]
    [InlineData("spec:\n  mtls:\n    enabled: true\nspec:\n  mtls:\n    enabled: true\n")]
    [InlineData("spec:\n    mtls:\n      enabled: true\n")]
    [InlineData("spec:\nmtls:\n  enabled: true\n")]
    [InlineData("spec:\n")]
    public void UnexpectedConfigurationShapesAreRefused(string document)
        => Should.Throw<InvalidOperationException>(() => WorksAppHostDiscoveryIsolation.Render(document, Registry));

    /// <summary>Only a plain absolute registry path can be embedded in the generated YAML scalar.</summary>
    /// <param name="registry">An unsafe or relative registry path.</param>
    [Theory]
    [InlineData("name-resolution.db")]
    [InlineData("/tmp/works\"injected/name-resolution.db")]
    [InlineData("/tmp/works\\name-resolution.db")]
    [InlineData("/tmp/works\nname-resolution.db")]
    public void UnsafeRegistryPathsAreRefused(string registry)
        => Should.Throw<InvalidOperationException>(() => WorksAppHostDiscoveryIsolation.Render(Policy, registry));

    private static bool IsDiscoveryLine(string line)
        => line is "  nameResolution:" or "    component: \"sqlite\"" or "    version: \"v1\"" or "    configuration:"
            or "      busyTimeout: \"5s\"" or "      timeout: \"2s\"" or "      disableWAL: \"true\""
            || line == "      connectionString: \"" + Registry + "\"";
}
