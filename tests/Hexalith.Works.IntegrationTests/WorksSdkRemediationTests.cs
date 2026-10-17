using System.Diagnostics;
using System.Text.Json;

using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Processes;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Persisted SDK behavior at the reviewed capacity, version and contextual acquisition boundaries.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksSdkRemediationTests
{
    private const string Tenant = "tenant-415";

    /// <summary>Five configured targets complete, while the whole over-capacity terminal envelope remains unresolved without submissions.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ConfiguredCascadeCapacityIsAtomicPerEnvelope(int children)
    {
        var tenant = new TenantId(Tenant);
        var parent = new WorkItemId("parent");
        var facts = new List<IEventPayload> { new WorkItemCreated(parent.Value, 1, tenant, parent, new Obligation("parent")) };
        for (int index = 0; index < children; index++)
            facts.Add(new ChildSpawned(parent.Value, index + 2, tenant, parent, new WorkItemId("child-" + index), new Obligation("child")));
        facts.Add(new WorkItemCancelled(parent.Value, children + 2, tenant, parent));
        StreamReadPage parentPage = Story48Streams.Page(Tenant, parent.Value, [.. facts]);
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            StreamReadRequest request = call.Arg<StreamReadRequest>();
            StreamReadPage page = request.AggregateId == parent.Value ? parentPage : Story48Streams.Page(Tenant, request.AggregateId!,
                new WorkItemCreated(request.AggregateId!, 1, tenant, new WorkItemId(request.AggregateId!), new Obligation("child"),
                    Parent: new ParentWorkItemReference(tenant, parent)));
            return Task.FromResult(Tail(page, request.FromSequence, request.PageSize));
        });
        var store = new InMemoryReadModelStore();
        var configured = new EventStoreProcessOptions { MaxEffectsPerEnvelope = 5, SourceServiceQuantum = TimeSpan.FromSeconds(15) };
        configured.Validate().ShouldBeEmpty();
        var options = Options.Create(configured);
        var checkpoints = new ProcessCheckpointStore(store, options, TimeProvider.System);
        var delegation = Substitute.For<IProcessDelegationProvider>();
        delegation.CreateAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TrustedEffectContext("works", "works.cascade-cancel", call.Arg<string>(), "synthetic")));
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        var submitter = Substitute.For<ITrustedEffectSubmitter>();
        submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            string effectId = EffectIdentityCodec.ComputeEffectId(call.Arg<TrustedEffectSubmission>().Identity);
            bool replayed = !accepted.Add(effectId);
            return Task.FromResult(new TrustedEffectResult(effectId, TrustedEffectDisposition.Success, replayed, null));
        });
        var source = new ProcessSource(Tenant, "work", parent.Value);
        var runner = new CheckpointedProcessRunner(gateway,
            new WorksProcessTranslator(new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()))),
            delegation, submitter, checkpoints, options);
        ProcessRunResult result;
        do { result = await runner.RunAsync(source, TestContext.Current.CancellationToken); }
        while (result.Status == ProcessSourceStatus.Incomplete);
        ProcessSourceState durable = await new ProcessCheckpointStore(store, options, TimeProvider.System)
            .GetAsync(source, TestContext.Current.CancellationToken);
        result.Status.ShouldBe(children == 5 ? ProcessSourceStatus.Active : ProcessSourceStatus.Quarantined);
        durable.Checkpoint.ShouldBe(children == 5 ? children + 2 : children + 1);
        accepted.Count.ShouldBe(children == 5 ? 5 : 0);
        durable.PendingPlan.ShouldBeNull();
        if (children == 6) durable.FailureSequence.ShouldBe(children + 2);
    }

    /// <summary>Unsupported positive versions are rejected even for decoded no-op facts, while JSON casing remains supported.</summary>
    [Theory]
    [InlineData(1, 1, "JSON", true)]
    [InlineData(1, 2, "json", false)]
    [InlineData(2, 1, "json", false)]
    public async Task VersionsAreCheckedBeforeDecodedNoOps(int metadata, int payload, string encoding, bool supported)
    {
        var source = new ProcessSource(Tenant, "work", "item");
        StreamReadPage page = Story48Streams.Page(Tenant, "item", new WorkItemCreated("item", 1,
            new TenantId(Tenant), new WorkItemId("item"), new Obligation("version")));
        StreamReadEvent envelope = page.Events[0] with { MetadataVersion = metadata, PayloadVersion = payload, SerializationFormat = encoding };
        page = page with { Events = [envelope] };
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(page);
        var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
        var translator = new WorksProcessTranslator(reader);
        if (supported)
        {
            (await reader.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken)).Count.ShouldBe(1);
            (await translator.TranslateAsync(source, envelope, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        }
        else
        {
            await Should.ThrowAsync<InvalidOperationException>(() => reader.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken));
            await Should.ThrowAsync<InvalidOperationException>(() => translator.TranslateAsync(source, envelope, TestContext.Current.CancellationToken).AsTask());
        }
    }

    /// <summary>Cumulative encoded bytes are charged across pages before a reminder can receive partial context.</summary>
    [Fact]
    public async Task CumulativeContextByteBudgetIsUnresolvedBeforeReminderIntent()
    {
        var item = new WorkItemId("item");
        var tenant = new TenantId(Tenant);
        StreamReadPage all = Story48Streams.Page(Tenant, item.Value,
            new WorkItemCreated(item.Value, 1, tenant, item, new Obligation(new string('x', 600))),
            new WorkItemSuspended(item.Value, 2, tenant, item, [AwaitCondition.DateReached(DateTimeOffset.UnixEpoch.AddYears(60))]));
        int firstSize = JsonSerializer.SerializeToUtf8Bytes(all.Events[0]).Length;
        int secondSize = JsonSerializer.SerializeToUtf8Bytes(all.Events[1]).Length;
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            long from = call.Arg<StreamReadRequest>().FromSequence;
            StreamReadEvent fact = all.Events[(int)from];
            return Task.FromResult(all with { Events = [fact], Metadata = all.Metadata with
            { FromSequence = from, LastSequenceReturned = fact.SequenceNumber, EventCount = 1, IsTruncated = from == 0 } });
        });
        var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions { MaxContextBytes = firstSize + secondSize - 1 }));
        await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => new WorksReminderIntentSource(reader)
            .GetCurrentIntentsAsync(new(Tenant, "work", item.Value), TestContext.Current.CancellationToken));
    }

    /// <summary>A non-cooperative gateway cannot monopolize reminder convergence or release late evidence.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WholeContextDeadlineBoundsSlowAndNonCooperativeReads(bool synchronous)
    {
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        var release = new TaskCompletionSource<StreamReadPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blocker = new ManualResetEventSlim();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (synchronous) blocker.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("the test must release the blocked provider");
            return release.Task;
        });
        var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions { ContextReadTimeout = TimeSpan.FromMilliseconds(100) }));
        var watch = Stopwatch.StartNew();
        try
        {
            await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => reader.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken));
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        }
        finally
        {
            blocker.Set();
            release.TrySetResult(Story48Streams.Page(Tenant, "item"));
        }
    }

    /// <summary>Individually short page reads share one whole acquisition deadline instead of resetting it per page.</summary>
    [Fact]
    public async Task SlowPagesConsumeOneCumulativeContextDeadline()
    {
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId("item");
        var all = Story48Streams.Page(Tenant, item.Value,
            new WorkItemCreated(item.Value, 1, tenant, item, new Obligation("slow pages")),
            new WorkItemSuspended(item.Value, 2, tenant, item, [AwaitCondition.DateReached(DateTimeOffset.UnixEpoch.AddYears(60))]));
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            long from = call.Arg<StreamReadRequest>().FromSequence;
            await Task.Delay(60).ConfigureAwait(false);
            var fact = all.Events[(int)from];
            return all with { Events = [fact], Metadata = all.Metadata with
            { FromSequence = from, LastSequenceReturned = fact.SequenceNumber, EventCount = 1, IsTruncated = from == 0 } };
        });
        var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions { ContextReadTimeout = TimeSpan.FromMilliseconds(100) }));
        var watch = Stopwatch.StartNew();
        await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => new WorksReminderIntentSource(reader)
            .GetCurrentIntentsAsync(new(Tenant, "work", item.Value), TestContext.Current.CancellationToken));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    /// <summary>Repeated abandoned reads retain SDK admission until providers really complete; refusal never invokes more work.</summary>
    [Fact]
    public async Task AbandonedProviderTasksRetainAdmissionAndRecoveryReleasesIt()
    {
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        var release = new TaskCompletionSource<StreamReadPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        int invoked = 0;
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref invoked);
            return release.Task;
        });
        var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions { ContextReadTimeout = TimeSpan.FromMilliseconds(100) }));
        try
        {
            for (int attempt = 0; attempt < 16; attempt++)
                await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => reader.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken));
            invoked.ShouldBe(16);
            var watch = Stopwatch.StartNew();
            var refused = await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => reader.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken));
            refused.InnerException.ShouldBeOfType<Hexalith.EventStore.Client.Streams.AuthoritativeStreamReadAdmissionException>();
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
            invoked.ShouldBe(16);
        }
        finally { release.TrySetResult(Story48Streams.Page(Tenant, "item")); }
        // Provider completion, rather than caller timeout, restores the SDK slots.
        var recovery = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                (await reader.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken)).ShouldBeEmpty();
                break;
            }
            catch (ProcessEvidenceUnavailableException exception) when (exception.InnerException is
                Hexalith.EventStore.Client.Streams.AuthoritativeStreamReadAdmissionException && recovery.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
        invoked.ShouldBe(17);
    }

    /// <summary>Later rescheduling or terminal history cannot mask a corrupt persisted creation witness.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidHistoricalWitnessRemainsQuarantinedBeforeAnyEffects(bool terminal)
    {
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId("corrupt-witness");
        DateTimeOffset due = DateTimeOffset.UnixEpoch.AddDays(1);
        var invalid = new WorkItemExpirySchedule(due, "altered-token", 1);
        var valid = new WorkItemExpirySchedule(due,
            Hexalith.EventStore.Contracts.Reminders.ReminderIdentityCodec.ComputeScheduleToken(Tenant, item.Value, due, 2), 2);
        IEventPayload next = terminal ? new WorkItemCancelled(item.Value, 2, tenant, item)
            : new WorkItemRescheduled(item.Value, 2, tenant, item, new WorkItemSchedule(), Expiry: valid);
        StreamReadPage page = Story48Streams.Page(Tenant, item.Value,
            new WorkItemCreated(item.Value, 1, tenant, item, new Obligation("corruption"), Expiry: invalid), next);
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(page);
        var streams = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
        await Should.ThrowAsync<InvalidOperationException>(() => new WorksReminderIntentSource(streams)
            .GetCurrentIntentsAsync(new(Tenant, "work", item.Value), TestContext.Current.CancellationToken));
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var checkpoints = new ProcessCheckpointStore(store, options, TimeProvider.System);
        var submitter = Substitute.For<ITrustedEffectSubmitter>();
        var runner = new CheckpointedProcessRunner(gateway, new WorksProcessTranslator(streams),
            Substitute.For<IProcessDelegationProvider>(), submitter, checkpoints, options);
        var source = new ProcessSource(Tenant, "work", item.Value);
        (await runner.RunAsync(source, TestContext.Current.CancellationToken)).Status.ShouldBe(ProcessSourceStatus.Quarantined);
        ProcessSourceState retained = await new ProcessCheckpointStore(store, options, TimeProvider.System).GetAsync(source, TestContext.Current.CancellationToken);
        retained.Checkpoint.ShouldBe(0);
        retained.PendingPlan.ShouldBeNull();
        retained.FailureSequence.ShouldBe(1);
        retained.ReasonCode.ShouldBe("invalid-evidence");
        submitter.ReceivedCalls().ShouldBeEmpty();
    }

    private static StreamReadPage Tail(StreamReadPage page, long from, int limit)
    {
        StreamReadEvent[] tail = [.. page.Events.Where(fact => fact.SequenceNumber > from).Take(limit)];
        return page with { Events = tail, Metadata = page.Metadata with
        { FromSequence = from, EventCount = tail.Length, LastSequenceReturned = tail.Length == 0 ? null : tail[^1].SequenceNumber,
            IsTruncated = tail.Length > 0 && tail[^1].SequenceNumber < page.Metadata.LatestSequence } };
    }
}
