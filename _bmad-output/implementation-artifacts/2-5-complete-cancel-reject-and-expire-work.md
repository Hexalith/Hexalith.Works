---
baseline_commit: 18143015200e573c17e7b572d12beeb2a7a0ce94
---

# Story 2.5: Complete, Cancel, Reject, and Expire Work

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an executor or coordinator,
I want Work Items to terminate through explicit domain acts,
so that completion and abnormal endings are auditable, replayable, and enforce terminal-state rules.

## Acceptance Criteria

1. **Given** an estimated Work Item reaches Remaining zero through progress
   **When** state is replayed
   **Then** `WorkItemCompleted` makes the item terminal
   **And** later progress, schedule, assignment, or suspend commands emit an `IRejectionEvent`
   **And** exact duplicate completion or terminal commands return `DomainResult.NoOp` only where
   `docs/lifecycle-transition-matrix.md` explicitly lists them as idempotent.

2. **Given** an unestimated Work Item is explicitly completed
   **When** the complete act is handled
   **Then** `WorkItemCompleted` is emitted
   **And** the completion does not rely on the Remaining=0 rule.

3. **Given** a non-terminal Work Item is cancelled
   **When** `CancelWorkItem` is handled
   **Then** `WorkItemCancelled` is emitted
   **And** the item becomes terminal with no further progress accepted.

4. **Given** a bound executor rejects an assignment
   **When** `RejectWorkItem` is handled with the default requeue behavior
   **Then** `WorkItemRejected` is emitted
   **And** the item returns to `Queued` for reassignment.

5. **Given** a bound executor rejects an assignment as non-requeuable
   **When** `RejectWorkItem` is handled
   **Then** `WorkItemRejected` is emitted
   **And** the item becomes terminal.

6. **Given** expiry is Due-Date or TTL driven
   **When** an expiry command is handled
   **Then** `WorkItemExpired` is emitted
   **And** the item becomes terminal without the aggregate reading a clock.

