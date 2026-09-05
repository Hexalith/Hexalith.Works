---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
lastStep: 8
status: 'complete'
completedAt: '2026-06-14'
updatedAt: '2026-09-05'
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
| 4.2 Lifecycle State Machine & Domain Events | FR-6–10 | A pure, explicit state machine (9 statuses); each transition a past-tense raw-act Domain Event; illegal transitions are `IRejectionEvent` domain rejections, not exceptions; completion = `Remaining 0`. The success-event catalog contains 15 events after the additive FR-21 correction. |
| 4.3 Effort Burn-Down & Recursive Roll-Up | FR-11–13 | Eventually-consistent, idempotent Roll-Up projection (`rolled = own + Σ rolled(children)`) on substrate projection infra; per-Unit subtotals (no cross-Unit coercion); acyclic single-parent single-tenant tree, bounded depth. |
| 4.4 Suspend / Resume Saga | FR-14–16 | Durable saga; `Handle` is clock-free — date/timer and external resumes enter as commands from adapters; resume keyed to an Await-Condition and idempotent; child-completion + date native, external signal via correlation-key port (deferred adapter). |
| 4.5 Executor Binding ("everything is a Party") | FR-17–19 | One value object `PartyId + Channel + AuthorityLevel`; assign/reassign/handoff = one uniform operation; push+pull coexist with single-claim-wins; AuthorityLevel carried-not-enforced in v1. |
| 4.6 Thin-Core Boundaries & Module Ports | FR-20–23 | "What's next" query projection; correlation-ID references to Parties/Conversations/EventStore/Tenants/Commons; additive `LinkConversation`/`ConversationLinked` support; ports `IExpectationResolver` (no-LLM impl) + `IExecutorRouter` (abstraction only); a written boundary decision record is a v1 deliverable. |
| 4.7 Platform-Hosted Runtime Test Harness | FR-24–25 | Canonical minimal EventStore domain-service host in Works; platform-owned Aspire topology and ServiceDefaults; Tier-1 pure tests; integration tests use substrate fakes/platform topology only at real boundaries. No production adapters. |

**Non-Functional Requirements (architecture drivers):**

- **Tenant isolation — mandatory, every layer**: identity, state keys, projection keys, queries, logs (`{tenant}:{domain}:{aggregateId}`); query-side authorization/result filtering required *in addition to* command-side checks. Negative-path tests for both cross-tenant and query-side paths.
- **Event-sourcing invariants**: persist-then-publish; `Handle(...)` pure → returns domain results/events; `Apply(...)` mutates only in-memory state; rejections are events, infra failures are exceptions/dead-letter; Works returns payloads only (EventStore owns envelope metadata).
- **Concurrency**: single-writer / optimistic-concurrency per Work Item; concurrent conflicting commands (e.g. two claims) → one success, rest domain-rejected; no lost updates. (Mechanism is an architecture decision; behavior is a v1 requirement.)
- **Projections rebuildable**: Roll-Up and "what's next" derive purely from event streams; replayable from scratch; hold no authoritative state. Readers remain on the prior generation until atomic Commit; ordinary projection delivery is quiesced or platform-fenced during shared inventory capture through Commit, then catches up.
- **Domain purity**: domain assembly takes no infra and no LLM/cost/routing dependency; `Handle` reads no clock/external system.
- **Observability & privacy**: structured logging only — never log payloads, personal data, secrets, or full command bodies; errors via ProblemDetails/RFC 9457 with correlation/tenant context.
- **Performance (qualitative for v1)**: incremental projection updates (no whole-stream re-read per query); no numeric budgets pinned — acceptance is build-signal based (SM-1…SM-5).

**Scale & Complexity:**

- Primary domain: **backend / event-sourced .NET 10 domain library + minimal EventStore domain-service host** (headless kernel; no Works-owned Aspire host and no v1 UI or channel adapters).
- Complexity level: **medium overall, high architectural rigor** — small public surface deliberately constrained by enterprise-grade substrate invariants; counter-metrics SM-C1 ("don't grow the kernel") and SM-C2 ("don't over-fit to deferred themes") are explicit guardrails against accidental scope.
- Estimated architectural components (ecosystem package layout): **Contracts** (events/commands/value objects) · **Server** (aggregate + handlers + ports + no-LLM resolver) · **Projections** (Roll-Up + "what's next") · minimal **domain-service executable** · **Testing** (fakes/builders). Aspire topology and ServiceDefaults are external platform concerns.

### Technical Constraints & Dependencies

**Inherited substrate (hard constraints, not open design space):**
- .NET 10 (`global.json` SDK `10.0.300`, `rollForward: latestPatch`); C# nullable + implicit usings + warnings-as-errors; central NuGet package management (`Directory.Packages.props`).
- **Dapr is the only permitted infrastructure abstraction** in domain services — no direct Redis/PostgreSQL/Cosmos/broker clients in Contracts/Client/domain.
- **EventStore** foundation: canonical `{tenant}:{domain}:{aggregateId}` identity; persist-then-publish; EventStore owns envelope metadata.
- **`Hexalith.PolymorphicSerializations`** for event/command payloads; `System.Text.Json` conventions.
- **Additive, serialization-tolerant schema evolution only** — no `V2` event types; every event ever produced must remain backward-compatibly deserializable.
- Naming: file-scoped namespaces under `Hexalith.*`; commands imperative (no `Command` suffix); events past-tense (no `Event` suffix); prefer sealed records; `Async` suffix; `_camelCase` fields; `I`-prefixed interfaces.
- Repo discipline: umbrella repo, root submodules only (never `--recursive`); Works holds **domain-centric code plus the canonical minimal EventStore domain-service host**. It ships no `*.AppHost`, `*.Aspire`, or `*.ServiceDefaults` project; topology is composed by a designated platform/host repository.

