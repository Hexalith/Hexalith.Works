using Hexalith.EventStore.DomainService;
using Hexalith.Works.Contracts.Commands;

[assembly: RouteCatalogAdapterSource(typeof(Hexalith.Works.Routing.WorksCatalogAdapters))]

namespace Hexalith.Works.Routing;

/// <summary>Owner adapters bind the full existing command payload without placing payload values in catalog metadata.</summary>
public static class WorksCatalogAdapters
{
    /// <summary>Creates exactly one typed adapter for each existing command; no runtime authority is enabled here.</summary>
    public static IReadOnlyList<IDeclaredIdempotencyIntentAdapter> Create() => Array.AsReadOnly<IDeclaredIdempotencyIntentAdapter>([
        Adapter<CreateWorkItem>(), Adapter<SpawnChild>(), Adapter<AssignWorkItem>(), Adapter<QueueWorkItem>(),
        Adapter<ClaimWorkItem>(), Adapter<SuspendWorkItem>(), Adapter<ResumeWorkItem>(), Adapter<CompleteWorkItem>(),
        Adapter<ReportProgress>(), Adapter<ReEstimate>(), Adapter<RescheduleWorkItem>(), Adapter<LinkConversation>(),
        Adapter<CancelWorkItem>(), Adapter<RejectWorkItem>(), Adapter<ExpireWorkItem>()]);

    private static DeclaredIdempotencyIntentAdapter<T> Adapter<T>() => new(
        WorksCatalogDeclarations.Commands.Single(command => command.Route.Execution!.HandlerType == typeof(T).FullName),
        payload => new Dictionary<string, object?> { ["payload-sha256"] = CanonicalIntentPayload.Sha256(payload) },
        completePayloadFields: payload => new Dictionary<string, object?> { ["payload-sha256"] = CanonicalIntentPayload.Sha256CanonicalJson(payload) });
}
