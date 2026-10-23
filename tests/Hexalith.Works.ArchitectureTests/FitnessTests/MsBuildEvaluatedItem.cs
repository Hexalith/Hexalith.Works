namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>
/// Describes one dependency item from the final evaluated MSBuild item set.
/// </summary>
/// <param name="ItemType">The evaluated MSBuild item type.</param>
/// <param name="Identity">The evaluated item identity.</param>
/// <param name="CanonicalPath">The canonical full path for path-backed items; otherwise <see langword="null"/>.</param>
/// <param name="DefiningProjectPath">The canonical project or import path that defined the effective item.</param>
/// <param name="ReferenceOutputAssembly">The evaluated ProjectReference output-assembly behavior, when applicable.</param>
/// <param name="OutputItemType">The evaluated ProjectReference output item type, when applicable.</param>
/// <param name="ProjectReferenceGlobalProperties">The effective child-project globals for a ProjectReference; otherwise an empty dictionary.</param>
/// <param name="Version">The evaluated version metadata for package items.</param>
/// <param name="VersionProvenance">The evaluation-time property assignments supplying the version expression, including aliases.</param>
/// <param name="HintPath">The evaluated assembly Reference hint path, if supplied.</param>
internal sealed record MsBuildEvaluatedItem(
    string ItemType,
    string Identity,
    string? CanonicalPath,
    string DefiningProjectPath,
    string? ReferenceOutputAssembly,
    string? OutputItemType,
    IReadOnlyDictionary<string, string> ProjectReferenceGlobalProperties,
    string Version,
    IReadOnlyList<MsBuildPropertyProvenance> VersionProvenance,
    string HintPath);
