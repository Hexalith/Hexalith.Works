using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

using Hexalith.PolymorphicSerializations;
using Hexalith.Works.Contracts.Commands;

using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Runtime-adapter governance guardrails introduced by Stories 4.5-4.7. These keep the pure kernel pure and confine
/// every EventStore-runtime / Dapr / ASP.NET-hosting concern to the single runnable host project
/// (<c>src/Hexalith.Works</c>), while freezing the production-surface vocabulary out of Works
/// source. All assertions use evaluated dependencies and source so the ArchitectureTests lane stays pure (no Dapr/Aspire).
/// </summary>
public sealed class RuntimeAdapterGovernanceTests
{
    private const string RunnableHost = "Hexalith.Works";

    [Fact]
    public void P0_RunnableHostIsTheOnlyWorksProjectReferencingEventStoreRuntimeAndDapr()
    {
        string root = RepositoryRoot.Locate();
        KernelDependencyPolicy.ReconcileSourceProjects(root).ShouldBeEmpty();
        foreach (string project in KernelDependencyPolicy.GovernedProjects.Append(RunnableHost))
        {
            MsBuildProjectEvaluation.TryEvaluateIndependentSupportedLanes(
                Path.Combine(root, "src", project, project + ".csproj"),
                out IReadOnlyList<MsBuildProjectSnapshot> snapshots, out string diagnostic).ShouldBeTrue(diagnostic);
            foreach (MsBuildProjectSnapshot snapshot in snapshots)
            {
                RuntimeBoundaryViolations(root, snapshot, project == RunnableHost).ShouldBeEmpty();
            }
        }
    }

