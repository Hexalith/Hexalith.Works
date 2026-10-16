using System.Reflection;
using System.Security.Cryptography;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Selects the actual compiled Platform file entry point for target-host conformance.</summary>
internal static class WorksAppHostSelection
{
    /// <summary>Whether this process was explicitly assigned the Platform target.</summary>
    internal static bool IsPlatform => Environment.GetEnvironmentVariable("WORKS_PLATFORM_APPHOST_ASSEMBLY") is not null;

    /// <summary>The persistent Scheduler volume owned by the selected host.</summary>
    internal static string SchedulerVolumeName => IsPlatform ? "hexalith-platform-dapr-scheduler" : "hexalith-works-dapr-scheduler";

    /// <summary>Returns the entry point, refusing a missing or changed Platform artifact without falling back.</summary>
    internal static Type EntryPoint()
        => ResolveEntryPoint(Environment.GetEnvironmentVariable("WORKS_PLATFORM_APPHOST_ASSEMBLY"),
            Environment.GetEnvironmentVariable("WORKS_PLATFORM_APPHOST_SHA256"),
            Environment.GetEnvironmentVariable("WORKS_PLATFORM_ROOT"));

    /// <summary>Resolves one explicit target without process-wide environment mutation in unit tests.</summary>
    internal static Type ResolveEntryPoint(string? path, string? expectedHash, string? expectedRoot)
    {
        if (path is null) return typeof(Projects.Hexalith_Works_AppHost);
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || string.IsNullOrWhiteSpace(expectedHash))
            throw new InvalidOperationException("Platform parity requires an absolute built apphost.dll and WORKS_PLATFORM_APPHOST_SHA256; fallback is forbidden.");
        string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Platform AppHost artifact changed after the verifier selected it.");
        Assembly assembly = Assembly.LoadFrom(path);
        string? projectName = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "apphostprojectname")?.Value;
        string? projectDirectory = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "apphostprojectpath")?.Value;
        if (projectName != "apphost.cs.csproj" || string.IsNullOrWhiteSpace(expectedRoot)
            || !Path.IsPathFullyQualified(expectedRoot) || projectDirectory != Path.GetFullPath(expectedRoot)
            || !File.Exists(Path.Combine(expectedRoot, "apphost.cs")))
            throw new InvalidOperationException("Platform parity requires the actual file AppHost built from WORKS_PLATFORM_ROOT.");
        return assembly.EntryPoint?.DeclaringType
            ?? throw new InvalidOperationException("The selected Platform assembly has no executable entry point.");
    }
}
