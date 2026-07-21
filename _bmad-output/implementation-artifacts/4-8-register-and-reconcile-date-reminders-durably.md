---
baseline_commit: 9526c31
---

# Story 4.8: Register and Reconcile Date Reminders Durably

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want date reminders registered when an item suspends and reconciled from a working pending-await source,
so that date-based resumes execute in steady state and survive recovery without hand configuration.

_Added by the 2026-07-21 correct-course (audit findings F-RT-3 critical, F-RT-5 major): no suspend-time reminder registration exists, the reconciliation stream scan uses a gateway route that unconditionally rejects tenant-wide reads, and reconciliation is off unless tenants are hand-configured — so no date-based resume executes in the live topology._

## Acceptance Criteria

1. **Given** an item suspends with a `DateReached` await-condition in the running topology
   **When** the event path observes the suspension
   **Then** a self-targeted durable Dapr reminder is registered with the deterministic name
   **And** duplicate registration remains idempotent
   **And** the item resumes when the date fires without requiring a host restart.

2. **Given** the reconciliation-on-recovery pass runs
   **When** pending `DateReached` awaits are scanned
   **Then** the scan reads a tenant-scoped pending-date-await index read model maintained by the projection dispatcher plus per-aggregate stream reads
   **And** it never issues the tenant-wide null-aggregate stream read the gateway rejects.

3. **Given** the host restarts after reminder firings were lost before recording
   **When** recovery completes
   **Then** overdue awaits are reissued as idempotent resume commands and future awaits are re-registered
   **And** reconciliation operates without per-tenant hand configuration (live SM-1 lane).

4. **Given** the kernel is inspected
   **When** fitness tests run
   **Then** `Handle` and the reactor remain clock-free.

## Tasks / Subtasks

