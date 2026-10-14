namespace Hexalith.Works.Projections.SharedRebuild;

/// <summary>Bounded page inventory for one aggregate's ordered source context; not a domain catalog entry.</summary>
/// <param name="AggregateId">The source aggregate.</param>
/// <param name="PageCount">The exact number of immutable-value logical pages.</param>
/// <param name="ByteCount">The complete serialized byte count.</param>
/// <param name="Digest">The complete source-context SHA-256.</param>
internal sealed record WorksRuntimeHistoryManifest(string AggregateId, int PageCount, int ByteCount, string Digest);
