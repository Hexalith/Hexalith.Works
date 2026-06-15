---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
lastStep: 8
status: 'complete'
completedAt: '2026-06-14'
inputDocuments:
  - '_bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/brief.md'
  - '_bmad-output/planning-artifacts/briefs/brief-works-2026-06-14/addendum.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md'
  - '_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/DESIGN.md'
  - '_bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md'
  - '_bmad-output/brainstorming/brainstorming-session-2026-06-14-0910.md'
  - 'Hexalith.Projects/_bmad-output/project-context.md (project context — ecosystem conventions)'
workflowType: 'architecture'
project_name: 'Hexalith.Works'
user_name: 'Administrator'
date: '2026-06-14'
---

# Architecture Decision Document — Hexalith.Works

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

## Project Context Analysis

### Requirements Overview

**Functional Requirements (25 FRs across 7 feature groups):**

| Feature group | FRs | Architectural meaning |
|---|---|---|
| 4.1 Work Item Aggregate & State | FR-1–5 | A single event-sourced aggregate root owning obligation, executor binding, unit-tagged burn-down, schedule, status, parent/children refs, await-conditions; everything else is a Reference Value Object (correlation ID). |
| 4.2 Lifecycle State Machine & Domain Events | FR-6–10 | A pure, explicit state machine (9 statuses); each transition a past-tense raw-act Domain Event; illegal transitions are `IRejectionEvent` domain rejections, not exceptions; completion = `Remaining 0`. Frozen v1 event catalog (14 events), additively extensible. |
| 4.3 Effort Burn-Down & Recursive Roll-Up | FR-11–13 | Eventually-consistent, idempotent Roll-Up projection (`rolled = own + Σ rolled(children)`) on substrate projection infra; per-Unit subtotals (no cross-Unit coercion); acyclic single-parent single-tenant tree, bounded depth. |
| 4.4 Suspend / Resume Saga | FR-14–16 | Durable saga; `Handle` is clock-free — date/timer and external resumes enter as commands from adapters; resume keyed to an Await-Condition and idempotent; child-completion + date native, external signal via correlation-key port (deferred adapter). |
| 4.5 Executor Binding ("everything is a Party") | FR-17–19 | One value object `PartyId + Channel + AuthorityLevel`; assign/reassign/handoff = one uniform operation; push+pull coexist with single-claim-wins; AuthorityLevel carried-not-enforced in v1. |
| 4.6 Thin-Core Boundaries & Module Ports | FR-20–23 | "What's next" query projection; correlation-ID references to Parties/Conversations/EventStore/Tenants/Commons; ports `IExpectationResolver` (no-LLM impl) + `IExecutorRouter` (abstraction only); a written boundary decision record is a v1 deliverable. |
| 4.7 Aspire Test Host & Harness | FR-24–25 | Repository-specific Aspire AppHost (ServiceDefaults/health/telemetry); Tier-1 pure tests; integration tests use substrate fakes/Aspire topology only at real boundaries. No production adapters. |

**Non-Functional Requirements (architecture drivers):**

- **Tenant isolation — mandatory, every layer**: identity, state keys, projection keys, queries, logs (`{tenant}:{domain}:{aggregateId}`); query-side authorization/result filtering required *in addition to* command-side checks. Negative-path tests for both cross-tenant and query-side paths.
- **Event-sourcing invariants**: persist-then-publish; `Handle(...)` pure → returns domain results/events; `Apply(...)` mutates only in-memory state; rejections are events, infra failures are exceptions/dead-letter; Works returns payloads only (EventStore owns envelope metadata).
- **Concurrency**: single-writer / optimistic-concurrency per Work Item; concurrent conflicting commands (e.g. two claims) → one success, rest domain-rejected; no lost updates. (Mechanism is an architecture decision; behavior is a v1 requirement.)
- **Projections rebuildable**: Roll-Up and "what's next" derive purely from event streams; replayable from scratch; hold no authoritative state.
- **Domain purity**: domain assembly takes no infra and no LLM/cost/routing dependency; `Handle` reads no clock/external system.
- **Observability & privacy**: structured logging only — never log payloads, personal data, secrets, or full command bodies; errors via ProblemDetails/RFC 9457 with correlation/tenant context.
- **Performance (qualitative for v1)**: incremental projection updates (no whole-stream re-read per query); no numeric budgets pinned — acceptance is build-signal based (SM-1…SM-5).

**Scale & Complexity:**

- Primary domain: **backend / event-sourced .NET 10 domain library + Aspire host** (headless kernel; no v1 UI or channel adapters).
- Complexity level: **medium overall, high architectural rigor** — small public surface deliberately constrained by enterprise-grade substrate invariants; counter-metrics SM-C1 ("don't grow the kernel") and SM-C2 ("don't over-fit to deferred themes") are explicit guardrails against accidental scope.
- Estimated architectural components (ecosystem package layout): **Contracts** (events/commands/value objects) · **Server** (aggregate + handlers + ports + no-LLM resolver) · **Projections** (Roll-Up + "what's next") · **Aspire/AppHost** (topology) · **Testing** (fakes/builders).

### Technical Constraints & Dependencies

**Inherited substrate (hard constraints, not open design space):**
- .NET 10 (`global.json` SDK `10.0.300`, `rollForward: latestPatch`); C# nullable + implicit usings + warnings-as-errors; central NuGet package management (`Directory.Packages.props`).
- **Dapr is the only permitted infrastructure abstraction** in domain services — no direct Redis/PostgreSQL/Cosmos/broker clients in Contracts/Client/domain.
- **EventStore** foundation: canonical `{tenant}:{domain}:{aggregateId}` identity; persist-then-publish; EventStore owns envelope metadata.
- **`Hexalith.PolymorphicSerializations`** for event/command payloads; `System.Text.Json` conventions.
- **Additive, serialization-tolerant schema evolution only** — no `V2` event types; every event ever produced must remain backward-compatibly deserializable.
- Naming: file-scoped namespaces under `Hexalith.*`; commands imperative (no `Command` suffix); events past-tense (no `Event` suffix); prefer sealed records; `Async` suffix; `_camelCase` fields; `I`-prefixed interfaces.
- Repo discipline: umbrella repo, root submodules only (never `--recursive`); Works holds **domain code only** — the Aspire host is the one acceptable technical component here.

**Sibling-module dependencies (referenced by correlation ID, never copied):**
- Identity → `Hexalith.Parties` (`PartyId`) · Dialogue → `Hexalith.Conversations` (`ConversationId`) · Persistence/events → `Hexalith.EventStore` · Isolation → `Hexalith.Tenants` (`TenantId`) · IDs → `Hexalith.Commons`.

**Open questions explicitly deferred to this architecture phase (PRD §13):**
1. Aggregate-ID derivation (Commons helper; caller- vs system-assigned).
2. Priority representation (enum vs numeric band) backing FR-4/FR-20.
3. Optimistic-concurrency mechanism (ETag/version) realizing §9 concurrency + single-claim-wins.
4. Timer/scheduler adapter raising date/timer resume commands (FR-15) and its delivery guarantees.
5. Projection rebuild/replay operational story for Roll-Up and "what's next".
6. Validation domains (`ProgressReported` deltas, Unit immutability, Due-Date/TTL config source).

