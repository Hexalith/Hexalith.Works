using System.Runtime.Loader;

using Hexalith.EventStore.Contracts.Routing;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksAppHostRuntimeEvidenceTests
{
    [Fact]
    public void RestartInventoryExcludesDisposedMetadataAndRetainsActualRuntimeAssemblies()
    {
        string directory = Directory.CreateTempSubdirectory("works-runtime-evidence-").FullName;
        var context = new AssemblyLoadContext("works-disposable-metadata-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var runtime = typeof(RouteCatalogCodec).Assembly;
            string copy = Path.Combine(directory, Path.GetFileName(runtime.Location));
            File.Copy(runtime.Location, copy);
            var metadata = context.LoadFromAssemblyPath(copy);
            AppDomain.CurrentDomain.GetAssemblies().ShouldContain(metadata);
            File.Delete(copy);
            var actual = WorksAppHostSmokeHarness.RuntimeSdkAssemblies();
            actual.ShouldNotContain(metadata);
            actual.ShouldContain(runtime);
            // The original live assembly remains mandatory, readable and hashable. No
            // File.Exists filter may hide a missing SDK from this current runtime context.
            File.ReadAllBytes(runtime.Location).ShouldNotBeEmpty();
        }
        finally
        {
            context.Unload();
            Directory.Delete(directory, recursive: true);
        }
    }
}
