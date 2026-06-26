using System.Text.Json.Nodes;
using System.Xml.Linq;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class BuildConfigurationTests
{
    [Fact]
    public void P0_GlobalJsonPinsSdkTestRunnerAndAspireSdk()
    {
        string root = RepositoryRoot.Locate();
        JsonNode globalJson = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "global.json")))!;

        globalJson["sdk"]?["version"]?.GetValue<string>().ShouldBe("10.0.301");
        globalJson["sdk"]?["rollForward"]?.GetValue<string>().ShouldBe("latestPatch");
        globalJson["test"]?["runner"]?.GetValue<string>().ShouldBe("Microsoft.Testing.Platform");
        // Aspire reconciled to 13.4.6 to match the checked-out Hexalith.EventStore submodule.
        globalJson["msbuild-sdks"]?["Aspire.AppHost.Sdk"]?.GetValue<string>().ShouldBe("13.4.6");
    }

    [Fact]
    public void P0_RootBuildConfigurationKeepsWarningsAsErrorsAndCentralPackages()
    {
        string root = RepositoryRoot.Locate();
        XDocument buildProps = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        XDocument packageProps = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));

        PropertyValue(buildProps, "TargetFramework").ShouldBe("net10.0");
        PropertyValue(buildProps, "Nullable").ShouldBe("enable");
        PropertyValue(buildProps, "ImplicitUsings").ShouldBe("enable");
        PropertyValue(buildProps, "TreatWarningsAsErrors").ShouldBe("true");
        PropertyValue(buildProps, "MinVerTagPrefix").ShouldBe("v");

        PropertyValue(packageProps, "ManagePackageVersionsCentrally").ShouldBe("true");
        PropertyValue(packageProps, "CentralPackageTransitivePinningEnabled").ShouldBe("true");
    }

    [Fact]
    public void P0_AspireConfigPointsAtWorksAppHost()
    {
        string root = RepositoryRoot.Locate();
        JsonNode aspireConfig = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "aspire.config.json")))!;

        aspireConfig["appHost"]?["path"]?.GetValue<string>()
            .ShouldBe("src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj");
    }

    [Fact]
    public void P1_EventStoreImplementationConstraintsAreRecorded()
    {
        string root = RepositoryRoot.Locate();
        string constraints = File.ReadAllText(Path.Combine(root, "docs", "eventstore-api-surface-constraints.md"));

        constraints.ShouldContain("does not expose an explicit `expectedVersion` append argument");
        constraints.ShouldContain("Dapr state-store ETag");
        constraints.ShouldContain("checkpoint-per-aggregate");
        constraints.ShouldContain("pausable");
        constraints.ShouldContain("not a shadow-projection plus atomic-swap model");
    }

    private static string? PropertyValue(XDocument document, string name)
        => document.Descendants()
            .SingleOrDefault(element => element.Name.LocalName == name)
            ?.Value;
}