**Sibling-module dependencies (referenced by correlation ID, never copied):**
- Identity → `Hexalith.Parties` (`PartyId`) · Dialogue → `Hexalith.Conversations` (`ConversationCorrelationId`) · Persistence/events → `Hexalith.EventStore` · Isolation → `Hexalith.Tenants` (`TenantId`) · IDs → `Hexalith.Commons`.

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

1. **Roll-Up = idempotent per-child accounting keyed by EventStore envelope `SequenceNumber`, never additive deltas and never clock/arrival order.** The projection stores each child's latest rolled contribution as `(childId → lastObservedEnvelopeSequence, value)`; lower-sequence (stale/replayed) writes are ignored. Normative recursive invariant: `rolled-Remaining(node) = own-Remaining(node) + Σ last-known per-child rolled-Remaining, recursively`. Out-of-order and at-least-once redelivery tests are mandatory (SM-2).
2. **Concurrency is two separate worlds.** Write-path: **ETag-backed optimistic concurrency** on the atomic actor-state save — single-claim-wins resolves as exactly one conflict loser, which receives an observable `IRejectionEvent` after retry and re-handling. Read-path: projections take **no locks**; they reconcile idempotently and order-tolerantly. Do not put a version check on a projection.
3. **Authority split on the numbers (type-separated).** Own-Remaining and Status (including the `Done = Remaining 0 → Completed` transition) are **aggregate-authoritative and synchronous**; only **rolled-Remaining is an eventually-consistent projection** — a projection never flips status. The two numbers must not share a type, field name, or serialized shape, so no consumer can gate control flow on the eventual value.
4. **A reactor / process-manager (event → command) is a real v1 component — and it lives OUTSIDE the kernel.** The kernel emits events and accepts commands and references no adapter. The reactor drives the two inherently multi-aggregate, non-atomic flows: child-completion → parent-resume (FR-15) and cascade cancel/expire → descendants (FR-10). Its hard contract is **at-least-once delivery + idempotent target commands + a checkpoint** so cascade is resumable (driven off a re-readable "descendants still needing cancel" projection, not an in-memory loop).
5. **The reactor is mechanical — no shadow kernel (collapses SM-C1 + SM-C2 into one leverage point).** The reactor contains no conditional a pure `Handle` could not have produced; every *decision* round-trips through the aggregate. This single falsifiable rule is the highest-leverage defense against kernel growth, because the kernel will be tempted to grow *at the reactor* and call it "just orchestration."
6. **Cascade correctness is a function of the per-state cancel/expire transition table** (cancelling an already-`Completed` child is a real domain decision, defined for each of the 9 states before the reactor can safely cascade). Idempotency on the command-*emit* side is a distinct problem from idempotency on the projection side; both are required.
7. **Time is a domain invariant currently delegated to infrastructure.** A clock-free `Handle` cannot distinguish "expired" from "not yet told it expired" — expiry is a property of *the timer adapter having fired*. v1 decision to record explicitly: **deadlines are advisory-until-fired**; the kernel may hold a "live" item that is, in reality, overdue, and no v1 query detects this without the timer. Re-validate against Theme 5 (cost-aware scheduling) before building, since retrofitting a logical clock is a redesign, not a patch.
8. **The timer/scheduler adapter is a partial SPOF for date-based resumes** and must be **durable + reconciliation-on-recovery** (at-least-once + idempotent resume; on restart, re-scan `DateReached` await-conditions for firings lost before they were recorded).
9. **Clock-free purity needs a mechanical test gate**, not a convention: no `DateTime.Now/UtcNow`, `DateTimeOffset.Now`, `Stopwatch`, `ITimer`, RNG, or I/O in `Works.Server` / `Works.Projections` (and the reactor's `react(event) → command[]` is **also pure**). Expiry/TTL enters only as a command.
10. **Tenant isolation in the roll-up requires more than key-prefixing.** Key-prefixing protects storage access, not tree traversal: parent/child references must be **tenant-closed**, and the roll-up must **assert tenant-equality at every hop** (turning a silent cross-tenant leak into a loud failure). Rebuild is per-tenant and reader-safe; shared rebuild delivery is quiesced or platform-fenced during capture-through-Commit.
11. **Projection rebuild must be reader-available and atomically visible at Commit.** Independent aggregate projections use EventStore's pausable checkpoint rebuild. Relationship-aware projections use the bounded `/project/rebuild/shared/v1` lifecycle over a sealed tenant inventory. Ordinary delivery is quiesced from inventory capture through Commit, or excluded by an equivalent platform fence, and catches up afterward. Readers stay on the prior generation until promotion and never observe staged or partial state.

**Inversion guardrails (anti-patterns that violate the success metrics):** additive roll-up totals (SM-2); timer/cascade/ranking/cost logic inside the domain assembly (SM-C1); infra/LLM type references from Contracts/Server (SM-4); clock/RNG in `Handle` *or* the reactor (SM-1); `switch (binding.Kind)` anywhere (SM-3); key-prefix-only tenant reads (isolation).

**Risk register (to carry into design + test strategy):**

| ID | Risk | Primary gate |
|---|---|---|
| RR-1 | Stale-write / out-of-order roll-up corruption (silent) | Property test (FsCheck): any permutation + duplication of a child-event multiset converges to identical state — fixed seed, build-gate |
| RR-2 | Mid-cascade / mid-reactor-step crash inconsistency | Chaos / crash-injection at each step boundary in the Aspire host (integration-gate); add **SM-1b: mid-reactor-step crash converges** |
| RR-3 | Double-claim on a Queued item | Deterministic ETag-conflict test (same persisted actor state → one atomic save commits, loser gets observable rejection event after retry); not a thread-race |
| RR-4 | Cross-tenant roll-up leak via recursive traversal | Mutation-validated negative tests (delete the isolation check → test goes red); seed colliding IDs in the other tenant |
| RR-5 | Purity / clock / identity / no-branch erosion over time | Architecture fitness functions (banned-symbol analyzer + no-branch-on-executor-kind), run every build |
| RR-6 | Serialization back-compat ("no V2 / tolerant evolution") unfalsifiable | Golden-payload corpus + round-trip contract test; start the corpus in v1 even near-empty |

**Test-type taxonomy (set up front):** *unit* (pure `Handle`/`Apply`) · *property* (roll-up convergence, claim idempotence) · *architecture-fitness* (SM-3 zero-branching, SM-4 purity, clock-free) · *contract* (serialization back-compat; Dapr pub/sub envelope) · *integration/topology* (platform-owned Aspire host: persist-then-publish seam) · *chaos* (crash-at-step-boundary; delivery-fenced shared rebuild). SM-1/SM-2 are scenario acceptance tests; SM-3/SM-4 are continuous fitness functions; **SM-C2 is a review-gate, not a build-gate** (you cannot unit-test "we didn't build too much").

**Open decisions carried into the decision steps (record, resolve later):**

- **D-1 Reactor placement** — confirmed direction: *outside the kernel* (Aspire host / adapter layer); the kernel references no reactor/timer/external adapter. (Strong roundtable consensus; pending user confirmation.)
- **D-2 Claim cardinality** — is "claim a Queued item" a **single-aggregate** operation under one optimistic-concurrency check (clean deterministic loser), or does it also write a separate queue/index aggregate (re-introduces multi-aggregate non-atomicity → inherits RR-2 crash semantics)?
- **D-3 Deadline semantics & AuthorityLevel** — is a deadline a **domain truth** (then design the logical-clock seam now) or an **adapter event** (then accept "advisory-until-fired" in writing)? Relatedly: does **any v1 behavior branch on `AuthorityLevel`**? If not, state explicitly that it is carried additively for deferred themes (SM-C2 honesty).
- **D-4 Unverified substrate premise** — confirm the **Dapr per-aggregate ordering + at-least-once** guarantees for the chosen broker before the convergence/idempotency proofs are meaningful.

## Starter Template Evaluation

### Primary Technology Domain

Backend / event-sourced **.NET 10 domain library + minimal EventStore domain-service host**
(headless kernel; no Works-owned Aspire host, v1 UI, or channel adapters). The stack is fully
dictated by the Hexalith ecosystem — this step selects the shared domain-service SDK boundary,
not a greenfield boilerplate. No open language/framework/database/cloud decisions exist.

### Starter Options Considered

- **`Hexalith.EventStore.DomainService` (selected runtime boundary)** — the authoritative domain-module
  SDK supplies standard service defaults, health/telemetry, aggregate/query/projection discovery, runtime
  activation, and canonical endpoints. Works consumes this SDK through a minimal executable.
- **Hexalith.Parties (domain-layout pattern donor only)** — useful for Contracts/Server/Testing
  conventions, but its historical AppHost/ServiceDefaults layout is not copied into Works.
- **`dotnet new` from scratch** — rejected; re-derives the build infrastructure, packaging,
  analyzers, and conventions that `Hexalith.Builds` already provides.
- **Third-party boilerplate** — rejected; irrelevant to a pinned .NET 10 / Dapr / EventStore
  ecosystem.

### Selected Starter: Hexalith canonical domain-module layout via `Hexalith.Builds` + `Hexalith.EventStore.DomainService`

**Rationale for Selection:**
Works uses the ecosystem's shared MSBuild infrastructure (`Hexalith.Builds`) and consumes
`Hexalith.EventStore.DomainService` for its runtime boundary. Domain projects retain the familiar
Contracts/Server/Projections/Testing layout, while the runnable `Hexalith.Works` executable stays at
the canonical minimal host seam. A designated platform/host repository owns Aspire topology and
ServiceDefaults. This preserves machine-checkable dependency direction and central package management
without copying generic platform plumbing into the domain module.

**v1 project set (create these):**

| Project | Role | In v1? |
|---|---|---|
| `Hexalith.Works.Contracts` | Events, commands, value objects (ExecutorBinding, effort Meter, AwaitCondition), Reference Value Objects including additive Conversation linking, port interfaces — low-dependency, no infra | ✅ |
| `Hexalith.Works.Server` | Aggregate `Handle`/`Apply`, lifecycle state machine, no-LLM `IExpectationResolver` impl, domain services | ✅ |
| `Hexalith.Works.Projections` | Roll-Up (per-child envelope-position accounting) + "what's next" query | ✅ |
| `Hexalith.Works.Reactor` | Pure event→command translators outside the kernel: child-completion→resume (`ChildCompletionResumeTranslator`) and terminal cascade→descendant cancel/expire (`TerminalCascadeTranslator`); references `Contracts` only, no dispatch/clock/infra. Realized in Epic 3 (resolves D-1). | ✅ |
| `Hexalith.Works.Testing` | Fakes/builders: `InMemoryEventLog`, `ReorderingProjectionDriver`, `RollUpProjectionBuilder` (tenant-required) | ✅ |
| `Hexalith.Works` | Minimal EventStore domain-service executable: registers the Works domain assembly with `AddEventStoreDomainService(...)` and maps it with `UseEventStoreDomainService()`. Domain-specific handlers are discovered through SDK contracts; generic Dapr, projection/query, subscription, health, and telemetry plumbing remains platform-owned. | ✅ |
| `Hexalith.Works.AppHost` + `Hexalith.Works.ServiceDefaults` | Prohibited in the Works domain module. The replacement Aspire topology and ServiceDefaults live in a designated platform/host repository. | ❌ |
| `.Client` | Consumer-facing integration | ◐ minimal/optional |
| `.UI` / `.Mcp` / `.AdminPortal` / `.ConsumerPortal` / `.Picker` / `.Security` | Channel & surface adapters | ❌ Themes 3–6 |

**Reactor placement note (ties to D-1):** the pure mechanical translators remain domain-focused and
outside the aggregate kernel; `Server`/`Projections` stay clock-free and infra-free. Runtime dispatch,
checkpointing, reminders, subscriptions, and recovery are expressed through EventStore platform seams and
composed by the platform host. Story 4.9 migrates the historical Works-owned runtime implementation to this
boundary without changing the translators or moving decisions out of `Handle`.

**Repo scaffolding (umbrella root, mirror siblings):** `global.json` (SDK pinned),
`Directory.Build.props`/`.targets`, `Directory.Packages.props`, `Directory.Solution.props`/`.targets`,
`Hexalith.Works.slnx`, `package.json` + `release.config.cjs`
(semantic-release + commitlint), `MSBuild.rsp`; `src/`, `tests/`. Shared deps come from
the root submodules (`Hexalith.Builds`, `Hexalith.EventStore`, `Hexalith.Commons`,
`Hexalith.PolymorphicSerializations`, `Hexalith.Parties`, `Hexalith.Conversations`,
`Hexalith.Tenants`).

**Inherited stack versions — verified current against the live repo (2026-06-14), not the
month-old `project-context.md` snapshot:**

| Component | Pin (align to current sibling pins) | Note |
|---|---|---|
| .NET SDK | `10.0.301`, `rollForward: latestPatch` | global.json |
| Dapr | `1.18.4` | only permitted infra abstraction |
| .NET Aspire | Platform-owned pin | The designated platform AppHost owns the Aspire SDK/integration versions; Works carries no AppHost package pin. |
| xUnit | v3 `3.2.2` + Microsoft.Testing.Platform | match siblings (v3, not v2) |
| Serialization | `Hexalith.PolymorphicSerializations` | event/command payloads |
| Fluent UI Blazor | `5.0.0-rc.3` | inherited but **unused in v1** (headless); still RC/high-risk |

Versions are **ecosystem-pinned by policy** ("do not casually upgrade"); Works aligns to the
**current** sibling pins via central package management, not to the older project-context snapshot.

**Migration note (approved 2026-09-05):** the original scaffold created Works-owned AppHost and
ServiceDefaults projects before the stricter baseline was adopted. Story 4.9 removes those projects only
after the replacement platform topology proves equivalent runtime behavior. Story 1.1 remains historical
evidence; this section is the current target architecture.

## Core Architectural Decisions

### Decision Priority Analysis

**Critical Decisions (block implementation):**
- A1 Aggregate-ID assigned at the edge (Commons), not inside `Handle`.
- A5 Roll-Up per-child envelope-position LWW data model.
- B1 Single-aggregate claim under ETag-backed optimistic concurrency.
- B2 No reliance on pub/sub ordering (idempotent, order-tolerant projections).
- C1 Reactor lives outside the kernel; C2 Dapr actor reminders for date resumes.

**Important Decisions (shape the architecture):**
- A2 Priority = ordered enum; A3 Unit immutable; A4 cost-ready `Meter`.
- B3 Authority split (synchronous own-Remaining/Status vs eventual rolled-Remaining).
- C3 Deadlines advisory-until-fired; C4 await-condition discriminated set.
- D2 Tenant-equality asserted at every roll-up hop; E1 per-tenant, reader-safe rebuild with delivery quiescence/fencing.

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
- **B1 — Concurrency & claim:** commands carry no expected-version or ETag input. EventStore serializes commands against one Work Item through its **single-writer actor + Dapr state-store ETag-backed** optimistic concurrency on the atomic actor-state save. **Claim is a single-aggregate operation** on the `WorkItem`; the claimable pool is a **read projection**, not an authoritative queue aggregate. Two racing claims → exactly one commits; EventStore retries the conflict from fresh state, and the normal retry path produces the existing `WorkItemTransitionRejected(InProgress, "Claim")`. Retry exhaustion surfaces an infrastructure `ConcurrencyConflict` with no loser append/publication/dead-letter effect. *Resolves D-2; avoids multi-aggregate non-atomicity.*
- **B2 — Delivery posture:** Dapr pub/sub is **at-least-once, not ordered** — Works does **not** rely on broker ordering. Write-path ordering comes from the single-writer actor; read-path correctness comes from idempotent, order-tolerant projections (A5) + substrate offset dedup. *Resolves D-4.*
- **C1 — Reactor / process-manager:** its domain translation lives **outside the aggregate kernel** and remains mechanical **event→command translation only — no shadow-kernel logic** (every decision round-trips through a pure `Handle`). Runtime delivery/checkpoint/subscription plumbing is supplied by EventStore platform seams and composed by the platform host. Contract: at-least-once delivery + **idempotent target commands** + **checkpoint-driven, resumable cascade** (cascade reads a re-readable "descendants still needing cancel" projection, not an in-memory loop). *Resolves D-1; mitigates RR-2; the single highest-leverage SM-C1/SM-C2 guard.*
- **C4 — Await-condition & resume:** discriminated value `{ ChildCompleted(childId) | DateReached(instant) | ExternalSignal(correlationId) }`; a suspended item holds a **set** and resumes on **first match**; resume is **idempotent** (key no longer matching = no-op; duplicate = no-op). v1 satisfiers: child-completion (reactor), date (reminder, below), external (generic command; concrete adapter deferred to Theme 3).
- **Ports:** `IExpectationResolver` (no-LLM impl shipped) and `IExecutorRouter` (abstraction only, no impl wired). Domain references no LLM/cost/routing/infra type.

### Frontend Architecture

- **Not applicable in v1** — Works is a headless domain kernel; no production UI/channel adapters ship (UX `DESIGN.md`/`EXPERIENCE.md` design the Theme 3–6 horizon through `Hexalith.FrontComposer`, but v1 builds none of it). The kernel only keeps projections **SignalR-ready** (live-update friendly) without shipping a surface.

### Infrastructure & Deployment

- **C2 — Timer/scheduler adapter:** **Dapr actor reminders via the Scheduler service** (Dapr >= 1.15 default; Works on 1.18.4). A `WorkItem` parked on `DateReached` registers a **self-targeted, durable reminder**; on fire it raises an internal `ResumeWorkItem(date)` command — `Handle` never reads a clock. Durable across crash/restart by construction; **reconciliation-on-recovery** covers firings lost before being recorded. The general Jobs API is *not* needed in v1 (cross-service scheduling is deferred). *Resolves OQ-4.*
- **C3 — Deadline semantics:** **adapter event, "advisory-until-fired"** — the kernel may hold a "live" item that is, in reality, overdue; no v1 query detects this without the timer firing. Recorded; **re-validate against Theme 5** (cost-aware scheduling) before that theme builds, since adding a logical clock later is a redesign.
- **E1 — Projection rebuild:** **per-tenant, reader-available, and atomically visible at Commit**. Independent aggregate projections use EventStore's pausable checkpoint rebuild. Relationship-aware Works projections use the bounded `/project/rebuild/shared/v1` Begin/Accumulate/Finalize/Stage/Commit lifecycle over a sealed tenant inventory. Ordinary projection delivery is quiesced from inventory capture through Commit, or excluded by an equivalent platform fence; it resumes and catches up afterward. Readers remain on the prior generation until promotion and never observe staged or partial state. *Resolves OQ-5 without promising uninterrupted projection delivery.*
- **E2 — Validation domains:** `ProgressReported` delta > 0 (zero/negative deltas rejected;
  `docs/lifecycle-transition-matrix.md` is authoritative) with `Remaining` clamped ≥ 0; `Estimated` ≥ 0; Unit immutable after first set; Due-Date/TTL sourced from **per-work-type/tenant policy** (configurable default). *Resolves OQ-6.*
- **Runtime host:** Works exposes the canonical minimal EventStore domain-service executable; a designated platform-owned AppHost + ServiceDefaults compose manual and automated integration tests. Works ships no AppHost/Aspire/ServiceDefaults project and duplicates no platform health, telemetry, Dapr, query/projection, or subscription plumbing. **Clock-free purity + no-branch-on-executor-kind are enforced as build-time architecture fitness functions** (RR-5).

### Decision Impact Analysis

**Implementation sequence:**
1. **Scaffold** the module (step-3 layout) — precondition for any green build.
2. **Contracts** — value objects (`ExecutorBinding`, `Meter`, `AwaitCondition`, Reference Value Objects, Priority enum), additive v1 catalog (15 commands + 15 state-changing success events + 10 rejection events = 40 durable types), port interfaces, and rejection payloads without state-changing `(AggregateId, Sequence)` fields.
3. **Server** — `WorkItem` aggregate `Handle`/`Apply`, 9-state machine + per-state cancel/expire table, no-LLM `IExpectationResolver`; EventStore owns ETag-backed atomic persistence.
4. **Projections** — Roll-Up (per-child envelope-position LWW) + "what's next"; tenant-equality assertions; rebuild support.
5. **Testing** — `InMemoryEventLog`, `ReorderingProjectionDriver`, `RollUpProjectionBuilder` (tenant-required); property/architecture-fitness gates.
6. **Platform runtime composition** — expose domain handlers through the EventStore SDK; wire reminders, cascade translation, and the Works service in the designated platform AppHost; run SM-1/SM-1b durability tests there.

**Cross-component dependencies:**
- A1 (ID at edge) gates every Contracts command shape **and** all test builders.
- A5 + B2 (per-child envelope-position, order-tolerant) use EventStore envelope `SequenceNumber` as the canonical persisted and projection-delivery position. State-changing Works payloads additionally carry `(AggregateId, Sequence)`, where payload `Sequence` is the state-changing ordinal; rejection payloads remain frozen without either field.
- B1 (ETag-backed atomic persistence) gates the aggregate concurrency contract — decide before writing `Handle`.
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
`CreateWorkItem`, `AssignWorkItem`, `QueueWorkItem`, `ClaimWorkItem`, `ReportProgress`, `ReEstimate`,
`RescheduleWorkItem`, `SpawnChild`, `SuspendWorkItem`, `ResumeWorkItem`, `CompleteWorkItem`,
`CancelWorkItem`, `RejectWorkItem`, `ExpireWorkItem`, `LinkConversation`.

**Events** — past-tense, **no `Event` suffix**, sealed records (the additive v1 success catalog, 15):
`WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`, `ProgressReported`,
`ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`, `WorkItemSuspended`, `WorkItemResumed`,
`WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`,
`ConversationLinked`. Rejection events implement `IRejectionEvent`; the additive link operation adds
`WorkItemConversationLinkRejected` for a conflicting relink. Infrastructure retry exhaustion remains
`ConcurrencyConflict` and is not a domain event.

**Value objects:** `WorkItemId`, `ExecutorBinding`, `Channel`, `AuthorityLevel`, `Priority`,
`Unit`, `Meter`, `AwaitCondition` (+ cases `ChildCompleted`/`DateReached`/`ExternalSignal`),
Reference Value Objects `PartyId`/`ConversationCorrelationId`/`TenantId`.

**Identity & derived keys** — everything derives from canonical `{tenant}:work:{workItemId}`:
state keys, projection keys, pub/sub topics, **actor-reminder names**, SignalR groups, log scopes.
No agent invents a parallel key scheme. Reminder name = a deterministic function of
`(workItemId, awaitConditionKey)` so it is idempotently (re)registerable.

### Structure Patterns

**Package boundaries & dependency direction (machine-checkable):**
`Contracts` (events/commands/value objects/ports — low-dependency, **no infra, no LLM**) ← `Server`
(domain behavior) ← `Projections` (read side). `Testing` references the above. The minimal
`Hexalith.Works` EventStore domain-service executable and the pure reactor translators reference
inward; the designated platform host owns topology, ServiceDefaults, delivery, scheduling, and
infrastructure. **Nothing in `Server`/`Projections` references an adapter, a clock, Dapr, or an LLM
type, and Works ships no AppHost/Aspire/ServiceDefaults project.**

**Tests:** in `Hexalith.Works.Testing` (reusable fakes/builders) and per-project `tests/` (xUnit
**v3** + Shouldly + NSubstitute). Tier-1 (`Handle`/`Apply`, projection handlers, validators) is
**pure** — no Dapr/Aspire/network/containers. Fakes/builders (`InMemoryEventLog`,
`ReorderingProjectionDriver`, `RollUpProjectionBuilder`) before any new test double.

### Format Patterns

**Event payload = the Raw Act, verbatim** — store reported values, never interpreted/derived ones
(interpretation is a Projection). State-changing raw-act payloads carry **`(AggregateId, Sequence)`**,
where `Sequence` is the state-changing ordinal. Frozen rejection payloads carry refusal context without
those fields. Order-tolerant projections receive the canonical EventStore envelope `SequenceNumber`;
the acting Party + timestamp also come from that envelope — **Works never populates envelope metadata.**

**`DomainResult` never mixes** success and rejection payloads. Rejections are events, not
exceptions; infrastructure failures are exceptions/dead-letter.

**Two sequence counters intentionally coexist.** EventStore envelope `SequenceNumber` is the canonical,
gapless persisted stream position used for reads, replay, and projection delivery; every persisted
success or `IRejectionEvent` consumes one. Works payload `Sequence` is only the ordinal of a
state-changing event and is copied into `WorkItemState.Sequence`. Applying a rejection is a no-op, so a
rejection at envelope position 1 followed by create at position 2 correctly yields
`WorkItemCreated.Sequence == 1`.

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

**Concurrency & idempotency:** writes use EventStore's **ETag-backed** optimistic concurrency on the
atomic actor-state save (loser → retry/re-handle → `IRejectionEvent`); reads/projections take **no
locks** and are **idempotent + order-tolerant** (per-child envelope-position LWW + offset dedup).
Single-claim-wins is a single-aggregate operation.

**Tenant scoping (every layer):** every command, query, key, projection, and log is tenant-scoped;
**query-side authorization is enforced in addition to key-prefixing**; the roll-up asserts
tenant-equality at every hop. Negative-path tests are mandatory.

**Logging/privacy:** structured logging only — **never** log event payloads, personal data,
secrets, raw tokens, or full command bodies.

### Enforcement Guidelines

**All AI agents MUST:**
- Keep `Handle`/`Apply`/reactor pure (no clock/RNG/I/O); take IDs as input.
- Treat EventStore envelope `SequenceNumber` as canonical for every persisted event and projection
  delivery. Carry payload `(AggregateId, Sequence)` only on state-changing Works events; payload
  `Sequence` is the state-changing ordinal. Keep frozen rejection payloads free of those fields, apply
  them as no-ops, and preserve them as persisted `IRejectionEvent`s.
- Use per-child envelope-position LWW for roll-up (never additive deltas); assert tenant-equality per hop.
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
- Claim race: both act from the same ETag-backed persisted state → one atomic save commits; the other
  retries against `InProgress` and persists the existing `WorkItemTransitionRejected` refusal.

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
│   │   ├── Commands/                          # CreateWorkItem, AssignWorkItem, QueueWorkItem, ClaimWorkItem,
│   │   │                                      #   ReportProgress, ReEstimate, RescheduleWorkItem,
│   │   │                                      #   SpawnChild, SuspendWorkItem, ResumeWorkItem,
│   │   │                                      #   CompleteWorkItem, CancelWorkItem, RejectWorkItem,
│   │   │                                      #   ExpireWorkItem, LinkConversation
│   │   ├── Events/                            # 15 success events + 10 IRejectionEvent types
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
│   │   ├── Strategies/                        # WorkItemRollUpProjection (per-(childId,childSequence) LWW),
│   │   │                                      #   WhatsNextQueueProjection, WhatsNextOrdering, WhatsNextQueryAuthorization
│   │   └── Models/                            # pure read-model strategy types + WhatsNextProjectionChange signal
│   │                                          # (shared rebuild is EventStore-owned; readers stay on the old
│   │                                          #  generation while delivery is quiesced/fenced until atomic commit)
│   │
│   ├── Hexalith.Works.Reactor/                # PURE (outside kernel) · mechanical event→command translators only
│   │   ├── ChildCompletionResumeTranslator.cs #   child-completion → ResumeWorkItem intent
│   │   ├── TerminalCascadeTranslator.cs       #   parent-terminal → descendant cancel/expire intents
│   │   ├── CascadeDescendant.cs / AwaitingParent.cs   #   pure value types for the translators
│   │   └── WorksReactorAssembly.cs            #   references Contracts only — no dispatch/clock/infra
│   │
│   └── Hexalith.Works/                         # canonical minimal EventStore domain-service executable
│       ├── Program.cs                         #   AddEventStoreDomainService / UseEventStoreDomainService
│       └── WorkItemEventStoreAggregate.cs     #   discovery wrapper delegating each command to the pure kernel
│
│   # No Works-owned AppHost, Aspire, ServiceDefaults, Dapr component, delivery, scheduling,
│   # query/projection plumbing, or subscription infrastructure. Story 4.9 migrates the historical
│   # adapter-edge implementation into the designated platform/host repository before removal here.
│
├── tests/
│   ├── Hexalith.Works.Testing/                # reusable: InMemoryEventLog, ReorderingProjectionDriver,
│   │                                          #   RollUpProjectionBuilder (tenant-required), WorkItemBuilder
│   ├── Hexalith.Works.UnitTests/              # Tier-1 pure: Handle/Apply, projection handlers, validators
│   ├── Hexalith.Works.PropertyTests/          # FsCheck: roll-up convergence (permutation+duplication)
│   ├── Hexalith.Works.ArchitectureTests/      # fitness: purity/banned-symbols, no-branch-on-kind, deps
│   └── Hexalith.Works.IntegrationTests/       # platform topology + chaos/crash-injection (SM-1/SM-1b)
```

`.Client` (consumer integration), `.UI`, `.Mcp`, portals, `.Security` are **deliberately absent**
in v1 (Themes 3–6; SM-C1/SM-C2).

### Architectural Boundaries

**Kernel vs platform (the load-bearing boundary):** the **kernel** = `Contracts` + `Server` +
`Projections` — pure, no clock/RNG/I/O, no Dapr/LLM type. `Reactor` contains pure mechanical
translations only, and `Hexalith.Works` is the canonical minimal EventStore domain-service host.
The designated platform/host repository owns delivery, checkpoints, subscriptions, reminders,
ServiceDefaults, Dapr components, and Aspire topology. **The kernel references no adapter.**

**Dependency direction (machine-checkable):** `Contracts ← Server ← Projections`; `Testing →`
kernel; `Reactor → Contracts` only; the minimal domain-service host references the SDK and kernel;
the external platform host composes published modules. No cycles and no inward reference to an adapter.

**Sibling-module boundaries (referenced, never copied):** `EventStore` (persistence/events/actors/
projection infra) · `Parties` (`PartyId`) · `Conversations` (`ConversationCorrelationId`) · `Tenants`
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
| 4.4 Suspend/Resume Saga (FR-14–16) | `Contracts` (AwaitCondition, Suspend/Resume/SpawnChild); `Server/Aggregates`; `Reactor` (pure translation); platform runtime (delivery + timer) |
| 4.5 Executor Binding (FR-17–19) | `Contracts/ValueObjects` (ExecutorBinding/Channel/AuthorityLevel); `Server` |
| 4.6 Boundaries & Ports (FR-20–23) | `Contracts/Ports` + `Models`; `Contracts/Commands/LinkConversation`; `Contracts/Events/ConversationLinked`; `Server/Resolvers`; `Projections/Handlers` (WhatsNext); `docs/boundary-decision-record.md` |
| 4.7 Platform-Hosted Runtime (FR-24–25) | minimal `Hexalith.Works` domain-service executable + designated external platform topology + `tests/*` |

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

**Data flow:** each persisted EventStore envelope supplies canonical `SequenceNumber` ordering and a
Works payload. State-changing raw-act payloads carry `AggregateId,Sequence`; rejection payloads retain
their frozen context-only shapes. Own-Remaining/Status are synchronous on the aggregate;
rolled-Remaining and "what's next" are eventual projections.

### File Organization Patterns

- **Configuration:** central (`Directory.Packages.props`, `global.json`, `Directory.Build.*`) at root;
  per-project `.csproj` carry no inline versions. Dapr/topology configuration belongs to the
  designated platform/host repository.
- **Source:** one public type per file; folders = namespaces under `Hexalith.Works.*`.
- **Tests:** Tier-1 pure (`UnitTests`, `PropertyTests`, `ArchitectureTests`) vs boundary
  (`IntegrationTests`); reusable doubles in `Testing`.
- **Assets:** none (headless); the FR-23 boundary record + golden-payload corpus live in `docs/` and
  `tests/`.

### Development Workflow Integration

- **Run:** start the designated platform-owned Aspire host that composes the published Works
  domain-service module; the concrete repository and command are a Story 4.9 architecture prerequisite.
- **Build:** `dotnet build Hexalith.Works.slnx`; warnings-as-errors; architecture-fitness tests run
  in the build.
- **Deploy:** v1 ships no production deployment from this domain repository. Release tooling is
  semantic-release + commitlint for its domain packages and minimal domain-service executable.

## Architecture Validation Results

### Coherence Validation ⚠️

**Decision Compatibility:** The approved correction makes the decisions mutually reinforcing:
Event-sourcing on `EventStore` · Dapr-only infrastructure · pure kernel + adapter ring ·
per-child envelope-position LWW roll-up · ETag-backed optimistic concurrency · Dapr actor reminders for
date resumes · explicit *do-not-rely-on-pub/sub-ordering* posture. Versions are mutually compatible
and inherited from current sibling pins (SDK 10.0.301 · Dapr 1.18.4 · Aspire 13.4.6 · xUnit v3 3.2.2).

**Pattern Consistency:** Implementation patterns (state-changing raw-act payloads carrying
`(AggregateId, Sequence)` ordinals; rejection payloads remaining context-only; envelope
`SequenceNumber` driving persisted order; pure `Handle`/`Apply`/reactor; idempotent order-tolerant
projections; zero branching on executor kind; reference-not-copy) directly enforce the decisions.
Naming follows ecosystem conventions (imperative commands, past-tense events, sealed records).

**Structure Alignment:** The kernel (`Contracts`/`Server`/`Projections`) and pure `Reactor`
translations remain in Works; the minimal domain-service executable exposes them through EventStore.
The designated external platform host owns runtime topology and infrastructure. Story 4.9 must migrate
the historical Works-owned hosting projects before the structure fully conforms.

### Requirements Coverage Validation ⚠️

**Functional Requirements Coverage:** All 25 FRs across 7 groups have a concrete home after adding
Story 1.5 for the missing post-creation Conversation link
(see Requirements→Structure mapping). Spot checks: FR-11–13 (roll-up/tree-guard/heterogeneous-unit)
→ `Projections` per-child envelope-position + per-Unit subtotals + Server tree guard; FR-17 (uniform
assign/handoff, zero branching) → `ExecutorBinding` + fitness test; FR-23 (boundary decision record)
→ `docs/boundary-decision-record.md` tracked deliverable.

**Non-Functional Requirements Coverage:** Tenant isolation (per-hop equality + query-side authz +
mutation-validated negatives) · ES invariants (persist-then-publish, pure Handle, in-memory Apply,
rejection events) · concurrency (ETag-backed atomic save, single-claim-wins) · rebuildable projections
(reader-available with delivery quiesced/fenced capture-through-commit, per-tenant) · domain purity
(kernel/platform boundary + fitness functions) · observability/privacy
(RFC 9457, structured logs, no payloads) · performance (qualitative, incremental updates; no numeric
budgets by design — acceptance is build-signal based). All addressed.

### Implementation Readiness Validation ⚠️

**Decision Completeness:** The four readiness conflicts are resolved in planning. One execution
prerequisite remains: the Solution Architect must name the designated platform/host repository before
Story 4.9 moves runtime topology.

**Structure Completeness:** The intended Works-owned directory tree and external platform boundary are
defined. The platform-side destination layout is deliberately deferred until its owning repository is named.

**Pattern Completeness:** Naming, structure, format, communication, and process patterns specified
with good/anti-pattern examples and build-gate enforcement (fitness functions, property tests,
mutation-validated negatives, golden-payload contract tests).

### Gap Analysis Results

**Critical Gaps:** The platform-host repository and owning team must be named before Story 4.9
implementation; removing the current Works-owned projects before equivalent topology exists is prohibited.

**Important Gaps (resolve in the corrective stories):**
- Implement Story 1.5's additive Conversation command/event/rejection catalog and lifecycle-neutral semantics.
- Migrate and prove the current runtime topology in the named platform host under Story 4.9, then remove
  the prohibited Works-owned AppHost/ServiceDefaults projects.
- Enumerate the 9-state cancel/expire transition table (Server story).
- Define the reminder reconciliation-on-recovery re-scan query for `DateReached` await-conditions.
- Choose the concrete config source/mechanism for Due-Date/TTL and validation bounds (E2).

**Nice-to-Have Gaps:** seed the golden-payload corpus; benchmark harness (deferred — no v1 numeric
budgets); MCP/CLI command surfaces (Theme 2, deliberately deferred).

### Validation Issues Addressed

- D-1 reactor placement → resolved: pure translation remains outside the kernel in Works; runtime
  delivery/checkpoint/reminder composition is owned by the designated platform host.
- D-2 claim cardinality → resolved: single-aggregate under ETag-backed optimistic concurrency; claimable pool is a
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
- [ ] External platform-host repository and destination layout named (Story 4.9 prerequisite)
- [x] Component boundaries established
- [x] Integration points mapped
- [x] Requirements to structure mapping complete

### Architecture Readiness Assessment

**Overall Status:** CONCERNS — COURSE CORRECTION APPROVED. The four planning contradictions have
explicit resolutions and backlog ownership. Implementation readiness remains conditional on naming
the target platform/host repository and completing Stories 4.9 and 1.5.

**Confidence Level:** High in the corrected boundaries and EventStore API facts; medium in migration
readiness until the platform destination and owner are recorded.

**Key Strengths:**
- A genuinely thin, pure, event-sourced kernel with a machine-checkable kernel/adapter boundary.
- The hard event-sourcing traps are pre-solved: idempotent per-child envelope-position roll-up,
  ETag-backed single-claim-wins, clock-free saga with durable Dapr reminders, and reader-available
  rebuild with delivery quiesced/fenced through atomic commit.
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
  `(AggregateId, Sequence)` ordinals only on state-changing payloads, keep rejection payloads frozen,
  and roll up accepted deliveries by EventStore envelope `SequenceNumber`.
- Respect the kernel/adapter boundary and dependency direction; reference siblings by correlation ID.

**First Implementation Priority:**
The Solution Architect first names the platform/host repository and migration owner. Then implement
Story 4.9 by reproducing and proving equivalent topology in that platform boundary before deleting the
Works-owned AppHost/ServiceDefaults projects. Story 1.5 additively implements post-creation Conversation
linking. Re-run sprint-planning readiness after both story specifications are implementation-ready.
