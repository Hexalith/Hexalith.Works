namespace Hexalith.Works.Projections;

/// <summary>Contains the validated tenant indexes and documents read with them in one SDK snapshot.</summary>
/// <param name="Indexes">The reconstructed indexes.</param>
/// <param name="Documents">The SDK-selected documents, including the index roots.</param>
internal sealed record WorksRuntimeIndexView(
    WorksRuntimeIndexes Indexes,
    IReadOnlyDictionary<string, WorksRuntimeDocument?> Documents);
