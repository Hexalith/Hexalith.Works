using System.Collections.ObjectModel;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Represents one immutable, explicitly configured MSBuild project evaluation.
/// </summary>
internal sealed class MsBuildProjectSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MsBuildProjectSnapshot"/> class.
    /// </summary>
    /// <param name="projectPath">The canonical evaluated project path.</param>
    /// <param name="globalProperties">The effective global properties used for this snapshot.</param>
    /// <param name="items">The final evaluated dependency items.</param>
    /// <param name="importPaths">The resolved custom import closure, excluding installed-SDK and generated build-output imports.</param>
    /// <param name="properties">The evaluated values of the governed dependency-mode properties.</param>
    internal MsBuildProjectSnapshot(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        IEnumerable<MsBuildEvaluatedItem> items,
        IEnumerable<string> importPaths,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ProjectPath = projectPath;
        GlobalProperties = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(globalProperties, StringComparer.OrdinalIgnoreCase));
        Items = new ReadOnlyCollection<MsBuildEvaluatedItem>([.. items]);
        ImportPaths = new ReadOnlyCollection<string>([.. importPaths]);
        Properties = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                properties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the canonical evaluated project path.
    /// </summary>
    internal string ProjectPath { get; }

    /// <summary>
    /// Gets the effective global properties used to evaluate this snapshot.
    /// </summary>
    internal IReadOnlyDictionary<string, string> GlobalProperties { get; }

    /// <summary>
    /// Gets the final evaluated dependency items.
    /// </summary>
    internal IReadOnlyList<MsBuildEvaluatedItem> Items { get; }

    /// <summary>
    /// Gets every resolved direct and transitive custom import path. Installed-SDK imports and imports
    /// generated under the evaluated project's build output are excluded because they are not governed inputs.
    /// </summary>
    internal IReadOnlyList<string> ImportPaths { get; }

    /// <summary>
    /// Gets the evaluated values of the governed dependency-mode properties, such as the external
    /// Hexalith module roots. Absent or empty properties are simply missing from the dictionary.
    /// </summary>
    internal IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>
    /// Gets one evaluated governed property value, or an empty string when it was not set.
    /// </summary>
    /// <param name="name">The property name to read.</param>
    /// <returns>The evaluated value, or an empty string.</returns>
    internal string PropertyValue(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Properties.TryGetValue(name, out string? value) ? value : string.Empty;
    }

    /// <summary>
    /// Gets final evaluated items of one MSBuild item type.
    /// </summary>
    /// <param name="itemType">The item type to select.</param>
    /// <returns>The matching items, compared case-insensitively like MSBuild item types.</returns>
    internal IEnumerable<MsBuildEvaluatedItem> ItemsOfType(string itemType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemType);

        return Items.Where(item => string.Equals(item.ItemType, itemType, StringComparison.OrdinalIgnoreCase));
    }
}
