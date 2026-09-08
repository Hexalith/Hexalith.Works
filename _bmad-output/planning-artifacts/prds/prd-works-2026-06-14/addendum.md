---
title: "Hexalith.Works PRD — Addendum (downstream depth)"
status: final
created: 2026-06-14
updated: 2026-09-08
---

# Addendum — Hexalith.Works PRD

Technical-how depth that supports the PRD but belongs to the **architecture / solution-design phase** rather than the PRD's capability narrative. This addendum carries the **inherited substrate constraints in detail** and a **non-binding event/port sketch** to hand to the architect. For context that lives elsewhere: the brief-level competitive landscape, why-now sources, and the foundation action plan are in `../briefs/brief-works-2026-06-14/addendum.md`; the source-of-record ideation is `../../brainstorming/brainstorming-session-2026-06-14-0910.md` (44 ideas / 6 themes).

## Inherited substrate constraints (Hexalith ecosystem)

These bound the v1 requirements; the architecture phase makes them concrete. Source: `Hexalith.Projects/_bmad-output/project-context.md` and the umbrella `CLAUDE.md`.

- **Platform:** .NET 10 (`global.json` pins SDK `10.0.300`, `rollForward: latestPatch`); C# nullable + implicit usings + warnings-as-errors; central NuGet package management via `Directory.Packages.props` (versions there, not inline).
- **Infrastructure abstraction:** Dapr is the *only* permitted infrastructure abstraction in domain services — no direct Redis/PostgreSQL/Cosmos/broker clients in Contracts/Client/domain packages.
- **EventStore foundation:** canonical identity `{tenant}:{domain}:{aggregateId}` — derive actor IDs, state keys, topics, projection keys, SignalR groups, and log scopes from it. EventStore owns event envelope metadata; Works returns event *payloads* only and must never populate/spoof envelope fields. Event flow is **persist-then-publish**.
- **Domain purity:** aggregate `Handle(...)` is pure → returns domain results/events; projection/state `Apply(...)` mutates only in-memory state. Domain rejections are events implementing `IRejectionEvent`; infrastructure failures are exceptions/dead-letter paths. A `DomainResult` never mixes success and rejection payloads.
- **Schema evolution:** additive and serialization-tolerant only; **no `V2` event types**; every event ever produced must remain backward-compatibly deserializable; tolerate unknown-but-additive fields. `System.Text.Json` conventions; `Hexalith.PolymorphicSerializations` for event/command payloads.
- **Naming:** file-scoped namespaces under `Hexalith.*`; commands are imperative with no `Command` suffix (e.g., `CreateWorkItem`); events are past-tense with no `Event` suffix (e.g., `WorkItemCreated`); prefer sealed records; async methods `Async`-suffixed; private fields `_camelCase`; interfaces `I`-prefixed.
- **Package layout:** `Contracts` (events/commands/models — low-dependency, no infra) · `Server` (domain behavior: `WorkItem` + Work-Tree Registry aggregates) · `Projections` (read side) · `Reactor` (the FR-26 process manager — a domain-focused supporting library) · `Client` (consumer integration, if needed) · a minimal EventStore domain-service executable · `Testing` (reusable test utilities). Dependency direction is direct-to-Contracts (`Server → Contracts`, `Projections → Contracts`, `Reactor → Contracts`; `Projections` never references `Server`) — AD-18. Topology and ServiceDefaults are platform-owned; a Works domain module ships no `*.AppHost`, `*.Aspire`, or `*.ServiceDefaults` project. Dependency direction is strict and machine-checkable; Contracts stay low-dependency.
- **Testing:** xUnit (match the surrounding module's major version), Shouldly assertions, NSubstitute mocks; Tier-1 tests pure (no Dapr/Aspire/network/containers); EventStore/Tenants testing fakes/builders before new doubles; tenant-isolation and rejection paths need negative-path tests.
- **Repo discipline:** `works` is an umbrella repo; only root submodules are initialized (never `--recursive`, never nested submodules). Works contains domain-centric code plus the canonical minimal EventStore domain-service host; the Aspire topology lives in a designated platform/host repository.

## Non-binding event / port sketch (for the architect)

Illustrative only — the PRD's FRs are the contract; these shapes are a starting point, not a decision.

**v1 Domain Event catalog (FR-7):** `WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`, `ProgressReported`, `ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`, `WorkItemSuspended`, `WorkItemResumed`, `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`, `ConversationLinked`. Each carries the verbatim Raw Act; acting-Party identity + timestamp come from the binding + EventStore envelope. `ConversationLinked` additively realizes FR-21's post-creation reference link. The implemented catalog also carries 15 commands and 10 rejection events (`WorkItemTransitionRejected`, `WorkItemProgressRejected`, `WorkItemReEstimateRejected`, `WorkItemInitialEffortRejected`, `WorkItemConversationLinkRejected`, …) — 40 serialized types at Story 1.5. AD-21 adds, additively: the registry's reserve/edge-reserved/edge-attached/edge-released contracts and one dedicated spawn-rejection event `(TenantId, ParentWorkItemId, ChildWorkItemId)`; `WorkItemTransitionRejected` stays frozen. Theme 4 adds a `WorkItemRouted`-style event additively (no V2, no reshape).

**Executor Binding value object (FR-17/19):** `ExecutorBinding(PartyId, Channel, AuthorityLevel)` — `Channel` an extensible enum/value (MCP, CLI, Chatbot, Email, …); `AuthorityLevel` the proposed ordered set `{ Read, Contribute, Coordinate, Administer }` (carried-not-enforced in v1).

**Burn-Down (FR-3, cost-ready):** a `Meter(Unit, Estimated, Done)` with derived `Remaining`; v1 instantiates one Effort meter, and Theme 5 adds a parallel Cost meter reusing the same roll-up. Roll-Up (FR-11) is a projection: `rolledRemaining(item) = item.Remaining + Σ rolledRemaining(child)`.

**Await-Condition (FR-14):** a discriminated value `{ ChildCompleted(childId) | DateReached(instant) | ExternalSignal(correlationId) }`; a Suspended item may hold a *set* of these and resumes on the first match. Resume is a `ResumeWorkItem(awaitCondition)` command (not a port) raised by the Reactor and its adapters — child-completion from the Reactor, the durable reminder adapter for dates, an external adapter for Theme 3 signals — keeping `Handle` clock-free (FR-15). The implemented lifecycle keeps the *last consumed* condition in state so that a repeat of it is the one `NoOp`; every other non-matching resume is `WorkItemTransitionRejected` (`docs/lifecycle-transition-matrix.md`).

**Work-Tree Registry attachment protocol (FR-13, FR-16 — AD-21, mechanism):** *(added 2026-09-08)*

- One tenant-scoped registry aggregate per tenant, its own single-writer actor under the EventStore ETag-backed save; state = `(childId → parentId)` edges with status `Reserved | Attached | Released`, from which ancestry and depth derive.
- Public entry: a registry **reserve** command carrying the complete `SpawnChild` payload verbatim (raw act, uninterpreted). The registry's pure `Handle` evaluates single-parent, acyclicity, bounded depth (`MaxDepth` from platform configuration, default 32) and tenant closure against its own rehydrated state → edge-reserved event, or a deterministic domain rejection. Concurrent attaches serialize on the registry actor.
- The Reactor translates edge-reserved → `SpawnChild` on the parent, attaching the registry-derived ancestry/depth snapshot; the parent's `WorkTreeAttachmentGuard` re-checks those facts as **assertions** (a mismatch is a domain rejection), then emits `ChildSpawned`; the pipeline then creates the child (`CreateWorkItem` with `ParentWorkItemReference`). `SpawnChild` is origin-restricted at the gateway to the Reactor's workload identity (AD-24).
- Edge lifecycle: `Reserved → Attached` on `ChildSpawned` evidence; `Reserved → Released` on the dedicated spawn-rejection event `(TenantId, ParentWorkItemId, ChildWorkItemId)` or on a platform-configured reservation timeout with no creation evidence. Duplicate `SpawnChild` for an `Attached` identical pair → `NoOp`. Cascade and the AD-22 roll-up fan-out enumerate `Attached` edges only.
- Repair: the registry stream is authoritative for topology among existing items; rebuild suppresses roll-up/cascade for a subtree whose evidence is genuinely divergent (cyclic, multiply-parented, asymmetric claims) until an audited operator repair — the "Unavailable" roll-up state of AD-22.
- Dependency: the reserve→spawn→release translations need deterministic causation/message IDs, so the transport-idempotency binding (VAL-H10, at the shared `IWorkCommandSubmitter` seam — AD-20 R11) precedes the registry story.

**Reactor / saga consistency (FR-26 — AD-10, mechanism):** *(added 2026-09-08)* `react(event) → command[]` is a pure translation living in the `Reactor` package; runtime delivery, checkpoints, retries, and reminder firing come from EventStore SDK seams composed by `Hexalith.Platform` (AD-20 R6/R7/R11). Cascade is checkpoint-driven off a re-readable projection of `Attached` edges (never an in-memory loop); the checkpoint concurrency protocol is VAL-H06 (R7). Every command the Reactor issues lands on a defined `NoOp` or rejection when redelivered, which is what makes crash-recovery re-issue safe. The Reactor acts under a workload identity plus an explicit tenant-delegation context (AD-23).

**Ports (FR-22):**
- `IExpectationResolver` — `Expectation Resolve(WorkItemState state)`; v1 ships a no-LLM implementation returning a structured/empty default. Theme 3 supplies an AI-inferring adapter.
- `IExecutorRouter` — abstraction only in v1 (no implementation wired); Theme 4 supplies routing/escalation adapters.

**Reference Value Objects (FR-21):** correlation IDs only — `PartyId` (Parties), `ConversationId` (Conversations), `TenantId` (Tenants), aggregate/ID helpers (Commons). `LinkConversation` records a post-creation reference through `ConversationLinked`; the first link is authoritative, same-ID retries are no-ops, and conflicting relinks are domain-rejected. No denormalized copies.

## Downstream handoff — edits owed after the 2026-09-08 PRD update

The PRD update did not touch `architecture.md`, `epics.md`, stories, or repository docs. These are the edits they now owe to stay consistent with the amended PRD; each names its owner.

| # | Artifact | Edit owed | Why | Owner |
|---|---|---|---|---|
| H1 | `architecture.md` Requirements Overview (feature table, row 4.1) | Replace "A single event-sourced aggregate root owning … parent/children refs" with "the `WorkItem` aggregate references its parent/children; the Work-Tree Registry aggregate owns the edges (AD-21)". Row 4.3's `rolled = own + Σ rolled(children)` phrasing is retired by AD-06 — restate as the flattened per-descendant form. | Register wins over prose, but the prose still contradicts it | Architect |
| H2 | `architecture.md` AD-13 rule | "no current match is a no-op; a duplicate is a no-op" → "a non-matching resume is a domain rejection; repeating the *consumed* condition is the only no-op" — the rule the implemented matrix and PRD FR-15 now state | AD-13 and `docs/lifecycle-transition-matrix.md` disagree; AD-17 makes the matrix authoritative | Architect |
| H3 | `epics.md` AR-6 | "`ProgressReported` delta ≥ 0" → "delta **> 0**" (AD-17; PRD FR-8) | Downstream self-contradiction the PRD now arbitrates | PM / SM |
| H4 | `epics.md` Stories 3.1, 3.2 | Supersede or amend: `SpawnChild` is no longer caller-facing (public act = registry reserve); the pure tree guard is an assertion check, not the authority. Draft the AD-21 registry story with FR-13/FR-16 as written 2026-09-08, after VAL-H10 is bound (R11). | AD-21 correct-course | PM / SM |
| H5 | `epics.md` FR coverage map | Add **FR-26** (Reactor) → Stories 3.5/3.6/4.6–4.8 (+ the registry story); point FR-16 at the registry story; add the §9 identity-provenance NFR as Story 4.9's requirement source. | New FR and NFR | PM / SM |
| H6 | `epics.md` Story 4.9 | Cite PRD §9 "Identity provenance & trusted origin" as the requirement behind its AD-23/AD-24 acceptance. | Traceability | PM / SM |
| H7 | `docs/lifecycle-transition-matrix.md` | Add a back-reference: PRD FR-6 now carries the normative product-level table; the matrix mirrors it 1:1. Record the Reject/Claim product decisions (2026-09-08) as PRD-owned. | Two tables must stay in lock-step | Dev (Story 2.1 owner) |
| H8 | `architecture.md` open-findings register | Add a row: PRD FR-26 states the cascade/child-completion consistency window as a product assumption; confirm it against the R7 checkpoint protocol (VAL-H06) when bound. | New PRD assumption depends on R7 | Architect |
| H9 | `epics.md` FR-6 / Story 2.1 inventory | Reflect that `Claim` is the single `InProgress` entry (no `WorkItemStarted`) and Reject's two outcomes as PRD decisions, not story-local choices. | Product ownership of lifecycle | PM / SM |

## Deferred-theme mechanism depth

Beyond the PRD §12 roadmap table, the brainstorm extraction captured per-theme mechanism detail (constrained-safe generation, confidence-gated auto-apply, start-cheap-escalate ↔ budget-degrade as one ladder run both ways, single-use bound expiring idempotent links, consent/residency-before-cost routing, cost-caps-as-DoS-guard). These are recorded in the session file and the brief addendum; they are *not* v1 requirements and are surfaced here only so the architect preserves the named v1 seams (ports, raw-act events, cost-ready burn-down, AuthorityLevel) that make them buildable additively.
