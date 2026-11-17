using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Proves that the running synthetic fixture was loaded from the child's ordinary dependency closure.</summary>
internal static class WorksDeclaredProofRuntime
{
    /// <summary>The explicit Platform configuration selecting the frozen proof build inputs.</summary>
    internal const string ManifestVariable = "Platform__Works__ProofRuntimeManifest";

    /// <summary>Reads and checks the running child's actual dependencies and the frozen proof source.</summary>
    /// <returns>The actual dependency and artifact identities, or null for the historical non-Platform fixture.</returns>
    internal static JsonObject? Capture()
    {
        if (Environment.GetEnvironmentVariable(ManifestVariable) is not { Length: > 0 } manifest) return null;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS")))
            throw new InvalidOperationException("Declared proof dependencies cannot use runtime startup hooks.");
        string entry = Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("The proof child has no actual entry assembly.");
        return Capture(manifest, Path.ChangeExtension(entry, ".deps.json"), typeof(WorksSdkProofHostingStartup).Assembly.Location);
    }

    /// <summary>Checks exact runtime dependency identities without loading any additional assembly.</summary>
    /// <param name="manifest">The frozen canonical proof input manifest.</param>
    /// <param name="dependencies">The actual child dependency file.</param>
    /// <param name="assembly">The proof assembly loaded inside that child.</param>
    /// <returns>Bound paths and SHA256 identities for the live verifier.</returns>
    internal static JsonObject Capture(string manifest, string dependencies, string assembly)
    {
        byte[] manifestBytes = WorksManifestReader.Read(manifest);
        string manifestHash = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
        using JsonDocument binding = JsonDocument.Parse(manifestBytes);
        JsonElement root = binding.RootElement;
        string original = root.GetProperty("assemblyPath").GetString()!;
        string expected = root.GetProperty("assemblySha256").GetString()!;
        string targets = root.GetProperty("targetsPath").GetString()!;
        string targetsHash = root.GetProperty("targetsSha256").GetString()!;
        using JsonDocument deps = JsonDocument.Parse(File.ReadAllBytes(dependencies));
        int declared = deps.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Where(library => library.Value.TryGetProperty("runtime", out _))
            .SelectMany(library => library.Value.GetProperty("runtime").EnumerateObject())
            .Count(asset => Path.GetFileName(asset.Name) == "Hexalith.Works.IntegrationTests.dll");
        if (root.GetProperty("schema").GetString() != "hexalith.works-proof-runtime.v1" || declared != 1
            || !root.GetProperty("files").TryGetProperty(original, out JsonElement bound) || bound.GetString() != expected
            || Path.GetFileName(assembly) != "Hexalith.Works.IntegrationTests.dll"
            || Path.GetDirectoryName(Path.GetFullPath(assembly)) != Path.GetDirectoryName(Path.GetFullPath(dependencies))
            || Digest(original) != expected || Digest(assembly) != expected || Digest(targets) != targetsHash)
            throw new InvalidOperationException("The running proof assembly is not the declared frozen runtime dependency.");
        if (Convert.ToHexStringLower(SHA256.HashData(WorksManifestReader.Read(manifest))) != manifestHash)
            throw new InvalidOperationException("The declared proof manifest changed during verification.");
        return new JsonObject
        {
            ["declared"] = true, ["dependenciesPath"] = dependencies, ["dependenciesSha256"] = Digest(dependencies),
            ["assemblyPath"] = assembly, ["assemblySha256"] = expected,
            ["manifestPath"] = manifest, ["manifestSha256"] = manifestHash,
            ["targetsPath"] = targets, ["targetsSha256"] = targetsHash,
        };
    }

    private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
