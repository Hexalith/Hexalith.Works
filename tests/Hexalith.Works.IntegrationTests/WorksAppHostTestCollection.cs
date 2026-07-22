namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Serializes full AppHost tests because every topology intentionally uses the same Dapr application ids.
/// </summary>
/// <remarks>
/// Concurrent topologies would advertise multiple <c>works</c> and <c>eventstore</c> instances through local
/// Dapr name resolution, allowing one test's sidecar to invoke another test's process.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorksAppHostTestCollection
{
    /// <summary>The xUnit collection name shared by live AppHost lanes.</summary>
    public const string Name = "Works AppHost live topology";
}