### Cross-Cutting Concerns Identified

- **Tenant isolation** — enforced at aggregate identity, state/projection keys, queries (incl. result filtering), and logs.
- **Concurrency & idempotency** — single-writer/optimistic per aggregate; single-claim-wins; resume idempotent against state; substrate offset dedup so replays don't double-count.
- **Projection consistency & rebuild** — eventual consistency, incremental updates, full replayability; no authoritative read-side state.
- **Domain purity via ports** — `IExpectationResolver` / `IExecutorRouter` keep LLM/cost/routing in adapters; clock/external triggers enter as commands.
- **Additive schema evolution & serialization** — `PolymorphicSerializations`; tolerant deserialization; no breaking event changes.
- **Observability & privacy** — structured logging, no sensitive payloads; RFC 9457 ProblemDetails with correlation/tenant context.
- **Seam preservation (designed-for, not built)** — ports, raw-act audit model, cost-ready burn-down, AuthorityLevel field — preserved without speculative machinery (SM-C2).

### Architectural Risk & Assumption Stress-Test (pre-decision)

_Derived from an advanced-elicitation pass (Assumption Audit · Pre-mortem · Cascading-Failure · Second-Order · Inversion) and a four-voice architect roundtable (Winston / Amelia / Murat / Dr. Quinn). These are constraints the architecture must satisfy and risks it must carry into the decisions that follow._

**Load-bearing invariants (honor when deciding):**

