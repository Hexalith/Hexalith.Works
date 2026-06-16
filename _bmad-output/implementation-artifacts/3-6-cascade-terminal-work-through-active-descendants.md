---
baseline_commit: f8856f2
---

# Story 3.6: Cascade Terminal Work Through Active Descendants

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a coordinator,
I want cancellation and expiry of parent work to cascade through still-active descendants,
so that an open subtree cannot keep burning down after its parent has terminated.

## Acceptance Criteria

1. **Given** a parent Work Item is cancelled or expired
   **When** descendants are still active
   **Then** the cascade process can issue terminal command intents for those descendants
   **And** the descendants apply their own transition rules through the aggregate.

2. **Given** a descendant is already terminal
   **When** a parent cancellation or expiry cascade is processed
   **Then** the descendant is unaffected
   **And** no duplicate terminal event is emitted.

3. **Given** cascade terminal commands are delivered more than once to the same descendant
   **When** the descendant aggregate handles a duplicate cancel or expire command for the already-applied terminal outcome
   **Then** the command is idempotent according to `docs/lifecycle-transition-matrix.md`
   **And** no duplicate terminal event is emitted.

4. **Given** the reactor translates parent terminal events
   **When** its pure translation is tested
   **Then** it emits only mechanical command intents
   **And** it does not decide domain outcomes that belong in `Handle`.

5. **Given** Story 3.6 scope is reviewed
   **When** cascade ownership is checked
   **Then** the story covers aggregate transition behavior, idempotent target commands, tenant-safe descendant selection contracts, and pure mechanical command intents
   **And** it does not implement Dapr dispatch, checkpoint persistence, AppHost restart recovery, reminder reconciliation, or Aspire recovery proof.

