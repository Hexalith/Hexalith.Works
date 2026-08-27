using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class EventStoreApiSurfaceCharacterizationTests
{
    [Fact]
    public void P1_EventStoreExposesConcurrencyAndProjectionRebuildSurfacesNeededByWorks()
    {
        string eventStoreRoot = RepositoryRoot.DependencyRoot("Hexalith.EventStore");

        Directory.Exists(eventStoreRoot).ShouldBeTrue("The root Hexalith.EventStore submodule must be initialized non-recursively before Works implementation depends on it.");

        File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Server", "Commands", "ConcurrencyConflictException.cs"))
            .ShouldBeTrue("Works claim commit-conflict handling needs the EventStore ETag concurrency surface.");

        File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Server", "Actors", "AggregateActor.cs"))
            .ShouldBeTrue("Works command handling depends on the EventStore aggregate actor pipeline.");

        File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Contracts", "Streams", "ProjectionRebuildOperation.cs"))
            .ShouldBeTrue("Works projection rebuild planning depends on the EventStore rebuild operation contract.");

        File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Contracts", "Streams", "ProjectionRebuildCheckpoint.cs"))
            .ShouldBeTrue("Works online rebuild planning depends on checkpointed projection rebuild support.");

        File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Contracts", "Projections", "ProjectionChangedNotification.cs"))
            .ShouldBeTrue("Works read-model freshness depends on projection-change notification support.");
    }

    [Fact]
    public void P1_EventStoreExposesETagBackedProjectionInvalidationSurfaces()
    {
        string eventStoreRoot = RepositoryRoot.DependencyRoot("Hexalith.EventStore");

        Directory.Exists(eventStoreRoot).ShouldBeTrue("The root Hexalith.EventStore submodule must be initialized non-recursively before Works implementation depends on it.");

        string projectionWriteActor = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Actors", "IProjectionWriteActor.cs");
        projectionWriteActor.ShouldContain("UpdateProjectionAsync");
        projectionWriteActor.ShouldContain("ETag", Case.Insensitive);

        string etagActor = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Actors", "IETagActor.cs");
        etagActor.ShouldContain("GetCurrentETagAsync");
        etagActor.ShouldContain("RegenerateAsync");

        string notifier = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Projections", "DaprProjectionChangeNotifier.cs");
        notifier.ShouldContain("ProjectionChangedNotification");
        notifier.ShouldContain("RegenerateAsync");
    }

    [Fact]
    public void P1_EventStorePersistsRejectionsAndUsesEnvelopeCanonicalSequencing()
    {
        string eventStoreRoot = RepositoryRoot.DependencyRoot("Hexalith.EventStore");

        string aggregateActor = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Actors", "AggregateActor.cs");
        int commandPath = aggregateActor.IndexOf("private async Task<CommandProcessingResult> ProcessCommandCoreAsync", StringComparison.Ordinal);
        commandPath.ShouldBeGreaterThanOrEqualTo(0, "EventStore must still route command handling through AggregateActor.ProcessCommandCoreAsync; the pipeline Works characterizes has moved or been renamed upstream.");

        int noOpGuard = aggregateActor.IndexOf("if (domainResult.IsNoOp)", commandPath, StringComparison.Ordinal);
        noOpGuard.ShouldBeGreaterThan(commandPath, "AggregateActor.ProcessCommandCoreAsync must still short-circuit only on IsNoOp before persisting; the no-op guard Works anchors its rejection characterization on is gone.");

        int persistCall = aggregateActor.IndexOf(".PersistEventsAsync(", noOpGuard, StringComparison.Ordinal);
        persistCall.ShouldBeGreaterThan(noOpGuard, "AggregateActor.ProcessCommandCoreAsync must still call PersistEventsAsync after the no-op guard; Works depends on that persist hop for envelope-canonical sequencing.");

        // Two complementary drift guards, because a rejection short-circuit can be injected anywhere ahead
        // of the persist call, not only after the no-op guard:
        //   1. across the whole command path, reject a rejection-CONDITIONAL short-circuit (the ternary at
        //      the pipeline's log line legitimately reads domainResult.IsRejection, so match `if (`);
        //   2. inside the narrow no-op-guard..persist window, reject the token outright.
        aggregateActor[commandPath..persistCall]
            .Contains("if (domainResult.IsRejection", StringComparison.Ordinal)
            .ShouldBeFalse("EventStore must not branch on IsRejection anywhere between entering ProcessCommandCoreAsync and PersistEventsAsync; Works depends on every rejection consuming an envelope position.");
        aggregateActor[noOpGuard..persistCall]
            .Contains("IsRejection", StringComparison.Ordinal)
            .ShouldBeFalse("EventStore must not branch on IsRejection between the no-op guard and PersistEventsAsync; Works depends on every rejection consuming an envelope position.");
        aggregateActor.ShouldContain(
            "string? rejectionEventType = domainResult.IsRejection",
            customMessage: "EventStore must still classify a persisted rejection by event type after the persist hop; Works reads that classification as evidence rejections travel the same pipeline as successes.");

        string eventPersister = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Server", "Events", "EventPersister.cs");
        eventPersister.ShouldContain(
            "long currentSequence = metadataResult.HasValue ? metadataResult.Value.CurrentSequence : 0;",
            customMessage: "EventPersister must still derive the current envelope position from persisted aggregate metadata, never from a payload field.");
        eventPersister.ShouldContain(
            "long sequenceNumber = currentSequence + 1 + i;",
            customMessage: "EventPersister must still assign one gapless envelope position per persisted event; Works' envelope/payload divergence depends on it.");
        eventPersister.ShouldContain(
            "SequenceNumber: sequenceNumber",
            customMessage: "EventPersister must still stamp the derived envelope position onto the stored envelope.");
        eventPersister.ShouldContain(
            "long newSequence = currentSequence + domainResult.Events.Count;",
            customMessage: "EventPersister must still advance the metadata watermark by the full event count, rejections included.");

        string aggregateReplayer = ReadEventStoreSource(eventStoreRoot, "Hexalith.EventStore.Client", "Aggregates", "AggregateReplayer.cs");
        aggregateReplayer.ShouldContain(
            ".OrderBy(e => e.SequenceNumber)",
            customMessage: "AggregateReplayer must still order replay by envelope SequenceNumber; Works relies on envelope order, not payload ordinals.");
        aggregateReplayer.ShouldContain(
            "if (eligible[i].SequenceNumber != expectedSequence)",
            customMessage: "AggregateReplayer must still gap-validate envelope positions; a tolerated gap would hide a dropped rejection envelope.");
        aggregateReplayer.ShouldContain(
            "lastApplied = evt.SequenceNumber;",
            customMessage: "AggregateReplayer must still report the last applied ENVELOPE position; Works asserts it lands at 2 after rejection-then-create.");
    }

    private static string ReadEventStoreSource(string eventStoreRoot, params string[] relativeSegments)
    {
        string path = Path.Combine([eventStoreRoot, "src", .. relativeSegments]);
        File.Exists(path).ShouldBeTrue($"Expected EventStore source '{path}' to exist; the API-surface contract Works depends on may have moved or been renamed upstream.");

        return File.ReadAllText(path);
    }
}
