---
baseline_commit: 0f413f7
---

# Story 4.2: Assign, Reassign, and Hand Off Work

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a coordinator,
I want to assign and hand off work through one operation,
so that moving work between a bot, a colleague, or an external party is symmetric and auditable.

## Acceptance Criteria

1. **Given** a Work Item can accept assignment
   **When** `AssignWorkItem` is handled with an `ExecutorBinding`
   **Then** `WorkItemAssigned` is emitted
   **And** replayed state contains the supplied binding.

2. **Given** a Work Item is already assigned
   **When** `AssignWorkItem` is handled with a different `ExecutorBinding`
   **Then** the same command path handles reassignment
   **And** no executor-kind-specific handoff command is required.

3. **Given** work is handed off from a human executor to a system executor or back
   **When** events are replayed
   **Then** the latest binding is authoritative for future executor acts
   **And** the event history preserves each handoff as raw-act evidence.

4. **Given** an assigned Work Item is returned to the shared pool
   **When** it is requeued
   **Then** `WorkItemQueued` is emitted
   **And** the item becomes claimable according to lifecycle rules.

5. **Given** assignment is attempted from a terminal state
   **When** the command is handled
   **Then** the command emits an `IRejectionEvent`
   **And** no binding mutation occurs after terminal closure.

## Tasks / Subtasks

