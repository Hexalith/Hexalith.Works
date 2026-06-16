---
baseline_commit: ccf73c51c49dffbf781d4ac5b1920f6cbc7198d9
---

# Story 2.3: Report Progress with Unit-Tagged Burn-Down

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an executor,
I want to report progress in the Work Item's Unit,
so that Remaining effort burns down as a fact and completion happens when Remaining reaches zero.

## Acceptance Criteria

1. **Given** a Work Item has an Effort `Meter(Unit, Estimated, Done)`
   **When** state is inspected after replay
   **Then** Remaining is derived as `Estimated - Done`
   **And** Remaining is never represented below zero.

2. **Given** an executor reports a positive Done delta in the Work Item's Unit
   **When** `ReportProgress` is handled for an estimated Work Item
   **Then** `ProgressReported` is emitted
   **And** replaying the event increases Done and decreases Remaining by the reported delta, clamped at zero.

3. **Given** progress causes Remaining to reach zero
   **When** the event sequence is replayed
   **Then** the Work Item transitions synchronously to `Completed`
   **And** `WorkItemCompleted` is emitted as part of the accepted completion path.

4. **Given** a Work Item has no Estimated effort
   **When** progress is reported
   **Then** the item does not complete through the Remaining=0 path
   **And** completion requires an explicit complete act.

5. **Given** progress uses a negative delta or a Unit different from the Work Item's established Unit
   **When** `ReportProgress` is handled
   **Then** the command is rejected as a domain rejection
   **And** replayed state is unchanged.

## Tasks / Subtasks

