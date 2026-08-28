using System.Xml.Linq;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class DependencyDirectionTests
{
    private static readonly IReadOnlyDictionary<string, (string[] Allowed, string Rationale)> _governedProjectReferences =
        new Dictionary<string, (string[] Allowed, string Rationale)>(StringComparer.Ordinal)
        {
            ["Hexalith.Works.Contracts"] = (
                [
                    "Hexalith.EventStore.Contracts",
                    "Hexalith.PolymorphicSerializations",
                    "Hexalith.PolymorphicSerializations.CodeGenerators",
                ],
                "Contracts may reference EventStore.Contracts plus the PolymorphicSerializations library and its code generator (analyzer) that register the v1 event/command catalog."),
            ["Hexalith.Works.Server"] = (
                ["Hexalith.Works.Contracts"],
                "Server owns the pure decision core and must reference inward to Contracts only."),
            ["Hexalith.Works.Projections"] = (
                ["Hexalith.Works.Contracts"],
                "Projections build read models from the v1 catalog and must reference inward to Contracts only."),
            ["Hexalith.Works.Reactor"] = (
                ["Hexalith.Works.Contracts"],
                "Reactor is an adapter-ring project and must reference inward to Contracts only."),
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
            string projectPath = Path.Combine(root, "src", project, project + ".csproj");
            File.Exists(projectPath).ShouldBeTrue(
                $"Governed project '{project}' must own its project file '{projectPath}' before its dependency direction can be checked.");

            ProjectReferenceNames(root, $"src/{project}/{project}.csproj")
                .ShouldBe(
                    allowedReferences,
                    ignoreOrder: true,
                    customMessage: $"{project} must retain its exact architecture dependency-direction allowlist: {rationale}");
        }
    }

    [Fact]
    public void P0_ArchitectureTestReferencesCoverEveryGovernedProject()
    {
        string root = RepositoryRoot.Locate();

        KernelDependencyPolicy.ReconcileSourceProjects(root).ShouldBeEmpty(
            "Every source project must be deliberately classified before governed restore coverage is compared.");

        string[] architectureReferences = [.. ProjectReferenceNames(
                root,
                "tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj")
            .Where(reference => reference.StartsWith('<')
                || KernelDependencyPolicy.SourceProjects.Contains(reference, StringComparer.Ordinal))];

        architectureReferences.ShouldBe(
            KernelDependencyPolicy.GovernedProjects,
            ignoreOrder: true,
            customMessage: "The architecture-test project must reference every governed project so isolated restore produces every evaluated dependency graph.");
    }

    [Fact]
    public void SemicolonDelimitedProjectReferencesAreAllDiscovered()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                "<Project><ItemGroup><ProjectReference Include=\"../Hexalith.Works.Projections/Hexalith.Works.Projections.csproj;../Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj\" /></ItemGroup></Project>");

            ProjectReferenceNames(temporaryRoot.FullName, "Synthetic.csproj")
                .ShouldBe(
                    ["Hexalith.Works.Projections", "Hexalith.Works.Contracts"],
                    ignoreOrder: true,
                    customMessage: "Exact direction checks must inspect every dependency in a semicolon-delimited MSBuild item specification, not only the first.");
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void MalformedProjectFileFailsExactDirectionDiscoveryClosed()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            File.WriteAllText(
                Path.Combine(temporaryRoot.FullName, "Malformed.csproj"),
                "<Project><ItemGroup><ProjectReference Include=\"../A/A.csproj\" ");
            File.WriteAllText(
                Path.Combine(temporaryRoot.FullName, "NotAProject.csproj"),
                "<Solution><ItemGroup><ProjectReference Include=\"../A/A.csproj\" /></ItemGroup></Solution>");

            string[] malformed = ProjectReferenceNames(temporaryRoot.FullName, "Malformed.csproj");
            malformed.ShouldHaveSingleItem();
            malformed[0].ShouldContain("unreadable ProjectReference source", Case.Sensitive);

            string[] missing = ProjectReferenceNames(temporaryRoot.FullName, "Absent.csproj");
            missing.ShouldHaveSingleItem();
            missing[0].ShouldContain("unreadable ProjectReference source", Case.Sensitive);

            string[] notAProject = ProjectReferenceNames(temporaryRoot.FullName, "NotAProject.csproj");
            notAProject.ShouldHaveSingleItem();
            notAProject[0].ShouldContain("the XML root is not a Project element", Case.Sensitive);

            // Every sentinel must survive the governed-set filter so an unreadable project file cannot
            // silently reduce the discovered reference set to an allowed one.
            malformed.Concat(missing).Concat(notAProject).ShouldAllBe(reference => reference.StartsWith('<'));
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void ConditionalProjectReferencesFailExactDirectionDiscoveryClosed()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                "<Project><ItemGroup><ProjectReference Include=\"../Hexalith.Commons/Hexalith.Commons.csproj\" Condition=\"'$(Configuration)' == 'Release'\" /></ItemGroup></Project>");

            string[] references = ProjectReferenceNames(temporaryRoot.FullName, "Synthetic.csproj");

            references.ShouldHaveSingleItem();
            references[0].ShouldContain("conditional ProjectReference", Case.Sensitive);
            references[0].ShouldContain("Hexalith.Commons.csproj", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void AncestorItemGroupConditionFailsExactDirectionDiscoveryClosed()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                "<Project><ItemGroup Condition=\"'$(Configuration)' == 'Release'\"><ProjectReference Include=\"../Hexalith.Commons/Hexalith.Commons.csproj\" /></ItemGroup></Project>");

            string[] references = ProjectReferenceNames(temporaryRoot.FullName, "Synthetic.csproj");

            references.ShouldHaveSingleItem();
            references[0].ShouldContain("conditional ProjectReference", Case.Sensitive);
            references[0].ShouldContain("Hexalith.Commons.csproj", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void EmptySemicolonProjectReferenceFailsDiscoveryClosed()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                "<Project><ItemGroup><ProjectReference Include=\"; ;\" /></ItemGroup></Project>");

            ProjectReferenceNames(temporaryRoot.FullName, "Synthetic.csproj")
                .ShouldBe(["<malformed ProjectReference with empty Include>"]);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void RemoveUpdateAndItemDefinitionProjectReferencesAreExcluded()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <ItemGroup>
                    <ProjectReference Include="../Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj" />
                    <ProjectReference Remove="../Bad/Bad.csproj" />
                    <ProjectReference Update="../Also.Bad/Also.Bad.csproj" />
                  </ItemGroup>
                  <ItemDefinitionGroup>
                    <ProjectReference Include="../Default.Bad/Default.Bad.csproj" />
                  </ItemDefinitionGroup>
                </Project>
                """);

            ProjectReferenceNames(temporaryRoot.FullName, "Synthetic.csproj")
                .ShouldBe(["Hexalith.Works.Contracts"]);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void CaseVariantProjectReferenceItemIsDiscovered()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                "<Project><iTeMgRoUp><pRoJeCtReFeReNcE Include=\"../Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj\" /></iTeMgRoUp></Project>");

            ProjectReferenceNames(temporaryRoot.FullName, "Synthetic.csproj")
                .ShouldBe(["Hexalith.Works.Contracts"]);
        }
        finally
        {
            Directory.Delete(temporaryRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void ArchitectureReferenceDiscoveryPreservesExtraAndMalformedAdditions()
    {
        DirectoryInfo temporaryRoot = Directory.CreateTempSubdirectory("Hexalith.Works.DependencyDirectionTests-");
        try
        {
            string projectPath = Path.Combine(temporaryRoot.FullName, "Synthetic.csproj");
            File.WriteAllText(
                projectPath,
                "<Project><ItemGroup><ProjectReference Include=\"../External.Adapter/External.Adapter.csproj;../*/Opaque.csproj\" /></ItemGroup></Project>");

            string[] references = ProjectReferenceNames(temporaryRoot.FullName, "Synthetic.csproj");

            references.Length.ShouldBe(2);
            references.ShouldContain("External.Adapter");
            references.ShouldContain(reference => reference.Contains("malformed ProjectReference", StringComparison.Ordinal)
                && reference.Contains("Opaque.csproj", StringComparison.Ordinal));
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

        ProjectReferenceNames(root, "src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj")
            .ShouldBe(
                [
                    "Hexalith.Works.Contracts",
                    "Hexalith.Works.Projections",
                    "Hexalith.Works.Reactor",
                    "Hexalith.Works.Server",
                    "Hexalith.Works.ServiceDefaults",
                    "Hexalith.EventStore.Aspire",
                    "Hexalith.EventStore.Operations",
                ],
                ignoreOrder: true,
                customMessage: "AppHost should wire only the Works topology plus EventStore Aspire and operations workloads.");
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

        string[] references = ProjectReferenceNames(root, "src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj");

        string[] violations = [.. references
            .Where(reference => forbiddenSiblingProjects.Any(forbidden => reference.StartsWith(forbidden, StringComparison.Ordinal)))];

        violations.ShouldBeEmpty("Works contracts may expose only reference IDs and must not depend on sibling client, server, adapter, or runtime projects.");
    }

    [Fact]
    public void P0_HexalithDependenciesUseProjectReferencesNotPackageReferences()
    {
        string root = RepositoryRoot.Locate();
        string[] projectFiles = [.. Directory.GetFiles(root, "Hexalith.Works*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_bmad-output{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !KernelDependencyPolicy.IsBuildOutput(path))];

        // Guard against a vacuous pass: if discovery returns nothing (wrong root, packaged output dir),
        // the violation scan below would trivially pass while enforcing almost nothing.
        projectFiles.ShouldNotBeEmpty("Expected to discover Hexalith.Works project files to govern.");
        projectFiles.ShouldContain(
            path => Path.GetFileName(path) == "Hexalith.Works.Contracts.csproj",
            "Hexalith.Works.Contracts.csproj must be discovered for this fitness guard to be meaningful.");

        string[] packageFiles = [Path.Combine(root, "Directory.Packages.props"), .. projectFiles];

        string[] violations = [.. packageFiles
            .SelectMany(file => PackageReferenceNames(file)
                .Where(name => name.StartsWith("Hexalith.", StringComparison.Ordinal))
                .Select(name => $"{Path.GetRelativePath(root, file)} contains PackageReference/PackageVersion {name}"))];

        violations.ShouldBeEmpty("Hexalith libraries must be consumed from checked-out sibling source with ProjectReference, never NuGet PackageReference or Directory.Packages.props entries.");
    }

    private static string[] ProjectReferenceNames(string root, string relativeProjectPath)
        => KernelDependencyPolicy.DeclaredReferenceNames(
            Path.Combine(root, relativeProjectPath),
            "ProjectReference");

    private static string[] PackageReferenceNames(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);

        return [.. project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "PackageVersion")
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value)
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))];
    }

}
