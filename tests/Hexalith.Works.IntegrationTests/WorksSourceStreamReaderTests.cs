using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Processes;
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
            "gap" => page with { Events = [page.Events[0] with { SequenceNumber = 2 }],
                Metadata = page.Metadata with { LastSequenceReturned = 2, LatestSequence = 2 } },
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
}
