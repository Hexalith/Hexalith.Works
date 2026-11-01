using System.Security.Cryptography;
using System.Text.Json;

using Aspire.Hosting.ApplicationModel;

using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.ServiceDefaults.Routing;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Read-only structural diagnostics; raw authentication/component/backend documents never leave private custody.</summary>
internal static class WorksCatalogStartupDiagnostics
{
    internal static async Task<string?> CaptureAsync(ResourceEvent resource, CancellationToken token)
    {
        string? directory = resource.Snapshot.EnvironmentVariables.SingleOrDefault(variable => variable.Name == "EventStore__RouteCatalog__DevelopmentBootstrap__Directory")?.Value;
        string? host = resource.Snapshot.EnvironmentVariables.SingleOrDefault(variable => variable.Name == "EventStore__RouteCatalog__DevelopmentBootstrap__HostId")?.Value;
        if (directory is null || host is null) return null;
        string path = Path.Combine(directory, host + ".observation.json");
        if (!File.Exists(path)) return JsonSerializer.Serialize(new { host, observationPresent = false });
        var observation = JsonSerializer.Deserialize<RouteCatalogHostObservation>(await WorksProofCallerProtocol.ReadAsync(path, token).ConfigureAwait(false))!;
        var identity = ObservedRouteCatalogHost.Identity(observation);
        string activationPath = Path.Combine(directory, "activation.json");
        string? validation = null;
        if (File.Exists(activationPath))
        {
            var activation = JsonSerializer.Deserialize<RouteCatalogActivation>(await WorksProofCallerProtocol.ReadAsync(activationPath, token).ConfigureAwait(false))!;
            try
            {
                _ = await new ObservedRouteCatalogHost(_ => Task.FromResult(observation)).ObserveAsync(activation, observation.AuthorityCoordinates!, token).ConfigureAwait(false);
                validation = "validated";
            }
            catch (RouteCatalogUnavailableException exception) { validation = exception.Category + ":" + exception.Message; }
        }
        using var authentication = JsonDocument.Parse(observation.AuthenticationProfile);
        return JsonSerializer.Serialize(new
        {
            identity, activationPresent = File.Exists(activationPath), localValidation = validation,
            participants = observation.RequiredHostIds.Select(required => new { host = required,
                observed = File.Exists(Path.Combine(directory, required + ".observation.json")), ready = File.Exists(Path.Combine(directory, required + ".ready.json")),
                committed = File.Exists(Path.Combine(directory, required + ".committed.json")) }).ToArray(),
            inventory = new { declarations = Hash(observation.Declarations), targets = Hash(observation.Targets), admission = Hash(observation.AdmissionPolicies),
                capabilities = Hash(observation.ProjectionCapabilities), auxiliary = Hash(observation.AuxiliaryOperations), operations = Hash(observation.Operations) },
            authenticationFields = authentication.RootElement.EnumerateObject().ToDictionary(field => field.Name, field => Hash(field.Value)),
        });
    }

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
