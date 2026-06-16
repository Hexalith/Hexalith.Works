---
baseline_commit: 6ea70b7ae8c4abcd67b73607b118eb2f08043125
---

# Story 2.1: Define the Lifecycle State Machine

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an executor,
I want Work Items to enforce a clear lifecycle,
so that every accepted transition is predictable and every invalid transition is rejected as a domain fact.

## Acceptance Criteria

1. **Given** a Work Item in Status `Created`
   **When** the executor assigns it or queues it
   **Then** the transition to `Assigned` or `Queued` is accepted
   **And** any unsupported transition from `Created` is rejected as an `IRejectionEvent`.

2. **Given** a Work Item in `Assigned` or `Queued`
   **When** the executor starts or claims work according to the lifecycle rules
   **Then** the item can transition to `InProgress`
   **And** `Assigned ↔ Queued` transitions are accepted where requeue or direct assignment is valid.

3. **Given** a Work Item in `InProgress`
   **When** it is suspended
   **Then** the item transitions to `Suspended`
   **And** resumption is represented only as a transition back to `InProgress`, not as a resting `Resumed` status.

4. **Given** a Work Item in any terminal status
   **When** a further lifecycle command is handled
   **Then** no transition out of `Completed`, `Cancelled`, non-requeuable `Rejected`, or `Expired` is accepted
   **And** non-idempotent lifecycle commands emit an `IRejectionEvent`
   **And** only exact duplicate terminal commands explicitly listed in `docs/lifecycle-transition-matrix.md` return `DomainResult.NoOp`.

5. **Given** a bound executor rejects an assignment with the default requeue behavior
   **When** the rejection is handled
   **Then** `WorkItemRejected` may be emitted as raw-act evidence
   **And** the resulting resting status is `Queued`, not terminal `Rejected`.

6. **Given** lifecycle rules are defined
   **When** Story 2.1 is complete
   **Then** `docs/lifecycle-transition-matrix.md` exists and enumerates accepted, rejected, and idempotent no-op outcomes for each command across all 9 statuses
   **And** later lifecycle stories reference this artifact rather than choosing behavior locally.

7. **Given** the lifecycle implementation is tested
   **When** the transition matrix is exercised
   **Then** every legal and illegal transition across the 9 statuses is covered by deterministic tests
   **And** the handler remains pure: no clock, RNG, I/O, Dapr, or EventStore envelope ownership.

## Tasks / Subtasks

