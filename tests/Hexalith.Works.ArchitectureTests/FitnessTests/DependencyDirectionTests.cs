using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class DependencyDirectionTests
{
    private static readonly IReadOnlyDictionary<string, (string[] Allowed, string Rationale)> _governedProjectReferences =
        new Dictionary<string, (string[] Allowed, string Rationale)>(StringComparer.Ordinal)
        {
            ["Hexalith.Works.Contracts"] = (
                [],
                "Release Contracts consumes external libraries only through centrally pinned packages."),
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
            foreach (MsBuildProjectSnapshot snapshot in EvaluateSupportedLanes(
                Path.Combine(root, "src", project, project + ".csproj")))
            {
                // Contracts deliberately consumes external source in Debug; only the Works-owned edges
                // are governed by this layer-direction allowlist. The mode gate below owns external edges.
                string[] externalRoots = [.. ExternalModuleRoots(snapshot)];
                ProjectReferenceDifferences(snapshot,
                    allowedReferences.Select(path => Path.Combine(root, path)),
                    path => !externalRoots.Any(external => IsUnderRoot(path, external)))
                    .ShouldBeEmpty($"{project} must retain its canonical architecture dependency-direction allowlist in {snapshot.PropertyValue("Configuration")}: {rationale}");
            }
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
    public void P0_ContractsAnalyzerProjectReferenceRemainsInTheDebugEvaluatedSet()
    {
        string root = RepositoryRoot.Locate();
        string polymorphicSerializationsRoot = RepositoryRoot.DependencyRoot("Hexalith.PolymorphicSerializations");
        string analyzerPath = Path.GetFullPath(Path.Combine(
            polymorphicSerializationsRoot,
            "src",
            "libraries",
            "Hexalith.PolymorphicSerializations.CodeGenerators",
            "Hexalith.PolymorphicSerializations.CodeGenerators.csproj"));
        MsBuildProjectSnapshot snapshot = EvaluateProject(
            Path.Combine(root, "src", "Hexalith.Works.Contracts", "Hexalith.Works.Contracts.csproj"),
            "Debug");

        MsBuildEvaluatedItem analyzer = snapshot.ItemsOfType("ProjectReference")
            .Where(reference => MsBuildProjectEvaluation.PathComparer.Equals(reference.CanonicalPath, analyzerPath))
            .ShouldHaveSingleItem();
        analyzer.ReferenceOutputAssembly.ShouldBe("false", StringCompareShould.IgnoreCase);
        analyzer.OutputItemType.ShouldBe("Analyzer", StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void P0_ExternalHexalithDependenciesUseDebugSourceAndReleasePackages()
    {
        string root = RepositoryRoot.Locate();
        string eventStoreRoot = RepositoryRoot.DependencyRoot("Hexalith.EventStore");
        string partiesRoot = RepositoryRoot.DependencyRoot("Hexalith.Parties");
        string platformRoot = RepositoryRoot.DependencyRoot("Hexalith.Platform");
        string polymorphicSerializationsRoot = RepositoryRoot.DependencyRoot("Hexalith.PolymorphicSerializations");
        var expectations = new[]
        {
            (
                Project: "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj",
                DebugProjects: new[]
                {
                    Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Contracts", "Hexalith.EventStore.Contracts.csproj"),
                    Path.Combine(polymorphicSerializationsRoot, "src", "libraries", "Hexalith.PolymorphicSerializations", "Hexalith.PolymorphicSerializations.csproj"),
                    Path.Combine(polymorphicSerializationsRoot, "src", "libraries", "Hexalith.PolymorphicSerializations.CodeGenerators", "Hexalith.PolymorphicSerializations.CodeGenerators.csproj"),
                },
                ReleaseProjects: Array.Empty<string>(),
                ReleasePackages: new[]
                {
                    "Hexalith.EventStore.Contracts",
                    "Hexalith.PolymorphicSerializations",
                    "Hexalith.PolymorphicSerializations.CodeGenerators",
                }),
            (
                Project: "src/Hexalith.Works/Hexalith.Works.csproj",
                DebugProjects: new[]
                {
                    Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.DomainService", "Hexalith.EventStore.DomainService.csproj"),
                    Path.Combine(partiesRoot, "src", "Hexalith.Parties.Client", "Hexalith.Parties.Client.csproj"),
                },
                ReleaseProjects: Array.Empty<string>(),
                ReleasePackages: new[] { "Hexalith.EventStore.DomainService", "Hexalith.Parties.Client" }),
            (
                Project: "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj",
                DebugProjects: new[]
                {
                    Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Admin.Abstractions", "Hexalith.EventStore.Admin.Abstractions.csproj"),
                    Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Testing", "Hexalith.EventStore.Testing.csproj"),
                    Path.Combine(platformRoot, "src", "Hexalith.Platform.Identity", "Hexalith.Platform.Identity.csproj"),
                    Path.Combine(platformRoot, "src", "Hexalith.Platform.Composition", "Hexalith.Platform.Composition.csproj"),
                },
                ReleaseProjects: new[]
                {
                    Path.Combine(platformRoot, "src", "Hexalith.Platform.Identity", "Hexalith.Platform.Identity.csproj"),
                    Path.Combine(platformRoot, "src", "Hexalith.Platform.Composition", "Hexalith.Platform.Composition.csproj"),
                },
                ReleasePackages: new[]
                {
                    "Hexalith.EventStore.Admin.Abstractions",
                    "Hexalith.EventStore.Testing",
                }),
        };

        foreach ((string project, string[] debugProjects, string[] releaseProjects, string[] releasePackages) in expectations)
        {
            string projectPath = Path.Combine(root, project);
            IReadOnlyList<MsBuildProjectSnapshot> snapshots = EvaluateSupportedLanes(projectPath);
            MsBuildProjectSnapshot debug = snapshots.Single(snapshot => snapshot.PropertyValue("Configuration") == "Debug");
            MsBuildProjectSnapshot release = snapshots.Single(snapshot => snapshot.PropertyValue("Configuration") == "Release");

            ExternalProjectReferences(debug).ShouldBe(
                debugProjects.Select(Path.GetFullPath),
                ignoreOrder: true,
                customMessage: $"{project} must consume external Hexalith dependencies from sibling source in Debug.");
            debug.Items.Where(item => item.ItemType is "PackageReference" or "GlobalPackageReference")
                .Where(item => item.Identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Identity)
                .ShouldBeEmpty($"{project} must not mix external Hexalith packages into Debug source mode.");

            ExternalProjectReferences(release).ShouldBe(
                releaseProjects.Select(path => Path.GetFullPath(Path.Combine(root, path))),
                ignoreOrder: true,
                customMessage: $"{project} has an unexpected external source edge in Release package mode.");
            release.Items.Where(item => item.ItemType is "PackageReference" or "GlobalPackageReference")
                .Where(item => item.Identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Identity)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ShouldBe(
                    releasePackages,
                    ignoreOrder: true,
                    customMessage: $"{project} must consume the exact centrally pinned external packages in Release.");
        }

        AssertNoUndeclaredExternalHexalithPackageReferences(root, expectations.Select(expectation => expectation.Project));
        AssertSharedCatalogOwnsRepositoryHexalithVersions(root);
    }

    /// <summary>Source Contracts avoids package promotion while Release retains the same selected SDK candidate.</summary>
    [Fact]
    public void P0_EventStoreContractsCentralPinMatchesSelectedDependencyMode()
    {
        string root = RepositoryRoot.Locate();
        string VersionIn(string file) => System.Xml.Linq.XDocument.Load(Path.Combine(root, file))
            .Descendants("HexalithEventStoreVersion").ShouldHaveSingleItem().Value;
        string candidate = VersionIn("Directory.Packages.props");
        candidate.ShouldNotBeNullOrWhiteSpace();
        VersionIn("Directory.Build.props").ShouldBe(candidate);
        System.Xml.Linq.XDocument.Load(Path.Combine(root, "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj"))
            .Descendants("AssemblyMetadata").Single(item => (string?)item.Attribute("Include") == "WorksPinnedEventStoreVersion")
            .Attribute("Value")!.Value.ShouldBe(candidate);

        foreach (string project in new[] { "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj",
            "src/Hexalith.Works/Hexalith.Works.csproj", "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj" })
        {
            MsBuildProjectSnapshot debug = EvaluateProject(Path.Combine(root, project), "Debug");
            debug.ItemsOfType("PackageVersion").ShouldNotContain(item => item.Identity == "Hexalith.EventStore.Contracts");
            MsBuildProjectSnapshot release = EvaluateProject(Path.Combine(root, project), "Release");
            release.ItemsOfType("PackageVersion").Where(item => item.Identity == "Hexalith.EventStore.Contracts")
                .ShouldHaveSingleItem().Version.ShouldBe(candidate);
        }
    }

    [Fact]
    public void PlatformTestEdgePropagatesEffectiveSdkModeWithoutChangingSdkSiblingModes()
    {
        string root = RepositoryRoot.Locate();
        string platform = Path.Combine(RepositoryRoot.DependencyRoot("Hexalith.Platform"), "src", "Hexalith.Platform.Identity", "Hexalith.Platform.Identity.csproj");
        string gateway = Path.GetFullPath(Path.Combine(RepositoryRoot.DependencyRoot("Hexalith.EventStore"), "src", "Hexalith.EventStore.Gateway", "Hexalith.EventStore.Gateway.csproj"));
        var debug = EvaluateProject(Path.Combine(root, "tests", "Hexalith.Works.IntegrationTests", "Hexalith.Works.IntegrationTests.csproj"), "Debug");
        var edge = debug.ItemsOfType("ProjectReference").Single(item => item.CanonicalPath == Path.GetFullPath(platform));
        // The SDK repository's own sibling policy remains local: the edge selects its source without globally
        // forcing source Commons alongside the SDK's existing package consumers.
        edge.ProjectReferenceGlobalProperties.ContainsKey("UseHexalithProjectReferences").ShouldBeFalse();
        MsBuildProjectEvaluation.TryEvaluate(platform, edge.ProjectReferenceGlobalProperties,
            out MsBuildProjectSnapshot? child, out string diagnostic).ShouldBeTrue(diagnostic);
        child!.PropertyValue("UseHexalithProjectReferences").ShouldBe("true");
        child.ItemsOfType("ProjectReference").Select(item => item.CanonicalPath).ShouldContain(gateway);
        child.ItemsOfType("PackageReference").Select(item => item.Identity).ShouldNotContain("Hexalith.EventStore.Gateway");
        var release = EvaluateProject(Path.Combine(root, "tests", "Hexalith.Works.IntegrationTests", "Hexalith.Works.IntegrationTests.csproj"), "Release");
        release.ItemsOfType("ProjectReference").ShouldContain(item => item.CanonicalPath == Path.GetFullPath(platform));
        release.ItemsOfType("PackageReference").ShouldNotContain(item => item.Identity == "Hexalith.Platform.Identity");
        var packagedChild = EvaluateProject(platform, "Release");
        packagedChild.ItemsOfType("ProjectReference").ShouldBeEmpty();
        packagedChild.ItemsOfType("PackageReference").ShouldContain(item => item.Identity == "Hexalith.EventStore.Gateway");
    }

    /// <summary>
    /// Verifies every evaluated Hexalith package version in the actual Works project set originates from the
    /// shared Builds catalog. Fixture-only checks would not catch a local <c>PackageVersion</c> override in this
    /// repository.
    /// </summary>
    private static void AssertSharedCatalogOwnsRepositoryHexalithVersions(string root)
    {
        string approvedCatalog = Path.GetFullPath(Path.Combine(
            RepositoryRoot.DependencyRoot("Hexalith.Builds"),
            "Props",
            "Directory.Packages.props"));
        string[] projectFiles = RepositoryWorksProjectFiles(root);
        string[] violations = [.. projectFiles
            .SelectMany(EvaluateSupportedLanes)
            .SelectMany(snapshot => KernelDependencyPolicy.EvaluatePackageVersionOwnership(snapshot, approvedCatalog, root))];

        violations.ShouldBeEmpty(
            "External Hexalith versions and effective properties must originate in Builds, except the existing approved EventStore pin.");
    }

    /// <summary>
    /// Sweeps every discovered Works project so an external Hexalith <c>PackageReference</c> added to a
    /// project that has no explicit expectation above is still gated. The four explicit expectations
    /// pin the exact dependency mode; this sweep keeps the governance repository-wide.
    /// </summary>
    private static void AssertNoUndeclaredExternalHexalithPackageReferences(
        string root,
        IEnumerable<string> explicitlyExpectedProjects)
    {
        HashSet<string> expected = [.. explicitlyExpectedProjects
            .Select(project => Path.GetFullPath(Path.Combine(root, project)))
            .Distinct(MsBuildProjectEvaluation.PathComparer)];

        string[] projectFiles = RepositoryWorksProjectFiles(root);

        projectFiles.ShouldNotBeEmpty("Expected to discover Hexalith.Works project files to sweep.");
        projectFiles.ShouldContain(
            path => Path.GetFileName(path) == "Hexalith.Works.Contracts.csproj",
            "Hexalith.Works.Contracts.csproj must be discovered for this sweep to be meaningful.");

        var violations = new List<string>();
        foreach (string projectFile in projectFiles.Where(path => !expected.Contains(Path.GetFullPath(path))))
        {
            foreach (MsBuildProjectSnapshot snapshot in EvaluateSupportedLanes(projectFile))
            {
                string[] externalPackages = [.. snapshot
                    .Items.Where(item => item.ItemType is "PackageReference" or "GlobalPackageReference")
                    .Select(item => item.Identity)
                    .Where(identity => identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase)
                        && !identity.StartsWith("Hexalith.Works.", StringComparison.OrdinalIgnoreCase))];
                if (externalPackages.Length > 0)
                {
                    violations.Add(
                        $"{Path.GetRelativePath(root, projectFile)} ({snapshot.PropertyValue("Configuration")}): {string.Join(", ", externalPackages.Order(StringComparer.Ordinal))}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Only the explicitly governed projects may consume external Hexalith packages; add an expectation before introducing a new one.");
    }

    private static string[] RepositoryWorksProjectFiles(string root)
        => [.. Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path =>
            {
                string[] segments = Path.GetRelativePath(root, path).Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);
                return !segments.Any(static segment => segment is "_bmad-output" or "references" or "bin" or "obj" or ".artifacts" or ".git");
            })];

    [Theory]
    [InlineData("PackageReference")]
    [InlineData("GlobalPackageReference")]
    public void UnfamiliarProjectNamesParticipateInRepositoryPackageGovernance(string itemType)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDiscovery-");
        try
        {
            string root = Path.Combine(temporaryRoot.FullName, "obj", "checkout");
            string contracts = WriteProject(root, "src/Hexalith.Works.Contracts.csproj", "<Project />");
            string unfamiliar = WriteProject(root, "tools/Unfamiliar.csproj",
                $"<Project><ItemGroup><{itemType} Include=\"Hexalith.Unapproved\" /></ItemGroup></Project>");
            foreach (string excluded in new[] { "references", "_bmad-output", "bin", "obj", ".artifacts", ".git" })
            {
                WriteProject(root, $"{excluded}/Unowned.csproj", "<Project />");
            }

            RepositoryWorksProjectFiles(root).ShouldBe([contracts, unfamiliar], ignoreOrder: true);
            Should.Throw<ShouldAssertException>(() => AssertNoUndeclaredExternalHexalithPackageReferences(root, []))
                .Message.ShouldContain("tools/Unfamiliar.csproj".Replace('/', Path.DirectorySeparatorChar));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void DeclaredPlatformsAndFrameworksContributeEveryDependencyAndImport()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DeclaredLanes-");
        try
        {
            string[] platforms = ["x64", "ARM64"];
            string[] frameworks = ["net9.0", "net10.0"];
            var expectedReferences = new List<string>();
            var expectedImports = new List<string>();
            foreach (string platform in platforms)
            {
                foreach (string framework in frameworks)
                {
                    string dependency = WriteProject(temporaryRoot.FullName, $"{platform}/{framework}/Child.csproj", "<Project />");
                    expectedReferences.Add(dependency);
                    expectedImports.Add(WriteProject(temporaryRoot.FullName, $"{platform}/{framework}.props",
                        $"<Project><ItemGroup><ProjectReference Include=\"{XmlPath(dependency)}\" /></ItemGroup></Project>"));
                }
            }

            string projectPath = WriteProject(temporaryRoot.FullName, "Owner.csproj", """
                <Project>
                  <PropertyGroup><Platforms>x64;ARM64</Platforms><TargetFrameworks>net9.0;net10.0</TargetFrameworks></PropertyGroup>
                  <Import Project="$(Platform)/$(TargetFramework).props" Condition="'$(TargetFramework)' != ''" />
                </Project>
                """);

            MsBuildProjectEvaluation.TryEvaluate(projectPath, out MsBuildProjectSnapshot? evaluated, out string laneDiagnostic).ShouldBeTrue(laneDiagnostic);
            MsBuildProjectSnapshot snapshot = evaluated.ShouldNotBeNull();
            AssertExactProjectReferences(snapshot, expectedReferences, "Every declared platform/framework pair must be governed.");
            snapshot.ImportPaths.ShouldBe(expectedImports, ignoreOrder: true);
            snapshot.ItemsOfType("ProjectReference").ShouldAllBe(item =>
                item.ProjectReferenceGlobalProperties["Platform"] == Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(item.CanonicalPath!)))
                && item.ProjectReferenceGlobalProperties["TargetFramework"] == Path.GetFileName(Path.GetDirectoryName(item.CanonicalPath!)));

            MsBuildProjectEvaluation.TryEvaluate(projectPath,
                new Dictionary<string, string> { ["Configuration"] = "Debug", ["Platform"] = "ARM64", ["TargetFramework"] = "net10.0" },
                out MsBuildProjectSnapshot? selected, out string diagnostic).ShouldBeTrue(diagnostic);
            AssertExactProjectReferences(selected!, [expectedReferences[^1]], "Explicit inherited globals must select exactly one lane.");
            selected!.ImportPaths.ShouldBe([expectedImports[^1]]);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void DebugOnlyWorksEdgesCannotEvadeExactDirectionAllowlists()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DebugDirection-");
        try
        {
            string dependency = WriteProject(temporaryRoot.FullName, "Hexalith.Works.Projections.csproj", "<Project />");
            string import = WriteProject(temporaryRoot.FullName, "debug.props",
                $"<Project><ItemGroup Condition=\"'$(Configuration)' == 'Debug'\"><ProjectReference Include=\"{XmlPath(dependency)}\" /></ItemGroup></Project>");
            string projectPath = WriteProject(temporaryRoot.FullName, "Hexalith.Works.Server.csproj",
                $"<Project><Import Project=\"{XmlPath(import)}\" /></Project>");

            IReadOnlyList<MsBuildProjectSnapshot> snapshots = EvaluateSupportedLanes(projectPath);
            ProjectReferenceDifferences(snapshots.Single(snapshot => snapshot.PropertyValue("Configuration") == "Release"), []).ShouldBeEmpty();
            ProjectReferenceDifferences(snapshots.Single(snapshot => snapshot.PropertyValue("Configuration") == "Debug"), [])
                .ShouldContain(difference => difference.Contains(dependency, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void FrameworkSpecificPlatformsAreRediscoveredAndSelectedImportsRemainRequired()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.FrameworkPlatforms-");
        try
        {
            string import = WriteProject(temporaryRoot.FullName, "x64.props",
                "<Project><ItemGroup><PackageReference Include=\"Dapr.Client\" /></ItemGroup></Project>");
            string projectPath = WriteProject(temporaryRoot.FullName, "Owner.csproj", """
                <Project>
                  <PropertyGroup><Platforms>AnyCPU</Platforms><TargetFrameworks>net10.0</TargetFrameworks></PropertyGroup>
                  <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'"><Platforms>x64</Platforms></PropertyGroup>
                  <Import Project="$(Platform).props" Condition="'$(TargetFramework)' == 'net10.0'" />
                </Project>
                """);

            string violation = KernelDependencyPolicy.EvaluateProjectFile("Hexalith.Works.Contracts", projectPath).ShouldHaveSingleItem();
            violation.ShouldContain("Dapr.Client");
            violation.ShouldContain(import);

            File.Delete(import);
            MsBuildProjectEvaluation.TryEvaluateSupportedLanes(projectPath, out _, out string diagnostic).ShouldBeFalse(
                "Missing imports in a selected supported lane must not be ignored.");
            diagnostic.ShouldContain("x64.props");
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void PlatformDiscoveryDoesNotRequireAnUnsupportedDefaultImport()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.PlatformDiscovery-");
        try
        {
            string import = WriteProject(temporaryRoot.FullName, "x64.props", "<Project />");
            string projectPath = WriteProject(temporaryRoot.FullName, "Owner.csproj",
                "<Project><PropertyGroup><Platforms>x64</Platforms></PropertyGroup><Import Project=\"$(Platform).props\" /></Project>");

            MsBuildProjectEvaluation.TryEvaluate(projectPath, out MsBuildProjectSnapshot? snapshot, out string diagnostic).ShouldBeTrue(diagnostic);
            snapshot!.ImportPaths.ShouldBe([import]);
            File.Delete(import);
            MsBuildProjectEvaluation.TryEvaluate(projectPath, out _, out diagnostic).ShouldBeFalse();
            diagnostic.ShouldContain("x64.props");
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData("NuGetPackageRoot", false)]
    [InlineData("NuGetPackageFolders", false)]
    [InlineData("RestorePackagesPath", false)]
    [InlineData("RestorePackagesPath", true)]
    public void RestoredPackageImportsAreExcludedWithoutDroppingCustomSiblings(string propertyName, bool relativeRoot)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.PackageImports-");
        try
        {
            string packageImport = WriteProject(temporaryRoot.FullName, "cache/package/1.0/buildTransitive/package.props", "<Project />");
            string customSibling = WriteProject(temporaryRoot.FullName, "cache-custom/custom.props", "<Project />");
            string nestedCustom = WriteProject(temporaryRoot.FullName, "owner/imports/nested.targets", "<Project />");
            string customImport = WriteProject(temporaryRoot.FullName, "owner/imports/custom.props",
                $"<Project><Import Project=\"{XmlPath(nestedCustom)}\" /></Project>");
            string packageRoot = relativeRoot ? "../cache" : Path.Combine(temporaryRoot.FullName, "cache");
            string propertyValue = propertyName == "NuGetPackageFolders" ? packageRoot + ";" + Path.Combine(temporaryRoot.FullName, "other-cache") : packageRoot;
            string projectPath = WriteProject(temporaryRoot.FullName, "owner/Owner.csproj", $"""
                <Project>
                  <PropertyGroup><{propertyName}>{XmlPath(propertyValue)}</{propertyName}></PropertyGroup>
                  <Import Project="{XmlPath(packageImport)}" />
                  <Import Project="{XmlPath(customSibling)}" />
                  <Import Project="{XmlPath(customImport)}" />
                </Project>
                """);

            EvaluateProject(projectPath).ImportPaths.ShouldBe([customSibling, customImport, nestedCustom], ignoreOrder: true);

            File.WriteAllText(packageImport, "<Project><ItemGroup><PackageReference Include=\"Dapr.Client\" /></ItemGroup></Project>");
            KernelDependencyPolicy.EvaluateProjectFile("Hexalith.Works.Contracts", projectPath).ShouldHaveSingleItem()
                .ShouldContain("Dapr.Client", Case.Sensitive, "Excluding package imports from freshness must preserve dependency governance.");
            File.WriteAllText(packageImport, "<Project><ItemGroup><PackageReference Include=\"$(MissingPackage)\" /></ItemGroup></Project>");
            KernelDependencyPolicy.EvaluateProjectFile("Hexalith.Works.Contracts", projectPath).ShouldHaveSingleItem()
                .ShouldContain("empty or unresolved Include");
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData("PackageReference")]
    [InlineData("GlobalPackageReference")]
    [InlineData("PackageVersion")]
    public void ImportedDebugOnlyPackagesCannotEvadeSourceConsumptionGovernance(string itemType)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DebugPackages-");
        try
        {
            string sharedCatalog = WriteProject(temporaryRoot.FullName, "shared.props", "<Project />");
            string import = WriteProject(temporaryRoot.FullName, "debug.props", $"""
                <Project><ItemGroup Condition="'$(Configuration)' == 'Debug'">
                  <{itemType} Include="Hexalith.Unapproved" Version="1.0.0" />
                </ItemGroup></Project>
                """);
            string projectPath = WriteProject(temporaryRoot.FullName, "Owner.csproj",
                $"<Project><Import Project=\"{XmlPath(import)}\" /></Project>");

            EvaluateProject(projectPath).ItemsOfType(itemType).ShouldBeEmpty("The Release control must not contain the Debug-only item.");
            string[] violations = KernelDependencyPolicy.EvaluateHexalithSourceConsumption(projectPath, sharedCatalog);
            violations.ShouldContain(violation => violation.Contains("Hexalith.Unapproved", StringComparison.Ordinal)
                && violation.Contains(import, StringComparison.Ordinal));
            violations.ShouldAllBe(violation => violation.Contains("Hexalith.Unapproved", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
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

            violations.Length.ShouldBe(5);
            violations.ShouldContain(violation => violation.Contains("influenced by property 'Configuration'", StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("<conditional PackageReference 'Hexalith.DebugOnly'>", StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("<conditional PackageVersion 'Hexalith.Conditional'>", StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("consumes evaluated PackageReference 'Hexalith.DebugOnly'", StringComparison.Ordinal));
            violations.ShouldContain(violation => violation.Contains("receives Works-owned PackageVersion 'Hexalith.Conditional'", StringComparison.Ordinal));
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

    /// <summary>Version ownership follows effective property assignments, including alias evaluation timing.</summary>
    /// <param name="scenario">The property provenance control.</param>
    /// <param name="unauthorized">Whether an unauthorized effective input must be reported.</param>
    [Theory]
    [InlineData("literal", false)]
    [InlineData("shared-property", false)]
    [InlineData("local-override", true)]
    [InlineData("recursive-alias", true)]
    [InlineData("overwritten-local", false)]
    [InlineData("post-item-reassignment", true)]
    [InlineData("captured-before-reassignment", false)]
    [InlineData("property-function", true)]
    [InlineData("assignment-condition", true)]
    [InlineData("group-condition", true)]
    [InlineData("overwritten-selector", false)]
    public void EffectivePackageVersionPropertyProvenanceIsGoverned(string scenario, bool unauthorized)
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.VersionProvenance-");
        try
        {
            string sharedProperties = "<PropertyGroup><SharedVersion>1.0.0</SharedVersion></PropertyGroup>";
            string before = string.Empty;
            string after = string.Empty;
            string expression = "$(SharedVersion)";
            switch (scenario)
            {
                case "literal": expression = "1.0.0"; break;
                case "assignment-condition":
                case "group-condition":
                case "overwritten-selector":
                    before = "<PropertyGroup><WorksSelector>local</WorksSelector></PropertyGroup>";
                    sharedProperties += scenario == "group-condition"
                        ? """<PropertyGroup Condition="'$(WorksSelector)' == 'local'"><SharedVersion>2.0.0</SharedVersion></PropertyGroup>"""
                        : """<PropertyGroup><SharedVersion Condition="'$(WorksSelector)' == 'local'">2.0.0</SharedVersion></PropertyGroup>""";
                    if (scenario == "overwritten-selector")
                    {
                        sharedProperties += "<PropertyGroup><SharedVersion>1.0.0</SharedVersion></PropertyGroup>";
                    }

                    break;
                case "local-override":
                    before = "<PropertyGroup><SharedVersion>2.0.0</SharedVersion></PropertyGroup>";
                    sharedProperties = """<PropertyGroup><SharedVersion Condition="'$(SharedVersion)' == ''">1.0.0</SharedVersion></PropertyGroup>""";
                    break;
                case "recursive-alias":
                    before = "<PropertyGroup><WorksVersion>2.0.0</WorksVersion></PropertyGroup>";
                    sharedProperties += "<PropertyGroup><Alias>$(WorksVersion)</Alias><OuterAlias>$(Alias)</OuterAlias></PropertyGroup>";
                    expression = "$(OuterAlias)";
                    break;
                case "property-function":
                    before = "<PropertyGroup><WorksVersion>2.0.0</WorksVersion></PropertyGroup>";
                    expression = "$([System.String]::Copy('$(WorksVersion)'))";
                    break;
                case "overwritten-local": before = "<PropertyGroup><SharedVersion>2.0.0</SharedVersion></PropertyGroup>"; break;
                case "post-item-reassignment": after = "<PropertyGroup><SharedVersion>2.0.0</SharedVersion></PropertyGroup>"; break;
                case "captured-before-reassignment":
                    sharedProperties += "<PropertyGroup><Alias>$(SharedVersion)</Alias></PropertyGroup>";
                    after = "<PropertyGroup><SharedVersion>2.0.0</SharedVersion></PropertyGroup>";
                    expression = "$(Alias)";
                    break;
            }

            string catalog = WriteProject(temporaryRoot.FullName, "shared.props",
                $"""<Project>{sharedProperties}<ItemGroup><PackageVersion Include="Hexalith.Shared" Version="{expression}" /></ItemGroup></Project>""");
            string owner = WriteProject(temporaryRoot.FullName, "Owner.csproj",
                $"""<Project>{before}<Import Project="{XmlPath(catalog)}" />{after}</Project>""");
            EvaluateProject(owner).ItemsOfType("PackageVersion").ShouldHaveSingleItem().Version
                .ShouldBe(unauthorized ? "2.0.0" : "1.0.0");
            string[] violations = KernelDependencyPolicy.EvaluateHexalithSourceConsumption(owner, catalog);
            if (unauthorized)
            {
                violations.ShouldContain(violation => violation.Contains("influenced by property", StringComparison.Ordinal)
                    && violation.Contains(owner, StringComparison.Ordinal));
            }
            else
            {
                violations.ShouldBeEmpty();
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>Item/metadata selectors and active import/Choose selection remain effective version inputs.</summary>
    /// <param name="selector">The distinct selector location to exercise.</param>
    [Theory]
    [InlineData("item")]
    [InlineData("item-group")]
    [InlineData("metadata")]
    [InlineData("otherwise")]
    [InlineData("later-when")]
    [InlineData("import-overwrite")]
    [InlineData("import-group-overwrite")]
    public void VersionSelectorsRetainLocalInfluence(string selector)
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Version & Selectors-");
        try
        {
            const string condition = "'$(WorksSelector)' == 'local'";
            string properties = "<PropertyGroup><SharedVersion>2.0.0</SharedVersion></PropertyGroup>";
            string items = "<ItemGroup><PackageVersion Include=\"Hexalith.Shared\" Version=\"$(SharedVersion)\" /></ItemGroup>";
            switch (selector)
            {
                case "item": items = $"<ItemGroup><PackageVersion Include=\"Hexalith.Shared\" Version=\"2.0.0\" Condition=\"{condition}\" /></ItemGroup>"; break;
                case "item-group": items = $"<ItemGroup Condition=\"{condition}\"><PackageVersion Include=\"Hexalith.Shared\" Version=\"2.0.0\" /></ItemGroup>"; break;
                case "metadata": items = $"<ItemGroup><PackageVersion Include=\"Hexalith.Shared\" Version=\"1.0.0\" /><PackageVersion Update=\"Hexalith.Shared\"><Version Condition=\"{condition}\">2.0.0</Version></PackageVersion></ItemGroup>"; break;
                case "otherwise":
                case "later-when":
                    string branch = selector == "otherwise"
                        ? "<Otherwise><PropertyGroup><SharedVersion>2.0.0</SharedVersion></PropertyGroup></Otherwise>"
                        : "<When Condition=\"true\"><PropertyGroup><SharedVersion>2.0.0</SharedVersion></PropertyGroup></When>";
                    properties = $"<Choose><When Condition=\"'$(WorksSelector)' == 'other'\"><PropertyGroup><SharedVersion>1.0.0</SharedVersion></PropertyGroup></When>{branch}</Choose>";
                    break;
                case "import-overwrite":
                case "import-group-overwrite":
                    properties = "<PropertyGroup><WorksSelector>shared</WorksSelector><SharedVersion>2.0.0</SharedVersion></PropertyGroup>";
                    break;
            }

            string catalog = WriteProject(temporary.FullName, "shared.props", $"<Project>{properties}{items}</Project>");
            string import = $"<Import Project=\"{XmlPath(catalog)}\" />";
            if (selector == "import-overwrite")
            {
                import = $"<Import Project=\"{XmlPath(catalog)}\" Condition=\"{condition}\" />";
            }
            else if (selector == "import-group-overwrite")
            {
                import = $"<ImportGroup Condition=\"{condition}\">{import}</ImportGroup>";
            }

            string owner = WriteProject(temporary.FullName, "Owner.csproj", $"<Project><PropertyGroup><WorksSelector>local</WorksSelector></PropertyGroup>{import}</Project>");
            MsBuildProjectSnapshot snapshot = EvaluateProject(owner);
            snapshot.ItemsOfType("PackageVersion").ShouldHaveSingleItem().Version.ShouldBe("2.0.0");
            snapshot.ItemsOfType("PackageVersion").ShouldHaveSingleItem().VersionProvenance.ShouldContain(source =>
                source.Name == "WorksSelector" && source.Value == "local" && source.DefiningProjectPath == owner);
            KernelDependencyPolicy.EvaluatePackageVersionOwnership(snapshot, catalog).ShouldNotBeEmpty();
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    /// <summary>A global property cannot hide its evaluated value or origin behind shared metadata.</summary>
    [Fact]
    public void GlobalVersionPropertyProvenanceRemainsVisibleAndUnauthorized()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.GlobalVersion-");
        try
        {
            string catalog = WriteProject(temporaryRoot.FullName, "shared.props",
                """<Project><ItemGroup><PackageVersion Include="Hexalith.Shared" Version="$(GlobalVersion)" /></ItemGroup></Project>""");
            string owner = WriteProject(temporaryRoot.FullName, "Owner.csproj",
                $"""<Project><Import Project="{XmlPath(catalog)}" /></Project>""");
            MsBuildProjectEvaluation.TryEvaluate(owner, new Dictionary<string, string> { ["GlobalVersion"] = "2.0.0" },
                out MsBuildProjectSnapshot? snapshot, out string diagnostic).ShouldBeTrue(diagnostic);
            snapshot!.ItemsOfType("PackageVersion").ShouldHaveSingleItem().VersionProvenance
                .ShouldContain(source => source.Name == "GlobalVersion" && source.Value == "2.0.0"
                    && source.DefiningProjectPath == "<global property>");
            KernelDependencyPolicy.EvaluatePackageVersionOwnership(snapshot, catalog).ShouldNotBeEmpty();
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>The approved pin permits only the existing EventStore property, value, family and central file.</summary>
    /// <param name="package">The package whose version uses the local property.</param>
    /// <param name="version">The Works-selected value.</param>
    /// <param name="property">The version property name.</param>
    /// <param name="definingFile">The property assignment file.</param>
    /// <param name="allowed">Whether this is the approved exception.</param>
    /// <param name="publicPin">The separately committed static public pin, or null to omit it.</param>
    [Theory]
    [InlineData("Hexalith.EventStore.Contracts", "3.126.1", "HexalithEventStoreVersion", "Directory.Packages.props", true)]
    [InlineData("Hexalith.EventStore.Client", "3.126.1", "HexalithEventStoreVersion", "Directory.Build.props", true)]
    [InlineData("Hexalith.EventStore.Client", "3.127.0", "HexalithEventStoreVersion", "Directory.Packages.props", false)]
    [InlineData("Hexalith.Other", "3.126.1", "HexalithEventStoreVersion", "Directory.Packages.props", false)]
    [InlineData("Hexalith.EventStore.Client", "3.126.1", "OtherVersion", "Directory.Packages.props", false)]
    [InlineData("Hexalith.EventStore.Client", "3.126.1", "HexalithEventStoreVersion", "arbitrary-import.props", false)]
    [InlineData("Hexalith.EventStore.Client", "3.127.0", "HexalithEventStoreVersion", "Directory.Packages.props", true, "3.127.0")]
    [InlineData("Hexalith.EventStore.Client", "3.126.1", "HexalithEventStoreVersion", "Directory.Packages.props", false, null)]
    [InlineData("Hexalith.EventStore.Client", "3.126.1", "HexalithEventStoreVersion", "Directory.Packages.props", false, "")]
    [InlineData("Hexalith.EventStore.Client", "3.126.1", "HexalithEventStoreVersion", "Directory.Packages.props", false, "$(SelectedVersion)")]
    [InlineData("Hexalith.EventStore.Client", "3.126.1", "HexalithEventStoreVersion", "Directory.Packages.props", false, "3.127.0")]
    public void ApprovedEventStorePinExceptionStaysNarrow(string package, string version, string property, string definingFile, bool allowed, string? publicPin = "3.126.1")
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.VersionPin-");
        try
        {
            if (publicPin is not null)
            {
                WriteProject(temporaryRoot.FullName, "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj",
                    $"""<Project><ItemGroup><AssemblyMetadata Include="WorksPinnedEventStoreVersion" Value="{XmlPath(publicPin)}" /></ItemGroup></Project>""");
            }
            string catalog = WriteProject(temporaryRoot.FullName, "shared.props",
                $"""<Project><ItemGroup><PackageVersion Include="{package}" Version="$({property})" /></ItemGroup></Project>""");
            string assignment = WriteProject(temporaryRoot.FullName, definingFile,
                $"""<Project><PropertyGroup><{property}>{version}</{property}></PropertyGroup></Project>""");
            string owner = WriteProject(temporaryRoot.FullName, "Owner.csproj",
                $"""<Project><Import Project="{XmlPath(assignment)}" /><Import Project="{XmlPath(catalog)}" /></Project>""");
            foreach (MsBuildProjectSnapshot snapshot in EvaluateSupportedLanes(owner))
            {
                string[] violations = KernelDependencyPolicy.EvaluatePackageVersionOwnership(snapshot, catalog, temporaryRoot.FullName);
                (violations.Length == 0).ShouldBe(allowed);
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    /// <summary>
    /// Selects the external Hexalith module references using the project's own evaluated module roots.
    /// A sibling-checkout workspace resolves those roots outside <c>references/</c>, so matching on a
    /// fixed path fragment would silently select nothing and make both mode assertions vacuous.
    /// </summary>
    private static IEnumerable<string> ExternalProjectReferences(MsBuildProjectSnapshot snapshot)
    {
        string[] externalRoots = [.. ExternalModuleRoots(snapshot)];
        externalRoots.ShouldNotBeEmpty(
            $"No external Hexalith module root evaluated for '{snapshot.ProjectPath}', so this gate would be vacuous.");

        return snapshot.ItemsOfType("ProjectReference")
            .Select(reference => reference.CanonicalPath!)
            .Where(path => externalRoots.Any(root => IsUnderRoot(path, root)))
            .Distinct(MsBuildProjectEvaluation.PathComparer);
    }

    /// <summary>Gets the evaluated external Hexalith module roots, normalized to canonical full paths.</summary>
    private static IEnumerable<string> ExternalModuleRoots(MsBuildProjectSnapshot snapshot)
        => new[] { "HexalithEventStoreRoot", "HexalithPartiesRoot", "HexalithPlatformRoot", "HexalithPolymorphicSerializationsRoot", "HexalithTenantsRoot" }
            .Select(snapshot.PropertyValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(value.Replace('\\', Path.DirectorySeparatorChar)))
            .Distinct(MsBuildProjectEvaluation.PathComparer);

    private static bool IsUnderRoot(string path, string root)
    {
        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, MsBuildProjectEvaluation.PathComparer == StringComparer.OrdinalIgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static MsBuildProjectSnapshot EvaluateProject(string projectPath, string configuration = "Release")
    {
        bool evaluated = MsBuildProjectEvaluation.TryEvaluate(
            projectPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BuildingInsideVisualStudio"] = "false",
                ["Configuration"] = configuration,
                ["DesignTimeBuild"] = "false",
            },
            out MsBuildProjectSnapshot? snapshot,
            out string diagnostic);

        evaluated.ShouldBeTrue(diagnostic);
        return snapshot!;
    }

    private static IReadOnlyList<MsBuildProjectSnapshot> EvaluateSupportedLanes(string projectPath)
    {
        MsBuildProjectEvaluation.TryEvaluateSupportedLanes(projectPath,
            out IReadOnlyList<MsBuildProjectSnapshot> snapshots, out string diagnostic).ShouldBeTrue(diagnostic);
        return snapshots;
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

    private static string XmlPath(string path) => System.Security.SecurityElement.Escape(path.Replace('\\', '/'))!;
}
