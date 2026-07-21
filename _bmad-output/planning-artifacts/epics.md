---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - '_bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/brief.md'
  - '_bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/addendum.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md'
project_name: 'Hexalith.Works'
scopeNote: 'v1 = Themes 1 & 2 (headless event-sourced domain kernel + Aspire test host). No production UI or channel adapters; UX requirements recorded for traceability, only v1-actionable ones generate stories.'
---

# Hexalith.Works - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for **Hexalith.Works v1**, decomposing
the requirements from the PRD (+addendum), the Architecture Decision Document, and the UX Design
spec (DESIGN.md + EXPERIENCE.md) into implementable stories.

**Scope reminder (load-bearing):** v1 delivers **Themes 1 & 2 only** — a pure, event-sourced domain
assembly (the `WorkItem` aggregate, its lifecycle, raw-act events, recursive roll-up, executor
binding, module ports as abstractions) plus a repository-specific .NET Aspire host that runs it
under test. v1 ships **no** production channel adapters and **no** end-user UI. Counter-metrics
**SM-C1** ("don't grow the kernel") and **SM-C2** ("don't over-fit v1 to deferred themes") are
explicit guardrails: UX components, email-as-UI, routing, cost, and security enforcement are
**Themes 3–6** and are recorded here only for traceability — they generate **no v1 stories**.

## Requirements Inventory

### Functional Requirements

> Source: PRD §4 (FR-1…FR-25), 7 feature groups, global FR numbering. All requirements are
> tenant-scoped (NFR-1) even where not restated.

**4.1 Work Item Aggregate & State**

- **FR-1: Create a Work Item** — A builder/Executor creates a Work Item with at minimum an
  Obligation description + Tenant context, optionally supplying initial Estimated effort (+Unit),
  Schedule, parent reference, and Executor Binding. Emits `WorkItemCreated`; canonical identity
  `{tenant}:work:{workItemId}`; Status `Created`. Unestimated creation is valid (completes only by
  explicit complete act, not the Remaining=0 rule).
- **FR-2: Carry an Obligation with an optional Expectation reference** — Holds a required, non-empty
  human-readable Obligation description plus an optional Expectation reference resolved via
  `IExpectationResolver` (no-LLM in v1; resolved on demand, never stored as an interpreted value).
- **FR-3: Hold a unit-tagged Effort Burn-Down** — Estimated/Done/Remaining each tagged with the
  item's Unit; Remaining = Estimated − Done (never < 0); Unit is per-item; no implicit cross-Unit
  arithmetic.
- **FR-4: Carry a Schedule (Priority + Due Date)** — Priority + optional Due Date establish standing
  in a contended queue; settable at creation and changeable later (each change emits an event); an
  item with neither sorts last in "what's next".
- **FR-5: Hold parent/children references and Await-Conditions** — At most one parent, zero-or-more
  children (referenced by ID), and zero-or-more Await-Conditions while Suspended (resumes on first
  match). Acyclic, single-parent (FR-13).

**4.2 Lifecycle State Machine & Domain Events**

- **FR-6: Enforce the lifecycle state machine** — 9 statuses (Created, Assigned, Queued, InProgress,
  Suspended, Completed, Cancelled, Rejected, Expired). Forward path
  `Created → Assigned|Queued → InProgress → Suspended → InProgress → Completed`; `Assigned ↔ Queued`
  bidirectional; terminals (Cancelled|Rejected|Expired) reachable per FR-10. Illegal transitions are
  domain rejections (`IRejectionEvent`), not exceptions. No transition out of a terminal state.
- **FR-7: Record raw-act domain events** — Each state change/progress fact is a past-tense Domain
  Event capturing acting Party + timestamp + verbatim payload. Frozen v1 catalog (14, additively
  extensible): `WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`,
  `ProgressReported`, `ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`, `WorkItemSuspended`,
  `WorkItemResumed`, `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`.
  Events store the Raw Act, not interpreted values; the ordered stream **is** the item's history.
- **FR-8: Report progress and complete by Remaining=0** — `ProgressReported` decreases Remaining by
  the Done delta (clamped at 0); reaching 0 → `Completed` + `WorkItemCompleted`. An **unestimated**
  item completes only by an explicit complete act. Crash/abandonment leaves Remaining > 0 and
  resumable ("retry" = continue the burn-down).
- **FR-9: Re-estimate and reschedule** — `ReEstimated` adjusts Estimated (and Remaining) as a
  normal expected event; Priority/Due-Date changes emit events and update "what's next" ordering.
- **FR-10: Cancel, reject, expire** — `WorkItemCancelled`/`WorkItemExpired` terminal; **Reject**
  (`WorkItemRejected`) defaults to requeue (`Queued`), terminal only when marked non-requeuable;
  **Expire** is Due-Date/TTL-driven, terminal, no auto-reactivation. **Cascade:** cancel/expire
  cascades termination to still-active descendants; terminal descendants contribute 0 to roll-up.

**4.3 Effort Burn-Down & Recursive Roll-Up**

- **FR-11: Maintain the recursive remaining-effort Roll-Up** — Roll-Up projection where
  `rolled-Remaining(node) = own Remaining + Σ rolled-Remaining(direct children)`, recursively;
  reflected incrementally per child event; eventually-consistent + **idempotent** under
  at-least-once, possibly out-of-order delivery; built on substrate projection infra.
- **FR-12: Roll up across heterogeneous units safely** — Same-Unit subtrees roll into one number;
  mixed-Unit subtrees expose **per-Unit subtotals**, never a coerced single figure; no Unit
  conversion in v1.
- **FR-13: Guard the Work Tree shape** — Acyclic, single-parent, **single-tenant** tree enforced at
  spawn; cross-tenant parent/child link rejected; depth bounded by a configured max (default 32,
  configurable); breadth uncapped.

**4.4 Suspend / Resume Saga**

- **FR-14: Suspend on an Await-Condition** — `WorkItemSuspended` records the Await-Condition kind +
  correlation key (child ID, target date, or external correlation ID); a Suspended item accepts no
  progress but still participates in Roll-Up with current Remaining.
- **FR-15: Resume on a matching trigger** — Resume is a command carrying a correlation key matching
  an Await-Condition; `Handle` never reads a clock — date/timer + external signals arrive **as
  commands** from adapters. Child-completion, date/timer (adapter), and external (deferred adapter)
  satisfiers; `WorkItemResumed` → InProgress; non-matching key = rejection; duplicate = idempotent
  no-op.
- **FR-16: Spawn child work** — `ChildSpawned` creates a child (FR-1 semantics) with a parent
  reference and emits on the parent; respects the tree guard (FR-13); parent may suspend awaiting it.

**4.5 Executor Binding — "Everything is a Party"**

