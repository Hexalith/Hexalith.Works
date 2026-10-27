using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using CommunityToolkit.Aspire.Hosting.Dapr;

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
    [InlineData(65535)]
    [InlineData(65536)]
    public void UnsafeSentryPortsAreRefused(int port)
        => Should.Throw<InvalidOperationException>(() => WorksAppHostDiscoveryIsolation.Render(Policy, Registry, port));

    /// <summary>The address must remain in the mTLS mapping, rather than a same-shaped unrelated policy.</summary>
    [Fact]
    public void ForeignSentryAddressMappingIsRefused()
        => Should.Throw<InvalidOperationException>(() => WorksAppHostDiscoveryIsolation.Render(
            Policy.Replace("  mtls:", "  unrelated:", StringComparison.Ordinal), Registry, 61001));

    /// <summary>The actual sidecar rewrite selects one owned registry and the explicit control plane for every application.</summary>
    /// <param name="runtimePath">The selected runtime override, or null to preserve the composed runtime.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("/qualified/runtime")]
    public async Task SidecarRewriteIsolatesEveryConfigurationAndSelectsTheOwnedControlPlane(string? runtimePath)
    {
        string fixture = Directory.CreateTempSubdirectory("works-sdk416-isolation-fixture-").FullName;
        string discovery = Directory.CreateTempSubdirectory(WorksAppHostDiscoveryIsolation.DirectoryPrefix).FullName;
        try
        {
            string firstPolicy = Path.Combine(fixture, "eventstore.yaml");
            string secondPolicy = Path.Combine(fixture, "works.yaml");
            File.WriteAllText(firstPolicy, Policy);
            File.WriteAllText(secondPolicy, Policy.Replace("accesscontrol-works", "accesscontrol-eventstore", StringComparison.Ordinal));
            string projectPath = Path.Combine(fixture, "Proof.csproj");
            File.WriteAllText(projectPath, "<Project />");
            IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
            {
                Args = ["--environment=Development"], DisableDashboard = true,
                AssemblyName = typeof(WorksAppHostDiscoveryIsolationTests).Assembly.GetName().Name,
            });
            var ports = (Sentry: 61001, Placement: 61005, Scheduler: 61006);
            foreach ((string name, int port) in new[]
            {
                ("dapr-sentry", 50001), ("dapr-placement-mtls", 51005), ("dapr-scheduler-mtls", 51006),
            })
                builder.AddContainer(name, "synthetic-never-launched")
                    .WithEndpoint(port: port, targetPort: port, name: "grpc");
            ContainerResource scheduler = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "dapr-scheduler-mtls");
            builder.CreateResourceBuilder(scheduler).WithArgs("--preserved", "--override-broadcast-host-port=localhost:51006");
            string[] applications = ["eventstore", "works", "eventstore-operations", "eventstore-admin"];
            foreach (string application in applications)
                builder.AddProject(application, projectPath).WithHttpEndpoint(port: 42000)
                    .WithDaprSidecar(sidecar => sidecar.WithOptions(new DaprSidecarOptions
                    {
                        AppId = application, Config = application is "eventstore" or "eventstore-admin" ? firstPolicy : secondPolicy,
                        AppPort = 42000, DaprHttpPort = 42001, DaprGrpcPort = 42002,
                        DaprInternalGrpcPort = 42003, MetricsPort = 42004, RuntimePath = "/composed/runtime",
                        PlacementHostAddress = "localhost:51005", SchedulerHostAddress = "localhost:51006",
                        EnableAppHealthCheck = true, AppHealthCheckPath = "/alive",
                    }));
            builder.AddProject("without-sidecar", projectPath).WithHttpEndpoint(port: 42000);

            WorksAppHostSmokeHarness.IsolateApplicationPorts(builder, discovery, true, ports, runtimePath);

            var configurations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ProjectResource project in builder.Resources.OfType<ProjectResource>())
            {
                project.Annotations.OfType<EndpointAnnotation>().ShouldAllBe(endpoint => endpoint.Port == null);
                DaprSidecarAnnotation? sidecar = project.Annotations.OfType<DaprSidecarAnnotation>().SingleOrDefault();
                if (sidecar is null) continue;
                DaprSidecarOptions options = sidecar.Sidecar.Annotations.OfType<DaprSidecarOptionsAnnotation>().Single().Options;
                options.AppId.ShouldBe(project.Name);
                options.AppPort.ShouldBeNull();
                options.DaprHttpPort.ShouldBeNull();
                options.DaprGrpcPort.ShouldBeNull();
                options.DaprInternalGrpcPort.ShouldBeNull();
                options.MetricsPort.ShouldBeNull();
                options.EnableAppHealthCheck.ShouldBe(true);
                options.AppHealthCheckPath.ShouldBe("/alive");
                options.RuntimePath.ShouldBe(runtimePath ?? "/composed/runtime");
                options.PlacementHostAddress.ShouldBe("localhost:61005");
                options.SchedulerHostAddress.ShouldBe("localhost:61006");
                string configurationPath = options.Config.ShouldNotBeNull();
                Path.GetDirectoryName(configurationPath).ShouldBe(discovery);
                Path.GetFileName(discovery).ShouldStartWith(WorksAppHostDiscoveryIsolation.DirectoryPrefix);
                configurations.Add(project.Name, configurationPath);
                var yaml = new YamlStream();
                yaml.Load(new StringReader(File.ReadAllText(configurationPath)));
                var document = (YamlMappingNode)yaml.Documents.Single().RootNode;
                var spec = (YamlMappingNode)document.Children[new YamlScalarNode("spec")];
                var resolution = (YamlMappingNode)spec.Children[new YamlScalarNode("nameResolution")];
                ((YamlScalarNode)resolution.Children[new YamlScalarNode("component")]).Value.ShouldBe("sqlite");
                ((YamlScalarNode)resolution.Children[new YamlScalarNode("version")]).Value.ShouldBe("v1");
                var configuration = (YamlMappingNode)resolution.Children[new YamlScalarNode("configuration")];
                ((YamlScalarNode)configuration.Children[new YamlScalarNode("connectionString")]).Value
                    .ShouldBe(Path.Combine(discovery, WorksAppHostDiscoveryIsolation.RegistryFileName));
                var mtls = (YamlMappingNode)spec.Children[new YamlScalarNode("mtls")];
                ((YamlScalarNode)mtls.Children[new YamlScalarNode("sentryAddress")]).Value.ShouldBe("127.0.0.1:61001");
            }
            configurations.Keys.Order().ShouldBe(applications.Order());
            configurations["eventstore"].ShouldBe(configurations["eventstore-admin"]);
            configurations["works"].ShouldBe(configurations["eventstore-operations"]);
            configurations.Values.Distinct().Count().ShouldBe(2);
            File.ReadAllText(firstPolicy).ShouldBe(Policy);
            File.ReadAllText(secondPolicy).ShouldBe(Policy.Replace("accesscontrol-works", "accesscontrol-eventstore", StringComparison.Ordinal));
            foreach ((string name, int port) in new[]
            {
                ("dapr-sentry", ports.Sentry), ("dapr-placement-mtls", ports.Placement), ("dapr-scheduler-mtls", ports.Scheduler),
            })
                builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == name)
                    .Annotations.OfType<EndpointAnnotation>().Single(endpoint => endpoint.Name == "grpc").Port.ShouldBe(port);
            List<object> arguments = [];
            foreach (CommandLineArgsCallbackAnnotation callback in scheduler.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
                await callback.Callback(new CommandLineArgsCallbackContext(arguments, TestContext.Current.CancellationToken));
            arguments.ShouldBe(["--preserved", "--override-broadcast-host-port=localhost:61006"]);
        }
        finally
        {
            Directory.Delete(discovery, recursive: true);
            Directory.Delete(fixture, recursive: true);
        }
    }

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
