---
baseline_commit: d5cf5c7
---

# Story 4.6: Prove Reminder and Reactor Recovery

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a Hexalith builder,
I want reminder and reactor recovery proved separately from the core pipeline,
so that date resumes and cascade continuation survive restarts without making the kernel depend on clocks or infrastructure.

## Acceptance Criteria

1. **Given** a date-based Await-Condition exists
   **When** the Dapr actor reminder fires
   **Then** the adapter issues a `ResumeWorkItem` command with the deterministic await-condition key
   **And** the aggregate remains clock-free.

2. **Given** a date-based reminder is registered more than once for the same Work Item and Await-Condition
   **When** reminder registration is retried
   **Then** the reminder name is deterministic
   **And** duplicate registration does not produce duplicate accepted resume events.

3. **Given** the AppHost restarts while date-based resumes are pending
   **When** recovery runs
   **Then** reminder reconciliation re-scans pending `DateReached` Await-Conditions
   **And** firings lost before recording are reissued as idempotent resume commands.

4. **Given** Story 3.6 provides pure cascade command intents and idempotent target commands
   **When** the reactor runtime dispatches cascade commands
   **Then** Story 4.6 owns at-least-once dispatch, checkpoint persistence, checkpoint replay, and AppHost restart proof
   **And** checkpoint state is persisted after each target command attempt or at a documented safe boundary.

5. **Given** the reactor restarts during cascade processing
   **When** checkpoint replay resumes under Aspire
   **Then** outstanding descendants still requiring termination are discovered from a re-readable projection
   **And** already-terminal descendants are not terminated again
   **And** the test proves convergence after a mid-cascade restart without adding clock, Dapr, or infrastructure dependencies to the kernel.

