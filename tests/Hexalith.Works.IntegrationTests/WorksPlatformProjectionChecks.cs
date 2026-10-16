using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.Contracts.Queries;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.ServiceDefaults.Authentication;
using Hexalith.Works.Projections;
using Hexalith.Works.Projections.SharedRebuild;
using Hexalith.Works.Queries;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Exercises actual target-host named ingress and complete fenced tenant rebuild over protected HTTP.</summary>
internal static class WorksPlatformProjectionChecks
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Captures the existing eleven-source fixture, proves redelivery/fencing, and verifies complete promotion.</summary>
    internal static async Task<object> VerifyAsync(DistributedApplication app, HttpClient gateway, IReadModelStore store,
        string tenant, string aggregateId, CancellationToken cancellationToken)
    {
        ResourceEvent worksResource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "works", cancellationToken, cancellationToken).ConfigureAwait(false);
        ResourceEvent gatewayResource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "eventstore", cancellationToken, cancellationToken).ConfigureAwait(false);
        using HttpClient gatewaySidecar = WorksAppHostTestReadiness.CreateDaprClient(gatewayResource, "EventStore");
        using HttpClient worksSidecar = WorksAppHostTestReadiness.CreateDaprClient(worksResource, "Works");
        string projectionAssertion = WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]);
        using HttpResponseMessage metadataResponse = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/admin/operational-index-metadata", new { },
            WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceMetadata]), cancellationToken).ConfigureAwait(false);
        metadataResponse.EnsureSuccessStatusCode();
        AdminOperationalIndexMetadata.Response metadata = (await metadataResponse.Content.ReadFromJsonAsync<AdminOperationalIndexMetadata.Response>(cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        metadata.AppId.ShouldBe("works");
        metadata.ServiceVersion.ShouldBe("v1");
        metadata.Domains.Single(domain => domain.Domain == "work").OrdinaryProjectionTypes.ShouldBe([WorksReadModelKeys.RuntimeFamily]);
        string catalog = metadata.CatalogFingerprint.ShouldNotBeNull();
        ProjectionDispatchRoute route = new("work", WorksReadModelKeys.RuntimeFamily);
        catalog.ShouldBe(ProjectionRouteCatalogFingerprint.Compute("works", "v1", [route], [route]));

        var checkpoints = new ProcessCheckpointStore(store, Options.Create(new EventStoreProcessOptions()), TimeProvider.System);
        int tail = (await checkpoints.ReadCandidateTailAsync(tenant, cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        List<ProcessSource> sources = [];
        for (int page = 0; page <= tail; page++)
        {
            ProcessIndexPage<ProcessSource> candidates = (await checkpoints.ReadCandidatePageAsync(tenant, page, cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
            candidates.Version.ShouldBe(1);
            candidates.Page.ShouldBe(page);
            candidates.NextPage.ShouldBe(page < tail ? page + 1 : null);
            sources.AddRange(candidates.Entries);
        }
        sources.Count.ShouldBe(11, "The existing supported fixture must remain complete without creating extra aggregates.");
        sources.Select(source => source.CatalogKey).Distinct(StringComparer.Ordinal).Count().ShouldBe(sources.Count);
        sources.ShouldAllBe(source => source.Tenant == tenant && source.Domain == "work");
        sources.ShouldContain(new ProcessSource(tenant, "work", aggregateId));

        var histories = new SortedDictionary<string, ProjectionEventDto[]>(StringComparer.Ordinal);
        foreach (ProcessSource source in sources)
            histories.Add(source.Aggregate, await ReadHistoryAsync(gateway, source, cancellationToken).ConfigureAwait(false));
        ProjectionEventDto[] history = histories[aggregateId];
        var request = new ProjectionDispatchRequest(new ProjectionRequest(tenant, "work", aggregateId, history),
            [WorksReadModelKeys.RuntimeFamily], "platform-redelivery-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), catalog);
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(tenant);
        var coordinator = new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store);
        var before = await SnapshotAsync(coordinator, scope, histories, cancellationToken).ConfigureAwait(false);

        ProjectionDispatchResponse redelivered = await DispatchAsync(gatewaySidecar, request, projectionAssertion, cancellationToken).ConfigureAwait(false);
        redelivered.Outcomes.ShouldHaveSingleItem().Status.ShouldBeOneOf(ProjectionDispatchStatus.Completed, ProjectionDispatchStatus.AlreadyCompleted);
        var afterRedelivery = await SnapshotAsync(coordinator, scope, histories, cancellationToken).ConfigureAwait(false);
        afterRedelivery.Digest.ShouldBe(before.Digest);
        afterRedelivery.Generation.ShouldBe(before.Generation);
        afterRedelivery.Checkpoints.ShouldBe(before.Checkpoints);

        // A permitted workload on its own sidecar still cannot impersonate the gateway caller.
        using HttpResponseMessage wrongOrigin = await WorksPlatformProofRequests.PostAsync(worksSidecar, "works", "/project/v2", request,
            projectionAssertion, cancellationToken).ConfigureAwait(false);
        wrongOrigin.IsSuccessStatusCode.ShouldBeFalse();
        wrongOrigin.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        using HttpResponseMessage wrongOperation = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/project/v2", request,
            WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceQuery]), cancellationToken).ConfigureAwait(false);
        wrongOperation.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ProjectionDispatchRequest invalidSequence = request with
        {
            DispatchId = request.DispatchId + "-zero-sequence",
            Request = request.Request with { Events = [history[0] with { SequenceNumber = 0 }] },
        };
        using HttpResponseMessage invalidSequenceResponse = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/project/v2",
            invalidSequence, projectionAssertion, cancellationToken).ConfigureAwait(false);
        int invalidSequenceStatus = (int)invalidSequenceResponse.StatusCode;
        if (invalidSequenceResponse.IsSuccessStatusCode)
        {
            ProjectionDispatchResponse refused = (await invalidSequenceResponse.Content.ReadFromJsonAsync<ProjectionDispatchResponse>(cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
            refused.Outcomes.ShouldHaveSingleItem().Status.ShouldBeOneOf(ProjectionDispatchStatus.Retryable, ProjectionDispatchStatus.Failed);
        }
        else invalidSequenceResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var afterRefusals = await SnapshotAsync(coordinator, scope, histories, cancellationToken).ConfigureAwait(false);
        afterRefusals.Digest.ShouldBe(before.Digest);
        afterRefusals.Checkpoints.ShouldBe(before.Checkpoints);

        string inventory = DomainSharedProjectionRebuildFingerprint.EmptyInventory;
        long ordinal = 0;
        foreach ((string owner, ProjectionEventDto[] events) in histories)
            inventory = DomainSharedProjectionRebuildFingerprint.AppendInventory(inventory, ordinal++, DomainSharedProjectionRebuildFingerprint.ComputeHistory(owner, false, events));
        var identity = new DomainSharedProjectionRebuildIdentity(tenant, "work", WorksReadModelKeys.RuntimeFamily,
            "platform-rebuild-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), catalog);
        DomainSharedProjectionRebuildRequest Lifecycle(DomainSharedProjectionRebuildAction action) => new(DomainSharedProjectionRebuildProtocol.Version, action, identity);
        DomainSharedProjectionRebuildResponse begun = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Begin) with
        {
            CaptureInventoryFingerprint = inventory,
            SourceHighWatermarks = histories.ToDictionary(pair => pair.Key, pair => pair.Value[^1].SequenceNumber, StringComparer.Ordinal),
        }, projectionAssertion, cancellationToken).ConfigureAwait(false);
        begun.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Accumulating);
        var captured = await SnapshotAsync(coordinator, scope, histories, cancellationToken, expectStale: true).ConfigureAwait(false);
        captured.Digest.ShouldBe(before.Digest);
        captured.Generation.ShouldBe(before.Generation);
        QueryResult staleQuery = await QueryAsync(gatewaySidecar, tenant, aggregateId, cancellationToken).ConfigureAwait(false);
        staleQuery.Success.ShouldBeTrue(staleQuery.ErrorMessage);
        staleQuery.Metadata.ShouldNotBeNull().IsStale.ShouldBe(true);

        ProjectionDispatchResponse fenced = await DispatchAsync(gatewaySidecar, request with { DispatchId = request.DispatchId + "-capturing" }, projectionAssertion, cancellationToken).ConfigureAwait(false);
        fenced.Outcomes.ShouldHaveSingleItem().Status.ShouldBe(ProjectionDispatchStatus.Retryable);
        var afterFence = await SnapshotAsync(coordinator, scope, histories, cancellationToken, expectStale: true).ConfigureAwait(false);
        afterFence.Digest.ShouldBe(before.Digest);
        afterFence.Checkpoints.ShouldBe(before.Checkpoints);

        ordinal = 0;
        DomainSharedProjectionRebuildResponse accumulated = begun;
        foreach ((string owner, ProjectionEventDto[] events) in histories)
            accumulated = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Accumulate) with
            {
                AggregateOrdinal = ordinal++, AggregateId = owner, Events = events,
            }, projectionAssertion, cancellationToken).ConfigureAwait(false);
        accumulated.AcceptedAggregateCount.ShouldBe(histories.Count);
        accumulated.InventoryFingerprint.ShouldBe(inventory);
        DomainSharedProjectionRebuildResponse finalized = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Finalize) with
        {
            ExpectedAggregateCount = histories.Count, ExpectedInventoryFingerprint = inventory,
        }, projectionAssertion, cancellationToken).ConfigureAwait(false);
        finalized.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Finalized);
        DomainSharedProjectionRebuildResponse staged = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Stage), projectionAssertion, cancellationToken).ConfigureAwait(false);
        staged.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Prepared);
        (await SnapshotAsync(coordinator, scope, histories, cancellationToken, expectStale: true).ConfigureAwait(false)).Digest.ShouldBe(before.Digest);
        DomainSharedProjectionRebuildResponse committed = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Commit), projectionAssertion, cancellationToken).ConfigureAwait(false);
        committed.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
        DomainSharedProjectionRebuildResponse verified = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Verify), projectionAssertion, cancellationToken).ConfigureAwait(false);
        verified.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
        verified.BatchFingerprint.ShouldBe(committed.BatchFingerprint);
        var after = await SnapshotAsync(new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store), scope, histories, cancellationToken).ConfigureAwait(false);
        after.Generation.ShouldBeGreaterThan(before.Generation);
        after.Digest.ShouldBe(before.Digest, "Capture-through-Commit must preserve the complete authoritative logical family.");
        after.Checkpoints.ShouldBe(before.Checkpoints);
        QueryResult currentQuery = await QueryAsync(gatewaySidecar, tenant, aggregateId, cancellationToken).ConfigureAwait(false);
        currentQuery.Success.ShouldBeTrue(currentQuery.ErrorMessage);
        currentQuery.Metadata.ShouldNotBeNull().IsStale.ShouldBe(false);
        currentQuery.GetPayload().ToString().ShouldContain(aggregateId);
        (await DispatchAsync(gatewaySidecar, request with { DispatchId = request.DispatchId + "-after-commit" }, projectionAssertion, cancellationToken).ConfigureAwait(false))
            .Outcomes.ShouldHaveSingleItem().Status.ShouldBeOneOf(ProjectionDispatchStatus.Completed, ProjectionDispatchStatus.AlreadyCompleted);
        (await SnapshotAsync(coordinator, scope, histories, cancellationToken).ConfigureAwait(false)).Digest.ShouldBe(after.Digest);
        return new { catalog, sources = histories.Keys.ToArray(), sourceGlobalPositions = after.Checkpoints, sourceHeads = histories.ToDictionary(pair => pair.Key, pair => pair.Value[^1].SequenceNumber, StringComparer.Ordinal), redelivered, fenced,
            negatives = new { wrongOrigin = (int)wrongOrigin.StatusCode, wrongOperation = (int)wrongOperation.StatusCode, invalidSequence = invalidSequenceStatus },
            before = new { before.Generation, before.Digest }, after = new { after.Generation, after.Digest }, inventory,
            begun, accumulated, finalized, staged, committed, verified, staleQuery = staleQuery.Metadata, currentQuery = currentQuery.Metadata };
    }

    private static async Task<ProjectionEventDto[]> ReadHistoryAsync(HttpClient gateway, ProcessSource source, CancellationToken cancellationToken)
    {
        List<ProjectionEventDto> events = [];
        long from = 0;
        for (int pageIndex = 0; pageIndex < 32; pageIndex++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/streams/read")
            {
                Content = JsonContent.Create(new { tenant = source.Tenant, domain = source.Domain, aggregateId = source.Aggregate, fromSequence = from, pageSize = 100 }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WorksAppHostSmokeHarness.MintToken(source.Tenant));
            using HttpResponseMessage response = await gateway.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            StreamReadPage page = (await response.Content.ReadFromJsonAsync<StreamReadPage>(cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
            page.Tenant.ShouldBe(source.Tenant);
            page.Domain.ShouldBe(source.Domain);
            page.AggregateId.ShouldBe(source.Aggregate);
            page.Metadata.FromSequence.ShouldBe(from);
            foreach (StreamReadEvent item in page.Events)
            {
                item.SequenceNumber.ShouldBe(events.Count + 1L);
                item.GlobalPosition.ShouldBeGreaterThan(0);
                events.Add(new ProjectionEventDto(item.EventTypeName, item.Payload, item.SerializationFormat, item.SequenceNumber,
                    item.Timestamp, item.CorrelationId ?? item.MessageId, item.MessageId, item.UserId, item.GlobalPosition)
                { MetadataVersion = item.MetadataVersion, StoredPayloadVersion = item.PayloadVersion });
            }
            if (!page.Metadata.IsTruncated)
            {
                events.Count.ShouldBeGreaterThan(0);
                events[^1].SequenceNumber.ShouldBe(page.Metadata.LatestSequence);
                return events.ToArray();
            }
            long next = page.Metadata.LastSequenceReturned.ShouldNotBeNull();
            next.ShouldBeGreaterThan(from);
            from = next;
        }
        throw new InvalidOperationException("The bounded synthetic stream history did not finish.");
    }

    private static async Task<(long Generation, string Digest, IReadOnlyDictionary<string, long> Checkpoints)> SnapshotAsync(
        SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope, IReadOnlyDictionary<string, ProjectionEventDto[]> histories,
        CancellationToken cancellationToken, bool expectStale = false)
    {
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
            .. histories.Keys.SelectMany(owner => new[] { WorksReadModelKeys.RuntimeRollUpKey(owner), WorksReadModelKeys.RuntimeHistoryKey(owner), WorksReadModelKeys.RuntimeParkingKey(owner) })];
        var family = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, cancellationToken).ConfigureAwait(false);
        family.IsAvailable.ShouldBeTrue();
        family.IsStale.ShouldBe(expectStale);
        family.IsDegraded.ShouldBeFalse();
        IReadOnlyDictionary<string, WorksRuntimeDocument?> documents = family.Value.ShouldNotBeNull();
        documents.Values.ShouldAllBe(value => value != null);
        Dictionary<string, object> complete = documents.ToDictionary(pair => pair.Key, pair => (object)pair.Value!, StringComparer.Ordinal);
        Dictionary<string, long> checkpoints = new(StringComparer.Ordinal);
        foreach ((string owner, ProjectionEventDto[] events) in histories)
        {
            WorkItemSharedRebuildAggregateHistory history = await WorkItemRuntimeHistoryStore.ReadAsync(coordinator, scope, family.Generation, owner, cancellationToken).ConfigureAwait(false);
            history.Events.Length.ShouldBe(events.Length);
            history.Events.Select(item => item.SequenceNumber).ShouldBe(events.Select(item => item.SequenceNumber));
            complete["complete-history:" + owner] = history;
            SharedProjectionCheckpoint checkpoint = (await coordinator.GetCheckpointAsync(scope, owner, cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
            checkpoint.Position.ShouldBe(events[^1].GlobalPosition);
            checkpoint.OwnerSequenceNumber.ShouldBe(events[^1].SequenceNumber);
            checkpoints.Add(owner, checkpoint.Position);
        }
        using var serialized = new MemoryStream();
        using (var writer = new Utf8JsonWriter(serialized)) Canonical(writer, JsonSerializer.SerializeToElement(complete, _json));
        return (family.Generation, Convert.ToHexStringLower(SHA256.HashData(serialized.ToArray())), checkpoints);
    }

    private static async Task<ProjectionDispatchResponse> DispatchAsync(HttpClient gatewaySidecar, ProjectionDispatchRequest request,
        string assertion, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/project/v2", request, assertion, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectionDispatchResponse>(cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
    }

    private static async Task<DomainSharedProjectionRebuildResponse> RebuildAsync(HttpClient gatewaySidecar, DomainSharedProjectionRebuildRequest request,
        string assertion, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/project/rebuild/shared/v1", request, assertion, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        DomainSharedProjectionRebuildResponse result = (await response.Content.ReadFromJsonAsync<DomainSharedProjectionRebuildResponse>(cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
        result.Status.ShouldBeOneOf(ProjectionDispatchStatus.Completed, ProjectionDispatchStatus.AlreadyCompleted);
        return result;
    }

    private static async Task<QueryResult> QueryAsync(HttpClient gatewaySidecar, string tenant, string owner, CancellationToken cancellationToken)
    {
        var query = new QueryEnvelope(tenant, "work", owner, GetWorkItemQueryHandler.GetWorkItemQueryType, [], "platform-rebuild-query", "workload:eventstore")
        { AuthenticatedWorkloadId = "eventstore" };
        using HttpResponseMessage response = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/query", query,
            WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceQuery]), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QueryResult>(cancellationToken).ConfigureAwait(false)).ShouldNotBeNull();
    }

    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Canonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in value.EnumerateArray()) Canonical(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
