using System.Security.Cryptography;
using System.Text.Json;

using Hexalith.EventStore.Client.Routing;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.Works.Runtime;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Read-only evidence from the actual Works child's final registrations and committed authority.</summary>
internal sealed class WorksProofRuntimeInventoryService(IServiceProvider services, IConfiguration configuration, IHostApplicationLifetime lifetime,
    IRouteCatalogAuthority authority) : BackgroundService
{
    internal const string PathKey = "WORKS_SDK_PROOF_RUNTIME_INVENTORY";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
        string output = configuration[PathKey]!;
        WorksProofCallerProtocol.RequireDirectory(Path.GetDirectoryName(output)!);
        string host = configuration["EventStore:RouteCatalog:DevelopmentBootstrap:HostId"]!;
        string directory = configuration["EventStore:RouteCatalog:DevelopmentBootstrap:Directory"]!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        // This read-only observer follows the real AppHost lifetime/startup budget. It must
        // not terminate a participant on a shorter, independent activation deadline.
        RouteCatalogGenerationReadback generation;
        while (true)
        {
            try { generation = await authority.ReadbackAsync(deadline.Token).ConfigureAwait(false); if (generation.Serving) break; }
            catch (RouteCatalogUnavailableException) { }
            await Task.Delay(50, deadline.Token).ConfigureAwait(false);
        }
        byte[] observationBytes = await WorksProofCallerProtocol.ReadAsync(Path.Combine(directory, host + ".observation.json"), deadline.Token).ConfigureAwait(false);
        var observation = JsonSerializer.Deserialize<RouteCatalogHostObservation>(observationBytes)!;
        var assembly = typeof(WorksHost).Assembly;
        string digest = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(assembly.Location, deadline.Token).ConfigureAwait(false)));
        if (observation.HostId != host || observation.AppId != "works" || observation.ExecutableOwners?.GetValueOrDefault(assembly.FullName!) != digest)
            throw new InvalidOperationException("The actual Works runtime differs from its independently captured SDK owner observation.");
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var declarations = CompiledRouteCatalogDeclarations.Read([assembly]);
        var targets = DevelopmentDomainRouteCatalog.Targets(provider, services.GetRequiredService<EndpointDataSource>().Endpoints, declarations).ToArray();
        if (!JsonSerializer.SerializeToUtf8Bytes(targets.OrderBy(target => target.RouteId, StringComparer.Ordinal)).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(observation.Targets.OrderBy(target => target.RouteId, StringComparer.Ordinal))))
            throw new InvalidOperationException("The final Works handler targets differ from its actual SDK observation.");
        var evidence = new WorksProofRuntimeInventory(Environment.ProcessId, host, digest, Convert.ToHexStringLower(SHA256.HashData(observationBytes)),
            provider.GetServices<IDomainProjectionHandler>().Where(handler => handler.Domain == "work").Select(handler => handler.GetType().FullName!).Order(StringComparer.Ordinal).ToArray(),
            provider.GetServices<IAsyncDomainProjectionHandler>().Where(handler => handler.Domain == "work").Select(handler => handler.GetType().FullName! + ":" + handler.ProjectionType).Order(StringComparer.Ordinal).ToArray(), targets, generation);
        await WorksProofCallerProtocol.WriteNewAsync(output, evidence, deadline.Token).ConfigureAwait(false);
    }
}