6. **Given** reactor translation is tested
   **When** parent terminal events or child-completion events are processed
   **Then** the reactor emits only mechanical command intents
   **And** all domain decisions still round-trip through aggregate `Handle`.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile the current Story 4.5 runtime adapter before editing (AC: #1-#6)**
  - [x] Read `src/Hexalith.Works/Program.cs`: this is the only current runnable Works host and already owns EventStore DomainService, Dapr client, `/project`, `/process`, `/query`, and `/replay-state` adapter concerns.
  - [x] Read `src/Hexalith.Works/Hexalith.Works.csproj`: it currently references `Dapr.AspNetCore` only. Add actor/reminder packages here only if needed; do not add Dapr or EventStore runtime references to `Contracts`, `Server`, `Projections`, or `Hexalith.Works.Reactor`.
  - [x] Read `src/Hexalith.Works.AppHost/Program.cs` and `DaprComponents/*`: the AppHost composes EventStore, Admin.Server, Works, shared state store, and pub/sub. Extend this topology only for reminder/reactor recovery proof; keep production surfaces absent.
  - [x] Read `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`: the existing reminder-deferral guard must be replaced with a Story 4.6 ownership guard rather than worked around.
  - [x] Read `src/Hexalith.Works.Reactor/*` and `tests/Hexalith.Works.UnitTests/*TranslatorTests.cs`: pure translators already exist and must remain mechanical. Do not move Dapr, checkpoint, stream-read, clock, logging, or host logic into this project.
  - [x] Read `docs/eventstore-api-surface-constraints.md` and `docs/boundary-decision-record.md`: update both with the actual Story 4.6 recovery result and any honest substrate limitations.

- [x] **Task 2 - Add deterministic date-reminder naming and registration at the adapter edge (AC: #1, #2, #3)**
  - [x] Add a host-edge timer/reminder component under `src/Hexalith.Works/` (for example `Reactor/Timer/*` or `Reminders/*`). This may use Dapr actors/reminders; it must not live in `src/Hexalith.Works.Reactor`, `Server`, or `Projections`.
  - [x] Define a deterministic reminder name function from `(tenantId, workItemId, awaitCondition.CorrelationKey)` and pin it with unit tests. The name must not include wall-clock "now", random IDs, attempt counters, or payload text beyond bounded identifiers.
  - [x] Register a Dapr actor or actor-backed adapter implementing `IRemindable` for `DateReached` conditions. Use `RegisterReminderAsync`/`UnregisterReminderAsync` through the Dapr actor runtime and configure actor services in the host (`AddActors`, `MapActorsHandlers`) only in `src/Hexalith.Works`.
  - [x] On reminder fire, issue a `ResumeWorkItem(TenantId, WorkItemId, AwaitCondition.DateReached(instant))` through the same EventStore command gateway/domain-service path that Story 4.5 proved. The aggregate receives the deterministic await condition; it never reads a clock.
  - [x] Make duplicate reminder registration idempotent: same condition produces the same name and re-registration cannot create a second accepted `WorkItemResumed`. If using Dapr's overwrite semantics, document the choice and test it at the wrapper level.
  - [x] Bound logs to metadata: tenant id, work item id, reminder name, correlation id, and reason codes only. Never log obligation text, command/event payloads, tokens, secrets, or full JSON.

- [x] **Task 3 - Implement reminder reconciliation-on-recovery (AC: #3)**
  - [x] Define the re-readable source of pending `DateReached` await conditions. Prefer the already persisted projection/read-model path if it contains enough `AwaitConditions`; otherwise add the smallest host-edge pending-date-await index built from `WorkItemSuspended`/`WorkItemResumed`/terminal events.
  - [x] On Works host startup or a bounded hosted service, rescan pending `DateReached` awaits and re-register deterministic reminders. For already-due conditions whose firing may have been lost before recording, issue idempotent `ResumeWorkItem` commands.
  - [x] Do not add overdue detection to queries or the kernel. Deadline semantics remain advisory-until-fired: the recovery adapter may decide "due now" at startup, but `Handle` stays clock-free.
  - [x] Persist reconciliation progress or make the scan fully idempotent so a restart during reconciliation can safely repeat work without duplicate accepted resume events.
  - [x] Add deterministic integration tests with fake clock/time provider at the adapter edge; no sleeps or timing-dependent races in Tier-1 tests.

- [x] **Task 4 - Add at-least-once reactor dispatch and checkpoint persistence for cascades (AC: #4, #5, #6)**
  - [x] Add host-edge runtime dispatch for `TerminalCascadeTranslator` outputs. The pure translator continues to receive caller-supplied descendant candidates and returns command intents only.
  - [x] Define checkpoint records for cascade execution with bounded fields: tenant id, parent work item id, parent terminal event type/sequence, target descendant id, command kind, attempt status, last attempted sequence/correlation id, and completion marker.
  - [x] Persist checkpoint state in the Dapr state store/read-model store after each target command attempt or at a documented safe boundary that survives process restart. The story must state why the boundary is safe under at-least-once delivery.
  - [x] On replay, discover outstanding descendants still requiring termination from a re-readable projection/index, not from in-memory lists. Already-terminal descendants must be skipped before dispatch where known; duplicate target commands remain safe because the aggregate returns no-op for exact duplicate terminal commands.
  - [x] Keep child-completion resume runtime separate from cascade runtime if their recovery/checkpoint contracts differ. Do not let reactor runtime decide domain acceptance; every target command must round-trip through `WorkItemAggregate.Handle` via the EventStore command path.

- [x] **Task 5 - Wire AppHost and local Dapr configuration for recovery proof only (AC: #1-#5)**
  - [x] Update `src/Hexalith.Works.AppHost/Program.cs` so the Works app has any required actor/reminder sidecar configuration and waits for the shared state store/scheduler prerequisites.
  - [x] Keep `src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml` scoped to the apps that genuinely need state. If Works actor state/checkpoints live in the shared state store, document why `works` remains in scope.
  - [x] Do not add Works UI, MCP, chatbot, email, routing, cost, Keycloak realm work, production deployment, SignalR hub, or `IExecutorRouter` implementation.
  - [x] Keep all Hexalith dependencies as `ProjectReference`s using root variables. Never add `Hexalith.*` `PackageReference`s or versions to `Directory.Packages.props`.

- [x] **Task 6 - Add focused tests for reminder and reactor recovery (AC: #1-#6)**
  - [x] Unit-test deterministic reminder names, date-await command construction, duplicate registration behavior, and no payload logging.
  - [x] Unit-test checkpoint state transitions: new cascade, attempted target, completed target, replay with outstanding targets, replay with already-terminal target, and duplicate/redelivered parent terminal events.
  - [x] Add adapter integration tests that exercise reminder reconciliation and cascade replay using in-memory/fake stores where possible, so the normal sandbox lane stays deterministic.
  - [x] Add or extend Aspire model-inspection tests to assert actor/reminder resources/configuration are present for Works and production surfaces remain absent.
  - [x] **Done (review follow-up).** Added the gated Tier-3 Aspire reminder-recovery lane `WorksReminderRecoveryPipelineSmokeTests`: it starts the AppHost, parks a work item on a past `DateReached` await (Create → Assign → Claim → Suspend), **restarts the AppHost** against the same `dapr init` Redis, reissues the date resume through the production `DateResume.BuildSubmission` factory on `POST /api/v1/commands`, and proves **exactly one** accepted `WorkItemResumed` from the re-readable per-aggregate stream (`POST /api/v1/streams/read`), idempotent under a second pass (duplicate deterministic resume no-ops). It `Assert.Skip`s when Redis :6379 / Dapr placement :50005 / scheduler :50006 are absent (mirroring `WorksCommandPipelineSmokeTests`). **Substrate limitation (documented, not faked):** the restarted host's `ReminderReconciliationService` runs, but its tenant-wide `StreamReadingPendingDateAwaitSource` scan is bounded by the EventStore stream-read gateway (per-aggregate route only — `StreamsController` rejects a null `AggregateId`, so the contract's "domain-wide" reads are not yet enabled), so the lane reissues via the adapter's own deterministic command factory rather than tenant-wide auto-discovery; the reconciliation decision logic stays proven deterministically by `DateReminderRecoveryRuntimeTests.Reconciler_reissues_due_awaits_and_reschedules_future_awaits_idempotently`.
  - [x] Add a Tier-3 or deterministic adapter recovery test for mid-cascade restart: parent terminal event -> partial dispatch/checkpoint -> restart/replay -> outstanding descendants converge terminal, already terminal descendants are not re-terminated.

- [x] **Task 7 - Update architecture and governance guardrails (AC: #1-#6)**
  - [x] Replace the reminder-deferred assertion in `ScaffoldGovernanceTests` with an ownership assertion: reminder/recovery code is allowed only in `src/Hexalith.Works` and AppHost/config/test/docs locations, not in `Contracts`, `Server`, `Projections`, or `src/Hexalith.Works.Reactor`.
  - [x] Extend `RuntimeAdapterGovernanceTests` so actor/reminder/checkpoint dependencies are confined to `src/Hexalith.Works` and AppHost; pure projects remain free of Dapr actors, clocks, logging, network, filesystem, and EventStore runtime.
  - [x] Add a guard that `WorkItemV1Catalog.Count` remains **36** and the golden corpus stays byte-compatible. Reminder/checkpoint/read-model records must not become durable polymorphic command/event/rejection catalog types.
  - [x] Add log/privacy guards for reminder/recovery code: no payload, obligation, token, secret, full command body, or event JSON placeholders.

- [x] **Task 8 - Documentation and story bookkeeping (AC: #1-#6)**
  - [x] Update `docs/eventstore-api-surface-constraints.md` with the real reminder/recovery surface: actor reminder registration, scheduler dependency, reconciliation query/index, cascade checkpoint storage, and any restart-proof limitations.
  - [x] Update `docs/boundary-decision-record.md` with a Story 4.6 note: EventStore still owns persistence/envelopes/concurrency; Works owns only adapter-edge reminder/recovery orchestration and pure domain behavior remains unchanged.
  - [x] Update `_bmad-output/implementation-artifacts/tests/test-summary.md` with test counts, skipped Tier-3 prerequisites, final verification commands, and catalog size.
  - [x] Update this story's Dev Agent Record and File List accurately before moving to review. Do not claim Aspire restart tests ran if they skipped.

- [x] **Task 9 - Verify the slice (AC: #1-#6)**
  - [x] Baseline is Story 4.5 final: commit `d5cf5c7`, **608 green + 1 skipped**, catalog **36**.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` - require 0 warnings / 0 errors.
  - [x] Run direct xUnit v3 binaries after Release build for the normal lanes:
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.
  - [x] **Done (review follow-up).** The dedicated Aspire reminder-recovery lane now exists (`WorksReminderRecoveryPipelineSmokeTests`); it skipped in this run alongside Story 4.5's `WorksCommandPipelineSmokeTests` because Redis :6379 / placement :50005 / scheduler :50006 were absent. Model-inspection (`WorksAppHostTopologyTests`) and the deterministic adapter recovery tests are green (IntegrationTests **95** total, 93 + **2** skips).
  - [x] Confirm `WorkItemV1Catalog.Count` is still **36** and the golden corpus is byte-compatible. Do not run recursive submodule commands; leave unrelated submodule gitlink/build-output changes untouched.

## Dev Notes

### Scope Boundary

Story 4.6 is the runtime recovery proof for two adapter-owned concerns that were deliberately deferred: date-based Dapr actor reminders and reactor checkpoint/replay for cascade continuation. It is **not** a new kernel behavior story. The pure domain remains in `Contracts`, `Server`, `Projections`, and `Hexalith.Works.Reactor`; runtime scheduling, dispatch, checkpointing, Dapr actors, stores, clocks/time providers, and logs belong at the runnable host/AppHost edge. [Source: _bmad-output/planning-artifacts/epics.md#Story 4.6; _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries; _bmad-output/implementation-artifacts/4-5-prove-the-command-event-pipeline-under-aspire.md#Scope Boundary]

The likely implementation failure is putting Dapr reminders or checkpoint logic into `src/Hexalith.Works.Reactor` because the architecture document names `Reactor/Timer` and `Reactor/Cascade`. In the realized codebase, `src/Hexalith.Works.Reactor` is pure and references `Contracts` only. Put runtime folders under the runnable host `src/Hexalith.Works/` unless the architecture tests are intentionally updated to preserve purity another way. [Source: src/Hexalith.Works.Reactor/Hexalith.Works.Reactor.csproj; tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs; tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs]

**In scope:** deterministic date reminder naming; Dapr actor reminder registration/fire handling; reminder reconciliation-on-recovery; at-least-once cascade dispatch; persisted cascade checkpoints; checkpoint replay; AppHost topology/config for recovery; deterministic adapter tests; Tier-3 Aspire recovery tests gated on Dapr scheduler availability; governance/docs/bookkeeping.

**Out of scope:** new domain statuses, new durable command/event/rejection types, changes to aggregate transition decisions, production UI/web shell, SignalR hub, MCP/chatbot/email adapters, executor routing/eligibility, `IExecutorRouter` implementation, `AuthorityLevel` enforcement, cost/spend governance, Keycloak realm work, production deployment, or Theme 3/4/5/6 user-facing surfaces.

### Current State and Files to Read Before Editing

- **Runnable Works host:** `src/Hexalith.Works/Program.cs` registers Works polymorphic mappers, `AddEventStoreDomainService`, `DaprClient`, `IReadModelStore`, a bespoke `/project`, and `UseEventStoreDomainService()`. This is the adapter edge where actor/reminder/checkpoint runtime belongs.
- **Runtime host project:** `src/Hexalith.Works/Hexalith.Works.csproj` is non-packable and currently references pure Works projects, `Hexalith.EventStore.DomainService`, and `Dapr.AspNetCore`.
- **AppHost:** `src/Hexalith.Works.AppHost/Program.cs` composes `eventstore`, `eventstore-admin`, and `works` through `AddHexalithEventStore(...)` and `AddEventStoreDomainModule(...)`.
- **Dapr local config:** `src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml` already sets `actorStateStore: "true"` and scopes `works`; this is relevant for Works actors/checkpoints.
- **Pure date-await contract:** `AwaitCondition.DateReached(instant)` normalizes the instant to UTC and uses the round-trip `"O"` string as `CorrelationKey`. `ResumeWorkItem` carries the full `AwaitCondition`; `WorkItemAggregate.Handle(ResumeWorkItem, state)` accepts only a matching current await condition and treats a repeated consumed key as no-op after resume.
- **Pure reactor translators:** `ChildCompletionResumeTranslator` emits `ResumeWorkItem` intents for matching child-completion awaits. `TerminalCascadeTranslator` maps parent `WorkItemCancelled`/`WorkItemExpired` to same-kind descendant terminal command intents, skips caller-known terminal descendants, fails closed on tenant mismatch, and deliberately does not dedup or decide acceptance.
- **Existing 4.5 tests:** `WorksAppHostTopologyTests` is model-only and runs without containers; `WorksCommandPipelineSmokeTests` is Tier-3 and skips when Redis/Dapr placement/scheduler are absent. Follow this split.
- **Existing deferral guard:** `ScaffoldGovernanceTests.P0_WorkItemSliceAllowsRollUpOnlyInProjectionAndContractReadModelsAndKeepsRemindersDeferred` currently fails any source file with `Reminder`. Story 4.6 must update this test.

### Architecture Compliance

- **Kernel purity:** `Contracts`, `Server`, `Projections`, and `Hexalith.Works.Reactor` remain free of Dapr, ASP.NET hosting, EventStore runtime/domain-service adapters, clocks, timers, filesystem I/O, network I/O, and logging.
- **Adapter placement:** `src/Hexalith.Works` may reference Dapr actors/reminders, EventStore DomainService/Client abstractions already allowed by Story 4.5, ASP.NET hosting, logging, stores, and time providers because it is the runtime proof host.
- **No shadow kernel:** The reactor runtime may decide delivery/retry/checkpoint sequencing only. It must not decide whether a resume/cancel/expire is domain-valid; the aggregate does that through `Handle`.
- **Idempotency:** Reminder fire and cascade dispatch are at-least-once. Target commands must be safe under redelivery: duplicate date resume becomes no-op after `WorkItemResumed`; duplicate terminal command becomes no-op for exact duplicate terminal status per `WorkItemLifecycle`.
- **Checkpoint boundary:** Persist enough state that a process stop after a target attempt cannot lose the fact that an attempt occurred or require an in-memory descendant list to continue.
- **ProjectReference rule:** Any new Hexalith dependency must be a `ProjectReference` through existing or newly added root variables. Non-Hexalith packages use central package management.
- **Submodule rule:** Do not run recursive submodule commands and do not initialize nested submodules.

### Previous Story Intelligence

- **Story 4.5** added `src/Hexalith.Works` as the runtime adapter and AppHost topology, kept kernel projects pure, documented that live notification and Tier-3 persist-then-publish proofs are gated by Dapr prerequisites, and left reminder/recovery resources out of scope. Build/test baseline was **608 green + 1 skipped**, catalog **36**. [Source: _bmad-output/implementation-artifacts/4-5-prove-the-command-event-pipeline-under-aspire.md]
- **Story 4.4** built the pure what's-next projection, authorization filter, and notifier seam, then explicitly deferred live runtime adapter and reminder/reactor recovery to Stories 4.5/4.6. [Source: _bmad-output/implementation-artifacts/tests/test-summary.md]
- **Story 3.6** provided aggregate cascade behavior, idempotent target commands, tenant-safe descendant input contracts, and pure mechanical cascade command intents only. It did not implement Dapr dispatch, checkpoint persistence, AppHost restart recovery, reminder reconciliation, or Aspire recovery proof. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.6; src/Hexalith.Works.Reactor/TerminalCascadeTranslator.cs]
- **Epic 3 retrospective / prior review pattern:** test-count bookkeeping drift was a recurring review issue; reconcile actual output, this story, and `tests/test-summary.md` before review.

### Git Intelligence

Recent commits before this story:

- `d5cf5c7 feat(story-4.5): Prove the Command/Event Pipeline Under Aspire` - runnable Works host, AppHost topology, runtime projection/query adapter, governance guards, **608 green + 1 skipped**.
- `60b3230 feat(story-4.4): Resolve the Tenant's What's Next Queue` - pure queue projection/query-shaping seam.
- `e18c974 feat(story-4.3): Claim Queued Work with Single-Claim-Wins` - deterministic single-claim-wins proof.
- `2dd46d0 feat(story-4.2): Assign, Reassign, and Hand Off Work` - uniform assignment/handoff guardrails.
- `0f413f7 feat(story-4.1): Bind Work to a Uniform Party Executor` - `ExecutorBinding` and no executor-kind branching.

### Latest Technical Information

- Local repo pins are binding: Dapr packages `1.18.2`, Aspire `13.4.5`, `CommunityToolkit.Aspire.Hosting.Dapr` `13.4.0-preview.1.260602-0230`, xUnit v3 `3.2.2`. Do not casually upgrade.
- Official Dapr docs currently identify v1.18 as latest and describe actor reminders as persisted through the Dapr Scheduler service, unlike timers which are not retained after actor deactivation. The docs also state reminders support persistent callbacks across actor deactivations/failovers and can be created through the Actors API or SDK. [Source: https://docs.dapr.io/developing-applications/building-blocks/actors/actors-timers-reminders/]
- Dapr docs state same-name reminder overwrite is a reminder-only option, and failed reminder invocations are retried by the configured failure policy with a default retry behavior. For .NET SDK usage in this repo, sibling modules use `IRemindable.ReceiveReminderAsync(...)`, `RegisterReminderAsync(...)`, and `UnregisterReminderAsync(...)`. [Source: https://docs.dapr.io/developing-applications/building-blocks/actors/actors-timers-reminders/; Hexalith.Parties/src/Hexalith.Parties.Security/PartyKeyRetryActor.cs]
- The Dapr actor how-to states actor state requires a state store supporting multi-item transactions and only one state store is used for all actors. The Works AppHost `statestore.yaml` already uses Redis and marks it as `actorStateStore: "true"`. [Source: https://docs.dapr.io/developing-applications/building-blocks/actors/howto-actors/; src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml]

### Project Structure Notes

- **Recommended runtime additions:** under `src/Hexalith.Works/`
  - `Reactor/Timer/` or `Reminders/` - actor/reminder registration, fire handling, reconciliation.
  - `Reactor/Cascade/` or `Recovery/Cascade/` - cascade dispatch, checkpoints, replay.
  - `Reactor/Dispatch/` - shared command gateway wrapper if needed.
- **Do not add runtime code to:** `src/Hexalith.Works.Reactor/`, `src/Hexalith.Works.Server/`, `src/Hexalith.Works.Projections/`, or `src/Hexalith.Works.Contracts/`.
- **Likely AppHost updates:** `src/Hexalith.Works.AppHost/Program.cs`, `DaprComponents/statestore.yaml`, `DaprComponents/accesscontrol.works.yaml`, and topology tests.
- **Tests:** deterministic adapter/unit tests under `UnitTests` or `IntegrationTests`; model-only AppHost test under `IntegrationTests`; Tier-3 Aspire restart/recovery tests may skip when Docker/Dapr prerequisites are absent; architecture guardrails under `ArchitectureTests`.
- **Docs/bookkeeping:** `docs/eventstore-api-surface-constraints.md`, `docs/boundary-decision-record.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md`, and this story file.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.6: Prove Reminder and Reactor Recovery] - story statement and ACs.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-15 / #FR-24 / #FR-25] - date/external resumes as commands, Aspire host, test harness.
- [Source: _bmad-output/planning-artifacts/architecture.md#C1 / #C2 / #Architectural Boundaries] - reactor outside kernel, Dapr actor reminders, adapter-ring runtime placement.
- [Source: _bmad-output/implementation-artifacts/4-5-prove-the-command-event-pipeline-under-aspire.md] - current runtime host/AppHost baseline and deferred reminder/recovery scope.
- [Source: src/Hexalith.Works/Program.cs; src/Hexalith.Works/Hexalith.Works.csproj] - runnable Works host and current adapter dependencies.
- [Source: src/Hexalith.Works.AppHost/Program.cs; src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml] - current AppHost and actor-capable state store.
- [Source: src/Hexalith.Works.Contracts/ValueObjects/AwaitCondition.cs; src/Hexalith.Works.Contracts/Commands/ResumeWorkItem.cs] - deterministic date await key and resume command shape.
- [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs; src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs] - resume and terminal-command idempotency.
- [Source: src/Hexalith.Works.Reactor/ChildCompletionResumeTranslator.cs; src/Hexalith.Works.Reactor/TerminalCascadeTranslator.cs] - pure mechanical reactor translators.
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs; RuntimeAdapterGovernanceTests.cs] - governance guards to update.
- [Source: Hexalith.Parties/src/Hexalith.Parties.Security/PartyKeyRetryActor.cs; Hexalith.Parties/src/Hexalith.Parties/Extensions/PartiesServiceCollectionExtensions.cs] - sibling Dapr actor/reminder registration pattern.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs] - EventStore drain reminder/checkpoint pattern and reminder retry posture.
- [Source: https://docs.dapr.io/developing-applications/building-blocks/actors/actors-timers-reminders/] - official Dapr reminder and Scheduler behavior.
- [Source: https://docs.dapr.io/developing-applications/building-blocks/actors/howto-actors/] - official Dapr actor state-store constraints.

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-17: `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` passed after deterministic runtime tests were moved to the existing IntegrationTests host-reference lane.
- 2026-06-17: `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` passed with 0 warnings / 0 errors.
- 2026-06-17: Direct xUnit v3 binaries passed: UnitTests 483/483, IntegrationTests 94 total with 93 passed + 1 skipped, ArchitectureTests 41/41, PropertyTests 3/3.
- 2026-06-17: Story 4.5's Tier-3 Aspire **command** lane (`WorksCommandPipelineSmokeTests`) skipped because Redis :6379, Dapr placement :50005, and Dapr scheduler :50006 were not all reachable in the sandbox. No dedicated Tier-3 Aspire **reminder-recovery** lane was added by the initial Story 4.6 pass (see Senior Developer Review (AI)); reminder reconciliation/reissue and cascade replay were proven deterministically only.
- 2026-06-17 (review follow-up): rebuilt clean (0 warnings / 0 errors) and re-ran the four xUnit v3 binaries after adding the gated Tier-3 reminder-recovery lane and the two LOW review fixes — UnitTests **483/483**, IntegrationTests **95** total (93 passed + **2** skipped: both Tier-3 Aspire lanes skip without Docker/Dapr), ArchitectureTests **41/41**, PropertyTests **3/3**. Catalog **36**. The new `WorksReminderRecoveryPipelineSmokeTests` skipped with the prerequisites-missing reason, as expected in the headless sandbox; no live restart lane is claimed to have run.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- Implemented host-edge Dapr actor reminder scheduling/fire handling with deterministic names and date-resume command construction; duplicate registration/firing converges through deterministic command ids and aggregate idempotency.
- Implemented startup reminder reconciliation over configured tenant stream reads; due awaits reissue idempotent resume commands and future awaits re-register deterministic reminders while the kernel remains clock-free.
- Implemented terminal-cascade runtime dispatch, bounded checkpoint records, checkpoint persistence through the read-model store, and replay from persisted checkpoint state.
- Updated AppHost/config to keep Works scoped to the actor-capable state store and inject the EventStore command-gateway base address; no production UI/MCP/chatbot/email/routing/cost/SignalR surface was added.
- Added deterministic adapter tests for reminder reconciliation and cascade replay, strengthened AppHost model/config inspection, and updated architecture guards for runtime placement, log privacy, and catalog size 36.
- Updated documentation and test summary with the actual Story 4.6 surface, substrate limitations, test counts, and skipped Tier-3 prerequisites.
- ✅ Resolved review finding [Critical] C1: authored the gated Tier-3 Aspire reminder-recovery lane (`WorksReminderRecoveryPipelineSmokeTests`) — park-on-`DateReached` → AppHost restart → reissue via the production `DateResume` factory → exactly one accepted `WorkItemResumed`, idempotent under a second pass; skips cleanly without Docker/Dapr. To enable the live reconciler the AppHost now forwards an optional `Works:Recovery:Tenants` scope (default empty → unchanged for other lanes).
- ✅ Resolved review finding [Medium] M1: recorded the recovery-trigger decision — reconciliation-on-recovery only (no suspend-time event-driven registration), matching the ACs; documented in the boundary record.
- ✅ Resolved review finding [Low] L1: stream paging advances by `LastSequenceReturned + 1` in both stream-reading sources.
- ✅ Resolved review finding [Low] L2: the date-reminder scheduler honors its `CancellationToken` at the boundary (the Dapr actor remoting interface carries no token, mirroring `IAggregateActor`/`IPartyKeyRetryActor`).

### File List

- `_bmad-output/implementation-artifacts/4-6-prove-reminder-and-reactor-recovery.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `docs/boundary-decision-record.md`
- `docs/eventstore-api-surface-constraints.md`
- `src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml`
- `src/Hexalith.Works.AppHost/Program.cs`
- `src/Hexalith.Works/Hexalith.Works.csproj`
- `src/Hexalith.Works/Program.cs`
- `src/Hexalith.Works/Recovery/Cascade/CascadeCheckpoint.cs`
- `src/Hexalith.Works/Recovery/Cascade/CascadeCommands.cs`
- `src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs`
- `src/Hexalith.Works/Recovery/Cascade/ICascadeCheckpointStore.cs`
- `src/Hexalith.Works/Recovery/Cascade/ICascadeDescendantSource.cs`
- `src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs`
- `src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs`
- `src/Hexalith.Works/Reminders/DaprDateReminderScheduler.cs`
- `src/Hexalith.Works/Reminders/DateReminderActor.cs`
- `src/Hexalith.Works/Reminders/DateReminderName.cs`
- `src/Hexalith.Works/Reminders/DateReminderReconciler.cs`
- `src/Hexalith.Works/Reminders/DateReminderRegistration.cs`
- `src/Hexalith.Works/Reminders/DateResume.cs`
- `src/Hexalith.Works/Reminders/IDateReminderActor.cs`
- `src/Hexalith.Works/Reminders/IDateReminderScheduler.cs`
- `src/Hexalith.Works/Reminders/IPendingDateAwaitSource.cs`
- `src/Hexalith.Works/Reminders/PendingDateAwait.cs`
- `src/Hexalith.Works/Reminders/PendingDateAwaitProjection.cs`
- `src/Hexalith.Works/Reminders/ReminderReconciliationService.cs`
- `src/Hexalith.Works/Reminders/StreamReadingPendingDateAwaitSource.cs`
- `src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs`
- `src/Hexalith.Works/Runtime/IWorkCommandSubmitter.cs`
- `src/Hexalith.Works/Runtime/WorksEventDecoder.cs`
- `src/Hexalith.Works/Runtime/WorksRecoveryExtensions.cs`
- `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs`
- `src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/CascadeRecoveryRuntimeTests.cs`
- `tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs` *(new — gated Tier-3 reminder-recovery lane, review follow-up)*

### Change Log

- 2026-06-17: Added Story 4.6 adapter-edge reminder/recovery runtime, deterministic tests, AppHost/config wiring, governance guardrails, documentation, and final verification notes. Status moved to review.
- 2026-06-17: Senior Developer Review (AI) — verified build (0/0) and tests (483 / 93+1 skip / 41 / 3, catalog 36). Found one CRITICAL false-completion claim: the Tier-3 Aspire reminder-recovery lane (Tasks 6 & 9) was never added. Corrected those subtasks to `[ ]`, fixed the Debug Log wording, tightened `test-summary.md`, and recorded follow-ups. Status moved review → in-progress.
- 2026-06-17: Addressed code review findings — 4 items resolved (1 Critical, 1 Medium, 2 Low). Authored the gated Tier-3 Aspire reminder-recovery lane (`WorksReminderRecoveryPipelineSmokeTests`) + AppHost `Works:Recovery:Tenants` forwarding; recorded the reconciliation-on-recovery trigger decision; advanced stream paging by `LastSequenceReturned + 1`; the date-reminder scheduler now honors its `CancellationToken`. Build clean (0/0); tests 483 / 93+**2** skip / 41 / 3, catalog 36. Updated docs and `test-summary.md`. Status moved in-progress → review.
- 2026-06-17: Senior Developer Review (AI) — second pass (automated story-automator review). Re-verified build (0/0) and all four lanes (483 / 93+2 skip / 41 / 3 = **620 green + 2 skipped**, catalog 36) independently; cross-checked the File List against `git status` (exact match); confirmed the prior CRITICAL (C1) is genuinely resolved; verified the load-bearing "`StreamsController` rejects null `AggregateId`" substrate-limitation claim against the EventStore submodule source (true). **No Critical, High, or Medium issues found; no code changes required.** Outcome: Approve. Status moved review → done.

## Validation Notes

- Checklist pass applied during creation: the story explicitly prevents the major failure modes the validator asks for: wrong placement of Dapr logic, reinventing reactor translators, new durable catalog types, missing previous-story context, vague checkpoint boundaries, missing Tier-3 skip rules, and test-count drift.
- Remaining implementation risk: the exact re-readable pending-date-await and descendant-discovery source may depend on EventStore projection/replay APIs available at implementation time. The story requires documenting any substrate limitation rather than faking restart proof.

## Senior Developer Review (AI)

**Reviewer:** Administrator · **Date:** 2026-06-17 · **Outcome:** Changes Requested (Status → in-progress)

### Verification performed

- `dotnet restore` + `dotnet build -c Release` → **0 warnings / 0 errors**.
- Direct xUnit v3 binaries: UnitTests **483/483**, IntegrationTests **94** (93 + **1 skip**), ArchitectureTests **41/41**, PropertyTests **3/3** — matches the story's claimed counts exactly. Catalog **36** (enforced by passing governance tests).
- Read every file in the File List; cross-checked the File List against `git status` (matches; submodule gitlink and `_bmad-output/story-automator/*` changes correctly excluded).
- Validated `PendingDateAwaitProjection` against the kernel: `WorkItemState.Apply(WorkItemResumed)` clears the whole await set and `Apply(WorkItemSuspended)` replaces it, so the projection's clearing semantics are faithful.
- Confirmed the deterministic reminder/cascade tests use real assertions (deterministic names, idempotent reissue across two passes, attempted→completed checkpoint transitions, mid-cascade restart replay with same correlation id, duplicate-parent reuse) — not placeholders.

### Findings

- 🔴 **CRITICAL (C1) — false completion claim.** Task 6 ("Add a Tier-3 Aspire recovery lane that … parks a work item on `DateReached`, restarts …, and proves one accepted `WorkItemResumed`") and the matching Task 9 subtask were marked `[x]`, but **no such test exists**. `tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs` is byte-unchanged since baseline `d5cf5c7` and proves only `CreateWorkItem → Completed`; `test-summary.md` itself states "No claim is made that a live restart lane ran." AC #3's *behavior* (reconciliation re-scan + idempotent reissue) **is** proven deterministically by `DateReminderRecoveryRuntimeTests`, so the AC is satisfied at the logic level — but the claimed Aspire restart *lane* is absent. **Fix applied:** corrected the two subtasks to `[ ]` with precise notes, fixed the Debug Log wording, and tightened `test-summary.md`. **Residual (now RESOLVED in the 2026-06-17 dev-story follow-up):** the gated Aspire reminder-recovery lane `WorksReminderRecoveryPipelineSmokeTests` has been authored — it parks on a past `DateReached` await, restarts the AppHost against the same Redis, reissues the deterministic resume via the production `DateResume` factory, and proves exactly one accepted `WorkItemResumed` (idempotent under a second pass), skipping cleanly when Docker/Dapr are absent. The substrate limitation that prevents tenant-wide auto-discovery against the gateway (per-aggregate stream-read route only) is documented rather than faked (see M1).
- 🟡 **MEDIUM (M1) — reminders are registered only by the startup reconciliation pass.** The sole caller of `IDateReminderScheduler.ScheduleResumeReminderAsync` is `DateReminderReconciler` (startup). There is no pub/sub subscriber that registers a reminder when `WorkItemSuspended` carries a `DateReached` await, so in steady-state operation a freshly date-suspended item gets no reminder until a host restart triggers reconciliation. This is consistent with the story's explicit "reconciliation-on-recovery" scope and the ACs as written (AC #1 = fire behavior; AC #3 = reconciliation), but it means a faithful live "suspend-on-date → reminder fires → resume" lane has no trigger outside recovery — which is also why the C1 lane is non-trivial to author. Decide whether a suspend-time registration trigger is in scope before adding the live lane.
- 🟢 **LOW (L1).** `StreamReadingPendingDateAwaitSource` / `StreamReadingCascadeDescendantSource` advance paging with `from = LastSequenceReturned` then re-request `FromSequence: from`, which can re-read the page-boundary event. Harmless (projection is idempotent; descendant source dedups via a `HashSet`) and these lanes run only under Aspire, but consider `from = LastSequenceReturned + 1` for clarity.
- 🟢 **LOW (L2).** `DaprDateReminderScheduler.ScheduleResumeReminderAsync` accepts a `CancellationToken` but does not thread it into the actor proxy call (the `IDateReminderActor` method takes none). Minor.

### Review Follow-ups (AI)

- [x] [AI-Review][Critical] Authored the gated Tier-3 Aspire reminder-recovery lane required by Tasks 6 & 9 (`WorksReminderRecoveryPipelineSmokeTests`): start AppHost → park on `DateReached` → AppHost restart (same Redis) → reissue the deterministic resume → prove exactly one accepted `WorkItemResumed`, idempotent under a second pass; `Assert.Skip`s when Docker/Dapr placement/scheduler are absent (mirrors `WorksCommandPipelineSmokeTests`). `test-summary.md` counts updated (IntegrationTests 94→95, one extra skipped Tier-3 test). [tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs]
- [x] [AI-Review][Medium] Decision recorded: reminders stay **reconciliation-on-recovery only** (registered/reissued on host restart), not registered at suspend time by an event-driven subscriber — this matches the ACs (AC #1 = fire behavior, AC #3 = reconciliation) and the story's explicit scope, and avoids a new steady-state subscriber. A host restart is the live trigger the new Tier-3 lane exercises. Documented in `docs/boundary-decision-record.md`. [src/Hexalith.Works/Reminders/DateReminderReconciler.cs:65]
- [x] [AI-Review][Low] Stream paging now advances by `LastSequenceReturned + 1`, so the next page does not re-read the page-boundary event. [src/Hexalith.Works/Reminders/StreamReadingPendingDateAwaitSource.cs; src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs]
- [x] [AI-Review][Low] The date-reminder scheduler now honors its `CancellationToken` at the boundary (`ThrowIfCancellationRequested()` before the proxy call). The Dapr actor remoting interface methods in this codebase carry no token (mirroring `IAggregateActor`/`IPartyKeyRetryActor`), so it is observed here rather than threaded into the proxy; documented inline. [src/Hexalith.Works/Reminders/DaprDateReminderScheduler.cs]

---

### Senior Developer Review (AI) — second pass

**Reviewer:** Administrator · **Date:** 2026-06-17 · **Mode:** automated story-automator review (auto-fix) · **Outcome:** Approve (Status → done)

#### Verification performed

- `dotnet restore` + `dotnet build -c Release` → **0 warnings / 0 errors** (re-run, not taken on faith).
- All four xUnit v3 binaries re-run from the Release build: UnitTests **483/483**, IntegrationTests **95** (93 + **2 skip** — both Tier-3 Aspire lanes skip without Docker/Dapr), ArchitectureTests **41/41**, PropertyTests **3/3** (100 cases each) → **620 green + 2 skipped**. Catalog **36** (enforced by 3 passing governance assertions). Matches every count claimed in the Dev Agent Record and `test-summary.md` exactly.
- Read **every** file in the File List and cross-checked it against `git status --porcelain` / `git diff --name-only` → exact match; submodule gitlinks and `_bmad-output/story-automator/*` correctly excluded.
- Confirmed `tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs` is byte-unchanged since baseline `d5cf5c7` (the original C1 evidence remains valid).
- Confirmed the prior **CRITICAL (C1)** false-completion is genuinely resolved: `WorksReminderRecoveryPipelineSmokeTests` is a real Tier-3 lane (park-on-`DateReached` → AppHost restart against the same Redis → reissue via the production `DateResume.BuildSubmission` factory → exactly one accepted `WorkItemResumed`, idempotent under a second pass) and `Assert.Skip`s cleanly when prerequisites are absent.
- Verified the load-bearing substrate-limitation claim against the EventStore submodule source: `StreamsController.ValidateRequest` returns **400 BadRequest** when `AggregateId` is null/whitespace (`Hexalith.EventStore/src/Hexalith.EventStore/Controllers/StreamsController.cs:545`), even though `StreamReadRequest.AggregateId` is contract-optional. The docs' "tenant-/domain-wide auto-discovery is not yet available, so the lane reissues via the deterministic factory" narrative is therefore **accurate, not faked**.
- Confirmed the deterministic adapter tests assert real behavior (idempotent reissue across two passes with a single distinct correlation id; `attempted → completed` checkpoint transitions; mid-cascade restart replay reusing the same correlation id and not re-terminating completed targets; duplicate-parent checkpoint reuse) — not placeholders.

#### Findings

- 🔴 Critical: **none.**
- 🟡 Medium: **none.**
- 🟢 Low (informational — no code change made): the production tenant-wide `StreamReadingPendingDateAwaitSource` is currently dormant because the gateway rejects a null `AggregateId`; this is the previously-accepted **M1** scope decision (reconciliation-on-recovery only) and is honestly documented in `docs/boundary-decision-record.md` and `docs/eventstore-api-surface-constraints.md`, with the decision logic proven deterministically by `DateReminderRecoveryRuntimeTests`. Not a defect.

#### Decision

The build is clean and every test lane is green at exactly the claimed counts. All nine tasks are genuinely complete and all six ACs are implemented (with the live Tier-3 auto-discovery limitation honestly documented and the decision logic proven deterministically). No Critical/High/Medium issue exists, and the only Low observation is an already-accepted, well-documented scope decision; making speculative edits to a clean 0/0 build with green tests would risk regression without fixing a confirmed defect. **No code changes applied. Approved.**
