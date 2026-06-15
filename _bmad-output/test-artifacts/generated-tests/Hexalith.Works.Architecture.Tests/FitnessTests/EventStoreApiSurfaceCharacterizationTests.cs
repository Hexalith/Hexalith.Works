using Shouldly;

using Xunit;

namespace Hexalith.Works.Architecture.Tests.FitnessTests;

public sealed class EventStoreApiSurfaceCharacterizationTests
{
    [Fact]
    public void P1_EventStoreExposesConcurrencyAndProjectionRebuildSurfacesNeededByWorks()
    {
        // Given Works later stories depend on EventStore concurrency and rebuild behavior.
        string eventStoreRoot = RepositoryRoot.PathFromRoot("Hexalith.EventStore");

        // When the root EventStore submodule source surface is inspected.
        Directory.Exists(eventStoreRoot).ShouldBeTrue("The root Hexalith.EventStore submodule must be initialized non-recursively before Works implementation depends on it.");

        // Then the expected source contracts are present.
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
        // Given Works projections need freshness and invalidation support.
        string eventStoreRoot = RepositoryRoot.PathFromRoot("Hexalith.EventStore");

        // When projection write, ETag, and notification surfaces are inspected.
        string projectionWriteActor = File.ReadAllText(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Server", "Actors", "IProjectionWriteActor.cs"));

        // Then the surfaces expose the operations Works planning depends on.
        projectionWriteActor.ShouldContain("UpdateProjectionAsync");
        projectionWriteActor.ShouldContain("ETag", Case.Insensitive);

        string etagActor = File.ReadAllText(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Server", "Actors", "IETagActor.cs"));
        etagActor.ShouldContain("GetCurrentETagAsync");
        etagActor.ShouldContain("RegenerateAsync");

        string notifier = File.ReadAllText(Path.Combine(eventStoreRoot, "src", "Hexalith.EventStore.Server", "Projections", "DaprProjectionChangeNotifier.cs"));
        notifier.ShouldContain("ProjectionChangedNotification");
        notifier.ShouldContain("RegenerateAsync");
    }
}
