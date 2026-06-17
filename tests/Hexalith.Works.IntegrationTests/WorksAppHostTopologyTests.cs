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

        // Works runs as an EventStore domain module: it has a Dapr sidecar (shared state store + pub/sub).
        IResource works = builder.Resources.Single(resource => string.Equals(resource.Name, "works", StringComparison.Ordinal));
        works.Annotations.Any(annotation => annotation.GetType().Name.Contains("Dapr", StringComparison.Ordinal))
            .ShouldBeTrue("The Works domain service must run with a Dapr sidecar.");

        // The shared Dapr infrastructure components (actor state store + pub/sub) are present.
        int daprComponents = builder.Resources.Count(resource => resource.GetType().Name.Contains("DaprComponent", StringComparison.Ordinal));
        daprComponents.ShouldBeGreaterThanOrEqualTo(2, "Expected the shared Dapr state store and pub/sub components.");

        // No production UI / MCP / chatbot / email / routing / cost / security-hardening adapters.
        string[] forbiddenFragments = ["mcp", "chatbot", "email", "mail", "datagrid", "webshell", "routing", "cost", "keycloak", "signalr"];
        string[] forbiddenSurfaces = [.. names.Where(name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))];
        forbiddenSurfaces.ShouldBeEmpty($"The pipeline proof must not compose production surfaces: {string.Join(", ", forbiddenSurfaces)}");

        string[] uiSurfaces = [.. names.Where(name => name.EndsWith("-ui", StringComparison.OrdinalIgnoreCase))];
        uiSurfaces.ShouldBeEmpty($"No UI surface is composed in the pipeline proof: {string.Join(", ", uiSurfaces)}");
    }
}
