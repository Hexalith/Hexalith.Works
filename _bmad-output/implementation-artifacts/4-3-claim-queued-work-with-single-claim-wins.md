---
baseline_commit: 2dd46d0
---

# Story 4.3: Claim Queued Work with Single-Claim-Wins

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an executor,
I want to claim work from a shared queue,
so that system agents and people can pull from the same backlog without double ownership.

## Acceptance Criteria

1. **Given** a Work Item is `Queued`
   **When** an executor claims it with an `ExecutorBinding`
   **Then** `WorkItemClaimed` is emitted
   **And** the item transitions to `InProgress` bound to the claimant.

2. **Given** two executors race to claim the same `Queued` Work Item
   **When** both commands use the same expected version
   **Then** exactly one claim succeeds
   **And** the loser receives an observable domain rejection such as `ClaimRejected` or `ConcurrencyRejected`.

3. **Given** a Work Item is not `Queued`
   **When** an executor attempts to claim it
   **Then** the command is rejected as not claimable
   **And** no binding or status change occurs.

4. **Given** claim eligibility filtering is deferred to Theme 4
   **When** v1 claim behavior is inspected
   **Then** any executor in the tenant may claim a queued item
   **And** no routing score, eligibility engine, escalation ladder, or AI decision record is implemented.

5. **Given** claim behavior is tested
   **When** deterministic concurrency tests run
   **Then** they prove single-claim-wins through expected-version conflict rather than timing-dependent thread races.

## Tasks / Subtasks

