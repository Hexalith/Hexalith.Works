using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Records, inside each live proof host, which hosting startups loaded and whether Platform's ran first.</summary>
internal static class WorksSdkProofHostingEvidence
{
    /// <summary>The per-resource evidence file the live harness assigns.</summary>
    internal const string PathVariable = "WORKS_PLATFORM_HOSTING_EVIDENCE";

    /// <summary>Platform's injected Works runtime assembly, composed by the Platform AppHost.</summary>
    internal const string PlatformRuntimeAssembly = "Hexalith.Platform.Works.Runtime";

    /// <summary>The telemetry publisher only Platform's hosting startup or the parity proof registers.</summary>
    internal const string PlatformTelemetryPublisher = "Hexalith.Platform.Works.Runtime.WorksRecoveryTelemetryPublisher";

    /// <summary>Whether Platform's Works recovery telemetry is registered.</summary>
    /// <param name="services">The host services composed so far.</param>
    /// <returns><see langword="true"/> when a publisher descriptor exists.</returns>
    internal static bool PlatformTelemetryRegistered(IServiceCollection services)
        => services.Any(static descriptor => descriptor.ServiceType.FullName == PlatformTelemetryPublisher);

    /// <summary>Writes the observed hosting composition when the harness assigned an evidence file.</summary>
    /// <param name="services">The host services after the proof registered its own.</param>
    /// <param name="platformTelemetryRegisteredBeforeProof">Whether Platform's hosting startup had already registered telemetry.</param>
    internal static void Write(IServiceCollection services, bool platformTelemetryRegisteredBeforeProof)
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is not { Length: > 0 } path) return;
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        string[] hostingStartupAssemblies = List("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", ';');
        object[] startups = [.. hostingStartupAssemblies.Select(name =>
        {
            Assembly? assembly = loaded.FirstOrDefault(candidate => !candidate.IsDynamic
                && string.Equals(candidate.GetName().Name, name, StringComparison.Ordinal));
            string? location = string.IsNullOrEmpty(assembly?.Location) ? null : assembly.Location;
            return (object)new
            {
                name, loaded = assembly is not null, location,
                sha256 = location is null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(location))),
                hostingStartupTypes = assembly?.GetCustomAttributes<HostingStartupAttribute>()
                    .Select(attribute => attribute.HostingStartupType.FullName).ToArray() ?? [],
            };
        })];
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            processId = Environment.ProcessId,
            hostingStartupAssemblies,
            startupHooks = List("DOTNET_STARTUP_HOOKS", Path.PathSeparator),
            startups,
            platformRuntimeEnabled = Environment.GetEnvironmentVariable("WORKS_PLATFORM_RUNTIME") == "1",
            platformTelemetryRegisteredBeforeProof,
            telemetryPublisherRegistrations = services.Count(static descriptor => descriptor.ServiceType.FullName == PlatformTelemetryPublisher),
            // The values this process actually received, compared by the verifier with Platform's composition.
            environment = Environment.GetEnvironmentVariables().Keys.OfType<string>()
                .Where(WorksPlatformTrackedEnvironment.IsTracked).Order(StringComparer.Ordinal)
                .ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal),
        }));
    }

    private static string[] List(string variable, char separator)
        => [.. (Environment.GetEnvironmentVariable(variable) ?? string.Empty)
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
