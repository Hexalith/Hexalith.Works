---
baseline_commit: cbf1cba3c267e52f353ab0852ad86adc346b7c4a
---

# Story 2.4: Re-Estimate and Reschedule Work

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an executor,
I want to re-estimate and reschedule a Work Item as first-class acts,
so that overruns, partial progress, priority changes, and due-date changes are recorded without
treating them as errors.

## Acceptance Criteria

1. **Given** a Work Item has an established Effort Unit
   **When** an executor re-estimates effort in the same Unit with a non-negative value
   **Then** `ReEstimated` is emitted
   **And** replayed state updates Estimated and derived Remaining consistently with existing Done.

2. **Given** a re-estimate uses a different Unit after the first estimate
   **When** `ReEstimate` is handled
   **Then** the command is rejected as a domain rejection
   **And** the Work Item's Unit remains unchanged.

3. **Given** an executor changes Priority or Due Date
   **When** `RescheduleWorkItem` is handled
   **Then** `WorkItemRescheduled` is emitted
   **And** replayed state reflects the new Schedule facts.

4. **Given** no Priority or Due Date is supplied
   **When** the Work Item is replayed
   **Then** the Schedule remains valid
   **And** the future "what's next" projection has enough data to sort the item last.

5. **Given** Priority is represented in v1
   **When** the contract is inspected
   **Then** Priority uses the ordered enum shape selected by architecture
   **And** no routing score, escalation band, LLM confidence, or cost policy is introduced.

## Tasks / Subtasks

