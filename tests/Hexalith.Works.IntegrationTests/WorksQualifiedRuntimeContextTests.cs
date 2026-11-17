using System.Security.Cryptography;
using System.Text.Json;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>The qualified runtime remains exact through disposal, which always unloads its metadata context.</summary>
public sealed class WorksQualifiedRuntimeContextTests
{
    /// <summary>Changing any original context refuses at final verification and still releases metadata ownership.</summary>
    [Theory]
    [InlineData("entry")]
    [InlineData("source")]
    [InlineData("manifest")]
    [InlineData("extra")]
    [InlineData("oversized-manifest")]
    public void ChangedRuntimeStillUnloadsAtDisposal(string mutation)
    {
        string root = Directory.CreateTempSubdirectory("works-qualified-runtime-").FullName;
        try
        {
            string project = Path.Combine(root, "Hexalith.Works.csproj"); File.WriteAllText(project, "<Project />");
            string directory = Directory.CreateDirectory(Path.Combine(root, "deployment")).FullName;
            string entry = Path.Combine(directory, "Hexalith.Works.dll"); File.WriteAllText(entry, "frozen compiled body");
            string manifest = Path.Combine(root, "runtime.json");
            string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                schema = "hexalith.works-qualified-runtime.v1", projectPath = project, entryPath = entry, entrySha256 = Hash(entry),
                files = new Dictionary<string, string> { [entry] = Hash(entry) }, sourceFiles = new Dictionary<string, string> { [project] = Hash(project) },
            }));
            var context = new WorksQualifiedRuntimeContext(manifest, project, directory);
            bool unloaded = false; context.Unloading += _ => unloaded = true;
            context.Verify();
            switch (mutation)
            {
                case "entry": File.AppendAllText(entry, "changed"); break;
                case "source": File.AppendAllText(project, "changed"); break;
                case "manifest": File.AppendAllText(manifest, " "); break;
                case "oversized-manifest": using (var file = File.OpenWrite(manifest)) file.SetLength(WorksManifestReader.MaximumBytes + 1L); break;
                case "extra": File.WriteAllText(Path.Combine(directory, "Foreign.dll"), "foreign"); break;
            }
            Should.Throw<InvalidOperationException>(context.Dispose).Message.ShouldContain(mutation == "oversized-manifest" ? "16 MiB" : "changed");
            unloaded.ShouldBeTrue();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OversizedManifestRefusesBeforeJsonParsing()
    {
        string directory = Directory.CreateTempSubdirectory("works-oversized-runtime-").FullName;
        try
        {
            string manifest = Path.Combine(directory, "manifest.json");
            using (var file = File.Create(manifest)) file.SetLength(WorksManifestReader.MaximumBytes + 1L);
            Should.Throw<InvalidOperationException>(() => new WorksQualifiedRuntimeContext(manifest,
                Path.Combine(directory, "Host.csproj"), directory)).Message.ShouldContain("16 MiB");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

}