- [x] **Task 1 - Add the progress contract surface (AC: #2, #5)**
  - [x] Add `src/Hexalith.Works.Contracts/Commands/ReportProgress.cs` as a sealed partial record with `[PolymorphicSerialization]`; fields should include `TenantId`, `WorkItemId`, `decimal DoneDelta`, `Unit Unit`, and optional `string? Note`.
  - [x] Add `src/Hexalith.Works.Contracts/Events/ProgressReported.cs` as a sealed partial record implementing `IEventPayload`; first fields must be `string AggregateId`, `long Sequence`, then `TenantId`, `WorkItemId`, `decimal DoneDelta`, `Unit Unit`, and optional `string? Note`.
  - [x] Add focused `IRejectionEvent` contract(s) for progress validation failures if `WorkItemTransitionRejected` is not specific enough. Rejection events carry no `Sequence` and are not stream-appended.
  - [x] Do not add `Hexalith.*` `PackageReference` or `PackageVersion`; use the existing PolymorphicSerializations ProjectReference already added by Story 2.2.

- [x] **Task 2 - Evolve effort state without breaking existing create semantics (AC: #1, #2, #4)**
  - [x] Extend `WorkItemEffort` with an explicit progress operation, for example `Report(decimal doneDelta)`, that validates positive deltas and returns a new `WorkItemEffort` with `Done = Min(Estimated, Done + doneDelta)`.
  - [x] Preserve current construction rules: `Estimated >= 0`, `Done >= 0`, `Unit` required, and externally constructed `Done > Estimated` remains invalid.
  - [x] Keep Remaining derived, not stored. Do not add a mutable Remaining field.
  - [x] Review `WorkItemState.InitialEffort`: it is currently the stored current effort field. Either retain the property name for compatibility while updating its value on progress, or add a clearer `Effort` alias without breaking existing tests.

- [x] **Task 3 - Handle progress in the aggregate (AC: #2, #3, #4, #5)**
  - [x] Add `WorkItemAggregate.Handle(ReportProgress command, WorkItemState? state)`.
  - [x] Accept progress only when the Work Item is in `InProgress`; other statuses, including `Suspended` and terminal states, must produce a domain rejection and no state change.
  - [x] Reject `DoneDelta <= 0` and Unit mismatches against the established effort Unit.
  - [x] For unestimated Work Items, reject `ReportProgress` rather than inventing unitless progress; explicit `CompleteWorkItem` remains the only completion path.
  - [x] On accepted progress, emit `ProgressReported` with the raw reported delta and Unit. If the updated own Remaining reaches zero, emit `WorkItemCompleted` in the same `DomainResult.Success` with the next sequence number.
  - [x] Keep `Handle` pure: no clock, RNG, I/O, EventStore envelope access, Dapr, logging, or sibling-module calls.

- [x] **Task 4 - Replay progress deterministically (AC: #1, #2, #3)**
  - [x] Add `WorkItemState.Apply(ProgressReported)` to update effort by replaying the reported delta and advancing `Sequence`.
  - [x] Apply `ProgressReported` before `WorkItemCompleted` in tests when both are emitted; final replay state must have `Status = Completed`, `Remaining = 0`, and `Sequence` equal to the completion event sequence.
  - [x] Continue trusting persisted success events on replay; validate at the writer (`Handle`), not defensively inside `Apply`.
  - [x] Preserve EventStore envelope ownership: Works events remain payloads only.

- [x] **Task 5 - Update lifecycle documentation and fitness gates (AC: #2, #3, #5)**
  - [x] Update `docs/lifecycle-transition-matrix.md` to include `ReportProgress` as a progress act: accepted only from `InProgress`, remaining-dependent target `InProgress` or `Completed`, rejected elsewhere.
  - [x] Update `LifecycleTransitionMatrixDocTests` and lifecycle/unit tests so the documentation and code remain synchronized.
  - [x] Update `P0_WorkItemSliceDoesNotIntroduceDeferredBurnDownRollUpOrReminderBehavior`: burn-down/progress terms become in-scope for Story 2.3, but `RollUp` and `Reminder` remain banned from `src`.
  - [x] Do not introduce projections, roll-up state, SignalR, AppHost wiring, timers, or adapters in this story.

- [x] **Task 6 - Extend serialization registration and golden corpus (AC: #2, #5)**
  - [x] Add the new command/event/rejection types to `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`; update the catalog count and keep vacuous-pass guards meaningful.
  - [x] Confirm `HexalithWorksContractsSerialization.RegisterPolymorphicMappers()` resolves the new types through `Polymorphic`.
  - [x] Add a frozen concrete `ProgressReported.v1.json` fixture under `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/`, generated from `JsonSerializerDefaults.Web`.
  - [x] Extend `SchemaEvolutionGoldenCorpusTests` to deserialize, round-trip, and tolerate an additive unknown field for `ProgressReported`.
  - [x] Keep concrete EventStore-persisted JSON free of `$type` and envelope fields; polymorphic `$type` appears only when serializing through the `Polymorphic` base in the registration tests.

- [x] **Task 7 - Add focused tests for the progress slice (AC: #1-#5)**
  - [x] Unit tests: positive progress reduces Remaining; over-progress clamps Remaining to zero; completion emits `ProgressReported` then `WorkItemCompleted`; negative/zero delta rejects; Unit mismatch rejects; unestimated progress rejects; suspended/terminal/non-started progress rejects.
  - [x] Integration tests: command -> aggregate -> event JSON -> replay proves progress and auto-completion across concrete serialization.
  - [x] Regression tests: explicit `CompleteWorkItem` still completes an unestimated `InProgress` or `Suspended` item according to the existing matrix.
  - [x] Architecture tests: kernel purity and dependency-direction gates still pass; no roll-up/reminder/adapters leak into `src`.

- [x] **Task 8 - Build and verify the slice (AC: #1-#5)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
  - [x] Run the built xUnit v3 executables directly:
    - `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`
    - `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`
    - `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`
    - `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`
  - [x] Baseline after Story 2.2 is 227 tests green: UnitTests 166, IntegrationTests 34, ArchitectureTests 26, PropertyTests 1. Reconcile the final counts in `tests/test-summary.md` and this story's Dev Agent Record.

## Dev Notes

### Scope Boundary

Story 2.3 owns FR-3 and FR-8 for a single Work Item's own effort meter: report positive progress in the established Unit, update own Done/Remaining synchronously, and emit `WorkItemCompleted` when own Remaining reaches zero. It also mints the `ReportProgress` command and `ProgressReported` event from the frozen v1 catalog. [Source: _bmad-output/planning-artifacts/epics.md#Story 2.3; _bmad-output/planning-artifacts/epics.md#FR-3; _bmad-output/planning-artifacts/epics.md#FR-8]

**In scope:** Contracts for `ReportProgress`, `ProgressReported`, and progress-specific rejection(s); aggregate handling; `WorkItemEffort` progress math; `WorkItemState` replay; lifecycle matrix update; PolymorphicSerializations registration tests; golden corpus fixture; unit/integration/architecture tests.

**Out of scope:** `ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`; recursive Roll-Up projections; heterogeneous subtree subtotals beyond preserving the Unit on own effort; SignalR notifications; "what's next"; AppHost/Dapr/EventStore append wiring; executor routing; cost meter; reminder/timer/reactor work. These belong to Stories 2.4, 3.2-3.4, 4.4-4.6, or deferred themes. [Source: _bmad-output/planning-artifacts/epics.md#Scope reminder; _bmad-output/planning-artifacts/architecture.md#Project Structure & Boundaries]

### Current State

- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs` already models `Estimated`, `Unit`, `Done`, and derived `Remaining`. The constructor rejects negative values and `Done > Estimated`, and current tests assert `Remaining = Estimated - Done`. Story 2.3 should add progress behavior without storing Remaining. [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs]
- `WorkItemAggregate` already normalizes initial effort to `Done = 0` during create, assigns monotonic `Sequence`, and emits lifecycle events through pure `Handle` methods. Progress should follow the same static `Handle(command, state) -> DomainResult` style. [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs]
- `WorkItemState` currently stores effort in `InitialEffort`, exposes `Remaining`, and sets status/sequence through `Apply` overloads. It has no `Apply(ProgressReported)` yet. [Source: src/Hexalith.Works.Contracts/State/WorkItemState.cs]
- `docs/lifecycle-transition-matrix.md` currently says explicit complete is implemented and Remaining=0 auto-completion is Story 2.3. Update the matrix and tests together. [Source: docs/lifecycle-transition-matrix.md#Lifecycle commands -> events]
- `P0_WorkItemSliceDoesNotIntroduceDeferredBurnDownRollUpOrReminderBehavior` still bans burn-down terms from `src`; this story is the point where burn-down becomes legitimate. Narrow the guard so `RollUp` and `Reminder` remain banned. [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs]

### Technical Requirements

- `ProgressReported` stores the raw reported `DoneDelta` and `Unit`; do not store interpreted Remaining in the event. Replay computes the resulting current effort from prior state. [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns]
- Every durable success event must carry `(AggregateId, Sequence)`. If progress reaches zero, emit `ProgressReported` at `state.Sequence + 1` and `WorkItemCompleted` at `state.Sequence + 2`. [Source: _bmad-output/planning-artifacts/epics.md#AR-4; _bmad-output/planning-artifacts/architecture.md#Pattern Examples]
- Unit is immutable after first estimate. `ReportProgress` must carry the same Unit or be rejected. [Source: _bmad-output/planning-artifacts/epics.md#AR-6; _bmad-output/planning-artifacts/architecture.md#Data Architecture]
- Own Remaining and Status are aggregate-authoritative and synchronous. Rolled Remaining is eventual projection state and must not be introduced or used to drive completion in this story. [Source: _bmad-output/planning-artifacts/epics.md#AR-9; _bmad-output/planning-artifacts/architecture.md#Data Architecture]
- Rejections are domain outcomes implementing `IRejectionEvent`, not exceptions. A rejected progress command must not mutate state or advance `Sequence`. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md#Inherited substrate constraints]
- Preserve the no-envelope rule: Works returns payloads only; EventStore owns envelope metadata. [Source: _bmad-output/planning-artifacts/epics.md#NFR-2]

### Project Structure Notes

- Add contracts under `src/Hexalith.Works.Contracts/Commands`, `src/Hexalith.Works.Contracts/Events`, and optionally `src/Hexalith.Works.Contracts/Events/Rejections`.
- Add behavior only in `src/Hexalith.Works.Server/Aggregates` and replay state in `src/Hexalith.Works.Contracts/State`.
- Keep tests in the existing projects: unit tests under `tests/Hexalith.Works.UnitTests`, contract/serialization tests under `tests/Hexalith.Works.IntegrationTests`, and guard updates under `tests/Hexalith.Works.ArchitectureTests`.
- Do not modify sibling submodule files. Do not initialize nested submodules. Hexalith library dependencies remain ProjectReferences, never PackageReferences. [Source: AGENTS.md#Hexalith library references; AGENTS.md#Submodule rules]

### Previous Story Intelligence

- Story 2.2 registered the v1 catalog with `Hexalith.PolymorphicSerializations` and established two serializer paths: base-typed `Polymorphic` for resolution tests, concrete `JsonSerializerDefaults.Web` for EventStore-persisted shape and golden corpus. Use the same split for `ReportProgress` and `ProgressReported`. [Source: _bmad-output/implementation-artifacts/2-2-record-raw-act-events-and-replay-state.md#Completion Notes List]
- Story 2.2 completed the durable-event golden corpus for all 10 existing success events and added strict file/count reconciliation expectations after review. Add the new progress event to the corpus and update counts in all affected places. [Source: _bmad-output/implementation-artifacts/tests/test-summary.md]
- Story 2.1 introduced `WorkItemLifecycle`, `LifecycleAct`, `docs/lifecycle-transition-matrix.md`, and the data-driven matrix tests. Later lifecycle stories must not choose transition behavior locally; progress must update the matrix as the source of truth. [Source: _bmad-output/implementation-artifacts/2-2-record-raw-act-events-and-replay-state.md#Git Intelligence; docs/lifecycle-transition-matrix.md]
- Prior reviews found documentation drift in file lists and test counts after QA additions. Reconcile the Dev Agent Record, File List, Change Log, and test summary before moving this story to review. [Source: _bmad-output/implementation-artifacts/2-2-record-raw-act-events-and-replay-state.md#Senior Developer Review]

### Git Intelligence

- `ccf73c5 feat(story-2.2)` added `[PolymorphicSerialization]` + `partial` to 23 existing contract types, added `WorkItemV1Catalog`, polymorphic resolution tests, raw-act additivity tests, golden corpus fixtures, and schema evolution tests. Extend these rather than creating parallel test infrastructure.
- `fb757f2 feat(story-2.1)` added the lifecycle command/event catalog, `WorkItemState.Sequence`, the pure transition table, the lifecycle matrix doc, and broad unit/integration tests. Preserve the same sequence and replay conventions.
- Current working tree has unrelated existing changes in `Hexalith.Parties` and `_bmad-output/story-automator/orchestration-1-20260615-182114.md`; do not revert or mix them into Story 2.3 implementation work.

### UX / Read Model Context

v1 has no production UI, but the UX artifacts make one data-shape requirement relevant: future burn-down views depend on own Remaining never being negative, status not being the sole progress signal, and own Remaining staying distinct from eventual rolled Remaining. This story implements only own effort; read-side roll-up remains later work. [Source: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Component Patterns; _bmad-output/planning-artifacts/epics.md#UX Derived Requirements]

### Latest Technical Specifics

No external web research is required for this story. The relevant technologies are already pinned or source-local: .NET 10, xUnit v3, Shouldly, `System.Text.Json`, EventStore payload conventions, and the checked-out `Hexalith.PolymorphicSerializations` source generator. Follow local source and existing tests rather than changing versions.

### Testing Standards

- Use xUnit v3 and Shouldly; do not add new test frameworks.
- Keep Tier-1 tests pure: no Dapr, Aspire, network, containers, timers, sleeps, or wall-clock reads.
- Run Release build with warnings-as-errors.
- `dotnet test` is blocked in this sandbox by Microsoft.Testing.Platform named-pipe permissions; run the built xUnit v3 executables directly as shown in Tasks.
- Preserve deterministic ordering in tests. For event sequences that auto-complete, assert the emitted event order and sequence values explicitly.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.3: Report Progress with Unit-Tagged Burn-Down] - story statement and AC #1-#5.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-3] - unit-tagged Effort Burn-Down and no implicit cross-Unit arithmetic.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-8] - progress decreases Remaining and Remaining=0 completes estimated work.
- [Source: _bmad-output/planning-artifacts/epics.md#AR-6] - progress delta validation and Unit immutability.
- [Source: _bmad-output/planning-artifacts/epics.md#AR-7] - `Meter(Unit, Estimated, Done)` with derived Remaining.
- [Source: _bmad-output/planning-artifacts/epics.md#AR-9] - own Remaining/Status synchronous, rolled Remaining eventual and distinct.
- [Source: _bmad-output/planning-artifacts/architecture.md#Data Architecture] - burn-down, Unit, roll-up, and consistency split decisions.
- [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns] - raw-act event payloads, serialization, and burn-down number rules.
- [Source: _bmad-output/planning-artifacts/architecture.md#Enforcement Guidelines] - register every new event/command and extend the golden corpus.
- [Source: docs/lifecycle-transition-matrix.md] - current lifecycle source of truth and Story 2.3 auto-completion placeholder.
- [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs] - current effort value object to evolve.
- [Source: src/Hexalith.Works.Contracts/State/WorkItemState.cs] - current replay state and `Remaining` property.
- [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs] - current pure handler pattern and sequence assignment.
- [Source: tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs] - serialization catalog to extend.
- [Source: tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs] - golden-corpus test pattern to extend.
- [Source: _bmad-output/implementation-artifacts/2-2-record-raw-act-events-and-replay-state.md] - previous-story serialization and review learnings.

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-16: Captured baseline commit `ccf73c51c49dffbf781d4ac5b1920f6cbc7198d9` and moved sprint/story tracking to `in-progress`.
- 2026-06-16: Ran `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- 2026-06-16: Ran `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — passed, 0 warnings, 0 errors.
- 2026-06-16: Ran built xUnit v3 executables directly — UnitTests 183/183, IntegrationTests 37/37, ArchitectureTests 26/26, PropertyTests 1/1.
- 2026-06-16 (review): Re-ran restore/build (0 warnings, 0 errors) and all four executables directly — UnitTests 188/188, IntegrationTests 39/39, ArchitectureTests 26/26, PropertyTests 1/1 (254 green). Reconciled the final counts below with `tests/test-summary.md` after the QA gap-fill run (+7 tests over the dev baseline of 247).

### Completion Notes List

- Added the `ReportProgress` command, `ProgressReported` durable event, and `WorkItemProgressRejected` domain rejection to the v1 polymorphic catalog without adding package references.
- Implemented own-effort progress math with derived Remaining, positive-delta validation, Unit matching, over-progress clamping, and synchronous auto-completion when Remaining reaches zero.
- Added deterministic replay for `ProgressReported` and preserved EventStore envelope ownership by keeping Works events as payloads only.
- Updated lifecycle documentation and architecture guards so progress/burn-down is in scope for Story 2.3 while roll-up, reminders, adapters, projections, timers, SignalR, and AppHost wiring remain out of scope.
- Extended unit, integration, schema-evolution, serialization-registration, and architecture coverage. Final reconciled count is 254 green tests: UnitTests 188, IntegrationTests 39, ArchitectureTests 26, PropertyTests 1 (the QA gap-fill run added +7 over the dev-authored baseline of 247; matches `tests/test-summary.md`).

### File List

- `_bmad-output/implementation-artifacts/2-3-report-progress-with-unit-tagged-burn-down.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `docs/lifecycle-transition-matrix.md`
- `src/Hexalith.Works.Contracts/Commands/ReportProgress.cs`
- `src/Hexalith.Works.Contracts/Events/ProgressReported.cs`
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemProgressRejected.cs`
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/LifecycleTransitionMatrixDocTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/ProgressReported.v1.json`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemProgressContractFlowTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemEffortTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs`

### Change Log

- 2026-06-16: Implemented Story 2.3 progress reporting, deterministic replay, schema registration/golden corpus updates, lifecycle documentation, and focused validation tests. Status moved to review.
- 2026-06-16: Adversarial code review (story-automator). No CRITICAL/HIGH findings; all 5 ACs verified IMPLEMENTED and all 254 tests confirmed green. Auto-fixed two MEDIUM documentation-integrity issues (File List omitted `WorkItemEffortTests.cs`; Dev Agent Record test counts not reconciled from 247 to 254) and one LOW (stale catalog doc comment). Status moved to done.

## Senior Developer Review (AI)

**Reviewer:** Administrator — 2026-06-16. **Outcome: Approve.**

Adversarial review validated every story claim against the implementation and against git reality. The
build is clean (Release, warnings-as-errors → 0/0) and all four xUnit v3 executables are green
(UnitTests 188, IntegrationTests 39, ArchitectureTests 26, PropertyTests 1 = 254).

**Acceptance Criteria — all IMPLEMENTED:**

- **AC #1** — `WorkItemEffort.Remaining => Estimated - Done` is derived (never stored); the constructor
  rejects `Done > Estimated` and `Report` clamps via `Math.Min(Estimated, Done + delta)`, so Remaining
  can never be represented below zero.
- **AC #2** — `WorkItemAggregate.Handle(ReportProgress)` emits `ProgressReported` for an estimated,
  `InProgress` item; `WorkItemState.Apply(ProgressReported)` replays the raw delta and burns down
  Remaining, clamped at zero. Multi-report accumulation proven (`...accumulates_done_and_burns_down...`).
- **AC #3** — When the replayed delta lands Remaining on zero, the same `DomainResult.Success` also emits
  `WorkItemCompleted` at `state.Sequence + 2`; replay reaches `Status = Completed`. Exact-zero boundary
  and over-shoot clamp both covered.
- **AC #4** — Unestimated items reject `ReportProgress` ("Progress requires estimated effort"), so they
  never complete through the Remaining=0 path; explicit `CompleteWorkItem` still completes unestimated
  `InProgress`/`Suspended` items (regression test present).
- **AC #5** — Non-positive delta and Unit mismatch each return a `WorkItemProgressRejected` domain
  rejection with no `Sequence` and no state change; status outside `InProgress` returns
  `WorkItemTransitionRejected`.

**Task audit:** All 8 tasks marked `[x]` are genuinely done. Kernel purity (`P0_WorkItemKernelRemainsPure`),
dependency direction, no-roll-up/reminder-in-`src`, golden corpus, and catalog count (constant-driven,
now 26) all pass. EventStore envelope ownership preserved (payloads only; no `$type`/envelope fields in
concrete JSON).

**Findings (auto-fixed):**

- *MEDIUM — File List incomplete.* `tests/Hexalith.Works.UnitTests/WorkItemEffortTests.cs` was modified
  in git but missing from the Dev Agent Record → File List. Added.
- *MEDIUM — Test-count drift.* Debug Log / Completion Notes reported 247 (UnitTests 183, IntegrationTests
  37) while the reconciled green total is 254 (188/39/26/1). Task 8 required reconciling these in the Dev
  Agent Record. Updated to match `tests/test-summary.md`.
- *LOW — Stale doc comment.* `WorkItemV1Catalog` was described as "the Story 2.2 v1 catalog" though it now
  carries the three Story 2.3 types (count 26). Clarified the comment.

**Observations (no change, by design):** `WorkItemState.InitialEffort` now holds the *current* effort and
is a slight misnomer, but Task 2 explicitly permitted retaining the name for compatibility while updating
its value on progress. The lifecycle doc lists `ReportProgress` in the command/event overview while its
dedicated "Progress act" section correctly states it is not a lifecycle reclassification act — internally
consistent.