- **FR-17: Bind, reassign, and hand off via one uniform operation** — Assign to a system Party
  (Channel=MCP), internal-user Party, or external Party (Channel=email) via the **identical**
  command (`WorkItemAssigned`); human↔AI handoff is the same reassign in either direction; **no
  domain code branches on executor kind** (the only variation is the binding's field values).
- **FR-18: Push and Pull coexist** — A `Queued` item can be claimed (`WorkItemClaimed` → InProgress
  bound to claimant); **single claim wins** (one success, loser domain-rejected, serialized by
  optimistic concurrency); `Assigned → Queued` (requeue) and `Queued → Assigned` (direct assign)
  are normal; any tenant Executor may claim in v1 (eligibility filtering deferred to Theme 4).
- **FR-19: Carry AuthorityLevel on the binding** — Binding persists an AuthorityLevel through
  create/assign/reassign; proposed ordered set `{Read, Contribute, Coordinate, Administer}`;
  **carried-not-enforced in v1** (no v1 behavior branches on it); additive-tolerant.

**4.6 Thin-Core Boundaries & Module Ports**

- **FR-20: Resolve a "what's next" ordering** — Read-side query returning a tenant's
  `Queued`+`Assigned` items ordered by Priority → earliest Due Date → creation order (neither sorts
  last); served by substrate query/projection infra; applies query-side authorization/result
  filtering in addition to tenant scoping; projection/query only — no routing engine.
- **FR-21: Reference sibling modules, never copy them** — Identity→`Parties` (PartyId),
  dialogue→`Conversations` (correlation ID), persistence/events→`EventStore`,
  isolation→`Tenants`, IDs→`Commons`; aggregate stores correlation IDs, not denormalized copies; a
  Conversation ID is optional/linkable and resolved on demand (no comment store in v1).
- **FR-22: Expose module ports as abstractions** — Domain depends on `IExpectationResolver` (no-LLM
  impl **shipped** in v1) and `IExecutorRouter` (**abstraction only**, no impl wired); the domain
  assembly compiles + all tests pass with no `IExecutorRouter` wired; no LLM/cost/routing/infra type
  referenced from the domain assembly.
- **FR-23: Produce the boundary decision record** — v1 includes a written owns-vs-references
  boundary decision record (`docs/boundary-decision-record.md`) as a tracked artifact referenced by
  the architecture phase.

**4.7 Aspire Test Host & Harness**

- **FR-24: Run the kernel under an Aspire host** — An Aspire AppHost wires Works + substrate
  dependencies for local manual + automated testing; the end-to-end lifecycle
  (create → progress → spawn → suspend → resume → complete) runs with correct Roll-Up; follows the
  ServiceDefaults/health/telemetry pattern; no production adapters.
- **FR-25: Exercise the command pipeline in tests** — Kernel exercisable through its command/event
  pipeline without production adapters; Tier-1 tests run pure (no Dapr/network/browser/containers);
  integration tests use substrate fakes/builders or Aspire topology only at real boundaries.

### NonFunctional Requirements

> Source: PRD §9 (Cross-Cutting NFRs) and §10 (Constraints & Guardrails — seams laid in v1).

- **NFR-1: Tenant isolation (mandatory, every layer)** — Every Work Item, identity, state key,
  projection key, query, and log is tenant-scoped (`{tenant}:work:{workItemId}`); **query-side
  authorization/result filtering required in addition to command-side checks**; the roll-up asserts
  tenant-equality at every hop (parent/child references tenant-closed); single-tenant trees.
  Mutation-validated negative-path tests for cross-tenant and query-side paths.
- **NFR-2: Event-sourcing invariants** — Persist-then-publish; `Handle(...)` pure → returns domain
  results/events; `Apply(...)` mutates only in-memory state; domain rejections are `IRejectionEvent`;
  infrastructure failures are exceptions/dead-letter; Works returns **payloads only** (EventStore
  owns envelope metadata); `DomainResult` never mixes success + rejection.
- **NFR-3: Concurrency** — Single-writer/optimistic (expected-version) per Work Item; concurrent
  conflicting commands (e.g., two claims on one `Queued` item) resolve to exactly one success and
  domain rejections for the rest; no lost updates.
- **NFR-4: Projections are rebuildable** — Roll-Up and "what's next" derive purely from event
  streams, are replayable from scratch, hold no authoritative state; rebuild is **online /
  non-disruptive** and per-tenant partitionable (shadow + atomic swap or versioned key).
- **NFR-5: Domain purity** — Domain assembly (Contracts/Server/Projections) takes no infra and no
  LLM/cost/routing dependency; `Handle`/`Apply` (and the reactor's pure translation) read no clock,
  RNG, or I/O; IDs supplied at the edge. Enforced by **build-time fitness functions** (banned-symbol
  analyzer; no-branch-on-executor-kind; dependency-direction).
- **NFR-6: Observability & privacy** — Structured logging only; **never** log event payloads, PII,
  secrets, raw tokens, or full command bodies; errors via ProblemDetails/**RFC 9457** with
  correlation + tenant context.
- **NFR-7: Performance (qualitative for v1)** — Roll-Up and "what's next" update incrementally (no
  whole-stream re-read per query); no numeric budgets pinned (acceptance is build-signal based).
- **NFR-8: Audit / non-repudiation (model laid in v1)** — Domain Events record the Raw Act (acting
  Party + timestamp + verbatim payload) so interpretation is a recomputable Projection; signed
  single-use link enforcement + auditor-facing query are Theme 6.
- **NFR-9: Idempotency (event-sourced)** — Resume is idempotent against current state (key no longer
  matching = no-op; duplicate = no-op); substrate offset dedup so replays don't double-count; no
  explicit per-act idempotency token in v1.
- **NFR-10: Cost-ready burn-down** — Burn-down + roll-up built so a second (Cost) Meter reuses the
  identical machinery (Theme 5) with no schema reshape.
- **NFR-11: NL-is-data boundary (designed-for)** — The Expectation/`IExpectationResolver` port is
  the future prompt-injection boundary; v1 ships only the no-LLM resolver, so no NL is interpreted
  as instructions in v1.
- **NFR-12: Additive schema evolution & serialization** — Additive, serialization-tolerant only;
  **no `V2` event types**; every event ever produced remains backward-compatibly deserializable;
  `Hexalith.PolymorphicSerializations` + `System.Text.Json`; a **golden-payload corpus** is started
  in v1 so back-compat is falsifiable.

### Additional Requirements

> Source: Architecture Decision Document (decisions A1–E2, structure, patterns, risk register).
> These technical requirements impact epic/story creation and acceptance criteria.

- **AR-1 [STARTER TEMPLATE — first story]** — Scaffold the module at the umbrella-repo root using
  the **Hexalith canonical domain-module layout via `Hexalith.Builds`, donor = `Hexalith.Parties`**,
  taking only the reduced v1 subset. v1 project set: `Contracts`, `Server`, `Projections`,
  `Reactor` (adapter), `Testing`, `AppHost`, `ServiceDefaults`, `samples/SampleHost`; root config
  (`global.json`, `Directory.*`, `Hexalith.Works.slnx`, `aspire.config.json`, semantic-release +
  commitlint). **Deliberately NOT scaffolded:** `.Client` (minimal/optional), `.UI`/`.Mcp`/portals/
  `.Security` (Themes 3–6; SM-C1/SM-C2). This is the **first implementation story** and a
  precondition for SM-1/SM-4 (green build + green tests under Aspire).
- **AR-2 [first-story verification]** — Verify the **live `Hexalith.EventStore` API surface**
  supports expected-version append, the projection infra (`CachingProjectionActor`, ETag actors,
  notifiers), and online rebuild before relying on the chosen patterns.
- **AR-3 (A1) Aggregate-ID at the edge** — ID assigned at the command-creation edge via the
  `Hexalith.Commons` helper and passed into `CreateWorkItem`; `Handle` never generates IDs
  (deterministic replay; idempotent create on retry).
- **AR-4 (A5/B2) Every event carries `(AggregateId, Sequence)`** — Contracts-level decision enabling
  order-tolerant projections; required on all 14 events.
- **AR-5 (A2) Priority = ordered enum** — `{Critical, High, Normal, Low}`, additive-tolerant; backs
  "what's next" ordering (vs numeric routing bands — YAGNI, SM-C2).
- **AR-6 (A3/E2) Unit immutability & validation domains** — Unit immutable after first estimate;
  `ProgressReported`/`ReEstimated` must carry the same Unit or are rejected; `ProgressReported`
  delta ≥ 0 with Remaining clamped ≥ 0; `Estimated` ≥ 0; Due-Date/TTL sourced from per-work-type/
  tenant policy (configurable default).
- **AR-7 (A4) `Meter(Unit, Estimated, Done)` cost-ready** — derived `Remaining`; one Effort meter in
  v1; a parallel Cost meter reuses the identical type (Theme 5).
- **AR-8 (A5) Roll-Up per-child-sequence LWW** — contribution keyed by `(childId, childEventSequence)`,
  last-write-wins, recursive, per-Unit, **idempotent + order-tolerant** (stale/lower-sequence writes
  ignored; replays don't double-count) on EventStore projection infra.
- **AR-9 (B3) Type-separated authority split** — own-Remaining + Status are aggregate-authoritative
  and **synchronous** (incl. `Done = Remaining 0 → Completed`); **rolled-Remaining** is an eventual
  projection with a **distinct type/field/serialized shape** so no consumer can gate control flow on
  the eventual value.
- **AR-10 (B1) Single-aggregate claim under expected-version** — Claim is a single-aggregate
  operation on the `WorkItem`; the claimable pool is a **read projection**, not an authoritative
  queue aggregate; two racing claims → one commits, loser gets `ClaimRejected`.
- **AR-11 (B2) No reliance on pub/sub ordering** — Dapr pub/sub is at-least-once, **not ordered**;
  write-order comes from the single-writer actor; read-path correctness from idempotent,
  order-tolerant projections + offset dedup.
- **AR-12 (C1) Reactor / process-manager lives OUTSIDE the kernel** — mechanical `react(event) →
  command[]` translation only (**no shadow-kernel logic** — every decision round-trips through a pure
  `Handle`); drives child-completion → parent-resume (FR-15) and cascade cancel/expire → descendants
  (FR-10); contract = at-least-once delivery + **idempotent target commands** + **checkpoint-driven,
  resumable cascade** off a re-readable "descendants still needing cancel" projection. The pure
  translation is unit-testable; runtime (delivery/checkpoint/reminders) lives in `Reactor/Dispatch|
  Cascade|Timer`, wired by `AppHost`.
- **AR-13 (C1/RR) 9-state cancel/expire transition table** — Enumerate the per-state cancel/expire
  decision for each of the 9 statuses (e.g., cancelling an already-`Completed` child is a defined
  domain decision) before the reactor can safely cascade.
- **AR-14 (C2) Dapr actor reminders for date resumes** — A `WorkItem` parked on `DateReached`
  registers a self-targeted, durable reminder; on fire it raises `ResumeWorkItem(date)` — `Handle`
  never reads a clock. Durable across crash/restart; **reconciliation-on-recovery** re-scans
  `DateReached` await-conditions for firings lost before being recorded; reminder name = a
  deterministic function of `(workItemId, awaitConditionKey)`.
- **AR-15 (C3) Deadlines advisory-until-fired** — The kernel may hold a "live" item that is, in
  reality, overdue; no v1 query detects this without the timer firing. Recorded explicitly;
  **re-validate against Theme 5** before that theme builds.
- **AR-16 (C4) AwaitCondition discriminated set** — `{ ChildCompleted(childId) | DateReached(instant)
  | ExternalSignal(correlationId) }`; a Suspended item holds a **set** and resumes on first match;
  the concrete external adapter is deferred to Theme 3 (the correlation key is the contract it fills).
- **AR-17 (E1) Online per-tenant projection rebuild** — Shadow projection + atomic swap or versioned
  projection key; per-tenant partitionable; produces state identical to a cold rebuild with no
  partial-state leak to readers.
- **AR-18 Ports realization (FR-22)** — `IExpectationResolver` no-LLM impl shipped; `IExecutorRouter`
  abstraction only, no impl wired; domain references no LLM/cost/routing/infra type.
- **AR-19 Boundary decision record (FR-23)** — `docs/boundary-decision-record.md` is a tracked v1
  deliverable enumerating owns-vs-references per sibling module.
- **AR-20 Pinned versions via central package management** — SDK `10.0.301` (rollForward
  latestPatch), Dapr `1.18.4`, .NET Aspire `13.4.6`, xUnit **v3** `3.2.2` + Microsoft.Testing.Platform;
  ecosystem-pinned by policy (align to current sibling pins, do not casually upgrade).
- **AR-21 Test taxonomy & build gates** — *unit* (pure `Handle`/`Apply`/validators); *property*
  (FsCheck roll-up convergence under permutation + duplication — RR-1); *architecture-fitness*
  (purity/banned-symbols, no-branch-on-kind, dependency-direction — SM-3/SM-4, continuous);
  *contract* (serialization back-compat golden-payload corpus — RR-6; Dapr pub/sub envelope);
  *integration/topology* (Aspire persist-then-publish seam); *chaos* (crash-at-step-boundary incl.
  **SM-1b** mid-reactor-step convergence; rebuild-while-live). **SM-C2 is a review-gate, not a
  build-gate.**
- **AR-22 Naming, structure & dependency direction** — file-scoped namespaces under
  `Hexalith.Works.*`; commands imperative (no `Command` suffix), events past-tense (no `Event`
  suffix), sealed records, one public type per file; machine-checkable dependency direction
  `Contracts ← Server ← Projections`, adapters (`Reactor`/`AppHost`/`ServiceDefaults`) reference
  inward, **kernel references no adapter**.

### UX Design Requirements

> Source: UX `DESIGN.md` + `EXPERIENCE.md`. Per the confirmed scope decision, **all** UX
> requirements are recorded for traceability and **tagged `[v1]` vs `[Theme N]`**. Only `[v1]`
> requirements — which shape the read models/projections the kernel actually builds — generate
> stories. `[Theme N]` items are the deferred human-facing horizon and generate **no v1 stories**
> (SM-C2); they are listed so the kernel's seams land in the right places.

**v1-actionable (shape the read-side contracts the kernel ships):**

- **UX-DR1 `[v1]` Projections are SignalR-ready** — Roll-Up and "what's next" projections emit
  change notifications (live-update friendly) so a future console updates without manual refresh.
  No surface ships in v1; the projection notification seam does. (Arch "Frontend N/A" note;
  EXPERIENCE "Real-time by default".)
- **UX-DR2 `[v1]` Read models preserve per-Unit subtotals** — `RollUpView` and burn-down read
  models never coerce heterogeneous Units into one figure; they expose labeled per-Unit subtotals
  (the "never summed" rule enforced at the data layer). Ties FR-12.
- **UX-DR3 `[v1]` Read models expose own-Remaining vs rolled-Remaining as distinct fields** —
  supports the future "one number" shown beside own Remaining; aligns with the type-separated
  authority split (AR-9). Ties FR-11.
- **UX-DR4 `[v1]` Read models carry executor kind + channel + AuthorityLevel as data** — so a
  future single, identical Party chip renders kind/channel via glyph and authority via a monochrome
  badge, with **zero kind-branching** in the model. Ties FR-17/FR-19, SM-3.
- **UX-DR5 `[v1]` Read models expose the 9 Status values + (for Suspended) the await-condition
  kind+key** — so a future Suspended pill can show "Waiting on: …" inline; status is never the sole
  progress signal. Ties FR-6/FR-14.
- **UX-DR6 `[v1]` Unified raw-act history is queryable** — the event stream exposes actor +
  timestamp + verbatim Raw Act (past-tense) for a future history timeline; Works holds the
  Conversation correlation ID, not a comment store. Ties FR-7/FR-21.

**Deferred — recorded for traceability, NO v1 stories:**

- **UX-DR7 `[Theme 5]` Cost meter** — a second burn-down in warm gold (`#C19C00`), parallel to
  effort; seam shaped via the cost-ready `Meter` (AR-7), not built.
- **UX-DR8 `[Theme 3]` Email-as-UI action-link set** — bespoke email surface outside the shell;
  each button = one valid single-use, expiring, signed domain act; ≥44px taps, table layout/inline
  styles/system fonts/plain-text fallback.
- **UX-DR9 `[Theme 3]` NL escape hatch** — "None of these — answer in my words"; free text mapped
  onto the valid action space; confidence-gated auto-apply; NL is data, never instructions.
- **UX-DR10 `[Theme 3]` Capture bar parity** — global quick-capture / email one-liner / single
  chatbot sentence converge on one Work Item (Obligation + Tenant minimum; no wizard).
- **UX-DR11 `[Theme 3]` Web shell IA + FrontComposer composition** — What's next / Work / Work Item
  detail / Capture surfaces; `<FrontComposerShell>` left rail; L2/L3/L4 composition leverage;
  desktop-first responsive (md collapse; phone read + simple-advance); keyboard (`g n` / `g w` /
  command palette).
- **UX-DR12 `[Theme 3]` Brand layer** — blurple `#5B5FC7` brand token, burn-down green, 9-status
  color vocabulary, tabular-nums `metric`/`metric-hero` type roles; Fluent UI v5 inherited wholesale
  (no restyling Fluent components).
- **UX-DR13 `[Themes 4/5]` Admin policy surface** — escalation ladders, authority levels, spend caps.
- **UX-DR14 `[Theme 6]` Audit surface** — query the signed non-repudiation record.
- **UX-DR15 `[Theme 3]` Voice & tone microcopy** — factual/terse, same voice to every audience
  ("What's next?", "Nothing waiting. You're clear.", "Someone else got there first.", count-not-percent).
- **UX-DR16 `[Theme 3 — applies when UI ships]` Accessibility floor** — WCAG 2.2 AA; progress never
  color-only (number + bar length + text label; SR announces "Remaining 3 of 8 interactions");
  `aria-live` (polite) for SignalR updates; tenant isolation reflected (hidden, not blocked); Tab
  order = reading order; `Esc` closes the topmost overlay; ecosystem a11y specimen gate (Playwright
  `npm run test:a11y`). *(v1 read-model support for "progress never color-only" — exposing
  number + label — is already covered by UX-DR2/UX-DR3/UX-DR5.)*

### FR Coverage Map

FR-1: Epic 1 - Create a tenant-scoped Work Item.
FR-2: Epic 1 - Obligation and optional Expectation reference.
FR-3: Epic 2 - Unit-tagged Effort Burn-Down.
FR-4: Epic 2 - Schedule with Priority and Due Date.
FR-5: Epic 3 - Parent/children references and Await-Conditions.
FR-6: Epic 2 - Lifecycle state machine.
FR-7: Epic 2 - Raw-act domain events.
FR-8: Epic 2 - Progress reporting and completion by Remaining=0.
FR-9: Epic 2 - Re-estimate and reschedule.
FR-10: Epic 2 - Cancel, reject, expire, and cascade semantics.
FR-11: Epic 3 - Recursive remaining-effort Roll-Up.
FR-12: Epic 3 - Heterogeneous Unit roll-up safety.
FR-13: Epic 3 - Work Tree shape guard.
FR-14: Epic 3 - Suspend on Await-Condition.
FR-15: Epic 3 - Resume on matching trigger.
FR-16: Epic 3 - Spawn child work.
FR-17: Epic 4 - Uniform executor binding and handoff.
FR-18: Epic 4 - Push/pull queue and single-claim-wins.
FR-19: Epic 4 - AuthorityLevel carried on binding.
FR-20: Epic 4 - "What's next" ordering.
FR-21: Epic 1 - Reference sibling modules, never copy them.
FR-22: Epic 1 - Module ports as abstractions.
FR-23: Epic 1 - Boundary decision record.
FR-24: Epic 4 - Aspire host.
FR-25: Epic 4 - Command pipeline test harness.

## Epic List

### Epic 1: Builder-Ready Work Item Kernel
A Hexalith builder can scaffold Works as a pure event-sourced module, create a tenant-scoped Work Item, reference sibling modules by ID, and rely on clear owns-vs-references boundaries.
**FRs covered:** FR-1, FR-2, FR-21, FR-22, FR-23.

### Epic 2: Reliable Single-Item Lifecycle and Burn-Down
An executor can advance one Work Item through the full lifecycle, report progress, re-estimate, reschedule, complete by Remaining=0, and terminate work through cancel/reject/expire with raw-act events.
**FRs covered:** FR-3, FR-4, FR-6, FR-7, FR-8, FR-9, FR-10.

### Epic 3: Work Tree Roll-Up and Durable Await
A coordinator can spawn child work, suspend a parent on await-conditions, resume on matching triggers, and trust recursive remaining-effort roll-up across a tenant-safe work tree.
**FRs covered:** FR-5, FR-11, FR-12, FR-13, FR-14, FR-15, FR-16.

### Epic 4: Shared Work Execution and Builder Runtime Validation
Teams, agents, and external parties can share one executor model: assign, reassign, claim, and hand off work through the same Party binding, while builders can validate the complete command/event pipeline under the Aspire host.
**FRs covered:** FR-17, FR-18, FR-19, FR-20, FR-24, FR-25.

## Epic 1: Builder-Ready Work Item Kernel

A Hexalith builder can scaffold Works as a pure event-sourced module, create a tenant-scoped Work Item, reference sibling modules by ID, and rely on clear owns-vs-references boundaries.

### Story 1.1: Set Up Initial Project from Starter Template

As a Hexalith builder,
I want a clean Works module scaffold aligned with the Hexalith ecosystem,
So that I can implement the Work Item kernel in a verified, buildable module without inventing technical layers.

**Acceptance Criteria:**

**Given** the Hexalith.Works umbrella repository with only root-level submodules available
**When** the Works module scaffold is created
**Then** the repository contains the v1 project set defined by architecture: `Hexalith.Works.Contracts`, `Hexalith.Works.Server`, `Hexalith.Works.Projections`, `Hexalith.Works.Reactor`, `Hexalith.Works.ServiceDefaults`, `Hexalith.Works.AppHost`, `Hexalith.Works.Testing`, and focused test projects
**And** no `.UI`, `.Mcp`, portal, `.Security`, routing, LLM, cost-governance, or production channel adapter project is created.

**Given** the scaffolded module
**When** package and build configuration is inspected
**Then** dependencies and versions are managed through central package management
**And** project files do not contain inline package versions
**And** the solution uses `.slnx`, not `.sln`.

**Given** the scaffolded module
**When** dependency direction is checked
**Then** `Contracts` remains low-dependency and infrastructure-free
**And** `Server` and `Projections` do not reference adapter, Dapr runtime, UI, LLM, routing, or cost-governance types
**And** adapter-ring projects reference inward without creating cycles.

**Given** the scaffolded module
**When** the live `Hexalith.EventStore` API surface is verified
**Then** the implementation notes or tests confirm whether expected-version append, projection infrastructure, ETag/notifier support, and online rebuild support are available for later stories
**And** any mismatch is recorded as a first-story implementation constraint before domain behavior depends on it.

**Given** the scaffolded module
**When** the baseline build/test command for the scaffold is run
**Then** the affected projects restore and build with warnings as errors
**And** no nested submodule initialization or recursive submodule command is required.

**Given** Story 1.1 is complete
**When** implemented scope is reviewed
**Then** it contains scaffold, build configuration, dependency boundaries, baseline build/test proof, and live EventStore API-surface verification only
**And** Work Item lifecycle, burn-down, roll-up, suspend/resume, executor-binding, and reactor runtime behavior remain in their later stories.

### Story 1.2: Create a Tenant-Scoped Work Item

As a Hexalith builder,
I want to create the first tenant-scoped Work Item through the domain contract,
So that Works proves it can record a durable, replayable obligation without copying sibling-module data.

**Acceptance Criteria:**

**Given** a caller supplies a `TenantId`, an edge-assigned `WorkItemId`, and a non-empty Obligation description
**When** `CreateWorkItem` is handled against no prior state
**Then** the domain returns a `WorkItemCreated` payload
**And** the created state replays to Status `Created`
**And** the aggregate identity is consistent with `{tenant}:work:{workItemId}`.

**Given** a caller supplies optional initial Effort, Unit, Schedule, parent reference, Executor Binding, or Conversation correlation ID
**When** the Work Item is created
**Then** `WorkItemCreated` carries only the supplied coordination facts and reference IDs
**And** no Party, Tenant, Conversation, EventStore envelope, or Commons implementation data is copied into the aggregate state.

**Given** a caller supplies no Estimated effort
**When** the Work Item is created
**Then** creation succeeds
**And** Remaining is represented as undefined-until-estimated
**And** the item is not considered completed by the Remaining=0 rule.

**Given** a caller supplies a missing or whitespace Obligation description
**When** `CreateWorkItem` is handled
**Then** creation is rejected as a domain rejection event
**And** the rejection does not mix with a success event in the same domain result.

**Given** `CreateWorkItem` is handled by the kernel
**When** purity checks or architecture tests run
**Then** the handler does not generate IDs, read a clock, perform I/O, call Dapr, or populate EventStore envelope metadata
**And** emitted events can be replayed deterministically into the same state.

### Story 1.3: Reference Sibling Modules Without Copying Data

As a Hexalith builder,
I want Work Items to carry only reference value objects for sibling-module concepts,
So that Works owns coordination facts while Parties, Conversations, Tenants, EventStore, and Commons remain the systems of record.

**Acceptance Criteria:**

**Given** the Works contracts define references to sibling concepts
**When** Work Item commands, events, state, and read-model contracts are inspected
**Then** Parties are represented by `PartyId`
**And** Conversations are represented by a correlation/reference ID
**And** Tenants are represented by `TenantId`
**And** Work IDs are supplied from the edge rather than generated in the aggregate.

**Given** a Work Item is created with Party, Conversation, Tenant, and parent/work references
**When** its event payloads and replayed state are inspected
**Then** they contain only stable reference IDs and coordination facts
**And** they do not contain Party display names, contact channels, tenant profiles, conversation messages, EventStore envelopes, or generated ID implementation details.

**Given** a Conversation correlation ID is absent
**When** a Work Item is created or replayed
**Then** the Work Item remains valid
**And** no comment store or conversation storage is created inside Works.

**Given** a future adapter or projection needs sibling-module details
**When** the domain contract is inspected
**Then** the contract exposes only references that can be resolved on demand outside the aggregate
**And** no direct infrastructure, client, or server dependency on sibling implementation details is required in `Contracts`.

**Given** tenant isolation is mandatory
**When** commands, events, keys, and log scopes are derived for a Work Item
**Then** the tenant reference is present in the coordination identity
**And** tests prove cross-tenant references cannot be silently treated as same-tenant data.

### Story 1.4: Expose Boundary Ports and Decision Record

As a Hexalith builder,
I want Works to expose explicit domain ports and a boundary decision record,
So that future LLM, routing, cost, security, and sibling-module integrations attach without changing the kernel's ownership model.

**Acceptance Criteria:**

**Given** the Works contract surface is inspected
**When** domain ports are reviewed
**Then** `IExpectationResolver` is available as a domain-owned abstraction
**And** a no-LLM implementation is provided for v1
**And** Work Item behavior remains valid when no interpreted Expectation is resolved.

**Given** executor routing is deferred to a later theme
**When** the Works contract surface is inspected
**Then** `IExecutorRouter` exists only as an abstraction
**And** no v1 implementation, routing engine, scoring model, escalation policy, LLM dependency, or cost-governance dependency is wired into the kernel.

**Given** the kernel dependency graph is checked
**When** `Contracts`, `Server`, and `Projections` are inspected
**Then** they reference no LLM, routing, cost-governance, UI, channel adapter, or infrastructure implementation type
**And** architecture-fitness tests enforce the dependency boundary.

**Given** the boundary decision record is generated
**When** `docs/boundary-decision-record.md` is reviewed
**Then** it enumerates what Works owns versus references for Parties, Conversations, EventStore, Tenants, Commons, and PolymorphicSerializations
**And** it explains why Works owns coordination facts but not identity, dialogue, persistence, isolation, or ID generation.

**Given** future themes will add adapters
**When** the decision record and port contracts are reviewed
**Then** they preserve the named seams for AI-inferred expectations, executor routing, cost meter/spend governance, and trust/security hardening
**And** they explicitly state that those deferred capabilities are not v1 behavior.

## Epic 2: Reliable Single-Item Lifecycle and Burn-Down

An executor can advance one Work Item through the full lifecycle, report progress, re-estimate, reschedule, complete by Remaining=0, and terminate work through cancel/reject/expire with raw-act events.

### Story 2.1: Define the Lifecycle State Machine

As an executor,
I want Work Items to enforce a clear lifecycle,
So that every accepted transition is predictable and every invalid transition is rejected as a domain fact.

**Acceptance Criteria:**

**Given** a Work Item in Status `Created`
**When** the executor assigns it or queues it
**Then** the transition to `Assigned` or `Queued` is accepted
**And** any unsupported transition from `Created` is rejected as an `IRejectionEvent`.

**Given** a Work Item in `Assigned` or `Queued`
**When** the executor starts or claims work according to the lifecycle rules
**Then** the item can transition to `InProgress`
**And** `Assigned ↔ Queued` transitions are accepted where requeue or direct assignment is valid.

**Given** a Work Item in `InProgress`
**When** it is suspended
**Then** the item transitions to `Suspended`
**And** resumption is represented only as a transition back to `InProgress`, not as a resting `Resumed` status.

**Given** a Work Item in any terminal status
**When** a further lifecycle command is handled
**Then** no transition out of `Completed`, `Cancelled`, non-requeuable `Rejected`, or `Expired` is accepted
**And** non-idempotent lifecycle commands emit an `IRejectionEvent`
**And** only exact duplicate terminal commands explicitly listed in `docs/lifecycle-transition-matrix.md` return `DomainResult.NoOp`.

**Given** a bound executor rejects an assignment with the default requeue behavior
**When** the rejection is handled
**Then** `WorkItemRejected` may be emitted as raw-act evidence
**And** the resulting resting status is `Queued`, not terminal `Rejected`.

**Given** lifecycle rules are defined
**When** Story 2.1 is complete
**Then** `docs/lifecycle-transition-matrix.md` exists and enumerates accepted, rejected, and idempotent no-op outcomes for each command across all 9 statuses
**And** later lifecycle stories reference this artifact rather than choosing behavior locally.

**Given** the lifecycle implementation is tested
**When** the transition matrix is exercised
**Then** every legal and illegal transition across the 9 statuses is covered by deterministic tests
**And** the handler remains pure: no clock, RNG, I/O, Dapr, or EventStore envelope ownership.

### Story 2.2: Record Raw-Act Events and Replay State

As a Hexalith builder,
I want every accepted Work Item act to be recorded as a replayable raw-act event,
So that the Work Item history is durable, auditable, and independent of interpreted projections.

**Acceptance Criteria:**

**Given** a Work Item state change or progress fact is accepted
**When** the domain result is produced
**Then** it contains a past-tense domain event from the v1 catalog
**And** the event stores the verbatim reported values required to replay the act.

**Given** a domain event is emitted
**When** its payload is inspected
**Then** it carries `AggregateId` and `Sequence` for order-tolerant projections
**And** Works does not populate or spoof EventStore envelope metadata.

**Given** a sequence of Work Item events exists
**When** the events are replayed in order through `Apply`
**Then** the same Work Item state is reconstructed deterministically
**And** no interpreted expectation, AI output, or sibling-module denormalization is required.

**Given** a command is rejected
**When** the domain result is inspected
**Then** the rejection is represented as an `IRejectionEvent`
**And** the same domain result does not mix success payloads with rejection payloads.

**Given** serialization compatibility is required
**When** the v1 event and command catalog is registered
**Then** `Hexalith.PolymorphicSerializations` can resolve the payload types
**And** a golden-payload corpus or equivalent contract test is started for additive, no-`V2` evolution.

### Story 2.3: Report Progress with Unit-Tagged Burn-Down

As an executor,
I want to report progress in the Work Item's Unit,
So that Remaining effort burns down as a fact and completion happens when Remaining reaches zero.

**Acceptance Criteria:**

**Given** a Work Item has an Effort `Meter(Unit, Estimated, Done)`
**When** state is inspected after replay
**Then** Remaining is derived as `Estimated - Done`
**And** Remaining is never represented below zero.

**Given** an executor reports a positive Done delta in the Work Item's Unit
**When** `ReportProgress` is handled for an estimated Work Item
**Then** `ProgressReported` is emitted
**And** replaying the event increases Done and decreases Remaining by the reported delta, clamped at zero.

**Given** progress causes Remaining to reach zero
**When** the event sequence is replayed
**Then** the Work Item transitions synchronously to `Completed`
**And** `WorkItemCompleted` is emitted as part of the accepted completion path.

**Given** a Work Item has no Estimated effort
**When** progress is reported
**Then** the item does not complete through the Remaining=0 path
**And** completion requires an explicit complete act.

**Given** progress uses a negative delta or a Unit different from the Work Item's established Unit
**When** `ReportProgress` is handled
**Then** the command is rejected as a domain rejection
**And** replayed state is unchanged.

### Story 2.4: Re-Estimate and Reschedule Work

As an executor,
I want to re-estimate and reschedule a Work Item as first-class acts,
So that overruns, partial progress, priority changes, and due-date changes are recorded without treating them as errors.

**Acceptance Criteria:**

**Given** a Work Item has an established Effort Unit
**When** an executor re-estimates effort in the same Unit with a non-negative value
**Then** `ReEstimated` is emitted
**And** replayed state updates Estimated and derived Remaining consistently with existing Done.

**Given** a re-estimate uses a different Unit after the first estimate
**When** `ReEstimate` is handled
**Then** the command is rejected as a domain rejection
**And** the Work Item's Unit remains unchanged.

**Given** an executor changes Priority or Due Date
**When** `RescheduleWorkItem` is handled
**Then** `WorkItemRescheduled` is emitted
**And** replayed state reflects the new Schedule facts.

**Given** no Priority or Due Date is supplied
**When** the Work Item is replayed
**Then** the Schedule remains valid
**And** the future "what's next" projection has enough data to sort the item last.

**Given** Priority is represented in v1
**When** the contract is inspected
**Then** Priority uses the ordered enum shape selected by architecture
**And** no routing score, escalation band, LLM confidence, or cost policy is introduced.

### Story 2.5: Complete, Cancel, Reject, and Expire Work

As an executor or coordinator,
I want Work Items to terminate through explicit domain acts,
So that completion and abnormal endings are auditable, replayable, and enforce terminal-state rules.

**Acceptance Criteria:**

**Given** an estimated Work Item reaches Remaining zero through progress
**When** state is replayed
**Then** `WorkItemCompleted` makes the item terminal
**And** later progress, schedule, assignment, or suspend commands emit an `IRejectionEvent`
**And** exact duplicate completion or terminal commands return `DomainResult.NoOp` only where `docs/lifecycle-transition-matrix.md` explicitly lists them as idempotent.

**Given** an unestimated Work Item is explicitly completed
**When** the complete act is handled
**Then** `WorkItemCompleted` is emitted
**And** the completion does not rely on the Remaining=0 rule.

**Given** a non-terminal Work Item is cancelled
**When** `CancelWorkItem` is handled
**Then** `WorkItemCancelled` is emitted
**And** the item becomes terminal with no further progress accepted.

**Given** a bound executor rejects an assignment
**When** `RejectWorkItem` is handled with the default requeue behavior
**Then** `WorkItemRejected` is emitted
**And** the item returns to `Queued` for reassignment.

**Given** a bound executor rejects an assignment as non-requeuable
**When** `RejectWorkItem` is handled
**Then** `WorkItemRejected` is emitted
**And** the item becomes terminal.

**Given** expiry is Due-Date or TTL driven
**When** an expiry command is handled
**Then** `WorkItemExpired` is emitted
**And** the item becomes terminal without the aggregate reading a clock.

**Given** cancel and expire may later cascade through a Work Tree
**When** the 9-status cancel/expire transition table is reviewed
**Then** every source status has an explicit decision
**And** already-terminal descendants are defined as unaffected for downstream cascade execution.

## Epic 3: Work Tree Roll-Up and Durable Await

A coordinator can spawn child work, suspend a parent on await-conditions, resume on matching triggers, and trust recursive remaining-effort roll-up across a tenant-safe work tree.

### Story 3.1: Guard Tenant-Safe Work Tree Shape

As a coordinator,
I want Work Items to form a tenant-safe acyclic tree,
So that parent-child coordination cannot create loops, duplicate parents, or cross-tenant roll-up leaks.

**Acceptance Criteria:**

**Given** a Work Item is attached to a parent
**When** the parent-child relationship is validated
**Then** the child has at most one parent
**And** the relationship stores references by ID rather than embedding child state.

**Given** a proposed parent-child relationship would create a cycle
**When** the relationship is handled
**Then** the command is rejected as a domain rejection
**And** the existing Work Tree state is unchanged.

**Given** a proposed parent-child relationship crosses tenants
**When** the relationship is handled
**Then** the command is rejected as a domain rejection
**And** no projection or traversal can silently treat the items as same-tenant data.

**Given** a proposed relationship exceeds the configured max depth
**When** the relationship is handled
**Then** the command is rejected as a domain rejection
**And** the default max depth is documented as 32 unless overridden by tenant/type policy.

**Given** tree-shape validation is tested
**When** negative-path tests run
**Then** cycle, second-parent, cross-tenant, and max-depth cases are covered
**And** breadth is not capped by the domain guard.

### Story 3.2: Spawn Child Work from a Parent

As a coordinator,
I want a Work Item to spawn child work,
So that a larger obligation can be broken into smaller replayable obligations without losing parent context.

**Acceptance Criteria:**

**Given** a parent Work Item is eligible to spawn child work
**When** `SpawnChild` is handled
**Then** `ChildSpawned` is emitted on the parent
**And** the child creation request follows `CreateWorkItem` semantics with a parent reference.

**Given** child work is spawned
**When** the child Work Item is created
**Then** the child carries the same Tenant as the parent
**And** the parent reference is stored as a reference ID.

**Given** a parent optionally suspends while spawning a child
**When** the spawn request includes an await-on-child intent
**Then** the parent records an Await-Condition for the child completion
**And** no progress is accepted on the parent while it is Suspended.

**Given** the spawn request violates the tree guard
**When** `SpawnChild` is handled
**Then** no parent event and no child creation intent are accepted
**And** the rejection is replay-safe.

**Given** spawn behavior is tested
**When** events are replayed
**Then** parent state, child reference, and optional await-condition reconstruct deterministically.

### Story 3.3: Maintain Recursive Roll-Up with Per-Child Sequence

As an objective owner,
I want a parent Work Item to expose rolled remaining effort across its subtree,
So that I can trust the all-in remaining effort of an objective as descendants progress.

**Acceptance Criteria:**

**Given** a Work Tree has parent and child Work Items
**When** child progress, re-estimate, completion, or terminal events are projected
**Then** the parent exposes own Remaining and subtree rolled Remaining
**And** rolled Remaining equals own Remaining plus the recursive rolled Remaining of direct children.

**Given** child events are delivered more than once
**When** the Roll-Up projection processes duplicates
**Then** the projection does not double-count child contribution
**And** the projected value converges to the same result as a single delivery.

**Given** child events arrive out of order
**When** the Roll-Up projection compares child event sequences
**Then** stale or lower-sequence contributions are ignored
**And** the latest per-child contribution wins.

**Given** a child Work Item becomes terminal through completion, cancellation, rejection, or expiry
**When** the Roll-Up projection processes the terminal child event
**Then** that child contributes `0` Remaining to its ancestors
**And** replaying the terminal event does not double-subtract the contribution.

**Given** roll-up state is exposed to consumers
**When** read-model contracts are inspected
**Then** own Remaining and rolled Remaining use distinct fields or types
**And** no consumer can confuse eventual rolled Remaining with aggregate-authoritative own Remaining.

**Given** roll-up correctness is tested
**When** property-style tests permute and duplicate child events
**Then** all permutations converge to the same projection result
**And** tenant equality is asserted at every traversal hop.

### Story 3.4: Preserve Heterogeneous Unit Subtotals

As an objective owner,
I want mixed-unit work trees to show separate subtotals,
So that Works never fabricates a misleading single remaining-effort number across incompatible Units.

**Acceptance Criteria:**

**Given** a Work Tree contains only one Unit
**When** Roll-Up is projected
**Then** the subtree exposes a single rolled subtotal for that Unit.

**Given** a Work Tree contains multiple Units
**When** Roll-Up is projected
**Then** the subtree exposes one rolled subtotal per Unit
**And** no implicit conversion or summation across Units occurs.

**Given** a child changes effort through progress or re-estimate
**When** the child Unit matches its established Unit
**Then** the matching per-Unit subtotal updates incrementally.

**Given** a progress or re-estimate command carries a Unit incompatible with the child's established Unit
**When** the command is handled
**Then** the command is rejected before event emission
**And** no Roll-Up projection update is produced from that invalid act.

**Given** replay or delivery exposes an already-persisted child event whose Unit violates the child's established Unit contract
**When** the Roll-Up projection processes the event
**Then** the projection fails closed by refusing the incompatible contribution, retaining the last valid projected value or marking that Work Item projection degraded
**And** logs include only tenant, work item, event type, and sequence metadata, never payload values
**And** no mixed-unit Roll-Up view is published as fresh.

**Given** future UI surfaces need burn-down and roll-up data
**When** `RollUpView` or equivalent read models are inspected
**Then** they expose labeled per-Unit subtotals
**And** they do not expose a coerced all-unit total.

### Story 3.5: Suspend and Resume on Await-Conditions

As a coordinator,
I want a Work Item to suspend on one or more Await-Conditions and resume on the first matching trigger,
So that long-running work can park safely until a child completes, a date arrives, or an external signal is received.

**Acceptance Criteria:**

**Given** an `InProgress` Work Item
**When** it is suspended with one or more Await-Conditions
**Then** `WorkItemSuspended` records each Await-Condition kind and correlation key
**And** the item transitions to `Suspended`.

**Given** a Work Item is `Suspended`
**When** progress is reported before a matching resume
**Then** the progress command is rejected
**And** current Remaining still participates in Roll-Up.

**Given** a resume command carries a correlation key matching one current Await-Condition
**When** `ResumeWorkItem` is handled
**Then** `WorkItemResumed` is emitted with the consumed Await-Condition key
**And** the item transitions back to `InProgress`
**And** all Await-Conditions from that suspension are cleared.

**Given** a `ResumeWorkItem` command carries no key matching the current Await-Condition set while the item is `Suspended`
**When** the command is handled
**Then** the command emits a domain rejection
**And** the item remains `Suspended`.

**Given** a `ResumeWorkItem` command repeats the consumed key from the accepted `WorkItemResumed` event
**When** the duplicate command is handled after the item has already resumed
**Then** the command returns `DomainResult.NoOp`
**And** no duplicate `WorkItemResumed` event is emitted.

**Given** child-completion resumes are required
**When** a child completes
**Then** the pure reactor translation can produce a parent `ResumeWorkItem` command intent for matching child-completion Await-Conditions
**And** the aggregate, not the reactor, decides whether the resume is accepted.

**Given** date and external resumes are required seams
**When** the contracts are inspected
**Then** `DateReached` and `ExternalSignal` Await-Condition cases exist
**And** the aggregate never reads a clock or calls an external adapter.

### Story 3.6: Cascade Terminal Work Through Active Descendants

As a coordinator,
I want cancellation and expiry of parent work to cascade through still-active descendants,
So that an open subtree cannot keep burning down after its parent has terminated.

**Acceptance Criteria:**

**Given** a parent Work Item is cancelled or expired
**When** descendants are still active
**Then** the cascade process can issue terminal command intents for those descendants
**And** the descendants apply their own transition rules through the aggregate.

**Given** a descendant is already terminal
**When** a parent cancellation or expiry cascade is processed
**Then** the descendant is unaffected
**And** no duplicate terminal event is emitted.

**Given** cascade terminal commands are delivered more than once to the same descendant
**When** the descendant aggregate handles a duplicate cancel or expire command for the already-applied terminal outcome
**Then** the command is idempotent according to `docs/lifecycle-transition-matrix.md`
**And** no duplicate terminal event is emitted.

**Given** the reactor translates parent terminal events
**When** its pure translation is tested
**Then** it emits only mechanical command intents
**And** it does not decide domain outcomes that belong in `Handle`.

**Given** Story 3.6 scope is reviewed
**When** cascade ownership is checked
**Then** the story covers aggregate transition behavior, idempotent target commands, tenant-safe descendant selection contracts, and pure mechanical command intents
**And** it does not implement Dapr dispatch, checkpoint persistence, AppHost restart recovery, reminder reconciliation, or Aspire recovery proof.

**Given** a parent and descendant belong to different tenants
**When** cascade traversal is attempted
**Then** tenant equality checks fail closed
**And** no cross-tenant terminal command is produced.

## Epic 4: Shared Work Execution and Builder Runtime Validation

Teams, agents, and external parties can share one executor model: assign, reassign, claim, and hand off work through the same Party binding, while builders can validate the complete command/event pipeline under the Aspire host.

### Story 4.1: Bind Work to a Uniform Party Executor

As a Hexalith builder,
I want every executor to be represented by one `ExecutorBinding`,
So that system agents, internal users, and external parties use the same domain model.

**Acceptance Criteria:**

**Given** the executor binding contract is inspected
**When** it is used by Work Item commands, events, state, and read models
**Then** it contains `PartyId`, `Channel`, and `AuthorityLevel`
**And** it does not contain an executor-kind-specific subtype or branch discriminator.

**Given** a Work Item is assigned to a system, internal user, or external party
**When** the binding is persisted
**Then** the same value-object shape is used for all three cases
**And** the only variation is field values such as Party ID, Channel, and AuthorityLevel.

**Given** `AuthorityLevel` is carried in v1
**When** create, assign, or reassign events are replayed
**Then** the AuthorityLevel is preserved in state and read models
**And** no v1 behavior branches on AuthorityLevel.

**Given** future UI surfaces need a single Party chip treatment
**When** read-model contracts are inspected
**Then** executor kind, Channel, and AuthorityLevel are exposed as data
**And** no separate model is required for bot, human, or external executor presentation.

**Given** architecture-fitness tests run
**When** domain code is scanned
**Then** there is no branch on executor kind
**And** no LLM, routing, email, MCP, UI, or security adapter is introduced for executor binding.

### Story 4.2: Assign, Reassign, and Hand Off Work

As a coordinator,
I want to assign and hand off work through one operation,
So that moving work between a bot, a colleague, or an external party is symmetric and auditable.

**Acceptance Criteria:**

**Given** a Work Item can accept assignment
**When** `AssignWorkItem` is handled with an `ExecutorBinding`
**Then** `WorkItemAssigned` is emitted
**And** replayed state contains the supplied binding.

**Given** a Work Item is already assigned
**When** `AssignWorkItem` is handled with a different `ExecutorBinding`
**Then** the same command path handles reassignment
**And** no executor-kind-specific handoff command is required.

**Given** work is handed off from a human executor to a system executor or back
**When** events are replayed
**Then** the latest binding is authoritative for future executor acts
**And** the event history preserves each handoff as raw-act evidence.

**Given** an assigned Work Item is returned to the shared pool
**When** it is requeued
**Then** `WorkItemQueued` is emitted
**And** the item becomes claimable according to lifecycle rules.

**Given** assignment is attempted from a terminal state
**When** the command is handled
**Then** the command emits an `IRejectionEvent`
**And** no binding mutation occurs after terminal closure.

### Story 4.3: Claim Queued Work with Single-Claim-Wins

As an executor,
I want to claim work from a shared queue,
So that system agents and people can pull from the same backlog without double ownership.

**Acceptance Criteria:**

**Given** a Work Item is `Queued`
**When** an executor claims it with an `ExecutorBinding`
**Then** `WorkItemClaimed` is emitted
**And** the item transitions to `InProgress` bound to the claimant.

**Given** two executors race to claim the same `Queued` Work Item
**When** both commands use the same expected version
**Then** exactly one claim succeeds
**And** the loser receives an observable domain rejection such as `ClaimRejected` or `ConcurrencyRejected`.

**Given** a Work Item is not `Queued`
**When** an executor attempts to claim it
**Then** the command is rejected as not claimable
**And** no binding or status change occurs.

**Given** claim eligibility filtering is deferred to Theme 4
**When** v1 claim behavior is inspected
**Then** any executor in the tenant may claim a queued item
**And** no routing score, eligibility engine, escalation ladder, or AI decision record is implemented.

**Given** claim behavior is tested
**When** deterministic concurrency tests run
**Then** they prove single-claim-wins through expected-version conflict rather than timing-dependent thread races.

### Story 4.4: Resolve the Tenant's What's Next Queue

As an executor,
I want to ask what work is next for a tenant,
So that assigned and claimable work can be ordered without introducing a routing engine.

**Acceptance Criteria:**

**Given** a tenant has `Queued` and `Assigned` Work Items
**When** the "what's next" query is executed
**Then** the query returns only that tenant's eligible queued and assigned items
**And** query-side authorization/result filtering is applied in addition to tenant scoping.

**Given** returned Work Items have Priority and Due Date values
**When** the query orders results
**Then** it sorts by Priority, then earliest Due Date, then creation order
**And** items with neither Priority nor Due Date sort last.

**Given** returned Work Items include burn-down, status, and executor data
**When** read-model contracts are inspected
**Then** they expose status, own Remaining, rolled Remaining where available, executor binding fields, and await-condition data without UI-specific types.

**Given** projection updates occur
**When** Work Item events change queue eligibility or ordering
**Then** the projection emits change notifications or uses the substrate notifier seam so future SignalR surfaces can update live
**And** no web shell, DataGrid, MCP, chatbot, or email surface is built in v1.

**Given** cross-tenant data exists with colliding IDs or similar schedules
**When** the query is executed for one tenant
**Then** no item from another tenant is returned
**And** logs do not expose payloads or personal data.

### Story 4.5: Prove the Command/Event Pipeline Under Aspire

As a Hexalith builder,
I want an Aspire-hosted proof of the Works command/event pipeline,
So that I can verify the kernel, projections, and substrate wiring without shipping production adapters.

**Acceptance Criteria:**

**Given** the Works AppHost is started for local testing
**When** topology is inspected
**Then** it wires Works, ServiceDefaults, EventStore dependencies, projection infrastructure, and Dapr components needed for command/event tests
**And** it does not expose production UI, MCP, chatbot, email, routing, cost, or security-hardening adapters.

**Given** the command/event pipeline is exercised under Aspire
**When** the sequence create → progress → spawn child → suspend → resume → complete runs
**Then** events persist before publication
**And** state and projections converge to the expected result.

**Given** integration and smoke tests run
**When** configured v1 test lanes complete
**Then** Tier-1 tests remain pure and do not require Aspire
**And** Aspire is used only for boundary/runtime proof.

**Given** observability is inspected
**When** pipeline errors occur
**Then** failures surface with correlation and tenant context
**And** logs avoid event payloads, personal data, secrets, raw tokens, and full command bodies.

### Story 4.6: Prove Reminder and Reactor Recovery

As a Hexalith builder,
I want reminder and reactor recovery proved separately from the core pipeline,
So that date resumes and cascade continuation survive restarts without making the kernel depend on clocks or infrastructure.

**Acceptance Criteria:**

**Given** a date-based Await-Condition exists
**When** the Dapr actor reminder fires
**Then** the adapter issues a `ResumeWorkItem` command with the deterministic await-condition key
**And** the aggregate remains clock-free.

**Given** a date-based reminder is registered more than once for the same Work Item and Await-Condition
**When** reminder registration is retried
**Then** the reminder name is deterministic
**And** duplicate registration does not produce duplicate accepted resume events.

**Given** the AppHost restarts while date-based resumes are pending
**When** recovery runs
**Then** reminder reconciliation re-scans pending `DateReached` Await-Conditions
**And** firings lost before recording are reissued as idempotent resume commands.

**Given** Story 3.6 provides pure cascade command intents and idempotent target commands
**When** the reactor runtime dispatches cascade commands
**Then** Story 4.6 owns at-least-once dispatch, checkpoint persistence, checkpoint replay, and AppHost restart proof
**And** checkpoint state is persisted after each target command attempt or at a documented safe boundary.

**Given** the reactor restarts during cascade processing
**When** checkpoint replay resumes under Aspire
**Then** outstanding descendants still requiring termination are discovered from a re-readable projection
**And** already-terminal descendants are not terminated again
**And** the test proves convergence after a mid-cascade restart without adding clock, Dapr, or infrastructure dependencies to the kernel.

**Given** reactor translation is tested
**When** parent terminal events or child-completion events are processed
**Then** the reactor emits only mechanical command intents
**And** all domain decisions still round-trip through aggregate `Handle`.

### Story 4.7: Trigger Reactor Translators from the Live Event Stream

_Added by the 2026-07-21 correct-course: the cascade dispatcher, child-completion resume
translator, and cascade checkpoint replay proved by Stories 3.6/4.6 have no production
trigger — the host maps no event consumption, so no reactor-driven behavior executes in the
live topology (audit findings F-RT-1, F-RT-2, F-RT-4, F-RT-7)._

As a Hexalith builder,
I want the running Works host to consume the domain event stream and drive the reactor translators,
So that cascade and child-completion resume actually execute in the live topology instead of only in component tests.

**Acceptance Criteria:**

**Given** the Works host runs under the Aspire topology
**When** a parent Work Item reaches `Cancelled` or `Expired`
**Then** an at-least-once event consumption path invokes the cascade dispatcher
**And** active descendants receive idempotent terminal commands discovered from a re-readable projection
**And** already-terminal descendants are identified from the persisted roll-up read model, not hardcoded as active.

**Given** a parent is suspended on a `ChildCompleted` await-condition
**When** the child completes in the running topology
**Then** a `WorkItemCompleted` consumer feeds the unchanged `ChildCompletionResumeTranslator` from a re-readable awaiting-parents source
**And** the parent resumes via an idempotent `ResumeWorkItem` submission
**And** every decision still round-trips through aggregate `Handle`.

**Given** the host crashes mid-cascade
**When** it restarts
**Then** a startup recovery pass discovers incomplete cascade checkpoints from a durable index and drives checkpoint replay
**And** the cascade converges without duplicate terminal effects (live SM-1b lane).

**Given** the new consumption path is inspected
**When** fitness and governance tests run
**Then** the kernel and reactor projects remain free of any new dependency
**And** the host contains no shadow-kernel conditional a pure `Handle` could not have produced.

### Story 4.8: Register and Reconcile Date Reminders Durably

_Added by the 2026-07-21 correct-course: no suspend-time reminder registration exists, the
reconciliation stream scan uses a gateway route that unconditionally rejects tenant-wide reads,
and reconciliation is off unless tenants are hand-configured — so no date-based resume executes
in the live topology (audit findings F-RT-3, F-RT-5)._

As a Hexalith builder,
I want date reminders registered when an item suspends and reconciled from a working pending-await source,
So that date-based resumes execute in steady state and survive recovery without hand configuration.

**Acceptance Criteria:**

**Given** an item suspends with a `DateReached` await-condition in the running topology
**When** the event path observes the suspension
**Then** a self-targeted durable Dapr reminder is registered with the deterministic name
**And** duplicate registration remains idempotent
**And** the item resumes when the date fires without requiring a host restart.

**Given** the reconciliation-on-recovery pass runs
**When** pending `DateReached` awaits are scanned
**Then** the scan reads a tenant-scoped pending-date-await index read model maintained by the projection dispatcher plus per-aggregate stream reads
**And** it never issues the tenant-wide null-aggregate stream read the gateway rejects.

**Given** the host restarts after reminder firings were lost before recording
**When** recovery completes
**Then** overdue awaits are reissued as idempotent resume commands and future awaits are re-registered
**And** reconciliation operates without per-tenant hand configuration (live SM-1 lane).

**Given** the kernel is inspected
**When** fitness tests run
**Then** `Handle` and the reactor remain clock-free
**And** all time enters as commands from the reminder adapter.
