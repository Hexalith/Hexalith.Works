namespace Hexalith.Works.IntegrationTests;

/// <summary>Verifies payload bytes and modes, with one exact empty SDK lock requiring real exclusive ownership while running.</summary>
internal static class WorksCapturedConfigurationFile
{
    internal static async Task VerifyAsync(string path, byte[] expected, UnixFileMode? mode, bool exclusiveOwnerLock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || !OperatingSystem.IsWindows() && mode is { } captured && File.GetUnixFileMode(path) != captured)
            throw new InvalidOperationException("A restored captured file or its exact permissions changed.");
        if (exclusiveOwnerLock)
        {
            if (expected.Length != 0 || file.Length != 0) throw new InvalidOperationException("The exact SDK owner lock must remain an empty synchronization file.");
            try
            {
                // A shared read must be refused by the SDK's FileShare.None ownership handle.
                // .NET10 Linux maps EWOULDBLOCK to raw errno11; Windows uses sharing violation32.
                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException exception) when (IsSharingViolation(exception)) { return; }
            throw new InvalidOperationException("The running SDK does not exclusively own its retained custody lock.");
        }
        byte[] actual = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        if (!actual.AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("A running restored fleet changed captured configuration or credential bytes.");
    }

    internal static bool IsSharingViolation(IOException exception)
        => OperatingSystem.IsLinux() ? exception.HResult == 11 : OperatingSystem.IsWindows() && (exception.HResult & 0xffff) == 32;
}
