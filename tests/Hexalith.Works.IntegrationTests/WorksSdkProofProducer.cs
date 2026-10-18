using System.Reflection;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Binds synthetic child builds to the exact SDK version selected by the test consumer.</summary>
internal static class WorksSdkProofProducer
{
    /// <summary>Gets the version emitted from the evaluated consumer dependency property.</summary>
    internal static string Version => typeof(WorksSdkProofProducer).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "WorksSelectedEventStoreVersion").Value
        ?? throw new InvalidOperationException("The synthetic proof has no selected SDK producer version.");
}
