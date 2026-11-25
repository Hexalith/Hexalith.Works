using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using CommunityToolkit.Aspire.Hosting.Dapr;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Relaxes Platform Dapr app retries so a 1.97s process operation can finish after one catalog miss.</summary>
internal static class WorksPlatformResiliencyOverlay
{
    /// <summary>Replaces the composed resiliency document with a fixture copy that does not retry eventstore invokes.</summary>
    internal static void Apply(IDistributedApplicationBuilder builder, string componentPath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentPath);
        DaprComponentResource? component = builder.Resources.OfType<DaprComponentResource>()
            .SingleOrDefault(resource => resource.Name == "resiliency");
        if (component?.Options?.LocalPath is not { Length: > 0 } original) return;
        string source = File.Exists(original) ? original : Path.Combine(original, "resiliency.yaml");
        string directory = Path.Combine(Path.GetDirectoryName(componentPath)!, "sidecar-components", "resiliency");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string replacement = Path.Combine(directory, "resiliency.yaml");
        File.Copy(source, replacement, overwrite: true);
        RelaxForProcessOperationBudget(replacement);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(replacement, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        typeof(DaprComponentResource).GetProperty(nameof(DaprComponentResource.Options))!
            .SetValue(component, new DaprComponentOptions { LocalPath = replacement });
    }

    /// <summary>Dapr's 1s×3 retry plus 30s breaker open exceed the fixture process operation budget.</summary>
    internal static void RelaxForProcessOperationBudget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string text = File.ReadAllText(path);
        const string retry = """
      defaultRetry:
        policy: constant
        duration: 1s
        maxRetries: 3
""";
        const string retryReplacement = """
      defaultRetry:
        policy: constant
        duration: 1s
        maxRetries: 0
""";
        const string breaker = """
      defaultBreaker:
        maxRequests: 1
        interval: 10s
        timeout: 30s
        trip: consecutiveFailures > 3
""";
        const string breakerReplacement = """
      defaultBreaker:
        maxRequests: 1
        interval: 10s
        timeout: 5s
        trip: consecutiveFailures > 20
""";
        if (!text.Contains(retry, StringComparison.Ordinal) || !text.Contains(breaker, StringComparison.Ordinal))
            throw new InvalidOperationException("Platform resiliency.yaml no longer contains the fixture overlay anchors.");
        File.WriteAllText(path, text.Replace(retry, retryReplacement, StringComparison.Ordinal)
            .Replace(breaker, breakerReplacement, StringComparison.Ordinal));
    }
}