- [ ] **Task 1 - Reconcile existing foundations before writing code (AC: #1-#4)**
  - [ ] Check `_bmad-output/implementation-artifacts/sprint-status.yaml` for the status of Story `4-7-trigger-reactor-translators-from-the-live-event-stream`. **If 4.7 has landed**, read its story file and prefer its event-consumption surface (an `IEventStoreDomainEventHandler<WorkItemSuspended>` on the domain-events subscription) as the steady-state registration trigger. **If 4.7 has NOT landed** (it is `backlog` at story-creation time), use the `/project` dispatch hook (Design Decision DD-1 below) and do NOT build 4.7's pub/sub subscription, cascade wiring, or checkpoint-replay startup pass in this story.
  - [ ] Read every file in `src/Hexalith.Works/Reminders/` (Story 4.6 built the full component set: `DateReminderName`, `DateReminderActor`/`IDateReminderActor`, `DaprDateReminderScheduler`/`IDateReminderScheduler`, `DateReminderRegistration`, `DateReminderReconciler`, `ReminderReconciliationService`, `IPendingDateAwaitSource`, `PendingDateAwait`, `PendingDateAwaitProjection`, `StreamReadingPendingDateAwaitSource`, `DateResume`). This story rewires their triggers and discovery source; it does not reinvent them.
  - [ ] Read `src/Hexalith.Works/Runtime/` (`IWorkCommandSubmitter` + `WorkCommandSubmission`, `EventStoreGatewayWorkCommandSubmitter`, `WorksEventDecoder`, `WorksRecoveryExtensions`, `WorksRecoveryOptions`, `WorksRecoveryLog`) and `src/Hexalith.Works/Program.cs` (bespoke `MapPost("/project", ...)` before `UseEventStoreDomainService()`; `MapActorsHandlers()`).
  - [ ] Read `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` and `WorksWhatsNextReadModel.cs`: the dispatcher is the already-live per-aggregate event path (the EventStore runtime posts each `work` aggregate's replayed stream to `/project`), and `WorksReadModelKeys` + `ReadModelWritePolicy.UpdateAsync<T>` is the established idempotent read-model upsert pattern.
  - [ ] Verify in `references/Hexalith.EventStore` (read-only; never init/update submodules) when the runtime dispatches `/project` relative to event persistence, so the steady-state registration latency is understood and documented. Also confirm `StreamsController.ValidateRequest` still 400-rejects a null `AggregateId` on `POST /api/v1/streams/read` — every stream read this story issues MUST carry an `AggregateId`.
  - [ ] Read `docs/boundary-decision-record.md` (Story 4.6's M1 note: "reconciliation-on-recovery only, not registered at suspend time") and `docs/eventstore-api-surface-constraints.md`. **This story supersedes the M1 decision** — the 2026-07-21 correct-course reaffirmed architecture decision C2 ("registers a reminder at suspend") as authoritative over the boundary record's reconciliation-only posture.
  - [ ] Note: prose docs (`boundary-decision-record.md`, `eventstore-api-surface-constraints.md`, `tests/test-summary.md`) still say the catalog "stays **36**"; the code and all four fitness guards assert **37** since the correct-course added `WorkItemInitialEffortRejected`. Trust the code; fix the prose you touch (Task 7).

- [ ] **Task 2 - Register the reminder when the event path observes the suspension (AC: #1)**
  - [ ] Hook the trigger surface chosen in Task 1. Default (4.7 not landed): inside `WorkItemProjectionDispatcher.DispatchAsync`, after decoding the aggregate's events, fold them with the existing pure `PendingDateAwaitProjection.PendingDateAwaits(ordered)` — if the item's current state holds pending `DateReached` awaits, ensure a reminder is registered for each via `IDateReminderScheduler.ScheduleResumeReminderAsync(pendingAwait, dueTime, ct)` with `dueTime = max(TimeSpan.Zero, pendingAwait.Instant - timeProvider.GetUtcNow())`. Inject `IDateReminderScheduler` and `TimeProvider` into the dispatcher's construction in `Program.cs` (clock reads stay at the host edge; the kernel receives none of this).
  - [ ] Idempotency under replay/redelivery: every re-dispatch of the same stream re-registers the same deterministic name (`DateReminderName.For(tenantId, workItemId, correlationKey)`); `DateReminderActor.ScheduleResumeAsync` already persists registration state then `RegisterReminderAsync` with the same name, overwriting in place. Prove with a test that dispatching the same suspension twice produces exactly one distinct reminder name and cannot produce a second accepted `WorkItemResumed` (the aggregate no-ops a consumed/non-matching await; `DateResume.BuildSubmission` derives the same `CorrelationId`/`CausationId`, deduped at the substrate by `MessageId`).
  - [ ] Registration failure must not wedge the projection dispatch: wrap the scheduler call, log via a `WorksRecoveryLog`-style bounded-metadata warning (reason code, tenant, work-item, reminder name only), and let the read-model writes proceed. At-least-once redelivery or the recovery pass retries registration.
  - [ ] Do not register non-date awaits. An item that resumes early on another condition may leave a stale reminder; on fire, the resulting resume no-ops at the aggregate and the actor cleans its state (`ReceiveReminderAsync` orphan path) — this is the accepted 4.6 posture; do not build proactive unregistration unless a test proves it is needed.
  - [ ] Steady-state proof shape: an item suspended on a near-future `DateReached` resumes when the Dapr Scheduler fires the reminder — no host restart, no reconciliation pass involved (live lane, Task 5).

- [ ] **Task 3 - Maintain the pending-date-await index read model in the `/project` dispatcher (AC: #2)**
  - [ ] Add a host-edge index read model (plain `System.Text.Json` records in `src/Hexalith.Works/` — NOT `[PolymorphicSerialization]`, NOT in Contracts; the durable catalog stays **37**): a per-tenant index document holding `workItemId → pending DateReached entries` (reuse the `PendingDateAwait` record for entries), plus a single well-known tenant-registry document listing tenant ids that have (or have had) pending date awaits — this registry is what removes per-tenant hand configuration from recovery.
  - [ ] Key scheme in `WorksReadModelKeys` (follow the existing pattern, store `"statestore"`): e.g. `PendingDateAwaitIndexKey(tenantId)` → `projection:works:pending-date-await:{tenantId}` and a constant registry key → `projection:works:pending-date-await:tenants`. Keys embed the tenant because `WorkItemId.Value` is a raw inner id (colliding across tenants).
  - [ ] Maintain both documents in `WorkItemProjectionDispatcher.DispatchAsync` alongside the two existing writes, using `ReadModelWritePolicy.UpdateAsync<T>` (ETag-based update-with-retry, safe under concurrent per-aggregate dispatches): fold the replayed stream with `PendingDateAwaitProjection`; pending set non-empty → upsert this aggregate's entries and ensure the tenant is in the registry; pending set empty → remove this aggregate's entry (resume/terminal events clear it). Removal of empty tenants from the registry is optional — a stale registry tenant costs one cheap read on recovery; document whichever you choose.
  - [ ] The index is per-aggregate-maintained (the `/project` contract delivers one aggregate's stream per call — same limitation as the what's-next index) and idempotent under redelivery/out-of-order dispatches (the fold is over the full replayed stream, so last dispatch wins with a complete picture).

- [ ] **Task 4 - Replace the rejected tenant-wide scan with an index-driven source; drop the hand-configured tenant gate (AC: #2, #3)**
  - [ ] Replace `StreamReadingPendingDateAwaitSource` with an index-driven `IPendingDateAwaitSource`: read the tenant registry → each tenant's index document → for each indexed entry, issue a **per-aggregate** `POST /api/v1/streams/read` (`AggregateId` always set) via `IEventStoreGatewayClient` and re-fold with `PendingDateAwaitProjection` to establish current truth before acting (the index is discovery, the stream is truth — a stale index entry whose stream shows the await cleared is skipped, and may be cleaned up). Never construct a `StreamReadRequest` with `AggregateId: null` anywhere.
  - [ ] Keep `DateReminderReconciler` behavior as-is (it is already proven idempotent): due awaits (`Instant <= now`) → `DateResume.BuildSubmission` reissue through `IWorkCommandSubmitter`; future awaits → `IDateReminderScheduler` re-registration. Only its `IPendingDateAwaitSource` input changes.
  - [ ] `WorksRecoveryOptions`: remove the `Tenants` list gate — `ReminderReconciliationService.ExecuteAsync` currently returns early when `Tenants.Count == 0`, which is exactly the hand-configuration AC #3 forbids. Reconciliation runs whenever `RunReconciliationOnStartup` (default `true`). Keep a bounded paging guard for the per-aggregate reads (rename/repurpose `MaxStreamPagesPerTenant` if useful, e.g. per-aggregate page cap; page by `FromSequence = LastSequenceReturned + 1`, `ContinuationToken` must stay null — the gateway fail-closes on non-null tokens).
  - [ ] Update `src/Hexalith.Works.AppHost/Program.cs`: delete the `Works:Recovery:Tenants` → `Works__Recovery__Tenants__{index}` forwarding block (no longer meaningful). Update `WorksAppHostTopologyTests` pins accordingly (the `EventStore__CommandGateway__BaseAddress` pin stays).
  - [ ] The whole recovery pass stays crash-safe by idempotency, not checkpoints: a restart mid-reconciliation repeats the scan; deterministic correlation ids dedup at the substrate and duplicate resumes no-op at the aggregate — preserve and re-assert this property.

- [ ] **Task 5 - Prove the live SM-1 lane under Aspire (AC: #1, #3)**
  - [ ] Rework `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs` into the full SM-1 proof, keeping the existing prerequisite gating (`Assert.Skip` unless Redis :6379, Dapr placement :50005, scheduler :50006 are reachable) and the dev-JWT auth path (`EnableKeycloak=false`, key `DevOnlySigningKey-AtLeast32Chars!`, issuer `hexalith-dev`, audience `hexalith-eventstore`):
    - **Steady-state phase (new, AC #1):** Create → Assign → Claim → Suspend on a near-future `DateReached` (a few seconds ahead); WITHOUT restarting, poll the per-aggregate stream (`POST /api/v1/streams/read`) with a bounded timeout until exactly one accepted `WorkItemResumed` appears — proving suspend-time registration + Dapr Scheduler fire end-to-end.
    - **Recovery phase (reworked, AC #3):** park a second item on a past `DateReached`, restart the AppHost against the same Redis **without any `--Works:Recovery:Tenants` argument**, and assert recovery auto-discovers it from the durable index: the overdue await is reissued as an idempotent resume (exactly one accepted `WorkItemResumed`, second pass adds none) and a future-dated third item gets its reminder re-registered (observable as its own later resume, or assert via the scheduler's registration side effects if a cheaper observable exists).
  - [ ] Use per-run unique ids for tenant and work items (the deferred-work ledger records that fixed ids against the persistent `dapr init` Redis now collide with the duplicate-create rejection introduced 2026-07-21).
  - [ ] **Known broad-gate blocker (do not fake around it):** at EventStore submodule `fbc78e58` both Tier-3 smoke lanes fail on a 60-second `HttpClient` timeout at the gateway submit even with both EventStore hosts built (they carry `SuppressBuild=true` — `dotnet build` them explicitly before running the lane). If the timeout persists after this story's changes, diagnose what you can, record the exact command/result honestly in the Dev Agent Record and `test-summary.md`, and keep the deterministic proofs green — the validation ladder records this as a substrate blocker, not a story failure to hide.

- [ ] **Task 6 - Deterministic tests for the new wiring (AC: #1-#3)**
  - [ ] Dispatcher/index tests (extend the existing dispatcher test lane): suspension with `DateReached` upserts the tenant index + registry; resume/terminal dispatch removes the entry; cross-tenant entries never merge (colliding inner ids in two tenants stay separate); double dispatch of the same stream is idempotent (same index state, one distinct reminder registration via a recording `IDateReminderScheduler` fake); a non-date suspension registers nothing; scheduler failure does not fail the dispatch.
  - [ ] Index-driven source tests (pattern: `DateReminderRecoveryRuntimeTests` fakes — `FixedTimeProvider`, `RecordingWorkCommandSubmitter`, recording scheduler, plus an in-memory read-model store and a fake `IEventStoreGatewayClient`): discovery from registry → index → per-aggregate re-fold; a stale index entry whose stream shows the await cleared is not reissued; **assert no `StreamReadRequest` is ever constructed with a null `AggregateId`** (recording gateway fake).
  - [ ] Reconciler end-to-end deterministic pass over the new source: due → reissued once across two passes (one distinct correlation id), future → rescheduled deterministically — mirror the existing idempotency proofs so they keep holding with the new source.
  - [ ] Governance: keep catalog guards at **37** (new index/read-model records are plain STJ, not `Polymorphic`); extend `RuntimeAdapterGovernanceTests.P0_ReminderActorAndCascadeCheckpointRuntimeAreConfinedToHostEdge`'s token list with the new type names if they should be host-edge-confined; the log-privacy guard (`P0_RuntimeAdapterLogsOnlyBoundedMetadataNeverPayloads`) must keep passing — use `LoggerMessage` templates with bounded metadata only.
  - [ ] AC #4 needs no new code — confirm `ScaffoldGovernanceTests.P0_WorkItemKernelRemainsPure` (Contracts/Server/Projections/Reactor banned-symbol scan incl. clocks, `TimeProvider`, RNG, `Environment.`) still passes untouched; all new clock reads live in `src/Hexalith.Works` only.

- [ ] **Task 7 - Documentation and governance bookkeeping (AC: #1-#4)**
  - [ ] `docs/boundary-decision-record.md`: supersede the Story 4.6 M1 note — record that Story 4.8 implements suspend-time registration per architecture decision C2 (reaffirmed by the 2026-07-21 correct-course over the reconciliation-only posture), and that recovery discovery now runs from the durable pending-date-await index without per-tenant configuration.
  - [ ] `docs/eventstore-api-surface-constraints.md`: update the Story 4.6 stream-read section — the null-`AggregateId` gateway limitation still exists but is no longer load-bearing for reminders (index + per-aggregate reads); describe the new index read model and its per-aggregate-maintenance semantics.
  - [ ] Fix the stale "catalog stays 36" prose to **37** in the doc sections you touch.
  - [ ] Append a Story 4.8 section to `_bmad-output/implementation-artifacts/tests/test-summary.md` with exact counts, skipped/blocked Tier-3 conditions, and verification commands. Keep this story's Dev Agent Record and File List accurate — the recurring review finding in this repo is test-count drift and false completion claims (Story 4.6 review found a CRITICAL one); never claim a live lane ran if it skipped or timed out.

- [ ] **Task 8 - Verify the slice (AC: #1-#4)**
  - [ ] Baseline at story creation (commit `9526c31`, post-correct-course 2026-07-21/22): build 0 warnings / 0 errors; UnitTests **496**, PropertyTests **3**, ArchitectureTests **44**, IntegrationTests **96/98** (the 2 Tier-3 Aspire smoke lanes red on the gateway-submit timeout when prerequisites are present; they `Assert.Skip` when Docker/Dapr placement/scheduler are absent). Catalog **37**.
  - [ ] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`, then `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — require 0 warnings / 0 errors. Do NOT use `dotnet test` (broken in this sandbox); run the four xUnit v3 binaries directly: `tests/<Proj>/bin/Release/net10.0/<Proj>` for UnitTests, IntegrationTests, ArchitectureTests, PropertyTests.
  - [ ] Confirm `WorkItemV1Catalog.Count` stays **37** and the golden corpus is byte-unchanged (this story adds no durable catalog type).
  - [ ] Never run recursive submodule commands or initialize nested submodules; leave the long-standing sibling submodule pointer drift (`Hexalith.FrontComposer`, `Hexalith.Parties`, `Hexalith.Tenants`) untouched and out of the File List.

## Dev Notes

### Scope Boundary

Story 4.8 closes the second half of the runtime-wiring gap found by the 2026-07-21 audit: date-based resumes must execute in the **live topology** — in steady state (suspend → reminder → fire → resume, no restart) and on recovery (restart → auto-discover pending awaits → reissue/re-register, no hand configuration). All components exist from Story 4.6; this story changes **when registration happens** (suspend-time, not restart-only), **where recovery discovers work** (durable index + per-aggregate reads, not the 400-rejected tenant-wide scan), and **how it is enabled** (on by default, not gated on `Works:Recovery:Tenants`).

**In scope:** suspend-time reminder registration on the live event path; a pending-date-await index read model + tenant registry maintained by the `/project` dispatcher; an index-driven `IPendingDateAwaitSource`; removal of the hand-configured tenant gate; AppHost/topology-test updates; the live SM-1 Tier-3 lane; deterministic tests; docs/governance bookkeeping.

**Out of scope:** Story 4.7's work — the pub/sub domain-events subscription wiring, cascade dispatcher live triggering, `ChildCompletionResumeTranslator` consumption, and checkpoint-replay startup recovery (if 4.7 has landed first, reuse its surface; never rebuild it here). Also out: new kernel behavior, new durable catalog types (stays 37), changes to `WorkItemAggregate.Handle`/`WorkItemLifecycle`, the persisted parent roll-up convergence limitation (deferred-work F-PROJ-1), mutation-testing gate (F-PROJ-2), production UI/MCP/chatbot/email/routing/cost surfaces, `IExecutorRouter` implementation, Keycloak realm work.

### Sequencing Dependency on Story 4.7

The correct-course sequenced 4.7 before 4.8 because an event-subscription surface is "4.8's natural trigger too" — but **4.7 is `backlog` at story-creation time** and this story is implementable standalone: the `/project` dispatch is an **already-live per-aggregate event path** (the EventStore runtime posts each `work` aggregate's replayed stream to the Works host's bespoke `/project` endpoint after appends — this is how the what's-next and roll-up read models get persisted in the live topology today). Registering reminders and maintaining the index from that dispatch satisfies AC #1/#2 without any pub/sub work. The dev agent MUST re-check 4.7's status at implementation start (Task 1) and prefer its consumption surface if it landed. Relevant if 4.7's surface is used: the EventStore submodule ships an **unused** consumer SDK — `AddEventStoreDomainEvents(...)` (Client/Registration), `MapEventStoreDomainEvents()` (DomainService), `IEventStoreDomainEventHandler<TEvent>`, `EventStoreDomainEventProcessor` (MessageId dedup, unknown-type skip, poison-safe 200s) — but topic naming is per-tenant (`{tenantId}.work.events`; `"{domain}.events"` only for tenant `system`), the shared pubsub component's `scopes` currently **exclude `works`**, and the only wildcard-topic precedent in the stack is `[Topic(..., "*.*.projection-changed")]` in `ProjectionNotificationController`. Those are 4.7 problems; do not solve them here.

### Current State and Files to Read Before Editing (verify-don't-reimplement)

**Reminder components (`src/Hexalith.Works/Reminders/`) — all exist, all proven deterministically:**

- `DateReminderName`: name = `"work-date-resume-" + SHA256-hex[..32]` of `(tenantId, workItemId, correlationKey)` joined with ``; `ActorId(tenantId, workItemId)` co-locates one item's reminders on one actor. No clock/RNG/payload in the name.
- `DateReminderActor : Actor, IDateReminderActor, IRemindable`: `ScheduleResumeAsync(DateReminderRegistration)` persists actor state under the reminder name, then `RegisterReminderAsync(name, null, dueTime, period: -1ms)` (one-shot; same-name re-registration overwrites in place — the idempotency this story leans on). On fire: reads state (orphan → unregister + return), builds `DateResume.BuildSubmission(tenant, workItem, instant)`, submits via `IWorkCommandSubmitter`, then removes state + unregisters. Redelivery before commit re-issues the same idempotent resume.
- `DaprDateReminderScheduler : IDateReminderScheduler` (`ScheduleResumeReminderAsync(PendingDateAwait, TimeSpan dueTime, ct)`): actor-proxy call; token observed at the boundary only (remoting carries none). This is the seam Task 2's trigger calls — production Dapr, tests use a recording fake.
- `DateResume.BuildSubmission`: pure factory — `ResumeWorkItem(tenant, workItem, AwaitCondition.DateReached(instant))`, `CorrelationId == CausationId == "date-resume-" + DateReminderName.For(...)` (deterministic → substrate `MessageId` dedup + aggregate no-op on duplicates).
- `DateReminderReconciler.ReconcileAsync`: groups pending awaits by tenant; due (`Instant <= TimeProvider.GetUtcNow()`) → reissue; future → re-register with `dueTime = Instant - now`. Idempotent across passes (proven by `DateReminderRecoveryRuntimeTests`). **Unchanged by this story except its input source.**
- `ReminderReconciliationService : BackgroundService`: currently gates on `RunReconciliationOnStartup && Tenants.Count > 0` — **the `Tenants.Count` clause is what AC #3 removes.** Exceptions are logged (`RecoveryStepFailed`), never crash the host — preserve this.
- `PendingDateAwaitProjection.PendingDateAwaits(events-in-sequence-order)`: pure fold mirroring `WorkItemState.Apply` — `WorkItemSuspended` sets the current `DateReached` set; `WorkItemResumed`/`WorkItemCancelled`/`WorkItemExpired`/`WorkItemCompleted`/`WorkItemRejected` clear it. **Reuse this fold for both the dispatcher index maintenance and the recovery re-fold — do not write a second fold.**
- `StreamReadingPendingDateAwaitSource`: the component this story **replaces** — iterates `WorksRecoveryOptions.Tenants` issuing `StreamReadRequest(Tenant, Domain: "work", AggregateId: null, FromSequence, ContinuationToken, PageSize: 200)` — the exact request shape `StreamsController.ValidateRequest` 400-rejects (`"Aggregate identifier is required for the current stream read route"`, reason `MissingRequiredField`; the controller also fail-closes on any non-null `ContinuationToken` — page only by `FromSequence = LastSequenceReturned + 1`).

**Runtime plumbing (`src/Hexalith.Works/Runtime/`):** `WorkCommandSubmission` (`WorkDomain = "work"`) → `EventStoreGatewayWorkCommandSubmitter` maps to `SubmitCommandRequest(MessageId: CausationId, ...)` on the gateway (`POST /api/v1/commands`, 202 + status polling; auth is platform-wired). `WorksRecoveryExtensions.AddWorksReminderAndCascadeRecovery(config)` holds every DI registration this story touches (options binding `"Works:Recovery"`, `TimeProvider.System`, gateway client with `EventStore:CommandGateway:BaseAddress`, submitter, source, scheduler, reconciler, hosted service, cascade pieces); `AddWorksDateReminderActors()` registers the actor. `WorksRecoveryLog`: `LoggerMessage` templates, EventIds 4600-4603 (reminders) — extend with new bounded-metadata templates in the same style.

**The live event path (`src/Hexalith.Works/Projections/`):** `Program.cs` maps the bespoke `POST /project` before `UseEventStoreDomainService()`; the handler constructs `WorkItemProjectionDispatcher(store, notifier?, logger)` per request. `DispatchAsync(ProjectionRequest)` decodes each `ProjectionEventDto` by simple type name (web JSON, no `$type`; unknown/malformed → skip + log 4501), replays through fresh `WhatsNextQueueProjection` + `WorkItemRollUpProjection` instances, then persists: `WorksWhatsNextTenantIndex` via `ReadModelWritePolicy.UpdateAsync<T>(store, "statestore", WorksReadModelKeys.WhatsNextIndexKey(tenant), ...)` (upsert-or-remove this aggregate's entry — the per-tenant-singleton pattern Task 3 mirrors) and `WorkItemRollUp` via `store.SaveAsync(...RollUpKey(tenant, id)...)`. Keys: `projection:works:whats-next:{tenantId}`, `projection:works:rollup:{tenantId}:{workItemId}`.

**Contracts shapes consumed (read-only this story):** `AwaitCondition.DateReached(instant)` normalizes to UTC; `CorrelationKey = utcInstant.ToString("O", InvariantCulture)`. `WorkItemSuspended` carries `AggregateId`, `Sequence`, `TenantId`, `WorkItemId`, `AwaitConditions` (normalized set). `ResumeWorkItem(TenantId, WorkItemId, AwaitCondition?)`; the aggregate accepts only a matching current await and no-ops a repeated consumed key. Lifecycle: `InProgress --Suspend--> Suspended`, `Suspended --Resume--> InProgress` (`docs/lifecycle-transition-matrix.md`).

**AppHost (`src/Hexalith.Works.AppHost/Program.cs`):** composes `eventstore`, `eventstore-admin`, `works` (+ optional Keycloak security); routes `work` commands to the Works `/process` via `EventStore__DomainServices__Registrations__wildcard_work_v1__*` env; injects `EventStore__CommandGateway__BaseAddress`; shared Redis `statestore.yaml` with `actorStateStore: "true"` scoped to `[eventstore, works, eventstore-admin]`. The `Works:Recovery:Tenants` forwarding block is removed by Task 4. Cross-repo `IProjectMetadata` classes resolve EventStore hosts under `references/Hexalith.EventStore/...` (fixed 2026-07-21 — do not regress) with `SuppressBuild=true` (build those hosts explicitly before live lanes).

### Key Design Decisions

- **DD-1 — Trigger surface:** default is the `/project` dispatch hook (already-live, per-aggregate, at-least-once, replay-idempotent); switch to an `IEventStoreDomainEventHandler<WorkItemSuspended>` only if Story 4.7's subscription surface has landed. Either way, registration is derived from the **folded current pending set**, never from a raw event in isolation — replays of historical `WorkItemSuspended` events for an item that has since resumed must register nothing.
- **DD-2 — Index shape:** per-tenant index document + one global tenant-registry document, plain STJ records in the host, `ReadModelWritePolicy.UpdateAsync<T>` ETag upserts. The registry is the whole answer to "without per-tenant hand configuration": recovery enumerates tenants from durable data instead of `WorksRecoveryOptions.Tenants` (Dapr state stores expose no key enumeration and the gateway exposes no tenant-wide read — both documented substrate limitations).
- **DD-3 — Index is discovery, stream is truth:** recovery re-folds each candidate's per-aggregate stream before acting, so a stale index entry can never cause a wrong reissue; the deterministic correlation id + aggregate idempotency make even a wrong-side race harmless (no-op).
- **DD-4 — Reconciliation on by default:** the only remaining gate is `RunReconciliationOnStartup` (default `true`); the pass stays fire-and-log (never crashes the host) and fully idempotent (restart mid-pass = repeat safely, no checkpoint needed).
- **DD-5 — No new durable types, no kernel change:** catalog stays 37; all new records are host-edge STJ; every clock read goes through the injected `TimeProvider` in `src/Hexalith.Works`; `Handle`, `Apply`, the reactor, and all kernel projects remain untouched (AC #4 is proven by existing fitness gates staying green).

### Architecture Compliance

- **Kernel purity (AC #4):** `Contracts`, `Server`, `Projections`, `Hexalith.Works.Reactor` remain free of Dapr, clocks, `TimeProvider`, RNG, `Environment.`, logging, network/filesystem I/O, `IEventStoreGatewayClient`, `IReadModelStore` (enforced by `ScaffoldGovernanceTests.P0_WorkItemKernelRemainsPure` + `RuntimeAdapterGovernanceTests`). All Story 4.8 code lives in `src/Hexalith.Works/` (+ AppHost/tests/docs) — the only locations the reminder-ownership guard (`P0_WorkItemSliceAllowsRollUpOnlyInProjectionAndOwnsReminderRecoveryOnlyAtAdapterEdge`) and the host-edge confinement guard allow `Reminder`/recovery tokens.
- **No shadow kernel:** the dispatcher/scheduler/reconciler decide only delivery timing and retries; whether a resume is domain-valid is decided exclusively by `WorkItemAggregate.Handle` via the command path. Deadlines stay advisory-until-fired (C3): "due now" is decided only at the host edge by the reconciler/scheduler, never in a query or the kernel.
- **Idempotency everywhere:** reminder fire and reconciliation are at-least-once; deterministic names + deterministic correlation ids + aggregate no-ops make duplicates safe. Never introduce a non-deterministic id (`Guid.NewGuid` is banned in the kernel and pointless here — derive ids from `DateReminderName`).
- **Tenant isolation:** index keys embed the tenant; entries never merge across tenants; logs carry tenant/work-item/reminder-name/correlation metadata only — never payloads, obligation text, tokens, or full command bodies (log-privacy guard enforces).
- **ProjectReference rule:** any new Hexalith dependency must be a `ProjectReference` through root variables — never a `Hexalith.*` `PackageReference` (`DependencyDirectionTests` scans every csproj and `Directory.Packages.props`). This story should need no new references at all.
- **Submodule rule:** root-level only, never `--recursive`, never init nested submodules; read `references/Hexalith.EventStore` sources freely.

### Previous Story Intelligence

- **Story 4.6** built every reminder/recovery component this story rewires, and its review history is the cautionary tale: the first pass falsely claimed the Tier-3 recovery lane existed (CRITICAL C1), and the M1 review finding explicitly flagged that "a freshly date-suspended item gets no reminder until a host restart" — the decision then was to stay reconciliation-only; **this story reverses that decision by design** (C2 reaffirmed). The review also verified the load-bearing substrate claim this story builds on: `StreamsController` 400-rejects null `AggregateId` (verified against submodule source).
- **Story 4.5** established the runtime host pattern: canonical EventStore domain-service host + bespoke `/project` + `UseEventStoreDomainService()`; the host uses the **platform** ServiceDefaults (do not fork `Hexalith.Works.ServiceDefaults`); `HexalithWorksContractsSerialization.RegisterPolymorphicMappers()` at startup.
- **2026-07-21 correct-course** (not a story, but the latest merged work): duplicate/late `CreateWorkItem` now rejects for any established status — which is why warm-Redis smoke reruns with fixed ids now fail (unique ids per run, Task 5); projections fail closed on unmatched deliveries; fitness gates extended (Reactor in the purity scan; RNG/Environment/Guid-parse banned; `EventShapeGovernanceTests` added); catalog 36 → **37**.
- **Recurring review findings in this repo:** test-count bookkeeping drift between the Dev Agent Record and `test-summary.md`, and completion claims for lanes that skipped — reconcile actual binary output before moving to review.

### Git Intelligence

Recent commits: `9526c31` (correct-course: read-side defenses, creation-boundary rejections, fitness hardening, pin bumps — the baseline), `6eb0044`/`d451797` (submodule pointer bumps), `993cd88`/`a31007e` (submodule relocation under `references/` + `Hexalith.AI.Tools`). Pattern: submodule pins drift and get reconciled in dedicated commits; story work must align to the **checked-out** submodule state, not story-spec version numbers.

### Latest Technical Information

- Pins at baseline (verified in `Directory.Packages.props` / `global.json`, 2026-07-22): Dapr `1.18.4`, Aspire `13.4.6` (+ `Aspire.AppHost.Sdk` 13.4.6), SDK `10.0.301`, xUnit v3 `3.2.2`, `Microsoft.Extensions.*` 10.0.10, `CommunityToolkit.Aspire.Hosting.Dapr` 13.4.1-beta.686. Do not casually upgrade; older numbers in prior stories are stale.
- Dapr actor reminders (docs.dapr.io, checked 2026-07-22): persisted via the **Scheduler service** across deactivation/failover; registering an existing name **fails with already-exists unless `overwrite: true`** at the raw API level — the .NET SDK `RegisterReminderAsync` path used by `DateReminderActor` overwrites in place (Story 4.6 documented and unit-tested this at the wrapper level; keep those tests green). Default failure policy retries 3× at 1s intervals. Delivery is effectively at-least-once — which is why every downstream step is idempotent.
- `Assert.Skip` (xUnit v3) is the gating mechanism for Tier-3 lanes; `DistributedApplicationTestingBuilder.CreateAsync<Projects.Hexalith_Works_AppHost>(args)` model-inspection runs without Docker, live `StartAsync` needs Docker + `dapr init` (placement :50005, scheduler :50006).

### Project Structure Notes

- **Modify:** `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` (+ its construction in `Program.cs`), `src/Hexalith.Works/Projections/WorksWhatsNextReadModel.cs` (keys — or a new sibling keys/model file), `src/Hexalith.Works/Reminders/` (new index-driven source; retire `StreamReadingPendingDateAwaitSource`), `src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs`, `WorksRecoveryOptions.cs`, `WorksRecoveryLog.cs`, `src/Hexalith.Works.AppHost/Program.cs`.
- **New (host-edge only):** pending-date-await index read model records + key builders; the index-driven `IPendingDateAwaitSource` implementation; new `LoggerMessage` templates.
- **Do not touch:** `src/Hexalith.Works.Contracts/`, `src/Hexalith.Works.Server/`, `src/Hexalith.Works.Projections/` (the pure project), `src/Hexalith.Works.Reactor/`, `Hexalith.Works.ServiceDefaults`, any `references/` submodule content.
- **Tests:** extend `tests/Hexalith.Works.IntegrationTests/` (dispatcher/index/source/reconciler deterministic lanes + the reworked `WorksReminderRecoveryPipelineSmokeTests` + `WorksAppHostTopologyTests` pins) and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/` (confinement token list; catalog stays 37). `WorksCommandPipelineSmokeTests`' fixed-id fix belongs to Story 4.7 per the deferred-work ledger — leave it unless trivially co-located.
- **Docs:** `docs/boundary-decision-record.md`, `docs/eventstore-api-surface-constraints.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md`.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.8: Register and Reconcile Date Reminders Durably] — story statement and ACs.
- [Source: _bmad-output/planning-artifacts/sprint-change-proposal-2026-07-21.md#4.2 New stories (Epic 4)] — F-RT-3/F-RT-5 findings, story intent, C2 reaffirmed over the boundary-record posture, 4.7→4.8 sequencing.
- [Source: _bmad-output/planning-artifacts/architecture.md#Infrastructure & Deployment (C2, C3)] — Dapr actor reminders registered at suspend; reconciliation-on-recovery; advisory-until-fired deadlines.
- [Source: _bmad-output/planning-artifacts/architecture.md#Naming Patterns] — reminder name = deterministic function of `(workItemId, awaitConditionKey)` under the canonical identity scheme.
- [Source: _bmad-output/implementation-artifacts/4-6-prove-reminder-and-reactor-recovery.md] — component inventory, M1 decision (superseded here), review history, substrate-limitation verification.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md#Deferred from: architecture/domain audit correct-course (2026-07-21)] — smoke-lane fixed-id collision, gateway-submit timeout at fbc78e58, F-PROJ-1 roll-up limitation (out of scope).
- [Source: src/Hexalith.Works/Reminders/*; src/Hexalith.Works/Runtime/*; src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs; src/Hexalith.Works/Program.cs] — current runtime state (verified 2026-07-22).
- [Source: references/Hexalith.EventStore/src/Hexalith.EventStore/Controllers/StreamsController.cs (ValidateRequest); .../Controllers/CommandsController.cs] — null-AggregateId 400, per-aggregate paging, command-submit contract.
- [Source: references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Registration/EventStoreDomainEventsServiceCollectionExtensions.cs; .../Client/Subscriptions/*; .../Hexalith.EventStore.DomainService/EventStoreDomainEventsEndpointExtensions.cs] — the (unused) domain-events subscription SDK, relevant only if 4.7 landed.
- [Source: tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs; DateReminderRecoveryRuntimeTests.cs; WorksAppHostTopologyTests.cs] — existing lanes to rework/extend.
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs; RuntimeAdapterGovernanceTests.cs; EventShapeGovernanceTests.cs; DependencyDirectionTests.cs] — the guards that constrain every new file.
- [Source: docs/boundary-decision-record.md; docs/eventstore-api-surface-constraints.md; docs/lifecycle-transition-matrix.md] — recorded decisions/limitations to update.
- [Source: https://docs.dapr.io/developing-applications/building-blocks/actors/actors-timers-reminders/] — reminder durability, overwrite semantics, failure policy (checked 2026-07-22).

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.

### File List

## Validation Notes

- Checklist pass applied during creation. The story pre-empts the known failure modes for this slice: reinventing Story 4.6's components (Task 1 inventories them; tasks rewire, not rebuild), placing runtime code outside the host edge (governance guards named), issuing the 400-rejected null-`AggregateId` read (banned explicitly, with a recording-fake assertion required), re-adding hand configuration (the `Tenants` gate removal is a task, not a hint), growing the durable catalog (index records pinned as plain STJ, catalog stays 37), trusting stale docs (catalog-36 prose and stale version pins flagged), fake completion of Tier-3 lanes (the fbc78e58 gateway-timeout blocker is named with explicit honest-reporting instructions), and building Story 4.7's subscription surface out of turn (dependency decision DD-1 with a standalone default path).
- Remaining implementation risks, carried openly: (a) the exact trigger timing of the EventStore runtime's `/project` dispatch is verified in Task 1, not assumed — if dispatch turns out to be lazy rather than post-append in the live topology, the steady-state trigger may need 4.7's subscription surface, making 4.7 a hard prerequisite (report as a blocker rather than working around); (b) the Tier-3 lanes may stay blocked by the drifted EventStore submodule's gateway-submit timeout — the deterministic proofs plus honest blocker documentation are the fallback the validation ladder expects.