6. **Given** a parent and descendant belong to different tenants
   **When** cascade traversal is attempted
   **Then** tenant equality checks fail closed
   **And** no cross-tenant terminal command is produced.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile existing terminal and cascade foundations before writing code (AC: #1-#6)**
  - [x] Read `src/Hexalith.Works.Contracts/Commands/CancelWorkItem.cs`,
    `src/Hexalith.Works.Contracts/Commands/ExpireWorkItem.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemCancelled.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemExpired.cs`,
    `src/Hexalith.Works.Contracts/State/WorkItemState.cs`,
    `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`,
    `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs`,
    `src/Hexalith.Works.Reactor/ChildCompletionResumeTranslator.cs`,
    `src/Hexalith.Works.Reactor/AwaitingParent.cs`,
    `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs`,
    `docs/lifecycle-transition-matrix.md`,
    `docs/work-roll-up-projection.md`,
    and `docs/work-tree-shape-guard.md`.
  - [x] Confirm the current terminal behavior: cancel/expire already accept from every non-terminal
    status, duplicate self-terminal commands return `DomainResult.NoOp`, and cross-terminal commands
    reject through `WorkItemTransitionRejected`.
  - [x] Preserve the AR-13 per-state cancel/expire table as the single source of truth. Do not create a
    second cascade-specific transition table.
  - [x] Preserve existing roll-up behavior: `WorkItemCancelled`, `WorkItemExpired`, `WorkItemCompleted`,
    and non-requeue `WorkItemRejected` make the node terminal and contribute zero to ancestors.

- [x] **Task 2 - Lock aggregate target-command idempotency for cascade delivery (AC: #2, #3)**
  - [x] Add focused tests proving duplicate `CancelWorkItem` against `Cancelled` returns `DomainResult.NoOp`
    with no `WorkItemCancelled`, no rejection event, and no sequence burn.
  - [x] Add focused tests proving duplicate `ExpireWorkItem` against `Expired` returns `DomainResult.NoOp`
    with no `WorkItemExpired`, no rejection event, and no sequence burn.
  - [x] Add focused tests proving cross-terminal commands reject: cancel of `Expired`, cancel of
    `Completed`/`Rejected`, expire of `Cancelled`, and expire of `Completed`/`Rejected` emit no terminal
    success event and leave state/sequence unchanged.
  - [x] If production code changes are required to satisfy the tests, change only
    `WorkItemLifecycle.Decide` / `WorkItemAggregate.Handle` and keep `docs/lifecycle-transition-matrix.md`
    synchronized in the same commit.

- [x] **Task 3 - Add tenant-safe cascade selection contracts in the Reactor project (AC: #1, #5, #6)**
  - [x] Add a small pure Reactor input model for descendants requiring terminal cascade, for example
    `CascadeDescendant` or `DescendantTerminalCandidate`, carrying only `TenantId`, `WorkItemId`, and
    minimal state needed for selection. Do not include EventStore envelopes, Dapr metadata, checkpoint
    state, parent status decisions, roll-up totals, Party data, or adapter details.
  - [x] Add a pure command-intent translator under `src/Hexalith.Works.Reactor`, for example
    `TerminalCascadeTranslator`, that maps `WorkItemCancelled` to descendant `CancelWorkItem` intents and
    `WorkItemExpired` to descendant `ExpireWorkItem` intents.
  - [x] The translator must fail closed on tenant mismatch: if the parent terminal event tenant and a
    descendant candidate tenant differ, emit no command for that descendant.
  - [x] The translator must emit commands only for descendants supplied by the caller as active or still
    requiring terminal cascade. It must not traverse the tree itself by reading EventStore, projections,
    files, Dapr state, or in-memory global state.
  - [x] The translator may skip candidates explicitly marked terminal, but it must not decide whether a
    target aggregate will accept the terminal command. Acceptance, rejection, and no-op remain owned by
    `WorkItemAggregate.Handle`.

- [x] **Task 4 - Keep the cascade reactor mechanical and pure (AC: #4, #5)**
  - [x] Keep `Hexalith.Works.Reactor` referencing inward only to `Hexalith.Works.Contracts`; the existing
    `DependencyDirectionTests` should continue to pass without relaxing the expected reference list.
  - [x] Do not add Dapr, Aspire, EventStore runtime, logging, filesystem, HTTP, timers, clocks, random
    IDs, queues, or database packages to the Reactor project.
  - [x] Do not add checkpoint persistence, retry loops, dispatch workers, host wiring, AppHost crash tests,
    reminder reconciliation, or durable cascade continuation. Those are Story 4.6 runtime concerns.
  - [x] Keep command intent generation deterministic from explicit inputs; input order may determine output
    order, but duplicates and redelivery must remain safe because target commands are idempotent.

- [x] **Task 5 - Add cascade unit coverage (AC: #1-#6)**
  - [x] Add reactor tests proving a parent `WorkItemCancelled` plus same-tenant active descendants produces
    `CancelWorkItem` intents for those descendants.
  - [x] Add reactor tests proving a parent `WorkItemExpired` plus same-tenant active descendants produces
    `ExpireWorkItem` intents for those descendants.
  - [x] Add reactor tests proving already-terminal candidates are skipped or omitted from selection and do
    not produce duplicate terminal command intents.
  - [x] Add reactor tests proving cross-tenant descendants are ignored even when work item IDs collide.
  - [x] Add reactor tests proving the translator carries no parent or descendant status acceptance decision
    beyond explicit active/terminal candidate filtering.
  - [x] Extend aggregate lifecycle tests where needed so AC #2/#3 are covered close to the transition table,
    not only indirectly through the reactor.

- [x] **Task 6 - Add integration, serialization, roll-up, and doc regression coverage (AC: #1-#6)**
  - [x] Confirm `CancelWorkItem`, `ExpireWorkItem`, `WorkItemCancelled`, and `WorkItemExpired` remain in
    `WorkItemV1Catalog` and the golden payload corpus. Story 3.6 should not add new durable event types
    unless unavoidable.
  - [x] If new Reactor command-intent models are public contracts, register and test them intentionally.
    Prefer keeping them Reactor-local so the v1 durable catalog remains unchanged.
  - [x] Add or extend roll-up tests proving that terminal events produced by cascade command handling still
    zero descendant contribution and therefore remove the open subtree from ancestors' rolled Remaining.
  - [x] Add or update a short cascade section in `docs/work-tree-shape-guard.md` or a focused cascade doc.
    State that tenant-safe descendant discovery is supplied to the pure translator, target aggregates decide
    outcomes, and runtime checkpoint/recovery is deferred to Story 4.6.
  - [x] Update `docs/lifecycle-transition-matrix.md` only if code behavior changes; otherwise leave the
    existing AR-13 table intact and reference it from cascade docs/tests.

- [x] **Task 7 - Update test summary and verify the slice (AC: #1-#6)**
  - [x] Update `_bmad-output/implementation-artifacts/tests/test-summary.md` with Story 3.6 commands,
    counts, files, and any explicitly not-applicable runtime/E2E surfaces.
  - [x] Use Story 3.5's final baseline of **457** green tests: UnitTests 368, IntegrationTests 60,
    ArchitectureTests 28, PropertyTests 1.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
    must finish with 0 warnings and 0 errors.
  - [x] Run direct xUnit v3 binaries, the reliable path in this sandbox:
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    and `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.

## Dev Notes

### Scope Boundary

Story 3.6 realizes the pure-domain and pure-reactor portion of FR-10 cascade semantics. A parent cancel
or expire event can be translated into same-kind terminal command intents for still-active descendants,
and each descendant aggregate applies its own lifecycle table. This story must make duplicate or
redelivered cascade commands safe by relying on target aggregate idempotency, not by adding an
out-of-band dedup store. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.6: Cascade Terminal Work Through Active Descendants; _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-10]

**In scope:** aggregate no-op/rejection tests for terminal duplicates and cross-terminal cases;
tenant-safe descendant candidate contracts; pure `WorkItemCancelled` -> `CancelWorkItem` and
`WorkItemExpired` -> `ExpireWorkItem` command-intent translation; same-tenant fail-closed checks;
tests proving the reactor remains mechanical; documentation and test-summary updates.

**Out of scope:** Dapr dispatch, EventStore stream reads, runtime checkpoint persistence, retry loops,
durable cascade continuation, AppHost restart recovery, reminder reconciliation, timer scheduling,
Aspire crash/recovery proof, production adapters, query-side authorization UI, and any new channel
surface. Runtime cascade recovery is deliberately deferred to Story 4.6. [Source:
_bmad-output/planning-artifacts/epics.md#Story 3.6: Cascade Terminal Work Through Active Descendants;
_bmad-output/planning-artifacts/epics.md#Story 4.6; _bmad-output/planning-artifacts/architecture.md#C1]

### Current State (files this story modifies or verifies - read before editing)

- `src/Hexalith.Works.Contracts/Commands/CancelWorkItem.cs` already documents that cascade through active
  descendants is Story 3.6 scope. The command currently carries only `TenantId` and `WorkItemId`; keep it
  that small unless a source requirement proves otherwise.
- `src/Hexalith.Works.Contracts/Commands/ExpireWorkItem.cs` already models expiry as adapter-fired command
  data and not a clock read. Do not add TTL/date calculation to the command or handler.
- `src/Hexalith.Works.Contracts/Events/WorkItemCancelled.cs` and `WorkItemExpired.cs` carry only
  `AggregateId`, `Sequence`, `TenantId`, and `WorkItemId`. Story 3.6 should use these existing terminal
  events as cascade triggers instead of adding `CascadeStarted` or `DescendantCancelled` event types.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` already applies `WorkItemCancelled` and
  `WorkItemExpired` by setting terminal status and advancing sequence. Rejection events do not advance
  sequence.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` already handles `CancelWorkItem` and
  `ExpireWorkItem` through `WorkItemLifecycle.Decide`. The story should add tests before changing this
  behavior; code changes may be unnecessary if the current table satisfies AC #2/#3.
- `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` already centralizes the 9-state transition
  table. Terminal rows currently no-op only exact duplicate terminal commands: `Completed+Complete`,
  `Cancelled+Cancel`, `Rejected+Reject(Requeue:false)`, and `Expired+Expire`.
- `docs/lifecycle-transition-matrix.md` already mirrors the lifecycle code and contains the AR-13
  per-state cancel/expire decision table. Treat that document as authoritative unless tests reveal drift.
- `src/Hexalith.Works.Reactor/ChildCompletionResumeTranslator.cs` is the local pattern for a pure
  mechanical translator: explicit inputs in, command intents out, no acceptance decision, no runtime
  dispatch.
- `src/Hexalith.Works.Reactor/AwaitingParent.cs` is a small Reactor-local input record. Reuse this style
  for cascade descendant candidate inputs.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` already zeros terminal nodes and
  ignores cross-tenant child traversal. Story 3.6 should not move cascade traversal into this projection.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs` already locks Reactor
  to `Hexalith.Works.Contracts` only.

### Key Design Decisions

- **D1 - Target aggregates decide terminal outcomes.** The cascade translator may choose a terminal
  command kind from the parent terminal event, but it must not decide whether a descendant is allowed to
  cancel/expire. The descendant `WorkItemAggregate.Handle` applies the lifecycle matrix. [Source:
  _bmad-output/planning-artifacts/architecture.md#C1]
- **D2 - Idempotency is on both sides.** The translator should avoid emitting commands for candidates
  explicitly known to be terminal, but duplicate delivery is still safe because an already-`Cancelled`
  item receiving `CancelWorkItem` and an already-`Expired` item receiving `ExpireWorkItem` return
  `DomainResult.NoOp`. Cross-terminal commands reject and emit no duplicate terminal event. [Source:
  docs/lifecycle-transition-matrix.md#Idempotent no-op list]
- **D3 - Tenant equality fails closed.** Candidate descendant tenant must equal the parent terminal event
  tenant before a command intent is emitted. Key-prefixing alone is not enough; tenant equality must be
  explicit in the pure selection/translation step. [Source:
  _bmad-output/planning-artifacts/architecture.md#D2; docs/work-tree-shape-guard.md#Rules]
- **D4 - Cascade discovery is supplied, not performed here.** The pure translator consumes a caller-supplied
  list of descendants still requiring terminal cascade. It does not query projections, recurse through
  EventStore, keep checkpoints, or scan a tree itself. [Source:
  _bmad-output/planning-artifacts/architecture.md#C1; _bmad-output/planning-artifacts/epics.md#Story 3.6]
- **D5 - No new durable catalog unless required.** FR-10 can be satisfied by existing terminal commands and
  events plus Reactor-local command-intent input/output. Avoid adding durable v1 event types that would
  expand the frozen catalog without source backing. [Source: _bmad-output/planning-artifacts/epics.md#FR-7; #FR-10]

### Technical Requirements

- Keep `Contracts`, `Server`, and `Projections` pure and infrastructure-free: no Dapr, EventStore.Server,
  HTTP, filesystem, timers/clocks, generated IDs, logging I/O, UI, LLM, routing, or cost-governance
  dependencies. [Source: _bmad-output/planning-artifacts/architecture.md#Process Patterns]
- Keep `Reactor` pure and deterministic. It may reference `Contracts` only and produce command intents
  from explicit inputs. Runtime dispatch/checkpointing belongs outside this story. [Source:
  _bmad-output/planning-artifacts/architecture.md#Structure Patterns]
- Use the existing `DomainResult` conventions: success and rejection payloads never mix; rejections are
  `IRejectionEvent`; idempotent terminal duplicates are `DomainResult.NoOp` with no events. [Source:
  _bmad-output/planning-artifacts/architecture.md#Format Patterns]
- Events and commands stay under v1 naming rules: commands imperative with no `Command` suffix, events
  past tense with no `Event` suffix, sealed records, file-scoped namespaces. [Source:
  _bmad-output/planning-artifacts/architecture.md#Naming Patterns]
- Use xUnit v3 + Shouldly; extend existing test projects and helpers instead of creating a new harness.
  [Source: Hexalith.EventStore/_bmad-output/project-context.md#Testing Rules; Directory.Packages.props]
- No new third-party packages and no version upgrades. The repo-pinned stack remains .NET SDK `10.0.301`,
  Aspire `13.4.3`, Dapr `1.18.2`, xUnit v3 `3.2.2`, Shouldly `4.3.0`, and FsCheck `3.3.3`. [Source:
  global.json; Directory.Packages.props]
- Hexalith dependencies must remain `ProjectReference`s through root submodule paths; never add
  `Hexalith.*` `PackageReference`s or `Directory.Packages.props` Hexalith versions. [Source: AGENTS.md]

### Project Structure Notes

- Reactor-local cascade candidate/translator types belong under `src/Hexalith.Works.Reactor`.
- Aggregate behavior changes, if any, belong only in `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
  and `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs`.
- Durable command/event contracts remain in `src/Hexalith.Works.Contracts/Commands` and
  `src/Hexalith.Works.Contracts/Events`; prefer reusing existing terminal commands/events.
- Roll-up regressions belong in `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs`; cascade
  translator tests can follow `ChildCompletionResumeTranslatorTests.cs`.
- Documentation belongs in `docs/lifecycle-transition-matrix.md` only when lifecycle behavior changes;
  cascade orchestration notes can live in `docs/work-tree-shape-guard.md` or a focused cascade doc.
- Do not create `.UI`, `.Mcp`, portal, `.Security`, channel-adapter, database, Dapr-actor, repository,
  runtime-host, reminder, or checkpoint projects for this story.

### Previous Story Intelligence

- Story 3.5 completed in commit `f8856f2` and left the workspace at **457** green tests: UnitTests 368,
  IntegrationTests 60, ArchitectureTests 28, PropertyTests 1. Use that as the baseline.
- Story 3.5 established the Reactor pattern with `ChildCompletionResumeTranslator`: pure, kind-aware,
  mechanical command-intent translation with no runtime dispatch and no aggregate acceptance decision.
  Story 3.6 should mirror that pattern for terminal cascade.
- Story 3.5 updated `DependencyDirectionTests` so `Hexalith.Works.Reactor` is an adapter-ring project
  that references `Hexalith.Works.Contracts` only. Do not relax this fitness guard to add runtime
  packages.
- Story 3.3/3.4 established roll-up terminal semantics: completed, cancelled, expired, and non-requeue
  rejected children contribute zero; requeued rejection does not. Cascade must reuse those terminal events,
  not add a projection-specific subtraction path.
- The reliable verification path remains restore -> build -> direct xUnit v3 binaries. Do not use
  solution-level `dotnet test` if the direct binaries are the stable path in this sandbox.

### Git Intelligence Summary

- Recent commits show additive story slices with focused tests/docs and no broad refactors:
  `f8856f2 feat(story-3.5): Suspend and resume on await conditions`,
  `61ec4c5 feat(story-3.4): Preserve heterogeneous unit subtotals`,
  `5c95d1e feat(story-3.3): Maintain recursive roll-up with per-child sequence`,
  `eaeaf2e feat(story-3.2): Spawn child work from a parent`,
  `5792291 feat(story-3.1): Guard tenant-safe work tree shape`.
- The working tree already has unrelated modified submodule pointers (`Hexalith.FrontComposer`,
  `Hexalith.Parties`) and a story-automator orchestration file. Do not revert or depend on those.

### Latest Technical Information

- No external web/API research is required for this story. Implementation uses existing repo-pinned
  .NET/Hexalith contracts and pure in-process logic.
- Do not upgrade Dapr/Aspire/xUnit or introduce scheduler, queue, checkpoint, or persistence packages to
  satisfy cascade. Runtime durability and recovery are deferred.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.6: Cascade Terminal Work Through Active Descendants]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-10]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-11]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-13]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-1]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-5]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-9]
