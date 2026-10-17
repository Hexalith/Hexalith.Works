using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Processes;
using Hexalith.EventStore.Contracts.Reminders;
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

/// <summary>Canonical committed-evidence regressions for iteration four.</summary>
[Collection(WorksAppHostTestCollection.Name)]
public sealed class WorksSdkIteration4Tests
{
    private const string Tenant = "tenant-415";
    private static readonly TenantId s_tenant = new(Tenant);
    private static readonly WorkItemId s_item = new("item");

    /// <summary>Missing required references cannot become acknowledged no-ops through either recovery seam.</summary>
    [Theory]
    [InlineData("created", "Obligation", false)]
    [InlineData("created", "Obligation", true)]
    [InlineData("child", "Obligation", false)]
    [InlineData("child", "ChildWorkItemId", true)]
    [InlineData("rescheduled", "Schedule", false)]
    [InlineData("assigned", "Binding", true)]
    [InlineData("suspended", "AwaitConditions", false)]
    [InlineData("suspended", "AwaitConditions", true)]
    [InlineData("suspended-empty", "AwaitConditions", false)]
    [InlineData("rejected", "Requeue", true)]
    [InlineData("rejected", "Requeue", false)]
    [InlineData("rejected-string", "Requeue", false)]
    [InlineData("progress", "Unit", false)]
    [InlineData("reestimated", "Unit", true)]
    [InlineData("conversation", "ConversationCorrelationId", false)]
    [InlineData("rejection", "AttemptedAct", true)]
    public async Task MalformedRequiredShapeIsDurableInvalidEvidence(string kind, string field, bool missing)
    {
        IEventPayload payload = kind switch
        {
            "created" => Created(),
            "child" => new ChildSpawned("item", 1, s_tenant, s_item, new("child"), new("child")),
            "rescheduled" => new WorkItemRescheduled("item", 1, s_tenant, s_item, new()),
            "assigned" => new WorkItemAssigned("item", 1, s_tenant, s_item, new(new("party"), Channel.Mcp, AuthorityLevel.Contribute)),
            "suspended" or "suspended-empty" => new WorkItemSuspended("item", 1, s_tenant, s_item, [AwaitCondition.DateReached(DateTimeOffset.UnixEpoch)]),
            "progress" => new ProgressReported("item", 1, s_tenant, s_item, 1, new("hours")),
            "reestimated" => new ReEstimated("item", 1, s_tenant, s_item, 2, new("hours")),
            "rejected" or "rejected-string" => new WorkItemRejected("item", 1, s_tenant, s_item, true),
            "conversation" => new ConversationLinked("item", 1, s_tenant, s_item, new("conversation")),
            _ => new Hexalith.Works.Contracts.Events.Rejections.WorkItemTransitionRejected(s_tenant, s_item, WorkItemStatus.Created, "proof"),
        };
        JsonObject json = JsonSerializer.SerializeToNode(payload, payload.GetType())!.AsObject();
        if (missing) json.Remove(field);
        else if (kind == "suspended-empty") json[field] = new JsonArray();
        else if (kind == "rejected-string") json[field] = "false";
        else json[field] = null;
        StreamReadPage page = Story48Streams.Page(Tenant, "item", payload);
        page = page with { Events = [page.Events[0] with { Payload = Encoding.UTF8.GetBytes(json.ToJsonString()) }] };
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(page);
        var streams = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
        await Should.ThrowAsync<InvalidOperationException>(() => streams.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken));
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var submitter = Substitute.For<ITrustedEffectSubmitter>();
        var runner = Runner(gateway, streams, store, options, submitter);
        var source = new ProcessSource(Tenant, "work", "item");
        (await runner.RunAsync(source, TestContext.Current.CancellationToken)).Status.ShouldBe(ProcessSourceStatus.Quarantined);
        var state = await new ProcessCheckpointStore(store, options, TimeProvider.System).GetAsync(source, TestContext.Current.CancellationToken);
        state.Checkpoint.ShouldBe(0);
        state.FailureSequence.ShouldBe(1);
        state.ReasonCode.ShouldBe("invalid-evidence");
        state.PendingPlan.ShouldBeNull();
        submitter.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Recovery quarantines undefined rejection states while defined Unknown and historical readers keep their meanings.</summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(999, false)]
    [InlineData((int)WorkItemStatus.Unknown, true)]
    [InlineData((int)WorkItemStatus.Created, true)]
    [InlineData((int)WorkItemStatus.Expired, true)]
    public async Task TransitionRejectionRequiresDefinedStatusOnlyInRecovery(int status, bool valid)
    {
        var payload = new Hexalith.Works.Contracts.Events.Rejections.WorkItemTransitionRejected(
            s_tenant, s_item, (WorkItemStatus)status, "proof");
        StreamReadPage page = Story48Streams.Page(Tenant, "item", payload);
        WorksEventDecoder.Decode(page.Events[0].EventTypeName, page.Events[0].Payload)
            .ShouldBeOfType<Hexalith.Works.Contracts.Events.Rejections.WorkItemTransitionRejected>()
            .FromStatus.ShouldBe((WorkItemStatus)status);
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Tail(page, call.Arg<StreamReadRequest>()));
        var streams = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
        if (valid) (await streams.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        else await Should.ThrowAsync<InvalidOperationException>(() => streams.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken));
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var submitter = Substitute.For<ITrustedEffectSubmitter>();
        var source = new ProcessSource(Tenant, "work", "item");
        (await Runner(gateway, streams, store, options, submitter).RunAsync(source, TestContext.Current.CancellationToken))
            .Status.ShouldBe(valid ? ProcessSourceStatus.Active : ProcessSourceStatus.Quarantined);
        var retained = await new ProcessCheckpointStore(store, options, TimeProvider.System).GetAsync(source, TestContext.Current.CancellationToken);
        retained.Checkpoint.ShouldBe(valid ? 1 : 0);
        if (!valid)
        {
            retained.Status.ShouldBe(ProcessSourceStatus.Quarantined);
            retained.FailureSequence.ShouldBe(1);
            retained.ReasonCode.ShouldBe("invalid-evidence");
        }
        retained.PendingPlan.ShouldBeNull();
        submitter.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Only actual short/full Works names are accepted; a foreign name cannot turn a fact into a checkpointed no-op.</summary>
    [Theory]
    [InlineData("short", true)]
    [InlineData("full", true)]
    [InlineData("foreign", false)]
    public async Task RecoveryEventTypeNamesAreClosed(string form, bool valid)
    {
        var page = Story48Streams.Page(Tenant, "item", Created());
        string name = form == "short" ? nameof(WorkItemCreated) : form == "full" ? typeof(WorkItemCreated).FullName! : "Foreign.Domain.WorkItemCreated";
        page = page with { Events = [page.Events[0] with { EventTypeName = name }] };
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call => Tail(page, call.Arg<StreamReadRequest>()));
        var streams = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
        if (valid) (await streams.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        else await Should.ThrowAsync<InvalidOperationException>(() => streams.ReadAsync(Tenant, "item", TestContext.Current.CancellationToken));
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var submitter = Substitute.For<ITrustedEffectSubmitter>();
        var source = new ProcessSource(Tenant, "work", "item");
        (await Runner(gateway, streams, store, options, submitter).RunAsync(source, TestContext.Current.CancellationToken))
            .Status.ShouldBe(valid ? ProcessSourceStatus.Active : ProcessSourceStatus.Quarantined);
        var retained = await new ProcessCheckpointStore(store, options, TimeProvider.System).GetAsync(source, TestContext.Current.CancellationToken);
        retained.Checkpoint.ShouldBe(valid ? 1 : 0);
        if (!valid) { retained.ReasonCode.ShouldBe("invalid-evidence"); retained.FailureSequence.ShouldBe(1); }
        retained.PendingPlan.ShouldBeNull();
        submitter.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Historical singular suspensions and explicit rejection booleans retain their existing meanings.</summary>
    [Theory]
    [InlineData("singular", false)]
    [InlineData("rejected", false)]
    [InlineData("rejected", true)]
    public async Task HistoricalCommittedDiscriminatorsRemainReadable(string kind, bool requeue)
    {
        IEventPayload payload = kind == "singular"
            ? new WorkItemSuspended("item", 1, s_tenant, s_item, awaitCondition: AwaitCondition.DateReached(DateTimeOffset.UnixEpoch))
            : new WorkItemRejected("item", 1, s_tenant, s_item, requeue);
        JsonObject json = JsonSerializer.SerializeToNode(payload, payload.GetType())!.AsObject();
        if (kind == "singular") json.Remove("AwaitConditions");
        // CamelCase is a supported historical transport spelling too.
        if (kind == "rejected") { json.Remove("Requeue"); json["requeue"] = requeue; }
        var decoded = WorksRecoveryEventDecoder.Decode(payload.GetType().FullName!, Encoding.UTF8.GetBytes(json.ToJsonString()));
        if (decoded is WorkItemSuspended suspended) suspended.AwaitConditions.ShouldHaveSingleItem();
        else ((WorkItemRejected)decoded).Requeue.ShouldBe(requeue);
    }

    /// <summary>Statelessly valid tokens do not permit rolled-back, removed or stale committed schedules.</summary>
    [Theory]
    [InlineData("equal", false)]
    [InlineData("decreasing", false)]
    [InlineData("drop", false)]
    [InlineData("stale-expired", false)]
    [InlineData("replacement", true)]
    [InlineData("scheduled-expired", true)]
    [InlineData("cascade-expired", true)]
    [InlineData("historical", true)]
    public async Task PersistedExpiryTransitionsUseThePureDomainRules(string transition, bool valid)
    {
        WorkItemExpirySchedule original = Schedule(2);
        IEventPayload next = transition switch
        {
            "stale-expired" => new WorkItemExpired("item", 2, s_tenant, s_item, Schedule(1)),
            "scheduled-expired" => new WorkItemExpired("item", 2, s_tenant, s_item, original),
            "cascade-expired" or "historical" => new WorkItemExpired("item", 2, s_tenant, s_item),
            _ => new WorkItemRescheduled("item", 2, s_tenant, s_item, new(), Expiry: transition == "drop" ? null
                : Schedule(transition == "decreasing" ? 1 : transition == "equal" ? 2 : 3)),
        };
        StreamReadPage page = Story48Streams.Page(Tenant, "item", Created(transition == "historical" ? null : original), next);
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call => Tail(page, call.Arg<StreamReadRequest>()));
        var streams = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
        var reminder = new WorksReminderIntentSource(streams);
        var target = new ReminderTarget(Tenant, "work", "item");
        if (!valid) await Should.ThrowAsync<InvalidOperationException>(() => reminder.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken));
        else
        {
            var intents = await reminder.GetCurrentIntentsAsync(target, TestContext.Current.CancellationToken);
            intents.Count.ShouldBe(transition == "replacement" ? 1 : 0);
        }
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var submitter = Substitute.For<ITrustedEffectSubmitter>();
        var source = new ProcessSource(Tenant, "work", "item");
        var result = await Runner(gateway, streams, store, options, submitter).RunAsync(source, TestContext.Current.CancellationToken);
        result.Status.ShouldBe(valid ? ProcessSourceStatus.Active : ProcessSourceStatus.Quarantined);
        var state = await new ProcessCheckpointStore(store, options, TimeProvider.System).GetAsync(source, TestContext.Current.CancellationToken);
        state.Checkpoint.ShouldBe(valid ? 2 : 1);
        if (!valid) { state.FailureSequence.ShouldBe(2); state.ReasonCode.ShouldBe("invalid-evidence"); }
        submitter.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Only a canonical missing related child retries; reconstruction converges with a receipt after separate creation.</summary>
    [Fact]
    public async Task CanonicalMissingRelatedChildRetriesThenConvergesAfterCreation()
    {
        var parent = new WorkItemId("parent");
        var child = new WorkItemId("child");
        StreamReadPage parentPage = Story48Streams.Page(Tenant, parent.Value,
            new WorkItemCreated(parent.Value, 1, s_tenant, parent, new("parent")),
            new ChildSpawned(parent.Value, 2, s_tenant, parent, child, new("child")),
            new WorkItemCancelled(parent.Value, 3, s_tenant, parent));
        bool exists = false;
        using var http = new HttpClient(new WorksSdkHttpEvidenceHandler(async (request, token) =>
        {
            StreamReadRequest read = (await request.Content!.ReadFromJsonAsync<StreamReadRequest>(cancellationToken: token).ConfigureAwait(false))!;
            if (read.AggregateId != parent.Value && (read.AggregateId != child.Value || !exists))
                return new(HttpStatusCode.NotFound) { Content = JsonContent.Create(new
                { type = "https://tools.ietf.org/html/rfc9110#section-15.5.5", title = "Not Found", status = 404,
                    detail = "Event stream does not exist.", reasonCode = StreamReplayReasonCodes.MissingStream }) };
            StreamReadPage page = read.AggregateId == parent.Value ? parentPage : Story48Streams.Page(Tenant, child.Value,
                new WorkItemCreated(child.Value, 1, s_tenant, child, new("child"), Parent: new(s_tenant, parent)));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Tail(page, read)) };
        })) { BaseAddress = new("https://eventstore.test/") };
        IEventStoreGatewayClient gateway = Gateway(http);
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions { RetryLimit = 1 });
        var submitter = Substitute.For<ITrustedEffectSubmitter>();
        submitter.SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TrustedEffectResult(EffectIdentityCodec.ComputeEffectId(call.Arg<TrustedEffectSubmission>().Identity), TrustedEffectDisposition.Success, false, null)));
        var source = new ProcessSource(Tenant, "work", parent.Value);
        var streams = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
        (await Runner(gateway, streams, store, options, submitter).RunAsync(source, TestContext.Current.CancellationToken)).Status.ShouldBe(ProcessSourceStatus.Parked);
        var checkpoints = new ProcessCheckpointStore(store, options, TimeProvider.System);
        var retained = await checkpoints.GetAsync(source, TestContext.Current.CancellationToken);
        retained.Checkpoint.ShouldBe(2);
        retained.FailureSequence.ShouldBe(3);
        retained.PendingPlan.ShouldBeNull();
        submitter.ReceivedCalls().ShouldBeEmpty();
        exists = true;
        // Authenticated, audited SDK disposition reactivates the parked source through the same validator.
        await checkpoints.EnsureCandidateAsync(source, cancellationToken: TestContext.Current.CancellationToken);
        var authorizer = Substitute.For<IProcessDispositionAuthorizer>();
        authorizer.AuthorizeAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>(), source, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        var audit = Substitute.For<IProcessDispositionAuditSink>();
        var disposition = new ProcessDispositionService(authorizer, audit, checkpoints, new ProcessSweepStore(store, options, TimeProvider.System), options);
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("sub", "synthetic-operator")], "test"));
        (await disposition.RevalidateAsync(principal, source, "related child created", TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await Runner(gateway, streams, store, options, submitter).RunAsync(source, TestContext.Current.CancellationToken)).Status.ShouldBe(ProcessSourceStatus.Active);
        (await new ProcessCheckpointStore(store, options, TimeProvider.System).GetAsync(source, TestContext.Current.CancellationToken)).Checkpoint.ShouldBe(3);
        await submitter.Received(1).SubmitAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<TrustedEffectContext>(), Arg.Any<CancellationToken>());
        // A missing source is authoritative corruption, even with the same canonical error.
        await Should.ThrowAsync<EventStoreGatewayException>(() => streams.ReadAsync(Tenant, "absent", TestContext.Current.CancellationToken));
    }

    /// <summary>The actual SDK client bounds transport separately from the smaller retained Works context allowance.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpTransportAndRetainedContextHaveIndependentFiniteBounds(bool overflow)
    {
        StreamReadPage page = Story48Streams.Page(Tenant, "item",
            new WorkItemCreated("item", 1, s_tenant, s_item, new(new string('x', 6000))),
            new ChildSpawned("item", 2, s_tenant, s_item, new("child"), new("child")),
            new WorkItemCancelled("item", 3, s_tenant, s_item));
        using var http = new HttpClient(new WorksSdkHttpEvidenceHandler(async (request, token) =>
        {
            StreamReadRequest read = (await request.Content!.ReadFromJsonAsync<StreamReadRequest>(cancellationToken: token).ConfigureAwait(false))!;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Tail(page, read)) };
        })) { BaseAddress = new("https://eventstore.test/") };
        var clientOptions = new EventStoreGatewayClientOptions();
        if (overflow) clientOptions.MaxStreamReadResponseBytes = 4096;
        IEventStoreGatewayClient gateway = Gateway(http, clientOptions);
        http.MaxResponseContentBufferSize.ShouldBe(overflow ? 4096 : 16 * 1024 * 1024);
        var streams = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions { MaxContextBytes = 1024 }));
        await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => new WorksReminderIntentSource(streams)
            .GetCurrentIntentsAsync(new(Tenant, "work", "item"), TestContext.Current.CancellationToken));
        var store = new InMemoryReadModelStore();
        var options = Options.Create(new EventStoreProcessOptions());
        var submitter = Substitute.For<ITrustedEffectSubmitter>();
        var source = new ProcessSource(Tenant, "work", "item");
        (await Runner(gateway, streams, store, options, submitter).RunAsync(source, TestContext.Current.CancellationToken)).Status.ShouldBe(ProcessSourceStatus.Retrying);
        ProcessSourceState retained = await new ProcessCheckpointStore(store, options, TimeProvider.System).GetAsync(source, TestContext.Current.CancellationToken);
        retained.Status.ShouldBe(ProcessSourceStatus.Retrying);
        retained.Checkpoint.ShouldBe(overflow ? 0 : 2);
        retained.PendingPlan.ShouldBeNull();
        retained.ReasonCode.ShouldNotBeNullOrWhiteSpace();
        submitter.ReceivedCalls().ShouldBeEmpty();
    }

    private static WorkItemCreated Created(WorkItemExpirySchedule? expiry = null) => new("item", 1, s_tenant, s_item, new("proof"), Expiry: expiry);

    private static WorkItemExpirySchedule Schedule(long revision)
        => new(DateTimeOffset.UnixEpoch.AddDays(revision), ReminderIdentityCodec.ComputeScheduleToken(Tenant, "item", DateTimeOffset.UnixEpoch.AddDays(revision), revision), revision);

    private static IEventStoreGatewayClient Gateway(HttpClient http, EventStoreGatewayClientOptions? options = null)
    {
        var assertions = Substitute.For<IEventStoreGatewayWorkloadAssertionSource>();
        assertions.IssueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<string?>("synthetic-bound-assertion"));
        return new EventStoreGatewayClient(http, Options.Create(options ?? new()), assertions);
    }

    private static CheckpointedProcessRunner Runner(IEventStoreGatewayClient gateway, WorksSourceStreamReader streams, InMemoryReadModelStore store,
        IOptions<EventStoreProcessOptions> options, ITrustedEffectSubmitter submitter)
    {
        var delegation = Substitute.For<IProcessDelegationProvider>();
        delegation.CreateAsync(Arg.Any<TrustedEffectSubmission>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TrustedEffectContext("works", "works.cascade-cancel", call.Arg<string>(), "synthetic")));
        return new(gateway, new WorksProcessTranslator(streams), delegation, submitter, new(store, options, TimeProvider.System), options);
    }

    private static StreamReadPage Tail(StreamReadPage page, StreamReadRequest read)
    {
        StreamReadEvent[] events = [.. page.Events.Where(fact => fact.SequenceNumber > read.FromSequence).Take(read.PageSize)];
        return page with { Events = events, Metadata = page.Metadata with { FromSequence = read.FromSequence, EventCount = events.Length,
            LastSequenceReturned = events.Length == 0 ? null : events[^1].SequenceNumber, IsTruncated = events.Length > 0 && events[^1].SequenceNumber < page.Metadata.LatestSequence } };
    }
}
