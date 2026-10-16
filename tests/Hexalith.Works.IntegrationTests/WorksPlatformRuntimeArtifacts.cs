namespace Hexalith.Works.IntegrationTests;

/// <summary>Resolves the exact qualified child deployment without sharing another host's build outputs.</summary>
internal static class WorksPlatformRuntimeArtifacts
{
    internal static string RequireMatchingConfigurations(string? consumer, string? host, string? retainedHost)
    {
        if (consumer is not ("Debug" or "Release") || host != consumer || retainedHost != consumer)
            throw new InvalidOperationException($"Qualified runtime configuration must match consumer, Platform host, and retained host: consumer={consumer ?? "missing"}, host={host ?? "missing"}, retained={retainedHost ?? "missing"}.");
        return host;
    }

    internal static string DirectoryFor(string project, string configuration)
    {
        string? artifacts = Environment.GetEnvironmentVariable("WORKS_PLATFORM_RUNTIME_ARTIFACTS");
        if (artifacts is null) return Path.Combine(Path.GetDirectoryName(project)!, "bin", configuration, "net10.0");
        if (!Path.IsPathFullyQualified(artifacts))
            throw new InvalidOperationException("Qualified runtime artifacts require an absolute owned output path.");
        return Path.Combine(artifacts, "bin", Path.GetFileNameWithoutExtension(project), configuration.ToLowerInvariant());
    }
}