- [x] **Task 1 — Reconcile the existing assign / requeue / claim / reject surface before changing code (AC: #1–#5)**
  - [x] Read `src/Hexalith.Works.Contracts/Commands/AssignWorkItem.cs`, `QueueWorkItem.cs`,
    `ClaimWorkItem.cs`; `src/Hexalith.Works.Contracts/Events/WorkItemAssigned.cs`, `WorkItemQueued.cs`,
    `WorkItemClaimed.cs`; `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs`;
    `src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs`;
    `src/Hexalith.Works.Contracts/State/WorkItemState.cs` (`Apply(WorkItemAssigned)` ~L111–117,
    `Apply(WorkItemQueued)` ~L119–124, `Apply(WorkItemClaimed)` ~L126–132, rejection `Apply` no-ops);
    `src/Hexalith.Works.Server/Aggregates/WorkItemLifecycle.cs` (the pure `Decide` table) and
    `WorkItemAggregate.cs` (`Handle(AssignWorkItem)` ~L118–134, `Handle(QueueWorkItem)` ~L136–151,
    `Handle(ClaimWorkItem)` ~L153–169, the `Reject(...)` helper ~L419); and `docs/lifecycle-transition-matrix.md`.
  - [x] Confirm the uniform path: `AssignWorkItem(TenantId, WorkItemId, ExecutorBinding Binding)` is the
    single bind/rebind/handoff command; `QueueWorkItem(TenantId, WorkItemId)` is the single requeue path;
    `ClaimWorkItem(TenantId, WorkItemId, ExecutorBinding Binding)` is the single `InProgress`-entry path.
  - [x] Confirm there is **no** executor-kind-specific handoff command/event (`HandoffToBot`,
    `ReassignToHuman`, `AssignToExternalParty`, `WorkItemHandedOff`, etc.) and that `ExecutorBinding`
    carries no kind discriminator — reassignment and handoff differ only by `ExecutorBinding` field values.
  - [x] Confirm the durable wire surface is frozen: `WorkItemV1Catalog.Count` is **36** (14 events / 14
    commands / 8 rejections) and the golden corpus under
    `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/` is unchanged. Story 4.2 adds **no** new event,
    command, or rejection type.

- [x] **Task 2 — Finalize the deferred `InProgress`-reassignment edge cell in the matrix (AC: #2, #5)**
  - [x] Story 2.1 flagged only **reassign of `InProgress` (Assign)** as "recommend R; hand-off-while-active is
    Story 4.2's to refine against this matrix" — that note lives in the historical
    `_bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md` artifact (line ~197); do **not**
    edit it. `InProgress → Queue = R` was already firmly decided in 2.1, not deferred. Confirm `WorkItemLifecycle.cs`
    already returns `Reject` for both `InProgress → Assign` and `InProgress → Queue` (no code change expected).
  - [x] **Decision to encode (do not silently add an active-handoff path):** v1 hand-off happens while the item
    is `Assigned` (rebind) or via `Assigned → Queue → (re)Claim` by a new executor. Active (`InProgress`/`Suspended`)
    work is not directly reassigned in v1; to change hands it is completed/cancelled or claimed by the new
    executor after a requeue. Adding an `InProgress → Assigned` transition is **out of scope** (matrix is the
    single source of truth; later stories must not choose transitions locally).
  - [x] `docs/lifecycle-transition-matrix.md` already shows the finalized cells (`InProgress`/`Suspended` rows:
    `Assign = R`, `Queue = R`) and carries **no** pending "to refine" note to remove. **Add** a one-line
    finalization note there recording D4 (active work is not directly reassigned/requeued in v1; hand-off is via
    `Assigned` rebind or `Assigned → Queue → (re)Claim`), keeping the cells identical to `WorkItemLifecycle.cs`.
    `LifecycleTransitionMatrixDocTests.TransitionMatrixDoc_existsAndEnumeratesEveryStatusAndLifecycleCommand` only
    checks that all status/command names plus `NoOp`/`WorkItemTransitionRejected` are present (not cell content), so
    it must stay green.

- [x] **Task 3 — Lock requeue binding semantics in replayed state (AC: #3, #4)**
  - [x] `Apply(WorkItemQueued)` sets `Status = Queued` and **does not clear** `ExecutorBinding` today. Keep this
    behavior: the event stream is the raw-act truth, queueing is not an executor-binding act, and "who currently
    owns a Queued item" is a read-model/projection concern owned by Story 4.4 — not an aggregate-state mutation.
    Do **not** add binding-clearing to `Apply(WorkItemQueued)` and do **not** add a binding field to `WorkItemQueued`.
  - [x] Add a focused unit test that locks this decision deliberately: after `Assigned(bindingA) → Queue`, replayed
    `WorkItemState.Status == Queued`, `Sequence` advanced by exactly 1, and `ExecutorBinding` still equals `bindingA`
    (last executor act), so the choice is intentional and a future change is a visible test break — not an accident.

- [x] **Task 4 — Prove assign / reassign / handoff symmetry and raw-act history (AC: #1, #2, #3)**
  - [x] Add a new unit test file (e.g. `tests/Hexalith.Works.UnitTests/WorkItemHandoffTests.cs`) — do **not**
    duplicate Story 4.1's `WorkItemUniformExecutorBindingTests` / `UniformExecutorBindingLifecycleFlowTests`;
    target the 4.2-specific gaps below.
  - [x] **AC#1:** `AssignWorkItem` from an assignable status (`Created`, `Queued`) emits exactly one
    `WorkItemAssigned` carrying the supplied `ExecutorBinding` at `Sequence + 1`; replayed `WorkItemState`
    has `Status == Assigned` and `ExecutorBinding` equal to the supplied binding.
  - [x] **AC#2:** a second `AssignWorkItem` with a **different** binding from `Assigned` is accepted through the
    same handler (`Assigned → Assigned` rebind), emits a fresh `WorkItemAssigned` (a distinct raw act, not a NoOp),
    and replay makes the **second** binding authoritative. Prove this with a `[Theory]` across the three
    representative executors so reassignment is symmetric (system-agent `Channel.Mcp`/`Chatbot`, internal-user
    `Channel.Cli`/`Mcp`, external-party `Channel.Email`) and uses only different field values — no kind branch.
    Every test binding must pass a **valid** `AuthorityLevel` (`Read`/`Contribute`/`Coordinate`/`Administer`):
    `ExecutorBinding` rejects `AuthorityLevel.Unknown` and undefined enum casts in its constructor (Story 4.1
    hardening), so an invalid authority throws at construction, not at `Handle`.
  - [x] **AC#3 (the novel assertion):** drive a human → system → human handoff chain and assert the **ordered event
    history** preserves each handoff as raw-act evidence: three contiguous `WorkItemAssigned` events at consecutive
    sequences, each with its own binding, in order — not collapsed. Then assert the **latest** binding is
    authoritative for the next executor act (e.g. the following `ClaimWorkItem`/`WorkItemClaimed` binds the
    most-recent party). Cover the human↔system direction both ways per FR-17.

- [x] **Task 5 — Prove requeue-to-claimable and terminal-state assignment rejection (AC: #4, #5)**
  - [x] **AC#4:** full path `Assigned(bindingA) → QueueWorkItem → WorkItemQueued (Status Queued) → ClaimWorkItem(bindingB)
    → WorkItemClaimed (Status InProgress)`. Assert `WorkItemQueued` is emitted on requeue, the item is then
    claimable per the lifecycle table, and a **different** executor (`bindingB`) can claim it (proving "returned to
    the shared pool"). Note for the dev: this is the `QueueWorkItem`/`WorkItemQueued` requeue path — distinct from
    Story 2.5's `RejectWorkItem(Requeue: true) → WorkItemRejected` decline-and-rest-at-`Queued` path; AC#4 is the
    former.
  - [x] **AC#5:** for **each** terminal status (`Completed`, `Cancelled`, `Rejected`, `Expired`), `AssignWorkItem`
    returns a rejection: `DomainResult` is a rejection carrying `WorkItemTransitionRejected(TenantId, WorkItemId,
    FromStatus = <terminal>, AttemptedAct = "Assign")`, **no** `WorkItemAssigned` is emitted, and applying the result
    leaves `WorkItemState.Status`, `Sequence`, and `ExecutorBinding` **unchanged** (no binding mutation, no sequence
    burn — `Apply(WorkItemTransitionRejected)` is a no-op). Use a `[Theory]` over the four terminals. Also assert
    `QueueWorkItem` from each terminal is rejected (no binding/status change) so the shared-pool path can't reopen
    a closed item.

- [x] **Task 6 — Architecture-fitness guardrails for one-operation handoff (AC: #2, #5)**
  - [x] Add a fitness method (mirror `ScaffoldGovernanceTests.P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority`,
    living on `ScaffoldGovernanceTests`) asserting the v1 surface introduces **no** executor-kind-specific
    handoff/reassign command or event. Match on **type declarations** (`record`/`class`/`enum` whose name matches
    `*HandoffTo*`, `*ReassignTo*`, `*AssignTo<Kind>*`, `*HandedOff*`, `Unassign*`, `ReturnToPool*`) — not raw
    substrings, since `AssignWorkItem`, `LifecycleAct.Reject`, and XML comments legitimately contain "Assign"/"Reject".
    Reuse the existing exclusion set (`bin`/`obj`, `*.g.cs`, `*Assembly.cs`, value-object definition files). Pair it
    with an assertion that `WorkItemV1Catalog.Count == 36`. The uniform `AssignWorkItem`/`QueueWorkItem`/`ClaimWorkItem`
    trio is the only assignment vocabulary.
  - [x] Preserve the existing guardrails unchanged and green: `ScaffoldGovernanceTests`
    (`P0_WorkItemDomainDoesNotBranchOnExecutorKindChannelOrAuthority`, `P0_ScaffoldContainsOnlyTheV1ProjectSet`,
    `P0_KernelProjectsStayInfrastructureFree`), `DependencyDirectionTests`, `BoundaryDecisionRecordTests`, and
    `LifecycleTransitionMatrixDocTests.TransitionMatrixDoc_existsAndEnumeratesEveryStatusAndLifecycleCommand` (note:
    it checks name/string presence in the matrix doc, **not** cell-by-cell code↔doc parity). Do not relax their
    expected reference/term lists to make 4.2 pass.
  - [x] Do not introduce an executor router, eligibility filter, escalation ladder, authority gate, routing score,
    LLM/NL parsing, MCP/chatbot/email adapter, UI type, signed link, or cost-governance package. Single-claim-wins
    concurrency (Story 4.3), the what's-next queue projection/query (Story 4.4), the Aspire pipeline (Story 4.5),
    and reminder/reactor recovery (Story 4.6) remain out of scope.

- [x] **Task 7 — Documentation and story bookkeeping (AC: #1–#5)**
  - [x] `docs/lifecycle-transition-matrix.md` — apply the Task 2 finalization (edge-cell decision + rationale) and,
    if helpful, a one-line note that `QueueWorkItem`/`WorkItemQueued` is the requeue path and the queued item retains
    its last binding in state while ownership presentation is a Story 4.4 projection concern. Keep it 1:1 with code.
  - [x] `docs/boundary-decision-record.md` — update only if wording drifts; it already records that Works references
    Parties by `PartyId`, carries-not-enforces `AuthorityLevel`, and has no executor-kind discriminator. Optionally
    add a one-line 4.2 note: assign/reassign/handoff/requeue/claim are one uniform vocabulary; handoff is symmetric
    and auditable through the raw-act event history.
  - [x] `_bmad-output/implementation-artifacts/tests/test-summary.md` — add a Story 4.2 section with verification
    commands, before/after counts, files changed, gaps closed, and not-applicable runtime/UI surfaces (mirror the
    Story 4.1 entry's structure).

- [x] **Task 8 — Verify the slice (AC: #1–#5)**
  - [x] Baseline is the Story 4.1 final of **528** green tests: UnitTests 419, IntegrationTests 79,
    ArchitectureTests 29, PropertyTests 1.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`.
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — require
    **0 warnings / 0 errors**.
  - [x] Run the direct xUnit v3 binaries after the Release build (the reliable sandbox path — `dotnet test` is
    blocked by Microsoft.Testing.Platform named-pipe permissions):
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.
  - [x] Confirm `WorkItemV1Catalog.Count` is still **36** and the golden corpus is unchanged. Do not run recursive
    submodule commands.

## Dev Notes

### Scope Boundary (read first — prevents over-build)

Story 4.2 is a **behavioral-proof, edge-cell-finalization, and guardrail** story for **FR-17** (bind/reassign/handoff
via one uniform operation) and **FR-18** (push/pull coexist; requeue re-emits `WorkItemQueued`). The lifecycle
mechanics it asserts were **already built**: Story 2.1 created `AssignWorkItem`/`WorkItemAssigned`,
`QueueWorkItem`/`WorkItemQueued`, `ClaimWorkItem`/`WorkItemClaimed`, the `WorkItemTransitionRejected` rejection, and the
pure `WorkItemLifecycle.Decide` transition table; Story 4.1 hardened the uniform `ExecutorBinding` and proved
reassignment-latest-wins and uniform create/assign/claim across the three executor kinds. **Do not re-build or
re-shape any of that.** [Source: _bmad-output/planning-artifacts/epics.md#Story 4.2: Assign, Reassign, and Hand Off Work;
_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-17; #FR-18; _bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md;
_bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md]

**In scope:** reconcile the existing assign/requeue/claim/reject surface; finalize the `InProgress`-reassignment edge
cell Story 2.1 deferred to 4.2 (keep `Reject`, document the decision); lock the requeue binding-in-state semantics with
a test; add focused unit + integration tests proving the 4.2-specific gaps — ordered raw-act handoff history, the full
requeue→reclaim-by-a-different-executor path, and terminal-state assignment rejection with no binding mutation / no
sequence burn across all four terminals; a fitness guard that no executor-kind-specific handoff command/event exists and
the catalog stays 36; documentation and test-summary updates.

**Out of scope (deferred — do not implement here):** single-claim-wins / expected-version concurrency (Story 4.3); the
"what's next" queue projection and query (Story 4.4); the Aspire command/event pipeline proof (Story 4.5);
reminder/reactor runtime recovery (Story 4.6); production UI, MCP/chatbot/email adapters, executor routing, eligibility
filtering, escalation ladders, `AuthorityLevel` enforcement, signed links, security hardening, LLM/NL parsing, and cost
governance. v1 claim is unconditional — any Executor of the tenant may claim a queued item; eligibility is a Theme-4
routing concern. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-18 (eligibility assumption);
#6.2 Out of Scope for MVP; _bmad-output/planning-artifacts/epics.md#Epic 4]

### Current State (files this story reconciles, tests, or documents — read before editing)

All paths under `Hexalith.Works` root; line numbers are approximate anchors from the current tree.

- **Commands** `src/Hexalith.Works.Contracts/Commands/`:
  - `AssignWorkItem.cs` — `[PolymorphicSerialization] public sealed partial record AssignWorkItem(TenantId TenantId,
    WorkItemId WorkItemId, ExecutorBinding Binding);` — the single bind/rebind/handoff command; `Binding` is required.
  - `QueueWorkItem.cs` — `[PolymorphicSerialization] public sealed partial record QueueWorkItem(TenantId TenantId,
    WorkItemId WorkItemId);` — the single requeue/return-to-pool command; **no** binding field.
  - `ClaimWorkItem.cs` — `…ClaimWorkItem(TenantId, WorkItemId, ExecutorBinding Binding);` — the only `InProgress`-entry
    command; single-claim-wins is Story 4.3 (this models the transition only).
  - **No** unassign / return-to-pool / handoff command exists, and none should be added.
- **Events** `src/Hexalith.Works.Contracts/Events/`:
  - `WorkItemAssigned.cs` — `…(string AggregateId, long Sequence, TenantId TenantId, WorkItemId WorkItemId,
    ExecutorBinding Binding) : IEventPayload;` — carries the binding (raw-act evidence for AC#3).
  - `WorkItemQueued.cs` — `…(string AggregateId, long Sequence, TenantId TenantId, WorkItemId WorkItemId) : IEventPayload;`
    — **no** binding field; marks every queue entry (from `Created` or `Assigned`) per FR-18.
  - `WorkItemClaimed.cs` — `…(…, ExecutorBinding Binding) : IEventPayload;` — captures the claimant binding.
  - `Events/Rejections/WorkItemTransitionRejected.cs` — `…(TenantId TenantId, WorkItemId WorkItemId, WorkItemStatus
    FromStatus, string AttemptedAct) : IRejectionEvent;` — **no `Sequence`** (returned to caller, not appended). This is
    the `IRejectionEvent` for AC#5.
- **Status enum** `src/Hexalith.Works.Contracts/ValueObjects/WorkItemStatus.cs` — `Unknown=0, Created=1, Assigned=2,
  Queued=3, InProgress=4, Suspended=5, Completed=6, Cancelled=7, Rejected=8, Expired=9` (string-serialized). Terminal =
  `Completed`/`Cancelled`/`Rejected`/`Expired`.
- **State** `src/Hexalith.Works.Contracts/State/WorkItemState.cs` — properties include `Status` and nullable
  `ExecutorBinding` (both private-set), and `Sequence`. `Apply(WorkItemAssigned)` → `Status=Assigned`,
  `ExecutorBinding=e.Binding`, `Sequence=e.Sequence`. `Apply(WorkItemQueued)` → `Status=Queued`, `Sequence=e.Sequence`,
  **leaves `ExecutorBinding` untouched** (see Decision D2). `Apply(WorkItemClaimed)` → `Status=InProgress`,
  `ExecutorBinding=e.Binding`. `Apply(WorkItemTransitionRejected)` is a `CA1822`-pragma no-op (no state change).
- **Aggregate** `src/Hexalith.Works.Server/Aggregates/`:
  - `WorkItemLifecycle.cs` — pure `static LifecycleOutcome Decide(WorkItemStatus from, LifecycleAct act, bool requeue=true)`.
    Relevant cells: `Created`→Assign=`→Assigned`, Queue=`→Queued`; `Assigned`→Assign=`→Assigned (rebind)`,
    Queue=`→Queued (requeue)`, Claim=`→InProgress`; `Queued`→Assign=`→Assigned`, Claim=`→InProgress`;
    `InProgress`/`Suspended`→Assign=`R`, Queue=`R`; all terminals→Assign/Queue=`R` (except the diagonal duplicate-terminal
    `NoOp`). `Unknown`/null state rejects everything.
  - `WorkItemAggregate.cs` — `Handle(AssignWorkItem, WorkItemState?)` validates `CurrentStatus(state)` via
    `Decide(from, Assign)`; Accept → `DomainResult.Success([new WorkItemAssigned(WorkItemId.Value, NextSequence(state),
    TenantId, WorkItemId, Binding)])`; else `Reject(TenantId, WorkItemId, from, nameof(LifecycleAct.Assign))` →
    `DomainResult.Rejection([new WorkItemTransitionRejected(...)])`. `Handle(QueueWorkItem, …)` mirrors it for `Queue`.
    `NextSequence(state) = (state?.Sequence ?? 0) + 1`; `CurrentStatus(null) = Unknown`.
- **Docs** `docs/lifecycle-transition-matrix.md` — the single source of truth that mirrors `WorkItemLifecycle.cs` 1:1
  by convention; `LifecycleTransitionMatrixDocTests` checks only that the doc enumerates every status/command name
  (string presence), **not** cell-by-cell parity — keeping the doc honest is a discipline, not an automated gate.
  `docs/boundary-decision-record.md` — Parties boundary + carried-not-enforced `AuthorityLevel`.
- **Tests** (baseline 528 green; catalog 36):
  - `tests/Hexalith.Works.UnitTests/WorkItemLifecycleTests.cs` — exhaustive `(status, act)` matrix `[Theory]`, plus
    `Assigned_and_queued_interchange_and_both_claim_into_progress` and
    `Terminal_status_rejects_other_commands_but_no_ops_the_listed_duplicate`. **Do not duplicate** matrix cells; add only
    the 4.2-specific behavioral assertions (ordered history, requeue→reclaim-by-new-executor, no-mutation-on-terminal-reject).
  - `tests/Hexalith.Works.UnitTests/WorkItemUniformExecutorBindingTests.cs` and
    `tests/Hexalith.Works.IntegrationTests/UniformExecutorBindingLifecycleFlowTests.cs` /
    `UniformExecutorBindingSerializationTests.cs` (Story 4.1) — already prove uniform create/assign/claim and
    reassignment-latest-wins across the three executors and serialization round-trip. Build on these; do not repeat.
  - `tests/Hexalith.Works.Testing/WorkItemStateBuilder.cs` — `InStatus(status, tenantId, workItemId, binding?)` arranges
    any of the 9 statuses by replaying the shortest legal event path. Use it to arrange `Assigned`/`Queued`/terminal states.
  - `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` — `Count = 36`; canonical samples of the assign/queue/claim
    commands and events. `SchemaEvolution/` golden JSON (incl. `WorkItemAssigned.v1.json`, `WorkItemQueued.v1.json`) must
    stay byte-compatible.
  - Fitness: `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`, `DependencyDirectionTests`,
    `BoundaryDecisionRecordTests`, the matrix-doc-sync test.

### Design Decisions and Guardrails

- **D1 — One uniform operation; zero branching on kind.** Assign, reassign, and human↔system handoff are the same
  `AssignWorkItem` command; the only variation is `ExecutorBinding` field values (`PartyId`, `Channel`, `AuthorityLevel`).
  No `HandoffToBot`/`ReassignToHuman`/kind discriminator — enforced by fitness test. Handoff is symmetric in both
  directions. [Source: prd.md#FR-17; architecture.md#Communication Patterns (executor binding); ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Component Patterns ("Hand off…" one action, identical for bot/human/external); SM-3/SM-5]
- **D2 — Requeue does not mutate the in-state binding.** `WorkItemQueued` carries no binding and `Apply(WorkItemQueued)`
  leaves `WorkItemState.ExecutorBinding` at its last value. The event stream is the raw-act truth; queueing is not an
  executor-binding act; "current owner of a Queued item" is a read-model/projection concern owned by **Story 4.4**, not
  an aggregate-state mutation. Keep this; lock it with a test (Task 3). Do not add a binding field to `WorkItemQueued`
  or clear the binding on requeue. [Source: prd.md#FR-18 (requeue re-emits `WorkItemQueued`); architecture.md#B1 (claimable pool is a read projection); _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/review-readiness.md#1.2]
- **D3 — Reassignment emits a fresh raw act (not a NoOp).** `Assigned → Assign` is `Accept(Assigned)`, so a second
  `AssignWorkItem` — even to the same binding — emits a new `WorkItemAssigned` at the next sequence. This is intentional:
  the ordered event history is the audit trail and "preserves each handoff as raw-act evidence" (AC#3). The `NoOp` arm in
  `Handle(AssignWorkItem)` is defensive; the matrix never returns `NoOp` for `Assign`. [Source: docs/lifecycle-transition-matrix.md#Transition matrix; architecture.md#Format Patterns (event payload = the raw act)]
- **D4 — `InProgress`-reassignment stays `Reject` (finalizing Story 2.1's deferred edge cell).** Active
  (`InProgress`/`Suspended`) work is not directly reassigned or requeued in v1. Handoff happens while `Assigned` (rebind)
  or via `Assigned → Queue → (re)Claim` by the new executor. Do not add an `InProgress → Assigned` transition.
  Story 2.1 deferred only **reassign of `InProgress` (Assign)** — `InProgress → Queue` was already decided `R`. The
  "Story 4.2's to refine" note lives in the historical 2.1 artifact (do not edit it); the matrix doc cells are already
  `R`, so record D4 as an **added** finalization note there rather than hunting for a pending note to remove. [Source: _bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md#Recommended Transition Matrix (edge-cell note, line ~197); docs/lifecycle-transition-matrix.md]
- **D5 — Terminal closure is final and immutable.** From any terminal status, `AssignWorkItem` (and `QueueWorkItem`)
  returns `WorkItemTransitionRejected(FromStatus=<terminal>, AttemptedAct="Assign"|"Queue")`, emits no success event, and
  applies as a no-op — `Status`, `Sequence`, and `ExecutorBinding` are unchanged (AC#5). Rejections are expected domain
  outcomes (events implementing `IRejectionEvent`), never exceptions. [Source: architecture.md#Format Patterns (DomainResult never mixes success/rejection); project-context.md#Critical Don't-Miss Rules; docs/lifecycle-transition-matrix.md#Transition matrix (terminal rows)]
- **D6 — AC#4 is the `QueueWorkItem` path, not the reject path.** "Returned to the shared pool → requeued →
  `WorkItemQueued`" is `Assigned → QueueWorkItem → WorkItemQueued`. This is distinct from Story 2.5's
  `RejectWorkItem(Requeue: true) → WorkItemRejected` (a bound executor *declines*, raw act `WorkItemRejected`, then rests at
  `Queued`). Use `QueueWorkItem` for AC#4. [Source: docs/lifecycle-transition-matrix.md#Lifecycle commands → events; #FR-10/#FR-18]

### Technical Requirements

- Keep `Handle`/`Apply` pure: no clock, RNG, I/O, Dapr, EventStore runtime, HTTP, files, timers, or generated IDs; IDs are
  supplied at the command edge (Commons). Aggregate `Handle(...)` returns `DomainResult` (success events or rejection
  events); `Apply(...)` mutates only in-memory `WorkItemState`. [Source: architecture.md#Process Patterns; project-context.md#Framework-Specific Rules]
- Keep the kernel projects (`Contracts`, `Server`, `Projections`) free of LLM/routing/email/MCP/UI/security/cost packages
  and sibling implementation DTOs. Reference Parties only by `PartyId` (a Works-owned reference value object); never copy
  party names, contact channels, or display metadata into Works. [Source: architecture.md#Structure Patterns; docs/boundary-decision-record.md]
- Evolve serialization additively only: every event/command is `[PolymorphicSerialization] sealed partial record` with a
  file-scoped namespace; **no `V2` types**; the v1 catalog stays 36 and the golden corpus stays byte-compatible. Events
  carry `(AggregateId, Sequence)`; rejection events carry context, no sequence. [Source: architecture.md#Format Patterns; #Serialization; _bmad-output/implementation-artifacts/tests/test-summary.md (catalog 36)]
- Tests: xUnit v3 + Shouldly (no raw `Assert.*`, Moq, or FluentAssertions); Tier-1 pure (no Dapr/Aspire/network/containers/sleeps);
  reuse `WorkItemStateBuilder` and the Story 4.1 binding fixtures before inventing new doubles. [Source: project-context.md#Testing Rules; architecture.md#Tests]
- Do not add or upgrade packages. Local pins remain authoritative (.NET SDK `10.0.301`, Dapr `1.18.2`, Aspire `13.4.3`,
  xUnit v3 `3.2.2`, Shouldly `4.3.0`). Hexalith dependencies stay root-submodule `ProjectReference`s — never add
  `Hexalith.*` `PackageReference`s. [Source: CLAUDE.md#Hexalith library references; Directory.Packages.props; architecture.md#Starter Template Evaluation]

### Previous Work Intelligence

- **Story 2.1** built the 9-state machine and the `Assign`/`Queue`/`Claim`/`Reject` transitions and events, and the pure
  `WorkItemLifecycle.Decide` table + `docs/lifecycle-transition-matrix.md`. It explicitly left the `InProgress + Assign`
  edge cell "to refine against this matrix" for Story 4.2 — D4 finalizes it. Do not redefine transitions locally; change
  the code table and the doc together. [Source: _bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md#Recommended Transition Matrix; #Decisions]
- **Story 2.5** owns `RejectWorkItem`'s requeue-vs-terminal behavior (`WorkItemRejected`, `Requeue:true` rests at
  `Queued`). Keep AC#4's requeue on the `QueueWorkItem` path (D6); do not conflate the two. [Source: _bmad-output/implementation-artifacts/2-5-complete-cancel-reject-and-expire-work.md]
- **Story 4.1** hardened `ExecutorBinding` (rejects `AuthorityLevel.Unknown`/undefined), added the
  `WorkItemExecutorBindingView` read model, and proved uniform create/assign/claim + reassignment-latest-wins across the
  three executors and serialization round-trip (+47 tests; 528 total). Build on `WorkItemUniformExecutorBindingTests`,
  `UniformExecutorBindingLifecycleFlowTests`, and the shape-lock/fitness tests; do not duplicate them. [Source: _bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md; _bmad-output/implementation-artifacts/tests/test-summary.md]
- **Epic 3 retrospective lessons** (carry forward): (1) first implementation passes can pass under-cover — make an explicit
  QA gap-filling pass and reconcile counts against `tests/test-summary.md` before review; (2) `dotnet test` is unreliable in
  this sandbox (named-pipe perms) — restore, Release build, then run the direct xUnit v3 binaries under `bin/Release/net10.0/`.
  [Source: _bmad-output/implementation-artifacts/epic-3-retro-2026-06-17.md#Action Items; #What Was Challenging]

### Git Intelligence

Recent commits before Story 4.2 (most recent first):

- `0f413f7 feat(story-4.1): Bind Work to a Uniform Party Executor` — uniform `ExecutorBinding` hardening, read-model view,
  zero-kind-branch fitness test, +47 tests (528 total). **This story's baseline.**
- `68de3f5 feat: Update documentation and project structure for Epic 3 completion` — docs/structure refresh after Epic 3.
- `216e9e7 feat(story-3.6): Cascade terminal work through active descendants` — pure reactor cascade translator + tests.
- `f8856f2 feat(story-3.5): Suspend and resume on await conditions` — await-condition resume semantics.
- `61ec4c5 feat(story-3.4): Preserve heterogeneous unit subtotals` — roll-up unit safety + diagnostics.

Story 4.2 stays on the Contracts/Server/test surfaces touched by 4.1 and 2.1; it should not need the Reactor or
Projections runtime work. A pre-existing working-tree `Hexalith.Tenants` submodule gitlink change is unrelated to this
story — leave it untouched (do not run recursive submodule commands).

### Project Structure Notes

- No new production types are expected. If any production change is warranted it is at most a doc-aligned matrix edit; do
  not add commands/events/value objects for assignment vocabulary.
- New tests belong in the existing projects: behavioral unit tests in `tests/Hexalith.Works.UnitTests/` (e.g. a new
  `WorkItemHandoffTests.cs`); handoff-chain serialization in `tests/Hexalith.Works.IntegrationTests/`; the
  no-kind-handoff-command / catalog-count guard in `tests/Hexalith.Works.ArchitectureTests/FitnessTests/`.
- Docs live at `docs/lifecycle-transition-matrix.md` (1:1 with `WorkItemLifecycle.cs`) and `docs/boundary-decision-record.md`.
- Do not modify sibling submodule files and do not initialize nested submodules. [Source: CLAUDE.md#Submodule rules; architecture.md#Complete Project Directory Structure]

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.2: Assign, Reassign, and Hand Off Work] — story statement and the five acceptance criteria.
- [Source: _bmad-output/planning-artifacts/epics.md#Epic 4] — shared work execution scope and FR coverage (FR-17–20, 24, 25).
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-17: Bind, reassign, and hand off via one uniform operation] — identical command, human↔AI symmetry, zero kind branch.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-18: Push and Pull coexist] — requeue re-emits `WorkItemQueued` (marks every queue entry); v1 claim unconditional; single-claim-wins deferred to §9/Story 4.3.
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/review-readiness.md#1.2] — the `WorkItemQueued` overload is intentional (one event for all queue entries), informing D2.
- [Source: _bmad-output/planning-artifacts/architecture.md#Communication Patterns] — assign/reassign/handoff/claim use the identical command path; zero branching on executor kind.
- [Source: _bmad-output/planning-artifacts/architecture.md#Format Patterns] — event payload = the raw act; `(AggregateId, Sequence)`; `DomainResult` never mixes success/rejection; rejections implement `IRejectionEvent`.
- [Source: _bmad-output/planning-artifacts/architecture.md#Lifecycle State Machine & Domain Events] — 9 statuses; illegal transitions are domain rejections, not exceptions; frozen v1 catalog, additively extensible.
- [Source: docs/lifecycle-transition-matrix.md] — the single source of truth for every legal/illegal/idempotent transition; `Assigned↔Queued`, terminal rejection, and the edge-cell note this story finalizes.
- [Source: _bmad-output/implementation-artifacts/2-1-define-the-lifecycle-state-machine.md] — origin of the lifecycle commands/events and the deferred `InProgress`-reassignment edge cell.
- [Source: _bmad-output/implementation-artifacts/4-1-bind-work-to-a-uniform-party-executor.md] — uniform `ExecutorBinding`, reassignment-latest-wins, fitness guard, and the test files to build on.
- [Source: _bmad-output/implementation-artifacts/tests/test-summary.md] — authoritative baseline counts (528 green) and catalog size (36).
- [Source: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Component Patterns; DESIGN.md#Components] — "Hand off…" is one symmetric action, identical Party chip for bot/human/external.
- [Source: Hexalith.Projects/_bmad-output/project-context.md#Critical Implementation Rules] — purity, persist-then-publish, rejection-as-event, additive serialization, file-scoped namespaces, sealed records, xUnit v3 + Shouldly.
- [Source: CLAUDE.md] — root-submodule and `ProjectReference`-not-`PackageReference` rules.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (1M context) — `claude-opus-4-8[1m]`, `bmad-dev-story` workflow.

### Debug Log References

- `dotnet restore Hexalith.Works.slnx` — passed.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore` — **0 warnings / 0 errors**.
- Direct xUnit v3 binaries (the reliable sandbox path; `dotnet test` is blocked by Microsoft.Testing.Platform
  named-pipe permissions): UnitTests **436/436**, IntegrationTests **80/80**, ArchitectureTests **30/30**,
  PropertyTests **1/1** (`Ok, passed 100 tests.`). Total **547** green (was 528).
- QA gap-fill pass (`bmad-qa-generate-e2e-tests`): rebuilt clean (0 warnings / 0 errors) and re-ran the four
  binaries — UnitTests **438/438**, IntegrationTests **80/80**, ArchitectureTests **30/30**, PropertyTests
  **1/1**. Total **549** green (+2 unit cases; catalog still 36; golden corpus byte-unchanged).

### Completion Notes List

- **No production code changed.** Story 4.2 is a behavioral-proof / edge-cell-finalization / guardrail story
  (FR-17, FR-18). Task 1 reconciliation confirmed the uniform `AssignWorkItem`/`QueueWorkItem`/`ClaimWorkItem`
  surface, the `WorkItemTransitionRejected` rejection, the `WorkItemLifecycle.Decide` table, and the four
  design invariants (D2 requeue-keeps-binding, D4 active-work-not-reassigned, D5 terminal-immutable) already
  existed exactly as specified. No new event, command, rejection, or value-object type was added.
- **Task 2 (D4):** `WorkItemLifecycle.cs` already returns `Reject` for both `InProgress → Assign` and
  `InProgress → Queue` (and the `Suspended` row), and the matrix doc cells were already `R` with no pending
  "to refine" note — so the finalization was recorded as an **added note** in
  `docs/lifecycle-transition-matrix.md` (no cell or code change). The historical 2.1 artifact was left
  untouched. `LifecycleTransitionMatrixDocTests` (name/string presence only) stays green.
- **Tasks 3–5:** Added `WorkItemHandoffTests.cs` (unit, +17) and `WorkItemHandoffChainContractFlowTests.cs`
  (integration, +1) proving: requeue keeps the last binding in state (D2); assign from `Created`/`Queued`;
  symmetric reassignment across the three representative executors (a fresh raw act, latest wins, no NoOp,
  no kind branch); the **ordered raw-act hand-off history** preserved both directions (human↔system) with
  the latest binding authoritative for the next claim (AC #3, incl. through serialization); the full
  requeue→reclaim-by-a-different-executor path (D6, distinct from Story 2.5's reject path); and terminal-state
  assignment/queue rejection with no binding mutation and no sequence burn across all four terminals (D5).
- **Task 6:** Added `ScaffoldGovernanceTests.P0_WorkItemSurfaceHasNoExecutorKindSpecificHandoffOrReassignTypeAndCatalogStays36`
  — matches declared type names (not substrings) against `HandoffTo*`/`ReassignTo*`/`AssignTo<Kind>*`/
  `*HandedOff`/`Unassign*`/`ReturnToPool*` (none exist) and asserts the v1 catalog stays 36 by reflecting the
  `Polymorphic`-derived concrete types in the Contracts assembly (ArchitectureTests cannot reference the
  internal `WorkItemV1Catalog`). Existing guardrails preserved unchanged and green.
- **Durable wire surface frozen:** `WorkItemV1Catalog.Count` stays **36**; the golden corpus under
  `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/` is byte-unchanged (verified within the green
  integration suite). No recursive submodule commands were run; the unrelated `Hexalith.Tenants` gitlink
  change in the working tree was left untouched.

### File List

Production code: **none changed.**

Tests:

- `tests/Hexalith.Works.UnitTests/WorkItemHandoffTests.cs` — **new** (Tasks 3–5; AC #1–#5; +17 cases at
  dev-story, +2 cases at QA gap-fill → **19 cases**; AC #1 + terminal-rejection theories also strengthened).
- `tests/Hexalith.Works.IntegrationTests/WorkItemHandoffChainContractFlowTests.cs` — **new** (Task 4; AC #3; +1 case).
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` — **modified** (Task 6; +1 fitness test + 2 usings).

Documentation / bookkeeping:

- `docs/lifecycle-transition-matrix.md` — **modified** (Tasks 2 & 7; D4 finalization note + requeue/binding note; no cell changed).
- `docs/boundary-decision-record.md` — **modified** (Task 7; one-line Story 4.2 uniform-vocabulary note).
- `_bmad-output/implementation-artifacts/tests/test-summary.md` — **modified** (Task 7; new Story 4.2 section).
- `_bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md` — **modified** (this story file).
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — **modified** (status → in-progress → review).

### Change Log

| Date | Change |
|---|---|
| 2026-06-17 | Story 4.2 dev-story: reconciled the assign/requeue/claim/reject surface (no production change); finalized the `InProgress`-reassignment edge cell as `Reject` (D4, matrix-doc note); added unit + integration tests proving assign/reassign/hand-off symmetry, ordered raw-act hand-off history, requeue→reclaim-by-a-different-executor, and terminal-state rejection with no binding mutation; added a fitness guard (no executor-kind hand-off/reassign type; catalog stays 36); updated docs and test-summary. +19 tests → **547** green. Status → review. |
| 2026-06-17 | Story 4.2 QA gap-fill (`bmad-qa-generate-e2e-tests`): closed four AC/design-decision coverage gaps with no production change — AC #5 emitted payload `ShouldBeAssignableTo<IRejectionEvent>()` (both terminal theories); AC #2/D3 same-binding reassign is a fresh raw act, never a NoOp (+1 Fact); FR-18/D2×AC #3 push-from-pool overrides the requeue-retained binding (+1 Fact); AC #1 emitted-event identity (`AggregateId`/`TenantId`/`WorkItemId`). +2 unit cases → **549** green; catalog still 36, golden corpus byte-unchanged. |
| 2026-06-17 | Adversarial code review (`bmad-story-automator-review`): validated all five ACs and all eight tasks against the actual implementation, git reality, a clean Release build, and a full test run. **Approved — 0 CRITICAL / 0 HIGH / 0 MEDIUM findings, nothing to fix.** Status → done; sprint-status synced. |

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot (`bmad-story-automator-review`, adversarial mode) — 2026-06-17
**Outcome:** ✅ **Approve.** 0 CRITICAL, 0 HIGH, 0 MEDIUM, 0 actionable-LOW findings; no fixes required.

### What was verified (claims vs. reality)

- **Git vs. File List:** exact match. Modified: `docs/lifecycle-transition-matrix.md`,
  `docs/boundary-decision-record.md`, `tests/.../ScaffoldGovernanceTests.cs`,
  `_bmad-output/.../tests/test-summary.md`, `sprint-status.yaml`. New (untracked):
  `tests/Hexalith.Works.UnitTests/WorkItemHandoffTests.cs`,
  `tests/Hexalith.Works.IntegrationTests/WorkItemHandoffChainContractFlowTests.cs`. The
  `Hexalith.Tenants` gitlink and the `_bmad-output/story-automator/orchestration-*.md` log are
  unrelated/excluded surfaces, correctly left untouched. **Production code: none changed — confirmed
  (`git diff -- src/` empty).**
- **Build:** `dotnet build -c Release` → **0 warnings / 0 errors**.
- **Tests (direct xUnit v3 binaries):** UnitTests **438/438**, IntegrationTests **80/80**,
  ArchitectureTests **30/30**, PropertyTests **1/1** (100 properties) → **549 green**, matching the Dev
  Agent Record exactly. The 19 new `WorkItemHandoffTests` cases reconcile to the +19 UnitTests delta.
- **Durable surface:** v1 catalog stays **36** (asserted by the new fitness test and `WorkItemV1Catalog`);
  `SchemaEvolution/` golden corpus byte-unchanged.
- **AC coverage:** AC #1 (assign-from-`Created`/`Queued` emits one addressed `WorkItemAssigned`); AC #2
  (same-handler rebind across the three representative executors + same-binding-is-still-a-raw-act, no
  kind branch); AC #3 (ordered raw-act hand-off history both directions, latest binds the next claim, incl.
  through JSON round-trip); AC #4 (`Assigned → Queue → Claim`-by-a-different-executor); AC #5 (all four
  terminals reject `Assign`/`Queue` as an `IRejectionEvent`, no binding/sequence mutation) — all genuinely
  implemented and asserted with real Shouldly assertions (no `Skip`, no `Assert.*`/Moq/FluentAssertions,
  no placeholders).
- **Doc↔code parity:** `docs/lifecycle-transition-matrix.md` verified **cell-by-cell** against
  `WorkItemLifecycle.Decide` (all 9 statuses × 9 acts), including the D4 `InProgress`/`Suspended` →
  `Assign=R`/`Queue=R` cells this story finalizes; the D4 note and the requeue/binding (D2/D6) note are
  accurate. `docs/boundary-decision-record.md` uniform-vocabulary note is consistent.

### Findings

None requiring action. The slice is a behavioral-proof / edge-cell-finalization / guardrail story that
added no production code; every assertion exercises real aggregate behavior.

### Observation (non-blocking, pre-existing — not a 4.2 defect)

Matrix-doc↔code parity remains a *discipline*, not an automated gate — `LifecycleTransitionMatrixDocTests`
only checks name/string presence, by design (Story 2.1 ownership; reaffirmed in this story's Task 6). This
review verified parity manually and found it exact. A strict cell-by-cell parity test would be a sensible
future hardening but is out of scope for 4.2 and intentionally not added here.
