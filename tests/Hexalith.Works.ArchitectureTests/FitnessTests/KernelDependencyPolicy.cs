using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Evaluates restored kernel dependency graphs for forbidden adapter and infrastructure libraries.
/// </summary>
internal static class KernelDependencyPolicy
{
    private static readonly IReadOnlyDictionary<string, bool> _sourceProjectClassifications =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Hexalith.Works.Contracts"] = true,
            ["Hexalith.Works.Server"] = true,
            ["Hexalith.Works.Projections"] = true,
            ["Hexalith.Works.Reactor"] = true,
            ["Hexalith.Works"] = false,
            ["Hexalith.Works.AppHost"] = false,
            ["Hexalith.Works.ServiceDefaults"] = false,
        };

    private static readonly IReadOnlyList<string> _sourceProjects = Array.AsReadOnly(
        _sourceProjectClassifications.Keys.ToArray());

    private static readonly IReadOnlyList<string> _governedProjects = Array.AsReadOnly(
        _sourceProjectClassifications
            .Where(classification => classification.Value)
            .Select(classification => classification.Key)
            .ToArray());

    private static readonly IReadOnlyList<string> _adapterProjects = Array.AsReadOnly(
        _sourceProjectClassifications
            .Where(classification => !classification.Value)
            .Select(classification => classification.Key)
            .ToArray());

    private static readonly string[] _supportedDirectReferenceKinds =
        ["ProjectReference", "PackageReference", "FrameworkReference", "Reference"];

    private static readonly string[] _packageItemKinds =
        ["PackageReference", "GlobalPackageReference", "PackageVersion"];

    private static readonly string[] _namedAdapterSegments =
    [
        "AdminPortal",
        "AppHost",
        "Channel",
        "Client",
        "ConsumerPortal",
        "CostGovernance",
        "Email",
        "Picker",
        "Routing",
        "Security",
        "ServiceDefaults",
    ];

    private static readonly string[] _networkClientPrefixes =
    [
        "AWSSDK",
        "Azure.Messaging",
        "Azure.Storage",
        "Confluent.Kafka",
        "Grpc.Net.Client",
        "MassTransit",
        "RabbitMQ.Client",
        "Refit",
    ];

    /// <summary>
    /// Gets every deliberately classified top-level Works source project.
    /// </summary>
    public static IReadOnlyList<string> SourceProjects => _sourceProjects;

    /// <summary>
    /// Gets the exact kernel project set whose evaluated dependency closures are governed.
    /// </summary>
    public static IReadOnlyList<string> GovernedProjects => _governedProjects;

    /// <summary>
    /// Gets the deliberate adapter projects that are excluded from kernel purity rules.
    /// </summary>
    public static IReadOnlyList<string> AdapterProjects => _adapterProjects;

    /// <summary>
    /// Resolves the source roots for every governed project.
    /// </summary>
    /// <param name="repositoryRoot">The repository root that owns <c>src</c>.</param>
    /// <returns>The governed source roots in canonical classification order.</returns>
    public static string[] GovernedProjectRoots(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        return [.. GovernedProjects.Select(project => Path.Combine(repositoryRoot, "src", project))];
    }

    /// <summary>
    /// Reconciles top-level source project discovery with the deliberate kernel/adapter classification.
    /// </summary>
    /// <param name="repositoryRoot">The repository root that owns <c>src</c>.</param>
    /// <returns>Actionable classification violations; an empty collection means every project is classified exactly once.</returns>
    public static string[] ReconcileSourceProjects(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        string sourceRoot = Path.Combine(repositoryRoot, "src");
        if (!Directory.Exists(sourceRoot))
        {
            return [$"Source project root '{sourceRoot}' is missing; no Works source project can be classified."];
        }

        try
        {
            // Discover the whole src tree, not just src/*/*.csproj: a project placed at any other depth
            // must be reported rather than silently escaping classification.
            string[] discoveredProjectFiles = [.. Directory
                .GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Order(StringComparer.Ordinal)];

            string[] projectFiles = [.. discoveredProjectFiles.Where(path => IsTopLevelSourceProject(sourceRoot, path))];

            var violations = new HashSet<string>(StringComparer.Ordinal);
            foreach (string misplacedProject in discoveredProjectFiles.Except(projectFiles, StringComparer.Ordinal))
            {
                violations.Add(
                    $"Source project file '{misplacedProject}' is not a top-level 'src/<name>/<name>.csproj' project and cannot be classified.");
            }

            if (projectFiles.Length == 0)
            {
                violations.Add($"Source project discovery under '{sourceRoot}' returned no top-level src/*/*.csproj files.");
            }

            foreach (string projectFile in projectFiles)
            {
                string projectName = Path.GetFileNameWithoutExtension(projectFile);
                string directoryName = Path.GetFileName(Path.GetDirectoryName(projectFile)!);
                if (!string.Equals(projectName, directoryName, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"Source project '{projectName}' at '{projectFile}' does not match its containing directory '{directoryName}'.");
                }

                if (!_sourceProjectClassifications.ContainsKey(projectName))
                {
                    violations.Add(
                        $"Source project '{projectName}' at '{projectFile}' is unclassified; add an explicit governed-kernel or deliberate-adapter classification.");
                }
            }

            foreach (string classifiedProject in SourceProjects)
            {
                string expectedPath = Path.Combine(sourceRoot, classifiedProject, classifiedProject + ".csproj");
                int matchingProjects = projectFiles.Count(path =>
                    string.Equals(Path.GetFileNameWithoutExtension(path), classifiedProject, StringComparison.Ordinal));

                if (!File.Exists(expectedPath))
                {
                    violations.Add(
                        $"Classified source project '{classifiedProject}' is missing its expected project file '{expectedPath}'.");
                }

                if (matchingProjects > 1)
                {
                    violations.Add(
                        $"Classified source project '{classifiedProject}' was discovered {matchingProjects} times; expected exactly once at '{expectedPath}'.");
                }
            }

            return [.. violations.Order(StringComparer.Ordinal)];
        }
        catch (IOException exception)
        {
            return [$"Source projects under '{sourceRoot}' could not be discovered: {exception.Message}"];
        }
        catch (UnauthorizedAccessException exception)
        {
            return [$"Source projects under '{sourceRoot}' could not be discovered: {exception.Message}"];
        }
    }

    /// <summary>
    /// Evaluates declared project, package, and framework references in one governed project file.
    /// </summary>
    /// <param name="governedProject">The kernel project that owns the declared references.</param>
    /// <param name="projectPath">The project file to inspect.</param>
    /// <returns>Actionable policy violations; an empty collection means every declared reference is allowed.</returns>
    public static string[] EvaluateProjectFile(string governedProject, string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(governedProject);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        if (!File.Exists(projectPath))
        {
            return [$"{governedProject} governed project file '{projectPath}' is missing."];
        }

        try
        {
            string projectXml = File.ReadAllText(projectPath);
            if (string.IsNullOrWhiteSpace(projectXml))
            {
                return [$"{governedProject} governed project file '{projectPath}' is unusable: the project XML is empty."];
            }

            var violations = new HashSet<string>(
                EvaluateProjectXml(governedProject, projectPath, projectXml),
                StringComparer.Ordinal);
            if (!MsBuildProjectEvaluation.TryEvaluate(projectPath, out MsBuildProjectSnapshot? snapshot, out string diagnostic))
            {
                violations.Add($"{governedProject} {diagnostic}");
                return [.. violations.Order(StringComparer.Ordinal)];
            }

            violations.UnionWith(EvaluateSnapshot(
                governedProject,
                snapshot!,
                Path.GetFullPath(projectPath)));
            return [.. violations.Order(StringComparer.Ordinal)];
        }
        catch (IOException exception)
        {
            return [$"{governedProject} governed project file '{projectPath}' could not be read: {exception.Message}"];
        }
        catch (UnauthorizedAccessException exception)
        {
            return [$"{governedProject} governed project file '{projectPath}' could not be read: {exception.Message}"];
        }
    }

    /// <summary>
    /// Evaluates synthetic project XML through the same direct-reference policy as repository project files.
    /// </summary>
    /// <param name="governedProject">The kernel project that owns the declared references.</param>
    /// <param name="projectPath">The diagnostic path of the project file.</param>
    /// <param name="projectXml">The complete project XML.</param>
    /// <returns>Actionable policy violations; an empty collection means every declared reference is allowed.</returns>
    public static string[] EvaluateProjectXml(string governedProject, string projectPath, string projectXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(governedProject);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        if (string.IsNullOrWhiteSpace(projectXml))
        {
            return [$"{governedProject} governed project file '{projectPath}' is unusable: the project XML is empty."];
        }

        try
        {
            XDocument project = XDocument.Parse(projectXml, LoadOptions.PreserveWhitespace);
            if (project.Root is null
                || !string.Equals(project.Root.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase))
            {
                return [$"{governedProject} governed project file '{projectPath}' is unusable: the XML root is not a Project element."];
            }

            var violations = new HashSet<string>(StringComparer.Ordinal);

            foreach (XElement reference in project
                .Descendants()
                .Where(element => string.Equals(element.Parent?.Name.LocalName, "ItemGroup", StringComparison.OrdinalIgnoreCase))
                .Where(element => element.Attribute("Include") is not null
                    || (element.Attribute("Remove") is null && element.Attribute("Update") is null)))
            {
                if (!TryGetDirectReferenceKind(reference.Name.LocalName, out string referenceKind))
                {
                    continue;
                }

                XAttribute? declaration = reference.Attribute("Include");

                if (declaration is null || string.IsNullOrWhiteSpace(declaration.Value))
                {
                    violations.Add(
                        $"{governedProject} governed project file '{projectPath}' contains a malformed {referenceKind} without a dependency name.");
                    continue;
                }

                string[] declaredItems = declaration.Value.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (declaredItems.Length == 0)
                {
                    violations.Add(
                        $"{governedProject} governed project file '{projectPath}' contains a malformed {referenceKind} without a dependency name.");
                    continue;
                }

                foreach (string declaredItem in declaredItems)
                {
                    if (!TryNormalizeDeclaredReference(referenceKind, declaredItem, out string dependencyName))
                    {
                        violations.Add(
                            $"{governedProject} governed project file '{projectPath}' contains malformed {referenceKind} declaration '{declaredItem}'.");
                        continue;
                    }

                    string? forbiddenFamily = ForbiddenFamily(dependencyName);
                    if (forbiddenFamily is not null)
                    {
                        violations.Add(
                            $"{governedProject} direct {referenceKind} '{dependencyName}' is forbidden ({forbiddenFamily}) in '{projectPath}'.");
                    }
                }
            }

            return [.. violations.Order(StringComparer.Ordinal)];
        }
        catch (XmlException exception)
        {
            return [$"{governedProject} governed project file '{projectPath}' could not be parsed: {exception.Message}"];
        }
    }

    /// <summary>
    /// Evaluates Hexalith package consumption and package-version ownership for one project.
    /// </summary>
    /// <param name="projectPath">The owning project to evaluate.</param>
    /// <param name="approvedSharedCatalogPath">The one externally owned shared package catalog.</param>
    /// <returns>Actionable source-consumption violations; an empty collection means sibling source is used.</returns>
    internal static string[] EvaluateHexalithSourceConsumption(
        string projectPath,
        string approvedSharedCatalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedSharedCatalogPath);

        if (!File.Exists(approvedSharedCatalogPath))
        {
            return [$"Approved shared Builds package catalog '{approvedSharedCatalogPath}' is missing."];
        }

        string approvedCatalog = Path.GetFullPath(approvedSharedCatalogPath);
        var violations = new HashSet<string>(StringComparer.Ordinal);

        // The Release-lane evaluation resolves conditions away and never sees an unevaluable declaration, so
        // scan the owning project XML conservatively first. Without this, a Hexalith package declared under an
        // inactive condition would leave this gate silently blind instead of failing closed.
        foreach (string packageItemKind in _packageItemKinds)
        {
            foreach (string declaration in DeclaredReferenceNames(projectPath, packageItemKind)
                .Where(declaration => declaration.StartsWith('<')))
            {
                violations.Add(
                    $"Owning project '{projectPath}' declares {packageItemKind} {declaration}; the evaluated source-consumption gate cannot inspect it.");
            }
        }

        if (!MsBuildProjectEvaluation.TryEvaluate(projectPath, out MsBuildProjectSnapshot? snapshot, out string diagnostic))
        {
            violations.Add(diagnostic);
            return [.. violations.Order(StringComparer.Ordinal)];
        }

        foreach (MsBuildEvaluatedItem packageReference in snapshot!.Items
            .Where(item => item.ItemType is "PackageReference" or "GlobalPackageReference")
            .Where(item => item.Identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add(
                $"Owning project '{snapshot.ProjectPath}' consumes evaluated {packageReference.ItemType} '{packageReference.Identity}' defined by '{packageReference.DefiningProjectPath}'; Hexalith libraries must use sibling ProjectReference source.");
        }

        foreach (MsBuildEvaluatedItem packageVersion in snapshot.ItemsOfType("PackageVersion")
            .Where(item => item.Identity.StartsWith("Hexalith.", StringComparison.OrdinalIgnoreCase))
            .Where(item => !MsBuildProjectEvaluation.PathComparer.Equals(item.DefiningProjectPath, approvedCatalog)))
        {
            violations.Add(
                $"Owning project '{snapshot.ProjectPath}' receives Works-owned PackageVersion '{packageVersion.Identity}' from '{packageVersion.DefiningProjectPath}'; only shared catalog '{approvedCatalog}' may define Hexalith package versions.");
        }

        return [.. violations.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Evaluates one governed kernel project from the repository layout the architecture gate uses.
    /// </summary>
    /// <param name="repositoryRoot">The repository root that owns <c>src</c> and the shared restore inputs.</param>
    /// <param name="governedProject">The kernel project that owns the evaluated dependency graph.</param>
    /// <returns>Actionable policy violations; an empty collection means the graph is current, usable, and allowed.</returns>
    public static string[] EvaluateGovernedProject(string repositoryRoot, string governedProject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(governedProject);

        string projectDirectory = Path.Combine(repositoryRoot, "src", governedProject);
        string governedProjectPath = Path.Combine(projectDirectory, governedProject + ".csproj");
        if (!TryEvaluateProjectClosure(
            governedProject,
            governedProjectPath,
            out IReadOnlyList<MsBuildProjectSnapshot> projectClosure,
            out string evaluationDiagnostic))
        {
            return [evaluationDiagnostic];
        }

        string[] evaluatedRestoreInputs = [.. projectClosure
            .SelectMany(snapshot => snapshot.ImportPaths.Prepend(snapshot.ProjectPath))
            .Distinct(MsBuildProjectEvaluation.PathComparer)];

        return EvaluateFileWithRequiredFreshnessInputs(
            governedProject,
            governedProjectPath,
            Path.Combine(projectDirectory, "obj", "project.assets.json"),
            SharedRestoreInputs(repositoryRoot),
            evaluatedRestoreInputs);
    }

    /// <summary>
    /// Evaluates a restored <c>project.assets.json</c> file for one governed project.
    /// </summary>
    /// <param name="governedProject">The kernel project that owns the evaluated dependency graph.</param>
    /// <param name="governedProjectPath">The expected path of the governed project file.</param>
    /// <param name="assetsPath">The path to the restored dependency artifact.</param>
    /// <param name="additionalFreshnessInputs">Extra restore inputs the artifact must not be older than; <see langword="null"/> checks the governed project file only.</param>
    /// <returns>Actionable policy violations; an empty collection means the graph is current, usable, and allowed.</returns>
    public static string[] EvaluateFile(
        string governedProject,
        string governedProjectPath,
        string assetsPath,
        IReadOnlyList<string>? additionalFreshnessInputs = null)
        => EvaluateFileCore(
            governedProject,
            governedProjectPath,
            assetsPath,
            additionalFreshnessInputs,
            requiredFreshnessInputs: null);

    internal static string[] EvaluateFileWithRequiredFreshnessInputs(
        string governedProject,
        string governedProjectPath,
        string assetsPath,
        IReadOnlyList<string>? optionalFreshnessInputs,
        IReadOnlyList<string> requiredFreshnessInputs)
    {
        ArgumentNullException.ThrowIfNull(requiredFreshnessInputs);

        return EvaluateFileCore(
            governedProject,
            governedProjectPath,
            assetsPath,
            optionalFreshnessInputs,
            requiredFreshnessInputs);
    }

    private static string[] EvaluateFileCore(
        string governedProject,
        string governedProjectPath,
        string assetsPath,
        IReadOnlyList<string>? optionalFreshnessInputs,
        IReadOnlyList<string>? requiredFreshnessInputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(governedProject);
        ArgumentException.ThrowIfNullOrWhiteSpace(governedProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsPath);

        if (!File.Exists(governedProjectPath))
        {
            return [$"{governedProject} governed project file '{governedProjectPath}' is missing."];
        }

        if (!File.Exists(assetsPath))
        {
            return [$"{governedProject} evaluated dependency artifact '{assetsPath}' is missing; run the repository restore before the architecture suite."];
        }

        try
        {
            string assetsJson = File.ReadAllText(assetsPath);
            var violations = new HashSet<string>(
                EvaluateJsonCore(governedProject, assetsPath, assetsJson, governedProjectPath),
                StringComparer.Ordinal);

            DateTime artifactWriteTimeUtc = File.GetLastWriteTimeUtc(assetsPath);
            IReadOnlyList<string> optionalInputs =
            [
                .. optionalFreshnessInputs ?? [],

                // A referenced project can pull a forbidden dependency into this closure without touching the
                // governed project file or any shared restore input, so it is a restore input in its own right.
                .. ReferencedProjectPaths(assetsJson),
            ];

            var freshnessWriteTimes = new List<(string Path, DateTime WriteTimeUtc)>();
            freshnessWriteTimes.AddRange(FreshnessInputs(governedProjectPath, optionalInputs)
                .Distinct(MsBuildProjectEvaluation.PathComparer)
                .Select(path => (path, File.GetLastWriteTimeUtc(path))));
            foreach (string requiredInput in (requiredFreshnessInputs ?? [])
                .Distinct(MsBuildProjectEvaluation.PathComparer))
            {
                if (!TryGetRequiredFreshnessWriteTime(requiredInput, out DateTime writeTimeUtc))
                {
                    violations.Add(
                        $"{governedProject} required evaluated restore input '{requiredInput}' disappeared after MSBuild evaluation; restore and rerun the architecture suite.");
                    continue;
                }

                freshnessWriteTimes.Add((requiredInput, writeTimeUtc));
            }

            string? newestStaleInput = freshnessWriteTimes
                .DistinctBy(input => input.Path, MsBuildProjectEvaluation.PathComparer)
                .Where(input => artifactWriteTimeUtc < input.WriteTimeUtc)
                .OrderByDescending(input => input.WriteTimeUtc)
                .ThenBy(input => input.Path, MsBuildProjectEvaluation.PathComparer)
                .Select(input => input.Path)
                .FirstOrDefault();
            if (newestStaleInput is not null)
            {
                violations.Add(
                    $"{governedProject} evaluated dependency artifact '{assetsPath}' is stale: it is older than newest restore input '{newestStaleInput}'; run restore again.");
            }

            return [.. violations.Order(StringComparer.Ordinal)];
        }
        catch (IOException exception)
        {
            return [$"{governedProject} evaluated dependency artifact '{assetsPath}' could not be read: {exception.Message}"];
        }
        catch (UnauthorizedAccessException exception)
        {
            return [$"{governedProject} evaluated dependency artifact '{assetsPath}' could not be read: {exception.Message}"];
        }
    }

    /// <summary>
    /// Evaluates synthetic restored dependency artifact content without real-file identity or freshness checks.
    /// </summary>
    /// <param name="governedProject">The kernel project that owns the evaluated dependency graph.</param>
    /// <param name="assetsPath">The diagnostic path of the restored dependency artifact.</param>
    /// <param name="assetsJson">The complete restored dependency artifact content.</param>
    /// <returns>Actionable policy violations; an empty collection means the graph is usable and allowed.</returns>
    public static string[] EvaluateJson(string governedProject, string assetsPath, string assetsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(governedProject);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsPath);

        return EvaluateJsonCore(governedProject, assetsPath, assetsJson, expectedProjectPath: null);
    }

    private static string[] EvaluateJsonCore(
        string governedProject,
        string assetsPath,
        string assetsJson,
        string? expectedProjectPath)
    {
        if (string.IsNullOrWhiteSpace(assetsJson))
        {
            return [UnusableGraph(governedProject, assetsPath, "the artifact is empty")];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(assetsJson);
            return EvaluateDocument(governedProject, assetsPath, document.RootElement, expectedProjectPath);
        }
        catch (JsonException exception)
        {
            return [$"{governedProject} evaluated dependency artifact '{assetsPath}' could not be parsed: {exception.Message}"];
        }
    }

    private static string[] EvaluateDocument(
        string governedProject,
        string assetsPath,
        JsonElement root,
        string? expectedProjectPath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [UnusableGraph(governedProject, assetsPath, "the JSON root is not an object")];
        }

        if (!root.TryGetProperty("project", out JsonElement project)
            || project.ValueKind != JsonValueKind.Object)
        {
            return [UnusableGraph(governedProject, assetsPath, "project is absent or malformed")];
        }

        var violations = new HashSet<string>(StringComparer.Ordinal);
        if (expectedProjectPath is not null)
        {
            ValidateArtifactIdentity(governedProject, assetsPath, expectedProjectPath, project, violations);
        }

        if (!project.TryGetProperty("frameworks", out JsonElement frameworks)
            || frameworks.ValueKind != JsonValueKind.Object)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, "project.frameworks is absent or malformed"));
            return [.. violations.Order(StringComparer.Ordinal)];
        }

        JsonProperty[] declaredFrameworks = [.. frameworks.EnumerateObject()];
        if (declaredFrameworks.Length == 0)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, "project.frameworks is empty"));
            return [.. violations.Order(StringComparer.Ordinal)];
        }

        if (!root.TryGetProperty("targets", out JsonElement targets)
            || targets.ValueKind != JsonValueKind.Object)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, "targets is absent or malformed"));
            return [.. violations.Order(StringComparer.Ordinal)];
        }

        JsonProperty[] targetGraphs = [.. targets.EnumerateObject()];
        if (targetGraphs.Length == 0)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, "targets is empty"));
            return [.. violations.Order(StringComparer.Ordinal)];
        }

        var inspectedTargetGraphs = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty framework in declaredFrameworks)
        {
            if (string.IsNullOrWhiteSpace(framework.Name)
                || framework.Value.ValueKind != JsonValueKind.Object)
            {
                violations.Add(UnusableGraph(governedProject, assetsPath, $"framework entry '{framework.Name}' is malformed"));
                continue;
            }

            var frameworkAliases = new List<string> { framework.Name };
            if (framework.Value.TryGetProperty("targetAlias", out JsonElement targetAlias))
            {
                if (targetAlias.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(targetAlias.GetString()))
                {
                    violations.Add(UnusableGraph(governedProject, assetsPath, $"framework '{framework.Name}' has a malformed targetAlias"));
                }
                else
                {
                    frameworkAliases.Add(targetAlias.GetString()!);
                }
            }

            EvaluateFrameworkReferences(governedProject, assetsPath, framework, violations);

            JsonProperty[] matches = [.. targetGraphs.Where(target => frameworkAliases.Any(alias =>
                string.Equals(target.Name, alias, StringComparison.OrdinalIgnoreCase)
                || target.Name.StartsWith(alias + "/", StringComparison.OrdinalIgnoreCase)))];

            if (matches.Length == 0)
            {
                violations.Add(UnusableGraph(governedProject, assetsPath, $"no target graph matches declared framework '{framework.Name}'"));
                continue;
            }

            foreach (JsonProperty target in matches)
            {
                inspectedTargetGraphs.Add(target.Name);
                EvaluateTargetGraph(governedProject, assetsPath, target, violations);
            }
        }

        // An unclaimed target graph is a closure nothing inspected, so it must fail closed like every other
        // unusable shape rather than silently escaping classification.
        foreach (JsonProperty target in targetGraphs.Where(target => !inspectedTargetGraphs.Contains(target.Name)))
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, $"target graph '{target.Name}' matches no declared framework"));
        }

        return [.. violations.Order(StringComparer.Ordinal)];
    }

    private static void ValidateArtifactIdentity(
        string governedProject,
        string assetsPath,
        string expectedProjectPath,
        JsonElement project,
        HashSet<string> violations)
    {
        if (!project.TryGetProperty("restore", out JsonElement restore)
            || restore.ValueKind != JsonValueKind.Object
            || !restore.TryGetProperty("projectPath", out JsonElement projectPath)
            || projectPath.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(projectPath.GetString()))
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, "project.restore.projectPath is absent or malformed"));
            return;
        }

        string reportedProjectPath = projectPath.GetString()!;
        try
        {
            string expectedFullPath = Path.GetFullPath(expectedProjectPath);
            string reportedFullPath = Path.GetFullPath(reportedProjectPath);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!string.Equals(expectedFullPath, reportedFullPath, comparison))
            {
                violations.Add(
                    $"{governedProject} evaluated dependency artifact '{assetsPath}' identifies project '{reportedProjectPath}', expected '{expectedProjectPath}'.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, $"project.restore.projectPath is invalid: {exception.Message}"));
        }
    }

    private static void EvaluateFrameworkReferences(
        string governedProject,
        string assetsPath,
        JsonProperty framework,
        HashSet<string> violations)
    {
        if (!framework.Value.TryGetProperty("frameworkReferences", out JsonElement frameworkReferences))
        {
            return;
        }

        if (frameworkReferences.ValueKind != JsonValueKind.Object)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, $"framework '{framework.Name}' has malformed frameworkReferences"));
            return;
        }

        foreach (JsonProperty frameworkReference in frameworkReferences.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(frameworkReference.Name)
                || frameworkReference.Value.ValueKind != JsonValueKind.Object)
            {
                violations.Add(UnusableGraph(governedProject, assetsPath, $"framework '{framework.Name}' contains malformed framework reference '{frameworkReference.Name}'"));
                continue;
            }

            AddForbiddenDependencyViolation(
                governedProject,
                assetsPath,
                frameworkReference.Name.Trim(),
                violations,
                "framework reference");
        }
    }

    private static void EvaluateTargetGraph(
        string governedProject,
        string assetsPath,
        JsonProperty target,
        HashSet<string> violations)
    {
        if (target.Value.ValueKind != JsonValueKind.Object)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, $"target graph '{target.Name}' is not an object"));
            return;
        }

        JsonProperty[] libraries = [.. target.Value.EnumerateObject()];
        if (libraries.Length == 0)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, $"target graph '{target.Name}' contains no evaluated libraries"));
            return;
        }

        foreach (JsonProperty library in libraries)
        {
            if (library.Value.ValueKind != JsonValueKind.Object
                || !TryNormalizeLibraryName(library.Name, out string dependencyName))
            {
                violations.Add(UnusableGraph(governedProject, assetsPath, $"target graph '{target.Name}' contains malformed library entry '{library.Name}'"));
                continue;
            }

            AddForbiddenDependencyViolation(governedProject, assetsPath, dependencyName, violations, "dependency");
            EvaluateLibraryFrameworkReferences(governedProject, assetsPath, dependencyName, library.Value, violations);
        }
    }

    private static void EvaluateLibraryFrameworkReferences(
        string governedProject,
        string assetsPath,
        string dependencyName,
        JsonElement library,
        HashSet<string> violations)
    {
        // A restored library carries its own shared-framework demands here; without this the kernel can acquire
        // the ASP.NET Core shared framework through a neutrally named package the name checks call safe.
        if (!library.TryGetProperty("frameworkReferences", out JsonElement frameworkReferences))
        {
            return;
        }

        if (frameworkReferences.ValueKind != JsonValueKind.Array)
        {
            violations.Add(UnusableGraph(governedProject, assetsPath, $"library '{dependencyName}' has malformed frameworkReferences"));
            return;
        }

        foreach (JsonElement frameworkReference in frameworkReferences.EnumerateArray())
        {
            if (frameworkReference.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(frameworkReference.GetString()))
            {
                violations.Add(UnusableGraph(governedProject, assetsPath, $"library '{dependencyName}' contains a malformed framework reference"));
                continue;
            }

            AddForbiddenDependencyViolation(
                governedProject,
                assetsPath,
                frameworkReference.GetString()!.Trim(),
                violations,
                $"transitive framework reference (via {dependencyName})");
        }
    }

    private static void AddForbiddenDependencyViolation(
        string governedProject,
        string assetsPath,
        string dependencyName,
        HashSet<string> violations,
        string dependencyKind)
    {
        string? forbiddenFamily = ForbiddenFamily(dependencyName);
        if (forbiddenFamily is not null)
        {
            violations.Add(
                $"{governedProject} evaluated {dependencyKind} '{dependencyName}' is forbidden ({forbiddenFamily}) in '{assetsPath}'.");
        }
    }

    private static string[] EvaluateSnapshot(
        string governedProject,
        MsBuildProjectSnapshot snapshot,
        string? owningProjectPath = null)
    {
        var violations = new HashSet<string>(StringComparer.Ordinal);
        foreach (MsBuildEvaluatedItem item in snapshot.Items.Where(item =>
            !string.Equals(item.ItemType, "PackageVersion", StringComparison.OrdinalIgnoreCase)
            && (owningProjectPath is null
                || !MsBuildProjectEvaluation.PathComparer.Equals(item.DefiningProjectPath, owningProjectPath))))
        {
            if (!TryNormalizeDeclaredReference(item.ItemType, item.Identity, out string dependencyName))
            {
                violations.Add(
                    $"{governedProject} evaluated {item.ItemType} '{item.Identity}' defined by '{item.DefiningProjectPath}' is malformed in '{snapshot.ProjectPath}'.");
                continue;
            }

            string? forbiddenFamily = ForbiddenFamily(dependencyName);
            if (forbiddenFamily is null)
            {
                continue;
            }

            string canonicalIdentity = item.CanonicalPath is null
                ? dependencyName
                : $"{dependencyName} at '{item.CanonicalPath}'";
            violations.Add(
                $"{governedProject} evaluated {item.ItemType} '{canonicalIdentity}' defined by '{item.DefiningProjectPath}' is forbidden ({forbiddenFamily}) in '{snapshot.ProjectPath}'.");
        }

        return [.. violations.Order(StringComparer.Ordinal)];
    }

    private static bool TryEvaluateProjectClosure(
        string governedProject,
        string governedProjectPath,
        out IReadOnlyList<MsBuildProjectSnapshot> projectClosure,
        out string diagnostic)
    {
        var snapshots = new List<MsBuildProjectSnapshot>();
        var pendingProjects = new Queue<(string Path, IReadOnlyDictionary<string, string>? GlobalProperties)>();
        var visitedEvaluations = new HashSet<string>(StringComparer.Ordinal);
        pendingProjects.Enqueue((governedProjectPath, null));

        while (pendingProjects.Count > 0)
        {
            (string projectPath, IReadOnlyDictionary<string, string>? globalProperties) = pendingProjects.Dequeue();
            string canonicalProjectPath;
            try
            {
                canonicalProjectPath = Path.GetFullPath(projectPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                projectClosure = snapshots.AsReadOnly();
                diagnostic = $"{governedProject} referenced project identity '{projectPath}' is unusable: {exception.Message}";
                return false;
            }

            if (globalProperties is not null
                && visitedEvaluations.Contains(ProjectEvaluationKey(canonicalProjectPath, globalProperties)))
            {
                continue;
            }

            bool evaluated = globalProperties is null
                ? MsBuildProjectEvaluation.TryEvaluate(
                    canonicalProjectPath,
                    out MsBuildProjectSnapshot? snapshot,
                    out string evaluationDiagnostic)
                : MsBuildProjectEvaluation.TryEvaluate(
                    canonicalProjectPath,
                    globalProperties,
                    out snapshot,
                    out evaluationDiagnostic);
            if (!evaluated)
            {
                projectClosure = snapshots.AsReadOnly();
                diagnostic = $"{governedProject} dependency evaluation failed while inspecting '{canonicalProjectPath}': {evaluationDiagnostic}";
                return false;
            }

            if (!visitedEvaluations.Add(ProjectEvaluationKey(snapshot!.ProjectPath, snapshot.GlobalProperties)))
            {
                continue;
            }

            snapshots.Add(snapshot);
            foreach (MsBuildEvaluatedItem reference in snapshot!.ItemsOfType("ProjectReference"))
            {
                if (reference.CanonicalPath is null)
                {
                    projectClosure = snapshots.AsReadOnly();
                    diagnostic = $"{governedProject} ProjectReference '{reference.Identity}' defined by '{reference.DefiningProjectPath}' has no canonical path.";
                    return false;
                }

                pendingProjects.Enqueue((reference.CanonicalPath, reference.ProjectReferenceGlobalProperties));
            }
        }

        projectClosure = snapshots.AsReadOnly();
        diagnostic = string.Empty;
        return true;
    }

    private static IEnumerable<string> FreshnessInputs(
        string governedProjectPath,
        IReadOnlyList<string>? additionalFreshnessInputs)
    {
        yield return governedProjectPath;

        foreach (string additionalInput in additionalFreshnessInputs ?? [])
        {
            if (File.Exists(additionalInput))
            {
                yield return additionalInput;
            }
        }
    }

    private static bool TryGetRequiredFreshnessWriteTime(string path, out DateTime writeTimeUtc)
    {
        var input = new FileInfo(path);
        input.Refresh();
        if (!input.Exists)
        {
            writeTimeUtc = default;
            return false;
        }

        writeTimeUtc = input.LastWriteTimeUtc;
        input.Refresh();
        return input.Exists;
    }

    private static string ProjectEvaluationKey(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties)
        => string.Join(
            '\u001f',
            [
                OperatingSystem.IsWindows() ? projectPath.ToUpperInvariant() : projectPath,
                .. globalProperties
                    .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(property => $"{property.Key.ToUpperInvariant()}={property.Value}"),
            ]);

    private static IReadOnlyList<string> ReferencedProjectPaths(string assetsJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(assetsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("project", out JsonElement project)
                || project.ValueKind != JsonValueKind.Object
                || !project.TryGetProperty("restore", out JsonElement restore)
                || restore.ValueKind != JsonValueKind.Object
                || !restore.TryGetProperty("frameworks", out JsonElement frameworks)
                || frameworks.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var referencedProjectPaths = new List<string>();
            foreach (JsonProperty framework in frameworks.EnumerateObject())
            {
                if (framework.Value.ValueKind != JsonValueKind.Object
                    || !framework.Value.TryGetProperty("projectReferences", out JsonElement projectReferences)
                    || projectReferences.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                referencedProjectPaths.AddRange(projectReferences
                    .EnumerateObject()
                    .Where(reference => reference.Value.ValueKind == JsonValueKind.Object)
                    .Select(reference => reference.Value.TryGetProperty("projectPath", out JsonElement projectPath)
                        && projectPath.ValueKind == JsonValueKind.String
                            ? projectPath.GetString()
                            : reference.Name)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!));
            }

            return referencedProjectPaths;
        }
        catch (JsonException)
        {
            // Parse failures are already reported as an unusable graph by the evaluation path.
            return [];
        }
    }

    private static string[] SharedRestoreInputs(string repositoryRoot) =>
    [
        Path.Combine(repositoryRoot, "Directory.Packages.props"),
        Path.Combine(repositoryRoot, "Directory.Build.props"),
        Path.Combine(repositoryRoot, "Directory.Build.targets"),
        Path.Combine(repositoryRoot, "Directory.Solution.props"),
        Path.Combine(repositoryRoot, "global.json"),

        // Case-sensitive filesystems make each spelling a distinct file, and only the present ones are checked.
        Path.Combine(repositoryRoot, "NuGet.Config"),
        Path.Combine(repositoryRoot, "NuGet.config"),
        Path.Combine(repositoryRoot, "nuget.config"),
    ];

    private static bool TryNormalizeLibraryName(string libraryKey, out string dependencyName)
    {
        int separator = libraryKey.LastIndexOf('/');
        if (separator <= 0
            || separator == libraryKey.Length - 1
            || string.IsNullOrWhiteSpace(libraryKey[..separator]))
        {
            dependencyName = string.Empty;
            return false;
        }

        dependencyName = libraryKey[..separator].Trim();
        return true;
    }

    private static bool TryNormalizeDeclaredReference(
        string referenceKind,
        string declaration,
        out string dependencyName)
    {
        string normalizedDeclaration = declaration.Trim();
        if (normalizedDeclaration.Length == 0)
        {
            dependencyName = string.Empty;
            return false;
        }

        if (normalizedDeclaration.IndexOfAny(['*', '?']) >= 0)
        {
            dependencyName = string.Empty;
            return false;
        }

        if (referenceKind == "ProjectReference")
        {
            string normalizedPath = normalizedDeclaration.Replace('\\', '/');
            string fileName = normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..];
            if (!fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || ContainsMsBuildExpression(fileName)
                || normalizedPath.Contains("@(", StringComparison.Ordinal)
                || normalizedPath.Contains("%(", StringComparison.Ordinal))
            {
                dependencyName = string.Empty;
                return false;
            }

            dependencyName = Path.GetFileNameWithoutExtension(fileName);
            return !string.IsNullOrWhiteSpace(dependencyName);
        }

        if (ContainsMsBuildExpression(normalizedDeclaration))
        {
            dependencyName = string.Empty;
            return false;
        }

        if (referenceKind == "Reference")
        {
            int fusionNameSeparator = normalizedDeclaration.IndexOf(',');
            string assemblyDeclaration = (fusionNameSeparator < 0
                ? normalizedDeclaration
                : normalizedDeclaration[..fusionNameSeparator]).Trim();

            // A hint-path style Reference names an assembly file; classify the assembly, not the path to it.
            string assemblyPath = assemblyDeclaration.Replace('\\', '/');
            string assemblyFile = assemblyPath[(assemblyPath.LastIndexOf('/') + 1)..];
            dependencyName = assemblyFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || assemblyFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(assemblyFile)
                : assemblyFile;
            return dependencyName.Length > 0;
        }

        dependencyName = normalizedDeclaration;
        return true;
    }

    /// <summary>
    /// Discovers one kind of declared MSBuild reference from a project file so every gate that reads
    /// project XML shares the same fail-closed discovery instead of keeping its own parser.
    /// </summary>
    /// <param name="projectPath">The project file to inspect.</param>
    /// <param name="referenceKind">The canonical MSBuild item kind, such as <c>ProjectReference</c>.</param>
    /// <returns>
    /// The normalized dependency names, plus an unmatchable sentinel for every conditional, opaque, or
    /// malformed item specification so an allowlist comparison fails closed rather than dropping it.
    /// </returns>
    internal static string[] DeclaredReferenceNames(string projectPath, string referenceKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKind);

        XDocument project;
        try
        {
            project = XDocument.Load(projectPath);
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            return [$"<unreadable {referenceKind} source '{projectPath}': {exception.Message}>"];
        }

        if (project.Root is null
            || !string.Equals(project.Root.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase))
        {
            return [$"<unusable {referenceKind} source '{projectPath}': the XML root is not a Project element>"];
        }

        return [.. project
            .Descendants()
            .Where(element => IsReferenceAddition(element, referenceKind))
            .SelectMany(element => DeclaredReferenceNames(element, referenceKind))];
    }

    private static bool IsReferenceAddition(XElement element, string referenceKind)
        => string.Equals(element.Name.LocalName, referenceKind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(element.Parent?.Name.LocalName, "ItemGroup", StringComparison.OrdinalIgnoreCase)
            && (element.Attribute("Include") is not null
                || (element.Attribute("Remove") is null && element.Attribute("Update") is null));

    private static IEnumerable<string> DeclaredReferenceNames(XElement reference, string referenceKind)
    {
        string? include = reference.Attribute("Include")?.Value;
        if (string.IsNullOrWhiteSpace(include))
        {
            return [$"<malformed {referenceKind} without Include>"];
        }

        for (XElement? current = reference; current is not null; current = current.Parent)
        {
            if (current.Attribute("Condition") is not null)
            {
                return [$"<conditional {referenceKind} '{include}'>"];
            }
        }

        string[] includedItems = include.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return includedItems.Length == 0
            ? [$"<malformed {referenceKind} with empty Include>"]
            : includedItems.Select(item => TryNormalizeDeclaredReference(referenceKind, item, out string dependencyName)
                ? dependencyName
                : $"<malformed {referenceKind} '{item}'>");
    }

    private static bool ContainsMsBuildExpression(string value)
        => value.Contains("$(", StringComparison.Ordinal)
            || value.Contains("@(", StringComparison.Ordinal)
            || value.Contains("%(", StringComparison.Ordinal);

    private static bool IsTopLevelSourceProject(string sourceRoot, string projectFile)
        => string.Equals(
            Path.GetFullPath(Path.GetDirectoryName(Path.GetDirectoryName(projectFile)!)!),
            Path.GetFullPath(sourceRoot),
            StringComparison.Ordinal);

    /// <summary>
    /// Determines whether a path lives under a build output directory, so every gate that walks source
    /// files shares one exclusion rule instead of keeping its own copy.
    /// </summary>
    /// <param name="path">The candidate file path.</param>
    /// <returns><see langword="true"/> when the path is build output.</returns>
    internal static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool TryGetDirectReferenceKind(string localName, out string referenceKind)
    {
        referenceKind = _supportedDirectReferenceKinds.FirstOrDefault(kind =>
            string.Equals(localName, kind, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return referenceKind.Length > 0;
    }

    private static string? ForbiddenFamily(string dependencyName)
    {
        if (string.Equals(dependencyName, "Hexalith.EventStore.Contracts", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (IsNameOrChild(dependencyName, "Hexalith.EventStore"))
        {
            return "EventStore client/runtime";
        }

        if (IsNameOrChild(dependencyName, "Dapr"))
        {
            return "Dapr runtime";
        }

        if (IsNameOrChild(dependencyName, "Microsoft.AspNetCore.Components")
            || IsNameOrChild(dependencyName, "Microsoft.FluentUI")
            || IsNameOrChild(dependencyName, "FluentUI")
            || IsNameOrChild(dependencyName, "MudBlazor")
            || IsNameOrChild(dependencyName, "Blazorise")
            || IsNameOrChild(dependencyName, "Radzen")
            || HasSegment(dependencyName, "Blazor")
            || IsNameOrChild(dependencyName, "Hexalith.FrontComposer")
            || IsNamedAdapterSegment(dependencyName, "UI"))
        {
            return "UI adapter";
        }

        if (IsNameOrChild(dependencyName, "ModelContextProtocol")
            || IsNamedAdapterSegment(dependencyName, "Mcp"))
        {
            return "MCP adapter";
        }

        if (HasSegment(dependencyName, "OpenAI")
            || HasSegment(dependencyName, "SemanticKernel")
            || IsNameOrChild(dependencyName, "Microsoft.Extensions.AI")
            || IsNameOrChild(dependencyName, "Azure.AI")
            || IsNameOrChild(dependencyName, "Anthropic")
            || IsNamedAdapterSegment(dependencyName, "Llm"))
        {
            return "LLM adapter";
        }

        if (IsNameOrChild(dependencyName, "Microsoft.OpenApi")
            || IsNameOrChild(dependencyName, "Microsoft.AspNetCore.OpenApi")
            || IsNameOrChild(dependencyName, "Swashbuckle")
            || IsNameOrChild(dependencyName, "NSwag"))
        {
            return "OpenAPI adapter";
        }

        if (IsNameOrChild(dependencyName, "Microsoft.AspNetCore")
            || IsNameOrChild(dependencyName, "Microsoft.Extensions.Hosting")
            || IsNameOrChild(dependencyName, "Microsoft.Extensions.Diagnostics.HealthChecks")
            || IsNameOrChild(dependencyName, "AspNetCore.HealthChecks")
            || IsNameOrChild(dependencyName, "Aspire.Hosting")
            || IsNamedAdapterSegment(dependencyName, "AppHost")
            || IsNamedAdapterSegment(dependencyName, "ServiceDefaults"))
        {
            return "hosting runtime";
        }

        if (IsNameOrChild(dependencyName, "OpenTelemetry")
            || IsNameOrChild(dependencyName, "Microsoft.ApplicationInsights")
            || IsNameOrChild(dependencyName, "Microsoft.Extensions.Telemetry")
            || IsNameOrChild(dependencyName, "Azure.Monitor.OpenTelemetry")
            || IsNameOrChild(dependencyName, "Serilog")
            || IsNameOrChild(dependencyName, "NLog"))
        {
            return "telemetry adapter";
        }

        if (IsNameOrChild(dependencyName, "Microsoft.EntityFrameworkCore")
            || IsNameOrChild(dependencyName, "EntityFramework")
            || IsNameOrChild(dependencyName, "StackExchange.Redis")
            || IsNameOrChild(dependencyName, "Microsoft.Extensions.Caching.StackExchangeRedis")
            || IsNameOrChild(dependencyName, "MongoDB")
            || IsNameOrChild(dependencyName, "Npgsql")
            || IsNameOrChild(dependencyName, "Microsoft.Data.SqlClient")
            || IsNameOrChild(dependencyName, "System.Data.SqlClient")
            || IsNameOrChild(dependencyName, "Azure.Cosmos")
            || IsNameOrChild(dependencyName, "Microsoft.Azure.Cosmos")
            || IsNameOrChild(dependencyName, "Marten")
            || IsNameOrChild(dependencyName, "Dapper"))
        {
            return "persistence adapter";
        }

        if (_networkClientPrefixes.Any(prefix => IsNameOrChild(dependencyName, prefix)))
        {
            return "network/messaging client";
        }

        if (_namedAdapterSegments.Any(segment => IsNamedAdapterSegment(dependencyName, segment)))
        {
            return "named adapter family";
        }

        return null;
    }

    private static bool IsNameOrChild(string dependencyName, string namespaceName)
        => string.Equals(dependencyName, namespaceName, StringComparison.OrdinalIgnoreCase)
            || dependencyName.StartsWith(namespaceName + ".", StringComparison.OrdinalIgnoreCase);

    private static bool IsNamedAdapterSegment(string dependencyName, string segment)
        => !IsSegmentMatchingExempt(dependencyName) && HasSegment(dependencyName, segment);

    // Only System.* is exempt from generic segment matching. Microsoft.* is deliberately NOT exempt: a
    // blanket Microsoft exemption is what let Microsoft-branded MCP, Client, and UI adapters into the
    // kernel. Do not widen this predicate without an explicit architecture decision.
    private static bool IsSegmentMatchingExempt(string dependencyName)
        => IsNameOrChild(dependencyName, "System");

    private static bool HasSegment(string dependencyName, string segment)
        => dependencyName.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Any(candidate => string.Equals(candidate, segment, StringComparison.OrdinalIgnoreCase));

    private static string UnusableGraph(string governedProject, string assetsPath, string reason)
        => $"{governedProject} evaluated dependency artifact '{assetsPath}' is unusable: {reason}.";
}
