---
baseline_commit: 9526c31
status: done
---

# Story 4.8: Register and Reconcile Date Reminders Durably

Status: done

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

- [x] **Task 1 - Reconcile existing foundations before writing code (AC: #1-#4)**
  - [x] Check `_bmad-output/implementation-artifacts/sprint-status.yaml` for the status of Story `4-7-trigger-reactor-translators-from-the-live-event-stream`. **If 4.7 has landed**, read its story file and prefer its event-consumption surface (an `IEventStoreDomainEventHandler<WorkItemSuspended>` on the domain-events subscription) as the steady-state registration trigger. **If 4.7 has NOT landed** (it is `backlog` at story-creation time), use the `/project` dispatch hook (Design Decision DD-1 below) and do NOT build 4.7's pub/sub subscription, cascade wiring, or checkpoint-replay startup pass in this story. → **4.7 is `done`.** Using its `work.events` subscription surface (`IEventStoreDomainEventHandler<WorkItemSuspended>`) as the steady-state trigger (see Dev Agent Record → Completion Notes, DD-1 resolution).
  - [x] Read every file in `src/Hexalith.Works/Reminders/` (Story 4.6 built the full component set: `DateReminderName`, `DateReminderActor`/`IDateReminderActor`, `DaprDateReminderScheduler`/`IDateReminderScheduler`, `DateReminderRegistration`, `DateReminderReconciler`, `ReminderReconciliationService`, `IPendingDateAwaitSource`, `PendingDateAwait`, `PendingDateAwaitProjection`, `StreamReadingPendingDateAwaitSource`, `DateResume`). This story rewires their triggers and discovery source; it does not reinvent them.
  - [x] Read `src/Hexalith.Works/Runtime/` (`IWorkCommandSubmitter` + `WorkCommandSubmission`, `EventStoreGatewayWorkCommandSubmitter`, `WorksEventDecoder`, `WorksRecoveryExtensions`, `WorksRecoveryOptions`, `WorksRecoveryLog`) and `src/Hexalith.Works/Program.cs` (bespoke `MapPost("/project", ...)` before `UseEventStoreDomainService()`; `MapActorsHandlers()`). Also read 4.7's `Runtime/Events/WorksDomainEventProcessor` + `Recovery/*` handlers.
  - [x] Read `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` and `WorksWhatsNextReadModel.cs`: the dispatcher is the already-live per-aggregate event path (the EventStore runtime posts each `work` aggregate's replayed stream to `/project`), and `WorksReadModelKeys` + `ReadModelWritePolicy.UpdateAsync<T>` is the established idempotent read-model upsert pattern.
  - [x] Verify in `references/Hexalith.EventStore` (read-only; never init/update submodules) when the runtime dispatches `/project` relative to event persistence, so the steady-state registration latency is understood and documented. Also confirm `StreamsController.ValidateRequest` still 400-rejects a null `AggregateId` on `POST /api/v1/streams/read` — every stream read this story issues MUST carry an `AggregateId`. → `/project` is delivered by `ProjectionPollerService` (a `BackgroundService` on per-domain `RefreshIntervalMs` cadence — near-real-time background delivery, poll-interval latency, NOT lazy-on-query). `StreamsController.ValidateRequest` still 400-rejects null/whitespace `AggregateId` (`StreamsController.cs:545-552`) and non-null `ContinuationToken` (585-592).
  - [x] Read `docs/boundary-decision-record.md` (Story 4.6's M1 note: "reconciliation-on-recovery only, not registered at suspend time") and `docs/eventstore-api-surface-constraints.md`. **This story supersedes the M1 decision** — the 2026-07-21 correct-course reaffirmed architecture decision C2 ("registers a reminder at suspend") as authoritative over the boundary record's reconciliation-only posture.
  - [x] Note: prose docs (`boundary-decision-record.md`, `eventstore-api-surface-constraints.md`, `tests/test-summary.md`) still say the catalog "stays **36**"; the code and all four fitness guards assert **40** (Story 1.5 raised the catalog; Story 4.8's delta is zero). Trust the code; fix the prose you touch (Task 7).

- [x] **Task 2 - Register the reminder when the event path observes the suspension (AC: #1)**
  - [x] Hook the trigger surface chosen in Task 1. Default (4.7 not landed): inside `WorkItemProjectionDispatcher.DispatchAsync`, after decoding the aggregate's events, fold them with the existing pure `PendingDateAwaitProjection.PendingDateAwaits(ordered)` — if the item's current state holds pending `DateReached` awaits, ensure a reminder is registered for each via `IDateReminderScheduler.ScheduleResumeReminderAsync(pendingAwait, dueTime, ct)` with `dueTime = max(TimeSpan.Zero, pendingAwait.Instant - timeProvider.GetUtcNow())`. Inject `IDateReminderScheduler` and `TimeProvider` into the dispatcher's construction in `Program.cs` (clock reads stay at the host edge; the kernel receives none of this).
  - [x] Idempotency under replay/redelivery: every re-dispatch of the same stream re-registers the same deterministic name (`DateReminderName.For(tenantId, workItemId, correlationKey)`); `DateReminderActor.ScheduleResumeAsync` already persists registration state then `RegisterReminderAsync` with the same name, overwriting in place. Prove with a test that dispatching the same suspension twice produces exactly one distinct reminder name and cannot produce a second accepted `WorkItemResumed` (the aggregate no-ops a consumed/non-matching await; `DateResume.BuildSubmission` derives the same `CorrelationId`/`CausationId`, deduped at the substrate by `MessageId`).
  - [x] Registration failure must remain retryable: wrap the scheduler call, log via a `WorksRecoveryLog`-style bounded-metadata warning (reason code, tenant, work-item, reminder name only), then rethrow the original exception so the domain-event marker is released and at-least-once redelivery retries registration. The recovery pass remains an additional retry path.
  - [x] Do not register non-date awaits. An item that resumes early on another condition may leave a stale reminder; on fire, the resulting resume no-ops at the aggregate and the actor cleans its state (`ReceiveReminderAsync` orphan path) — this is the accepted 4.6 posture; do not build proactive unregistration unless a test proves it is needed.
  - [x] Steady-state proof shape: an item suspended on a near-future `DateReached` resumes when the Dapr Scheduler fires the reminder — no host restart, no reconciliation pass involved (live lane, Task 5).

- [x] **Task 3 - Maintain the pending-date-await index read model in the `/project` dispatcher (AC: #2)**
  - [x] Add a host-edge index read model (plain `System.Text.Json` records in `src/Hexalith.Works/` — NOT `[PolymorphicSerialization]`, NOT in Contracts; the durable catalog is **40** because of Story 1.5, and Story 4.8's delta is zero): a per-tenant index document holding `workItemId → pending DateReached entries` (reuse the `PendingDateAwait` record for entries), plus a single well-known tenant-registry document listing tenant ids that have (or have had) pending date awaits — this registry is what removes per-tenant hand configuration from recovery.
  - [x] Key scheme in `WorksReadModelKeys` (follow the existing pattern, store `"statestore"`): e.g. `PendingDateAwaitIndexKey(tenantId)` → `projection:works:pending-date-await:{tenantId}` and a constant registry key → `projection:works:pending-date-await:tenants`. Keys embed the tenant because `WorkItemId.Value` is a raw inner id (colliding across tenants).
  - [x] Maintain both documents in `WorkItemProjectionDispatcher.DispatchAsync` alongside the two existing writes, using `ReadModelWritePolicy.UpdateAsync<T>` (ETag-based update-with-retry, safe under concurrent per-aggregate dispatches): fold the replayed stream with `PendingDateAwaitProjection`; pending set non-empty → upsert this aggregate's entries and ensure the tenant is in the registry; pending set empty → remove this aggregate's entry (resume/terminal events clear it). Removal of empty tenants from the registry is optional — a stale registry tenant costs one cheap read on recovery; document whichever you choose.
  - [x] The index is per-aggregate-maintained (the `/project` contract delivers one aggregate's stream per call — same limitation as the what's-next index) and idempotent under redelivery/out-of-order dispatches (the fold is over the full replayed stream, so last dispatch wins with a complete picture).

- [x] **Task 4 - Replace the rejected tenant-wide scan with an index-driven source; drop the hand-configured tenant gate (AC: #2, #3)**
  - [x] Replace `StreamReadingPendingDateAwaitSource` with an index-driven `IPendingDateAwaitSource`: read the tenant registry → each tenant's index document → for each indexed entry, issue a **per-aggregate** `POST /api/v1/streams/read` (`AggregateId` always set) via `IEventStoreGatewayClient` and re-fold with `PendingDateAwaitProjection` to establish current truth before acting (the index is discovery, the stream is truth — a stale index entry whose stream shows the await cleared is skipped, and may be cleaned up). Never construct a `StreamReadRequest` with `AggregateId: null` anywhere.
  - [x] Keep `DateReminderReconciler` behavior as-is (it is already proven idempotent): due awaits (`Instant <= now`) → `DateResume.BuildSubmission` reissue through `IWorkCommandSubmitter`; future awaits → `IDateReminderScheduler` re-registration. Only its `IPendingDateAwaitSource` input changes.
  - [x] `WorksRecoveryOptions`: remove the `Tenants` list gate — `ReminderReconciliationService.ExecuteAsync` currently returns early when `Tenants.Count == 0`, which is exactly the hand-configuration AC #3 forbids. Reconciliation runs whenever `RunReconciliationOnStartup` (default `true`). Keep a bounded paging guard for the per-aggregate reads (rename/repurpose `MaxStreamPagesPerTenant` if useful, e.g. per-aggregate page cap; page by `FromSequence = LastSequenceReturned + 1`, `ContinuationToken` must stay null — the gateway fail-closes on non-null tokens).
  - [x] Update `src/Hexalith.Works.AppHost/Program.cs`: delete the `Works:Recovery:Tenants` → `Works__Recovery__Tenants__{index}` forwarding block (no longer meaningful). Update `WorksAppHostTopologyTests` pins accordingly (the `EventStore__CommandGateway__BaseAddress` pin stays).
  - [x] The whole recovery pass stays crash-safe by idempotency, not checkpoints: a restart mid-reconciliation repeats the scan; deterministic correlation ids dedup at the substrate and duplicate resumes no-op at the aggregate — preserve and re-assert this property.

- [x] **Task 5 - Prove the live SM-1 lane under Aspire (AC: #1, #3)**
  - [x] Rework `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs` into the full SM-1 proof, keeping the existing prerequisite gating (`Assert.Skip` unless Redis :6379, Dapr placement :50005, scheduler :50006 are reachable) and the dev-JWT auth path (`EnableKeycloak=false`, key `DevOnlySigningKey-AtLeast32Chars!`, issuer `hexalith-dev`, audience `hexalith-eventstore`):
    - **Steady-state phase (new, AC #1):** Create → Assign → Claim → Suspend on a near-future `DateReached` (a few seconds ahead); WITHOUT restarting, poll the per-aggregate stream (`POST /api/v1/streams/read`) with a bounded timeout until exactly one accepted `WorkItemResumed` appears — proving suspend-time registration + Dapr Scheduler fire end-to-end.
    - **Recovery phase (reworked, AC #3):** park a second item on a past `DateReached`, restart the AppHost against the same Redis **without any `--Works:Recovery:Tenants` argument**, and assert recovery auto-discovers it from the durable index: the overdue await is reissued as an idempotent resume (exactly one accepted `WorkItemResumed`, second pass adds none) and a future-dated third item gets its reminder re-registered (observable as its own later resume, or assert via the scheduler's registration side effects if a cheaper observable exists).
  - [x] Use per-run unique ids for tenant and work items (the deferred-work ledger records that fixed ids against the persistent `dapr init` Redis now collide with the duplicate-create rejection introduced 2026-07-21).
  - [x] **Known broad-gate blocker (do not fake around it):** at EventStore submodule `fbc78e58` both Tier-3 smoke lanes fail on a 60-second `HttpClient` timeout at the gateway submit even with both EventStore hosts built (they carry `SuppressBuild=true` — `dotnet build` them explicitly before running the lane). If the timeout persists after this story's changes, diagnose what you can, record the exact command/result honestly in the Dev Agent Record and `test-summary.md`, and keep the deterministic proofs green — the validation ladder records this as a substrate blocker, not a story failure to hide.

- [x] **Task 6 - Deterministic tests for the new wiring (AC: #1-#3)**
  - [x] Dispatcher/index tests (extend the existing dispatcher test lane): suspension with `DateReached` upserts the tenant index + registry; resume/terminal dispatch removes the entry; cross-tenant entries never merge (colliding inner ids in two tenants stay separate); double dispatch of the same stream is idempotent (same index state, one distinct reminder registration via a recording `IDateReminderScheduler` fake); a non-date suspension registers nothing; scheduler failure does not fail the dispatch.
  - [x] Index-driven source tests (pattern: `DateReminderRecoveryRuntimeTests` fakes — `FixedTimeProvider`, `RecordingWorkCommandSubmitter`, recording scheduler, plus an in-memory read-model store and a fake `IEventStoreGatewayClient`): discovery from registry → index → per-aggregate re-fold; a stale index entry whose stream shows the await cleared is not reissued; **assert no `StreamReadRequest` is ever constructed with a null `AggregateId`** (recording gateway fake).
  - [x] Reconciler end-to-end deterministic pass over the new source: due → reissued once across two passes (one distinct correlation id), future → rescheduled deterministically — mirror the existing idempotency proofs so they keep holding with the new source.
  - [x] Governance: keep catalog guards at **40** (Story 1.5; Story 4.8's delta is zero; new index/read-model records are plain STJ, not `Polymorphic`); extend `RuntimeAdapterGovernanceTests.P0_ReminderActorAndCascadeCheckpointRuntimeAreConfinedToHostEdge`'s token list with the new type names if they should be host-edge-confined; the log-privacy guard (`P0_RuntimeAdapterLogsOnlyBoundedMetadataNeverPayloads`) must keep passing — use `LoggerMessage` templates with bounded metadata only.
  - [x] AC #4 needs no new code — confirm `ScaffoldGovernanceTests.P0_WorkItemKernelRemainsPure` (Contracts/Server/Projections/Reactor banned-symbol scan incl. clocks, `TimeProvider`, RNG, `Environment.`) still passes untouched; all new clock reads live in `src/Hexalith.Works` only.

- [x] **Task 7 - Documentation and governance bookkeeping (AC: #1-#4)**
  - [x] `docs/boundary-decision-record.md`: supersede the Story 4.6 M1 note — record that Story 4.8 implements suspend-time registration per architecture decision C2 (reaffirmed by the 2026-07-21 correct-course over the reconciliation-only posture), and that recovery discovery now runs from the durable pending-date-await index without per-tenant configuration.
  - [x] `docs/eventstore-api-surface-constraints.md`: update the Story 4.6 stream-read section — the null-`AggregateId` gateway limitation still exists but is no longer load-bearing for reminders (index + per-aggregate reads); describe the new index read model and its per-aggregate-maintenance semantics.
  - [x] Fix the stale "catalog stays 36" prose to the current count **40** (Story 4.8 delta 0) in the doc sections you touch.
  - [x] Append a Story 4.8 section to `_bmad-output/implementation-artifacts/tests/test-summary.md` with exact counts, skipped/blocked Tier-3 conditions, and verification commands. Keep this story's Dev Agent Record and File List accurate — the recurring review finding in this repo is test-count drift and false completion claims (Story 4.6 review found a CRITICAL one); never claim a live lane ran if it skipped or timed out.

- [x] **Task 8 - Verify the slice (AC: #1-#4)**
  - [x] Baseline at story creation (commit `9526c31`, post-correct-course 2026-07-21/22): build 0 warnings / 0 errors; UnitTests **496**, PropertyTests **3**, ArchitectureTests **44**, IntegrationTests **96/98** (the 2 Tier-3 Aspire smoke lanes red on the gateway-submit timeout when prerequisites are present; they `Assert.Skip` when Docker/Dapr placement/scheduler are absent). Catalog **37** (story-creation snapshot; current count is 40 / 4.8 delta 0).
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`, then `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — require 0 warnings / 0 errors. Do NOT use `dotnet test` (broken in this sandbox); run the four xUnit v3 binaries directly: `tests/<Proj>/bin/Release/net10.0/<Proj>` for UnitTests, IntegrationTests, ArchitectureTests, PropertyTests.
  - [x] Confirm `WorkItemV1Catalog.Count` is **40** (Story 1.5; Story 4.8's delta is zero) and the golden corpus is byte-unchanged (this story adds no durable catalog type).
  - [x] Never run recursive submodule commands or initialize nested submodules; leave the long-standing sibling submodule pointer drift (`Hexalith.FrontComposer`, `Hexalith.Parties`, `Hexalith.Tenants`) untouched and out of the File List.

### Review Findings

- [x] [Review][Patch] [High] Fix the live Dapr actor-reminder callback path and make AC #1's smoke test fail after prerequisites pass when no resume occurs [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:108] — resolved 2026-09-06: composed the missing mTLS actor control plane, then traced the reached callback path to Dapr actor remoting rejecting `DateReminderRegistration`; added its data-contract metadata and a serializer regression fact. The combined live class now passes 4/4 with no skips.
- [x] [Review][Patch] [High] Propagate incomplete reconciliation scans and retry startup reconciliation with bounded backoff until success or the configured limit [src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:28]
- [x] [Review][Patch] [High] Preserve a per-aggregate sequence watermark so an older replay cannot remove or overwrite a newer pending-date index entry [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:214]
- [x] [Review][Patch] [High] Make the recovery smoke establish durable-index state, prove zero pre-restart resumes, execute a genuine repeated reconciliation pass, and align its test-summary claim [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:85]
- [x] [Review][Patch] [High] Add the required live recovery proof that a future pending await is re-registered and later resumes [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:61]
- [x] [Review][Patch] [High] Fail closed on malformed known lifecycle events instead of folding an incomplete stream into reminder truth or index removal [src/Hexalith.Works/Reminders/PendingDateAwaitStreamReader.cs:49]
- [x] [Review][Patch] [High] Validate stream-page domain and decoded event tenant/work-item/aggregate identities before scheduling or indexing reminders [src/Hexalith.Works/Reminders/PendingDateAwaitStreamReader.cs:43]
- [x] [Review][Patch] [High] Validate `MaxStreamPagesPerTenant` as positive at startup so zero or negative configuration cannot silently disable reads [src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs:23]
- [x] [Review][Patch] [Medium] Add multi-page, page-advance, and exhausted-budget tests for `PendingDateAwaitStreamReader` [tests/Hexalith.Works.IntegrationTests/Story48Streams.cs:14]
- [x] [Review][Patch] [Medium] Add coordinated concurrent index-update tests for the singleton registry and same-tenant aggregate index [tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs:31]
- [x] [Review][Patch] [High] Extend the domain-event processor dispatch test to cover the newly consumed `WorkItemSuspended` envelope [tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs:76]
- [x] [Review][Patch] [Low] Split the two production index models and three test fakes into one correctly named C# file per type [src/Hexalith.Works/Projections/PendingDateAwaitIndex.cs:17]
- [x] [Review][Patch] [Low] Restore a real `last_updated` YAML value instead of an inline-commented null [\_bmad-output/implementation-artifacts/sprint-status.yaml:38]
- [x] [Review][Defer] [Medium] Event processing can return success after durable marker completion fails [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:119] — deferred, pre-existing
- [x] [Review][Defer] [Medium] Event processing does not reject envelopes whose domain is not `work` [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:227] — deferred, pre-existing

### Review Findings (2026-09-01, bmad-code-review)

- [x] [Review][Patch] [High] Diagnose and fix the Aspire AppHost's `DistributedApplication.StartAsync` failure (`TaskCanceledException` after 5m 08s, per this diff's own `test-summary.md`), which currently blocks all Tier-3 live proof for this story — not just AC #1's reminder-callback lane already tracked above, but AC #2/#3's recovery lane too. Root cause is undiagnosed; verified it is **not** the `works` resource dropping its explicit `.WaitFor(eventStore)`/`.WithReference(eventStore)` (that dependency is preserved internally by `AddEventStoreDomainModule`'s `References=[eventStore.EventStore]`/`WaitFor=[eventStore.EventStore]` in `references/Hexalith.EventStore/src/Hexalith.EventStore.Aspire/HexalithEventStoreDomainModuleExtensions.cs:73-74`). Investigate the newer topology this same diff adds to `src/Hexalith.Works.AppHost/Program.cs`: the `eventstore-operations` project + its own Dapr sidecar, and the `resiliency` Dapr component now wired via reflection (`SidecarOf`) into every discovered sidecar. Resolved 2026-09-06: explicit Development environment forwarding fixed the operations-host exit, and AppHost-owned mTLS Sentry/placement/Scheduler plus fixed test-harness proxy ports closed the remaining control-plane startup failures. The reminder, command, and cascade live lanes all start and pass. [src/Hexalith.Works.AppHost/Program.cs]
- [x] [Review][Defer] [Medium] This diff bundles substantial functionality outside Story 4.8's own declared scope into `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` and the wholesale-new `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs`: a schema-v2 migration path (`UseCurrentSchemaAsync`, `CurrentWhatsNextIndexKey`/`CurrentRollUpKey`, read at `WorkItemProjectionDispatcher.cs:143` once per dispatch and reused across awaited I/O — a plausible stale-key race under concurrent tenant migration), a roll-up child-reconciliation merge against persisted state, `WorkItemProjectionBoundarySanitizer`, and monotonic-write watermarks. None of this is mentioned in the story's Tasks, its File List annotations ("maintain index + registry" only), or the test-summary's own "Production code changed" bullets for Story 4.8. The Dev Notes explicitly place "the persisted parent roll-up convergence limitation (deferred-work F-PROJ-1)" **out of scope** for this story, yet these dispatcher changes are substantially about exactly that. Also confirmed two related File List gaps that fit the same pattern: `WorksHost.cs:82` (not in the File List) is where `WorkItemSuspendedReminderHandler` actually gets registered, and `WorksDomainEventProcessorTests.cs` (also not in the File List) already covers `WorkItemSuspended` dispatch, resolving the concern that checked-off Review Finding line above it. [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:143-213,394-413] — deferred, reason: out of scope per Dev Notes (F-PROJ-1 was already named as deferred); split into its own tracked story with explicit acceptance criteria and test plan. Tracked as DW-84 in deferred-work.md.
- [x] [Review][Patch] [Medium] Document the pending-date-await index/registry unbounded-growth tradeoff in `docs/boundary-decision-record.md`: `PendingDateAwaitTenantIndex.LastSequences` (`WorkItemProjectionDispatcher.MaintainPendingDateAwaitIndexAsync`, `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:415-482`) gets one permanent entry per work item ever dispatched through `/project` in a tenant — not only items that ever had a `DateReached` await — with no pruning; same for `PendingDateAwaitTenantRegistry.Tenants`, which is append-only forever. This diff's own `CascadeCheckpointIndex` got a `CascadeCheckpointIndexStaleAfterHours` retention knob (verified consumed and clamped in `CascadeRecoveryReconciler.cs:42`); the pending-date-await index has no analogous mechanism. Resolved 2026-09-02: accept as a documented limitation rather than building pruning now. [docs/boundary-decision-record.md] — resolved 2026-09-05: added the "Accepted limitation" paragraph to the Story 4.8 entry.
- [x] [Review][Patch] [Medium] Reconcile Story 4.8's test-count and result-count claims in `_bmad-output/implementation-artifacts/tests/test-summary.md`: it claims "+18" new IntegrationTests but the four new test files contain 27 `[Fact]`/`[Theory]` methods (verified by count: `PendingDateAwaitIndexDispatcherTests.cs` has 11, not the claimed 7; `IndexedPendingDateAwaitSourceTests.cs` has 9, not the claimed 5; `ReminderReconciliationServiceTests.cs` isn't mentioned at all). Separately, the embedded "2026-08-28 code-review rerun" paragraph reports UnitTests 528/528, ArchitectureTests 207/207, deterministic IntegrationTests 198/198 — numbers that don't reconcile with the rest of the same document (496/44/37 elsewhere) and read as pasted from an unrelated run. This is the exact "test-count bookkeeping drift" failure mode the story's own Dev Notes name as this repo's most recurring review finding. [_bmad-output/implementation-artifacts/tests/test-summary.md] — resolved 2026-09-05: corrected the per-file counts with an explicit correction note, added the omitted `ReminderReconciliationServiceTests`, and appended a dated "2026-09-05 code-review remediation session" section with counts verified against this session's actual binary runs (UnitTests 529/529, ArchitectureTests 236/236, PropertyTests 3/3, deterministic IntegrationTests 268/268).
- [x] [Review][Patch] [Medium] `IndexedPendingDateAwaitSource.ScanTenantAsync` propagates any single aggregate's stream-read failure, which aborts the entire cross-tenant `GetPendingDateAwaitsAsync` scan (by design — the comment states this is deliberate, to avoid treating a partial scan as success). Combined with `ReminderReconciliationService.ExecuteAsync` running its bounded retries only once at host startup and then giving up permanently on exhaustion, one persistently-unreadable stream in any single tenant silently disables date-reminder recovery for **every** tenant until the next host restart — a cross-tenant blast-radius regression versus the retired `StreamReadingPendingDateAwaitSource`, which caught and logged per-tenant failures and kept scanning the rest. Recommend isolating per-tenant failures (log + skip that tenant, still mark the overall pass incomplete for retry) instead of aborting the whole scan. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:65-73] — resolved 2026-09-05: `GetPendingDateAwaitsAsync` now catches per-tenant, logs (`WorksRecoveryLog.PendingDateAwaitTenantScanFailed`), continues scanning the remaining tenants, and throws the now-actually-wired-in `PendingDateAwaitScanIncompleteException` carrying the partial results once every tenant has been attempted; `DateReminderReconciler.ReconcileAsync` acts on those partial results before rethrowing so the retry-worthy failure is still surfaced. Proven by `IndexedPendingDateAwaitSourceTests.Isolates_a_single_tenant_scan_failure_and_still_returns_partial_results_from_the_others` and `DateReminderRecoveryRuntimeTests.Reconciler_acts_on_partial_results_then_rethrows_when_the_source_scan_is_incomplete`. This also closes the 2026-09-05 finding below (line 130) that the exception was dead code.
- [x] [Review][Patch] [Low] `WorksEventIdentity.Matches` treats a payload type with no `AggregateId` property as a match (`payloadAggregate is null || ...`) instead of failing closed. Low impact today since `TenantId`+`WorkItemId` remain mandatory, but it silently weakens the identity check for any future payload shape lacking that property. [src/Hexalith.Works/Runtime/WorksEventIdentity.cs:18-21] — resolved 2026-09-05: fails closed for any non-rejection payload missing `AggregateId`. Note: all nine `IRejectionEvent` types (`WorkItemTransitionRejected` etc.) legitimately carry no `AggregateId` by design, so an unconditional fail-closed broke `WorkItemProjectionQueryAdapterTests` in the first pass of this fix — corrected to check `payload is IRejectionEvent` before failing closed, keeping rejection events matching by `TenantId`+`WorkItemId` alone. Proven by new `WorksEventIdentityTests` (5 facts).
- [x] [Review][Patch] [Low] `PendingDateAwaitStreamReader.RebuildAsync` doesn't advance its `from` cursor when a page reports `IsTruncated=true` but `Metadata.LastSequenceReturned=null` (a documented-possible shape per `StreamReadMetadata`'s own XML doc: null "when the page is empty"), so it re-reads the same page every remaining iteration until the page budget is exhausted, then fails closed with a message ("exceeded the ... page budget") that misdescribes the actual condition. [src/Hexalith.Works/Reminders/PendingDateAwaitStreamReader.cs:75-89] — resolved 2026-09-05: fails closed immediately with an accurate message when this shape is seen, instead of looping to budget exhaustion. Proven by `IndexedPendingDateAwaitSourceTests.Fails_closed_immediately_when_a_truncated_page_reports_no_last_sequence_instead_of_stalling_the_cursor` (asserts exactly one gateway call).
- [x] [Review][Defer] [Low] `WorksDomainEventProcessor.MarkCompletedSafelyAsync`/`ReleaseSafelyAsync` (new file) catch every exception from the durable marker store and only log — no retry, no escalation. A marker-store failure after a handler already ran successfully leaves that message id permanently "acquired but unresolved," discoverable only via log monitoring. [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:268-302] — accepted deferral 2026-09-05: this is the same family of issue already tracked as `deferred-work.md` DW-56 ("Completed-marker failure can still be acknowledged as processed"), whose own recorded decision requires extending the **EventStore submodule's** marker protocol with a durable post-dispatch/retry state — a cross-repo change out of a single Works-repo session's scope, not an in-band patch. Cross-referenced to DW-56 rather than building a narrower, protocol-incompatible retry mechanism.
- [x] [Review][Patch] [Low] `_bmad-output/implementation-artifacts/sprint-status.yaml` — this diff rewrites every line of the file for what is only a couple of actual field changes (`4-8` status, `last_updated`), most likely a line-ending/whitespace normalization slip. Worth a clean re-save so the diff reflects only the real change. [_bmad-output/implementation-artifacts/sprint-status.yaml] — resolved 2026-09-05: normalized to consistent CRLF (5 lines had drifted to LF-only); re-save now diffs as only the 5 corrected lines.

### Review Findings (2026-09-05, bmad-code-review)

- [x] [Review][Patch] [Medium] Commit `4bbc01c` ("add `PendingDateAwaitScanIncompleteException` for handling incomplete tenant scans") does not actually close the still-open finding above (line 122): the new exception type's own XML doc claims it is "thrown by `IndexedPendingDateAwaitSource` when one or more tenants could not be scanned," but a repo-wide search finds it referenced nowhere outside its own declaration — never thrown, caught, or tested. `IndexedPendingDateAwaitSource.ScanTenantAsync`/`GetPendingDateAwaitsAsync` (`src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:54-76`) is byte-identical in behavior: no try/catch around the per-tenant loop, still propagates the first raw exception and aborts the whole cross-tenant scan. The class is dead code that documents a fix which was never wired in; the underlying cross-tenant blast-radius regression from line 122 remains fully open. [src/Hexalith.Works/Reminders/PendingDateAwaitScanIncompleteException.cs] — resolved 2026-09-05 together with line 122 above: the exception is now actually thrown, caught, and tested (see line 122's resolution note).
- [x] [Review][Patch] [Low] `PendingDateAwaitScanIncompleteException`'s constructor formats its message with `partialResults?.Count ?? 0` in the `base(...)` call, which evaluates before `ArgumentNullException.ThrowIfNull(partialResults)` runs in the constructor body — the null-tolerant formatting is dead: a null `partialResults` still throws `ArgumentNullException`, discarding the message entirely. Use `partialResults.Count` directly (after moving/relying on the null check). [src/Hexalith.Works/Reminders/PendingDateAwaitScanIncompleteException.cs:46-56] — resolved 2026-09-05: message building moved to a `BuildMessage` static helper evaluated as the `base(...)` argument, which validates `partialResults` via `ArgumentNullException.ThrowIfNull` before formatting anything. Proven by `PendingDateAwaitScanIncompleteExceptionTests.Constructor_throws_argument_null_for_a_null_partial_results_list`.
- [x] [Review][Patch] [Low] `PendingDateAwaitScanIncompleteException` is `public`, but its only documented thrower, `IndexedPendingDateAwaitSource`, is `internal sealed` — inconsistent with the assembly's internal-by-default surface. Make it `internal` (or withhold adding it until it is actually wired in). [src/Hexalith.Works/Reminders/PendingDateAwaitScanIncompleteException.cs:10] — resolved 2026-09-05: made `internal` (the assembly's `InternalsVisibleTo` to `Hexalith.Works.IntegrationTests` keeps it testable).
- [x] [Review][Patch] [Low] `deferred-work.md`'s DW-53 resolution note cites `ProjectionPayloadCoverageTests.cs:12-32`, a range that also spans DW-54's own cited what's-next assertions (27-32); tighten DW-53's citation to the roll-up-only sub-range (e.g. 21-26) so the two don't overlap. [_bmad-output/implementation-artifacts/deferred-work.md] — resolved 2026-09-05: DW-53 now cites `12-26,41-72` (setup + roll-up-specific call + shared helper), no longer overlapping DW-54's `27-32`.
- [x] [Review][Defer] [Low] `WorkItemRollUpPayloadCoverageTests.cs`'s single assertion is now a near-strict subset of `ProjectionPayloadCoverageTests.Projection_catalogs_cover_every_non_rejection_contract_payload_exactly_once`'s roll-up half, with no `EffectDisposition` checks of its own — two overlapping tests must now be kept in sync by hand. [tests/Hexalith.Works.ArchitectureTests/FitnessTests/WorkItemRollUpPayloadCoverageTests.cs] — deferred, pre-existing: the overlap was created when `ProjectionPayloadCoverageTests.cs` was added in the prior review round (commit `01d527a`); this diff only updates the older test to compile against the renamed `WorkItemRollUpPayloadDescriptor.Catalog` API.

**Rejected:**
- `false` — "`deferred-work.md` was not updated to record/close the line-122 finding": the open finding is already tracked directly in this story's own Review Findings section (line 122, unchecked); `deferred-work.md` is reserved for out-of-scope DW-numbered items, not in-scope open review findings.
- `false` — "DW-53/DW-54 closure is scope-mixed into the Story 4.8 exception commit": they are two separate, correctly-labeled commits (`4bbc01c` adds the exception class; `dbe1706 chore(sweep): close resolved deferred-work entries` closes DW-53/54) that only appear merged because this review's diff was scoped across the full commit range since the last 4.8 review; git history attributes them separately and both resolutions were independently verified as factually correct.
- `false` (×3) — "commits `d9235f5`/`73ea7cf`/`643b9894` aren't reflected in the reviewed diff": all three touch files outside Story 4.8's scope (an EventStore fixture spec + submodule pointer bump, a BMAD tooling sync, and an unrelated spec's `baseline_revision` field) and were deliberately excluded when this review's diff was scoped to Story-4.8-relevant files; not a defect in the change under review.
- `low`, not worth fixing — "no validation that `failedTenantCount > 0`" in the new exception's constructor: the class is currently unreferenced and unreachable, so this is unlikely to be encountered, and the fix (an added guard clause) is more than a direct correction for code that isn't wired in yet.
- `low`, not worth fixing — "the new `AllowsDelivery` test helper discards `TryResolve`'s success bool without asserting it": a separate existing test in the same file already asserts fixture types match the descriptor registry exactly, which independently catches the scenario this would silently mask; adding a redundant assertion here is speculative guarding.
- `maybe-false` (if true, only `low`) — "the exception constructor stores `partialResults` by reference instead of defensively copying it, so a caller mutating the list afterward would corrupt `PartialResults`": unverifiable because the type is never constructed anywhere in the codebase, so no real call site exists to check; even if it existed, post-throw mutation of exception state is a low-impact latent issue, not worth tracking further.

### Review Findings (2026-09-07, bmad-code-review)

_Human ruling 2026-09-08: all seven `[Review][Decision]` items below and in the 2026-09-06 round are decided — each decision is recorded inline on its own bullet and is binding. Scope for this run is **every unchecked `[Review][Patch]` item in both the 2026-09-06 and 2026-09-07 rounds**, including the cosmetic ones. The single remaining 2026-09-01 `[Review][Patch] [Low]` item stays open and cross-referenced to DW-56 (cross-repo EventStore marker-protocol change, out of scope)._

_Diff scoped to Story 4.8's own surface (`ff329cc..HEAD` over `src/Hexalith.Works/{Reminders,Projections,Runtime,Program.cs}`, the 4.8 tests, and `docs/`; 53 files, +3442/-527). The story frontmatter's `baseline_commit: 9526c31` yields a 122-commit, 471K-line diff spanning Stories 4.7 and 1.5 plus two BMAD tooling upgrades, and was not used._

- [x] [Review][Decision] Pending-date-await index is written on every `/project` dispatch, not only for date-await items — `MaintainPendingDateAwaitIndexAsync` runs its `UpdateAsync` unconditionally once `events` is non-empty, stamping `LastSequences[aggregateId]` for **every** work aggregate in the tenant into one singleton document. Two distinct harms: (a) unbounded growth — already accepted as a documented limitation on 2026-09-05; (b) **not** covered by that note — every concurrent `/project` dispatch in a tenant now contends on that one key, so `ReadModelWritePolicy.UpdateAsync`'s default 3 attempts (`ReadModelWritePolicy.DefaultMaxAttempts`) can exhaust and throw, turning an ordinary projection dispatch into a 500 and a poller retry. Options: guard the write on `pending.Count > 0 || index.Entries.ContainsKey(id) || index.LastSequences.ContainsKey(id)` (bounds both harms, keeps the tombstone semantics for items that ever held an await); shard the index per aggregate; or raise `maxAttempts` explicitly. [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:414-478] — **Decided 2026-09-08 (human): guard the write.** Only call `UpdateAsync` when `pending.Count > 0 || index.Entries.ContainsKey(id) || index.LastSequences.ContainsKey(id)`. This bounds both the contention and the documented unbounded growth while keeping tombstone semantics for items that ever held an await. Do not shard the index and do not merely raise `maxAttempts`. No durable read-model shape change. — **Implemented 2026-09-08:** `MaintainPendingDateAwaitIndexAsync` now reads the tenant index once and returns without writing when the dispatch holds no pending awaits and the index carries neither an entry nor a watermark for that aggregate — bounding both the contention and the documented growth while keeping tombstones for items that ever held an await. Not sharded; `maxAttempts` unchanged; no durable shape change. Proven by `PendingDateAwaitIndexDispatcherTests.An_item_that_never_held_a_date_await_writes_no_index_document_at_all` (and the reworked `Older_replay_cannot_resurrect_an_await_cleared_by_a_newer_replay`, which now establishes a real entry first, matching the decided tombstone semantics).
- [x] [Review][Decision] One unreadable candidate stream aborts every remaining candidate in the same tenant — `ScanTenantAsync` accumulates into a local list and lets the first `PendingDateAwaitStreamReader.RebuildAsync` failure propagate, discarding the awaits already collected for that tenant. The 2026-09-05 round fixed isolation at the *tenant* level; within a tenant the blast radius is still total. In the single-tenant AppHost topology this repo actually runs, that means one wedged work item disables date-reminder recovery for the whole system. Options: catch per candidate and roll the failure into `PendingDateAwaitScanIncompleteException` (needs a decision on whether `FailedTenantCount` becomes a candidate count or gains a second field); or accept and document the per-tenant granularity as final. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:95-104] — **Decided 2026-09-08 (human): isolate per candidate.** Catch per candidate, keep the awaits already collected for that tenant, and roll the failures into `PendingDateAwaitScanIncompleteException` via a **second field** for the failed-candidate count; leave `FailedTenantCount` meaning what it means today. Recovery must degrade, not collapse. — **Implemented 2026-09-08:** `ScanTenantAsync` catches per candidate, keeps the awaits already collected for that tenant, logs `WorksRecoveryLog.PendingDateAwaitCandidateScanFailed`, and rolls the failures into a **second** `PendingDateAwaitScanIncompleteException.FailedCandidateCount` field; `FailedTenantCount` keeps its meaning (a tenant whose index/registry read itself failed). Proven by `IndexedPendingDateAwaitSourceTests.Keeps_the_readable_candidates_of_a_tenant_when_one_candidate_stream_is_unreadable` and `.Isolates_a_single_tenant_index_read_failure_and_still_scans_the_other_tenants` (the latter using a new read-failure injection hook on the in-memory store fake).
- [x] [Review][Decision] A tenant literally named `tenants` silently disables all reminder recovery — `PendingDateAwaitIndexKey("tenants")` renders `projection:works:pending-date-await:tenants`, byte-identical to the well-known `PendingDateAwaitRegistryKey`. The dispatcher would overwrite the registry with a `PendingDateAwaitTenantIndex`, which deserializes into `PendingDateAwaitTenantRegistry` with an empty `Tenants` set, so `GetPendingDateAwaitsAsync` returns `[]` for every tenant with no error and no log. Options: give the registry a suffix that cannot collide (e.g. `...:pending-date-await-registry`, a durable-key change needing a migration note); or reject `tenants` as a tenant id at the host edge. [src/Hexalith.Works/Projections/WorksReadModelKeys.cs:49-59] — **Decided 2026-09-08 (human): reject `tenants` as a tenant id at the host edge.** Fail closed on the reserved id where tenant ids enter the host, with a clear message naming the key collision. Do **not** rename `PendingDateAwaitRegistryKey` — the durable key stays as-is and no migration is needed. — **Implemented 2026-09-08:** `WorksReadModelKeys.ReservedTenantId`/`IsReservedTenantId` added; the id is refused at both host edges where tenant ids enter (`WorkItemProjectionDispatcher.DispatchAsync` throws, `WorksDomainEventProcessor` returns `FailedInvalidPayload` with reason `reserved-tenant-id`) and by `PendingDateAwaitIndexKey` itself, each message naming the registry-key collision. `PendingDateAwaitRegistryKey` is unchanged — no migration. Proven by `PendingDateAwaitIndexDispatcherTests.A_reserved_tenant_id_is_refused_at_the_project_host_edge`.
- [x] [Review][Decision] Index removal is no longer reachable when the roll-up yields nothing — `UpsertTenantIndexAsync` was unconditional at `ff329cc` (`WorkItemProjectionDispatcher.cs:122`); it is now inside `if (model is not null)`. When `WorkItemProjectionBoundarySanitizer.Sanitize` returns null (roll-up refused every event), the stale eligible entry and its `LastSequences` watermark are retained forever instead of being removed. Restoring the unconditional call is not a drop-in: the new monotonic guard `persistedRollUp.LatestAcceptedSourceSequence <= model.LatestAcceptedSourceSequence` needs `model`. Part of the same dispatcher rework already deferred as DW-84, but the removal path is a behaviour change, not a refactor. [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:190-215] — **Decided 2026-09-08 (human): fix the removal path now.** This is a behaviour regression introduced in this diff, not the DW-84 refactor, so it is in scope. Restore reachable removal for the `model is null` case with a monotonic guard that does not depend on `model` (e.g. compare against the persisted `LastSequences` watermark for that aggregate). Do not defer it to DW-84. — **Implemented 2026-09-08, narrowed on evidence:** removal is reachable again for the `model is null` case, guarded by the delivered stream's own last sequence against the persisted `LastSequences` watermark (never against `model`). Narrowed because `Sanitize` returns null **only** when `projected` is null, which also covers empty and rejection-only replays — restoring the call unconditionally made `WorkItemProjectionQueryAdapterTests.Empty_and_rejection_only_replays_do_not_mutate_authoritative_projection_models` fail (verified, not assumed). The branch therefore requires `stateEvidenceDelivered` (at least one decoded non-`IRejectionEvent` payload), which is exactly "the roll-up refused real events" and excludes "there was nothing to project".
- [x] [Review][Decision] A permanently-undecodable state-affecting event poisons `/project` for its aggregate forever — the 2026-07 round deliberately added `throw new InvalidOperationException("A state-affecting Works projection event could not be decoded.")` to fail closed. Correct as fail-closed, but there is no terminal disposition: the endpoint 500s, `ProjectionPollerService` redispatches the same aggregate on every poll indefinitely, and that aggregate's roll-up and what's-next writes are blocked for good. Contrast `WorksDomainEventProcessor`, which terminally acknowledges malformed deliveries rather than looping. Options: park the aggregate after N failures; return a terminal `ProjectionResponse`; or accept the loop and document it as the intended fail-closed posture. [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:113-116] — **Decided 2026-09-08 (human): park the aggregate after N failures.** Keep failing closed, but after a bounded, configurable failure count record the aggregate as parked with a distinct log event and stop redispatching it, so the blast radius is one visible aggregate instead of a permanent poller loop. Do not return a terminal response on the first failure (transient causes must still retry) and do not accept the indefinite loop. — **Implemented 2026-09-08:** decoding still fails closed on every attempt, but consecutive failures on the *same* sequence are counted durably at `projection:works:parked:{tenant}:{aggregateId}` (`WorkItemProjectionParking`); on reaching `Works:Projection:MaxUndecodableEventDispatchesBeforeParking` (new `WorksProjectionOptions`, default 5, validated `ValidateOnStart`) the dispatch is acknowledged with a distinct `ProjectionAggregateParked` error log and no longer redispatched. Nothing is read or written on the healthy path. Proven by `PendingDateAwaitIndexDispatcherTests.Undecodable_state_affecting_event_parks_the_aggregate_after_the_configured_failure_budget` and `.A_failure_at_a_new_sequence_restarts_the_parking_budget`.
- [x] [Review][Decision] `MaxStreamPagesPerTenant` is a per-**aggregate** budget — its own XML doc says "per aggregate", `PendingDateAwaitStreamReader` applies it per work item, and the failure text reads "exceeded the configured MaxStreamPagesPerTenant per-aggregate page budget". An operator sizing this from the name will size it wrong by the number of items in the tenant. Renaming changes the bound `Works:Recovery` configuration key, so it needs a call on whether to rename with a bound alias or only correct the remarks. [src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs:22-23] — **Decided 2026-09-08 (human): rename with a bound alias.** Rename the property to `MaxStreamPagesPerAggregate` and keep the legacy `Works:Recovery:MaxStreamPagesPerTenant` configuration key readable as a deprecated alias so existing configuration keeps binding. Update the XML doc, the failure text, and the `ValidateOnStart` chain to match, and cover the alias with a test. — **Implemented 2026-09-08:** renamed to `MaxStreamPagesPerAggregate` (default via `DefaultMaxStreamPagesPerAggregate`); `MaxStreamPagesPerTenant` remains as a nullable deprecated alias that wins when set, so `Works:Recovery:MaxStreamPagesPerTenant` keeps binding. All consumers read `EffectiveMaxStreamPagesPerAggregate`; the `ValidateOnStart` chain and the fail-closed message text follow the new name. Covered by the new `WorksRecoveryOptionsTests` (alias binds, alias wins, defaults, and the validation chain).

- [x] [Review][Patch] **HIGH** — `PendingDateAwaitStreamReader` advances its page cursor one event too far, silently dropping one event per page boundary from the stream that is the story's declared source of truth [src/Hexalith.Works/Reminders/PendingDateAwaitStreamReader.cs:91] — resolved 2026-09-08: `StreamReadRequest.FromSequence` is an **exclusive** lower bound (`AggregateActor.ReadEventsRangeAsync` reads from `fromSequence + 1`; the gateway fake filters `> fromSequence`; EventStore's own `DaprBackupCommandService` pages with no increment), so the cursor now advances to `from = lastSequence`. `IndexedPendingDateAwaitSourceTests.Advances_to_the_next_page_and_folds_the_complete_stream` now pins `[0, 1]`. Note: `StreamReadingCascadeDescendantSource` and `StreamReadingChildCompletionAwaitingParentSource` (Story 4.7) carry the identical `+ 1` bug and are left untouched as out of this story's scope — reported to the caller.
- [x] [Review][Patch] `WorksEventDecoder.Decode` catches only `JsonException`, so a persisted `WorkItemSuspended` whose `[JsonConstructor]` guard trips (`ArgumentException`/`ArgumentNullException`) escapes the decoder instead of returning null and taking the Works fail-closed path [src/Hexalith.Works/Runtime/WorksEventDecoder.cs:41] — resolved 2026-09-08: `WorksEventDecoder.Decode` now catches `JsonException or ArgumentException or NotSupportedException`, so a constructor/value-object guard tripping returns null and takes the callers' fail-closed path instead of escaping the decoder.
- [x] [Review][Patch] AC #1's "duplicate registration remains idempotent" — required explicitly by Task 2 — has no test on the registration path that actually shipped; none of the six facts delivers the same `WorkItemSuspended` twice [tests/Hexalith.Works.IntegrationTests/WorkItemSuspendedReminderHandlerTests.cs:1] — resolved 2026-09-08: added `WorkItemSuspendedReminderHandlerTests.Redelivering_the_same_suspension_registers_the_same_single_reminder_name` — the identical delivery twice yields two registrations that collapse to one distinct `DateReminderName`, one await, and one due time.
- [x] [Review][Patch] Nothing asserts the built host resolves `IEventStoreDomainEventHandler<WorkItemSuspended>` to `WorkItemSuspendedReminderHandler`; deleting that registration makes the processor return `SkippedNoHandlers` → 200 OK and silently reverts AC #1 to restart-only recovery, with every deterministic test still green [src/Hexalith.Works/Runtime/WorksHost.cs:82] — resolved 2026-09-08: `WorksDomainEventSubscriptionTests` now resolves `IEventStoreDomainEventHandler<WorkItemSuspended>` from the built host and asserts exactly one `WorkItemSuspendedReminderHandler`.
- [x] [Review][Patch] The new `WorksRecoveryOptions` `ValidateOnStart` chain is never exercised with an invalid value — no test configures one, so the fail-fast guard can be weakened silently, turning a misconfiguration into a permanently and silently disabled recovery pass [src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs:31-36] — resolved 2026-09-08: new `WorksRecoveryOptionsTests` drives `AddWorksReminderAndCascadeRecovery` with invalid configuration for every validated knob and asserts `OptionsValidationException`, plus a valid-configuration and defaults fact.
- [x] [Review][Patch] The dispatcher's new non-Works-domain rejection has no test — every `ProjectionRequest` under `tests/` passes the literal `"work"`, so removing the guard would let a foreign aggregate id into the tenant's authoritative `MemberWorkItemIds` manifest undetected [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:81-84] — resolved 2026-09-08: `PendingDateAwaitIndexDispatcherTests.A_projection_request_for_another_domain_is_refused` dispatches a `party`-domain request and asserts the throw plus zero read-model writes.
- [x] [Review][Patch] `WorkItemSharedProjectionRebuildHandler`'s `ValidateIdentity` mismatch throws, its cross-tenant history refusal, and `ProjectAsync`'s `UnsupportedCapability` result are all untested; all 12 facts hard-code a matching identity [tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs:694] — resolved 2026-09-08: added three facts to `WorkItemSharedProjectionRebuildHandlerTests` — identity mismatch on all three lifecycle entry points (theory over domain/projection-type mismatches), cross-tenant and cross-domain history refusal, and `ProjectAsync` returning `Failed`/`UnsupportedCapability`.
- [x] [Review][Patch] `throw incompleteScan;` resets the captured exception's stack trace, and a throwing `SubmitAsync`/`ScheduleResumeReminderAsync` inside the loop discards the typed incomplete-scan signal entirely — use `ExceptionDispatchInfo.Capture(...).Throw()` and preserve the typed signal [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:107] — resolved 2026-09-08: the clean path rethrows via `ExceptionDispatchInfo.Capture(incompleteScan).Throw()`, and a throwing submit/schedule inside the loop now rethrows the **typed** signal carrying both causes (`new AggregateException(incompleteScan, ex)` as inner) instead of discarding it. The processing loop moved to a private `ProcessAsync` so the catch can wrap it.
- [x] [Review][Patch] `PendingDateAwaitIndex.cs` declares `PendingDateAwaitTenantIndex` — the "one correctly named C# file per type" finding above (line 111) is checked off but only the registry half was split [src/Hexalith.Works/Projections/PendingDateAwaitIndex.cs:17] — resolved 2026-09-08: the file is renamed to `PendingDateAwaitTenantIndex.cs` (git mv), matching its single type.
- [x] [Review][Patch] `WorksEventDecoder.IsKnownEventType` has zero callers anywhere in `src/` or `tests/` — dead code as landed; `PendingDateAwaitStreamReader` and `WorkItemProjectionEventDecoder` each do their own catalog lookup [src/Hexalith.Works/Runtime/WorksEventDecoder.cs:48] — resolved 2026-09-08: `WorksEventDecoder.IsKnownEventType` deleted (zero callers).
- [x] [Review][Patch] `LegacyWhatsNextIndexKey`/`WhatsNextIndexKey` and `LegacyRollUpKey`/`RollUpKey` are two live public names for one key string each, with a byte-identical copy-pasted summary on the roll-up pair; the manifest builder uses the `Legacy*` names while the dispatcher, queries, and cascade source use the others [src/Hexalith.Works/Projections/WorksReadModelKeys.cs:24-46] — resolved 2026-09-08: the `Legacy*` duplicates are deleted; `WhatsNextIndexKey`/`RollUpKey` are the single names, and `WorkItemSharedRebuildManifestBuilder` plus the rebuild tests use them. The class summary no longer refers to `Legacy*`.
- [x] [Review][Patch] The pending-date-await index transform mutates the store's `current` instance in place (`index.LastSequences[...] = `, `index.Entries.Remove(...)`) while the what's-next transform in the same file deliberately builds replacement dictionaries; `ReadModelWritePolicy.UpdateAsync` documents that the update func "must be idempotent — it can run on every retry" [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:459-473] — resolved 2026-09-08: the pending-date-await transform now builds replacement dictionaries like the what's-next transform, so it stays idempotent across `ReadModelWritePolicy` retries instead of mutating the store's `current` instance.
- [x] [Review][Patch] The `WorksHost` comment claims the canonical-byte manifest ceiling "is what actually bounds a rebuild", but `ProjectionDispatchOptions.MaxSharedRebuildCandidateBytes` (1 MiB default, never raised by Works) is checked on every accumulate, long before any manifest is built [src/Hexalith.Works/Runtime/WorksHost.cs:53-59] — resolved 2026-09-08: the comment now names `ProjectionDispatchOptions.MaxSharedRebuildCandidateBytes` (1 MiB default, never raised here, checked on every accumulate) as the first ceiling a large rebuild meets, with the manifest byte ceiling and operation bound as the other two.
- [x] [Review][Patch] `## Story 4.8` is inserted between the Story 4.6 and Story 4.7 sections, breaking the file's chronological order and separating 4.7's subscription surface from the 4.8 text built on it [docs/eventstore-api-surface-constraints.md:196] — resolved 2026-09-08: the `## Story 4.8` section is moved after `## Story 4.7`, restoring chronological order.
- [x] [Review][Patch] `using Dapr;` is placed after `using Hexalith.EventStore.Client.Subscriptions;`, inconsistent with `DaprDateReminderScheduler.cs`, `DateReminderActor.cs`, and `IDateReminderActor.cs` [src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:1-3] — resolved 2026-09-08: `using Dapr;` moved to the first group, matching `DaprDateReminderScheduler.cs`/`DateReminderActor.cs`.
- [x] [Review][Patch] A hardcoded durable-catalog count ("stays 40 after Story 1.5") lives in a source comment; the sibling registry file makes the same point without a number, and this diff already had to rewrite that count across five documents [src/Hexalith.Works/Projections/PendingDateAwaitIndex.cs:15] — resolved 2026-09-08: the hardcoded count is gone — the remark now says the record adds nothing to the durable catalog, like the sibling registry file.
- [x] [Review][Patch] All four facts in `WorksReminderRecoveryPipelineSmokeTests` share one static `Tenant`, so each restart's auto-discovering reconciliation re-folds the other facts' items — the per-run GUID item ids give no tenant-level isolation; separately `MtlsAllowsEventStoreAndDeniesAnUnauthorizedCallerAtWorksProcess` is an authorization test in a reminder-recovery file and the only PascalCase name in a `snake_case_naming` class [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:1] — resolved 2026-09-08: each fact now mints its own per-run tenant (`NewTenant(purpose)`) as well as its own work-item ids, so one fact's auto-discovering reconciliation can never re-fold another's items; the mTLS fact moved to its own `WorksMtlsAuthorizationSmokeTests` class with a snake_case name, and the shared live harness (AppHost start/dispose, gating, dev-JWT submission, stream observation) moved to `WorksAppHostSmokeHarness`.

- [x] [Review][Defer] Startup reminder reconciliation gives up permanently after ~5 s (`ReminderReconciliationMaxAttempts = 5` × `ReminderReconciliationRetryDelayMilliseconds = 1000`, fixed delay, no backoff or jitter, no distinct "gave up" event) and never runs again until the next host restart — a state store or gateway unready longer than that voids AC #3 for a whole host lifetime [src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:37-61] — deferred: pre-existing; already recorded in this story's 2026-09-05 triage log as "unchanged, pre-existing, out-of-scope code this session did not touch". The retry *budget* is new in this diff, so it is worth a dedicated story rather than another round of the same deferral.
- [x] [Review][Defer] The `Projections/SharedRebuild/*` subsystem carries four real defects: `AccumulateAsync` is O(n²) in candidate size (deserialize-all, rebuild, reserialize per aggregate); the candidate retains raw `ProjectionEventDto[]` against a 1 MiB `MaxSharedRebuildCandidateBytes` default, so a tenant rebuild can fail on candidate size well before the 10,000-aggregate bound; `WorkItemSharedRebuildRelationshipGraph.IsRolledTotalUnavailable` recurses once per member with cycle detection but no depth cap, so a deep parent/child chain raises an uncatchable `StackOverflowException`; and `WorkItemSharedRebuildManifestBuilder` throws on one out-of-identity payload instead of marking that aggregate incomplete, aborting the whole tenant rebuild [src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildRelationshipGraph.cs:58-81] — deferred: this subsystem traces to `spec-shared-rollup-reconciliation.md`, not Story 4.8; it arrived in the reviewed range and needs review against its own acceptance criteria.
- [x] [Review][Defer] Recovery has a durability blind window and no backfill: registration happens immediately on the `work.events` subscription, but the index recovery reads is written only by `MaintainPendingDateAwaitIndexAsync` on `ProjectionPollerService`'s refresh-interval cadence, so a crash inside that window leaves the await unindexed; and because the registry is populated only by post-deploy `/project` dispatches, an item suspended before this deploy with no subsequent events is never dispatched, never indexed, and never discovered — the retired `StreamReadingPendingDateAwaitSource` scanned configured tenants directly [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:41-51] — deferred, `maybe-false` on reachability: settled by checking whether `ProjectionPollerService` redispatches every aggregate's full stream on a projection-cursor reset (backfill exists, finding is moot) or only aggregates with events after the cursor (finding is real and medium). The boundary record's Story 4.8 entry documents the growth limitation but says nothing about either window.

**Rejected:**
- `false` — "`Task.Delay` inside the `catch` block escapes `ExecuteAsync`, so the `BackgroundService` faults on shutdown": the escape is real (a C# `catch` clause does not catch exceptions thrown from a sibling `catch` clause of the same `try`), but the named bad outcome does not occur. `stoppingToken` is cancelled only from `StopAsync`, so `_stopCalled` is true, and the async state machine faults the task with an `OperationCanceledException` whose token matches — putting it in the `Canceled` state. `Microsoft.Extensions.Hosting.Internal.Host.TryExecuteBackgroundServiceAsync` returns silently on exactly `_stopCalled && backgroundTask.IsCanceled && ex is OperationCanceledException`, so there is no `BackgroundServiceFaulted` log and no `StopHost`. Reported independently by blind-hunter and edge-case-hunter; same refutation.
- `low`, not worth fixing — "the hardcoded `deadletter.` dead-letter topic prefix is not enforced against EventStore's configurable `EventStore__Publisher__DeadLetterTopicPrefix`": the invariant is real and documented, but `src/Hexalith.Works.AppHost/DaprComponents/pubsub.yaml` is the only deployer and already sets the prefix to `commanddeadletter` with the collision spelled out in its own comments (lines 14-17) and the topic scopes pinned (lines 33-39). Meeting the defect requires an operator to change that prefix, and the fix (a startup validator reading another service's configuration) adds real complexity for a config-file-enforced invariant.
- `low`, not worth fixing — "`PendingDateAwaitTenantIndex`/`PendingDateAwaitTenantRegistry` have no `SchemaVersion` handle, unlike the `WorksWhatsNextTenantIndex` v2 rollout landing alongside them": the unversioned key is a recorded deliberate decision (`WorksReadModelKeys.cs:52-58` — reminder recovery reads the registry independently of the roll-up read-model generation), and the fix adds a version field plus validation surface to two documents that have had no shape change.
- `low`, not worth fixing — "`FailedInvalidPayload => Results.Ok()` means malformed payloads are acknowledged and never reach `deadletter.work.events`, contradicting the operator guide's triage step 2": the ack is the deliberate poison-message protection, and the guide's "malformed or unidentified entries" is still reachable — an envelope that fails model binding never reaches `MapProcessingResult` and does dead-letter through Dapr's retry policy. The guide is not wrong as written.
- rejected — "the File List credits `Program.cs` and the deleted `WorksWhatsNextReadModel.cs` while omitting `WorksHost.cs`, `WorksReadModelKeys.cs`, `WorksWhatsNextTenantIndex.cs`, `WorksWhatsNextTenantIndexValidation.cs`, `PendingDateAwaitScanIncompleteException.cs`, `WorksRecoveryLog.cs`, `WorksDomainEventEndpointExtensions.cs`, and `WorksDomainEventLog.cs`": accurate as filed, but the fix is to edit the File List inside the spec under review, which this workflow does not do. The `WorksHost.cs` half is already acknowledged at line 119.

### Review Findings (2026-09-22, bmad-code-review, incremental `c4d40e5..f1f377f`)

_Scope: the incremental diff of commit `f1f377f` (VG-01/VG-02 test patches), 137 lines, chosen instead of the full `9526c31` baseline diff. Four layers ran: blind-hunter (10 raw), edge-case-hunter (3 raw), verification-gap (clean), acceptance-auditor (4 raw)._

- [x] [Review][Patch] Keycloak credential fact asserts the password by resolved value, not by binding identity — `ResolveAsync` silently falls back to `ToString()` for non-`IValueProvider` values, and `Secret.ShouldBeTrue()` is checked on a parameter looked up by name, independently of the env var. The env value is the `ParameterResource` itself (`WithEventStoreClientCredentials` passes the builder), so assert `environment["EventStore__Authentication__Password"]` is that same secret `ParameterResource` instance (and the username likewise) instead of relying on value equality. [tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs:263-274] — fixed: identity assertions via a `Parameter` helper; `ResolveAsync` now takes an `IValueProvider` (no `ToString()` fallback); focused fact 1/1.
- [x] [Review][Patch] `test-summary.md` still reports non-smoke Integration **550/550** after `f1f377f` added two facts (expected 552); only focused 1/1 runs were recorded — re-run the non-smoke Integration lane and record the actual total. [_bmad-output/implementation-artifacts/tests/test-summary.md:3774] — fixed: lane re-run **552/552**, 0 skipped; recorded in a new test-summary section.
- [x] [Review][Defer] Topology facts never dispose `IDistributedApplicationTestingBuilder` [tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs:246] — deferred, pre-existing: the new fact follows the file-wide pattern (no `await using`) shared by every existing topology fact; each undisposed builder retains its host configuration sources (appsettings reload watchers). Fix belongs file-wide, not in this increment.

**Rejected:**
- `low`, not worth fixing — (blind-hunter + edge-case-hunter + acceptance-auditor) "Keycloak fallback defaults (`tenant-a-user`, random 24-byte password) are untested": true, but the branch is pre-existing, no acceptance lane runs Keycloak mode, and closing it requires a new fact rather than a direct correction.
- `low`, not worth fixing — (acceptance-auditor) "`TokenEndpoint` env var not asserted": emitted by the EventStore submodule's `WithEventStoreClientCredentials`, not Works code; asserting it duplicates the submodule's contract.
- `false` — (blind-hunter) "`works-client-username` parameter name/secret flag unchecked, so a rename would go unnoticed": resolution goes through the env value object, not the name; a rename has no bad outcome and the username is intentionally non-secret.
- `false` — (blind-hunter) "Keycloak test doesn't prove dev symmetric-key vars are absent": `Program.cs` composes them only in the `else` branch of `if (security is not null)`; both branches cannot apply.
- `false` — (blind-hunter) "`Should.ThrowAsync<InvalidOperationException>` could pass on a processor-raised exception": the handler throws on attempt 1 before any post-handler transition, release failures are swallowed by `ReleaseSafelyAsync`, and `attempts == 2` + `Received(2)` pin the injected failure.
- `false` — (blind-hunter) "marker release not asserted directly": without the release, the second delivery returns `RetryableInProgress` (`WorksDomainEventProcessor.cs:88-91`), so asserting `Processed` proves the release.
- `false` — (edge-case-hunter) "with several handlers, already-successful handlers re-run on redelivery": that is the processor's documented at-least-once contract (`DispatchAsync<TEvent>` iterates all handlers); consumed handlers are idempotent by deterministic reminder names/message ids, so no bad outcome.
- `low`, not worth fixing — (blind-hunter) "handler `OperationCanceledException` path untested": it flows through the same bare `catch` → `ReleaseSafelyAsync` path the new fact already exercises.
- `low`, not worth fixing — (acceptance-auditor) "VG-01 uses `WorkItemCancelled` rather than `WorkItemSuspended`": the release-on-failure path is type-agnostic shared processor code.
- `low`, not worth fixing — (blind-hunter / edge-case-hunter) `ResolveAsync` maps null to `""`, `attempts` duplicates `Received(2)`, and indexing throws `KeyNotFoundException` rather than a Shouldly message: diagnostics-only; the facts still fail on regression.
- rejected — (acceptance-auditor) "frontmatter `status: done` contradicts body `in-review` and sprint `review`": real, but the fix edits the spec under review; this workflow's status sync (step 6) realigns frontmatter, body, and sprint status.

## Review Triage Log

_bmad-build step-04 review of the 2026-09-05 code-review-remediation session's own working-tree diff (`{diff_file}` scoped to this session's uncommitted changes, not the full since-`baseline_commit` history — the literal full-history diff since story creation spans 590K+ unrelated lines across other stories/epics and was judged unreviewable; this session's incremental delta is the only genuinely new, previously-unreviewed material)._

| Layer | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| verification-gap | The changed real-child fail-safe branch was not exercised by a passing test. | medium | The fact proves the child exited before `finally`; its former one-off second kill/wait ran only after an earlier assertion failure. A regression there could leak the retained child into later integration tests. | patch — route retained-handle cleanup through the already injectable `ProcessSchedulerVolumeProbe`; its false-first-wait fact requires two 5000 ms waits and confirmed exit. |
| verification-gap | Frontmatter, body, and sprint tracking reported different lifecycle states. | low | Frontmatter was `in-review` while the legacy body and sprint board still read `in-progress`, despite the close-out claiming readiness for review. | patch — retain the workflow frontmatter vocabulary and synchronize the body/sprint vocabulary to `review`. |
| edge-case | Frontmatter entered review while the body and sprint tracking remained in progress. | low | Same mismatch independently observed by the edge-case layer; tools reading different artifacts would route the story differently. | patch — grouped with the verification-gap lifecycle finding. |
| edge-case | A non-`InvalidOperationException` from the real-child fail-safe kill/wait could escape the ad-hoc cleanup and leave the child running. | medium | The one-off `finally` caught only `InvalidOperationException`; `AggregateException`, `Win32Exception`, and `NotSupportedException` are supported process-lifecycle failures already handled by the adapter. | patch — remove the one-off lifecycle and reuse the two-attempt adapter with its classified failure policy. |

_2026-09-20 File List increment close-out review: edge-case and verification-gap layers reviewed the current
working-tree delta; blind-hunter was skipped because the four-agent session cap left only two simultaneous
reviewer slots._

- **[verification-gap] The IPv6 test called only the error classifier, so deleting the production bind-path catch still left it green.** Verdict: `medium`. Confirmed by the layer's mutation demonstration; on an IPv6-disabled host the live lane would wait through every boundary and reject the topology. → **patch**: the production port probe now delegates the exclusive bind through an injectable action, and the test drives that exact catch path for four unavailable-family errors plus genuine IPv6/IPv4 conflicts.
- **[verification-gap] The Docker timeout facts injected a completed `TimeoutException` and never exercised cancellation, child termination, redirected-read observation, or the termination bound.** Verdict: `medium`. Confirmed by the layer's mutation demonstration; removing the real process deadline left all four prior facts green and could hang a live boundary. → **patch**: the production process lifecycle now sits behind an injectable probe, and a deliberately non-completing fake proves deadline cancellation, `Kill`, both read observations, the bounded second exit wait, and the final classified timeout.
- **[edge-case-hunter] A probe deadline could cancel redirected reads just after `WaitForExitAsync` succeeded, letting the local `OperationCanceledException` escape the outer retry.** Verdict: `medium`. Confirmed: only the exit wait was inside the local-timeout catch; both stream awaits were outside it. → **patch**: exit and stream completion now share the same probe-local cancellation boundary.
- **[edge-case-hunter] `Process.Kill` can throw `Win32Exception` or `NotSupportedException`, which escaped instead of producing a bounded retry diagnostic.** Verdict: `medium`. Confirmed from the actual `Process` wrapper path (the reported `AggregateException` arm was not applicable to the synchronous API). → **patch**: termination failures are retained, both stream tasks are observed, the bounded termination wait still runs, and the outer loop receives `InvalidOperationException` for retry/diagnosis.
- **[edge-case-hunter] Disabled IPv6 can report `ProtocolFamilyNotSupported` or `OperationNotSupported`, neither of which was classified as inapplicable.** Verdict: `medium` for `ProtocolFamilyNotSupported`; the `OperationNotSupported` half is `false` because it is not uniquely an unavailable address-family signal and treating it as available could hide a real exclusive-bind failure. → **patch**: added only `ProtocolFamilyNotSupported` and pinned it through the production classifier.
- **[edge-case-hunter] A backward wall-clock adjustment could extend the nominal 60-second resource-release budget.** Verdict: `low`. Confirmed: the loop compared `DateTimeOffset.UtcNow` with a wall-clock deadline. The current change exposes this as a bounded settling contract and the correction is direct. → **patch**: the release budget now uses monotonic `Stopwatch.Elapsed`.

- **[blind-hunter] ArchitectureTests count jumped from the story-spec baseline (44) to 236 with only one ArchitectureTests file touched in this diff.** Verdict: `false`. The growth (44→207→236) happened across this story's earlier, already-committed and already-reviewed rounds (2026-07-23 through 2026-09-01); this session's diff only shows its own incremental delta. Directly ran `Hexalith.Works.ArchitectureTests`: 236/236, matching the claim exactly.
- **[blind-hunter] UnitTests count jumped from 496 to 529 with zero UnitTests files touched in this diff.** Verdict: `false`. Same cause as above — the growth predates this session. Directly ran `Hexalith.Works.UnitTests`: 529/529, matching the claim exactly.
- **[blind-hunter + edge-case-hunter, grouped] `DateReminderReconciler.ReconcileAsync`'s incomplete-scan rethrow path discards pass-level context: the reissued/rescheduled outcome counts are never surfaced when the `incompleteScan` rethrow fires, and if `_submitter`/`_scheduler` also throws while acting on the partial results, the original `PendingDateAwaitScanIncompleteException` (tenant count, cause) is discarded entirely in favor of the new exception.** Verdict: `low`. Confirmed by reading `DateReminderReconciler.cs:36-103`: `throw incompleteScan;` is reached only if the tenant loop completes without its own exception, and the method never returns `reissued`/`rescheduled` alongside a rethrow. No functional harm — `ReminderReconciliationService` already retries on any exception and idempotency makes a repeat pass safe; the per-tenant failure reason is also already logged unconditionally inside `IndexedPendingDateAwaitSource` at the point of failure. Purely an aggregate-diagnostic gap at the reconciler layer. → **patch**.
- **[blind-hunter] `sprint-status.yaml`'s `last_updated` comment still reads "... is dead code, unresolved" even though this same diff's Review Findings section resolves that exact finding.** Verdict: `low`. Confirmed: the diff only normalized line-endings on this file, leaving the comment text unchanged and now stale/self-contradictory next to the story's own Review Findings resolution. → **patch**.
- **[blind-hunter] The new "Accepted limitation — unbounded index/registry growth" entry in `docs/boundary-decision-record.md` gets no DW-number cross-reference in `deferred-work.md`, unlike DW-84/DW-56.** Verdict: `low`, rejected. No established repo convention requires a DW-number for a permanently-accepted architectural limitation (as opposed to a deferred fix) — grepped `boundary-decision-record.md` for other "accepted limitation" precedent and found none either way. Low-impact traceability nicety, not a defect.
- **[blind-hunter] `test-summary.md` uses `-class- "*SmokeTests"` in one place and `-class *SmokeTests` in another for "the identical binary and filter."** Verdict: `false`. These are deliberately opposite-polarity xUnit v3 filters, not a typo: `-class-` excludes smoke-test classes (the deterministic-only run) and `-class` (no trailing hyphen) includes only smoke-test classes (the Tier-3 live-lane attempt). Verified by running both directly: `-class "*SmokeTests"` selected 24 live-lane tests (3 failed on the known, already-open Aspire/Dapr sandbox hang, since live containers happened to be up during this review); `-class- "*SmokeTests"` selected 268 deterministic tests, all green — exactly matching the story's own claimed counts for each purpose.
- **[blind-hunter] `deferred-work.md`'s tightened DW-53 citation (`12-26,41-72`) never explains what lines `41-72` are, though the story file's own resolution bullet calls it "the shared helper."** Verdict: `low`. Confirmed: `deferred-work.md`'s DW-53 entry carries no explanation for the `41-72` sub-range, unlike the spec file's own note. → **patch**.
- **[blind-hunter] `PendingDateAwaitScanIncompleteException` was made `internal`, citing an `InternalsVisibleTo` to `Hexalith.Works.IntegrationTests` that "no ... csproj change appears anywhere in this diff."** Verdict: `false`. Grepped `src/Hexalith.Works/Hexalith.Works.csproj`: `<InternalsVisibleTo Include="Hexalith.Works.IntegrationTests" />` already exists there, pre-dating this diff — the claim is verifiable, just not from the diff alone.
- **[blind-hunter] New test calls `Story48Streams.PageAt(TenantA, WorkFuture, 1, isTruncated: true)`, but `Story48Streams.cs` isn't touched in this diff, risking a missing overload / compile failure.** Verdict: `false`. Ran the full solution Release build directly: 0 warnings, 0 errors, including `Hexalith.Works.IntegrationTests`, proving the overload already exists and the call compiles.
- **[blind-hunter] The resolution note for the per-tenant-isolation finding reads "as if the whole finding is closed" while `ReminderReconciliationService`'s permanent give-up after exhausted startup retries is left unaddressed.** Verdict: `false`. Re-read the resolution note directly: it describes only the per-tenant-isolation fix and does not claim to change `ReminderReconciliationService`'s retry-exhaustion behavior, which is unchanged, pre-existing, out-of-scope code this session did not touch.
- **[blind-hunter, grouped with the `DateReminderReconciler` entry above] No log call surfaces the caught exception's `FailedTenantCount`/`PartialResults.Count` at the reconciler layer — only the per-tenant warning was added.** Verdict: `low`. Same root cause and disposition as the grouped `DateReminderReconciler` entry above. → **patch**.
- **[blind-hunter] `WorksEventIdentity.Matches`'s "every non-rejection event carries `AggregateId`" closed-world assumption lives only in a code comment, with no fitness/architecture test enforcing it against a future violating type.** Verdict: `low`, rejected. Real but unlikely to be hit in ordinary development (would require adding a wholly new non-rejection payload type without an `AggregateId` property), and the smallest fix (a new reflection-based fitness test) is more than a direct correction.
- **[edge-case-hunter, grouped with the `DateReminderReconciler` entry above] If `_submitter`/`_scheduler` throws while processing partial results, the caught `PendingDateAwaitScanIncompleteException` is discarded in favor of the new exception.** Verdict: `low`. Same root cause and disposition as the grouped `DateReminderReconciler` entry above. → **patch**.
- **[verification-gap] No findings.** The layer traced every behavioral change in the diff to a passing test and reported nothing to triage.

_2026-09-20 final baseline-diff review after the AppHost harness close-out and one-type-per-file correction.
All three layers ran against the full `9526c31` baseline diff; findings outside the current increment were
classified as pre-existing rather than silently folded into the harness patch._

- **[blind-hunter] Frontmatter says `in-review` while the legacy body and sprint vocabulary say `review`.** Verdict: `low`, rejected. The mismatch is real, but `in-review` is the current workflow's required machine-readable frontmatter state and the only correction proposed is an edit to the spec under review, which this review must reject.
- **[blind-hunter] Startup reminder reconciliation permanently stops after its bounded retry budget.** Verdict: `medium` → **defer**. Confirmed in `ReminderReconciliationService`: after the configured attempts it returns and has no periodic retry or distinct readiness state, so a dependency outage lasting beyond startup can defer recovery until restart; this predates the current harness increment.
- **[blind-hunter] An empty tenant registry can make startup reconciliation finish before a later index/backfill appears.** Verdict: `maybe-false` → **defer**. The source returns success for an empty registry and the service is single-run, but reachability depends on the EventStore projection poller's backfill/reset semantics; settle by proving whether pre-index aggregates are replayed after the reconciliation service exits.
- **[blind-hunter] A parked projection candidate is skipped without reading its authoritative stream and has no supported unpark path.** Verdict: `medium` → **defer**. Confirmed by the parked branch in `IndexedPendingDateAwaitSource`; a stale or mistaken durable park suppresses reminder recovery until an operator mechanism exists, and this is pre-existing story behavior.
- **[blind-hunter] Exact caller cancellation during an in-tenant parking/stream read can discard that tenant's already-collected local evidence.** Verdict: `medium` → **defer**. Confirmed and explicitly documented in the source remarks; only cross-tenant evidence preservation was closed by the prior human decision.
- **[blind-hunter] The reconciler aborts after the first submit/schedule failure and can starve later awaits.** Verdict: `medium` → **defer**. `ProcessAsync` rethrows from either branch before visiting later entries; an earlier persistent failure can consume every startup retry.
- **[blind-hunter] The due-now submit branch lacks bounded tenant/work-item/reminder failure telemetry.** Verdict: `medium` → **defer**. Confirmed: it reaches generic EventId 4603 with an attached gateway exception, while only the scheduler branch emits identity-bearing 4609; this asymmetry is pre-existing.
- **[blind-hunter] The actor removes reminder state after command acceptance rather than terminal command success.** Verdict: `medium` → **defer**. `EventStoreGatewayWorkCommandSubmitter` returns the gateway's accepted response without polling terminal status, after which the actor removes its registration; a later terminal failure has no in-process retry trigger.
- **[blind-hunter] Actor remoting cannot observe cancellation once `ScheduleResumeAsync` starts.** Verdict: `maybe-false` → **defer**. The token is checked only before the proxy call, but the practical bound depends on Dapr actor-client timeout behavior not established by this diff; settle with the configured remoting timeout or a stalled-proxy integration proof.
- **[blind-hunter] The tenant registry and historical per-item watermarks grow without compaction.** Verdict: `low`, rejected. The registry is deliberately append-only and index writes are already bounded to items with date-await history; ordinary impact is low and compaction/sharding adds a migration policy rather than a direct correction.
- **[blind-hunter] A probe plus termination wait and retry delay can exceed the nominal release-wait duration.** Verdict: `low`, rejected. The monotonic loop budget is real but checked between bounded observations, and teardown already supplies an extra bounded allowance; enforcing one aggregate deadline requires more cancellation plumbing for a rare test-harness failure path.
- **[blind-hunter] Redirected process reads are awaited before the termination wait and might ignore cancellation indefinitely.** Verdict: `maybe-false` → **defer**. The production implementation uses `StreamReader.ReadToEndAsync(token)` and kills the process first, but the diff does not prove every platform completes those tasks; settle with a real child-process test whose pipe survives kill/cancellation.
- **[blind-hunter] Pipe I/O/disposal faults can mask the intended Docker timeout and prevent observation of the sibling read task.** Verdict: `medium` → **patch, resolved**. Confirmed: `ObserveProbeTaskAsync` caught only cancellation and the calls were sequential. The cleanup now observes both reads concurrently and absorbs only expected cancellation, `IOException`, and `ObjectDisposedException`; a focused pipe-close regression preserves the classified timeout.
- **[blind-hunter] Timed-out AppHost disposal tasks continue after `WaitAsync` abandons them.** Verdict: `low`, rejected. The rare cleanup-timeout path deliberately continues to bounded resource diagnostics; retaining/canceling non-cancelable disposals adds lifecycle complexity for negligible additional test-only harm.
- **[blind-hunter] `CountResumedAsync` reads only the first 100 stream events.** Verdict: `false`. Every caller uses a newly created per-run work item and exercises only a small fixed command sequence, so none can reach a 100-event stream in the acceptance lane under review.
- **[blind-hunter] A three-second settle interval cannot prove no late duplicate resume.** Verdict: `false`. Redeliveries reuse the deterministic command identity and the aggregate no-ops a consumed/non-matching await, so a late retry cannot create a second accepted `WorkItemResumed`; the stream assertion checks accepted events, not callbacks.
- **[verification-gap] The Docker command-shape fact omits the ordered `--filter`/`--format` pairs.** Verdict: `medium` → **patch, resolved**. The layer demonstrated that deleting `--filter` left the prior fact green while Docker failed and all four live facts became prerequisite skips. The regression now pins the complete ordered `ArgumentList`.
- **[edge-case-hunter] Projection replay does not reject duplicate or gapped positive sequence numbers.** Verdict: `maybe-false` → **defer**. The endpoint sorts the delivered replay but does not validate contiguity; settle by proving whether the EventStore projection contract can emit a duplicate/gap (otherwise the trigger is unreachable).
- **[edge-case-hunter] A non-truncated stream page is trusted even when metadata claims an unread tail.** Verdict: `maybe-false` → **defer**. The reader stops on `IsTruncated == false`; settle by establishing the gateway's `LatestSequence`/`LastSequenceReturned` invariant or a malformed-page test showing this cross-service shape is reachable.
- **[edge-case-hunter] Paging trusts `LastSequenceReturned` even when it exceeds the highest returned event.** Verdict: `maybe-false` → **defer**. Such metadata would skip events on the next exclusive cursor, but the diff does not establish that the EventStore gateway can produce the inconsistent page; settle with contract evidence or a fault-injection test.
- **[edge-case-hunter] A reconciler failure starves later pending awaits.** Verdict: `medium` → **defer**, grouped with the blind-hunter fail-fast reconciler finding above; both describe the same `ProcessAsync` early-exit behavior.
- **[edge-case-hunter] A steady-state handler failure on one date await starves later awaits on the same suspension.** Verdict: `medium` → **defer**, grouped with the fail-fast reminder-processing family. The handler rethrows immediately to obtain redelivery, so a permanently failing first await prevents later registrations.
- **[edge-case-hunter claim] The task text says paging advances by `LastSequenceReturned + 1`, while the implementation correctly uses the exclusive lower bound unchanged.** Verdict: `low`, rejected. The claim mismatch is real, but source/submodule evidence proves the implementation is correct and the proposed correction edits only this spec.
- **[edge-case-hunter claim] The task text says only the pending-await source changes while the reconciler now acts on partial results and rethrows.** Verdict: `low`, rejected. The behavior is intentional and documented by later review decisions; the remaining inconsistency is solely in the spec under review.
- **[edge-case-hunter claim] A test-task bullet says scheduler failure does not fail dispatch, while the selected 4.7 event-handler path rethrows for redelivery.** Verdict: `low`, rejected. Task 2 explicitly requires rethrow on the selected subscription surface and the runtime behavior is correct; only stale alternative-path wording in this spec disagrees.
- **[edge-case-hunter claim] The stale-reminder note names the actor orphan path, but stored stale registrations submit an idempotent no-op before cleanup.** Verdict: `low`, rejected. Runtime behavior remains safe and intentional; correcting the wording would edit the spec under review.
- **[edge-case-hunter claim] The task says last out-of-order dispatch wins, while the index uses a monotonic highest-sequence watermark.** Verdict: `low`, rejected. The implementation prevents an older replay from resurrecting cleared state and is correct; the finding asks only to rewrite this spec's obsolete wording.

_2026-09-20 bmad-build full-baseline review after the spec-10 five-patch close-out. All 23 raw findings
were classified before grouping. Repeated claims retain their prior verdict and route with carried evidence._

- **[blind-hunter] Re-suspending one item on the same date instant within EventStore's 24-hour command-status retention reuses the earlier `DateResume` message id.** Verdict: `medium` → **defer**. Confirmed from `DateResume` and `EventStoreGatewayWorkCommandSubmitter`; the second legitimate occurrence can be substrate-deduplicated. This identity design predates Story 4.8's baseline and requires an occurrence/sequence identity across reminder records, names, and submissions rather than a close-out patch.
- **[blind-hunter] `DateReminderReconciler` snapshots `now` once, so later future awaits receive a relative delay that includes time already spent on earlier candidates.** Verdict: `medium` → **defer**. Confirmed, but carried code history shows the behavior already existed at baseline; a large pass can delay a reminder by its own scan duration.
- **[blind-hunter] The reconciler aborts after the first submit or schedule failure and can starve later awaits.** Verdict: `medium` → **defer**. Carried: `ProcessAsync` still exits on the first failure exactly as the earlier triage row records; do not verify or defer it again.
- **[blind-hunter] The steady-state handler aborts after the first schedule failure and can starve later awaits in the same suspension.** Verdict: `medium` → **defer**. Carried: the earlier fail-fast reminder-processing row already records this behavior and route.
- **[blind-hunter] Registry-before-index ordering can let startup reconciliation observe an empty index and finish before a later index write appears.** Verdict: `maybe-false` → **defer**. Carried: the prior empty-registry/backfill row records the same reachability question; projection-poller replay semantics are still the evidence needed to settle it.
- **[blind-hunter] Startup reconciliation permanently stops after its bounded retry budget.** Verdict: `medium` → **defer**. Carried: `ReminderReconciliationService` still returns after the final attempt, matching the existing triage and deferred-work entry.
- **[blind-hunter] Parked reminder candidates have no supported unpark/replay path.** Verdict: `medium` → **defer**. Carried: the parked branch remains unchanged and the canonical unpark/replay entry already owns this work.
- **[blind-hunter] Duplicate identical `DateReached` conditions survive projection folding.** Verdict: `low` → **defer**. Confirmed and pre-existing at baseline: duplicates produce repeated schedule/submit attempts and inflated counts, although deterministic names and message ids keep accepted outcomes idempotent. Normalization belongs with the durable await-set contract.
- **[blind-hunter] Early resume leaves stale actor/reminder state until each future reminder fires.** Verdict: `low`, rejected. The behavior is real but explicitly accepted by the Story 4.8 task and inherited from Story 4.6; proactive unregistration needs new event-consumption/state-cleanup machinery for a low ordinary-use impact.
- **[blind-hunter] Reserving tenant id `tenants` is avoidable by migrating the registry key.** Verdict: `low`, rejected. The collision is real, but the human explicitly selected host-edge rejection and preservation of the durable registry key; a registry-key migration is not a direct correction.
- **[blind-hunter] Mixed-case `TENANTS` bypasses the raw ordinal projection guard before canonicalization.** Verdict: `low` → **defer**. Carried: this direct-`/project`-only hole and its lowercasing path remain exactly as the prior triage/deferred rows describe.
- **[blind-hunter] Local EventStore and Admin Dapr configurations remain allow-by-default.** Verdict: `medium` → **defer**. Confirmed, but both allow policies predate the baseline and are explicitly labeled local-development only; Story 4.8 added mTLS while preserving the existing Works receiver's deny-by-default policy.
- **[blind-hunter] The Works caller receives a broad EventStore `POST /**` allow policy.** Verdict: `medium` → **defer**. Confirmed and pre-existing; narrowing it requires enumerating the EventStore command/read routes needed by every Works recovery path and belongs with the local EventStore ACL hardening above.
- **[blind-hunter] Child-completion stream paging forwards the rejected continuation token and advances the exclusive cursor by one too many.** Verdict: `high` → **defer**. Carried: DW-86 already owns this exact Story 4.7 paging defect; the code is unchanged.
- **[blind-hunter] Child-completion stream reads do not validate domain identity and silently ignore undecodable lifecycle evidence.** Verdict: `medium` → **defer**. Confirmed in the pre-existing Story 4.7 reader; wrong-domain or malformed evidence can produce a false parent-resume decision.
- **[blind-hunter] Cascade descendant reads do not validate page/payload identity and silently ignore malformed child evidence.** Verdict: `medium` → **defer**. Confirmed in the pre-existing cascade reader; foreign or missing child evidence can make a durable checkpoint incomplete.
- **[edge-case-hunter] Mixed-case `TENANTS` can poison registry state after partial projection writes.** Verdict: `low` → **defer**. Carried: same location and canonicalization claim as the blind finding and prior direct-`/project` row.
- **[edge-case-hunter] A stream response older than the index/trigger watermark can be accepted as current truth.** Verdict: `maybe-false` → **defer**. Carried into the existing malformed-metadata inquiry: the reader has no minimum watermark, but reachability requires gateway contract or fault-injection evidence showing an append-only stream can regress.
- **[edge-case-hunter] A non-truncated page can omit events while metadata advertises a higher latest sequence.** Verdict: `maybe-false` → **defer**. Carried: the prior non-truncated unread-tail row already records the same invariant question and route.
- **[edge-case-hunter] One reconciler failure starves later tenants' awaits.** Verdict: `medium` → **defer**. Carried: identical root cause and route as the blind reconciler fail-fast finding.
- **[verification-gap] `ProcessSchedulerVolumeProbe` has no throwing kill/wait delegate coverage for its supported exception classification.** Verdict: `medium` → **patch, resolved**. The adapter facts now inject every supported exception type into kill and wait delegates, require two 5000 ms attempts, verify disposal, and require `SchedulerVolumeProbeCleanupException` when exit remains unconfirmed.
- **[verification-gap] Termination-phase wait and `HasExited` exception handling are untested.** Verdict: `medium` → **patch, resolved**. The fake now supports call-indexed wait/state failures; focused facts reach both cleanup catches and preserve exact caller-cancellation precedence.
- **[verification-gap] Uncancelled ordinary wrapper-disposal failures, alone or combined with a probe failure, are untested.** Verdict: `medium` → **patch, resolved**. Focused wrapper facts require ordinary cleanup classification and preserve both probe and disposal causes.

_2026-09-21 bmad-build review after the spec-11 bookkeeping close-out. All three layers reviewed the full
`9526c31` baseline diff. Repeated findings retain their earlier verdict and route with carried evidence._

- **[blind-hunter BH-01] Startup reminder reconciliation permanently stops after its bounded retry budget.** Verdict: `medium` → **defer**. Carried: `ReminderReconciliationService` still returns after the final attempt, exactly as the earlier baseline-diff triage and deferred entry record; do not defer it again.
- **[blind-hunter BH-02] A missing or empty tenant registry can let startup reconciliation finish before projection indexing/backfill appears.** Verdict: `maybe-false` → **defer**. Carried: the earlier empty-registry/backfill row records the same reachability question; projection-poller replay semantics are still the evidence needed to settle it.
- **[blind-hunter BH-03] A parked candidate is treated as a clean skip even though there is no supported unpark/replay path.** Verdict: `medium` → **defer**. Carried: the parked branch and terminal-disposition behavior are unchanged, and the canonical unpark/replay entry already owns this work.
- **[blind-hunter BH-04] Exact caller cancellation inside a tenant can discard that tenant's already-collected partial evidence.** Verdict: `medium` → **defer**. Carried: the source remarks deliberately preserve this in-tenant limitation and the earlier triage/deferred row records it.
- **[blind-hunter BH-05] A first submit or schedule failure can starve later awaits in the reconciler or steady-state handler.** Verdict: `medium` → **defer**. Carried: both cited loops still fail fast exactly as the earlier reconciler and handler rows record; do not defer either again.
- **[blind-hunter BH-06] The due-now resume-submit branch lacks identity-bearing failure telemetry.** Verdict: `medium` → **defer**. Carried: the existing recovery-diagnostics decision records the generic 4603 fallback and intentionally defers a new event surface.
- **[blind-hunter BH-07] The actor removes reminder state after gateway acceptance rather than terminal command success.** Verdict: `medium` → **defer**. Carried: the gateway submitter and actor cleanup behavior are unchanged, and the prior baseline review already records this retry gap.
- **[blind-hunter BH-08] Re-suspending an item at the same instant can reuse the earlier `DateResume` message identity.** Verdict: `medium` → **defer**. Carried: the existing same-instant identity row records the required occurrence/sequence redesign; do not defer it again.
- **[blind-hunter BH-09] The reconciler snapshots `now` once, so later schedules include time spent processing earlier candidates.** Verdict: `medium` → **defer**. Carried: the implementation is unchanged and the prior stale-delay row already owns this behavior.
- **[blind-hunter BH-10] Duplicate identical `DateReached` conditions survive folding and cause repeated work.** Verdict: `low` → **defer**. Carried: deterministic names and command ids keep accepted outcomes idempotent, while the prior row assigns normalization to the durable await-set contract.
- **[blind-hunter BH-11] Skipping an unknown projection event while advancing the raw-sequence watermark can prevent a later decoder upgrade from repairing the pending-await index.** Verdict: `medium` → **defer**. Confirmed: `MaintainPendingDateAwaitIndexAsync` persists `MaxSequence(events)` while folding only decoded events, and its `storedLastSequence >= incomingLastSequence` guard rejects the same replay after an upgrade; this predates the current bookkeeping increment.
- **[blind-hunter BH-12] Mixed-case `TENANTS` can pass the ordinal projection guard before canonicalization.** Verdict: `low` → **defer**. Carried: the direct-`/project` normalization hole remains exactly as the prior triage and deferred entries describe.
- **[blind-hunter BH-13] Stream paging does not independently validate sequence/metadata consistency.** Verdict: `maybe-false` → **defer**. Carried: the earlier duplicate/gap, unread-tail, and `LastSequenceReturned` rows record the same gateway-contract uncertainty and the evidence needed to settle it.
- **[blind-hunter BH-14] The tenant registry and per-item watermark collections grow without compaction.** Verdict: `low`, rejected. Carried: growth is deliberate and ordinary impact remains low; sharding or rebuild-safe pruning requires migration policy rather than a direct correction.
- **[verification-gap VG-01] Credential retry and bounded fail-closed behavior have only an immediate-success topology test.** Verdict: `medium` → **patch, resolved**. Added a bounded internal credential-read seam plus deterministic retry-success and three-attempt exhaustion facts; the focused two-class lane passed 46/46 with zero skips.
- **[verification-gap VG-02] Stream refolding has no test proving that an unknown non-state event is ignored beside a valid suspension.** Verdict: `medium` → **patch, resolved**. Added a focused source regression with an unknown informational event beside a valid suspension; the pending await remains discoverable and the focused lane passed 46/46.
- **[edge-case-hunter EC-01] A stream response older than the delivered suspension can be accepted as current truth.** Verdict: `maybe-false` → **defer**. Carried: the earlier minimum-watermark row records the same missing trigger/index watermark and still requires gateway consistency evidence or fault injection to establish reachability.
- **[edge-case-hunter EC-02] The reconciler's one-time `now` snapshot can make later reminders fire late.** Verdict: `medium` → **defer**. Carried: this is the same location and consequence as BH-09 and the prior stale-delay row.
- **[edge-case-hunter EC-03] A first reconciler failure can prevent healthy later awaits from being processed.** Verdict: `medium` → **defer**. Carried: despite the reviewer's non-current guard snippet, the cited loop still exits on the first exception and the earlier fail-fast row owns the defect.
- **[edge-case-hunter EC-04] The steady-state handler snapshots `now` once, so later registrations include latency from earlier actor calls.** Verdict: `medium` → **defer**. Confirmed in `WorkItemSuspendedReminderHandler`: every due time is computed from one pre-loop timestamp; this is pre-existing and belongs with the stale-delay family.
- **[edge-case-hunter EC-05] Cancellation cannot bound an actor scheduling call once remoting has started.** Verdict: `maybe-false` → **defer**. Carried: the actual scheduler checks the token before the proxy call and has no `WaitAsync`; practical harm still depends on the unestablished Dapr remoting timeout, exactly as the earlier row records.
- **[edge-case-hunter EC-06] Early resume or termination leaves stale reminder state until its original due time.** Verdict: `low`, rejected. Carried: Story 4.8 explicitly accepts the idempotent stale-reminder posture, and proactive cleanup adds event-consumption/state machinery for low ordinary impact.
- **[edge-case-hunter EC-07] A tenant's historical pending-await index can eventually exceed one state document.** Verdict: `low`, rejected. Carried: index growth is deliberately bounded to date-await history, and compaction/sharding requires migration policy rather than a direct correction.
- **[edge-case-hunter EC-08] The append-only tenant registry can eventually exceed one state document.** Verdict: `low`, rejected. Carried with EC-07/BH-14: the scenario is low-impact in ordinary use and the proposed remedy is a durable schema redesign.
- **[edge-case-hunter EC-09] Configuring the per-aggregate page budget near `int.MaxValue` can make a malformed scan extremely long.** Verdict: `low`, rejected. The outcome requires an extreme operator override plus a gateway that remains truncated indefinitely; imposing an arbitrary upper limit adds policy and compatibility surface for a negligible ordinary-use risk.

_2026-09-21 final full-baseline review after the four-patch close-out. All three layers reviewed the
`9526c31` baseline diff. Every raw finding is classified below before grouping; repeated findings retain
their earlier verdict and route with carried evidence._

- **[blind-hunter BH-01] Startup reconciliation stops permanently after its bounded retry budget.** Verdict: `medium` → **defer**. Carried: the same `ReminderReconciliationService` location and give-up behavior remain exactly as the earlier triage and canonical deferred entry record; do not defer it again.
- **[blind-hunter BH-02] An empty registry/index window can let startup reconciliation finish before backfill appears.** Verdict: `maybe-false` → **defer**. Carried: the existing empty-registry/backfill row owns the same reachability question and still requires projection-poller replay evidence.
- **[blind-hunter BH-03] Parked candidates are skipped without an unpark/replay path.** Verdict: `medium` → **defer**. Carried: the unchanged parked branch and terminal-disposition behavior are already logged and documented; do not defer them again.
- **[blind-hunter BH-04] One reconciler submit/schedule failure starves later awaits.** Verdict: `medium` → **defer**. Carried: `ProcessAsync` still exits on the first failure exactly as the earlier fail-fast row records.
- **[blind-hunter BH-05] One steady-state scheduling failure starves later awaits in the suspension.** Verdict: `medium` → **defer**. Carried: the earlier handler fail-fast row owns this unchanged behavior.
- **[blind-hunter BH-06] The reconciler's one-time clock snapshot delays later reminders.** Verdict: `medium` → **defer**. Carried: the existing reconciliation stale-delay family entry owns this unchanged behavior.
- **[blind-hunter BH-07] The actor removes reminder state after gateway acceptance rather than terminal command success.** Verdict: `medium` → **defer**. Carried: the earlier actor-cleanup retry-gap row records this same code and outcome.
- **[blind-hunter BH-08] Re-suspending at the same instant reuses the earlier resume identity.** Verdict: `medium` → **defer**. Carried: the canonical same-instant occurrence-identity entry already owns the required redesign.
- **[blind-hunter BH-09] Recovery does not enforce the index's minimum stream watermark.** Verdict: `maybe-false` → **defer**. Carried: the existing lagging-stream/minimum-watermark row records the same missing proof and the gateway evidence needed to settle reachability.
- **[blind-hunter BH-10] Stream folding does not independently reject duplicate/gapped sequences or an advertised unread tail.** Verdict: `maybe-false` → **defer**. Carried: the earlier sequence/metadata-consistency rows own both claims and still require gateway-contract evidence or fault injection.
- **[blind-hunter BH-11] Actor remoting cannot observe cancellation after scheduling starts.** Verdict: `maybe-false` → **defer**. Carried: the earlier remoting-timeout row owns the same location and uncertainty.
- **[blind-hunter BH-12] Mixed-case `TENANTS` bypasses the raw ordinal reserved-id guard.** Verdict: `low` → **defer**. Carried: the direct-`/project` normalization hole is unchanged and already logged.
- **[blind-hunter BH-13] Duplicate identical date conditions survive folding and cause redundant calls.** Verdict: `low` → **defer**. Carried: deterministic identities bound accepted outcomes, while the existing durable-await normalization entry owns the redundant work.
- **[blind-hunter BH-14] Host 1 can stop after reminder/index side effects but before the suspension subscription marker completes.** Verdict: `medium` → **patch, resolved**. The live proof now reads the exact `WorkItemSuspended` event message id and waits for its durable consumer marker to reach `Completed` before deleting either recovery reminder or stopping Host 1.
- **[blind-hunter BH-15] Host 3 counts the pre-existing resume immediately and does not observe completion of its startup reconciliation pass.** Verdict: `medium` → **patch, resolved**. A separate future-await canary is marker-complete on Host 1, re-registered and deleted on Host 2, then required to be re-registered by Host 3 before the no-duplicate assertion; the exact three-host fact passed **1/1**, zero skipped, in **380.665s**.
- **[blind-hunter BH-16] The task text still says `LastSequenceReturned + 1` although the cursor is exclusive.** Verdict: `low`, rejected. Carried: the implementation correctly uses the unchanged exclusive lower bound, and the proposed correction edits only this build's spec.
- **[verification-gap VG-01] Whitespace-only Sentry credential content has no fail-closed regression.** Verdict: `medium` → **patch, resolved**. The credential exhaustion fact is now a theory over empty and whitespace-only content and preserves the exact attempt budget, delay sequence, diagnostic, and null inner exception; the topology class passed **18/18**.
- **[edge-case-hunter EC-01] Mixed-case `TENANTS` can poison registry state after partial projection writes.** Verdict: `low` → **defer**. Carried: this is the same location and canonicalization claim as BH-12 and the earlier direct-`/project` row.
- **[edge-case-hunter EC-02] The reconciler's one-time clock snapshot can make later awaits late.** Verdict: `medium` → **defer**. Carried: same location and consequence as BH-06 and the canonical reconciliation stale-delay entry.
- **[edge-case-hunter EC-03] The handler's one-time clock snapshot can make later registrations late.** Verdict: `medium` → **defer**. Carried: the existing steady-state stale-delay sibling entry owns this unchanged behavior.
- **[edge-case-hunter EC-04] A truncated page can advertise a cursor beyond its highest returned event.** Verdict: `maybe-false` → **defer**. Carried: the earlier cursor-consistency row records the same claim and the gateway/fault-injection evidence needed to settle it.
- **[edge-case-hunter EC-05] A non-truncated page can advertise a newer sequence than it returns.** Verdict: `maybe-false` → **defer**. Carried: the prior unread-tail row owns the same invariant question.
- **[edge-case-hunter EC-06] Cascade paging advances an exclusive cursor by one too many.** Verdict: `high` → **defer**. Carried: DW-86 already owns this unchanged Story 4.7 defect; do not defer it again.
- **[edge-case-hunter EC-07] Child-completion paging advances an exclusive cursor by one too many.** Verdict: `high` → **defer**. Carried: DW-86 already owns this unchanged Story 4.7 defect; do not defer it again.
- **[edge-case-hunter EC-08] Cascade reads do not validate page/payload identity.** Verdict: `high` → **defer**. Carried: the earlier cascade-reader row owns this pre-existing cross-boundary evidence gap.
- **[edge-case-hunter EC-09] Cascade reads silently ignore malformed `ChildSpawned` evidence.** Verdict: `high` → **defer**. Carried: the same earlier cascade-reader row owns this pre-existing fail-open behavior.
- **[edge-case-hunter EC-10] Child-completion reads silently ignore undecodable lifecycle evidence.** Verdict: `high` → **defer**. Carried: the earlier child-completion row owns this unchanged Story 4.7 behavior.
- **[edge-case-hunter EC-11] Child-completion reads do not reject a foreign stream domain.** Verdict: `high` → **defer**. Carried: the same earlier child-completion row owns this unchanged cross-domain behavior.

_2026-09-22 bmad-build full-baseline review after the Group 2/3 seven-patch close-out. All three layers
reviewed the `9526c31` baseline diff. Every raw finding is classified below before grouping; repeated findings
retain their earlier verdict and route with carried evidence._

- **[blind-hunter BH-01] Mixed-case `TENANTS` bypasses the raw ordinal `/project` guard.** Verdict: `low` → **defer**. Carried: the same projection guard, lowercasing path, and direct-endpoint consequence remain exactly as the prior triage row records.
- **[blind-hunter BH-02] Startup reminder reconciliation stops permanently after its bounded retry budget.** Verdict: `medium` → **defer**. Carried: `ReminderReconciliationService` still returns after the final attempt and the canonical retry/readiness entry already owns the behavior.
- **[blind-hunter BH-03] An empty registry/index window can let startup reconciliation finish before backfill appears.** Verdict: `maybe-false` → **defer**. Carried: the existing row owns the same reachability question; projection-poller reset/backfill semantics remain the evidence needed to settle it.
- **[blind-hunter BH-04] Parked reminder candidates have no supported unpark/replay path.** Verdict: `medium` → **defer**. Carried: the parked skip and missing operator recovery path are unchanged and the canonical unpark entry already owns the work.
- **[blind-hunter BH-05] A first failed reminder action can starve later reconciliation and steady-state awaits.** Verdict: `medium` → **defer**. Carried: both cited loops still fail fast exactly as the earlier reconciler and handler rows record.
- **[blind-hunter BH-06] Due-now reconciliation has no identity-bearing failure telemetry.** Verdict: `medium` → **defer**. Carried: the existing recovery-diagnostics decision intentionally keeps the generic 4603 fallback and defers a new event surface.
- **[blind-hunter BH-07] The actor removes reminder state after gateway acceptance rather than terminal command success.** Verdict: `medium` → **defer**. Carried: the actor-cleanup retry-gap row records the same unchanged behavior.
- **[blind-hunter BH-08] Re-suspending at the same instant reuses the earlier resume identity.** Verdict: `medium` → **defer**. Carried: the canonical occurrence/sequence-identity entry already owns the required redesign.
- **[blind-hunter BH-09] Reconciler and steady-state scheduling reuse a stale pre-loop clock snapshot.** Verdict: `medium` → **defer**. Carried: the existing reconciliation and handler stale-delay entries own both unchanged loops.
- **[blind-hunter BH-10] Exact caller cancellation inside a tenant can discard that tenant's accumulated evidence.** Verdict: `medium` → **defer**. Carried: the source remarks and prior human ruling deliberately retain this exact-token in-tenant limitation.
- **[blind-hunter BH-11] A raw-sequence watermark can prevent repair after a decoder upgrade.** Verdict: `medium` → **defer**. Carried: the same `MaxSequence` plus `>=` repairability gap remains recorded in the pending-index row.
- **[blind-hunter BH-12] The sibling future-reminder live fact can pass on subscription redelivery instead of startup recovery.** Verdict: `medium` → **defer**. Carried: the exact `Recovery_re_registers_a_still_future_await_that_later_fires` marker-wait ambiguity is already recorded as the pre-existing sibling-fact gap.
- **[blind-hunter BH-13] Marker acquisition is not exclusive across concurrent deliveries.** Verdict: `high` → **defer**. Carried: the EventStore marker protocol still has no durable in-progress lease and the cross-repository protocol entry already owns the correction.
- **[blind-hunter BH-14] Duplicate identical `DateReached` conditions survive folding.** Verdict: `low` → **defer**. Carried: deterministic identities bound accepted outcomes while the durable await-set normalization entry owns the redundant work.
- **[blind-hunter BH-15] Shared rebuild accumulation is quadratic and retains full raw histories under a 1 MiB candidate ceiling.** Verdict: `medium` → **defer**. Carried: this is the same pre-existing shared-rebuild subsystem defect already assigned to its owning reconciliation spec.
- **[blind-hunter BH-16] Shared-rebuild descendant traversal is recursive without an explicit depth bound.** Verdict: `medium` → **defer**. Carried: the shared-rebuild subsystem entry already records the 10,000-member/deep-chain stack risk and routes it to its owning spec.
- **[verification-gap VG-01] EventIds 4806 and 4807 do not verify their warning level.** Verdict: `medium` → **patch, resolved**. The capturing logger now retains `LogLevel`, and both existing facts require `Warning`; the focused processor class passed 25/25 with zero skips.
- **[verification-gap VG-02] Strict-completion and release marker failures do not verify their structured exception.** Verdict: `medium` → **patch, resolved**. A strict-completion fact and a new release-failure fact now require EventId 4803 to carry the exact caught exception; the focused processor class passed 25/25 with zero skips.
- **[edge-case-hunter EC-01] One reconciler failure can starve later awaits.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-05 and the canonical fail-fast reconciler row.
- **[edge-case-hunter EC-02] One steady-state scheduler failure can starve later awaits.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-05 and the canonical handler fail-fast row.
- **[edge-case-hunter EC-03] A later suspension can reuse a consumed date condition's resume identity.** Verdict: `medium` → **defer**. Carried: identical root cause and route to BH-08 and the occurrence-identity entry.
- **[edge-case-hunter EC-04] Early resume or termination leaves stale reminders until their due time.** Verdict: `low`, rejected. Carried: Story 4.8 explicitly accepts the idempotent stale-reminder posture, and proactive cleanup requires new event-consumption machinery.
- **[edge-case-hunter EC-05] Duplicate date conditions cause redundant registrations or submissions.** Verdict: `low` → **defer**. Carried: identical root cause and route to BH-14 and the durable await-set normalization entry.
- **[edge-case-hunter EC-06] Stream metadata sequence can disagree with the decoded payload sequence.** Verdict: `maybe-false` → **defer**. The reader orders by `StreamReadEvent.SequenceNumber` and does not compare a payload ordinal, but reachability requires EventStore contract evidence or fault injection showing persisted envelope/payload sequence divergence.
- **[edge-case-hunter EC-07] Historical pending-index watermarks grow without compaction.** Verdict: `low`, rejected. Carried: ordinary impact remains low and safe pruning requires a migration/retention policy rather than a direct correction.
- **[edge-case-hunter EC-08] The append-only tenant registry grows without compaction.** Verdict: `low`, rejected. Carried with EC-07: sharding or removal is a durable schema redesign for a low ordinary-use risk.
- **[edge-case-hunter EC-09] Pre-index suspended streams can be undiscoverable after retiring configured-tenant scans.** Verdict: `maybe-false` → **defer**. Carried: this is the same empty-registry/backfill reachability question as BH-03 and still depends on projection-poller reset behavior.

_2026-09-22 final bmad-build review after the final three-finding close-out. All three layers reviewed the
`9526c31` baseline diff. Every raw finding is classified below before grouping; repeated findings retain
their earlier verdict and route with carried evidence._

- **[blind-hunter BH-01] Startup reminder reconciliation permanently stops after its bounded retry budget.** Verdict: `medium` → **defer**. Carried: the same `ReminderReconciliationService` location and give-up behavior remain exactly as the existing retry/readiness rows and deferred entry record; do not defer it again.
- **[blind-hunter BH-02] EventId 4603 promises an at-least-once retry even on the last attempt or at call sites without a retry loop.** Verdict: `medium` → **defer**. Carried: the unchanged shared template and final-attempt consequence are already recorded in the 4603 exhaustion-policy entries; do not defer it again.
- **[blind-hunter BH-03] One submit or schedule failure can starve later reconciliation and steady-state awaits.** Verdict: `medium` → **defer**. Carried: both cited loops still fail fast exactly as the canonical reconciler/handler ordering entries record.
- **[blind-hunter BH-04] Reconciliation and steady-state scheduling reuse one pre-loop clock snapshot.** Verdict: `medium` → **defer**. Carried: the existing reconciler and handler stale-delay entries own both unchanged loops.
- **[blind-hunter BH-05] Due-now reconciliation has no identity-bearing failure telemetry.** Verdict: `medium` → **defer**. Carried: the existing diagnostics decision retains generic 4603 and assigns any new event surface to the exhaustion/ordering work.
- **[blind-hunter BH-06] Re-suspending at the same instant reuses the earlier resume identity.** Verdict: `medium` → **defer**. Carried: the canonical occurrence/sequence-identity entry already owns the required cross-record redesign.
- **[blind-hunter BH-07] Duplicate or very large date-await sets produce redundant work and larger index documents.** Verdict: `low` → **defer**. Carried: deterministic identities bound accepted outcomes, while the existing durable await-set normalization entry owns duplicate conditions; no separate maximum-policy defect was demonstrated.
- **[blind-hunter BH-08] Early resume or termination leaves stale reminders until their due time.** Verdict: `low`, rejected. Carried: Story 4.8 explicitly accepts the idempotent stale-reminder posture, and proactive cleanup requires new event-consumption/state machinery.
- **[blind-hunter BH-09] Pending-date watermark and tenant-registry documents grow without compaction.** Verdict: `low`, rejected. Carried: ordinary impact remains low, and safe pruning or sharding requires a durable migration/retention policy rather than a direct correction.
- **[blind-hunter BH-10] Parked reminder candidates have no supported unpark/replay path.** Verdict: `medium` → **defer**. Carried: the unchanged parked skip and terminal disposition are already owned by the canonical unpark/replay entry.
- **[blind-hunter BH-11] Exact caller cancellation inside a tenant can discard that tenant's accumulated evidence.** Verdict: `medium` → **defer**. Carried: the source remarks and prior human ruling deliberately retain this exact-token in-tenant limitation.
- **[blind-hunter BH-12] The sibling future-reminder live fact can pass on subscription redelivery instead of startup recovery.** Verdict: `medium` → **defer**. Carried: the exact sibling-fact marker-barrier ambiguity is already recorded as a pre-existing verification gap.
- **[blind-hunter BH-13] A raw-sequence watermark can prevent index repair after a decoder upgrade.** Verdict: `medium` → **defer**. Carried: the same `MaxSequence` plus `>=` repairability gap remains recorded in the pending-index entry.
- **[blind-hunter BH-14] Local EventStore/Admin ACLs allow by default and Works receives broad EventStore `POST /**`.** Verdict: `medium` → **defer**. Carried: the two existing local-ACL entries own both unchanged policies and require route enumeration before narrowing.
- **[blind-hunter BH-15] Story lifecycle and historical task prose are internally stale.** Verdict: `low`, rejected. The runtime and tests use the correct exclusive cursor and retry semantics, and this finding's correction edits the build spec under review; presentation synchronizes lifecycle tracking separately.
- **[verification-gap VG-01] EventId 4500 did not adopt the new single-line correlation sanitization.** Verdict: `medium` → **patch, resolved**. `BoundCorrelationId` now replaces control characters within its 128-character bound; the focused regression injects CR/LF/tab/control-1 and the affected class passed **25/25**.
- **[verification-gap VG-02] CI inherited Dapr runtime 1.18.2 while the runnable smoke harness requires at least 1.18.3.** Verdict: `high` → **patch, resolved**. The exact pinned reusable workflow defaults to 1.18.2 and consumes its input for the integration job; `.github/workflows/ci.yml` now passes `dapr-runtime-version: '1.18.3'`, and `actionlint` passes.
- **[edge-case-hunter EC-01] The reconciler's one-time clock snapshot can make later awaits late.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-04 and the canonical reconciliation stale-delay entry.
- **[edge-case-hunter EC-02] The handler's one-time clock snapshot can make later registrations late.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-04 and the canonical steady-state stale-delay entry.
- **[edge-case-hunter EC-03] Stream-page cursor metadata can disagree with the request or returned sequences.** Verdict: `maybe-false` → **defer**. Carried: the cursor-consistency and unread-tail rows own these unchanged invariant questions and still require gateway-contract evidence or fault injection.
- **[edge-case-hunter EC-04] Projection replay does not reject duplicate or gapped positive sequence numbers.** Verdict: `maybe-false` → **defer**. Carried: the existing projection-ingress row records the same missing contiguity check and the contract evidence needed to establish reachability.
- **[edge-case-hunter EC-05] Pending awaits that predate registry population may remain undiscoverable after upgrade.** Verdict: `maybe-false` → **defer**. Carried: the existing empty-registry/backfill row owns the same reachability question and still depends on projection-poller replay semantics.
- **[edge-case-hunter EC-06] Startup reconciliation can finish before late projection index entries appear.** Verdict: `maybe-false` → **defer**. Carried with EC-05: the no-readiness-barrier consequence is unchanged, while reachability still depends on the same projection-poller/backfill evidence.
- **[edge-case-hunter EC-07] Task text says `LastSequenceReturned + 1` although the cursor is exclusive.** Verdict: `low`, rejected. Carried: the implementation correctly preserves the exclusive lower bound, and the proposed correction edits only the build spec under review.

_2026-09-22 bmad-build full-baseline review after commit `c4d40e5`. All three layers reviewed the
`9526c31` baseline diff. Every raw finding is classified below before grouping; repeated findings retain
their earlier verdict and route with carried evidence._

- **[blind-hunter BH-01] Mixed-case `TENANTS` bypasses the raw ordinal `/project` guard.** Verdict: `low` → **defer**. Carried: the same guard, `TenantId` lowercasing path, and direct-endpoint consequence remain exactly as the prior triage row records; do not defer it again.
- **[blind-hunter BH-02] Startup reconciliation permanently stops after its bounded attempt budget.** Verdict: `medium` → **defer**. Carried: `ReminderReconciliationService` still returns after the final attempt and the canonical retry/readiness entry already owns the behavior.
- **[blind-hunter BH-03] Registry-before-index persistence can let startup reconciliation finish in the empty-index window.** Verdict: `maybe-false` → **defer**. Carried: the existing empty-registry/backfill row owns the same reachability question; projection-poller reset/backfill semantics remain the evidence needed to settle it.
- **[blind-hunter BH-04] A first steady-state scheduling failure can starve later awaits in the same suspension.** Verdict: `medium` → **defer**. Carried: the handler still rethrows on the first failure and the canonical fail-fast handler entry already owns the ordering defect.
- **[blind-hunter BH-05] A first reconciliation action failure can starve later awaits, and due-submit failures lack identity-bearing telemetry.** Verdict: `medium` → **defer**. Carried: the canonical fail-fast reconciler and due-submit diagnostics entries own both unchanged claims; do not defer either again.
- **[blind-hunter BH-06] Cascade and child-completion readers advance an exclusive cursor by one too many.** Verdict: `high` → **defer**. Carried: DW-86 owns both unchanged Story 4.7 reader defects.
- **[blind-hunter BH-07] Cascade and child-completion readers forward continuation tokens rejected by the gateway.** Verdict: `high` → **defer**. Carried: the continuation-token half is already grouped with the same DW-86 paging defect.
- **[blind-hunter BH-08] Cascade reads do not validate page/payload identity and silently ignore malformed `ChildSpawned` evidence.** Verdict: `high` → **defer**. Carried: the existing cascade-reader row owns both pre-existing fail-open outcomes.
- **[blind-hunter BH-09] Child-completion reads omit domain validation and silently discard malformed lifecycle evidence.** Verdict: `high` → **defer**. Carried: the existing child-completion reader row owns both unchanged outcomes.
- **[blind-hunter BH-10] A raw-sequence watermark can prevent pending-index repair after a decoder upgrade.** Verdict: `medium` → **defer**. Carried: the same `MaxSequence` plus `>=` repairability gap remains recorded in the pending-index entry.
- **[blind-hunter BH-11] Re-suspending at the same instant can reuse the earlier resume message identity.** Verdict: `medium` → **defer**. Carried: the canonical occurrence/sequence-identity entry already owns the required redesign.
- **[blind-hunter BH-12] Reconciliation and steady-state registration reuse one pre-loop clock snapshot.** Verdict: `medium` → **defer**. Carried: the existing reconciler and handler stale-delay entries own both unchanged loops.
- **[blind-hunter BH-13] Historical per-item pending-index watermarks grow without compaction.** Verdict: `low`, rejected. Carried: ordinary impact remains low and safe pruning needs a durable retention/migration policy rather than a direct correction.
- **[blind-hunter BH-14] Parked candidates have no supported unpark, deletion, or replay path.** Verdict: `medium` → **defer**. Carried: the terminal parked skip and missing operator recovery path remain owned by the canonical unpark/replay entry.
- **[blind-hunter BH-15] Shared-rebuild accumulation is quadratic and retains full raw histories under the candidate byte ceiling.** Verdict: `medium` → **defer**. Carried: the shared-rebuild subsystem entry already assigns both defects to its owning reconciliation spec.
- **[blind-hunter BH-16] Shared-rebuild descendant traversal is recursive without an explicit depth bound.** Verdict: `medium` → **defer**. Carried: the existing shared-rebuild entry owns the deep-chain stack risk.
- **[blind-hunter BH-17] Domain-event skip, duplicate, and marker-failure logs retain raw event type and correlation metadata.** Verdict: `medium` → **defer**. Carried: the prior domain-event logging review and canonical deferred entry already record the control-character and unbounded-length exposure.
- **[blind-hunter BH-18] Marker acquisition is not exclusive across concurrent deliveries.** Verdict: `high` → **defer**. Carried: the EventStore marker protocol still has no durable in-progress lease and the cross-repository protocol entry already owns the correction.
- **[edge-case-hunter EC-01] Failures at one sequence can accumulate across an intervening healthy replay and park the aggregate.** Verdict: `maybe-false` → **defer**. The durable counter is not reset on the healthy path, but the current decoder is deterministic for an immutable stream; settle reachability by proving the projection contract can deliver the same sequence successfully between two failing deliveries (including a supported upgrade/rollback sequence). If reachable, separated transient failures can violate the human-selected consecutive-failure budget.
- **[edge-case-hunter EC-02] The handler's one-time clock snapshot can make later registrations late.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-12 and the canonical steady-state stale-delay entry.
- **[edge-case-hunter EC-03] The first handler scheduling failure can starve later awaits.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-04 and the canonical fail-fast handler entry.
- **[edge-case-hunter EC-04] The reconciler's one-time clock snapshot can make later awaits late.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-12 and the canonical reconciliation stale-delay entry.
- **[edge-case-hunter EC-05] The first reconciler action failure can starve later awaits and tenants.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-05 and the canonical fail-fast reconciler entry.
- **[edge-case-hunter EC-06] Startup reconciliation stops when its bounded retry budget is exhausted.** Verdict: `medium` → **defer**. Carried: identical location and consequence to BH-02 and the canonical retry/readiness entry.
- **[edge-case-hunter EC-07] A missing registry at upgraded startup can leave pre-index awaits undiscovered.** Verdict: `maybe-false` → **defer**. Carried: identical reachability question to BH-03 and the existing empty-registry/backfill entry.
- **[edge-case-hunter EC-08] The singleton tenant index can exceed state-store limits as historical work items accumulate.** Verdict: `low`, rejected. Carried with BH-13: ordinary impact remains low and sharding/compaction requires durable schema migration policy.
- **[edge-case-hunter EC-09] The append-only singleton tenant registry can eventually exceed state-store limits.** Verdict: `low`, rejected. Carried: the existing registry-growth row rejects the same low ordinary-use risk because sharding or removal is a durable schema redesign.
- **[edge-case-hunter EC-10] A future await-affecting lifecycle event could be added to only one of the projection's two switches.** Verdict: `false`. Every current state-affecting event type appears in both `IsStateAffectingEventType` and `PendingDateAwaits`; no present event produces the claimed stale recovery state. A hypothetical future event is a change to review, not a current bad outcome.
- **[edge-case-hunter EC-11] Exact caller cancellation inside a tenant can discard that tenant's accumulated partial evidence.** Verdict: `medium` → **defer**. Carried: the source remarks and prior human ruling deliberately retain this exact-token in-tenant limitation.
- **[edge-case-hunter EC-12] A non-truncated page can be accepted while metadata advertises an unread tail.** Verdict: `maybe-false` → **defer**. Carried: the existing unread-tail row owns the same gateway-contract uncertainty and the fault-injection evidence needed to settle it.
- **[edge-case-hunter EC-13 claim] Task text says paging advances by `LastSequenceReturned + 1` although the cursor is exclusive.** Verdict: `low`, rejected. Carried: runtime and tests use the correct unchanged exclusive lower bound, and the proposed correction edits only this build's spec.
- **[edge-case-hunter EC-14 claim] Task text describes last-write-wins while the implementation rejects stale replays by watermark.** Verdict: `low`, rejected. Carried: the implementation intentionally prevents stale resurrection, and the proposed correction edits only the spec under review.
- **[verification-gap VG-01] Handler-failure redelivery was not verified through `WorksDomainEventProcessor`.** Verdict: `medium` → **patch, resolved**. Added a fail-once handler fact that requires the same envelope to run twice, complete its marker, and return `Duplicate` on a third delivery; the focused fact passed **1/1**, zero skipped.
- **[verification-gap VG-02] Keycloak credential wiring was checked only as source text.** Verdict: `medium` → **patch, resolved**. Added a Keycloak-enabled composed-model fact that resolves the Works client id, username, and password and requires the password parameter to be secret; the focused fact passed **1/1**, zero skipped.

## Dev Notes

### Scope Boundary

Story 4.8 closes the second half of the runtime-wiring gap found by the 2026-07-21 audit: date-based resumes must execute in the **live topology** — in steady state (suspend → reminder → fire → resume, no restart) and on recovery (restart → auto-discover pending awaits → reissue/re-register, no hand configuration). All components exist from Story 4.6; this story changes **when registration happens** (suspend-time, not restart-only), **where recovery discovers work** (durable index + per-aggregate reads, not the 400-rejected tenant-wide scan), and **how it is enabled** (on by default, not gated on `Works:Recovery:Tenants`).

**In scope:** suspend-time reminder registration on the live event path; a pending-date-await index read model + tenant registry maintained by the `/project` dispatcher; an index-driven `IPendingDateAwaitSource`; removal of the hand-configured tenant gate; AppHost/topology-test updates; the live SM-1 Tier-3 lane; deterministic tests; docs/governance bookkeeping.

**Out of scope:** Story 4.7's work — the pub/sub domain-events subscription wiring, cascade dispatcher live triggering, `ChildCompletionResumeTranslator` consumption, and checkpoint-replay startup recovery (if 4.7 has landed first, reuse its surface; never rebuild it here). Also out: new kernel behavior, new durable catalog types (current count 40; Story 4.8 delta 0), changes to `WorkItemAggregate.Handle`/`WorkItemLifecycle`, the persisted parent roll-up convergence limitation (deferred-work F-PROJ-1), mutation-testing gate (F-PROJ-2), production UI/MCP/chatbot/email/routing/cost surfaces, `IExecutorRouter` implementation, Keycloak realm work.

### Sequencing Dependency on Story 4.7

The correct-course sequenced 4.7 before 4.8 because an event-subscription surface is "4.8's natural trigger too" — but **4.7 is `backlog` at story-creation time** and this story is implementable standalone: the `/project` dispatch is an **already-live per-aggregate event path** (the EventStore runtime posts each `work` aggregate's replayed stream to the Works host's bespoke `/project` endpoint after appends — this is how the what's-next and roll-up read models get persisted in the live topology today). Registering reminders and maintaining the index from that dispatch satisfies AC #1/#2 without any pub/sub work. The dev agent MUST re-check 4.7's status at implementation start (Task 1) and prefer its consumption surface if it landed. Relevant if 4.7's surface is used: the EventStore submodule ships an **unused** consumer SDK — `AddEventStoreDomainEvents(...)` (Client/Registration), `MapEventStoreDomainEvents()` (DomainService), `IEventStoreDomainEventHandler<TEvent>`, `EventStoreDomainEventProcessor` (MessageId dedup, unknown-type skip, poison-safe 200s) — but topic naming is per-tenant (`{tenantId}.work.events`; `"{domain}.events"` only for tenant `system`), and the only wildcard-topic precedent in the stack is `[Topic(..., "*.*.projection-changed")]` in `ProjectionNotificationController`. Those are 4.7 problems; do not solve them here.

> **2026-07-22 correction (Story 4.7 code review):** the note above that "the shared pubsub component's `scopes` currently exclude `works`" was true of `references/Hexalith.EventStore/src/Hexalith.EventStore.AppHost/DaprComponents/pubsub.yaml`, but that file governs `Hexalith.EventStore.AppHost`'s own standalone topology, not Works's. `src/Hexalith.Works.AppHost/Program.cs` calls `AddHexalithEventStore(...)` without a `pubSubComponentPath`, so Works's sidecar gets an auto-generated, **unscoped** pubsub component instead (confirmed live via the sidecar's materialized component resource and `/v1.0/metadata`). Story 4.7 landed a host-local Web-JSON subscription processor (not the generic SDK path) on this unscoped component and proved live delivery. The unscoped-component gap itself is tracked as deferred work (`deferred-work.md`, "Works's Dapr pubsub component has zero access-control scoping") — not blocking, but worth knowing if this story's implementer reuses the same subscription surface.

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
- **DD-5 — No new durable types, no kernel change:** catalog is 40 (Story 1.5; 4.8 delta 0); all new records are host-edge STJ; every clock read goes through the injected `TimeProvider` in `src/Hexalith.Works`; `Handle`, `Apply`, the reactor, and all kernel projects remain untouched (AC #4 is proven by existing fitness gates staying green).

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
- **Tests:** extend `tests/Hexalith.Works.IntegrationTests/` (dispatcher/index/source/reconciler deterministic lanes + the reworked `WorksReminderRecoveryPipelineSmokeTests` + `WorksAppHostTopologyTests` pins) and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/` (confinement token list; catalog is 40 / 4.8 delta 0). `WorksCommandPipelineSmokeTests`' fixed-id fix belongs to Story 4.7 per the deferred-work ledger — leave it unless trivially co-located.
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

claude-opus-4-8 (Claude Code dev-story workflow).

### Debug Log References

- **2026-09-08 review-remediation session (this session).** Scope: the human ruling at the top of the
  2026-09-07 round — every unchecked `[Review][Patch]` in the 2026-09-06 and 2026-09-07 rounds plus the seven
  decided `[Review][Decision]` items. **Pre-existing blocker found and fixed to make validation possible:** at
  session start `dotnet build Hexalith.Works.slnx -c Release` failed with CS7036 at
  `src/Hexalith.Works.AppHost/Program.cs:221` — HEAD `52a56c6` ("chore: update subproject commits") advanced the
  EventStore submodule, whose `WithEventStoreClientCredentials` now requires an explicit client id plus
  user-name/password parameter resources. Repaired minimally by mirroring the EventStore AppHost's own pattern
  (`LocalAuthentication:TenantAUsername`/`TenantAPassword` configuration with a per-run random password
  fallback, `HexalithEventStoreSecurityOptions.DefaultEventStoreClientId`); the governance pin
  `P0_AppHostProgramUsesPlatformEventStoreHelpersNotHandRolledDaprWiring` was updated from the old
  single-argument call shape to the call plus the shared client-id constant. This is submodule drift, not
  Story 4.8 functionality.
- **2026-09-08 Tier-3 root cause: the submodule bump took the whole live lane down.** With prerequisites
  present (dapr-init Redis on :6379, control-plane ports free), the first live attempt failed in
  `DistributedApplication.StartAsync` with the new diagnostic naming the culprit exactly:
  `eventstore[state=Finished,health=unknown,exit=134]` while sentry/placement/scheduler were all Healthy. DCP's
  captured stderr gave the reason: `OptionsValidationException: Authentication:JwtBearer requires either
  'Authority' (production OIDC) or 'SigningKey' (development symmetric key) to be configured.` Diffing the
  submodule across the pin bump (`910fda6a` → `8745b14b`, superproject HEAD `52a56c6`) shows
  `src/Hexalith.EventStore/appsettings.Development.json` **lost its entire `Authentication:JwtBearer` block**
  (issuer `hexalith-dev`, audience `hexalith-eventstore`, key `DevOnlySigningKey-AtLeast32Chars!`,
  `RequireHttpsMetadata:false`) — the exact values every Works smoke lane mints against. Repaired in
  `src/Hexalith.Works.AppHost/Program.cs` by composing that development symmetric-key validation for
  `eventstore` and `eventstore-admin` on the `--EnableKeycloak=false` path, mirroring the EventStore AppHost's
  own `ConfigureLocalSymmetricValidation` (overridable via `Works:Authentication:DevSigningKey`; the host still
  refuses symmetric keys outside Development). Also required: the EventStore/Operations/Admin hosts carry
  `SuppressBuild=true` and must be built explicitly before a live run, per Task 5.
- **2026-09-08 live-lane hazard found by the new port gate (and bounded because of it).** Aspire leaks a DCP
  controller holding the fixed control-plane proxy ports (51005/51006) after a killed run — and after an
  ordinary `WorksAppHostTopologyTests` run, which builds testing builders without starting them. The new gate
  now waits up to 60 seconds for each port to free before deciding it is foreign-held, then skips with the port
  and holder named. That is a deliberate trade: an actionable skip beats a five-minute `StartAsync` budget
  burning on a port that can never bind. Operator note: `ss -ltnp | grep 5100` finds the leaked `dcp` process.
- **2026-09-08 substrate re-verification (the HIGH cursor finding).** Read the pinned submodule directly rather
  than trusting comments: `StreamReadRequest.FromSequence` is documented "exclusive lower bound",
  `AggregateActor.ReadEventsRangeAsync` computes `startSequence = fromSequence + 1`,
  `FakeAggregateActor` filters `SequenceNumber > fromSequence`, and EventStore's own
  `DaprBackupCommandService` pages with `readCursor = LastSequenceReturned` (no increment). The
  `StreamsController` inline comment saying "paginate by setting FromSequence = lastSequenceReturned + 1" is
  wrong about its own API. `PendingDateAwaitStreamReader` was following that comment and dropped exactly one
  event per page boundary.
- **2026-09-08 Tier-3 live result (after the two repairs above): 4/4 PASS, 0 skipped, 957.974 seconds.**
  `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class
  "…WorksReminderRecoveryPipelineSmokeTests" -class "…WorksMtlsAuthorizationSmokeTests"` ran every fact with the
  prerequisites genuinely present: steady-state suspend-time registration → Dapr Scheduler fire → resume with no
  restart (AC #1); overdue recovery auto-discovered from the durable index across three AppHost lifecycles with
  no tenant configuration and the original Scheduler reminder deliberately deleted (AC #2/#3); future-await
  re-registration and its later fire across two lifecycles; and the mTLS ACL fact (`eventstore` allowed,
  `eventstore-admin` 403 at Works `/process`). Each fact now runs in its own tenant, so this is four independent
  proofs rather than four views of one shared tenant's state.
- **2026-09-08 external commits and a submodule move mid-session (not this session's doing).** An external
  actor (author `Jérôme Piquot`, 08:51) committed the working tree as `39643e5` and `42c4318`, which carry this
  session's work **plus unrelated submodule pointer bumps** (`Hexalith.Builds`, `Hexalith.EventStore`,
  `Hexalith.FrontComposer`, `Hexalith.Parties`, `Hexalith.Projects`) — notably EventStore `8745b14b` →
  `d45206f7`. Nothing was reverted. The Tier-3 4/4 live pass was produced against `8745b14b`; after the move the
  deterministic gates were re-run against `d45206f7` and are unchanged (build 0/0; 568 / 237 / 3 / 317), but the
  live lane has not been re-run against the newer pin.
- **2026-09-08 deterministic verification:** `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1
  -v minimal` → 0 warnings / 0 errors. Direct xUnit v3 binaries: UnitTests **568/568**, ArchitectureTests
  **237/237**, PropertyTests **3/3**, IntegrationTests excluding `*SmokeTests` **317/317**, 0 skipped. Catalog
  guard still asserts **40** (Story 1.5's count; Story 4.8's delta stays zero).

- **2026-09-06 live-proof completion:** after the human approved the Sentry/mTLS scope expansion, direct `daprd`
  probes separated Dapr's `localhost` control-plane trust domain from the certificate-issued `public` workload
  trust domain. AppHost-owned TLS placement and Scheduler resources replaced the plaintext `dapr init` pair;
  Scheduler data persists in a named volume and every sidecar waits for the three control-plane resources. The
  mTLS ACL fact then passed (`eventstore` allowed to Works `/process`, `eventstore-admin` denied with 403). Live
  Works logs subsequently isolated `DateReminderRegistration` as non-serializable at the actor-remoting boundary;
  `[DataContract]`/`[DataMember]` fixed it. Review then froze required member names/order and strengthened both
  recovery facts to observe the exact durable index, delete the original Scheduler reminder, and require startup
  recreation. Final results: reminder class 4/4 with no skips in 923.387s; command smoke 1/1 in 105.366s; cascade
  class 18/18 in 332.959s; Release build 0 warnings/errors; Unit 567/567, Architecture 236/236, Property 3/3,
  non-smoke Integration 287/287, topology 14/14. The current catalog is 40 only because separately committed
  Story 1.5 added three types; Story 4.8's catalog delta is zero.
- **2026-09-06 Story 4.8 runtime-proof session:** aligned the AppHost SDK to 13.5.3 and upgraded the local Dapr
  runtime from 1.18.2 to 1.18.3 without clearing Redis. The first focused three-fact run finished naturally after
  924 seconds with all facts timing out in `DistributedApplication.StartAsync`; DCP logs isolated
  `eventstore-operations` exiting because its Development-only application-channel-token fallback was not active,
  while `eventstore-admin` waited for that resource. Forwarding the AppHost `DOTNET_ENVIRONMENT` fixed startup;
  the topology model then passed 7/7. A full focused rerun finished naturally in 279 seconds: every fact reached
  healthy EventStore/Works resources and a Works Dapr 1.18.3 sidecar advertising `DateReminderActor` with actor
  runtime `RUNNING`, `hostReady=true`, placement connected, and scheduler connected. All three then failed at the
  first CreateWorkItem submission (HTTP 500), before reminder registration. The bounded response body retained
  the correlation/tenant; DCP application logs isolated the inner failure to EventStore's
  `DaprDomainServiceInvoker`: `403 Forbidden` invoking AppId `works`, method `process`; the Works application saw
  no `/process` request. Target-sidecar debug output and the Dapr 1.18.3 ACL implementation confirmed the local
  self-hosted topology provides no certificate-backed SPIFFE caller identity (mTLS disabled; no Sentry), so the
  existing Works `defaultAction: deny` applies. The ineffective namespace/debug experiments were removed; the
  ACL was not weakened, no mTLS/Sentry topology was added, and actor code was not patched because execution never
  reached the reminder boundary.
- **2026-09-06 deterministic verification:** exact restore succeeded; Release solution build 0 warnings / 0
  errors; UnitTests 529/529, ArchitectureTests 236/236, PropertyTests 3/3, IntegrationTests excluding
  `*SmokeTests` 268/268; focused BuildConfigurationTests 6/6 and WorksAppHostTopologyTests 7/7. Catalog remains
  37 (session snapshot; current count is 40 / 4.8 delta 0). `dapr --version` reported CLI 1.18.0/runtime 1.18.3. `aspire stop --non-interactive` reported no running
  AppHost, corroborated by the process list.
- Baseline (HEAD `ff329cc`, after Story 4.7 landed): `dotnet build Hexalith.Works.slnx -c Release` → 0 warnings / 0 errors. UnitTests **496**, PropertyTests **3**, ArchitectureTests **44**, all green. (Story-spec baseline `9526c31` predates 4.7; 4.7 added no unit/arch count drift.)
- Post-change: build 0/0; UnitTests **496**, PropertyTests **3**, ArchitectureTests **44** (incl. 3 new host-edge governance tokens); IntegrationTests deterministic (`-class- "*SmokeTests"`) **143/143**, 0 skipped.
- Tier-3 diagnosis: a two-host isolation run (park on a past `DateReached` → host 1; restart with no `--Works:Recovery:Tenants` → host 2) returned `steadyResume=0, recoveryResume=1`. Recovery (AC #2/#3) is proven live; the Dapr actor-reminder fire (AC #1 steady-state, Story-4.6 `DateReminderActor`/Scheduler path, never exercised live before) did not deliver in the WSL2 `dapr init` sandbox. `ResourceLoggerService.WatchAsync` surfaced no Works logs in this harness, so the behavioral two-host probe was used instead. Cascade smoke lane (same `work.events` subscription) passes independently, confirming subscription + gateway health.
- **2026-09-05 session — build was broken at session start (pre-existing, not this story's scope):** `dotnet build Hexalith.Works.slnx -c Release` failed with 21 errors before any Story 4.8 finding was touched: CS0177 in `WorkItemRollUpPayloadDescriptor.TryResolve`/`WhatsNextPayloadDescriptor.TryResolve` (an `&&`-short-circuit expression body never assigns the `out descriptor` parameter on its false branch) and CS0122/CS0053 in `WorkItemRollUpProjection` (nested `RollUpNode.Key`/`ChildKeys`/`ParentKey` and `NodeKey` were `private`, unreachable from the enclosing class). Both traced to commit `df46f71` ("Refactor WorkItem Roll-Up Projection and Tenant Isolation"), well outside Story 4.8's own file list. Fixed with minimal, behavior-preserving changes (block-bodied `TryResolve` with an explicit `descriptor = null; return false;`; widened three members + one nested type from `private` to `internal`, still assembly-only) since no Story 4.8 change could be validated otherwise. Also found and fixed `ArchitectureTests.BuildConfigurationTests.P0_GlobalJsonPinsSdkTestRunnerAndAspireSdk` asserting the stale `10.0.301` SDK pin (commit `0904f06` bumped `global.json` to `10.0.400` without updating this test).
- 2026-09-05 session full re-run after all fixes (production + pre-existing build break): `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` → 0 warnings / 0 errors. UnitTests **529/529**, ArchitectureTests **236/236**, PropertyTests **3/3**, IntegrationTests deterministic (`-class- "*SmokeTests"`) **268/268**, 0 skipped.
- 2026-09-05 session Tier-3 attempt (honest, inconclusive): `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class *SmokeTests` against live Dapr prerequisites confirmed present (`dapr_placement`/`dapr_scheduler`/`dapr_redis`/`dapr_zipkin` containers already up, ports 6050/6060/6379 listening). The process ran **13+ minutes** consuming almost no CPU (12s total) — the same symptom (stuck, not crashing) as the 2026-08-28/2026-09-01 `DistributedApplication.StartAsync` hang finding (open, line 118) — and was terminated (`kill -9`) to stop burning session time on an already-tracked, previously-undiagnosed blocker rather than re-diagnose it from scratch. **This truncated the process's own output before its failure-detail lines flushed**: the captured output shows both `Suspend_time_registration_resumes_the_item_when_the_scheduler_fires` and `Recovery_reissues_a_parked_date_await_from_the_durable_index_without_hand_configuration` as `[FAIL]`, but with no stack trace or exception detail (lost to the forced kill), so this session cannot add a verified root cause beyond confirming the blocker still reproduces. **This is not a fresh live-pass or live-fail claim** — findings #100 (line 100) and #118 (line 118) remain open exactly as before, now with one more reproduction data point. In hindsight, terminating the process before its normal 5-minute internal `CancelAfter` timeout was premature; a future session should let it run to that natural timeout (or the outer harness timeout) rather than force-kill, so the process's own diagnostic output survives intact.

### Completion Notes List

- **2026-09-22 open review-patch close-out.** Projection log sanitizers now replace Unicode line and paragraph separators (U+2028, U+2029) as well as control characters, and known-type skip paths are pinned to the catalog simple name. `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx --configuration Release --no-restore -m:1 -v minimal -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` passed with zero warnings/errors. `PendingDateAwaitIndexDispatcherTests` **28/28**, non-smoke Integration (`-class- "*SmokeTests"`) **550/550**, Unit **568/568**, Property **3/3** (100 cases each), and Architecture **268/268** passed with zero skips. `git diff --check` passed. No Tier-3 live evidence was repeated. The same commands and totals are in `tests/test-summary.md`.

- **2026-09-22 final bmad-build review close-out.** EventId 4500 now applies the same bounded
  control-character sanitization as projection skip diagnostics, and CI explicitly installs Dapr runtime
  **1.18.3**, matching the live harness minimum instead of inheriting **1.18.2**. `actionlint` passed; Release
  build passed with zero warnings/errors; non-smoke Integration **547/547**, Unit **568/568**, Property
  **3/3**, and Architecture **268/268** passed with zero skips. No Tier-3 live evidence was repeated.

- **2026-09-22 final three-finding close-out.** The roll-up sequence guard now heals a newer document stored
  under a foreign tenant/work-item identity, projection skip diagnostics retain the sanitized simple event type
  on one bounded line, and the Group 2/3 build record includes the NuGet-audit and MinVer pins actually used.
  Release build passed with zero warnings/errors; focused Integration **58/58**, Unit **568/568**, Property
  **3/3**, non-smoke Integration **547/547**, and Architecture **268/268** all passed with zero skips. The older
  DW-56 marker-protocol review item is normalized as an accepted cross-repository deferral.

- **2026-09-22 Group 2/3 review-patch and full-baseline review close-out.** Closed all seven latest review patches: fail-closed
  persisted-child identity coverage, bounded projection log fields, constructor-rejected suspension coverage on
  both delivery paths, identity-bearing in-progress/reserved-tenant diagnostics, structured marker exceptions,
  and positive cascade stale-window validation. The full-baseline review then closed two focused verification
  gaps by pinning warning levels and the two remaining marker-exception paths. Release solution build passed
  with zero warnings/errors; focused Integration **99/99**, Unit **568/568**, Property **3/3**, non-smoke
  Integration **547/547**, and Architecture **268/268** all passed with zero skips. The pre-existing Debug
  duplicate-assembly conflict remains separate from this patch; the older DW-56 marker-protocol item remains
  intentionally open.

- **2026-09-21 final full-baseline review close-out.** The three surviving review patches now distinguish
  startup reconciliation from subscription redelivery: Host 1 waits for each suspension marker to complete,
  and Host 3 must re-register a future-await canary before convergence is asserted. Empty and whitespace-only
  Sentry credentials share the bounded fail-closed theory. Release build passed with zero warnings/errors;
  topology **18/18**, the exact three-host recovery fact **1/1** with zero skips in **380.665s**, Unit
  **568/568**, Property **3/3**, non-smoke Integration **539/539**, and Architecture **268/268** all passed.

- **2026-09-21 final four-patch close-out.** Added the persistently-empty Sentry credential regression, moved
  the two final deferred rows under their own review heading, and linked the steady-state stale-delay item to
  the canonical reconciliation family. The isolated pre-edit AppHost baseline reached healthy Works and Dapr
  control-plane resources and stopped cleanly. Release build passed with zero warnings/errors; the affected
  classes passed **47/47**, Unit **568/568**, Property **3/3**, non-smoke Integration **538/538**, and
  Architecture **268/268**, all with zero skips. No production behavior changed and no Tier-3 credit is claimed.

- **2026-09-21 final bmad-build review patches.** Added deterministic coverage for bounded Sentry-credential
  retry success and exhausted-budget failure, plus the forward-compatible pending-await refold case that ignores
  an unknown non-state event beside a valid suspension. The focused IntegrationTests project build passed with
  zero warnings/errors, and the two directly affected test classes passed **46/46** with zero skips. Two newly
  verified historical design gaps were appended to `deferred-work.md`; carried findings were not duplicated.

- **2026-09-21 spec-11 review close-out.** Closed the two final bookkeeping findings by adding the dated
  spec-11 inventory and reconciling these completion notes with the already-observed evidence. No production or
  test code changed in this close-out. The verified spec-11 results remain Release build 0 warnings/errors,
  focused harness **41/41**, Unit **568/568**, Property **3/3**, serial non-smoke Integration **534/534**, and
  Architecture **268/268**, all with zero skips. The Tier-3 aggregate was not repeated after the test-only
  spec-11 fixes; its preceding **0/4** result remains current and no fresh live acceptance credit is claimed.

- **2026-09-20 bmad-build full-baseline review remediation.** Three verification gaps in the AppHost probe
  harness were patched with six focused facts: adapter kill/wait exception classification, cleanup-phase
  wait/state failures plus cancellation precedence, and ordinary wrapper-disposal failure aggregation. The
  independent post-patch run passed Release build 0 warnings/errors, focused harness **40/40**, Unit
  **568/568**, Property **3/3**, serial non-smoke Integration **533/533**, and Architecture **268/268**, all
  with zero skips.

- **2026-09-20 spec-10 five-patch close-out.** Docker non-zero exits now preserve their direct inspect
  diagnostic instead of being rewrapped as generic process-observation failures. The exact production
  `(IPAddress, int)` bind overload returns the socket settings observed on the listener it started, and the
  focused regression pins IPv4 `ExclusiveAddressUse`, IPv6 `DualMode`, and real occupied-port behavior. The
  run/dispose wrapper has a successful owner-list/disposal fact, both adapter disposal waits are pinned to
  5000 ms, and the real-child fail-safe performs and verifies a second bounded kill/wait when the first wait
  returns false. Release build passed with 0 warnings/errors; focused harness, Unit, Property, serial non-smoke
  Integration, and Architecture passed **34/34**, **568/568**, **3/3**, **527/527**, and **268/268** with zero
  skips. The Tier-3 aggregate was attempted honestly and finished **0/4** in **2112.534s**: three reminder facts
  stopped before Works readiness because EventStore stayed unhealthy or exited 1, while the mTLS fact reached
  readiness but received HTTP 500 on its first command. No fresh live credit is claimed; `aspire describe`
  confirmed no AppHost remained after the run.

- **2026-09-20 File List increment review close-out.** Re-ran the Story 4.8 Tier-3 reminder and mTLS lanes
  against the final reviewed checkout: all **4/4** facts passed with zero skips in **1023.751s**, covering overdue
  recovery, future-reminder recovery, steady-state Scheduler delivery, and deny-by-default mTLS authorization.
  Hardened the shared AppHost settling boundary so unavailable IPv6 loopback families are inapplicable rather
  than occupied, Docker ownership probes include stopped containers, and transient probe failures/timeouts remain
  inside the bounded retry. Added six deterministic harness regressions, including production-path IPv6,
  complete Docker argument ordering, pipe-close cleanup,
  timed-out-process coverage from the independent review. Corrected the CI/CD/deferred-work
  evidence and stable citations, made spec-9 source paths repository-relative, and recorded the approved
  Aspire AppHost SDK 13.5.4 deviation from spec-9's dependency-version boundary. Release build passed with
  0 warnings/errors; Unit, Property, serial non-smoke Integration, and Architecture passed **568/568**,
  **3/3**, **498/498**, and **268/268** with zero skips. The AppHost stopped cleanly after the live run.

- **2026-09-20 spec-9 review-patch close-out.** Closed the final three spec-9 findings: the clean final-index
  cancellation fact now pins the empty first-index read, zero stream reads, and zero logs; the 4608 architecture
  guard requires the complete shutdown-or-exhaustion restart sentence; and the test-summary citation now names
  only the parking and stream filters covered by `:145-197`. Release build passed with 0 warnings/errors;
  focused Integration and Architecture classes passed **29/29** and **3/3**; Unit, Property, and serial
  non-smoke Integration passed **568/568**, **3/3**, and **493/493**; full Architecture passed **268/268**.
  Current root gitlinks and checkouts agree at Chatbot `a782557875a27648a85b063572eb6c62c53459f8`,
  Conversations `c610fbb8c5491fb0d7987c2f8294199887f4a44c`, and EventStore
  `4bc61d9a60fae13a65f23963c1cb731065222f2a`. No pointer changed and no Tier-3 credit is claimed.

- **2026-09-18 four-patch spec-9 close-out.** Added the direct two-tenant clean next-index cancellation
  regression: the first index is empty, the final index throws the exact caller token, the exact exception
  propagates, the final index is read once, and no warning is logged. EventId 4608 guidance and its architecture
  guard now condition a later attempt on the host still running and retry budget remaining, and require restart
  after shutdown or exhaustion. The append-only in-tenant cancellation citation now covers the stream-read
  exact-token filter through line 197. The root index and all three checkouts agree on the adopted gitlinks:
  Chatbot `1047ef38d3845406227891639aaeb853e5d4f116`, Conversations
  `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, and EventStore
  `b5541259058320a0a7f1db19038709cbd02dad85`; the earlier revisions remain recorded below as transition
  history, not current-state claims. Release build passed with 0 warnings/errors; focused Integration classes
  passed **29/29**, **13/13**, **4/4**, and **136/136**; focused documentation passed **3/3**; Unit, Property,
  and serial non-smoke Integration passed **568/568**, **3/3**, and **493/493**. Architecture passed
  **237/238** with only the pre-existing SDK-pin mismatch and **237/237** excluding that exact fact. No Tier-3
  smoke lane ran and no new live credit is claimed.

- **2026-09-17 seven-patch spec-8 close-out.** The outer exact-caller cancellation catch now preserves
  previously accumulated cross-tenant partials, tenant/candidate counts, and the earlier cause when the next
  tenant-index read is canceled, without counting or logging cancellation or changing any in-tenant filter.
  Operator guidance renders `Reason` correctly, makes later 4604/4606 attempts conditional on host lifecycle,
  and distinguishes reason-scoped 4603 plus steady-state 4609 from bounded startup-scan evidence. The 4605
  runtime fact rejects the old `will retry` promise. At that close-out, the reviewed range recorded Chatbot
  `3c787993213ccf33f8912e6ad5caac605586fa15`, Conversations
  `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, and EventStore
  `629168e3983e5a9cd1639013f39d758fb0068cac`. Later adopted superproject commits advanced Chatbot to
  `1047ef38d3845406227891639aaeb853e5d4f116` and EventStore to
  `b5541259058320a0a7f1db19038709cbd02dad85`; Conversations remained unchanged. Those old-to-new revisions are
  historical transition evidence, and the current root index plus checkouts agree on the adopted endpoints.
  Release build passed with 0 warnings/errors; focused Integration classes passed
  **28/28**, **13/13**, **4/4**, and **136/136**; focused documentation passed **3/3**; Unit, Property, and
  non-smoke Integration passed **568/568**, **3/3**, and **492/492**. Architecture passed **237/238** with only
  the pre-existing SDK-pin mismatch and **237/237** excluding that exact fact. The pre-edit Aspire baseline
  stopped at EventStore exit 134 with Works waiting; the post-review rerun stopped earlier when Dapr Sentry
  reported `no space left on device` while mounting its credentials and left dependent resources waiting. Both
  AppHosts stopped cleanly and no live credit is claimed. Independent review added the candidate-failure side of
  the exact-cancellation regression and qualified when 4605 can still be followed by 4603.

- **2026-09-17 nine-action close-out.** Between-tenant shutdown now preserves typed incomplete-scan evidence
  after any recorded tenant/candidate failure and does not read the next tenant; clean boundary shutdown still
  propagates the exact caller cancellation. The three in-tenant exact-token filters remain unchanged and their
  known ordering limitation is documented. Exact stopping-token cancellation during the hosted service's retry
  delay now completes cleanly after one 4603 without another attempt. Recovery telemetry no longer promises that
  later tenants/candidates always run during shutdown, 4605 points operators to 4603 for a later processing
  failure, and 4603 has its own actionable runbook row. Seventy data-contract rows pin null, empty, over-length,
  non-ASCII, and invalid-shape envelope tenants across all fourteen ordinary adapters while existing
  `LinkConversation` identity guards stay strict. Ledger corrections are append-only dated notes; the real
  SDK-pin mismatch and recovery-submit telemetry deferral remain open. Release build passed with 0 warnings and
  0 errors; focused Integration classes passed **26/26**, **4/4**, and **136/136**, and the focused documentation
  class passed **3/3**. Unit, Property, and non-smoke Integration passed **568/568**, **3/3**, and **489/489**.
  Architecture passed **237/238** with only the pre-existing SDK-pin mismatch and **237/237** when that one fact
  was excluded. No Tier-3 smoke lane was run and no live credit is claimed.

- **2026-09-16 six-finding close-out.** All three pending-date discovery cancellation filters now require the
  caller token to be canceled and the caught exception to carry that exact token; foreign tenant-index, parking,
  and stream cancellations remain typed incomplete-scan failures. Clean and incomplete multi-tenant scans now
  have explicit regressions proving parked counts add across tenants without reading parked streams. Recovery
  scheduling mirrors the steady-state 4609 warning/structured-cause/bare-rethrow policy while exact caller
  cancellation stays warning-free. The shared reserved-tenant helper normalizes only the envelope tenant, and
  28 catalog-driven malformed-domain/aggregate rows prove all fourteen ordinary adapters retain their kernel
  results while `LinkConversation` keeps full identity validation. The boundary/API/operator docs now describe
  full-replay history gating, catalog 40 / delta zero, all fifteen envelope-aware wrappers, terminal parking, and
  concrete 4604–4609 responses. Review fixes stop scanning when caller cancellation races a classified foreign
  dependency failure, preserve incomplete-scan evidence and both causes, and require an exact stopping token at
  the hosted-service shutdown boundary. The docs distinguish missing-reminder recovery from an already-durable
  reminder that may still fire; the documentation-test class passed 3/3. Release build passed with 0 warnings/errors;
  focused spec Integration classes passed 102/102 and the hosted-service class passed 3/3; Unit, Property, and
  deterministic non-smoke Integration passed 568/568, 3/3, and 416/416. Architecture
  passed 237/238 with only the pre-existing SDK-pin mismatch; excluding it passed 237/237. The required Aspire
  baseline did not reach Works readiness after `dapr-sentry` exited, and no live reminder credit is claimed.

- **2026-09-16 reminder recovery/scheduling hardening.** Reminder discovery now returns an explicit pass result
  carrying the parked-skip count through clean outcomes, typed incomplete scans, reconciler rewraps, EventId
  4605, and the public reconciliation outcome. Parked candidates emit Warning 4607 without a stream read or
  retry failure; parking-store faults emit bounded Warning 4608, do not touch the stream, and retain partial
  results for retry. Steady-state scheduling failures emit bounded Warning 4609 and rethrow the original
  exception, except caller-attributable cancellation. Deterministic coverage now pins both dates in a
  multi-date suspension, exact startup retry exhaustion, exact projection validation, and exception-safe test
  key cleanup. Release builds passed with zero warnings/errors; the six focused classes passed 56/56; Unit,
  Property, and deterministic non-smoke Integration suites passed 568/568, 3/3, and 377/377. Architecture
  passed 236/237 with only the pre-existing SDK-pin assertion (`10.0.400` expected versus checked-in
  `10.0.401`); excluding that assertion passed 236/236. Per the approved boundary, the live lane was not rerun.

- **2026-09-16 reserved-tenant command-envelope hardening.** All fifteen EventStore aggregate wrappers now
  receive `CommandEnvelope` and reject reserved tenant `tenants` from either the normalized payload or envelope
  before kernel delegation. `LinkConversation` retains its stronger domain/tenant/aggregate identity checks.
  The canonical command catalog drives fifteen reserved-envelope reflection rows plus fifteen ordinary-envelope
  kernel-parity rows, with a separate mixed-case canonical-envelope regression fact; the reserved-event processor
  fact now uses matching payload/envelope identity, a matching handler, and an explicitly acquire-capable marker.
  The focused classes passed 61/61, the Release solution
  build passed with 0 warnings/errors, and ArchitectureTests passed 236/237 with only the pre-existing SDK-pin
  assertion (`10.0.400` expected versus checked-in `10.0.401`); excluding it passed 236/236 including catalog 40.
  The pre-change Aspire baseline did not reach Works health because `dapr-sentry` exited; no reminder/live-lane
  evidence is claimed. (2026-09-16 code review: the sprint entry was advanced to `review` by a later commit and
  that value is ratified — `review` is exactly the state that routes a story to this workflow. The story `Status:`
  line is now driven by the review outcome, not by this bundle.)

- **2026-09-15 projection replay/invalidation hardening.** `/project` now derives date-await history from the
  authoritative full replay, writes an initially absent cleared tombstone, rejects non-positive source sequences
  before any write, and reports terminal parking count/sequence correctly with retry-stable race classification.
  Runtime and projection decoding share one handled-failure predicate. Foreign identity emits bounded,
  identity-specific live telemetry while the logger-free shared builder degrades the affected roll-up to
  incomplete. Post-write `changed && indexAccepted` invalidation remains deliberately at least once: a notifier
  failure propagates and identical equal-watermark redelivery retries notification. Release build passed with
  0 warnings/errors; focused classes passed 23/23, 31/31, and 19/19; Unit 568/568, Property 3/3, and the
  pre-follow-up deterministic non-smoke Integration run passed 332/332. Architecture passed 236/237; only the pre-existing SDK assertion
  (`10.0.400` expected versus checked-in `10.0.401`) failed, and excluding exactly it passed 236/236 including
  catalog guard 40. The live lane was not rerun; historical 4/4 remains older evidence only.

- **2026-09-08 close-out.** Runtime evidence already on disk: Tasks 1–8 and in-scope review patches checked; live 4/4 at 957.974s against EventStore `8745b14b`. Status advanced to `review` (not `done`). Leftover catalog-37 gates reconciled to current count **40** with Story 4.8 catalog delta **0** (Story 1.5). The 2026-09-01 marker-store checkbox stays open and cross-referenced to DW-56. No reminder/index/recovery production code and no live-lane re-run.

- **2026-09-08 review remediation (this session).** All 28 unchecked `[Review][Patch]` items from the
  2026-09-06 and 2026-09-07 rounds are closed, and all seven human-decided `[Review][Decision]` items are
  implemented as decided — with one evidence-driven narrowing recorded inline: the "restore reachable index
  removal" decision could not be implemented as an unconditional call, because `WorkItemProjectionBoundarySanitizer`
  returns null **only** when the roll-up produced nothing, which also covers empty and rejection-only replays;
  restoring it unconditionally was tried and made
  `WorkItemProjectionQueryAdapterTests.Empty_and_rejection_only_replays_do_not_mutate_authoritative_projection_models`
  fail. The branch therefore requires at least one decoded non-rejection payload, which is precisely the decided
  condition ("the roll-up refused every event") without regressing the "nothing to project" invariant.
- **Highest-impact fix:** `PendingDateAwaitStreamReader` advanced its page cursor one event too far. Every
  multi-page per-aggregate rebuild — the story's declared source of truth for both suspend-time registration and
  recovery — silently dropped one event per page boundary. Since `PageSize` is 200 and the drop is at a page
  boundary, a work item with a long stream could lose the very `WorkItemResumed`/`WorkItemSuspended` that decides
  whether a reminder should exist.
- **Same bug, out of scope, reported not fixed:** `src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs:86`
  and `src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:159`
  carry the identical `LastSequenceReturned + 1` cursor (and the cascade file even documents the wrong rule in a
  comment). They are Story 4.7 surfaces, outside this story's scope, and are left untouched — they need their
  own change with their own tests.
- **Left open deliberately:** the single remaining 2026-09-01 `[Review][Patch] [Low]` item (line 125,
  `WorksDomainEventProcessor` marker-store failure) stays open and cross-referenced to `deferred-work.md` DW-56,
  exactly as the human ruling directs — it needs a cross-repo EventStore marker-protocol change.

> The original 2026-07-23 and intermediate remediation notes below are historical. The 2026-09-06 final live
> resolution supersedes their blocker status.

- **2026-09-06 final live resolution:** the previously recorded blocker is closed. The AppHost composes a
  self-hosted Dapr 1.18.3 mTLS control plane (Sentry, placement, durable Scheduler) without weakening Works'
  deny-by-default ACL or committing credentials. Control-plane identity uses `localhost`; workload ACL identity
  remains `public`. The reached actor-remoting path exposed and fixed the missing data-contract metadata on
  `DateReminderRegistration`, whose required member names/order and forward-read payload are now frozen.
  Recovery facts wait for the exact durable pending-await index, delete the original Scheduler reminder, and
  prove startup recreation; the future deadline is rechecked after host-2 readiness. All steady, overdue
  recovery, future recovery, and mTLS allow/deny facts pass together; the affected command/cascade lanes also
  pass.
- **2026-09-06 runtime-proof remediation:** Aspire 13.5.3 is now pinned consistently and its CLI bundle enabled.
  The AppHost startup hang is fixed by carrying the AppHost environment to the operations workload. The live
  harness proves and reports each evidence-ladder boundary (resource health → sidecar version → actor/placement/
  scheduler readiness → exact reminder → delivery/exactly-once), and no longer skips once prerequisites are
  present. The former startup blocker is closed; the current live blocker is the pre-existing self-hosted
  deny-by-default invocation ACL rejecting `eventstore -> works/process` without a SPIFFE identity. Both steady
  and recovery facts stop at submission, so no new live-pass claim is made and status is not advanced to done.

- **Code-review remediation (2026-08-28):** added explicit containerized/native Dapr placement+scheduler endpoint
  resolution; bounded startup reconciliation retry; complete-scan propagation; positive options validation;
  pending-index sequence tombstones; page/domain/payload identity validation; malformed lifecycle fail-closed;
  multipage, budget, concurrency, stale-replay, processor-dispatch, and retry tests; future-recovery and repeated-
  restart live facts; one-type-per-file cleanup; and valid sprint YAML bookkeeping.
- **Review validation:** `Hexalith.Works` and AppHost build with 0 warnings/errors; UnitTests **528/528**,
  PropertyTests **3/3**, ArchitectureTests **207/207**, all non-smoke IntegrationTests **198/198**, and the focused
  review set **37/37** pass. The normal repository test build is independently
  blocked by the checked-out central-package conflict (`xunit.v3` 4.0.0 versus 3.2.2 extension pins), so review
  compilation used a temporary 3.2.2 package override without modifying repository dependencies.
- **Open acceptance evidence:** the focused reminder smoke command reached the containerized Dapr prerequisites
  but failed in the first `DistributedApplication.StartAsync` after **5m 08s** with `TaskCanceledException`.
  Because no reminder assertion ran, the AC #1 live-callback finding remains open.

- **Task 1 reconciliation — DD-1 resolution (steady-state trigger surface).** Story 4.7 is `done` (sprint-status + merge `ff329cc`), so per Task 1 I use its live `work.events` domain-event subscription as the steady-state registration trigger via a new `IEventStoreDomainEventHandler<WorkItemSuspended>`, mirroring 4.7's `WorkItemCompletedResumeHandler` (read stream → rebuild state → act). Evidence-based rationale that resolves Validation-Note risk (a): the `/project` dispatch is delivered by EventStore's `ProjectionPollerService` (`BackgroundService`, per-domain `RefreshIntervalMs` cadence) — a live background path but with poll-interval latency and dependent on a configured refresh interval, whereas the `work.events` subscription fires immediately on publish and is already proven live end-to-end by 4.7. The subscription is therefore the low-latency, reliable steady-state trigger; the `/project` dispatcher's role is narrowed to maintaining the durable pending-date-await index (recovery treats the index as discovery and re-folds each candidate's stream for truth per DD-3, so poll-interval index staleness is tolerated by design).
- **Substrate invariant re-verified against submodule `6a8f3866` (v3.81.0-4):** `StreamsController.ValidateRequest` 400-rejects null/whitespace `AggregateId` (`MissingRequiredField`) and any non-null `ContinuationToken`; page only by `FromSequence = LastSequenceReturned + 1`, `PageSize` ≤ 1000. Every stream read this story issues carries an `AggregateId` (per-aggregate reads only).
- **Task 2 (AC #1):** `WorkItemSuspendedReminderHandler : IEventStoreDomainEventHandler<WorkItemSuspended>` on 4.7's `work.events` subscription re-folds the suspended aggregate's per-aggregate stream through the pure `PendingDateAwaitProjection` (folded current set, never a raw event — DD-1) and registers one durable reminder per pending `DateReached` await via the injected `TimeProvider` + `IDateReminderScheduler`. Added `WorkItemSuspended` to `WorksDomainEventProcessor.s_consumedEvents` and registered the handler in `Program.cs`. Failures propagate for at-least-once redelivery; registration is idempotent.
- **Task 3 (AC #2):** `WorkItemProjectionDispatcher` maintains a per-tenant pending-date-await index document + one well-known tenant-registry document (new plain-STJ `PendingDateAwaitTenantIndex`/`PendingDateAwaitTenantRegistry`, new keys in `WorksReadModelKeys`) alongside the existing what's-next/roll-up writes, via `ReadModelWritePolicy.UpdateAsync` — registry written before index so a crash strands only a cheap empty read.
- **Task 4 (AC #2/#3):** `IndexedPendingDateAwaitSource` (registry → per-tenant index → per-aggregate re-fold) replaces the retired `StreamReadingPendingDateAwaitSource`; shared `PendingDateAwaitStreamReader` does the per-aggregate read+fold (always `AggregateId`-set, fail-closed on truncation). Removed `WorksRecoveryOptions.Tenants`, the `ReminderReconciliationService` `Tenants.Count` gate, and the AppHost `Works:Recovery:Tenants` forwarding block. `DateReminderReconciler` unchanged except its source.
- **Task 5 (AC #1/#3):** Reworked `WorksReminderRecoveryPipelineSmokeTests` into two facts — a live recovery proof (passes) and, at first landing, a steady-state fact that skipped honestly when the sandbox Scheduler did not fire actor reminders. Per-run-unique tenant + work-item ids. **Superseded 2026-09-08:** the recorded live 4/4 includes the scheduler-fire resume (AC #1); the steady-state fact no longer skips.
- **Task 6:** +18 deterministic IntegrationTests across three new classes (dispatcher index, suspend handler, indexed source + reconciler), plus `RuntimeAdapterGovernanceTests` token-list hardening. Catalog is 40 (Story 1.5; 4.8 delta 0); kernel-purity guards untouched and green (AC #4).
- **Task 7:** Updated `docs/boundary-decision-record.md` (M1 superseded + Story 4.8 entry), `docs/eventstore-api-surface-constraints.md` (Story 4.8 section, null-`AggregateId` no longer load-bearing), and appended a Story 4.8 section to `tests/test-summary.md`; leftover catalog-37 claims reconciled to count 40 / 4.8 delta 0.
- **AC #1 status (honest):** suspend-time registration is implemented and deterministically proven. **2026-09-05:** the live resume-without-restart depended on the Dapr actor-reminder fire (Story-4.6 infra), which the WSL2 `dapr init` sandbox did not deliver — recorded as a substrate blocker (test-summary), not hidden. That session's Tier-3 attempt did not add a fresh live-pass or live-fail data point (AppHost `StartAsync` hang, finding line 118; run terminated before diagnostics flushed); AC #1 and the AppHost-startup finding remained open then. **Superseded 2026-09-08:** the recorded live 4/4 includes the scheduler-fire resume (AC #1); AC #1 is no longer open. AC #2/#3 were already proven live; AC #4 remains proven by unchanged green kernel-purity guards.

### File List

**2026-09-22 open review-patch close-out**
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs`
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-22 final bmad-build review close-out**
- `.github/workflows/ci.yml`
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-22 final three-finding close-out**
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs`
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-22 Group 2/3 review-patch close-out**
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs`
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventLog.cs`
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs`
- `src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs`
- `src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs`
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-21 final full-baseline review close-out**
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-21 final four-patch close-out**
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-21 final bmad-build review patches**
- `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs`
- `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`

**2026-09-21 spec-11 review close-out**
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-10.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-11.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-20 spec-10 five-patch close-out**
- `tests/Hexalith.Works.IntegrationTests/ProcessSchedulerVolumeProbe.cs`
- `tests/Hexalith.Works.IntegrationTests/SchedulerVolumeProbeCleanupException.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs`
- `tests/Hexalith.Works.IntegrationTests/HangingSchedulerVolumeProbe.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-20 File List increment review close-out**
- `tests/Hexalith.Works.IntegrationTests/ISchedulerVolumeProbe.cs`
- `tests/Hexalith.Works.IntegrationTests/ProcessSchedulerVolumeProbe.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-20 spec-9 review-patch close-out**
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**2026-09-18 four-patch spec-9 close-out**
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`
- `docs/operations/subscriber-dead-letter-operator.md`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-8.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**Current adopted gitlinks (read-only evidence; spec-9 changed no pointer)**
- `references/Hexalith.Chatbot` — `3c787993213ccf33f8912e6ad5caac605586fa15` →
  `1047ef38d3845406227891639aaeb853e5d4f116` → `a782557875a27648a85b063572eb6c62c53459f8`
- `references/Hexalith.Conversations` — `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49` →
  `c610fbb8c5491fb0d7987c2f8294199887f4a44c`
- `references/Hexalith.EventStore` — `629168e3983e5a9cd1639013f39d758fb0068cac` →
  `b5541259058320a0a7f1db19038709cbd02dad85` → `4bc61d9a60fae13a65f23963c1cb731065222f2a`

**2026-09-17 seven-patch spec-8 close-out**
- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs`
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs`
- `docs/operations/subscriber-dead-letter-operator.md`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-7.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-8.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**Gitlinks recorded by the spec-8 run (historical transition start)**
- `references/Hexalith.Chatbot` — `3c787993213ccf33f8912e6ad5caac605586fa15`
- `references/Hexalith.Conversations` — `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`
- `references/Hexalith.EventStore` — `629168e3983e5a9cd1639013f39d758fb0068cac`

**2026-09-17 nine-action close-out**
- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs`
- `src/Hexalith.Works/Reminders/ReminderReconciliationService.cs`
- `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs`
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/ReminderReconciliationServiceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs`
- `docs/operations/subscriber-dead-letter-operator.md`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-7.md`

**2026-09-16 six-finding close-out**
- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs`
- `src/Hexalith.Works/Reminders/DateReminderReconciler.cs`
- `src/Hexalith.Works/Reminders/ReminderReconciliationService.cs`
- `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs`
- `src/Hexalith.Works/WorkItemEventStoreAggregate.cs`
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs`
- `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs`
- `docs/boundary-decision-record.md`
- `docs/eventstore-api-surface-constraints.md`
- `docs/operations/subscriber-dead-letter-operator.md`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-6.md`

**2026-09-16 reminder recovery/scheduling hardening**
- `src/Hexalith.Works/Reminders/IPendingDateAwaitSource.cs`
- `src/Hexalith.Works/Reminders/PendingDateAwaitScanResult.cs`
- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs`
- `src/Hexalith.Works/Reminders/PendingDateAwaitScanIncompleteException.cs`
- `src/Hexalith.Works/Reminders/DateReminderReconciler.cs`
- `src/Hexalith.Works/Reminders/ReminderReconciliationOutcome.cs`
- `src/Hexalith.Works/Reminders/ReminderReconciliationService.cs`
- `src/Hexalith.Works/Reminders/WorkItemSuspendedReminderHandler.cs`
- `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs`
- `tests/Hexalith.Works.IntegrationTests/Story48RecordingLogger.cs`
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs`
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitScanIncompleteExceptionTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemSuspendedReminderHandlerTests.cs`
- `tests/Hexalith.Works.IntegrationTests/ReminderReconciliationServiceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-5.md`
- `_bmad-output/implementation-artifacts/deferred-work.md` (added by the 2026-09-16 code review, which found it omitted)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (`4-8-…` → `review`; added by the 2026-09-16 code review, which found it omitted)

**2026-09-16 reserved-tenant command-envelope hardening**
- `src/Hexalith.Works/WorkItemEventStoreAggregate.cs`
- `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `_bmad-output/implementation-artifacts/spec-4-8-reserved-tenant-command-envelope-hardening.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (`4-8-…` → `review`; added by the 2026-09-16 code review, which found it omitted)

**2026-09-15 projection replay/invalidation hardening**
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs`
- `src/Hexalith.Works/Runtime/WorksEventDecoder.cs`
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs`
- `docs/whats-next-projection.md`
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-4.md`

**2026-09-08 Group 1 host-edge patches**
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` (already-parked 4503 no-write; registry copy-on-write; distinct ProjectionType tokens)
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs` (identity mismatch → Malformed; catch `NotSupportedException`; skip EventId 4504)
- `src/Hexalith.Works/Projections/WorksReadModelKeys.cs` (index/registry/parking projection tokens; `ThrowIfReservedTenantId`)
- `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` (reserved tenant on every `Handle` + LinkConversation envelope)
- `src/Hexalith.Works/Runtime/WorksHost.cs` (`/project` uses required `IOptions<WorksProjectionOptions>`)
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs` (4503 skip; identity/`NotSupportedException` park; registry mutate)
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` (reserved tenant before marker)
- `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs` (reserved-tenant `CreateWorkItem`)
- `tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs` (indexed source; projection bind/validate; bound Max=1 `/project`)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (`4-8-…` → `review`)

**2026-09-08 close-out (bookkeeping only)**
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` (status `review`; catalog prose 40 / 4.8 delta 0)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (`4-8-…` → `review`; `last_updated` 2026-09-08)
- `_bmad-output/implementation-artifacts/tests/test-summary.md` (kept 4/4 on `8745b14b`; pin note: live lane not re-run on HEAD `c6efdbba`)

**2026-09-08 review-remediation session**

_Production — `src/Hexalith.Works/`_
- `Reminders/PendingDateAwaitStreamReader.cs` (HIGH: exclusive-`FromSequence` page cursor; renamed budget)
- `Reminders/IndexedPendingDateAwaitSource.cs` (per-candidate failure isolation)
- `Reminders/PendingDateAwaitScanIncompleteException.cs` (added `FailedCandidateCount`)
- `Reminders/DateReminderReconciler.cs` (`ExceptionDispatchInfo` rethrow; typed signal preserved; `ProcessAsync`)
- `Reminders/WorkItemSuspendedReminderHandler.cs` (renamed budget)
- `Runtime/WorksEventDecoder.cs` (catch guard-clause exceptions; deleted dead `IsKnownEventType`)
- `Runtime/WorksRecoveryOptions.cs` (`MaxStreamPagesPerAggregate` + deprecated alias + effective value)
- `Runtime/WorksRecoveryExtensions.cs` (validation on the effective budget)
- `Runtime/WorksRecoveryLog.cs` (`PendingDateAwaitCandidateScanFailed`; scan-incomplete now logs both counts)
- `Runtime/WorksProjectionOptions.cs` (new — `Works:Projection` parking budget)
- `Runtime/WorksHost.cs` (bind/validate `WorksProjectionOptions`; pass to the dispatcher; corrected rebuild-ceiling comment)
- `Runtime/Events/WorksDomainEventProcessor.cs` (reserved-tenant refusal)
- `Runtime/Events/WorksDomainEventEndpointExtensions.cs` (using order)
- `Recovery/Cascade/StreamReadingCascadeDescendantSource.cs`, `Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs` (renamed budget only — the cursor bug there is out of scope, see Completion Notes)
- `Projections/WorkItemProjectionDispatcher.cs` (guarded index write; non-mutating transform; reachable removal; reserved tenant; parking)
- `Projections/PendingDateAwaitTenantIndex.cs` (renamed from `PendingDateAwaitIndex.cs`; no hardcoded catalog count)
- `Projections/WorkItemProjectionParking.cs` (new — bounded parking record)
- `Projections/WorksReadModelKeys.cs` (removed `Legacy*` duplicates; `ReservedTenantId`; `ProjectionParkingKey`)
- `Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs` (canonical key names)

_Production — AppHost_
- `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs` (bounded credential re-read, empty-PEM refusal)
- `src/Hexalith.Works.AppHost/Program.cs` (submodule-drift repair for `WithEventStoreClientCredentials`)

_Tests_
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs` (new — shared live harness, port gating, phase-tagged failures)
- `tests/Hexalith.Works.IntegrationTests/WorksMtlsAuthorizationSmokeTests.cs` (new — mTLS fact split out, snake_case)
- `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs` (per-fact tenants; harness; already-delivered probe)
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs` (snapshot on budget expiry; version fail-closed; exact `connected`; null `entries`; already-delivered probes; 404-on-delete)
- `tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs` (new — validation chain + deprecated alias)
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs` (+6 facts: guarded write, reserved tenant, foreign domain, parking ×2, reworked tombstone)
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs` (+2 facts; candidate/tenant counts; `[0, 1]` cursor)
- `tests/Hexalith.Works.IntegrationTests/WorkItemSuspendedReminderHandlerTests.cs` (+1 fact: duplicate delivery)
- `tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs` (+3 facts: identity, cross-tenant, unsupported capability)
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs` (host resolves the suspend handler)
- `tests/Hexalith.Works.IntegrationTests/DateReminderRegistrationSerializationTests.cs` (theory over all five required members)
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` (Sentry args pinned)
- `tests/Hexalith.Works.IntegrationTests/Story47InMemoryReadModelStore.cs` (`FailNextGets` injection)
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitScanIncompleteExceptionTests.cs`, `DateReminderRecoveryRuntimeTests.cs` (new constructor arity)
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs` (+1 fact: AppHost SDK/CLI-bundle pin)
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs` (host-edge token list +2; credentials pin)

_Docs_
- `docs/boundary-decision-record.md` (AD-20 provenance note; 2026-09-08 remediation paragraph)
- `docs/eventstore-api-surface-constraints.md` (Story 4.8 section moved after 4.7; exclusive-`FromSequence`, per-aggregate budget, reserved tenant, parking)
- `_bmad-output/implementation-artifacts/tests/test-summary.md`

**Production — `src/Hexalith.Works/`**
- `Reminders/DateReminderRegistration.cs` (2026-09-06: Dapr actor-remoting data contract)
- `Reminders/WorkItemSuspendedReminderHandler.cs` (new)
- `Reminders/PendingDateAwaitStreamReader.cs` (new)
- `Reminders/IndexedPendingDateAwaitSource.cs` (new)
- `Reminders/StreamReadingPendingDateAwaitSource.cs` (deleted)
- `Reminders/ReminderReconciliationService.cs` (drop `Tenants` gate)
- `Projections/PendingDateAwaitIndex.cs` (new — index model + sequence tombstones)
- `Projections/PendingDateAwaitTenantRegistry.cs` (new — registry model split to one type/file)
- `Projections/WorkItemProjectionDispatcher.cs` (maintain index + registry)
- `Projections/WorksWhatsNextReadModel.cs` (new keys)
- `Runtime/WorksRecoveryOptions.cs` (remove `Tenants`)
- `Runtime/WorksRecoveryExtensions.cs` (swap source registration)
- `Runtime/WorksEventDecoder.cs` / `Runtime/WorksEventIdentity.cs` (fail-closed decoding and stream identity checks)
- `Runtime/Events/WorksDomainEventProcessor.cs` (consume `WorkItemSuspended`)
- `Program.cs` (register the suspend handler)

**Production — AppHost**
- `global.json` (2026-09-06: AppHost SDK 13.5.3 pin)
- `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` (2026-09-06: SDK 13.5.3 + CLI bundle)
- `src/Hexalith.Works.AppHost/Program.cs` (delete `Works:Recovery:Tenants` forwarding; 2026-09-06: forward the
  AppHost environment to `eventstore-operations`, compose the default mTLS placement/Scheduler pair, and support
  an explicit externally managed endpoint pair)
- `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs` (2026-09-06: external Sentry credentials plus TLS-enabled
  placement/durable Scheduler resources; review hardening for authoritative sidecar identity, symlink-aware
  certificate containment, cross-drive paths, and loopback-only embedded etcd)
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.yaml`
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml`
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.eventstore-admin.yaml`
- `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.eventstore-operations.yaml`
- `src/Hexalith.Works.AppHost/DaprComponents/sentry.yaml`

**Tests — `tests/Hexalith.Works.IntegrationTests/`**
- `PendingDateAwaitIndexDispatcherTests.cs` (new)
- `WorkItemSuspendedReminderHandlerTests.cs` (new)
- `IndexedPendingDateAwaitSourceTests.cs` (new; 2026-09-05: +2 facts — per-tenant isolation, cursor-stall fix; 3
  existing fail-closed facts updated to expect `PendingDateAwaitScanIncompleteException`)
- `Story48FixedTimeProvider.cs`, `Story48RecordingScheduler.cs`, `Story48RecordingSubmitter.cs` (split test fakes)
- `ReminderReconciliationServiceTests.cs` (bounded startup retry)
- `Story48Streams.cs` (new — shared stream-page helper)
- `WorksReminderRecoveryPipelineSmokeTests.cs` (reworked into recovery + steady-state facts)
- `DateReminderRegistrationSerializationTests.cs` (2026-09-06: actor-remoting round trip, frozen forward-read
  payload, and missing-required-member rejection)
- `WorksAppHostTestReadiness.cs` (2026-09-06: bounded phase diagnostics plus Dapr/actor/reminder readiness probes;
  durable-index observation and exact reminder deletion for loss-sensitive recovery proof)
- `WorksAppHostTopologyTests.cs` (2026-09-06: explicit Development environment, operations forwarding, Sentry,
  mTLS placement/Scheduler, credential mounts, fixed proxy ports, durable Scheduler volume, workload/policy trust
  domains, one-sided endpoint rejection, authoritative sidecar environment, and certificate-path rejection)
- `WorksCommandPipelineSmokeTests.cs` (2026-09-06: AppHost-owned control-plane prerequisites and fixed test ports)
- `WorksCascadeRecoveryPipelineSmokeTests.cs` (2026-09-06: AppHost-owned control-plane prerequisites and fixed test ports)
- `DateReminderRecoveryRuntimeTests.cs` (2026-09-05: +1 fact — reconciler acts on partial results then rethrows)
- `WorksEventIdentityTests.cs` (new 2026-09-05 — fail-closed/rejection-event identity matching)
- `PendingDateAwaitScanIncompleteExceptionTests.cs` (new 2026-09-05 — constructor null-check ordering)

**Tests — `tests/Hexalith.Works.ArchitectureTests/`**
- `FitnessTests/RuntimeAdapterGovernanceTests.cs` (host-edge token list +3)
- `FitnessTests/BuildConfigurationTests.cs` (2026-09-05: fixed stale `10.0.301` SDK-pin assertion to match the
  checked-in `global.json` `10.0.400`, unrelated pre-existing drift found while verifying this session's changes;
  2026-09-06: Aspire AppHost SDK assertion 13.5.3)

**Docs**
- `docs/boundary-decision-record.md` (2026-09-05: + unbounded-growth tradeoff paragraph)
- `docs/eventstore-api-surface-constraints.md`
- `_bmad-output/implementation-artifacts/tests/test-summary.md` (2026-09-05: corrected counts + new session section)
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably.md`
- `_bmad-output/implementation-artifacts/deferred-work.md` (2026-09-05: tightened DW-53's citation range)

**Sprint tracking**
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (2026-09-05: normalized line endings, no content change)

**2026-09-20 File List close-out review patches**
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs` (bounded two-attempt termination,
  redirected-read classification, production exclusive-bind configuration seam)
- `tests/Hexalith.Works.IntegrationTests/ProcessSchedulerVolumeProbe.cs` (bounded disposal that terminates a live child)
- `tests/Hexalith.Works.IntegrationTests/SchedulerVolumeProbeCleanupException.cs` (non-retryable unconfirmed-child
  cleanup diagnostic)
- `tests/Hexalith.Works.IntegrationTests/HangingSchedulerVolumeProbe.cs` (new standalone configurable lifecycle fake)
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs` (33 focused lifecycle/bind facts)
- `_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-10.md`
- `_bmad-output/implementation-artifacts/deferred-work.md` (four canonical duplicate cross-links)
- `_bmad-output/implementation-artifacts/tests/test-summary.md` and
  `_bmad-output/implementation-artifacts/sprint-status.yaml` (observed totals and review status)

**Out-of-scope pre-existing build break fixed while validating (2026-09-05, not Story 4.8 functionality):**
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpPayloadDescriptor.cs` (fixed CS0177: `TryResolve`'s
  `&&` short-circuit expression body never assigned `out descriptor` on the false branch)
- `src/Hexalith.Works.Projections/Strategies/WhatsNextPayloadDescriptor.cs` (same CS0177 fix)
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` (fixed CS0122/CS0053: nested
  `RollUpNode.Key`/`ChildKeys`/`ParentKey` and `NodeKey` widened from `private` to `internal` so the enclosing
  class can reach them — introduced by commit `df46f71`, left the solution unable to build in Release)

## Change Log

- 2026-09-22 — Closed the two remaining review patches: projection diagnostics replace Unicode line and paragraph separators, and known-type skip logs are pinned to the catalog simple name. Release build and every deterministic suite passed with zero skips; no fresh Tier-3 live credit is claimed.

- 2026-09-22 — Final bmad-build review sanitized EventId 4500 correlation metadata and aligned the blocking CI
  lane with the harness's Dapr runtime 1.18.3 minimum. Workflow lint, Release build, and every deterministic
  suite passed; no fresh Tier-3 live credit is claimed.

- 2026-09-22 — Closed the final three review findings by healing newer foreign-identity roll-ups, retaining a
  sanitized simple event type in bounded projection diagnostics, and recording the exact build pins. Release
  build and all deterministic suites passed with zero skips; the historical DW-56 marker-protocol finding is
  normalized as an accepted cross-repository deferral.

- 2026-09-22 — Closed all seven latest Group 2/3 review patches plus two full-baseline verification gaps with
  bounded structured diagnostics, fail-closed projection and decoder regressions, warning-level pins, complete
  marker-exception coverage, and positive stale-window validation. Release build and every deterministic suite
  passed with zero skips; the older cross-repository DW-56 marker-protocol finding remains open.

- 2026-09-21 — Closed the final full-baseline review with whitespace credential coverage and a marker/canary
  live proof that distinguishes subscription redelivery from both startup reconciliation passes. Release build
  and every deterministic suite passed, and the exact three-host recovery fact passed 1/1 with zero skips.

- 2026-09-21 — Closed the final four Story 4.8 review patches: pinned persistent empty-credential exhaustion,
  recorded the observed **538/538** non-smoke Integration total, repaired deferred-work provenance/cross-links,
  and reconciled lifecycle state. Release build and every deterministic suite passed with zero skips; the
  Tier-3 aggregate was not repeated and no fresh live credit is claimed.

- 2026-09-21 — Closed the final bmad-build verification gaps with bounded credential retry/exhaustion tests and
  unknown non-state stream-event tolerance coverage. The affected project built with zero warnings/errors, the
  focused two-class lane passed 46/46 with zero skips, and two new historical design gaps were deferred.

- 2026-09-21 — Closed the two final spec-11 review findings by adding its dated File List inventory and
  prepending completion evidence at harness 41, Unit 568, Property 3, non-smoke Integration 534, and
  Architecture 268. No code or test changed, the Tier-3 aggregate was not repeated, and Story 4.8 plus sprint
  tracking returned to `review`.

- 2026-09-20 — Closed the six remaining spec-10 review actions: corrected the parent lifecycle state, pinned the
  already-correct probe-failure rethrow, hardened IPv6 and real-child test observations, normalized five ledger
  locators, and completed the historical File List. Release build and deterministic gates passed at harness 41,
  Unit 568, Property 3, non-smoke Integration 534, and Architecture 268. The pre-edit AppHost baseline stopped at
  an exited EventStore resource, so the Tier-3 aggregate was not repeated and no fresh live credit is claimed.

- 2026-09-20 — Full-baseline bmad-build review triaged 23 findings, carried previously recorded broad-history
  issues, deferred five pre-existing concern groups, and closed three AppHost probe verification gaps with six
  focused facts. Final deterministic gates are green at harness 40, Unit 568, Property 3, non-smoke Integration
  533, and Architecture 268.

- 2026-09-20 — Closed all five spec-10 review patches, refreshed every deterministic gate, and recorded the
  attempted Tier-3 run's exact EventStore/AppHost blocker without claiming live acceptance credit. Story 4.8
  is ready for review.

- 2026-09-20 — Spec-10 increment review (`origin/main...HEAD`) left five patch findings as action items
  (non-zero Docker-exit classification, production exclusive-bind assertions, wrapper success fact,
  adapter disposal wait budget, real-child fail-safe `WaitForExit`) and returned Story 4.8 plus sprint
  tracking to `in-progress`.

- 2026-09-20 — Closed the fifteen File List close-out review patches: Docker probe cleanup now makes two bounded
  termination attempts, classifies supported process/pipe failures, and protects adapter disposal; production
  listener settings and every reviewed lifecycle/occupancy branch are exercised by 33 focused facts. Observed
  gates passed: build 0 warnings/errors; Integration 526, Unit 568, Property 3, Architecture 268. The final reviewed
  tree passed the live reminder/mTLS aggregate 4/4 in 1020.742s with zero skips, and no AppHost remained afterward.
  Story 4.8 and sprint tracking returned to `review`.

- 2026-09-20 — Closed all eight File List increment review patches: reran the live Tier-3 reminder/mTLS lane,
  hardened the shared IPv6 and Docker occupancy probes with deterministic coverage, corrected ledger paths and
  citations, and recorded the approved Aspire 13.5.4 spec-9 deviation. Every live and deterministic gate passed;
  Story 4.8 and sprint tracking returned to `review`.

- 2026-09-20 — File List increment review (`28724f2...HEAD`) left eight patch findings as action items
  (Tier-3 re-run, Aspire 13.5.4 deviation record, harness occupancy probes, ledger citations) and returned
  Story 4.8 plus sprint tracking to `in-progress`.

- 2026-09-20 — Closed the final three spec-9 review patches, reran every deterministic gate on the current
  adopted gitlinks, and returned Story 4.8 plus sprint tracking to `review` without new live credit.

- 2026-09-18 — Closed the four remaining spec-8 review patches: added the clean final-index exact-cancellation
  regression, qualified and pinned EventId 4608 host-lifecycle guidance, extended the append-only citation through
  the stream cancellation filter, and reconciled current adopted gitlinks while preserving revision transitions.
  Every deterministic gate was refreshed; Story and sprint status returned to `review` without new live credit.

- 2026-09-17 — Closed the seven spec-8 review patches: retained typed cross-tenant evidence when the next
  tenant-index read throws exact caller cancellation, corrected the source remarks and 4603–4606 operator
  semantics, pinned 4605's retry-eligibility wording, corrected the append-only ledger, recorded exact retained
  gitlinks for that run, and refreshed every deterministic gate. Later adopted pointer transitions are recorded
  by the 2026-09-18 close-out. Story and
  sprint status returned to `review`; the blocked pre-edit Aspire baseline adds no live credit.

- 2026-09-17 — Closed the final nine Story 4.8 review actions: preserved cross-tenant partial evidence on
  shutdown, contained exact-token retry-delay cancellation, pinned clean/incomplete boundary cancellation,
  data-contract malformed tenant behavior for all fourteen ordinary adapters, reconciled 4603–4606 operator
  guidance and XML documentation, corrected append-only ledger history and File List omissions, and recorded
  exact deterministic evidence. Story and sprint status returned to `review`; no Tier-3 smoke lane was run.

- 2026-09-16 — Closed the six remaining in-scope Story 4.8 review gaps and the review-discovered cancellation
  race/hosted-service masking paths: exact-token cancellation attribution at
  all discovery boundaries, additive cross-tenant parked counts, recovery-path 4609 parity, tenant-only shared
  envelope guarding with ordinary-kernel parity, and current operator/architecture documentation. Deterministic
  gates: build 0/0; focused spec Integration 102; hosted-service Integration 3; focused operator-documentation
  Architecture 3; Unit 568; Property 3; non-smoke Integration 416; Architecture 237/238 with only the already-recorded SDK-pin mismatch (237/237 when
  excluded). Aspire baseline recorded; live lane not rerun.

- 2026-09-16 — bmad-code-review of the un-reviewed remediation bundle (`bbfacbb..HEAD`, HEAD `e54c6a1`), scoped to this
  story's own File List: 33 files, +1916/−241. Four layers reported, none failed; 37 raw findings triaged to 22 entries
  (3 decision, 6 patch, 6 defer, 7 rejected). All three decisions resolved: recovery telemetry carries its cause again
  at 4603/4605/4608/4609 with `Reason` now a bounded exception type name; the submodule-pin advance is kept with every
  deterministic gate re-executed on HEAD and re-recorded; and the sprint value `review` is ratified with the artifacts
  that contradicted it corrected. Gates after the change: build 0/0; Unit 568, Property 3, deterministic Integration
  377, Architecture 236/237 (pre-existing SDK-pin failure). Live lane not re-run.


- 2026-09-16 — Completed the reminder recovery/scheduling hardening bundle: parked skips are countable but not
  retryable, parking-store faults are classified separately without a stream read, scheduling faults produce
  bounded diagnostics while retaining original-exception redelivery, and the focused deterministic gaps are
  pinned. Deterministic gates are current; historical live evidence and catalog 40 remain unchanged.

- 2026-09-16 — Hardened all fifteen command wrappers against a reserved envelope tenant, retained payload-side
  refusal and `LinkConversation` identity enforcement, repaired the reserved-event pre-marker proof, and pinned
  both reserved-envelope refusal and ordinary-envelope kernel parity from the canonical command catalog. Story
  4.8 added no reminder or live-topology completion evidence. (2026-09-16 code review: this bundle changed
  `sprint-status.yaml` to `review` although its own spec Never list forbade it. The value is ratified rather than
  reverted — see the resolved status decision in the 2026-09-16 Review Findings.)

- 2026-09-15 — Projection-only hardening completed after the EC-03 revert: authoritative replay tombstones,
  pre-write positive sequences, correct parking diagnostics, shared decode classification, bounded identity
  telemetry, incomplete shared-rebuild degradation, and retry-safe at-least-once invalidation. Deterministic
  gates are current; live 4/4 remains historical and was not rerun.

- 2026-09-08 — Close-out only: story and sprint status → `review`; leftover catalog-37 claims reconciled to count 40 / 4.8 delta 0; recorded live 4/4 on EventStore `8745b14b` stands (not re-run on HEAD `c6efdbba`). DW-56 remains open.

- 2026-09-08 — Review-remediation session closing every unchecked `[Review][Patch]` from the 2026-09-06 and
  2026-09-07 rounds and implementing all seven human-decided `[Review][Decision]` items. Substance: the
  per-aggregate stream reader no longer drops one event per page boundary (`FromSequence` is exclusive, verified
  in the pinned submodule); recovery degrades per candidate instead of collapsing a whole tenant; the
  pending-date-await index write is guarded so only items that ever held an await touch the singleton key;
  index/what's-next removal is reachable again when the roll-up refuses real events (narrowed on test evidence
  so empty/rejection-only replays still write nothing); a permanently undecodable state-affecting event parks
  its aggregate after a bounded, configurable failure count; the tenant id `tenants` is refused at both host
  edges; and `MaxStreamPagesPerTenant` is renamed to `MaxStreamPagesPerAggregate` with the old configuration key
  kept as a binding alias. Live-lane harness: per-fact tenants, control-plane port skip gates, phase-tagged
  failures, snapshot-on-budget-expiry, exact placement `connected`, fail-closed runtime version, tolerance for a
  reminder that already fired, and bounded empty-PEM-refusing credential reads. Also repaired a pre-existing
  AppHost build break from the HEAD submodule bump. Gates: build 0/0; UnitTests 568, ArchitectureTests 237,
  PropertyTests 3, deterministic IntegrationTests 317 — all green, 0 skipped. Tier-3: the reminder +
  mTLS live classes pass **4/4 with zero skips in 957.974 s**, after repairing two pre-existing submodule-drift
  breakages that had taken the whole live lane down (the AppHost client-credentials signature change, and the
  EventStore host losing its development JWT settings in `appsettings.Development.json`).

- 2026-09-06 — Review remediation closed the in-scope findings: recovery now proves the exact pending-await index
  is durable, deletes the Dapr Scheduler reminder, and observes overdue reissue/future re-registration after a
  genuine restart; sidecar control-plane identity is explicit and overwrites stale inherited values; certificate
  containment resolves symlinks and handles Windows drive roots; embedded etcd uses loopback; caller cancellation
  and HTTP timeout diagnostics remain distinct; global test environment mutation is gone; ACL trust domains and
  partial endpoint configuration are pinned; and the reminder data contract freezes required names/order plus a
  forward-read fixture. Final gates: build 0/0; Unit 567; Architecture 236; Property 3; deterministic Integration
  287; topology 14; reminder live 4 in 923.387 seconds; command live 1 in 105.366 seconds; cascade class 18 in
  332.959 seconds — all pass with zero live skips.
- 2026-09-06 — Completed the approved live-proof expansion: composed AppHost-owned Dapr 1.18.3 TLS placement and
  durable Scheduler alongside Sentry; preserved the deny-by-default workload ACL and external credentials;
  corrected the control-plane/workload trust-domain split; pinned the topology; fixed
  `DateReminderRegistration` actor-remoting serialization; made recovery deadlines readiness-relative; and
  aligned the affected live harnesses with explicit Development/fixed-port settings. Final gates: build 0/0;
  Unit 567, Architecture 236, Property 3, deterministic Integration 280, topology 9, reminder live 4, command
  live 1, cascade class 18 — all pass with zero skips in the live runs. Current catalog 40 due separately
  completed Story 1.5; Story 4.8 adds no catalog type.
- 2026-09-06 — Runtime-proof remediation aligned Aspire to 13.5.3, enabled its CLI bundle, fixed the AppHost
  startup hang by forwarding `DOTNET_ENVIRONMENT` to `eventstore-operations`, and added phase-specific resource,
  sidecar, actor, deterministic-reminder, submission, and delivery diagnostics. The local Dapr runtime was
  upgraded to 1.18.3 without clearing Redis. All deterministic gates are green (build 0/0; 529 Unit, 236
  Architecture, 3 Property, 268 non-smoke Integration), but all three live facts now stop honestly at the next
  boundary: EventStore receives 403 invoking `works/process` because the existing self-hosted, mTLS-disabled
  topology cannot supply the SPIFFE caller identity required by the deny-by-default ACL. Per the approved scope,
  the ACL was not weakened, Sentry/mTLS topology was not added, actor code was not changed, and story/sprint
  completion status was not advanced.

- 2026-09-05 — Code-review remediation session (this session). Closed the remaining Medium/Low review findings
  from the 2026-09-01/2026-09-05 rounds: per-tenant scan-failure isolation actually wired in
  (`PendingDateAwaitScanIncompleteException` now thrown/caught/tested, closing both the "dead code" finding and
  the underlying cross-tenant blast-radius regression), its constructor's null-check-before-message-format
  ordering and `public`→`internal` visibility, the `PendingDateAwaitStreamReader` cursor-stall-on-truncated-empty-
  page bug, `WorksEventIdentity`'s fail-closed gap (careful to keep the nine `IRejectionEvent` types matching by
  tenant/work-item alone, since they legitimately carry no `AggregateId`), the boundary-decision-record growth-
  tradeoff doc, the test-summary.md count reconciliation, the DW-53/DW-54 citation overlap, and the
  sprint-status.yaml line-ending normalization. Left open: the `WorksDomainEventProcessor` marker-failure
  finding (cross-referenced to the already-tracked, cross-repo-scoped `deferred-work.md` DW-56) and both
  Tier-3 live-infrastructure findings (AC #1 actor-reminder callback, Aspire AppHost `StartAsync` failure) —
  see the Debug Log for this session's own live-lane attempt. While validating, found and fixed a pre-existing,
  out-of-scope build break (commit `df46f71`) that left the solution unable to build in Release at session
  start, and a pre-existing `ArchitectureTests` failure from a stale SDK-pin assertion (commit `0904f06` bumped
  `global.json` without updating the test). Full re-run after all fixes: build 0/0; UnitTests 529/529,
  ArchitectureTests 236/236, PropertyTests 3/3, deterministic IntegrationTests 268/268 — all green.

- 2026-08-28 — Adversarial code-review remediation applied: 12/13 patch findings closed, 2 pre-existing findings
  deferred, and the live callback finding retained as open because Aspire timed out before the acceptance assertion.

- 2026-07-23 — Story 4.8 implemented (dev-story, claude-opus-4-8). Suspend-time durable date-reminder registration
  on Story 4.7's live `work.events` subscription; durable pending-date-await index + tenant registry maintained by
  the `/project` dispatcher; index-driven `IPendingDateAwaitSource` replacing the retired tenant-wide scan; removed
  the `Works:Recovery:Tenants` gate + AppHost forwarding (recovery on by default). +18 deterministic IntegrationTests;
  governance token list hardened; docs updated; catalog was 37 at implementation (current count 40 / 4.8 delta 0); kernel-purity guards untouched. Build 0/0;
  UnitTests 496, PropertyTests 3, ArchitectureTests 44, deterministic IntegrationTests 143 — all green. Live SM-1:
  recovery lane PASS (AC #2/#3 proven live, no hand config); steady-state lane SKIP (AC #1 actor-reminder fire is a
  Story-4.6 Dapr-Scheduler substrate path that does not deliver in the WSL2 `dapr init` sandbox — registration logic
  proven deterministically; recorded honestly, not hidden). Status → review.

## Validation Notes

- Checklist pass applied during creation. The story pre-empts the known failure modes for this slice: reinventing Story 4.6's components (Task 1 inventories them; tasks rewire, not rebuild), placing runtime code outside the host edge (governance guards named), issuing the 400-rejected null-`AggregateId` read (banned explicitly, with a recording-fake assertion required), re-adding hand configuration (the `Tenants` gate removal is a task, not a hint), growing the durable catalog (index records pinned as plain STJ, catalog is 40 / 4.8 delta 0), trusting stale docs (catalog-36 prose and stale version pins flagged), fake completion of Tier-3 lanes (the fbc78e58 gateway-timeout blocker is named with explicit honest-reporting instructions), and building Story 4.7's subscription surface out of turn (dependency decision DD-1 with a standalone default path).
- Remaining implementation risks, carried openly: (a) the exact trigger timing of the EventStore runtime's `/project` dispatch is verified in Task 1, not assumed — if dispatch turns out to be lazy rather than post-append in the live topology, the steady-state trigger may need 4.7's subscription surface, making 4.7 a hard prerequisite (report as a blocker rather than working around); (b) the Tier-3 lanes may stay blocked by the drifted EventStore submodule's gateway-submit timeout — the deterministic proofs plus honest blocker documentation are the fallback the validation ladder expects.

### Review Findings (2026-09-06, bmad-code-review)

Latest File List increment vs `062426c` (~2713 lines). Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor.

- [x] [Review][Decision] Boundary-decision-record 4.9 migration-start language shipped in this 4.8 increment — Commit `33a27e2` adds Hexalith.Platform ownership and "Migration may begin" to `docs/boundary-decision-record.md` while implementing 4.8 mTLS. Spec-4-8 forbids absorbing Story 4.9. Revert those paragraphs into 4.9, or keep them as the already-decided hosting destination restated while 4.8 touched the same file. — **Decided 2026-09-08 (human): keep as a restatement.** The 2026-09-06 architecture gate already decided host=Hexalith.Platform and unblocked Story 4.9; the paragraphs restate a settled decision in a file this increment was already editing. Keep them, and record that provenance (gate date + decision) in the boundary record so the text is not mistaken for 4.9 implementation work. — **Kept as a restatement, provenance recorded 2026-09-08:** `docs/boundary-decision-record.md` now carries a "Provenance of the two paragraphs above" note naming the 2026-09-06 architecture gate (AD-01…AD-25, AD-20 names the host repository and unblocks Story 4.9) as the deciding event, so the text cannot be mistaken for Story 4.9 implementation work.

- [x] [Review][Patch] Live AC #1/#3 proofs can treat a successful Scheduler fire as a registration or delete failure [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:201] — resolved 2026-09-08: `WaitForReminderRegisteredAsync` takes an optional already-delivered probe and returns successfully when the reminder's own effect (an accepted resume) is already observable — a fired-and-removed reminder is no longer read as a registration failure; `DeleteReminderAsync` treats a 404 on the DELETE as "already gone" and also consults the probe before failing.
- [x] [Review][Patch] Live skip gates probe only Redis :6379, so occupied AppHost control-plane ports hang instead of skip [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:528] — resolved 2026-09-08: `WorksAppHostSmokeHarness.PrerequisiteGapAsync` returns a reason string and now also refuses to run when any AppHost-owned control-plane port (50001 Sentry, 51005 placement, 51006 scheduler) is already bound, so an occupied port skips with the port named instead of hanging to the 5-minute startup budget.
- [x] [Review][Patch] Sidecar credential read is single-shot and accepts empty PEM after Sentry reports healthy [src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs:186] — resolved 2026-09-08: `ReadCredentialAsync` retries a bounded 20 × 500 ms while the credential is missing, locked, or **empty**, and fails closed with the actual reason and budget instead of handing the sidecar an empty PEM.
- [x] [Review][Patch] Sentry `--trust-domain=localhost` is not pinned by EvaluateArgsAsync [tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs:54] — resolved 2026-09-08: `DefaultAppHostComposesTheMtlsActorControlPlane` now evaluates the Sentry container's own args and pins `--trust-domain=localhost`, `--issuer-credentials=…`, and `--config=…`.
- [x] [Review][Patch] Missing required DataContract members other than Instant are untested [tests/Hexalith.Works.IntegrationTests/DateReminderRegistrationSerializationTests.cs:63] — resolved 2026-09-08: `Missing_required_actor_remoting_member_is_rejected` became a `[Theory]` over all five required members, each asserted to throw `SerializationException` (with a guard that the omitted element really was present).
- [x] [Review][Patch] Fitness test pins Aspire 13.5.3 only in global.json, not the AppHost Sdk or AspireUseCliBundle [tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:20] — resolved 2026-09-08: new `BuildConfigurationTests.P0_AppHostProjectPinsTheSameAspireSdkAndCliBundle` reads the AppHost csproj and asserts its `Sdk` attribute equals `Aspire.AppHost.Sdk/<global.json pin>` and that `AspireUseCliBundle` is `true`.
- [x] [Review][Patch] WaitForResourceHealthyAsync rethrows startup-budget cancellation without a resource snapshot [tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs:113] — resolved 2026-09-08: `WaitForResourceHealthyAsync` takes the caller's own token; only a cancellation of *that* token rethrows bare, while a startup-budget expiry now surfaces the resource snapshot and says the budget expired.
- [x] [Review][Patch] WithAppHostAsync wraps phase-tagged body failures in a generic [runtime] message [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:327] — resolved 2026-09-08: the harness walks the exception chain for the innermost phase tag and reports `Phase=[registration]`/`[delivery]`/`[submission]`… instead of relabelling everything `[runtime]`.
- [x] [Review][Patch] WaitForWorksActorRuntimeAsync does not fail-closed on a missing or unparseable runtimeVersion [tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs:221] — resolved 2026-09-08: an unreported or unparseable `runtimeVersion` now throws immediately naming the observed value, instead of spinning to a 60-second timeout whose message blames placement or the actor host.
- [x] [Review][Patch] Placement readiness uses Contains("connected"), which matches "disconnected" [tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs:239] — resolved 2026-09-08: placement readiness now tokenises the status and requires an exact `connected` token, so `disconnected` no longer satisfies it.
- [x] [Review][Patch] WaitForPendingDateAwaitIndexedAsync can NullReferenceException when Entries is JSON-null [tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs:368] — resolved 2026-09-08: a JSON-null `entries` object is handled (`index?.Entries is { } indexEntries`, plus a null-entry-list guard) instead of throwing `NullReferenceException` inside the polling loop.

- [x] [Review][Defer] DeleteReminderAsync GET-after-DELETE may need to treat 204 as gone [tests/Hexalith.Works.IntegrationTests/WorksAppHostTestReadiness.cs:441] — deferred: unverified on Dapr 1.18.3; live 4/4 implies GET returns 404 here. Settle by capturing the GET status immediately after a successful DELETE against this runtime.
- [x] [Review][Defer] deferred-work.md gained unstructured concurrent-review bullets without DW-id fields [_bmad-output/implementation-artifacts/deferred-work.md:805] — deferred: append-only ledger from a concurrent review; several bullets are already contradicted by this patch (`ShouldNotContain("--etcd-client-listen-address=0.0.0.0")`, Redis-only skip gates, `AssertMtls` policy domains). Needs a sweep, not an in-band 4.8 code fix.

**Rejected:**
- `false` — "`DAPR_CERT_KEY` PEM in sidecar env is a missing secret wrap": Dapr consumes the issuer key through that env var; `SidecarEnvironmentUsesTheAppHostOwnedControlPlaneIdentity` pins it; `DescribeResourceStates` dumps state/health/exit only; credential files remain outside the repository.
- `false` — "empty/whitespace `Dapr:Mtls:CertificateDirectory` accepts cwd": null uses `~/.dapr/certs/hexalith-works`; paths inside the repo throw; empty `GetFullPath` fails closed.
- `false` — "non-Linux hosts skip `--user` so Sentry cannot write issuer files": `--user` exists so a Linux host can read Sentry-created files; Docker Desktop without `--user` still writes and the files remain visible.
- `false` — "`DateReminderRegistration` accepts empty required strings and silently schedules": `DateReminderName.For` throws on whitespace; empty remoting values cannot reach `RegisterReminderAsync`.
- `false` — "`AppHostModelExposesTheExactCommandEventTopology` forces plaintext 6050/6060 against mTLS Sentry": that fact inspects explicit override forwarding; the default TLS pair is pinned in `DefaultAppHostComposesTheMtlsActorControlPlane`; reminder smokes do not pass those args.
- `false` — "this diff absorbs Story 1.5 catalog/VAL-H11 work": `ShouldBe(40)` matches the separately committed 1.5 catalog; 4.8 adds no catalog type; VAL-H11/sprint 1.5 hunks come from `0b38153` in shared File List docs.
- `false` — "`DateReminderRegistrationSerializationTests` in IntegrationTests / one-row cascade theory / mismatched skip reasons": the serializer facts run and pin remoting; the cascade helper still works with one Redis port; skip wording is cosmetic.
- rejected spec-edit — contradictory 403 vs 4/4 completion notes and spec-4-8 "no actor change" / catalog-37 frozen text: the fix is editing the spec/story under review.
- `low` — `DeleteReminderAsync` has no retry on the initial DELETE transport timeout: uncommon in this serialized live class; retry/backoff is more than a direct correction.

### Review Findings (2026-09-08, bmad-code-review, Group 1)

_Production reminder / index / recovery only (`ff329cc...HEAD` over `Reminders/`, dispatcher, parking, recovery/runtime, `Program.cs`; 24 files, +1589/−329). Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor. Groups 2–4 (AppHost/mTLS, tests, docs) were not in this diff._

- [x] [Review][Decision] A parked date-await candidate keeps every startup reconciliation incomplete — **Decided 2026-09-08 (human): skip parked keys in the scan.** Do not count them incomplete, do not tombstone the index, do not change the stream reader. — **Implemented 2026-09-08:** `ScanTenantAsync` reads `WorkItemProjectionParking` before `RebuildAsync`; a `Parked` candidate is skipped (no stream read, no `failedCandidateCount`) and logged as `PendingDateAwaitParkedCandidateSkipped`. A not-yet-parked parking document still rebuilds and can mark the scan incomplete. Proven by `IndexedPendingDateAwaitSourceTests.Skips_a_parked_candidate_without_reading_its_stream_or_marking_the_scan_incomplete` and `.Still_fails_a_candidate_that_has_parking_history_but_is_not_yet_parked`.

- [x] [Review][Patch] **MEDIUM** — `ProjectionParkedDispatchSkipped` (EventId 4503) is unreachable: once `Parked` is true the transform returns the document unchanged, so `FailureCount` stays equal to `maxFailures` and every later poller pass logs `ProjectionAggregateParked` at Error instead of the skip Warning [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:615-647] — resolved 2026-09-08: already-parked documents are detected before increment/write, acknowledged with EventId 4503, and leave `FailureCount` unchanged.
- [x] [Review][Patch] **MEDIUM** — payload identity mismatch throws out of `WorkItemProjectionEventDecoder.Decode` (and `NotSupportedException` is not caught there, unlike `WorksEventDecoder`); `ParkOrRetryAsync` never runs, so a permanently wrong-identity or STJ-unsupported state-affecting event 500s `/project` forever [src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:65-76] — resolved 2026-09-08: identity mismatch returns `Malformed`; `NotSupportedException` is caught like `WorksEventDecoder`; parking starts.
- [x] [Review][Patch] **MEDIUM** — reserved tenant id `tenants` is refused on `/project` and `/work/events` only; `WorkItemEventStoreAggregate` still accepts `/process` for that id, after which every poller dispatch throws before parking and the item never gets reminders or projections [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:29-31] — resolved 2026-09-08: every adapter `Handle` (and the `LinkConversation` envelope) refuses reserved tenant `tenants` before persist.
- [x] [Review][Patch] **MEDIUM** — `EnsureTenantRegisteredAsync` mutates `existing.Tenants` in place inside `ReadModelWritePolicy.UpdateAsync`, contrary to the copy-on-write rule the sibling pending-date index transform just adopted; an ETag retry can see a HashSet already changed by the failed attempt [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:689-693] — resolved 2026-09-08: registry transform builds a replacement `HashSet`.
- [x] [Review][Patch] **MEDIUM** — nothing asserts `WorksDomainEventProcessor` refuses `TenantId = "tenants"` before marker acquisition; deleting the branch leaves every processor test green [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:52-60] — resolved 2026-09-08: `Works_processor_rejects_reserved_tenant_before_marker_acquisition`.
- [x] [Review][Patch] **MEDIUM** — the built host never asserts `IPendingDateAwaitSource` is `IndexedPendingDateAwaitSource`; a no-op registration disables AC #2/#3 while deterministic source/reconciler tests stay green [src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs:72] — resolved 2026-09-08: built host resolves `IndexedPendingDateAwaitSource`.
- [x] [Review][Patch] **MEDIUM** — `Works:Projection` is bound and `ValidateOnStart`'d on the host, but no test binds an invalid budget or proves `/project` uses the bound value rather than `new WorksProjectionOptions()` defaults [src/Hexalith.Works/Runtime/WorksHost.cs:92-123] — resolved 2026-09-08: invalid Max fails ValidateOnStart; `/project` uses `IOptions` and a bound Max=1 parks on the first undecodable dispatch.
- [x] [Review][Patch] `WorkItemProjectionDispatcher` assigns EventId 4501 to `ProjectionDecodeFailed` after removing its local `SkippedEvent` logger, but `WorkItemProjectionEventDecoder.SkippedEvent` still uses EventId 4501, so skip and parking-retry telemetry collapse onto one id [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:48] — resolved 2026-09-08: decoder skip is EventId 4504.
- [x] [Review][Patch] Pending-date index, tenant-registry, and parking writes all set `ReadModelWriteContext.ProjectionType` to `WhatsNextProjectionType`, so conflict/exhaustion logs cannot be attributed to the document that actually failed [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:547-549] — resolved 2026-09-08: distinct `WorksReadModelKeys` tokens for index, registry, and parking.

- [x] [Review][Defer] No unpark, delete, or operator replay path for `projection:works:parked:{tenant}:{id}` — a later-fixed decoder still leaves the parking document in place [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:596-650] — deferred: parking's intended terminal disposition is the Error log plus "needs operator action"; an unpark/replay tool is a new ops feature, not an in-band 4.8 fix.
- [x] [Review][Defer] Shared rebuild never emits operations for `PendingDateAwaitIndexKey`, `PendingDateAwaitRegistryKey`, or parking keys [src/Hexalith.Works/Projections/SharedRebuild/] — deferred: already recorded 2026-09-07 against `spec-shared-rollup-reconciliation.md`; this Group 1 pass re-observed the same gap.
- [x] [Review][Defer] Index durability window + one-shot empty-registry success + retired tenant-wide scan [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:41-51] [src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:37-61] — deferred: already recorded 2026-09-07 (blind window / no backfill, and the ~5 s give-up). This pass re-observed the same AC #3 window: subscription registers immediately, `/project` writes the index later, empty registry is a clean `[]`, and the startup pass never runs again.
- [x] [Review][Defer] Equal-sequence `PersistRollUpAsync` can overwrite a concurrently merged child set [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:452-454] — deferred: roll-up child-reconciliation / F-PROJ-1 is out of this story's scope (DW-84 / shared-rebuild).
- [x] [Review][Defer] `UseCurrentSchemaAsync` is read once per dispatch and reused across later awaits [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:200] — deferred: documented generation-switch race in the same dispatcher rework; not Story 4.8 reminder/index behavior.

**Rejected:**
- `false` — "rejection-only `/project` deliveries permanently drop a live pending-date-await index entry": EventStore `/project` is a stateless full-replay contract; a stream that still contains `WorkItemSuspended` cannot arrive as rejection-only. An empty fold after a true full replay means the stream currently has no pending date awaits, so tombstoning a stale discovery entry is the stream-is-truth rule.
- `false` — "`storedLastSequence >= incoming` blocks a corrected same-sequence fold": that is the anti-resurrection watermark (first writer at sequence N wins). What's-next uses `>` for a different document; it does not make this guard wrong.
- `false` — "skip the pending-date tombstone unless a state-affecting event was decoded": under full replay an empty fold is current truth; the proposed guard would retain stale discovery after a real resume/terminal that decoded cleanly as an empty pending set.
- `false` — "blank `PendingDateAwait.CorrelationKey` throws after registration starts": `AwaitCondition.NormalizeDate` / `RequireKey` always set the ISO instant string; `DateReminderName.For` cannot see whitespace from a successfully decoded `WorkItemSuspended`.
- `false` — "empty folded set must unregister the Dapr reminder": Story Task 2 explicitly accepts stale reminders and actor orphan cleanup; do not build proactive unregistration.
- `false` — "registry containing tenant id `tenants` fails every reconciliation": `/project` refuses that id before `EnsureTenantRegisteredAsync`; the historical collision overwrote the registry document rather than adding the string to `Tenants`.
- `false` — "`WorksEventDecoder.Decode` must catch all `Exception`": the 4.8 change already catches `JsonException` / `ArgumentException` / `NotSupportedException`; unclassified failures must escape.
- `low`, not worth fixing — null `Entries` / `LastSequences` / `Tenants` on deserialized index/registry documents: production writes always supply new dictionaries/sets; a JSON-null document is corruption, and adding a validation surface is more than a direct correction.
- `low`, not worth fixing — `WorksEventIdentity.Matches` uses reflection and lacks `<param>` docs: no functional miss; cached accessors plus XML is extra surface for an internal helper.
- `low`, not worth fixing — persisted roll-up identity mismatch with a newer sequence blocks writes: keys already embed tenant + work-item id; the case is store corruption.
- `low`, not worth fixing — empty `EventTypeName` skipped instead of fail-closed: `IsStateAffectingEventType("")` is false; EventStore does not persist nameless lifecycle events.
- `low`, not worth fixing — `StreamReadPage.Events` / `Metadata` null: the contract type is non-nullable; null-guarding the gateway page is speculative.

### Review Findings (2026-09-15, bmad-code-review, Group 1 patch round)

_Group 1 host-edge patch round only (`6e2fb4d..HEAD` over `src` + `tests`; commits `0527d12`, `5c86eab`; 12 files, +575/−45). Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. Reviews the patches the 2026-09-08 round recorded as resolved._

- [x] [Review][Decision] **HIGH** — the reserved-tenant guard reads the payload tenant, but the envelope tenant is what keys the persisted stream — `RefuseReservedTenant` inspects `command.TenantId` only; `Handle(LinkConversation …)` is the one overload that also checks `envelope.TenantId`. `EventStoreAggregate.ProcessAsync` performs no envelope/payload cross-check, and `AggregateActor.cs:166` calls `tenantValidator.Validate(request.Command.TenantId, Host.Id.GetId())` — envelope tenant vs actor-id tenant, both envelope-derived. So `SubmitCommandRequest(Tenant: "tenants", Payload: CreateWorkItem(TenantId: "t1"))` passes the guard, persists under `tenants`, and every later `/project` dispatch throws at `WorkItemProjectionDispatcher.cs:114` *before* `ParkOrRetryAsync` — the unparkable poller 500-loop this round's parking machinery exists to prevent. Supersedes triage entry B7, which read `TenantValidator` as an envelope/payload check. Options: three-parameter `Handle` overloads carrying the envelope (public surface on 14 methods), or refuse the reserved id once where the envelope enters the host. [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:150-155] — **Decided 2026-09-15 (human): three-parameter `Handle` overloads.** Add `CommandEnvelope` as the third parameter to the 14 unguarded overloads exactly as `Handle(LinkConversation …)` already does (`EventStoreAggregate.DiscoverHandleMethods` accepts 3-parameter `Handle`), guard `envelope.TenantId`, and pin all 15 with a `[Theory]` whose envelope tenant deliberately disagrees with the payload tenant. → **patch**.
- [x] [Review][Decision] **HIGH** — the foreign-identity decode change has a second consumer and no telemetry — `Decode` now returns `Malformed` instead of throwing, and it is the only malformed return in the method that does not call `LogSkipped` (the four siblings at lines 40, 46, 57, 62 all do), so a cross-stream payload emits no EventId at all. `PendingDateAwaitProjection.IsStateAffectingEventType` covers 6 lifecycle types; for every other Works event `/project` now sets `malformedEvidence` and returns 200 where it previously hard-failed. The unreviewed consumer is `WorkItemSharedRebuildManifestBuilder.Build` (`SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs:59-69`), which maps `Malformed` to `MarkIncomplete(...); continue;` — so `/project/rebuild/shared/v1` now *commits* the authoritative current-schema tenant index from evidence it silently dropped, where it previously aborted with no manifest. It passes `logger: null`, so nothing is logged there either. `PendingDateAwaitStreamReader.cs:70` still throws on the same condition, so the two readers now disagree. **Note the direction:** `deferred-work.md:863` defect (4), recorded 2026-09-07, asks for exactly this change ("throws on one out-of-identity payload instead of marking that aggregate incomplete, aborting the whole tenant rebuild rather than degrading it") — so this is not plainly a regression, it is an out-of-scope change to another spec's component (`spec-shared-rollup-reconciliation.md`) that silently satisfies a recommendation nobody routed here, with no test, no log, and no mention in the commit. Options: accept it, pin it with a rebuild fact, and close the ledger item; or restore fail-closed for the rebuild path and leave the change to its own spec. Either way the missing `LogSkipped` is a straight correction. — **Decided 2026-09-15 (human): accept, pin, and close the ledger item.** Keep the mark-incomplete behavior, add the missing `LogSkipped` to the foreign-identity branch, add a rebuild fact accumulating a history event whose own identity disagrees with its aggregate, close `deferred-work.md:863` defect (4) with a pointer to this commit, and record the `/project` + rebuild behavior change in the story. → **patch**. [src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:65-71]
- [x] [Review][Decision] **MEDIUM** — a parked candidate is dropped from date-reminder recovery with no countable signal — the 2026-09-08 human decision ("skip parked keys in the scan") is implemented as intended, but the skip increments nothing on `TenantScanResult` (`Pending`, `FailedCandidateCount`, `LastFailure` are all untouched), so `DateReminderReconciler` sees a clean, complete pass that silently dropped real pending date-awaits. The only residue is `PendingDateAwaitParkedCandidateSkipped` at Information for an outcome meaning "this item's reminder will never fire". Options: add a skipped-parked counter to the scan result and raise the log level, or accept silence and record why. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:115-127] — **Decided 2026-09-15 (human): count it and raise the level.** Add `SkippedParkedCount` to `TenantScanResult` and surface it on the pass result so `DateReminderReconciler` can see that a clean pass still dropped real awaits; raise `PendingDateAwaitParkedCandidateSkipped` (4607) from Information to Warning. → **patch**.
- [x] [Review][Decision] **MEDIUM** — `/process` reserved tenant raises an unbounded throw at the ingress whose sibling was just made bounded — the same commit's whole argument for `/project` is that caller-supplied poison must park rather than fault forever, yet it adds 15 new `InvalidOperationException` sites for caller-supplied input with no bounded-failure equivalent and nothing establishing that the submission terminates rather than being redelivered. Decide throw vs rejection `DomainResult`. [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:150-155] — **Decided 2026-09-15 (human): keep the throw, document the asymmetry.** Verified this review: `EventStoreGatewayWorkCommandSubmitter` has no retry and `SubmitCommandHandler` converts exceptions into caller-visible failure responses, so `/process` is caller-driven — a throw is one response, not the `ProjectionPollerService` redelivery loop that makes an unbounded `/project` fault dangerous. A rejection `DomainResult` would also write a rejection event into a stream keyed by the very tenant id being refused. No code change; rationale recorded so the next reviewer does not re-raise it. → **no code change**.

- [x] [Review][Patch] **MEDIUM** — 14 of the 15 new `Handle` reserved-tenant guards are unpinned; deleting the guard from `Handle(SpawnChild …)` leaves the whole suite green, and the new `LinkConversation` envelope guard has no test at all [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:34-142] — resolved 2026-09-16: the canonical fifteen-command catalog now drives a reflection-dispatch theory whose ordinary payload is addressed by reserved envelope tenant `tenants`; every row must fail with the registry-collision exception. A companion fifteen-row theory proves ordinary matching envelopes still return the pure kernel result.
- [x] [Review][Patch] **MEDIUM** — `Works_processor_rejects_reserved_tenant_before_marker_acquisition` carries a vacuous assertion: the substitute is `IEventStoreDomainEventHandler<WorkItemSuspended>` but the dispatched envelope is `WorkItemCancelled`, so `handler.DidNotReceiveWithAnyArgs()` can never fail. It also overrides only `TenantId`, leaving the payload's own tenant, so with the branch deleted the post-acquisition identity check still rejects — only the `markerStore` line distinguishes the two causes [tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs:547-576] — resolved 2026-09-16: payload and envelope now both carry reserved tenant `tenants`, the registered substitute handles the matching `WorkItemCancelled`, and the marker explicitly returns `Acquired`; the fact proves the early guard prevents both marker acquisition and a handler that would otherwise run.
- [x] [Review][Patch] **MEDIUM** — the parked-document read sits inside the per-candidate `try`, so a transient state-store fault on the parking key is counted as a candidate scan failure and logged 4606 "candidate stream {WorkItemId} … could not be read", attributing a read-model fault to a stream that was never touched [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:113-117] — resolved 2026-09-16: parking lookup has its own failure boundary, emits bounded Warning 4608, never reads the candidate stream, and preserves partial typed-retry behavior; pinned by `Parking_lookup_failure_is_classified_without_reading_the_candidate_stream`.
- [x] [Review][Patch] **LOW** — `racedWithExistingPark` is declared outside a transform that `ReadModelWritePolicy.UpdateAsync` reruns per ETag attempt and is only ever set `true`, never reset — the exact hazard the sibling `EnsureTenantRegisteredAsync` hunk fixes in the same commit. Latent only because `Parked` is monotonic, an invariant nothing asserts; a stale flag would log 4503 Warning instead of 4502 Error on a genuine first park. Fix: `racedWithExistingPark = current is { Parked: true };` at the top of the transform [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:611-629] — resolved 2026-09-15: every retry invocation assigns the classification from the current persisted value before branching.
- [x] [Review][Patch] **LOW** — `Invalid_projection_parking_budget_fails_validate_on_start` asserts only `thrown.Failures.ShouldNotBeEmpty()`, so any other `ValidateOnStart` failure satisfies it and removing the parking-budget validation keeps it green [tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs:132] — resolved 2026-09-16: the fact requires the single exact validation message for `MaxUndecodableEventDispatchesBeforeParking`.
- [x] [Review][Patch] **LOW** — `A_not_supported_decode_failure_is_malformed_and_parks` proves its `NotSupportedException` half by calling `IsHandledDecodeFailure` directly — a tautology over the predicate under test — while the payload it actually parks throws `JsonException`, as its own comment concedes. Rename to what it pins, or drive a real `NotSupportedException` through `Decode` [tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs:404-435] — resolved 2026-09-15: renamed to describe the shared classification contract and retained the real malformed-JSON parking path.
- [x] [Review][Patch] **LOW** — `IsHandledDecodeFailure`'s XML doc says it "Mirrors `WorksEventDecoder`", but `WorksEventDecoder.cs:41` still inlines the identical `ex is JsonException or ArgumentException or NotSupportedException` filter and never calls the helper, so the two can drift with nothing failing [src/Hexalith.Works/Runtime/WorksEventDecoder.cs:41] — resolved 2026-09-15: the predicate now lives in `WorksEventDecoder` and both decoders call it.
- [x] [Review][Patch] **LOW** — the temp data-protection key directory leaks when `WorksHost.Build` or `app.DisposeAsync` throws; neither is wrapped in try/finally [tests/Hexalith.Works.IntegrationTests/WorksRecoveryOptionsTests.cs:194-230] — resolved 2026-09-16: build failure deletes in `catch`, and the disposable wrapper deletes in `finally` after application disposal.
- [x] [Review][Patch] **LOW** — `ParkOrRetryAsync`'s `<remarks>` still says "a failure at a different sequence restarts the count", which the new pre-read early return makes impossible once parked (the "nothing is read on the healthy dispatch path" clause is still accurate) [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:585-588] — resolved 2026-09-15: remarks distinguish pre-parking sequence resets from terminal parked delivery.
- [x] [Review][Patch] **HIGH** — _from Decision 1_ — add `CommandEnvelope` as a third parameter to the 14 unguarded `Handle` overloads and guard `envelope.TenantId`; pin all 15 with a `[Theory]` whose envelope tenant deliberately disagrees with the payload tenant [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:32-143] — resolved 2026-09-16: all fifteen wrappers use EventStore's supported three-parameter reflection shape and one helper refuses either the normalized payload tenant or envelope tenant before kernel delegation. `LinkConversation` retains its separate domain/tenant/aggregate identity checks.
- [x] [Review][Patch] **HIGH** — _from Decision 2_ — add the missing `LogSkipped` to the foreign-identity branch, add a shared-rebuild fact for a history event whose own identity disagrees with its accumulated aggregate, and close `deferred-work.md:863` defect (4) [src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:65-71] — resolved 2026-09-15: the live path emits bounded identity-specific EventId 4505; shared rebuild is pinned to incomplete degradation; only defect (4) is closed in the ledger.
- [x] [Review][Patch] **MEDIUM** — _from Decision 3_ — add `SkippedParkedCount` to `TenantScanResult` and the pass result; raise EventId 4607 from Information to Warning [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:115-127] — resolved 2026-09-16: the count flows through the explicit pass result, typed incomplete exception, 4605, and reconciler outcome; 4607 is Warning and the parked stream remains unread.

- [x] [Review][Defer] **MEDIUM** — DW-56 is `status: done 2026-09-05` in the ledger while this story asserts six times that it stays open [_bmad-output/implementation-artifacts/deferred-work.md:548] — deferred: pre-existing (closed before this round's baseline `6e2fb4d`), but this round re-asserted "DW-56 left open" as a verification claim in spec-3's Implementation Notes. The 2026-09-01 marker-store checkbox now cross-references a closed entry, so the one deliberately-unfixed review item has no live tracking. Needs a human call on whether the sweep bundle `dw-domain-event-processing-hardening` actually resolved it or DW-56 should be reopened; the fix edits tracking artifacts, not code.
- [x] [Review][Defer] **LOW** — the `/project` reserved-tenant guard is Ordinal on the raw `request.TenantId`, two lines before `new TenantId(...)` lowercases it via `AggregateIdentity.cs:28`, so a direct `POST /project` with `TenantId = "TENANTS"` passes the guard and reaches `PendingDateAwaitIndexKey("tenants")` [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:112] — deferred: pre-existing, already triaged as E1 (medium/defer). Narrower than E1 recorded: the `/process` guard does **not** have the hole because it reads the already-normalized `command.TenantId.Value`, and the EventStore poller sends lowercase stream identity, so only a direct post reaches it. The new shared helper was the natural place to normalize once.
- [x] [Review][Defer] **LOW** — moving decoder skip from EventId 4501 to 4504 is correct but ships as an undeclared telemetry break, and no registry or fitness test asserts the 45xx/46xx ids are unique [src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:121] — deferred: the fix is a telemetry-contract note in story docs plus a uniqueness guard, not an in-band code correction.
- [x] [Review][Defer] **LOW** — no unpark, delete, or operator replay path for a parked aggregate, now also excluding it from date-reminder recovery [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:596-655] — deferred: re-observation of the identical entry already deferred in the 2026-09-08 round ("an unpark/replay tool is a new ops feature, not an in-band 4.8 fix"). Recorded because this round raised the cost: parking now also removes the item from reminder recovery, not just `/project`.

**Rejected:**
- `false` — "the `?? new WorksProjectionOptions()` constructor fallback is dead now that `WorksHost` uses `GetRequiredService`": the 4-arg constructor's `options = null` default is still exercised by `PendingDateAwaitIndexDispatcherTests.NewDispatcher`, which passes three arguments.
- `false` — "commit `0527d12`'s message contradicts the code by claiming the skip marks the scan incomplete": the sentence reads "preventing [unnecessary retries] **and** [marking the scan as incomplete]" — both are prevented, which is what the code does.
- `low`, not worth fixing — the three new `ProjectionType` tokens are asserted by no test: `ReadModelWritePolicy` uses `ProjectionType` only in conflict/exhaustion log messages, so the relabel is diagnostics-only; pinning the string values is more than a direct correction.
- `low`, not worth fixing — commit `0527d12` claims "Updated documentation" while only the `IndexedPendingDateAwaitSource` XML `<remarks>` changed: commit-message nit with no effect on code or artifacts.

### Review Findings (2026-09-15, bmad-code-review, Group 1 production/runtime)

_Production/runtime chunk of `9526c31..HEAD` selected after the baseline diff proved too large for one reliable pass: 33 files, 4,677 diff lines. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. AppHost/topology, tests, and documentation/bookkeeping remain separate review chunks. Reconfirmed findings already open in the 2026-09-15 patch round are retained here because the current production code still exhibits them._

- [x] [Review][Decision] **MEDIUM** — a newer no-await replay can return before recording a watermark, then an older suspension can create a stale pending-date index entry. `MaintainPendingDateAwaitIndexAsync` first reads the index through `HasPendingDateAwaitHistoryAsync` and returns when the aggregate has never held an await; that read is not atomic with an older dispatch's later `UpdateAsync`. Stream re-folding prevents a wrong resume, but the stale discovery entry survives and is scanned on every recovery. — **Decided 2026-09-15 (human, after architecture, second-order, failure-mode, first-principles, and assumption-audit elicitation): derive tombstone necessity from the authoritative full replay.** A cleared replay that contains a historical `WorkItemSuspended` with a `DateReached` condition must enter the monotonic `UpdateAsync` even when the persisted index is initially absent; aggregates that never contained a date await still write nothing. This closes the ordering race without restoring all-aggregate document growth or tenant-wide contention. Pin both the initially-absent cleared replay and subsequent older-replay non-resurrection. → **patch**. [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:530-538]

- [x] [Review][Patch] **HIGH** — validate the reserved tenant against the command envelope on all 15 aggregate handlers, and pin mismatched envelope/payload tenants with a complete theory [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:32] — resolved 2026-09-16 by the fifteen-row canonical-catalog reflection theory and the shared payload-plus-envelope guard; the existing reserved-payload Create fact remains green.
- [x] [Review][Patch] **HIGH** — log foreign event identity, prove shared rebuild marks that history incomplete, and close the corresponding deferred-work defect [src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:65] — resolved 2026-09-15 with bounded identity-specific live telemetry and a logger-free incomplete-rebuild regression fact.
- [x] [Review][Patch] **MEDIUM** — surface a skipped-parked count from reminder discovery and raise EventId 4607 to Warning [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:121] — resolved 2026-09-16 with `PendingDateAwaitScanResult`, count propagation through clean/incomplete reconciliation, and Warning 4607 coverage.
- [x] [Review][Patch] **MEDIUM** — distinguish a parking read-model failure from a candidate stream-read failure instead of reporting both as EventId 4606 [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:113] — resolved 2026-09-16 with bounded Warning 4608, no candidate stream read, and no misleading 4606 emission.
- [x] [Review][Patch] **MEDIUM** — reject non-positive projection sequence numbers before they can mutate tenant membership or the pending-date index [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:130] — resolved 2026-09-15 with a pre-write gate and zero/negative no-write theory.
- [x] [Review][Patch] **MEDIUM** — emit the Task 2 bounded-metadata warning when steady-state reminder scheduling fails while preserving exception propagation for redelivery [src/Hexalith.Works/Reminders/WorkItemSuspendedReminderHandler.cs:55] — resolved 2026-09-16: bounded Warning 4609 precedes a bare rethrow of the original scheduler exception; caller-attributable cancellation is rethrown without the warning.
- [x] [Review][Patch] **MEDIUM** — prove one suspension containing multiple `DateReached` conditions schedules every distinct deterministic reminder [tests/Hexalith.Works.IntegrationTests/WorkItemSuspendedReminderHandlerTests.cs:24] — resolved 2026-09-16: `Registers_every_distinct_date_await_from_one_suspension` pins both deterministic names, instants, and due times.
- [x] [Review][Patch] **LOW** — correct the swapped failure-count and sequence-number arguments in the parked-aggregate log [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:52] — resolved 2026-09-15 and pinned against count 3 / sequence 7 diagnostics.
- [x] [Review][Patch] **LOW** — suppress duplicate projection-change notifications when an identical full replay is accepted at the stored watermark [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:388] — superseded 2026-09-15 by the EC-03 human policy: accepted equal-watermark redelivery deliberately retries post-commit invalidation, permitting idempotent duplicates without adding an outbox or marker. A notifier-failure/identical-redelivery fact pins the at-least-once behavior.
- [x] [Review][Patch] **LOW** — prove permanent reconciliation failure stops at the configured maximum attempt count [tests/Hexalith.Works.IntegrationTests/ReminderReconciliationServiceTests.cs:12] — resolved 2026-09-16: `Permanent_failure_finishes_naturally_after_exactly_the_configured_maximum_attempts` pins exact attempts, natural completion, and bounded 4603 warnings.
- [x] [Review][Patch] **MEDIUM** — _from the concurrency decision_ — derive date-await history from the full replay so a cleared replay writes its higher watermark even when the index is initially absent; prove an older suspended replay cannot subsequently resurrect the entry [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:530] — resolved 2026-09-15 with authoritative replay history and monotonic tombstone coverage.

- [x] [Review][Defer] **MEDIUM** — equal-watermark roll-up replacement can discard concurrently merged child evidence or restore totals from an older clean prefix [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:446] — deferred: roll-up convergence is outside Story 4.8 and already belongs to F-PROJ-1/shared-rebuild work.
- [x] [Review][Defer] **HIGH** — shared rebuild leaves an old current-schema roll-up readable when a rebuilt manifest member produces no replacement model [src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs:134] — deferred: introduced by the separately committed shared-rollup reconciliation work, not this reminder story.
- [x] [Review][Defer] **HIGH** — shared rebuild records relationship edges before the projection accepts the event sequence, allowing duplicate/rejected evidence to create ghost edges [src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs:59] — deferred: introduced by the separately committed shared-rollup reconciliation work, not this reminder story.
- [x] [Review][Defer] **HIGH** — child-completion and cascade stream readers advance the exclusive cursor by `last + 1` and forward continuation tokens the gateway rejects, skipping one boundary event per page [src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:152] — deferred: pre-existing Story 4.7/4.6 readers; the paging defect is already represented by DW-86 and needs both readers plus multi-page tests fixed together.
- [x] [Review][Defer] **HIGH** — the child-completion reader neither validates page domain nor fails closed on undecodable lifecycle evidence [src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:137] — deferred: Story 4.7 recovery surface outside Story 4.8.
- [x] [Review][Defer] **HIGH** — the cascade reader validates neither page/payload identity nor malformed `ChildSpawned` evidence before omitting a descendant [src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs:62] — deferred: pre-existing Story 4.6/4.7 recovery surface outside Story 4.8.
- [x] [Review][Defer] **MEDIUM** — cascade terminal-state discovery performs serial per-child roll-up reads that repeatedly reload the tenant manifest, creating an N+1 recovery path [src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs:114] — deferred: separately committed cascade/shared-rollup performance work.
- [x] [Review][Defer] **HIGH** — the production Dapr event marker's `TryAcquireAsync` does not persist an in-progress lease, so concurrent delivery of one message can execute handlers twice [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:62] — deferred: requires the EventStore marker protocol/cross-repository ownership rather than a Story 4.8 patch.
- [x] [Review][Defer] **LOW** — a hand-crafted `/project` request with mixed-case `TENANTS` passes the raw ordinal reserved-id check and is then canonicalized to `tenants` [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:114] — deferred: pre-existing, direct-endpoint-only issue already recorded by the prior 2026-09-15 review.
- [x] [Review][Defer] **MEDIUM** — deterministic host composition does not pin the three terminal-event handlers registered for cancellation, expiry, and completion [tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs:109] — deferred: Story 4.7 registration coverage, outside the reminder slice.
- [x] [Review][Defer] **MEDIUM** — `global.json` pins SDK 10.0.401 while the architecture fitness test still asserts 10.0.400 [tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:16] — deferred: unrelated SDK/tooling drift already tracked outside Story 4.8.

**Rejected:**
- `false` — stale `racedWithExistingPark` can misclassify a genuine first park: parking is monotonic and there is no unpark/delete path, so once any retry observes `Parked=true`, a later retry cannot become the first park.
- `low`, not worth fixing — projection descriptors do not compare payload `AggregateId` themselves: runtime adapters already perform the complete identity check; only direct developer use reaches this gap, and expanding every pure descriptor's surface is disproportionate.
- `false` — the production chunk contains no test changes: tests were intentionally placed in a later chunk after the oversized baseline diff was split; corresponding test files exist.
- `false` — Story 4.8 itself caused the child-completion/shared-rebuild scope expansion: Git history attributes those files to separate Story 4.7 and shared-rollup commits inside the broad `9526c31..HEAD` baseline.

### Review Findings (2026-09-16, bmad-code-review, un-reviewed remediation bundle)

_Scope: `bbfacbb..HEAD` (HEAD `e54c6a1`) filtered to this story's own File List — 33 files, +1916/−241, 3181 diff lines. Covers the three remediation commits that closed the 2026-09-15 rounds (`bd76a9b`, `1100405`, `9f29491`) plus the two submodule-pointer commits that followed them (`6fe8899`, `e54c6a1`). Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 37 raw findings triaged to 22 entries._

- [x] [Review][Decision] **Startup recovery telemetry lost its cause, at one of seven sibling call sites** — `ReminderReconciliationService.cs:48-52` now catches `Exception` without binding it and calls `WorksRecoveryLog.RecoveryStepFailed(_logger, "startup-reminder-reconciliation")` with no exception. It is the only one of seven `RecoveryStepFailed` call sites that drops it (`DateReminderActor.cs:88`, `CascadeRecoveryService.cs:31`, `CascadeRecoveryReconciler.cs:95`, `StreamReadingCascadeDescendantSource.cs:107,167`, `StreamReadingChildCompletionAwaitingParentSource.cs:69` all still pass it), so one EventId 4603 template now emits two different payload shapes. `WorksRecoveryLog.PendingDateAwaitScanIncomplete` also dropped its `Exception?` parameter outright, and the new 4608/4609 records pass compile-time constants (`"parking-read-failed"`, `"scheduler-failure"`) that merely restate their own EventId names. Net effect: a submit/schedule failure on an otherwise clean scan, and a parking-document read failure, now have **zero recorded cause anywhere** — `ReminderReconciliationService` retries `ReminderReconciliationMaxAttempts` times and then returns silently, so a host can come up with date-reminder recovery permanently broken and nothing says why. The stated rationale ("gateway/store exception text can contain unbounded details") is **not** forced by any gate: `P0_RuntimeAdapterLogsOnlyBoundedMetadataNeverPayloads` bans payload-naming *placeholders* in log templates, not attached exceptions, and 4604/4606 still attach theirs and pass. `DateReminderReconciler.cs:53-55` still comments that the log preserves "failed-tenant count **and cause**" — now false. Options: (a) restore the exception at `ReminderReconciliationService.cs:52` and re-add `Exception?` to 4605/4608/4609, keeping the bounded *fields*; or (b) keep exception-free telemetry here and strip it from the other six for one consistent 4603 contract. [src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:48-52] — **Decided 2026-09-16 (human): restore the cause, and make the bounded reason mean something.** `ReminderReconciliationService` binds and attaches the exception to 4603 again, matching the six sibling callers; `PendingDateAwaitScanIncomplete` (4605) regained its `Exception?` parameter; 4608 and 4609 now attach the caught exception and carry `exception.GetType().Name` as `Reason` instead of a constant that restated the EventId. The bounded *fields* are unchanged and the exception rides in the structured exception slot, never the message template — `Message.ShouldNotContain(expected.Message)` still holds at 4609 and a new `ShouldNotContain("Injected read failure")` holds at 4608. The six other 4603 callers were left alone. → **patch, applied**.
- [x] [Review][Decision] **Three submodule pointers moved after the recorded gates, against spec-5's frozen Never list** — spec-5 says **Never** "update submodules", yet `6fe8899` moves `references/Hexalith.EventStore` and `references/Hexalith.FrontComposer` and `e54c6a1` moves `references/Hexalith.Conversations`, both committed after `9f29491`. These are ProjectReference-compiled dependencies, so the gates recorded for this bundle (build 0/0; Unit 568, Property 3, deterministic Integration 377; Architecture 236/237) describe a tree that no longer exists — EventStore advanced 6 commits / 13 files (+359/−53) after the runs. Live evidence is older still: the only live proof is 4/4 in 957.974 s from the 2026-09-08 close-out against EventStore `8745b14b`, three production bundles ago, so AC #1's "resumes when the date fires without requiring a host restart" and AC #3's live SM-1 lane have no evidence on this code. The most exposed change is the fifteen `Handle` signatures moving to EventStore's three-parameter reflection shape — the actual runtime dispatch contract — verified only in-process. The working tree also still carries two further uncommitted submodule moves (`references/Hexalith.EventStore`, `references/Hexalith.FrontComposer`). Options: revert the pointer moves and keep the recorded gates authoritative; or re-run the deterministic gates (and ideally the live lane) on `e54c6a1` and re-record. [references/Hexalith.EventStore] — **Decided 2026-09-16 (human): keep the pointers, re-record the gates, state the deviation.** Every deterministic gate was re-executed on HEAD `e54c6a1` and is unchanged (build 0/0; Unit 568, Property 3, deterministic Integration 377, Architecture 236/237 with the pre-existing SDK-pin failure), then re-run again after this review's telemetry change with identical results. The only Works-facing EventStore change across the boundary is `IEventStoreGatewayClient.GetCommandStatusAsync` gaining a fail-closed default interface implementation — additive, not source-breaking. Recorded in `tests/test-summary.md` under the 2026-09-16 re-verification heading, including the explicit note that this deviates from spec-5's "never update submodules". The live lane is untouched and gives this bundle no new credit. → **no code change; evidence re-recorded**.
- [x] [Review][Decision] **Story and sprint disagree on status, and the sprint edit was explicitly out of scope** — `sprint-status.yaml:78` reads `4-8-register-and-reconcile-date-reminders-durably: review` with `last_updated: 09-16-2026 # Story 4.8 reminder recovery/scheduling hardening verified; status advanced to review`, while the story's own `Status:` line still reads `in-progress`, this bundle's Completion Notes say "Story 4.8 remains in progress for the separate reminder bundle", the Change Log says "Story 4.8 remains in progress", and the test-summary section it adds says "Story 4.8 and the sprint entry remain `in-progress`". The reserved-tenant spec's frozen Never list says **never** "change Story 4.8 sprint status while that bundle remains open". `sprint-status.yaml` appears in none of the three dated File List blocks this bundle adds, and in no spec's Code Map or task list; `deferred-work.md`, which the same commits appended three entries to, is likewise absent from the 2026-09-16 File List blocks. The sprint board is the input every downstream BMad skill reads, so this is not cosmetic. Options: revert the sprint entry to `in-progress` and let this review's outcome drive it; or ratify `review` and correct the story `Status:` line, Completion Notes, Change Log, and test-summary to match. [_bmad-output/implementation-artifacts/sprint-status.yaml:78] — **Decided 2026-09-16 (human): ratify `review`, fix the artifacts that contradict it.** The file's own STATUS DEFINITIONS make `review` mean "Ready for code review (via Dev's code-review workflow)", which is precisely the state that licensed this run, and this workflow sets the status from the review outcome regardless. The two Completion Note/Change Log sentences asserting "remains in progress" were corrected, and `sprint-status.yaml` plus `deferred-work.md` were added to the 2026-09-16 File List blocks that omitted them. The Never-list deviation is recorded rather than reverted. → **no code change; artifacts reconciled**.

- [x] [Review][Patch] Authoritative docs describe behavior this bundle deleted — `HasPendingDateAwaitHistoryAsync` no longer exists anywhere in `src/`, but the boundary record still says the index write is guarded when "the index already carries an entry or watermark for that aggregate"; the API-surface record still says "one `public static DomainResult Handle(TCommand, WorkItemState?)` wrapper per Works command" with "`LinkConversation` is the envelope-aware exception" although all fifteen are now three-parameter and envelope-aware; and the boundary record still says the catalog "stayed **37** at Story 4.8 completion" where the sibling doc was reconciled to 40 [docs/boundary-decision-record.md:294,316-318] — resolved 2026-09-16: both records now describe authoritative full-replay history gating, all fifteen envelope-aware wrappers, the `LinkConversation`-only full identity check, and current catalog 40 / Story 4.8 delta zero.
- [x] [Review][Patch] A parked aggregate's date reminder never fires again, and no document says so — the parked-candidate skip is terminal (no unpark/delete/replay path exists in `src/`), so an item suspended on a `DateReached` whose projection parks is permanently exempt from AC #3's "overdue awaits are reissued". The index-growth tradeoff got an "Accepted limitation" paragraph; this consequence got none, and `docs/operations/subscriber-dead-letter-operator.md` — the only operator runbook — documents none of 4605/4607/4608/4609, so a 4607 Warning meaning "this item's reminder will never fire" arrives with no documented action. spec-5 forbids adding unpark/replay, so the in-scope fix is the documentation [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:138-145] — resolved 2026-09-16: the boundary/API records state the permanent reminder consequence, and the operator guide gives concrete responses for 4605/4607/4608/4609 without adding replay behavior.
- [x] [Review][Patch] Cross-tenant parked-skip aggregation is pinned by no test — all four parked-count facts seed exactly one tenant via `SeedAsync`, and the two multi-tenant facts have no parked candidates, so replacing `skippedParkedCount += tenantScan.SkippedParkedCount` with `=` keeps the whole suite green. In a multi-tenant host the 4605 warning and the incomplete-scan message would then report only the last tenant's skips [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:72] — resolved 2026-09-16: clean and incomplete two-tenant facts pin additive parked counts while proving parked streams remain unread.
- [x] [Review][Patch] The new 4609 scheduling-failure rule was applied to the steady-state handler but not to the recovery reconciler — `DateReminderReconciler.ProcessAsync` calls the same `ScheduleResumeReminderAsync` on the same folded `PendingDateAwait` and logs the same success event, but has no failure branch, so a scheduler fault there escapes with no tenant, work item, or reminder name. Recovery is the path that runs after a crash against a possibly cold actor runtime. No test in the repo drives a throwing scheduler through the reconciler [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:126-134] — resolved 2026-09-16: recovery scheduling now emits bounded 4609 with the structured cause and bare-rethrows; ordinary, exact-caller, and foreign-cancellation facts pin the policy.
- [x] [Review][Patch] The cancellation-attribution fix landed at one of three sites in the same bundle — `WorkItemSuspendedReminderHandler.cs:67-69` now matches `ex.CancellationToken == cancellationToken`, but all three `OperationCanceledException` filters in `IndexedPendingDateAwaitSource` (tenant loop, the newly added parking-read catch, and the stream-read catch) still test only `cancellationToken.IsCancellationRequested` [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:75,126,159] — resolved 2026-09-16: all three filters require caller cancellation plus exact token equality; foreign tenant-index, parking, and stream cancellations remain typed incomplete-scan failures at their correct 4604/4608/4606 boundaries.
- [x] [Review][Patch] The reserved-tenant guard silently couples all fifteen adapters to full identity validation — `RefuseReservedTenant` ends with `ThrowIfReservedTenantId(envelope.AggregateIdentity.TenantId)`, and `CommandEnvelope.AggregateIdentity` is a computed property that constructs a fresh `AggregateIdentity`, whose constructor regex-validates tenant, domain **and** aggregate id. `CommandEnvelope`'s eager validation is explicitly bypassed by `DataContractSerializer` (its own comment says so), so a remoted envelope with a malformed `Domain`/`AggregateId` now throws `ArgumentException` out of fourteen wrappers that were previously envelope-blind — a different exception type than the guard documents, pinned by no test. `envelope.TenantId?.ToLowerInvariant()` gives the same normalization without the coupling [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:157] — resolved 2026-09-16: the shared guard normalizes only `envelope.TenantId`; 28 catalog-driven rows prove malformed domain/aggregate fields preserve all fourteen ordinary kernel results while `LinkConversation` retains explicit full identity checks.

- [x] [Review][Defer] A permanently failing scheduler has no terminal disposition, and the first failing await blocks its siblings [src/Hexalith.Works/Reminders/WorkItemSuspendedReminderHandler.cs:63-81] — deferred: spec-5's matrix freezes "rethrow the original exception" and its Never list forbids "a new exhaustion policy", so both the unbounded redelivery and the per-await ordering need their own spec.
- [x] [Review][Defer] `deferred-work.md` still carries the reserved-tenant envelope entry and the reminder recovery/scheduling entry as open (the latter twice, once with `source_spec: none`) although this bundle implements all three [_bmad-output/implementation-artifacts/deferred-work.md:924] — deferred: the fix edits the tracking ledger, not code; a sweep will otherwise reschedule finished work.
- [x] [Review][Defer] No non-reminder EventId 4603 call site pins whether its record carries an exception [src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:31] — deferred: filed `defer` by the verification-gap layer; belongs with the existing bounded-recovery-telemetry ledger item for 4604/4606 rather than reopened here. Settled by whichever way the 4603 decision above resolves.
- [x] [Review][Defer] `ArchitectureTests` is red at 236/237 — `global.json` pins SDK `10.0.401` while `BuildConfigurationTests.cs:16` asserts `10.0.400` [tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:16] — deferred: pre-existing tooling drift, honestly recorded and already tracked outside Story 4.8, but the gate is not green.
- [x] [Review][Defer] `IsReservedTenantId` is still `StringComparison.Ordinal`, so a hand-crafted `POST /project` with `TENANTS` passes the guard and is then canonicalized to `tenants` [src/Hexalith.Works/Projections/WorksReadModelKeys.cs:69] — deferred: pre-existing, already recorded at `deferred-work.md:888`, and the reserved-tenant spec's Never list explicitly puts mixed-case direct `/project` requests out of scope. The command path is normalized and does not have the hole.
- [x] [Review][Defer] `Handle(LinkConversation …)` compares the raw `envelope.TenantId` ordinally against `command.TenantId?.Value`, which `AggregateIdentity` has already lowercased, so a mixed-case envelope tenant is refused for `LinkConversation` alone while the other fourteen accept it [src/Hexalith.Works/WorkItemEventStoreAggregate.cs:113-115] — deferred: pre-existing; this bundle did not touch those lines, it only added the normalized reserved-tenant guard above them.

**Rejected:**
- `low`, not worth fixing — the tombstone rule can be defeated by an unknown event type: `hasDateAwaitHistory` folds `decodedEvents` only, so a forward-version suspension is skipped without parking and a cleared replay writes no tombstone. Requires a schema version that does not yet exist, and the only fix is to re-consult the persisted index — exactly the read the recorded 2026-09-15 human decision removed ("Do not consult persisted index state to make this decision").
- `low`, not worth fixing — the non-positive-sequence gate throws before `ParkOrRetryAsync` can engage, so a redelivered poison request would 500 forever on the poller path. `SequenceNumber <= 0` requires a corrupt or hand-crafted request; already rejected as production-unreachable by this story's own BH-03 triage, and routing it through parking adds branches.
- `low`, not worth fixing — the resolution note at line 885 cites `Parking_lookup_failure_is_classified_without_reading_the_candidate_stream`, but the test is actually `Parking_lookup_failure_is_classified_while_later_candidates_remain_partial_results` (`IndexedPendingDateAwaitSourceTests.cs:423`), so the evidence pointer dead-ends. Real, but the fix edits this story file — the spec under review.
- `low`, not worth fixing — `HostKeyDirectory.Dispose` deletes the key directory in a `finally` with no guard, so an `IOException` there would replace a real disposal failure in test output. Test-ergonomics only.
- `false` — the governance token list omits the new reminder types: `IndexedPendingDateAwaitSource`, `WorkItemSuspendedReminderHandler`, `DateReminderReconciler`, `PendingDateAwaitStreamReader`, `WorkItemProjectionParking` and `WorksProjectionOptions` are all present in `RuntimeAdapterGovernanceTests.cs:72-93`. The genuinely absent names (`PendingDateAwaitScanResult`, `ReminderReconciliationOutcome`, `PendingDateAwaitScanIncompleteException`) are plain DTO and exception types; the list guards runtime coupling (actors, gateway, store), not data shapes.
- `low`, not worth fixing — `ProcessAsync_preserves_kernel_results_for_every_ordinary_command` passes `currentState: null`, so fourteen of fifteen rows compare a rejection-against-uninitialized-state result on both sides. Adding real state to every row is more than a direct correction, and a wrapper that dropped `state` would break many other facts.
- `false` — the parked-skip count is invisible on a clean pass: `PendingDateAwaitParkedCandidateSkipped` (4607, Warning) is emitted per skipped candidate on **every** path at `IndexedPendingDateAwaitSource.cs:143`, so operators see each skip regardless of outcome. What remains is that `ReminderReconciliationOutcome.SkippedParkedCount` has no production consumer (`ReminderReconciliationService.cs:41` discards it) — but spec-5's matrix requires the count be exposed on clean and incomplete outcomes and its Design Notes explicitly decline a second aggregate log, so the surface is spec-mandated.

### Review Findings (2026-09-16, bmad-code-review, six-finding close-out bundle)

_Scope: `e54c6a1..HEAD` (HEAD `5387ff6`) — the two commits that closed the 2026-09-16 un-reviewed-remediation round (`06d64b0` telemetry, `5387ff6` six-finding close-out). 20 files, +1140/−60, 1639 diff lines. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 38 raw findings triaged to 11 entries plus 16 rejections. The story's frontmatter `baseline_commit: 9526c31` is stale (151 commits / 3482 files) and was not used._

- [x] [Review][Decision] **The cancellation-evidence fix stops at `OperationCanceledException`, so the identical masking survives for every other failure type** — `IndexedPendingDateAwaitSource.cs:69` still calls `cancellationToken.ThrowIfCancellationRequested()` **outside** the `try` that opens at line 70, and all four new breaks (`:77`, `:93`, `:150`, `:192`) require `ex is OperationCanceledException`. Reachable sequence: tenant A's index read fails with a non-cancellation exception (`HttpRequestException`, `TimeoutException`, a store fault) → caught at `:88`, `failedTenantCount = 1`, 4604 logged, no break → the caller token is canceled → tenant B's `:69` throws a bare exact-caller `OperationCanceledException` that escapes `GetPendingDateAwaitsAsync` entirely, bypassing the typed throw at `:100-108`. `pending`, all three counts and `lastFailure` are discarded; `DateReminderReconciler.cs:48` catches only `PendingDateAwaitScanIncompleteException`, so the bare OCE reaches `ReminderReconciliationService.cs:44-46`, which classifies it as clean shutdown and returns with **no 4603 and no 4605**. The same shape exists one level down: a non-OCE candidate failure (`:187`) followed by an exact-caller OCE from the next `GetAsync` (`:139`) or stream read (`:181`) rethrows and takes that tenant's accumulated `pending` and counts with it. `ScanTenantAsync` also keeps a single `lastFailure` slot (`:148`, `:190`), so a foreign OCE recorded before the caller cancels is overwritten by a later ordinary failure, after which `:77` sees a non-OCE `LastFailure` and continues into the same `:69` discard. This is the BH-1/EC-1 defect class the bundle exists to close, half-closed. Mitigating: the sole production caller passes the host stopping token, so the loss is shutdown-only diagnostics — the pass is idempotent and repeats next boot. All five new cancellation facts use an `OperationCanceledException` as the *first* failure, so none covers this ordering. Options: (a) broaden the guard to `lastFailure is not null && cancellationToken.IsCancellationRequested` and move `:69` inside the `try`, suppressing genuine caller cancellation in favour of the typed incomplete result whenever any failure was already recorded; or (b) keep exact-token cancellation authoritative and accept that evidence recorded before a shutdown is dropped, documenting it as the frozen posture. spec-6's **Always** says "Preserve … partial-result reconciliation" but its cancellation rule ("attribute cancellation to the caller only when both the caller token is canceled and the caught `OperationCanceledException` carries that exact token") points the other way — the two constraints collide here and only a human can pick. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:69] — _blind-hunter+edge-case-hunter+verification-gap+acceptance-auditor_ — **Decided 2026-09-17 (human): close the between-tenant leak only, and keep the exact-token filters.** Option A: `:69` becomes a cancellation check that breaks to the typed throw when `failedTenantCount > 0 || failedCandidateCount > 0` and otherwise throws bare as today, so the diversion fires only where `:100-108` was already going to throw. The three exact-token filters added by the 2026-09-16 bundle (`:82`, `:139`, `:181`) are left as ratified, so the in-tenant ordering (a non-OCE candidate failure followed by an exact-caller cancellation inside the same tenant scan) stays open and is recorded in the class doc rather than fixed — closing it costs five sites and reverses a four-day-old decision. Because Option A turns shutdown-after-failure into a *typed* exception the service treats as retryable, `ReminderReconciliationService.cs:62-65` gains the shutdown guard its sibling filter at `:44` already has, so the retry `Task.Delay` cannot escape `ExecuteAsync` as a spurious faulted-BackgroundService log. The overstated sentence at `:30-32` is trimmed to match. — **Resolved 2026-09-17:** the between-tenant guard, typed-evidence and clean-cancellation facts, retry-delay exact-token guard, and narrowed class documentation implement the ruling; the in-tenant limitation remains explicitly deferred.
- [x] [Review][Decision] **The recovery submit branch is the one failure path in this bundle with no bounded telemetry, and the new runbook tells operators to look for it** — this bundle added 4609 to the reconciler's scheduler branch (`DateReminderReconciler.cs:135-156`: bounded tenant/work-item/reminder-name + structured cause, then bare rethrow), closing the prior round's "applied to the steady-state handler but not to the recovery reconciler" finding. The sibling due-now branch at `:119` is still a bare `await _submitter.SubmitAsync(submission, cancellationToken)` with no failure log, so a submit fault during recovery escapes carrying no tenant, work item, or reminder identity — it reaches 4603 with the exception attached but nothing bounded to search on. The new 4605 runbook row explicitly names "a later **submit** or schedule failure can stop processing", pointing operators at a failure mode that emits no event of its own. Either branch throwing mid-loop also skips `DateRemindersReconciled` (`:167`) for that tenant, so partial `tenantReissued`/`tenantRescheduled` progress is lost and the remaining tenants are never processed. Options: (a) add a `DateResumeIssueFailed` EventId (4610) mirroring 4609's shape — new operator surface, absent from spec-6's Code Map and from every execution task; (b) reuse no event and record the asymmetry in `deferred-work.md` as a known recovery-diagnostics gap; or (c) reword the 4605 runbook row so it stops promising submit-failure evidence that does not exist. [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:119] — _blind-hunter+edge-case-hunter_ — **Decided 2026-09-17 (human): make the documentation true now, defer the surface.** Options 3 and 2 together, which are complementary rather than exclusive: the 4605 runbook row stops promising submit-failure evidence that no event emits and points at 4603 instead, and the asymmetry is filed in `deferred-work.md` beside the two open items it belongs with (the permanently failing scheduler with no terminal disposition, and the first failing await blocking its siblings). No 4610 now: adding an EventId outside spec-6's Code Map would hand the eventual exhaustion/ordering spec a surface it did not design. — **Resolved 2026-09-17:** the 4605 row now points to the new 4603 guidance, and the submit-telemetry asymmetry remains recorded with the scheduler exhaustion/ordering family without introducing EventId 4610.

- [x] [Review][Patch] The post-return cross-tenant break is pinned by no test and deleting it keeps the suite green [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:77-80] — resolved 2026-09-17: the redundant break is removed; clean and incomplete between-tenant shutdown facts pin both sides of the boundary policy.
- [x] [Review][Patch] Two ledger entries were appended by the same commits that falsified them, and a third was inverted [_bmad-output/implementation-artifacts/deferred-work.md:938,944,945] — resolved 2026-09-17 with append-only dated resolution/correction notes; the SDK-pin mismatch remains open with the accurate 237/238 count.
- [x] [Review][Patch] The new early-stop is not reflected in the telemetry that describes the scan [src/Hexalith.Works/Runtime/WorksRecoveryLog.cs:42,54] — resolved 2026-09-17: 4604/4606 now say later work remains eligible unless shutdown stops the scan.
- [x] [Review][Patch] The six-finding close-out File List omits three files that commit changed, including `sprint-status.yaml` again [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:539] — resolved 2026-09-17: `ReminderReconciliationService.cs`, `WorksRecoveryLog.cs`, and `sprint-status.yaml` are restored to that dated block.
- [x] [Review][Patch] No fact pins what a null, empty, or malformed envelope tenant now does after `AggregateIdentity` was dropped from the guard — constructing `AggregateIdentity` previously threw `ArgumentException` on an empty, >64-character, non-ASCII, or non-`^[a-z0-9]([a-z0-9-]*[a-z0-9])?$` tenant (`AggregateIdentity.cs:102-118`); `envelope.TenantId?.ToLowerInvariant()` validates nothing, so on the `DataContractSerializer` path — the one that bypasses `CommandEnvelope`'s eager `_identityValidated` field — all of those now reach the kernel. The 28-row `OrdinaryMalformedEnvelopeFixtures` theory mutates only `Domain` and `AggregateId`, so no row covers it. Behavior is benign as written (null/malformed -> `IsReservedTenantId` false -> kernel), and the 2026-09-16 human decision deliberately dropped envelope identity validation, so this is a pinning gap, not a regression to undo [tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs:57] — resolved 2026-09-17: 70 data-contract cases cover five malformed tenant shapes across all fourteen ordinary adapters and preserve kernel parity; existing LinkConversation identity facts remain green.
- [x] [Review][Patch] EventId 4603 is absent from the new operator table although this bundle changed its payload shape [docs/operations/subscriber-dead-letter-operator.md:122] — resolved 2026-09-17: the table now documents 4603 meaning, cause handling, retry exhaustion, and the submit-path diagnostic fallback.
- [x] [Review][Patch] `PendingDateAwaitScanIncomplete` is the one changed log helper in the hunk that got no doc block for its new `Exception?` [src/Hexalith.Works/Runtime/WorksRecoveryLog.cs:125-131] — resolved 2026-09-17: the helper documents every count plus the structured exception slot.

- [x] [Review][Defer] Reserved-tenant normalization landed at one of four ingresses [src/Hexalith.Works/Projections/WorksReadModelKeys.cs:69] — deferred: pre-existing. `IsReservedTenantId` is still `StringComparison.Ordinal`, and `WorkItemProjectionDispatcher.cs:114` and `WorksDomainEventProcessor.cs:52` pass un-normalized ids while `WorkItemEventStoreAggregate.cs:157` now lowercases, so the command adapter is stricter than the other two ingresses for the same reserved id. The underlying hole is already recorded at `deferred-work.md:888` and again at `:946`, and the reserved-tenant spec's Never list puts mixed-case direct `/project` requests out of scope. Normalizing inside `IsReservedTenantId` would close all four at once but widens behavior across three ingresses this story does not own.
- [x] [Review][Defer] `CascadeRecoveryService` keeps the loose cancellation filter this bundle replaced, and no test reads that path [src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:25] — deferred: pre-existing and outside spec-6's frozen scope. The structural twin of `ReminderReconciliationService` still reads `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` and swallows a genuine dependency fault as shutdown with no log at all; `CascadeRecoveryReconciler.cs:89` has the same filter and aborts the remaining index entries. Grepping `tests/` returns only two DI-descriptor assertions for `CascadeRecoveryService` and no behavioral test of `ExecuteAsync`. The existing ledger item covers exception *attachment* for 4603 only, so the cancellation-filter non-adoption is currently untracked.

**Rejected:**
- `false` — the dropped envelope-tenant shape validation lets a non-ASCII or over-length tenant render into the durable read-model key `projection:works:pending-date-await:{tenantId}`: that key is built in `WorkItemProjectionDispatcher.cs:117` from `request.TenantId` of the **projection request**, which is immediately wrapped in `new TenantId(request.TenantId)` and validated there — not from the command envelope. The command payload's tenant is likewise a `TenantId` value object validated in its own constructor. The envelope tenant reaches no Works key construction, so the collision consequence does not occur. The unpinned behavior half of this finding is kept as a patch above.
- `false` — reason codes changed from stable literals to exception type names breaks operator alerting: `git log -S` puts both `PendingDateAwaitParkingLookupFailed` (4608) and `DateReminderSchedulingFailed` (4609) in `9f29491`, the immediately preceding un-released commit. `"parking-read-failed"` and `"scheduler-failure"` never shipped, so no dashboard or alert rule can be keyed on them.
- `false` — the new `deferred-work.md` mixed-case deferral cites `AggregateIdentity` normalization this commit deleted: the claim is about the *payload* tenant at `WorkItemEventStoreAggregate.cs:156` (`tenantId(command)?.Value`), which is a `TenantId` value object normalized in its own constructor, not the removed `envelope.AggregateIdentity`. `deferred-work.md:888` states exactly that. The rationale still holds.
- `false` — the `sprint-status.yaml` comment claims a status advance that did not happen: the diff is comment-only and the `development_status` entry already read `review` before this bundle. The comment is a changelog line describing the bundle, and the superseded comment said the same thing.
- `false` — the boundary/API records deleted the catalog's 37→40 provenance, leaving 40 unexplained: the replacement text does explain it ("Story 4.8 added no durable catalog type and the current catalog remains **40**"). The 37→40 delta is Story 1.5's provenance and belongs in Story 1.5's record.
- `false` — "these numbers were re-run after the telemetry change and are unchanged" cannot hold: `06d64b0` touched four test files for +10/−8 and added **zero** `[Fact]`s (assertion edits only), so 377 → 377 holds. `5387ff6` added 12 facts plus 27 theory rows = 39, and 377 + 39 = 416 exactly.
- `low`, not worth fixing — the new fitness test's assertions are too generic to fail on a bad edit: the terms are asserted *within* the matched EventId's table row, so each row is still pinned to carry actionable verbs, and the cited counter-example is wrong ("Scheduler" does not substring-match "scheduling"). The `.Single(...)` diagnostics nit is real but test-ergonomics only, and rewriting nine rows of assertions is more than a direct correction.
- `low`, not worth fixing — the date-reminder section is off-scope for the subscriber dead-letter runbook: real topical mismatch, but the placement was the prior round's ratified choice, it is the only file in `docs/operations/`, and the fix (a new runbook plus retargeting the fitness test's section bounds) is more than a direct correction.
- `low`, not worth fixing — the payload-redaction section was not reconciled with attaching exceptions: the section's list (raw bodies, event payloads, body hashes, full command/event objects) does not name exceptions, so it is silent rather than contradicted, and the new section carries the mitigating instruction. The human decision of 2026-09-16 ratified attaching the cause.
- `low`, not worth fixing — the retry `Task.Delay` escapes `ExecuteAsync` on shutdown: confirmed real — `ReminderReconciliationService.cs:62-65` sits inside the `catch` at `:50`, so its `TaskCanceledException` is not caught by the sibling filter at `:44` and surfaces as a spurious "BackgroundService faulted" during shutdown. It needs a reconciliation failure *and* a shutdown inside the retry delay, and the fix adds another guard.
- `low`, not worth fixing — nothing constrains what the newly attached exceptions carry into sinks: the verification-gap layer filed this as a recorded trade-off rather than a defect. `RuntimeAdapterGovernanceTests` is a source-text placeholder scan by design and the runbook now states the policy explicitly.
- `low`, not worth fixing — `test-summary.md:3004` still says Story 4.8 "remains `in-progress`": the file is an append-only dated ledger (this bundle added 104 lines and deleted none), the sentence sits under the 2026-09-15 heading scoped "for the separate reminder recovery/scheduling bundle", and the very next heading is 2026-09-16. Rewriting it would falsify the historical record rather than correct a standing claim.
- `low`, not worth fixing — AC #4's kernel-preservation half is proven only against `currentState: null`: the prior round already triaged this `low` on the sibling `ProcessAsync_preserves_kernel_results_for_every_ordinary_command`. The "does not throw `ArgumentException`" half *is* genuinely proven, and giving all 28 rows real state is more than a direct correction.
- Fix edits the spec under review — the 4603/4608/4609 telemetry change has no execution task and no acceptance criterion in spec-6.
- Fix edits the spec under review — spec-6's Code Map never names `ReminderReconciliationService.cs`, which its own Spec Change Log says it changed.
- Fix edits the spec under review — spec-6's frontmatter carries `review_loop_iteration: 0` and `status: 'done'` while the file holds three Spec Change Log entries and a 17-row triage log.
- Fix edits the spec under review — spec-6's BH-7 rejection rationale still reads "the parent story and sprint remain `in-progress`", which the committed state contradicts.

### Review Findings (2026-09-17, bmad-code-review, spec-7 close-out)

_Scope: `3c042f9...HEAD` (HEAD `e5c173e`) — spec-7 nine-action close-out plus the following submodule gitlinks. 16 files, +566/−40, 900 diff lines. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 22 raw findings triaged to 2 decision, 5 patch, 3 defer, 8 rejected; both decisions resolved 2026-09-17 to patch (7 patch remaining). The story's frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] **Three submodule pointers moved in the reviewed range, against spec-7's frozen Never list** — spec-7 says **Never** "dependency/submodule updates", the 2026-09-17 File List omits `references/`, and `tests/test-summary.md` says no submodule pin changed, yet `3c042f9...HEAD` moves `references/Hexalith.Chatbot` `206bdd4`→`3c78799`, `references/Hexalith.Conversations` `8bdf026`→`c0abd5c`, and `references/Hexalith.EventStore` `27cc17f`→`fc43b4e` (`e5c173e`, after `0daddee`). EventStore advanced 3 commits / 20 files (+779/−197), including payload-protection work; Chatbot and Conversations are unrelated to this reminder close-out. The recorded gates describe `3c042f9`, not this tree. [references/Hexalith.EventStore] — _blind-hunter+acceptance-auditor_ — **Decided 2026-09-17 (human): keep the pointers, re-record the gates, state the deviation.** Spec-8 recorded the then-observed checkout separately. Superseded by spec-9: later commits adopted the current pointers, index and checkout now match, and the earlier revisions remain as transition history.

- [x] [Review][Patch] **The remaining between-tenant shutdown window is real, and spec-7 deferred it on the wrong site** — after a recorded failure, `IndexedPendingDateAwaitSource` preserves typed evidence only at the loop-head check (`:70-78`). `ScanTenantAsync` then issues the next tenant-index `GetAsync` with no local catch (`:121-123`); an exact-caller `OperationCanceledException` from that read is rethrown at `:88-92`, discarding earlier `pending`, counts, and `lastFailure`. That outer catch is the between-tenant exception path, not one of the three in-tenant filters (`:145-149`, `:187-191`, and the same outer `throw` for in-tenant rethrows). spec-7 AC requires typed evidence and no later tenant read; its BH-01/EC-01 deferral and `deferred-work.md:959` say preserving it "requires changing an exact-token in-tenant filter". It does not. The new tests cancel and then throw `InvalidOperationException`, so they never hit `:88-92`. [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:88-92] — _blind-hunter+edge-case-hunter_ — **Decided 2026-09-17 (human): close the exception path with the same rule as the loop head.** Resolved in spec-8: the outer exact-token catch breaks to the typed throw when either count is non-zero; the regression proves prior partials/counts/cause survive, the cancellation is not counted or logged, and the following tenant is not read. All three in-tenant filters remain unchanged.

- [x] [Review][Patch] Class remarks still say every eligible tenant is attempted and that between-tenant caller cancellation always preserves collected failure evidence [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:26-33] — resolved in spec-8: remarks now qualify shutdown at a tenant boundary and limit preservation to evidence already incorporated into cross-tenant results/counts.
- [x] [Review][Patch] The 4603 table row is reason-scoped, then the following paragraph treats 4603–4609 as startup-recovery evidence handled by the bounded retry policy; the fitness test only reads table rows [docs/operations/subscriber-dead-letter-operator.md:139] — resolved in spec-8: prose now distinguishes 4603/4609 non-startup origins from 4604/4605/4606/4608 startup-scan evidence, and the architecture test reads the normalized post-table guidance.
- [x] [Review][Patch] Operator 4604 and 4606 rows still tell operators to confirm a later startup/reconciliation attempt, without the shutdown caveat this bundle added to those log templates [docs/operations/subscriber-dead-letter-operator.md:131] — resolved in spec-8: later-attempt guidance is conditional on the host and retry budget, with explicit restart advice after shutdown or exhaustion; the row assertions pin both caveats.
- [x] [Review][Patch] Nested backticks in the 4603/4605 cells (`same-`Reason``) break the rendered field name; the architecture test matches the raw source [docs/operations/subscriber-dead-letter-operator.md:130] — resolved in spec-8: both cells use rendered `Reason` phrasing, and the architecture test rejects the broken nested form.
- [x] [Review][Patch] Restoring the old 4605 template ending `will retry` still keeps `DateReminderRecoveryRuntimeTests` green because it only asserts EventId, `"2 parked"`, and the structured exception [tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs:157] — resolved in spec-8: the runtime fact requires `remains eligible for retry under the configured recovery policy` and rejects `will retry`.

- [x] [Review][Defer] EventId 4603's runtime template still says the step "will be retried at-least-once" [src/Hexalith.Works/Runtime/WorksRecoveryLog.cs:36] — deferred: pre-existing shared template already recorded at `deferred-work.md` spec-5 "Distinguish final recovery exhaustion from retryable EventId 4603 failures"; this close-out newly fires it on shutdown-after-incomplete as well as final-attempt exhaustion. Changing the template is operator surface for all seven `Reason` values and is outside spec-7's Code Map.
- [x] [Review][Defer] Exact caller cancellation after a prior in-tenant candidate failure still discards that tenant's locals [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:145-191] — deferred: spec-7 Never list freezes in-tenant cancellation changes; already recorded at `deferred-work.md:957`.
- [x] [Review][Defer] `DateReminderReconciler` remarks still say an incomplete scan is rethrown so the hosted service retries it [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:33-38] — deferred: spec-7 Never list leaves `DateReminderReconciler` unchanged; the service now logs 4603 and can complete from the canceled retry delay without another attempt.

**Rejected:**
- `false` — EventId 4608's log template needs the new shutdown caveat: 4608 never claimed later tenants remain eligible; it only says the pass is signalled incomplete for retry, which remains true when the new loop-head guard stops the scan.
- `false` — non-empty due-now partials plus exact-token cancellation "drop the typed retry signal": `DateReminderReconciler` lets exact-caller OCE escape `ProcessAsync` by design, and the new 4605 runbook row states that exact caller cancellation ends quietly without 4603.
- `false` — the spec-7 ledger append is misplaced under the 2026-09-16 six-finding list: it is a new `source_spec` YAML item after that prose section, which is this ledger's normal form. The incorrect "in-tenant filter" rationale is carried in the decision item above.
- `low`, not worth fixing — `PendingDateAwaitTenantScanFailed` / `CandidateScanFailed` / `RecoveryStepFailed` still lack remarks describing shutdown vs retry: spec-7 only required documenting 4605's exception slot; adding sibling XML is optional commentary, not a direct correction of a shipped contract.
- `low`, not worth fixing — the 70-row data-contract theory omits reserved ids (`tenants`, `TENANTS`, `unidentified`): existing reserved facts still refuse those values on in-memory envelopes, and expanding the serializer matrix is extra rows rather than a one-line pin of the malformed-tenant AC.
- `low`, not worth fixing — the five non-startup 4603 `Reason` literals are only paraphrased in the new table cell: operators already have the `Reason` on the event they are triaging; listing cascade/actor strings in the reminder runbook is completeness, not an everyday miss.
- `low`, not worth fixing — no hosted-service fact drives `PendingDateAwaitScanIncompleteException` then cancels the retry delay: `Shutdown_during_the_retry_delay_finishes_cleanly_without_another_attempt` already pins 4603-then-stop for `Exception`, and incomplete scans take that same `catch (Exception)` path after the reconciler rethrows.
- Fix edits the spec under review — spec-7's frontmatter still has `review_loop_iteration: 0` while the file holds a 17-row triage log and a `done` status.

### Review Findings (2026-09-17, bmad-code-review, spec-8 close-out)

_Scope: `e5c173e...HEAD` (HEAD `28724f2`) — unreviewed since the last completed 4.8 review. 14 files, +574/−19, 788 diff lines. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 15 raw findings triaged to 1 decision, 3 patch, 0 defer, 7 rejected (6 appendix bullets; the two Aspire-result findings share one line); the gitlink decision resolved 2026-09-17 to patch. All four patches were closed by spec-9 on 2026-09-18. spec-8 is the review spec; the story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] **MEDIUM** — recorded gitlinks do not match HEAD, and the reviewed range moves submodule pointers against spec-8's frozen Never list — spec-8 **Never** forbids submodule-pointer updates and treats Chatbot `3c787993213ccf33f8912e6ad5caac605586fa15`, Conversations `d956c9b1de73bcf15969d5e1a6435d6d98a2dd49`, and EventStore `629168e3983e5a9cd1639013f39d758fb0068cac` as read-only evidence; AC4 requires exact gitlinks. `ea0590a` moved Conversations `c0abd5c`→`d956c9b` and EventStore `fc43b4e`→`629168e`. `28724f2` then moved Chatbot `3c78799`→`1047ef38d3845406227891639aaeb853e5d4f116` and EventStore `629168e`→`b5541259058320a0a7f1db19038709cbd02dad85`. **Resolved 2026-09-18:** the root index and all three checkouts match the adopted full SHAs; current-state artifacts use those values, while the old-to-new revisions remain as historical transition evidence. No submodule pointer changed in spec-9.

- [x] [Review][Patch] Clean next-index cancellation at the edited outer catch is untested: dropping the `throw` arm returns a successful scan [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:97] — resolved 2026-09-18 by an exact two-tenant final-index regression; focused class passed 29/29.
- [x] [Review][Patch] Operator 4608 still tells operators to confirm reconciliation succeeds with no host-lifecycle caveat, while this bundle added that caveat to 4604/4606 [docs/operations/subscriber-dead-letter-operator.md:135] — resolved 2026-09-18; the row and architecture guard pin host-running, retry-budget, shutdown, exhaustion, and restart guidance.
- [x] [Review][Patch] The in-tenant cancellation ledger citation ends at `:145-191` and misses the stream-read exact-token filter now at `:192-197` [_bmad-output/implementation-artifacts/deferred-work.md:967] — resolved 2026-09-18 by extending the append-only citation through line 197 without changing its decision.

**Rejected:**
- `false` — the outer exact-token catch converting later-tenant parking/stream rethrows after prior failure "changes in-tenant semantics": the three in-tenant filters still `throw`; the outer catch is the specified `ScanTenantAsync` boundary, and preserving already-recorded cross-tenant evidence is the Always rule. spec-8 BH-05 already declined extra pins for that interleaving.
- `false` — spec-8 left the DateReminderReconciler remarks deferral without a ledger item: it is already recorded at `deferred-work.md:968`; spec-8's YAML `source_spec` entries are only for spec-8's own new defers.
- `low`, not worth fixing — no hosted-service fact covers 4605's new "typed incomplete can still be followed by 4603 unless a partial operation propagates cancellation" sentence: source tests pin classification, reconciler tests pin exact-token escape vs typed rethrow (`DateReminderReconciler.cs:73-87`), and a new composition test for this shutdown interleaving is more than a direct correction. spec-8 BH-08 already rejected that pin.
- Fix edits the spec under review — spec-8 frontmatter still has `review_loop_iteration: 0` and `status: 'done'` while the file holds a 14-row triage log and a second Spec Change Log line.
- Fix edits the spec under review — spec-8 Implementation Notes record a pre-edit Aspire EventStore exit 134, while Executed results for the same command record Dapr Sentry `no space left on device`.
- Fix edits the spec under review — spec-8 Code Map/Implementation Notes still describe only ordinary tenant-index failure → next-read cancellation and never name the candidate-failure arrangement added by the independent review.

### Review Findings (2026-09-18, bmad-code-review, spec-9 close-out)

_Scope: `a292e3b...HEAD` (HEAD `3a29f59`). 10 files, +1,197/−328, 2,036 diff lines. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — verification-gap returned empty (`failed_layers`: Verification Gap Reviewer). 18 raw findings triaged to 0 decision, 3 patch, 4 defer, 11 rejected._

- [x] [Review][Patch] EventId 4608 restart-after-shutdown is not uniquely pinned: deleting `shutdown ended the pass or` from the restart sentence still leaves `shutdown`, `restart`, and `startup retries were exhausted` in the row [tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs:148] — resolved 2026-09-20: the complete restart sentence is now required verbatim in the 4608 row.
- [x] [Review][Patch] spec-9 close-out evidence says the `:145-197` citation covers all three in-tenant exact-token filters, but that range is the parking and stream filters only; the outer catch is at `:88-97` [_bmad-output/implementation-artifacts/tests/test-summary.md:3285] — resolved 2026-09-20: the summary now attributes the range only to the parking and stream filters.
- [x] [Review][Patch] The new clean final-index cancellation fact does not `Received()` the empty first-tenant index read, allows Information/Debug logs, and omits the sibling `DidNotReceive().ReadStreamAsync` pin [tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs:619] — resolved 2026-09-20: both exact-token index reads, no stream read, and no log entry are pinned.

- [x] [Review][Defer] Compacted ledger stubs and `deferred-work-archive.md` have no title, policy, backlink, or archive path [\_bmad-output/implementation-artifacts/deferred-work-archive.md:1] — deferred: already recorded at the spec-9 `source_spec` bullets in `deferred-work.md`; this review of `a292e3b...HEAD` reconfirmed the same discovery gap.
- [x] [Review][Defer] `Clean_shutdown_between_tenants_preserves_the_exact_caller_cancellation` still stubs and verifies with `Arg.Any<CancellationToken>()` [tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs:582] — deferred: pre-existing sibling fact; spec-9 only hardened the new final-index regression.
- [x] [Review][Defer] Story 4.8 still treats DW-56 as an open marker-store patch after the ledger archived it as `done 2026-09-05` [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:125] — deferred: pre-existing tracking contradiction already recorded at `deferred-work.md:792`.
- [x] [Review][Defer] 4604/4606 architecture pins still omit the retry-budget and exhaustion phrases that 4608 now asserts [tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs:92] — deferred: pre-existing; spec-9 required mirroring 4604/4606 wording into 4608, not tightening those older pins.

**Rejected:**
- `false` — File List omits `deferred-work-archive.md`: the dated four-patch File List is the close-out implementation inventory; the archive is not one of those four patches.
- `false` — ledger archive rewrite exceeds the citation-only task: the in-tenant cancellation decision is unchanged at `:145-197`; spec-9 already triaged the `5856fab` compaction as outside the four patches.
- `false` — archive headings for DW-20/53/54/55 are uniquely truncated: the live ledger uses the same truncated H3 titles.
- `false` — archive is an incomplete copy because it omits later DWs: open DW-58+ remain full on the live ledger; the archive holds the compacted done set.
- `false` — leftover `:82`/`:139`/`:181` and parent-story `:69`/`:70` citations are spec-9 defects: they are historical 2026-09-16 records; the required citation was updated to `:145-197`.
- `false` — 4608 “reads the candidate” is too vague: spec-9 AC/I/O/BH-14 require that exact phrase.
- `false` — gitlink transition histories disagree: current index/checkout SHAs agree; File List and test-summary use different accurate historical windows.
- Fix edits the spec under review — spec-9 Code Map still cites `deferred-work.md:967`.
- Fix edits the spec under review — spec-9 Implementation Notes say “all three” filters; the surviving patch is the test-summary claim only.
- Fix edits the spec under review — spec-8 BH-10 remaining `false | reject`.
- Fix edits the spec under review — I/O matrix has no fourth gitlink AC row.

### Review Findings (2026-09-20, bmad-code-review, File List increment `28724f2...HEAD`)

_Scope: `28724f2...HEAD` (HEAD `776b869`) filtered to Story 4.8 File List — 17 files, +1318/−382, 2435 diff lines. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 28 raw findings triaged to 2 decision, 6 patch, 8 defer, 12 rejected. The story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] **Live SM-1 harness and AppHost startup path changed without new Tier-3 evidence** — This increment mutates `WorksAppHostSmokeHarness` (IPv6 exclusive bind, Docker scheduler-volume probe, sentry/placement/scheduler health waits, 10-minute startup, bounded dispose) and AppHost `Program.cs` MSBuild environment variables, while `test-summary.md` 2026-09-20 records that no Tier-3 smoke lane ran and no new live evidence is claimed. Story AC #1/#3 and Task 5 are the live proof. **Decided 2026-09-20 (human): re-run Tier-3 on this checkout and record the exact results.** [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:92] — resolved 2026-09-20: after review hardening, the final focused reminder-recovery plus mTLS command passed **4/4**, zero skipped, in **1023.751s**; all live resources reached healthy state and the AppHost stopped cleanly.

- [x] [Review][Patch] **Aspire AppHost SDK moved 13.5.3 → 13.5.4 against spec-9 Never** — `global.json` and `Hexalith.Works.AppHost.csproj` bump `Aspire.AppHost.Sdk` to 13.5.4 and `BuildConfigurationTests` is rewritten to match. spec-9 frozen Never lists dependency versions. **Decided 2026-09-20 (human): keep 13.5.4 and record the spec-9 Never deviation.** [global.json:10] — resolved 2026-09-20: spec-9 Implementation Notes and Change Log now record the approved parent-story exception explicitly; the 13.5.4 pins remain unchanged.

- [x] [Review][Patch] IPv6-disabled hosts treat loopback bind failure as an occupied control-plane port [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:623] — resolved 2026-09-20: IPv6 address-family/address/protocol unavailability is treated as an inapplicable loopback probe while genuine bind conflicts remain occupied; a deterministic regression pins both sides.
- [x] [Review][Patch] Docker occupancy probe failures, timeouts, and running-only `docker ps` escape the 60-second retry loop [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:538] — resolved 2026-09-20: the probe uses `docker ps --all`; timeout and Docker-start/exit failures are classified as occupied diagnostics and retried within the shared monotonic budget; timed-out children and redirected reads are bounded/observed. Four deterministic Docker regressions cover command shape, transient recovery, exhausted diagnostics, and the real timeout/termination lifecycle.
- [x] [Review][Patch] CI/CD ledger evidence in this increment is already contradicted by the same diff [\_bmad-output/implementation-artifacts/deferred-work.md:904] — resolved 2026-09-20: evidence now names only the cascade and command-pipeline live starters that actually retain separate teardown paths.
- [x] [Review][Patch] Two spec-9 `source_spec` paths are machine-absolute [\_bmad-output/implementation-artifacts/deferred-work.md:940] — resolved 2026-09-20: both paths are repository-relative.
- [x] [Review][Patch] The DW-56 contradiction bullet still cites compacted line 548 [\_bmad-output/implementation-artifacts/deferred-work.md:792] — resolved 2026-09-20: the contradiction uses the stable `DW-56` heading rather than a drifting line number.
- [x] [Review][Patch] DW-58 still cites the removed reminder `IsPortReachableAsync` [\_bmad-output/implementation-artifacts/deferred-work.md:476] — resolved 2026-09-20: DW-58 is narrowed to the surviving command-pipeline probe and records that the shared reminder harness preserves caller cancellation.

- [x] [Review][Defer] Cascade and command-pipeline live starters omit the new occupancy and health waits [tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:543] — deferred: already recorded at `deferred-work.md:904` under the CI/CD routing item; this increment did not close it.
- [x] [Review][Defer] Compacted ledger stubs and archive still have no title, policy, backlink, or archive path [\_bmad-output/implementation-artifacts/deferred-work-archive.md:1] — deferred: already recorded at the spec-9 `source_spec` navigation item.
- [x] [Review][Defer] EventId 4608 still tells operators to restore a named parking key that the log does not emit [docs/operations/subscriber-dead-letter-operator.md:135] — deferred: already recorded at `deferred-work.md:944`; this increment preserved the pre-existing phrase while adding host-lifecycle text.
- [x] [Review][Defer] 4604/4606 architecture pins still omit the retry-budget and exhaustion phrases that 4608 now asserts [tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs:92] — deferred: already recorded at `deferred-work.md:898`.
- [x] [Review][Defer] DW-20/53/54/55 live-ledger headings remain truncated mid-word [\_bmad-output/implementation-artifacts/deferred-work.md:144] — deferred: already recorded at `deferred-work.md:940`.
- [x] [Review][Defer] spec-8 triage rewrote original `false | reject` rows to `superseded | patch` [\_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-8.md:104] — deferred: fix edits another spec's historical record; the current index/checkout facts remain in spec-9.
- [x] [Review][Defer] Killed MSBuild child uses unbounded `WaitForExitAsync(CancellationToken.None)` [tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs:333] — deferred: maybe-false; settle by showing `Kill(entireProcessTree: true)` can leave a live child on this runner.
- [x] [Review][Defer] EventStore and Admin nested hosts omit `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER` [src/Hexalith.Works.AppHost/Program.cs:92] — deferred: maybe-false; settle with a DCP command line showing those hosts still `dotnet run` after `SuppressBuild => false`.

**Rejected:**
- `false` — AppHost taking `Hexalith.EventStore.Aspire` as a Release `PackageReference` violates Story 4.8 Architecture Compliance: `DependencyDirectionTests.P0_AppHostReferencesOnlyWorksTopologyProjects` already requires the Release package and Debug project-reference split; this diff implements that pin.
- `false` — `DisposeAsync().WaitAsync(DisposalWait)` binds ports while DCP still owns them: bind failure is occupied, and the following 60-second occupancy wait is the intended settle path.
- `false` — harness never waits for `eventstore-operations`/`eventstore-admin`: SM-1 uses eventstore + works; operations `WaitFor(works)` and is not on the reminder acceptance path; the method comment still matches.
- `false` — harness no longer gives the acceptance body a time budget: `WaitForResumedCountAsync` defaults to a 90-second deadline and `PollToTerminalAsync` to 60 seconds.
- `false` — `RuntimeAdapterGovernanceTests` Debug/Release loop never evaluates the AppHost source/package split: Configuration already drives the Works host defaults; AppHost dual-mode is asserted in `DependencyDirectionTests`.
- `false` — `P0_NuGetAuditRemainsVisible` must assert `NuGetAuditLevel`: `Directory.Build.props` does not set one; NU1901–NU1904 stay visible as non-WAE warnings, which is the test's actual contract.
- `false` — `deferred-work.md:809` still claiming the fitness test expects `10.0.400` is current drift: that bullet is a timestamped prior-review record; this increment updated the assertion to match the already-pinned SDK.
- Fix edits the spec under review — 2026-09-20 File List names only the six reminder-review files.
- Fix edits the spec under review — story Review Findings still cite `deferred-work.md:792` after spec-9 R2-BH-06.
- `low`, not worth fixing — Docker `Kill` TOCTOU after a 10-second probe timeout is folded into the occupancy-probe patch rather than a separate everyday defect.
- `low`, not worth fixing — unobserved `ReadToEndAsync` after that same probe timeout is the same occupancy-probe failure path.
- Fix edits the spec under review — Task 7 File List accuracy for concurrent CI/CD files that happen to sit on the story File List.

### Review Findings (2026-09-20, bmad-code-review, File List close-out `776b869...HEAD`)

_Scope: `776b869...HEAD` (HEAD `059e9a0`) — unreviewed File List increment close-out. 9 files, +716/−63, 1011 diff lines. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 23 raw findings triaged to 0 decision, 15 patch, 1 defer, 3 rejected. The story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] Story frontmatter is `status: done` while the body, Change Log, and sprint tracking all return Story 4.8 to `review` [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:3] — resolved 2026-09-20: frontmatter, body, Change Log, and sprint tracking now all say `review`.
- [x] [Review][Patch] Timed-out Docker probe facts do not fire the termination wait: `Kill` sets `HasExited`, so the second `WaitForExitAsync` returns immediately and a child that ignores `Kill` can hang occupancy [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs:160] — resolved 2026-09-20: the configurable fake can ignore the first kill; the focused fact proves a second bounded wait and termination attempt.
- [x] [Review][Patch] `Win32Exception` / `NotSupportedException` from `Kill` have no throwing fake, so deleting the catch still leaves the timeout facts green [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:658] — resolved 2026-09-20: the fake injects both failures and the focused theory verifies classified cleanup failure.
- [x] [Review][Patch] `Process.Kill(entireProcessTree: true)` can throw `AggregateException`, which escapes the termination catch and aborts resource settling instead of becoming a retryable `InvalidOperationException` [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:650] — resolved 2026-09-20: both harness and adapter cleanup classify `AggregateException`; the injected regression passes.
- [x] [Review][Patch] Successful `WaitForExitAsync` plus cancelled redirected reads is untested, so moving those awaits back out of the probe-CTS `try` would leak `OperationCanceledException` into live start/teardown [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:631] — resolved 2026-09-20: the nominal-exit/cancelled-read fact preserves the bounded probe timeout and observes both reads.
- [x] [Review][Patch] After the termination wait expires the code does not `Kill` again, and `ProcessSchedulerVolumeProbe.Dispose` does not terminate a live child, so hung `docker` CLIs can accumulate across the 60s settle loop [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:672] — resolved 2026-09-20: cleanup performs a final kill/wait pair, adapter disposal repeats bounded termination, and a real child-process fact proves disposal exits it.
- [x] [Review][Patch] A successful process exit whose redirected read then throws `IOException` or `ObjectDisposedException` escapes as an unclassified exception and aborts occupancy retry [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:633] — resolved 2026-09-20: stdout and stderr fault regressions both surface retryable `InvalidOperationException` diagnostics.
- [x] [Review][Patch] The IPv6 facts never call `BindPortExclusively`, so deleting `DualMode = false`, `ExclusiveAddressUse`, or `listener.Start()` still leaves all six harness facts green [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs:16] — resolved 2026-09-20: a deliberately misconfigured real listener is passed through the production configuration seam, and a genuinely occupied port exercises `Start()`.
- [x] [Review][Patch] `Scheduler_volume_probe_includes_stopped_containers` pins only `FileName` and `ArgumentList`, not `RedirectStandardOutput` / `RedirectStandardError` / `UseShellExecute = false` [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs:49] — resolved 2026-09-20: command redirection, shell execution, no-window, and ordered arguments are all pinned.
- [x] [Review][Patch] `RunSchedulerVolumeProbeAsync` has no success-path fact (empty owners, parsed `{{.ID}} {{.Names}}` lines, or non-zero Docker exit → `InvalidOperationException`) [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:636] — resolved 2026-09-20: empty output, trimmed line parsing, and non-zero exit details have direct facts.
- [x] [Review][Patch] Close-out arithmetic does not add up: spec-9 recorded serial non-smoke Integration **493/493**, this increment adds six harness facts and records **498/498** (493+6=499) [_bmad-output/implementation-artifacts/tests/test-summary.md:3424] — resolved 2026-09-20: the final reviewed binary was observed at **526/526**; the summary records that total rather than deriving it from stale arithmetic.
- [x] [Review][Patch] `RunSchedulerVolumeProbeAsync` has no caller-cancellation fact, so the cleanup-then-`throw;` path can regress into a classified `TimeoutException` while hanging-probe tests stay green [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:678] — resolved 2026-09-20: exact caller-token propagation is asserted after bounded cleanup and child termination.
- [x] [Review][Patch] New `source_spec` rows restate already-open unpark, fail-fast starvation, due-now 4603, and in-tenant cancellation gaps without linking the earlier ledger entries [_bmad-output/implementation-artifacts/deferred-work.md:967] — resolved 2026-09-20: all four later rows retain append-only history and now link their earlier canonical entries.
- [x] [Review][Patch] `HangingSchedulerVolumeProbe` is a second type in `WorksAppHostSmokeHarnessTests.cs` after this increment extracted the other probe types to one-file-per-type [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs:144] — resolved 2026-09-20: the fake is now the sole type in `HangingSchedulerVolumeProbe.cs`.
- [x] [Review][Patch] Control-plane wait facts never mark a port occupied, so deleting the port loop from the new injectable wait still leaves both facts green [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs:67] — resolved 2026-09-20: deterministic retry and exhausted-budget facts make `127.0.0.1:50001` unavailable and pin the resource/address/port diagnostic.

- [x] [Review][Defer] spec-9 Code Map still cites `deferred-work.md:967`, which this increment's ledger append retargeted onto the new unpark `source_spec` [_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-9.md:43] — deferred: fix edits another spec.

**Rejected:**
- `false` — `ObserveProbeTaskAsync` must swallow faults beyond cancellation/`IOException`/`ObjectDisposedException`: those three are the demonstrated pipe-close and cancel outcomes from `ReadToEndAsync` after `Kill`; no other reachable redirected-read exception was shown, and a loud failure on an undemonstrated fault is not a defect.
- `false` — the new 2026-09-20 ledger bullets cite drifting `deferred-work.md:904` / `:944` / `:898` / `:940`: those line numbers still land on the intended CI/CD routing, 4608, 4604/4606, and truncated-heading items; this increment appended *after* them and already replaced the DW-56 line citation with a heading.
- `low`, not worth fixing — treating a successful `WaitForExitAsync` plus cancelled reads as a probe timeout: the race is rare inside a 60s settle budget, the conservative retry is safe, and splitting the wait/read cancellation tokens adds lifecycle complexity rather than a direct correction.

### Review Findings (2026-09-20, bmad-code-review, spec-10 increment `origin/main...HEAD`)

_Scope: `origin/main...HEAD` (HEAD `1a3edcc`) — spec-10 AppHost probe close-out. 10 files, +1605/−145, 2110 diff lines. Spec: `spec-4-8-register-and-reconcile-date-reminders-durably-10.md`. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 24 raw findings triaged to 0 decision, 5 patch, 0 defer, 13 rejected. The story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] Non-zero Docker exit is rewrapped as `process observation failed`, and the inspect fact still passes on substring match [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:709] — resolved 2026-09-20: the exit-code classification now occurs outside the process-observation catch, and the fact requires the exact direct diagnostic, no generic prefix, and no inner exception.
- [x] [Review][Patch] Production exclusive-bind `(IPAddress, int)` never observes `ExclusiveAddressUse` / IPv6 `DualMode` on the listener it starts [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:848] — resolved 2026-09-20: the exact production overload returns the settings observed on its started listener; the regression pins IPv4 exclusivity and IPv6 single-stack behavior while retaining occupied-port coverage.
- [x] [Review][Patch] `RunAndDisposeSchedulerVolumeProbeAsync` has no successful owner-list fact, so dropping the wrapper success return stays green [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:676] — resolved 2026-09-20: the wrapper must return both parsed owners and dispose the probe exactly once.
- [x] [Review][Patch] Adapter disposal waits are never asserted to be 5000 ms [tests/Hexalith.Works.IntegrationTests/ProcessSchedulerVolumeProbe.cs:136] — resolved 2026-09-20: the two-attempt disposal regression records and requires `[5000, 5000]`.
- [x] [Review][Patch] Real-child fail-safe ignores `WaitForExit` false, so a child that survives `Kill` can outlive the test [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs:813] — resolved 2026-09-20: a false first fail-safe wait triggers a second kill/wait, whose result must confirm exit.

**Rejected:**
- `false` — success-path `Task.WhenAll` hangs past `DockerProbeWait`: redirected reads still use `probeCts` (`CancelAfter(probeWait)`), so a honoring `ReadToEndAsync` is still bounded; cleanup `WaitAsync` is extra defense, not the only bound.
- `low`, not worth fixing — simultaneous stdout+stderr faults after exit surface as `AggregateException` "process observation failed": one stream fault is already classified, and aggregating the second pipe adds machinery this spec already rejected (R2-BH-13).
- `false` — `ProcessSchedulerVolumeProbe.Dispose` releasing a still-live handle: spec-10 requires two bounded termination attempts *then* dispose the process handle; fail-fast stops further probes rather than accumulating children.
- Fix edits the spec under review — Commands still show `DOTNET_CLI_HOME=/tmp` and `aspire describe --format json` while Observed/`test-summary.md` record `/var/tmp/story48-root` and `--format Json --non-interactive`.
- `low`, not worth fixing — the live `Process` fact does not set redirects or call `RunSchedulerVolumeProbeAsync`: that is adapter-disposal coverage; a real redirected Docker pipeline is more than a direct correction and would re-open the deferred pipe-after-kill item.
- `false` — injectable constructor vs `_process` split: production ctor wires both to the same `Process`; the delegates exist only for disposal-lifecycle tests.
- `false` — `ProbeHasExited` omitting `AggregateException`, or recording `Kill` `AggregateException` after exit: `HasExited`/`Kill` on an already-exited process is the demonstrated `InvalidOperationException` path, which is swallowed when `HasExited` is true.
- `false` — cancel during success-path reads can return owners: production always goes through `RunAndDisposeSchedulerVolumeProbeAsync`, which rechecks the caller token after dispose.
- `low`, not worth fixing — kill-failure coverage is a `[Fact]` `foreach` while the story says "theory": the fact still fails on the first uncovered exception type; converting to `[Theory]` is cosmetic reporting.
- `low`, not worth fixing — `HangingSchedulerVolumeProbe` constructor lacks `<param>` tags: the test assembly does not gate XML docs, and this is cosmetic.
- `false` — wrapper cancel fact injects `InvalidOperationException` rather than `SchedulerVolumeProbeCleanupException`: spec-10 requires exact caller cancellation to win after cleanup; production checks the token first.
- `false` — termination-wait timeout-as-exit extra `Kill` classifies a dead child as cleanup failure: `TryTerminateProbe` swallows `InvalidOperationException` when `HasExited`, and `TerminateAndObserveProbeAsync` rechecks `HasExited` before failing.
- `false` — fail-safe `Kill` throwing `Win32Exception`/`NotSupportedException`/`AggregateException` masks the assertion: those shapes were not shown on the cooperative `sleep`/`ping` child; `InvalidOperationException` is already caught.

### Review Findings (2026-09-20, bmad-code-review, spec-10 close-out `c4c925b...HEAD`)

_Scope: `c4c925b...HEAD` (HEAD `900f99c`) — spec-10 five-patch close-out plus probe-cleanup hardening. 10 files, +2,032/−152, 2,614 diff lines. Spec: `spec-4-8-register-and-reconcile-date-reminders-durably-10.md`. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 22 raw findings triaged to 0 decision, 6 patch, 0 defer, 10 rejected. The story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] Parent-story YAML frontmatter is still `status: done` after this increment marked the lifecycle mismatch resolved; body and sprint tracking say `review`, and spec-10 task 3 requires `in-review` [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:3] — RESOLVED 2026-09-20: frontmatter now uses build-workflow `in-review`, while the story body and sprint board use their `review` vocabulary.
- [x] [Review][Patch] `RunAndDisposeSchedulerVolumeProbeAsync` never rethrows a probe failure when Dispose succeeds, so a non-zero Docker inspect whose child has already exited returns a null owner list and the control-plane wait NREs instead of the classified retryable inspect diagnostic [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs:671] — RESOLVED 2026-09-20 as a false production finding: the wrapper already rethrows `probeFailure` through `ExceptionDispatchInfo`; a direct wrapper regression now pins the exact non-zero diagnostic and successful disposal.
- [x] [Review][Patch] The IPv6 exclusive-bind fallback constructs `new TcpListener(IPAddress.IPv6Loopback, 0)` outside the unavailable-address predicate, so a kernel-disabled IPv6 loopback that already classified production bind as inapplicable can still fail the fact [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs:77] — RESOLVED 2026-09-20: listener construction and configuration now share the unavailable-loopback classification, with null-safe cleanup.
- [x] [Review][Patch] The real-child disposal fact waits on `Process.Exited` after adapter `Dispose` closed that `Process`, so a queued exit notification can be dropped and the fact times out even though the child was killed [tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs:1017] — RESOLVED 2026-09-20: the fact observes bounded exit through the retained independent process handle and still exercises fail-safe disposal.
- [x] [Review][Patch] Five new `deferred-work.md` rows use machine-absolute `source_spec` paths instead of the repository-relative locators and `#story-4-8-canonical-*` anchors this increment added for portability [_bmad-output/implementation-artifacts/deferred-work.md:1012] — RESOLVED 2026-09-20: all five locators are repository-relative; the distinct rows and existing canonical anchors remain intact.
- [x] [Review][Patch] The dated **2026-09-20 spec-10 five-patch close-out** File List omits `ProcessSchedulerVolumeProbe.cs` and `SchedulerVolumeProbeCleanupException.cs`, the files that gained two-attempt disposal and the non-retryable cleanup type [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:711] — RESOLVED 2026-09-20: both files are recorded in that historical File List block.

**Rejected:**
- `false` — adapter `Dispose` orphans a stubborn Docker CLI by calling `Process.Dispose` after two failed kills: those kills already left the child unkillable, and `WaitForControlPlaneResourcesReleasedAsync` rethrows `SchedulerVolumeProbeCleanupException` without starting another probe.
- `low`, not worth fixing — `HangingSchedulerVolumeProbe` ignore-cancellation reads use uncancellable `Task.Delay` and `Dispose` does not complete them: only one deterministic fact hits that fake, and adding a completion CTS is extra test machinery rather than a direct production correction.
- `false` — harness `ProbeHasExited` omitting `AggregateException` lets cleanup abort before redirected-read observation: production `Process.HasExited` was not shown to throw `AggregateException`; `Kill` aggregates are already classified in `TryTerminateProbe`, and an already-exited `InvalidOperationException` is swallowed when `HasExited` is true.
- `false` — `WaitForProbeExitAsync` returning false on termination-CTS timeout without an inner `HasExited` recheck misclassifies a just-exited child: `TryTerminateProbe` swallows `InvalidOperationException` when `HasExited` is true, and `TerminateAndObserveProbeAsync` rechecks `HasExited` before failing cleanup.
- `false` — adapter `WaitForExit` catch returns false without a trailing `TryGetHasExited`, causing a false unconfirmed-termination: `Process.WaitForExit` on an already-exited child returns true rather than throwing; a throwing wait is a real lifecycle failure that correctly continues to a second attempt.
- Fix edits the spec under review — spec-10 Observed still lists harness 33/33, Integration 526/526, and live 4/4, and Commands still show `DOTNET_CLI_HOME=/tmp` / `aspire describe --format json`, while this diff's story and `test-summary.md` record 40/40, 533/533, and live 0/4 with `/var/tmp` and `--format Json --non-interactive`.
- `false` — empty/whitespace stderr fallback and `StartProbeRead` null-task diagnostic have no facts: deleting the fallback still throws on non-zero exit, and current `ISchedulerVolumeProbe` implementations are not shown to return a null read task.
- `false` — real-child fail-safe only asserts `SafeHandle.IsClosed` and does not prove two-attempt false-wait: the retained-handle Dispose call is observable, and the two-attempt false-wait path is already required by `Process_scheduler_volume_probe_disposal_retries_when_the_first_wait_is_false_with_bounded_waits`.
- `false` — returning Story 4.8 to `review` after an honest 0/4 live run claims live credit: spec-10 AC3 requires recording actual totals and returning to review; the latest story Change Log and `test-summary.md` explicitly withhold live acceptance credit.
- `low`, not worth fixing — `GetProcessById` after `Process.Start` can throw and leak the 30-second child: that window is not everyday, and wrapping Start in a new try/finally is an extra isolation guard rather than a direct assertion fix.

### Review Findings (2026-09-21, bmad-code-review, spec-11 close-out `origin/main...HEAD`)

_Scope: `origin/main...HEAD` (HEAD `0639a69`) — spec-11 remaining spec-10 review-patch close-out. 7 files, +140/−27, 317 diff lines. Spec: `spec-4-8-register-and-reconcile-date-reminders-durably-11.md`. Layers: blind-hunter, verification-gap, and acceptance-auditor reported; edge-case-hunter returned empty and is recorded as failed. 8 raw findings triaged to 0 decision, 2 patch, 2 defer, 4 rejected. The story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] The parent File List has no dated spec-11 inventory, so this increment’s tree (`spec-4-8-register-and-reconcile-date-reminders-durably-11.md`, the edited spec-10 file, `WorksAppHostSmokeHarnessTests.cs`, story/sprint/`test-summary`/`deferred-work`) is unlisted while earlier close-outs added their own dated blocks [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:709] — resolved 2026-09-21: added the dated seven-file spec-11 inventory to the parent File List.
- [x] [Review][Patch] Dev Agent Record Completion Notes were not prepended for spec-11, so the latest bullet still reports focused harness 40/40 and non-smoke Integration 533/533 against this increment’s Change Log and `test-summary.md` totals of 41 and 534 [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:490] — resolved 2026-09-21: prepended the spec-11 close-out note with the observed 41/41 focused harness and 534/534 non-smoke Integration totals.
- [x] [Review][Defer] Spec-10 Verification Observed is still titled “final reviewed tree” with harness 33/33, Integration 526/526, and live 4/4, which can be read as current against spec-11’s 41/534 and no-new-live-credit evidence [_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-10.md:212] — deferred: fix edits another spec
- [x] [Review][Defer] The dated **2026-09-20 spec-10 five-patch close-out** File List still omits `spec-4-8-register-and-reconcile-date-reminders-durably-10.md`, unlike earlier dated blocks that include their implementing specs [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:711] — deferred: pre-existing spec-10 BH-12 inventory gap, not one of spec-11’s six named patches

**Rejected:**
- `false` — real-child disposal still races because `WaitForExitAsync` starts after adapter `Dispose` and never sets `EnableRaisingEvents`: the independent `GetProcessById` handle is retained before dispose, and `WaitForExitAsync` waits on that process handle rather than the disposed `Exited` event the spec-11 patch replaced.
- `false` — IPv6 fallback `DualMode = true` before production configuration, plus the empty unavailable-loopback catch, lets IPv6-capable CI stay green with no assertions: that block runs only after production bind already classified IPv6 as inapplicable; capable hosts take the primary branch and assert both settings, and `DualMode = true` is the pre-condition that production `DualMode = false` is measured against.
- `false` — the five deferred-work rows still need `#story-4-8-canonical-*` fragment ids: those rows are distinct spec-10 concerns, not re-observations of the existing canonical unpark/scheduler/telemetry items, and `source_spec` plus summary already identify them.
- `false` — the wrapper non-zero regression is incomplete without `using var probe` and `ShouldNotContain("process observation failed")`: sibling wrapper facts omit `using` because the wrapper already disposes, `InnerException.ShouldBeNull()` already rejects a typical wrap, and a same-message rewrite would need brittle identity/stack pins that spec-11 BH-08 left out of defect scope.

### Review Findings (2026-09-21, bmad-code-review, final bmad-build increment `origin/main...HEAD`)

_Scope: `origin/main...HEAD` (HEAD `5cc1956`) — unreviewed increment `test(reminders): close story 4.8 review gaps`. 8 files, +229/−13, 401 diff lines. Spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. 15 raw findings triaged to 0 decision, 4 patch, 0 defer, 6 rejected. The story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] Parent YAML is `status: done` while the body, Change Log, and sprint board return Story 4.8 to `review`; spec-10/spec-11 already required frontmatter `in-review` for that pair [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:3] — resolved 2026-09-21: the resumed build workflow restored `in-progress` before implementation; the final review close-out returns the frontmatter to `in-review` with the body and sprint board at their `review` vocabulary.
- [x] [Review][Patch] Persistently empty Sentry credential files are never fail-closed in tests: `SentryCredentialReadFailsClosedAfterTheBoundedBudget` uses a missing file and pins `FileNotFoundException`, so a mutation that returns empty PEM when `lastFailure` is null stays green [src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs:248] — resolved 2026-09-21: `SentryCredentialReadFailsClosedWhenTheFileStaysEmpty` keeps the credential file empty across all three reads, pins both retry delays and the empty-file diagnostic, and requires a null inner exception.
- [x] [Review][Patch] This increment adds three non-smoke facts but leaves `test-summary.md` and the latest serial Integration total at spec-11's **534/534** [_bmad-output/implementation-artifacts/tests/test-summary.md:3559] — resolved 2026-09-21: the close-out adds the missing empty-file fact and records the observed current non-smoke total at **538/538**, with the two affected classes at **47/47** and zero skips.
- [x] [Review][Patch] The two new deferred-work `source_spec` rows were appended under the spec-11 heading and the steady-state stale-delay row does not cross-link the existing `DateReminderReconciler` family entry [_bmad-output/implementation-artifacts/deferred-work.md:1032] — resolved 2026-09-21: the rows now have their own final-review heading, and the steady-state item links the canonical reconciliation stale-delay family entry.

**Rejected:**
- `false` — `maxAttempts <= 0`, negative `retryDelay`, and null `delayAsync` have no facts: `ConfigureSidecar` and the private three-argument wrapper always pass `CredentialReadAttempts` (20), `CredentialReadRetryDelay` (500 ms), and `Task.Delay`, so those guards are unreachable on the live sidecar path.
- `false` — caller cancellation during `delayAsync` or `File.ReadAllTextAsync` could be rewritten as `InvalidOperationException`: `OperationCanceledException` is outside the `IOException`/`UnauthorizedAccessException` filter and already propagates.
- `false` — `Ignores_an_unknown_non_state_event_beside_a_valid_suspension` replaces `Created` and omits `SkippedParkedCount`: the stream is still `[unknown, Suspended]`, `WorksEventDecoder` returns null for `FutureInformationalEvent`, the non-state skip continues, and `WorkItemSuspended` alone is enough for the fold; the sibling discovery fact already pins `SkippedParkedCount` on the same seed path.
- `false` — empty or whitespace `certificateDirectory`/`fileName` can read a relative CWD credential: `ConfigureSidecar` already `ThrowIfNullOrWhiteSpace`s the directory, and production file names are literals (`ca.crt` / `issuer.crt` / `issuer.key`).
- `false` — a throwing, null, or never-completing `delayAsync` skips remaining attempts and never wraps fail-closed: production injects `Task.Delay`; a delay failure already escapes, which is fail-closed, and a null/hanging delay is not reachable from the wrapper.
- `low`, not worth fixing — the new facts call only the six-parameter seam, so changing the private 20×500 ms wrapper budget would stay green: pinning that budget needs either `internal` constants or a ~9.5 s real-delay wrapper test, which is extra surface rather than a direct empty-file assertion.

_2026-09-21 final bmad-build implementation review: edge-case and verification-gap layers reviewed the
current working-tree increment; the blind-hunter layer was skipped because the four-agent session cap left
only two simultaneous reviewer slots._

- **[edge-case] The persistent-empty credential fact created its temporary file before entering the cleanup `try` block.** Verdict: `low`. Confirmed: a cancelled or failed initial write after directory creation could leave the temporary directory behind. → **patch**: move the write inside the existing `try/finally`, keeping setup failure cleanup deterministic.
- **[verification-gap] No findings.** The layer traced the increment's claims to the recorded focused and full deterministic runs and reported nothing to triage.

### Review Findings (2026-09-21, bmad-code-review, increment `5cc1956...HEAD`)

_Scope: `5cc1956...HEAD` (HEAD `3538ff5`) — unreviewed increment `test(reminders): harden recovery proof`. 6 files, +375/−4, 535 diff lines. Spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported; verification-gap found no gaps. 17 raw findings triaged to 0 decision, 1 patch, 1 defer, 13 rejected. The story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Patch] Parent YAML is still `status: done` and sprint `last_updated` still says in-progress after this increment claimed a review close-out [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:3] — resolved 2026-09-21: frontmatter is `in-review`; body and sprint `development_status` stay `review`; `last_updated` no longer claims in-progress.
- [x] [Review][Defer] `Recovery_re_registers_a_still_future_await_that_later_fires` still deletes the Host 1 reminder without waiting for the suspension marker [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:174] — deferred: pre-existing sibling fact this increment did not change; the three-host overdue fact now waits

**Rejected:**
- `false` — Host 3 can treat a leftover Host 2 canary registration as its own startup pass: `DeleteReminderAsync` polls until 404, Host 2's one-shot reconciler has already returned, and Host 1's completed markers make Host 2 subscription a Duplicate skip, so nothing on this path re-registers during the 3s settle.
- `false` — Host 3 can miss a duplicate overdue resume because it counts immediately after the canary appears: same-tenant index insertion is overdue then canary, so `ProcessAsync` submits the due resume before scheduling the canary; a Host 3 resubmit uses the same `DateResume` identity and cannot add a second `WorkItemResumed`.
- `false` — `WaitForSuspensionMarkerCompletedAsync` aborts the retry loop on a second `WorkItemSuspended` or missing JSON fields: this fact parks each item once, and a malformed page failing closed is correct rather than a reachable false pass.
- `false` — the appended appendix disagrees with itself about whether all four layers ran: the `5cc1956` triage block and the later "implementation review" hunter notes are two sessions; the second skipped Blind Hunter because of the four-agent cap.
- `false` — the temp-file write-outside-`try` item is still an open patch: this increment moved `File.WriteAllTextAsync` inside the existing `try/finally`.
- `false` — whitespace-then-success credential retry has no sibling of `SentryCredentialReadRetriesUntilMaterialIsAvailable`: `IsNullOrWhiteSpace` is the same branch as empty; the exhaustion theory already pins persistent whitespace and the retry-success fact pins empty-to-material.
- `false` — the Host 3 canary instant can elapse during later startups: the recorded 1/1 run finished in 380.665s against a 10-minute canary, and the actor only removes the reminder after fire, which that run never reached.
- `low`, not worth fixing — `WaitForSuspensionMarkerCompletedAsync` is a private copy of stream-read helpers instead of a `WorksAppHostTestReadiness` method: extracting it adds test-harness surface, and the sibling fact is the deferred pre-existing hole rather than a new helper defect.
- `low`, not worth fixing — the recovery fact's class remarks and Host 2 comment still describe a single overdue park/resume: the marker/canary protocol is already recorded in Completion Notes and `test-summary.md`.
- `low`, not worth fixing — the canonical reconciliation stale-delay ledger row does not link back to the new steady-state sibling: the sibling already points at `#story-4-8-canonical-reconciliation-stale-delay`.
- Fix edits the spec under review — appendix arithmetic "15 raw findings triaged to 4 patch + 6 rejected" does not add to 15.
- Fix edits the spec under review — the new hunter block reuses BH-14, EC-08, and EC-09 for different issues than the preceding list.
- Fix edits the spec under review — close-out notes still name `SentryCredentialReadFailsClosedWhenTheFileStaysEmpty` while the added fact is `SentryCredentialReadFailsClosedWhenTheFileStaysEmptyOrWhitespace`.

### Review Findings (2026-09-21, bmad-code-review, Group 1 Reminders `9526c31...HEAD`)

_Scope: `9526c31...HEAD` over `src/Hexalith.Works/Reminders` (HEAD `3373ab1`) — 13 files, +685/−158, 1,009 diff lines. Spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported; acceptance-auditor found no Group 1 AC violations. 17 raw findings triaged to 0 decision, 1 patch, 5 defer, 8 rejected._

- [x] [Review][Patch] Page-budget fail-closed test does not observe the configured budget [tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs:172] — resolved 2026-09-21: the fact now counts `ReadStreamAsync` (expect 1) and requires the inner message to name `MaxStreamPagesPerAggregate`. Focused Debug lane 2/2, 0 skips.
- [x] [Review][Defer] One reconciler submit/schedule failure starves later awaits [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:111] — deferred: pre-existing; canonical per-await isolation/exhaustion ledger
- [x] [Review][Defer] Due-now resume submission has no bounded identity-bearing failure log [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:119] — deferred: pre-existing; canonical due-now telemetry ledger
- [x] [Review][Defer] Exact caller cancellation inside a tenant drops that tenant's collected evidence [src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:192] — deferred: pre-existing; source remarks and canonical in-tenant cancellation ledger retain the exact-token policy
- [x] [Review][Defer] Startup reconciliation returns permanently after the attempt budget [src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:57] — deferred: pre-existing; ledger already records post-startup retry/readiness policy
- [x] [Review][Defer] Stream-page cursor metadata is trusted without an advance or AD-27 envelope check [src/Hexalith.Works/Reminders/PendingDateAwaitStreamReader.cs:82] — deferred: maybe-false (would be medium); settle with gateway-contract evidence or fault injection that `LastSequenceReturned` can stall or envelopes can be non-positive/non-increasing

**Rejected:**
- `false` — discarding `ReminderReconciliationOutcome` hides parked-skip summaries: spec-5 declined a second aggregate log; EventId 4607 already warns per parked candidate on every path.
- `false` — the index-driven source never writes back a cleared await: Task 4 allows skip without cleanup; the index is discovery and the stream is truth.
- `false` — the steady-state handler schedules already-due awaits as `TimeSpan.Zero` instead of submitting `DateResume`: Task 2 requires `dueTime = max(Zero, Instant - now)`; recovery reissue is the reconciler's job.
- `false` — `IPendingDateAwaitSource` still "reads persisted work streams" and `DateReminderReconciler` throws an internal incomplete-scan type: the production source does read per-aggregate streams after index discovery; the only production caller is the same assembly, and tests already have `InternalsVisibleTo`.
- `false` — `PendingDateAwaitScanIncompleteException` stores `PartialResults` by reference and accepts negative counts: the throw site passes a local list that is not mutated afterward, and both failure counters only increment.
- `false` — a registry entry equal to reserved tenant `tenants` burns the startup retry budget: `/project` and `/process` refuse that id before an index key is minted; the mixed-case `TENANTS` hole is already on the ledger.
- `false` — a folded stream that has not reached `context.SequenceNumber` acks and skips registration: a still-truncated rebuild throws (redelivery); empty pending is the specified already-resumed path; persist-then-publish was not shown to return a complete prefix missing the delivered sequence.
- `false` — a deserialized registry with a null `Tenants` collection throws before tenant isolation: the dispatcher always writes a `HashSet`; a missing JSON property uses the property initializer; `"Tenants": null` was not shown on a production write.

### Review Findings (2026-09-21, bmad-code-review, Group 2 Projections `9526c31...HEAD`)

_Scope: `9526c31...HEAD` over `src/Hexalith.Works/Projections` (HEAD `20a8ca5`) — 16 files, +1370/−103, 1,712 diff lines. Spec: `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported; acceptance-auditor found no Group 2 AC violations. 36 raw findings triaged to 0 decision, 2 patch, 4 defer, 30 rejected._

- [x] [Review][Patch] Dispatch merge of persisted children has no identity-mismatch test [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:209] — resolved 2026-09-22: a two-case regression plants a roll-up under the requested key with a foreign tenant or work-item identity, then proves dispatch refuses its persisted children and preserves the requested identity.
- [x] [Review][Patch] SkippedEvent and EventId 4500 still log unbounded EventTypeName or CorrelationId [src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:48] — resolved 2026-09-22: both caller-controlled values are bounded to 128 characters on skip and projected-event paths, with a focused regression proving suffixes are absent.
- [x] [Review][Defer] No unpark, delete, or operator replay path; a later successful shared rebuild still leaves the parking document in place [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:609] — deferred: pre-existing; canonical unpark/replay ledger already owns this, including the reminder-recovery skip
- [x] [Review][Defer] Mixed-case reserved tenant `TENANTS` still misses the `/project` Ordinal guard, then `TenantId` lowercases to `tenants` [src/Hexalith.Works/Projections/WorksReadModelKeys.cs:69] — deferred: pre-existing; reserved-tenant spec Never list puts mixed-case direct `/project` out of scope
- [x] [Review][Defer] `UseCurrentSchemaAsync` is read once per dispatch and reused across later awaits [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:199] — deferred: documented generation-switch race in the DW-84 dispatcher rework; not Story 4.8 reminder/index behavior
- [x] [Review][Defer] Equal-sequence pending-date watermarks (`>=`) can reject the same full replay after a decoder upgrade would make a skipped event state-affecting [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:565] — deferred: already recorded in the 2026-09-21 final bmad-build ledger row on index repairability

**Rejected:**
- `false` — `MaxSequence` throws on an all-null `Events` list: `MaintainPendingDateAwaitIndexAsync` returns on `Count == 0`; the poller delivers `GetEventsAsync(0)` envelopes, not `[null]`; throwing on that hand-crafted body is fail-closed.
- `false` — shared rebuild never rewrites `PendingDateAwaitTenantIndex` / registry: those keys are deliberately unversioned and independent of roll-up generation (`WorksReadModelKeys` registry remarks); Task 3 maintains them from `/project`; DD-3 already treats a stale index as discovery with stream as truth.
- `false` — ghost `MemberWorkItemIds` with no roll-up are a query lie: membership is sealed inventory (every accumulated aggregate id); `GetWorkItemQueryHandler` NotFound when the roll-up is missing is the identity fail-closed path, not a false child.
- `false` — an empty shared-rebuild candidate can never cut over a genuinely empty tenant: `Build` documents empty inventory as a failed capture and refuses to delete the legacy index; `CreateEmptyCandidateAsync` is the accumulate seed, not a sealed empty tenant.
- `false` — `WorkItemSharedProjectionRebuildHandler` must call `ThrowIfReservedTenantId`: rebuild writes only generation-qualified what's-next/roll-up keys, never the pending-date index that collides with the registry.
- `false` — `model is null && stateEvidenceDelivered` upserts a roll-up-less eligible item: `WorkItemProjectionBoundarySanitizer` no longer returns null when a model exists (it only clears rolled totals); a `Get`-null roll-up with decoded non-rejection events was not shown.
- `false` — unknown or nameless event types skip `malformedEvidence` and publish rolled totals: unknown types are not Works catalog events (`KnownEventType: false`, `Malformed: false`); `/project` skips them; shared rebuild's incomplete mark is the operator-inventory fail-closed path, not the poller contract.
- `false` — a slice without `WorkItemSuspended` is treated as never-had and leaves a stale pending entry: `ProjectionUpdateOrchestrator` full-replays from sequence 0; the never-had gate is the 2026-09-15 history-gating decision proven by `An_item_that_never_held_a_date_await_writes_no_index_document_at_all`.
- `false` — deserialized pending-index `Entries`/`LastSequences` or registry `Tenants` null throws: production transforms always write dictionaries/`HashSet`; property initializers cover omitted JSON; `"…": null` was not shown on a production write.
- `false` — a null `Histories` item or null `Events` array throws mid-rebuild: `AccumulateAsync` coalesces `Events ?? []` into a non-null array; `FromCandidate` already rejects a null `Histories` collection; a null element was not shown on a production candidate.
- `false` — shared rebuild commits from non-positive sequences and therefore misses the `bd76a9b` "before any write" claim: `/project` already throws at `DispatchAsync` line 118; rebuild inventory is the same EventStore prefix; a non-positive `SequenceNumber` was not shown on a sealed candidate.
- `false` — rebuild keys and parent matching use raw mixed-case `identity.TenantId`: shared-rebuild identity is EventStore `AggregateIdentity`, which lowercases tenant id before delivery.
- `false` — `FromCandidate` catching only `JsonException` lets negative `GlobalPosition` fail unclassified: the candidate state has no `GlobalPosition`; `JsonSerializer.Deserialize` of this DTO was not shown to throw `ArgumentException`/`NotSupportedException`.
- `false` — decode exceptions outside `IsHandledDecodeFailure` skip parking and 500-loop the poller: the filter is `JsonException | ArgumentException | NotSupportedException`, the same set `WorksEventDecoder` documents as handled malformed evidence; `InvalidOperationException` was only asserted as unclassified in a governance check, not thrown from `Deserialize`.
- `false` — a null `ChildWorkItemIds` element throws during merge `Exists`/`Sort`: the merge already skips `child?.Value` whitespace; roll-up projection was not shown to persist a null child id.
- `false` — `PersistRollUpAsync` watermark ignores stored identity mismatch and can permanently block the real aggregate: this dispatcher always writes `model` from the requested identity; a foreign higher-sequence document at that key was not shown to be produced here (query tests plant miskeyed docs for GET, not for persist).
- `false` — missing `LastSequences` falling back to an ahead item watermark freezes additive rollout: the comment at `UpsertTenantIndexAsync` documents that fallback as the alternative to maximizing both watermarks, which would freeze harder.
- `false` — parking budget is not required to be positive: `WorksHost` `ValidateOnStart` requires `MaxUndecodableEventDispatchesBeforeParking > 0`; the constructor `?? new` fallback is test-only.
- `false` — neither dispatcher nor rebuild handler implements `IDeclaresProjectionReadModelSlots`: `/project` is a bespoke `MapPost`, not an SDK `IDomainProjectionHandler`; the slot scan does not apply to this adapter.
- `false` — `IsRolledTotalUnavailable` can `StackOverflowException` on a deep parent/child chain: traversal already cycle-detects; a work-item tree deep enough to blow the stack was not shown.
- `low`, not worth fixing — watermark-rejected what's-next/pending transforms still allocate and `TrySaveAsync`: skipping the save needs a write-policy no-op seam, not a one-line correction, and equal-sequence redelivery is the common path.

### Review Findings (2026-09-22, bmad-code-review, Group 3 Runtime `9526c31...HEAD`)

_Scope: `9526c31...HEAD` over `src/Hexalith.Works/Runtime` (HEAD `643d375`) — 11 files, +986/−22, 1,158 diff lines. Spec: this story. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported; acceptance-auditor found no Group 3 AC violations. 20 raw findings triaged to 1 decision, 5 patch, 1 defer, 12 rejected. The story frontmatter `baseline_commit: 9526c31` was the diff baseline, narrowed to Runtime._

- [x] [Review][Patch] Cascade startup still stops after one failed pass — resolved 2026-09-22: keep the single pass. `CascadeRecoveryService` remarks now state that a thrown pass is logged and replayed from the durable incomplete index on the next process start, and that a per-entry failure stays on that index. No in-process retry loop. [src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:9]

- [x] [Review][Patch] Constructor-rejected `WorkItemSuspended` is untested on the subscription and `/project` paths [src/Hexalith.Works/Runtime/WorksEventDecoder.cs:56] — resolved 2026-09-22: malformed `AwaitConditions: [null]` now proves terminal fail-closed subscription handling and budgeted `/project` parking with no index write.
- [x] [Review][Patch] In-progress marker acquisition returns HTTP 500 with no log [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:88] — resolved 2026-09-22: EventId 4807 emits a structured warning with the bounded message id before the retryable outcome is returned.
- [x] [Review][Patch] Reserved-tenant refusal logs only a reason code and omits the message id [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:58] — resolved 2026-09-22: EventId 4806 now retains both structured `MessageId` and `ReasonCode` fields, covered by the processor regression.
- [x] [Review][Patch] Marker-store failures keep the exception type name and drop the exception [src/Hexalith.Works/Runtime/Events/WorksDomainEventLog.cs:45] — resolved 2026-09-22: EventId 4803 now receives the caught exception as structured logger exception data on strict and best-effort marker paths.
- [x] [Review][Patch] `CascadeCheckpointIndexStaleAfterHours` of zero or less is accepted and prunes the crash window immediately [src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs:65] — resolved 2026-09-22: startup options validation requires a positive value, with zero and negative cases pinned by a theory.

- [x] [Review][Defer] Concurrent deliveries that both observe no marker both run handlers [src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:62] — deferred: pre-existing Dapr marker-store contract; `TryAcquireAsync` does not persist a lease, and adding one is an EventStore protocol change

**Rejected:**
- `false` — `IsValidUniqueId` turns a non-format parser failure into a terminal ack: `UniqueIdHelper.ToGuid` only throws `ArgumentException` or `FormatException`, and its inner catch wraps every other failure as `FormatException` before it escapes.
- `false` — an unknown event type is completed, so a later deploy cannot dispatch it: the EventStore exemplar processor also `MarkCompleted`s unknown types and returns `SkippedUnknownEventType`, which this endpoint already maps to HTTP 200; Works uses `FailedInvalidPayload` for the same ack-and-complete contract.
- `false` — a constructor or converter defect on the subscription is permanently dropped, and the parking budget never applies: `WorksHost` documents terminal malformed-delivery handling, and the decoder comment requires constructor rejection to take that fail-closed path; `/project` parking is a different consumer.
- `false` — the identity check permanently skips a future event whose aggregate id is not the work-item id, contrary to a comment that the shape should be admitted: the comment says the second comparison is defense-in-depth against that shape, and the skip is the check working.
- `false` — `TENANTS` is refused on `/process` and accepted by the subscription: ordinal `IsReservedTenantId` misses `TENANTS`, then `TenantId` lowercases the payload to `tenants`, and the identity check completes `SkippedAggregateMismatch` before any handler runs.
- `false` — the endpoint remarks disagree, and a caller that can reach the app port bypasses the sidecar policy: the first paragraph already makes topology the boundary because pub/sub has no `dapr-caller-app-id`; the second paragraph adds the sidecar allow-list for service invocation and does not claim the app checks that header.
- `false` — dropping the SDK `EventStoreDomainEventProcessor` registration would leave the Works route mapped but not unwrapped: `UseEventStoreDomainService` turns CloudEvents and `/dapr/subscribe` on from that registration, and `WorksHost` still calls `AddEventStoreDomainEvents`, so the current route is unwrapped.
- `false` — the deprecated page-budget alias silently wins, and a leftover `Works:Recovery:Tenants` list is ignored: the alias remarks say the old key wins when set, and AC #3 requires reconciliation without that hand-configured list.
- `false` — a consumed type with no registered handler is acknowledged and never schedules a reminder: `WorksHost` registers `WorkItemSuspendedReminderHandler`; the outcome needs a later deletion of that registration.
- `false` — a failed `MarkDispatched` leaves an in-progress lease that redelivery can never complete: the registered Dapr store does not persist `InProgress`; a failed transition leaves no marker, and the next delivery is `Acquired` and runs handlers again.
- `false` — a non-HTTP gateway URI makes recovery commands target that endpoint: `HttpClient` rejects schemes other than HTTP and HTTPS when the request is sent.
- `low`, not worth fixing — the gateway URI is checked on first client use, `Uri.TryCreate` accepts userinfo, query, and fragment, and a null `DAPR_API_TOKEN` is forwarded: a bad absolute URI already throws, non-HTTP fails at send, and `DaprServiceInvocationHandler` adds the token header only when the token is non-empty; extra origin checks are more than a direct correction of an everyday miss.

### Review Findings (2026-09-22, bmad-code-review, commit `c2a59b8`)

_Scope: `c2a59b8^..c2a59b8` (`fix(reminders): harden durable reminder recovery`) — 13 files, +384/−26, 810 diff lines. Spec: this story. Layers: blind-hunter, verification-gap (no gaps), acceptance-auditor. Edge Case Hunter returned empty and is excluded. 9 raw findings triaged to 0 decision, 3 patch, 1 defer, 5 rejected._

- [x] [Review][Patch] A newer foreign roll-up at the aggregate key is kept, so the identity fail-closed read never heals the document [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:465] — resolved 2026-09-22: the sequence guard now preserves only a newer roll-up whose embedded tenant/work-item identity matches the requested key; a newer foreign document is replaced by the authoritative replay. The two-case regression uses a foreign sequence 99 and proves the repaired sequence is 2 with no foreign children.
- [x] [Review][Patch] The new 128-character skip bound slices the raw event-type name, so a long namespace drops the simple name and control characters stay in the line [src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:49] — resolved 2026-09-22: every skip path logs the catalog-derived simple name, and the shared bound replaces control characters before structured logging. The regression uses a 2,000-character namespace plus CR/LF/tab and proves the simple name remains visible on one bounded line.
- [x] [Review][Patch] The 2026-09-22 zero-warning build line omits the NuGetAudit and MinVer pins the previous close-outs passed on the build [\_bmad-output/implementation-artifacts/tests/test-summary.md:3653] — resolved 2026-09-22: the recorded command now includes `-p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0`; the same command completed with 0 warnings and 0 errors in this close-out.
- [x] [Review][Defer] Domain-event Skipped, Duplicate, and MarkerFailure still log raw EventTypeName and CorrelationId [src/Hexalith.Works/Runtime/Events/WorksDomainEventLog.cs:31] — deferred: pre-existing; this commit only added the caught exception argument on MarkerFailure

**Rejected:**
- rejected, fix edits the spec — frontmatter `status: done`, body `Status: in-progress`, and sprint `review` disagree (blind-hunter and acceptance-auditor). Aligning them edits this story file.
- `false` — `CascadeCheckpointIndexStaleAfterHours <= 0` still prunes via `Math.Clamp`: `AddWorksReminderAndCascadeRecovery` now `ValidateOnStart`s `> 0`, and the clamp ceiling is the documented overflow guard (`MaxStaleAfterHours`). A running host cannot construct the reconciler with zero.
- `false` — the projection truncation fact does not prove a 128-character cut: it requires the 128-character prefix and rejects the following `y`/`z`, so a shorter or longer formatted value fails.
- `false` — EventId 4806/4807 and the marker-failure facts accept any `MessageId` row: 4806 also requires `ReasonCode=reserved-tenant-id`, 4807 requires EventId 4807 at Warning, and the release and completion facts require EventId 4803 with the caught exception.

### Review Findings (2026-09-22, bmad-code-review, unpushed close-out `origin/main...HEAD`)

_Scope: `origin/main...HEAD` (HEAD `94c2e34`) — 8 files, +210/−34, 471 diff lines. Spec: this story. Layers: blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor — all four reported, none failed. The story frontmatter `baseline_commit: 9526c31` was not used._

- [x] [Review][Decision] **DW-56 is closed while this close-out still treats the marker-store failure as an open deferral** — Decided 2026-09-22 (human): reopen DW-56. The 2026-09-05 sweep had closed the row; `MarkCompletedSafelyAsync` and `ReleaseSafelyAsync` still only log. The story deferral stays on that row, and the Group 2/3 test-summary sentence now names the reopened deferral.

- [x] [Review][Patch] Unicode line separators still pass the new single-line sanitizers [src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:342] — resolved 2026-09-22: both projection sanitizers replace U+2028 and U+2029 along with control characters, and the log regression requires those separators to be absent from EventIds 4500 and 4504.
- [x] [Review][Patch] Known-type projection skip paths are not pinned to the simple event name [src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:58] — resolved 2026-09-22: null-payload, malformed known-type, and matching null conversation-link skips must log the catalog simple name and must not log the namespace prefix.

- [x] [Review][Defer] Read-model write diagnostics still copy raw correlation ids and event-type names [references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/ReadModelWritePolicy.cs:254] — deferred: pre-existing; `WithEventDiagnostics` is unchanged by this close-out and still forwards the first raw correlation id plus up to eight raw event-type names into conflict and exhaustion logs

**Rejected:**
- rejected, fix edits the spec — story YAML `status: done` while the body and sprint board say `review` (blind-hunter and acceptance-auditor). Aligning the frontmatter edits this story file.
- `false` — the final-review block defers BH-01–BH-14 and EC-01–EC-06 against a “do not defer it again” instruction: those clauses carry an existing deferred entry and tell a later review not to open a second one.
- `false` — healing a foreign roll-up leaves a foreign sequence on the tenant index: `PersistRollUpAsync` writes only the roll-up document. `UpsertTenantIndexAsync` compares `LastSequences` with the delivered request sequence, not the foreign roll-up’s `LatestAcceptedSourceSequence`.
- `false` — the current-schema roll-up heal is a different code path: `PersistRollUpAsync` selects `CurrentRollUpKey` or `RollUpKey` and runs the same identity-and-sequence predicate for both.
- `false` — a control-character suffix makes `WorkItemCreated` miss the catalog: `SimpleTypeName` keeps the characters after the last dot, so a contaminated token is an unknown type and is skipped. That is the fail-closed lookup.
- `false` — a 256-character work-item id breaks the 512-character log assertion: `AggregateIdentity` already caps aggregate ids at 256 ASCII characters with no controls. Event 4500 logs that validated id. `ShouldBeLessThan(512)` bounds the short fixture message, not every legal identifier.
- `low`, not worth fixing — Task 4 still says `FromSequence = LastSequenceReturned + 1` while the cursor is exclusive: the implementation and tests keep the exclusive lower bound, and the correction edits this story’s task text.

### Review Findings (2026-09-22, bmad-build patch close-out)

_Scope: the uncommitted Story 4.8 close-out, 5 files. The frontmatter baseline `9526c31` spans 3563 files and was not used, matching the prior close-out review. Layers: blind-hunter (5 findings), edge-case-hunter (no findings), verification-gap (no gaps)._

- [x] [Review][Patch] Known-type skip facts still pass when the logged type is a dotted tail of the raw name [tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs:421] — resolved 2026-09-22: the three new facts require `event {SimpleName} for work item` and reject `.{SimpleName}`, so a namespace tail cannot sit beside the simple name. `PendingDateAwaitIndexDispatcherTests` was re-run **28/28**.

**Rejected:**
- `false` — the known-type branches should log `eventType.Name` instead of the sliced simple name: after a catalog hit, `simpleName` is the dictionary key and equals `eventType.Name`. Passing `dto.EventTypeName` fails these facts because the first 128 characters are the namespace and do not contain `WorkItemCreated`.
- `false` — the null conversation fact only repeats the malformed-JSON result shape: a `conversationCorrelationId: null` payload deserializes to a matching `ConversationLinked` whose correlation is null, so decode takes the pattern branch. The catch needs a throw, and that JSON does not throw. The fact already requires `ConversationLinked` and rejects the 128-character namespace prefix.
- `false` — `AssertSingleLine` would stay green if `char.IsControl` were replaced by six literals, so U+0085 could survive: both sanitizers still call `char.IsControl`, which includes U+0085, and also replace U+2028 and U+2029. The new assertions add the two separators the review named.
- `low`, not worth fixing — `IsSingleLineUnsafe` is copied in the dispatcher and the decoder: both copies use the same predicate, and EventIds 4500 and 4504 are both asserted. A shared helper is a new member, not a direct correction of a current miss.
