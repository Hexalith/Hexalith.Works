namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>One bounded source-context value written only by the SDK fenced family writer.</summary>
/// <param name="Data">The exact serialized context fragment.</param>
internal sealed record WorksRuntimeHistoryPage(byte[] Data);
