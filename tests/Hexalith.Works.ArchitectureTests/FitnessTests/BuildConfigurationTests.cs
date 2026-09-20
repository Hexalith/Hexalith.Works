using System.Diagnostics;
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

        globalJson["sdk"]?["version"]?.GetValue<string>().ShouldBe("10.0.401");
        globalJson["sdk"]?["rollForward"]?.GetValue<string>().ShouldBe("latestPatch");
        globalJson["test"]?["runner"]?.GetValue<string>().ShouldBe("Microsoft.Testing.Platform");
        // Aspire reconciled to 13.5.4 to match the checked-out Hexalith.EventStore submodule and hosting packages.
        globalJson["msbuild-sdks"]?["Aspire.AppHost.Sdk"]?.GetValue<string>().ShouldBe("13.5.4");
    }

    /// <summary>
    /// The AppHost project must carry the same pinned Aspire SDK version and the CLI bundle the live lanes need.
    /// Pinning only <c>global.json</c> leaves the project's own <c>Sdk</c> attribute free to drift, which is what
    /// actually decides the AppHost build.
    /// </summary>
    [Fact]
    public void P0_AppHostProjectPinsTheSameAspireSdkAndCliBundle()
    {
        string root = RepositoryRoot.Locate();
        string appHostProjectPath = Path.Combine(root, "src", "Hexalith.Works.AppHost", "Hexalith.Works.AppHost.csproj");
        XDocument appHostProject = XDocument.Load(appHostProjectPath);
        JsonNode globalJson = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "global.json")))!;

        string pinnedAspireSdk = globalJson["msbuild-sdks"]?["Aspire.AppHost.Sdk"]?.GetValue<string>()
            ?? throw new InvalidOperationException("global.json must pin Aspire.AppHost.Sdk.");

        appHostProject.Root.ShouldNotBeNull().Attribute("Sdk")?.Value.ShouldBe(
            $"Aspire.AppHost.Sdk/{pinnedAspireSdk}",
            "The AppHost project Sdk attribute must pin the same Aspire version as global.json.");
        PropertyValue(appHostProject, "AspireUseCliBundle").ShouldBe(
            "true",
            "The AppHost must keep the Aspire CLI bundle enabled for the live lanes.");
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
    public void P0_NuGetAuditRemainsVisibleInProjectAndSolutionRestoreModes()
    {
        string root = RepositoryRoot.Locate();
        XDocument buildProps = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        XDocument solutionProps = XDocument.Load(Path.Combine(root, "Directory.Solution.props"));

        foreach (XDocument policy in new[] { buildProps, solutionProps })
        {
            PropertyValue(policy, "NuGetAudit").ShouldBe("true");
            PropertyValue(policy, "NuGetAuditMode").ShouldBe("all");

            // An explicit audit level is required: without it, exempting every advisory code from
            // warnings-as-errors makes the audit unable to fail a build at any severity, which is
            // indistinguishable from having it switched off.
            // ci-cd-standards.md (Dependency Auditing) requires the audit to stay enabled while
            // NU1901-NU1904 remain exempt from warnings-as-errors, so an advisory surfaces in the build
            // log without blocking CI. An individual advisory is waived with NuGetAuditSuppress.
            foreach (string advisory in new[] { "NU1901", "NU1902", "NU1903", "NU1904" })
            {
                PropertyContainsWarning(policy, "WarningsNotAsErrors", advisory).ShouldBeTrue(
                    $"{advisory} must remain visible without becoming a warnings-as-errors build break.");
            }
        }
    }

    [Theory]
    [InlineData("maybe", "true", "", "")]
    [InlineData("false", "maybe", "", "")]
    [InlineData("true", "true", "", "")]
    [InlineData("false", "false", "", "")]
    [InlineData("false", "true", "true", "")]
    [InlineData("true", "false", "", "false")]
    public async Task P0_InvalidDependencyModesFailTheExecutableMsBuildGuardAsync(
        string useProjectReferences,
        string useNuGetDependencies,
        string eventStoreFromSource,
        string polymorphicFromSource)
    {
        ArgumentNullException.ThrowIfNull(useProjectReferences);
        ArgumentNullException.ThrowIfNull(useNuGetDependencies);
        ArgumentNullException.ThrowIfNull(eventStoreFromSource);
        ArgumentNullException.ThrowIfNull(polymorphicFromSource);

        string root = RepositoryRoot.Locate();
        string project = Path.Combine(root, "src", "Hexalith.Works.Contracts", "Hexalith.Works.Contracts.csproj");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-t:ValidateHexalithDependencyMode");
        start.ArgumentList.Add("-p:Configuration=Release");
        start.ArgumentList.Add($"-p:UseHexalithProjectReferences={useProjectReferences}");
        start.ArgumentList.Add($"-p:UseNuGetDeps={useNuGetDependencies}");
        if (eventStoreFromSource.Length > 0)
        {
            start.ArgumentList.Add($"-p:HexalithEventStoreFromSource={eventStoreFromSource}");
        }

        if (polymorphicFromSource.Length > 0)
        {
            start.ArgumentList.Add($"-p:HexalithPolymorphicSerializationsFromSource={polymorphicFromSource}");
        }

        using Process process = Process.Start(start).ShouldNotBeNull();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitForExitOrKillAsync(process, cancellationToken).ConfigureAwait(true);
        string standardOutput = await standardOutputTask.ConfigureAwait(true);
        string standardError = await standardErrorTask.ConfigureAwait(true);

        process.ExitCode.ShouldNotBe(0, "Every invalid or contradictory dependency mode must fail closed.");
        (standardOutput + standardError).ShouldContain("HXW0001", Case.Sensitive);
    }

    [Theory]
    [InlineData("UseHexalithProjectReferences")]
    [InlineData("UseNuGetDeps")]
    [InlineData("HexalithEventStoreFromSource")]
    [InlineData("HexalithPolymorphicSerializationsFromSource")]
    public async Task P0_NonBooleanFinalDependencyModesFailAfterProjectEvaluationAsync(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        string root = RepositoryRoot.Locate();
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.BuildConfigurationTests-");
        try
        {
            string project = Path.Combine(temporary.FullName, "FinalMode.proj");
            new XDocument(
                new XElement(
                    "Project",
                    new XElement(
                        "Import",
                        new XAttribute("Project", Path.Combine(root, "Directory.Build.props"))),
                    new XElement("PropertyGroup", new XElement(propertyName, "maybe"))))
                .Save(project);

            (int exitCode, string output) = await RunMsBuildTargetAsync(
                root,
                project,
                "-p:Configuration=Release").ConfigureAwait(true);

            exitCode.ShouldNotBe(0, $"A project-assigned non-boolean final {propertyName} must fail closed.");
            output.ShouldContain("HXW0001", Case.Sensitive);
            output.ShouldContain($"final {propertyName}", Case.Insensitive);
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task P0_ConfigurationFreeDependencyModeDefaultsToPackagesAndWarnsAsync()
    {
        string root = RepositoryRoot.Locate();
        string project = Path.Combine(root, "src", "Hexalith.Works.Contracts", "Hexalith.Works.Contracts.csproj");
        (int exitCode, string output) = await RunMsBuildTargetAsync(root, project).ConfigureAwait(true);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("HXW0002", Case.Sensitive);

        bool evaluated = MsBuildProjectEvaluation.TryEvaluate(
            project,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            out MsBuildProjectSnapshot? snapshot,
            out string diagnostic);

        evaluated.ShouldBeTrue(diagnostic);
        snapshot.ShouldNotBeNull().PropertyValue("UseHexalithProjectReferences").ShouldBe("false");
        snapshot.PropertyValue("UseNuGetDeps").ShouldBe("true");
        snapshot.PropertyValue("HexalithDependencyModeDefaulted").ShouldBe("true");
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

    private static async Task<(int ExitCode, string Output)> RunMsBuildTargetAsync(
        string root,
        string project,
        params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-t:ValidateHexalithDependencyMode");
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start).ShouldNotBeNull();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitForExitOrKillAsync(process, cancellationToken).ConfigureAwait(true);
        string standardOutput = await standardOutputTask.ConfigureAwait(true);
        string standardError = await standardErrorTask.ConfigureAwait(true);
        return (process.ExitCode, standardOutput + standardError);
    }

    private static async Task WaitForExitOrKillAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The child exited between the state check and the kill request; it still must be reaped below.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }

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
