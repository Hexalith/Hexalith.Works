using System.Reflection;
using System.Text;
using System.Text.Json;

using Hexalith.EventStore.Contracts.Commands;
using Hexalith.EventStore.Contracts.Routing;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.ServiceDefaults.Routing;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;
using Hexalith.Works.Routing;
using Hexalith.Parties.Contracts.Queries;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Verifies that compiled catalog ownership covers the real Works receivers without widening their authority.</summary>
public sealed class WorksRoutingCatalogTests
{
    /// <summary>Fixture metadata uses the actual public DTO shape and matches reviewed compiled owner metadata when source-selected.</summary>
    [Fact]
    public void IdentityReaderFixtureMatchesPublicContractsAndReviewedOwnerDeclaration()
    {
        var declaration = WorksIdentityReaderCatalog.Declaration;
        var operation = WorksIdentityReaderCatalog.Operation;
        declaration.Execution!.HandlerType.ShouldBe(typeof(ResolvePartyIdentity).FullName);
        declaration.MessageType.ShouldBe(typeof(ResolvePartyIdentity).FullName);
        operation.RequestSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
            .ShouldBe(new[] { "expectedActorId", "partyId", "tenantId" });
        operation.Ingress.ShouldBe("internal");
        operation.Eligibility.ShouldBe("UI-only");
        Type? reviewed = typeof(ResolvePartyIdentity).Assembly.GetType("Hexalith.Parties.Contracts.Routing.PartiesCatalogDeclarations");
        if (reviewed is not null)
        {
            var routes = (IReadOnlyList<RouteContractDeclaration>)reviewed.GetProperty("Queries")!.GetValue(null)!;
            RouteCatalogCodec.ComputeDeclarationDigest(declaration).ShouldBe(RouteCatalogCodec.ComputeDeclarationDigest(
                routes.Single(route => route.MessageType == declaration.MessageType)));
            var operations = (IReadOnlyList<RouteOperationContract>)reviewed.GetProperty("Operations")!.GetValue(null)!;
            operation.Digest.ShouldBe(operations.Single(candidate => candidate.RouteId == declaration.RouteId).Digest);
        }
    }

    /// <summary>An added or removed aggregate handler must change the finite compiled command inventory.</summary>
    [Fact]
    public void EveryActualAggregateCommandHasExactlyOneDeclaredTypedAdapter()
    {
        Type[] actual = [.. typeof(WorkItemEventStoreAggregate).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "Handle").Select(method => method.GetParameters()[0].ParameterType)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
        WorksCatalogDeclarations.CommandTypes.OrderBy(type => type.FullName, StringComparer.Ordinal).ShouldBe(actual);
        var adapters = WorksCatalogAdapters.Create();
        adapters.Count.ShouldBe(actual.Length);
        foreach (Type type in actual)
        {
            var adapter = adapters.Single(item => item.Declaration.Route.Execution!.HandlerType == type.FullName);
            adapter.CommandType.ShouldBe(type.Name);
            adapter.Declaration.Route.Execution!.AliasPolicy.ShouldBe("clr-short-name-v1");
            adapter.Declaration.Route.Execution.Aliases.ShouldBe([type.Name]);
            adapter.Declaration.Route.Domain.ShouldBe("work");
            adapter.Declaration.Route.PlatformAdmission.ShouldBeNull();
            adapter.Declaration.RequiresAuthenticatedSubject.ShouldBeFalse();
            adapter.RetentionTier.ShouldBe(IdempotencyReplayRetentionTier.Mutation);
            RouteCatalogValidator.ValidateDeclaration(adapter.Declaration.Route);
            RouteCatalogValidator.ValidateDescriptor(adapter.Declaration.DescriptorSchema);
        }
    }

