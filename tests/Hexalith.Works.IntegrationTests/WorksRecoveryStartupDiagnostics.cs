using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Aspire.Hosting.ApplicationModel;

using Dapr.Client;

using Hexalith.EventStore.Client.Projections;
using Hexalith.Works.Projections;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Read-only bounded projection of existing recovery control state, never domain payloads or credentials.</summary>
internal static class WorksRecoveryStartupDiagnostics
{
    internal static async Task<string> CaptureAsync(ResourceEvent resource, CancellationToken token)
    {
        string Value(string name) => resource.Snapshot.EnvironmentVariables.Single(variable => variable.Name == name).Value!;
        using var dapr = new DaprClientBuilder().UseHttpEndpoint(Value("DAPR_HTTP_ENDPOINT")).UseGrpcEndpoint(Value("DAPR_GRPC_ENDPOINT")).Build();
        var store = new DaprReadModelStore(dapr);
        var reads = new Dictionary<string, object?>();
        async Task<JsonObject?> ReadAsync(string label, string key)
        {
            long started = Stopwatch.GetTimestamp();
            var entry = await store.GetAsync<JsonObject>("statestore", key, token).ConfigureAwait(false);
            reads[label] = new { milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds, state = Project(entry.Value) };
            return entry.Value;
        }
        const string registryKey = "eventstore:reminders:v1:WorksSdkReminderActor:control:tenants";
        _ = await ReadAsync("reminder-indexed", registryKey + ":indexed").ConfigureAwait(false);
        _ = await ReadAsync("reminder-catalog", registryKey + ":catalog").ConfigureAwait(false);
        _ = await ReadAsync("reminder-tenants", registryKey).ConfigureAwait(false);
        string controlKey = "shared-control-index:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WorksReadModelKeys.RuntimeControlIndex)));
        var control = await ReadAsync("projection-control-index", controlKey).ConfigureAwait(false);
        string[] tenants = Property(control, "tenantIds") is JsonArray array ? array.Take(4).Select(item => item!.GetValue<string>()).ToArray() : [];
        foreach (string tenant in tenants)
        {
            var scope = new SharedProjectionScope("statestore", tenant, "work", WorksReadModelKeys.RuntimeFamily, ["works-runtime-v3-writer"]);
            _ = await ReadAsync("family-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tenant))),
                "shared-epoch:" + scope.ComputeHash()).ConfigureAwait(false);
        }
        return JsonSerializer.Serialize(reads);
    }

    internal static IReadOnlyDictionary<string, object?> Project(JsonObject? value)
    {
        var result = new Dictionary<string, object?> { ["present"] = value is not null };
        if (value is null) return result;
        foreach (string key in new[] { "complete", "isIncomplete", "reasonCode", "generation", "revision", "epoch", "phase", "activeGeneration" })
            if (Property(value, key) is JsonValue scalar) result[key] = scalar.DeepClone();
        foreach (string key in new[] { "tenants", "tenantIds", "journal", "consumerCheckpoints", "scopeReceipts", "readinessIssues" })
        {
            JsonNode? collection = Property(value, key);
            if (collection is JsonArray list) result[key + "Count"] = list.Count;
            else if (collection is JsonObject map) result[key + "Count"] = map.Count;
        }
        if (Property(value, "afterKey") is JsonValue after && after.TryGetValue<string>(out string? text))
            result["afterKeySha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return result;
    }

    private static JsonNode? Property(JsonObject? value, string name)
        => value?.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