7. **Given** cancel and expire may later cascade through a Work Tree
   **When** the 9-status cancel/expire transition table is reviewed
   **Then** every source status has an explicit decision
   **And** already-terminal descendants are defined as unaffected for downstream cascade execution.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile Story 2.5 scope against existing lifecycle implementation (AC: #1-#7)**
  - [x] Read `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` and
    `docs/lifecycle-transition-matrix.md` before changing code. The table already contains the
    Story 2.5 terminal decisions: complete from `InProgress`/`Suspended`, cancel/expire from every
    non-terminal status, reject from `Assigned` with `Requeue` selecting `Queued` vs terminal
    `Rejected`, and exact duplicate terminal no-ops only for completed/cancelled/rejected/expired.
  - [x] Treat `WorkItemLifecycle.Decide(...)` and `docs/lifecycle-transition-matrix.md` as a paired
    source of truth. If implementation changes are needed, update both in the same change and keep
    `LifecycleTransitionMatrixDocTests` green.
  - [x] Do not create a second transition table, status helper, command dispatcher, or cascade policy
    object for this story. Extend the existing aggregate/table/tests.

- [x] **Task 2 - Verify and fill explicit completion behavior (AC: #1, #2)**
  - [x] Confirm `CompleteWorkItem` emits `WorkItemCompleted` from `InProgress` and `Suspended` with
    `Sequence = state.Sequence + 1`, and `WorkItemState.Apply(WorkItemCompleted)` sets
    `Status = Completed` and advances `Sequence`.
  - [x] Add or keep focused tests proving explicit completion works for an unestimated
    `InProgress` item and an unestimated `Suspended` item without requiring `Remaining == 0`.
  - [x] Add a focused test for progress-driven completion: `ReportProgress` that lands Remaining on
    zero emits `ProgressReported` followed by `WorkItemCompleted`; after replay, terminal-state
    commands outside the idempotent duplicate list return `WorkItemTransitionRejected` and do not
    advance `Sequence`.
  - [x] Ensure explicit completion does not change `InitialEffort`, fabricate an estimate, or store a
    derived Remaining field.

- [x] **Task 3 - Verify and fill cancel behavior (AC: #3, #7)**
  - [x] Confirm `CancelWorkItem` emits `WorkItemCancelled` from `Created`, `Assigned`, `Queued`,
    `InProgress`, and `Suspended`; replay makes `Status = Cancelled`.
  - [x] Confirm `CancelWorkItem` against `Cancelled` returns `DomainResult.NoOp`, and cancel against
    `Completed`, `Rejected`, or `Expired` returns `WorkItemTransitionRejected`.
  - [x] Add targeted tests proving no post-cancel progress, reschedule, assign, queue, claim,
    suspend, resume, complete, reject, or expire command can move the item out of `Cancelled`.
  - [x] Keep cascade execution out of this story. Story 2.5 must define the per-state target-command
    semantics that Story 3.6 will use; it must not implement descendant traversal, projections,
    checkpoints, reactor dispatch, Dapr, AppHost recovery, or roll-up updates.

- [x] **Task 4 - Verify and fill reject behavior (AC: #4, #5)**
  - [x] Confirm `RejectWorkItem(TenantId, WorkItemId)` defaults `Requeue = true`, emits
    `WorkItemRejected(..., Requeue: true)` only from `Assigned`, and replay rests at `Queued`.
  - [x] Confirm `RejectWorkItem(..., Requeue: false)` from `Assigned` emits
    `WorkItemRejected(..., Requeue: false)` and replay rests at terminal `Rejected`.
  - [x] Confirm `RejectWorkItem(Requeue: false)` against already-`Rejected` returns
    `DomainResult.NoOp`; `RejectWorkItem(Requeue: true)` against already-`Rejected` is rejected and
    never reopens the terminal item.
  - [x] Add or keep tests that disambiguate `WorkItemRejected` from `WorkItemTransitionRejected`.
    `WorkItemRejected` is accepted raw-act evidence and may rest at `Queued`; `WorkItemTransitionRejected`
    is a rejection-only payload for illegal commands and is never stream-appended.

- [x] **Task 5 - Verify and fill expiry behavior without clocks (AC: #6, #7)**
  - [x] Confirm `ExpireWorkItem` emits `WorkItemExpired` from every non-terminal status and replay
    makes `Status = Expired`.
  - [x] Confirm `ExpireWorkItem` against `Expired` returns `DomainResult.NoOp`; expire against
    `Completed`, `Cancelled`, or `Rejected` returns `WorkItemTransitionRejected`.
  - [x] Add or keep an architecture/negative test proving expiry handling reads no clock, timer,
    scheduler, Dapr, or I/O API. The command is the adapter-fired signal; Due-Date/TTL sourcing and
    reminder delivery are later adapter work.
  - [x] Do not add `DateTime.Now`, `DateTimeOffset.Now`, `Stopwatch`, timer abstractions, reminder
    registration, scheduler dependencies, or expiry query logic in `Contracts`, `Server`, or
    `Projections`.

- [x] **Task 6 - Strengthen terminal-state and no-op coverage (AC: #1, #3-#7)**
  - [x] Keep the data-driven lifecycle matrix test covering every non-reject command across all nine
    statuses. Extend it only if a Story 2.5 gap is found.
  - [x] Add focused tests for post-terminal rejection of the planning acts added in Story 2.4:
    `ReEstimate` and `RescheduleWorkItem` must return `WorkItemTransitionRejected` from
    `Completed`, `Cancelled`, `Rejected`, and `Expired`.
  - [x] Assert rejection and no-op commands leave `Status`, `InitialEffort`, `Schedule`,
    `ExecutorBinding`, and `Sequence` unchanged where relevant.
  - [x] Verify `DomainResult` never mixes success and rejection payloads; rejections carry no
    `Sequence`, and no-op results carry no events.

- [x] **Task 7 - Preserve serialization catalog and golden corpus (AC: #1-#6)**
  - [x] Confirm the existing `WorkItemV1Catalog.Count` remains **31** unless new payload types are
    genuinely added. Story 2.5 should normally add no new command/event type: terminal commands and
    events already exist in source and in the catalog.
  - [x] Confirm `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, and
    `WorkItemExpired` remain `[PolymorphicSerialization]` records implementing `IEventPayload`, with
    success events carrying `(AggregateId, Sequence)` first.
  - [x] Keep or add golden-corpus tests for all terminal events. Existing fixtures include
    `WorkItemCompleted.v1.json`, `WorkItemCancelled.v1.json`, `WorkItemRejected.v1.json`, and
    `WorkItemExpired.v1.json`; update them only through the production `JsonSerializerDefaults.Web`
    serializer if the concrete shape intentionally changes.
  - [x] Confirm rejection payloads such as `WorkItemTransitionRejected` serialize without EventStore
    envelope fields and without `sequence`.

- [x] **Task 8 - Update documentation only where behavior or coverage changes (AC: #1-#7)**
  - [x] If the implementation already matches the matrix, avoid noisy documentation churn.
  - [x] If any matrix cell changes, update `docs/lifecycle-transition-matrix.md` and its notes,
    especially the "Per-state Cancel / Expire decision" table and the idempotent no-op list.
  - [x] Keep explicit documentation that already-terminal descendants are unaffected for downstream
    cascade execution. Do not document cascade runtime behavior as implemented in Story 2.5.

- [x] **Task 9 - Build and verify the slice (AC: #1-#7)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
  - [x] Run the built xUnit v3 executables directly because `dotnet test` is blocked in this sandbox
    by Microsoft.Testing.Platform named-pipe permissions:
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    and `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.
  - [x] Reconcile final counts in `_bmad-output/implementation-artifacts/tests/test-summary.md` and
    this story's Dev Agent Record. Story 2.4 final baseline is **289** green tests: UnitTests 217,
    IntegrationTests 45, ArchitectureTests 26, PropertyTests 1.

## Dev Notes

### Scope Boundary

Story 2.5 owns the final Epic 2 lifecycle semantics for a **single Work Item**: explicit completion,
cancel, reject, expire, terminal-state rejection/no-op rules, and the cancel/expire per-state table
needed by later cascade work. It closes **FR-10** and verifies the terminal edge of **FR-6/FR-7/FR-8**.
[Source: _bmad-output/planning-artifacts/epics.md#Story 2.5: Complete, Cancel, Reject, and Expire Work;
_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-8; #FR-10]

**In scope:** `CompleteWorkItem`, `CancelWorkItem`, `RejectWorkItem`, `ExpireWorkItem`; replay of
`WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`; terminal-state
rejections/no-ops; matrix/doc-code synchronization; contract-flow, serialization, and focused unit
tests.

**Out of scope:** descendant traversal, Work Tree shape, roll-up contribution updates, the cascade
reactor, checkpoints, Dapr dispatch, reminder/timer registration, Due-Date/TTL policy lookup,
query-side expiry detection, "what's next" ordering, AuthorityLevel enforcement, UI/channel adapters,
and production AppHost runtime behavior. Those belong to Stories 3.1-3.6 and Epic 4.
[Source: _bmad-output/planning-artifacts/epics.md#Story 3.6; _bmad-output/planning-artifacts/architecture.md#C1; #C2; #C3]

### Current State (files this story modifies or verifies - read before editing)

- `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` already encodes the full lifecycle
  table. It accepts cancel/expire from live statuses, accepts explicit completion from `InProgress`
  and `Suspended`, handles `RejectWorkItem` from `Assigned` based on the `Requeue` flag, and
  returns no-op only for exact duplicate terminal commands. Preserve this as the single code table.
- `docs/lifecycle-transition-matrix.md` mirrors `WorkItemLifecycle.cs`. It already has a
  per-state cancel/expire table and the idempotent no-op list. Any code behavior change must update
  the document in the same commit.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` already has handlers for
  `CompleteWorkItem`, `CancelWorkItem`, `RejectWorkItem`, and `ExpireWorkItem`. They should keep the
  same guard shape as the lifecycle handlers: null checks, `CurrentStatus`, `WorkItemLifecycle.Decide`,
  then success/no-op/rejection. Do not add clock or infrastructure calls.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` already applies the terminal events:
  `WorkItemCompleted` -> `Completed`, `WorkItemCancelled` -> `Cancelled`,
  `WorkItemRejected` -> `Queued` when `Requeue` is true or `Rejected` when false, and
  `WorkItemExpired` -> `Expired`. Rejection `Apply` overloads are trusted no-ops.
- `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs` already contains the data-driven
  transition-matrix coverage and reject-flag coverage. It is the first place to add missing terminal
  edge cases.
- `tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs` already covers progress-driven
  completion and explicit completion for unestimated `InProgress`/`Suspended` items. Reuse or extend
  it if AC #1/#2 gaps remain.
- `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs` already proves
  lifecycle command -> event -> JSON -> replay for the happy path, created branch events, reject
  replay, and illegal transition rejection serialization. Extend it if Story 2.5 needs more
  terminal JSON coverage.
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` already lists 13 success events,
  13 commands, and 5 rejection events. Story 2.5 should normally keep `Count = 31`.
- Golden files for the terminal events already exist under
  `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/`. Do not hand-author golden JSON.

### Key Design Decisions

- **D1 - Terminal no-op list is closed.** Only these pairs no-op:
  `Completed + CompleteWorkItem`, `Cancelled + CancelWorkItem`, `Expired + ExpireWorkItem`, and
  `Rejected + RejectWorkItem(Requeue: false)`. Every other command from a terminal status is a
  domain rejection. [Source: docs/lifecycle-transition-matrix.md#Idempotent no-op list]
- **D2 - Reject is raw-act evidence, not always terminal.** `WorkItemRejected(Requeue: true)` rests
  at `Queued`; `WorkItemRejected(Requeue: false)` rests at terminal `Rejected`. Do not conflate this
  success event with `WorkItemTransitionRejected`, which represents an illegal command.
  [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-10]
- **D3 - Expiry is command-driven.** The aggregate does not decide that a Due Date or TTL has passed.
  An adapter/timer later raises `ExpireWorkItem`; `Handle` stays clock-free. Deadlines are
  advisory-until-fired. [Source: _bmad-output/planning-artifacts/architecture.md#C3]
- **D4 - Cascade is defined here only as target aggregate semantics.** Cancel/expire of a parent
  will later cascade to still-active descendants, but Story 2.5 only defines how each descendant
  aggregate responds to duplicate or terminal cancel/expire commands. Runtime traversal and
  checkpointing are Story 3.6. [Source: _bmad-output/planning-artifacts/architecture.md#C1; #AR-13]
- **D5 - Completion paths are two distinct entry points.** Estimated work can complete through
  `ReportProgress` when Remaining reaches zero; unestimated work completes only through explicit
  `CompleteWorkItem`. Re-estimate is not a completion path. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-8; 2-4 story#D5]

### Technical Requirements

- Durable success events carry `(AggregateId, Sequence)` first and implement `IEventPayload`.
  Rejection events implement `IRejectionEvent`, carry no `Sequence`, and are not appended as success
  stream events. [Source: _bmad-output/planning-artifacts/epics.md#AR-4; #NFR-2]
- `DomainResult` must never mix success and rejection payloads. No-op results carry no events.
  [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns]
- `Handle` and `Apply` remain pure: no clock, RNG, I/O, Dapr, EventStore envelope APIs, logging, or
  scheduler/reminder calls inside `Contracts`, `Server`, or `Projections`. [Source:
  _bmad-output/planning-artifacts/architecture.md#Process Patterns]
- EventStore owns envelope metadata. Works events are payloads only and must not contain
  `messageId`, `correlationId`, `causationId`, `userId`, `metadata`, or `cloudEvent`. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md#Inherited substrate constraints]
- Additive, no-`V2` serialization applies to all terminal events. Preserve existing field names and
  tolerate unknown future fields. [Source: _bmad-output/planning-artifacts/epics.md#NFR-12]
- AuthorityLevel is carried, not enforced in v1. Do not add authorization branching to cancel,
  reject, complete, or expire handlers in this story. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-19]

### Project Structure Notes

- Contracts live under `src/Hexalith.Works.Contracts/{Commands,Events,Events/Rejections,State}`.
  Aggregate behavior lives in `src/Hexalith.Works.Server/Aggregates`. Tests stay in the existing
  `tests/Hexalith.Works.*` projects.
- Use xUnit v3 + Shouldly. Do not introduce Moq, FluentAssertions, raw `Assert.*`, sleeps, timers,
  Dapr, Aspire, containers, or browser tooling for this pure domain slice.
- Keep Hexalith dependencies as `ProjectReference` through the root submodules. Do not add
  `PackageReference` or `Directory.Packages.props` versions for `Hexalith.*` libraries, and do not
  initialize nested submodules.
- This repository owns Works domain code only. Persistence, ID generation, and shared substrate
  changes belong in the relevant sibling module, not here.

### Previous Story Intelligence

- Story 2.4 finished with 289 green tests and no production-code changes during QA gap fill. Keep
  Dev Agent Record counts and `_bmad-output/implementation-artifacts/tests/test-summary.md` in
  lockstep; earlier stories had count drift that review flagged.
- Story 2.4 established that terminal/planning guard behavior should reuse existing helpers and
  matrix documentation rather than inventing local rules. It also confirmed direct execution of the
  xUnit v3 binaries is the reliable verification path in this sandbox.
- Story 2.3 already owns progress math, progress-driven completion, unestimated-progress rejection,
  and explicit complete for unestimated work. Story 2.5 should strengthen terminal semantics around
  those paths, not rewrite burn-down.
- Story 2.1 owns the lifecycle transition matrix as the single source of truth. Story 2.5 must
  reference and verify it.

### Git Intelligence

- `1814301 feat(story-2.4): Re-estimate and reschedule work` added the latest lifecycle-adjacent
  command/event/rejection slice, extended the catalog to 31, added golden fixtures, and reconciled
  test counts to 289.
- `cbf1cba feat(story-2.3): Report progress with unit-tagged burn-down` added progress-driven
  completion and explicit-complete tests relevant to AC #1/#2.
- `ccf73c5 feat(story-2.2): Record raw-act events and replay state` added polymorphic
  registration and golden-corpus scaffolding.
- `fb757f2 feat(story-2.1): Define the lifecycle state machine` added the lifecycle table,
  matrix doc, and broad matrix tests this story must preserve.
- The current working tree has unrelated pre-existing changes in `Hexalith.FrontComposer`,
  `Hexalith.Parties`, and `_bmad-output/story-automator/orchestration-1-20260615-182114.md`.
  Do not revert or mix them into Story 2.5 work.

### UX / Read-Model Context

No production UI ships in v1. The UX artifacts still matter as traceability: terminal statuses will
later render as status pills and cascade states in the Work Tree, and terminal descendants contribute
zero to Roll-Up. Story 2.5 does not implement those surfaces or projections; it provides the raw
terminal events and stable state semantics they will consume. [Source:
_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#State Patterns]

### Latest Technical Specifics

No external web research is required for this story. The implementation is source-local and version
pinned: .NET 10 / `net10.0`, xUnit v3 `3.2.2`, Shouldly, `System.Text.Json`
`JsonSerializerDefaults.Web`, and `Hexalith.PolymorphicSerializations`. Do not change pinned
versions or introduce new libraries.

### Testing Standards

- Tier-1 pure tests first: aggregate `Handle`, state `Apply`, transition matrix, and concrete JSON
  round-trip. No Dapr/Aspire/network/clock.
- Assert event order and `Sequence` explicitly. Rejections/no-ops must not advance `Sequence`.
- Tests should arrange state by replaying events through `WorkItemStateBuilder` or local replay
  helpers; do not mutate private state or depend on test order.
- Generate golden fixtures from the production serializer only if a concrete event shape changes,
  then delete any temporary emitter.
- Release build is warnings-as-errors. `dotnet test` is known to be blocked in this sandbox; use the
  built xUnit executable files directly after build.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.5: Complete, Cancel, Reject, and Expire Work]
  - story statement and AC #1-#7.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-10] - cancel/reject/expire semantics and
  cascade traceability.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-8] - unestimated
  work completes only by explicit complete act.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-10] - reject defaults
  to requeue; expire is Due-Date/TTL driven; cancel/expire cascade to active descendants later.
- [Source: _bmad-output/planning-artifacts/architecture.md#C1] - reactor/process-manager lives
  outside the kernel; cascade is checkpoint-driven and resumable later.
- [Source: _bmad-output/planning-artifacts/architecture.md#C3] - deadlines are advisory-until-fired;
  no clock read in the aggregate.
- [Source: docs/lifecycle-transition-matrix.md] - single source of truth for lifecycle decisions,
  terminal no-op list, and per-state cancel/expire table.
- [Source: src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs] - pure lifecycle decision table
  to preserve.
- [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs] - existing terminal handlers
  and guard cascade.
- [Source: src/Hexalith.Works.Contracts/State/WorkItemState.cs] - replay behavior for terminal
  events and rejection no-op applies.
- [Source: tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs] - lifecycle matrix coverage.
- [Source: tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs] - progress-driven completion and
  explicit complete coverage.
- [Source: tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs] - lifecycle
  JSON contract-flow coverage.
- [Source: tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs] - polymorphic v1 catalog,
  currently `Count = 31`.

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-16: Loaded `bmad-dev-story` workflow and DoD checklist.
- 2026-06-16: Verified `WorkItemLifecycle.Decide(...)`, `WorkItemAggregate`, `WorkItemState`, and
  `docs/lifecycle-transition-matrix.md` already matched Story 2.5 terminal semantics.
- 2026-06-16: Added focused unit coverage for post-cancel guards, progress-driven terminal guards,
  terminal planning-act rejection, and DomainResult no-op/rejection separation.
- 2026-06-16: QA generate-e2e-tests pass added focused unit coverage for default reject requeue and
  expiry from every non-terminal state.
- 2026-06-16: Extended the kernel purity architecture test to scan `Contracts`, `Server`, and
  `Projections` for clocks, timers, Dapr, HTTP, generated IDs, and filesystem APIs.
- 2026-06-16: Validation passed: restore, Release build, and direct xUnit v3 executable runs.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- No production lifecycle behavior changed; `WorkItemLifecycle`, `WorkItemAggregate`,
  `WorkItemState`, and `docs/lifecycle-transition-matrix.md` already encoded the required Story 2.5
  terminal decisions.
- Added 43 focused unit test cases covering cancel and expire from all non-terminal states,
  post-cancel immutability, default reject requeue behavior, progress-driven completion terminal
  rejection/no-op behavior, terminal planning-act rejection, DomainResult no-op/rejection payload
  separation, and default-reject-as-rejection from every non-`Assigned` status (including the
  `Rejected + Reject(requeue: true)` cell the data-driven matrix Theory deliberately excludes, proving
  a requeue reject never reopens a terminal item).
- Strengthened the architecture purity guard so expiry remains an adapter-fired command with no
  clock/timer/scheduler/Dapr/I/O access in `Contracts`, `Server`, or `Projections`.
- Confirmed serialization/catalog coverage remains stable: `WorkItemV1Catalog.Count` is 31 and
  existing terminal-event golden fixtures remain valid.
- Final validation: 332/332 tests passed (UnitTests 260, IntegrationTests 45, ArchitectureTests 26,
  PropertyTests 1).

### File List

- `_bmad-output/implementation-artifacts/2-5-complete-cancel-reject-and-expire-work.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs`

### Change Log

- 2026-06-16: Started Story 2.5, captured baseline commit, and moved sprint/story status through
  implementation to review.
- 2026-06-16: Added Story 2.5 terminal lifecycle coverage without changing production lifecycle code
  or the lifecycle matrix documentation.
- 2026-06-16: Updated test-summary counts from the Story 2.4 baseline of 289 to the Story 2.5 final
  total of 324 passing tests.
- 2026-06-16: Automated code review (bmad-story-automator-review). Verified clean Release build
  (0 warnings) and reproduced the suite independently. Found and auto-fixed one MEDIUM coverage gap:
  the `Reject` act was never asserted as a `WorkItemTransitionRejected` from any status, leaving the
  Task 4 claim "`RejectWorkItem(Requeue: true)` against already-`Rejected` is rejected and never
  reopens" unverified. Added `Default_reject_from_any_non_assigned_status_is_a_transition_rejection_and_never_reopens`
  (8 cases). New total: 332 passing tests (UnitTests 260). No production code changed.
