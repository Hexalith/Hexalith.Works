using Dapr.Actors.Runtime;

using Hexalith.EventStore.Admin.Abstractions.Models.DeadLetters;
using Hexalith.EventStore.Operations.Actors;
using Hexalith.EventStore.Operations.Configuration;
using Hexalith.EventStore.Operations.Models;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Regresses the reusable EventStore operations workload consumed by the Works topology.
/// </summary>
public sealed class EventStoreOperationsTests
{
    /// <summary>Verifies replay recovery and raw-body retention stay inside the actor workload.</summary>
    [Fact]
    public void WorkloadHasReminderRecoveryAndNoRawOperatorResponseSurface()
    {
        typeof(DeadLetterDrainActor).GetInterfaces().ShouldContain(typeof(IRemindable));
        typeof(DeadLetterDrainActor).GetInterfaces().ShouldContain(typeof(IDeadLetterDrainActor));
        typeof(DeadLetterRecord).GetProperty(nameof(DeadLetterRecord.Body)).ShouldNotBeNull();
        typeof(DeadLetterListItem).GetProperty("Body").ShouldBeNull();
        typeof(DeadLetterListItem).GetProperty("BodySha256").ShouldBeNull();
        typeof(DeadLetterEntry).GetProperty("Body").ShouldBeNull();
        typeof(DeadLetterEntry).GetProperty("BodySha256").ShouldBeNull();
    }

    /// <summary>Verifies the Works composition defaults remain exact and caller-scoped.</summary>
    [Fact]
    public void WorkloadDefaultsMatchWorksDeadLetterBoundary()
    {
        var options = new EventStoreOperationsOptions();

        options.PubSubName.ShouldBe("pubsub");
        options.TopicName.ShouldBe("deadletter.work.events");
        options.CaptureRoute.ShouldBe("/dead-letters/work/events");
        options.AdminCallerAppId.ShouldBe("eventstore-admin");
        options.ReplayAppId.ShouldBe("works");
        options.ReplayMethodName.ShouldBe("work/events");
    }
}
