namespace Hexalith.Works.IntegrationTests;

/// <summary>Reads one ordinary immutable manifest without allocating or parsing beyond the proof bound.</summary>
internal static class WorksManifestReader
{
    internal const int MaximumBytes = 16 * 1024 * 1024;

    internal static byte[] Read(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new InvalidOperationException("The proof manifest is not an ordinary file.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > MaximumBytes)
            throw new InvalidOperationException("The proof manifest exceeds the 16 MiB bound.");
        byte[] bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1)
            throw new InvalidOperationException("The proof manifest changed while being read.");
        return bytes;
    }
}
