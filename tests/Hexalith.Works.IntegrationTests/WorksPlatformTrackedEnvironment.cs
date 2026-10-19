namespace Hexalith.Works.IntegrationTests;

/// <summary>The environment keys whose effective values a Platform start records and compares with Platform's composition.</summary>
/// <remarks>Kept free of Aspire types: the in-host proof hosting startup also reads it.</remarks>
internal static class WorksPlatformTrackedEnvironment
{
    /// <summary>Exact tracked keys.</summary>
    internal static readonly string[] Keys = ["DOTNET_ENVIRONMENT", "Works__Recovery__UseSdkRuntime", "Works__Projection__UseSdkDelivery"];

    /// <summary>Tracked key prefixes.</summary>
    internal static readonly string[] Prefixes = ["EventStore__RateLimiting__", "Authentication__Workload__AllowedCallers__"];

    /// <summary>Whether the verifier compares this key's effective value with Platform's composition.</summary>
    /// <param name="key">The environment key.</param>
    /// <returns><see langword="true"/> for a tracked key.</returns>
    internal static bool IsTracked(string key)
        => Keys.Contains(key, StringComparer.Ordinal) || Prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
}
