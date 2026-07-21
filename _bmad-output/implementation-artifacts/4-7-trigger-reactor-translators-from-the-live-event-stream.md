---
baseline_commit: 9526c31
---

# Story 4.7: Trigger Reactor Translators from the Live Event Stream

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want the running Works host to consume the domain event stream and drive the reactor translators,
so that cascade and child-completion resume actually execute in the live topology instead of only in component tests.

## Acceptance Criteria

1. **Given** the Works host runs under the Aspire topology
   **When** a parent Work Item reaches `Cancelled` or `Expired`
   **Then** an at-least-once event consumption path invokes the cascade dispatcher
   **And** active descendants receive idempotent terminal commands discovered from a re-readable projection
   **And** already-terminal descendants are identified from the persisted roll-up read model, not hardcoded as active.

2. **Given** a parent is suspended on a `ChildCompleted` await-condition
   **When** the child completes in the running topology
   **Then** a `WorkItemCompleted` consumer feeds the unchanged `ChildCompletionResumeTranslator` from a re-readable awaiting-parents source
   **And** the parent resumes via an idempotent `ResumeWorkItem` submission
   **And** every decision still round-trips through aggregate `Handle`.

3. **Given** the host crashes mid-cascade
   **When** it restarts
   **Then** a startup recovery pass discovers incomplete cascade checkpoints from a durable index and drives checkpoint replay
   **And** the cascade converges without duplicate terminal effects (live SM-1b lane).

4. **Given** the new consumption path is inspected
   **When** fitness and governance tests run
   **Then** the kernel and reactor projects remain free of any new dependency
   **And** the host contains no shadow-kernel conditional a pure `Handle` could not have produced.

## Tasks / Subtasks

