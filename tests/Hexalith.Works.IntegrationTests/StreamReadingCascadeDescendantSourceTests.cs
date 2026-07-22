using System.Text.Json;

using Hexalith.EventStore.Client.Gateway;
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Contracts.Streams;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Models;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Projections;
using Hexalith.Works.Reactor;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Verifies cascade descendant discovery reads persisted per-item terminal status fail-closed.
/// </summary>
public sealed class StreamReadingCascadeDescendantSourceTests
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private static readonly TenantId s_tenant = new("tenant-alpha");
    private static readonly WorkItemId s_parent = new("parent-001");
    private static readonly WorkItemId s_terminalChild = new("child-terminal");
    private static readonly WorkItemId s_activeChild = new("child-active");
    private static readonly WorkItemId s_missingChild = new("child-missing");

    /// <summary>Terminal roll-ups are skipped while active and missing roll-ups remain cascade targets.</summary>
    [Fact]
    public async Task Descendants_use_persisted_rollup_terminality_and_treat_missing_as_active()
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateParentPage());

        IReadModelStore store = Substitute.For<IReadModelStore>();
        store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(s_tenant.Value, s_terminalChild.Value),
                Arg.Any<CancellationToken>())
            .Returns(new ReadModelEntry<WorkItemRollUp>(CreateRollUp(s_terminalChild, WorkItemStatus.Cancelled), "terminal-etag"));
        store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(s_tenant.Value, s_activeChild.Value),
                Arg.Any<CancellationToken>())
            .Returns(new ReadModelEntry<WorkItemRollUp>(CreateRollUp(s_activeChild, WorkItemStatus.InProgress), "active-etag"));
        store
            .GetAsync<WorkItemRollUp>(
                WorksReadModelKeys.StateStoreName,
                WorksReadModelKeys.RollUpKey(s_tenant.Value, s_missingChild.Value),
                Arg.Any<CancellationToken>())
            .Returns(new ReadModelEntry<WorkItemRollUp>(null, null));

        var source = new StreamReadingCascadeDescendantSource(
            gateway,
            store,
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<StreamReadingCascadeDescendantSource>.Instance);

        IReadOnlyList<CascadeDescendant> descendants = await source.GetDescendantsAsync(
            s_tenant.Value,
            s_parent.Value,
            TestContext.Current.CancellationToken);

        descendants.Count.ShouldBe(3);
        descendants.Single(value => value.WorkItemId == s_terminalChild).IsTerminal.ShouldBeTrue();
        descendants.Single(value => value.WorkItemId == s_activeChild).IsTerminal.ShouldBeFalse();
        descendants.Single(value => value.WorkItemId == s_missingChild).IsTerminal.ShouldBeFalse();
    }

    /// <summary>A transient parent-stream failure escapes so the live subscription can retry the event.</summary>
    [Fact]
    public async Task Descendant_stream_failure_propagates_for_subscription_retry()
    {
        IEventStoreGatewayClient gateway = Substitute.For<IEventStoreGatewayClient>();
        gateway
            .ReadStreamAsync(Arg.Any<StreamReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StreamReadPage>(new HttpRequestException("gateway unavailable")));
        var source = new StreamReadingCascadeDescendantSource(
            gateway,
            Substitute.For<IReadModelStore>(),
            Options.Create(new WorksRecoveryOptions()),
            NullLogger<StreamReadingCascadeDescendantSource>.Instance);

        _ = await Should.ThrowAsync<HttpRequestException>(() => source.GetDescendantsAsync(
            s_tenant.Value,
            s_parent.Value,
            TestContext.Current.CancellationToken));
    }

    private static StreamReadPage CreateParentPage()
    {
        ChildSpawned[] events =
        [
            CreateSpawn(1, s_terminalChild),
            CreateSpawn(2, s_activeChild),
            CreateSpawn(3, s_missingChild),
        ];
        StreamReadEvent[] streamEvents =
        [
            .. events.Select(value => new StreamReadEvent(
                value.Sequence,
                typeof(ChildSpawned).FullName!,
                JsonSerializer.SerializeToUtf8Bytes(value, s_web),
                "json",
                1,
                $"message-{value.Sequence}",
                $"correlation-{value.Sequence}",
                null,
                new DateTimeOffset(2026, 7, 22, 8, 0, checked((int)value.Sequence), TimeSpan.Zero),
                null)),
        ];

        return new StreamReadPage(
            s_tenant.Value,
            WorkCommandSubmission.WorkDomain,
            s_parent.Value,
            streamEvents,
            new StreamReadMetadata(0, null, 3, 3, 3, IsTruncated: false, NextContinuationToken: null));
    }

    private static ChildSpawned CreateSpawn(long sequence, WorkItemId child)
    {
        return new ChildSpawned(
            s_parent.Value,
            sequence,
            s_tenant,
            s_parent,
            child,
            new Obligation($"Spawn {child.Value}"));
    }

    private static WorkItemRollUp CreateRollUp(WorkItemId workItemId, WorkItemStatus status)
    {
        return new WorkItemRollUp(
            s_tenant,
            workItemId,
            status,
            null,
            null,
            null,
            [],
            [],
            0,
            1);
    }
}
