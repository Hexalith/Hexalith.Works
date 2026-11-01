using Hexalith.EventStore.Contracts.Authorization;
using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.Contracts.Serialization;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Models;
using System.Text.Json;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;

[assembly: RouteCatalogDeclarationSource(typeof(Hexalith.Works.Routing.WorksCatalogDeclarations))]

namespace Hexalith.Works.Routing;

/// <summary>Compiled inventory of the existing Works domain-service receivers and their owner policies.</summary>
public static class WorksCatalogDeclarations
{
    /// <summary>The existing command payload types, with their current short CLR wire names.</summary>
    public static IReadOnlyList<Type> CommandTypes { get; } = Array.AsReadOnly<Type>([
        typeof(CreateWorkItem), typeof(SpawnChild), typeof(AssignWorkItem), typeof(QueueWorkItem),
        typeof(ClaimWorkItem), typeof(SuspendWorkItem), typeof(ResumeWorkItem), typeof(CompleteWorkItem),
        typeof(ReportProgress), typeof(ReEstimate), typeof(RescheduleWorkItem), typeof(LinkConversation),
        typeof(CancelWorkItem), typeof(RejectWorkItem), typeof(ExpireWorkItem)]);

    /// <summary>Ordinary command admission retains its Mutation policy; trusted effects retain their independent SDK receipt lifecycle.</summary>
    public static IReadOnlyList<CommandIntentDeclaration> Commands { get; } = Array.AsReadOnly(
        CommandTypes.Select(Command).ToArray());

    /// <summary>Existing protected queries; their domain policy continues to decide coordinator and delegated human authority.</summary>
    public static IReadOnlyList<RouteContractDeclaration> Queries { get; } = Array.AsReadOnly<RouteContractDeclaration>([
        Query(GetWorkItemQueryHandler.GetWorkItemQueryType, typeof(GetWorkItemQueryHandler), WorksReadModelKeys.WorkItemViewProjectionType),
        Query(WhatsNextQueryHandler.WhatsNextQueryType, typeof(WhatsNextQueryHandler), WorksReadModelKeys.WhatsNextProjectionType)]);

    /// <summary>The existing named shared projection and its only owned output family.</summary>
    public static IReadOnlyList<RouteContractDeclaration> Projections { get; } = Array.AsReadOnly<RouteContractDeclaration>([
        new("work-runtime-projection", "projection", "work", null, WorksReadModelKeys.RuntimeFamily, "v1", null,
            "workload", EventStoreOperationIdentifiers.DomainServiceProject, null, [],
            new(typeof(WorkItemSharedProjectionRebuildHandler).FullName!, "exact", [WorksReadModelKeys.RuntimeFamily],
                "domain-service", null, [WorksReadModelKeys.RuntimeFamily]))]);

    /// <summary>Complete finite declaration inventory, independent of deployment configuration.</summary>
    public static IReadOnlyList<RouteContractDeclaration> Routes { get; } = Array.AsReadOnly<RouteContractDeclaration>([
        .. Commands.Select(command => command.Route), .. Queries, .. Projections]);

    /// <summary>The actual receiver method for each existing route.</summary>
    public static IReadOnlyDictionary<string, string> ReceiverMethods { get; } = Routes.ToDictionary(route => route.RouteId,
        route => route.Kind switch { "command" => "process", "query" => "query", _ => "project/v2" }, StringComparer.Ordinal);

    /// <summary>Existing gateway payloads remain public UI-only; the named projection remains an internal service operation.</summary>
    public static IReadOnlyList<RouteOperationContract> Operations { get; } = Array.AsReadOnly(Routes.Select(route =>
    {
        (Type request, Type result) = route.Kind switch
        {
            "command" => (CommandTypes.Single(type => type.FullName == route.Execution!.HandlerType), typeof(SubmitCommandResponse)),
            "query" => (typeof(WorksExecutorQueryParameters), route.MessageType == GetWorkItemQueryHandler.GetWorkItemQueryType
                ? typeof(WorkItemView) : typeof(IReadOnlyList<WhatsNextItem>)),
            _ => (typeof(ProjectionDispatchRequest), typeof(ProjectionDispatchResponse)),
        };
        return RouteOperationSchema.Create(route, request, result, route.Kind == "projection" ? "internal" : "public", "UI-only",
            route.Kind == "query" ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : EventStorePayloadSerialization.Options);
    }).ToArray());

    private static CommandIntentDeclaration Command(Type type)
    {
        string wire = type.Name;
        string id = string.Concat(wire.Select((character, index) => char.IsUpper(character) && index > 0
            ? "-" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
        return new(new("work-command-" + id, "command", "work", wire, null, "v1", 1, "workload",
            EventStoreOperationIdentifiers.DomainServiceProcess, null, [],
            new(type.FullName!, "clr-short-name-v1", [wire], "domain-service", null, [])),
            "hexalith:work:" + id + ":intent-v1", "work:" + id, "work:" + id + ":local-policy-v1",
            new("hexalith:work:" + id + ":descriptor-v1", 1, [new("target", "string"), new("payload-sha256", "string")]),
            IdempotencyReplayRetentionTier.Mutation, false);
    }

    private static RouteContractDeclaration Query(string wire, Type handler, string projectionType) => new("work-query-" + wire, "query", "work",
        wire, projectionType, "v1", null, "workload", EventStoreOperationIdentifiers.DomainServiceQuery,
        null, [], new(handler.FullName!, "exact", [wire], "domain-service", "projection", []));
}