    /// <summary>Checks one independently evaluated build lane against trusted root configuration.</summary>
    /// <param name="root">The owning repository root whose build file supplies dependency authority.</param>
    /// <param name="snapshot">The inspected project's evaluated evidence.</param>
    /// <param name="host">Whether the project is the runnable host.</param>
    /// <returns>Every runtime boundary violation in this lane.</returns>
    internal static string[] RuntimeBoundaryViolations(string root, MsBuildProjectSnapshot snapshot, bool host)
    {
        // Evaluate the root file on its own: a property assigned by the inspected project cannot
        // authorize itself. Explicit caller/environment overrides still resolve in both evaluations.
        if (!MsBuildProjectEvaluation.TryEvaluate(Path.Combine(root, "Directory.Build.props"),
            snapshot.GlobalProperties, out MsBuildProjectSnapshot? authority, out string diagnostic))
        {
            return [$"Cannot establish independent dependency root authority: {diagnostic}"];
        }

        var violations = new List<string>();
        string TrustedRoot(string name)
        {
            string approved = authority!.PropertyValue(name);
            if (string.IsNullOrWhiteSpace(approved))
            {
                violations.Add($"Trusted root configuration '{authority.ProjectPath}' has no {name} in {snapshot.Lane}.");
                return Path.Combine(root, "<missing dependency authority>");
            }

            string approvedPath = Path.GetFullPath(approved.Replace('\\', Path.DirectorySeparatorChar), root);
            string inspected = snapshot.PropertyValue(name);
            if (!string.IsNullOrWhiteSpace(inspected)
                && !MsBuildProjectEvaluation.PathComparer.Equals(approvedPath,
                    Path.GetFullPath(inspected.Replace('\\', Path.DirectorySeparatorChar), Path.GetDirectoryName(snapshot.ProjectPath)!)))
            {
                violations.Add($"{snapshot.ProjectPath} redirects {name} to '{inspected}' away from independently trusted '{approvedPath}' in {snapshot.Lane}.");
            }

            return approvedPath;
        }

        string sdkRoot = TrustedRoot("HexalithEventStoreRoot");
        string SdkProject(string name) => Path.Combine(sdkRoot, "src", name, name + ".csproj");
        string domainService = SdkProject("Hexalith.EventStore.DomainService");
        HashSet<string> trustedRoots = new(MsBuildProjectEvaluation.PathComparer) { root, sdkRoot };
        HashSet<string> allowed = new(MsBuildProjectEvaluation.PathComparer)
        {
            SdkProject("Hexalith.EventStore.Contracts"),
        };
        foreach (string project in KernelDependencyPolicy.GovernedProjects)
        {
            allowed.Add(Path.GetFullPath(Path.Combine(root, "src", project, project + ".csproj")));
        }

        string serializationRoot = TrustedRoot("HexalithPolymorphicSerializationsRoot");
        trustedRoots.Add(serializationRoot);
        foreach (string project in new[] { "Hexalith.PolymorphicSerializations", "Hexalith.PolymorphicSerializations.CodeGenerators" })
        {
            allowed.Add(Path.Combine(serializationRoot, "src", "libraries", project, project + ".csproj"));
        }

        if (host)
        {
            allowed.Add(domainService);
            string partiesRoot = TrustedRoot("HexalithPartiesRoot");
            trustedRoots.Add(partiesRoot);
            allowed.Add(Path.Combine(partiesRoot, "src", "Hexalith.Parties.Client", "Hexalith.Parties.Client.csproj"));
        }

        foreach (MsBuildEvaluatedItem reference in snapshot.ItemsOfType("ProjectReference"))
        {
            if (reference.CanonicalPath is null || !allowed.Contains(reference.CanonicalPath))
            {
                violations.Add($"{snapshot.ProjectPath} has unapproved source dependency '{reference.CanonicalPath}' in {snapshot.Lane}; dependencies require canonical approved source paths.");
            }
            else
            {
                string trustedRoot = trustedRoots.Where(candidate => IsWithin(reference.CanonicalPath, candidate))
                    .OrderByDescending(candidate => candidate.Length).First();
                string? link = LinkedDescendant(reference.CanonicalPath, trustedRoot);
                if (link is not null)
                {
                    violations.Add($"{snapshot.ProjectPath} has linked project path '{link}' below trusted root '{trustedRoot}' in {snapshot.Lane}.");
                }
            }
        }

        MsBuildEvaluatedItem[] packages = [.. snapshot.Items.Where(item => item.ItemType is "PackageReference" or "GlobalPackageReference")];
        foreach (MsBuildEvaluatedItem item in packages.Concat(snapshot.ItemsOfType("Reference")))
        {
            var names = new List<string> { item.Identity };
            if (item.ItemType == "Reference" && !string.IsNullOrWhiteSpace(item.HintPath))
            {
                string assemblyPath = Path.GetFullPath(item.HintPath.Replace('\\', Path.DirectorySeparatorChar), Path.GetDirectoryName(snapshot.ProjectPath)!);
                try
                {
                    names.Add(AssemblyName.GetAssemblyName(assemblyPath).Name
                        ?? throw new InvalidOperationException($"Assembly '{assemblyPath}' has no identity."));
                }
                catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException or InvalidOperationException)
                {
                    violations.Add($"{snapshot.ProjectPath} cannot inspect evaluated Reference '{item.Identity}' HintPath '{assemblyPath}' in {snapshot.Lane}: {exception.Message}");
                }
            }

            foreach (string identity in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string name = identity.Split(',')[0].Trim().Replace('\\', '/');
                name = name[(name.LastIndexOf('/') + 1)..];
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    name = Path.GetFileNameWithoutExtension(name);
                }

                bool dapr = name.Equals("Dapr", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Dapr.", StringComparison.OrdinalIgnoreCase);
                bool runtime = (name.Equals("Hexalith.EventStore", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Hexalith.EventStore.", StringComparison.OrdinalIgnoreCase))
                    && !name.Equals("Hexalith.EventStore.Contracts", StringComparison.OrdinalIgnoreCase);
                if (dapr || (runtime && (!host || item.ItemType == "Reference")))
                {
                    violations.Add($"{snapshot.ProjectPath} has forbidden evaluated {item.ItemType} '{item.Identity}' (assembly identity '{name}') from '{item.DefiningProjectPath}' in {snapshot.Lane}.");
                }
            }
        }

        if (host)
        {
            int edges = snapshot.ItemsOfType("ProjectReference").Count(item =>
                MsBuildProjectEvaluation.PathComparer.Equals(item.CanonicalPath, domainService)
                && (string.IsNullOrEmpty(item.ReferenceOutputAssembly) || item.ReferenceOutputAssembly.Equals("true", StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(item.OutputItemType) || item.OutputItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase)))
                + packages.Count(item => item.Identity.Equals("Hexalith.EventStore.DomainService", StringComparison.OrdinalIgnoreCase));
            if (edges != 1)
            {
                violations.Add($"{snapshot.ProjectPath} must resolve exactly one canonical EventStore domain-service edge in {snapshot.Lane}; found {edges}.");
            }
        }

        return [.. violations];
    }

    /// <summary>Imported packages and assembly references remain governed in both configurations.</summary>
    /// <param name="itemType">The evaluated reference kind.</param>
    /// <param name="host">Whether this fixture represents a host.</param>
    /// <param name="runtimeAssembly">An optional EventStore runtime assembly identity.</param>
    [Theory]
    [InlineData("PackageReference", true)]
    [InlineData("GlobalPackageReference", true)]
    [InlineData("Reference", true)]
    [InlineData("PackageReference", false)]
    [InlineData("Reference", false)]
    [InlineData("Reference", true, "Hexalith.EventStore.Server")]
    public void ImportedConditionalDaprDependenciesAreRejected(string itemType, bool host, string? runtimeAssembly = null)
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Runtime & Boundary-");
        try
        {
            WriteRuntimeAuthority(temporary.FullName);
            string import = Path.Combine(temporary.FullName, "import.props");
            File.WriteAllText(import, $"""
                <Project><ItemGroup>
                  <{itemType} Include="{runtimeAssembly ?? "dApR.Client"}" Condition="'$(Configuration)' == 'Debug'" />
                  <{itemType} Include="{runtimeAssembly ?? "Dapr.Actors"}" Condition="'$(Configuration)' == 'Release'" />
                </ItemGroup></Project>
                """);
            string owner = Path.Combine(temporary.FullName, "Owner.csproj");
            File.WriteAllText(owner, $"""
                <Project><ItemGroup><PackageReference Include="Hexalith.EventStore.DomainService" Condition="'{host}' == 'True'" /></ItemGroup>
                <Import Project="{XmlPath(import)}" /></Project>
                """);
            foreach (MsBuildProjectSnapshot snapshot in IndependentLanes(owner))
            {
                string violation = RuntimeBoundaryViolations(temporary.FullName, snapshot, host).ShouldHaveSingleItem();
                violation.ShouldContain("forbidden evaluated");
                violation.ShouldContain(import);
            }
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    /// <summary>Neither the project basename nor an inspected-project root redirect can authorize a source edge.</summary>
    /// <param name="host">Whether the fixture requires the DomainService edge.</param>
    /// <param name="redirect">Whether the inspected project also redirects the SDK root.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void SourceImpersonationAndInspectedProjectRootRedirectAreRejected(bool host, bool redirect)
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Runtime & Identity-");
        try
        {
            WriteRuntimeAuthority(temporary.FullName);
            string name = host ? "Hexalith.EventStore.DomainService" : "Hexalith.EventStore.Contracts";
            string genuineRoot = Path.Combine(temporary.FullName, "sdk");
            string impostorRoot = Path.Combine(temporary.FullName, "impostor");
            string ProjectAt(string dependencyRoot) => Path.Combine(dependencyRoot, "src", name, name + ".csproj");
            foreach (string dependencyRoot in new[] { genuineRoot, impostorRoot })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProjectAt(dependencyRoot))!);
                File.WriteAllText(ProjectAt(dependencyRoot), "<Project />");
            }