- [x] **Task 1 — Reconcile the existing claim + concurrency surface before changing code (AC: #1–#5)**
  - [x] Read the claim vocabulary: `src/Hexalith.Works.Contracts/Commands/ClaimWorkItem.cs`;
    `src/Hexalith.Works.Contracts/Events/WorkItemClaimed.cs`;
    `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs`;
    `src/Hexalith.Works.Contracts/State/WorkItemState.cs` (`Apply(WorkItemClaimed)` ~L126–132, the `Sequence`
    property ~L22–27, the rejection `Apply` no-ops); `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`
    (`Handle(ClaimWorkItem)` L153–169, the `Reject(...)` helper ~L418–419, `NextSequence` ~L407–409,
    `CurrentStatus` ~L403–404); `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` (`Decide` L21–77,
    the `Claim` cells) and `LifecycleAct.cs`; and `docs/lifecycle-transition-matrix.md` (claim note ~L172–173).
  - [x] Confirm the claim path is already built (Story 2.1): `Queued → Claim = Accept(InProgress)` (~L45) and
    `Assigned → Claim = Accept(InProgress)` (~L36); **every other status — including `InProgress` and
    `Suspended` — rejects `Claim`**; `Handle(ClaimWorkItem)` emits `WorkItemClaimed(WorkItemId.Value,
    NextSequence(state), TenantId, WorkItemId, Binding)` on accept and `Reject(..., nameof(LifecycleAct.Claim))`
    → `WorkItemTransitionRejected(FromStatus, "Claim")` otherwise. AC #1 and AC #3 mechanics already exist;
    this story **proves and guards** them — do not re-shape them.
  - [x] Read the substrate concurrency surface this story depends on (do NOT modify the submodule):
    `Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs` (the 5-step
    `ProcessCommandAsync` pipeline; ETag atomic commit `StateManager.SaveStateAsync()` ~L442–481;
    conflict → `ConcurrencyConflictException` ~L447–452; retry governed by `MaxPersistenceConflictRetries`);
    `Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventPersister.cs` (gapless sequence from
    `AggregateMetadata.CurrentSequence`); `…/Commands/ConcurrencyConflictException.cs`;
    `…/Configuration/CommandConcurrencyOptions.cs` (`DefaultMaxPersistenceConflictRetries = 1`);
    `…/Events/AggregateMetadata.cs` (`CurrentSequence`, `LastModified`, `ETag?`). Confirm
    `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs` already
    asserts that surface exists (`ConcurrencyConflictException`, `AggregateActor`).
  - [x] Confirm the durable wire surface is frozen: the v1 catalog is **36** (14 events / 14 commands / 8
    rejections); `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` `Count == 36`; the golden corpus
    under `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/` already contains
    `WorkItemClaimed.v1.json`. **Story 4.3 adds no new event, command, or rejection type (DC1; see Dev Notes).**

- [x] **Task 2 — Prove deterministic single-claim-wins via expected-version conflict (AC: #2, #5)**
  - [x] Add a focused unit test file `tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs`. Model
    the race **deterministically — no threads, no `Task.Run`, no sleeps, no shared-mutable-state interleaving**.
    The "expected version" is `WorkItemState.Sequence` the claim was computed against.
  - [x] **Core proof:** arrange a `Queued` item at `Sequence == N` (`WorkItemStateBuilder.InStatus(Queued, …)`).
    Build two distinct claims `claimA`/`claimB` with **different** valid `ExecutorBinding`s. Handle **both
    against the same observed state** (`queued`, version N): each returns `IsSuccess` with a single
    `WorkItemClaimed` whose `Sequence == N + 1`. Assert **both target the same sequence `N + 1`** — that
    collision *is* the expected-version conflict (only one event can persist at sequence `N+1` for the
    aggregate).
  - [x] **Winner commits, loser re-handles:** apply the winner's `WorkItemClaimed` (`queued.Apply(claimedA)`)
    → state is `InProgress` at `N + 1`, bound to A. Then re-handle `claimB` against the now-advanced state
    (this is exactly what the substrate's conflict-retry does: clear cache, rehydrate, re-run `Handle`) →
    `IsRejection` true, single `WorkItemTransitionRejected` with `FromStatus == InProgress` and
    `AttemptedAct == nameof(LifecycleAct.Claim)` (i.e. `"Claim"`). Assert applying that rejection is a no-op:
    `Status`, `Sequence`, and `ExecutorBinding` (still A) are unchanged.
  - [x] **Exactly-one assertion:** across winner + loser there is exactly one accepted `WorkItemClaimed` and
    exactly one observable `IRejectionEvent` (`loser.Events.Single().ShouldBeAssignableTo<IRejectionEvent>()`).
  - [x] Add an XML-doc/comment on the test class stating that the live ETag append/retry/exhaustion path is
    exercised under Aspire in **Story 4.5** (not here); this test proves the *domain outcome* of the
    expected-version collision deterministically (RR-3). Keep Tier-1 purity (no Dapr/Aspire/network).

- [x] **Task 3 — Prove the happy-path claim and the not-claimable rejection focused on claim (AC: #1, #3)**
  - [x] **AC #1 (focused, do not duplicate `WorkItemUniformExecutorBindingTests`):** in the new file, assert a
    single claim from `Queued` emits exactly one `WorkItemClaimed` at `Sequence + 1` carrying the supplied
    binding, replays to `Status == InProgress`, and `ExecutorBinding` equals the claimant binding. Cover the
    `Assigned → Claim` entry too (the second accept cell). Add an identity assertion on the emitted event
    (`AggregateId == WorkItemId.Value`, `TenantId`, `WorkItemId`).
  - [x] **AC #3:** for **each non-claimable status** (`Created`, `InProgress`, `Suspended`, and the four
    terminals `Completed`/`Cancelled`/`Rejected`/`Expired`) a `ClaimWorkItem` returns a rejection
    (`WorkItemTransitionRejected(FromStatus = <status>, AttemptedAct = "Claim")`), emits **no** `WorkItemClaimed`,
    and applying the result leaves `Status`, `Sequence`, and `ExecutorBinding` unchanged. Use a `[Theory]`.
    `WorkItemLifecycleTests` already covers the (status, Claim) matrix cells — these assertions are
    **claim-state-mutation** focused (no binding/sequence burn), not a duplicate of the matrix theory.

- [x] **Task 4 — Guardrail: unconditional claim, no eligibility/routing, catalog stays 36 (AC: #4)**
  - [x] Add a fitness method on `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`
    (mirror `P0_WorkItemSurfaceHasNoExecutorKindSpecificHandoffOrReassignTypeAndCatalogStays36`): scan declared
    **type names** under `src/` (reuse the existing `declarationRegex` + exclusion set) for a forbidden claim/
    routing vocabulary — patterns such as `ClaimEligibility`, `ClaimRouter`, `EligibilityFilter`,
    `EscalationLadder`, `RoutingScore`, `ExecutorRanking`, `ClaimDecisionRecord` — and assert none exist.
    Pair it with the same reflection-based frozen-catalog assertion (`polymorphicCatalogCount.ShouldBe(36, …)`)
    so adding a claim-specific or concurrency rejection type breaks the build.
  - [x] Confirm `IExecutorRouter` remains an **abstraction with no wired impl** and that `Handle(ClaimWorkItem)`
    takes no eligibility/authority/routing input (it validates only `TenantId`, `WorkItemId`, `Binding` and the
    lifecycle cell). Do not add an authority gate — `AuthorityLevel` stays carried-not-enforced (D1/FR-19).
  - [x] Preserve unchanged and green: `P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority`,
    `P0_ScaffoldContainsOnlyTheV1ProjectSet`, `P0_KernelProjectsStayInfrastructureFree`, both
    `EventStoreApiSurfaceCharacterizationTests` facts, `DependencyDirectionTests`, `BoundaryDecisionRecordTests`,
    and `LifecycleTransitionMatrixDocTests`.

- [x] **Task 5 — (Optional, taxonomy "claim idempotence") property test for claim convergence (AC: #2, #5)**
  - [x] Only if it adds falsifiable value beyond Task 2: in `tests/Hexalith.Works.PropertyTests/` (mirror the
    FsCheck wiring in `WorkItemRollUpConvergencePropertyTests.cs`) add a property that for a `Queued` item and
    **any** generated sequence of `K ≥ 2` distinct claim attempts applied in **any order**, **exactly one**
    `WorkItemClaimed` is accepted and the remaining `K − 1` are rejected (single-claim-wins is order-independent).
  - [x] Document explicitly (test comment) that true *duplicate-delivery* idempotency (same command redelivered)
    is a **substrate** concern (CausationId/offset dedup in `AggregateActor`, NFR-9/AR-11), **not** the kernel:
    re-handling a duplicate claim at the domain level against `InProgress` is a **rejection**, not a
    `DomainResult.NoOp`. Do not change the lifecycle to make duplicate claim a NoOp.

- [x] **Task 6 — Documentation and story bookkeeping (AC: #1–#5)**
  - [x] `docs/lifecycle-transition-matrix.md` — finalize the claim note (currently "Single-claim-wins
    concurrency is Story 4.3", ~L172–173): record that single-claim-wins is realized as the composition of the
    pure lifecycle (`Queued/Assigned → Claim = Accept(InProgress)`; all else `R`) **and** the EventStore
    substrate's expected-version (ETag) optimistic concurrency — the loser of a same-expected-version race
    re-handles to `WorkItemTransitionRejected(InProgress, "Claim")` (DC1); no new rejection type; catalog stays
    36. Keep cells 1:1 with `WorkItemLifecycle.cs` (no cell change).
  - [x] `docs/boundary-decision-record.md` — confirm/extend the EventStore line (it already records EventStore
    owns the **concurrency mechanism**, ~L36) and the Theme-4 line (routing/eligibility deferred, ~L77–78) with
    a one-line 4.3 note: single-claim-wins is the kernel lifecycle + substrate expected-version composition;
    Works adds no claim-eligibility/routing type in v1.
  - [x] `_bmad-output/implementation-artifacts/tests/test-summary.md` — add a Story 4.3 section (verification
    commands, before/after counts, files changed, gaps closed, not-applicable runtime/UI surfaces), mirroring
    the Story 4.2 entry structure.

- [x] **Task 7 — Verify the slice (AC: #1–#5)**
  - [x] Baseline is the Story 4.2 final of **549** green tests: UnitTests 438, IntegrationTests 80,
    ArchitectureTests 30, PropertyTests 1.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
    require **0 warnings / 0 errors**.
  - [x] Run the direct xUnit v3 binaries after the Release build (the reliable sandbox path; `dotnet test` is
    blocked by Microsoft.Testing.Platform named-pipe permissions):
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.
  - [x] Confirm `WorkItemV1Catalog.Count` is still **36** and the golden corpus
    (`SchemaEvolution/Golden/WorkItemClaimed.v1.json`) is byte-unchanged. Do not run recursive submodule
    commands; leave the unrelated `Hexalith.Tenants` gitlink change in the working tree untouched.

## Dev Notes

### Scope Boundary (read first — prevents over-build)

Story 4.3 is a **deterministic-concurrency-proof + guardrail** story for **FR-18** (push/pull coexist;
single-claim-wins), **NFR-3** (single-writer/optimistic; two claims → one success + domain rejection), and
**AR-10/B1** (single-aggregate claim under expected-version; the claimable pool is a *read projection*, not an
authoritative queue aggregate). The claim transition and rejection were **already built** in Story 2.1
(`ClaimWorkItem`/`WorkItemClaimed`, `WorkItemTransitionRejected`, the pure `WorkItemLifecycle.Decide` table) and
proved uniform across executor kinds in Story 4.1. The **expected-version optimistic concurrency mechanism is
owned by `Hexalith.EventStore`** (the `AggregateActor` → `EventPersister` → ETag `SaveStateAsync` pipeline), not
by Works. **Do not re-build or re-shape any of that.** [Source: _bmad-output/planning-artifacts/epics.md#Story
4.3: Claim Queued Work with Single-Claim-Wins; _bmad-output/planning-artifacts/architecture.md#API &
Communication Patterns (B1); #Architectural Risk & Assumption Stress-Test (invariant 2; RR-3);
_bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md;
_bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md]

**In scope:** reconcile the claim + substrate-concurrency surface; a **deterministic** single-claim-wins test
(two claims at the same expected version → one `WorkItemClaimed`, loser → `WorkItemTransitionRejected(InProgress,
"Claim")`, no thread race); focused AC #1 (claim emits/binds/transitions) and AC #3 (not-claimable rejection with
no mutation) tests; an AC #4 guardrail (no eligibility/routing/escalation/AI claim type; catalog stays 36);
optional claim-convergence property test; doc + test-summary updates.

**Out of scope (deferred — do not implement here):** the "what's next" queue projection and query — including any
**claimable-pool read projection** (Story 4.4; AR-10 says the pool is a read projection, but 4.3 is the
*single-aggregate claim* proof only); the Aspire command/event pipeline proof and the **live** ETag
append/retry/exhaustion behavior (Story 4.5); reminder/reactor recovery (Story 4.6); production UI, MCP/chatbot/
email adapters, executor routing, eligibility filtering, escalation ladders, `AuthorityLevel` enforcement, signed
links, security hardening, LLM/NL parsing, cost governance. **Do not add a `ClaimRejected` or `ConcurrencyRejected`
type** (DC1). v1 claim is unconditional — any tenant Executor may claim a queued item; eligibility is a Theme-4
routing concern. [Source: _bmad-output/planning-artifacts/architecture.md#API & Communication Patterns (B1, B2,
Ports); #6.x Out of Scope; docs/boundary-decision-record.md (EventStore owns concurrency; Theme 4 owns
claim/queue eligibility)]

### How single-claim-wins actually works (the load-bearing mental model)

Single-claim-wins is **not** new kernel logic. It is the composition of two existing, separately-owned layers:

1. **Pure lifecycle (Works kernel — built):** `Queued → Claim = Accept(InProgress)` and `Assigned → Claim =
   Accept(InProgress)`; **every other status, including `InProgress`, rejects `Claim`** →
   `WorkItemTransitionRejected(FromStatus, "Claim")`. `Handle` is pure: it sees only the rehydrated
   `WorkItemState` and returns a `DomainResult`; it never sees a version number or a clock.
   [Source: src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs (Decide L21–77); WorkItemAggregate.cs
   (Handle(ClaimWorkItem) L153–169)]
2. **Expected-version optimistic concurrency (EventStore substrate — built):** `AggregateActor.ProcessCommandAsync`
   rehydrates state, invokes `Handle`, then `EventPersister.PersistEventsAsync` assigns the next gapless sequence
   from `AggregateMetadata.CurrentSequence` and `StateManager.SaveStateAsync()` commits **atomically under DAPR
   ETag optimistic locking**. A conflicting concurrent commit throws `InvalidOperationException`, wrapped as
   `ConcurrencyConflictException(conflictSource:"StateStore")`; the actor **retries** up to
   `MaxPersistenceConflictRetries` (default **1**) by clearing cache, rehydrating fresh state, and re-running the
   pipeline. [Source: Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs (~L442–481);
   Events/EventPersister.cs; Commands/ConcurrencyConflictException.cs; Configuration/CommandConcurrencyOptions.cs;
   Events/AggregateMetadata.cs]

**The race resolved:** two claims observe `Queued` at version `N` and each computes `WorkItemClaimed` at
sequence `N+1`. The store admits exactly one append at `N+1` (the **winner** → `InProgress`). The **loser's**
commit conflicts (its expected version `N` ≠ actual `N+1`); on retry it re-handles `ClaimWorkItem` against the
now-`InProgress` state → `Handle` returns `WorkItemTransitionRejected(InProgress, "Claim")` — an **observable
`IRejectionEvent`**. Exactly one `WorkItemClaimed`; the loser gets a domain rejection. This is what the
**deterministic** Task 2 test reproduces at the domain level (no threads) — faithful to RR-3 ("same expected
version → one commits, loser gets observable rejection event; not a thread-race"). [Source:
_bmad-output/planning-artifacts/architecture.md#Pattern Examples ("Claim race: both append at expected version N →
one commits, the other emits ClaimRejected"); #Architectural Risk & Assumption Stress-Test (RR-3, invariant 2)]

### Design Decisions and Guardrails

- **DC1 — The loser's observable rejection is `WorkItemTransitionRejected(InProgress, "Claim")`; do NOT add
  `ClaimRejected`/`ConcurrencyRejected`.** The architecture lists those two names only as *illustrative examples*
  ("Rejection events implement `IRejectionEvent` (e.g. `WorkItemTransitionRejected`, `ClaimRejected`,
  `ConcurrencyRejected`)"). The realized rejection is the existing generic `WorkItemTransitionRejected`, which is
  observable, carries the actual current status (`InProgress` ⇒ "someone got there first" — matches the
  EXPERIENCE microcopy "Someone else got there first."), and satisfies AC #2 and AC #5. Adding a new type would
  grow the **frozen catalog beyond 36** (SM-C1 "don't grow the kernel"), force golden-corpus additions, and add
  vocabulary no v1 behavior branches on (SM-C2). A future theme that must distinguish "lost a race" from "never
  claimable" can add `ConcurrencyRejected` **additively** then (no `V2`). [Source:
  _bmad-output/planning-artifacts/architecture.md#Data Model & Schema (Events catalog, e.g. list); #Counter-metrics
  SM-C1/SM-C2; _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md (voice/microcopy)]
- **DC2 — Retry-exhaustion is an infrastructure failure, not a domain rejection — and it is Story 4.5's to
  exercise.** If the substrate's conflict-retry budget exhausts under hot contention, it surfaces a failed
  `CommandProcessingResult`/`ConcurrencyConflictException` (NFR-2: infra failures are exceptions/dead-letter). With
  the default 1 retry the loser virtually always lands on the re-handle → domain-rejection path (DC1). Do **not**
  unit-test the live ETag/retry path here (it needs the Dapr actor runtime); the deterministic Task 2 test models
  the domain outcome, and the live path is proved under Aspire in Story 4.5. [Source:
  _bmad-output/planning-artifacts/architecture.md#Core Principles (event-sourcing invariants: rejections are
  events, infra failures are exceptions/dead-letter); Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/
  AggregateActor.cs (retry/exhaustion); _bmad-output/planning-artifacts/epics.md#Story 4.5]
- **DC3 — No new production code is expected in the Works kernel** (mirroring Story 4.2). The claim transition,
  the rejection, and the substrate concurrency mechanism all already exist; 4.3 *proves and guards* them. Run
  Task 1 reconciliation first and only then confirm "no production change". If reconciliation surfaces a genuine
  gap, the fix is a test (or at most a doc-aligned matrix note) — not a new command/event/value-object.
- **DC4 — Claim is unconditional in v1 (AC #4).** Any tenant Executor may claim a `Queued` item; `Handle` takes
  only `TenantId`/`WorkItemId`/`Binding` and the lifecycle cell. No eligibility filter, routing score, escalation
  ladder, authority gate, or AI decision record. `IExecutorRouter` stays an abstraction with no wired impl;
  `AuthorityLevel` stays carried-not-enforced. The Task 4 fitness test makes a regression here a build break.
  [Source: _bmad-output/planning-artifacts/epics.md#Story 4.3 (AC #4); architecture.md#Ports;
  _bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md (carried-not-enforced
  AuthorityLevel)]
- **DC5 — Duplicate-claim idempotency lives in the substrate, not the kernel.** Re-delivery of the *same* claim
  command is deduped by the actor's CausationId/offset idempotency (NFR-9/AR-11). At the domain level, a second
  claim against `InProgress` is a **rejection**, not `DomainResult.NoOp` (the lifecycle returns `NoOp` only for the
  listed terminal self-duplicates, never for `Claim`). Do not "fix" this by making duplicate claim a NoOp.
  [Source: src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs (NoOp only on terminal diagonals);
  architecture.md#Concurrency & idempotency; docs/lifecycle-transition-matrix.md#Idempotent no-op list]

### Current State (files this story reconciles, tests, or documents — read before editing)

All paths under the `Hexalith.Works` root unless noted; line numbers are approximate anchors from the current tree.

- **Command** `src/Hexalith.Works.Contracts/Commands/ClaimWorkItem.cs` —
  `[PolymorphicSerialization] public sealed partial record ClaimWorkItem(TenantId TenantId, WorkItemId WorkItemId,
  ExecutorBinding Binding);` — the single `InProgress`-entry command. `Binding` is required.
- **Event** `src/Hexalith.Works.Contracts/Events/WorkItemClaimed.cs` —
  `…(string AggregateId, long Sequence, TenantId TenantId, WorkItemId WorkItemId, ExecutorBinding Binding) :
  IEventPayload;` — captures the claimant binding (raw act).
- **Rejection** `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs` —
  `…(TenantId TenantId, WorkItemId WorkItemId, WorkItemStatus FromStatus, string AttemptedAct) : IRejectionEvent;`
  — **no `Sequence`** (returned to caller, not appended). This is the AC #2/#3 rejection.
- **State** `src/Hexalith.Works.Contracts/State/WorkItemState.cs` — `Apply(WorkItemClaimed e)` (~L126–132) →
  `Status = InProgress`, `Sequence = e.Sequence`, `ExecutorBinding = e.Binding`. `Sequence` property (~L22–27,
  private-set, starts at 0). `Apply(WorkItemTransitionRejected)` is a `CA1822`-pragma no-op (no state change).
- **Aggregate** `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` — `Handle(ClaimWorkItem, WorkItemState?)`
  (L153–169) validates `CurrentStatus(state)` via `Decide(from, Claim)`: Accept → `DomainResult.Success([new
  WorkItemClaimed(WorkItemId.Value, NextSequence(state), TenantId, WorkItemId, Binding)])`; NoOp → `DomainResult.NoOp()`
  (defensive; the matrix never returns NoOp for Claim); else `Reject(..., nameof(LifecycleAct.Claim))` →
  `DomainResult.Rejection([new WorkItemTransitionRejected(...)])`. `NextSequence(state) = (state?.Sequence ?? 0) + 1`
  (~L407–409); `CurrentStatus(null) = Unknown` (~L403–404); `Reject(...)` (~L418–419).
- **Lifecycle** `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` — `static LifecycleOutcome Decide(from,
  act, requeue=true)` (L21–77). Claim cells: `Assigned → Claim = Accept(InProgress)` (~L36); `Queued → Claim =
  Accept(InProgress)` (~L45); `Created`/`InProgress`/`Suspended`/all terminals `→ Claim = Reject`. `LifecycleAct`
  (`LifecycleAct.cs`): `Assign, Queue, Claim, Suspend, Resume, Complete, Cancel, Reject, Expire`. `LifecycleOutcome`
  /`LifecycleDecision` (`LifecycleOutcome.cs`): `Accept(target)` / `Reject` / `NoOp`.
- **DomainResult** `Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Results/DomainResult.cs` — `IsSuccess`
  (non-empty, first not rejection), `IsRejection` (non-empty, first is `IRejectionEvent`), `IsNoOp` (empty),
  `.Events`; factories `Success(events)`, `Rejection(rejectionEvents)`, `NoOp()`. **Mixed success+rejection is
  rejected at construction** — never mix.
- **Substrate concurrency (read-only; do NOT edit the submodule)** —
  `Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs` (5-step `ProcessCommandAsync`;
  ETag commit + `ConcurrencyConflictException` + retry ~L442–481); `Events/EventPersister.cs`
  (`PersistEventsAsync`; sequence from `AggregateMetadata.CurrentSequence`); `Commands/ConcurrencyConflictException.cs`;
  `Configuration/CommandConcurrencyOptions.cs` (`DefaultMaxPersistenceConflictRetries = 1`, section
  `"EventStore:CommandConcurrency"`); `Events/AggregateMetadata.cs` (`CurrentSequence`, `LastModified`, `ETag?`).
- **Docs** `docs/lifecycle-transition-matrix.md` (claim note ~L172–173: "Single-claim-wins concurrency is Story
  4.3" — finalize per Task 6); `docs/boundary-decision-record.md` (EventStore owns the concurrency mechanism ~L36;
  Theme-4 owns claim/queue eligibility ~L77–78).
- **Tests (baseline 549 green; catalog 36):**
  - `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs` — exhaustive `(status, act)` matrix `[Theory]`,
    incl. every Claim cell (`Queued/Assigned → Accept`; `Created`/`InProgress`/`Suspended`/terminals → Reject).
    **Do not duplicate** the matrix cells; add only claim-mutation/concurrency-focused assertions.
  - `tests/Hexalith.Works.UnitTests/WorkItemUniformExecutorBindingTests.cs` —
    `ClaimWorkItem_carries_the_uniform_binding_through_event_and_replay` (~L72–86) already proves the uniform
    binding flows through claim across the three executor kinds. Build on it; do not repeat.
  - `tests/Hexalith.Works.UnitTests/WorkItemHandoffTests.cs` — proves the latest binding binds the next claim
    (Story 4.2, AC #3). Reuse the pattern; don't duplicate.
  - `tests/Hexalith.Works.Testing/WorkItemStateBuilder.cs` — `InStatus(WorkItemStatus status, TenantId tenantId,
    WorkItemId workItemId, ExecutorBinding? binding = null)` replays the shortest legal event path:
    `Queued` = `WorkItemQueued` (~L52–54); `InProgress` = `WorkItemAssigned` → `WorkItemClaimed` (~L56–59).
    `DefaultBinding()` = `ExecutorBinding(PartyId("party-exec"), Channel.Mcp, AuthorityLevel.Administer)`. Use it
    to arrange `Queued`/non-claimable states.
  - `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs` — the FsCheck wiring template
    (`Gen.ArrayOf`/`Arb.ToArbitrary`/`Prop.ForAll`/`Check.One(Config.QuickThrowOnFailure, …)`) for an optional
    Task 5 claim-convergence property.
  - `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` — `Count == 36`; canonical claim/queue/assign
    samples. `SchemaEvolution/Golden/WorkItemClaimed.v1.json` + `SchemaEvolutionGoldenCorpusTests.cs` must stay
    byte-compatible (no new type → no corpus change).
  - `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` —
    `P0_WorkItemSurfaceHasNoExecutorKindSpecificHandoffOrReassignTypeAndCatalogStays36` (L366–422) is the exact
    pattern to mirror for the Task 4 claim/routing-vocabulary guard (`declarationRegex`, exclusion set,
    reflection-based `polymorphicCatalogCount.ShouldBe(36, …)`).
  - `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs` —
    `P1_EventStoreExposesConcurrencyAndProjectionRebuildSurfacesNeededByWorks` already asserts
    `ConcurrencyConflictException` + `AggregateActor` exist (the substrate surface 4.3 relies on). Keep green.

### Technical Requirements

- Keep `Handle`/`Apply` pure: no clock, RNG, I/O, Dapr, EventStore runtime, HTTP, files, timers, or generated IDs;
  IDs supplied at the command edge (Commons). `Handle(...)` returns `DomainResult` (success **or** rejection
  events, never mixed); `Apply(...)` mutates only in-memory `WorkItemState`. [Source:
  architecture.md#Process Patterns / Domain purity; project-context.md#Framework-Specific Rules]
- Keep kernel projects (`Contracts`, `Server`, `Projections`) free of LLM/routing/email/MCP/UI/security/cost
  packages and sibling implementation DTOs; reference Parties only by `PartyId`. The concurrency mechanism stays
  in the EventStore substrate — **do not** add a version/ETag/append type to Works. [Source:
  architecture.md#Structure Patterns; docs/boundary-decision-record.md]
- Evolve serialization additively only: every event/command is `[PolymorphicSerialization] sealed partial record`,
  file-scoped namespace; **no `V2` types**; the v1 catalog stays **36** and the golden corpus stays byte-compatible.
  Events carry `(AggregateId, Sequence)`; rejection events carry context, no sequence. [Source:
  architecture.md#Format Patterns / Serialization; _bmad-output/implementation-artifacts/tests/test-summary.md]
- Tests: xUnit v3 + Shouldly (no raw `Assert.*`, no FluentAssertions; NSubstitute only where a double is genuinely
  needed — none expected here); Tier-1 pure (no Dapr/Aspire/network/containers/sleeps/threads). Concurrency proof
  is **deterministic** (RR-3), reusing `WorkItemStateBuilder` and the Story 4.1 binding fixtures. [Source:
  project-context.md#Testing Rules; architecture.md#Tests; architecture.md#RR-3]
- Do not add or upgrade packages. Local pins remain authoritative (.NET SDK `10.0.301`, Dapr `1.18.2`, Aspire
  `13.4.3`, xUnit v3 `3.2.2`, Shouldly `4.3.0`). Hexalith deps stay root-submodule `ProjectReference`s — never add
  `Hexalith.*` `PackageReference`s, never edit submodule files, never init nested submodules. [Source:
  CLAUDE.md#Hexalith library references; CLAUDE.md#Submodule rules; architecture.md#Starter Template Evaluation]

### Previous Work Intelligence

- **Story 2.1** built the 9-state machine including `Queued/Assigned → Claim = Accept(InProgress)` and all reject
  cells, the `WorkItemClaimed` event, the `WorkItemTransitionRejected` rejection, and the matrix doc. Single-claim
  -wins was explicitly left to 4.3 (matrix note). Do not redefine transitions locally; if a cell ever changes,
  change `WorkItemLifecycle.cs` and the doc together. [Source:
  _bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md]
- **Story 4.1** hardened `ExecutorBinding` (rejects `AuthorityLevel.Unknown`/undefined `Channel`; every test
  binding must pass a **valid** `AuthorityLevel`: `Read`/`Contribute`/`Coordinate`/`Administer`, or construction
  throws) and proved uniform create/assign/**claim** across the three executor kinds. Reuse those fixtures for the
  concurrency test's two distinct bindings. [Source:
  _bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md]
- **Story 4.2** was the analogous **no-production-code** proof/guardrail story (FR-17/FR-18 reconcile + tests +
  fitness): it added the `…CatalogStays36` fitness test to mirror, finalized matrix edge cells, and confirmed the
  claim path "models the transition only; single-claim-wins is Story 4.3." [Source:
  _bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md]
- **Epic 3 retrospective lessons** (carry forward): (1) first passes can pass under-cover — make an explicit QA
  gap-filling pass and reconcile counts against `tests/test-summary.md` before review; (2) `dotnet test` is
  unreliable in this sandbox (named-pipe perms) — restore, Release build, then run the direct xUnit v3 binaries
  under `bin/Release/net10.0/`. (3) EventStore's own review history (CLAUDE.md) shows reviewers find ≥1 real gap
  per story — budget for review-found rework even on a "proof" story. [Source:
  _bmad-output/implementation-artifacts/epic-3-retro-2026-06-17.md; Hexalith.EventStore/CLAUDE.md#Code Review
  Process]

### Git Intelligence

Recent commits before Story 4.3 (most recent first):

- `2dd46d0 feat(story-4.2): Assign, Reassign, and Hand Off Work` — uniform handoff proof/guardrail, +21 tests
  (549 total), no production change, catalog stays 36. **This story's baseline.**
- `0f413f7 feat(story-4.1): Bind Work to a Uniform Party Executor` — uniform `ExecutorBinding` hardening + claim
  binding proof across kinds.
- `68de3f5 feat: Update documentation and project structure for Epic 3 completion`.
- `216e9e7 feat(story-3.6): Cascade terminal work through active descendants`.
- `f8856f2 feat(story-3.5): Suspend and resume on await conditions`.

Story 4.3 stays on the Contracts/Server reconciliation + test/fitness surfaces; it should need no Reactor,
Projections, or AppHost change. A pre-existing working-tree `Hexalith.Tenants` gitlink change is unrelated — leave
it untouched (do not run recursive submodule commands).

### Project Structure Notes

- No new production types expected (DC3). If any production change is warranted it is at most a doc-aligned matrix
  note; do not add claim/concurrency commands, events, value objects, or a claimable-pool projection (the pool is
  Story 4.4).
- New tests belong in existing projects: concurrency + claim behavior in
  `tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs`; the claim/routing-vocabulary +
  catalog-stays-36 guard in `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`; the
  optional convergence property in `tests/Hexalith.Works.PropertyTests/`.
- Docs live at `docs/lifecycle-transition-matrix.md` (1:1 with `WorkItemLifecycle.cs`) and
  `docs/boundary-decision-record.md`. [Source: CLAUDE.md#Submodule rules; architecture.md#Complete Project
  Directory Structure]

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.3: Claim Queued Work with Single-Claim-Wins] — story statement and the five acceptance criteria.
- [Source: _bmad-output/planning-artifacts/epics.md#Epic 4] — shared work execution scope; FR-17–20, 24, 25.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-18: Push and Pull coexist] — single claim wins (one success, loser domain-rejected, serialized by optimistic concurrency); any tenant Executor may claim; eligibility deferred to Theme 4.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-3: Concurrency] — single-writer/optimistic per item; two claims → exactly one success, rest domain-rejected; no lost updates.
- [Source: _bmad-output/planning-artifacts/architecture.md#API & Communication Patterns (B1)] — claim is a single-aggregate operation under expected-version (ETag) concurrency; claimable pool is a read projection, not a queue aggregate; loser receives a domain rejection.
- [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Risk & Assumption Stress-Test] — invariant 2 (write-path expected-version → version-conflict loser gets observable `IRejectionEvent`); RR-3 (deterministic version-conflict test, not a thread race).
- [Source: _bmad-output/planning-artifacts/architecture.md#Data Model & Schema] — frozen 14-event catalog; rejections implement `IRejectionEvent` (`WorkItemTransitionRejected`, *e.g.* `ClaimRejected`/`ConcurrencyRejected`).
- [Source: _bmad-output/planning-artifacts/architecture.md#Pattern Examples] — "Claim race: both append at expected version N → one commits, the other emits ClaimRejected."
- [Source: _bmad-output/planning-artifacts/architecture.md#AR-10 / AR-11 / NFR-9] — single-aggregate claim under expected-version; no reliance on pub/sub ordering; resume/dedup idempotency in the substrate.
- [Source: docs/lifecycle-transition-matrix.md] — the single source of truth; the `Claim` cells and the "single-claim-wins is Story 4.3" note this story finalizes.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs; Events/EventPersister.cs; Commands/ConcurrencyConflictException.cs; Configuration/CommandConcurrencyOptions.cs; Events/AggregateMetadata.cs] — the substrate's expected-version ETag append + conflict-retry pipeline that realizes single-claim-wins.
- [Source: Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Results/DomainResult.cs] — `IsSuccess`/`IsRejection`/`IsNoOp`; mixed results rejected at construction.
- [Source: _bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md] — origin of the claim transition and the matrix.
- [Source: _bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md] — uniform `ExecutorBinding`, valid-`AuthorityLevel` requirement, claim-binding proof across kinds.
- [Source: _bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md] — the analogous no-production-code proof/guardrail pattern and the `…CatalogStays36` fitness test to mirror.
- [Source: _bmad-output/implementation-artifacts/tests/test-summary.md] — authoritative baseline counts (549 green) and catalog size (36).
- [Source: Hexalith.Projects/_bmad-output/project-context.md#Critical Implementation Rules] — purity, persist-then-publish, rejection-as-event, additive serialization, file-scoped namespaces, sealed records, xUnit v3 + Shouldly.
- [Source: CLAUDE.md] — root-submodule and `ProjectReference`-not-`PackageReference` rules.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8[1m] (Claude Opus 4.8, 1M context)

### Debug Log References

- `dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — **0 warnings / 0 errors**.
- Direct xUnit v3 binaries (`bin/Release/net10.0/`): UnitTests **449/449**, IntegrationTests **80/80**,
  ArchitectureTests **31/31**, PropertyTests **2/2** (`Ok, passed 100 tests.` ×2). Total **562** green.

### Completion Notes List

- **No production code changed (DC3).** Task 1 reconciliation confirmed the claim path
  (`Queued/Assigned → Claim = Accept(InProgress)`, all else reject), the rejection
  (`WorkItemTransitionRejected(FromStatus, "Claim")`), and the EventStore-owned expected-version
  substrate all already exist exactly as specified — so single-claim-wins is *proved and guarded*, not
  re-shaped. No new event/command/rejection/value-object type; `WorkItemV1Catalog.Count` stays **36** and
  the golden corpus (incl. `WorkItemClaimed.v1.json`) is byte-unchanged.
- **Task 2/5 — deterministic single-claim-wins (AC #2/#5, RR-3).** Modelled the expected-version collision
  with **no threads/Task.Run/sleeps**: two (Task 2) and arbitrary `K ≥ 2` (Task 5 property) claims at the
  same observed version `N` all target sequence `N+1`; the winner advances to `InProgress`, every loser
  re-handles to `WorkItemTransitionRejected(InProgress, "Claim")` (the existing rejection — DC1). The live
  ETag append/retry/exhaustion path is deferred to Story 4.5 (documented on the test class).
- **Task 3 — focused AC #1 (claim emits/binds/transitions) and AC #3 (not-claimable rejection with no
  binding/status/sequence mutation across all 7 non-claimable statuses, each arranged carrying a binding).**
- **DC5 (QA pass) — added `Duplicate_claim_by_the_current_holder_of_an_in_progress_item_is_rejected_not_a_no_op`**:
  proves a duplicate claim by the *current holder* of an `InProgress` item is an observable
  `WorkItemTransitionRejected(InProgress, "Claim")`, explicitly **not** a `DomainResult.NoOp` (guards DC5 so
  "fixing" duplicate claim into a silent NoOp breaks the build). This is the 11th unit case in the file.
- **Task 4 — AC #4 guardrail** fitness test: no claim-eligibility/routing/escalation/ranking/AI-decision
  type and no `ClaimRejected`/`ConcurrencyRejected` (DC1), paired with catalog-stays-36. `IExecutorRouter`
  stays abstraction-only; `AuthorityLevel` carried-not-enforced (DC4).
- **Task 6** — finalized the matrix claim note (no cell change), added the boundary-record 4.3 note, and
  added the Story 4.3 test-summary section.
- Added `Hexalith.Works.Server` + `Hexalith.Works.Testing` `ProjectReference`s to the PropertyTests project
  for the Task 5 property (ProjectReference, not a Hexalith PackageReference; dependency direction unchanged).
- Left the unrelated `Hexalith.Tenants` gitlink change untouched; ran no recursive submodule commands.

### File List

- `tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs` — **new** (Tasks 2–3 + DC5 QA pass; +11 cases).
- `tests/Hexalith.Works.PropertyTests/WorkItemClaimConvergencePropertyTests.cs` — **new** (Task 5; +1 property).
- `tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj` — added Server + Testing ProjectReferences.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` — **+1 fitness method** (Task 4).
- `docs/lifecycle-transition-matrix.md` — finalized the claim/single-claim-wins note (no cell change) (Task 6).
- `docs/boundary-decision-record.md` — added the Story 4.3 note (Task 6).
- `_bmad-output/implementation-artifacts/tests/test-summary.md` — added the Story 4.3 section (Task 6).
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — status `ready-for-dev → in-progress → review`.
- `_bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md` — checkboxes, Dev Agent Record, Change Log, Status.

## Change Log

| Date | Change |
|------|--------|
| 2026-06-17 | Story 4.3 dev-story pass: deterministic single-claim-wins proof (unit + property), AC #1/#3 focused claim tests, AC #4 claim-eligibility/routing + catalog-stays-36 fitness guard, matrix/boundary/test-summary docs. No production code changed; catalog stays 36. +13 tests (562 green: UnitTests 449, IntegrationTests 80, ArchitectureTests 31, PropertyTests 2). Status → review. |
| 2026-06-17 | Senior Developer Review (AI) pass: re-ran restore/Release build (0W/0E) and all four xUnit v3 binaries — 562 green; verified every AC, every `[x]` task, DC3 (no `src/` change), catalog 36, golden corpus byte-unchanged. Synced stale bookkeeping (Debug Log References, File List, Completion Notes, this Change Log) from the under-reported 448/561/+12 to the actual 449/562/+13. Status → done. |

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot (AI adversarial review) · **Date:** 2026-06-17 · **Outcome:** ✅ Approve (status → done)

**Verification performed (independent re-run, not trusting the record):**

- `DOTNET_CLI_HOME=/tmp dotnet restore … && dotnet build … -c Release` → **0 warnings / 0 errors**.
- All four xUnit v3 binaries under `bin/Release/net10.0/`: UnitTests **449/449**, IntegrationTests **80/80**,
  ArchitectureTests **31/31**, PropertyTests **2/2** (`Ok, passed 100 tests.` ×2) — **562 green**.
- `git status` confirms **no `src/` change** (DC3), no golden-corpus change (`WorkItemClaimed.v1.json`
  byte-unchanged), and the `Hexalith.Tenants` gitlink left untouched.
- Cross-checked the claim path against source: `WorkItemAggregate.Handle(ClaimWorkItem)` (L153–169),
  `WorkItemLifecycle.Decide` Claim cells (`Queued/Assigned → Accept(InProgress)`; all else `R`),
  `WorkItemState.Apply(WorkItemClaimed/WorkItemTransitionRejected)`, and `WorkItemStateBuilder` — all match
  the story's claims exactly.

**AC coverage (all IMPLEMENTED):** AC #1 (`Claim_from_a_claimable_status…`, Queued + Assigned);
AC #2/#5 (`Two_claims_at_the_same_expected_version_collide…` + the `WorkItemClaimConvergencePropertyTests`
order-independent property — both deterministic, no threads, RR-3-faithful); AC #3
(`Claim_from_a_non_claimable_status…` across all 7 non-claimable statuses + DC5 duplicate-holder case);
AC #4 (`P0_WorkItemSurfaceHasNoClaimEligibilityRoutingOrConcurrencyRejectionTypeAndCatalogStays36` fitness
guard + boundary-record note). Every `[x]` task verified done.

**Findings:**

- 🟡 **M1 (fixed)** — Dev Agent Record / Change Log reported stale counts (448 / 561 / +12); the actual suite
  and `test-summary.md`'s QA-pass table are 449 / 562 / +13. Synced.
- 🟡 **M2 (fixed)** — File List annotated the new unit file "+10 cases"; it has 11 (the DC5
  duplicate-claim `[Fact]` was added by the QA pass). Corrected and noted in Completion Notes.
- 🟢 **L1 (accepted)** — `NonClaimableStateCarryingBinding` overlaps `WorkItemHandoffTests`'
  `TerminalStateCarryingBinding` / `WorkItemStateBuilder.InStatus`; consolidation into the shared Testing
  builder is a future cleanup. Not changed (shared-test-infra churn out of scope for a guardrail story).
- 🟢 **L2 (accepted, by design)** — the new fitness method mirrors the 4.2 fitness scaffolding **as Task 4
  explicitly instructs**; refactoring would touch already-green code. Left intentionally duplicated.

No Critical or High findings. DC1 (`WorkItemTransitionRejected`, no new `ClaimRejected`/`ConcurrencyRejected`)
and DC3/DC4 (no production change, claim unconditional, `AuthorityLevel` carried-not-enforced) all hold.