    /// <summary>Queries retain domain-owned coordinator/delegation checks and the named projection keeps its real output family.</summary>
    [Fact]
    public void CompiledOperationsPreserveExistingUiIngressAndInternalProjection()
    {
        var declarations = CompiledRouteCatalogDeclarations.Read([typeof(WorkItemEventStoreAggregate).Assembly]);
        declarations.Count.ShouldBe(18);
        declarations.Select(route => route.RouteId).Distinct(StringComparer.Ordinal).Count().ShouldBe(declarations.Count);
        WorksCatalogDeclarations.Queries.Select(route => route.MessageType).Order(StringComparer.Ordinal).ShouldBe(
            new[] { GetWorkItemQueryHandler.GetWorkItemQueryType, WhatsNextQueryHandler.WhatsNextQueryType }.Order(StringComparer.Ordinal));
        foreach (var query in WorksCatalogDeclarations.Queries)
        {
            query.Execution!.RequiresDelegatedContext.ShouldBeFalse();
            query.ProjectionType.ShouldBe(query.MessageType == GetWorkItemQueryHandler.GetWorkItemQueryType
                ? WorksReadModelKeys.WorkItemViewProjectionType : WorksReadModelKeys.WhatsNextProjectionType);
            query.Execution.QueryKind.ShouldBe("projection");
        }
        var projection = WorksCatalogDeclarations.Projections.Single();
        projection.Execution!.HandlerType.ShouldBe(typeof(WorkItemSharedProjectionRebuildHandler).FullName);
        projection.Execution.ProjectionOutputs.ShouldBe([WorksReadModelKeys.RuntimeFamily]);
        WorksCatalogDeclarations.ReceiverMethods[projection.RouteId].ShouldBe("project/v2");
        var operations = CompiledRouteCatalogDeclarations.ReadOperations([typeof(WorkItemEventStoreAggregate).Assembly]);
        operations.Select(operation => operation.RouteId).Order(StringComparer.Ordinal)
            .ShouldBe(declarations.Select(route => route.RouteId).Order(StringComparer.Ordinal));
        foreach (var operation in operations)
        {
            operation.Ingress.ShouldBe(operation.Kind == "projection" ? "internal" : "public");
            operation.Eligibility.ShouldBe("UI-only");
            operation.Digest.ShouldBe(RouteCatalogCodec.ComputeOperationContractDigest(operation));
        }
    }

    /// <summary>Trusted effect command semantics remain complete while ordinary admission keeps its separate Mutation tier.</summary>
    [Theory]
    [InlineData(nameof(ResumeWorkItem))]
    [InlineData(nameof(CancelWorkItem))]
    [InlineData(nameof(ExpireWorkItem))]
    public void EffectCommandIntentUsesFullCanonicalPayloadWithoutChangingReceiptRetention(string commandType)
    {
        var tenant = new TenantId("routing-tenant");
        var item = new WorkItemId("routing-work-item");
        object payload = commandType switch
        {
            nameof(ResumeWorkItem) => new ResumeWorkItem(tenant, item),
            nameof(CancelWorkItem) => new CancelWorkItem(tenant, item),
            _ => new ExpireWorkItem(tenant, item),
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var adapter = WorksCatalogAdapters.Create().Single(candidate => candidate.CommandType == commandType);
        var command = new IdempotencyIntentCommand(commandType, tenant.Value, "work", item.Value, bytes, null);
        var original = adapter.CreateIntent(command);
        using var parsed = JsonDocument.Parse(bytes);
        string reordered = "{" + string.Join(",", parsed.RootElement.EnumerateObject().Reverse()
            .Select(property => JsonSerializer.Serialize(property.Name) + ":" + property.Value.GetRawText())) + "}";
        adapter.CreateIntent(command with { Payload = Encoding.UTF8.GetBytes(reordered) }).SemanticPayload.ShouldBe(original.SemanticPayload);
        var additional = Encoding.UTF8.GetBytes(reordered[..^1] + ",\"futureSemanticMember\":true}");
        adapter.CreateIntent(command with { Payload = additional }).SemanticPayload.ShouldNotBe(original.SemanticPayload);
        adapter.CreateIntent(command with { AggregateId = "other-work-item" }).CanonicalTarget.ShouldNotBe(original.CanonicalTarget);
        adapter.RetentionTier.ShouldBe(IdempotencyReplayRetentionTier.Mutation);
    }
}
