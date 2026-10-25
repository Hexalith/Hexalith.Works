namespace Hexalith.EventStore.AppHost.Tests.Configuration;

using global::Aspire.Hosting;
using global::Aspire.Hosting.ApplicationModel;

using CommunityToolkit.Aspire.Hosting.Dapr;

using Hexalith.EventStore.Aspire;

/// <summary>Protects repeat composition of the private application-channel credential.</summary>
public sealed class AppChannelTokenAspireExtensionTests
{
    /// <summary>Historical explicit registration reuses the token already installed by the topology.</summary>
    [Fact]
    public async Task TopologyThenGeneratedRegistrationRetainsOnePrivateBindingAsync()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<ProjectResource> gateway = builder.AddProject<EventStoreProjectMetadata>("eventstore");
        _ = builder.AddHexalithEventStore(gateway, null);
        ParameterResource token = builder.Resources.OfType<ParameterResource>().Single();
        IDaprSidecarResource sidecar = Sidecar(gateway);
        int appCallbacks = gateway.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>().Count();
        int sidecarCallbacks = sidecar.Annotations.OfType<EnvironmentCallbackAnnotation>().Count();

        gateway.WithGeneratedEventStoreAppChannelToken().ShouldBeSameAs(gateway);
        gateway.WithGeneratedEventStoreAppChannelToken().ShouldBeSameAs(gateway);