1. **Roll-Up = idempotent per-child accounting keyed by child event *sequence*, never additive deltas and never clock/arrival order.** The projection stores each child's latest rolled contribution as `(childId → lastObservedSequence, value)`; lower-sequence (stale/replayed) writes are ignored. Normative recursive invariant: `rolled-Remaining(node) = own-Remaining(node) + Σ last-known per-child rolled-Remaining, recursively`. Out-of-order and at-least-once redelivery tests are mandatory (SM-2).
2. **Concurrency is two separate worlds.** Write-path: **expected-version optimistic concurrency** on append — single-claim-wins resolves as exactly one version-conflict loser, which receives an observable `IRejectionEvent`. Read-path: projections take **no locks**; they reconcile idempotently and order-tolerantly. Do not put a version check on a projection.
3. **Authority split on the numbers (type-separated).** Own-Remaining and Status (including the `Done = Remaining 0 → Completed` transition) are **aggregate-authoritative and synchronous**; only **rolled-Remaining is an eventually-consistent projection** — a projection never flips status. The two numbers must not share a type, field name, or serialized shape, so no consumer can gate control flow on the eventual value.
4. **A reactor / process-manager (event → command) is a real v1 component — and it lives OUTSIDE the kernel.** The kernel emits events and accepts commands and references no adapter. The reactor drives the two inherently multi-aggregate, non-atomic flows: child-completion → parent-resume (FR-15) and cascade cancel/expire → descendants (FR-10). Its hard contract is **at-least-once delivery + idempotent target commands + a checkpoint** so cascade is resumable (driven off a re-readable "descendants still needing cancel" projection, not an in-memory loop).
5. **The reactor is mechanical — no shadow kernel (collapses SM-C1 + SM-C2 into one leverage point).** The reactor contains no conditional a pure `Handle` could not have produced; every *decision* round-trips through the aggregate. This single falsifiable rule is the highest-leverage defense against kernel growth, because the kernel will be tempted to grow *at the reactor* and call it "just orchestration."
6. **Cascade correctness is a function of the per-state cancel/expire transition table** (cancelling an already-`Completed` child is a real domain decision, defined for each of the 9 states before the reactor can safely cascade). Idempotency on the command-*emit* side is a distinct problem from idempotency on the projection side; both are required.
7. **Time is a domain invariant currently delegated to infrastructure.** A clock-free `Handle` cannot distinguish "expired" from "not yet told it expired" — expiry is a property of *the timer adapter having fired*. v1 decision to record explicitly: **deadlines are advisory-until-fired**; the kernel may hold a "live" item that is, in reality, overdue, and no v1 query detects this without the timer. Re-validate against Theme 5 (cost-aware scheduling) before building, since retrofitting a logical clock is a redesign, not a patch.
8. **The timer/scheduler adapter is a partial SPOF for date-based resumes** and must be **durable + reconciliation-on-recovery** (at-least-once + idempotent resume; on restart, re-scan `DateReached` await-conditions for firings lost before they were recorded).
9. **Clock-free purity needs a mechanical test gate**, not a convention: no `DateTime.Now/UtcNow`, `DateTimeOffset.Now`, `Stopwatch`, `ITimer`, RNG, or I/O in `Works.Server` / `Works.Projections` (and the reactor's `react(event) → command[]` is **also pure**). Expiry/TTL enters only as a command.
10. **Tenant isolation in the roll-up requires more than key-prefixing.** Key-prefixing protects storage access, not tree traversal: parent/child references must be **tenant-closed**, and the roll-up must **assert tenant-equality at every hop** (turning a silent cross-tenant leak into a loud failure). Rebuild must be **online, per-tenant, non-blocking**.
11. **Projection rebuild must be online / non-disruptive** (shadow + atomic swap or versioned), producing state identical to a cold rebuild with no leak of partial state to readers.

**Inversion guardrails (anti-patterns that violate the success metrics):** additive roll-up totals (SM-2); timer/cascade/ranking/cost logic inside the domain assembly (SM-C1); infra/LLM type references from Contracts/Server (SM-4); clock/RNG in `Handle` *or* the reactor (SM-1); `switch (binding.Kind)` anywhere (SM-3); key-prefix-only tenant reads (isolation).

**Risk register (to carry into design + test strategy):**

| ID | Risk | Primary gate |
|---|---|---|
| RR-1 | Stale-write / out-of-order roll-up corruption (silent) | Property test (FsCheck): any permutation + duplication of a child-event multiset converges to identical state — fixed seed, build-gate |
| RR-2 | Mid-cascade / mid-reactor-step crash inconsistency | Chaos / crash-injection at each step boundary in the Aspire host (integration-gate); add **SM-1b: mid-reactor-step crash converges** |
| RR-3 | Double-claim on a Queued item | Deterministic version-conflict test (same expected version → one commits, loser gets observable rejection event); not a thread-race |
| RR-4 | Cross-tenant roll-up leak via recursive traversal | Mutation-validated negative tests (delete the isolation check → test goes red); seed colliding IDs in the other tenant |
| RR-5 | Purity / clock / identity / no-branch erosion over time | Architecture fitness functions (banned-symbol analyzer + no-branch-on-executor-kind), run every build |
| RR-6 | Serialization back-compat ("no V2 / tolerant evolution") unfalsifiable | Golden-payload corpus + round-trip contract test; start the corpus in v1 even near-empty |

**Test-type taxonomy (set up front):** *unit* (pure `Handle`/`Apply`) · *property* (roll-up convergence, claim idempotence) · *architecture-fitness* (SM-3 zero-branching, SM-4 purity, clock-free) · *contract* (serialization back-compat; Dapr pub/sub envelope) · *integration/topology* (Aspire host: persist-then-publish seam) · *chaos* (crash-at-step-boundary; rebuild-while-live). SM-1/SM-2 are scenario acceptance tests; SM-3/SM-4 are continuous fitness functions; **SM-C2 is a review-gate, not a build-gate** (you cannot unit-test "we didn't build too much").

**Open decisions carried into the decision steps (record, resolve later):**

- **D-1 Reactor placement** — confirmed direction: *outside the kernel* (Aspire host / adapter layer); the kernel references no reactor/timer/external adapter. (Strong roundtable consensus; pending user confirmation.)
- **D-2 Claim cardinality** — is "claim a Queued item" a **single-aggregate** operation under one optimistic-concurrency check (clean deterministic loser), or does it also write a separate queue/index aggregate (re-introduces multi-aggregate non-atomicity → inherits RR-2 crash semantics)?
- **D-3 Deadline semantics & AuthorityLevel** — is a deadline a **domain truth** (then design the logical-clock seam now) or an **adapter event** (then accept "advisory-until-fired" in writing)? Relatedly: does **any v1 behavior branch on `AuthorityLevel`**? If not, state explicitly that it is carried additively for deferred themes (SM-C2 honesty).
- **D-4 Unverified substrate premise** — confirm the **Dapr per-aggregate ordering + at-least-once** guarantees for the chosen broker before the convergence/idempotency proofs are meaningful.

## Starter Template Evaluation

### Primary Technology Domain

Backend / event-sourced **.NET 10 domain library + .NET Aspire test host** (headless kernel; no
v1 UI or channel adapters). The stack is fully dictated by the Hexalith ecosystem — this step
selects a **scaffolding strategy / pattern-donor**, not a greenfield boilerplate. No open
language/framework/database/cloud decisions exist.

### Starter Options Considered

- **Hexalith.Parties (selected as pattern-donor)** — the closest sibling: an EventStore-based
  domain module with the richest canonical layout (Contracts · Server · Projections · Testing ·
  AppHost · ServiceDefaults) and the Party model Works references.
- **Hexalith.Tenants** — secondary reference; same foundation but no `.Projections` project, and
  Works is projection-heavy (Roll-Up + "what's next").
- **`dotnet new` from scratch** — rejected; re-derives the build infrastructure, packaging,
  analyzers, and conventions that `Hexalith.Builds` already provides.
- **Third-party boilerplate** — rejected; irrelevant to a pinned .NET 10 / Dapr / EventStore
  ecosystem.

### Selected Starter: Hexalith canonical domain-module layout via `Hexalith.Builds`, donor = `Hexalith.Parties`

**Rationale for Selection:**
Works is scaffolded at the umbrella-repo root using the ecosystem's shared MSBuild infrastructure
(`Hexalith.Builds`), mirroring the canonical layout of `Hexalith.Parties` but taking only the
**reduced v1 subset** needed for a headless kernel. This keeps Works structurally identical to its
siblings (developer familiarity, machine-checkable dependency direction, central package
management) while honoring SM-C1 (don't grow the kernel) and SM-C2 (don't over-fit to deferred
themes) by **not** scaffolding any UI/channel/portal/security surface.

**v1 project set (create these):**

| Project | Role | In v1? |
|---|---|---|
| `Hexalith.Works.Contracts` | Events, commands, value objects (ExecutorBinding, effort Meter, AwaitCondition), Reference Value Objects, port interfaces — low-dependency, no infra | ✅ |
| `Hexalith.Works.Server` | Aggregate `Handle`/`Apply`, lifecycle state machine, no-LLM `IExpectationResolver` impl, domain services | ✅ |
| `Hexalith.Works.Projections` | Roll-Up (per-child-sequence accounting) + "what's next" query | ✅ |
| `Hexalith.Works.Testing` | Fakes/builders: `InMemoryEventLog`, `ReorderingProjectionDriver`, `RollUpProjectionBuilder` (tenant-required) | ✅ |
| `Hexalith.Works.AppHost` + `Hexalith.Works.ServiceDefaults` | Aspire topology + service defaults/health/telemetry for manual + automated tests | ✅ |
| `.Client` | Consumer-facing integration | ◐ minimal/optional |
| `.UI` / `.Mcp` / `.AdminPortal` / `.ConsumerPortal` / `.Picker` / `.Security` | Channel & surface adapters | ❌ Themes 3–6 |

**Reactor placement note (ties to open decision D-1):** the reactor / process-manager lives
**outside the kernel** — in `Hexalith.Works.AppHost`/adapter layer (or a dedicated adapter
project), never in `Server` or `Contracts`. `Server`/`Projections` stay clock-free and infra-free.

**Repo scaffolding (umbrella root, mirror siblings):** `global.json` (SDK pinned),
`Directory.Build.props`/`.targets`, `Directory.Packages.props`, `Directory.Solution.props`/`.targets`,
`Hexalith.Works.slnx`, `aspire.config.json`, `package.json` + `release.config.cjs`
(semantic-release + commitlint), `MSBuild.rsp`; `src/`, `tests/`, `samples/`. Shared deps come from
the root submodules (`Hexalith.Builds`, `Hexalith.EventStore`, `Hexalith.Commons`,
`Hexalith.PolymorphicSerializations`, `Hexalith.Parties`, `Hexalith.Conversations`,
`Hexalith.Tenants`).

**Inherited stack versions — verified current against the live repo (2026-06-14), not the
month-old `project-context.md` snapshot:**

| Component | Pin (align to current sibling pins) | Note |
|---|---|---|
| .NET SDK | `10.0.301`, `rollForward: latestPatch` | global.json |
| Dapr | `1.18.2` | only permitted infra abstraction |
| .NET Aspire | `13.4.3` | released 2026-06-01; test host only |
| xUnit | v3 `3.2.2` + Microsoft.Testing.Platform | match siblings (v3, not v2) |
| Serialization | `Hexalith.PolymorphicSerializations` | event/command payloads |
| Fluent UI Blazor | `5.0.0-rc.3` | inherited but **unused in v1** (headless); still RC/high-risk |

Versions are **ecosystem-pinned by policy** ("do not casually upgrade"); Works aligns to the
**current** sibling pins via central package management, not to the older project-context snapshot.

**Note:** Project initialization using this scaffolding should be the **first implementation
story** (and is a precondition for SM-1/SM-4 green-build-under-Aspire).

## Core Architectural Decisions

### Decision Priority Analysis

**Critical Decisions (block implementation):**
- A1 Aggregate-ID assigned at the edge (Commons), not inside `Handle`.
- A5 Roll-Up per-child-sequence LWW data model.
- B1 Single-aggregate claim under expected-version concurrency.
- B2 No reliance on pub/sub ordering (idempotent, order-tolerant projections).
- C1 Reactor lives outside the kernel; C2 Dapr actor reminders for date resumes.

**Important Decisions (shape the architecture):**
- A2 Priority = ordered enum; A3 Unit immutable; A4 cost-ready `Meter`.
- B3 Authority split (synchronous own-Remaining/Status vs eventual rolled-Remaining).
- C3 Deadlines advisory-until-fired; C4 await-condition discriminated set.
- D2 Tenant-equality asserted at every roll-up hop; E1 online per-tenant rebuild.

**Deferred Decisions (Themes 3–6, seams only):**
- AI-inferred Expectation / magic links / NL parsing (Theme 3) — seam: `IExpectationResolver`, `ExternalSignal` await-kind, Channel.
- Executor routing/escalation (Theme 4) — seam: `IExecutorRouter` port, push/pull states, `AuthorityLevel` field, additive assignment events.
- Cost meter + spend governance (Theme 5) — seam: cost-ready `Meter` + reusable roll-up.
- Security hardening (Theme 6) — seam: raw-act event model, idempotency, `AuthorityLevel`.

### Data Architecture

- **Event-sourced on `Hexalith.EventStore`** (pre-decided). Canonical identity **`{tenant}:work:{workItemId}`**; persist-then-publish; EventStore owns envelope metadata; payloads via `Hexalith.PolymorphicSerializations`; additive/serialization-tolerant evolution (no `V2`).
- **A1 — Aggregate-ID derivation:** assigned at the command-creation edge via **`Hexalith.Commons`** ID helper and passed into `CreateWorkItem`; `Handle` never generates IDs. *Rationale:* keeps `Handle` pure, makes replay deterministic, enables idempotent create (client retry → same ID). *Affects:* Contracts (command shape), all test builders.
- **A4 — Burn-Down:** `Meter(Unit, Estimated, Done)` with derived `Remaining` (never < 0); one **Effort** meter in v1; a parallel **Cost** meter reuses the identical type (Theme 5). *Affects:* Contracts, Server, Projections.
- **A3 — Unit:** per-item value object, **immutable after first estimate**; `ProgressReported`/`ReEstimated` must carry the same Unit or are rejected; mixed-Unit roll-up exposes **per-Unit subtotals**, never a coerced single figure.
- **A2 — Priority:** small **ordered enum** (`Critical/High/Normal/Low`), additive-tolerant; backs "what's next" ordering (Priority → Due Date → creation order; none sorts last). *Rationale:* YAGNI vs numeric routing bands (Theme 4, SM-C2).
- **A5 — Roll-Up projection:** per-child contribution keyed by **`(childId, childEventSequence)`**, last-write-wins, recursive (`rolled = own + Σ per-child rolled`), per-Unit, **idempotent + order-tolerant** (stale/lower-sequence writes ignored; replays don't double-count). Built on EventStore projection infra (CachingProjectionActor, ETag actors, notifiers). *Validates SM-2; mitigates RR-1.*
- **B3 — Consistency split (type-separated):** own-Remaining + Status are **aggregate-authoritative and synchronous** (including `Done = Remaining 0 → Completed`); **rolled-Remaining is an eventually-consistent projection** with a distinct type/field/serialized shape so no consumer can gate control flow on it.

### Authentication & Security

- **D2 — Tenant isolation (mandatory, every layer):** identity/state/projection keys, queries, logs all tenant-scoped; **query-side authorization is a distinct control from key-prefixing**; the roll-up **asserts tenant-equality at every hop** (parent/child references are tenant-closed); single-tenant tree enforced at spawn. *Negative-path tests (mutation-validated) required (RR-4).*
- **D1 — `AuthorityLevel`:** ordered set `{Read, Contribute, Coordinate, Administer}` **carried on the binding but not enforced in v1 — no v1 behavior branches on it.** Recorded explicitly as an additive seam for Themes 4/6 (SM-C2 honesty); additive-tolerant so behavior can attach later without a `V2`.
- **Identity/Authn** themselves are referenced from `Hexalith.Parties`/`Hexalith.Tenants` (correlation IDs), never re-implemented. Real auth (step-up, signed links) is Theme 6.

### API & Communication Patterns

- **Public surface = the domain contract** (events, commands, value objects, ports) — no production channel adapter in v1. Errors via **ProblemDetails / RFC 9457** with correlation/tenant context; domain rejections are `IRejectionEvent` (never exceptions).
- **B1 — Concurrency & claim:** commands against one Work Item serialize via the **single-writer actor + expected-version** (EventStore ETag/version) optimistic concurrency. **Claim is a single-aggregate operation** on the `WorkItem`; the claimable pool is a **read projection**, not an authoritative queue aggregate. Two racing claims → exactly one commits, the loser receives a domain rejection. *Resolves D-2; avoids multi-aggregate non-atomicity.*
- **B2 — Delivery posture:** Dapr pub/sub is **at-least-once, not ordered** — Works does **not** rely on broker ordering. Write-path ordering comes from the single-writer actor; read-path correctness comes from idempotent, order-tolerant projections (A5) + substrate offset dedup. *Resolves D-4.*
- **C1 — Reactor / process-manager:** lives **outside the kernel** (adapter near AppHost). Mechanical **event→command translation only — no shadow-kernel logic** (every decision round-trips through a pure `Handle`). Contract: at-least-once delivery + **idempotent target commands** + **checkpoint-driven, resumable cascade** (cascade reads a re-readable "descendants still needing cancel" projection, not an in-memory loop). *Resolves D-1; mitigates RR-2; the single highest-leverage SM-C1/SM-C2 guard.*
- **C4 — Await-condition & resume:** discriminated value `{ ChildCompleted(childId) | DateReached(instant) | ExternalSignal(correlationId) }`; a suspended item holds a **set** and resumes on **first match**; resume is **idempotent** (key no longer matching = no-op; duplicate = no-op). v1 satisfiers: child-completion (reactor), date (reminder, below), external (generic command; concrete adapter deferred to Theme 3).
- **Ports:** `IExpectationResolver` (no-LLM impl shipped) and `IExecutorRouter` (abstraction only, no impl wired). Domain references no LLM/cost/routing/infra type.

### Frontend Architecture

- **Not applicable in v1** — Works is a headless domain kernel; no production UI/channel adapters ship (UX `DESIGN.md`/`EXPERIENCE.md` design the Theme 3–6 horizon through `Hexalith.FrontComposer`, but v1 builds none of it). The kernel only keeps projections **SignalR-ready** (live-update friendly) without shipping a surface.

### Infrastructure & Deployment

- **C2 — Timer/scheduler adapter:** **Dapr actor reminders via the Scheduler service** (Dapr ≥ 1.15 default; Works on 1.18.2). A `WorkItem` parked on `DateReached` registers a **self-targeted, durable reminder**; on fire it raises an internal `ResumeWorkItem(date)` command — `Handle` never reads a clock. Durable across crash/restart by construction; **reconciliation-on-recovery** covers firings lost before being recorded. The general Jobs API is *not* needed in v1 (cross-service scheduling is deferred). *Resolves OQ-4.*
- **C3 — Deadline semantics:** **adapter event, "advisory-until-fired"** — the kernel may hold a "live" item that is, in reality, overdue; no v1 query detects this without the timer firing. Recorded; **re-validate against Theme 5** (cost-aware scheduling) before that theme builds, since adding a logical clock later is a redesign.
- **E1 — Projection rebuild:** **online / non-disruptive** (shadow projection + atomic swap, or versioned projection key), **per-tenant partitionable** so one large tenant's rebuild doesn't block others; produces state identical to a cold rebuild with no partial-state leak to readers. *Resolves OQ-5.*
- **E2 — Validation domains:** `ProgressReported` delta ≥ 0 with `Remaining` clamped ≥ 0; `Estimated` ≥ 0; Unit immutable after first set; Due-Date/TTL sourced from **per-work-type/tenant policy** (configurable default). *Resolves OQ-6.*
- **Aspire host:** repository-specific AppHost + ServiceDefaults (health/telemetry) for manual + automated tests; **clock-free purity + no-branch-on-executor-kind enforced as build-time architecture fitness functions** (RR-5). Versions inherit the current sibling pins (SDK 10.0.301 · Dapr 1.18.2 · Aspire 13.4.3 · xUnit v3 3.2.2) via central package management.

### Decision Impact Analysis

**Implementation sequence:**
1. **Scaffold** the module (step-3 layout) — precondition for any green build.
2. **Contracts** — value objects (`ExecutorBinding`, `Meter`, `AwaitCondition`, Reference Value Objects, Priority enum), v1 event catalog (14 events, each carrying `(AggregateId, Sequence)`), commands, port interfaces, `IRejectionEvent` types.
3. **Server** — `WorkItem` aggregate `Handle`/`Apply`, 9-state machine + per-state cancel/expire table, no-LLM `IExpectationResolver`, expected-version append.
4. **Projections** — Roll-Up (per-child-sequence LWW) + "what's next"; tenant-equality assertions; rebuild support.
5. **Testing** — `InMemoryEventLog`, `ReorderingProjectionDriver`, `RollUpProjectionBuilder` (tenant-required); property/architecture-fitness gates.
6. **AppHost + reactor + timer adapter** — wire reminders, cascade reactor, Aspire topology; SM-1/SM-1b durability tests.

**Cross-component dependencies:**
- A1 (ID at edge) gates every Contracts command shape **and** all test builders.
- A5 + B2 (per-child-sequence, order-tolerant) require every domain event to carry `(AggregateId, Sequence)` — a Contracts-level `[KERNEL+]` decision.
- B1 (expected-version) gates the aggregate append contract — decide before writing `Handle`.
- C1/C2 (reactor + reminders outside the kernel) keep Server/Projections clock-free and infra-free — protects SM-C1/SM-4.
- D2 (tenant-equality at every hop) couples Projections to the tree-shape guard in Server.

## Implementation Patterns & Consistency Rules

### Pattern Categories Defined

**Critical conflict points identified:** ~12 areas where AI agents could make divergent choices.
Most generic web concerns (DB casing, REST routes, JSON wrappers) are **not applicable** (headless
event-sourced kernel) or **pre-locked** by `.editorconfig` + the ecosystem `project-context.md`.
The rules below are the **Works-specific** consistency contract; ecosystem rules (file-scoped
namespaces, sealed records, `_camelCase`, `Async` suffix, central package management) are inherited
verbatim and not restated.

### Naming Patterns

**Namespaces & files:** file-scoped namespaces matching folder path under `Hexalith.Works.*`
(`Hexalith.Works.Contracts`, `.Server`, `.Projections`, `.Testing`); one public type per file,
file named after the type.

**Commands** — imperative, **no `Command` suffix**, sealed records:
`CreateWorkItem`, `AssignWorkItem`, `ClaimWorkItem`, `ReportProgress`, `ReEstimate`,
`RescheduleWorkItem`, `SpawnChild`, `SuspendWorkItem`, `ResumeWorkItem`, `CancelWorkItem`,
`RejectWorkItem`, `ExpireWorkItem`.

**Events** — past-tense, **no `Event` suffix**, sealed records (the frozen v1 catalog, 14):
`WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`, `ProgressReported`,
`ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`, `WorkItemSuspended`, `WorkItemResumed`,
`WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`. Rejection events
implement `IRejectionEvent` (e.g. `WorkItemTransitionRejected`, `ClaimRejected`,
`ConcurrencyRejected`).

**Value objects:** `WorkItemId`, `ExecutorBinding`, `Channel`, `AuthorityLevel`, `Priority`,
`Unit`, `Meter`, `AwaitCondition` (+ cases `ChildCompleted`/`DateReached`/`ExternalSignal`),
Reference Value Objects `PartyId`/`ConversationId`/`TenantId`.

**Identity & derived keys** — everything derives from canonical `{tenant}:work:{workItemId}`:
state keys, projection keys, pub/sub topics, **actor-reminder names**, SignalR groups, log scopes.
No agent invents a parallel key scheme. Reminder name = a deterministic function of
`(workItemId, awaitConditionKey)` so it is idempotently (re)registerable.

### Structure Patterns

**Package boundaries & dependency direction (machine-checkable):**
`Contracts` (events/commands/value objects/ports — low-dependency, **no infra, no LLM**) ← `Server`
(domain behavior) ← `Projections` (read side). `Testing` references the above. `AppHost` /
`ServiceDefaults` / **reactor + timer adapter** sit at the edge and reference inward; **nothing in
`Server`/`Projections` references an adapter, a clock, Dapr, or an LLM type.**

**Tests:** in `Hexalith.Works.Testing` (reusable fakes/builders) and per-project `tests/` (xUnit
**v3** + Shouldly + NSubstitute). Tier-1 (`Handle`/`Apply`, projection handlers, validators) is
**pure** — no Dapr/Aspire/network/containers. Fakes/builders (`InMemoryEventLog`,
`ReorderingProjectionDriver`, `RollUpProjectionBuilder`) before any new test double.

### Format Patterns

**Event payload = the Raw Act, verbatim** — store reported values, never interpreted/derived ones
(interpretation is a Projection). Every domain event carries **`(AggregateId, Sequence)`** for
order-tolerant projections. The acting Party + timestamp come from the binding + EventStore
envelope — **Works never populates envelope metadata.**

**`DomainResult` never mixes** success and rejection payloads. Rejections are events, not
exceptions; infrastructure failures are exceptions/dead-letter.

**Serialization:** `Hexalith.PolymorphicSerializations` for every event/command; `System.Text.Json`
conventions; additive, tolerant evolution only (**no `V2`**); start a **golden-payload corpus** in
v1 so back-compat is falsifiable.

**Burn-Down numbers:** `Meter(Unit, Estimated, Done)` → derived `Remaining` (clamped ≥ 0);
mixed-Unit roll-up → **per-Unit subtotals**, never a coerced single number. **Authoritative
own-Remaining and eventual rolled-Remaining are distinct types** — never interchangeable.

**Errors:** ProblemDetails / **RFC 9457** with correlation + tenant context.

### Communication Patterns

**Event flow is persist-then-publish.** `Handle(state, command) → events` (pure); projection/state
`Apply(...)` mutates only in-memory state. No publish before persistence succeeds.

**Reactor pattern (outside the kernel):** `react(event) → command[]` is **mechanical and pure** —
event-to-command translation only. **No conditional a pure `Handle` could not have produced**
(every decision round-trips through the aggregate). Targets are **idempotent**; cascade is
**checkpoint-driven** off a re-readable "descendants still needing cancel" projection.

**Await/resume:** a Suspended item holds a **set** of `AwaitCondition`s and resumes on **first
match**; `ResumeWorkItem(correlationKey)` is **idempotent** (no current match = no-op; duplicate =
no-op). Date resumes arrive only as commands from the reminder adapter — never a clock read.

**Executor binding ("everything is a Party"):** assign/reassign/handoff/claim use the identical
command path; **zero branching on executor kind** — no `switch (binding.Kind)` / `if channel ==`
anywhere in the domain. The only variation is field values on `ExecutorBinding`.

**Reference, never copy:** identities/dialogue/persistence/isolation/IDs are correlation IDs
resolved on demand from the owning sibling module. LLM/cost/routing live behind ports
(`IExpectationResolver`, `IExecutorRouter`), never in the domain.

### Process Patterns

**Domain purity:** `Handle` and `Apply` (and the reactor) read **no clock, no RNG, no I/O, no
external system**; IDs are supplied at the edge (Commons). Enforced as a build-time fitness
function (banned-symbol analyzer over `Server`/`Projections`).

**Concurrency & idempotency:** writes use **expected-version** optimistic concurrency on append
(loser → `IRejectionEvent`); reads/projections take **no locks** and are **idempotent +
order-tolerant** (per-child-sequence LWW + offset dedup). Single-claim-wins is a single-aggregate
operation.

**Tenant scoping (every layer):** every command, query, key, projection, and log is tenant-scoped;
**query-side authorization is enforced in addition to key-prefixing**; the roll-up asserts
tenant-equality at every hop. Negative-path tests are mandatory.

**Logging/privacy:** structured logging only — **never** log event payloads, personal data,
secrets, raw tokens, or full command bodies.

### Enforcement Guidelines

**All AI agents MUST:**
- Keep `Handle`/`Apply`/reactor pure (no clock/RNG/I/O); take IDs as input.
- Carry `(AggregateId, Sequence)` on every event; store the raw act; keep success/rejection
  payloads separate; rejections implement `IRejectionEvent`.
- Use per-child-sequence LWW for roll-up (never additive deltas); assert tenant-equality per hop.
- Never branch on executor kind; never reference a clock/Dapr/LLM/infra type from
  `Contracts`/`Server`/`Projections`.
- Register every new event/command with `PolymorphicSerializations`; evolve additively (no `V2`);
  extend the golden-payload corpus.

**Pattern enforcement (build gates):** architecture-fitness tests (purity/banned-symbols,
no-branch-on-kind, dependency-direction); property tests (roll-up convergence under
permutation+duplication); mutation-validated cross-tenant negative tests; golden-payload contract
tests. SM-C2 ("don't over-fit deferred themes") is a **review-gate**, not a build-gate. Pattern
changes are recorded in this document and `project-context.md`.

### Pattern Examples

**Good:**
- `public sealed record ProgressReported(WorkItemId AggregateId, long Sequence, decimal DoneDelta, Unit Unit, string? Note) : IDomainEvent;`
- Roll-up: `contributions[childId] = (childSequence, childRolledRemaining); rolled = own + contributions.Values.Sum(...)` — stale `childSequence` ignored.
- Claim race: both append at expected version N → one commits, the other emits `ClaimRejected`.

**Anti-patterns:**
- `var id = Guid.NewGuid();` inside `Handle` · `if (DateTime.UtcNow > dueDate)` inside the domain.
- `parentRemaining += delta;` in the roll-up.
- `switch (binding.Kind) { case Bot: … case Human: … }`.
- Reactor deciding "is this the last child?" itself instead of letting `Handle` decide.
- Logging the full command body or event payload.

## Project Structure & Boundaries

### Complete Project Directory Structure

The Works domain module is created **at the umbrella-repo root**, alongside the dependency
submodules (which are not modified). New files/dirs only:

```
works/                                        # umbrella repo root = Hexalith.Works
├── global.json                               # SDK 10.0.301, rollForward latestPatch, MTP runner
├── Directory.Build.props / .targets          # walk-up; import Hexalith.Builds shared config
├── Directory.Packages.props                  # central versions, aligned to current sibling pins
├── Directory.Solution.props / .targets
├── Hexalith.Works.slnx
├── aspire.config.json
├── package.json / release.config.cjs / commitlint.config.mjs   # semantic-release + commitlint
├── MSBuild.rsp
├── README.md / CHANGELOG.md
├── CLAUDE.md / AGENTS.md                      # (exist)
├── docs/
│   └── boundary-decision-record.md           # FR-23 tracked deliverable (owns-vs-references)
├── Hexalith.Builds/ … Hexalith.Conversations/  # (existing root submodules = dependencies)
│
├── src/
│   ├── Hexalith.Works.Contracts/             # KERNEL · low-dependency · no infra, no LLM
│   │   ├── Commands/                          # CreateWorkItem, AssignWorkItem, ClaimWorkItem,
│   │   │                                      #   ReportProgress, ReEstimate, RescheduleWorkItem,
│   │   │                                      #   SpawnChild, SuspendWorkItem, ResumeWorkItem,
│   │   │                                      #   CancelWorkItem, RejectWorkItem, ExpireWorkItem
│   │   ├── Events/                            # 14-event v1 catalog + IRejectionEvent types
│   │   ├── ValueObjects/                      # WorkItemId, ExecutorBinding, Channel,
│   │   │                                      #   AuthorityLevel, Priority, Unit, Meter,
│   │   │                                      #   AwaitCondition{ChildCompleted|DateReached|ExternalSignal}
│   │   ├── State/                             # WorkItemState (rehydration target for Apply)
│   │   ├── Results/                           # DomainResult + rejection results
│   │   ├── Models/                            # read-model contracts: WhatsNextItem, RollUpView
│   │   └── Ports/                             # IExpectationResolver, IExecutorRouter, Expectation
│   │
│   ├── Hexalith.Works.Server/                 # KERNEL · domain behavior · PURE (no clock/RNG/IO)
│   │   ├── Aggregates/                        # WorkItem: Handle/Apply, 9-state machine,
│   │   │                                      #   per-state cancel/expire transition table, tree guard
│   │   ├── Resolvers/                         # no-LLM IExpectationResolver implementation
│   │   ├── Validation/                        # ProgressReported/ReEstimate/Unit validators
│   │   └── Registration/                      # DI/service registration extensions
│   │
│   ├── Hexalith.Works.Projections/            # KERNEL · read side · PURE handlers
│   │   ├── Abstractions/
│   │   ├── Handlers/                          # RollUpHandler, WhatsNextHandler (idempotent)
│   │   ├── Strategies/                        # per-(childId,childSequence) LWW accounting
│   │   ├── Actors/                            # caching projection actors (EventStore infra)
│   │   ├── Services/                          # rebuild (shadow+swap, per-tenant) services
│   │   └── Configuration/
│   │
│   ├── Hexalith.Works.Reactor/                # ADAPTER (outside kernel) · process-manager + timer
│   │   ├── WorkItemReactor.cs                 #   PURE react(event)→command-intent[] (unit-testable)
│   │   ├── Dispatch/                          #   at-least-once delivery + checkpoint (Dapr-bound)
│   │   ├── Cascade/                           #   checkpoint-driven resumable cancel/expire cascade
│   │   └── Timer/                             #   Dapr actor-reminder adapter → ResumeWorkItem(date)
│   │
│   ├── Hexalith.Works.ServiceDefaults/        # health/telemetry/service discovery
│   └── Hexalith.Works.AppHost/               # the one acceptable technical component here
│       ├── DaprComponents/                    # pub/sub, state store, scheduler config
│       ├── Properties/
│       └── AppHost.cs                         # wires kernel + reactor + reminders + topology
│
├── tests/
│   ├── Hexalith.Works.Testing/                # reusable: InMemoryEventLog, ReorderingProjectionDriver,
│   │                                          #   RollUpProjectionBuilder (tenant-required), WorkItemBuilder
│   ├── Hexalith.Works.UnitTests/              # Tier-1 pure: Handle/Apply, projection handlers, validators
│   ├── Hexalith.Works.PropertyTests/          # FsCheck: roll-up convergence (permutation+duplication)
│   ├── Hexalith.Works.ArchitectureTests/      # fitness: purity/banned-symbols, no-branch-on-kind, deps
│   └── Hexalith.Works.IntegrationTests/       # Aspire topology + chaos/crash-injection (SM-1/SM-1b)
│
└── samples/
    └── Hexalith.Works.SampleHost/             # manual exercise of the full lifecycle
```

`.Client` (consumer integration), `.UI`, `.Mcp`, portals, `.Security` are **deliberately absent**
in v1 (Themes 3–6; SM-C1/SM-C2).

### Architectural Boundaries

**Kernel vs adapter (the load-bearing boundary):** the **kernel** = `Contracts` + `Server` +
`Projections` — pure, no clock/RNG/I/O, no Dapr/LLM type. The **adapter ring** = `Reactor`,
`AppHost`, `ServiceDefaults` — owns all delivery, scheduling, and infrastructure. **The kernel
references no adapter.** The reactor reconciliation: its **pure translation** (`react(event) →
command-intent[]`) is unit-testable with no infra; its **runtime** (delivery, checkpoint, reminder
registration) lives in `Reactor/Dispatch`, `Reactor/Cascade`, `Reactor/Timer`, wired by `AppHost`.

**Dependency direction (machine-checkable):** `Contracts ← Server ← Projections`; `Testing →`
kernel; `Reactor → Contracts` only; `AppHost →` everything. No cycles; no inward reference to an
adapter.

**Sibling-module boundaries (referenced, never copied):** `EventStore` (persistence/events/actors/
projection infra) · `Parties` (`PartyId`) · `Conversations` (`ConversationId`) · `Tenants`
(`TenantId`, isolation) · `Commons` (ID generation) · `PolymorphicSerializations` (payloads). All
via correlation IDs resolved on demand.

**Data boundaries:** event streams + state keys + projection keys all under `{tenant}:work:{id}`;
roll-up asserts tenant-equality per hop; projections hold no authoritative state.

### Requirements to Structure Mapping

| FR group | Primary location |
|---|---|
| 4.1 Aggregate & State (FR-1–5) | `Contracts/ValueObjects` + `State`; `Server/Aggregates` |
| 4.2 Lifecycle & Events (FR-6–10) | `Contracts/Events` + `Commands`; `Server/Aggregates` (state machine + cancel/expire table) |
| 4.3 Roll-Up (FR-11–13) | `Projections/Handlers` + `Strategies`; `Server/Aggregates` (tree guard at spawn) |
| 4.4 Suspend/Resume Saga (FR-14–16) | `Contracts` (AwaitCondition, Suspend/Resume/SpawnChild); `Server/Aggregates`; `Reactor` (resume dispatch + timer) |
| 4.5 Executor Binding (FR-17–19) | `Contracts/ValueObjects` (ExecutorBinding/Channel/AuthorityLevel); `Server` |
| 4.6 Boundaries & Ports (FR-20–23) | `Contracts/Ports` + `Models`; `Server/Resolvers`; `Projections/Handlers` (WhatsNext); `docs/boundary-decision-record.md` |
| 4.7 Aspire Host (FR-24–25) | `AppHost` + `ServiceDefaults`; `tests/*` |

**Cross-cutting concerns:** tenant isolation → identity/keys/queries across `Server` + `Projections`
(+ negative tests in IntegrationTests); concurrency/idempotency → `Server` append + `Projections`
strategies; observability/privacy → all layers (structured logs, RFC 9457).

### Integration Points

**Internal (event-sourced flow):** command → `Server.Handle` (pure) → events persisted by EventStore
→ published (persist-then-publish) → `Projections` update (idempotent, order-tolerant) **and**
`Reactor` translates events → commands (child-completion→parent-resume, cascade). Date await →
`Reactor/Timer` Dapr reminder → `ResumeWorkItem`.

**External integrations:** none in v1 beyond sibling Hexalith modules (no production channel adapter).
Projections are SignalR-ready for the deferred UI horizon.

**Data flow:** raw-act event (carrying `AggregateId,Sequence`) is the source of truth; own-Remaining/
Status are synchronous on the aggregate; rolled-Remaining and "what's next" are eventual projections.

### File Organization Patterns

- **Configuration:** central (`Directory.Packages.props`, `global.json`, `Directory.Build.*`) at root;
  per-project `.csproj` carry no inline versions; Dapr/topology config in `AppHost/DaprComponents`.
- **Source:** one public type per file; folders = namespaces under `Hexalith.Works.*`.
- **Tests:** Tier-1 pure (`UnitTests`, `PropertyTests`, `ArchitectureTests`) vs boundary
  (`IntegrationTests`); reusable doubles in `Testing`.
- **Assets:** none (headless); the FR-23 boundary record + golden-payload corpus live in `docs/` and
  `tests/`.

### Development Workflow Integration

- **Run:** `dotnet run` the `Hexalith.Works.AppHost` (Aspire) to exercise the full lifecycle locally.
- **Build:** `dotnet build Hexalith.Works.slnx`; warnings-as-errors; architecture-fitness tests run
  in the build.
- **Deploy:** v1 ships no production deployment — the AppHost is a test/manual harness only (Aspire
  topology); release tooling is semantic-release + commitlint for the packages.

## Architecture Validation Results

### Coherence Validation ✅

**Decision Compatibility:** All decisions are mutually reinforcing and contradiction-free.
Event-sourcing on `EventStore` · Dapr-only infrastructure · pure kernel + adapter ring ·
per-child-sequence LWW roll-up · expected-version optimistic concurrency · Dapr actor reminders for
date resumes · explicit *do-not-rely-on-pub/sub-ordering* posture. Versions are mutually compatible
and inherited from current sibling pins (SDK 10.0.301 · Dapr 1.18.2 · Aspire 13.4.3 · xUnit v3 3.2.2).

**Pattern Consistency:** Implementation patterns (raw-act events carrying `(AggregateId, Sequence)`;
pure `Handle`/`Apply`/reactor; idempotent order-tolerant projections; zero branching on executor
kind; reference-not-copy) directly enforce the decisions. Naming follows ecosystem conventions
(imperative commands, past-tense events, sealed records).

**Structure Alignment:** The kernel (`Contracts`/`Server`/`Projections`) vs adapter ring
(`Reactor`/`AppHost`/`ServiceDefaults`) split, with machine-checkable dependency direction
(`Contracts ← Server ← Projections`; adapters reference inward; kernel references no adapter),
physically realizes domain purity (SM-4) and the don't-grow-the-kernel guard (SM-C1).

### Requirements Coverage Validation ✅

**Functional Requirements Coverage:** All 25 FRs across 7 groups have a concrete home
(see Requirements→Structure mapping). Spot checks: FR-11–13 (roll-up/tree-guard/heterogeneous-unit)
→ `Projections` per-child-sequence + per-Unit subtotals + Server tree guard; FR-17 (uniform
assign/handoff, zero branching) → `ExecutorBinding` + fitness test; FR-23 (boundary decision record)
→ `docs/boundary-decision-record.md` tracked deliverable.

**Non-Functional Requirements Coverage:** Tenant isolation (per-hop equality + query-side authz +
mutation-validated negatives) · ES invariants (persist-then-publish, pure Handle, in-memory Apply,
rejection events) · concurrency (expected-version, single-claim-wins) · rebuildable projections
(online, per-tenant) · domain purity (kernel/adapter + fitness functions) · observability/privacy
(RFC 9457, structured logs, no payloads) · performance (qualitative, incremental updates; no numeric
budgets by design — acceptance is build-signal based). All addressed.

### Implementation Readiness Validation ✅

**Decision Completeness:** All critical decisions documented with versions and rationale; the
PRD §13 open questions (OQ-1…6) and the D-1…D-4 roundtable decisions are all resolved.

**Structure Completeness:** Complete, specific directory tree (root config, `src/` 7 projects,
`tests/` 5 projects, `samples/`, `docs/`); boundaries, integration points, and FR mapping defined.

**Pattern Completeness:** Naming, structure, format, communication, and process patterns specified
with good/anti-pattern examples and build-gate enforcement (fitness functions, property tests,
mutation-validated negatives, golden-payload contract tests).

### Gap Analysis Results

**Critical Gaps:** None blocking — all critical architectural decisions are made.

**Important Gaps (resolve in the first stories, not architecture-blocking):**
- Verify the live `Hexalith.EventStore` API surface (expected-version append; `CachingProjectionActor`/
  ETag actors/notifiers; online rebuild support) matches the chosen patterns — **first-story task**.
- Enumerate the 9-state cancel/expire transition table (Server story).
- Define the reminder reconciliation-on-recovery re-scan query for `DateReached` await-conditions.
- Choose the concrete config source/mechanism for Due-Date/TTL and validation bounds (E2).

**Nice-to-Have Gaps:** seed the golden-payload corpus; benchmark harness (deferred — no v1 numeric
budgets); MCP/CLI command surfaces (Theme 2, deliberately deferred).

### Validation Issues Addressed

- D-1 reactor placement → resolved: outside kernel (pure translation in `Reactor`, runtime in
  `Reactor/Dispatch|Cascade|Timer`, wired by `AppHost`).
- D-2 claim cardinality → resolved: single-aggregate under expected-version; claimable pool is a
  read projection.
- D-3 deadline semantics + AuthorityLevel → resolved: advisory-until-fired; AuthorityLevel carried,
  not enforced, no v1 branch.
- D-4 Dapr ordering → resolved by verification: at-least-once not ordered → projections idempotent +
  order-tolerant; write-order from the single-writer actor.

### Architecture Completeness Checklist

**Requirements Analysis**
- [x] Project context thoroughly analyzed
- [x] Scale and complexity assessed
- [x] Technical constraints identified
- [x] Cross-cutting concerns mapped

**Architectural Decisions**
- [x] Critical decisions documented with versions
- [x] Technology stack fully specified
- [x] Integration patterns defined
- [x] Performance considerations addressed

**Implementation Patterns**
- [x] Naming conventions established
- [x] Structure patterns defined
- [x] Communication patterns specified
- [x] Process patterns documented

**Project Structure**
- [x] Complete directory structure defined
- [x] Component boundaries established
- [x] Integration points mapped
- [x] Requirements to structure mapping complete

### Architecture Readiness Assessment

**Overall Status:** READY FOR IMPLEMENTATION (all 16 checklist items `[x]`; no Critical Gaps open;
the four Important Gaps are first-story verification tasks, not architecture blockers).

**Confidence Level:** High — with the single explicit early-verification item being the
`Hexalith.EventStore` API surface, which the design depends on but was not verified against live
package APIs in this session.

**Key Strengths:**
- A genuinely thin, pure, event-sourced kernel with a machine-checkable kernel/adapter boundary.
- The hard event-sourcing traps are pre-solved: idempotent per-child-sequence roll-up, expected-
  version single-claim-wins, clock-free saga with durable Dapr reminders, online per-tenant rebuild.
- "Everything is a Party" enforced as a fitness function (zero branching), not a hope.
- SM-C1/SM-C2 collapsed into one falsifiable rule: the reactor stays mechanical (no shadow kernel).

**Areas for Future Enhancement:**
- Theme 3–6 adapters (LLM interaction, routing, cost, security) on the laid seams.
- Numeric performance budgets + benchmark harness once usage shape is known.

### Implementation Handoff

**AI Agent Guidelines:**
- Follow all architectural decisions exactly as documented; treat the Implementation Patterns &
  Consistency Rules as binding.
- Keep the kernel pure; keep the reactor mechanical; never branch on executor kind; carry
  `(AggregateId, Sequence)` on every event; roll up by per-child-sequence LWW.
- Respect the kernel/adapter boundary and dependency direction; reference siblings by correlation ID.

**First Implementation Priority:**
Scaffold the module per *Project Structure* (donor = `Hexalith.Parties`, via `Hexalith.Builds`),
**and as part of that first story verify the `Hexalith.EventStore` API surface** supports
expected-version append, the projection infrastructure, and online rebuild. This scaffold is the
precondition for SM-1/SM-4 (green build + green tests under Aspire).
