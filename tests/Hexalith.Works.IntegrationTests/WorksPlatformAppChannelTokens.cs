using System.Security.Cryptography;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using CommunityToolkit.Aspire.Hosting.Dapr;

using Hexalith.EventStore.Aspire;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Binds each synthetic receiver and its own sidecar to captured, restorable secret bytes.</summary>
internal static class WorksPlatformAppChannelTokens
{
    private static readonly string[] Receivers = ["eventstore", "eventstore-admin", "works", "works-ha", "eventstore-operations"];

    internal static string EnsureCaptured(string componentPath)
    {
        string path = Path.Combine(Path.GetDirectoryName(componentPath)!, "app-channel-tokens.json");
        if (!File.Exists(path))
        {
            var tokens = Receivers.ToDictionary(name => name, _ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), StringComparer.Ordinal);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(tokens);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var stream = new FileStream(path, options);
            stream.Write(bytes);
        }
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            throw new InvalidOperationException("Synthetic channel tokens require owner-only file permissions.");
        return path;
    }

    internal static void Configure(IDistributedApplicationTestingBuilder builder, string componentPath)
    {
        string path = EnsureCaptured(componentPath);
        Dictionary<string, string> tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path))
            ?? throw new InvalidOperationException("Captured synthetic channel tokens are unavailable.");
        if (!tokens.Keys.Order(StringComparer.Ordinal).SequenceEqual(Receivers.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || tokens.Values.Any(token => token.Length != 64 || !token.All(char.IsAsciiHexDigit))
            || tokens.Values.Distinct(StringComparer.Ordinal).Count() != Receivers.Length)
            throw new InvalidOperationException("Captured synthetic channel tokens do not match the explicit receiver set.");
        foreach (ProjectResource receiver in builder.Resources.OfType<ProjectResource>()
            .Where(resource => resource.Annotations.OfType<DaprSidecarAnnotation>().Any()).ToArray())
        {
            if (!tokens.TryGetValue(receiver.Name, out string? token))
                throw new InvalidOperationException("A synthetic receiver has no captured app-channel token.");
            IResourceBuilder<ParameterResource> parameter = builder.AddParameter("proof-" + receiver.Name + "-app-api-token", () => token, secret: true);
            builder.CreateResourceBuilder(receiver).WithEventStoreAppChannelToken(parameter);
        }
    }
}
