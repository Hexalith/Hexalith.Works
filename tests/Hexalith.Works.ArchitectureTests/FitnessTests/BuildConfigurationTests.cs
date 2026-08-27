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
    public void P0_RootAnalyzerSeveritiesMatchWarningsAsErrorsPolicy()
    {
        string root = RepositoryRoot.Locate();
        string editorConfig = File.ReadAllText(Path.Combine(root, ".editorconfig"));
        XDocument buildProps = XDocument.Load(Path.Combine(root, "Directory.Build.props"));

        PropertyValue(buildProps, "TreatWarningsAsErrors").ShouldBe(
            "true",
            "Directory.Build.props must keep TreatWarningsAsErrors enabled.");

        foreach (string analyzerId in new[] { "CA1062", "CA1822", "CA2007" })
        {
            string[] severities = RootCSharpAnalyzerSeverities(editorConfig, analyzerId);
            severities.Length.ShouldBe(
                1,
                $"The root .editorconfig [*.cs] section must declare exactly one explicit severity for {analyzerId}.");
            severities[0].ShouldBe(
                "error",
                $"The root .editorconfig [*.cs] section must declare {analyzerId} as an error.");

            PropertyContainsWarning(buildProps, "NoWarn", analyzerId).ShouldBeFalse(
                $"Directory.Build.props NoWarn must not exempt {analyzerId}.");
            PropertyContainsWarning(buildProps, "WarningsNotAsErrors", analyzerId).ShouldBeFalse(
                $"Directory.Build.props WarningsNotAsErrors must not exempt {analyzerId}.");
        }
    }

    [Fact]
    public void P0_AnalyzerSeverityParserHonorsEditorConfigSectionApplicability()
    {
        const string editorConfig = """
            [*]
            dotnet_diagnostic.CA1062.severity = error

            [*.cs]
            dotnet_diagnostic.CA1822.severity = error

            [tests/**/*.cs]
            dotnet_diagnostic.CA2007.severity = error
            """;

        RootCSharpAnalyzerSeverities(editorConfig, "CA1062").ShouldBeEmpty(
            "A global declaration must not satisfy the explicit root C# analyzer policy.");
        RootCSharpAnalyzerSeverities(editorConfig, "CA1822").ShouldBe(new[] { "error" });
        RootCSharpAnalyzerSeverities(editorConfig, "CA2007").ShouldBeEmpty(
            "A narrow path declaration must not satisfy the explicit root C# analyzer policy.");
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

    private static string[] RootCSharpAnalyzerSeverities(string editorConfig, string analyzerId)
    {
        List<string> severities = [];
        bool isRootCSharpSection = false;
        string settingName = $"dotnet_diagnostic.{analyzerId}.severity";

        foreach (string rawLine in editorConfig.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                isRootCSharpSection = string.Equals(line, "[*.cs]", StringComparison.Ordinal);
                continue;
            }

            if (!isRootCSharpSection)
            {
                continue;
            }

            string[] parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[0], settingName, StringComparison.OrdinalIgnoreCase))
            {
                severities.Add(parts[1]);
            }
        }

        return [.. severities];
    }

    private static bool PropertyContainsWarning(XDocument document, string propertyName, string warningId)
        => document.Descendants()
            .Where(element => element.Name.LocalName == propertyName)
            .SelectMany(element => element.Value.Split(
                [';', ',', ' ', '\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(value => string.Equals(value, warningId, StringComparison.OrdinalIgnoreCase));
}
