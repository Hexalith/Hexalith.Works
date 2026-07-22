using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Contracts.Events;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Hexalith.Works.Recovery.ChildCompletion;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Verifies the child-to-parent stream traversal that rebuilds the parent's current await state.
/// </summary>
public sealed class StreamReadingChildCompletionAwaitingParentSourceTests
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private static readonly TenantId s_tenant = new("tenant-alpha");
    private static readonly WorkItemId s_parent = new("parent-001");
    private static readonly WorkItemId s_child = new("child-001");

    /// <summary>The source reads the child parent reference and then reconstructs the current parent await set.</summary>
    [Fact]
    public async Task Source_rebuilds_current_awaiting_parent_from_two_per_aggregate_streams()
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(PageFor(call.ArgAt<StreamReadRequest>(0).AggregateId)));
        var source = new StreamReadingChildCompletionAwaitingParentSource(
            gateway,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<StreamReadingChildCompletionAwaitingParentSource>.Instance);

        IReadOnlyList<AwaitingParent> parents = await source.GetAwaitingParentsAsync(
            new WorkItemCompleted(s_child.Value, 7, s_tenant, s_child),
            TestContext.Current.CancellationToken);

        AwaitingParent parent = parents.ShouldHaveSingleItem();
        parent.TenantId.ShouldBe(s_tenant);
        parent.WorkItemId.ShouldBe(s_parent);
        parent.AwaitConditions.ShouldBe(
        [
            AwaitCondition.ExternalSignal("approval"),
            AwaitCondition.ChildCompleted(s_child),
        ]);
        await gateway.Received(1).ReadStreamAsync(
            Arg.Is<StreamReadRequest>(value => value != null && value.AggregateId == s_child.Value),
            Arg.Any<CancellationToken>());
        await gateway.Received(1).ReadStreamAsync(
            Arg.Is<StreamReadRequest>(value => value != null && value.AggregateId == s_parent.Value),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A later parent resume clears the re-read await state.</summary>
    [Fact]
    public async Task Source_returns_no_awaiting_parent_after_parent_resumed()
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(PageFor(call.ArgAt<StreamReadRequest>(0).AggregateId, includeResume: true)));
        var source = new StreamReadingChildCompletionAwaitingParentSource(
            gateway,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<StreamReadingChildCompletionAwaitingParentSource>.Instance);

        IReadOnlyList<AwaitingParent> parents = await source.GetAwaitingParentsAsync(
            new WorkItemCompleted(s_child.Value, 7, s_tenant, s_child),
            TestContext.Current.CancellationToken);

        parents.ShouldBeEmpty();
    }

    /// <summary>A foreign-tenant parent reference is rejected before any parent stream read.</summary>
    [Fact]
    public async Task Source_fails_closed_on_cross_tenant_parent_reference()
    {
        var foreignTenant = new TenantId("tenant-beta");
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(PageFor(
                call.ArgAt<StreamReadRequest>(0).AggregateId,
                childParentTenant: foreignTenant)));
        var source = new StreamReadingChildCompletionAwaitingParentSource(
            gateway,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<StreamReadingChildCompletionAwaitingParentSource>.Instance);

        IReadOnlyList<AwaitingParent> parents = await source.GetAwaitingParentsAsync(
            new WorkItemCompleted(s_child.Value, 7, s_tenant, s_child),
            TestContext.Current.CancellationToken);

        parents.ShouldBeEmpty();
        await gateway.Received(1).ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A transient stream failure escapes so the durable subscription marker remains retryable.</summary>
    [Fact]
    public async Task Source_propagates_gateway_failure_for_subscription_retry()
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamReadPage>(new HttpRequestException("gateway unavailable")));
        var source = new StreamReadingChildCompletionAwaitingParentSource(
            gateway,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<StreamReadingChildCompletionAwaitingParentSource>.Instance);

        _ = await Should.ThrowAsync<HttpRequestException>(() => source.GetAwaitingParentsAsync(
            new WorkItemCompleted(s_child.Value, 7, s_tenant, s_child),
            TestContext.Current.CancellationToken));
    }

    private static StreamReadPage PageFor(
        string? aggregateId,
        bool includeResume = false,
        TenantId? childParentTenant = null)
    {
        IEventPayload[] events = aggregateId switch
        {
            var value when value == s_child.Value =>
            [
                new WorkItemCreated(
                    s_child.Value,
                    1,
                    s_tenant,
                    s_child,
                    new Obligation("Child work"),
                    Parent: new ParentWorkItemReference(childParentTenant ?? s_tenant, s_parent)),
            ],
            var value when value == s_parent.Value && includeResume =>
            [
                new WorkItemSuspended(
                    s_parent.Value,
                    4,
                    s_tenant,
                    s_parent,
                    [AwaitCondition.ExternalSignal("approval"), AwaitCondition.ChildCompleted(s_child)]),
                new WorkItemResumed(
                    s_parent.Value,
                    5,
                    s_tenant,
                    s_parent,
                    AwaitCondition.ChildCompleted(s_child)),
            ],
            var value when value == s_parent.Value =>
            [
                new WorkItemSuspended(
                    s_parent.Value,
                    4,
                    s_tenant,
                    s_parent,
                    [AwaitCondition.ExternalSignal("approval"), AwaitCondition.ChildCompleted(s_child)]),
            ],
            _ => [],
        };

        StreamReadEvent[] streamEvents =
        [
            .. events.Select((value, index) => new StreamReadEvent(
                index + 1,
                value.GetType().FullName!,
                JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), s_web),
                "json",
                1,
                $"message-{aggregateId}-{index}",
                $"correlation-{aggregateId}-{index}",
                null,
                new DateTimeOffset(2026, 7, 22, 8, 0, index, TimeSpan.Zero),
                null)),
        ];

        return new StreamReadPage(
            s_tenant.Value,
            WorkCommandSubmission.WorkDomain,
            aggregateId,
            streamEvents,
            new StreamReadMetadata(0, null, streamEvents.Length, streamEvents.Length, streamEvents.Length, false, null));
    }
}
