using System.Security.Claims;

using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Real Works translation retains SDK unresolved state and reuses its validator after audited repair.</summary>
public sealed class WorksSdkProcessRecoveryTests
{
    /// <summary>Valid scoped streams with inconsistent reciprocal relationships quarantine before any plan or target effect.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InconsistentRelatedStreamsRetainQuarantine(bool missingReciprocalSpawn)
    {
        const string tenant = "tenant-415";
        var tenantId = new TenantId(tenant);
        var parent = new WorkItemId("parent-415");
        var child = new WorkItemId("child-415");
        string item = missingReciprocalSpawn ? child.Value : parent.Value;
        StreamReadPage parentPage = missingReciprocalSpawn
            ? Story48Streams.Page(tenant, parent.Value, new WorkItemCreated(parent.Value, 1, tenantId, parent, new Obligation("parent")))
            : Story48Streams.Page(tenant, parent.Value,
                new WorkItemCreated(parent.Value, 1, tenantId, parent, new Obligation("parent")),
                new ChildSpawned(parent.Value, 2, tenantId, parent, child, new Obligation("child")),
                new WorkItemCancelled(parent.Value, 3, tenantId, parent));
        var claimedParent = missingReciprocalSpawn ? parent : new WorkItemId("different-parent");
        StreamReadPage childPage = missingReciprocalSpawn
            ? Story48Streams.Page(tenant, child.Value,
                new WorkItemCreated(child.Value, 1, tenantId, child, new Obligation("child"), Parent: new ParentWorkItemReference(tenantId, claimedParent)),
                new WorkItemCompleted(child.Value, 2, tenantId, child))
            : Story48Streams.Page(tenant, child.Value,
                new WorkItemCreated(child.Value, 1, tenantId, child, new Obligation("child"), Parent: new ParentWorkItemReference(tenantId, claimedParent)));
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            StreamReadRequest request = call.Arg<StreamReadRequest>();
            StreamReadPage page = request.AggregateId == parent.Value ? parentPage : childPage;
            StreamReadEvent[] tail = [.. page.Events.Where(fact => fact.SequenceNumber > request.FromSequence)];
            return page with { Events = tail, Metadata = page.Metadata with
            { FromSequence = request.FromSequence, EventCount = tail.Length, LastSequenceReturned = tail.LastOrDefault()?.SequenceNumber } };
        });
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var source = new ProcessSource(tenant, "work", item);
        ProcessCheckpointStore Checkpoints() => new(store, options, TimeProvider.System);
        ITrustedEffectSubmitter submitter = Substitute.For<ITrustedEffectSubmitter>();
        var runner = new CheckpointedProcessRunner(gateway,
            new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()))),
            Substitute.For<IProcessDelegationProvider>(), submitter, Checkpoints(), options);
        ProcessRunResult result = await runner.RunAsync(source, TestContext.Current.CancellationToken);
        result.Status.ShouldBe(ProcessSourceStatus.Quarantined);
        result.Checkpoint.ShouldBe(missingReciprocalSpawn ? 1 : 2);
        ProcessSourceState durable = await Checkpoints().GetAsync(source, TestContext.Current.CancellationToken);
        durable.Checkpoint.ShouldBe(result.Checkpoint);
        durable.ReasonCode.ShouldBe("invalid-evidence");
        durable.PendingPlan.ShouldBeNull();
        (await runner.RunAsync(source, TestContext.Current.CancellationToken)).Checkpoint.ShouldBe(result.Checkpoint);
        submitter.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Enumerated terminal facts must match the authoritative contextual reread before checkpointing.</summary>
    [Theory]
    [InlineData("matching")]
    [InlineData("message-id")]
    [InlineData("event-type")]
    [InlineData("payload")]
    public async Task TerminalSourceMustMatchItsContextualReread(string difference)
    {
        const string tenant = "tenant-415";
        const string item = "source-415";
        var tenantId = new TenantId(tenant);
        var itemId = new WorkItemId(item);
        StreamReadPage enumerated = Story48Streams.Page(tenant, item, new WorkItemCancelled(item, 1, tenantId, itemId));
        StreamReadEvent committed = enumerated.Events[0];
        committed = difference switch
        {
            "message-id" => committed with { MessageId = "different-committed-message" },
            "event-type" => committed with { EventTypeName = typeof(WorkItemExpired).FullName! },
            "payload" => committed with { Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new WorkItemCancelled(item, 2, tenantId, itemId)) },
            _ => committed,
        };
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            StreamReadRequest request = call.Arg<StreamReadRequest>();
            StreamReadEvent[] tail = [.. enumerated.Events.Where(fact => fact.SequenceNumber > request.FromSequence)];
            return enumerated with { Events = tail, Metadata = enumerated.Metadata with
            { FromSequence = request.FromSequence, EventCount = tail.Length, LastSequenceReturned = tail.LastOrDefault()?.SequenceNumber } };
        });
        IEventStoreGatewayClient contextGateway = Substitute.For<IEventStoreGatewayClient>();
        contextGateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(enumerated with { Events = [committed] });
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var source = new ProcessSource(tenant, "work", item);
        var checkpoints = new ProcessCheckpointStore(store, options, TimeProvider.System);
        ITrustedEffectSubmitter submitter = Substitute.For<ITrustedEffectSubmitter>();
        var runner = new CheckpointedProcessRunner(gateway,
            new WorksProcessTranslator(new WorksSourceStreamReader(contextGateway, Options.Create(new WorksRecoveryOptions()))),
            Substitute.For<IProcessDelegationProvider>(), submitter, checkpoints, options);

        bool matching = difference == "matching";
        ProcessRunResult result = await runner.RunAsync(source, TestContext.Current.CancellationToken);
        result.Status.ShouldBe(matching ? ProcessSourceStatus.Active : ProcessSourceStatus.Quarantined);
        result.Checkpoint.ShouldBe(matching ? 1 : 0);
        ProcessSourceState durable = await checkpoints.GetAsync(source, TestContext.Current.CancellationToken);
        durable.Status.ShouldBe(result.Status);
        durable.Checkpoint.ShouldBe(result.Checkpoint);
        if (!matching)
        {
            durable.FailureSequence.ShouldBe(1);
            durable.ReasonCode.ShouldBe("invalid-evidence");
        }
        durable.PendingPlan.ShouldBeNull();
        submitter.ReceivedCalls().ShouldBeEmpty();
        await contextGateway.Received(1).ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("outage")]
    [InlineData("budget")]
    [InlineData("slow")]
    [InlineData("corruption")]
    [InlineData("foreign")]
    public async Task UnresolvedEvidenceSurvivesReconstructionAndUsesTheSameValidator(string failure)
    {
        const string tenant = "tenant-415";
        const string item = "source-415";
        var tenantId = new TenantId(tenant);
        var itemId = new WorkItemId(item);
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var source = new ProcessSource(tenant, "work", item);
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        StreamReadPage committed = Story48Streams.Page(tenant, item,
            new WorkItemCreated(item, 1, tenantId, itemId, new Obligation("context recovery")),
            new WorkItemCancelled(item, 2, tenantId, itemId));
        bool broken = true;
        int reads = 0;
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            StreamReadRequest request = call.Arg<StreamReadRequest>();
            reads++;
            if (broken && failure == "outage" && reads > 1)
                throw new HttpRequestException("Synthetic related evidence unavailable.");
            if (broken && failure == "slow" && reads > 1)
                Thread.Sleep(150);
            StreamReadPage page = committed;
            if (broken && failure is "corruption" or "foreign")
            {
                StreamReadEvent altered = failure == "corruption"
                    ? page.Events[1] with { EventTypeName = "UnknownWorksFact" }
                    : page.Events[1] with { Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                        new WorkItemCancelled(item, 2, new TenantId("foreign-tenant"), itemId)) };
                page = page with { Events = [page.Events[0], altered] };
            }
            StreamReadEvent[] tail = [.. page.Events.Where(value => value.SequenceNumber > request.FromSequence)];
            return Task.FromResult(page with { Events = tail, Metadata = page.Metadata with
            {
                FromSequence = request.FromSequence, EventCount = tail.Length,
                LastSequenceReturned = tail.Length == 0 ? null : tail[^1].SequenceNumber,
            } });
        });
        ITrustedEffectSubmitter submitter = Substitute.For<ITrustedEffectSubmitter>();
        ProcessCheckpointStore Checkpoints() => new(store, options, TimeProvider.System);
        CheckpointedProcessRunner Runner() => new(gateway,
            new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions
            { MaxContextBytes = broken && failure == "budget" ? 1 : 4 * 1024 * 1024, ContextReadTimeout = TimeSpan.FromMilliseconds(100) }))),
            Substitute.For<IProcessDelegationProvider>(), submitter, Checkpoints(), options);

        ProcessRunResult unresolved = await Runner().RunAsync(source, TestContext.Current.CancellationToken);
        unresolved.Checkpoint.ShouldBe(1);
        unresolved.Status.ShouldBe(failure is "outage" or "budget" or "slow" ? ProcessSourceStatus.Retrying : ProcessSourceStatus.Quarantined);
        ProcessSourceState durable = await Checkpoints().GetAsync(source, TestContext.Current.CancellationToken);
        durable.Checkpoint.ShouldBe(1);
        durable.ReasonCode.ShouldBe(failure is "outage" or "budget" or "slow" ? "evidence-unavailable" : "invalid-evidence");

        broken = false;
        if (failure is not ("outage" or "budget" or "slow"))
        {
            // Fixing evidence alone cannot bypass quarantine. Both failed audit and unauthenticated
            // disposition preserve the exact source; authorized audit precedes the SDK revalidation.
            (await Runner().RunAsync(source, TestContext.Current.CancellationToken)).Status.ShouldBe(ProcessSourceStatus.Quarantined);
            IProcessDispositionAuthorizer authorizer = Substitute.For<IProcessDispositionAuthorizer>();
            authorizer.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), source, Arg.Any<CancellationToken>()).Returns(true);
            IProcessDispositionAuditSink audit = Substitute.For<IProcessDispositionAuditSink>();
            var disposition = new ProcessDispositionService(authorizer, audit, Checkpoints(),
                new ProcessSweepStore(store, options, TimeProvider.System), options);
            await Should.ThrowAsync<UnauthorizedAccessException>(() => disposition.RevalidateAsync(new ClaimsPrincipal(), source,
                "synthetic repair", TestContext.Current.CancellationToken));
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "synthetic-operator")], "fixture"));
            audit.RecordAsync(principal, source, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("Synthetic audit unavailable.")));
            await Should.ThrowAsync<InvalidOperationException>(() => disposition.RevalidateAsync(principal, source,
                "synthetic repair", TestContext.Current.CancellationToken));
            (await Checkpoints().GetAsync(source, TestContext.Current.CancellationToken)).Status.ShouldBe(ProcessSourceStatus.Quarantined);
            audit.RecordAsync(principal, source, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            (await disposition.RevalidateAsync(principal, source, "synthetic repair", TestContext.Current.CancellationToken)).ShouldBeTrue();
        }
        ProcessRunResult recovered = await Runner().RunAsync(source, TestContext.Current.CancellationToken);
        recovered.Checkpoint.ShouldBe(2);
        recovered.Status.ShouldBe(ProcessSourceStatus.Active);
        submitter.ReceivedCalls().ShouldBeEmpty();
    }
}
