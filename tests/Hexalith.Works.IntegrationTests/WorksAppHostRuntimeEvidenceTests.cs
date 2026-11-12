using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Routing;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

public sealed class WorksAppHostRuntimeEvidenceTests
{
    [Fact]
    public void ManagedHostInventoryMeasuresTheActualLoadedAssemblies()
    {
        JsonElement actual = WorksManagedRuntimeEvidence.CaptureHost();
        actual.GetProperty("pid").GetInt32().ShouldBe(Environment.ProcessId);
        actual.GetProperty("coreVersion").GetString().ShouldBe("11.0.0");
        Path.GetDirectoryName(typeof(JsonSerializer).Assembly.Location).ShouldBe(Path.GetDirectoryName(typeof(object).Assembly.Location));
        foreach ((string field, string path) in new[]
        {
            ("coreLib", typeof(object).Assembly.Location),
            ("json", typeof(JsonSerializer).Assembly.Location),
        })
        {
            actual.GetProperty(field).GetProperty("path").GetString().ShouldBe(path);
            actual.GetProperty(field).GetProperty("sha256").GetString()
                .ShouldBe(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }

    [Fact]
    public void LinuxProcessMappingsIndependentlyMatchTheActualHostAssemblyInventory()
    {
        if (!OperatingSystem.IsLinux()) return;
        JsonElement host = WorksManagedRuntimeEvidence.CaptureHost();
        JsonElement mapped = WorksManagedRuntimeEvidence.CaptureProcess(Environment.ProcessId);
        mapped.GetProperty("pid").GetInt32().ShouldBe(Environment.ProcessId);
        mapped.GetProperty("coreVersion").GetString().ShouldBe(host.GetProperty("coreVersion").GetString());
        foreach (string field in new[] { "coreLib", "json" })
            mapped.GetProperty(field).GetRawText().ShouldBe(host.GetProperty(field).GetRawText());
    }

    [Fact]
    public void MissingOrAmbiguousLoadedAssembliesRefuseRuntimeEvidence()
    {
        string core = typeof(object).Assembly.Location;
        string json = typeof(JsonSerializer).Assembly.Location;
        string Mapping(string path) => "1000-2000 r--p 00000000 00:00 1 " + path;
        Should.Throw<InvalidOperationException>(() => WorksManagedRuntimeEvidence.CaptureMappings(Environment.ProcessId, [Mapping(core)]));
        // Distinct CoreLib paths cannot be attributed to a single shared runtime merely because one is trusted.
        Should.Throw<InvalidOperationException>(() => WorksManagedRuntimeEvidence.CaptureMappings(Environment.ProcessId,
            [Mapping(core), Mapping(json), Mapping("/untrusted/System.Private.CoreLib.dll")]));
        Should.Throw<InvalidOperationException>(() => WorksManagedRuntimeEvidence.CaptureMappings(Environment.ProcessId,
            [Mapping(core), Mapping(json + " (deleted)")]));
    }

    [Fact]
    public void AServiceObservationCannotBeAttributedToTheQualificationHost()
    {
        if (!OperatingSystem.IsLinux()) return;
        Should.Throw<InvalidOperationException>(() => WorksManagedRuntimeEvidence.ResolveServicePid(
            Environment.ProcessId, typeof(WorksAppHostRuntimeEvidenceTests).Assembly.Location));
        Should.Throw<InvalidOperationException>(() => WorksManagedRuntimeEvidence.CaptureProcess(0));
    }

    [Theory]
    [InlineData("works", true)]
    [InlineData("works-ha", true)]
    [InlineData("eventstore", true)]
    [InlineData("eventstore-operations", true)]
    [InlineData("eventstore-admin", true)]
    [InlineData("foreign", false)]
    [InlineData("eventstore-dapr-cli", false)]
    public void ManagedRuntimeCaptureHasAFiniteServiceInventory(string resource, bool expected)
        => WorksManagedRuntimeEvidence.IsRuntimeResource(resource).ShouldBe(expected);

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
