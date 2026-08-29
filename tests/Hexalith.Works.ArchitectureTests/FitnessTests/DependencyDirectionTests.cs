using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class DependencyDirectionTests
{
    private static readonly IReadOnlyDictionary<string, (string[] Allowed, string Rationale)> _governedProjectReferences =
        new Dictionary<string, (string[] Allowed, string Rationale)>(StringComparer.Ordinal)
        {
            ["Hexalith.Works.Contracts"] = (
                [
                    "references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Hexalith.EventStore.Contracts.csproj",
                    "references/Hexalith.PolymorphicSerializations/src/libraries/Hexalith.PolymorphicSerializations/Hexalith.PolymorphicSerializations.csproj",
                    "references/Hexalith.PolymorphicSerializations/src/libraries/Hexalith.PolymorphicSerializations.CodeGenerators/Hexalith.PolymorphicSerializations.CodeGenerators.csproj",
                ],
                "Contracts may reference EventStore.Contracts plus the PolymorphicSerializations library and its non-output analyzer project."),
            ["Hexalith.Works.Server"] = (
                ["src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj"],
                "Server owns the pure decision core and must reference inward to Contracts only."),
            ["Hexalith.Works.Projections"] = (
                ["src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj"],
                "Projections build read models from the v1 catalog and must reference inward to Contracts only."),
            ["Hexalith.Works.Reactor"] = (
                ["src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj"],
                "Reactor is a pure adapter-ring translator and must reference inward to Contracts only."),
        };

    [Fact]
    public void P0_SourceProjectReferencesFollowWorksArchitectureDirection()
    {
        string root = RepositoryRoot.Locate();

        _governedProjectReferences.Keys.ShouldBe(
            KernelDependencyPolicy.GovernedProjects,
            ignoreOrder: true,
            customMessage: "Every governed source project must have one exact dependency-direction allowlist, with no stale rules.");

        foreach ((string project, (string[] allowedReferences, string rationale)) in _governedProjectReferences)
        {
            MsBuildProjectSnapshot snapshot = EvaluateProject(
                Path.Combine(root, "src", project, project + ".csproj"));

            AssertExactProjectReferences(
                snapshot,
                allowedReferences.Select(path => Path.Combine(root, path)),
                $"{project} must retain its canonical architecture dependency-direction allowlist: {rationale}");
        }
    }

    [Fact]
    public void P0_ArchitectureTestReferencesCoverEveryGovernedProject()
    {
        string root = RepositoryRoot.Locate();

        KernelDependencyPolicy.ReconcileSourceProjects(root).ShouldBeEmpty(
            "Every source project must be deliberately classified before governed restore coverage is compared.");

        MsBuildProjectSnapshot snapshot = EvaluateProject(
            Path.Combine(root, "tests", "Hexalith.Works.ArchitectureTests", "Hexalith.Works.ArchitectureTests.csproj"));
        ProjectReferenceDifferences(
            snapshot,
            KernelDependencyPolicy.GovernedProjects.Select(project =>
                Path.Combine(root, "src", project, project + ".csproj")),
            referencePath => KernelDependencyPolicy.SourceProjects.Any(project =>
                MsBuildProjectEvaluation.PathComparer.Equals(
                    referencePath,
                    Path.GetFullPath(Path.Combine(root, "src", project, project + ".csproj")))))
            .ShouldBeEmpty(
                "The architecture-test project must reference every governed Works source project; unrelated test helpers remain allowed.");
    }

    [Fact]
    public void ArchitectureTestCoverageAllowsUnrelatedHelperProjectReferences()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string governedProject = WriteProject(
                temporaryRoot.FullName,
                "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj",
                "<Project />");
            string helperProject = WriteProject(
                temporaryRoot.FullName,
                "tests/TestHelper/TestHelper.csproj",
                "<Project />");
            string architectureProject = WriteProject(
                temporaryRoot.FullName,
                "tests/Architecture/Architecture.csproj",
                $"<Project><ItemGroup><ProjectReference Include=\"{XmlPath(governedProject)}\" /><ProjectReference Include=\"{XmlPath(helperProject)}\" /></ItemGroup></Project>");

            MsBuildProjectSnapshot snapshot = EvaluateProject(architectureProject);
            ProjectReferenceDifferences(
                snapshot,
                [governedProject],
                referencePath => MsBuildProjectEvaluation.PathComparer.Equals(referencePath, governedProject))
                .ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void ImportedAddRemoveAndReleaseConditionsProduceTheFinalReferenceSet()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string removedProject = WriteProject(temporaryRoot.FullName, "Removed/Dependency.csproj", "<Project />");
            string releaseProject = WriteProject(temporaryRoot.FullName, "Release/Dependency.csproj", "<Project />");
            string debugProject = WriteProject(temporaryRoot.FullName, "Debug/Dependency.csproj", "<Project />");
            string nestedImport = WriteProject(
                temporaryRoot.FullName,
                "nested.props",
                $"""
                <Project>
                  <ItemGroup>
                    <ProjectReference Include="{XmlPath(removedProject)}" />
                    <ProjectReference Include="{XmlPath(releaseProject)}" Condition="'$(Configuration)' == 'Release'" />
                    <ProjectReference Include="{XmlPath(debugProject)}" Condition="'$(Configuration)' == 'Debug'" />
                  </ItemGroup>
                </Project>
                """);
            string outerImport = WriteProject(
                temporaryRoot.FullName,
                "outer.targets",
                $"<Project><Import Project=\"{XmlPath(nestedImport)}\" /></Project>");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"""
                <Project>
                  <Import Project="{XmlPath(outerImport)}" />
                  <ItemGroup>
                    <ProjectReference Remove="{XmlPath(removedProject)}" />
                  </ItemGroup>
                </Project>
                """);

            MsBuildProjectSnapshot snapshot = EvaluateProject(projectPath);

            AssertExactProjectReferences(
                snapshot,
                [releaseProject],
                "Release evaluation must observe imported additions, removals, and conditions exactly as MSBuild does.");
            snapshot.ImportPaths.ShouldBe(
                [Path.GetFullPath(outerImport), Path.GetFullPath(nestedImport)],
                ignoreOrder: true);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void MultiTargetEvaluationMergesTargetFrameworkSpecificReferencesAndCustomImports()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string netNineProject = WriteProject(temporaryRoot.FullName, "NetNine/Dependency.csproj", "<Project />");
            string netTenProject = WriteProject(temporaryRoot.FullName, "NetTen/Dependency.csproj", "<Project />");
            string nestedImport = WriteProject(
                temporaryRoot.FullName,
                "imports/nested.props",
                $"<Project><ItemGroup><ProjectReference Include=\"{XmlPath(netTenProject)}\" /></ItemGroup></Project>");
            string targetFrameworkImport = WriteProject(
                temporaryRoot.FullName,
                "imports/net10.targets",
                $"<Project><Import Project=\"{XmlPath(nestedImport)}\" /></Project>");
            string generatedImport = WriteProject(
                temporaryRoot.FullName,
                "obj/Owner.generated.props",
                "<Project />");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"""
                <Project>
                  <PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks></PropertyGroup>
                  <Import Project="{XmlPath(generatedImport)}" />
                  <Import Project="{XmlPath(targetFrameworkImport)}" Condition="'$(TargetFramework)' == 'net10.0'" />
                  <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
                    <ProjectReference Include="{XmlPath(netNineProject)}" />
                  </ItemGroup>
                </Project>
                """);

            MsBuildProjectSnapshot snapshot = EvaluateProject(projectPath);

            AssertExactProjectReferences(
                snapshot,
                [netNineProject, netTenProject],
                "Every Release target framework must contribute its evaluated dependency set.");
            snapshot.ImportPaths.ShouldContain(Path.GetFullPath(targetFrameworkImport));
            snapshot.ImportPaths.ShouldContain(Path.GetFullPath(nestedImport));
            snapshot.ImportPaths.ShouldNotContain(Path.GetFullPath(generatedImport));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void BuildOutputSegmentAboveTheProjectDoesNotExcludeCustomImports()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            // The checkout itself lives under an 'obj' segment here: only segments below the project may
            // classify an import as generated, otherwise the whole custom-import closure silently empties.
            string customImport = WriteProject(
                temporaryRoot.FullName,
                "obj/checkout/imports/custom.props",
                "<Project />");
            string generatedImport = WriteProject(
                temporaryRoot.FullName,
                "obj/checkout/obj/Owner.generated.props",
                "<Project />");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "obj/checkout/Owner.csproj",
                $"""
                <Project>
                  <Import Project="{XmlPath(customImport)}" />
                  <Import Project="{XmlPath(generatedImport)}" />
                </Project>
                """);

            MsBuildProjectSnapshot snapshot = EvaluateProject(projectPath);

            snapshot.ImportPaths.ShouldBe([Path.GetFullPath(customImport)]);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void SameBasenameProjectOutsideTheAllowlistIsRejectedByCanonicalIdentity()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string allowedProject = WriteProject(
                temporaryRoot.FullName,
                "allowed/Hexalith.Works.Contracts.csproj",
                "<Project />");
            string unrelatedProject = WriteProject(
                temporaryRoot.FullName,
                "unrelated/Hexalith.Works.Contracts.csproj",
                "<Project />");
            string ownerProject = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"<Project><ItemGroup><ProjectReference Include=\"{XmlPath(unrelatedProject)}\" /></ItemGroup></Project>");

            MsBuildProjectSnapshot snapshot = EvaluateProject(ownerProject);
            string[] differences = ProjectReferenceDifferences(snapshot, [allowedProject]);

            differences.ShouldContain(difference => difference.Contains(Path.GetFullPath(allowedProject), StringComparison.Ordinal));
            differences.ShouldContain(difference => difference.Contains(Path.GetFullPath(unrelatedProject), StringComparison.Ordinal));
            MsBuildProjectEvaluation.PathComparer.Equals(allowedProject, unrelatedProject).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void MissingImportFailsEvaluationClosedWithOwningPaths()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string missingImport = Path.Combine(temporaryRoot.FullName, "missing.props");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"<Project><Import Project=\"{XmlPath(missingImport)}\" /></Project>");

            MsBuildProjectEvaluation.TryEvaluate(projectPath, out _, out string diagnostic).ShouldBeFalse();
            diagnostic.ShouldContain(projectPath, Case.Sensitive);
            diagnostic.ShouldContain(missingImport, Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void OpaqueEvaluatedPackageIdentityFailsClosed()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                "<Project><ItemGroup><PackageReference Include=\"$(UndefinedPackage)\" /></ItemGroup></Project>");

            MsBuildProjectEvaluation.TryEvaluate(projectPath, out _, out string diagnostic).ShouldBeFalse();
            diagnostic.ShouldContain("PackageReference", Case.Sensitive);
            diagnostic.ShouldContain("UndefinedPackage", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData("ProjectReference")]
    [InlineData("PackageReference")]
    [InlineData("PackageVersion")]
    [InlineData("FrameworkReference")]
    [InlineData("Reference")]
    public void SemicolonOnlySupportedDependencyIncludeFailsClosed(string itemType)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"<Project><ItemGroup><{itemType} Include=\" ; ; \" /></ItemGroup></Project>");

            MsBuildProjectEvaluation.TryEvaluate(projectPath, out _, out string diagnostic).ShouldBeFalse();
            diagnostic.ShouldContain(itemType, Case.Sensitive);
            diagnostic.ShouldContain("empty or unresolved Include", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void SupportedDependencyItemKindsAreCanonicalizedCaseInsensitively()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string referencedProject = WriteProject(temporaryRoot.FullName, "Dependency.csproj", "<Project />");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"""
                <Project>
                  <ItemGroup>
                    <pRoJeCtReFeReNcE Include="{XmlPath(referencedProject)}" />
                    <pAcKaGeReFeReNcE Include="Other.Package" />
                    <pAcKaGeVeRsIoN Include="Other.Package" Version="1.0.0" />
                    <fRaMeWoRkReFeReNcE Include="Microsoft.NETCore.App" />
                    <rEfErEnCe Include="System.Runtime" />
                  </ItemGroup>
                </Project>
                """);

            MsBuildProjectSnapshot snapshot = EvaluateProject(projectPath);

            snapshot.Items.Select(item => item.ItemType).ShouldBe(
                ["ProjectReference", "PackageReference", "PackageVersion", "FrameworkReference", "Reference"],
                ignoreOrder: true);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void P0_AppHostReferencesOnlyWorksTopologyProjects()
    {
        string root = RepositoryRoot.Locate();
        string appHostPath = Path.Combine(root, "src", "Hexalith.Works.AppHost", "Hexalith.Works.AppHost.csproj");
        MsBuildProjectSnapshot snapshot = EvaluateProject(appHostPath);

        // The Release-lane evaluation below resolves conditions away, so a conditional, opaque, or malformed
        // declaration would leave the exact topology silently short. Reject those declarations first.
        KernelDependencyPolicy.DeclaredReferenceNames(appHostPath, "ProjectReference")
            .Where(reference => reference.StartsWith('<'))
            .ShouldBeEmpty("AppHost must declare every project reference in a form the exact topology gate can evaluate.");

        AssertExactProjectReferences(
            snapshot,
            [
                Path.Combine(root, "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj"),
                Path.Combine(root, "src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj"),
                Path.Combine(root, "src/Hexalith.Works.Reactor/Hexalith.Works.Reactor.csproj"),
                Path.Combine(root, "src/Hexalith.Works.Server/Hexalith.Works.Server.csproj"),
                Path.Combine(root, "src/Hexalith.Works.ServiceDefaults/Hexalith.Works.ServiceDefaults.csproj"),
                Path.Combine(root, "references/Hexalith.EventStore/src/Hexalith.EventStore.Aspire/Hexalith.EventStore.Aspire.csproj"),
                Path.Combine(root, "references/Hexalith.EventStore/src/Hexalith.EventStore.Operations/Hexalith.EventStore.Operations.csproj"),
            ],
            "AppHost should wire only the Works topology plus EventStore Aspire and operations workloads.");
    }

    [Fact]
    public void P0_ContractsDoesNotReferenceSiblingImplementationProjects()
    {
        string root = RepositoryRoot.Locate();
        string[] forbiddenSiblingProjects =
        [
            "Hexalith.Parties.Client",
            "Hexalith.Parties.Server",
            "Hexalith.Conversations.Client",
            "Hexalith.Conversations.Server",
            "Hexalith.Tenants.Server",
            "Hexalith.EventStore.Client",
            "Hexalith.EventStore.Server",
            "Hexalith.EventStore.Aspire",
        ];

        MsBuildProjectSnapshot snapshot = EvaluateProject(
            Path.Combine(root, "src", "Hexalith.Works.Contracts", "Hexalith.Works.Contracts.csproj"));
        string[] violations = [.. snapshot.ItemsOfType("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.CanonicalPath!))
            .Where(reference => forbiddenSiblingProjects.Any(forbidden =>
                reference.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase)))];

        violations.ShouldBeEmpty(
            "Works contracts may expose only reference IDs and must not depend on sibling client, server, adapter, or runtime projects.");
    }

    [Fact]
    public void P0_ContractsAnalyzerProjectReferenceRemainsInTheEvaluatedSet()
    {
        string root = RepositoryRoot.Locate();
        string analyzerPath = Path.GetFullPath(Path.Combine(
            root,
            "references/Hexalith.PolymorphicSerializations/src/libraries/Hexalith.PolymorphicSerializations.CodeGenerators/Hexalith.PolymorphicSerializations.CodeGenerators.csproj"));
        MsBuildProjectSnapshot snapshot = EvaluateProject(
            Path.Combine(root, "src", "Hexalith.Works.Contracts", "Hexalith.Works.Contracts.csproj"));

        MsBuildEvaluatedItem analyzer = snapshot.ItemsOfType("ProjectReference")
            .Where(reference => MsBuildProjectEvaluation.PathComparer.Equals(reference.CanonicalPath, analyzerPath))
            .ShouldHaveSingleItem();
        analyzer.ReferenceOutputAssembly.ShouldBe("false", StringCompareShould.IgnoreCase);
        analyzer.OutputItemType.ShouldBe("Analyzer", StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void P0_HexalithDependenciesUseProjectReferencesNotPackageReferences()
    {
        string root = RepositoryRoot.Locate();
        string approvedCatalog = Path.Combine(
            root,
            "references",
            "Hexalith.Builds",
            "Props",
            "Directory.Packages.props");
        string[] projectFiles = [.. Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !KernelDependencyPolicy.IsBuildOutput(path))];

        projectFiles.ShouldNotBeEmpty("Expected to discover Hexalith.Works project files to govern.");
        projectFiles.ShouldContain(
            path => Path.GetFileName(path) == "Hexalith.Works.Contracts.csproj",
            "Hexalith.Works.Contracts.csproj must be discovered for this fitness guard to be meaningful.");

        string[] violations = [.. projectFiles.SelectMany(project =>
            KernelDependencyPolicy.EvaluateHexalithSourceConsumption(project, approvedCatalog))];

        violations.ShouldBeEmpty(
            "Hexalith libraries must be consumed from checked-out sibling source; only the externally owned shared Builds catalog may define their versions.");
    }

    [Fact]
    public void EvaluatedPackageVariantsAndCatalogOwnershipFailClosed()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string sharedCatalog = WriteProject(
                temporaryRoot.FullName,
                "references/Hexalith.Builds/Props/Directory.Packages.props",
                "<Project><ItemGroup><PackageVersion Include=\"Hexalith.Shared\" Version=\"1.0.0\" /></ItemGroup></Project>");
            string localVersions = WriteProject(
                temporaryRoot.FullName,
                "local.props",
                "<Project><ItemGroup><PackageVersion Update=\"Hexalith.Shared\" Version=\"2.0.0\" /><pAcKaGeVeRsIoN Include=\"Other.Local;hExAlItH.Local\" Version=\"1.0.0\" /><pAcKaGeReFeReNcE Include=\"Other.Package;hExAlItH.Forbidden\" /></ItemGroup></Project>");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"""
                <Project>
                  <Import Project="{XmlPath(sharedCatalog)}" />
                  <Import Project="{XmlPath(localVersions)}" />
                </Project>
                """);

            string[] violations = KernelDependencyPolicy.EvaluateHexalithSourceConsumption(
                projectPath,
                sharedCatalog);

            violations.Length.ShouldBe(3);
            violations.ShouldContain(violation => violation.Contains("hExAlItH.Forbidden", StringComparison.Ordinal)
                && violation.Contains(localVersions, StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("Hexalith.Shared", StringComparison.Ordinal)
                && violation.Contains(localVersions, StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("hExAlItH.Local", StringComparison.Ordinal)
                && violation.Contains(localVersions, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void ConditionalOwningProjectPackageDeclarationsFailSourceConsumptionClosed()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string sharedCatalog = WriteProject(
                temporaryRoot.FullName,
                "references/Hexalith.Builds/Props/Directory.Packages.props",
                "<Project><ItemGroup><PackageVersion Include=\"Hexalith.Shared\" Version=\"1.0.0\" /></ItemGroup></Project>");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"""
                <Project>
                  <Import Project="{XmlPath(sharedCatalog)}" />
                  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                    <PackageReference Include="Hexalith.DebugOnly" />
                  </ItemGroup>
                  <ItemGroup>
                    <PackageVersion Include="Hexalith.Conditional" Version="1.0.0" Condition="'$(Configuration)' == 'Debug'" />
                  </ItemGroup>
                </Project>
                """);

            string[] violations = KernelDependencyPolicy.EvaluateHexalithSourceConsumption(
                projectPath,
                sharedCatalog);

            violations.Length.ShouldBe(2);
            violations.ShouldContain(violation => violation.Contains("<conditional PackageReference 'Hexalith.DebugOnly'>", StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("<conditional PackageVersion 'Hexalith.Conditional'>", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void GlobalPackageReferenceHexalithConsumptionFailsClosed()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string sharedCatalog = WriteProject(
                temporaryRoot.FullName,
                "references/Hexalith.Builds/Props/Directory.Packages.props",
                "<Project><ItemGroup><PackageVersion Include=\"Hexalith.Shared\" Version=\"1.0.0\" /></ItemGroup></Project>");
            string injectedPackages = WriteProject(
                temporaryRoot.FullName,
                "injected.props",
                "<Project><ItemGroup><gLoBaLpAcKaGeReFeReNcE Include=\"hExAlItH.Injected\" Version=\"1.0.0\" /></ItemGroup></Project>");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"""
                <Project>
                  <Import Project="{XmlPath(sharedCatalog)}" />
                  <Import Project="{XmlPath(injectedPackages)}" />
                </Project>
                """);

            string violation = KernelDependencyPolicy.EvaluateHexalithSourceConsumption(projectPath, sharedCatalog)
                .ShouldHaveSingleItem();

            violation.ShouldContain("GlobalPackageReference", Case.Sensitive);
            violation.ShouldContain("hExAlItH.Injected", Case.Sensitive);
            violation.ShouldContain(injectedPackages, Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void SharedBuildsCatalogPackageVersionsRemainAccepted()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string sharedCatalog = WriteProject(
                temporaryRoot.FullName,
                "references/Hexalith.Builds/Props/Directory.Packages.props",
                "<Project><ItemGroup><PackageVersion Include=\"Hexalith.Shared\" Version=\"1.0.0\" /></ItemGroup></Project>");
            string projectPath = WriteProject(
                temporaryRoot.FullName,
                "Owner.csproj",
                $"<Project><Import Project=\"{XmlPath(sharedCatalog)}\" /></Project>");

            KernelDependencyPolicy.EvaluateHexalithSourceConsumption(projectPath, sharedCatalog)
                .ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    private static MsBuildProjectSnapshot EvaluateProject(string projectPath)
    {
        bool evaluated = MsBuildProjectEvaluation.TryEvaluate(
            projectPath,
            out MsBuildProjectSnapshot? snapshot,
            out string diagnostic);

        evaluated.ShouldBeTrue(diagnostic);
        return snapshot!;
    }

    private static void AssertExactProjectReferences(
        MsBuildProjectSnapshot snapshot,
        IEnumerable<string> expectedPaths,
        string message)
        => ProjectReferenceDifferences(snapshot, expectedPaths).ShouldBeEmpty(message);

    private static string[] ProjectReferenceDifferences(
        MsBuildProjectSnapshot snapshot,
        IEnumerable<string> expectedPaths,
        Func<string, bool>? includeActualPath = null)
    {
        string[] expected = [.. expectedPaths.Select(Path.GetFullPath).Distinct(MsBuildProjectEvaluation.PathComparer)];
        string[] actual = [.. snapshot.ItemsOfType("ProjectReference")
            .Select(reference => reference.CanonicalPath ?? $"<missing canonical path for {reference.Identity}>")
            .Where(path => includeActualPath is null || includeActualPath(path))
            .Distinct(MsBuildProjectEvaluation.PathComparer)];
        var differences = new List<string>();

        differences.AddRange(expected
            .Except(actual, MsBuildProjectEvaluation.PathComparer)
            .Select(path => $"Expected canonical ProjectReference '{path}' was not evaluated for '{snapshot.ProjectPath}'."));
        differences.AddRange(actual
            .Except(expected, MsBuildProjectEvaluation.PathComparer)
            .Select(path => $"Unexpected canonical ProjectReference '{path}' was evaluated for '{snapshot.ProjectPath}'."));
        if (actual.Length != expected.Length)
        {
            differences.Add(
                $"ProjectReference cardinality for '{snapshot.ProjectPath}' was {actual.Length}; expected {expected.Length}. Actual: {string.Join(", ", actual)}.");
        }

        return [.. differences.Order(StringComparer.Ordinal)];
    }

    private static string WriteProject(string root, string relativePath, string contents)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private static string XmlPath(string path) => path.Replace('\\', '/');
}
