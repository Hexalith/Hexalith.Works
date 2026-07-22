using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>
/// Story 4.5 AC #1/#3 topology proof. This is a <em>model-inspection</em> test: it evaluates the Works AppHost
/// resource graph in-process via <see cref="DistributedApplicationTestingBuilder"/> without starting any
/// container, so it runs in the normal sandbox lane (no Docker/Dapr required) and a miswired topology fails
/// here. It asserts the command/event pipeline resources exist (EventStore gateway, Admin.Server, the runnable
/// Works domain service with a Dapr sidecar, and the shared Dapr state store / pub-sub), and that no production
/// UI / MCP / chatbot / email / routing / cost / security-hardening surface is composed for this proof.
/// </summary>
public sealed class WorksAppHostTopologyTests
{
    [Fact]
    public async Task AppHost_model_wires_the_command_event_pipeline_and_excludes_production_surfaces()
    {
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Hexalith_Works_AppHost>(["--EnableKeycloak=false"], TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        string[] names = [.. builder.Resources.Select(resource => resource.Name)];

        names.ShouldContain("eventstore", "The EventStore command gateway must be composed.");
        names.ShouldContain("eventstore-admin", "The EventStore Admin.Server must be composed.");
        names.ShouldContain("works", "The runnable Works domain service must be composed.");

        IResource eventStore = builder.Resources.Single(resource => string.Equals(resource.Name, "eventstore", StringComparison.Ordinal));
        eventStore.Annotations.OfType<HealthCheckAnnotation>().ShouldNotBeEmpty(
            "Tier-3 lanes must not submit commands until the EventStore HTTP host reports alive.");

        // Works runs as an EventStore domain module: it has a Dapr sidecar (shared state store + pub/sub).
        IResource works = builder.Resources.Single(resource => string.Equals(resource.Name, "works", StringComparison.Ordinal));
        works.Annotations.Any(annotation => annotation.GetType().Name.Contains("Dapr", StringComparison.Ordinal))
            .ShouldBeTrue("The Works domain service must run with a Dapr sidecar.");
        works.Annotations.OfType<EndpointAnnotation>()
            .Any(endpoint => string.Equals(endpoint.Name, "http", StringComparison.Ordinal))
            .ShouldBeTrue("The Works HTTP endpoint must supply the Dapr sidecar app port for service invocation and subscriptions.");
        works.Annotations.OfType<HealthCheckAnnotation>().ShouldNotBeEmpty(
            "Tier-3 lanes must not submit commands until the Works HTTP host reports alive.");

        // The shared Dapr infrastructure components (actor state store + pub/sub) are present.
        int daprComponents = builder.Resources.Count(resource => resource.GetType().Name.Contains("DaprComponent", StringComparison.Ordinal));
        daprComponents.ShouldBeGreaterThanOrEqualTo(2, "Expected the shared Dapr state store and pub/sub components.");

        string root = LocateRepositoryRoot();
        string statestore = File.ReadAllText(Path.Combine(root, "src", "Hexalith.Works.AppHost", "DaprComponents", "statestore.yaml"));
        statestore.ShouldContain("actorStateStore", Case.Sensitive, "Story 4.6 date reminders require an actor-capable state store.");
        statestore.ShouldContain("value: \"true\"", Case.Sensitive, "The shared state store must be marked as the Dapr actor state store.");
        statestore.ShouldContain("- works", Case.Sensitive, "The Works app-id must stay scoped to the actor-capable state store for reminders and cascade checkpoints.");

        string appHostProgram = File.ReadAllText(Path.Combine(root, "src", "Hexalith.Works.AppHost", "Program.cs"));
        appHostProgram.ShouldContain(
            "EventStore__CommandGateway__BaseAddress",
            Case.Sensitive,
            "The Works recovery runtime must receive the EventStore command gateway base address for reminder/cascade reissue.");
        appHostProgram.ShouldContain(
            "EventStore__Publisher__TopicOverrides__work",
            Case.Sensitive,
            "Works events must publish to the shared work.events subscription topic.");
        appHostProgram.ShouldContain(
            "Authentication__DaprInternal__AllowedCallers__0",
            Case.Sensitive,
            "EventStore must explicitly allow the Works Dapr identity for authenticated recovery reads and commands.");

        string recoveryExtensions = File.ReadAllText(
            Path.Combine(root, "src", "Hexalith.Works", "Runtime", "WorksRecoveryExtensions.cs"));
        recoveryExtensions.ShouldContain(
            "DAPR_HTTP_ENDPOINT",
            Case.Sensitive,
            "The recovery gateway client must route through the local Dapr sidecar when one is composed.");
        recoveryExtensions.ShouldContain(
            "AddEventStoreDaprServiceInvocation",
            Case.Sensitive,
            "The recovery gateway client must carry the authoritative EventStore app-id routing header.");

        // No production UI / MCP / chatbot / email / routing / cost / security-hardening adapters.
        string[] forbiddenFragments = ["mcp", "chatbot", "email", "mail", "datagrid", "webshell", "routing", "cost", "keycloak", "signalr"];
        string[] forbiddenSurfaces = [.. names.Where(name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))];
        forbiddenSurfaces.ShouldBeEmpty($"The pipeline proof must not compose production surfaces: {string.Join(", ", forbiddenSurfaces)}");

        string[] uiSurfaces = [.. names.Where(name => name.EndsWith("-ui", StringComparison.OrdinalIgnoreCase))];
        uiSurfaces.ShouldBeEmpty($"No UI surface is composed in the pipeline proof: {string.Join(", ", uiSurfaces)}");
    }

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
