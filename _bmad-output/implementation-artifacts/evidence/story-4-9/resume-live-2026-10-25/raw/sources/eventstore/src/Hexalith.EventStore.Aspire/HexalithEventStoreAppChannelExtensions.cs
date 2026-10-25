using System.Security.Cryptography;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using CommunityToolkit.Aspire.Hosting.Dapr;

namespace Hexalith.EventStore.Aspire;

/// <summary>
/// Wires the Dapr application-channel token (AD-28) between an application and its own Dapr sidecar.
/// </summary>
/// <remarks>
/// The sidecar presents <c>APP_API_TOKEN</c> in the <c>dapr-api-token</c> header on every call it makes to its
/// application, and the application compares it in constant time. The token proves only that a request crossed the
/// application's own sidecar; it never identifies the calling workload.
/// </remarks>
public static class HexalithEventStoreAppChannelExtensions
{
    /// <summary>Gets the environment variable shared by the application and its sidecar.</summary>
    public const string AppChannelTokenVariable = "APP_API_TOKEN";

    /// <summary>
    /// Generates a per-run secret app-channel token and gives it to the project and to its Dapr sidecar.
    /// Reuses an existing SDK-generated token without rotating it or adding another resource.
    /// </summary>
    /// <param name="project">A project resource that already has a Dapr sidecar.</param>
    /// <returns>The project resource builder.</returns>
    public static IResourceBuilder<ProjectResource> WithGeneratedEventStoreAppChannelToken(
        this IResourceBuilder<ProjectResource> project)
    {
        ArgumentNullException.ThrowIfNull(project);
        IDaprSidecarResource sidecar = GetSidecar(project);
        if (project.Resource.TryGetLastAnnotation<EventStoreAppChannelTokenBindingAnnotation>(out var binding))
        {
            if (!ReferenceEquals(binding.Sidecar, sidecar)
                || !project.ApplicationBuilder.Resources.Any(resource => ReferenceEquals(resource, binding.Token)))
            {
                throw new InvalidOperationException(
                    $"Resource '{project.Resource.Name}' no longer has its configured app-channel token binding.");
            }

            return project;
        }

        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        IResourceBuilder<ParameterResource> parameter = project.ApplicationBuilder.AddParameter(
            project.Resource.Name + "-app-api-token",
            () => token,
            secret: true);
        _ = project.WithEventStoreAppChannelToken(parameter);
        project.Resource.Annotations.Add(new EventStoreAppChannelTokenBindingAnnotation(parameter.Resource, sidecar));
        return project;
    }

    /// <summary>
    /// Gives an app-channel token to the project and to its Dapr sidecar.
    /// </summary>
    /// <param name="project">A project resource that already has a Dapr sidecar.</param>
    /// <param name="token">The secret token parameter.</param>
    /// <returns>The project resource builder.</returns>
    /// <exception cref="InvalidOperationException">The project has no Dapr sidecar.</exception>
    public static IResourceBuilder<ProjectResource> WithEventStoreAppChannelToken(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<ParameterResource> token)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(token);
        IDaprSidecarResource sidecar = GetSidecar(project);
        ParameterResource tokenResource = token.Resource;
        if (project.Resource.TryGetLastAnnotation<EventStoreAppChannelTokenBindingAnnotation>(out var binding)
            && (!ReferenceEquals(binding.Token, tokenResource) || !ReferenceEquals(binding.Sidecar, sidecar)))
        {
            _ = project.Resource.Annotations.Remove(binding);
        }

        sidecar.Annotations.Add(new EnvironmentCallbackAnnotation(
            context => context.EnvironmentVariables[AppChannelTokenVariable] = tokenResource));
        return project.WithEnvironment(AppChannelTokenVariable, token);
    }

    /// <summary>Finds the one sidecar that shares the project's private application channel.</summary>
    /// <param name="project">The project whose sidecar is required.</param>
    /// <returns>The project's sidecar.</returns>
    private static IDaprSidecarResource GetSidecar(IResourceBuilder<ProjectResource> project)
        => project.Resource.Annotations.OfType<DaprSidecarAnnotation>().SingleOrDefault()?.Sidecar
            ?? throw new InvalidOperationException(
                $"Resource '{project.Resource.Name}' must have a Dapr sidecar before its app-channel token is wired.");
}
