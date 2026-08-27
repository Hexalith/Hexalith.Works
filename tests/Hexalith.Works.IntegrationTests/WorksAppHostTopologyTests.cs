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
    private const string PubSubName = "pubsub";
    private const string ResiliencyName = "resiliency";
    private const string StateStoreName = "statestore";
    private const string WorksName = "works";

    /// <summary>Verifies exact project, endpoint, sidecar, relationship, and environment values.</summary>
    [Fact]
    public async Task AppHostModelExposesTheExactCommandEventTopology()
    {
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(["--EnableKeycloak=false"], TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        ProjectResource eventStore = Project(builder, EventStoreName);
        ProjectResource adminServer = Project(builder, EventStoreAdminName);
        ProjectResource works = Project(builder, WorksName);

        HealthKeys(eventStore).ShouldBe(Sorted(["eventstore_http_/alive_200_check"]));
        HealthKeys(works).ShouldBe(Sorted(["works_http_/alive_200_check"]));

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

        DaprSidecarOptions eventStoreOptions = SidecarOptions(eventStoreSidecar);
        eventStoreOptions.AppId.ShouldBe(EventStoreName);
        eventStoreOptions.DaprHttpPort.ShouldBe(3501);
        eventStoreOptions.Config.ShouldBe(Path.Combine(componentsDirectory, "accesscontrol.yaml"));
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
        worksOptions.AppPort.ShouldBeNull();
        ReferencedComponents(worksSidecar).ShouldBe([PubSubName, ResiliencyName, StateStoreName]);

        // Two "Reference" relationships to eventstore, not one: AddEventStoreDomainModule contributes the
        // domain-module reference and the explicit EventStore__CommandGateway__BaseAddress endpoint reference
        // contributes the second. Counted rather than de-duplicated so losing either one fails here.
        ReferencedResources(works).ShouldBe([EventStoreName, EventStoreName]);
        WaitedResources(works).ShouldBe([EventStoreName, StateStoreName]);
        ReferencedResources(adminServer).ShouldBe([EventStoreName]);

        // The bounded inbound retry budget only holds where the CRD's directory reaches --resources-path, so no
        // composed sidecar may be missing the reference — including one added after this test was written.
        IDaprSidecarResource[] allSidecars =
        [
            .. builder.Resources.OfType<ProjectResource>()
                .Where(static resource => resource.TryGetAnnotationsOfType<DaprSidecarAnnotation>(out _))
                .Select(Sidecar)
                .Distinct(),
        ];
        allSidecars.Length.ShouldBe(3);
        allSidecars.ShouldAllBe(sidecar => ReferencedComponents(sidecar).Contains(ResiliencyName));

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

        IDaprComponentResource stateStore = Component(builder, StateStoreName);
        stateStore.Type.ShouldBe("state.redis");
        stateStore.Options.ShouldNotBeNull().LocalPath.ShouldBe(Path.Combine(componentsDirectory, "statestore.yaml"));
        IDaprComponentResource pubSub = Component(builder, PubSubName);
        pubSub.Type.ShouldBe("pubsub.redis");
        pubSub.Options.ShouldNotBeNull().LocalPath.ShouldBe(Path.Combine(
            LocateRepositoryRoot(),
            "references",
            "Hexalith.EventStore",
            "src",
            "Hexalith.EventStore.AppHost",
            "DaprComponents",
            "pubsub.yaml"));
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
            .ShouldBe(Sorted([EventStoreName, WorksName, EventStoreAdminName]));
    }

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
        ProjectResource resource,
        DistributedApplicationExecutionContext executionContext)
    {
        var context = new EnvironmentCallbackContext(
            executionContext,
            resource,
            new Dictionary<string, object>(),
            TestContext.Current.CancellationToken);
        foreach (EnvironmentCallbackAnnotation annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        return context.EnvironmentVariables;
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

    private static string[] WaitedResources(ProjectResource resource)
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
