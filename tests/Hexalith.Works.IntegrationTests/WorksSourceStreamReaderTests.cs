using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Recovery;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Complete contextual stream reads share the SDK page validator and preserve every envelope.</summary>
public sealed class WorksSourceStreamReaderTests
{
    private const string Tenant = "tenant-415";
    private const string Item = "work-415";

    /// <summary>Every state-changing contract requires a positive domain ordinal under both retained JSON casings.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public void RecoveryRejectsNonpositiveDomainSequences(long sequence, bool web)
    {
        IEventPayload[] facts = [.. WorkItemV1Catalog.All.OfType<IEventPayload>()
            .Where(fact => fact.GetType().GetProperty(nameof(WorkItemCreated.Sequence)) is not null)];
        facts.Length.ShouldBe(15);
        foreach (IEventPayload fact in facts)
        {
            var options = web ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions();
            var node = JsonSerializer.SerializeToNode(fact, fact.GetType(), options)!.AsObject();
            node[web ? "sequence" : "Sequence"] = sequence;
            Should.Throw<InvalidOperationException>(() => WorksRecoveryEventDecoder.Decode(fact.GetType().Name,
                JsonSerializer.SerializeToUtf8Bytes(node))).Message.ShouldContain("positive domain sequence");
        }
    }

    /// <summary>Positive domain ordinals need not equal envelope offsets, and rejections have no domain ordinal.</summary>
    [Fact]
    public void RecoveryPreservesPositiveOrdinalsAndUnsequencedRejections()
    {
        foreach (IEventPayload fact in WorkItemV1Catalog.All.OfType<IEventPayload>())
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(fact, fact.GetType());
            WorksRecoveryEventDecoder.Decode(fact.GetType().FullName!, bytes).ShouldBe(fact);
        }
        var created = new WorkItemCreated(Item, 97, new TenantId(Tenant), new WorkItemId(Item), new Obligation("proof"));
        ((WorkItemCreated)WorksRecoveryEventDecoder.Decode(nameof(WorkItemCreated), JsonSerializer.SerializeToUtf8Bytes(created)))
            .Sequence.ShouldBe(97);
    }

    /// <summary>A bad domain ordinal after valid committed evidence prevents any recovery context from escaping.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public async Task InvalidOrdinalAfterValidPrefixRefusesWholeRead(long ordinal, bool paged)
    {
        var created = new WorkItemCreated(Item, 1, new TenantId(Tenant), new WorkItemId(Item), new Obligation("proof"));
        var suspended = new WorkItemSuspended(Item, ordinal, new TenantId(Tenant), new WorkItemId(Item),
            [AwaitCondition.DateReached(DateTimeOffset.UnixEpoch)]);
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (!paged) return Story48Streams.Page(Tenant, Item, created, suspended);
            return call.Arg<StreamReadRequest>().FromSequence == 0
                ? Story48Streams.Page(Tenant, Item, created) with
                { Metadata = new StreamReadMetadata(0, null, 1, 2, 1, true, null) }
                : Story48Streams.PageAt(Tenant, Item, 2, false, suspended);
        });
        IReadOnlyList<WorksCommittedEvent>? context = null;
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () => context = await new WorksSourceStreamReader(gateway,
            Options.Create(new WorksRecoveryOptions())).ReadAsync(Tenant, Item, TestContext.Current.CancellationToken).ConfigureAwait(false));
        exception.Message.ShouldContain("positive domain sequence");
        context.ShouldBeNull();
        if (paged)
            await gateway.Received(1).ReadWorkloadStreamAsync(Arg.Is<StreamReadRequest>(request => request.FromSequence == 1), Arg.Any<CancellationToken>());
    }

    /// <summary>Envelope coordinates remain authoritative when a rejection makes payload ordinals diverge.</summary>
    [Fact]
    public async Task EnvelopePositionsAndMessageIdsSurvivePageBoundaries()
    {
        var created = new WorkItemCreated(Item, 1, new TenantId(Tenant), new WorkItemId(Item), new Obligation("proof"));
        var suspended = new WorkItemSuspended(Item, 2, new TenantId(Tenant), new WorkItemId(Item),
            [AwaitCondition.DateReached(DateTimeOffset.UnixEpoch)]);
        var rejection = new Hexalith.Works.Contracts.Events.Rejections.WorkItemTransitionRejected(
            new TenantId(Tenant), new WorkItemId(Item), WorkItemStatus.Created, "invalid-act");
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                long from = call.Arg<StreamReadRequest>().FromSequence;
                StreamReadPage page = from == 0
                    ? Story48Streams.Page(Tenant, Item, created, rejection)
                    : Story48Streams.PageAt(Tenant, Item, 3, false, suspended);
                if (from > 0) page = page with { Events = [page.Events[0] with { MessageId = "committed-second-page-suspension" }] };
                return Task.FromResult(page with
                {
                    Metadata = page.Metadata with { LatestSequence = 3, IsTruncated = from == 0 },
                });
            });
        var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));

        IReadOnlyList<WorksCommittedEvent> events = await reader.ReadAsync(Tenant, Item, TestContext.Current.CancellationToken);

        events.Count.ShouldBe(3);
        events[^1].Envelope.SequenceNumber.ShouldBe(3);
        ((WorkItemSuspended)events[^1].Payload).Sequence.ShouldBe(2);
        events[0].Envelope.MessageId.ShouldBe("message-work-415-0");
        events[^1].Envelope.MessageId.ShouldBe("committed-second-page-suspension");
        await gateway.Received(1).ReadWorkloadStreamAsync(
            Arg.Is<StreamReadRequest>(request => request.FromSequence == 2), Arg.Any<CancellationToken>());
    }

    /// <summary>Every exclusive page boundary preserves rejection offsets and the final current date await.</summary>
    /// <param name="pageSize">The number of committed envelopes returned by each fake gateway page.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MultipleExclusivePageBoundariesPreserveEveryCommittedEnvelope(int pageSize)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var tenant = new TenantId(Tenant);
        var item = new WorkItemId(Item);
        var firstDate = AwaitCondition.DateReached(DateTimeOffset.UnixEpoch);
        var nextDate = AwaitCondition.DateReached(DateTimeOffset.UnixEpoch.AddDays(1));
        IEventPayload[] facts = [new WorkItemCreated(Item, 1, tenant, item, new Obligation("paged recovery")),
            new Hexalith.Works.Contracts.Events.Rejections.WorkItemTransitionRejected(tenant, item, WorkItemStatus.Created, "invalid-act"),
            new WorkItemSuspended(Item, 2, tenant, item, [firstDate]),
            new WorkItemResumed(Item, 3, tenant, item, firstDate),
            new WorkItemSuspended(Item, 4, tenant, item, [nextDate])];
        var cursors = new List<long>();
        var gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<StreamReadRequest>().ContinuationToken.ShouldBeNull();
            long from = call.Arg<StreamReadRequest>().FromSequence;
            cursors.Add(from);
            int offset = checked((int)from);
            IEventPayload[] selected = facts.Skip(offset).Take(pageSize).ToArray();
            StreamReadPage page = Story48Streams.PageAt(Tenant, Item, from + 1,
                offset + selected.Length < facts.Length, selected);
            return page with
            {
                Events = [.. page.Events.Select(envelope => envelope with { MessageId = $"committed-{envelope.SequenceNumber}" })],
                Metadata = page.Metadata with
                {
                    LatestSequence = facts.Length,
                    NextContinuationToken = page.Metadata.IsTruncated ? new ReplayContinuationToken($"opaque-{from}") : null,
                },
            };
        });

        var reader = new WorksSourceStreamReader(gateway, Options.Create(new WorksRecoveryOptions()));
        IReadOnlyList<WorksCommittedEvent> result = await reader.ReadAsync(Tenant, Item, ct);

        result.Select(entry => entry.Envelope.SequenceNumber).ShouldBe([1L, 2L, 3L, 4L, 5L]);
        result.Select(entry => entry.Envelope.MessageId).ShouldBe(Enumerable.Range(1, 5).Select(index => $"committed-{index}"));
        result.Select(entry => entry.Payload).ShouldBe(facts);
        cursors.ShouldBe(pageSize == 1 ? [0L, 1L, 2L, 3L, 4L] : [0L, 2L, 4L]);
        var intents = await new Hexalith.Works.Reminders.WorksReminderIntentSource(reader)
            .GetCurrentIntentsAsync(new Hexalith.EventStore.Contracts.Reminders.ReminderTarget(Tenant, "work", Item), ct);
        intents.ShouldHaveSingleItem().DueUtc.ShouldBe(nextDate.Instant!.Value);
    }

    /// <summary>Bad scope, positions, metadata, formats, payloads, or omitted heads never become partial context.</summary>
    [Theory]
    [InlineData("foreign-page")]
    [InlineData("foreign-payload")]
    [InlineData("gap")]
    [InlineData("count")]
    [InlineData("format")]
    [InlineData("unknown")]
    [InlineData("malformed")]
    [InlineData("missing-head")]
    public async Task InvalidEvidenceFailsClosed(string failure)
    {
        var fact = new WorkItemCreated(Item, 1,
            new TenantId(failure == "foreign-payload" ? "foreign-tenant" : Tenant),
            new WorkItemId(Item), new Obligation("proof"));
        StreamReadPage page = Story48Streams.Page(Tenant, Item, fact);
        page = failure switch
        {
            "foreign-page" => page with { Tenant = "foreign-tenant" },
            "gap" => page with
            {
                Events = [page.Events[0] with { SequenceNumber = 2 }],
                Metadata = page.Metadata with { LastSequenceReturned = 2, LatestSequence = 2 }
            },
            "count" => page with { Metadata = page.Metadata with { EventCount = 0 } },
            "format" => page with { Events = [page.Events[0] with { SerializationFormat = "binary" }] },
            "unknown" => page with { Events = [page.Events[0] with { EventTypeName = "UnknownWorksFact" }] },
            "malformed" => page with { Events = [page.Events[0] with { Payload = [123] }] },
            "missing-head" => page with { Metadata = page.Metadata with { LatestSequence = 2 } },
            _ => page,
        };
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>()).Returns(page);

        await Should.ThrowAsync<InvalidOperationException>(() => new WorksSourceStreamReader(gateway,
            Options.Create(new WorksRecoveryOptions())).ReadAsync(Tenant, Item, TestContext.Current.CancellationToken));
    }

    /// <summary>Ordinary dependency outages are typed separately from retained corrupt source evidence.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyOutageUsesRetryableContract(bool gatewayFailure)
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamReadPage>(gatewayFailure
                ? new EventStoreGatewayException(503, "unavailable")
                : new HttpRequestException("unavailable")));

        await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => new WorksSourceStreamReader(gateway,
            Options.Create(new WorksRecoveryOptions())).ReadAsync(Tenant, Item, TestContext.Current.CancellationToken));
    }
    /// <summary>Gateway proof and workload authority outages remain retryable contextual evidence failures.</summary>
    [Theory]
    [InlineData("EventStore stream read proof is unavailable.")]
    [InlineData("EventStore workload credentials are unavailable.")]
    public async Task GatewayAuthorityOutageUsesRetryableContract(string message)
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamReadPage>(new InvalidOperationException(message)));

        await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => new WorksSourceStreamReader(gateway,
            Options.Create(new WorksRecoveryOptions())).ReadAsync(Tenant, Item, TestContext.Current.CancellationToken));
    }

    /// <summary>Workload assertion and proof denials describe unavailable authority, not corrupt stored evidence.</summary>
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task GatewayAuthorityDenialUsesRetryableContract(int status)
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway.ReadWorkloadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamReadPage>(new EventStoreGatewayException(status, "authority-unavailable")));

        await Should.ThrowAsync<ProcessEvidenceUnavailableException>(() => new WorksSourceStreamReader(gateway,
            Options.Create(new WorksRecoveryOptions())).ReadAsync(Tenant, Item, TestContext.Current.CancellationToken));
    }

    /// <summary>A rejection that the aggregate cannot emit must not become a checkpointed recovery no-op.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidInitialEffortRejectionFailsClosed(bool missingDone)
    {
        var fact = new Hexalith.Works.Contracts.Events.Rejections.WorkItemInitialEffortRejected(
            new TenantId(Tenant), new WorkItemId(Item), 0);
        byte[] bytes = missingDone
            ? JsonSerializer.SerializeToUtf8Bytes(new { fact.TenantId, fact.WorkItemId })
            : JsonSerializer.SerializeToUtf8Bytes(fact);

        Should.Throw<InvalidOperationException>(() => WorksRecoveryEventDecoder.Decode(nameof(Hexalith.Works.Contracts.Events.Rejections.WorkItemInitialEffortRejected), bytes));
    }

}
