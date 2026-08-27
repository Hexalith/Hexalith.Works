using System.Text.Json;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Evaluates restored kernel dependency graphs for forbidden adapter and infrastructure libraries.
/// </summary>
internal static class KernelDependencyPolicy
{
    private static readonly IReadOnlyList<string> _governedProjects = Array.AsReadOnly<string>(
    [
        "Hexalith.Works.Contracts",
        "Hexalith.Works.Server",
        "Hexalith.Works.Projections",
        "Hexalith.Works.Reactor",
    ]);

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
    /// Gets the exact kernel project set whose evaluated dependency closures are governed.
    /// </summary>
    public static IReadOnlyList<string> GovernedProjects => _governedProjects;

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

        return EvaluateFile(
            governedProject,
            Path.Combine(projectDirectory, governedProject + ".csproj"),
            Path.Combine(projectDirectory, "obj", "project.assets.json"),
            SharedRestoreInputs(repositoryRoot));
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
            IReadOnlyList<string> freshnessInputs =
            [
                .. additionalFreshnessInputs ?? [],

                // A referenced project can pull a forbidden dependency into this closure without touching the
                // governed project file or any shared restore input, so it is a restore input in its own right.
                .. ReferencedProjectPaths(assetsJson),
            ];

            foreach (string freshnessInput in FreshnessInputs(governedProjectPath, freshnessInputs))
            {
                if (artifactWriteTimeUtc < File.GetLastWriteTimeUtc(freshnessInput))
                {
                    violations.Add(
                        $"{governedProject} evaluated dependency artifact '{assetsPath}' is stale: it is older than restore input '{freshnessInput}'; run restore again.");
                }
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
                frameworkReference.Name,
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
        => !IsFrameworkLibrary(dependencyName) && HasSegment(dependencyName, segment);

    private static bool IsFrameworkLibrary(string dependencyName)
        => IsNameOrChild(dependencyName, "System") || IsNameOrChild(dependencyName, "Microsoft");

    private static bool HasSegment(string dependencyName, string segment)
        => dependencyName.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Any(candidate => string.Equals(candidate, segment, StringComparison.OrdinalIgnoreCase));

    private static string UnusableGraph(string governedProject, string assetsPath, string reason)
        => $"{governedProject} evaluated dependency artifact '{assetsPath}' is unusable: {reason}.";
}
