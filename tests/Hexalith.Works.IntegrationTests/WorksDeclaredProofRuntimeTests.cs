using System.Security.Cryptography;
using System.Text.Json;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Declared proof transport rejects absent, changed and foreign dependency bodies.</summary>
public sealed class WorksDeclaredProofRuntimeTests
{
    /// <summary>The proof must be an ordinary runtime dependency with the exact frozen source body.</summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("undeclared")]
    [InlineData("changed")]
    [InlineData("targets")]
    [InlineData("foreign")]
    public void RuntimeDependencyMustMatchItsFrozenInput(string mutation)
    {
        string root = Directory.CreateTempSubdirectory("works-declared-proof-").FullName;
        try
        {
            string original = Path.Combine(root, "consumer", "Hexalith.Works.IntegrationTests.dll");
            string child = Path.Combine(root, "child", "Hexalith.Works.IntegrationTests.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(original)!);
            Directory.CreateDirectory(Path.GetDirectoryName(child)!);
            File.WriteAllText(original, "actual controlled assembly body");
            File.Copy(original, child);
            string targets = Path.Combine(root, "proof.targets");
            File.WriteAllText(targets, "controlled reference declaration");
            string manifest = Path.Combine(root, "manifest.json");
            string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                schema = "hexalith.works-proof-runtime.v1", targetsPath = targets, targetsSha256 = Digest(targets),
                assemblyPath = original, assemblySha256 = Digest(original), files = new Dictionary<string, string> { [original] = Digest(original) },
            }));
            string dependencies = Path.Combine(Path.GetDirectoryName(child)!, "Child.deps.json");
            File.WriteAllText(dependencies, JsonSerializer.Serialize(new
            {
                targets = new Dictionary<string, object> { ["net11"] = new Dictionary<string, object>
                {
                    ["Proof/1"] = new { runtime = mutation == "undeclared" ? new Dictionary<string, object>()
                        : new Dictionary<string, object> { ["Hexalith.Works.IntegrationTests.dll"] = new { } } },
                } },
            }));
            if (mutation == "changed") File.AppendAllText(child, "changed");
            if (mutation == "targets") File.AppendAllText(targets, "changed");
            if (mutation == "foreign") child = original;
            if (mutation == "valid")
                WorksDeclaredProofRuntime.Capture(manifest, dependencies, child)["declared"]!.GetValue<bool>().ShouldBeTrue();
            else
                Should.Throw<InvalidOperationException>(() => WorksDeclaredProofRuntime.Capture(manifest, dependencies, child));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
