using Hexalith.EventStore.Client.Routing;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.Works.Routing;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Strict per-host committed authority for isolated HTTP security tests; distributed activation is verified by the Platform cohort.</summary>
internal sealed class WorksHttpCatalogFixture : IRouteCatalogAuthority
{
    private readonly Lazy<CommittedRouteCatalogSnapshot> _snapshot;

    private WorksHttpCatalogFixture(IServiceProvider services, string appId,
        IReadOnlyList<RouteContractDeclaration> declarations, IReadOnlyList<RouteOperationContract> operations,
        Func<IReadOnlyList<Endpoint>>? captureEndpoints)
        => _snapshot = new(() => Capture(services, appId, declarations, operations, captureEndpoints));

    private WorksHttpCatalogFixture(CommittedRouteCatalogSnapshot snapshot) => _snapshot = new(() => snapshot);

    internal static void RegisterIdentityGateway(IServiceCollection services, string tenant)
    {
        var declaration = WorksIdentityReaderCatalog.Declaration;
        var operation = WorksIdentityReaderCatalog.Operation;
        var envelope = RouteCatalogCodec.Seal(new(3, 1, "Development", false, [declaration],
            [new(declaration.RouteId, 1, RouteCatalogCodec.ComputeDeclarationDigest(declaration), "parties", "query", "v1",
                declaration.CredentialKind, declaration.Operation, false, tenant, operation.Digest)], [], string.Empty, string.Empty, [operation], []));
        services.AddSingleton<IRouteCatalogAuthority>(new WorksHttpCatalogFixture(new CommittedRouteCatalogSnapshot(
            RouteCatalogCodec.Encode(envelope), RouteCatalogCodec.ComputeRootDigest(envelope), 1)));
    }

    private static CommittedRouteCatalogSnapshot Capture(IServiceProvider services, string appId,
        IReadOnlyList<RouteContractDeclaration> declarations, IReadOnlyList<RouteOperationContract> operations,
        Func<IReadOnlyList<Endpoint>>? captureEndpoints)
    {
        using var scope = services.CreateScope();
        var local = scope.ServiceProvider;
        var endpoints = captureEndpoints?.Invoke() ?? services.GetRequiredService<EndpointDataSource>().Endpoints;
        var targets = DevelopmentDomainRouteCatalog.Targets(local, endpoints, declarations).Select(target => target with
            { OperationContractDigest = operations.Single(operation => operation.RouteId == target.RouteId).Digest }).ToArray();
        var commands = WorksCatalogDeclarations.Commands.Where(command => declarations.Any(route => route.RouteId == command.Route.RouteId));
        var envelope = RouteCatalogCodec.Seal(new(3, 1, "Development", false, declarations, targets,
            commands.Select(command => DevelopmentIdempotencyBindings.Bind(command, 1)).ToArray(), string.Empty, string.Empty, operations, []));
        return new(RouteCatalogCodec.Encode(envelope), RouteCatalogCodec.ComputeRootDigest(envelope), 1,
            RouteCatalogDomainObservation.Auxiliary(local, endpoints), RouteCatalogDomainObservation.Capabilities(local), appId);
    }

    internal static void Register(IServiceCollection services, string appId,
        IReadOnlyList<RouteContractDeclaration>? declarations = null, IReadOnlyList<RouteOperationContract>? operations = null,
        Func<IReadOnlyList<Endpoint>>? captureEndpoints = null)
    {
        services.RemoveAll<IRouteCatalogAuthority>();
        services.AddSingleton<IRouteCatalogAuthority>(provider => new WorksHttpCatalogFixture(provider, appId,
            declarations ?? WorksCatalogDeclarations.Routes, operations ?? WorksCatalogDeclarations.Operations, captureEndpoints));
        services.Configure<RouteCatalogHostOptions>(options => options.Identity = new(appId, appId,
            "isolated-http-fixture", "isolated-http-fixture", "isolated-http-fixture"));
    }

    internal static HttpClient Client(WebApplication app, string address) => new(new Caller(app.Services.GetRequiredService<IRouteCatalogAuthority>()))
        { BaseAddress = new Uri(address) };

    public Task<RouteCatalogDispatchLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RouteCatalogDispatchLease(_snapshot.Value, static () => Task.CompletedTask));
    }

    public Task<RouteCatalogGenerationReadback> ReadbackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _snapshot.Value;
        var envelope = snapshot.Envelope;
        return Task.FromResult(new RouteCatalogGenerationReadback(1, 1, snapshot.RootDigest, envelope.RouteFacetDigest,
            envelope.IdempotencyFacetDigest, envelope.Profile, [], true));
    }

    public Task PrepareAsync(RouteCatalogActivation candidate, CancellationToken cancellationToken = default) => Task.FromException(Immutable());
    public Task<RouteCatalogHostAttestation> ReadyAsync(string attemptId, CancellationToken cancellationToken = default) => Task.FromException<RouteCatalogHostAttestation>(Immutable());
    public Task CommitAsync(string attemptId, CancellationToken cancellationToken = default) => Task.FromException(Immutable());
    public Task RollbackAsync(RouteCatalogRollbackRequest request, CancellationToken cancellationToken = default) => Task.FromException(Immutable());
    public Task ReconcileAsync(RouteCatalogQuiescenceRequest request, CancellationToken cancellationToken = default) => Task.FromException(Immutable());

    private static RouteCatalogUnavailableException Immutable() => new("Isolated Works HTTP fixtures accept only their actual immutable receiver catalog.");

    private sealed class Caller(IRouteCatalogAuthority authority) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var lease = await authority.AcquireAsync(cancellationToken).ConfigureAwait(false);
            await using var release = lease.ConfigureAwait(false);
            if (!request.Headers.Contains(RouteCatalogInvocationHandler.RootHeader)) request.Headers.Add(RouteCatalogInvocationHandler.RootHeader, lease.Snapshot.RootDigest);
            if (!request.Headers.Contains(RouteCatalogInvocationHandler.EpochHeader)) request.Headers.Add(RouteCatalogInvocationHandler.EpochHeader,
                lease.Snapshot.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return await lease.InvokeRemoteAsync(async () =>
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                try { await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false); }
                catch { response.Dispose(); throw; }
                return response;
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
