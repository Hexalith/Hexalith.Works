using System.Text.RegularExpressions;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using CommunityToolkit.Aspire.Hosting.Dapr;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Relaxes Platform Dapr app retries so a 1.97s process operation can finish after one catalog miss.</summary>
internal static class WorksPlatformResiliencyOverlay
{
    private static readonly Regex DefaultRetry = new(
        @"defaultRetry:\r?\n[ \t]*policy: constant\r?\n[ \t]*duration: 1s\r?\n[ \t]*maxRetries: 3\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex DefaultBreaker = new(
        @"defaultBreaker:\r?\n[ \t]*maxRequests: 1\r?\n[ \t]*interval: 10s\r?\n[ \t]*timeout: 30s\r?\n[ \t]*trip: consecutiveFailures > 3\b",
        RegexOptions.CultureInvariant);

    /// <summary>Replaces the composed resiliency document with a fixture copy that does not retry eventstore invokes.</summary>
    internal static void Apply(IDistributedApplicationBuilder builder, string componentPath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentPath);
        DaprComponentResource? component = builder.Resources.OfType<DaprComponentResource>()
            .SingleOrDefault(resource => resource.Name == "resiliency");
        if (component?.Options?.LocalPath is not { Length: > 0 } original) return;
        string source = File.Exists(original) ? original : Path.Combine(original, "resiliency.yaml");
        if (!File.Exists(source)) return;
        string directory = Path.Combine(Path.GetDirectoryName(componentPath)!, "sidecar-components", "resiliency");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string replacement = Path.Combine(directory, "resiliency.yaml");
        File.Copy(source, replacement, overwrite: true);
        if (!RelaxForProcessOperationBudget(replacement)) return;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(replacement, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        typeof(DaprComponentResource).GetProperty(nameof(DaprComponentResource.Options))!
            .SetValue(component, new DaprComponentOptions { LocalPath = replacement });
    }

    /// <summary>Returns false when the document is not the Platform app-retry resiliency (retained hosts keep theirs).</summary>
    internal static bool RelaxForProcessOperationBudget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string text = File.ReadAllText(path);
        if (!DefaultRetry.IsMatch(text) || !DefaultBreaker.IsMatch(text)) return false;
        string updated = DefaultRetry.Replace(text, match => match.Value.Replace("maxRetries: 3", "maxRetries: 0", StringComparison.Ordinal), 1);
        updated = DefaultBreaker.Replace(updated, match => match.Value
            .Replace("timeout: 30s", "timeout: 5s", StringComparison.Ordinal)
            .Replace("consecutiveFailures > 3", "consecutiveFailures > 20", StringComparison.Ordinal), 1);
        File.WriteAllText(path, updated);
        return true;
    }
}
