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
        string eventStoreRoot = RepositoryRoot.DependencyRoot("Hexalith.EventStore");
        string appHostPath = Path.Combine(root, "src", "Hexalith.Works.AppHost", "Hexalith.Works.AppHost.csproj");
        string[] worksTopology =
        [
            Path.Combine(root, "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj"),
            Path.Combine(root, "src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj"),
            Path.Combine(root, "src/Hexalith.Works.Reactor/Hexalith.Works.Reactor.csproj"),
            Path.Combine(root, "src/Hexalith.Works.Server/Hexalith.Works.Server.csproj"),
            Path.Combine(root, "src/Hexalith.Works.ServiceDefaults/Hexalith.Works.ServiceDefaults.csproj"),
        ];

        // Assert both modes: a Debug-only conditional ProjectReference would otherwise satisfy a
        // single Release evaluation while silently changing the topology developers actually run.
        AssertExactProjectReferences(
            EvaluateProject(appHostPath, "Release"),
            worksTopology,
            "Release AppHost should use the EventStore Aspire package and represent external runtime hosts only through project metadata.");

        AssertExactProjectReferences(
            EvaluateProject(appHostPath, "Debug"),
            [
                .. worksTopology,
                Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Aspire", "Hexalith.EventStore.Aspire.csproj"),
            ],
            "Debug AppHost should wire the Works topology plus the source-backed EventStore Aspire library; external hosts remain runtime-only metadata paths.");
    }

    [Fact]
    public void P0_EventStoreRuntimeHostsAreMetadataNotCompileGraphProjects()
    {
        string root = RepositoryRoot.Locate();
        string[] runtimeProjects =
        [
            "Hexalith.EventStore.csproj",
            "Hexalith.EventStore.Admin.Server.Host.csproj",
            "Hexalith.EventStore.Operations.csproj",
        ];
        string solution = File.ReadAllText(Path.Combine(root, "Hexalith.Works.slnx"));
        string appHost = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Hexalith.Works.AppHost",
            "Hexalith.Works.AppHost.csproj"));
        string integration = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Hexalith.Works.IntegrationTests",
            "Hexalith.Works.IntegrationTests.csproj"));
        string[] metadataFiles =
        [
            "HexalithEventStore.cs",
            "HexalithEventStoreAdminServerHost.cs",
            "HexalithEventStoreOperations.cs",
        ];

        foreach ((string runtimeProject, string metadataFile) in runtimeProjects.Zip(metadataFiles))
        {
            string metadata = File.ReadAllText(Path.Combine(
                root,
                "src",
                "Hexalith.Works.AppHost",
                metadataFile));

            solution.ShouldNotContain(runtimeProject, Case.Sensitive);
            appHost.ShouldNotContain(runtimeProject, Case.Sensitive);
            integration.ShouldNotContain(runtimeProject, Case.Sensitive);
            metadata.ShouldContain(runtimeProject, Case.Sensitive);
            metadata.ShouldContain("SuppressBuild => false", Case.Sensitive);
        }

        string metadataProbe = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Hexalith.Works.IntegrationTests",
            "EventStoreOperationsTests.cs"));
        metadataProbe.ShouldContain("System.Reflection.Metadata", Case.Sensitive);
        metadataProbe.ShouldContain("Hexalith.EventStore.Operations.csproj", Case.Sensitive);
        metadataProbe.ShouldContain("Dapr.Actors:Dapr.Actors.Runtime.IRemindable", Case.Sensitive);
        metadataProbe.ShouldContain(
            "Hexalith.EventStore.Operations:Hexalith.EventStore.Operations.Actors.IDeadLetterDrainActor",
            Case.Sensitive);
        metadataProbe.ShouldContain("recordProperties.ShouldContain(\"Body\")", Case.Sensitive);
        metadataProbe.ShouldNotContain("using Hexalith.EventStore.Operations", Case.Sensitive);
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
                },
                ReleaseProjects: Array.Empty<string>(),
                ReleasePackages: new[] { "Hexalith.EventStore.DomainService" }),
            (
                Project: "src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj",
                DebugProjects: new[]
                {
                    Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Aspire", "Hexalith.EventStore.Aspire.csproj"),
                },
                ReleaseProjects: Array.Empty<string>(),
                ReleasePackages: new[] { "Hexalith.EventStore.Aspire" }),
            (
                Project: "tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj",
                DebugProjects: new[]
                {
                    Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Admin.Abstractions", "Hexalith.EventStore.Admin.Abstractions.csproj"),
                    Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Testing", "Hexalith.EventStore.Testing.csproj"),
                },
                ReleaseProjects: Array.Empty<string>(),
                ReleasePackages: new[]
                {
                    "Hexalith.EventStore.Admin.Abstractions",
                    "Hexalith.EventStore.Testing",
                }),
        };

        foreach ((string project, string[] debugProjects, string[] releaseProjects, string[] releasePackages) in expectations)
        {
            string projectPath = Path.Combine(root, project);
            MsBuildProjectSnapshot debug = EvaluateProject(projectPath, "Debug");
            MsBuildProjectSnapshot release = EvaluateProject(projectPath, "Release");

            ExternalProjectReferences(debug).ShouldBe(
                debugProjects.Select(Path.GetFullPath),
                ignoreOrder: true,
                customMessage: $"{project} must consume external Hexalith dependencies from sibling source in Debug.");
            debug.ItemsOfType("PackageReference")
                .Where(item => item.Identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Identity)
                .ShouldBeEmpty($"{project} must not mix external Hexalith packages into Debug source mode.");

            ExternalProjectReferences(release).ShouldBe(
                releaseProjects.Select(path => Path.GetFullPath(Path.Combine(root, path))),
                ignoreOrder: true,
                customMessage: $"{project} has an unexpected external source edge in Release package mode.");
            release.ItemsOfType("PackageReference")
                .Where(item => item.Identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Identity)
                .ShouldBe(
                    releasePackages,
                    ignoreOrder: true,
                    customMessage: $"{project} must consume the exact centrally pinned external packages in Release.");
        }

        AssertNoUndeclaredExternalHexalithPackageReferences(root, expectations.Select(expectation => expectation.Project));
        AssertSharedCatalogOwnsRepositoryHexalithVersions(root);
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
            .SelectMany(project => new[] { EvaluateProject(project, "Debug"), EvaluateProject(project, "Release") })
            .SelectMany(snapshot => snapshot.ItemsOfType("PackageVersion")
                .Where(item => item.Identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase))
                .Where(item => !MsBuildProjectEvaluation.PathComparer.Equals(item.DefiningProjectPath, approvedCatalog))
                .Select(item => $"{Path.GetRelativePath(root, snapshot.ProjectPath)} receives {item.Identity} from {item.DefiningProjectPath}."))];

        violations.ShouldBeEmpty(
            "Every external Hexalith package version used by a Works project must originate in the shared Builds catalog.");
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
            foreach (string configuration in new[] { "Debug", "Release" })
            {
                string[] externalPackages = [.. EvaluateProject(projectFile, configuration)
                    .ItemsOfType("PackageReference")
                    .Select(item => item.Identity)
                    .Where(identity => identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase)
                        && !identity.StartsWith("Hexalith.Works.", StringComparison.OrdinalIgnoreCase))];
                if (externalPackages.Length > 0)
                {
                    violations.Add(
                        $"{Path.GetRelativePath(root, projectFile)} ({configuration}): {string.Join(", ", externalPackages.Order(StringComparer.Ordinal))}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Only the explicitly governed projects may consume external Hexalith packages; add an expectation before introducing a new one.");
    }

    private static string[] RepositoryWorksProjectFiles(string root)
        => [.. Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path =>
            {
                string[] segments = Path.GetRelativePath(root, path).Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);
                return !segments.Any(static segment => segment is "_bmad-output" or "references" or "bin" or "obj");
            })];

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
            .Where(path => externalRoots.Any(root => IsUnderRoot(path, root)));
    }

    /// <summary>Gets the evaluated external Hexalith module roots, normalized to canonical full paths.</summary>
    private static IEnumerable<string> ExternalModuleRoots(MsBuildProjectSnapshot snapshot)
        => new[] { "HexalithEventStoreRoot", "HexalithPolymorphicSerializationsRoot", "HexalithTenantsRoot" }
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
                ["Platform"] = "AnyCPU",
            },
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