            string owner = Path.Combine(temporary.FullName, "Owner.csproj");
            foreach (bool impostor in new[] { false, true })
            {
                string selectedRoot = impostor ? impostorRoot : genuineRoot;
                string properties = redirect ? $"<PropertyGroup><HexalithEventStoreRoot>{XmlPath(selectedRoot)}</HexalithEventStoreRoot></PropertyGroup>" : string.Empty;
                File.WriteAllText(owner, $"<Project>{properties}<ItemGroup><ProjectReference Include=\"{XmlPath(ProjectAt(selectedRoot))}\" /></ItemGroup></Project>");
                foreach (MsBuildProjectSnapshot snapshot in IndependentLanes(owner))
                {
                    string[] violations = RuntimeBoundaryViolations(temporary.FullName, snapshot, host);
                    if (!impostor)
                    {
                        violations.ShouldBeEmpty();
                    }
                    else
                    {
                        string impostorProject = ProjectAt(impostorRoot);
                        violations.ShouldContain(violation => violation.Contains(impostorProject, StringComparison.Ordinal));
                        if (redirect)
                        {
                            violations.ShouldContain(violation => violation.Contains("independently trusted", StringComparison.Ordinal));
                        }
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    /// <summary>Each platform and framework must independently contain exactly one valid host edge.</summary>
    /// <param name="frameworks">Whether to vary frameworks instead of platforms.</param>
    /// <param name="missing">Whether the second lane omits its host edge.</param>
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void IndependentHostLanesCannotMaskMissingOrDifferentValidEdges(bool frameworks, bool missing)
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Runtime & Lanes-");
        try
        {
            WriteRuntimeAuthority(temporary.FullName);
            string dependency = Path.Combine(temporary.FullName, "sdk", "src", "Hexalith.EventStore.DomainService", "Hexalith.EventStore.DomainService.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(dependency)!);
            File.WriteAllText(dependency, "<Project />");
            string declaration = frameworks ? "<TargetFrameworks>net9.0;net10.0</TargetFrameworks>" : "<Platforms>AnyCPU;x64</Platforms>";
            string selector = frameworks ? "$(TargetFramework)" : "$(Platform)";
            string first = frameworks ? "net9.0" : "AnyCPU";
            string alternate = missing ? string.Empty : $"<PackageReference Include=\"Hexalith.EventStore.DomainService\" Condition=\"'{selector}' != '{first}'\" />";
            string owner = Path.Combine(temporary.FullName, "Owner.csproj");
            File.WriteAllText(owner, $"""
                <Project><PropertyGroup>{declaration}</PropertyGroup><ItemGroup>
                  <ProjectReference Include="{XmlPath(dependency)}" Condition="'{selector}' == '{first}'" />
                  {alternate}
                </ItemGroup></Project>
                """);
            IReadOnlyList<MsBuildProjectSnapshot> snapshots = IndependentLanes(owner);
            snapshots.Count.ShouldBe(4);
            foreach (MsBuildProjectSnapshot snapshot in snapshots)
            {
                string[] violations = RuntimeBoundaryViolations(temporary.FullName, snapshot, host: true);
                if (missing && snapshot.PropertyValue(frameworks ? "TargetFramework" : "Platform") != first)
                {
                    violations.ShouldHaveSingleItem().ShouldContain("found 0");
                }
                else
                {
                    violations.ShouldBeEmpty();
                }
            }
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    /// <summary>False output-assembly and analyzer-only SDK edges cannot satisfy the host runtime dependency.</summary>
    /// <param name="metadata">The project-reference metadata that removes its runtime output.</param>
    [Theory]
    [InlineData("ReferenceOutputAssembly=\"false\"")]
    [InlineData("OutputItemType=\"Analyzer\"")]
    public void HostRequiresUsableDomainServiceProjectOutput(string metadata)
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Runtime & Output-");
        try
        {
            WriteRuntimeAuthority(temporary.FullName);
            string dependency = Path.Combine(temporary.FullName, "sdk", "src", "Hexalith.EventStore.DomainService", "Hexalith.EventStore.DomainService.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(dependency)!);
            File.WriteAllText(dependency, "<Project />");
            string owner = Path.Combine(temporary.FullName, "Owner.csproj");
            File.WriteAllText(owner, $"<Project><ItemGroup><ProjectReference Include=\"{XmlPath(dependency)}\" {metadata} /></ItemGroup></Project>");
            foreach (MsBuildProjectSnapshot snapshot in IndependentLanes(owner))
            {
                RuntimeBoundaryViolations(temporary.FullName, snapshot, host: true).ShouldHaveSingleItem().ShouldContain("found 0");
            }
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    /// <summary>A real assembly's identity remains governed when Include and filename are harmless aliases.</summary>
    [Fact]
    public void AssemblyHintPathAliasCannotHideDaprIdentity()
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Runtime & Assembly-");
        try
        {
            WriteRuntimeAuthority(temporary.FullName);
            string assembly = Path.Combine(temporary.FullName, "innocent.dll");
            var builder = new PersistedAssemblyBuilder(new AssemblyName("Dapr.Client"), typeof(object).Assembly);
            builder.DefineDynamicModule("fixture").DefineType("Probe", TypeAttributes.Public).CreateType();
            builder.Save(assembly);
            AssemblyName.GetAssemblyName(assembly).Name.ShouldBe("Dapr.Client");
            string owner = Path.Combine(temporary.FullName, "Owner.csproj");
            File.WriteAllText(owner, $"""
                <Project><ItemGroup><PackageReference Include="Hexalith.EventStore.DomainService" />
                <Reference Include="HarmlessAlias"><HintPath>{XmlPath(assembly)}</HintPath></Reference></ItemGroup></Project>
                """);
            foreach (MsBuildProjectSnapshot snapshot in IndependentLanes(owner))
            {
                snapshot.ItemsOfType("Reference").ShouldHaveSingleItem().HintPath.ShouldBe(assembly);
                RuntimeBoundaryViolations(temporary.FullName, snapshot, host: true).ShouldHaveSingleItem().ShouldContain("Dapr.Client");
            }
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    /// <summary>A file link or Windows junction below the trusted SDK root cannot impersonate its project.</summary>
    /// <param name="directoryLink">Whether to impersonate through a linked descendant directory.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedProjectFileCannotImpersonateCanonicalSource(bool directoryLink)
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Runtime & Link-");
        try
        {
            WriteRuntimeAuthority(temporary.FullName);
            const string name = "Hexalith.EventStore.Contracts";
            string targetDirectory = Path.Combine(temporary.FullName, "impostor");
            string approvedDirectory = Path.Combine(temporary.FullName, "sdk", "src", name);
            Directory.CreateDirectory(targetDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(approvedDirectory)!);
            string target = Path.Combine(targetDirectory, name + ".csproj");
            string linked = Path.Combine(approvedDirectory, name + ".csproj");
            File.WriteAllText(target, "<Project />");
            if (OperatingSystem.IsWindows() || directoryLink)
            {
                CreateDirectoryAlias(approvedDirectory, targetDirectory);
            }
            else
            {
                Directory.CreateDirectory(approvedDirectory);
                File.CreateSymbolicLink(linked, target);
            }

            string owner = Path.Combine(temporary.FullName, "Owner.csproj");
            File.WriteAllText(owner, $"<Project><ItemGroup><ProjectReference Include=\"{XmlPath(linked)}\" /></ItemGroup></Project>");
            bool evaluated = MsBuildProjectEvaluation.TryEvaluateIndependentSupportedLanes(owner,
                out IReadOnlyList<MsBuildProjectSnapshot> snapshots, out string diagnostic);
            if (OperatingSystem.IsWindows() || directoryLink)
            {
                evaluated.ShouldBeTrue(diagnostic);
                foreach (MsBuildProjectSnapshot snapshot in snapshots)
                {
                    RuntimeBoundaryViolations(temporary.FullName, snapshot, host: false).ShouldHaveSingleItem().ShouldContain("linked project path");
                }
            }
            else
            {
                evaluated.ShouldBeFalse();
                diagnostic.ShouldContain("linked project path");
                diagnostic.ShouldContain(linked);
            }
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    /// <summary>A workspace directory alias above the trusted dependency roots is a valid checkout location.</summary>
    [Fact]
    public void WorkspaceAliasAboveTrustedRootsRemainsAllowed()
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("Hexalith.Works.Runtime & Alias-");
        try
        {
            string workspace = Path.Combine(temporary.FullName, "workspace");
            string alias = Path.Combine(temporary.FullName, "alias");
            Directory.CreateDirectory(workspace);
            CreateDirectoryAlias(alias, workspace);
            WriteRuntimeAuthority(alias);
            const string name = "Hexalith.EventStore.Contracts";
            string project = Path.Combine(alias, "sdk", "src", name, name + ".csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            File.WriteAllText(project, "<Project />");
            string owner = Path.Combine(alias, "Owner.csproj");
            File.WriteAllText(owner, $"<Project><ItemGroup><ProjectReference Include=\"{XmlPath(project)}\" /></ItemGroup></Project>");
            foreach (MsBuildProjectSnapshot snapshot in IndependentLanes(owner))
            {
                RuntimeBoundaryViolations(alias, snapshot, host: false).ShouldBeEmpty();
            }
        }
        finally
        {
            Directory.Delete(temporary.FullName, recursive: true);
        }
    }

    private static void CreateDirectoryAlias(string alias, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(alias, target);
            return;
        }

        var start = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "/d", "/c", "mklink", "/J", alias, target })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start).ShouldNotBeNull();
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, output);
    }

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static string? LinkedDescendant(string path, string trustedRoot)
    {
        for (FileSystemInfo? entry = new FileInfo(path); entry is not null && !MsBuildProjectEvaluation.PathComparer.Equals(entry.FullName, trustedRoot);
            entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
        {
            if (entry.LinkTarget is not null)
            {
                return entry.FullName;
            }
        }

        return null;
    }

    private static IReadOnlyList<MsBuildProjectSnapshot> IndependentLanes(string owner)
    {
        MsBuildProjectEvaluation.TryEvaluateIndependentSupportedLanes(owner, out IReadOnlyList<MsBuildProjectSnapshot> snapshots,
            out string diagnostic).ShouldBeTrue(diagnostic);
        return snapshots;
    }

    private static void WriteRuntimeAuthority(string root)
        => File.WriteAllText(Path.Combine(root, "Directory.Build.props"), $"""
            <Project><PropertyGroup>
              <HexalithEventStoreRoot>{XmlPath(Path.Combine(root, "sdk"))}</HexalithEventStoreRoot>
              <HexalithPolymorphicSerializationsRoot>{XmlPath(Path.Combine(root, "serialization"))}</HexalithPolymorphicSerializationsRoot>
              <HexalithPartiesRoot>{XmlPath(Path.Combine(root, "parties"))}</HexalithPartiesRoot>
            </PropertyGroup></Project>
            """);

    private static string XmlPath(string path) => System.Security.SecurityElement.Escape(path.Replace('\\', '/'))!;

    [Fact]
    public void P0_ReminderCascadeAndDomainEventRuntimeAreConfinedToHostEdge()
    {
        string root = RepositoryRoot.Locate();

        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))];

        string[] runtimeTokens =
        [
            "IRemindable",
            "RegisterReminderAsync",
            "UnregisterReminderAsync",
            "DateReminderActor",
            "DateReminderReconciler",
            "CascadeCheckpoint",
            "ICascadeCheckpointIndex",
            "CascadeDispatcher",
            "IChildCompletionAwaitingParentSource",
            "IEventStoreDomainEventHandler",
            "IWorkCommandSubmitter",
            "IEventStoreGatewayClient",
            "IReadModelStore",
            "WorksDomainEventProcessor",
            "WorkItemSuspendedReminderHandler",
            "IndexedPendingDateAwaitSource",
            "PendingDateAwaitStreamReader",
            "WorkItemProjectionParking",
            "WorksProjectionOptions",
        ];

        string[] misplaced = [.. sourceFiles
            .Where(path => !IsAllowedRuntimeAdapterLocation(root, path))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => runtimeTokens
                .Where(token => file.Text.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(root, file.Path)} contains host-edge runtime token '{token}'"))];

        misplaced.ShouldBeEmpty("Reminder actors, domain-event consumption, command submission, stream reads, and checkpoint persistence must stay in src/Hexalith.Works, not in pure kernel projects.");
    }

    [Fact]
    public void P0_PureProjectsRemainFreeOfActorClockLoggingNetworkFileAndEventStoreRuntimeApis()
    {
        string root = RepositoryRoot.Locate();
        string[] bannedSourcePatterns =
        [
            @"^\s*using\s+Dapr\.",
            @"\bIRemindable\b",
            @"\bRegisterReminderAsync\b",
            @"\bUnregisterReminderAsync\b",
            @"\bTimeProvider\b",
            @"\bDateTimeOffset\.UtcNow\b",
            @"\bDateTimeOffset\.Now\b",
            @"\bDateTime\.UtcNow\b",
            @"\bDateTime\.Now\b",
            @"\bILogger\b",
            @"\bLoggerMessage\b",
            @"\bHttpClient\b",
            @"\bIEventStoreGatewayClient\b",
            @"\bIReadModelStore\b",
            @"\bFile\.",
            @"\bDirectory\.",
        ];

        KernelDependencyPolicy.ReconcileSourceProjects(root).ShouldBeEmpty(
            "Every source project must be deliberately classified before governed roots are scanned.");

        string[] violations = [.. KernelDependencyPolicy.GovernedProjectRoots(root)
            .SelectMany(projectRoot => Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => bannedSourcePatterns
                .Where(pattern => Regex.IsMatch(file.Text, pattern, RegexOptions.Multiline))
                .Select(pattern => $"{Path.GetRelativePath(root, file.Path)} matches forbidden pure-project runtime pattern /{pattern}/"))];

        violations.ShouldBeEmpty($"The governed projects ({string.Join(", ", KernelDependencyPolicy.GovernedProjects)}) must remain free of Dapr actors, clocks, logging, network/filesystem I/O, read-model stores, and EventStore gateway/runtime APIs.");
    }

    [Fact]
    public void P0_RuntimeSubscriptionAndRecoveryRecordsDoNotExpandDurablePolymorphicCatalog()
    {
        int polymorphicCatalogCount = typeof(AssignWorkItem).Assembly.GetTypes()
            .Count(type => !type.IsAbstract && type != typeof(Polymorphic) && typeof(Polymorphic).IsAssignableFrom(type));

        polymorphicCatalogCount.ShouldBe(40, "Subscription envelopes, reminder/checkpoint/index records, and source records are host-edge runtime records, not durable polymorphic command/event/rejection catalog types.");
    }



    [Fact]
    public void P0_WorkItemAggregateStaysPureStaticAndEventStoreAdapterLivesInHost()
    {
        string root = RepositoryRoot.Locate();

        string kernelAggregate = File.ReadAllText(Path.Combine(root, "src", "Hexalith.Works.Server", "Aggregates", "WorkItemAggregate.cs"));
        kernelAggregate.ShouldContain("static class WorkItemAggregate", Case.Sensitive, "WorkItemAggregate must remain the pure static kernel.");
        kernelAggregate.ShouldNotContain("EventStoreAggregate", Case.Sensitive, "The pure kernel aggregate must not inherit EventStore runtime types.");

        string adapter = File.ReadAllText(Path.Combine(root, "src", RunnableHost, "WorkItemEventStoreAggregate.cs"));
        adapter.ShouldContain("EventStoreAggregate<WorkItemState>", Case.Sensitive, "The host adapter aggregate must subclass EventStoreAggregate<WorkItemState>.");
        adapter.ShouldContain("[EventStoreDomain(\"work\")]", Case.Sensitive, "The host adapter aggregate must declare the canonical \"work\" domain.");
        adapter.ShouldContain("Server.Aggregates.WorkItemAggregate", Case.Sensitive, "The host adapter must delegate to the pure kernel WorkItemAggregate.");
    }

    [Fact]
    public void P0_NoProductionSurfaceVocabularyInWorksSource()
    {
        string root = RepositoryRoot.Locate();

        string[] forbiddenTypeNamePatterns =
        [
            "Mcp",
            "Chatbot",
            "EmailSurface",
            "MailSurface",
            "DataGrid",
            "WebShell",
            "RoutingEngine",
            "EligibilityScore",
            "EscalationLadder",
            "CostMeter",
            "SpendGovernance",
        ];

        var declarationRegex = new Regex(
            @"\b(?:public\s+|internal\s+|private\s+|protected\s+|sealed\s+|partial\s+|abstract\s+|static\s+|readonly\s+|file\s+)*(?:record(?:\s+(?:class|struct))?|class|enum|interface|struct)\s+([A-Za-z_]\w*)");

        string[] sourceFiles = [.. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))];

        sourceFiles.ShouldNotBeEmpty("Expected to discover Works src files to govern for production-surface vocabulary.");

        string[] vocabularyViolations = [.. sourceFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => declarationRegex.Matches(file.Text)
                .Select(match => match.Groups[1].Value)
                .Where(typeName => forbiddenTypeNamePatterns.Any(pattern => typeName.Contains(pattern, StringComparison.Ordinal)))
                .Select(typeName => $"{Path.GetRelativePath(root, file.Path)} declares forbidden production-surface type '{typeName}'"))];

        vocabularyViolations.ShouldBeEmpty("Story 4.5 ships no production UI/MCP/chatbot/email/DataGrid/web-shell/routing/eligibility/escalation/cost/spend surface in Works source.");

        // No concrete IExecutorRouter implementation may appear (the abstraction-only Theme-4 seam stays impl-free).
        var routerImplRegex = new Regex(@"\bclass\s+[A-Za-z_]\w*\s*(?:<[^>]*>)?\s*:\s*[^\{]*\bIExecutorRouter\b");
        string[] routerImplViolations = [.. sourceFiles
            .Where(path => !Path.GetFileName(path).Equals("IExecutorRouter.cs", StringComparison.Ordinal))
            .Where(path => routerImplRegex.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))];

