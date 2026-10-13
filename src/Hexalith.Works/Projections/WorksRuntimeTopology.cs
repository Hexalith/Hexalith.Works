namespace Hexalith.Works.Projections;

/// <summary>The tenant's exact direct-child relationships in one runtime generation.</summary>
/// <param name="ChildrenByParent">Direct children ordered by ordinal identity.</param>
public sealed record WorksRuntimeTopology(IReadOnlyDictionary<string, IReadOnlyList<string>> ChildrenByParent);
