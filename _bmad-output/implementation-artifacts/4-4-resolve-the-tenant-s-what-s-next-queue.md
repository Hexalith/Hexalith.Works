---
baseline_commit: e18c974
---

# Story 4.4: Resolve the Tenant's What's Next Queue

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an executor,
I want to ask what work is next for a tenant,
so that assigned and claimable work can be ordered without introducing a routing engine.

## Acceptance Criteria

1. **Given** a tenant has `Queued` and `Assigned` Work Items
   **When** the "what's next" query is executed
   **Then** the query returns only that tenant's eligible queued and assigned items
   **And** query-side authorization/result filtering is applied in addition to tenant scoping.

2. **Given** returned Work Items have Priority and Due Date values
   **When** the query orders results
   **Then** it sorts by Priority, then earliest Due Date, then creation order
   **And** items with neither Priority nor Due Date sort last.

3. **Given** returned Work Items include burn-down, status, and executor data
   **When** read-model contracts are inspected
   **Then** they expose status, own Remaining, rolled Remaining where available, executor binding fields,
   and await-condition data without UI-specific types.

4. **Given** projection updates occur
   **When** Work Item events change queue eligibility or ordering
   **Then** the projection emits change notifications or uses the substrate notifier seam so future SignalR
   surfaces can update live
   **And** no web shell, DataGrid, MCP, chatbot, or email surface is built in v1.

5. **Given** cross-tenant data exists with colliding IDs or similar schedules
   **When** the query is executed for one tenant
   **Then** no item from another tenant is returned
   **And** logs do not expose payloads or personal data.

## Tasks / Subtasks

