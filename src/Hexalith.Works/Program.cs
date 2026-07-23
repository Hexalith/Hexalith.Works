using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Registration;
using Hexalith.EventStore.Contracts.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.Works;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.Extensions;
using Hexalith.Works.Projections;
using Hexalith.Works.Recovery.Cascade;
using Hexalith.Works.Recovery.ChildCompletion;
using Hexalith.Works.Reminders;
using Hexalith.Works.Runtime;
using Hexalith.Works.Runtime.Events;

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

// Unhandled endpoint failures (e.g. read-model store outages inside the bespoke /project handler) must
// surface as RFC 9457 application/problem+json — never a bare 500. The default service carries the
// traceId correlation extension; payload-bearing detail stays out per the logging/privacy rules.
_ = builder.Services.AddProblemDetails();

// Dapr-backed persisted read models for the what's-next projection/query adapter.
builder.Services.AddDaprClient();
_ = builder.Services.AddEventStoreReadModelStore();

// EventStore publishes Works events as Web JSON on one shared work.events topic. Register the SDK's durable
// Dapr marker store, but route the payload through the Works-local Web JSON processor so malformed deliveries
// are terminally acknowledged instead of becoming a poison-message retry loop.
_ = builder.Services.AddEventStoreDomainEvents(typeof(WorkItemCreated).Assembly, static options =>
{
    options.TopicName = "work.events";
    options.SubscriptionRoute = "/work/events";
});
_ = builder.Services.AddDaprEventStoreDomainEventMarkerStore();
_ = builder.Services.AddSingleton<WorksDomainEventProcessor>();
_ = builder.Services.AddEventStoreDomainEventHandler<WorkItemCancelled, WorkItemCancelledCascadeHandler>();
_ = builder.Services.AddEventStoreDomainEventHandler<WorkItemExpired, WorkItemExpiredCascadeHandler>();
_ = builder.Services.AddEventStoreDomainEventHandler<WorkItemCompleted, WorkItemCompletedResumeHandler>();

// Story 4.8, AC #1: registering a durable date reminder at suspend time is the steady-state trigger — a
// date-suspended item resumes when the date fires without a host restart. Derived from the folded current
// pending set (DD-1), idempotent under the subscription's at-least-once redelivery.
_ = builder.Services.AddEventStoreDomainEventHandler<WorkItemSuspended, WorkItemSuspendedReminderHandler>();

// Story 4.6 recovery edge: date-resume reminder reconciliation and terminal-cascade dispatch/checkpoint/
// replay. The Dapr actor reminders, gateway command path, stores, and clock all live here at the host edge;
// the pure kernel stays clock-/Dapr-free. The reconciliation pass is gated by Works:Recovery configuration.
_ = builder.Services.AddWorksDateReminderActors();
_ = builder.Services.AddWorksReminderAndCascadeRecovery(builder.Configuration);

WebApplication app = builder.Build();

// Route unhandled exceptions through the registered ProblemDetails service (RFC 9457).
_ = app.UseExceptionHandler();
_ = app.UseStatusCodePages();
app.UseCloudEvents();

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

// Programmatic Dapr subscription discovery and the Works-local Web JSON endpoint form one subscription edge.
app.MapWorksDomainEvents();
app.MapSubscribeHandler();

// Map the Dapr actor-runtime endpoints so the date-resume reminder actor receives reminder callbacks.
app.MapActorsHandlers();

await app.RunAsync().ConfigureAwait(false);
