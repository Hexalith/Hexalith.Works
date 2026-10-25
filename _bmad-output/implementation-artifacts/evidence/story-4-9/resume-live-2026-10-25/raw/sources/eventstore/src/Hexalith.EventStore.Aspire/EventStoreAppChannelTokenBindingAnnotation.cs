using Aspire.Hosting.ApplicationModel;

using CommunityToolkit.Aspire.Hosting.Dapr;

namespace Hexalith.EventStore.Aspire;

/// <summary>Records the private SDK-generated token already wired to a project and its own Dapr sidecar.</summary>
/// <param name="Token">The selected application-channel token parameter.</param>
/// <param name="Sidecar">The sidecar that shares the token with its application.</param>
internal sealed record EventStoreAppChannelTokenBindingAnnotation(
    ParameterResource Token,
    IDaprSidecarResource Sidecar) : IResourceAnnotation;
