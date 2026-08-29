using System.Collections.ObjectModel;
using System.Reflection;

using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Evaluates dependency-affecting MSBuild state with a fixed Release-lane configuration.
/// </summary>
internal static class MsBuildProjectEvaluation
{
    private static readonly string[] _dependencyItemTypes =
        ["ProjectReference", "PackageReference", "GlobalPackageReference", "PackageVersion", "FrameworkReference", "Reference"];

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
                ["Platform"] = "AnyCPU",
            });

    private static readonly Lazy<string> _msBuildToolsPath = new(ConfigureInstalledMsBuild);

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
        => TryEvaluate(projectPath, _releaseGlobalProperties, out snapshot, out diagnostic);

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(globalProperties);

        try
        {
            snapshot = Evaluate(projectPath, globalProperties);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            IOException or
            InvalidOperationException or
            InvalidProjectFileException or
            NotSupportedException or
            PathTooLongException or
            UnauthorizedAccessException)
        {
            snapshot = null;
            diagnostic = $"MSBuild evaluation of project '{projectPath}' failed: {exception.Message}";
            return false;
        }
    }

    private static MsBuildProjectSnapshot Evaluate(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties)
    {
        _ = _msBuildToolsPath.Value;
        string canonicalProjectPath = CanonicalExistingFile(projectPath, "project");
        var initialGlobalProperties = new Dictionary<string, string>(
            globalProperties,
            StringComparer.OrdinalIgnoreCase);
        using var projectCollection = new ProjectCollection();
        var projects = new List<Project>
        {
            LoadProject(projectCollection, canonicalProjectPath, initialGlobalProperties),
        };

        if (!initialGlobalProperties.ContainsKey("TargetFramework"))
        {
            foreach (string targetFramework in DeclaredTargetFrameworks(projects[0]))
            {
                var targetFrameworkProperties = new Dictionary<string, string>(
                    initialGlobalProperties,
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["TargetFramework"] = targetFramework,
                };
                projects.Add(LoadProject(projectCollection, canonicalProjectPath, targetFrameworkProperties));
            }
        }

        var items = new List<MsBuildEvaluatedItem>();
        var imports = new List<string>();
        foreach (Project project in projects)
        {
            ValidateDependencyDeclarations(project);
            items.AddRange(project.Items
                .Where(item => TryGetCanonicalDependencyItemType(item.ItemType, out _))
                .Select(item => EvaluatedItem(item, project.GlobalProperties)));
            imports.AddRange(project.ImportsIncludingDuplicates
                .Select(import => CanonicalExistingFile(
                    import.ImportedProject.FullPath,
                    $"import resolved by '{canonicalProjectPath}'"))
                .Where(import => IsCustomImportPath(import, canonicalProjectPath)));
        }

        return new MsBuildProjectSnapshot(
            canonicalProjectPath,
            initialGlobalProperties,
            items.DistinctBy(EvaluatedItemKey, StringComparer.Ordinal),
            imports.Distinct(PathComparer));
    }

    private static Project LoadProject(
        ProjectCollection projectCollection,
        string canonicalProjectPath,
        IReadOnlyDictionary<string, string> globalProperties)
        => new(
            canonicalProjectPath,
            new Dictionary<string, string>(globalProperties, StringComparer.OrdinalIgnoreCase),
            toolsVersion: null,
            projectCollection,
            ProjectLoadSettings.FailOnUnresolvedSdk
                | ProjectLoadSettings.RecordDuplicateButNotCircularImports
                | ProjectLoadSettings.RejectCircularImports);

    private static IEnumerable<string> DeclaredTargetFrameworks(Project project)
    {
        string declaration = project.GetPropertyValue("TargetFrameworks").Trim();
        if (declaration.Length == 0)
        {
            return [];
        }

        if (ContainsOpaqueExpression(declaration))
        {
            throw new InvalidOperationException(
                $"Project '{project.FullPath}' has an unusable target-framework declaration '{declaration}'.");
        }

        string[] targetFrameworks = declaration.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (targetFrameworks.Length == 0)
        {
            throw new InvalidOperationException(
                $"Project '{project.FullPath}' has an empty target-framework declaration '{declaration}'.");
        }

        return targetFrameworks.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static MsBuildEvaluatedItem EvaluatedItem(
        ProjectItem item,
        IDictionary<string, string> evaluationGlobalProperties)
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
            projectReferenceGlobalProperties);
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
            properties);
    }

    private static string CanonicalComparisonPath(string path)
        => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;

    private static bool ContainsOpaqueExpression(string value)
        => value.Contains("$(", StringComparison.Ordinal)
            || value.Contains("@(", StringComparison.Ordinal)
            || value.Contains("%(", StringComparison.Ordinal);

    private static bool IsCustomImportPath(string path, string owningProjectPath)
        => !IsUnderDirectory(path, _msBuildToolsPath.Value)
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

        return canonicalPath;
    }

    private static string ConfigureInstalledMsBuild()
    {
        string? toolsPath = typeof(MsBuildProjectEvaluation).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => string.Equals(
                attribute.Key,
                "MSBuildToolsPath",
                StringComparison.Ordinal))
            ?.Value;
        if (string.IsNullOrWhiteSpace(toolsPath))
        {
            throw new InvalidOperationException("The architecture-test assembly does not identify its installed MSBuild tools path.");
        }

        string canonicalToolsPath = Path.GetFullPath(toolsPath);
        string msBuildAssemblyPath = Path.Combine(canonicalToolsPath, "MSBuild.dll");
        string sdkPath = Path.Combine(canonicalToolsPath, "Sdks");
        if (!File.Exists(msBuildAssemblyPath) || !Directory.Exists(sdkPath))
        {
            throw new InvalidOperationException(
                $"Installed MSBuild surface '{canonicalToolsPath}' is incomplete; expected '{msBuildAssemblyPath}' and '{sdkPath}'.");
        }

        Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", msBuildAssemblyPath);
        Environment.SetEnvironmentVariable("MSBuildSDKsPath", sdkPath);
        return canonicalToolsPath;
    }
}