        builder.Resources.OfType<ParameterResource>().ShouldHaveSingleItem().ShouldBeSameAs(token);
        token.Secret.ShouldBeTrue();
        gateway.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>().Count().ShouldBe(appCallbacks);
        sidecar.Annotations.OfType<EnvironmentCallbackAnnotation>().Count().ShouldBe(sidecarCallbacks);
        (await EnvironmentAsync(builder, gateway.Resource).ConfigureAwait(true))["APP_API_TOKEN"].ShouldBeSameAs(token);
        (await EnvironmentAsync(builder, sidecar).ConfigureAwait(true))["APP_API_TOKEN"].ShouldBeSameAs(token);
    }

    /// <summary>Caller-supplied credentials do not become the SDK-generated token cache.</summary>
    [Fact]
    public async Task GeneratedRegistrationDoesNotAdoptAnExplicitCustomTokenAsync()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<ProjectResource> gateway = builder.AddProject<EventStoreProjectMetadata>("eventstore").WithDaprSidecar();
        IResourceBuilder<ParameterResource> custom = builder.AddParameter("custom-token", () => "custom-fixture-secret", secret: true);
        _ = gateway.WithEventStoreAppChannelToken(custom).WithGeneratedEventStoreAppChannelToken().WithGeneratedEventStoreAppChannelToken();

        ParameterResource generated = builder.Resources.OfType<ParameterResource>().Single(token => token.Name == "eventstore-app-api-token");
        generated.ShouldNotBeSameAs(custom.Resource);
        generated.Secret.ShouldBeTrue();
        builder.Resources.OfType<ParameterResource>().Count().ShouldBe(2);
        (await EnvironmentAsync(builder, gateway.Resource).ConfigureAwait(true))["APP_API_TOKEN"].ShouldBeSameAs(generated);
        (await EnvironmentAsync(builder, Sidecar(gateway)).ConfigureAwait(true))["APP_API_TOKEN"].ShouldBeSameAs(generated);
    }

    /// <summary>Repeat registration never makes two applications share their channel credential.</summary>
    [Fact]
    public async Task GeneratedTokensRemainPrivateToEachProjectAndItsSidecarAsync()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<ProjectResource> first = builder.AddProject<EventStoreProjectMetadata>("first").WithDaprSidecar();
        IResourceBuilder<ProjectResource> second = builder.AddProject<EventStoreProjectMetadata>("second").WithDaprSidecar();
        _ = first.WithGeneratedEventStoreAppChannelToken().WithGeneratedEventStoreAppChannelToken();
        _ = second.WithGeneratedEventStoreAppChannelToken().WithGeneratedEventStoreAppChannelToken();
        ParameterResource firstToken = builder.Resources.OfType<ParameterResource>().Single(token => token.Name == "first-app-api-token");
        ParameterResource secondToken = builder.Resources.OfType<ParameterResource>().Single(token => token.Name == "second-app-api-token");
        firstToken.ShouldNotBeSameAs(secondToken);
        firstToken.Secret.ShouldBeTrue();
        secondToken.Secret.ShouldBeTrue();
        foreach ((IResourceBuilder<ProjectResource> project, ParameterResource token) in new[] { (first, firstToken), (second, secondToken) })
        {
            (await EnvironmentAsync(builder, project.Resource).ConfigureAwait(true))["APP_API_TOKEN"].ShouldBeSameAs(token);
            (await EnvironmentAsync(builder, Sidecar(project)).ConfigureAwait(true))["APP_API_TOKEN"].ShouldBeSameAs(token);
        }
    }

    /// <summary>An explicit override invalidates the old generated-token cache without changing its binding.</summary>
    [Fact]
    public async Task ExplicitRebindingCannotReuseTheOldGeneratedTokenCacheAsync()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<ProjectResource> gateway = builder.AddProject<EventStoreProjectMetadata>("eventstore").WithDaprSidecar().WithGeneratedEventStoreAppChannelToken();
        IResourceBuilder<ParameterResource> custom = builder.AddParameter("custom-token", () => "override-fixture-secret", secret: true);
        _ = gateway.WithEventStoreAppChannelToken(custom);

        Should.Throw<DistributedApplicationException>(() => gateway.WithGeneratedEventStoreAppChannelToken());
        builder.Resources.OfType<ParameterResource>().Count().ShouldBe(2);
        (await EnvironmentAsync(builder, gateway.Resource).ConfigureAwait(true))["APP_API_TOKEN"].ShouldBeSameAs(custom.Resource);
        (await EnvironmentAsync(builder, Sidecar(gateway)).ConfigureAwait(true))["APP_API_TOKEN"].ShouldBeSameAs(custom.Resource);
    }

    /// <summary>A project without its own sidecar cannot leave an orphan secret behind.</summary>
    [Fact]
    public void MissingSidecarRefusesBeforeAddingASecret()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<ProjectResource> gateway = builder.AddProject<EventStoreProjectMetadata>("eventstore");
        Should.Throw<InvalidOperationException>(() => gateway.WithGeneratedEventStoreAppChannelToken()).Message.ShouldContain("must have a Dapr sidecar");
        builder.Resources.OfType<ParameterResource>().ShouldBeEmpty();
    }

    /// <summary>A coincidentally named parameter is never adopted as an SDK-owned channel binding.</summary>
    [Fact]
    public void UnrelatedParameterNameCollisionIsStillRefused()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<ProjectResource> gateway = builder.AddProject<EventStoreProjectMetadata>("eventstore").WithDaprSidecar();
        _ = builder.AddParameter("eventstore-app-api-token", () => "unrelated-fixture-secret", secret: true);
        EnvironmentCallbackAnnotation[] appCallbacks = [.. gateway.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>()];
        EnvironmentCallbackAnnotation[] sidecarCallbacks = [.. Sidecar(gateway).Annotations.OfType<EnvironmentCallbackAnnotation>()];
        Should.Throw<DistributedApplicationException>(() => gateway.WithGeneratedEventStoreAppChannelToken());
        gateway.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>().ShouldBe(appCallbacks);
        Sidecar(gateway).Annotations.OfType<EnvironmentCallbackAnnotation>().ShouldBe(sidecarCallbacks);
    }

    /// <summary>A same-name replacement cannot turn a stale token binding into successful registration.</summary>
    [Fact]
    public void ReplacedSelectedTokenIsRefused()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<ProjectResource> gateway = builder.AddProject<EventStoreProjectMetadata>("eventstore").WithDaprSidecar().WithGeneratedEventStoreAppChannelToken();
        _ = builder.Resources.Remove(builder.Resources.OfType<ParameterResource>().Single());
        IResourceBuilder<ParameterResource> replacement = builder.AddParameter("eventstore-app-api-token", () => "replacement-fixture-secret", secret: true);
        Should.Throw<InvalidOperationException>(() => gateway.WithGeneratedEventStoreAppChannelToken()).Message.ShouldContain("no longer has its configured app-channel token binding");
        builder.Resources.OfType<ParameterResource>().ShouldHaveSingleItem().ShouldBeSameAs(replacement.Resource);
    }

    /// <summary>Finds the application's one Dapr sidecar.</summary>
    /// <param name="project">The application resource.</param>
    /// <returns>The sidecar resource.</returns>
    private static IDaprSidecarResource Sidecar(IResourceBuilder<ProjectResource> project)
        => project.Resource.Annotations.OfType<DaprSidecarAnnotation>().Single().Sidecar;

    /// <summary>Evaluates the actual environment callbacks without starting a process.</summary>
    /// <param name="builder">The owning application builder.</param>
    /// <param name="resource">The application or sidecar.</param>
    /// <returns>The configured environment.</returns>
    private static async Task<Dictionary<string, object>> EnvironmentAsync(IDistributedApplicationBuilder builder, IResource resource)
    {
        Dictionary<string, object> environment = [];
        foreach (EnvironmentCallbackAnnotation callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(new EnvironmentCallbackContext(builder.ExecutionContext, resource, environment, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        }

        return environment;
    }
}
