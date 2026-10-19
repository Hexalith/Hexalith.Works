using System.Security.Cryptography;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Target-host selection never silently substitutes the retained rollback AppHost.</summary>
public sealed class WorksAppHostSelectionTests
{
    /// <summary>The unselected historical lane retains its original entry point.</summary>
    [Fact]
    public void UnselectedLaneRetainsWorksHost()
        => WorksAppHostSelection.ResolveEntryPoint(null, null, null).ShouldBe(typeof(Projects.Hexalith_Works_AppHost));

    /// <summary>An explicit but absent artifact is a hard failure.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("apphost.dll")]
    public void InvalidArtifactPathCannotFallBack(string path)
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(path, "hash", "/tmp"));

    /// <summary>The recorded artifact hash is mandatory even for an existing assembly.</summary>
    [Fact]
    public void MissingHashCannotFallBack()
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(typeof(WorksAppHostSelectionTests).Assembly.Location, null, "/tmp"));

    /// <summary>A changed assembly cannot execute under the verifier's earlier provenance.</summary>
    [Fact]
    public void ChangedArtifactIsRefused()
        => Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(typeof(WorksAppHostSelectionTests).Assembly.Location, new string('0', 64), "/tmp"));

    /// <summary>A correct hash does not turn a test or old-host assembly into the Platform file entry point.</summary>
    [Fact]
    public void NonPlatformAssemblyIsRefusedEvenWithItsCorrectHash()
    {
        string path = typeof(WorksAppHostSelectionTests).Assembly.Location;
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Should.Throw<InvalidOperationException>(() => WorksAppHostSelection.ResolveEntryPoint(path, hash, "/tmp"));
    }
}
