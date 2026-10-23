using System.Reflection;
using System.Security.Cryptography;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Selects the actual compiled Platform file entry point for target-host conformance.</summary>
internal static class WorksAppHostSelection
{
    /// <summary>The run-owned control-plane host ports; container ports and trust identities remain unchanged.</summary>
    internal static (int Sentry, int Placement, int Scheduler) ControlPlanePorts => ResolveControlPlanePorts(
        Environment.GetEnvironmentVariable("WORKS_PLATFORM_SENTRY_PORT"),
        Environment.GetEnvironmentVariable("WORKS_PLATFORM_PLACEMENT_PORT"),
        Environment.GetEnvironmentVariable("WORKS_PLATFORM_SCHEDULER_PORT"));

    /// <summary>Accepts a complete, distinct unprivileged port selection, or the historical defaults.</summary>
    internal static (int Sentry, int Placement, int Scheduler) ResolveControlPlanePorts(string? sentry, string? placement, string? scheduler)
    {
        if (sentry is null && placement is null && scheduler is null) return (50001, 51005, 51006);
        int Parse(string? value)
            => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port)
                && port is >= 1024 and < 65535 ? port
                : throw new InvalidOperationException("A Platform proof requires three explicit unprivileged control-plane ports.");
        var selected = (Sentry: Parse(sentry), Placement: Parse(placement), Scheduler: Parse(scheduler));
        if (selected.Sentry == selected.Placement || selected.Sentry == selected.Scheduler || selected.Placement == selected.Scheduler)
            throw new InvalidOperationException("Platform proof control-plane ports must be distinct.");
        return selected;
    }

    /// <summary>Whether this process was explicitly assigned the Platform target.</summary>
    internal static bool IsPlatform => Environment.GetEnvironmentVariable("WORKS_PLATFORM_APPHOST_ASSEMBLY") is not null;

    /// <summary>The persistent Scheduler volume owned by the selected host, or null for Platform.</summary>
    /// <remarks>Platform binds each run's Scheduler to a private run directory, so only a proof volume can have owners.</remarks>
    internal static string? SchedulerVolumeName => IsPlatform ? null : "hexalith-works-dapr-scheduler";

    /// <summary>Returns the entry point, refusing a missing or changed Platform artifact without falling back.</summary>
    internal static Type EntryPoint()
        => ResolveEntryPoint(Environment.GetEnvironmentVariable("WORKS_PLATFORM_APPHOST_ASSEMBLY"),
            Environment.GetEnvironmentVariable("WORKS_PLATFORM_APPHOST_SHA256"),
            Environment.GetEnvironmentVariable("WORKS_PLATFORM_ROOT"));

    /// <summary>Resolves one explicit target without process-wide environment mutation in unit tests.</summary>
    internal static Type ResolveEntryPoint(string? path, string? expectedHash, string? expectedRoot)
    {
        if (path is null) throw new InvalidOperationException("Works hosting belongs to Platform. Run eng/verify-works-host.sh from Platform to select its built host.");
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
    /// <summary>The historical topology project selected separately from the current Works source.</summary>
    internal static string RetainedProjectPath => Path.Combine(
        Environment.GetEnvironmentVariable("WORKS_RETAINED_HOST_ROOT")
            ?? throw new InvalidOperationException("A separately qualified retained host source is required for rollback."),
        "src", "Hexalith.Works.AppHost", "Hexalith.Works.AppHost.csproj");

    /// <summary>Loads only the exact historical host the verifier built and bound for rollback.</summary>
    internal static Type RetainedEntryPoint()
    {
        string path = Environment.GetEnvironmentVariable("WORKS_RETAINED_HOST_ASSEMBLY")
            ?? throw new InvalidOperationException("A separately qualified retained host assembly is required for rollback.");
        string? expectedHash = Environment.GetEnvironmentVariable("WORKS_RETAINED_HOST_SHA256");
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || string.IsNullOrWhiteSpace(expectedHash)
            || !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The retained host is absent or differs from the rollback evidence.");
        Assembly assembly = Assembly.LoadFrom(path);
        string? project = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "apphostprojectpath")?.Value;
        if (project != Path.GetDirectoryName(RetainedProjectPath)
            || assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(attribute => attribute.Key == "apphostprojectname")?.Value != "Hexalith.Works.AppHost.csproj")
            throw new InvalidOperationException("The rollback assembly is not the separately selected retained Works host.");
        return assembly.EntryPoint?.DeclaringType ?? throw new InvalidOperationException("The retained host has no entry point.");
    }

}
