using System.Net.Sockets;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using CommunityToolkit.Aspire.Hosting.Dapr;

using Shouldly;

using YamlDotNet.RepresentationModel;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Verifies the executable Works AppHost model and its committed Dapr configuration.
/// </summary>
public sealed class WorksAppHostTopologyTests
{
    private const string EventStoreName = "eventstore";
    private const string EventStoreAdminName = "eventstore-admin";
    private const string EventStoreOperationsName = "eventstore-operations";
    private const string PubSubName = "pubsub";
    private const string PlacementName = "dapr-placement-mtls";
    private const string ResiliencyName = "resiliency";
    private const string SchedulerName = "dapr-scheduler-mtls";
    private const string SentryName = "dapr-sentry";
    private const string StateStoreName = "statestore";
    private const string WorksName = "works";
    private const string SourceTopic = "work.events";
    private const string DeadLetterTopic = "deadletter.work.events";
    private const string CommandDeadLetterPrefix = "commanddeadletter";
    private const string CommandDeadLetterTopic = CommandDeadLetterPrefix + "." + SourceTopic;

    /// <summary>Verifies exact project, endpoint, sidecar, relationship, and environment values.</summary>
    [Fact]
    public async Task AppHostModelExposesTheExactCommandEventTopology()
    {
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(
                [
                    "--EnableKeycloak=false",
                    "--environment=Development",
                    "--Dapr:PlacementHostAddress=localhost:6050",
                    "--Dapr:SchedulerHostAddress=localhost:6060",
                ],
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        ProjectResource eventStore = Project(builder, EventStoreName);
        ProjectResource adminServer = Project(builder, EventStoreAdminName);
        ProjectResource operations = Project(builder, EventStoreOperationsName);
        ProjectResource works = Project(builder, WorksName);
        ContainerResource sentry = builder.Resources
            .OfType<ContainerResource>()
            .Single(static resource => string.Equals(resource.Name, SentryName, StringComparison.Ordinal));

        ContainerImageAnnotation sentryImage = sentry.Annotations.OfType<ContainerImageAnnotation>().ShouldHaveSingleItem();
        sentryImage.Image.ShouldBe("daprio/sentry");
        sentryImage.Tag.ShouldBe("1.18.3");
        sentry.Entrypoint.ShouldBe("/sentry");
        EndpointAnnotation sentryGrpc = sentry.Annotations.OfType<EndpointAnnotation>()
            .Single(static endpoint => string.Equals(endpoint.Name, "grpc", StringComparison.Ordinal));
        sentryGrpc.Port.ShouldBe(50001);
        sentryGrpc.TargetPort.ShouldBe(50001);
        sentryGrpc.IsProxied.ShouldBeFalse();
        EndpointAnnotation sentryHealth = sentry.Annotations.OfType<EndpointAnnotation>()
            .Single(static endpoint => string.Equals(endpoint.Name, "health", StringComparison.Ordinal));
        sentryHealth.TargetPort.ShouldBe(8080);
        sentry.Annotations.OfType<HealthCheckAnnotation>().ShouldHaveSingleItem();
        ContainerMountAnnotation credentialsMount = sentry.Annotations.OfType<ContainerMountAnnotation>()
            .Single(static mount => string.Equals(mount.Target, "/var/run/dapr/credentials", StringComparison.Ordinal));
        credentialsMount.Source.ShouldNotBeNull().ShouldNotStartWith(LocateRepositoryRoot(), Case.Sensitive);
        credentialsMount.IsReadOnly.ShouldBeFalse();
        sentry.Annotations.OfType<ContainerMountAnnotation>()
            .Single(static mount => string.Equals(mount.Target, "/var/run/dapr/config/sentry.yaml", StringComparison.Ordinal))
            .IsReadOnly.ShouldBeTrue();

        HealthKeys(eventStore).ShouldBe(Sorted(["eventstore_http_/alive_200_check"]));
        HealthKeys(works).ShouldBe(Sorted(["works_http_/alive_200_check"]));
        HealthKeys(operations).ShouldBe(Sorted(["eventstore-operations_http_/alive_200_check"]));

        EndpointAnnotation worksHttp = works.Annotations
            .OfType<EndpointAnnotation>()
            .Single(static endpoint => string.Equals(endpoint.Name, "http", StringComparison.Ordinal));
        worksHttp.UriScheme.ShouldBe("http");
        worksHttp.Transport.ShouldBe("http");
        worksHttp.Protocol.ShouldBe(ProtocolType.Tcp);
        worksHttp.Port.ShouldBeNull();
        worksHttp.TargetPort.ShouldBeNull();
        worksHttp.IsExternal.ShouldBeFalse();
        worksHttp.IsProxied.ShouldBeTrue();

        string componentsDirectory = ComponentsDirectory();
        IDaprSidecarResource eventStoreSidecar = Sidecar(eventStore);
        IDaprSidecarResource adminSidecar = Sidecar(adminServer);
        IDaprSidecarResource worksSidecar = Sidecar(works);
        IDaprSidecarResource operationsSidecar = Sidecar(operations);

        DaprSidecarOptions eventStoreOptions = SidecarOptions(eventStoreSidecar);
        eventStoreOptions.AppId.ShouldBe(EventStoreName);
        eventStoreOptions.DaprHttpPort.ShouldBe(3501);
        eventStoreOptions.Config.ShouldBe(Path.Combine(componentsDirectory, "accesscontrol.yaml"));
        eventStoreOptions.PlacementHostAddress.ShouldBe("localhost:6050");
        eventStoreOptions.SchedulerHostAddress.ShouldBe("localhost:6060");
        ReferencedComponents(eventStoreSidecar).ShouldBe([PubSubName, ResiliencyName, StateStoreName]);

        DaprSidecarOptions adminOptions = SidecarOptions(adminSidecar);
        adminOptions.AppId.ShouldBe(EventStoreAdminName);
        adminOptions.Config.ShouldBe(Path.Combine(componentsDirectory, "accesscontrol.eventstore-admin.yaml"));
        ReferencedComponents(adminSidecar).ShouldBe([ResiliencyName, StateStoreName]);

        DaprSidecarOptions worksOptions = SidecarOptions(worksSidecar);
        worksOptions.AppId.ShouldBe(WorksName);
        worksOptions.Config.ShouldBe(Path.Combine(componentsDirectory, "accesscontrol.works.yaml"));
        worksOptions.EnableAppHealthCheck.ShouldBe(true);
        worksOptions.AppHealthCheckPath.ShouldBe("/alive");
        worksOptions.PlacementHostAddress.ShouldBe("localhost:6050");
        worksOptions.SchedulerHostAddress.ShouldBe("localhost:6060");
        worksOptions.AppPort.ShouldBeNull();
        ReferencedComponents(worksSidecar).ShouldBe([PubSubName, ResiliencyName, StateStoreName]);

        DaprSidecarOptions operationsOptions = SidecarOptions(operationsSidecar);
        operationsOptions.AppId.ShouldBe(EventStoreOperationsName);
        operationsOptions.Config.ShouldBe(Path.Combine(componentsDirectory, "accesscontrol.eventstore-operations.yaml"));
        operationsOptions.EnableAppHealthCheck.ShouldBe(true);
        operationsOptions.AppHealthCheckPath.ShouldBe("/alive");
        operationsOptions.PlacementHostAddress.ShouldBe("localhost:6050");
        operationsOptions.SchedulerHostAddress.ShouldBe("localhost:6060");
        ReferencedComponents(operationsSidecar).ShouldBe([PubSubName, ResiliencyName, StateStoreName]);

        // Two "Reference" relationships to eventstore, not one: AddEventStoreDomainModule contributes the
        // domain-module reference and the explicit EventStore__CommandGateway__BaseAddress endpoint reference
        // contributes the second. Counted rather than de-duplicated so losing either one fails here.
        ReferencedResources(works).ShouldBe([EventStoreName, EventStoreName]);
        WaitedResources(eventStore).ShouldBe([SentryName]);
        WaitedResources(works).ShouldBe([SentryName, EventStoreName, StateStoreName]);
        ReferencedResources(adminServer).ShouldBe([EventStoreName, EventStoreOperationsName]);
        WaitedResources(adminServer).ShouldBe([SentryName, EventStoreOperationsName]);
        ReferencedResources(operations).ShouldBe([WorksName]);
        WaitedResources(operations).ShouldBe([SentryName, StateStoreName, WorksName]);

        // The bounded inbound retry budget only holds where the CRD's directory reaches --resources-path, so no
        // composed sidecar may be missing the reference — including one added after this test was written.
        IDaprSidecarResource[] allSidecars =
        [
            .. builder.Resources.OfType<ProjectResource>()
                .Where(static resource => resource.TryGetAnnotationsOfType<DaprSidecarAnnotation>(out _))
                .Select(Sidecar)
                .Distinct(),
        ];
        allSidecars.Length.ShouldBe(4);
        allSidecars.ShouldAllBe(sidecar => ReferencedComponents(sidecar).Contains(ResiliencyName));
        allSidecars.ShouldAllBe(sidecar => sidecar.Annotations.OfType<EnvironmentCallbackAnnotation>().Count() == 1);

        Dictionary<string, object> eventStoreEnvironment = await EvaluateEnvironmentAsync(eventStore, builder.ExecutionContext);
        StringValue(eventStoreEnvironment, "EventStore__DomainServices__Registrations__wildcard_work_v1__AppId").ShouldBe(WorksName);
        StringValue(eventStoreEnvironment, "EventStore__DomainServices__Registrations__wildcard_work_v1__MethodName").ShouldBe("process");
        StringValue(eventStoreEnvironment, "EventStore__DomainServices__Registrations__wildcard_work_v1__TenantId").ShouldBe("*");
        StringValue(eventStoreEnvironment, "EventStore__DomainServices__Registrations__wildcard_work_v1__Domain").ShouldBe("work");
        StringValue(eventStoreEnvironment, "EventStore__DomainServices__Registrations__wildcard_work_v1__Version").ShouldBe("v1");
        StringValue(eventStoreEnvironment, "EventStore__Publisher__TopicOverrides__work").ShouldBe("work.events");
        StringValue(eventStoreEnvironment, "Authentication__DaprInternal__AllowedCallers__0").ShouldBe(WorksName);
        EnvironmentKeys(eventStoreEnvironment, "EventStore__DomainServices__Registrations__wildcard_work_v1__")
            .ShouldBe([
                "EventStore__DomainServices__Registrations__wildcard_work_v1__AppId",
                "EventStore__DomainServices__Registrations__wildcard_work_v1__Domain",
                "EventStore__DomainServices__Registrations__wildcard_work_v1__MethodName",
                "EventStore__DomainServices__Registrations__wildcard_work_v1__TenantId",
                "EventStore__DomainServices__Registrations__wildcard_work_v1__Version",
            ]);
        EnvironmentKeys(eventStoreEnvironment, "EventStore__Publisher__TopicOverrides__")
            .ShouldBe(["EventStore__Publisher__TopicOverrides__work"]);
        EnvironmentKeys(eventStoreEnvironment, "Authentication__DaprInternal__AllowedCallers__")
            .ShouldBe(["Authentication__DaprInternal__AllowedCallers__0"]);

        Dictionary<string, object> worksEnvironment = await EvaluateEnvironmentAsync(works, builder.ExecutionContext);
        StringValue(worksEnvironment, "EventStore__DomainService__AppId").ShouldBe(WorksName);
        StringValue(worksEnvironment, "EventStore__DomainService__ServiceVersion").ShouldBe("v1");
        EnvironmentKeys(worksEnvironment, "EventStore__DomainService__")
            .ShouldBe(["EventStore__DomainService__AppId", "EventStore__DomainService__ServiceVersion"]);
        EndpointReference gateway = worksEnvironment["EventStore__CommandGateway__BaseAddress"]
            .ShouldBeOfType<EndpointReference>();
        gateway.Resource.Name.ShouldBe(EventStoreName);
        gateway.EndpointName.ShouldBe("http");

        Dictionary<string, object> adminEnvironment = await EvaluateEnvironmentAsync(adminServer, builder.ExecutionContext);
        StringValue(adminEnvironment, "AdminServer__ResiliencyConfigPath")
            .ShouldBe(Path.Combine(componentsDirectory, ResiliencyName, "resiliency.yaml"));
        StringValue(adminEnvironment, "AdminServer__OperationsAppId").ShouldBe(EventStoreOperationsName);

        Dictionary<string, object> operationsEnvironment = await EvaluateEnvironmentAsync(operations, builder.ExecutionContext);
        StringValue(operationsEnvironment, "DOTNET_ENVIRONMENT").ShouldBe("Development");
        StringValue(operationsEnvironment, "EventStoreOperations__PubSubName").ShouldBe(PubSubName);
        StringValue(operationsEnvironment, "EventStoreOperations__TopicName").ShouldBe("deadletter.work.events");
        StringValue(operationsEnvironment, "EventStoreOperations__CaptureRoute").ShouldBe("/dead-letters/work/events");
        StringValue(operationsEnvironment, "EventStoreOperations__AdminCallerAppId").ShouldBe(EventStoreAdminName);
        StringValue(operationsEnvironment, "EventStoreOperations__ReplayAppId").ShouldBe(WorksName);
        StringValue(operationsEnvironment, "EventStoreOperations__ReplayMethodName").ShouldBe("work/events");
        EnvironmentKeys(operationsEnvironment, "EventStoreOperations__").ShouldBe([
            "EventStoreOperations__AdminCallerAppId",
            "EventStoreOperations__CaptureRoute",
            "EventStoreOperations__PubSubName",
            "EventStoreOperations__ReplayAppId",
            "EventStoreOperations__ReplayMethodName",
            "EventStoreOperations__TopicName",
        ]);

        IDaprComponentResource stateStore = Component(builder, StateStoreName);
        stateStore.Type.ShouldBe("state.redis");
        stateStore.Options.ShouldNotBeNull().LocalPath.ShouldBe(Path.Combine(componentsDirectory, "statestore.yaml"));
        IDaprComponentResource pubSub = Component(builder, PubSubName);
        pubSub.Type.ShouldBe("pubsub.redis");
        pubSub.Options.ShouldNotBeNull().LocalPath.ShouldBe(Path.Combine(componentsDirectory, "pubsub.yaml"));
        IDaprComponentResource resiliency = Component(builder, ResiliencyName);
        resiliency.Type.ShouldBe("resiliency");
        resiliency.Options.ShouldNotBeNull().LocalPath
            .ShouldBe(Path.Combine(componentsDirectory, ResiliencyName, "resiliency.yaml"));

        string[] forbiddenFragments = ["mcp", "chatbot", "email", "mail", "datagrid", "webshell", "routing", "cost", "keycloak", "signalr"];
        string[] forbiddenSurfaces =
        [
            .. builder.Resources
                .Select(static resource => resource.Name)
                .Where(name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase))),
        ];
        forbiddenSurfaces.ShouldBeEmpty($"The pipeline proof must not compose production surfaces: {string.Join(", ", forbiddenSurfaces)}");
        builder.Resources
            .Select(static resource => resource.Name)
            .ShouldNotContain(static name => name.EndsWith("-ui", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies the default local topology owns a TLS actor control plane with durable scheduler data.</summary>
    [Fact]
    public async Task DefaultAppHostComposesTheMtlsActorControlPlane()
    {
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(
                ["--EnableKeycloak=false"],
                TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        ContainerResource placement = builder.Resources
            .OfType<ContainerResource>()
            .Single(static resource => string.Equals(resource.Name, PlacementName, StringComparison.Ordinal));
        ContainerResource scheduler = builder.Resources
            .OfType<ContainerResource>()
            .Single(static resource => string.Equals(resource.Name, SchedulerName, StringComparison.Ordinal));

        AssertControlPlaneImageAndCredentials(placement, "./placement");
        AssertControlPlaneImageAndCredentials(scheduler, "./scheduler");

        string[] placementArgs = await EvaluateArgsAsync(placement);
        placementArgs.ShouldContain("--tls-enabled");
        placementArgs.ShouldContain("--sentry-address=dapr-sentry:50001");
        placementArgs.ShouldContain("--trust-domain=localhost");
        placementArgs.ShouldContain("--trust-anchors-file=/var/run/dapr/credentials/ca.crt");

        string[] schedulerArgs = await EvaluateArgsAsync(scheduler);
        schedulerArgs.ShouldContain("--tls-enabled");
        schedulerArgs.ShouldContain("--sentry-address=dapr-sentry:50001");
        schedulerArgs.ShouldContain("--trust-domain=localhost");
        schedulerArgs.ShouldContain("--trust-anchors-file=/var/run/dapr/credentials/ca.crt");
        schedulerArgs.ShouldContain("--etcd-data-dir=/var/lock/dapr/scheduler");
        schedulerArgs.ShouldContain("--override-broadcast-host-port=localhost:51006");
        schedulerArgs.ShouldNotContain("--etcd-client-listen-address=0.0.0.0");

        AssertGrpcEndpoint(placement, port: 51005, targetPort: 50005);
        AssertGrpcEndpoint(scheduler, port: 51006, targetPort: 50006);
        ContainerMountAnnotation schedulerData = scheduler.Annotations.OfType<ContainerMountAnnotation>()
            .Single(static mount => string.Equals(mount.Target, "/var/lock", StringComparison.Ordinal));
        schedulerData.Type.ShouldBe(ContainerMountType.Volume);
        schedulerData.Source.ShouldBe("hexalith-works-dapr-scheduler");
        schedulerData.IsReadOnly.ShouldBeFalse();

        WaitedResources(placement).ShouldBe([SentryName]);
        WaitedResources(scheduler).ShouldBe([SentryName]);
        foreach (ProjectResource project in builder.Resources
                     .OfType<ProjectResource>()
                     .Where(static resource => resource.TryGetAnnotationsOfType<DaprSidecarAnnotation>(out _)))
        {
            WaitedResources(project).ShouldContain(PlacementName);
            WaitedResources(project).ShouldContain(SchedulerName);
            DaprSidecarOptions options = SidecarOptions(Sidecar(project));
            options.PlacementHostAddress.ShouldBe("localhost:51005");
            options.SchedulerHostAddress.ShouldBe("localhost:51006");
        }
    }

    /// <summary>A partial external actor control-plane tuple is rejected before any topology is built.</summary>
    [Theory]
    [InlineData("--Dapr:PlacementHostAddress=localhost:6050")]
    [InlineData("--Dapr:SchedulerHostAddress=localhost:6060")]
    public async Task AppHostRejectsOneSidedActorControlPlaneConfiguration(string configuredEndpoint)
    {
        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            _ = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.Hexalith_Works_AppHost>(
                    ["--EnableKeycloak=false", configuredEndpoint],
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(true));

        exception.Message.ShouldContain("Configure both", Case.Sensitive);
    }

    /// <summary>Issuer material is rejected when an operator points its directory into the checkout.</summary>
    [Fact]
    public async Task AppHostRejectsRepositoryLocalCertificateDirectory()
    {
        string repositoryDirectory = Path.Combine(LocateRepositoryRoot(), "src");

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            _ = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.Hexalith_Works_AppHost>(
                    ["--EnableKeycloak=false", $"--Dapr:Mtls:CertificateDirectory={repositoryDirectory}"],
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(true));

        exception.Message.ShouldContain("must resolve outside the repository", Case.Sensitive);
    }

    /// <summary>AppHost credentials and control-plane identity replace inherited sidecar values.</summary>
    [Fact]
    public async Task SidecarEnvironmentUsesTheAppHostOwnedControlPlaneIdentity()
    {
        string certificateDirectory = Path.Combine(
            Path.GetTempPath(),
            "hexalith-works-topology-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(certificateDirectory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(certificateDirectory, "ca.crt"), "test-anchor", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(certificateDirectory, "issuer.crt"), "test-chain", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(certificateDirectory, "issuer.key"), "test-key", cancellationToken);

        try
        {
            IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.Hexalith_Works_AppHost>(
                    ["--EnableKeycloak=false", $"--Dapr:Mtls:CertificateDirectory={certificateDirectory}"],
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            IDaprSidecarResource sidecar = Sidecar(Project(builder, WorksName));
            var inherited = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["DAPR_TRUST_ANCHORS"] = "stale-anchor",
                ["DAPR_CERT_CHAIN"] = "stale-chain",
                ["DAPR_CERT_KEY"] = "stale-key",
                ["DAPR_CONTROLPLANE_TRUST_DOMAIN"] = "stale-domain",
                ["DAPR_CONTROLPLANE_NAMESPACE"] = "stale-namespace",
                ["NAMESPACE"] = "stale-namespace",
            };

            Dictionary<string, object> environment = await EvaluateEnvironmentAsync(
                sidecar,
                builder.ExecutionContext,
                inherited);

            StringValue(environment, "DAPR_TRUST_ANCHORS").ShouldBe("test-anchor");
            StringValue(environment, "DAPR_CERT_CHAIN").ShouldBe("test-chain");
            StringValue(environment, "DAPR_CERT_KEY").ShouldBe("test-key");
            StringValue(environment, "DAPR_CONTROLPLANE_TRUST_DOMAIN").ShouldBe("localhost");
            StringValue(environment, "DAPR_CONTROLPLANE_NAMESPACE").ShouldBe("default");
            StringValue(environment, "NAMESPACE").ShouldBe("default");
        }
        finally
        {
            Directory.Delete(certificateDirectory, recursive: true);
        }
    }

    /// <summary>An external-looking symlink cannot redirect issuer material back into the checkout.</summary>
    [Fact]
    public async Task AppHostRejectsCertificateDirectorySymlinkedIntoTheRepository()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Creating directory symlinks requires host-specific privileges on Windows.");
            return;
        }

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "hexalith-works-topology-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string linkedDirectory = Path.Combine(temporaryDirectory, "credentials");
        _ = Directory.CreateSymbolicLink(linkedDirectory, LocateRepositoryRoot());

        try
        {
            InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
                _ = await DistributedApplicationTestingBuilder
                    .CreateAsync<Projects.Hexalith_Works_AppHost>(
                        ["--EnableKeycloak=false", $"--Dapr:Mtls:CertificateDirectory={linkedDirectory}"],
                        TestContext.Current.CancellationToken)
                    .ConfigureAwait(true));

            exception.Message.ShouldContain("must resolve outside the repository", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(linkedDirectory);
            Directory.Delete(temporaryDirectory);
        }
    }

    /// <summary>Verifies actor state-store metadata and app scopes from parsed YAML nodes.</summary>
    [Fact]
    public void StateStoreComponentHasExactActorMetadataAndScopes()
    {
        YamlMappingNode root = LoadYaml("statestore.yaml");

        Scalar(root, "apiVersion").ShouldBe("dapr.io/v1alpha1");
        Scalar(root, "kind").ShouldBe("Component");
        Scalar(Mapping(root, "metadata"), "name").ShouldBe(StateStoreName);
        YamlMappingNode spec = Mapping(root, "spec");
        Scalar(spec, "type").ShouldBe("state.redis");
        Scalar(spec, "version").ShouldBe("v1");

        YamlSequenceNode metadataNodes = Sequence(spec, "metadata");
        metadataNodes.Children.ShouldAllBe(static item => item is YamlMappingNode);
        Dictionary<string, string> metadata = metadataNodes.Children
            .Cast<YamlMappingNode>()
            .ToDictionary(static item => Scalar(item, "name"), static item => Scalar(item, "value"), StringComparer.Ordinal);
        metadata.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["redisHost"] = "localhost:6379",
            ["redisPassword"] = string.Empty,
            ["actorStateStore"] = "true",
        });

        YamlSequenceNode scopeNodes = Sequence(root, "scopes");
        scopeNodes.Children.ShouldAllBe(static item => item is YamlScalarNode);
        Sorted(scopeNodes.Children
            .Cast<YamlScalarNode>()
            .Select(static scope => scope.Value ?? string.Empty))
            .ShouldBe(Sorted([EventStoreName, WorksName, EventStoreAdminName, EventStoreOperationsName]));
    }

