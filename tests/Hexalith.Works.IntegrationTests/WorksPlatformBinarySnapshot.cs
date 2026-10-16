using System.Security.Cryptography;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Copies and restores the exact stopped synthetic fleet's runtime artifacts.</summary>
internal sealed class WorksPlatformBinarySnapshot : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "works-sdk416-binaries-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _copies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnixFileMode> _modes = new(StringComparer.Ordinal);

    private WorksPlatformBinarySnapshot(IReadOnlyDictionary<string, string> hashes) => Hashes = hashes;

    internal IReadOnlyDictionary<string, string> Hashes { get; }

    internal static WorksPlatformBinarySnapshot Capture(IReadOnlyDictionary<string, string> hashes)
    {
        var snapshot = new WorksPlatformBinarySnapshot(hashes);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(snapshot._directory);
        else Directory.CreateDirectory(snapshot._directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            foreach ((string source, string digest) in hashes)
            {
                string copy = Path.Combine(snapshot._directory, snapshot._copies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!OperatingSystem.IsWindows()) snapshot._modes.Add(source, File.GetUnixFileMode(source));
                File.Copy(source, copy);
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copy))) != digest)
                    throw new InvalidOperationException("A stopped fleet artifact changed during capture.");
                snapshot._copies.Add(source, copy);
            }
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    internal void Restore()
    {
        foreach ((string target, string copy) in _copies)
        {
            string replacement = target + ".works-sdk416-restore-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(copy, replacement);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(replacement, _modes[target]);
                // Atomic replacement preserves any loaded fixture/host assembly mapping while
                // restoring the on-disk deployment artifact from the captured bytes.
                File.Move(replacement, target, overwrite: true);
            }
            finally { if (File.Exists(replacement)) File.Delete(replacement); }
            if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(target) != _modes[target])
                throw new InvalidOperationException("Fleet artifact permissions did not match their captured mode.");
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) != Hashes[target])
                throw new InvalidOperationException("Fleet binary restore did not match its captured digest.");
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
