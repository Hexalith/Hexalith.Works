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
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
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
        using HttpResponseMessage metadataResponse = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/admin/operational-index-metadata",
            new AdminOperationalIndexMetadata.Request(["work"]) { AppId = "works", ServiceVersion = "v1" },
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
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(tenant);
        var coordinator = new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store);
        object initialConvergence = await WaitForInitialConvergenceAsync(coordinator, scope, gateway, histories, cancellationToken).ConfigureAwait(false);
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } initialEvidencePath)
        {
            using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            writeDeadline.CancelAfter(TimeSpan.FromSeconds(2));
            await File.AppendAllTextAsync(initialEvidencePath, JsonSerializer.Serialize(new
            {
                phase = "platform-initial-projection-convergence", state = initialConvergence,
            }) + Environment.NewLine, writeDeadline.Token).ConfigureAwait(false);
        }
        ProjectionEventDto[] history = histories[aggregateId];
        var request = new ProjectionDispatchRequest(new ProjectionRequest(tenant, "work", aggregateId, history),
            [WorksReadModelKeys.RuntimeFamily], "platform-redelivery-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), catalog);
        var before = await SnapshotAsync(coordinator, scope, histories, cancellationToken).ConfigureAwait(false);

        string projectionAssertion = WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]);

        ProjectionDispatchResponse redelivered = await DispatchAsync(gatewaySidecar, request, projectionAssertion, cancellationToken).ConfigureAwait(false);
        redelivered.Outcomes.ShouldHaveSingleItem().Status.ShouldBeOneOf(ProjectionDispatchStatus.Completed, ProjectionDispatchStatus.AlreadyCompleted);
        var afterRedelivery = await SnapshotAsync(coordinator, scope, histories, cancellationToken).ConfigureAwait(false);
        afterRedelivery.Digest.ShouldBe(before.Digest);
        afterRedelivery.Generation.ShouldBe(before.Generation);
        afterRedelivery.Checkpoints.ShouldBe(before.Checkpoints);

        int circuitRefusals = 0;
        async Task ConfirmUnchangedRedeliveryAsync()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            while (true)
            {
                using HttpResponseMessage response = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/project/v2",
                    request with { DispatchId = request.DispatchId + "-negative-control" },
                    WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]), deadline.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    ProjectionDispatchResponse result = (await response.Content.ReadFromJsonAsync<ProjectionDispatchResponse>(deadline.Token).ConfigureAwait(false)).ShouldNotBeNull();
                    result.Outcomes.ShouldHaveSingleItem().Status.ShouldBeOneOf(ProjectionDispatchStatus.Completed, ProjectionDispatchStatus.AlreadyCompleted);
                    var unchanged = await SnapshotAsync(coordinator, scope, histories, deadline.Token).ConfigureAwait(false);
                    unchanged.Generation.ShouldBe(before.Generation);
                    unchanged.Digest.ShouldBe(before.Digest);
                    unchanged.Checkpoints.ShouldBe(before.Checkpoints);
                    return;
                }
                string detail = await ReadFailureBodyAsync(response, deadline.Token).ConfigureAwait(false);
                bool explicitCircuitRefusal = response.StatusCode == HttpStatusCode.InternalServerError
                    && detail.Contains("circuit breaker", StringComparison.OrdinalIgnoreCase)
                    && (detail.Contains("open", StringComparison.OrdinalIgnoreCase) || detail.Contains("too many requests", StringComparison.OrdinalIgnoreCase));
                if (!explicitCircuitRefusal)
                    throw new InvalidOperationException("Unchanged projection control refused. HTTP=" + (int)response.StatusCode + "; body=" + detail);
                circuitRefusals++;
                await Task.Delay(1000, deadline.Token).ConfigureAwait(false);
            }
        }

        // A permitted workload on its own sidecar still cannot impersonate the gateway caller.
        using HttpResponseMessage wrongOrigin = await WorksPlatformProofRequests.PostAsync(worksSidecar, "works", "/project/v2", request,
            projectionAssertion, cancellationToken).ConfigureAwait(false);
        wrongOrigin.IsSuccessStatusCode.ShouldBeFalse();
        wrongOrigin.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        using HttpResponseMessage wrongOperation = await WorksPlatformProofRequests.PostAsync(gatewaySidecar, "works", "/project/v2", request,
            WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceQuery]), cancellationToken).ConfigureAwait(false);
        wrongOperation.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "Actual Dapr refusal body: " + await ReadFailureBodyAsync(wrongOperation, cancellationToken).ConfigureAwait(false));
        // Restore a demonstrated successful protected transport after an independent denial consumed its retry policy.
        await ConfirmUnchangedRedeliveryAsync().ConfigureAwait(false);
        projectionAssertion = WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]);
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
        else invalidSequenceResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest,
            "Actual Dapr invalid-sequence body: " + await ReadFailureBodyAsync(invalidSequenceResponse, cancellationToken).ConfigureAwait(false));
        await ConfirmUnchangedRedeliveryAsync().ConfigureAwait(false);
        projectionAssertion = WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]);
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
            SourceHighWatermarks = histories.ToDictionary(pair => pair.Key, pair => pair.Value[^1].GlobalPosition, StringComparer.Ordinal),
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
        object concurrentDelivery = await VerifyConcurrentDeliveryAsync(coordinator, scope, gateway, gatewaySidecar, histories,
            aggregateId, catalog, projectionAssertion, cancellationToken).ConfigureAwait(false);
        return new { initialConvergence, concurrentDelivery, catalog, sources = histories.Keys.ToArray(), sourceGlobalPositions = after.Checkpoints, sourceHeads = histories.ToDictionary(pair => pair.Key, pair => pair.Value[^1].SequenceNumber, StringComparer.Ordinal), redelivered, fenced,
            negatives = new { wrongOrigin = (int)wrongOrigin.StatusCode, wrongOperation = (int)wrongOperation.StatusCode, invalidSequence = invalidSequenceStatus, circuitRefusals, unchangedProtectedControls = 2 },
            before = new { before.Generation, before.Digest }, after = new { after.Generation, after.Digest }, inventory,
            begun, accumulated, finalized, staged, committed, verified, staleQuery = staleQuery.Metadata, currentQuery = currentQuery.Metadata };
    }

    private static async Task<object> VerifyConcurrentDeliveryAsync(SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope,
        HttpClient gateway, HttpClient gatewaySidecar, SortedDictionary<string, ProjectionEventDto[]> histories, string owner,
        string catalog, string assertion, CancellationToken cancellationToken)
    {
        assertion = WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]);
        var before = await SnapshotAsync(coordinator, scope, histories, cancellationToken).ConfigureAwait(false);
        string inventory = DomainSharedProjectionRebuildFingerprint.EmptyInventory;
        long ordinal = 0;
        foreach ((string source, ProjectionEventDto[] events) in histories)
            inventory = DomainSharedProjectionRebuildFingerprint.AppendInventory(inventory, ordinal++, DomainSharedProjectionRebuildFingerprint.ComputeHistory(source, false, events));
        var identity = new DomainSharedProjectionRebuildIdentity(scope.TenantId, "work", WorksReadModelKeys.RuntimeFamily,
            "platform-concurrent-rebuild-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), catalog);
        DomainSharedProjectionRebuildRequest Lifecycle(DomainSharedProjectionRebuildAction action) => new(DomainSharedProjectionRebuildProtocol.Version, action, identity);
        _ = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Begin) with
        {
            CaptureInventoryFingerprint = inventory,
            SourceHighWatermarks = histories.ToDictionary(pair => pair.Key, pair => pair.Value[^1].GlobalPosition, StringComparer.Ordinal),
        }, assertion, cancellationToken).ConfigureAwait(false);
        string correlation = "synthetic-capture-416-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5));
        await WorksAppHostSmokeHarness.SubmitToTerminalAsync(gateway, scope.TenantId, owner, nameof(LinkConversation),
            new LinkConversation(new TenantId(scope.TenantId), new WorkItemId(owner), new ConversationCorrelationId(correlation)), cancellationToken).ConfigureAwait(false);
        ProjectionEventDto[] changed = await ReadHistoryAsync(gateway, new ProcessSource(scope.TenantId, "work", owner), cancellationToken).ConfigureAwait(false);
        changed.Length.ShouldBe(histories[owner].Length + 1);
        changed[^1].EventTypeName.Split('.').Last().ShouldBe(nameof(ConversationLinked));
        JsonSerializer.Deserialize<ConversationLinked>(changed[^1].Payload, _json)!.ConversationCorrelationId.Value.ShouldBe(correlation);
        var during = await SnapshotAsync(coordinator, scope, histories, cancellationToken, expectStale: true).ConfigureAwait(false);
        during.Generation.ShouldBe(before.Generation);
        during.Digest.ShouldBe(before.Digest);
        QueryResult stale = await QueryAsync(gatewaySidecar, scope.TenantId, owner, cancellationToken).ConfigureAwait(false);
        stale.Success.ShouldBeTrue(stale.ErrorMessage);
        stale.Metadata.ShouldNotBeNull().IsStale.ShouldBe(true);
        stale.GetPayload().ToString().ShouldNotContain(correlation);
        ordinal = 0;
        foreach ((string source, ProjectionEventDto[] events) in histories)
            _ = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Accumulate) with
            {
                AggregateOrdinal = ordinal++, AggregateId = source, Events = events,
            }, assertion, cancellationToken).ConfigureAwait(false);
        _ = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Finalize) with
        {
            ExpectedAggregateCount = histories.Count, ExpectedInventoryFingerprint = inventory,
        }, assertion, cancellationToken).ConfigureAwait(false);
        _ = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Stage), assertion, cancellationToken).ConfigureAwait(false);
        (await SnapshotAsync(coordinator, scope, histories, cancellationToken, expectStale: true).ConfigureAwait(false)).Digest.ShouldBe(before.Digest);
        DomainSharedProjectionRebuildResponse committed = await RebuildAsync(gatewaySidecar, Lifecycle(DomainSharedProjectionRebuildAction.Commit), assertion, cancellationToken).ConfigureAwait(false);
        committed.Phase.ShouldBe(DomainSharedProjectionRebuildPhase.Committed);
        try
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                while (true)
                {
                    SharedProjectionCheckpoint? checkpoint = await coordinator.GetCheckpointAsync(scope, owner, deadline.Token).ConfigureAwait(false);
                    SharedProjectionEpochStatus status = await coordinator.GetStatusAsync(scope, deadline.Token).ConfigureAwait(false);
                    if (checkpoint?.OwnerSequenceNumber == changed[^1].SequenceNumber && checkpoint.Position == changed[^1].GlobalPosition
                        && !status.IsBuilding && status.PendingDeliveryCount == 0 && !status.IsDegraded) break;
                    await Task.Delay(100, deadline.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await CaptureConcurrentDeliveryFailureAsync(coordinator, scope, gatewaySidecar, owner, changed, catalog,
                WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]), cancellationToken).ConfigureAwait(false);
            throw;
        }
        assertion = WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]);
        var updated = new SortedDictionary<string, ProjectionEventDto[]>(histories, StringComparer.Ordinal) { [owner] = changed };
        var after = await SnapshotAsync(coordinator, scope, updated, cancellationToken).ConfigureAwait(false);
        after.Generation.ShouldBeGreaterThan(before.Generation);
        after.Digest.ShouldNotBe(before.Digest);
        // Independently fold authoritative streams into logical documents. This checks every persisted
        // family value and complete source history after transport, promotion and automatic SDK catch-up.
        DomainProjectionRebuildPlan expected = WorkItemSharedRebuildManifestBuilder.BuildEpoch(identity,
            new WorkItemSharedRebuildCandidateState([.. updated.Select(pair => new WorkItemSharedRebuildAggregateHistory(pair.Key, pair.Value))]), cancellationToken);
        string[] selected = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
            .. updated.Keys.SelectMany(source => new[] { WorksReadModelKeys.RuntimeRollUpKey(source), WorksReadModelKeys.RuntimeHistoryKey(source), WorksReadModelKeys.RuntimeParkingKey(source) })];
        Dictionary<string, object> complete = expected.Operations.Where(operation => selected.Contains(operation.Key, StringComparer.Ordinal))
            .ToDictionary(operation => operation.Key, operation => (object)JsonSerializer.Deserialize<WorksRuntimeDocument>(operation.CanonicalValue.Span, _json)!, StringComparer.Ordinal);
        complete.Count.ShouldBe(selected.Length);
        foreach ((string source, ProjectionEventDto[] events) in updated)
            complete["complete-history:" + source] = new WorkItemSharedRebuildAggregateHistory(source, events);
        using var serialized = new MemoryStream();
        using (var writer = new Utf8JsonWriter(serialized)) Canonical(writer, JsonSerializer.SerializeToElement(complete, _json));
        string expectedDigest = Convert.ToHexStringLower(SHA256.HashData(serialized.ToArray()));
        after.Digest.ShouldBe(expectedDigest);
        QueryResult current = await QueryAsync(gatewaySidecar, scope.TenantId, owner, cancellationToken).ConfigureAwait(false);
        current.Success.ShouldBeTrue(current.ErrorMessage);
        current.Metadata.ShouldNotBeNull().IsStale.ShouldBe(false);
        current.GetPayload().ToString().ShouldContain(correlation);
        var redelivery = new ProjectionDispatchRequest(new ProjectionRequest(scope.TenantId, "work", owner, changed),
            [WorksReadModelKeys.RuntimeFamily], identity.OperationId + "-redelivery", catalog);
        (await DispatchAsync(gatewaySidecar, redelivery, assertion, cancellationToken).ConfigureAwait(false))
            .Outcomes.ShouldHaveSingleItem().Status.ShouldBeOneOf(ProjectionDispatchStatus.Completed, ProjectionDispatchStatus.AlreadyCompleted);
        (await SnapshotAsync(coordinator, scope, updated, cancellationToken).ConfigureAwait(false)).Digest.ShouldBe(after.Digest);
        (await ReadHistoryAsync(gateway, new ProcessSource(scope.TenantId, "work", owner), cancellationToken).ConfigureAwait(false))
            .Count(item => item.EventTypeName.Split('.').Last() == nameof(ConversationLinked)).ShouldBe(1);
        return new { command = nameof(LinkConversation), committedSourceHead = changed[^1].SequenceNumber, committedGlobalPosition = changed[^1].GlobalPosition,
            before = new { before.Generation, before.Digest }, after = new { after.Generation, after.Digest }, expectedDigest,
            actualNewDeliveryDuringCapture = true, oldGenerationStayedReadable = true, automaticCatchUpAfterCommit = true, redeliveryLogicalCount = 1 };
    }

    private static async Task CaptureConcurrentDeliveryFailureAsync(SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope,
        HttpClient gatewaySidecar, string owner, ProjectionEventDto[] changed, string catalog, string assertion, CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is not { Length: > 0 } path) return;
        object? beforeStatus = null;
        object? beforeCheckpoint = null;
        object? dispatch = null;
        object? afterStatus = null;
        object? afterCheckpoint = null;
        List<string> errors = [];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            beforeStatus = await coordinator.GetStatusAsync(scope, deadline.Token).ConfigureAwait(false);
            beforeCheckpoint = await coordinator.GetCheckpointAsync(scope, owner, deadline.Token).ConfigureAwait(false);
            // This single diagnostic dispatch can change durable state. It never qualifies automatic catch-up:
            // the caller always rethrows the original timeout, including if this dispatch completes successfully.
            dispatch = await DispatchAsync(gatewaySidecar, new ProjectionDispatchRequest(
                new ProjectionRequest(scope.TenantId, "work", owner, changed), [WorksReadModelKeys.RuntimeFamily],
                "diagnostic-after-automatic-catchup-timeout-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), catalog),
                assertion, deadline.Token).ConfigureAwait(false);
            afterStatus = await coordinator.GetStatusAsync(scope, deadline.Token).ConfigureAwait(false);
            afterCheckpoint = await coordinator.GetCheckpointAsync(scope, owner, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            errors.Add(exception.GetType().Name);
        }
        try
        {
            using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            writeDeadline.CancelAfter(TimeSpan.FromSeconds(2));
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new
            {
                phase = "platform-concurrent-delivery-timeout",
                state = new { diagnosticOnly = true, qualifies = false, diagnosticDispatchMayMutate = true,
                    expectedOwnerSequenceNumber = changed[^1].SequenceNumber, expectedGlobalPosition = changed[^1].GlobalPosition,
                    beforeStatus, beforeCheckpoint, dispatch, afterStatus, afterCheckpoint, errors },
            }) + Environment.NewLine, writeDeadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Diagnostics must preserve the original failure even when their bounded evidence write fails.
        }
    }

    /// <summary>Conflicting named redelivery persists SDK quarantine and refuses queries without changing accepted data.</summary>
    internal static async Task<object> VerifyQuarantineAsync(DistributedApplication app, HttpClient gateway, IReadModelStore store,
        string tenant, string aggregateId, CancellationToken cancellationToken)
    {
        ResourceEvent gatewayResource = await WorksAppHostTestReadiness.WaitForResourceHealthyAsync(app, "eventstore", cancellationToken, cancellationToken).ConfigureAwait(false);
        using HttpClient sidecar = WorksAppHostTestReadiness.CreateDaprClient(gatewayResource, "EventStore");
        string assertion = WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceProject]);
        using HttpResponseMessage metadataResponse = await WorksPlatformProofRequests.PostAsync(sidecar, "works", "/admin/operational-index-metadata",
            new AdminOperationalIndexMetadata.Request(["work"]) { AppId = "works", ServiceVersion = "v1" },
            WorksPlatformProofRequests.Assertion("eventstore", "works", [EventStoreWorkloadOperations.DomainServiceMetadata]), cancellationToken).ConfigureAwait(false);
        metadataResponse.EnsureSuccessStatusCode();
        string catalog = (await metadataResponse.Content.ReadFromJsonAsync<AdminOperationalIndexMetadata.Response>(cancellationToken).ConfigureAwait(false))!.CatalogFingerprint.ShouldNotBeNull();
        ProjectionEventDto[] history = await ReadHistoryAsync(gateway, new ProcessSource(tenant, "work", aggregateId), cancellationToken).ConfigureAwait(false);
        var histories = new Dictionary<string, ProjectionEventDto[]>(StringComparer.Ordinal) { [aggregateId] = history };
        var handler = new WorkItemSharedProjectionRebuildHandler(Options.Create(new WorksProjectionOptions { UseSdkDelivery = true }));
        SharedProjectionScope scope = handler.CreateScope(tenant);
        var coordinator = new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            while ((await coordinator.GetCheckpointAsync(scope, aggregateId, deadline.Token).ConfigureAwait(false))?.Position != history[^1].GlobalPosition)
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
        var before = await SnapshotAsync(coordinator, scope, histories, cancellationToken).ConfigureAwait(false);
        ProjectionEventDto original = history[0];
        WorkItemCreated created = JsonSerializer.Deserialize<WorkItemCreated>(original.Payload, _json).ShouldNotBeNull();
        ProjectionEventDto conflicting = original with
        {
            Payload = JsonSerializer.SerializeToUtf8Bytes(created with { Obligation = new Obligation("Conflicting synthetic delivery identity") }, _json),
        };
        ProjectionDispatchRequest request = new(new ProjectionRequest(tenant, "work", aggregateId, [conflicting]),
            [WorksReadModelKeys.RuntimeFamily], "platform-quarantine-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), catalog);
        ProjectionDispatchResponse refused = await DispatchAsync(sidecar, request, assertion, cancellationToken).ConfigureAwait(false);
        ProjectionDispatchOutcome outcome = refused.Outcomes.ShouldHaveSingleItem();
        outcome.Status.ShouldBe(ProjectionDispatchStatus.Failed);
        outcome.ReasonCode.ShouldBe(ProjectionDispatchReasonCodes.DeliveryIdentityConflict);
        // A new coordinator must see durable SDK conflict evidence, independent of the serving process.
        var cold = new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store);
        SharedProjectionEpochStatus quarantined = await cold.GetStatusAsync(scope, cancellationToken).ConfigureAwait(false);
        quarantined.IsDegraded.ShouldBeTrue();
        SharedProjectionDeliveryConflict conflict = quarantined.DeliveryConflicts.ShouldHaveSingleItem();
        conflict.SourceStream.ShouldBe(aggregateId);
        conflict.EnvelopePosition.ShouldBe(original.GlobalPosition);
        conflict.IsResolved.ShouldBeFalse();
        var after = await SnapshotAsync(cold, scope, histories, cancellationToken, expectDegraded: true).ConfigureAwait(false);
        after.Generation.ShouldBe(before.Generation);
        after.Digest.ShouldBe(before.Digest);
        after.Checkpoints.ShouldBe(before.Checkpoints);
        QueryResult denied = await QueryAsync(sidecar, tenant, aggregateId, cancellationToken).ConfigureAwait(false);
        denied.Success.ShouldBeFalse();
        denied.Metadata.ShouldNotBeNull().IsDegraded.ShouldBe(true);
        denied.Metadata.Lifecycle.ShouldBe(ProjectionLifecycleState.Unavailable);
        // A retried conflict remains refused and cannot multiply evidence or change the accepted family.
        (await DispatchAsync(sidecar, request with { DispatchId = request.DispatchId + "-retry" }, assertion, cancellationToken).ConfigureAwait(false))
            .Outcomes.ShouldHaveSingleItem().Status.ShouldBe(ProjectionDispatchStatus.Failed);
        (await new SharedProjectionEpochCoordinator(store, (IReadModelBatchStore)store).GetStatusAsync(scope, cancellationToken).ConfigureAwait(false))
            .DeliveryConflicts.ShouldHaveSingleItem();
        return new { tenant, aggregateId, outcome, conflict, query = denied.Metadata,
            acceptedGeneration = before.Generation, acceptedFamilySha256 = before.Digest, unchangedFamilySha256 = after.Digest,
            unchangedCheckpoints = after.Checkpoints, repeatedConflictCount = 1 };
    }

    private static async Task<string> ReadFailureBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            Stream body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            await using var disposal = body.ConfigureAwait(false);
            byte[] bytes = new byte[16384];
            int retained = 0;
            while (retained < bytes.Length)
            {
                int read = await body.ReadAsync(bytes.AsMemory(retained), deadline.Token).AsTask()
                    .WaitAsync(deadline.Token).ConfigureAwait(false);
                if (read == 0) break;
                retained += read;
            }
            return System.Text.Encoding.UTF8.GetString(bytes, 0, retained);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "<failure body exceeded five-second diagnostic deadline>";
        }
        catch (IOException)
        {
            return "<failure body could not be read>";
        }
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
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Source history read refused. HTTP=" + (int)response.StatusCode
                    + "; source=" + source.Tenant + ":" + source.Domain + ":" + source.Aggregate
                    + "; body=" + await ReadFailureBodyAsync(response, cancellationToken).ConfigureAwait(false));
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

    private static async Task<object> WaitForInitialConvergenceAsync(SharedProjectionEpochCoordinator coordinator,
        SharedProjectionScope scope, HttpClient gateway, SortedDictionary<string, ProjectionEventDto[]> histories, CancellationToken cancellationToken)
    {
        // Committed commands and process receipts precede asynchronous projection delivery. Observe
        // the natural SDK path before taking the baseline; never repair it with a direct dispatch.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        Dictionary<string, SharedProjectionCheckpoint?> observed = new(StringComparer.Ordinal);
        int polls = 0;
        int recapturedHeads = 0;
        async Task<bool> RecaptureAsync(string owner)
        {
            ProjectionEventDto[] prior = histories[owner];
            ProjectionEventDto[] current = await ReadHistoryAsync(gateway, new ProcessSource(scope.TenantId, "work", owner), deadline.Token).ConfigureAwait(false);
            if (current.Length < prior.Length || !JsonSerializer.SerializeToUtf8Bytes(current.Take(prior.Length).ToArray(), _json).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(prior, _json)))
                throw new InvalidOperationException("Authoritative source history changed its captured prefix before the projection baseline: " + owner);
            if (current.Length == prior.Length) return false;
            histories[owner] = current;
            recapturedHeads++;
            return true;
        }
        object Heads() => histories.ToDictionary(pair => pair.Key, pair => new
        {
            expectedOwnerSequence = pair.Value[^1].SequenceNumber,
            expectedGlobalPosition = pair.Value[^1].GlobalPosition,
            observed = observed.GetValueOrDefault(pair.Key),
        }, StringComparer.Ordinal);
        try
        {
            while (true)
            {
                polls++;
                foreach (string owner in histories.Keys.ToArray())
                {
                    observed[owner] = await coordinator.GetCheckpointAsync(scope, owner, deadline.Token).ConfigureAwait(false);
                    if (observed[owner]?.OwnerSequenceNumber > histories[owner][^1].SequenceNumber)
                        _ = await RecaptureAsync(owner).ConfigureAwait(false);
                }
                if (histories.All(pair => observed[pair.Key]?.OwnerSequenceNumber == pair.Value[^1].SequenceNumber
                    && observed[pair.Key]!.Position == pair.Value[^1].GlobalPosition))
                {
                    bool advanced = false;
                    // Recheck committed sources before accepting an otherwise aligned snapshot.
                    // A legitimate append keeps its exact prior prefix and must converge naturally too.
                    foreach (string owner in histories.Keys.ToArray())
                        advanced |= await RecaptureAsync(owner).ConfigureAwait(false);
                    if (!advanced)
                        return new { naturalCatchUp = true, polls, recapturedHeads, elapsedMilliseconds = elapsed.ElapsedMilliseconds, heads = Heads() };
                }
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            object heads = Heads();
            string detail = JsonSerializer.Serialize(heads);
            if (Environment.GetEnvironmentVariable("WORKS_SDK_PROOF_EVIDENCE_PATH") is { Length: > 0 } path)
            {
                using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                writeDeadline.CancelAfter(TimeSpan.FromSeconds(2));
                await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new
                {
                    phase = "platform-initial-projection-catchup-timeout",
                    state = new { qualifies = false, manualDispatchOccurred = false, polls, recapturedHeads, heads },
                }) + Environment.NewLine, writeDeadline.Token).ConfigureAwait(false);
            }
            throw new InvalidOperationException("Initial natural projection catch-up did not reach the authoritative owner/global heads within two minutes: " + detail, exception);
        }
    }

    private static async Task<(long Generation, string Digest, IReadOnlyDictionary<string, long> Checkpoints)> SnapshotAsync(
        SharedProjectionEpochCoordinator coordinator, SharedProjectionScope scope, IReadOnlyDictionary<string, ProjectionEventDto[]> histories,
        CancellationToken cancellationToken, bool expectStale = false, bool expectDegraded = false)
    {
        string[] keys = [WorksReadModelKeys.RuntimeQueueKey, WorksReadModelKeys.RuntimePendingDateKey, WorksReadModelKeys.RuntimeTopologyKey,
            .. histories.Keys.SelectMany(owner => new[] { WorksReadModelKeys.RuntimeRollUpKey(owner), WorksReadModelKeys.RuntimeHistoryKey(owner), WorksReadModelKeys.RuntimeParkingKey(owner) })];
        var family = await coordinator.ReadManyAsync<WorksRuntimeDocument>(scope, keys, cancellationToken).ConfigureAwait(false);
        family.IsAvailable.ShouldBeTrue();
        family.IsStale.ShouldBe(expectStale);
        family.IsDegraded.ShouldBe(expectDegraded);
        IReadOnlyDictionary<string, WorksRuntimeDocument?> documents = family.Value.ShouldNotBeNull();
        documents.Values.ShouldAllBe(value => value != null);
        Dictionary<string, object> complete = documents.ToDictionary(pair => pair.Key, pair => (object)pair.Value!, StringComparer.Ordinal);
        Dictionary<string, long> checkpoints = new(StringComparer.Ordinal);
        foreach ((string owner, ProjectionEventDto[] events) in histories)
        {
            WorkItemSharedRebuildAggregateHistory history = await WorkItemRuntimeHistoryStore.ReadAsync(coordinator, scope, family.Generation, owner, cancellationToken).ConfigureAwait(false);
            history.Events.Length.ShouldBe(events.Length, $"owner={owner}; selected generation={family.Generation}; authoritative owner={events[^1].SequenceNumber}, global={events[^1].GlobalPosition}");
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