- [x] **Task 1 — Add the re-estimate contract surface (AC: #1, #2)**
  - [x] Add `src/Hexalith.Works.Contracts/Commands/ReEstimate.cs` as a sealed partial record with
    `[PolymorphicSerialization]`. Fields: `TenantId TenantId`, `WorkItemId WorkItemId`,
    `decimal Estimated`, `Unit Unit`, optional `string? Note = null`. `Estimated` is the **new
    absolute** estimate (not a delta) — mirror `CreateWorkItem`'s estimate semantics, not
    `ReportProgress`'s delta semantics. Command name is imperative with **no `Command` suffix**
    (`ReEstimate`, per architecture naming).
  - [x] Add `src/Hexalith.Works.Contracts/Events/ReEstimated.cs` as a sealed partial record
    implementing `IEventPayload`. First two fields MUST be `string AggregateId`, `long Sequence`
    (AR-4), then `TenantId`, `WorkItemId`, `decimal Estimated`, `Unit Unit`, optional
    `string? Note = null`. Store the **raw reported** Estimated + Unit (the Raw Act), never a derived
    Remaining.
  - [x] Add `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemReEstimateRejected.cs` as a sealed
    partial record `(TenantId, WorkItemId, string Reason) : IRejectionEvent` (mirror
    `WorkItemProgressRejected`: no `Sequence`, never stream-appended). Use it for re-estimate-specific
    invariant failures (Unit mismatch, negative Estimated). Status-based failures (terminal/Unknown)
    reuse `WorkItemTransitionRejected`.
  - [x] Do **not** add any `Hexalith.*` `PackageReference`/`PackageVersion`; reuse the existing
    PolymorphicSerializations ProjectReference. [Source: AGENTS.md#Hexalith library references]

- [x] **Task 2 — Add the reschedule contract surface (AC: #3, #4, #5)**
  - [x] Add `src/Hexalith.Works.Contracts/Commands/RescheduleWorkItem.cs` as a sealed partial record
    with `[PolymorphicSerialization]`. Fields: `TenantId TenantId`, `WorkItemId WorkItemId`,
    `WorkItemSchedule Schedule`, optional `string? Note = null`. Carry the **new end-state**
    `WorkItemSchedule` (the existing `(Priority? Priority, DateOnly? DueDate)` value object) — see Key
    Design Decision D3 for the whole-schedule-replacement rationale.
  - [x] Add `src/Hexalith.Works.Contracts/Events/WorkItemRescheduled.cs` as a sealed partial record
    implementing `IEventPayload`: `(string AggregateId, long Sequence, TenantId TenantId,
    WorkItemId WorkItemId, WorkItemSchedule Schedule, string? Note = null)`.
  - [x] Do **not** introduce any routing score, escalation band, numeric priority weight, LLM
    confidence, or cost/spend field on the command, event, or `WorkItemSchedule`. Reschedule carries
    only the existing `Priority` ordered enum + `DateOnly?` due date (AC #5; SM-C2). The `Priority`
    enum and `WorkItemSchedule` value object already exist (Story 1.2) — reuse them, do not redefine.

- [x] **Task 3 — Evolve effort state with a re-estimate operation (AC: #1, #2)**
  - [x] Add a pure `ReEstimate(decimal newEstimated)` method to
    `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs` returning a new `WorkItemEffort` that
    **preserves `Unit`** (immutable, A3), sets `Estimated = newEstimated`, and clamps
    `Done = Math.Min(Done, newEstimated)` so the derived `Remaining = Estimated − Done` is never
    negative (E2; mirror the existing `Report` clamp). Reject `newEstimated < 0` (mirror the existing
    `ArgumentOutOfRangeException` ctor guards).
  - [x] Keep `Remaining` derived, never stored. Do not add a mutable Remaining field. Do not change the
    `Unit` on this path.

- [x] **Task 4 — Handle re-estimate in the aggregate (AC: #1, #2)**
  - [x] Add `WorkItemAggregate.Handle(ReEstimate command, WorkItemState? state)` following the existing
    static `Handle(command, state) → DomainResult` style and guard cascade (null checks →
    status guard → invariant guards → emit).
  - [x] **Status guard:** reject re-estimate from terminal statuses (`Completed`, `Cancelled`,
    `Rejected`, `Expired`) and from `Unknown`/null ("not created") with `WorkItemTransitionRejected`
    (see Key Design Decision D4 for the accepted non-terminal set). Re-estimate does **not** change
    `Status`.
  - [x] **Invariant guards (return `WorkItemReEstimateRejected`, never throw):** reject
    `command.Estimated < 0`; reject when the item already has an established effort whose `Unit` differs
    from `command.Unit`. On the established-unit path the existing `Unit` is unchanged (AC #2).
  - [x] **First-estimate path (Key Design Decision D2):** when the item is unestimated
    (`state.InitialEffort is null`), an accepted `ReEstimate` establishes the first estimate — its
    `Unit` becomes the established Unit for all later acts. (If the dev elects the conservative
    alternative of rejecting re-estimate on unestimated items, document that choice in the matrix and
    cover it with a rejection test instead.)
  - [x] On accept, emit `ReEstimated` at `NextSequence(state)` carrying the raw `Estimated` + `Unit`.
    Do **not** emit `WorkItemCompleted` even if the re-estimate lands Remaining at zero — re-estimate is
    not a completion path (Key Design Decision D5). Keep `Handle` pure (no clock, RNG, I/O, Dapr,
    EventStore envelope, logging).

- [x] **Task 5 — Handle reschedule in the aggregate (AC: #3, #4)**
  - [x] Add `WorkItemAggregate.Handle(RescheduleWorkItem command, WorkItemState? state)`.
  - [x] **Status guard:** reject reschedule from terminal statuses and `Unknown`/null with
    `WorkItemTransitionRejected`; accept from the non-terminal set (D4). Reschedule does **not** change
    `Status`.
  - [x] No schedule-content validation rejection: any `Priority?` (including null) and any `DateOnly?`
    (including null) is valid — a both-null schedule is the legitimate "sorts last" case (AC #4). Do not
    require at least one field.
  - [x] On accept, emit `WorkItemRescheduled` at `NextSequence(state)` carrying the command's
    `WorkItemSchedule` verbatim.

- [x] **Task 6 — Replay re-estimate and reschedule deterministically (AC: #1, #3, #4)**
  - [x] Add `WorkItemState.Apply(ReEstimated e)`: if `InitialEffort is null`, construct
    `new WorkItemEffort(e.Estimated, e.Unit)` (first estimate, Done = 0); otherwise
    `InitialEffort = InitialEffort.ReEstimate(e.Estimated)` (preserve Done, clamp). Advance `Sequence`.
    Do not change `Status`.
  - [x] Add `WorkItemState.Apply(WorkItemRescheduled e)`: `Schedule = e.Schedule;` (whole-schedule
    replacement, D3); advance `Sequence`. Do not change `Status`.
  - [x] Add the rejection no-op overload `Apply(WorkItemReEstimateRejected e)` (guard
    `ArgumentNullException.ThrowIfNull`, `#pragma warning disable CA1822`), placed with the other
    rejection Apply overloads (replay convention — rejection events are trusted persisted no-ops).
  - [x] Continue trusting persisted success events on replay; validate at the writer (`Handle`), not in
    `Apply`. Preserve EventStore envelope ownership — Works events stay payloads only (NFR-2).

- [x] **Task 7 — Update the lifecycle transition matrix (AC: #1, #2, #3)**
  - [x] In `docs/lifecycle-transition-matrix.md`, add `ReEstimate` and `RescheduleWorkItem` to the
    "Lifecycle commands → events" table and add dedicated **"Re-estimate act"** and **"Reschedule act"**
    sections (mirror the existing "Progress act" section): both are non-lifecycle acts that emit a
    raw-act event **without changing `Status`**, accepted from every non-terminal status and `R` from
    every terminal status / `Unknown`. State explicitly that re-estimate emits no `WorkItemCompleted`
    (D5) and that the established `Unit` is immutable (A3).
  - [x] Add `"ReEstimate"` and `"RescheduleWorkItem"` to the `RequiredCommands` array in
    `tests/Hexalith.Works.ArchitectureTests/FitnessTests/LifecycleTransitionMatrixDocTests.cs` so the
    doc/code-sync gate enforces their presence (mirror how Story 2.3 added `ReportProgress`).
  - [x] Do not introduce projections, roll-up, "what's next" ordering, SignalR, AppHost/Dapr/timer, or
    adapters in this story — those remain in Stories 3.3/4.4/4.5/4.6.

- [x] **Task 8 — Extend serialization registration and golden corpus (AC: #1, #2, #3)**
  - [x] In `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`, add the new types to the `All`
    list (1 instance each of `ReEstimated`, `WorkItemRescheduled` success events; `ReEstimate`,
    `RescheduleWorkItem` commands; `WorkItemReEstimateRejected` rejection) and update `Count`
    `26 → 31`, plus the doc-comment tallies (now **13** success events + **13** commands + **5**
    rejection events; 13 of the frozen-14 catalog events exist — `ChildSpawned` remains Story 3.2).
  - [x] Confirm `WorkItemSerializationRegistrationTests` resolves the 5 new types through the empty
    `Polymorphic` base (emits `$type` == type name, **no** version suffix) and that the catalog-count
    vacuous-pass guard tracks the new `Count`.
  - [x] Freeze concrete `ReEstimated.v1.json` and `WorkItemRescheduled.v1.json` under
    `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/`, **generated from the production
    `JsonSerializerDefaults.Web` serializer** (temporary emitter run once then deleted — do not
    hand-author), so camelCase / enum-name casing / property order are byte-accurate and carry **no**
    `$type` or envelope fields. (`WorkItemRescheduled` exercises the nested `Schedule` object with the
    `Priority` enum serialized by name + a `DateOnly` due date.)
  - [x] Extend `SchemaEvolutionGoldenCorpusTests` with deserialize-from-frozen + round-trip +
    additive-unknown-field tolerance tests for both new events (mirror the `ProgressReported` trio).

- [x] **Task 9 — Add focused tests for the re-estimate / reschedule slice (AC: #1–#5)**
  - [x] Unit tests (new `WorkItemReEstimateTests` / `WorkItemRescheduleTests`, or extend the effort/
    schedule unit tests):
    - Re-estimate **up** in the same Unit → `ReEstimated`; replay raises Estimated and re-derives
      Remaining against existing Done (AC #1).
    - Re-estimate with a **different Unit** → `WorkItemReEstimateRejected`; Unit + Estimated unchanged,
      `Sequence` unchanged (AC #2).
    - Re-estimate with a **negative** value → `WorkItemReEstimateRejected`; state unchanged.
    - Re-estimate **below current Done** → Done clamps to the new Estimated, Remaining = 0, and **no**
      `WorkItemCompleted` is emitted (D5).
    - Re-estimate on an **unestimated** item → establishes the first estimate/Unit (D2 path) — or, if
      the conservative alternative is chosen, asserts the rejection.
    - Re-estimate from each **terminal** status and `Unknown` → `WorkItemTransitionRejected`.
    - Reschedule setting Priority + Due Date → `WorkItemRescheduled`; replay reflects the new Schedule
      (AC #3).
    - Reschedule with **both** Priority and Due Date null → accepted, replayed `Schedule` is the
      both-null "sorts last" schedule (AC #4).
    - Reschedule from each **terminal** status and `Unknown` → `WorkItemTransitionRejected`.
    - `WorkItemEffort.ReEstimate` value-object contract (preserves Unit, clamps Done, rejects negative).
    - Priority ordered-enum guard for AC #5 (a `Priority.Critical < High < Normal < Low` assertion
      already exists in `WorkItemContractValueObjectTests`; add an assertion that `RescheduleWorkItem` /
      `WorkItemRescheduled` expose only `Priority` + `DateOnly?` and no scoring/band/cost field).
  - [x] Integration / contract-flow tests: command → `WorkItemAggregate.Handle` → event → concrete
    `JsonSerializerDefaults.Web` JSON → replay, for both `ReEstimated` (Estimated + Remaining converge,
    Unit preserved) and `WorkItemRescheduled` (Schedule converges through JSON), asserting EventStore
    envelope fields stay absent from the concrete payload.
  - [x] Architecture tests stay green: kernel purity (`P0_WorkItemKernelRemainsPure`),
    dependency-direction, `P0_WorkItemSliceDoesNotIntroduceDeferredBurnDownRollUpOrReminderBehavior`
    (no `RollUp`/`Reminder` in `src`), and the matrix doc-sync test (now including the two new
    commands).
  - [x] If `WorkItemStateBuilder.InStatus` is insufficient (it arranges statuses with **no** effort or
    schedule), add an estimated/scheduled arrange helper (local, or extend the builder with optional
    `effort`/`schedule` parameters) rather than duplicating replay plumbing across tests.

- [x] **Task 10 — Build and verify the slice (AC: #1–#5)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
    (warnings-as-errors → expect 0/0).
  - [x] Run the built xUnit v3 executables **directly** (`dotnet test` is blocked in this sandbox by the
    Microsoft.Testing.Platform named-pipe permission):
    - `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`
    - `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`
    - `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`
    - `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`
  - [x] Baseline after Story 2.3 is **254** green (UnitTests 188, IntegrationTests 39,
    ArchitectureTests 26, PropertyTests 1). Reconcile the final counts in
    `_bmad-output/implementation-artifacts/tests/test-summary.md` and this story's Dev Agent Record.

## Dev Notes

### Scope Boundary

Story 2.4 owns **FR-9** for a single Work Item: re-estimate effort (`ReEstimate` → `ReEstimated`) and
reschedule Priority/Due-Date (`RescheduleWorkItem` → `WorkItemRescheduled`) as **normal expected
raw-act events**, not errors. It mints the last two of Epic 2's catalog events; after this story 13 of
the frozen-14 v1 events exist (`ChildSpawned` is the only remaining one, owned by Story 3.2).
[Source: _bmad-output/planning-artifacts/epics.md#Story 2.4; #FR-9; #AR-5; #AR-6]

**In scope:** `ReEstimate`/`ReEstimated`, `RescheduleWorkItem`/`WorkItemRescheduled`, the
`WorkItemReEstimateRejected` domain rejection; `WorkItemEffort.ReEstimate` math; `WorkItemState` replay
for the two events; the per-act status guards + matrix documentation; PolymorphicSerializations catalog
+ golden corpus; unit/integration/architecture tests.

**Out of scope (do not build here):** the **"what's next" projection/ordering** that consumes Priority
+ Due Date (Story 4.4 — this story only records the Schedule *facts* it will sort on); recursive
Roll-Up / re-deriving rolled-Remaining after a re-estimate (Epic 3, Stories 3.3/3.4); SignalR/notifier
seams; AppHost/Dapr/EventStore-append wiring; reminders/timers/reactor; cost meter; routing/scoring.
[Source: _bmad-output/planning-artifacts/epics.md#Scope reminder; #FR-20; #UX-DR2/3]

### Current State (files this story modifies — read before editing)

- **`WorkItemEffort.cs`** already models `Estimated`, immutable `Unit`, `Done`, derived
  `Remaining = Estimated − Done`; the ctor rejects negatives and `Done > Estimated`; `Report(delta)`
  clamps `Done = Min(Estimated, Done + delta)`. Add a sibling `ReEstimate(newEstimated)` that preserves
  `Unit` and clamps `Done`. **Preserve** the existing ctor/`Report` behavior — Story 2.3 tests depend
  on it. [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs]
- **`WorkItemSchedule.cs`** is `sealed record WorkItemSchedule(Priority? Priority = null,
  DateOnly? DueDate = null)` — already created at `WorkItemCreated` time and replayed into
  `WorkItemState.Schedule`. Reschedule **replaces** this value. No change to the value object needed.
  [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemSchedule.cs]
- **`Priority.cs`** is the ordered enum `{ Unknown=0, Critical=1, High=2, Normal=3, Low=4 }` with a
  `JsonStringEnumConverter` (serialized by name). It satisfies AC #5 as-is — do not add bands/scores.
  [Source: src/Hexalith.Works.Contracts/ValueObjects/Priority.cs]
- **`WorkItemAggregate.cs`** holds the pure static handlers. `Handle(ReportProgress, state)` is the
  closest analog: it does its own status guard (`from != InProgress → Reject`) and invariant guards
  returning a focused rejection (`RejectProgress(...)`), assigns `NextSequence(state)`, and never reads
  a clock. Re-estimate/reschedule follow the same shape but use a **terminal/Unknown** status guard
  instead of an exact-status guard. Reuse the private helpers `CurrentStatus`, `NextSequence`, `Reject`.
  [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs#Handle(ReportProgress)]
- **`WorkItemState.cs`** stores current effort in `InitialEffort` (a slight misnomer kept for
  compatibility — it is the *current* effort, updated on progress; Story 2.3 review noted this), exposes
  derived `Remaining`, and replays via `Apply(...)` overloads that set the target field + monotonic
  `Sequence`. Rejection events have no-op `Apply` overloads. Add `Apply(ReEstimated)`,
  `Apply(WorkItemRescheduled)`, and the no-op `Apply(WorkItemReEstimateRejected)`.
  [Source: src/Hexalith.Works.Contracts/State/WorkItemState.cs]
- **`docs/lifecycle-transition-matrix.md`** is the single source of truth, mirrored 1:1 by
  `WorkItemLifecycle.cs` and gated by `LifecycleTransitionMatrixDocTests`. It already documents
  `ReportProgress` as a non-lifecycle "Progress act." Add parallel "Re-estimate act" / "Reschedule act"
  sections. [Source: docs/lifecycle-transition-matrix.md#Progress act]

### Key Design Decisions (resolve these explicitly; record final choices in the matrix + tests)

- **D1 — Absolute estimate, not a delta.** `ReEstimate.Estimated` is the **new total** estimate (like
  `CreateWorkItem`), distinct from `ReportProgress.DoneDelta` (an increment). This is the natural read
  of "re-estimate effort … with a non-negative value" and keeps replay deterministic. [Source:
  _bmad-output/planning-artifacts/epics.md#Story 2.4 AC #1; #AR-6]
- **D2 — First estimate via `ReEstimate` (recommended: allow).** FR-1 permits unestimated creation, and
  FR-9 frames re-estimate as the normal estimate-adjustment event, so an unestimated item's first
  `ReEstimate` should **establish** its Unit + Estimated (Unit immutable thereafter). The conservative
  alternative (reject re-estimate until an estimate exists) is acceptable **if** documented in the
  matrix and covered by a rejection test — but the recommended path unblocks estimating items that were
  created without one. Either way, the AC #2 "different Unit after the first estimate" rejection still
  applies once a Unit is established. [Source: _bmad-output/planning-artifacts/epics.md#FR-1; #FR-9; #AR-6]
- **D3 — Reschedule replaces the whole `WorkItemSchedule` (recommended).** The command/event carry the
  desired **end-state** `WorkItemSchedule`, and `Apply` does `Schedule = e.Schedule`. This keeps the
  event a self-contained Raw Act and replay order-independent. A both-null schedule is a valid "clear to
  sorts-last" act (AC #4). The alternative (per-field patch where null = "unchanged") is **not**
  recommended — it makes the Raw Act ambiguous and replay history-dependent. If the dev needs
  patch-style ergonomics, build the full target `WorkItemSchedule` at the command edge, not in `Handle`.
  [Source: _bmad-output/planning-artifacts/epics.md#Story 2.4 AC #3/#4; #FR-4; architecture.md#A2]
- **D4 — Accepted-status set for both acts: every non-terminal status.** Accept `ReEstimate` and
  `RescheduleWorkItem` from `Created`, `Assigned`, `Queued`, `InProgress`, `Suspended`; reject from the
  four terminal statuses (`Completed`, `Cancelled`, `Rejected`, `Expired`) and from `Unknown`/null with
  `WorkItemTransitionRejected`. Rationale: re-estimate/reschedule adjust the *plan*, valid while work is
  live (including `Suspended`, which rejects *progress* but not planning), and a terminal item's plan is
  frozen. Unlike `ReportProgress` (which only `InProgress` accepts), do not over-narrow these. Record
  the full per-status decision in the matrix. [Source: _bmad-output/planning-artifacts/epics.md#FR-6;
  docs/lifecycle-transition-matrix.md]
- **D5 — Re-estimate is NOT a completion path.** Even when a re-estimate clamps Remaining to 0 (new
  estimate ≤ Done), emit only `ReEstimated` — never `WorkItemCompleted`. The synchronous
  "Remaining 0 → Completed" rule (AR-9) is owned exclusively by `ReportProgress` (Story 2.3) and
  explicit `CompleteWorkItem`. Mixing completion into re-estimate would let a planning act silently
  terminate work. Document this in the "Re-estimate act" matrix section.
  [Source: _bmad-output/planning-artifacts/epics.md#AR-9; #FR-8; 2-3 story#AC #3]

### Technical Requirements

- Every durable success event carries `(AggregateId, Sequence)` first (AR-4); rejection events carry no
  `Sequence` and are never stream-appended. [Source: epics.md#AR-4; #NFR-2]
- Unit is immutable after the first estimate; `ReEstimated` must carry the established Unit or be
  rejected (AC #2). Estimated ≥ 0 (E2). Remaining derived and clamped ≥ 0 (E2/A4).
  [Source: epics.md#AR-6; architecture.md#E2; #A4]
- `DomainResult` never mixes success and rejection payloads; rejections implement `IRejectionEvent`,
  not exceptions; a rejected command mutates no state and does not advance `Sequence`. The
  `WorkItemEffort.ReEstimate` value-object guard may throw `ArgumentOutOfRangeException`, but the
  **aggregate must pre-validate** (Estimated ≥ 0, Unit match) and return a domain rejection rather than
  letting the throw escape `Handle`. [Source: architecture.md#Format Patterns; epics.md#NFR-2]
- Works returns **payloads only** — never populate or spoof EventStore envelope metadata
  (`messageId`/`correlationId`/`causationId`/`userId`/`metadata`/`cloudEvent`). [Source: epics.md#NFR-2;
  WorkItemV1Catalog.EnvelopeFields]
- `Handle`/`Apply` stay pure: no clock, RNG, I/O, Dapr, EventStore envelope APIs (enforced by
  `P0_WorkItemKernelRemainsPure`). [Source: epics.md#NFR-5; ScaffoldGovernanceTests]
- Additive, no-`V2` serialization: new fields/types only; every event ever produced stays
  deserializable; PolymorphicSerializations + `System.Text.Json`; extend the golden corpus.
  [Source: epics.md#NFR-12; SchemaEvolution/Golden/README.md]

### Project Structure Notes

- Contracts under `src/Hexalith.Works.Contracts/{Commands,Events,Events/Rejections,ValueObjects}`;
  aggregate behavior in `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`; replay in
  `src/Hexalith.Works.Contracts/State/WorkItemState.cs`; one public type per file, file-scoped
  namespaces, sealed records (ecosystem rules inherited verbatim — do not restate or bulk-add XML docs).
  [Source: architecture.md#Naming Patterns; Hexalith.Parties project-context.md#Language-Specific Rules]
- Tests stay in the existing projects (`UnitTests`, `IntegrationTests`, `ArchitectureTests`); reusable
  arrange helpers in `Hexalith.Works.Testing`. xUnit **v3** + Shouldly only — no Moq/FluentAssertions/
  raw `Assert.*`. [Source: architecture.md#Tests; Hexalith.Parties project-context.md#Testing Rules]
- Hexalith dependencies remain `ProjectReference`, never `PackageReference`; never init nested
  submodules. [Source: AGENTS.md#Hexalith library references; #Submodule rules]

### Previous Story Intelligence

- **Story 2.3 (Report Progress)** established the exact template this story extends: a focused
  command/event/rejection trio, a value-object operation with a clamp, an aggregate `Handle` with a
  status guard + invariant guards returning a focused rejection, a `WorkItemState.Apply` overload, a
  matrix "act" section, catalog-count bump, and a golden fixture + schema-evolution trio. Mirror it.
  [Source: 2-3-report-progress-with-unit-tagged-burn-down.md#Tasks; #Completion Notes List]
- **2.3 review/QA learnings to pre-empt:** (1) **reconcile counts everywhere** — Dev Agent Record, File
  List, Change Log, and `tests/test-summary.md` drifted in 2.2/2.3 and were flagged MEDIUM; keep them
  in lockstep. (2) The **golden corpus must cover the new durable events** — 2.2 shipped only 3/10 and
  QA had to backfill; freeze both new events up front. (3) Golden fixtures are **generated from the
  production serializer**, never hand-authored, so casing/order are byte-exact. (4) The **File List must
  include every modified test file** (2.3 omitted `WorkItemEffortTests.cs`). [Source:
  2-3 story#Senior Developer Review; tests/test-summary.md#Notes]
- **Story 2.1** owns the matrix as the single source of truth; later lifecycle stories reference it and
  must not choose transition behavior locally — re-estimate/reschedule decisions live in the matrix.
  [Source: docs/lifecycle-transition-matrix.md#header]

### Git Intelligence

- `cbf1cba feat(story-2.3)` (baseline) added the `ReportProgress`/`ProgressReported`/
  `WorkItemProgressRejected` slice, `WorkItemEffort.Report`, `WorkItemState.Apply(ProgressReported)`,
  the matrix "Progress act" section + `ReportProgress` in `RequiredCommands`, catalog `Count` 23→26, and
  the `ProgressReported.v1.json` corpus entry. This story is the structurally-identical sibling — extend
  the same files, do not fork parallel infrastructure.
- `ccf73c5 feat(story-2.2)` added `[PolymorphicSerialization]` + `partial` across the contract types,
  `WorkItemV1Catalog`, and the golden-corpus + schema-evolution test scaffolding to extend.
- Working tree carries **unrelated** pre-existing changes in `Hexalith.Parties` and
  `_bmad-output/story-automator/orchestration-1-20260615-182114.md` — do not revert, stage, or mix them
  into Story 2.4 work.

### UX / Read-Model Context

No production UI in v1, but the recorded Schedule shapes a future read model: AC #4 + UX-DR1/FR-20
require that an item with **neither** Priority nor Due Date carries enough data to **sort last** in the
future "what's next" projection (Story 4.4). This story only persists those Schedule facts — it builds
no ordering. Keep `Priority`/`DueDate` nullable and never coerce a missing value into a default band.
[Source: epics.md#FR-4; #FR-20; #UX-DR1; architecture.md#A2]

### Latest Technical Specifics

No external web research required. The stack is pinned and source-local: .NET 10 / `net10.0`, xUnit v3
(`3.2.2`), Shouldly, `System.Text.Json` (`JsonSerializerDefaults.Web`), and the checked-out
`Hexalith.PolymorphicSerializations` source generator. `DateOnly` serializes to an ISO `yyyy-MM-dd`
string and `Priority` to its enum name via the existing `JsonStringEnumConverter` — verify both in the
`WorkItemRescheduled.v1.json` round-trip. Do not change pinned versions. [Source: epics.md#AR-20;
2-3 story#Latest Technical Specifics]

### Testing Standards

- xUnit v3 + Shouldly; Tier-1 stays pure (no Dapr/Aspire/network/containers/timers/sleeps/clock reads).
- Release build, warnings-as-errors. `dotnet test` is blocked in this sandbox — run the built xUnit v3
  executables directly (see Task 10).
- Assert event order and `Sequence` values explicitly. Each test arranges its own state via replay (no
  inter-test order dependence). Generate golden fixtures from the production serializer, then delete the
  emitter. [Source: 2-3 story#Testing Standards; tests/test-summary.md]

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.4: Re-Estimate and Reschedule Work] — story
  statement + AC #1–#5.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-9] — re-estimate adjusts Estimated/Remaining as a
  normal event; Priority/Due-Date changes emit events and update "what's next" ordering.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-4] — Schedule = Priority + optional Due Date;
  changeable later (each change emits an event); neither → sorts last.
- [Source: _bmad-output/planning-artifacts/epics.md#AR-5] — Priority ordered enum `{Critical, High,
  Normal, Low}`, additive-tolerant (vs numeric routing bands — YAGNI/SM-C2).
- [Source: _bmad-output/planning-artifacts/epics.md#AR-6] — Unit immutable after first estimate;
  `ReEstimated` carries the same Unit or is rejected; Estimated ≥ 0; Remaining clamped ≥ 0.
- [Source: _bmad-output/planning-artifacts/epics.md#AR-9] — own-Remaining/Status synchronous; rolled
  Remaining is a distinct eventual projection (out of scope here).
- [Source: _bmad-output/planning-artifacts/architecture.md#Data Architecture] — A2 Priority enum, A3
  Unit immutability, A4 cost-ready `Meter` with derived Remaining.
- [Source: _bmad-output/planning-artifacts/architecture.md#Infrastructure & Deployment] — E2 validation
  domains (Estimated ≥ 0, Unit immutable, Remaining clamped).
- [Source: _bmad-output/planning-artifacts/architecture.md#Naming Patterns] — `ReEstimate` /
  `RescheduleWorkItem` commands, `ReEstimated` / `WorkItemRescheduled` events (no suffixes, sealed).
- [Source: docs/lifecycle-transition-matrix.md] — single source of truth; "Progress act" pattern to
  mirror for the new acts; terminal-row rule.
- [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs] — `Handle(ReportProgress)` guard
  cascade + `NextSequence`/`Reject`/`CurrentStatus` helpers to reuse.
- [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs] — effort math + clamp to mirror.
- [Source: src/Hexalith.Works.Contracts/State/WorkItemState.cs] — replay convention + rejection no-op
  Apply pattern.
- [Source: tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs] — catalog + `Count` to extend.
- [Source: tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs;
  .../Golden/README.md] — golden-corpus pattern + generation rules.
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/LifecycleTransitionMatrixDocTests.cs] —
  `RequiredCommands` array to extend.
- [Source: tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs] — unit-test structure to mirror.
- [Source: _bmad-output/implementation-artifacts/2-3-report-progress-with-unit-tagged-burn-down.md] —
  sibling-story template + review/QA learnings.

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- 2026-06-16: `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  passed.
- 2026-06-16: `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
  passed with 0 warnings and 0 errors.
- 2026-06-16: xUnit executables passed: UnitTests 213/213, IntegrationTests 45/45,
  ArchitectureTests 26/26, PropertyTests 1/1 (dev baseline — total 285).
- 2026-06-16 (review reconciliation): re-ran Release build (0 warnings / 0 errors) and all four xUnit v3
  executables. Reconciled **final** counts — UnitTests **217/217**, IntegrationTests **45/45**,
  ArchitectureTests **26/26**, PropertyTests **1/1** (total **289**). The +4 over the dev baseline are the
  QA gap-fill unit tests recorded in `tests/test-summary.md` (D2→AC#2 establish-then-reject, the zero
  re-estimate boundary, the D3 whole-schedule clear, and the due-date-only partial). Dev Agent Record and
  `test-summary.md` are now in lockstep at 289.

### Completion Notes List

- Added the `ReEstimate` / `ReEstimated` / `WorkItemReEstimateRejected` contract surface and the
  `RescheduleWorkItem` / `WorkItemRescheduled` contract surface without adding Hexalith package
  references.
- Implemented `WorkItemEffort.ReEstimate`, aggregate handlers, and deterministic replay. Re-estimate is
  accepted from live statuses, can establish the first estimate, preserves the established Unit after
  that, clamps Done when the estimate drops below current Done, and never completes the item.
- Implemented whole-schedule replacement for reschedule. Both-null schedules remain valid and no
  scoring/band/confidence/cost fields were introduced.
- Updated the lifecycle transition matrix, polymorphic v1 catalog, golden corpus, schema tests,
  contract-flow tests, and focused unit tests for AC #1-#5.

### File List

- `_bmad-output/implementation-artifacts/2-4-re-estimate-and-reschedule-work.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `docs/lifecycle-transition-matrix.md`
- `src/Hexalith.Works.Contracts/Commands/ReEstimate.cs`
- `src/Hexalith.Works.Contracts/Commands/RescheduleWorkItem.cs`
- `src/Hexalith.Works.Contracts/Events/ReEstimated.cs`
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemReEstimateRejected.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemRescheduled.cs`
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs`
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/LifecycleTransitionMatrixDocTests.cs`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/ReEstimated.v1.json`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/WorkItemRescheduled.v1.json`
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemReEstimateRescheduleContractFlowTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemContractValueObjectTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemEffortTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemRescheduleTests.cs`

### Change Log

- 2026-06-16: Adversarial code review (story-automator-review). 0 Critical / 0 High; 1 Medium + 1 Low
  auto-fixed (Dev Agent Record test-count drift reconciled to 289; sprint-status `last_updated` precision
  restored). All AC #1–#5 verified implemented, build clean (0/0), 289 tests green. Status set to done.
- 2026-06-16: Implemented Story 2.4 re-estimate/reschedule contracts, aggregate handling, replay,
  matrix documentation, serialization catalog/golden corpus, and tests. Status set to review.
- 2026-06-16: Story 2.4 drafted via create-story (ultimate context engine analysis). Status set to
  ready-for-dev.

### Senior Developer Review (AI)

**Reviewer:** Administrator · **Date:** 2026-06-16 · **Outcome:** Approve (auto-fixed)

Adversarial validation of every story claim against the actual implementation and git reality:

- **AC #1 (re-estimate, same Unit)** — `ReEstimate`→`ReEstimated` emits the raw absolute `Estimated`+`Unit`;
  `WorkItemState.Apply(ReEstimated)` re-derives Remaining against existing Done. Verified in
  `WorkItemReEstimateTests` + contract-flow + golden corpus.
- **AC #2 (different Unit after first estimate)** — rejected via `WorkItemReEstimateRejected`; Unit/Estimated
  preserved, `Sequence` not advanced. Covered via both the created-with-effort path and the D2
  establish-then-reject path.
- **AC #3/#4 (reschedule / both-null "sorts last")** — `RescheduleWorkItem`→`WorkItemRescheduled` replaces the
  whole `WorkItemSchedule` (D3); both-null accepted; partials (priority-only, due-date-only) and the
  whole-replacement clear are tested.
- **AC #5 (ordered Priority enum only, no scoring/band/cost)** — reflection guard asserts `WorkItemSchedule`
  exposes exactly `{Priority?, DateOnly?}` and that no Score/Band/Confidence/Cost/Spend/Weight member exists.
- **D5** — re-estimate never emits `WorkItemCompleted` even when Remaining clamps to 0 (verified at the
  below-Done and exact-zero boundaries).
- **Kernel purity / NFR-2** — handlers/replay read no clock/RNG/IO; events stay payloads only (no envelope
  fields, no `$type` in the EventStore transport form).

**Evidence:** Release build 0 warnings / 0 errors; UnitTests 217, IntegrationTests 45, ArchitectureTests 26,
PropertyTests 1 — **289 green**. File List matches `git status` exactly.

**Findings (auto-fixed):**

- *Medium* — Dev Agent Record Debug Log recorded the 285 dev baseline as final; reconciled to the post-QA
  **289** (217 unit) to match `tests/test-summary.md`, closing the count-drift the story explicitly warned
  about.
- *Low* — `sprint-status.yaml` `last_updated` had been written as a bare date; restored to a full ISO-8601
  offset timestamp for format consistency with `generated:`.

**Observation (no change — by convention):** the accepted-status set for the two planning acts is encoded in
the aggregate's local `IsLive` predicate rather than derived from the lifecycle matrix, and
`LifecycleTransitionMatrixDocTests` only asserts command-name presence (not per-status outcomes) for these
acts. This intentionally mirrors the existing `ReportProgress` treatment; flagged for a future doc-sync
hardening rather than changed here.
