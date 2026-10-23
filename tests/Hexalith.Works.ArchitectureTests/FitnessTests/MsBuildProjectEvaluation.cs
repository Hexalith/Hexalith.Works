using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Evaluates dependency-affecting MSBuild state across explicitly selected and declared build lanes.
/// </summary>
internal static class MsBuildProjectEvaluation
{
    private static readonly string[] _dependencyItemTypes =
        ["ProjectReference", "PackageReference", "GlobalPackageReference", "PackageVersion", "FrameworkReference", "Reference"];

    private static readonly string[] _governedPropertyNames =
    [
        "Configuration",
        "Platform",
        "TargetFramework",
        "HexalithDependencyModeDefaulted",
        "HexalithEventStoreRoot",
        "HexalithEventStoreFromSource",
        "HexalithPartiesRoot",
        "HexalithPlatformRoot",
        "HexalithPolymorphicSerializationsRoot",
        "HexalithTenantsRoot",
        "IsPackable",
        "PackageId",
        "ShouldUnsetParentConfigurationAndPlatform",
        "UseHexalithProjectReferences",
        "UseNuGetDeps",
    ];

    private static readonly IReadOnlyDictionary<string, string> _emptyGlobalProperties =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static readonly IReadOnlyDictionary<string, string> _releaseGlobalProperties =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BuildingInsideVisualStudio"] = "false",
                ["Configuration"] = "Release",
                ["DesignTimeBuild"] = "false",
            });

    /// <summary>
    /// Gets the platform-appropriate canonical-path comparer.
    /// </summary>
    internal static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Tries to evaluate a project without leaking evaluator exceptions into architecture tests.
    /// </summary>
    /// <param name="projectPath">The project file to evaluate.</param>
    /// <param name="snapshot">The immutable evaluation snapshot on success.</param>
    /// <param name="diagnostic">An actionable evaluator diagnostic on failure.</param>
    /// <returns><see langword="true"/> when evaluation produced a complete usable snapshot.</returns>
    internal static bool TryEvaluate(
        string projectPath,
        out MsBuildProjectSnapshot? snapshot,
        out string diagnostic)
        => TryEvaluateCore(projectPath, _releaseGlobalProperties, expandDeclaredPlatforms: true, out snapshot, out diagnostic);

    /// <summary>
    /// Evaluates both supported configurations, including their declared platforms and frameworks.
    /// </summary>
    /// <param name="projectPath">The project file to evaluate.</param>
    /// <param name="snapshots">The complete configuration snapshots, or an empty collection on failure.</param>
    /// <param name="diagnostic">An actionable evaluator diagnostic on failure.</param>
    /// <returns>Whether every supported lane was evaluated successfully.</returns>
    internal static bool TryEvaluateSupportedLanes(
        string projectPath,
        out IReadOnlyList<MsBuildProjectSnapshot> snapshots,
        out string diagnostic)
    {
        var evaluated = new List<MsBuildProjectSnapshot>();
        foreach (string configuration in new[] { "Debug", "Release" })
        {
            var properties = new Dictionary<string, string>(_releaseGlobalProperties, StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = configuration,
            };
            if (!TryEvaluateCore(projectPath, properties, expandDeclaredPlatforms: true, out MsBuildProjectSnapshot? snapshot, out diagnostic))
            {
                snapshots = [];
                diagnostic = $"{configuration} lane: {diagnostic}";
                return false;
            }

            evaluated.Add(snapshot!);
        }

        snapshots = evaluated.AsReadOnly();
        diagnostic = string.Empty;
        return true;
    }

    /// <summary>Evaluates each supported configuration/platform/framework independently for universal checks.</summary>
    /// <param name="projectPath">The project to inspect.</param>
    /// <param name="snapshots">One snapshot for each supported build lane, without unioning evidence.</param>
    /// <param name="diagnostic">An actionable failure diagnostic.</param>
    /// <returns>Whether all supported lanes could be evaluated.</returns>
    internal static bool TryEvaluateIndependentSupportedLanes(
        string projectPath,
        out IReadOnlyList<MsBuildProjectSnapshot> snapshots,
        out string diagnostic)
        => TryEvaluateIndependentSupportedLanes(projectPath, _emptyGlobalProperties, out snapshots, out diagnostic);

    /// <summary>Evaluates independent supported lanes with explicit caller-selected global overrides.</summary>
    /// <param name="projectPath">The project to inspect.</param>
    /// <param name="globalOverrides">Explicit globals applied equally to every supported configuration.</param>
    /// <param name="snapshots">The independent evaluation evidence.</param>
    /// <param name="diagnostic">An actionable failure diagnostic.</param>
    /// <returns>Whether every selected lane could be evaluated.</returns>
    internal static bool TryEvaluateIndependentSupportedLanes(
        string projectPath,
        IReadOnlyDictionary<string, string> globalOverrides,
        out IReadOnlyList<MsBuildProjectSnapshot> snapshots,
        out string diagnostic)
    {
        try
        {
            _ = MsBuildRuntime.ToolsPath;
            snapshots = EvaluateIndependentSupportedLanes(projectPath, globalOverrides);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsEvaluationFailure(exception))
        {
            snapshots = [];
            diagnostic = $"MSBuild lane evaluation of project '{projectPath}' failed: {exception.Message}";
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<MsBuildProjectSnapshot> EvaluateIndependentSupportedLanes(
        string projectPath, IReadOnlyDictionary<string, string> globalOverrides)
    {
        string canonicalPath = CanonicalExistingFile(projectPath, "project");
        using var collection = new ProjectCollection();
        var snapshots = new List<MsBuildProjectSnapshot>();
        foreach (string configuration in new[] { "Debug", "Release" })
        {
            var globals = new Dictionary<string, string>(_releaseGlobalProperties, StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = configuration,
            };
            foreach ((string name, string value) in globalOverrides)
            {
                globals[name] = value;
            }

            foreach (Project project in LoadEvaluationLanes(collection, canonicalPath, globals, expandDeclaredPlatforms: true))
            {
                // The outer multi-targeting evaluation discovers frameworks but does not build an assembly.
                if (string.IsNullOrWhiteSpace(project.GetPropertyValue("TargetFrameworks"))
                    || project.GlobalProperties.ContainsKey("TargetFramework"))
                {
                    snapshots.Add(Snapshot(canonicalPath, new Dictionary<string, string>(project.GlobalProperties, StringComparer.OrdinalIgnoreCase), [project]));
                }
            }
        }

        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException($"Project '{projectPath}' declares no usable supported build lane.");
        }

        return snapshots.AsReadOnly();
    }

    /// <summary>
    /// Tries to evaluate a referenced project with its exact effective global properties.
    /// </summary>
    /// <param name="projectPath">The project file to evaluate.</param>
    /// <param name="globalProperties">The complete effective global-property set inherited through ProjectReference metadata.</param>
    /// <param name="snapshot">The immutable evaluation snapshot on success.</param>
    /// <param name="diagnostic">An actionable evaluator diagnostic on failure.</param>
    /// <returns><see langword="true"/> when evaluation produced a complete usable snapshot.</returns>
    internal static bool TryEvaluate(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        out MsBuildProjectSnapshot? snapshot,
        out string diagnostic)
        => TryEvaluateCore(projectPath, globalProperties, expandDeclaredPlatforms: false, out snapshot, out diagnostic);

    private static bool TryEvaluateCore(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        bool expandDeclaredPlatforms,
        out MsBuildProjectSnapshot? snapshot,
        out string diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(globalProperties);

        try
        {
            _ = MsBuildRuntime.ToolsPath;
            snapshot = Evaluate(projectPath, globalProperties, expandDeclaredPlatforms);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsEvaluationFailure(exception))
        {
            snapshot = null;
            diagnostic = $"MSBuild evaluation of project '{projectPath}' failed: {exception.Message}";
            return false;
        }
    }

    // This bootstrap boundary must not mention MSBuild types in its signatures or exception filters.
    private static bool IsEvaluationFailure(Exception exception)
        => exception is ArgumentException or IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException
            || exception.GetType().FullName == "Microsoft.Build.Exceptions.InvalidProjectFileException";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MsBuildProjectSnapshot Evaluate(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        bool expandDeclaredPlatforms)
    {
        string canonicalProjectPath = CanonicalExistingFile(projectPath, "project");
        var initialGlobalProperties = new Dictionary<string, string>(
            globalProperties,
            StringComparer.OrdinalIgnoreCase);
        using var projectCollection = new ProjectCollection();
        IReadOnlyList<Project> projects = LoadEvaluationLanes(
            projectCollection, canonicalProjectPath, initialGlobalProperties, expandDeclaredPlatforms);

        return Snapshot(canonicalProjectPath, initialGlobalProperties, projects);
    }

    private static MsBuildProjectSnapshot Snapshot(
        string canonicalProjectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        IReadOnlyList<Project> projects)
    {
        var items = new List<MsBuildEvaluatedItem>();
        var imports = new List<string>();
        foreach (Project project in projects)
        {
            ValidateDependencyDeclarations(project);
            string[] packageRoots = [.. RestoredPackageRoots(project)];
            IReadOnlyDictionary<string, IReadOnlyList<MsBuildPropertyProvenance>> provenance = PropertyProvenance(project);
            items.AddRange(project.Items
                .Where(item => TryGetCanonicalDependencyItemType(item.ItemType, out _))
                .Select(item => EvaluatedItem(item, project.GlobalProperties, provenance)));
            imports.AddRange(project.ImportsIncludingDuplicates
                .Select(import => CanonicalExistingFile(
                    import.ImportedProject.FullPath,
                    $"import resolved by '{canonicalProjectPath}'"))
                .Where(import => IsCustomImportPath(import, canonicalProjectPath))
                .Where(import => !packageRoots.Any(root => IsUnderDirectory(import, root))));
        }

        return new MsBuildProjectSnapshot(
            canonicalProjectPath,
            globalProperties,
            items.DistinctBy(EvaluatedItemKey, StringComparer.Ordinal),
            imports.Distinct(PathComparer),
            GovernedProperties(projects[0]));
    }

    /// <summary>
    /// Captures the evaluated dependency-mode properties the governance tests reason about, so a
    /// sibling-checkout workspace is classified by its real evaluated roots rather than a fixed path shape.
    /// </summary>
    private static IReadOnlyDictionary<string, string> GovernedProperties(Project project)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in _governedPropertyNames)
        {
            string value = project.GetPropertyValue(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                properties[name] = value;
            }
        }

        return properties;
    }

    private static Project LoadProject(
        ProjectCollection projectCollection,
        string canonicalProjectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        bool ignoreMissingImports = false)
        => new(
            canonicalProjectPath,
            new Dictionary<string, string>(globalProperties, StringComparer.OrdinalIgnoreCase),
            toolsVersion: null,
            projectCollection,
            ProjectLoadSettings.FailOnUnresolvedSdk
                | ProjectLoadSettings.RecordDuplicateButNotCircularImports
                | ProjectLoadSettings.RejectCircularImports
                | (ignoreMissingImports ? ProjectLoadSettings.IgnoreMissingImports : default));

    private static IReadOnlyList<Project> LoadEvaluationLanes(
        ProjectCollection projectCollection,
        string projectPath,
        IReadOnlyDictionary<string, string> initialProperties,
        bool expandDeclaredPlatforms)
    {
        var projects = new List<Project>();
        var pending = new Queue<IReadOnlyDictionary<string, string>>();
        var requestedLanes = new HashSet<string>(StringComparer.Ordinal);
        var loadedLanes = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(initialProperties);
        while (pending.TryDequeue(out IReadOnlyDictionary<string, string>? requested))
        {
            if (!requestedLanes.Add(GlobalPropertiesKey(requested)))
            {
                continue;
            }

            string[] platforms = [];
            if (expandDeclaredPlatforms && !initialProperties.ContainsKey("Platform"))
            {
                // Discovery may run outside every supported platform. It contributes no evidence: each
                // selected lane below is loaded strictly, so a missing supported import still fails closed.
                using var discoveryCollection = new ProjectCollection();
                Project discovery = LoadProject(discoveryCollection, projectPath, requested, ignoreMissingImports: true);
                platforms = [.. DeclaredLanes(discovery, "Platforms")];
            }

            IEnumerable<IReadOnlyDictionary<string, string>> lanes = platforms.Length == 0
                ? [requested]
                : platforms.Select(platform => new Dictionary<string, string>(requested, StringComparer.OrdinalIgnoreCase)
                {
                    ["Platform"] = platform,
                });
            foreach (IReadOnlyDictionary<string, string> lane in lanes)
            {
                if (!loadedLanes.Add(GlobalPropertiesKey(lane)))
                {
                    continue;
                }

                Project project = LoadProject(projectCollection, projectPath, lane);
                projects.Add(project);
                if (!lane.ContainsKey("TargetFramework"))
                {
                    foreach (string framework in DeclaredLanes(project, "TargetFrameworks"))
                    {
                        pending.Enqueue(new Dictionary<string, string>(lane, StringComparer.OrdinalIgnoreCase)
                        {
                            ["TargetFramework"] = framework,
                        });
                    }
                }
            }
        }

        return projects.AsReadOnly();
    }

    private static string GlobalPropertiesKey(IReadOnlyDictionary<string, string> properties)
        => string.Join('\u001e', properties.OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
            .Select(property => $"{property.Key.ToUpperInvariant()}={property.Value}"));

    private static IEnumerable<string> DeclaredLanes(Project project, string propertyName)
    {
        string declaration = project.GetPropertyValue(propertyName).Trim();
        if (declaration.Length == 0)
        {
            return [];
        }

        if (ContainsOpaqueExpression(declaration))
        {
            throw new InvalidOperationException(
                $"Project '{project.FullPath}' has an unusable {propertyName} declaration '{declaration}'.");
        }

        string[] lanes = declaration.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lanes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Project '{project.FullPath}' has an empty {propertyName} declaration '{declaration}'.");
        }

        return lanes.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> RestoredPackageRoots(Project project)
        => new[] { "NuGetPackageRoot", "NuGetPackageFolders", "RestorePackagesPath" }
            .SelectMany(name => project.GetPropertyValue(name).Split(
                ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(path => Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar), project.DirectoryPath))
            .Distinct(PathComparer);

    private static MsBuildEvaluatedItem EvaluatedItem(
        ProjectItem item,
        IDictionary<string, string> evaluationGlobalProperties,
        IReadOnlyDictionary<string, IReadOnlyList<MsBuildPropertyProvenance>> provenance)
    {
        if (!TryGetCanonicalDependencyItemType(item.ItemType, out string itemType))
        {
            throw new InvalidOperationException($"Unsupported dependency item type '{item.ItemType}' was selected for evaluation.");
        }

        string identity = item.EvaluatedInclude.Trim();
        if (identity.Length == 0 || ContainsOpaqueExpression(identity) || identity.IndexOfAny(['*', '?']) >= 0)
        {
            throw new InvalidOperationException(
                $"Evaluated {itemType} item '{item.UnevaluatedInclude}' has an unusable identity '{item.EvaluatedInclude}'.");
        }

        string definingProjectPath = EffectiveDefiningProjectPath(item, itemType);
        string? canonicalPath = null;
        string? referenceOutputAssembly = null;
        string? outputItemType = null;
        IReadOnlyDictionary<string, string> projectReferenceGlobalProperties = _emptyGlobalProperties;
        if (string.Equals(itemType, "ProjectReference", StringComparison.Ordinal))
        {
            canonicalPath = CanonicalExistingFile(
                item.GetMetadataValue("FullPath"),
                $"ProjectReference '{identity}' defined by '{definingProjectPath}'");
            referenceOutputAssembly = item.GetMetadataValue("ReferenceOutputAssembly");
            outputItemType = item.GetMetadataValue("OutputItemType");
            projectReferenceGlobalProperties = EffectiveProjectReferenceGlobalProperties(
                item,
                evaluationGlobalProperties);
        }

        return new MsBuildEvaluatedItem(
            itemType,
            identity,
            canonicalPath,
            definingProjectPath,
            referenceOutputAssembly,
            outputItemType,
            projectReferenceGlobalProperties,
            item.GetMetadataValue("Version"),
            VersionProvenance(item, provenance),
            item.GetMetadataValue("HintPath"));
    }

    private static string EffectiveDefiningProjectPath(ProjectItem item, string itemType)
    {
        if (string.Equals(itemType, "PackageVersion", StringComparison.Ordinal))
        {
            ProjectMetadata? version = item.GetMetadata("Version");
            string? metadataProjectPath = version?.Xml?.ContainingProject?.FullPath;
            if (!string.IsNullOrWhiteSpace(metadataProjectPath))
            {
                return CanonicalExistingFile(metadataProjectPath, $"Version metadata for PackageVersion '{item.EvaluatedInclude}'");
            }
        }

        return CanonicalExistingFile(
            item.Xml.ContainingProject.FullPath,
            $"definition of {itemType} '{item.EvaluatedInclude}'");
    }

    private static IReadOnlyDictionary<string, string> EffectiveProjectReferenceGlobalProperties(
        ProjectItem item,
        IDictionary<string, string> evaluationGlobalProperties)
    {
        var properties = new Dictionary<string, string>(
            evaluationGlobalProperties,
            StringComparer.OrdinalIgnoreCase);

        // UndefineProperties is the ProjectReference spelling authors write; the common targets translate it
        // into GlobalPropertiesToRemove for the build. Honour both so the child closure is evaluated with the
        // same global properties the build uses.
        RemoveGlobalProperties(item, properties, "UndefineProperties");
        RemoveGlobalProperties(item, properties, "GlobalPropertiesToRemove");
        ApplyGlobalPropertyMetadata(item, "SetConfiguration", "Configuration", properties);
        ApplyGlobalPropertyMetadata(item, "SetPlatform", "Platform", properties);
        ApplyGlobalPropertyMetadata(item, "SetTargetFramework", "TargetFramework", properties);
        ApplyGlobalPropertyMetadata(item, "AdditionalPropertiesFromProject", defaultPropertyName: null, properties);
        ApplyGlobalPropertyMetadata(item, "AdditionalProperties", defaultPropertyName: null, properties);

        return new ReadOnlyDictionary<string, string>(properties);
    }

    private static void RemoveGlobalProperties(
        ProjectItem item,
        IDictionary<string, string> properties,
        string metadataName)
    {
        string metadata = item.GetMetadataValue(metadataName).Trim();
        if (metadata.Length == 0)
        {
            return;
        }

        string[] propertyNames = metadata.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (propertyNames.Length == 0)
        {
            throw new InvalidOperationException(
                $"ProjectReference '{item.EvaluatedInclude}' has unusable {metadataName} metadata '{metadata}'.");
        }

        foreach (string propertyName in propertyNames)
        {
            ValidateGlobalPropertyName(item, metadataName, propertyName);
            _ = properties.Remove(propertyName);
        }
    }

    private static void ApplyGlobalPropertyMetadata(
        ProjectItem item,
        string metadataName,
        string? defaultPropertyName,
        IDictionary<string, string> properties)
    {
        string metadata = item.GetMetadataValue(metadataName).Trim();
        if (metadata.Length == 0)
        {
            return;
        }

        if (defaultPropertyName is not null && !metadata.Contains('=', StringComparison.Ordinal))
        {
            ValidateGlobalPropertyValue(item, metadataName, metadata);
            properties[defaultPropertyName] = metadata;
            return;
        }

        string[] assignments = metadata.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (assignments.Length == 0)
        {
            throw new InvalidOperationException(
                $"ProjectReference '{item.EvaluatedInclude}' has unusable {metadataName} metadata '{metadata}'.");
        }

        foreach (string assignment in assignments)
        {
            int separator = assignment.IndexOf('=');
            if (separator <= 0)
            {
                throw new InvalidOperationException(
                    $"ProjectReference '{item.EvaluatedInclude}' has malformed {metadataName} assignment '{assignment}'.");
            }

            string propertyName = assignment[..separator].Trim();
            string propertyValue = assignment[(separator + 1)..].Trim();
            ValidateGlobalPropertyName(item, metadataName, propertyName);
            ValidateGlobalPropertyValue(item, metadataName, propertyValue);
            properties[propertyName] = propertyValue;
        }
    }

    private static void ValidateGlobalPropertyName(
        ProjectItem item,
        string metadataName,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName)
            || propertyName.Contains('=', StringComparison.Ordinal)
            || ContainsOpaqueExpression(propertyName))
        {
            throw new InvalidOperationException(
                $"ProjectReference '{item.EvaluatedInclude}' has malformed {metadataName} property name '{propertyName}'.");
        }
    }

    private static void ValidateGlobalPropertyValue(
        ProjectItem item,
        string metadataName,
        string propertyValue)
    {
        if (ContainsOpaqueExpression(propertyValue))
        {
            throw new InvalidOperationException(
                $"ProjectReference '{item.EvaluatedInclude}' has opaque {metadataName} property value '{propertyValue}'.");
        }
    }

    private static void ValidateDependencyDeclarations(Project project)
    {
        string owningProjectPath = CanonicalExistingFile(project.FullPath, "evaluated project");
        foreach (ProjectItemElement item in project.GetLogicalProject().OfType<ProjectItemElement>())
        {
            if (!TryGetCanonicalDependencyItemType(item.ItemType, out string itemType)
                || (!string.IsNullOrWhiteSpace(item.Remove) && string.IsNullOrWhiteSpace(item.Include))
                || (!string.IsNullOrWhiteSpace(item.Update) && string.IsNullOrWhiteSpace(item.Include)))
            {
                continue;
            }

            string definingPath = CanonicalExistingFile(
                item.ContainingProject.FullPath,
                $"definition of {itemType}");
            if (!IsCustomImportPath(definingPath, owningProjectPath))
            {
                continue;
            }

            string expandedInclude = project.ExpandString(item.Include).Trim();
            string[] identities = expandedInclude.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (expandedInclude.Length == 0 || identities.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{itemType} declaration in '{definingPath}' has an empty or unresolved Include '{item.Include}'.");
            }

            foreach (string identity in identities)
            {
                if (ContainsOpaqueExpression(identity) || identity.IndexOfAny(['*', '?']) >= 0)
                {
                    throw new InvalidOperationException(
                        $"{itemType} declaration '{item.Include}' in '{definingPath}' evaluated to opaque identity '{identity}'.");
                }
            }
        }
    }

    private static bool TryGetCanonicalDependencyItemType(string itemType, out string canonicalItemType)
    {
        canonicalItemType = _dependencyItemTypes.FirstOrDefault(candidate =>
            string.Equals(candidate, itemType, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return canonicalItemType.Length > 0;
    }

    private static string EvaluatedItemKey(MsBuildEvaluatedItem item)
    {
        string identity = item.CanonicalPath is null
            ? item.Identity.ToUpperInvariant()
            : CanonicalComparisonPath(item.CanonicalPath);
        string properties = string.Join(
            '\u001e',
            item.ProjectReferenceGlobalProperties
                .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
                .Select(property => $"{property.Key.ToUpperInvariant()}={property.Value}"));
        return string.Join(
            '\u001f',
            item.ItemType,
            identity,
            CanonicalComparisonPath(item.DefiningProjectPath),
            item.ReferenceOutputAssembly,
            item.OutputItemType,
            item.HintPath,
            properties,
            item.Version,
            string.Join(';', item.VersionProvenance.Select(source => $"{source.Name}={source.Value}@{source.DefiningProjectPath}:{source.Influence}")));
    }

    private static string CanonicalComparisonPath(string path)
        => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;

    private static bool ContainsOpaqueExpression(string value)
        => value.Contains("$(", StringComparison.Ordinal)
            || value.Contains("@(", StringComparison.Ordinal)
            || value.Contains("%(", StringComparison.Ordinal);

    private static bool IsCustomImportPath(string path, string owningProjectPath)
        => !IsUnderDirectory(path, MsBuildRuntime.ToolsPath)
            && !HasBuildOutputSegment(path, owningProjectPath);

    // Only segments below the closest directory that also contains the evaluated project can be that
    // project's build output. Walking every ancestor instead would let a checkout whose own path has a
    // 'bin' or 'obj' segment classify every custom import as generated, silently emptying the import
    // closure the freshness and declaration gates depend on.
    private static bool HasBuildOutputSegment(string path, string owningProjectPath)
    {
        string? ceiling = ClosestSharedDirectory(path, owningProjectPath);
        if (ceiling is null)
        {
            return false;
        }

        for (DirectoryInfo? directory = new FileInfo(path).Directory;
            directory is not null && !PathComparer.Equals(directory.FullName, ceiling);
            directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directory.Name, "obj", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ClosestSharedDirectory(string path, string owningProjectPath)
    {
        for (DirectoryInfo? candidate = new FileInfo(owningProjectPath).Directory;
            candidate is not null;
            candidate = candidate.Parent)
        {
            if (IsUnderDirectory(path, candidate.FullName))
            {
                return candidate.FullName;
            }
        }

        return null;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        string relativePath = Path.GetRelativePath(directory, path);
        return !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
    }

    private static string CanonicalExistingFile(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"The {description} has no usable path.");
        }

        string canonicalPath = Path.GetFullPath(path);
        if (!File.Exists(canonicalPath))
        {
            throw new InvalidOperationException($"The {description} path '{canonicalPath}' does not exist.");
        }

        // File links cannot establish project identity. Directory aliases are checked below each
        // trusted source root by the boundary guard, allowing workspace/temp aliases above that root.
        if ((canonicalPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || canonicalPath.EndsWith(".proj", StringComparison.OrdinalIgnoreCase))
            && new FileInfo(canonicalPath).LinkTarget is not null)
        {
            throw new InvalidOperationException($"The {description} path '{canonicalPath}' contains linked project path '{canonicalPath}'; an approved project must be a real file in its trusted source root.");
        }

        return canonicalPath;
    }

    // Replay the active construction/import order while taking values only from MSBuild's own
    // evaluated assignments. Import selectors are captured on entry, before an imported overwrite.
    private static IReadOnlyDictionary<string, IReadOnlyList<MsBuildPropertyProvenance>> PropertyProvenance(Project project)
    {
        var sources = new Dictionary<string, IReadOnlyList<MsBuildPropertyProvenance>>(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectProperty property in project.Properties.Where(property =>
            property.IsGlobalProperty || property.IsEnvironmentProperty || property.IsReservedProperty))
        {
            sources[property.Name] = [new(property.Name, property.EvaluatedValue,
                property.IsGlobalProperty ? "<global property>" : "<environment or reserved property>", "external assignment")];
        }

        ProjectProperty[] assignments = [.. project.AllEvaluatedProperties.Where(property => property.Xml is not null)];
        ResolvedImport[] imports = [.. project.Imports];
        var importedSelectors = new Dictionary<string, IReadOnlyList<MsBuildPropertyProvenance>>(PathComparer);
        int nextAssignment = 0;

        void VisitImport(ResolvedImport import, IReadOnlyList<MsBuildPropertyProvenance> inherited)
        {
            IReadOnlyList<MsBuildPropertyProvenance> selectors = inherited;
            if (IsCustomImportPath(import.ImportingElement.ContainingProject.FullPath, project.FullPath)
                && !IsSanctionedCatalogImport(import.ImportingElement))
            {
                selectors = [.. inherited, .. SelectorProvenance(import.ImportingElement, sources)];
            }

            VisitRoot(import.ImportedProject, selectors);
        }

        void VisitElement(ProjectElement element, IReadOnlyList<MsBuildPropertyProvenance> selectors)
        {
            if (element is ProjectImportElement importElement)
            {
                foreach (ResolvedImport import in imports.Where(import => ReferenceEquals(import.ImportingElement, importElement)))
                {
                    VisitImport(import, selectors);
                }
            }
            else if (element is ProjectPropertyElement
                && nextAssignment < assignments.Length && ReferenceEquals(assignments[nextAssignment].Xml, element))
            {
                ProjectProperty property = assignments[nextAssignment++];
                MsBuildPropertyProvenance[] dependencies = [.. ExpressionProvenance(property.UnevaluatedValue, sources),
                    .. SelectorProvenance(element, sources), .. selectors];
                sources[property.Name] = [new(property.Name, property.EvaluatedValue, property.Xml.ContainingProject.FullPath,
                    $"assignment '{property.UnevaluatedValue}'"), .. dependencies.Distinct()];
            }
            else if (element is ProjectElementContainer container && element is not ProjectTargetElement)
            {
                foreach (ProjectElement child in container.Children)
                {
                    VisitElement(child, selectors);
                }
            }
        }

        void VisitRoot(ProjectRootElement root, IReadOnlyList<MsBuildPropertyProvenance> selectors)
        {
            importedSelectors[root.FullPath] = selectors;
            foreach (ResolvedImport import in imports.Where(import => ReferenceEquals(import.ImportingElement.ContainingProject, root)
                && import.ImportingElement.ImplicitImportLocation == ImplicitImportLocation.Top))
            {
                VisitImport(import, selectors);
            }

            foreach (ProjectElement child in root.Children)
            {
                VisitElement(child, selectors);
            }

            foreach (ResolvedImport import in imports.Where(import => ReferenceEquals(import.ImportingElement.ContainingProject, root)
                && import.ImportingElement.ImplicitImportLocation == ImplicitImportLocation.Bottom))
            {
                VisitImport(import, selectors);
            }
        }

        VisitRoot(project.Xml, []);
        if (nextAssignment != assignments.Length)
        {
            throw new InvalidOperationException($"Cannot reconstruct complete property influence for '{project.FullPath}'; "
                + $"unmatched assignment '{assignments[nextAssignment].Name}' in '{assignments[nextAssignment].Xml.ContainingProject.FullPath}'.");
        }

        foreach ((string path, IReadOnlyList<MsBuildPropertyProvenance> selectors) in importedSelectors)
        {
            sources[ImportProvenanceKey(path)] = selectors;
        }

        return sources;
    }

    private static IReadOnlyList<MsBuildPropertyProvenance> VersionProvenance(
        ProjectItem item, IReadOnlyDictionary<string, IReadOnlyList<MsBuildPropertyProvenance>> sources)
    {
        ProjectMetadata? metadata = item.GetMetadata("Version");
        var provenance = new List<MsBuildPropertyProvenance>(ExpressionProvenance(metadata?.UnevaluatedValue ?? string.Empty, sources));
        foreach (ProjectElement element in new ProjectElement?[] { item.Xml, metadata?.Xml }.OfType<ProjectElement>())
        {
            provenance.AddRange(SelectorProvenance(element, sources));
            if (sources.TryGetValue(ImportProvenanceKey(element.ContainingProject.FullPath), out IReadOnlyList<MsBuildPropertyProvenance>? imports))
            {
                provenance.AddRange(imports);
            }
        }

        return [.. provenance.Distinct()];
    }

    private static IEnumerable<MsBuildPropertyProvenance> SelectorProvenance(
        ProjectElement element, IReadOnlyDictionary<string, IReadOnlyList<MsBuildPropertyProvenance>> sources)
    {
        for (ProjectElement? current = element; current is not null; current = current.Parent)
        {
            if (!string.IsNullOrWhiteSpace(current.Condition) && !IsSanctionedPackageModeSelection(current))
            {
                foreach (MsBuildPropertyProvenance source in ExpressionProvenance(current.Condition, sources, ignoreUndefined: true))
                {
                    yield return source with { Influence = $"{current.ElementName} condition '{current.Condition}' via {source.Influence}" };
                }
            }

            if (current is ProjectOtherwiseElement or ProjectWhenElement)
            {
                for (ProjectElement? preceding = current.PreviousSibling; preceding is not null; preceding = preceding.PreviousSibling)
                {
                    if (preceding is ProjectWhenElement)
                    {
                        foreach (MsBuildPropertyProvenance source in ExpressionProvenance(preceding.Condition, sources, ignoreUndefined: true))
                        {
                            yield return source with { Influence = $"preceding When condition '{preceding.Condition}' via {source.Influence}" };
                        }
                    }
                }
            }
        }
    }

    private static string ImportProvenanceKey(string path) => "\0import:" + path;

    // These existing repository gates select the approved catalog location and source/package lane;
    // they do not choose version values. Keep this allowance tied to their exact files and expressions.
    private static bool IsSanctionedCatalogImport(ProjectImportElement element)
        => PathComparer.Equals(element.ContainingProject.FullPath, Path.Combine(RepositoryRoot.Locate(), "Directory.Packages.props"))
            && element.Condition == (element.Project switch
            {
                "$(Hexalith1BuildPackageProps)" => "Exists('$(Hexalith1BuildPackageProps)') And '$(HexalithVersionsLoaded)' != 'true'",
                "$(Hexalith2BuildPackageProps)" => "Exists('$(Hexalith2BuildPackageProps)') And '$(HexalithVersionsLoaded)' != 'true'",
                "$(Hexalith3BuildPackageProps)" => "'$(HexalithVersionsLoaded)' != 'true'",
                _ => "<unrecognized catalog import>",
            });

    private static bool IsSanctionedPackageModeSelection(ProjectElement element)
        => element is ProjectItemElement { ItemType: "PackageVersion", Include: "Hexalith.EventStore.Contracts" }
            && PathComparer.Equals(element.ContainingProject.FullPath,
                Path.Combine(RepositoryRoot.DependencyRoot("Hexalith.Builds"), "Props", "Directory.Packages.props"))
            && element.Condition == "'$(UseHexalithProjectReferences)' != 'true' or !$(MSBuildProjectName.StartsWith('Hexalith.Works'))";

    private static IReadOnlyList<MsBuildPropertyProvenance> ExpressionProvenance(
        string expression,
        IReadOnlyDictionary<string, IReadOnlyList<MsBuildPropertyProvenance>> sources,
        bool ignoreUndefined = false)
    {
        var provenance = new List<MsBuildPropertyProvenance>();
        foreach (Match match in Regex.Matches(expression, @"\$\(([^()]+)\)"))
        {
            string name = match.Groups[1].Value;
            if (sources.TryGetValue(name, out IReadOnlyList<MsBuildPropertyProvenance>? values))
            {
                provenance.AddRange(values);
            }
            else if (!ignoreUndefined)
            {
                provenance.Add(new(name, string.Empty, "<unresolved property>", $"expression '{expression}'"));
            }
        }

        string remaining = Regex.Replace(expression, @"\$\(([^()]+)\)", string.Empty);
        if (ContainsOpaqueExpression(remaining))
        {
            provenance.Add(new("<unsupported expression>", expression, "<unresolved expression>", "unsupported expression"));
        }

        return [.. provenance.Distinct()];
    }
}
