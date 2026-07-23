using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
using Hexalith.Works.Reactor;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Verifies the cascade dispatcher's operational pacing knob is bounded and never drops or duplicates a target.
/// </summary>
public sealed class CascadeDispatcherPacingTests
{
    private static readonly TenantId s_tenant = new("tenant-alpha");
    private static readonly WorkItemId s_parent = new("parent-001");

    /// <summary>A configured interval above the supported maximum is clamped and the clamp is recorded.</summary>
    [Fact]
    public void Configured_interval_above_maximum_is_clamped()
    {
        var logger = new CapturingLogger<CascadeDispatcher>();

        _ = new CascadeDispatcher(
            Substitute.For<ICascadeCheckpointStore>(),
            Substitute.For<ICascadeDescendantSource>(),
            Substitute.For<IWorkCommandSubmitter>(),
            logger,
            Options.Create(new WorksRecoveryOptions { CascadeTargetIntervalMilliseconds = 120_000 }));

        logger.Events.ShouldContain(value => value.Id == 4703);
    }

    /// <summary>Pacing between targets does not drop or duplicate any terminal submission.</summary>
    [Fact]
    public async Task Paced_dispatch_still_submits_every_target_exactly_once()
    {
        WorkItemId[] children = [new("child-001"), new("child-002"), new("child-003")];

        ICascadeCheckpointStore checkpointStore = Substitute.For<ICascadeCheckpointStore>();
        checkpointStore
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CascadeCheckpoint?>(null));
        ICascadeDescendantSource source = Substitute.For<ICascadeDescendantSource>();
        source
            .GetDescendantsAsync(s_tenant.Value, s_parent.Value, Arg.Any<CancellationToken>())
            .Returns([.. children.Select(child => new CascadeDescendant(s_tenant, child, IsTerminal: false))]);
        IWorkCommandSubmitter submitter = Substitute.For<IWorkCommandSubmitter>();
        var dispatcher = new CascadeDispatcher(
            checkpointStore,
            source,
            submitter,
            NullLogger<CascadeDispatcher>.Instance,
            Options.Create(new WorksRecoveryOptions { CascadeTargetIntervalMilliseconds = 1 }));

        await dispatcher.DispatchAsync(
            new WorkItemCancelled(s_parent.Value, 7, s_tenant, s_parent),
            TestContext.Current.CancellationToken);

        foreach (WorkItemId child in children)
        {
            await submitter.Received(1).SubmitAsync(
                Arg.Is<WorkCommandSubmission>(value => value != null && value.AggregateId == child.Value),
                Arg.Any<CancellationToken>());
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<EventId> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Events.Add(eventId);
    }
}
