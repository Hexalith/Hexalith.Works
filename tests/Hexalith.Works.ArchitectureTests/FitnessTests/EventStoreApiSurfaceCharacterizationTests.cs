using Shouldly;

namespace Hexalith.Works.ArchitectureTests.FitnessTests;

public sealed class EventStoreApiSurfaceCharacterizationTests
{
    [Fact]
    public void P1_EventStoreExposesConcurrencyAndProjectionRebuildSurfacesNeededByWorks()
    {
        string eventStoreRoot = RepositoryRoot.PathFromRoot("Hexalith.EventStore");

        Directory.Exists(eventStoreRoot).ShouldBeTrue("The root Hexalith.EventStore submodule must be initialized non-recursively before Works implementation depends on it.");

        File.Exists(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Server", "Commands", "ConcurrencyConflictException.cs"))
            .ShouldBeTrue("Works claim/expected-version behavior needs an EventStore concurrency-conflict surface.");

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
        string eventStoreRoot = RepositoryRoot.PathFromRoot("Hexalith.EventStore");

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

    private static string ReadEventStoreSource(string eventStoreRoot, params string[] relativeSegments)
    {
        string path = Path.Combine([eventStoreRoot, "src", .. relativeSegments]);
        File.Exists(path).ShouldBeTrue($"Expected EventStore source '{path}' to exist; the API-surface contract Works depends on may have moved or been renamed upstream.");

        return File.ReadAllText(path);
    }
}
