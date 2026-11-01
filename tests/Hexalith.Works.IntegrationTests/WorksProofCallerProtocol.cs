using System.Text.Json;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Closed test IPC vocabulary. It grants no HTTP endpoint or workload identity.</summary>
internal static class WorksProofCallerProtocol
{
    internal const string DirectoryKey = "WORKS_SDK_PROOF_CALLER_DIRECTORY";
    internal const string SessionKey = "WORKS_SDK_PROOF_CALLER_SESSION";
    internal const int MaximumBytes = 8 * 1024 * 1024;
    internal static readonly HttpRequestOptionsKey<bool> UseActualSender = new("WorksProof.ActualSdkSender");

    internal static string Method(string command) => command switch
    {
        "metadata" => "admin/operational-index-metadata",
        "query" => "query",
        "named-project" => "project/v2",
        "shared-rebuild" => "project/rebuild/shared/v1",
        _ => throw new InvalidOperationException("Unknown private SDK proof command."),
    };

    internal static string Command(string method) => method switch
    {
        "admin/operational-index-metadata" => "metadata",
        "query" => "query",
        "project/v2" => "named-project",
        "project/rebuild/shared/v1" => "shared-rebuild",
        _ => throw new InvalidOperationException("The private SDK proof caller does not expose that method."),
    };

    internal static void Validate(WorksProofCallerRequest request, string nonce, string session, DateTimeOffset now)
    {
        if (!Guid.TryParseExact(nonce, "N", out _) || request.Nonce != nonce || request.Session != session
            || request.ExpiresAt <= now || request.ExpiresAt > now.AddMinutes(2)
            || request.Payload.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The private SDK proof request is stale, foreign or malformed.");
        _ = Method(request.Command);
    }

    internal static void RequireDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidOperationException("A private absolute proof directory is required.");
        var selected = new DirectoryInfo(directory);
        if (!selected.Exists || !OperatingSystem.IsWindows() && (File.GetUnixFileMode(directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite
            | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("The proof directory must be owner-private.");
        for (DirectoryInfo? parent = selected; parent is not null; parent = parent.Parent)
            if (parent.LinkTarget is not null || (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The private proof path cannot traverse a link.");
    }

    internal static async Task<byte[]> ReadAsync(string path, CancellationToken token)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > MaximumBytes
            || !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("The private proof message is not a bounded regular private file.");
        return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
    }

    internal static async Task WriteNewAsync(string path, object value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumBytes) throw new InvalidOperationException("The private proof message exceeds its bound.");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        string temporary = path + ".writing";
        var stream = new FileStream(temporary, options);
        await using (stream.ConfigureAwait(false)) await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: false);
    }
}
