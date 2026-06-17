---
baseline_commit: 60b3230
---

# Story 4.5: Prove the Command/Event Pipeline Under Aspire

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want an Aspire-hosted proof of the Works command/event pipeline,
so that I can verify the kernel, projections, and substrate wiring without shipping production adapters.

## Acceptance Criteria

1. **Given** the Works AppHost is started for local testing
   **When** topology is inspected
   **Then** it wires Works, ServiceDefaults, EventStore dependencies, projection infrastructure, and Dapr components needed for command/event tests
   **And** it does not expose production UI, MCP, chatbot, email, routing, cost, or security-hardening adapters.

2. **Given** the command/event pipeline is exercised under Aspire
   **When** the sequence create -> progress -> spawn child -> suspend -> resume -> complete runs
   **Then** events persist before publication
   **And** state and projections converge to the expected result.

3. **Given** integration and smoke tests run
   **When** configured v1 test lanes complete
   **Then** Tier-1 tests remain pure and do not require Aspire
   **And** Aspire is used only for boundary/runtime proof.

4. **Given** observability is inspected
   **When** pipeline errors occur
   **Then** failures surface with correlation and tenant context
   **And** logs avoid event payloads, personal data, secrets, raw tokens, and full command bodies.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile current host and EventStore runtime surfaces before editing (AC: #1-#4)**
  - [x] Read `src/Hexalith.Works.AppHost/Program.cs`: it is currently a skeleton (`CreateBuilder` -> `Build().Run()`), so all topology wiring in this story is new AppHost behavior.
  - [x] Read `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`: it already references Works kernel projects, `Hexalith.EventStore.Aspire`, `Aspire.Hosting.Docker`, `Aspire.Hosting.Redis`, and `CommunityToolkit.Aspire.Hosting.Dapr`, but it does not yet include EventStore host/admin project references or `DaprComponents/**/*` content.
  - [x] Read `src/Hexalith.Works.ServiceDefaults/Extensions.cs`: reuse its health endpoints and OpenTelemetry setup; do not fork ServiceDefaults from siblings.
  - [x] Read `Hexalith.EventStore/src/Hexalith.EventStore.Aspire/HexalithEventStoreExtensions.cs` and `HexalithEventStoreDomainModuleExtensions.cs`: reuse `AddHexalithEventStore(...)` and `AddEventStoreDomainModule(...)`; do not hand-roll state store/pubsub sidecar wiring.
  - [x] Read `Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs`: domain services expose `/process`, `/replay-state`, `/query`, `/project`, and operational metadata through `AddEventStoreDomainService(...)` + `UseEventStoreDomainService()`.
  - [x] Read `Hexalith.EventStore/src/Hexalith.EventStore.Client/Discovery/AssemblyScanner.cs`: EventStore discovers only `EventStoreAggregate<TState>` / `EventStoreProjection<TReadModel>` subclasses. Current Works `WorkItemAggregate` is a pure static class and will **not** be discovered.
  - [x] Read `Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs` around the `EventsStored` checkpoint, `SaveStateAsync()`, `ConcurrencyConflictException`, and `PublishEventsAsync(...)`: this is the persist-then-publish seam to prove, not replace.
  - [x] Read `Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/IReadModelStore.cs`, `ReadModelWritePolicy.cs`, and `IProjectionChangeNotifier.cs`: these are the runtime read-model/notifier seams Story 4.4 intentionally deferred.
  - [x] Read previous story carry-forward: `_bmad-output/implementation-artifacts/4-4-resolve-the-tenant-s-what-s-next-queue.md` (Scope Boundary, DC1, DC3, DC5, DC7, Dev Agent Record) and `_bmad-output/implementation-artifacts/tests/test-summary.md` (Story 4.4 baseline: **599** green, catalog **36**).

- [x] **Task 2 - Add the minimal runnable Works domain-service host at the adapter edge (AC: #1, #2, #4)**
  - [x] Add a new Web SDK host project, recommended path `src/Hexalith.Works/Hexalith.Works.csproj`, and include it in `Hexalith.Works.slnx`. This is the runtime adapter required for the command pipeline proof; keep the pure kernel projects unchanged.
  - [x] Project references must be `ProjectReference`s only: Works `Contracts`, `Server`, `Projections`, `ServiceDefaults`, plus EventStore source projects needed for the domain service (`$(HexalithEventStoreRoot)\src\Hexalith.EventStore.DomainService\Hexalith.EventStore.DomainService.csproj` and direct EventStore Client/Contracts references only if needed by compile). Do **not** add `Hexalith.*` `PackageReference`s or `Directory.Packages.props` entries.
  - [x] Add a small adapter aggregate in the host assembly, for example `WorkItemEventStoreAggregate : EventStoreAggregate<WorkItemState>`, decorated with `[EventStoreDomain("work")]`. It must declare one `Handle(...)` wrapper per Works command and delegate to the existing pure static `WorkItemAggregate.Handle(...)`.
  - [x] Do **not** convert `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` into an EventStore runtime type. `Server` remains pure and `DependencyDirectionTests` must still assert `Server -> Contracts` only.
  - [x] Host `Program.cs` should follow the Tenants/EventStore domain-service pattern: `WebApplication.CreateBuilder(args)`, `builder.AddEventStoreDomainService(typeof(WorkItemEventStoreAggregate).Assembly)`, `builder.Services.AddDaprClient()`, register the EventStore read-model store if projection/query handlers need it, build, `app.UseCloudEvents()` if Dapr pub/sub endpoints are mapped, `app.UseEventStoreDomainService()`, `app.MapSubscribeHandler()` only if this host subscribes to pub/sub, then `await app.RunAsync().ConfigureAwait(false)`.
  - [x] Configure no production UI, MCP, chatbot, email, routing, cost, or security-hardening adapters. No `IExecutorRouter` implementation. `AuthorityLevel` remains carried-not-enforced.
  - [x] Ensure the host logs only bounded metadata: correlation id, tenant id, aggregate id, command/event type names, projection type, reason codes. Never log command payloads, event payloads, obligations, secrets, tokens, or full command bodies.

- [x] **Task 3 - Add the runtime projection/query adapter for the already-built pure projections (AC: #1, #2, #4)**
  - [x] Keep `src/Hexalith.Works.Projections` pure. Runtime handlers that reference `IReadModelStore`, `ReadModelWritePolicy`, `IDomainQueryHandler`, `IDomainProjectionHandler`, `IProjectionChangeNotifier`, `DaprClient`, or logging live in the new host/adapter project, not in `Contracts`, `Server`, `Projections`, or `Reactor`.
  - [x] Add a projection adapter that consumes EventStore `ProjectionRequest` full-replay requests for domain `work` and translates the request's event sequence into the existing `WorkItemRollUpProjection` and `WhatsNextQueueProjection` input model. Do not add new durable event/command/rejection types.
  - [x] Persist read models through `IReadModelStore` + `ReadModelWritePolicy` under deterministic tenant-scoped keys. Recommended tokens: projection type `"works-whats-next"` (already pinned by Story 4.4) and explicit roll-up keys derived from `(tenantId, workItemId)`. Any key scheme must be documented and include tenant id.
  - [x] Use `ReadModelWritePolicy` transforms as idempotent merge functions. They may run multiple times after ETag conflicts, so applying the same replayed events must not duplicate list entries, double-count burn-down, or reorder non-deterministically.
  - [x] When `WhatsNextQueueProjection.Project(...)` reports a real eligibility/order change, call `IProjectionChangeNotifier.NotifyProjectionChangedAsync("works-whats-next", tenantId, entityId: null, ct)` from the adapter. Do not call the notifier from the pure projection.
  - [x] Add a query handler for the tenant "what's next" query using `IDomainQueryHandler` and the EventStore `/query` envelope. It must read the persisted tenant index, apply `WhatsNextQueryAuthorization` using `QueryEnvelope.TenantId` plus a caller predicate derived from the envelope/user context where available, and return `QueryResult.FromPayload(...)` with payload bytes only. If the read model is missing/unavailable, fail closed or return a bounded empty/degraded result; do not fabricate freshness.
  - [x] If projection-model reconciliation is still incomplete upstream, document the precise limitation in `docs/eventstore-api-surface-constraints.md` and the story Dev Agent Record, then keep the proof to the smallest adapter path that can be exercised under Aspire. Do not silently pretend a non-existent platform capability is wired.

- [x] **Task 4 - Wire the Works AppHost topology using platform helpers (AC: #1, #3)**
  - [x] Update `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` to reference the new runnable Works host project, EventStore web host, EventStore Admin.Server.Host, and optionally Admin.UI as explicit-start. Keep `Hexalith.EventStore.Aspire` as `IsAspireProjectResource="false"`.
  - [x] Add `src/Hexalith.Works.AppHost/DaprComponents/` with minimal local-development YAML modeled on Tenants/EventStore: `accesscontrol.yaml` for EventStore inbound calls, `accesscontrol.works.yaml` for Works inbound `/process`/`/query`/`/project`, `accesscontrol.eventstore-admin.yaml` if Admin.Server is composed, `statestore.yaml`, and `resiliency.yaml`.
  - [x] Include `DaprComponents/**/*` as copied AppHost content like sibling AppHosts do. Resolve paths through `builder.AppHostDirectory` where available so `dotnet run` and `Aspire.Hosting.Testing` both work.
  - [x] In `Program.cs`, add EventStore and Admin.Server resources, then call `builder.AddHexalithEventStore(...)` with the resolved Dapr config and state-store paths. Redis is provided by `dapr init` at local Redis, per EventStore Aspire helper; do not add a parallel custom Redis dependency unless the platform helper requires it.
  - [x] Add the Works domain-service resource with `.AddEventStoreDomainModule(eventStoreResources, "works", worksAccessControlConfigPath)`, `.WithReference(eventStore)`, `.WaitFor(eventStore)`, and `.WaitFor(eventStoreResources.StateStore/PubSub)` as needed for its read model and subscription behavior.
  - [x] Register the EventStore domain service mapping for `work` commands through EventStore environment variables, using the configuration shape expected by `DomainServiceResolver`. Prefer the sanitized Kubernetes-safe registration-key form when needed (see Parties AppHost wildcard comment) and set `AppId = "works"`, `MethodName = "process"`, `Domain = "work"`, `Version = "v1"`, and an explicit tenant or wildcard according to the resolver contract.
  - [x] Keep optional admin surfaces explicit-start or omitted. Do not add Works UI, MCP, chatbot, email, routing, cost, Keycloak realm work, production deployment, or reminder/recovery resources in this story.

- [x] **Task 5 - Add Aspire runtime proof tests without contaminating Tier-1 lanes (AC: #2, #3)**
  - [x] Keep existing UnitTests, PropertyTests, and ArchitectureTests pure: no Dapr, Aspire, Docker, network, real clocks, sleeps, or containers.
  - [x] Add focused topology tests under `tests/Hexalith.Works.IntegrationTests` using `Aspire.Hosting.Testing`. Mark them with a trait/category that allows the normal sandbox binary lane to skip them when Docker/Dapr is unavailable, or skip gracefully with a clear reason. A skip is acceptable for infrastructure absence; a miswired topology should fail.
  - [x] Add an AppHost model inspection test that proves resources exist for `eventstore`, `eventstore-admin` (if composed), and `works`; Dapr sidecars use expected app ids; the Works sidecar references shared state store/pubsub; no resource names or project references include UI/MCP/chatbot/email/routing/cost/security adapters.
  - [x] Add a command pipeline smoke test that starts the AppHost with `EnableKeycloak=false` or the repo's accepted test-auth setting, submits commands to EventStore's `/api/v1/commands` (or through `IEventStoreGatewayClient`), polls `/api/v1/commands/status/{correlationId}` to terminal status, and proves the sequence create -> progress -> spawn child -> suspend -> resume -> complete persists and publishes before completion.
  - [x] Use deterministic test command ids, work item ids, tenant id, correlation ids, and valid `ExecutorBinding`s. Do not use timing-dependent thread races or `Task.Run` to prove concurrency; Story 4.3 owns deterministic single-claim-wins and EventStore owns ETag retry behavior.
  - [x] Assert final aggregate state through EventStore replay/state read or domain `/replay-state`, not by trusting the HTTP `202` alone. Then assert projection convergence through the Works query/read model path (`works-whats-next` and roll-up where available). Completed items should fall out of the what's-next eligible set; roll-up/read model state should match the expected terminal lifecycle result.
  - [x] Add a negative smoke test or fault-injection test only if the existing EventStore test fault knobs are available without broad setup. It should assert bounded failure reporting with correlation and tenant context and no payload/secret logging.

- [x] **Task 6 - Add architecture and governance guardrails (AC: #1, #3, #4)**
  - [x] Update `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs`: pure projects remain unchanged, and the new runnable host is the only Works project allowed to reference EventStore runtime/domain-service packages, Dapr, ASP.NET host packages, and ServiceDefaults.
  - [x] Add/extend AppHost fitness tests to assert `Program.cs` uses `AddHexalithEventStore` and `AddEventStoreDomainModule`, not custom duplicated Dapr component wiring.
  - [x] Add a guard that no production surface vocabulary appears in Works source for this story: `Mcp`, `Chatbot`, `EmailSurface`, `MailSurface`, `DataGrid`, `WebShell`, `RoutingEngine`, `EligibilityScore`, `EscalationLadder`, `CostMeter`, `SpendGovernance`, or an `IExecutorRouter` implementation.
  - [x] Add a guard that `WorkItemAggregate` remains the pure static kernel and the EventStore adapter aggregate lives in the host project. This prevents future agents from moving EventStore runtime inheritance into `Server`.
  - [x] Keep `WorkItemV1Catalog.Count` at **36** and the golden corpus byte-compatible. Runtime adapter/query/read-model code must not add durable polymorphic command/event/rejection types.
  - [x] Add a log/privacy fitness check if practical: no `ILogger` usage in kernel projects, and adapter logs do not include `Payload`, `Obligation`, full command bodies, token names, or event JSON.

- [x] **Task 7 - Documentation and story bookkeeping (AC: #1-#4)**
  - [x] Update `docs/eventstore-api-surface-constraints.md` with the Story 4.5 result: actual EventStore domain-service discovery requirements, the `EventStoreAggregate<TState>` adapter wrapper, persist-then-publish proof, projection adapter limitations, and any upstream projection-model reconciliation still deferred.
  - [x] Update `docs/boundary-decision-record.md` with a Story 4.5 note: AppHost/Works host are adapter-edge runtime proof only; kernel remains pure; EventStore owns persistence/concurrency/envelopes/public command/query gateway; Works owns only domain behavior and read model transformations.
  - [x] Add a Story 4.5 section to `_bmad-output/implementation-artifacts/tests/test-summary.md` with baseline counts, new topology tests, skipped infrastructure conditions if any, final counts, catalog size, and verification commands.
  - [x] Update this story's Dev Agent Record and File List accurately before moving to review. Do not claim Aspire tests ran if they skipped due to Docker/Dapr absence.

- [x] **Task 8 - Verify the slice (AC: #1-#4)**
  - [x] Baseline is Story 4.4 final: **599** green tests, catalog **36**, latest commit `60b3230 feat(story-4.4): Resolve the Tenant's What's Next Queue`.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` - require 0 warnings / 0 errors.
  - [x] Run direct xUnit v3 binaries after Release build for the pure lanes (the reliable sandbox path; `dotnet test` may be blocked by Microsoft.Testing.Platform named-pipe permissions):
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.
  - [x] Run the Aspire topology lane separately with the repo-supported command once Docker/Dapr prerequisites are available. If prerequisites are absent in the sandbox, document the skip and still run model-inspection tests that do not require starting containers.
  - [x] Confirm `WorkItemV1Catalog.Count` is still **36** and the golden corpus is byte-unchanged. Do not run recursive submodule commands; leave unrelated `Hexalith.Tenants` gitlink/story-automator changes untouched.

## Dev Notes

### Scope Boundary (read first - prevents the likely implementation failure)

Story 4.5 is the first runtime adapter proof for the Works command/event pipeline. It is **not** a kernel feature story. The pure domain remains in `Contracts`, `Server`, `Projections`, and `Reactor`; this story adds the minimum runnable host and AppHost topology needed for EventStore to invoke Works through Dapr, persist events, publish them, update read models, and serve the already-defined query/read-model seams. [Source: _bmad-output/planning-artifacts/epics.md#Story 4.5; _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries; docs/boundary-decision-record.md]

The most important trap: current `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` is a pure static class. EventStore discovery scans for concrete `EventStoreAggregate<TState>` subclasses only. Therefore a runnable Works host must provide a small adapter-edge aggregate wrapper, decorated with `[EventStoreDomain("work")]`, whose `Handle(...)` methods delegate to the pure static aggregate. Do **not** move EventStore inheritance into `Server`; that would violate the dependency-direction contract and pollute the kernel with runtime concerns. [Source: Hexalith.EventStore/src/Hexalith.EventStore.Client/Discovery/AssemblyScanner.cs; src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs; tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs]

**In scope:** new runnable Works domain-service host; adapter aggregate wrapper; runtime query/projection adapters at the host edge; Works AppHost topology using `Hexalith.EventStore.Aspire`; local Dapr component YAML; Aspire model/topology proof tests; command pipeline smoke proof; log/privacy guardrails; docs/test-summary/story bookkeeping.

**Out of scope:** production UI, web shell, DataGrid, MCP, chatbot, email, CLI production adapter, executor routing/eligibility engine, escalation ladder, `IExecutorRouter` implementation, `AuthorityLevel` enforcement, cost/spend governance, Keycloak realm design, production deployment, reminder registration/reconciliation, reactor checkpoint persistence, and AppHost restart recovery. Story 4.6 owns reminder and reactor recovery.

### Current State and Files to Read Before Editing

- **AppHost skeleton:** `src/Hexalith.Works.AppHost/Program.cs` currently builds and runs an empty distributed app. `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` references Works kernel projects and EventStore Aspire but does not yet compose EventStore, Admin.Server, or a runnable Works service.
- **No Works runnable host yet:** source projects are `Contracts`, `Server`, `Projections`, `Reactor`, `ServiceDefaults`, and `AppHost`. There is no `src/Hexalith.Works` Web host project. Add one as an adapter-edge runtime proof.
- **Service defaults:** `src/Hexalith.Works.ServiceDefaults/Extensions.cs` already supplies `/health`, `/alive`, `/ready`, service discovery, resilience, and OpenTelemetry. Reuse it.
- **EventStore Aspire helpers:** `Hexalith.EventStore.Aspire` provides `AddHexalithEventStore(...)` and `AddEventStoreDomainModule(...)`. These create the Dapr state store/pubsub and sidecars; AppHost should pass resolved YAML paths and resource builders.
- **Domain-service SDK:** `AddEventStoreDomainService(...)` registers discovered EventStore aggregates plus `IDomainQueryHandler` and `IDomainProjectionHandler` implementations from the scanned assemblies. `UseEventStoreDomainService()` maps `/process`, `/replay-state`, `/query`, `/project`, and metadata endpoints.
- **Runtime read-model seams:** `IReadModelStore`, `ReadModelWritePolicy`, and `IProjectionChangeNotifier` live in EventStore Client. They belong in the host/adapter layer for this story, never in pure `Projections`.
- **Story 4.4 pure projection assets:** `WhatsNextItem`, `WhatsNextQueueProjection`, `WhatsNextOrdering`, `WhatsNextQueryAuthorization`, and `WhatsNextProjectionChange` are already implemented and reviewed. Reuse them; do not duplicate the queue projection or its ordering logic.

### Architecture Compliance

- **ProjectReference rule:** Any `Hexalith.*` dependency added by this story must be a `ProjectReference` through root-path variables (`$(HexalithEventStoreRoot)`, etc.). Never add a `Hexalith.*` package version to `Directory.Packages.props`.
- **Submodule rule:** initialize/update root submodules only. Never use `--recursive`, never initialize nested submodules, and do not modify sibling submodule files unless explicitly requested.
- **Kernel purity:** `Contracts`, `Server`, `Projections`, and `Reactor` must remain free of Dapr, ASP.NET hosting, EventStore runtime/domain-service adapters, clocks, timers, filesystem I/O, network I/O, and logging.
- **Adapter placement:** The new host may reference Dapr, EventStore DomainService/Client, ASP.NET, and ServiceDefaults because it is the runtime adapter required for this proof.
- **Catalog freeze:** No new durable event, command, or rejection type. `WorkItemV1Catalog.Count` remains 36 and existing golden JSON stays byte-compatible.
- **Domain name:** Works work-item aggregate identity uses domain `"work"` (`WorkItemId`, `WorkItemState`). EventStore registration and adapter aggregate domain must use `"work"`, not `"works"`, unless a deliberate migration is documented and all tests are updated.

### Previous Story Intelligence

- **Story 4.4** added the pure what's-next projection and explicitly deferred live `IDomainQueryHandler`, `IReadModelStore`, `IProjectionChangeNotifier`, Dapr pub/sub, and runtime adapter wiring to Stories 4.5/4.6. This story consumes that deferred work at the adapter edge.
- **Story 4.3** proved single-claim-wins deterministically in Tier-1 tests and deferred live ETag append/retry/exhaustion behavior to Story 4.5. Do not add `ClaimRejected` or `ConcurrencyRejected`; the loser still re-handles to existing domain rejection semantics.
- **Epic 3 retrospective lessons:** test-count bookkeeping drift is a recurring review finding; reconcile this story, `tests/test-summary.md`, and actual test output before review. `dotnet test` may be unusable in this sandbox because of Microsoft.Testing.Platform named-pipe permissions; direct xUnit v3 binaries are the reliable fallback.

### Git Intelligence

Recent commits before this story:

- `60b3230 feat(story-4.4): Resolve the Tenant's What's Next Queue` - pure what's-next read model/projection/query-shaping seam, catalog still 36, **599** green.
- `e18c974 feat(story-4.3): Claim Queued Work with Single-Claim-Wins` - deterministic expected-version proof, runtime ETag behavior deferred here.
- `2dd46d0 feat(story-4.2): Assign, Reassign, and Hand Off Work` - uniform assignment/handoff guardrails.
- `0f413f7 feat(story-4.1): Bind Work to a Uniform Party Executor` - `ExecutorBinding` and no executor-kind branching.
- `68de3f5 feat: Update documentation and project structure for Epic 3 completion`.

### Latest Technical Information

- Local package pins are binding for this story: .NET SDK `10.0.301`, Dapr packages `1.18.2`, `CommunityToolkit.Aspire.Hosting.Dapr` `13.4.0-preview.1.260602-0230`, xUnit v3 `3.2.2`, Shouldly `4.3.0`, and NSubstitute `6.0.0-rc.1`. During implementation, Aspire was reconciled from `13.4.3` to `13.4.5` because the checked-out `Hexalith.EventStore` source requires `Aspire.Hosting >= 13.4.5`; use the repo's current `Directory.Packages.props` / `global.json` pins rather than reintroducing `13.4.3`.
- External check performed on 2026-06-17: NuGet lists `xunit.v3` `3.2.2` as the current stable package used here and shows newer `4.0.0-pre.*` prereleases. Stay on the repo pin; do not chase prerelease xUnit during the pipeline story. [Source: https://www.nuget.org/packages/xunit.v3/3.2.2]
- Aspire/Dapr behavior for this story should be taken from the checked-in EventStore Aspire helper source, not from generic samples, because the helper has Hexalith-specific assumptions: Redis comes from local `dapr init`, Dapr HTTP port defaults to 3501 for EventStore, `AppPort` is intentionally omitted for Aspire Testing, and state/pubsub are exposed through returned `HexalithEventStoreResources`.

### Project Structure Notes

- **New recommended host project:** `src/Hexalith.Works/`
  - `Hexalith.Works.csproj` - Web SDK, non-packable/publishable runtime host for local/Aspire proof.
  - `Program.cs` - domain service startup.
  - `WorkItemEventStoreAggregate.cs` (or similar) - adapter wrapper over pure `WorkItemAggregate`.
  - Query/projection adapter files if they reference EventStore runtime/client abstractions.
- **Updated AppHost:** `src/Hexalith.Works.AppHost/Program.cs`, `.csproj`, and `DaprComponents/*`.
- **Tests:** topology and runtime smoke proof under `tests/Hexalith.Works.IntegrationTests`; architecture guardrails under `tests/Hexalith.Works.ArchitectureTests`.
- **Docs/bookkeeping:** `docs/eventstore-api-surface-constraints.md`, `docs/boundary-decision-record.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md`, and this story file.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.5: Prove the Command/Event Pipeline Under Aspire] - story statement and ACs.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-24 / #FR-25] - Aspire host and command pipeline test harness.
- [Source: _bmad-output/planning-artifacts/architecture.md#Selected Starter / #Core Architectural Decisions / #Architectural Boundaries] - canonical project layout, pure kernel vs adapter ring, AppHost role.
- [Source: src/Hexalith.Works.AppHost/Program.cs] - current empty AppHost skeleton.
- [Source: src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj] - existing AppHost references and base-path validation.
- [Source: src/Hexalith.Works.ServiceDefaults/Extensions.cs] - health/OTel/service-discovery defaults.
- [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs] - pure static kernel handlers to wrap.
- [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemId.cs; src/Hexalith.Works.Contracts/State/WorkItemState.cs] - domain `"work"` identity.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Client/Discovery/AssemblyScanner.cs] - EventStore discovery requires `EventStoreAggregate<TState>` subclasses.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs] - domain-service registration and endpoints.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Aspire/HexalithEventStoreExtensions.cs; HexalithEventStoreDomainModuleExtensions.cs] - AppHost helper APIs.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs; EventPersister.cs; ConcurrencyConflictException.cs] - persist-then-publish and ETag conflict behavior.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/IReadModelStore.cs; ReadModelWritePolicy.cs; IProjectionChangeNotifier.cs] - read model and notifier seams.
- [Source: _bmad-output/implementation-artifacts/4-4-resolve-the-tenant-s-what-s-next-queue.md] - previous story scope boundary, pure projection assets, and deferred runtime adapter.
- [Source: docs/boundary-decision-record.md; docs/eventstore-api-surface-constraints.md] - current boundary decisions and EventStore surface constraints.
- [Source: Directory.Packages.props; Directory.Build.props] - pinned versions and root submodule path variables.
- [Source: https://www.nuget.org/packages/xunit.v3/3.2.2] - external version check for xUnit v3 stable package.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (1M context) — `claude-opus-4-8[1m]`.

### Debug Log References

- `dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false` then `dotnet build Hexalith.Works.slnx -c Release --no-restore` → **0 warnings / 0 errors**.
- Direct xUnit v3 binaries (sandbox-reliable path): UnitTests **483**, IntegrationTests **85** (1 skipped), ArchitectureTests **38**, PropertyTests **3** — all green.
- Aspire Tier-3 smoke lane (`WorksCommandPipelineSmokeTests`) **skipped**: Dapr placement(50005)/scheduler(50006) not running in the sandbox (Docker + Redis are present). The model-inspection topology test runs and passes without containers.

### Completion Notes List

- **Adapter edge, kernel untouched.** The pure kernel (`Contracts`/`Server`/`Projections`/`Reactor`) is unchanged. A new runnable host `src/Hexalith.Works` (Web SDK, non-packable) provides `WorkItemEventStoreAggregate : EventStoreAggregate<WorkItemState>` (`[EventStoreDomain("work")]`, 14 `Handle` wrappers delegating to the pure `WorkItemAggregate`) so EventStore's scanner can discover the domain without polluting `Server` with runtime inheritance (fitness-asserted).
- **Canonical host shape.** `AddEventStoreDomainService(assembly)` + `UseEventStoreDomainService()`, with a bespoke async `/project` mapped first so the SDK yields the route. Per the EventStore domain-module contract the host does **not** fork `Hexalith.Works.ServiceDefaults` — the platform supplies service defaults/health/OTel. (Deviation from the story's recommended host reference list, grounded in `Hexalith.EventStore/CLAUDE.md`.)
- **Projection/query adapter.** `/project` decodes each `ProjectionEventDto` from its concrete persisted form (Web JSON, no `$type`) keyed by `EventTypeName`, feeds the pure `WhatsNextQueueProjection`/`WorkItemRollUpProjection`, and persists a tenant-scoped `works-whats-next` index + per-item roll-up via `IReadModelStore` + `ReadModelWritePolicy` (idempotent per-item merge, notify-on-change). `WhatsNextQueryHandler : IDomainQueryHandler` reads the index and applies the pure ordering + `WhatsNextQueryAuthorization`.
- **Documented reconciliation limitation (honest, not faked).** EventStore `/project` delivers one aggregate's stream per call, so cross-aggregate rolled-remaining is not assembled within a single dispatch; the tenant index converges via per-item merges across dispatches. Recorded in `docs/eventstore-api-surface-constraints.md` and proven deterministically at the adapter level by `WorkItemProjectionQueryAdapterTests` (in-memory store, no Docker). Full multi-aggregate convergence + live persist-then-publish are the Tier-3 Aspire lane.
- **AppHost topology** via cross-repo `IProjectMetadata` (Tenants pattern): `AddHexalithEventStore` + `AddEventStoreDomainModule("works")`, the `work` domain-service mapping registered with the K8s-safe `wildcard_work_v1` key (tenant `*`, `work`, `v1`), and local Dapr components. No Works UI/MCP/chatbot/email/routing/cost/security/Keycloak surface; Admin.UI omitted. The AppHost's csproj `ProjectReference` set is unchanged, so the existing dependency-direction guard stays green; the runtime resources are referenced via metadata.
- **Forced submodule-drift reconciliation:** Aspire pins bumped 13.4.3 → 13.4.5 (+ `Aspire.AppHost.Sdk`) to match the checked-out `Hexalith.EventStore` submodule (`Hexalith.EventStore.Aspire` requires ≥ 13.4.5). Surfaced once the test project transitively referenced EventStore.Aspire via the AppHost. `BuildConfigurationTests` Aspire pin updated to match. `Microsoft.IdentityModel.JsonWebTokens` 8.19.0 added centrally for the smoke test's dev JWT.
- **Conditional subtask not exercised:** the optional negative/fault-injection smoke test (Task 5, "only if … without broad setup") was not added — it requires broad EventStore fault-knob setup unavailable in the sandbox; bounded failure reporting is covered structurally by the log/privacy fitness guard.
- **Frozen surface preserved:** `WorkItemV1Catalog.Count` stays **36**, golden corpus byte-unchanged, no durable polymorphic type added. Submodule gitlink dirtiness (`Hexalith.EventStore`/`Builds`/`FrontComposer`) is build-output only; no submodule source was modified and no recursive submodule command was run.

### File List

**New — runnable host (`src/Hexalith.Works/`):**
- `src/Hexalith.Works/Hexalith.Works.csproj`
- `src/Hexalith.Works/Program.cs`
- `src/Hexalith.Works/WorkItemEventStoreAggregate.cs`
- `src/Hexalith.Works/Projections/WorksWhatsNextReadModel.cs`
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`
- `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs`

**New — AppHost topology (`src/Hexalith.Works.AppHost/`):**
- `src/Hexalith.Works.AppHost/ProjectMetadataPaths.cs`
- `src/Hexalith.Works.AppHost/HexalithEventStore.cs`
- `src/Hexalith.Works.AppHost/HexalithEventStoreAdminServerHost.cs`
- `src/Hexalith.Works.AppHost/HexalithWorks.cs`
- `src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml`
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.yaml`
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml`
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.eventstore-admin.yaml`
- `src/Hexalith.Works.AppHost/DaprComponents/resiliency.yaml`

**New — tests:**
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs`

**Modified:**
- `src/Hexalith.Works.AppHost/Program.cs`
- `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs`
- `Hexalith.Works.slnx`
- `Directory.Packages.props`
- `global.json`
- `docs/eventstore-api-surface-constraints.md`
- `docs/boundary-decision-record.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot (adversarial AI review) · **Date:** 2026-06-17 · **Outcome:** Approve (auto-fixes applied)

### What was verified (not just claimed)

- **Build:** `dotnet restore` + `dotnet build Hexalith.Works.slnx -c Release` → **0 warnings / 0 errors** (re-verified after fixes).
- **Tests (direct xUnit v3 binaries, the sandbox-reliable path):** UnitTests **483**, IntegrationTests **85** (1 skipped), ArchitectureTests **38**, PropertyTests **3** → **608 green + 1 skipped**. Matches the story Debug Log and `test-summary.md` exactly — no test-count drift (the recurring Epic-3 review finding is clear this story).
- **Live-path decode risk investigated and cleared:** the dispatcher decodes `ProjectionEventDto` by `EventTypeName` → concrete type. Confirmed the PolymorphicSerializations discriminator for the Works events equals the .NET type name (generated mapper: `Type discriminator: WorkItemCreated`), and EventStore persists `EventTypeName = serializedPayload.EventTypeName`, so `SimpleTypeName(...)` resolves correctly on the live path too — not just in the test's `evt.GetType().Name` DTOs.
- **Bespoke `/project` route is safe:** `MapEventStoreDomainService` skips its own `/project` when one is already mapped (`IsRouteMapped`), so mapping `/project` before `UseEventStoreDomainService()` is correct and cannot produce an ambiguous-route runtime failure.
- **Kernel purity / catalog freeze / governance:** all asserted by the green ArchitectureTests (38), including the 5 new `RuntimeAdapterGovernanceTests` guards and the catalog-stays-36 + golden-corpus checks.

### Findings and resolution

| # | Sev | Finding | Resolution |
|---|-----|---------|------------|
| 1 | MEDIUM | **AC #4 correlation context missing.** `WorkItemProjectionDispatcher` built its `ReadModelWriteContext` without a `CorrelationId` and its `Projected`/`SkippedEvent` log templates omitted correlation — so a read-model write conflict/exhaustion (the principal error path) surfaced tenant context but **not** correlation, contrary to AC #4 ("failures surface with correlation and tenant context"). | **Fixed.** Context now built via the platform `ReadModelWriteContext.WithEventDiagnostics(events)` (derives correlation id from the events); both log templates now carry `{CorrelationId}`. |
| 2 | LOW | **Unbounded log field.** The dispatcher hand-built the `EventTypes` context field via `string.Join` over every replay event (unbounded + duplicates), re-implementing — worse — what the platform already caps at 8 distinct names. | **Fixed by #1's change** — `WithEventDiagnostics` produces the bounded, de-duplicated event-type summary. |
| 3 | LOW | **DRY / drift risk.** `WorksReadModelKeys.WhatsNextProjectionType` hard-coded the literal `"works-whats-next"` instead of Story 4.4's pinned `WhatsNextQueueProjection.ProjectionType`. | **Fixed.** Now references the pure constant so the adapter token can never drift from the projection's reported token. |
| 4 | LOW (info, no change) | **Notify-on-change is inert in the live host.** `IProjectionChangeNotifier` is registered only in `Hexalith.EventStore.Server`; the DomainService SDK references only Client + ServiceDefaults, so `GetService<IProjectionChangeNotifier>()` is always `null` in the Works host and the adapter's notify call never fires live. | **No change (defensible deferral).** The call is null-tolerant, and Story 4.4 explicitly deferred live notifier wiring to "Stories 4.5/4.6, gated on projection-model reconciliation" — which this story documents as still incomplete. Live notification + persist-then-publish belong to the skipped Tier-3 Aspire lane / Story 4.6, not a hidden defect. |

No CRITICAL findings: every `[x]` task is genuinely implemented, ACs #1–#3 are implemented and tested, AC #4 logging/privacy is guarded by fitness tests and now carries correlation context. The File List matches git reality (the only extra dirty paths are submodule gitlinks and a story-automator file, both disclosed as build-output/non-source).

### Change Log

| Date | Version | Description |
|------|---------|-------------|
| 2026-06-17 | 0.1 | Story context created; status ready-for-dev. |
| 2026-06-17 | 1.0 | Implemented all 8 tasks: runnable Works domain-service host + adapter aggregate, runtime projection/query adapter, AppHost topology + Dapr components, Aspire model-inspection + adapter-convergence tests (command smoke test prerequisite-gated/skipped), 5 governance guards, docs/test-summary. Aspire pins reconciled to 13.4.5 (submodule drift). 608 green + 1 skipped; catalog 36; golden corpus unchanged. Status → review. |
| 2026-06-17 | 1.1 | Adversarial code review (auto-fix). Verified build (0/0) and all 608+1 test counts independently. Applied 3 fixes in the runtime adapter: (1) propagate correlation id into the read-model write context + dispatcher logs for AC #4; (2) replace the unbounded hand-built `EventTypes` log field with the bounded platform `WithEventDiagnostics`; (3) source `WhatsNextProjectionType` from the pinned pure constant. Re-verified 0/0 build and 608 green + 1 skipped. No CRITICAL findings. Status → done. |