        routerImplViolations.ShouldBeEmpty("No concrete IExecutorRouter implementation may exist; executor routing stays abstraction-only (Theme 4).");
    }

    [Fact]
    public void P0_RuntimeAdapterLogsOnlyBoundedMetadataNeverPayloads()
    {
        string root = RepositoryRoot.Locate();

        string[] hostFiles = [.. Directory.GetFiles(Path.Combine(root, "src", RunnableHost), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))];

        hostFiles.ShouldNotBeEmpty("Expected to discover runnable-host source files.");

        // A log-template placeholder that names a payload-bearing field would leak event/command content.
        string[] forbiddenLogPlaceholders =
        [
            "{Payload",
            "{Obligation",
            "{Command",
            "{Secret",
            "{Token",
            "{Body",
            "{EventJson",
            "{Json",
        ];

        string[] logLeakViolations = [.. hostFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => forbiddenLogPlaceholders
                .Where(token => file.Text.Contains(token, StringComparison.OrdinalIgnoreCase))
                .Select(token => $"{Path.GetRelativePath(root, file.Path)} contains forbidden log placeholder '{token}'"))];

        logLeakViolations.ShouldBeEmpty("Adapter logs must carry only bounded metadata (ids, type names, reason/projection types) — never payloads, obligations, secrets, tokens, or event/command bodies (AC #4 / NFR-6).");

        // Structured logging only: no interpolated log calls that could embed a payload variable.
        string[] interpolatedLogViolations = [.. hostFiles
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => Regex.IsMatch(file.Text, @"\.Log(Information|Warning|Error|Debug|Trace|Critical)\s*\(\s*\$"))
            .Select(file => Path.GetRelativePath(root, file.Path))];

        interpolatedLogViolations.ShouldBeEmpty("The runtime adapter must use compile-time LoggerMessage definitions, never interpolated log calls that can embed payloads.");
    }

    // One build-output exclusion rule for every gate: delegate instead of keeping a second copy.
    private static bool IsBuildOutput(string path) => KernelDependencyPolicy.IsBuildOutput(path);

    private static bool IsAllowedRuntimeAdapterLocation(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.StartsWith(Path.Combine("src", RunnableHost) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
