namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Scopes Dapr name resolution for one live topology start to an owned SQLite registry.
/// </summary>
/// <remarks>
/// Self-hosted daprd defaults to mDNS, which discovers every local sidecar that shares an app ID, including
/// an unrelated session's <c>eventstore</c> or <c>works</c> application. Round-robin to that foreign sidecar
/// fails the mTLS handshake and, without mTLS, would deliver synthetic proof traffic to another host. Each start
/// therefore uses a derived configuration that keeps every committed line and adds only a run-owned
/// <c>nameResolution</c> block, mirroring the Builds composition renderer's isolated SQLite discovery.
/// </remarks>
internal static class WorksAppHostDiscoveryIsolation
{
    /// <summary>The prefix of each start's owned discovery directory.</summary>
    internal const string DirectoryPrefix = "works-sdk416-discovery-";

    /// <summary>The registry file shared by every sidecar of one topology start.</summary>
    internal const string RegistryFileName = "name-resolution.db";

    /// <summary>Returns the committed configuration with one isolated SQLite name-resolution block after <c>spec:</c>.</summary>
    /// <param name="committed">The exact Dapr Configuration document the sidecar would otherwise load.</param>
    /// <param name="registryPath">The absolute registry path owned by this topology start.</param>
    /// <returns>The derived configuration document.</returns>
    /// <exception cref="InvalidOperationException">The document already selects discovery or has an unexpected shape.</exception>
    internal static string Render(string committed, string registryPath)
    {
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        if (!Path.IsPathFullyQualified(registryPath) || registryPath.IndexOfAny(['"', '\\', '\r', '\n']) >= 0)
            throw new InvalidOperationException("The isolated Dapr name-resolution registry must be a plain absolute path.");
        string newline = committed.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = committed.Split(newline);
        int[] specs = [.. lines.Select((line, index) => (line, index)).Where(item => item.line.TrimEnd() == "spec:").Select(item => item.index)];
        if (specs.Length != 1)
            throw new InvalidOperationException("A Dapr configuration must contain exactly one top-level spec mapping to isolate discovery.");
        if (lines.Any(line => line.TrimStart().StartsWith("nameResolution:", StringComparison.Ordinal)))
            throw new InvalidOperationException("A Dapr configuration that already selects name resolution cannot be isolated silently.");
        string? first = lines.Skip(specs[0] + 1).FirstOrDefault(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#'));
        if (first is null || !first.StartsWith("  ", StringComparison.Ordinal) || first[2] == ' ')
            throw new InvalidOperationException("A Dapr configuration spec must use two-space mapping indentation to isolate discovery.");
        string[] block =
        [
            "  nameResolution:",
            "    component: \"sqlite\"",
            "    version: \"v1\"",
            "    configuration:",
            "      connectionString: \"" + registryPath + "\"",
            // Sidecars of one start initialize the fresh registry concurrently. Rollback journaling with a
            // bounded busy wait serializes those migrations; WAL's first journal-mode switch can fail immediately.
            "      busyTimeout: \"5s\"",
            "      timeout: \"2s\"",
            "      disableWAL: \"true\"",
        ];
        return string.Join(newline, [.. lines.Take(specs[0] + 1), .. block, .. lines.Skip(specs[0] + 1)]);
    }
}