- [x] **Task 1 — Reconcile the existing read-side surface before writing code (AC: #1–#5)**
  - [x] Read the **roll-up projection precedent** this story mirrors (it is the canonical pure read-side
    pattern): `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` (pure in-memory class:
    `Project(WorkItemRollUpEvent)` + `Get(tenant, id)` + `Snapshot()`; per-item `SortedDictionary<long,
    IEventPayload>` LWW; `EventMatchesDelivery` tenant/id guard; `AddEdge` cross-tenant skip; `Rebuild`/
    `ApplyPayload` deriving Status/own-effort/terminal). `src/Hexalith.Works.Projections/Models/WorkItemRollUpEvent.cs`
    (the `(TenantId, WorkItemId, long Sequence, IEventPayload Payload)` delivery envelope).
  - [x] Read the **read-model contracts** already in `Contracts/Models/`: `WorkItemRollUp.cs`,
    `OwnRemaining.cs` (`(decimal Value, Unit? Unit)`), `RolledRemaining.cs` (same shape; **distinct type** —
    AR-9/B3), `RollUpProjectionDiagnostic.cs`, and especially `WorkItemExecutorBindingView.cs` — whose XML doc
    already states *"The tenant 'what's next' queue projection that would populate this view at scale is owned
    by Story 4.4."* This is your breadcrumb: the binding view is the shape the what's-next read model exposes
    binding data through.
  - [x] Read the **state + value objects** the read model surfaces: `Contracts/State/WorkItemState.cs`
    (no creation timestamp; `Sequence` is per-aggregate monotonic; `Schedule`, `ExecutorBinding`,
    `InitialEffort`/`Remaining`, `AwaitConditions`, `Status`); `ValueObjects/WorkItemSchedule.cs`
    (`(Priority? Priority = null, DateOnly? DueDate = null)` — **both nullable**); `ValueObjects/Priority.cs`
    (`Unknown=0, Critical=1, High=2, Normal=3, Low=4`); `ValueObjects/WorkItemStatus.cs`
    (`Unknown=0 … Queued=3, Assigned=2 …`); `ValueObjects/ExecutorBinding.cs` (`PartyId`+`Channel`+
    `AuthorityLevel`); `ValueObjects/AwaitCondition.cs` (`Kind`+`CorrelationKey`+case fields) /
    `AwaitConditionKind.cs`.
  - [x] Read the **substrate query/projection/notifier seams** this story references but does NOT wire live
    (do not modify the submodule): `Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IDomainQueryHandler.cs`
    (`Domain`/`QueryType`/`ExecuteAsync(QueryEnvelope, ct) → QueryResult`);
    `…/IDomainProjectionHandler.cs` (`Domain`/`Project(ProjectionRequest) → ProjectionResponse`);
    `Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Queries/QueryEnvelope.cs` (carries `TenantId`,
    `Domain`, `AggregateId`, `QueryType`, `Payload`, `CorrelationId`, **`UserId`**, `EntityId`) and
    `QueryResult.cs`; `…/Client/Projections/IReadModelStore.cs` + `ReadModelWritePolicy.cs`;
    `…/Client/Projections/IProjectionChangeNotifier.cs`
    (`NotifyProjectionChangedAsync(projectionType, tenantId, entityId?, ct)`). Confirm the analogous
    query-side authorization gate in a sibling: `Hexalith.Tenants/src/Hexalith.Tenants/Queries/Handlers/
    GetTenantQueryHandler.cs` (`TenantQueryHandlerBase`: not-found→forbidden-if-not-admin gate, then
    `IsAuthorizedForTenantAsync` gate, then serialize) and the tenant result filter
    `Hexalith.Projects/src/Hexalith.Projects.Server/TenantAccess/ProjectQueryTenantFilter.cs`.
  - [x] Confirm the durable wire surface is frozen: `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`
    `Count == 36` (14 events / 14 commands / 8 rejections). **Story 4.4 adds NO new event, command, or
    rejection type (DC3).** Read models (`WhatsNextItem`, like `WorkItemRollUp`) are plain `System.Text.Json`
    records, **not** `[PolymorphicSerialization]` catalog types and not in the golden corpus.
  - [x] Confirm dependency direction with `tests/Hexalith.Works.ArchitectureTests/FitnessTests/
    DependencyDirectionTests.cs`: `Projections` references **only** `Contracts` (+ `EventStore.Contracts`).
    The pure what's-next projection logic lives in `Projections`/`Contracts`; the live `IDomainQueryHandler`/
    `IReadModelStore`/`IProjectionChangeNotifier` wiring is the **deferred runtime adapter** (Stories 4.5/4.6)
    — see Scope Boundary in Dev Notes. Do not add an EventStore.Client/DomainService reference to the kernel.

- [x] **Task 2 — Define the `WhatsNextItem` read-model contract (AC: #3)**
  - [x] Add `src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs` — a sealed `System.Text.Json` record (mirror
    `WorkItemRollUp` style; **not** polymorphic). Expose only data Works owns, **no UI-specific types**
    (no colour/glyph/label/DataGrid types). Required fields (AC #3 + UX-DR2/3/4/5):
    `TenantId`, `WorkItemId`, `WorkItemStatus Status`, `Priority? Priority`, `DateOnly? DueDate`,
    `ExecutorBinding? ExecutorBinding` (PartyId+Channel+AuthorityLevel as data — UX-DR4, **zero kind branching**),
    `OwnRemaining? OwnRemaining` (UX-DR3), `RolledRemaining? RolledRemaining` + `IReadOnlyList<RolledRemaining>
    RolledRemainingByUnit` ("where available" — populated only when the roll-up value is known; never coerce
    heterogeneous units — UX-DR2), `IReadOnlyList<AwaitCondition> AwaitConditions` (kind+key, for a future
    "Waiting on: …" pill — UX-DR5), and a freshness watermark `long LatestAcceptedSourceSequence`
    (mirror `WorkItemRollUp`).
  - [x] Add a `WorkItemExecutorBindingViewTests`-style unit assertion that `OwnRemaining`/`RolledRemaining`
    remain **distinct types** on the model (AR-9 type-separated authority; copy the
    `own.GetType().ShouldNotBe(rolled.GetType())` idiom from `WorkItemRollUpProjectionTests`).
  - [x] Decide whether to **compose** `WorkItemExecutorBindingView` (reuse) vs. carry `ExecutorBinding?`
    directly. Recommended: carry `ExecutorBinding?` directly (the view is a 3-field projection of the same
    data; a `WhatsNextItem.ToBindingView()` helper can bridge if needed) and note it in Dev Notes. Either way,
    **no executor-kind discriminator**.

- [x] **Task 3 — Build the pure `WhatsNextQueueProjection` (AC: #1, #5)**
  - [x] Add `src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs` — a pure in-memory class
    mirroring `WorkItemRollUpProjection`: `void Project(WorkItemRollUpEvent delivery)` (reuse the existing
    delivery envelope — do **not** add a parallel envelope type) and a query method
    `IReadOnlyList<WhatsNextItem> WhatsNext(TenantId tenantId)`. Per-item state keyed by
    `(tenantId, workItemId)`; events accepted into a per-item `SortedDictionary<long, IEventPayload>` so the
    projection is **idempotent + order-tolerant** (replays/duplicates/out-of-order do not corrupt it — B2,
    NFR-4, NFR-9). Derive `Status`/`Schedule`/`ExecutorBinding`/own-`Remaining`/`AwaitConditions` by replaying
    the per-item events in sequence order (mirror `Rebuild`/`ApplyPayload`).
  - [x] **Eligibility (AC #1):** `WhatsNext(tenant)` returns **only** items whose current derived `Status ∈
    { Queued, Assigned }** for that tenant. Exclude `Created`, `InProgress`, `Suspended`, and all terminals.
    Enforce tenant-equality on read (return only items whose `TenantId == tenant`).
  - [x] **Binding for queued items:** queued items keep their **last** `ExecutorBinding` (the last raw act —
    `WorkItemQueued` carries no binding and does not clear it). Surface that last binding on the read model;
    "who currently owns a `Queued` item" is what's-next presentation, not aggregate-state mutation
    (lifecycle-transition-matrix.md ~L194–200, D2/D6).
  - [x] **Cross-tenant isolation (AC #5):** like `WorkItemRollUpProjection.AddEdge` / `CalculateRolled`,
    compare tenant by ordinal string and **never** surface another tenant's item. Items from different tenants
    with **colliding inner ids** must remain distinct (key includes tenant; `WorkItemId.Value` already composes
    the tenant via `AggregateIdentity`, but key on `(tenant, id)` explicitly and assert it).
  - [x] **Rolled-Remaining composition (AC #3, DC7):** do NOT rebuild the tree here. Derive each item's **own**
    Remaining from its own events; populate `WhatsNextItem.RolledRemaining`/`RolledRemainingByUnit` only by
    composing with the existing roll-up read model **where available** (e.g. an optional
    `Func<TenantId, WorkItemId, WorkItemRollUp?>`/roll-up snapshot passed into `WhatsNext(...)`), leaving them
    `null`/empty otherwise. Reinventing `WorkItemRollUpProjection`'s tree walk inside this projection is the
    anti-pattern to avoid.

- [x] **Task 4 — Implement the ordering comparator (AC: #2)**
  - [x] Implement a **total, deterministic, order-tolerant** ordering applied by `WhatsNext(tenant)`. Use this
    exact comparator (records the DC2 design decision — see Dev Notes), in order:
    1. **Priority rank** — present priority ranks `Critical(0) < High(1) < Normal(2) < Low(3)`; **absent or
       `Unknown` priority ranks last (4)**.
    2. **Due Date** — earliest first; **absent Due Date sorts after all present Due Dates** (treat null as a
       max sentinel).
    3. **Creation order** — the deterministic tiebreak (see DC2). Default: `WorkItemId.Value` ordinal
       (`StringComparer.Ordinal`) — a pure, rebuild-stable function of identity (independent of delivery order,
       so it survives out-of-order/duplicate delivery — B2/NFR-4). Do **not** use first-seen arrival order.
  - [x] **"Neither sorts last" (AC #2 / FR-4):** an item with **neither** Priority **nor** Due Date lands at
    the bottom by construction (Priority rank 4 **and** Due-Date max sentinel). Add an explicit test for: a
    fully-unscheduled item ordered after a Low-priority item and after a no-priority-but-due-dated item.
  - [x] Cover the matrix in tests: priority ordering across all four ranks; due-date tiebreak within one
    priority; creation-order tiebreak within identical priority+due-date; only-priority vs only-due-date;
    neither-present sorts last; and **order-independence** (project the same events in two different arrival
    orders → identical ordered output).

- [x] **Task 5 — Query-side authorization / result filtering as a pure seam (AC: #1)**
  - [x] Add a **pure** filter — e.g. `src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs`
    (or a static `Filter`/`FilterList` on a dedicated type) — mirroring
    `Hexalith.Projects` `ProjectQueryTenantFilter` (`Filter` / `FilterList`). It applies an **authoritative
    tenant id** check **in addition to** the projection's tenant scoping (defense-in-depth: D2 says query-side
    authorization is a *distinct control* from key-prefixing), and accepts an optional caller-supplied
    authorization predicate (the seam a future `IDomainQueryHandler` fills from `QueryEnvelope.UserId`).
    Keep it pure (no I/O, no `IExecutorRouter`, no authority enforcement — `AuthorityLevel` stays
    carried-not-enforced, D1/FR-19).
  - [x] Test: an item whose tenant ≠ authoritative tenant is dropped even if it somehow reached the list;
    a custom predicate that rejects an item removes only that item; a null/empty authoritative tenant returns
    an empty result (fail-closed, like `ProjectQueryTenantFilter.FilterList`).

- [x] **Task 6 — Notifier seam + no-surface guardrail (AC: #4)**
  - [x] Do **not** wire a live notifier/SignalR/Dapr surface (deferred — DC1). Instead make AC #4 satisfiable
    by the **substrate seam** `IProjectionChangeNotifier.NotifyProjectionChangedAsync(projectionType,
    tenantId, …)` (EventStore.Client). Express the "what changed" decision purely: expose, from the projection,
    a way to learn that a delivery **changed queue eligibility or ordering** for a tenant (e.g. `Project(...)`
    returns or records whether the tenant's what's-next set/order changed) so the deferred runtime adapter can
    call the notifier only on real change. Define a stable `projectionType` token (kebab-case, e.g.
    `"works-whats-next"`) derived from the canonical key scheme; document it.
  - [x] Document (Dev Notes + `docs/whats-next-projection.md`) that live notification/SignalR broadcast
    (`DaprProjectionChangeNotifier` → `IProjectionChangedBroadcaster` → `SignalRProjectionChangedBroadcaster`,
    group `{projectionType}:{tenantId}`) is the deferred runtime wiring (Stories 4.5/4.6), and that **no web
    shell, DataGrid, MCP, chatbot, or email surface** ships in v1 (UX-DR1 ships the seam, not a surface).

- [x] **Task 7 — Tenant-isolation + privacy negative tests (AC: #5)**
  - [x] Mutation-validated cross-tenant tests (RR-4/D2): two tenants with **colliding inner work-item ids**
    and **similar schedules** → `WhatsNext(tenantA)` returns only A's items, never B's; flipping the queried
    tenant flips the result set; an eligible item in B never leaks into A's ordering.
  - [x] Privacy (AC #5 / NFR-6): the projection and read model perform **no logging**; assert (by design +
    a fitness check if practical) that the kernel does not reference `ILogger`/log event payloads/PII/Obligation
    text into any sink. (The read model carries data for consumers; it must not be *logged* by the kernel.)

- [x] **Task 8 — Guardrail fitness: catalog stays 36, no routing/surface type in the kernel (AC: #1, #4)**
  - [x] Extend `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` (mirror
    `P0_WorkItemSurfaceHasNo…AndCatalogStays36`): scan declared type names under `src/` for a forbidden
    what's-next vocabulary — `RoutingEngine`, `EligibilityScore`/`EligibilityEngine`, `EscalationLadder`,
    `ExecutorRanking`, `*Router` impl, plus surface types `*DataGrid`, `*Hub`, `*SignalR*`, `*WebShell`,
    `*MailSurface`/`*EmailSurface`, `*McpTool`, `*Chatbot*` — and assert none exist in the Works kernel.
    Pair with the reflection-based frozen-catalog assertion `polymorphicCatalogCount.ShouldBe(36, …)` so adding
    a new durable type breaks the build.
  - [x] Confirm `IExecutorRouter` stays an abstraction with **no wired impl** and the what's-next query takes
    **no** routing/eligibility/authority input (FR-20 is projection/query only; FR-22). Keep
    `BoundaryPortTests`/`DependencyDirectionTests`/`EventStoreApiSurfaceCharacterizationTests` green.

- [x] **Task 9 — (Optional) ordering/convergence property test (AC: #2, #5)**
  - [x] Only if it adds falsifiable value beyond Task 4: in `tests/Hexalith.Works.PropertyTests/` (mirror the
    FsCheck wiring in `WorkItemRollUpConvergencePropertyTests.cs` / `WorkItemClaimConvergencePropertyTests.cs`):
    for **any** generated set of items and **any** permutation/duplication of their delivery, `WhatsNext(tenant)`
    yields the **same** ordered list (order-tolerant convergence) and the ordering relation is a **total order**
    (no two distinct items compare equal under the full comparator including the id tiebreak).

- [x] **Task 10 — Documentation and story bookkeeping (AC: #1–#5)**
  - [x] Add `docs/whats-next-projection.md` (mirror `docs/work-roll-up-projection.md`): the eligible set
    ({Queued, Assigned}), the ordering rule + the exact comparator (DC2), tenant scoping + query-side
    authorization seam, the read-model field contract, the notifier seam + deferred runtime wiring, and the
    NFR-4 rebuild posture (checkpoint-per-aggregate per `docs/eventstore-api-surface-constraints.md`, **not**
    shadow+swap).
  - [x] Add a Story 4.4 note to `docs/boundary-decision-record.md` (extend the Theme-4/EventStore lines): the
    what's-next queue is realized as a **pure read projection + query-shaping** over Works' own events; ordering
    is Priority→DueDate→creation (neither last); tenant scoping + query-side authorization are distinct controls;
    Works adds **no** routing/eligibility type and **no** durable catalog type (stays 36); live query/notifier
    runtime is the deferred Aspire wiring (4.5/4.6).
  - [x] Add a Story 4.4 section to `_bmad-output/implementation-artifacts/tests/test-summary.md` (verification
    commands, before/after counts, files changed, gaps closed, not-applicable runtime/UI surfaces) — mirror the
    Story 4.3 entry.

- [x] **Task 11 — Verify the slice (AC: #1–#5)**
  - [x] Baseline is the Story 4.3 final of **562** green tests: UnitTests **449**, IntegrationTests **80**,
    ArchitectureTests **31**, PropertyTests **2**; catalog **36**.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
    require **0 warnings / 0 errors** (warnings-as-errors).
  - [x] Run the direct xUnit v3 binaries after the Release build (`dotnet test` is blocked by
    Microsoft.Testing.Platform named-pipe permissions in this sandbox):
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.
  - [x] Confirm `WorkItemV1Catalog.Count` is still **36** and the golden corpus is byte-unchanged. Do not run
    recursive submodule commands; leave the unrelated `Hexalith.Tenants` gitlink change in the working tree
    untouched.

## Dev Notes

### Scope Boundary (read first — prevents over-build and a purity violation)

Story 4.4 is the **pure read-side** realization of **FR-20** ("what's next" ordering): a tenant-scoped
projection + read model + query-shaping that returns a tenant's `Queued`+`Assigned` items ordered by
Priority → earliest Due Date → creation order (neither sorts last), with a query-side authorization seam and a
notifier seam. The claimable pool **is** a read projection, **not** an authoritative queue aggregate
(AR-10/B1; boundary-decision-record.md). [Source: _bmad-output/planning-artifacts/epics.md#Story 4.4;
#FR-20; architecture.md#API & Communication Patterns (B1); #Data Architecture (A2 Priority); AR-10]

**Follow the Epic-3 pattern exactly: build the PURE projection logic now, defer the runtime adapter.** The
roll-up projection (Stories 3.3/3.4) shipped as a pure in-memory `WorkItemRollUpProjection` + `WorkItemRollUp`
read model in `Projections`/`Contracts` (referencing only `Contracts`), with the live EventStore/Dapr runtime
adapter deferred. Do the same here. The substrate query/projection/notifier runtime —
`IDomainQueryHandler` (`/query`), `IDomainProjectionHandler` (`/project`), `IReadModelStore` persistence,
`IProjectionChangeNotifier`/SignalR broadcast — lives in **EventStore.Client/DomainService** and therefore
**cannot be referenced from the pure kernel** (`Projections` references only `Contracts` —
`DependencyDirectionTests`). Wiring it is Stories 4.5/4.6, and is gated on the **EventStore projection-model
reconciliation** (stateless full-replay `/project` vs checkpoint-per-aggregate rebuild) that the Epic-3 retro
records as an open Action item. [Source: _bmad-output/implementation-artifacts/epic-3-retro-2026-06-17.md
(§4.5, §6, Action 4, §7 "4.4 consumes the roll-up read models…"); docs/eventstore-api-surface-constraints.md;
2-1…3-6 story records (pure-core / deferred-runtime split)]

**In scope (pure, Tier-1):** the `WhatsNextItem` read-model contract; the pure `WhatsNextQueueProjection`
(`Project(delivery)` + `WhatsNext(tenant)`); the ordering comparator; the query-side authorization filter (a
pure seam); a change-detection signal for the notifier seam (no live wiring); cross-tenant isolation + privacy
negative tests; the catalog-stays-36/no-routing/no-surface fitness guard; an optional convergence property test;
docs + boundary-record + test-summary updates.

**Out of scope (deferred — do NOT implement here):** the live `IDomainQueryHandler`/`/query` endpoint,
`IReadModelStore` persistence, Dapr pub/sub delivery, the live `IProjectionChangeNotifier`/SignalR broadcast,
and the EventStore projection-model reconciliation runtime adapter (**Stories 4.5/4.6**); any routing engine,
eligibility scoring, escalation ladder, executor ranking, `IExecutorRouter` impl, or AI decision record
(**Theme 4**); `AuthorityLevel` enforcement (**Theme 6**); pagination cursor (`IQueryCursorCodec` — not required
by the ACs; mention as a future seam, don't build); any web shell, DataGrid, MCP, chatbot, or email surface
(**Theme 3**). **Do not add a new event/command/rejection type** — the v1 catalog stays **36** and the golden
corpus stays byte-compatible (DC3). [Source: epics.md#Story 4.4 (AC #4 surfaces; AC #1 "no routing engine");
#UX Design Requirements (UX-DR1 deferred surface); architecture.md#Deferred Decisions; #Counter-metrics
SM-C1/SM-C2; docs/boundary-decision-record.md (Theme-4 routing/eligibility; EventStore owns rebuild
checkpointing)]

### Design Decisions and Guardrails

- **DC1 — The notifier requirement (AC #4) is satisfied by *referencing* the substrate seam, not by wiring a
  live surface.** The seam exists today: `IProjectionChangeNotifier.NotifyProjectionChangedAsync(projectionType,
  tenantId, entityId?, ct)` (EventStore.Client) → `DaprProjectionChangeNotifier` →
  `IProjectionChangedBroadcaster` → `SignalRProjectionChangedBroadcaster` (group `{projectionType}:{tenantId}`,
  fail-open). The Works kernel cannot reference Client (dependency direction), so v1 ships the **pure
  change-detection decision** (did this delivery change a tenant's what's-next set or order?) plus a documented
  `projectionType` token (e.g. `"works-whats-next"`); the deferred runtime adapter (4.5/4.6) calls the notifier
  on real change. AC #4's "**emits change notifications OR uses the substrate notifier seam**" is met by the
  latter. [Source: Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/IProjectionChangeNotifier.cs;
  …/Server/Projections/DaprProjectionChangeNotifier.cs; …/SignalRHub/SignalRProjectionChangedBroadcaster.cs;
  architecture.md#Frontend Architecture ("keeps projections SignalR-ready … without shipping a surface"); UX-DR1]
- **DC2 — The ordering comparator is an explicit total order (resolves an ambiguity in the AC).** The AC gives
  "Priority → earliest Due Date → creation order; neither sorts last," but Works has no creation timestamp in
  the kernel (`WorkItemState` carries only a per-aggregate `Sequence`; envelope timestamps are EventStore-owned
  and out of the pure kernel). Use the comparator in Task 4: priority rank with **absent/`Unknown`→last**,
  due-date ascending with **absent→last**, then a **deterministic id tiebreak** (`WorkItemId.Value` ordinal).
  The id tiebreak is chosen over first-seen arrival order because it is a **pure function of identity** —
  stable across rebuilds and immune to out-of-order/duplicate delivery (B2/NFR-4). Note: `WorkItemId.Value` is
  an `AggregateIdentity`-composed string (`{tenant}:work:{id}`); production inner ids are ULID-shaped so they
  sort ~by creation time, but the tiebreak's contract is **determinism**, not literal wall-clock order. Pin the
  comparator with tests; if the reviewer prefers a carried creation ordinal instead, it must still be
  rebuild-deterministic and order-tolerant. [Source: epics.md#Story 4.4 (AC #2); #FR-4; architecture.md#Data
  Architecture (A2: Priority ordered enum; "none sorts last"); #B2 (no pub/sub ordering); NFR-4;
  Hexalith.Projects ProjectListProjection ordering precedent (Sequence→IdempotencyKey→Fingerprint)]
- **DC3 — No new durable contract type; catalog stays 36.** `WhatsNextItem` is a plain read-model record (like
  `WorkItemRollUp`/`WorkItemExecutorBindingView`), **not** `[PolymorphicSerialization]`, not stream-appended,
  not in the golden corpus. Adding an event/command/rejection would grow the frozen catalog (SM-C1). The Task 8
  fitness test makes a regression a build break. [Source: architecture.md#Format Patterns (serialization);
  #Data Model & Schema; tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs (Count 36); SM-C1/SM-C2]
- **DC4 — Query-side authorization is a *distinct control* from tenant key-scoping (defense-in-depth).** D2/NFR-1
  require query-side authorization/result filtering **in addition to** tenant scoping. Model it as a pure filter
  (mirror `ProjectQueryTenantFilter.Filter`/`FilterList`) applied on top of the projection's own tenant scoping,
  with an optional caller predicate the future `IDomainQueryHandler` fills from `QueryEnvelope.UserId`. v1 does
  **not** enforce `AuthorityLevel` and adds **no** `IExecutorRouter` impl (FR-19/FR-22; D1). [Source:
  architecture.md#Authentication & Security (D2); #Process Patterns (tenant scoping); epics.md#NFR-1;
  Hexalith.Tenants GetTenantQueryHandler (auth gate pattern); Hexalith.Projects ProjectQueryTenantFilter]
- **DC5 — The projection is idempotent + order-tolerant (NFR-4/NFR-9/B2).** Reuse the roll-up pattern: per-item
  `SortedDictionary<long, IEventPayload>` keyed by `Sequence`; re-deriving status/schedule/binding/remaining
  from the accepted set on each change makes replay, duplicate delivery, and out-of-order delivery converge to
  the same read model and the same ordering. Rebuild is per-tenant and event-derived (holds no authoritative
  state) — aligning to EventStore's checkpoint-per-aggregate rebuild, not the superseded shadow+swap wording.
  [Source: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs; architecture.md#B2; NFR-4;
  AR-17; docs/eventstore-api-surface-constraints.md (#Online Rebuild)]
- **DC6 — Reuse the existing delivery envelope; do not add a parallel one.** `WhatsNextQueueProjection.Project`
  consumes the same `WorkItemRollUpEvent(TenantId, WorkItemId, long Sequence, IEventPayload Payload)` the
  roll-up projection uses (it is a generic per-aggregate event delivery, not roll-up-specific). If a more
  neutral name is warranted later, that is a separate refactor — do not fork the envelope here. [Source:
  src/Hexalith.Works.Projections/Models/WorkItemRollUpEvent.cs]
- **DC7 — Rolled-Remaining is COMPOSED from the existing roll-up, never re-derived inside the what's-next
  projection.** The what's-next projection is a single-item, eligibility+ordering projection — it derives each
  item's **own** Status/Schedule/binding/own-Remaining/await-conditions from that item's own events. It must
  **not** rebuild the parent/child tree to compute rolled-Remaining (that is `WorkItemRollUpProjection`'s job —
  reinventing it is the anti-pattern the checklist warns about). `WhatsNextItem.RolledRemaining`/
  `RolledRemainingByUnit` are **nullable "where available"** (AC #3 says *where available*): populate them by
  composing with the existing roll-up read model when one is supplied (e.g. an optional roll-up lookup passed to
  the query method), and leave them `null`/empty otherwise. v1 may legitimately return them null for items whose
  roll-up is not co-available — the contract field existing satisfies AC #3; do not grow a second tree walker.
  [Source: epics.md#Story 4.4 (AC #3 "where available"); architecture.md#B3/AR-9 (rolled is a distinct eventual
  projection); src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs; UX-DR3]

### Current State (files this story reads, adds, or documents — read before editing)

All paths under the `Hexalith.Works` root unless noted; line numbers are approximate anchors.

- **Read-model contracts (`src/Hexalith.Works.Contracts/Models/`)** —
  `WorkItemRollUp.cs` (rich read model: `TenantId, WorkItemId, Status, Parent, OwnRemaining?, RolledRemaining?,
  RolledRemainingByUnit, ChildWorkItemIds, ChildContributionCount, LatestAcceptedSourceSequence` + `Degraded` +
  `ProjectionDiagnostics`); `OwnRemaining.cs` = `(decimal Value, Unit? Unit)`; `RolledRemaining.cs` = same shape,
  **distinct type** (AR-9); `WorkItemExecutorBindingView.cs` = `(TenantId, WorkItemId, ExecutorBinding?)` with
  the doc breadcrumb naming Story 4.4 as its scaled populator. **NEW: `WhatsNextItem.cs` (Task 2).**
- **Projections (`src/Hexalith.Works.Projections/`)** — `Strategies/WorkItemRollUpProjection.cs` (the pure
  pattern to mirror: `Project`/`Get`/`Snapshot`, per-item `SortedDictionary` LWW, tenant-ordinal guards);
  `Models/WorkItemRollUpEvent.cs` (delivery envelope to reuse — DC6); `WorksProjectionsAssembly.cs` (marker).
  **NEW: `Strategies/WhatsNextQueueProjection.cs` (Task 3) + `Strategies/WhatsNextQueryAuthorization.cs`
  (Task 5).**
- **State (`src/Hexalith.Works.Contracts/State/WorkItemState.cs`)** — `Status` (9-state), `Sequence`
  (per-aggregate monotonic; **no creation timestamp**), `Schedule` (`WorkItemSchedule?`), `ExecutorBinding?`
  (last raw act; `Apply(WorkItemQueued)` does **not** clear it), `InitialEffort`/`Remaining` (own), derived
  `Remaining => InitialEffort?.Remaining`, `AwaitConditions`, `LastConsumedAwaitCondition`.
- **Value objects (`src/Hexalith.Works.Contracts/ValueObjects/`)** — `WorkItemSchedule.cs` =
  `(Priority? Priority = null, DateOnly? DueDate = null)`; `Priority.cs` = `{Unknown=0, Critical=1, High=2,
  Normal=3, Low=4}`; `WorkItemStatus.cs` = `{Unknown=0, Created=1, Assigned=2, Queued=3, InProgress=4,
  Suspended=5, Completed=6, Cancelled=7, Rejected=8, Expired=9}`; `ExecutorBinding.cs` = `(PartyId, Channel,
  AuthorityLevel)` (rejects `Channel.Unknown`/`AuthorityLevel.Unknown`); `Channel.cs` = `{Unknown=0, Mcp=1,
  Cli=2, Chatbot=3, Email=4}`; `AuthorityLevel.cs` = `{Unknown=0, Read=1, Contribute=2, Coordinate=3,
  Administer=4}`; `AwaitCondition.cs` (`Kind`, `CorrelationKey`, `ChildWorkItemId?`/`Instant?`/
  `ExternalCorrelationId?`); `AwaitConditionKind.cs` = `{ChildCompleted=1, DateReached=2, ExternalSignal=3}`;
  `WorkItemId.cs` (`Value` is `AggregateIdentity("tenant","work",value).AggregateId` — a composed string).
- **Substrate seams (read-only; do NOT edit the submodule, do NOT reference from the kernel)** —
  `Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IDomainQueryHandler.cs` (`Domain`, `QueryType`,
  `ExecuteAsync(QueryEnvelope, ct) → QueryResult`) + `IDomainProjectionHandler.cs`;
  `…/Contracts/Queries/QueryEnvelope.cs` (`TenantId, Domain, AggregateId, QueryType, Payload, CorrelationId,
  UserId, EntityId`) + `QueryResult.cs`; `…/Client/Projections/IReadModelStore.cs` + `ReadModelWritePolicy.cs`;
  `…/Client/Projections/IProjectionChangeNotifier.cs`. Sibling query-side auth/filter precedent:
  `Hexalith.Tenants/src/Hexalith.Tenants/Queries/Handlers/GetTenantQueryHandler.cs`;
  `Hexalith.Projects/src/Hexalith.Projects.Server/TenantAccess/ProjectQueryTenantFilter.cs`;
  `Hexalith.Projects/src/Hexalith.Projects/Projections/ProjectList/ProjectListProjection.cs` (deterministic
  ordering precedent).
- **Docs** — `docs/work-roll-up-projection.md` (mirror for the new `docs/whats-next-projection.md`);
  `docs/boundary-decision-record.md` (Theme-4 routing/eligibility ~L74–79; EventStore rebuild ownership ~L36;
  Story 4.3 note ~L111–121); `docs/eventstore-api-surface-constraints.md` (#Online Rebuild —
  checkpoint-per-aggregate, not shadow+swap); `docs/lifecycle-transition-matrix.md` (~L194–200: "who currently
  owns a `Queued` item" is the 4.4 what's-next projection's concern, not aggregate state).
- **Tests (baseline 562 green; catalog 36)** —
  `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` (the projection-test pattern: static
  `TenantId`/`WorkItemId`/`Unit` fixtures, a `Project(projection, evt)` helper, `Get`/`Snapshot` assertions,
  the `own.GetType().ShouldNotBe(rolled.GetType())` distinct-type idiom) — **mirror for new
  `WhatsNextQueueProjectionTests.cs`**; `WorkItemExecutorBindingViewTests.cs` (read-model contract test
  pattern); `tests/Hexalith.Works.Testing/WorkItemStateBuilder.cs` (`InStatus(...)` arranger — the projection
  consumes event deliveries, but the builder is useful for cross-checks); `WorkItemRollUpConvergencePropertyTests.cs`
  / `WorkItemClaimConvergencePropertyTests.cs` (FsCheck wiring for the optional Task 9 property);
  `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` (`Count == 36`);
  `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` (mirror the
  `…AndCatalogStays36` fitness method), `DependencyDirectionTests.cs`, `BoundaryDecisionRecordTests.cs`,
  `BoundaryPortTests.cs`, `EventStoreApiSurfaceCharacterizationTests.cs`.

### Technical Requirements

- Keep `Projections` pure: no clock, RNG, I/O, Dapr, EventStore runtime/Client, HTTP, files, timers, generated
  IDs, or `ILogger`. The projection mutates only its in-memory maps; the query method returns immutable read
  models. `Projections` references **only** `Contracts` (+ `EventStore.Contracts`). [Source:
  architecture.md#Process Patterns (domain purity); #Structure Patterns (dependency direction); NFR-5]
- Keep kernel projects (`Contracts`, `Server`, `Projections`) free of LLM/routing/email/MCP/UI/SignalR/cost
  packages and sibling implementation DTOs; reference Parties only by `PartyId`. The query/notifier runtime
  stays in the substrate — do **not** add `IDomainQueryHandler`/`IReadModelStore`/`IProjectionChangeNotifier`
  references to the kernel. [Source: architecture.md#Structure Patterns; docs/boundary-decision-record.md]
- Read models expose **data only, no UI types**: status, own-Remaining vs rolled-Remaining as **distinct
  fields/types** (AR-9), per-Unit subtotals never coerced (UX-DR2), executor binding fields with **zero
  kind-branching** (UX-DR4), 9 status values + await-condition kind+key (UX-DR5). [Source: epics.md#UX-DR2…5;
  architecture.md#Format Patterns (own vs rolled distinct types)]
- Serialization: `WhatsNextItem` is a plain `System.Text.Json` `sealed record` (file-scoped namespace, one
  public type per file) — **not** polymorphic, **no `V2`**, catalog stays **36**, golden corpus byte-compatible.
  [Source: architecture.md#Format Patterns; DC3]
- Tests: xUnit v3 + Shouldly (no raw `Assert.*`, no FluentAssertions; NSubstitute only where a genuine double
  is needed — none expected here). Tier-1 pure (no Dapr/Aspire/network/containers/sleeps/threads). Ordering and
  isolation proofs are **deterministic**; reuse `WorkItemRollUpProjectionTests` fixtures and patterns.
  Mutation-validated cross-tenant negative tests are mandatory (RR-4/D2). [Source: architecture.md#Tests;
  #Enforcement Guidelines; epics.md#NFR-1]
- Do not add or upgrade packages. Local pins remain authoritative (.NET SDK `10.0.301`, Dapr `1.18.2`, Aspire
  `13.4.3`, xUnit v3 `3.2.2`, Shouldly `4.3.0`). Hexalith deps stay root-submodule `ProjectReference`s — never
  add `Hexalith.*` `PackageReference`s, never edit submodule files, never init nested submodules. [Source:
  CLAUDE.md#Hexalith library references; CLAUDE.md#Submodule rules; architecture.md#Starter Template Evaluation]

### Previous Work Intelligence

- **Stories 3.3/3.4 (Roll-Up)** established the canonical pure read-side pattern this story mirrors:
  `WorkItemRollUpProjection` (pure `Project`/`Get`/`Snapshot`, per-(childId,sequence) LWW, tenant-equality per
  hop, refuse-don't-coerce on unit mismatch) + the `WorkItemRollUp`/`OwnRemaining`/`RolledRemaining` read
  models. Reuse the structure, fixtures, and test idioms; do **not** reinvent a projection harness. Carried
  debt **D4** (roll-up read model recomputes via full traversals; memoization deferred) means a similar
  recompute-on-query is acceptable for v1 — favor clarity, no premature memoization. [Source:
  _bmad-output/implementation-artifacts/3-3-…md; 3-4-…md; epic-3-retro-2026-06-17.md (§3, D4)]
- **Story 4.1** added `WorkItemExecutorBindingView` (Contracts/Models) — the binding read-model whose doc
  explicitly defers the scaled what's-next populator to **this** story — and hardened `ExecutorBinding` (rejects
  `Channel.Unknown`/`AuthorityLevel.Unknown`; every test binding must use a **valid** `AuthorityLevel`). Reuse
  those binding fixtures. [Source: _bmad-output/implementation-artifacts/4-1-…md; WorkItemExecutorBindingView.cs]
- **Story 4.2/4.3** were no-production-code proof/guardrail stories that added the `…CatalogStays36` fitness
  pattern to mirror (Task 8) and recorded that the **claimable pool is a read projection (Story 4.4)**, claim is
  unconditional, and `AuthorityLevel` is carried-not-enforced. The matrix note (~L194–200) records that the
  "who owns a queued item" presentation is the 4.4 what's-next projection's job. [Source:
  _bmad-output/implementation-artifacts/4-2-…md; 4-3-…md; docs/lifecycle-transition-matrix.md;
  docs/boundary-decision-record.md (Story 4.3 note)]
- **Epic 3 retrospective lessons** (carry forward): (1) **test-count bookkeeping drift** is the dominant
  recurring review finding — reconcile the Dev Agent Record against `tests/test-summary.md` **before** review.
  (2) **The first dev pass under-covers — an explicit QA gap-filling pass is expected**, not a remediation
  (3.5 needed +22). Budget for it: ordering edge cases and cross-tenant negatives are exactly where gaps hide.
  (3) `dotnet test` is unusable in the sandbox — restore, Release build, then run the xUnit v3 binaries under
  `bin/Release/net10.0/`. (4) The **EventStore projection-model reconciliation** (stateless `/project` vs
  checkpoint rebuild) is an open blocker for the *runtime* adapter (4.5/4.6) — out of scope here, but is **why**
  this story stops at pure logic. (5) Reviewers find ≥1 real gap per story — budget for rework. [Source:
  _bmad-output/implementation-artifacts/epic-3-retro-2026-06-17.md (§4, §6, §8, §9); Hexalith.EventStore/CLAUDE.md
  #Code Review Process]

### Git Intelligence

Recent commits before Story 4.4 (most recent first):

- `e18c974 feat(story-4.3): Claim Queued Work with Single-Claim-Wins` — deterministic single-claim-wins proof +
  guardrail; no production change; **catalog stays 36**; +13 tests (**562** green). **This story's baseline.**
- `2dd46d0 feat(story-4.2): Assign, Reassign, and Hand Off Work` — uniform handoff proof/guardrail.
- `0f413f7 feat(story-4.1): Bind Work to a Uniform Party Executor` — uniform `ExecutorBinding` +
  `WorkItemExecutorBindingView`.
- `68de3f5 feat: Update documentation and project structure for Epic 3 completion`.
- `216e9e7 feat(story-3.6): Cascade terminal work through active descendants`.

Unlike 4.2/4.3, Story 4.4 **adds production code** — but only on the read side (`Contracts/Models`,
`Projections/Strategies`) plus tests, fitness, and docs. It needs no aggregate (`Server`), no `Reactor`, no
`AppHost`, and no new contract/event/command/rejection type. A pre-existing working-tree `Hexalith.Tenants`
gitlink change (retro debt D6) is unrelated — leave it untouched; run no recursive submodule commands.

### Project Structure Notes

- New production types: `src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs`;
  `src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs`;
  `src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs` (or equivalent pure filter). One
  public type per file; file-scoped namespaces; sealed records/classes. These align with the architecture's
  named locations: read-model contracts in `Contracts/Models` (`WhatsNextItem` is explicitly named in the
  architecture's planned structure), and the what's-next handler in `Projections`. [Source: architecture.md
  #Complete Project Directory Structure (`Contracts/Models` → `WhatsNextItem, RollUpView`; `Projections/Handlers`
  → `WhatsNextHandler`); #Requirements to Structure Mapping (4.6 → `Projections/Handlers (WhatsNext)`)]
- New tests in existing projects: `tests/Hexalith.Works.UnitTests/WhatsNextQueueProjectionTests.cs` (+ a small
  `WhatsNextItem` contract test); the catalog-stays-36/no-routing/no-surface guard extends
  `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`; the optional convergence
  property in `tests/Hexalith.Works.PropertyTests/`.
- Docs: new `docs/whats-next-projection.md`; updates to `docs/boundary-decision-record.md` and
  `_bmad-output/implementation-artifacts/tests/test-summary.md`. [Source: CLAUDE.md#Repository responsibility;
  architecture.md#File Organization Patterns]

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.4: Resolve the Tenant's What's Next Queue] — the story statement and the five acceptance criteria.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-20] — read-side query returning a tenant's `Queued`+`Assigned` items ordered Priority→Due Date→creation order (neither last); query-side authorization in addition to tenant scoping; projection/query only, no routing engine.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-4 / #AR-5] — Schedule = Priority + optional Due Date; Priority is an ordered enum `{Critical, High, Normal, Low}`; an item with neither sorts last.
- [Source: _bmad-output/planning-artifacts/epics.md#NFR-1 / #NFR-4 / #NFR-6 / #NFR-7] — tenant isolation + query-side authorization; projections rebuildable/idempotent/order-tolerant; structured logging, never log payloads/PII; incremental updates (no whole-stream re-read per query).
- [Source: _bmad-output/planning-artifacts/epics.md#UX-DR1…UX-DR5] — SignalR-ready notification seam (no surface); per-Unit subtotals never coerced; own vs rolled Remaining distinct fields; executor kind/channel/authority as data (no branching); 9 statuses + await-condition kind+key.
- [Source: _bmad-output/planning-artifacts/architecture.md#Data Architecture] — A2 Priority ordered enum backing "what's next"; A5 roll-up; B3/AR-9 own-Remaining (synchronous) vs rolled-Remaining (eventual, distinct type).
- [Source: _bmad-output/planning-artifacts/architecture.md#API & Communication Patterns (B1) / #AR-10] — the claimable pool is a read projection, not an authoritative queue aggregate.
- [Source: _bmad-output/planning-artifacts/architecture.md#Authentication & Security (D2) / #Process Patterns] — query-side authorization is a distinct control from tenant key-prefixing; tenant-scope every query; mutation-validated negative tests.
- [Source: _bmad-output/planning-artifacts/architecture.md#Frontend Architecture / #Infrastructure & Deployment (E1)] — projections kept SignalR-ready without a surface; online per-tenant rebuild.
- [Source: _bmad-output/planning-artifacts/architecture.md#Complete Project Directory Structure / #Requirements to Structure Mapping] — `Contracts/Models` (`WhatsNextItem`) and `Projections/Handlers` (WhatsNext) are the named homes.
- [Source: docs/eventstore-api-surface-constraints.md#Online Rebuild] — EventStore online rebuild is checkpoint-per-aggregate via `IProjectionRebuildOrchestrator`, **not** shadow+swap; align rebuild posture to it.
- [Source: docs/boundary-decision-record.md] — Theme-4 owns routing/eligibility; EventStore owns the rebuild/concurrency mechanism; the claimable pool is the Story 4.4 read projection.
- [Source: docs/lifecycle-transition-matrix.md (~L194–200)] — "who currently owns a `Queued` item" is the 4.4 what's-next projection's presentation concern, not aggregate-state mutation.
- [Source: src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs; Models/WorkItemRollUpEvent.cs] — the pure projection pattern and the delivery envelope to reuse (DC6).
- [Source: src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs; OwnRemaining.cs; RolledRemaining.cs; WorkItemExecutorBindingView.cs] — read-model field/shape precedents (incl. the 4.4 breadcrumb on the binding view).
- [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemSchedule.cs; Priority.cs; WorkItemStatus.cs; ExecutorBinding.cs; Channel.cs; AuthorityLevel.cs; AwaitCondition.cs; WorkItemId.cs] — exact enum members, schedule nullability, and the composed-id shape behind DC2's tiebreak.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IDomainQueryHandler.cs; IDomainProjectionHandler.cs; …/Contracts/Queries/QueryEnvelope.cs; QueryResult.cs; …/Client/Projections/IReadModelStore.cs; ReadModelWritePolicy.cs; IProjectionChangeNotifier.cs] — the deferred substrate query/projection/notifier runtime (referenced, not wired in v1).
- [Source: Hexalith.Tenants/src/Hexalith.Tenants/Queries/Handlers/GetTenantQueryHandler.cs; Hexalith.Projects/src/Hexalith.Projects.Server/TenantAccess/ProjectQueryTenantFilter.cs; Hexalith.Projects/src/Hexalith.Projects/Projections/ProjectList/ProjectListProjection.cs] — sibling precedents for query-side authorization, tenant result filtering, and deterministic list ordering.
- [Source: _bmad-output/implementation-artifacts/epic-3-retro-2026-06-17.md] — pure-core/deferred-runtime split, the EventStore projection-model reconciliation blocker (Action 4), test-count-drift and QA-gap-pass process lessons, and the explicit "4.4 consumes the roll-up read models" dependency note.
- [Source: _bmad-output/implementation-artifacts/tests/test-summary.md] — authoritative baseline counts (562 green) and catalog size (36).
- [Source: Hexalith.Projects/_bmad-output/project-context.md; Hexalith.EventStore/CLAUDE.md#Domain-Module Authoring] — purity, persist-then-publish, rejection-as-event, additive serialization, `IDomainQueryHandler`/`IReadModelStore`/`IQueryCursorCodec` SDK contracts, xUnit v3 + Shouldly.
- [Source: CLAUDE.md] — root-submodule and `ProjectReference`-not-`PackageReference` rules; Works holds domain code only (read-model/projection here; runtime wiring in the substrate/AppHost).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8[1m] (Claude Opus 4.8, 1M context)

### Debug Log References

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — **0 warnings / 0 errors**.
- Ran the four xUnit v3 binaries directly under `bin/Release/net10.0/` (`dotnet test` is blocked by the
  sandbox's named-pipe permissions): UnitTests **483/483**, IntegrationTests **80/80**, ArchitectureTests
  **33/33**, PropertyTests **3/3** (`Ok, passed 100 tests.` ×3) — **599** green after the QA gap-fill pass
  (590 green at dev-story handoff).
- One pre-emptive fix during dev: bound locals (`is { } reported` / `is { } estimated`) in the projection's
  own-effort cases so nullable-flow stays clean under `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`;
  used explicit discards (`_ = …` / a void `Deliver` helper) for ignored `Project(...)` returns to satisfy
  IDE0058 (`discard_variable:warning`).

### Completion Notes List

- **Read-side only, Epic-3 pattern.** Added the pure `WhatsNextQueueProjection` + `WhatsNextItem` read model
  (mirroring `WorkItemRollUpProjection`/`WorkItemRollUp`), the `WhatsNextOrdering` comparator, the pure
  `WhatsNextQueryAuthorization` filter, and the `WhatsNextProjectionChange` notifier signal. The live
  `IDomainQueryHandler`/`IReadModelStore`/`IProjectionChangeNotifier` runtime stays deferred (Stories
  4.5/4.6) — `Projections` references only `Contracts` (+ EventStore.Contracts).
- **Reconciliation finding (Task 1):** `WorkItemId.Value` is the **raw inner id**, not tenant-composed, so
  colliding inner ids across tenants are byte-identical — the explicit `(tenant, id)` key is what isolates
  them; the id tiebreak is a strict total order *within* a tenant (ids unique per tenant).
- **AC #1** eligibility = `{Queued, Assigned}` + a distinct query-side authorization control (DC4).
  **AC #2** Priority→DueDate→identity total order, neither sorts last (DC2). **AC #3** read model exposes
  status, own vs rolled remaining as distinct types, executor binding (no kind branching), await data — no
  UI types; rolled composed only where a roll-up is supplied (DC7). **AC #4** notifier seam via a pure
  change-signal + stable `"works-whats-next"` token; no live surface (DC1). **AC #5** mutation-validated
  cross-tenant isolation + a no-logging privacy fitness check.
- **DC3 guard:** `WhatsNextItem` is a plain STJ record, not a polymorphic catalog type; the v1 catalog stays
  **36** (fitness-asserted) and the golden corpus is byte-unchanged. No new event/command/rejection type.
- **QA gap-fill pass:** `bmad-qa-generate-e2e-tests` added nine non-redundant unit checks for authorization
  composition/order/trim/null behavior, suspend/resume eligibility, re-estimation, terminal non-reentry,
  due-date change signaling, freshness watermarking, and per-item rolled composition. Full suite is **599/599**.
- Left the unrelated `Hexalith.Tenants` gitlink change untouched; ran no recursive submodule commands.

### File List

**New production code**

- `src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs`
- `src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs`
- `src/Hexalith.Works.Projections/Strategies/WhatsNextOrdering.cs`
- `src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs`
- `src/Hexalith.Works.Projections/Models/WhatsNextProjectionChange.cs`

**New tests**

- `tests/Hexalith.Works.UnitTests/WhatsNextQueueProjectionTests.cs`
- `tests/Hexalith.Works.PropertyTests/WhatsNextOrderingConvergencePropertyTests.cs`

**Modified tests**

- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` (added Task 8 routing/
  surface + catalog-stays-36 guard and Task 7 no-logging privacy guard)

**Documentation / bookkeeping**

- `docs/whats-next-projection.md` (new)
- `docs/boundary-decision-record.md` (Story 4.4 note)
- `_bmad-output/implementation-artifacts/tests/test-summary.md` (Story 4.4 section)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (4-4 → in-progress → review)
- `_bmad-output/implementation-artifacts/4-4-resolve-the-tenant-s-what-s-next-queue.md` (this story file)

### Change Log

| Date | Version | Description |
|------|---------|-------------|
| 2026-06-17 | 0.1 | Dev-story pass: pure what's-next projection + read model + ordering comparator + query-side authorization + notifier change-signal; fitness + property + docs. +28 tests (590 green); catalog stays 36. Status → review. |
| 2026-06-17 | 0.2 | QA gap-fill pass added +9 unit tests; full suite **599/599** green; catalog stays 36. |
| 2026-06-17 | 0.3 | Adversarial code review (story-automator). All 5 ACs verified implemented; all tasks verified done; build 0/0; **599/599** independently re-run; catalog 36; File List complete. One LOW efficiency fix auto-applied (skip the order-signature rebuild on the duplicate-delivery short-circuit in `Project`, behavior-preserving). No CRITICAL/HIGH/MEDIUM findings. Status → done. |

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot · **Date:** 2026-06-17 · **Outcome:** ✅ Approve (auto-fix mode) · **Status →** done

### Verification performed (claims checked against reality, not taken on trust)

- **Build:** `dotnet build Hexalith.Works.slnx -c Release` → **0 warnings / 0 errors** (warnings-as-errors). Confirmed.
- **Tests (independently re-run from `bin/Release/net10.0/` binaries):** UnitTests **483/483**, IntegrationTests **80/80**,
  ArchitectureTests **33/33**, PropertyTests **3/3** (`Ok, passed 100 tests.` ×3) = **599** green. Matches the claimed counts exactly.
- **Frozen wire surface:** the reflection-based catalog assertion (`polymorphicCatalogCount.ShouldBe(36)`) passes — `WhatsNextItem`
  is a plain STJ read model, not a `[PolymorphicSerialization]` catalog type (DC3). Catalog stays **36**.
- **File List vs git:** every new/modified production, test, and doc file is present and documented. The only undocumented working-tree
  changes are the pre-existing `Hexalith.Tenants` gitlink (retro debt D6, explicitly out of scope) and the story-automator orchestration
  log (excluded from review). No false "changed" claims, no undocumented source changes.
- **AC audit:** AC #1 eligibility `{Queued, Assigned}` + distinct query-side authorization control ✓; AC #2 Priority→DueDate→identity
  total order, neither-last ✓; AC #3 read model exposes status / own vs rolled remaining as distinct types / executor binding (no kind
  branching) / await data, no UI types ✓; AC #4 change-signal + `"works-whats-next"` token, no live surface ✓; AC #5 mutation-validated
  cross-tenant isolation + no-logging fitness check ✓. All five implemented, not just claimed.
- **Task audit:** all 11 tasks spot-checked against the code and tests — each `[x]` is genuinely done.

### Adversarial findings chased to ground

- **`ChildSpawned` not handled by the projection (vs. the roll-up projection it mirrors).** Investigated as a potential missing-spawn-facts
  gap. **Not a defect:** the `SpawnChild`→`ChildSpawned` contract has the command pipeline build an equivalent child `CreateWorkItem`, so a
  spawned child carries its own Schedule/effort/binding via its own `WorkItemCreated`; `ChildSpawned` only establishes the parent/child tree
  edges, which DC7 deliberately keeps out of this projection. Ignoring it yields the identical eligibility result. Correct call.
- **Change-signal scope (Assigned↔Queued / binding-only updates do not notify).** Confirmed this is intentional and matches AC #4's literal
  scope ("change *eligibility or ordering*"), and is pinned by tests. Not a defect.
- **Authoritative-tenant comparison uses the normalized `TenantId.Value`.** Verified consistent with `TenantId` normalization and documented
  on the filter. Correct.

### Issues found and dispositions

| Sev | Finding | Disposition |
|-----|---------|-------------|
| LOW | `Project` computed the `before` order signature (a full O(n log n) eligible-set rebuild) *before* the duplicate-sequence `Accept` short-circuit, so every at-least-once duplicate delivery paid for a signature it discarded. | **Auto-fixed** — moved the `before` capture after the `Accept` guard. Behavior-preserving (projected state isn't re-derived until `Rebuild`); covered by `Project_reports_no_change_for_a_duplicate_sequence_delivery`. Re-verified 599/599 green. |

No CRITICAL, HIGH, or MEDIUM issues. The implementation faithfully mirrors the `WorkItemRollUpProjection` precedent, keeps `Projections`
pure (only `Contracts` + `EventStore.Contracts`), and stays read-side only — no aggregate, runtime adapter, or new durable type, as scoped.
