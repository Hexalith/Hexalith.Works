using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Runtime snapshots use the real canonical/retained artifacts and refuse every missing selected input.</summary>
public sealed class WorksPlatformRollbackBinaryTests
{
    /// <summary>Copies every actual selected runtime byte without replacing any frozen or current executable.</summary>
    [Fact(Explicit = true)]
    public void ActualSelectedRuntimeBinariesCaptureWithoutMutation()
    {
        if (!WorksAppHostSelection.IsPlatform) throw new InvalidOperationException("The binary preflight requires the actual verifier-selected artifacts.");
        var before = WorksPlatformRollbackChecks.RuntimeBinaryHashes();
        using (var copies = WorksPlatformBinarySnapshot.Capture(before)) copies.VerifyCopies();
        WorksPlatformRollbackChecks.RuntimeBinaryHashes().OrderBy(pair => pair.Key).ShouldBe(before.OrderBy(pair => pair.Key));
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } evidence)
            File.AppendAllText(evidence, System.Text.Json.JsonSerializer.Serialize(new { phase = "selected-runtime-binary-capture-preflight",
                files = before.Count, totalBytes = before.Keys.Sum(path => new FileInfo(path).Length),
                inventorySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(before.OrderBy(pair => pair.Key)))) }) + "\n");
    }

    [Fact]
    public void OwnedBinaryCopiesRestoreExactBytesAndExecutableModes()
    {
        string directory = Directory.CreateTempSubdirectory("works-binary-roundtrip-").FullName;
        try
        {
            string path = Path.Combine(directory, "owned-runtime"); File.WriteAllBytes(path, [1, 2, 3]);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var hashes = new Dictionary<string, string> { [path] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) };
            using var captured = WorksPlatformBinarySnapshot.Capture(hashes);
            captured.VerifyCopies(); File.WriteAllBytes(path, [9]); captured.Restore();
            File.ReadAllBytes(path).ShouldBe(new byte[] { 1, 2, 3 });
            if (!OperatingSystem.IsWindows()) File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing-current-directory")]
    [InlineData("missing-current-assembly")]
    [InlineData("missing-retained-assembly")]
    [InlineData("missing-platform-assembly")]
    [InlineData("foreign-required-assembly")]
    public void ActualSelectedHostsAreHashedWithoutInventingAnObsoleteProjectOutput(string fault)
    {
        string root = Directory.CreateTempSubdirectory("works-actual-binary-snapshot-").FullName;
        try
        {
            string current = Path.Combine(root, "current"), retained = Path.Combine(root, "archived"), platform = Path.Combine(root, "canonical-filehost");
            foreach (string directory in new[] { current, retained, platform }) Directory.CreateDirectory(directory);
            string currentAssembly = Path.Combine(current, "Hexalith.Works.dll"), retainedAssembly = Path.Combine(retained, "Hexalith.Works.AppHost.dll"), platformAssembly = Path.Combine(platform, "apphost.dll");
            string[] required = [currentAssembly, retainedAssembly, platformAssembly];
            foreach (string path in required) File.WriteAllBytes(path, [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(current, "Hexalith.EventStore.Client.dll"), [4, 5]);
            string unusedLegacyOutput = Path.Combine(root, "current-artifacts/Hexalith.Works.AppHost/debug");
            Directory.Exists(unusedLegacyOutput).ShouldBeFalse();
            if (fault == "foreign-required-assembly")
            {
                string foreign = Path.Combine(root, "foreign.dll"); File.WriteAllBytes(foreign, [9]); required = [.. required, foreign];
            }
            if (fault == "missing-current-directory") Directory.Delete(current, recursive: true);
            if (fault == "missing-current-assembly") File.Delete(currentAssembly);
            if (fault == "missing-retained-assembly") File.Delete(retainedAssembly);
            if (fault == "missing-platform-assembly") File.Delete(platformAssembly);
            IReadOnlyDictionary<string, string> Capture() => WorksPlatformRollbackChecks.HashActualBinaryDirectories([current], retainedAssembly, platformAssembly, required);
            if (fault == "valid")
            {
                var hashes = Capture(); hashes.Count.ShouldBe(4);
                foreach (string path in required) hashes.Keys.ShouldContain(path);
                hashes.Keys.ShouldContain(Path.Combine(current, "Hexalith.EventStore.Client.dll"));
                hashes.Values.ShouldAllBe(hash => hash.Length == 64);
                File.WriteAllBytes(currentAssembly, [9]); Capture()[currentAssembly].ShouldNotBe(hashes[currentAssembly]);
            }
            else if (fault == "foreign-required-assembly") Should.Throw<InvalidOperationException>(() => Capture());
            else Should.Throw<IOException>(() => Capture());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
