namespace Hexalith.Works.ArchitectureTests.FitnessTests;

/// <summary>Identifies one effective assignment or selector influencing package version metadata.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Value">The value captured when this assignment was evaluated.</param>
/// <param name="DefiningProjectPath">The canonical assignment file, or an explicit non-file origin.</param>
/// <param name="Influence">The assignment or enclosing condition through which this property influences the version.</param>
internal sealed record MsBuildPropertyProvenance(string Name, string Value, string DefiningProjectPath, string Influence);