- [ ] **Task 1 - Reconcile the current runtime surface and blockers before writing code (AC: #1-#4)**
  - [ ] Read `src/Hexalith.Works/Program.cs` end to end: today it registers polymorphic mappers (L17-18), `AddEventStoreDomainService` (L25), ProblemDetails (L30), `AddDaprClient` + `AddEventStoreReadModelStore` (L33-34), reminder actors + recovery composition (L39-40), maps the bespoke `/project` BEFORE `UseEventStoreDomainService()` (L51-65), and `MapActorsHandlers()` (L68). There is **no** `UseCloudEvents()`, no `MapSubscribeHandler()`, no `MapEventStoreDomainEvents()` — the host consumes zero events; the only inbound event path is the EventStore-server-invoked per-aggregate `/project` replay.
  - [ ] Read `src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs`: `DispatchAsync(WorkItemCancelled|WorkItemExpired)` (L32, L49) and `ReplayAsync(tenantId, parentWorkItemId, parentTerminalEventType)` returning `false` when no checkpoint exists (L70-83); duplicate parent-terminal reuses the checkpoint, never re-discovers (L98-101); `Attempted` is persisted BEFORE each submit (L152-157). These semantics are frozen by `tests/Hexalith.Works.IntegrationTests/CascadeRecoveryRuntimeTests.cs` (e.g. `source.Reads.ShouldBe(1)` at L81).
  - [ ] Read `src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs` — `IsTerminal: false` is hardcoded at L60 (this story replaces it, AC #1) — and `ReadModelCascadeCheckpointStore.cs` — key `projection:works:cascade-checkpoint:{tenantId}:{parentWorkItemId}:{parentTerminalEventType}` (L41-42), **no index of incomplete checkpoints exists**.
  - [ ] Read `src/Hexalith.Works.Reactor/ChildCompletionResumeTranslator.cs` and `TerminalCascadeTranslator.cs` — both stay byte-identical this story (AC #2 says "unchanged"); the Reactor csproj references Contracts only and is covered by the kernel purity scan.
  - [ ] Read the EventStore SDK subscription seam (read-only, never modify `references/`): `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainEventsEndpointExtensions.cs` (L28-62), `.../Hexalith.EventStore.Client/Subscriptions/EventStoreDomainEventProcessor.cs`, `EventStoreDomainEventsOptions.cs`, `.../Registration/EventStoreDomainEventsServiceCollectionExtensions.cs`, `DaprEventStoreDomainEventMarkerStore.cs`.
  - [ ] Read the publisher topic logic: `.../Hexalith.EventStore.Server/Events/EventPublisher.cs` (L197-208), `.../Configuration/EventPublisherOptions.cs` (`GetPubSubTopic`, L42-47), `.../Hexalith.EventStore.Contracts/Identity/AggregateIdentity.cs` (L92-94: tenant-scoped topic = `{tenantId}.{domain}.events`). This is Trap 1 (Dev Notes).
  - [ ] Record the checked-out EventStore submodule commit (`git -C references/Hexalith.EventStore log -1 --oneline`; observed `c6b72caa` on 2026-07-22 — it has moved past the `fbc78e58` state where the Tier-3 lanes timed out). Build both EventStore hosts explicitly (they carry `SuppressBuild=true`), rerun the two red Tier-3 lanes once, and re-baseline whether the 60 s gateway-submit timeout still reproduces BEFORE changing code.
  - [ ] Reconcile the stale test baseline: `_bmad-output/implementation-artifacts/tests/test-summary.md` ends at Story 4.6 (2026-06-17, 620 green + 2 skipped, catalog 36) but the actual post-correct-course baseline at `9526c31` is UnitTests 496, PropertyTests 3, ArchitectureTests 44, IntegrationTests 96/98 (2 red Tier-3 lanes), catalog **37** (`tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs:18`). Record the reconciled baseline in the Debug Log.

- [ ] **Task 2 - Wire the at-least-once event consumption path at the host edge (AC: #1, #2, #4)**
  - [ ] Builder side in `src/Hexalith.Works/Program.cs`: `AddEventStoreDomainEvents(typeof(WorkItemCreated).Assembly, ...)` + `AddDaprEventStoreDomainEventMarkerStore()` (the default marker store is IN-MEMORY — `EventStoreDomainEventsServiceCollectionExtensions.cs:50`; durable Dapr markers are required for at-least-once dedup across restarts) + one `AddEventStoreDomainEventHandler<TEvent, THandler>()` per consumed event.
  - [ ] App side: `app.UseCloudEvents()` + `app.MapSubscribeHandler()` + `app.MapEventStoreDomainEvents()` (pairing documented at `EventStoreDomainEventsEndpointExtensions.cs:15-19`; the EventStore server's own `Program.cs:31,45` is the exemplar). Keep the bespoke `/project` mapped before `UseEventStoreDomainService()` — do not disturb that ordering.
  - [ ] Resolve **Trap 1 (topic names)**: events for tenant `t` publish to `{t}.work.events`, not `work.events`, so a static `ForDomain("work")` subscription receives nothing. Either set `EventStore__Publisher__TopicOverrides__work` on the eventstore resource in `src/Hexalith.Works.AppHost/Program.cs` (config section `EventStore:Publisher`) so all work events land on one topic, or subscribe tenant-composed topics. Prove delivery live before building on it.
  - [ ] Resolve **Trap 2 (payload decode)**: `EventStoreDomainEventProcessor.cs:147` deserializes payloads with DEFAULT (non-Web) `JsonSerializerOptions`, while Works payloads are camelCase Web JSON; a bind failure surfaces as `FailedInvalidPayload` → 200 OK → **silently acked and lost**. Write the deterministic decode test (Task 6) FIRST with real `WorkItemV1Catalog` Web-JSON bytes. If the SDK processor cannot bind them, do NOT patch `references/` — use the proposal's permitted alternative: an equivalent host-local subscription endpoint (`MapPost` + `.WithTopic(...)`) that decodes with the existing `Runtime/WorksEventDecoder.cs` pattern and applies the same durable-marker dedup.
  - [ ] Rejection events are returned, never stream-appended, so the consumer only ever sees accepted events — no rejection filtering logic belongs in the consumer (a conditional on rejection types would be shadow-kernel logic, AC #4).
  - [ ] New logging via source-generated `LoggerMessage` only, metadata-only fields (tenant id, work item id, correlation id, event type name, reason codes). Event-id ranges 4600-4603 (reminders) and 4700-4702 (cascade) are taken — use a fresh range (e.g. 4800+). The host logging scan bans the placeholder substrings `{Payload`, `{Obligation`, `{Command`, `{Secret`, `{Token`, `{Body`, `{EventJson`, `{Json` and any interpolated `.LogXxx($"...")` (`RuntimeAdapterGovernanceTests.cs:229-266`) — use `{Kind}` not `{CommandType}`.

- [ ] **Task 3 - Drive the cascade dispatcher from parent-terminal events (AC: #1)**
  - [ ] Add `WorkItemCancelled` and `WorkItemExpired` consumers that invoke the existing, DI-registered `CascadeDispatcher.DispatchAsync` (registered singleton at `Runtime/WorksRecoveryExtensions.cs:50`, currently with zero production callers). Preserve the deterministic correlation-id format `cascade-{kind}-{tenantId}-{parentWorkItemId}-{parentSequence}-{descendantWorkItemId}` (`CascadeCommands.cs:21`) — substrate dedup depends on it.
  - [ ] Replace `IsTerminal: false` (`StreamReadingCascadeDescendantSource.cs:60`) with a lookup of the persisted roll-up read model `projection:works:rollup:{tenant}:{childWorkItemId}` (`WorksWhatsNextReadModel.RollUpKey`, persisted by `WorkItemProjectionDispatcher.PersistRollUpAsync` L180-193) reading `WorkItemRollUp.Status`/terminal flag. Fail closed: a missing roll-up entry means treat-as-active — a redundant terminal command to an already-terminal descendant is rejected idempotently by `Handle`, which is safe; skipping an active descendant is not.
  - [ ] Trust the roll-up entry ONLY for per-item terminal status, never for rolled-remaining: deferred item F-PROJ-1 (persisted parent rolled-remaining never converges — per-aggregate `/project` dispatch limitation) explicitly says "revisit alongside Story 4.7's read-model work". Do NOT pull the convergence fix in (it waits on an EventStore substrate seam); record the revisit decision in `deferred-work.md`.

- [ ] **Task 4 - Feed the unchanged ChildCompletionResumeTranslator from a re-readable awaiting-parents source (AC: #2)**
  - [ ] Add a `WorkItemCompleted` consumer. No awaiting-parents source exists today: `Reminders/PendingDateAwaitProjection.cs` filters to `DateReached` only (L43), the what's-next index drops Suspended items, and `WorkItemRollUp` carries no await conditions.
  - [ ] Recommended source shape (mirrors the existing `StreamReading*Source` pattern and per-aggregate gateway constraint): on `WorkItemCompleted(child)`, read the child's stream for `WorkItemCreated.Parent`, then read the parent's stream and replay its await state to build `AwaitingParent(TenantId, WorkItemId, AwaitConditions)`. Alternative: persist an awaiting-parents index read model from the `/project` dispatch path (keyed by tenant + awaited child id) — acceptable, but keep F-PROJ-1 staleness discipline in mind. Either way the source must be re-readable, per-aggregate (the gateway 400-rejects null-`AggregateId` reads, `StreamsController.cs:545-552`), and fail-closed on tenant mismatch.
  - [ ] Feed `ChildCompletionResumeTranslator.ToResumeCommands` UNCHANGED; submit resulting `ResumeWorkItem` commands through `Runtime/IWorkCommandSubmitter` with deterministic correlation ids (pattern: `CascadeCommands`/`DateResume`). `ResumeWorkItem(correlationKey)` is idempotent at `Handle` (no match = no-op, duplicate = no-op), so at-least-once redelivery is safe by construction — add no dedup machinery beyond the durable subscription marker.

- [ ] **Task 5 - Durable incomplete-checkpoint index + startup recovery pass (AC: #3)**
  - [ ] The Dapr state store offers no key enumeration, so replay discovery needs an explicit durable index. Maintain it in the same store as the checkpoints (statestore via `IReadModelStore`), written ETag-safely with `ReadModelWritePolicy.UpdateAsync` (the retrying pattern `WorkItemProjectionDispatcher.UpsertTenantIndexAsync` L141-178 already uses): add the `(tenantId, parentWorkItemId, parentTerminalEventType)` identity when a checkpoint is created incomplete, remove it when the checkpoint completes.
  - [ ] Keep `CascadeCheckpoint` shape, the checkpoint key format, `ReplayAsync`'s signature and false-on-missing semantics, and Attempted-before-submit write ordering EXACTLY as they are — `CascadeRecoveryRuntimeTests` freezes them.
  - [ ] Add a startup recovery hosted service (pattern: `Reminders/ReminderReconciliationService.cs`) that enumerates the index and calls `CascadeDispatcher.ReplayAsync` per entry, removing entries whose replay reports completion. Unlike reminder reconciliation, this must NOT require per-tenant hand configuration — the durable index itself is the discovery scope.
  - [ ] Document (in `docs/boundary-decision-record.md`) that checkpoints persisted before this story carry no index entry and are not auto-discovered; that is acceptable because no production path ever created one (dispatcher had no callers).

- [ ] **Task 6 - Deterministic test lanes (AC: #1-#3)**
  - [ ] Consumer handler tests with fakes (`RecordingWorkCommandSubmitter`, in-memory checkpoint store/index — extend the doubles in `CascadeRecoveryRuntimeTests.cs:130-187`): parent-terminal event → `DispatchAsync` invoked; `WorkItemCompleted` → translator fed from the source → idempotent resume submissions; duplicate delivery → no duplicate effect (marker + `Handle` idempotency).
  - [ ] The Trap-2 decode test: real catalog Web-JSON payload bytes through whichever decode path Task 2 lands (SDK processor or host-local endpoint), proving every consumed event type binds; undecodable input is logged-and-skipped, never throws into the Dapr retry loop as a poison message.
  - [ ] Descendant terminality tests: roll-up entry terminal → skipped; active → targeted; missing entry → treated active (fail-closed).
  - [ ] Index tests: created-incomplete adds entry, completed removes it, replay-after-simulated-crash converges from the index alone; second recovery pass is a no-op.
  - [ ] All existing lanes stay green untouched — especially `CascadeRecoveryRuntimeTests` (replay-not-rediscover), `TerminalCascadeTranslatorTests` (11), `ChildCompletionResumeTranslatorTests` (5), `DateReminderRecoveryRuntimeTests` (5).

- [ ] **Task 7 - Live SM-1b Tier-3 lane and smoke-lane repairs (AC: #1-#3)**
  - [ ] Author the gated live cascade lane (e.g. `WorksCascadeRecoveryPipelineSmokeTests` — avoid banned type-name tokens like `Hub`/`Router`): under the full AppHost, create parent + children, cancel the parent, prove descendants receive terminal commands through the LIVE consumption path (assert stream end-state via `POST /api/v1/streams/read`, never HTTP 202); then restart the AppHost mid-cascade and prove checkpoint replay converges with exactly one terminal event per descendant (live SM-1b). Copy the reminder lane's gating (`Assert.Skip` when Redis :6379 / placement :50005 / scheduler :50006 absent) and its **per-run-unique aggregate ids** (`"work-cascade-" + Guid[..12]` pattern, `WorksReminderRecoveryPipelineSmokeTests.cs:64`).
  - [ ] Fix the deferred-work item assigned to this story: switch `WorksCommandPipelineSmokeTests` from fixed `work-smoke-1` (`WorksCommandPipelineSmokeTests.cs:43`) to a per-run-unique id — against the persistent `dapr init` Redis the fixed id now collides with the duplicate-create rejection introduced 2026-07-21 (terminal status `Rejected` ≠ `Completed`).
  - [ ] Re-prove the Tier-3 lanes at the current submodule. If the 60 s gateway-submit timeout still reproduces, record the exact command and result as a broad-gate blocker per the validation ladder (build both EventStore hosts explicitly first — they carry `SuppressBuild=true`), keep the deterministic evidence separate, and never weaken the gate to hide it. Do not claim Aspire lanes ran if they skipped; log which ports were absent.

- [ ] **Task 8 - Fitness and governance compliance (AC: #4)**
  - [ ] Zero csproj changes to Contracts/Server/Projections/Reactor; `DependencyDirectionTests` expected sets stay untouched (Reactor → Contracts only, L36-40).
  - [ ] All new types live under `src/Hexalith.Works/` (or AppHost config): the runtime-token confinement gate (`RuntimeAdapterGovernanceTests.cs:60-91`) fails any file outside host/AppHost containing `CascadeDispatcher`, `IWorkCommandSubmitter`, `IEventStoreGatewayClient`, `IReadModelStore`, etc. If new consumption tokens warrant confinement, extend that gate by the ownership-guard pattern (Story 4.6 precedent), never weaken it.
  - [ ] No new durable polymorphic types: `WorkItemV1Catalog.Count` stays **37** (pinned in three `ScaffoldGovernanceTests` facts + `RuntimeAdapterGovernanceTests.cs:139-145`). Subscription envelopes, index records, and source records are plain STJ types, NOT catalog entries.
  - [ ] Banned type-name fragments anywhere in `src/`: `Router`, `Hub`, `SignalR`, `Chatbot`, `McpTool`, `RoutingEngine`, `Eligibility*`, `EscalationLadder`, `ExecutorRanking` (`ScaffoldGovernanceTests.cs:547-564`).
  - [ ] AppHost `Program.cs` string contract: keep `AddHexalithEventStore`/`AddHexalithEventStoreSecurity`/`AddEventStoreDomainModule`/`WithJwtBearerSecurity(security)`/`WithEventStoreClientCredentials(security)`; never introduce `AddDaprStateStore`, `AddDaprPubSub` (case-insensitive match!), or `AddKeycloak(` (`RuntimeAdapterGovernanceTests.cs:148-163`). A `TopicOverrides` env var is fine; a new pub/sub component is not needed — the works sidecar already references the shared `pubsub` component (`AspireDaprDomainModuleAspireExtensions.cs:41-48`).
  - [ ] No shadow-kernel conditionals in the host: consumers decide only delivery/retry/checkpoint sequencing; anything resembling "is this child the last one?", "should this parent resume?", or per-status branching beyond mechanical translation belongs in `Handle` and already lives there.

- [ ] **Task 9 - Documentation and story bookkeeping (AC: #1-#4)**
  - [ ] Update `docs/boundary-decision-record.md`: the consumption-path decision (SDK seam vs equivalent host hook + why), the topic-name resolution, the awaiting-parents source shape, the checkpoint-index decision, pre-index checkpoint non-discovery.
  - [ ] Update `docs/eventstore-api-surface-constraints.md` with any newly confirmed substrate facts (topic composition, processor decode behavior, marker semantics).
  - [ ] Update `deferred-work.md`: mark the fixed-id smoke-lane item done; record the F-PROJ-1 revisit outcome; add anything newly deferred.
  - [ ] Append the Story 4.7 section to `_bmad-output/implementation-artifacts/tests/test-summary.md` AFTER the 4.6 section (new sections append at file end), including the Task-1 baseline reconciliation note.

- [ ] **Task 10 - Verify the slice (AC: #1-#4)**
  - [ ] Baseline is the 2026-07-21 correct-course final at commit `9526c31`: UnitTests **496**, IntegrationTests **96/98** (2 red Tier-3 lanes = known blocker, not regression), ArchitectureTests **44**, PropertyTests **3**, catalog **37**.
  - [ ] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` then `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — MUST be 0 warnings / 0 errors (`dotnet test` is sandbox-blocked by Microsoft.Testing.Platform named pipes).
  - [ ] Run all four suites as direct binaries: `tests/<Proj>/bin/Release/net10.0/<Proj>` for UnitTests, IntegrationTests, ArchitectureTests, PropertyTests.
  - [ ] Reconcile final counts across Dev Agent Record, this story file, and test-summary.md against actual binary output — zero drift is a blocking self-check (Epic 4 retro Action Item 1). State skip reasons for any gated lane that skipped.

## Dev Notes

### Scope Boundary

**The likely implementation failure, up front:** wiring the SDK subscription and assuming it works. Two independent live traps make a naive wiring silently dead: (1) the publisher emits to tenant-composed topics `{tenantId}.work.events` (`AggregateIdentity.cs:92-94`) while the SDK's `ForDomain("work")` default subscribes `work.events` — nothing arrives; (2) the SDK processor deserializes payloads with default non-Web `JsonSerializerOptions` (`EventStoreDomainEventProcessor.cs:147`) while Works persists camelCase Web JSON — bind failures are acked as `FailedInvalidPayload` (poison-loop protection) and **silently dropped**. Prove delivery and decode with tests before building the handlers on top. The correct-course proposal explicitly permits the fallback: "or an equivalent hook on the `/project` dispatch" — a host-local subscription endpoint reusing `WorksEventDecoder` is legitimate if the SDK seam's decode proves broken; patching `references/` is not in scope.

**In scope (the five correct-course work items, F-RT-1/2/4/7):**
1. At-least-once event consumption path at the host edge.
2. `CascadeDispatcher.DispatchAsync` invoked on parent-terminal events (`WorkItemCancelled`/`WorkItemExpired`).
3. Re-readable awaiting-parents source + `WorkItemCompleted` consumer feeding the **unchanged** `ChildCompletionResumeTranslator` into idempotent `ResumeWorkItem` submissions.
4. Startup recovery pass over a new durable incomplete-checkpoint index driving `CascadeDispatcher.ReplayAsync` (live SM-1b lane).
5. `CascadeDescendant.IsTerminal` from the persisted roll-up read model instead of hardcoded `false`.
Plus: the deferred-work smoke-lane fix (unique-per-run ids in `WorksCommandPipelineSmokeTests`) and re-proving the Tier-3 lanes.

**Out of scope (Story 4.8 — do not pull in):** suspend-time date-reminder registration, the pending-date-await index replacing the tenant-wide `AggregateId: null` scan, reminder reconciliation without per-tenant hand configuration, the live SM-1 date-resume lane. Design the subscription surface so 4.8 can simply add a `WorkItemSuspended` handler ("subscription surface is 4.8's natural trigger too" — proposal sequencing note). Story 4.8's file already exists (created in parallel, 2026-07-22) and its DD-1 explicitly prefers an `IEventStoreDomainEventHandler<WorkItemSuspended>` on this story's subscription surface once 4.7 lands — see `4-8-register-and-reconcile-date-reminders-durably.md` Task 1 + DD-1. Also out: F-PROJ-1 roll-up convergence (waits on an EventStore substrate seam — record the revisit decision only), F-PROJ-2 mutation-testing gate, any change to rejection-event shapes, any new domain/durable type, Keycloak, production surfaces, and everything on Story 4.6's out-of-scope list.

### Current State and Files to Read Before Editing

**Host (`src/Hexalith.Works/`, Web SDK, the only Works project permitted to reference EventStore runtime/Dapr/ASP.NET):**
- `Program.cs` — full wiring today (see Task 1). Insertion points: builder-side registrations near L33-40; app-side `UseCloudEvents`/`MapSubscribeHandler`/`MapEventStoreDomainEvents` around the existing L45-68 pipeline. The bespoke `/project` must stay mapped before `UseEventStoreDomainService()` (SDK yields via `IsRouteMapped`, `EventStoreDomainServiceExtensions.cs:146-150`).
- `Recovery/Cascade/` — `CascadeDispatcher` (at-least-once dispatch + checkpoints; zero production callers), `CascadeCheckpoint` (`Pending/Attempted/Completed` targets; `CancelKind`/`ExpireKind`), `ReadModelCascadeCheckpointStore` (durable, per-identity key, no index), `StreamReadingCascadeDescendantSource` (parent-stream `ChildSpawned` scan, direct children, `IsTerminal: false` at L60, swallows exceptions into `WorksRecoveryLog.RecoveryStepFailed`), `CascadeCommands` (deterministic correlation ids + `BuildSubmission`).
- `Runtime/` — `IWorkCommandSubmitter`/`EventStoreGatewayWorkCommandSubmitter` (maps `CausationId → SubmitCommandRequest.MessageId`, the substrate dedup key), `WorksEventDecoder` (host-edge event decoding — the Trap-2 fallback), `WorksRecoveryExtensions` (DI composition; gateway base address `EventStore:CommandGateway:BaseAddress`, default `http://eventstore`), `WorksRecoveryLog` (LoggerMessage ids 4600-4603, 4700-4702), `WorksRecoveryOptions`.
- `Reminders/ReminderReconciliationService.cs` — the hosted-service startup-pass pattern to mirror for checkpoint recovery (but its per-tenant `Works:Recovery:Tenants` gating must NOT be copied — the checkpoint index is self-describing).
- `Projections/WorkItemProjectionDispatcher.cs` — decodes `ProjectionEventDto` by `EventTypeName` → concrete Web-JSON (no `$type`); `PersistRollUpAsync` (L180-193) writes `projection:works:rollup:{tenant}:{workItemId}`; `UpsertTenantIndexAsync` (L141-178) is the ETag-safe `ReadModelWritePolicy.UpdateAsync` exemplar for the new checkpoint index. `IProjectionChangeNotifier` is always null in this host (registered only in EventStore.Server) — do not rely on notify-on-change.

**Reactor (`src/Hexalith.Works.Reactor/`, pure, Contracts-only, UNCHANGED this story):** `TerminalCascadeTranslator` (skips `IsTerminal`, fail-closed tenant equality, never decides acceptance), `ChildCompletionResumeTranslator.ToResumeCommands(WorkItemCompleted, IReadOnlyList<AwaitingParent>)`, `AwaitingParent(TenantId, WorkItemId, AwaitConditions)`, `CascadeDescendant(TenantId, WorkItemId, IsTerminal)`.

**Read models available for AC #1's terminality:** `WorkItemRollUp` (Contracts/Models, shape structurally frozen by `ScaffoldGovernanceTests.cs:249-299`) carries `Status` + terminal tracking maintained by `WorkItemRollUpProjection.SetTerminal` on Completed/Cancelled/Expired/Rejected-noRequeue — per-child terminal status is reliable because each child's own stream projection persists its own roll-up entry; only CROSS-aggregate rolled-remaining is stale (F-PROJ-1).

**EventStore SDK subscription seam (read-only exemplars):**
- `MapEventStoreDomainEvents` maps `POST {SubscriptionRoute}` with `.WithTopic(PubSubName, TopicName)`; result mapping acks `Processed/Duplicate/Skipped*/FailedInvalidPayload` with 200 and returns 500 only for `RetryableInProgress` (`EventStoreDomainEventsEndpointExtensions.cs:28-62`).
- `EventStoreDomainEventProcessor`: ULID envelope validation; marker dedup (`Acquired/Completed→Duplicate/InProgress→500`); type registry keyed by `Type.FullName`; DI-scoped dispatch to all `IEventStoreDomainEventHandler<TEvent>`; context carries TenantId/AggregateId/MessageId/SequenceNumber/CorrelationId/CausationId/GlobalPosition.
- Options: `PubSubName="pubsub"`, `TopicName="domain.events"`, `SubscriptionRoute="/domain-events"`, `MarkerKeyPrefix="eventstore:domain-events:markers:"`, `ForDomain(domain)` → `{domain}.events` + `/{domain}/events`.
- Envelope: `EventStoreDomainEventEnvelope(MessageId, AggregateId, TenantId, EventTypeName, SequenceNumber, Timestamp, CorrelationId, SerializationFormat, byte[] Payload, ...)`.
- Gateway constraints shaping any re-readable source: per-aggregate reads only (`StreamsController.ValidateRequest` 400s null/whitespace `AggregateId` at L545-552 and any `ContinuationToken` at L585-592; `PageSize` 1..1000). Page by advancing `LastSequenceReturned + 1` (Story 4.6 review fix L1).

**AppHost (`src/Hexalith.Works.AppHost/`):** works sidecar already references the shared `pubsub` + `statestore` components via `AddEventStoreDomainModule` — no component changes needed; the only likely AppHost edit is the `EventStore__Publisher__TopicOverrides__work` env on the eventstore resource. `IProjectMetadata` paths already carry the `references/Hexalith.EventStore/...` prefix — do not regress them.

**Tier-3 lanes (both currently RED at the correct-course submodule state, 60 s HttpClient timeout at gateway submit):** `WorksCommandPipelineSmokeTests` (fixed `work-smoke-1` — the id-collision fix belongs to this story) and `WorksReminderRecoveryPipelineSmokeTests` (already per-run-unique ids; two-phase AppHost restart pattern; asserts exactly-one `WorkItemResumed` via stream read; dev JWT: signing key `DevOnlySigningKey-AtLeast32Chars!`, issuer `hexalith-dev`, audience `hexalith-eventstore`). Aspire model-inspection tests (`WorksAppHostTopologyTests`) run without Docker; live `StartAsync` lanes gate on Redis :6379 + placement :50005 + scheduler :50006.

### Architecture Compliance

- **C1 / load-bearing invariants 4-6, 9** [architecture.md:95-103, 255]: reactor logic is mechanical event→command translation; at-least-once delivery + idempotent target commands + checkpoint-driven resumable cascade off a re-readable projection; `react(event) → command[]` stays pure; command-emit idempotency and projection idempotency are BOTH required. Every decision round-trips through pure `Handle` — the host may never grow a conditional `Handle` could not have produced (SM-C1/SM-C2 guard, AC #4).
- **Placement (realized Epic 4)** [architecture.md:169, 180-183, 492-499]: runtime dispatch/checkpointing/wiring lives in the runnable host `src/Hexalith.Works` (`Recovery/Cascade/`, `Runtime/`, `Reminders/`); `Hexalith.Works.Reactor` stays pure translators referencing Contracts only. Older architecture text naming `Reactor/Dispatch|Cascade|Timer` folders is superseded — trust the realized layout.
- **B2 delivery posture** [architecture.md:254]: Dapr pub/sub is at-least-once, NOT ordered — never rely on broker ordering; write-path ordering comes from the single-writer actor; read-path correctness from idempotent, order-tolerant projections + offset/marker dedup.
- **SM-1b** [architecture.md:114, 514]: RR-2's gate — "mid-reactor-step crash converges", chaos/crash-injection at step boundaries under the Aspire host, lives in IntegrationTests. This story's AC #3 is its live realization.
- **Identity discipline** [architecture.md:322-326]: every key derives from `{tenant}:work:{workItemId}` — checkpoint-index keys, topic names, marker keys, correlation ids all follow; no parallel key scheme. Identifiers are ULIDs — never `Guid.Parse`/`Guid.TryParse` (banned symbols).
- **Observability** [architecture.md:48, 397-398]: structured logs with correlation + tenant context; never log event payloads, personal data, secrets, raw tokens, or full command bodies. Rejections are domain events, never exceptions; infra failures are exceptions/dead-letter.
- **Tenant isolation** [architecture.md:43, 246, 393-395]: every consumer, source read, and index entry tenant-scoped; fail closed on cross-tenant mismatch (the translators already do).
- **Testing tiers** [architecture.md:120, 335-338, 578-580]: Tier-1 stays pure (no Dapr/Aspire/network); Aspire only for boundary/runtime proof. Prefer existing fakes/builders before new doubles.
- **Version policy** [architecture.md:205-206]: ecosystem-pinned; align `Directory.Packages.props` to the checked-out submodule pins, never to stale doc snapshots. No casual upgrades; only submodule requirements force pin changes.

### Previous Story Intelligence

- **Story 4.6's C1 CRITICAL review finding is the sharpest lesson**: subtasks for a live/restart lane were marked done while the lane did not exist — the review reopened the story. Every "live" AC here needs its named, authored, gated Tier-3 lane before any `[x]`, even if it `Assert.Skip`s in the sandbox (Epic 4 retro Action Item 2).
- **Story 4.6 M1** recorded the exact gap this story closes: the only caller of the reminder scheduler is startup reconciliation; no subscriber consumes live events. The documented decision was "reconciliation-on-recovery only for 4.6 scope" — 4.7/4.8 are its planned closure.
- **Idempotency designed once is the recovery pattern** (Epic 4 retro): at-least-once dispatch + idempotent targets + checkpoint-after-attempt means no recovery-specific dedup machinery anywhere — keep it that way in the consumers.
- **Document substrate limits, never fake them**: both 4.5 and 4.6 hit gateway boundaries and recorded them in `docs/eventstore-api-surface-constraints.md` with deterministic coverage plus a gated live lane. Reviewers verify such claims against submodule source.
- **Guard update, not workaround** (4.6 Task 7 precedent): deferral fitness guards are replaced by ownership assertions in the story that takes ownership.
- **Story 4.5 traps that recur here**: `AssemblyScanner` discovers only concrete `EventStoreAggregate<TState>` subclasses; `MapEventStoreDomainService` yields routes already mapped; `ReadModelWriteContext.WithEventDiagnostics(events)` is how correlation enters read-model write logs; assert stream end-state, never trust HTTP 202.
- **Review hygiene**: File List must exactly match `git status --porcelain` (exclude submodule gitlinks + `_bmad-output/story-automator/*`); zero test-count drift between Dev Agent Record, story file, and test-summary.md; state skip reasons explicitly.
- **Submodule drift** (`Hexalith.FrontComposer`, `Hexalith.Parties`, `Hexalith.Tenants` gitlinks) is a long-standing dirty-tree artifact — leave untouched, disclose, exclude from File List.

### Git Intelligence

Recent commits (HEAD = baseline `9526c31`):
- `9526c31` fix: implement read-side defenses and rejection handling for WorkItem creation and effort management — the 2026-07-21 correct-course implementation: duplicate/late create rejection, `WorkItemInitialEffortRejected` (catalog 36→37), fail-closed projections, Reactor added to purity scan + RNG/Environment/Guid-parse bans, `EventShapeGovernanceTests`, RFC 9457 ProblemDetails, AppHost `references/` path fixes, Stories 4.7/4.8 added to epics + sprint status.
- `6eb0044` / `d451797` build(deps): submodule bumps — EventStore moved to/past fbc78e58 (v3.80.0+), forcing `Microsoft.Extensions.*` 10.0.10, `Http.Resilience`/`ServiceDiscovery` 10.8.0, `OpenTelemetry.*` 1.17.0, `CommunityToolkit.Aspire.Hosting.Dapr` 13.4.1-beta.686, Aspire 13.4.6.
- `993cd88` fix: move submodules / `a31007e` feat: update submodule paths — the `references/` relocation; all cross-repo paths carry the `references/` prefix now.
- Conventional Commits mandatory; never bypass commitlint; commit only when the task requires it.

### Latest Technical Information

- **Pins are submodule-driven, not doc-driven**: .NET SDK 10.0.301 (`global.json`), Dapr 1.18.x, Aspire 13.4.6, xUnit v3 3.2.2 + Shouldly + NSubstitute, `Microsoft.Extensions.*` 10.0.10, `OpenTelemetry.*` 1.17.0. Any story or architecture text pinning older versions is stale — copy live values from `Directory.Packages.props` aligned to the checked-out submodules. NU1109 surfaces only once a project transitively pulls EventStore.Aspire.
- **EventStore submodule state moved after the correct-course**: memory recorded the Tier-3 blocker at `fbc78e58`; the checkout observed on 2026-07-22 is `c6b72caa`. Task 1 re-baselines the blocker; `EventStoreApiSurfaceCharacterizationTests` pins the substrate surfaces a bump must keep.
- **Dapr programmatic subscriptions** (the SDK seam uses them): subscriptions declared via `MapSubscribeHandler` discovery of `.WithTopic(...)` endpoints; `UseCloudEvents` unwraps the CloudEvents envelope. Marker-store dedup makes redelivery a `Duplicate` ack. Dead-letter topic convention: `deadletter.{topic}`.
- No external library upgrades are needed or permitted for this story — everything required ships in the checked-out `Hexalith.EventStore` submodule and existing pins.

### Project Structure Notes

- New source files go under `src/Hexalith.Works/` only — suggested: a new `Events/` folder for the consumers/handlers beside the existing `Recovery/`, `Reminders/`, `Runtime/`, or extend `Recovery/Cascade/` for the index + recovery service; AppHost gets at most env-var wiring. No new project (the frozen project set + forbidden-fragment gates), no new NuGet packages, all `Hexalith.*` refs stay ProjectReferences.
- Tests: deterministic lanes in `tests/Hexalith.Works.IntegrationTests/` (consumer/index/source) and `tests/Hexalith.Works.UnitTests/` where kernel-free; the live lane joins the Tier-3 smoke set in IntegrationTests; fitness updates in `tests/Hexalith.Works.ArchitectureTests/FitnessTests/` follow the ownership-guard pattern.
- Docs: `docs/` is never scanned by fitness tests — decision records and constraint notes belong there.
- File naming: one type per file, file named for the type, file-scoped namespaces matching folder path, Allman braces, `_camelCase` fields, XML docs on all public/protected/internal members.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story-4.7 (L1151-1184)] - story + verbatim ACs; 4.8 boundary at L1186-1218; Story 4.6 ACs at L1112-1149.
- [Source: _bmad-output/planning-artifacts/sprint-change-proposal-2026-07-21.md] - findings F-RT-1/2/4/7 (§2-§4), prescribed consumption surface + permitted alternative (L176-188), 4.7/4.8 split (L176-197), applied fixes (L109-172), Tier-3 blocker record (L259-266), success criteria (L268-277).
- [Source: _bmad-output/planning-artifacts/architecture.md] - C1 reactor contract (L255), invariants 4-6/8/9 (L95-103), B2 delivery posture (L254), realized host layout (L169, 180-183, 492-499), SM-1b/RR-2 (L114, 514), identity discipline (L322-326), observability (L397-398), enforcement checklist (L402-418).
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md] - SM-1 lane (L394), cascade FR (L183), projection idempotency mandate (L198), rebuildability (L377).
- [Source: _bmad-output/implementation-artifacts/4-6-prove-reminder-and-reactor-recovery.md] - previous story: files built, C1/M1/L1/L2 review findings, patterns (deterministic names, checkpoint boundaries, two-phase restart lane).
- [Source: _bmad-output/implementation-artifacts/4-5-prove-the-command-event-pipeline-under-aspire.md] - host/AppHost genesis, dispatcher decode pattern, smoke-lane JWT + gating.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] - F-PROJ-1 revisit-with-4.7 note; fixed-id smoke-lane item assigned to 4.7.
- [Source: _bmad-output/implementation-artifacts/epic-4-retro-2026-06-17.md] - debt items E1-E5, action items 1-2, realized-layout note (§10).
- [Source: src/Hexalith.Works/Program.cs; Recovery/Cascade/*.cs; Runtime/*.cs; Reminders/ReminderReconciliationService.cs; Projections/WorkItemProjectionDispatcher.cs] - host current state, cited by line throughout.
- [Source: src/Hexalith.Works.Reactor/ChildCompletionResumeTranslator.cs; TerminalCascadeTranslator.cs; AwaitingParent.cs; CascadeDescendant.cs] - unchanged translator surface.
- [Source: references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainEventsEndpointExtensions.cs; ...Client/Subscriptions/EventStoreDomainEventProcessor.cs; EventStoreDomainEventsOptions.cs; ...Registration/EventStoreDomainEventsServiceCollectionExtensions.cs] - the subscription seam.
- [Source: references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventPublisher.cs; ...Configuration/EventPublisherOptions.cs; ...Contracts/Identity/AggregateIdentity.cs (L92-94); ...Controllers/StreamsController.cs (L534-595)] - topic composition + gateway read constraints.
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs; ScaffoldGovernanceTests.cs; DependencyDirectionTests.cs; EventStoreApiSurfaceCharacterizationTests.cs] - the gates Task 8 satisfies.
- [Source: tests/Hexalith.Works.IntegrationTests/CascadeRecoveryRuntimeTests.cs; WorksCommandPipelineSmokeTests.cs; WorksReminderRecoveryPipelineSmokeTests.cs; WorkItemV1Catalog.cs] - frozen semantics, smoke-lane patterns, catalog pin 37.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

### Change Log

- 2026-07-22: Story context created from the 2026-07-21 correct-course (findings F-RT-1/2/4/7); status ready-for-dev.

## Validation Notes

- Created via the create-story workflow with four parallel deep-analysis passes (sprint-change proposal, architecture.md, live codebase incl. the EventStore SDK subscription seam, previous-story/test intelligence); all Dev Notes claims carry file:line citations verified on 2026-07-22 at baseline `9526c31`.
- Highest implementation risks, in order: (1) the tenant-composed topic-name trap and (2) the non-Web payload-decode trap — both make a naive SDK-seam wiring silently dead; Task 2 sequences the proofs before the build-out. (3) The Tier-3 gateway-timeout blocker predates this story and may persist at the current submodule; Task 1/Task 7 re-baseline it and record honestly rather than gating the story on infrastructure outside this repo's control.
- The EventStore submodule has moved (`c6b72caa` observed) since the correct-course recorded the blocker at `fbc78e58` — Task 1 re-verifies every submodule-dependent claim before code changes.
