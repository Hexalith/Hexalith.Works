# EventStore API Surface Constraints

Story 1.1 verified the live `Hexalith.EventStore` source surface before Works depends on domain behavior in later stories.

## Concurrency

EventStore does not expose an explicit `expectedVersion` append argument. Optimistic concurrency is implemented through the Dapr state-store ETag used by `AggregateActor.SaveStateAsync()`, which raises `ConcurrencyConflictException` after configured retries. Later Works claim and single-writer stories must translate that infrastructure conflict into Works domain rejections instead of assuming an expected-version append API.

## Online Rebuild

EventStore online rebuild is operator-initiated, checkpoint-per-aggregate, and pausable through `IProjectionRebuildOrchestrator`, `ProjectionRebuildCheckpoint`, and `ProjectionRebuildStatus`. It is not a shadow-projection plus atomic-swap model. Later Works projection stories must align per-tenant rebuild behavior to the checkpoint orchestrator before depending on the earlier architecture wording.

## Story 4.5 — Command/Event Pipeline Under Aspire (runtime adapter proof)

Story 4.5 wired the first runnable adapter edge (`src/Hexalith.Works`) and the Works AppHost topology. The
verified EventStore domain-service surface is:

- **Discovery requires a concrete `EventStoreAggregate<TState>` subclass.** `AssemblyScanner` only discovers
  subclasses of `Hexalith.EventStore.Client.Aggregates.EventStoreAggregate<TState>` (and
  `EventStoreProjection<TReadModel>`). The pure static `WorkItemAggregate` is **not** discovered. The host
  therefore provides `WorkItemEventStoreAggregate : EventStoreAggregate<WorkItemState>` decorated
  `[EventStoreDomain("work")]` (the convention would otherwise derive `work-item-event-store`), declaring one
  `public static DomainResult Handle(TCommand, WorkItemState?)` wrapper per Works command that delegates verbatim
  to the pure kernel. No EventStore runtime inheritance leaks into `Server` — the `Server -> Contracts` direction
  is preserved (fitness-asserted).
- **Canonical host shape.** A domain module is two lines — `builder.AddEventStoreDomainService(assembly)` then
  `app.UseEventStoreDomainService()`. The SDK supplies the platform service defaults (health/OpenTelemetry),
  convention discovery/registration of aggregates + `IDomainQueryHandler`s, runtime activation, and the canonical
  `/process`, `/replay-state`, `/query`, `/project`, and `/admin/operational-index-metadata` endpoints. Per the
  EventStore domain-module contract a domain must **not** fork its own ServiceDefaults, so the host does not
  reference `Hexalith.Works.ServiceDefaults`.
- **Polymorphic registration is required at the host edge.** Commands arrive at `/process` through the shared
  static `PolymorphicSerializationResolver` registry, so the host calls
  `HexalithWorksContractsSerialization.RegisterPolymorphicMappers()` (plus the DI registration) at startup.
- **Persist-then-publish.** `AggregateActor` persists events (the `EventsStored` checkpoint via `SaveStateAsync`,
  raising `ConcurrencyConflictException` on the Dapr state-store ETag conflict) **before** `PublishEventsAsync`.
  Command status advances Received → Processing → EventsStored → EventsPublished → Completed; the smoke test polls
  `/api/v1/commands/status/{correlationId}` to a terminal status.

### Projection-model reconciliation — what is wired and what is still deferred

- **Wired:** a bespoke async `/project` handler (mapped before `UseEventStoreDomainService`, so the SDK yields the
  route) decodes each `ProjectionEventDto` from its **concrete** persisted form (`JsonSerializerDefaults.Web`, no
  polymorphic `$type` — the byte-frozen golden-corpus form) keyed by `EventTypeName`, feeds the pure
  `WhatsNextQueueProjection` and `WorkItemRollUpProjection`, and persists a tenant-scoped `works-whats-next` index
  plus per-item roll-up through `IReadModelStore` + `ReadModelWritePolicy` (idempotent per-item merge, ETag-guarded).
  A discovered `WhatsNextQueryHandler : IDomainQueryHandler` reads that index and applies the pure
  `WhatsNextOrdering` + `WhatsNextQueryAuthorization`.
- **Still deferred (documented limitation, not silently faked):** the EventStore `/project` contract delivers a
  **single aggregate's** event stream per call. Cross-aggregate "rolled remaining" (child/sibling contributions)
  cannot be assembled within one dispatch; each item composes its own remaining, and the tenant-wide eligible set
  is assembled by idempotent per-item merges across dispatches. Full multi-aggregate roll-up convergence and the
  live persist-then-publish round-trip are exercised only by the Tier-3 Aspire runtime lane (Docker + `dapr init`
  + placement/scheduler). In a headless sandbox that lane **skips**; the adapter-level convergence is proven
  deterministically by `WorkItemProjectionQueryAdapterTests` and the topology by `WorksAppHostTopologyTests`.

### Build reconciliation

Aspire package pins were reconciled from 13.4.3 to **13.4.5** (and `Aspire.AppHost.Sdk` to 13.4.5) to match the
checked-out `Hexalith.EventStore` submodule, which `Hexalith.EventStore.Aspire` requires. This is a submodule-drift
alignment forced by the ProjectReference rule, not a discretionary upgrade.