    /// <summary>Verifies exact component, publishing, and subscription scopes for both Works topics.</summary>
    [Fact]
    public void PubSubComponentHasExactLiteralEndpointAndTopicScopes()
    {
        YamlMappingNode root = LoadYaml("pubsub.yaml");
        YamlMappingNode spec = Mapping(root, "spec");
        Scalar(spec, "type").ShouldBe("pubsub.redis");
        Dictionary<string, string> metadata = Sequence(spec, "metadata").Children
            .Cast<YamlMappingNode>()
            .ToDictionary(static item => Scalar(item, "name"), static item => Scalar(item, "value"), StringComparer.Ordinal);
        metadata.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["redisHost"] = "localhost:6379",
            ["redisPassword"] = string.Empty,
            ["publishingScopes"] = "eventstore=work.events,commanddeadletter.work.events;works=deadletter.work.events;eventstore-operations=",
            ["subscriptionScopes"] = "eventstore=;works=work.events;eventstore-operations=deadletter.work.events",
            ["allowedTopics"] = "work.events,deadletter.work.events,commanddeadletter.work.events",
            ["protectedTopics"] = "work.events,deadletter.work.events,commanddeadletter.work.events",
        });
        Sorted(Sequence(root, "scopes").Children.Cast<YamlScalarNode>().Select(static item => item.Value ?? string.Empty))
            .ShouldBe(Sorted([EventStoreName, WorksName, EventStoreOperationsName]));
        string.Join(';', metadata.Values).ShouldNotContain("{env:");

        // The scope strings above are literals; these assertions pin the invariant that makes them correct.
        // Dapr forwards a poison message from the *subscribing* sidecar, publishing under its own app id, and
        // deadletter.work.events is a protected topic. So the topic Works may publish must be exactly the topic
        // the operations workload drains, or the capture path receives nothing and the feature is inert.
        IReadOnlyDictionary<string, string> publish = ParseScopes(metadata["publishingScopes"]);
        IReadOnlyDictionary<string, string> subscribe = ParseScopes(metadata["subscriptionScopes"]);
        publish[WorksName].ShouldBe(subscribe[EventStoreOperationsName]);
        publish[WorksName].ShouldBe(DeadLetterTopic);
        subscribe[WorksName].ShouldBe(SourceTopic);
        publish[EventStoreOperationsName].ShouldBeEmpty();
        subscribe[EventStoreName].ShouldBeEmpty();
        Sorted(metadata["protectedTopics"].Split(',')).ShouldBe(
            Sorted([SourceTopic, DeadLetterTopic, CommandDeadLetterTopic]));

        // EventStore publishes the domain topic and its own command dead letters, and nothing else. The command
        // dead-letter topic is derived, not configured: EventPublisherOptions builds it as
        // "{DeadLetterTopicPrefix}.{GetPubSubTopic(identity)}", and this composition overrides the work domain's
        // topic to work.events. With the shipped default prefix that derivation lands on the subscriber DLQ, so
        // this asserts both halves of the fix: the grant exists, and the derived topic is a different queue.
        Sorted(publish[EventStoreName].Split(',')).ShouldBe(Sorted([SourceTopic, CommandDeadLetterTopic]));
        CommandDeadLetterTopic.ShouldNotBe(DeadLetterTopic);
    }

    /// <summary>
    /// Verifies the composed command dead-letter prefix keeps that queue off the subscriber dead-letter topic.
    /// </summary>
    /// <remarks>
    /// The prefix is only half of the invariant -- the other half is the domain topic override, which this same
    /// AppHost sets. Deriving the topic here the way EventPublisherOptions does keeps the two settings pinned
    /// together: changing either one alone would silently re-merge the queues or forbid the publish.
    /// </remarks>
    [Fact]
    public async Task EventStoreCommandDeadLettersUseADistinctTopicFromTheSubscriberQueue()
    {
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(["--EnableKeycloak=false"], TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        ProjectResource eventStore = Project(builder, EventStoreName);
        Dictionary<string, object> environment =
            await EvaluateEnvironmentAsync(eventStore, builder.ExecutionContext);

        StringValue(environment, "EventStore__Publisher__TopicOverrides__work").ShouldBe(SourceTopic);
        StringValue(environment, "EventStore__Publisher__DeadLetterTopicPrefix").ShouldBe(CommandDeadLetterPrefix);

        string derived = StringValue(environment, "EventStore__Publisher__DeadLetterTopicPrefix")
            + "."
            + StringValue(environment, "EventStore__Publisher__TopicOverrides__work");
        derived.ShouldBe(CommandDeadLetterTopic);
        derived.ShouldNotBe(DeadLetterTopic);
    }

    /// <summary>
    /// Verifies both Dapr access-control configurations deny by default and grant exactly one caller path.
    /// </summary>
    /// <remarks>
    /// These two documents are the enforcement point for operator access and for replay reaching Works. Without
    /// a content assertion, deleting the operations grant or restoring an allow-by-default action would break
    /// replay, or open the operator surface to any app id, with every test still green.
    /// </remarks>
    [Fact]
    public void AccessControlConfigurationsDenyByDefaultAndGrantExactlyOneCallerPath()
    {
        YamlMappingNode worksAccessControl = Mapping(LoadYaml("accesscontrol.works.yaml"), "spec", "accessControl");
        AssertMtls(LoadYaml("accesscontrol.works.yaml"));
        Scalar(worksAccessControl, "defaultAction").ShouldBe("deny");
        YamlMappingNode eventStorePolicy = Sequence(worksAccessControl, "policies").Children
            .Cast<YamlMappingNode>()
            .Single(static policy => string.Equals(
                Scalar(policy, "appId"),
                EventStoreName,
                StringComparison.Ordinal));
        Scalar(eventStorePolicy, "defaultAction").ShouldBe("deny");
        YamlMappingNode sharedRebuildOperation = Sequence(eventStorePolicy, "operations").Children
            .Cast<YamlMappingNode>()
            .Single(static operation => string.Equals(
                Scalar(operation, "name"),
                "/project/rebuild/shared/v1",
                StringComparison.Ordinal));
        Scalar(sharedRebuildOperation, "action").ShouldBe("allow");
        Sequence(sharedRebuildOperation, "httpVerb").Children
            .Cast<YamlScalarNode>()
            .Select(static verb => verb.Value ?? string.Empty)
            .ShouldBe(["POST"]);

        YamlMappingNode replayPolicy = Sequence(worksAccessControl, "policies").Children
            .Cast<YamlMappingNode>()
            .Single(static policy => string.Equals(
                Scalar(policy, "appId"),
                EventStoreOperationsName,
                StringComparison.Ordinal));
        Scalar(replayPolicy, "defaultAction").ShouldBe("deny");
        YamlMappingNode replayOperation = Sequence(replayPolicy, "operations").Children
            .Cast<YamlMappingNode>()
            .ShouldHaveSingleItem();
        Scalar(replayOperation, "name").ShouldBe("/work/events");
        Scalar(replayOperation, "action").ShouldBe("allow");
        Sequence(replayOperation, "httpVerb").Children
            .Cast<YamlScalarNode>()
            .Select(static verb => verb.Value ?? string.Empty)
            .ShouldBe(["POST"]);

        YamlMappingNode operationsAccessControl = Mapping(
            LoadYaml("accesscontrol.eventstore-operations.yaml"),
            "spec",
            "accessControl");
        AssertMtls(LoadYaml("accesscontrol.eventstore-operations.yaml"));
        Scalar(operationsAccessControl, "defaultAction").ShouldBe("deny");
        YamlMappingNode operatorPolicy = Sequence(operationsAccessControl, "policies").Children
            .Cast<YamlMappingNode>()
            .ShouldHaveSingleItem();
        Scalar(operatorPolicy, "appId").ShouldBe(EventStoreAdminName);
        Scalar(operatorPolicy, "defaultAction").ShouldBe("deny");
        YamlMappingNode operatorOperation = Sequence(operatorPolicy, "operations").Children
            .Cast<YamlMappingNode>()
            .ShouldHaveSingleItem();
        Scalar(operatorOperation, "name").ShouldBe("/internal/dead-letters/**");
        Scalar(operatorOperation, "action").ShouldBe("allow");
        Sorted(Sequence(operatorOperation, "httpVerb").Children
            .Cast<YamlScalarNode>()
            .Select(static verb => verb.Value ?? string.Empty))
            .ShouldBe(Sorted(["GET", "POST"]));
    }

    /// <summary>Verifies every receiver configuration and Sentry use the same enabled mTLS control plane.</summary>
    [Fact]
    public void DaprConfigurationsUseTheSameSelfHostedSentry()
    {
        AssertMtls(LoadYaml("accesscontrol.yaml"));
        AssertMtls(LoadYaml("accesscontrol.eventstore-admin.yaml"));
        AssertMtls(LoadYaml("accesscontrol.eventstore-operations.yaml"));
        AssertMtls(LoadYaml("accesscontrol.works.yaml"));

        YamlMappingNode sentryMtls = Mapping(LoadYaml("sentry.yaml"), "spec", "mtls");
        Scalar(sentryMtls, "enabled").ShouldBe("true");
        Scalar(sentryMtls, "workloadCertTTL").ShouldBe("24h");
        Scalar(sentryMtls, "allowedClockSkew").ShouldBe("15m");
    }

    private static void AssertMtls(YamlMappingNode configuration)
    {
        YamlMappingNode mtls = Mapping(configuration, "spec", "mtls");
        Scalar(mtls, "enabled").ShouldBe("true");
        Scalar(mtls, "sentryAddress").ShouldBe("127.0.0.1:50001");
        Scalar(mtls, "controlPlaneTrustDomain").ShouldBe("localhost");

        YamlMappingNode accessControl = Mapping(configuration, "spec", "accessControl");
        Scalar(accessControl, "trustDomain").ShouldBe("public");
        Sequence(accessControl, "policies").Children
            .Cast<YamlMappingNode>()
            .ShouldAllBe(static policy => string.Equals(
                Scalar(policy, "trustDomain"),
                "public",
                StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> ParseScopes(string value)
        => value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(static entry => entry.Split('=', 2))
            .ToDictionary(
                static parts => parts[0],
                static parts => parts.Length > 1 ? parts[1] : string.Empty,
                StringComparer.Ordinal);

    /// <summary>Verifies the inbound pub/sub retry target resolves to the intended bounded policy.</summary>
    [Fact]
    public void ResiliencyComponentHasExactInboundRetryTargetAndPolicy()
    {
        YamlMappingNode root = LoadYaml(Path.Combine(ResiliencyName, "resiliency.yaml"));

        Scalar(root, "apiVersion").ShouldBe("dapr.io/v1alpha1");
        Scalar(root, "kind").ShouldBe("Resiliency");
        Scalar(Mapping(root, "metadata"), "name").ShouldBe("resiliency");

        YamlMappingNode inboundPolicy = Mapping(root, "spec", "policies", "retries", "pubsubRetryInbound");
        Scalar(inboundPolicy, "policy").ShouldBe("exponential");
        Scalar(inboundPolicy, "maxInterval").ShouldBe("30s");
        Scalar(inboundPolicy, "maxRetries").ShouldBe("10");

        // daprd unmarshals every spec.policies section into string-valued Go structs and rejects the *entire*
        // Resiliency document when any leaf is a mapping — which would leave the bounded inbound retry budget
        // above inert while every value assertion here still passed. Asserting the shape of only the section
        // that broke once would leave the same hole open everywhere else, so check the whole policy tree:
        // timeouts is map[string]string, retries and circuitBreakers are map[string]<struct of scalars>.
        YamlMappingNode timeouts = Mapping(root, "spec", "policies", "timeouts");
        ShouldBeScalarValued(timeouts, "spec.policies.timeouts");
        ShouldBeScalarValuedPolicies(Mapping(root, "spec", "policies", "retries"), "spec.policies.retries");
        ShouldBeScalarValuedPolicies(Mapping(root, "spec", "policies", "circuitBreakers"), "spec.policies.circuitBreakers");
        Scalar(timeouts, "daprSidecar").ShouldBe("5s");
        Scalar(timeouts, "pubsubTimeout").ShouldBe("10s");
        Scalar(timeouts, "subscriberTimeout").ShouldBe("30s");

        YamlMappingNode inboundTarget = Mapping(root, "spec", "targets", "components", PubSubName, "inbound");
        Scalar(inboundTarget, "retry").ShouldBe("pubsubRetryInbound");
        Scalar(inboundTarget, "timeout").ShouldBe("subscriberTimeout");
        inboundTarget.Children.Keys
            .OfType<YamlScalarNode>()
            .Select(static key => key.Value ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["retry", "timeout"]);

        YamlMappingNode stateStoreTarget = Mapping(root, "spec", "targets", "components", StateStoreName);
        stateStoreTarget.Children.Keys.Cast<YamlScalarNode>().Select(static key => key.Value ?? string.Empty)
            .ShouldBe(["outbound"]);
        YamlMappingNode stateStoreOutbound = Mapping(stateStoreTarget, "outbound");
        Scalar(stateStoreOutbound, "retry").ShouldBe("defaultRetry");
        Scalar(stateStoreOutbound, "timeout").ShouldBe("daprSidecar");
        Scalar(stateStoreOutbound, "circuitBreaker").ShouldBe("defaultBreaker");
    }

    /// <summary>Verifies the resiliency resources directory stays isolated to the one committed CRD.</summary>
    [Fact]
    public void ResiliencyResourceDirectoryContainsOnlyTheCommittedPolicyDocument()
    {
        // Every composed sidecar gets DaprComponents/resiliency on --resources-path, so anything else dropped
        // into that directory reaches all three sidecars. Equally, a Resiliency document left in the
        // DaprComponents root would be loaded incidentally again — the exact coupling the move removed.
        string componentsDirectory = ComponentsDirectory();
        Directory.GetFiles(Path.Combine(componentsDirectory, ResiliencyName))
            .Select(Path.GetFileName)
            .ShouldBe(["resiliency.yaml"]);
        Directory.GetFiles(componentsDirectory, "*.yaml")
            .ShouldAllBe(file => !File.ReadAllText(file).Contains("kind: Resiliency", StringComparison.Ordinal));
    }

    private static void ShouldBeScalarValued(YamlMappingNode section, string path)
        => section.Children.ShouldAllBe(
            static entry => entry.Value is YamlScalarNode,
            $"Every '{path}' entry must be a duration scalar; daprd rejects the whole document otherwise.");

    private static void ShouldBeScalarValuedPolicies(YamlMappingNode section, string path)
    {
        foreach (KeyValuePair<YamlNode, YamlNode> policy in section.Children)
        {
            string name = policy.Key.ShouldBeOfType<YamlScalarNode>().Value ?? string.Empty;
            ShouldBeScalarValued(
                policy.Value.ShouldBeOfType<YamlMappingNode>(),
                $"{path}.{name}");
        }
    }

    private static IDaprComponentResource Component(IDistributedApplicationTestingBuilder builder, string name)
        => builder.Resources
            .OfType<IDaprComponentResource>()
            .Single(component => string.Equals(component.Name, name, StringComparison.Ordinal));

    private static async Task<Dictionary<string, object>> EvaluateEnvironmentAsync(
        IResource resource,
        DistributedApplicationExecutionContext executionContext,
        Dictionary<string, object>? initialEnvironment = null)
    {
        var context = new EnvironmentCallbackContext(
            executionContext,
            resource,
            initialEnvironment ?? new Dictionary<string, object>(),
            TestContext.Current.CancellationToken);
        foreach (EnvironmentCallbackAnnotation annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        return context.EnvironmentVariables;
    }

    private static async Task<string[]> EvaluateArgsAsync(ContainerResource resource)
    {
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args, resource, TestContext.Current.CancellationToken);
        foreach (CommandLineArgsCallbackAnnotation annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        return [.. args.Select(static argument => argument.ToString() ?? string.Empty)];
    }

    private static void AssertControlPlaneImageAndCredentials(ContainerResource resource, string entrypoint)
    {
        ContainerImageAnnotation image = resource.Annotations.OfType<ContainerImageAnnotation>().ShouldHaveSingleItem();
        image.Image.ShouldBe("daprio/dapr");
        image.Tag.ShouldBe("1.18.3");
        resource.Entrypoint.ShouldBe(entrypoint);
        resource.Annotations.OfType<HealthCheckAnnotation>().ShouldHaveSingleItem();

        ContainerMountAnnotation credentials = resource.Annotations.OfType<ContainerMountAnnotation>()
            .Single(static mount => string.Equals(mount.Target, "/var/run/dapr/credentials", StringComparison.Ordinal));
        credentials.Type.ShouldBe(ContainerMountType.BindMount);
        credentials.Source.ShouldNotBeNull().ShouldNotStartWith(LocateRepositoryRoot(), Case.Sensitive);
        credentials.IsReadOnly.ShouldBeTrue();
    }

    private static void AssertGrpcEndpoint(ContainerResource resource, int port, int targetPort)
    {
        EndpointAnnotation grpc = resource.Annotations.OfType<EndpointAnnotation>()
            .Single(static endpoint => string.Equals(endpoint.Name, "grpc", StringComparison.Ordinal));
        grpc.Port.ShouldBe(port);
        grpc.TargetPort.ShouldBe(targetPort);
        grpc.IsProxied.ShouldBeTrue();
    }

    private static string[] HealthKeys(ProjectResource resource)
        => Sorted(resource.Annotations.OfType<HealthCheckAnnotation>().Select(static annotation => annotation.Key));

    private static string[] Sorted(IEnumerable<string> values)
        => [.. values.Order(StringComparer.Ordinal)];

    private static YamlMappingNode LoadYaml(string relativePath)
    {
        var yaml = new YamlStream();
        using TextReader reader = File.OpenText(Path.Combine(ComponentsDirectory(), relativePath));
        yaml.Load(reader);
        return yaml.Documents.ShouldHaveSingleItem().RootNode.ShouldBeOfType<YamlMappingNode>();
    }

    private static string ComponentsDirectory()
        => Path.Combine(LocateRepositoryRoot(), "src", "Hexalith.Works.AppHost", "DaprComponents");

    private static string[] ReferencedComponents(IDaprSidecarResource sidecar)
        => sidecar.TryGetAnnotationsOfType<DaprComponentReferenceAnnotation>(out IEnumerable<DaprComponentReferenceAnnotation>? annotations)
            ? [.. annotations.Select(static annotation => annotation.Component.Name).Order(StringComparer.Ordinal)]
            : [];

    private static string[] ReferencedResources(ProjectResource resource)
        => [.. resource.Annotations
            .OfType<ResourceRelationshipAnnotation>()
            .Where(static annotation => string.Equals(annotation.Type, "Reference", StringComparison.Ordinal))
            .Select(static annotation => annotation.Resource.Name)
            .Order(StringComparer.Ordinal)];

    private static string[] WaitedResources(IResource resource)
        => [.. resource.Annotations
            .OfType<WaitAnnotation>()
            .Select(static annotation => annotation.Resource.Name)
            .Order(StringComparer.Ordinal)];

    private static ProjectResource Project(IDistributedApplicationTestingBuilder builder, string name)
        => builder.Resources
            .OfType<ProjectResource>()
            .Single(resource => string.Equals(resource.Name, name, StringComparison.Ordinal));

    private static IDaprSidecarResource Sidecar(ProjectResource project)
    {
        project.TryGetAnnotationsOfType<DaprSidecarAnnotation>(out IEnumerable<DaprSidecarAnnotation>? annotations)
            .ShouldBeTrue();
        return annotations.ShouldNotBeNull().ShouldHaveSingleItem().Sidecar;
    }

    private static DaprSidecarOptions SidecarOptions(IDaprSidecarResource sidecar)
    {
        sidecar.TryGetLastAnnotation<DaprSidecarOptionsAnnotation>(out DaprSidecarOptionsAnnotation? annotation)
            .ShouldBeTrue();
        return annotation.ShouldNotBeNull().Options;
    }

    private static string StringValue(IReadOnlyDictionary<string, object> environment, string name)
        => environment[name].ShouldBeOfType<string>();

    private static string[] EnvironmentKeys(IReadOnlyDictionary<string, object> environment, string prefix)
        => [.. environment.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    private static YamlMappingNode Mapping(YamlMappingNode root, params string[] path)
    {
        YamlNode current = root;
        foreach (string segment in path)
        {
            current = current.ShouldBeOfType<YamlMappingNode>().Children[new YamlScalarNode(segment)];
        }

        return current.ShouldBeOfType<YamlMappingNode>();
    }

    private static YamlSequenceNode Sequence(YamlMappingNode root, string key)
        => root.Children[new YamlScalarNode(key)].ShouldBeOfType<YamlSequenceNode>();

    private static string Scalar(YamlMappingNode root, string key)
        => root.Children[new YamlScalarNode(key)].ShouldBeOfType<YamlScalarNode>().Value ?? string.Empty;

    private static string LocateRepositoryRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hexalith.Works.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Hexalith.Works.slnx from the test working directory.");
    }
}