- [Source: _bmad-output/planning-artifacts/architecture.md#C1]
- [Source: _bmad-output/planning-artifacts/architecture.md#D2]
- [Source: _bmad-output/planning-artifacts/architecture.md#Communication Patterns]
- [Source: docs/lifecycle-transition-matrix.md#Per-state Cancel / Expire decision (AR-13)]
- [Source: docs/lifecycle-transition-matrix.md#Idempotent no-op list]
- [Source: docs/work-roll-up-projection.md#Rules]
- [Source: docs/work-tree-shape-guard.md#Rules]
- [Source: _bmad-output/implementation-artifacts/3-5-suspend-and-resume-on-await-conditions.md#Previous Story Intelligence]
- [Source: AGENTS.md#Hexalith library references -- ALWAYS use ProjectReference, NEVER PackageReference]
- [Source: AGENTS.md#Submodule rules -- READ BEFORE RUNNING ANY git submodule COMMAND]

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-17: Create-story workflow loaded `SKILL.md`, `discover-inputs.md`, `template.md`, and
  `checklist.md`; resolved BMAD customization; loaded sprint status, epics, architecture, PRD source
  hints, previous Story 3.5, persistent project-context facts, current code files, docs, package pins,
  and recent git history.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- Story 3.6 scoped to pure aggregate idempotency verification, tenant-safe descendant selection contracts,
  and mechanical Reactor command-intent translation.
- Runtime cascade checkpointing, dispatch, AppHost restart recovery, reminder reconciliation, and Aspire
  crash/recovery proof explicitly deferred to Story 4.6.

**dev-story implementation (2026-06-17):**

- **Task 1 (reconciliation):** Confirmed the existing terminal/cascade foundations are already correct and
  no aggregate/lifecycle production change is required for AC #2/#3. `WorkItemLifecycle.Decide` already
  no-ops exact-duplicate terminal commands (`Cancelled+Cancel`, `Expired+Expire`) and rejects every
  cross-terminal command via `WorkItemTransitionRejected` without burning sequence; the roll-up already
  zeros terminal nodes. The AR-13 table in `docs/lifecycle-transition-matrix.md` remains the single source
  of truth — left intact (D5, no second cascade table).
- **Tasks 3 & 4 (pure reactor slice):** Added Reactor-local `CascadeDescendant` (TenantId, WorkItemId,
  IsTerminal — no envelope/Dapr/checkpoint/status/roll-up/Party/adapter detail) and the pure
  `TerminalCascadeTranslator` mapping `WorkItemCancelled`→`CancelWorkItem` and `WorkItemExpired`→
  `ExpireWorkItem` intents. Fail-closed tenant equality (D3), explicit terminal-candidate skip (D2),
  input-order preserving, no tree traversal, no acceptance decision (D1/D4). Reactor still references only
  `Hexalith.Works.Contracts`; `DependencyDirectionTests` unchanged and green. No new third-party packages.
- **Task 2 (aggregate idempotency tests):** `WorkItemTerminalCascadeIdempotencyTests` (+8 cases) proves
  the cascade-delivery contract at the transition table — duplicate self-terminal = NoOp with no event /
  no rejection / no sequence burn; cross-terminal = `WorkItemTransitionRejected` with no terminal event and
  no state change. No production code needed (current table satisfies AC #2/#3).
- **Task 5 (reactor unit coverage):** `TerminalCascadeTranslatorTests` (+8 facts) covers cancel/expire
  cascade, active-vs-terminal filtering, fail-closed cross-tenant ignore (colliding ids), empty input, and
  the no-acceptance-decision/no-dedup contract.
- **Task 6 (integration + roll-up + docs):** `TerminalCascadeContractFlowTests` (+2) drives the cascade
  through real `System.Text.Json` into descendant aggregates with idempotent redelivery; +2 roll-up facts
  prove cascade terminal events zero descendant contribution and drop the open subtree from a still-active
  ancestor. `WorkItemV1Catalog.Count` stays 36 and the golden corpus is unchanged (no new durable type;
  the Reactor types are not polymorphic-serialized). Added a cascade section to
  `docs/work-tree-shape-guard.md`; `docs/lifecycle-transition-matrix.md` left intact.
- **Task 7 (verify):** Restore + Release build clean (0 warnings / 0 errors). Direct xUnit v3 binaries:
  UnitTests **386/386**, IntegrationTests **62/62**, ArchitectureTests **28/28**, PropertyTests **1/1**
  (`Ok, passed 100 tests.`). Total **477** green (Story 3.5 baseline 457, +20).
- **All 6 ACs satisfied:** #1 cascade emits terminal intents that descendants apply (translator + contract
  flow); #2 already-terminal descendant unaffected, no duplicate event (idempotency tests + roll-up);
  #3 duplicate cascade delivery is idempotent per AR-13 (idempotency + integration redelivery); #4 pure
  mechanical translation, no domain-outcome decision (translator tests); #5 scope held to aggregate
  behavior + idempotent target commands + tenant-safe selection contracts + pure intents, runtime deferred;
  #6 cross-tenant tenant equality fails closed (translator + contract-flow + roll-up).

### Change Log

- 2026-06-17: Created Story 3.6 context file and marked sprint status ready-for-dev.
- 2026-06-17: dev-story implementation — added pure Reactor cascade slice (`CascadeDescendant`,
  `TerminalCascadeTranslator`), aggregate idempotency + reactor + roll-up + integration cascade tests, and
  a cascade doc section. No aggregate/lifecycle or durable-catalog change. 477 green tests (+20). Status →
  review.
- 2026-06-17: senior-developer review (AI, auto-fix) — verified all 6 ACs and all 7 tasks against the
  implementation; re-ran restore/Release build (0 warnings / 0 errors) and all four xUnit v3 binaries
  (UnitTests 389, IntegrationTests 63, ArchitectureTests 28, PropertyTests 1 = 481 green). Found 1 LOW
  documentation finding (dangling `D2/D3` cross-reference in the cascade doc) and auto-fixed it. 0 CRITICAL
  / 0 HIGH / 0 MEDIUM. Status → done.

### File List

- `_bmad-output/implementation-artifacts/3-6-cascade-terminal-work-through-active-descendants.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `src/Hexalith.Works.Reactor/CascadeDescendant.cs` (new)
- `src/Hexalith.Works.Reactor/TerminalCascadeTranslator.cs` (new)
- `tests/Hexalith.Works.UnitTests/WorkItemTerminalCascadeIdempotencyTests.cs` (new)
- `tests/Hexalith.Works.UnitTests/TerminalCascadeTranslatorTests.cs` (new)
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` (modified)
- `tests/Hexalith.Works.IntegrationTests/TerminalCascadeContractFlowTests.cs` (new)
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` (modified)
- `docs/work-tree-shape-guard.md` (modified)

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot · **Date:** 2026-06-17 · **Mode:** adversarial review with auto-fix ·
**Outcome:** Approve (1 LOW finding auto-fixed).

### Scope & method

Validated every story claim against the actual implementation rather than the narrative: read all source
in the File List, cross-referenced the File List against `git status` (the only undocumented changes are
the pre-existing, unrelated `Hexalith.FrontComposer` / `Hexalith.Parties` submodule pointers and the
`_bmad-output/story-automator/` orchestration file — all excluded from review), traced all six ACs and
all seven `[x]` tasks to code, and independently re-ran the verification.

### Verification performed

- `dotnet build Hexalith.Works.slnx -c Release` → **0 warnings, 0 errors**.
- Direct xUnit v3 binaries: **UnitTests 389/389, IntegrationTests 63/63, ArchitectureTests 28/28,
  PropertyTests 1/1 = 481 green** — matches the story's claimed counts exactly.
- `DependencyDirectionTests` (green) confirms `Hexalith.Works.Reactor` references `Hexalith.Works.Contracts`
  only (D-direction held; no Dapr/Aspire/EventStore-runtime/clock/IO added).
- `WorkItemV1Catalog.Count == 36` (14 events + 14 commands + 8 rejections) — no new durable type (D5 held);
  the new Reactor types are not polymorphic-serialized.

### Acceptance Criteria

- **AC #1–#6: all satisfied.** Cancel/expire cascade emits same-kind intents for same-tenant active
  descendants (`TerminalCascadeTranslator` + contract-flow); already-terminal descendants are
  skipped/no-op with no duplicate event (idempotency + roll-up tests); duplicate/redelivered cascade is
  idempotent per AR-13 (idempotency + integration redelivery, incl. cross-terminal through serialization);
  translation is pure/mechanical with no domain-outcome decision (translator tests); scope held to
  aggregate behavior + idempotent commands + tenant-safe selection + pure intents with runtime deferred;
  cross-tenant tenant equality fails closed even with colliding ids (translator + contract-flow + roll-up).

### Findings

- 🔴 CRITICAL: none · 🟡 HIGH: none · 🟡 MEDIUM: none
- 🟢 **LOW-1 (auto-fixed):** `docs/work-tree-shape-guard.md` cascade section ended the tenant-equality
  bullet with "(see [Rules](#rules), D2/D3)". The `#rules` anchor is valid but contains no `D2/D3` labels
  (those are story/architecture design-decision tags, where D2 is *idempotency*, not tenant equality), so
  the reference was unresolvable. Rewritten to point to the concrete cross-tenant fail-closed rule that
  actually lives in the Rules section.

### Notes

Clean, well-scoped pure slice that correctly leans on existing target-aggregate idempotency instead of an
out-of-band dedup store; tests are real assertions (state, sequence, event shape, order) rather than
placeholders. No production-code change was required or made by the review beyond the documentation fix.
