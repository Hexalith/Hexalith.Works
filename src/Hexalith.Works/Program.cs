using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Registration;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works;
using Hexalith.Works.Contracts.Extensions;
using Hexalith.Works.Projections;
using Hexalith.Works.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register the Works v1 polymorphic catalog (commands + events) with the shared static resolver and DI so
// the domain-service pipeline can (de)serialize Works commands on /process and events on /project.
HexalithWorksContractsSerialization.RegisterPolymorphicMappers();
_ = builder.Services.AddHexalithWorksContractsPolymorphicMappers();

// The EventStore domain-service SDK supplies the platform service defaults (health, OpenTelemetry,
// resilience), convention discovery/registration of the WorkItemEventStoreAggregate (domain "work") plus the
// discovered WhatsNextQueryHandler, runtime activation, and the canonical /process, /replay-state, /query,
// /project, and /admin/operational-index-metadata endpoints. The Works kernel stays pure; this host is the
// only adapter edge that may reference EventStore runtime, Dapr, and ASP.NET hosting.
_ = builder.AddEventStoreDomainService(typeof(WorkItemEventStoreAggregate).Assembly);

// Dapr-backed persisted read models for the what's-next projection/query adapter.
builder.Services.AddDaprClient();
_ = builder.Services.AddEventStoreReadModelStore();

// Story 4.6 recovery edge: date-resume reminder reconciliation and terminal-cascade dispatch/checkpoint/
// replay. The Dapr actor reminders, gateway command path, stores, and clock all live here at the host edge;
// the pure kernel stays clock-/Dapr-free. The reconciliation pass is gated by Works:Recovery configuration.
_ = builder.Services.AddWorksDateReminderActors();
_ = builder.Services.AddWorksReminderAndCascadeRecovery(builder.Configuration);

WebApplication app = builder.Build();

// Bespoke /project handler: translate a single work item's replayed events into the tenant-scoped what's-next
// index + per-item roll-up and notify on a real eligibility/order change. Mapping it before
// UseEventStoreDomainService makes the SDK yield the /project route to this persisted-read-model handler.
_ = app.MapPost("/project", static async (
    ProjectionRequest request,
    IReadModelStore store,
    IServiceProvider services,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var dispatcher = new WorkItemProjectionDispatcher(
        store,
        services.GetService<IProjectionChangeNotifier>(),
        loggerFactory.CreateLogger<WorkItemProjectionDispatcher>());
    return Results.Ok(await dispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false));
});

app.UseEventStoreDomainService();

// Map the Dapr actor-runtime endpoints so the date-resume reminder actor receives reminder callbacks.
app.MapActorsHandlers();

await app.RunAsync().ConfigureAwait(false);
