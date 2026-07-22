using Hexalith.EventStore.Client.Subscriptions;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Verifies that live terminal-event consumers are mechanical adapters over <see cref="CascadeDispatcher"/>.
/// </summary>
public sealed class TerminalCascadeEventHandlerTests
{
    private static readonly TenantId s_tenant = new("tenant-alpha");
    private static readonly WorkItemId s_parent = new("parent-001");
    private static readonly WorkItemId s_child = new("child-001");

    /// <summary>Verifies a consumed cancellation drives one cancel cascade submission.</summary>
    [Fact]
    public async Task Cancelled_event_drives_existing_cascade_dispatcher()
    {
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        WorkItemCancelledCascadeHandler handler = new(CreateDispatcher(submitter));

        await handler.HandleAsync(
            new WorkItemCancelled(s_parent.Value, 7, s_tenant, s_parent),
            CreateContext(7),
            TestContext.Current.CancellationToken);

        await submitter.Received(1).SubmitAsync(
            Arg.Is<WorkCommandSubmission>(value => value != null && value.AggregateId == s_child.Value && value.CommandType == "CancelWorkItem"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies a consumed expiration drives one expire cascade submission.</summary>
    [Fact]
    public async Task Expired_event_drives_existing_cascade_dispatcher()
    {
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        WorkItemExpiredCascadeHandler handler = new(CreateDispatcher(submitter));

        await handler.HandleAsync(
            new WorkItemExpired(s_parent.Value, 9, s_tenant, s_parent),
            CreateContext(9),
            TestContext.Current.CancellationToken);

        await submitter.Received(1).SubmitAsync(
            Arg.Is<WorkCommandSubmission>(value => value != null && value.AggregateId == s_child.Value && value.CommandType == "ExpireWorkItem"),
            Arg.Any<CancellationToken>());
    }

    private static CascadeDispatcher CreateDispatcher(IWorkCommandSubmitter submitter)
    {
        ICascadeCheckpointStore checkpointStore = Substitute.For<ICascadeCheckpointStore>();
        checkpointStore
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CascadeCheckpoint?>(null));
        checkpointStore
            .SaveAsync(Arg.Any<CascadeCheckpoint>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        ICascadeDescendantSource source = Substitute.For<ICascadeDescendantSource>();
        source
            .GetDescendantsAsync(s_tenant.Value, s_parent.Value, Arg.Any<CancellationToken>())
            .Returns([new CascadeDescendant(s_tenant, s_child, IsTerminal: false)]);

        return new CascadeDispatcher(checkpointStore, source, submitter, NullLogger<CascadeDispatcher>.Instance);
    }

    private static EventStoreDomainEventContext CreateContext(long sequence)
    {
        return new EventStoreDomainEventContext(
            s_tenant.Value,
            s_parent.Value,
            "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            sequence,
            new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero),
            $"story-4-7-{sequence}");
    }
}