- [x] **Task 1 — Expand `WorkItemStatus` to the full 9-state machine (AC: #1-#5)**
  - [x] Edit `src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs` to add the remaining 7 statuses, keeping `Unknown = 0` (pre-creation sentinel) and `Created = 1`. Add: `Assigned`, `Queued`, `InProgress`, `Suspended`, `Completed`, `Cancelled`, `Rejected`, `Expired`. Keep the existing `[JsonConverter(typeof(JsonStringEnumConverter<WorkItemStatus>))]`.
  - [x] Assign stable explicit integer values (additive, never renumber existing) — e.g. `Assigned = 2, Queued = 3, InProgress = 4, Suspended = 5, Completed = 6, Cancelled = 7, Rejected = 8, Expired = 9`. These are serialized as **names** (string enum), so the integers are for stability only; do not reorder.
  - [x] Do NOT add any "Resumed" status — resume is a transition back to `InProgress` only (AC #3, FR-6).

- [x] **Task 2 — Add the lifecycle commands (transition triggers) (AC: #1-#5)**
  - [x] Create the lifecycle command records under `src/Hexalith.Works.Contracts/Commands/` (one public sealed record per file, imperative names, NO `Command` suffix — AR-22). Each carries at minimum `TenantId TenantId` and `WorkItemId WorkItemId` plus only the fields its transition needs in v1 (keep payloads MINIMAL; later stories enrich additively per NFR-12 — no `V2`):
    - `AssignWorkItem(TenantId, WorkItemId, ExecutorBinding Binding)` — binds/rebinds an executor.
    - `QueueWorkItem(TenantId, WorkItemId)` — places into the shared pool (Created→Queued, Assigned→Queued requeue).
    - `ClaimWorkItem(TenantId, WorkItemId, ExecutorBinding Binding)` — the InProgress-entry act (Assigned|Queued→InProgress). **Single-claim-wins concurrency is OUT OF SCOPE here — Story 4.3.** Model only the transition.
    - `SuspendWorkItem(TenantId, WorkItemId)` — InProgress→Suspended. **Await-condition payloads/sets are OUT OF SCOPE — Story 3.5.** No `AwaitCondition` field in v1 of this command.
    - `ResumeWorkItem(TenantId, WorkItemId)` — Suspended→InProgress. **Correlation-key matching is OUT OF SCOPE — Story 3.5.**
    - `CompleteWorkItem(TenantId, WorkItemId)` — explicit complete act. **Remaining=0 auto-completion is OUT OF SCOPE — Story 2.3.**
    - `CancelWorkItem(TenantId, WorkItemId)` — terminal cancel.
    - `RejectWorkItem(TenantId, WorkItemId, bool Requeue = true)` — bound executor declines; `Requeue=true` (default) → resting `Queued`, `Requeue=false` → terminal `Rejected` (AC #5; FR-10; Story 2.5 AC#4/#5).
    - `ExpireWorkItem(TenantId, WorkItemId)` — terminal expiry. **The command is the adapter-fired signal; `Handle` reads no clock** (AR-15/C3). TTL/date sourcing is OUT OF SCOPE — Story 4.6.
  - [x] Command names not explicitly listed in architecture's command catalog (`QueueWorkItem`, `CompleteWorkItem`) are implied by the event catalog (`WorkItemQueued`, `WorkItemCompleted`) and epics ACs; name them per AR-22 conventions. [Source: architecture.md#Naming (lines 296-299); epics.md#Story 2.5]

- [x] **Task 3 — Add the lifecycle events (past-tense raw-act markers) (AC: #1-#5)**
  - [x] Create the success events under `src/Hexalith.Works.Contracts/Events/` (sealed records, past-tense, NO `Event` suffix, implementing `IEventPayload`, mirroring `WorkItemCreated.cs`). Each MUST carry `(string AggregateId, long Sequence)` as the first two members (AR-4), then `TenantId`, `WorkItemId`, and only the minimal transition payload:
    - `WorkItemAssigned(AggregateId, Sequence, TenantId, WorkItemId, ExecutorBinding Binding)`
    - `WorkItemQueued(AggregateId, Sequence, TenantId, WorkItemId)`
    - `WorkItemClaimed(AggregateId, Sequence, TenantId, WorkItemId, ExecutorBinding Binding)`
    - `WorkItemSuspended(AggregateId, Sequence, TenantId, WorkItemId)`
    - `WorkItemResumed(AggregateId, Sequence, TenantId, WorkItemId)`
    - `WorkItemCompleted(AggregateId, Sequence, TenantId, WorkItemId)`
    - `WorkItemCancelled(AggregateId, Sequence, TenantId, WorkItemId)`
    - `WorkItemRejected(AggregateId, Sequence, TenantId, WorkItemId, bool Requeue)`
    - `WorkItemExpired(AggregateId, Sequence, TenantId, WorkItemId)`
  - [x] These are 9 of the frozen 14-event v1 catalog. Do NOT introduce events outside the catalog (e.g. no `WorkItemStarted`) — `WorkItemClaimed` is the catalog's only `InProgress`-entry event. [Source: epics.md#FR-7; architecture.md#Naming (lines 302-304)]
  - [x] Add the rejection event for illegal transitions: `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs` — `sealed record WorkItemTransitionRejected(TenantId TenantId, WorkItemId WorkItemId, WorkItemStatus FromStatus, string AttemptedAct) : IRejectionEvent`. Mirror the context-carrying pattern of `WorkItemCannotReferenceParentFromAnotherTenant.cs` (rejections carry context, no `Sequence` — they are returned to the caller, not appended to the stream). Do NOT add `ClaimRejected`/`ConcurrencyRejected` here — those belong to Story 4.3's concurrency work.

- [x] **Task 4 — Add the authoritative transition table + `Handle`/`Apply` (AC: #1-#7)**
  - [x] Create the single source of truth for transitions in `src/Hexalith.Works.Server/Aggregates/` — a pure, static lifecycle decision (e.g. `WorkItemLifecycle.cs`) that, given `(WorkItemStatus current, <act>)`, returns the outcome: Accept (→ target status) / Reject / NoOp. Encode the recommended matrix in **Dev Notes → Transition Matrix** below as a table/switch. This table is what `docs/lifecycle-transition-matrix.md` (Task 6) must mirror 1:1.
  - [x] Add a `Handle` overload to `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` for **each** lifecycle command. Each overload:
    - Guards null args (mirror existing `CreateWorkItem` handler).
    - Reads `state.Status` (treat `state is null` or `Status == Unknown` as "not created" → reject every lifecycle command except create).
    - Consults the transition table.
    - Returns `DomainResult.Success([<event>])` with `Sequence = (state.Sequence ?? 0) + 1`, `DomainResult.Rejection([new WorkItemTransitionRejected(...)])`, or `DomainResult.NoOp()` per the matrix.
  - [x] **Sequence tracking gap (must fix):** `WorkItemState` has no sequence/version field today and `CreateWorkItem`'s handler hardcodes `state is null ? 1 : 2`. Add a `public long Sequence { get; private set; }` to `WorkItemState`, set it in every `Apply(success event)` to `e.Sequence`, and change the create handler to derive the next sequence from `state?.Sequence` (preserving create-on-null behavior). Multi-event lifecycles require monotonic sequence assignment. [Source: WorkItemAggregate.cs:34; WorkItemState.cs; deferred-work.md#Rejection-event sequencing]
  - [x] Add an `Apply(...)` overload to `src/Hexalith.Works.Contracts/State/WorkItemState.cs` for each new success event: set `Status` to the target, set `Sequence = e.Sequence`, and update only the minimal field this story owns (e.g. `WorkItemAssigned`/`WorkItemClaimed` set `ExecutorBinding`; `WorkItemRejected` with `Requeue=true` sets `Status = Queued`, with `Requeue=false` sets `Status = Rejected`). Do NOT mutate burn-down, await-conditions, or roll-up (deferred). Add no-op `Apply` overloads for `WorkItemTransitionRejected` if the EventStore replay convention requires them (mirror the existing rejection `Apply` no-ops at `WorkItemState.cs:39-46`, with the `CA1822` pragma).
  - [x] Keep `WorkItemAggregate` and `WorkItemState` **pure**: no clock, `Guid.NewGuid`, RNG, I/O, Dapr, or EventStore envelope APIs (NFR-5; `P0_WorkItemKernelRemainsPure`).

- [x] **Task 5 — Update the deferred-runtime fitness guard (AC: #7 — CRITICAL, build will fail otherwise)**
  - [x] `P0_WorkItemSliceDoesNotIntroduceDeferredRuntimeBehavior` in `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:167-191` currently bans the substrings `BurnDown, Burndown, RollUp, Suspend, Resume, Reminder, Claim, Queue` in `src/**/*.cs`. Story 2.1 legitimately introduces `Queued`/`Suspended` statuses and `Suspend`/`Resume`/`Claim`/`Queue` lifecycle commands & events. **Remove `Suspend`, `Resume`, `Queue`, and `Claim` from the `deferredDomainTerms` list. KEEP `BurnDown`, `Burndown`, `RollUp`, and `Reminder` banned** (burn-down math → Story 2.3; roll-up → Epic 3; reminders → Story 4.6 remain deferred).
  - [x] Rename the test and its failure message away from the stale "Story 1.2 permits create/replay only" wording to reflect that the remaining banned terms are the still-deferred runtime behaviors (burn-down, roll-up, reminders).
  - [x] Do NOT weaken `P0_WorkItemKernelRemainsPure` — the kernel-purity bans (clock/RNG/I/O/Dapr) still apply unchanged.
  - [x] Confirm `ExpireWorkItem`/`Handle` introduces no banned purity symbol (expiry is command-driven; `Handle` reads no clock).

- [x] **Task 6 — Author `docs/lifecycle-transition-matrix.md` (AC: #4, #6)**
  - [x] Create `docs/lifecycle-transition-matrix.md` enumerating, for **each of the 9 statuses** × **each lifecycle command**, the outcome: **Accept** (→ target status + emitted event), **Reject** (`WorkItemTransitionRejected`), or **NoOp** (`DomainResult.NoOp`). Mirror the **Dev Notes → Transition Matrix** table exactly and keep it 1:1 with the code table in Task 4.
  - [x] Include a dedicated **per-state cancel/expire decision** section covering all 9 statuses (AR-13/C1) — the reactor cascade (Story 3.6) and Story 2.5 depend on this. State explicitly that already-terminal items are unaffected by cancel/expire (the basis for idempotent cascade).
  - [x] Include the **idempotent no-op list**: the exact `(terminal status, duplicate terminal command)` pairs that return `DomainResult.NoOp` (e.g. `Cancelled` + `CancelWorkItem` → NoOp; `Completed` + `CompleteWorkItem` → NoOp; `Expired` + `ExpireWorkItem` → NoOp; `Rejected` + non-requeuable `RejectWorkItem` → NoOp). Everything else from a terminal state is a `Reject`.
  - [x] Add a header note: this artifact is the **single source of truth**; Stories 2.3/2.4/2.5, 3.5, 3.6, 4.1-4.3, 4.6 reference it and must not choose transition behavior locally (AC #6).

- [x] **Task 7 — Add a lifecycle test helper (optional but recommended) (AC: #7)**
  - [x] `tests/Hexalith.Works.Testing/` is currently marker-only. Add a small pure helper (e.g. `WorkItemStateBuilder` or `WorkItemReplay`) that replays an event sequence to land a `WorkItemState` in a target status, so transition tests can arrange any of the 9 statuses without duplication. Keep it pure (Contracts-only reference; no infra). This realizes the architecture's planned `WorkItemBuilder` testing double. [Source: architecture.md#Testing doubles (lines 488-489)]

- [x] **Task 8 — Add deterministic transition tests (AC: #1-#7)**
  - [x] In `tests/Hexalith.Works.UnitTests/`, add a `WorkItemLifecycleTests.cs` (xUnit v3 + Shouldly) covering **every (status, command) cell** of the matrix: accepted transitions assert `DomainResult.Success` + the right event type + replayed `Status` equals the target; rejected cells assert `IsRejection` + a single `WorkItemTransitionRejected`; no-op cells assert `IsNoOp`. Use `[Theory]`/`[InlineData]` to keep it exhaustive and compact. Mirror the arrange→Handle→assert→Apply→assert pattern in `WorkItemCreateTests.cs`.
  - [x] Cover the AC-specific cases explicitly: Created→Assigned/Queued (AC#1) and an unsupported-from-Created rejection; Assigned↔Queued + Assigned/Queued→InProgress (AC#2); InProgress→Suspended→InProgress with **no `Resumed` status** (AC#3); terminal-state reject + listed idempotent NoOp (AC#4); `RejectWorkItem(Requeue:true)` resting at `Queued` and `RejectWorkItem(Requeue:false)` resting at terminal `Rejected` (AC#5).
  - [x] Add a fitness test (in `ArchitectureTests`) asserting `docs/lifecycle-transition-matrix.md` exists and references each of the 9 statuses and each lifecycle command, using `RepositoryRoot.PathFromRoot("docs", "lifecycle-transition-matrix.md")` and a vacuous-pass guard (assert content discovered before asserting completeness) — mirror `BoundaryDecisionRecordTests.cs`.
  - [x] (Optional) Add a purity meta-assertion or rely on `P0_WorkItemKernelRemainsPure` to enforce AC#7's clock/RNG/I/O-free requirement.
  - [x] Do NOT add raw `Assert.*`, Moq, or FluentAssertions; extend the existing Shouldly style. Keep all Tier-1 tests pure (no Dapr/Aspire/containers/network/file I/O).

- [x] **Task 9 — Build and verify the slice (AC: #1-#7)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` (warnings-as-errors).
  - [x] Run the affected test executables directly (Microsoft.Testing.Platform named-pipe is blocked in this sandbox): at minimum `UnitTests`, `IntegrationTests`, `ArchitectureTests`, `PropertyTests` from `tests/<proj>/bin/Release/net10.0/<proj>`.
  - [x] Confirm the previously-green suite stays green (baseline after 1.4: UnitTests 60, IntegrationTests 13, ArchitectureTests 25, PropertyTests 1) plus the new lifecycle tests.
  - [x] Do not use recursive submodule commands. Do not modify sibling submodule files.

## Dev Notes

### Scope Boundary (read first — prevents over-build)

Story 2.1 is the **first Epic 2 story** and owns exactly one thing: **the lifecycle state machine** — the authoritative set of legal/illegal/idempotent transitions across the 9 statuses, plus the human-readable `docs/lifecycle-transition-matrix.md` that later stories conform to. It covers **FR-6** (and the transition-level slice of FR-10's reject-requeue rule). [Source: epics.md#Story 2.1; #FR-6; FR Coverage Map (line 347)]

**IN scope:** the 9-state `WorkItemStatus`; the lifecycle commands + events as *thin transition triggers* (minimal payloads); the pure transition table in `Server`; `Handle`/`Apply` overloads that enforce it; `WorkItemTransitionRejected`; `DomainResult.NoOp` for listed terminal duplicates; the matrix doc (incl. the AR-13 per-state cancel/expire decisions); sequence tracking on `WorkItemState`; the fitness-guard update; exhaustive transition tests.

**OUT of scope (defer — do NOT implement here; SM-C1/SM-C2 are binding):**
- Burn-down math / `Remaining=0 → Completed` auto-completion → **Story 2.3** (keep `BurnDown`/`RollUp` banned).
- `ReportProgress`, `ReEstimate`, `RescheduleWorkItem`, `SpawnChild` commands/events → **Stories 2.3/2.4/3.2**.
- Await-condition value object/sets and resume-by-correlation-key matching → **Story 3.5** (`SuspendWorkItem`/`ResumeWorkItem` here carry NO await payload).
- Single-claim-wins concurrency, `ClaimRejected`/`ConcurrencyRejected`, expected-version conflict → **Story 4.3** (`ClaimWorkItem` here is transition-only).
- Reassignment/hand-off binding policy nuance → **Story 4.2** (this story sets the legal transitions; 4.2 enriches binding behavior).
- Cascade execution, reminders, Aspire pipeline proof, reactor runtime → **Stories 3.6/4.5/4.6**.
- Recursive roll-up and the "what's next" projection → **Epic 3 / Story 4.4**.
- PolymorphicSerializations registration + golden-payload corpus → **Story 2.2** (this story's new events are plain `System.Text.Json` records; no registration here).

[Source: epics.md#Scope reminder (lines 23-30); #Epic 2 stories; #SM-C1; #SM-C2; #FR Coverage Map]

### Current State (what exists after Story 1.4 — read before coding)

- The kernel is scaffolded and green (UnitTests 60, IntegrationTests 13, ArchitectureTests 25, PropertyTests 1 at Release, 0 warnings). [Source: 1-4...md#Verification evidence]
- `WorkItemStatus` has only `Unknown = 0, Created = 1` today — Story 2.1 adds the other 7. [Source: src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs]
- `WorkItemAggregate` is a pure `static class` with a single `Handle(CreateWorkItem, WorkItemState?) → DomainResult`. It hardcodes `Sequence = state is null ? 1 : 2`. [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:12,34]
- `WorkItemState` exposes `Status` (private setter, set by `Apply(WorkItemCreated)` to `Created`) and `Apply` overloads for the two rejection events (no-op, `CA1822` pragma). **It has no `Sequence`/version field** — Task 4 adds one. [Source: src/Hexalith.Works.Contracts/State/WorkItemState.cs]
- `CreateWorkItem.cs` is the only command; only `WorkItemCreated` + two rejection events exist. The matrix doc does not exist. [Source: src/Hexalith.Works.Contracts/Commands; docs/]
- `DomainResult` (EventStore.Contracts) already provides `Success(events)`, `Rejection(rejectionEvents)`, **`NoOp()`** (static factory → empty events), and `IsSuccess`/`IsRejection`/`IsNoOp`. Mixed success+rejection throws at construction. [Source: Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Results/DomainResult.cs:63-95]
- Test helper `DomainResultAssertions` (EventStore.Testing) offers `ShouldBeSuccess(result, count)`, `ShouldBeRejection`, `ShouldBeNoOp`, `ShouldContainEvent<T>` — usable from the Works tests. [Source: Hexalith.EventStore/src/Hexalith.EventStore.Testing/Assertions/DomainResultAssertions.cs]

### CRITICAL — fitness guards your changes interact with

1. **`P0_WorkItemSliceDoesNotIntroduceDeferredRuntimeBehavior` WILL fail unless updated (Task 5).** It scans every `src/**/*.cs` (except `*Assembly.cs`, `ServiceDefaults/Extensions.cs`, `AppHost/Program.cs`) for `Suspend`/`Resume`/`Claim`/`Queue` — all of which 2.1 legitimately introduces. Remove those four; keep `BurnDown`/`Burndown`/`RollUp`/`Reminder`. This is the single most likely build-breaker. [Source: ScaffoldGovernanceTests.cs:167-191]
2. **`P0_WorkItemKernelRemainsPure` (unchanged, must stay green).** Bans `DateTime.Now/UtcNow`, `DateTimeOffset.*`, `Stopwatch`, `Guid.NewGuid`, `UniqueIdHelper.Generate`, `File.`, `Directory.`, `HttpClient`, `Dapr` anywhere in `Server`. Your `Handle`/transition-table code reads no clock — `ExpireWorkItem` is a command, not a clock read (AR-15). [Source: ScaffoldGovernanceTests.cs:194-220]
3. **`P0_WorkItemServerDependsOnlyOnContracts` (unchanged, must stay green).** `Server` references only `Contracts`. The pure transition table stays in `Server`; no EventStore.Client/Dapr dependency is added here. [Source: ScaffoldGovernanceTests.cs:222-239]
4. **`DependencyDirectionTests` (unchanged, must stay green).** Contracts→EventStore.Contracts only; Server/Projections/Reactor→Contracts only; no sibling implementation refs; Hexalith deps are ProjectReferences not PackageReferences. New commands/events live in `Contracts`, the table in `Server` — no new ProjectReference is required. [Source: DependencyDirectionTests.cs]

### Decisions (do not re-litigate)

- **Single source of truth.** Transitions are decided by ONE pure table in `Server` (Task 4); `docs/lifecycle-transition-matrix.md` mirrors it 1:1. Later stories consult it; they do not redefine transitions. (AC #6) [Source: epics.md#Story 2.1 AC#6; architecture.md#9-state machine + per-state cancel/expire table (lines 100, 267, 461-462)]
- **`WorkItemClaimed` is the `InProgress`-entry event** (the only one in the frozen 14-event catalog). Use it for both `Assigned→InProgress` and `Queued→InProgress`. Do not invent a `WorkItemStarted` event. Single-claim-wins is Story 4.3. [Source: epics.md#FR-7; #FR-18; architecture.md#Naming (lines 302-304); #B1 (line 244)]
- **Reject vs Rejected — two distinct concepts.** `WorkItemTransitionRejected` (`IRejectionEvent`) = a *command was refused, no state change*. The `Rejected` *status* = a terminal state reached only by `RejectWorkItem(Requeue:false)`. `RejectWorkItem(Requeue:true)` (default) emits `WorkItemRejected` raw-act evidence but rests at `Queued`. (AC #5; epics overrides architecture here — architecture.md is silent on auto-requeue, the epics/FR-10 requeue-default rule wins.) [Source: epics.md#FR-10; #Story 2.1 AC#5; #Story 2.5 AC#4-#5]
- **Expire is command-driven, clock-free.** `ExpireWorkItem` is the adapter-fired signal; `Handle` never reads a clock; deadlines are "advisory-until-fired" (AR-15/C3). TTL/date sourcing + reminder firing is Story 4.6. [Source: architecture.md#C3 (lines 257-258); #C2 (line 256); epics.md#Story 2.5 AC#6]
- **Sequence is tracked in state.** Add `WorkItemState.Sequence`; `Handle` assigns `(state?.Sequence ?? 0) + 1` to the next event; each success `Apply` updates it. This replaces the `state is null ? 1 : 2` placeholder for multi-event streams. Full EventStore stream-append/replay wiring stays deferred (this is pure in-memory sequencing for deterministic replay). [Source: WorkItemAggregate.cs:34; deferred-work.md#Rejection-event sequencing]
- **Minimal, additive event payloads.** Carry only `(AggregateId, Sequence, TenantId, WorkItemId)` + the one field a transition needs (binding / requeue flag). NFR-12 forbids `V2` types — later stories ADD nullable fields to these same records. Do not pre-add burn-down/await/roll-up fields now. [Source: epics.md#NFR-12; #AR-4]

### Recommended Transition Matrix (encode this in code + doc; finalize edge cells)

Statuses: `Created`, `Assigned`, `Queued`, `InProgress`, `Suspended` (non-terminal); `Completed`, `Cancelled`, `Rejected`, `Expired` (terminal). `Unknown`/`null` state = "not created" → every lifecycle command **Reject**.

Acts: Assign, Queue, Claim, Suspend, Resume, Complete, Cancel, Reject(requeue=true→Queued / false→Rejected), Expire. (`R` = Reject via `WorkItemTransitionRejected`; `→X` = Accept, transition to X with the paired event; `NoOp` = `DomainResult.NoOp`.)

| From \ Act | Assign | Queue | Claim | Suspend | Resume | Complete | Cancel | Reject | Expire |
|---|---|---|---|---|---|---|---|---|---|
| **Created**    | →Assigned | →Queued | R | R | R | R | →Cancelled | R (not bound) | →Expired |
| **Assigned**   | →Assigned (rebind) | →Queued | →InProgress | R | R | R | →Cancelled | →Queued *(requeue)* / →Rejected *(non-requeue)* | →Expired |
| **Queued**     | →Assigned | R | →InProgress | R | R | R | →Cancelled | R (not bound) | →Expired |
| **InProgress** | R | R | R | →Suspended | R | →Completed | →Cancelled | R | →Expired |
| **Suspended**  | R | R | R | R | →InProgress | →Completed | →Cancelled | R | →Expired |
| **Completed**  | R | R | R | R | R | NoOp | R | R | R |
| **Cancelled**  | R | R | R | R | R | R | NoOp | R | R |
| **Rejected**   | R | R | R | R | R | R | R | NoOp *(non-requeue dup)* | R |
| **Expired**    | R | R | R | R | R | R | R | R | NoOp |

Notes:
- The terminal row rule (AC #4): from any terminal state, **every** command is `R` **except** the exact-duplicate terminal command, which is `NoOp` (the diagonal NoOp cells above). List these pairs explicitly in the doc.
- `Assigned + Reject` is the only cell whose target depends on the command's `Requeue` flag (AC #5).
- Cancel and Expire have an explicit decision in **every** row (AR-13) — non-terminal → terminal; terminal → R/NoOp. This is the per-state cancel/expire table the reactor (Story 3.6) and Story 2.5 require.
- Edge cells flagged for confirmation (defensible either way — pick, encode, document, test): `Created+Complete` (recommend R — nothing to complete), `Created+Reject` (R — no binding), `Queued+Reject` (R — no binding to decline), reassign of `InProgress` (recommend R here; hand-off-while-active is Story 4.2's to refine against this matrix). Keep the code table and the doc identical to whatever you finalize.

### File Structure (target locations — match the architecture tree)

- Commands → `src/Hexalith.Works.Contracts/Commands/<Name>.cs` (one type per file).
- Success events → `src/Hexalith.Works.Contracts/Events/<Name>.cs`; rejection event → `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs`.
- 9-state enum → edit `src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs`.
- Transition table → `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs`; `Handle` overloads added to `WorkItemAggregate.cs`; `Apply` overloads + `Sequence` added to `src/Hexalith.Works.Contracts/State/WorkItemState.cs`.
- Doc → `docs/lifecycle-transition-matrix.md`. Test helper → `tests/Hexalith.Works.Testing/`. Tests → `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs`, fitness test in `tests/Hexalith.Works.ArchitectureTests/FitnessTests/`.

[Source: architecture.md#Complete Project Directory Structure (lines 446-485); AR-22 (lines 292-324)]

### Testing Standards

- xUnit **v3** + Shouldly. No raw `Assert.*`, Moq, or FluentAssertions. One pure `Handle`→assert→`Apply`→assert flow per transition; `[Theory]`/`[InlineData]` for matrix coverage. Mirror `tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs`. [Source: 1-4...md#Testing Standards; WorkItemCreateTests.cs]
- Arrange any of the 9 statuses by replaying events into a fresh `WorkItemState` (Apply `WorkItemCreated` then the events that reach the target status) — the optional `WorkItemStateBuilder` (Task 7) keeps this DRY.
- Serialization: new events are plain `System.Text.Json` records; if you add a round-trip test, use `new JsonSerializerOptions(JsonSerializerDefaults.Web)` like `WorkItemCreateContractFlowTests.cs`. PolymorphicSerializations registration + golden corpus is Story 2.2 — do not add it here. [Source: IntegrationTests/WorkItemCreateContractFlowTests.cs; epics.md#Story 2.2 AC#5]
- Keep Tier-1 tests pure (no Dapr/Aspire/containers/network/file I/O/sleeps). The matrix-doc fitness test is file-system/reflection only. [Source: architecture.md#Test taxonomy (line 120); AR-21]

### Build / test execution (sandbox reality)

```bash
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build  Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal
# dotnet test is blocked by Microsoft.Testing.Platform named-pipe perms — run the built xUnit v3 executables directly:
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests
```
[Source: _bmad-output/implementation-artifacts/tests/test-summary.md]

### Previous Story Intelligence

- **Pure static handler pattern** is established: `WorkItemAggregate.Handle(command, state?)` returns `DomainResult`; `WorkItemState.Apply(event)` mutates in-memory state with private setters; rejections are `IRejectionEvent` with context, never exceptions. Extend this — add overloads, do not restructure. [Source: 1-2/1-3/1-4 stories; WorkItemAggregate.cs; WorkItemState.cs]
- **Validate at the writer, trust on replay.** Story 1.3 enforced the cross-tenant parent invariant once in `Handle` and applied the event verbatim on replay (no defensive `Apply` re-check). Follow the same posture: `Handle` decides the transition; `Apply` trusts the event and sets the status. [Source: 1-3...md#Decisions; WorkItemState.cs:48-52]
- **Closed enums reject `Unknown`/undefined casts** (`Channel`, `AuthorityLevel`). `WorkItemStatus` keeps `Unknown=0` as a pre-creation sentinel only; lifecycle `Handle` overloads must treat `Unknown`/`null` state as "not created" → reject. [Source: 1-3...md; src/.../Channel.cs]
- **Adversarial review is expected** (Blind Hunter + Edge Case Hunter + Acceptance Auditor) and historically produces ≥1 patch per story. Pre-empt it: vacuous-pass guards on new reflection/file-system tests; cover every matrix cell incl. terminal-from-terminal; assert the `Requeue` flag's two outcomes; assert `Sequence` increments across a multi-event replay. [Source: 1-4...md#Previous Story Intelligence; Hexalith.EventStore/CLAUDE.md#Code Review Process]
- **Deferred-work ledger** records that rejection events carry no `Sequence`/`AggregateId` and rejection `Apply` overloads are no-ops, and that the `state is null ? 1 : 2` sequencing assumes a rejection never advanced the stream. This story's `Sequence` field on `WorkItemState` is the clean fix for success-event sequencing; rejection-event stream-persistence remains deferred to the EventStore stream-append story. [Source: deferred-work.md]

### Git Intelligence

- `6ea70b7 feat(story-1.4)` (baseline) — added the `Ports/`+`Resolvers/` seam, `Obligation.Reference`, `docs/boundary-decision-record.md`, and boundary fitness tests; the `ArchitectureTests` project already references the three kernel projects (Contracts/Server/Projections) for reflection.
- `b0687e2 feat(story-1.3)` — `PartyId`/`Channel`/`ExecutorBinding`, tenant-scoped parent + cross-tenant rejection, `DependencyDirectionTests`. `5f3e497 feat(story-1.2)` — the pure create handler + create/replay + JSON round-trip tests. Build on these files; do not fork a parallel harness.

### Project Structure Notes

- Works holds domain code only; the Aspire host is the one allowed technical component. The lifecycle state machine is domain code and belongs here (`Contracts` + `Server`). No new technical layer is introduced. [Source: CLAUDE.md#Repository responsibility]
- Hexalith libraries are `ProjectReference` via `$(Hexalith<Module>Root)` root-path variables, never `PackageReference`; this story needs no new sibling reference. Only root submodules; never `--recursive`. [Source: CLAUDE.md#Hexalith library references; #Submodule rules]

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.1: Define the Lifecycle State Machine] — story statement + ACs.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-6] — 9-status lifecycle, forward path, illegal transitions as `IRejectionEvent`, no transition out of terminal.
- [Source: _bmad-output/planning-artifacts/epics.md#FR-7] — frozen 14-event raw-act catalog (event names).
- [Source: _bmad-output/planning-artifacts/epics.md#FR-10] — cancel/reject/expire; reject defaults to requeue (Queued), terminal only when non-requeuable; cascade context.
- [Source: _bmad-output/planning-artifacts/epics.md#NFR-2; #NFR-5; #NFR-9; #NFR-12] — event-sourcing invariants, domain purity, idempotency, additive no-`V2` serialization.
- [Source: _bmad-output/planning-artifacts/architecture.md#9-state machine + per-state cancel/expire table (lines 100, 267, 461-462); #AR-13] — transition-table design + per-state cancel/expire.
- [Source: _bmad-output/planning-artifacts/architecture.md#Naming, structure & dependency direction (lines 292-324); #AR-22] — command/event naming, file-scoped namespaces, dependency direction.
- [Source: _bmad-output/planning-artifacts/architecture.md#B1 (line 244); #C2/C3 (lines 256-258); #AR-15] — claim single-aggregate, expire advisory-until-fired/clock-free.
- [Source: src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs; src/Hexalith.Works.Contracts/State/WorkItemState.cs; .../ValueObjects/WorkItemStatus.cs] — current handler/state/enum to extend.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Results/DomainResult.cs:63-95] — `Success`/`Rejection`/`NoOp`/`IsNoOp`.
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:167-220] — deferred-term guard (update) + kernel-purity guard (keep).
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/RepositoryRoot.cs; .../BoundaryDecisionRecordTests.cs] — `PathFromRoot` + file-system fitness test pattern (mirror for the matrix doc).
- [Source: tests/Hexalith.Works.UnitTests/WorkItemCreateTests.cs; tests/Hexalith.Works.IntegrationTests/WorkItemCreateContractFlowTests.cs] — test + JSON round-trip patterns to mirror.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — rejection-event sequencing / stream-persistence deferral.
- [Source: _bmad-output/implementation-artifacts/tests/test-summary.md] — sandbox build/test commands.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (claude-opus-4-8[1m])

### Debug Log References

- `dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false` → up to date.
- `dotnet build Hexalith.Works.slnx -c Release` → **0 Warning(s), 0 Error(s)** (warnings-as-errors).
- Initial build failure CS0051 (inconsistent accessibility): the data-driven test helper enums `Act`/`Expect` were
  declared `internal` but consumed by `public` xUnit theory methods. Resolved by promoting both to top-level `public`
  enums (top-level, so CA1034 nested-visible-type does not apply).
- Initial ArchitectureTests failure: `P0_WorkItemSliceDoesNotIntroduceDeferredBurnDownRollUpOrReminderBehavior`
  tripped on the word "reminder" in `ExpireWorkItem.cs`'s XML doc comment. Reworded the comment (kept the Story 4.6
  deferral meaning) — the term is still legitimately banned in `src`.
- Test executables run directly (Microsoft.Testing.Platform named-pipe blocked in sandbox).

### Completion Notes List

- Expanded `WorkItemStatus` to the full 9-state machine (additive explicit integers; no `Resumed` status — resume is a
  transition back to `InProgress`).
- Added the 9 lifecycle commands (thin transition triggers, minimal payloads, no `Command` suffix) and the 9 success
  events (`(AggregateId, Sequence)`-first, `IEventPayload`), plus the `WorkItemTransitionRejected` rejection event.
- Encoded the authoritative transition table once as a pure, internal `WorkItemLifecycle.Decide(status, act, requeue)`
  in `Server`; added a `Handle` overload per command and an `Apply` overload per success event. `docs/lifecycle-transition-matrix.md`
  mirrors this table 1:1 and is the documented single source of truth.
- Fixed the sequence-tracking gap: added `WorkItemState.Sequence`, set it in every success `Apply`, and changed the
  create handler from the `state is null ? 1 : 2` placeholder to `(state?.Sequence ?? 0) + 1`. A dedicated test proves
  monotonic sequencing across a multi-event lifecycle and that a rejection does not advance the sequence.
- Updated the deferred-runtime fitness guard: removed `Suspend`/`Resume`/`Queue`/`Claim` (now legitimately introduced),
  kept `BurnDown`/`Burndown`/`RollUp`/`Reminder` banned, and renamed the test + message away from the stale Story-1.2
  wording. `P0_WorkItemKernelRemainsPure`, `P0_WorkItemServerDependsOnlyOnContracts`, and `DependencyDirectionTests`
  remain unchanged and green.
- Added the pure `WorkItemStateBuilder` testing double (Contracts-only) that replays the shortest legal path to any of
  the 9 statuses, and exhaustive `[Theory]` coverage of every (status, command) cell (81 cells across two theories),
  the AC-specific cases (#1–#5), uncreated-state rejection, and a matrix-doc fitness test with a vacuous-pass guard.
- **Verification:** Release build 0/0; UnitTests 166 (60 baseline + 106 new), IntegrationTests 18 (13 baseline + 5
  serialization-boundary contract-flow tests added by the QA-automation pass), ArchitectureTests 26 (25 baseline + 1 new),
  PropertyTests 1 — **211 total, all passing, no regressions** (re-verified during review; the original Dev Agent Record
  recorded 13/206 before the QA pass added the integration tests, see Senior Developer Review (AI)).

### File List

**Added — source (Contracts):**
- `src/Hexalith.Works.Contracts/Commands/AssignWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/QueueWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/ClaimWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/SuspendWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/ResumeWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/CompleteWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/CancelWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/RejectWorkItem.cs`
- `src/Hexalith.Works.Contracts/Commands/ExpireWorkItem.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemAssigned.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemQueued.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemClaimed.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemSuspended.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemResumed.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemCompleted.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemCancelled.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemRejected.cs`
- `src/Hexalith.Works.Contracts/Events/WorkItemExpired.cs`
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs`

**Added — source (Server):**
- `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs`
- `src/Hexalith.Works.Server/Aggregates/LifecycleAct.cs`
- `src/Hexalith.Works.Server/Aggregates/LifecycleOutcome.cs`

**Modified — source:**
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs` (7 new statuses)
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` (`Sequence` field + 10 new `Apply` overloads)
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` (9 lifecycle `Handle` overloads + sequence-derivation fix)

**Added — docs:**
- `docs/lifecycle-transition-matrix.md`

**Added — tests:**
- `tests/Hexalith.Works.Testing/WorkItemStateBuilder.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/LifecycleTransitionMatrixDocTests.cs`
- `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs` (+5 serialization-boundary contract-flow tests added by the QA-automation pass; see test-summary.md)

**Modified — tests:**
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` (deferred-term guard update + rename)
- `tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj` (ProjectReference to `Hexalith.Works.Testing`)

**Modified — tracking:**
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (2-1 → in-progress → review → done)
- `_bmad-output/implementation-artifacts/tests/test-summary.md` (Story 2.1 QA-automation summary; records the +5 integration tests and the 211/211 result)

## Change Log

| Date | Version | Description |
|---|---|---|
| 2026-06-16 | 2.1.0 | Implemented the 9-state work-item lifecycle state machine: 9 lifecycle commands + 9 success events + `WorkItemTransitionRejected`; a single pure transition table in `Server` with `Handle`/`Apply` overloads; `WorkItemState.Sequence` tracking; the authoritative `docs/lifecycle-transition-matrix.md`; deferred-runtime fitness-guard update; the `WorkItemStateBuilder` test double; and exhaustive transition tests. All ACs (#1–#7) satisfied; Release build 0/0; 211 tests passing (Dev Agent Record originally recorded 206 before the QA-automation pass added 5 integration tests). Status → review. |
| 2026-06-16 | 2.1.1 | Adversarial code review (auto-fix). Re-verified ground truth: Release build 0/0; UnitTests 166, IntegrationTests 18, ArchitectureTests 26, PropertyTests 1 = **211/211 passing**. No code/CRITICAL/HIGH findings — implementation matches the matrix 1:1 and satisfies AC #1–#7. Fixed two MEDIUM documentation findings: (1) File List omitted `WorkItemLifecycleContractFlowTests.cs`; (2) stale counts (IntegrationTests 13→18, total 206→211). Status → done. |

## Senior Developer Review (AI)

_Reviewer: Jérôme Piquot on 2026-06-16 — adversarial review with auto-fix._

**Outcome: Approved (done).** Build `Release` **0 warnings / 0 errors** (warnings-as-errors); test
executables re-run directly: **UnitTests 166, IntegrationTests 18, ArchitectureTests 26, PropertyTests
1 = 211/211 passing, 0 failures.** No CRITICAL or HIGH findings; the implementation is faithful to the
story.

### Claim validation (git reality vs. story claims)

- **AC #1–#7 — all IMPLEMENTED.** Verified cell-by-cell that `WorkItemLifecycle.Decide` matches the
  Dev Notes matrix and `docs/lifecycle-transition-matrix.md` **1:1** (all 9 statuses × 9 acts, the
  flag-dependent `Assigned+Reject` and `Rejected+Reject` cells, and the four diagonal terminal NoOps).
  Kernel purity (AC #7) holds — `P0_WorkItemKernelRemainsPure` is green and no banned clock/RNG/I/O/Dapr
  symbol appears in `Server`. `ExpireWorkItem.Handle` reads no clock.
- **Tasks 1–9 — all genuinely done.** Every `[x]` was checked against the source: 9 commands + 9
  success events + `WorkItemTransitionRejected` exist with the AR-4 `(AggregateId, Sequence)`-first
  shape; `WorkItemState.Sequence` replaces the `state is null ? 1 : 2` placeholder; the deferred-term
  fitness guard correctly dropped `Suspend/Resume/Queue/Claim` and kept `BurnDown/RollUp/Reminder`.
- **Scope discipline — clean.** No out-of-scope build (no burn-down/await/roll-up/concurrency fields;
  events are plain `System.Text.Json` records with no PolymorphicSerializations registration).

### Findings (all fixed this run)

- **[MEDIUM] File List incomplete.** `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs`
  exists in git but was absent from the Dev Agent Record File List. It was added by the QA-automation
  pass (see `test-summary.md`) after the dev wrote the record. → Added to File List.
- **[MEDIUM] Stale verification evidence.** Completion Notes claimed `IntegrationTests 13` and the
  Change Log claimed `206 tests`; ground truth is **18** integration tests and **211** total (the QA pass
  added 5 serialization-boundary tests). The dev record was never reconciled after that pass.
  → Counts corrected in Completion Notes and Change Log; `test-summary.md` already had the right numbers.
- **[LOW] (not changed — recorded for awareness)** `LifecycleOutcome.Target` is computed by the table
  but never read by the `Handle` overloads (the resting status is determined by each event's `Apply`).
  It is correct in every accept cell and serves as in-table documentation, so it is left as-is to avoid
  churning a green, exhaustively-tested kernel; flagged so a future refactor can decide whether the
  table or the event should be the single authority for the target status.
