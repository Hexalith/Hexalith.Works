---
title: "Hexalith.Works — Product Requirements Document"
status: final
created: 2026-06-14
updated: 2026-06-14
---

# PRD: Hexalith.Works
*Working title — confirm.*

## 0. Document Purpose

This PRD specifies **v1 (the foundation: Themes 1 & 2)** of Hexalith.Works — the work-item coordination kernel for the Hexalith ecosystem. It is written for the architect who will turn it into a solution design, the developers who will implement the `WorkItem` aggregate, and the owners of the sibling Hexalith modules Works references. It builds on — and does not duplicate — the finalized **product brief** (`../briefs/brief-works-2026-06-14/brief.md`), its **addendum** (foundation action plan, deferred-theme backlog, competitive digest), and the **brainstorming session** (`../../brainstorming/brainstorming-session-2026-06-14-0910.md`, 44 ideas / 6 themes). Vocabulary is anchored in §3 Glossary and used verbatim throughout; features group globally numbered FRs nested under them; and inferred decisions are tagged inline `[ASSUMPTION: …]` and collected in §14. Technical-how depth (detailed substrate constraints, a proposed event catalog, port-shape sketches) lives in `addendum.md` for the architecture phase.

Scope decision (confirmed 2026-06-14): v1 delivers Themes 1 & 2 as buildable requirements; Themes 3–6 are captured as a forward-looking **Roadmap / Designed-For** section (§12) so the seams laid in v1 — ports, the signed-raw-act audit model, and a cost-ready burn-down — are documented but not specified as v1 work.

## 1. Vision

Hexalith.Works is the small, durable spine that owns a unit of *work to be done* and coordinates *who does it* — a system or AI agent, an internal user, or an external person reached by email — without becoming a task database or a workflow-diagram engine. A **Work Item** burns down toward zero remaining effort, competes for attention by priority and due date, spawns and suspends like a durable saga, and rolls its remaining effort up a parent→child tree so an objective's all-in remaining work is a single number. Everything else — identities, dialogue, persistence, isolation, IDs — is a late-resolved reference to a sibling Hexalith module, never copied.

The defining bet is **"everything is a Party":** system, user, and external-by-email collapse into one **Executor Binding** (`PartyId + Channel + AuthorityLevel`), so assignment, reassignment, and human⇄AI handoff run identically for a bot, a colleague, or a customer — with zero branching on executor type. A second bet, laid structurally in v1 and realized later, is **AI in the loop but never in the system-of-record:** the canonical event is the raw act, and any interpretation of it is a recomputable projection.

v1 proves the spine. It delivers a pure, event-sourced domain assembly — the aggregate, its lifecycle, its raw-act events, the recursive (cost-ready) roll-up, the executor binding, the module ports as abstractions, and an Aspire host that runs it under test — so that the LLM-native interaction, executor routing, cost governance, and security-hardening themes can be built on top without reshaping the core.

## 2. Target User

### 2.1 Jobs To Be Done

- **As a Hexalith builder,** I need a single domain object to coordinate work across system/AI, internal, and external doers, so I stop stitching three systems (a task manager, a durable-execution engine, an approvals tool) together with bespoke glue.
- **As a builder,** I need to attach a new kind of doer (an MCP agent today, a Slack approver tomorrow) at one well-defined seam, without editing the core.
- **As a builder,** I need every state change to be an append-only, replayable fact in the Hexalith event-sourcing substrate, so audit and roll-up are derivable, not reconstructed by hand.
- **As a system/AI executor (a Party),** I need to claim, advance, suspend, and complete work through one uniform command surface, identically to how a human executor would.
- **As an end user (the brief's co-primary audience; in v1 served *through* builders),** my work must be capturable and advanceable from wherever it surfaces — inbox, chat, an assistant — instead of scattering into shadow lists because it never reached a task app. v1 makes the kernel channel-ready for this; the adapters that deliver it are Theme 3.
- **As an objective owner (served later, through builders),** I need one rolled-up number for the remaining effort of a whole work tree.

*This restates the brief's two-sided problem at the kernel altitude — builder "stop stitching three systems" pain and end-user "work never gets captured" pain. v1 directly relieves the builder side and lays the seams for the end-user side.*

### 2.2 Non-Users (v1)

*These are co-primary audiences of the **product** (per the brief) who are not **direct v1 consumers** because the surfaces they need are deferred — a v1 scoping cut, not a narrowing of the thesis.*

- **End users via channel surfaces** — people creating/advancing work from email, chatbot, MCP tools, or CLI. The brief commits to "omnichannel capture from day one" *as a product vision*; v1 lays the **Channel** seam so the model is channel-ready but ships no production channel adapter (Theme 3). v1's direct consumer is the builder, exercising the kernel through the Aspire test host. *(This is the one place the PRD deliberately tightens the brief's "day one" language to "seam on day one, adapter later" — see §5.)*
- **Tenant admins setting escalation ladders / spend caps** — the policy surfaces they configure are Themes 4 & 5.
- **Auditors querying a signed non-repudiation record** — the raw-act event model is laid in v1; the auditor-facing query/UI and signed-link enforcement are Theme 6.

### 2.3 Key User Journeys

*Lean, capability-illustrating narratives for a v1 that is a headless domain kernel. UJ-1–UJ-3 are realized by v1; UJ-4 is the deferred end-user horizon the kernel is shaped to enable, shown to anchor the §12 roadmap.*

- **UJ-1. A builder wires Works into a module.** A Hexalith builder references the Works domain assembly, resolves the reference value objects (Party, Conversation, Tenant) for their context, supplies the no-LLM `IExpectationResolver`, and issues a create command. They get back an event-sourced Work Item with correct identity under their tenant — no infrastructure code written, no sibling-module data copied.

- **UJ-2. A system/AI executor burns down work through one uniform surface.** A service Party — channel = MCP, machine authority — is bound to a Work Item and reports progress in the item's unit until remaining reaches zero, at which point the item completes. The exact same commands would advance a human or external Party; the code path does not branch on executor kind. *Realizes the "everything is a Party / zero branching" build signal.*

- **UJ-3. A work item spawns a child, suspends, and resumes.** An item spawns a child, parks itself on an await-condition (the child's completion), and the engine resumes it when the child completes — the parent's rolled-up remaining effort reflecting the child's burn-down throughout. *Realizes the durable-saga + correct-roll-up build signal.* **Edge case:** if the item is also parked on a date that arrives first, the date trigger resumes it independently.

- **UJ-4. (Deferred — Theme 3 horizon, not built in v1.)** Mary captures a to-do in one line of email; an external supplier she only reaches by inbox advances it with a single tap, no login; the AI's reading of the supplier's reply is a projection over the verbatim signed reply. v1 lays the executor-binding, await-condition, and raw-act event seams this depends on; the email-as-UI, magic links, and NL parsing are Theme 3.

## 3. Glossary

*Downstream workflows and readers must use these terms exactly. FRs, UJs, and SMs use Glossary terms verbatim.*

- **Work Item** — the irreducible coordination unit and the aggregate root: an obligation with a burn-down, a schedule, a status, an executor binding, optional parent/children, and an optional await-condition. Owns those facts; references everything else.
- **Obligation** — what the Work Item commits to getting done: a human-readable description plus an optional **Expectation** reference. Not an implementation of the work.
- **Expectation** — a representation of "what is expected now, and from whom," resolved from the item's state by the `IExpectationResolver`. In v1, a structured, non-LLM value; later AI-inferred (Theme 3).
- **Burn-Down** — the trio of unit-tagged quantities **Estimated**, **Done**, **Remaining** describing progress toward completion. Progress is a fact (remaining decreases), not a status flag. The v1 burn-down is the single Effort **meter**; a second Cost meter reuses the identical machinery later (Theme 5).
- **Effort** — the v1 Burn-Down dimension (work to do). **Cost** is the second, cost-ready-but-deferred dimension (Theme 5).
- **Unit** — the domain-chosen measure a Burn-Down is expressed in (e.g., hours, steps, tokens, interactions). Per-item; pluggable; not global.
- **Remaining** — Estimated minus Done for this item; the quantity that rolls up. **Done = Remaining is 0** is the completion invariant.
- **Roll-Up** — the recursive projection where a parent's Remaining (and, later, Cost) equals its own plus the recursive Remaining of all descendants.
- **Work Tree** — a Work Item and its transitive children; acyclic, single-parent.
- **Executor** — the doer of a Work Item: a system/AI agent, an internal user, or an external person — all modeled as a Party.
- **Party** — an identity from `Hexalith.Parties`, referenced by `PartyId`. The single executor concept; there is no "external party" type.
- **Channel** — the delivery/interaction medium for an executor (e.g., MCP, CLI, chatbot, email). Orthogonal to the Executor; a Party may change Channel mid-work.
- **AuthorityLevel** — what an Executor Binding is permitted to do. Carried on the binding in v1; enforcement is Theme 4/6. *(Proposed ordered set — see §4.5 / `[ASSUMPTION]`.)*
- **Executor Binding** — the value object `PartyId + Channel + AuthorityLevel`. The one pluggable seam where new doer kinds attach.
- **Schedule** — the Work Item's **Priority** and **Due Date**, giving it standing in a contended queue.
- **Status** — the resting lifecycle state (see §4.2): one of Created, Assigned, Queued, InProgress, Suspended, Completed, Cancelled, Rejected, Expired. Resumption is a *transition* back into InProgress, not a resting state.
- **Await-Condition** — an event a Suspended Work Item is parked on. A Work Item may hold **more than one** and resumes on the **first match** — a child completing, a date arriving, or an external signal correlated by ID. (Unifies dependencies, timers, and external triggers into "park until event X.")
- **Saga** — the spawn→suspend→resume continuation pattern a Work Item embodies.
- **Raw Act** — the literal, attributable fact recorded as a domain event (who acted, when, and the verbatim payload), as opposed to any interpretation of it.
- **Domain Event** — a past-tense, additively-versioned record of a Raw Act, serialized via `Hexalith.PolymorphicSerializations` (e.g., `WorkItemCreated`).
- **Projection** — a recomputable read model derived from the event stream (the Roll-Up and the "what's next" query are projections).
- **Reference Value Object** — a correlation identifier (Party, Conversation, Tenant, etc.) resolved on demand; never a copy of the referenced data.
- **Port** — a domain-owned abstraction (`IExpectationResolver`, `IExecutorRouter`) that keeps the domain pure and pushes LLM/cost/routing concerns into adapters.
- **Tenant** — the isolation boundary from `Hexalith.Tenants`; every Work Item, key, projection, and query is tenant-scoped.

## 4. Features

*Each subsection is a coherent feature: behavioral description first, FRs nested, global FR numbering for stable downstream references. Glossary terms used verbatim. All requirements are tenant-scoped (see §9 NFRs) even where not restated.*

### 4.1 Work Item Aggregate & State

**Description:** The `WorkItem` aggregate is the irreducible coordination unit. It owns its identity, its Obligation (description + optional Expectation reference), its single Executor Binding, its Effort Burn-Down (unit-tagged Estimated/Done/Remaining), its Schedule (Priority + Due Date), its Status, its parent and children references, and its optional Await-Condition. It owns only these facts; identities, dialogue, persistence, and isolation are Reference Value Objects. Realizes UJ-1.

**Functional Requirements:**

#### FR-1: Create a Work Item
A builder or Executor can create a Work Item with at minimum an Obligation description and a Tenant context, optionally supplying an initial Estimated effort (+Unit), Schedule, parent reference, and Executor Binding.

**Consequences (testable):**
- Creating a Work Item emits `WorkItemCreated` carrying the Obligation, Tenant, and any supplied Burn-Down/Schedule/parent/binding fields.
- A created Work Item has a canonical identity consistent with the substrate's `{tenant}:{domain}:{aggregateId}` model and Status `Created`.
- Creation with no Estimated effort is valid; Remaining is then undefined-until-estimated and the item cannot be `Completed` via the Done=0 path until an estimate exists `[ASSUMPTION: an unestimated item completes only by explicit complete-without-estimate, not by the Remaining=0 rule]`.

#### FR-2: Carry an Obligation with an optional Expectation reference
A Work Item holds a human-readable Obligation description and an optional reference to an Expectation resolved via the `IExpectationResolver` port.

**Consequences (testable):**
- The Obligation description is required and non-empty at creation.
- When no Expectation is resolved, the Work Item is fully valid (the no-LLM resolver may return an empty/structured default). `[ASSUMPTION: the Expectation is referenced/resolved on demand, not stored as an interpreted value on the aggregate — keeping interpretation a Projection.]`

#### FR-3: Hold a unit-tagged Effort Burn-Down
A Work Item carries Estimated, Done, and Remaining effort, each tagged with the item's Unit.

**Consequences (testable):**
- Estimated, Done, and Remaining are expressed in the same Unit for a given item; the Unit is set per-item, not globally.
- Remaining is derived as Estimated − Done (never below 0) and is the quantity that rolls up.
- Mixed-Unit arithmetic across items is never performed implicitly; roll-up across differing units is governed by FR-12. `[ASSUMPTION]`

#### FR-4: Carry a Schedule (Priority + Due Date)
A Work Item carries a Priority and an optional Due Date that establish its standing in a contended queue.

**Consequences (testable):**
- Priority and Due Date are settable at creation and changeable later (FR-9), each change emitting an event.
- A Work Item with neither Priority nor Due Date is valid and sorts last in the "what's next" query (FR-20).

#### FR-5: Hold parent/children references and Await-Conditions
A Work Item references at most one parent and zero-or-more children, and may hold one or more Await-Conditions while Suspended.

**Consequences (testable):**
- A Work Item has at most one parent; the Work Tree is acyclic (FR-13). `[ASSUMPTION: single-parent, acyclic enforced at spawn time.]`
- A Suspended Work Item may be parked on multiple Await-Conditions simultaneously and resumes on the first to fire (e.g., a child completing *or* a date arriving — realizing the UJ-3 edge case).
- Children are referenced by ID (Reference Value Objects), not embedded.

### 4.2 Lifecycle State Machine & Domain Events

**Description:** A Work Item moves through an explicit lifecycle, and every transition is a Raw-Act Domain Event recorded append-only via `Hexalith.PolymorphicSerializations`. Completion is defined by the burn-down, not a flag: **Done = Remaining is 0.** Realizes UJ-2, UJ-3.

**Functional Requirements:**

#### FR-6: Enforce the lifecycle state machine
The aggregate enforces a defined set of legal transitions. The forward path is `Created → Assigned | Queued → InProgress → Suspended → InProgress → Completed`; in addition, `Assigned ↔ Queued` is bidirectional (FR-18), and the terminal states `Cancelled | Rejected | Expired` are reachable from any non-terminal state per FR-10.

**Consequences (testable):**
- Only transitions in the defined legal set are accepted; any other (e.g., `Completed → InProgress`) is a domain rejection (`IRejectionEvent`), not an exception.
- `Assigned` is the push entry (a specific Executor bound); `Queued` is the pull entry (claimable; see FR-18); an item may move `Assigned → Queued` (requeue) and `Queued → Assigned` (direct assign).
- Resumption is a *transition* from `Suspended` back to `InProgress` (FR-15); there is no resting `Resumed` status.
- No transition is legal out of a terminal state.

#### FR-7: Record raw-act domain events
Each state change and progress fact is recorded as a past-tense Domain Event capturing the acting Party, timestamp, and verbatim payload.

**Consequences (testable):**
- v1 event catalog (names final for v1; additively extensible): `WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`, `ProgressReported`, `ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`, `WorkItemSuspended`, `WorkItemResumed`, `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`. `[ASSUMPTION: this closes the open-ended "…" event list from the brainstorm; names follow ecosystem past-tense convention. WorkItemRescheduled carries Priority and/or Due-Date changes (FR-9).]`
- Events store the Raw Act (verbatim reported values), not interpreted/derived values; the acting Party identity and timestamp are recorded (via the binding and the EventStore envelope — Works does not populate envelope metadata).
- The ordered event stream **is** the Work Item's narrative history (the brainstorm's "comment stream and event stream are two views of one history"); `ProgressReported`, `ReEstimated`, and abnormal-termination events may carry an optional human-readable note. Conversational dialogue itself is delegated to `Hexalith.Conversations` by correlation ID (FR-21).
- Rejection outcomes implement `IRejectionEvent` and do not mix success and rejection payloads in one result.

#### FR-8: Report progress and complete by Remaining=0
An Executor can report progress in the item's Unit; when Remaining reaches 0 the item completes.

**Consequences (testable):**
- `ProgressReported` decreases Remaining by the reported Done delta (clamped at 0).
- Reaching Remaining = 0 transitions the item to `Completed` and emits `WorkItemCompleted`; completion is never set independently of the burn-down for an estimated item.
- An **unestimated** item (no Estimated set) completes only by an explicit complete act, which also emits `WorkItemCompleted` (it cannot complete via the Remaining=0 path). `[ASSUMPTION — confirmed by acceptance.]`
- A crash or abandonment leaves Remaining > 0 and the item resumable — "retry" is "continue the burn-down," not a separate state.

#### FR-9: Re-estimate and reschedule
An authorized Executor can re-estimate remaining effort and change Priority/Due Date as first-class facts.

**Consequences (testable):**
- `ReEstimated` adjusts Estimated (and therefore Remaining) and is a normal, expected event — over-run and partial progress are native, not errors.
- Schedule changes emit events and update the "what's next" query ordering.

#### FR-10: Cancel, reject, expire
A Work Item can terminate abnormally via Cancel or Expire; a bound Executor can Reject an assignment.

**Consequences (testable):**
- `WorkItemCancelled` and `WorkItemExpired` are terminal; no further progress is accepted.
- **Cancel** is an explicit act (authority-gated when enforcement lands; carried-not-enforced in v1, per FR-19).
- **Reject** (`WorkItemRejected`) is a bound Executor declining its assignment; by default the item returns to `Queued` for reassignment, and it is terminal only when the caller marks it non-requeuable. `[ASSUMPTION — default = requeue.]`
- **Expire** (`WorkItemExpired`) fires when the Due Date passes (or, absent a Due Date, a configured per-type TTL) without completion; expiry is terminal with no auto-reactivation. `[ASSUMPTION.]`
- **Cascade:** Cancelling or Expiring a Work Item cascades the same termination to its still-active descendants (a parent's death cancels its open subtree); already-terminal descendants are unaffected. Terminal items contribute 0 to a parent's rolled Remaining (FR-11). `[ASSUMPTION — cascade-cancel chosen over orphaning; resolves the open "parent termination → children" question.]`

### 4.3 Effort Burn-Down & Recursive Roll-Up

**Description:** A parent's remaining effort is its own plus the recursive remaining of its whole subtree, computed by a Projection over the event stream, built so the identical machinery serves a second (Cost) meter later. Realizes UJ-3 and the "one number" build signal.

**Functional Requirements:**

#### FR-11: Maintain the recursive remaining-effort Roll-Up
The system maintains a Roll-Up projection where each Work Item exposes its own Remaining and its subtree-rolled Remaining.

**Consequences (testable):**
- For any Work Item, rolled Remaining = own Remaining + Σ(rolled Remaining of direct children), recursively.
- A child's `ProgressReported` / `ReEstimated` / `WorkItemCompleted` / terminal event is reflected incrementally in every ancestor's rolled Remaining (no whole-stream re-read per query).
- The Roll-Up is an eventually-consistent Projection, not a synchronous aggregate field. `[ASSUMPTION: eventual consistency via projection; a stale read is acceptable and converges — the brainstorm calls roll-up a "projection" but never states the consistency model.]`
- The Roll-Up is built on the substrate's projection infrastructure (`CachingProjectionActor`, ETag actors, projection notifiers) rather than custom read-side routing, and is **idempotent** under at-least-once, possibly out-of-order event delivery (replaying an event does not double-count). *(Mandated by the ecosystem projection rules.)*

#### FR-12: Roll up across heterogeneous units safely
The Roll-Up does not silently sum incompatible Units.

**Consequences (testable):**
- Same-Unit subtrees roll into a single number.
- For mixed-Unit subtrees, the Roll-Up exposes per-Unit subtotals rather than a coerced single figure. `[ASSUMPTION: no Unit conversion in v1; conversion policy (if any) deferred.]`

#### FR-13: Guard the Work Tree shape
Spawning enforces an acyclic, single-parent tree within a bounded depth.

**Consequences (testable):**
- Attaching a child that would create a cycle or a second parent is rejected as a domain rejection.
- A Work Tree is **single-tenant**: a parent and child must share a Tenant; a cross-tenant parent/child link is rejected (prevents a cross-tenant roll-up leak). `[ASSUMPTION.]`
- Tree depth is bounded by a configured maximum; exceeding it is rejected. `[ASSUMPTION: default max depth = 32; configurable per tenant/type. The brainstorm states no limit — proposing a guard to bound runaway trees. Breadth/fan-out is not capped; the incremental Roll-Up (FR-11) keeps wide trees affordable.]`

### 4.4 Suspend / Resume Saga

**Description:** Suspension-on-an-event is the primitive: a Work Item parks on an Await-Condition and resumes when the matching event arrives. v1 handles child-completion and date/timer natively; external signals resume through a generic resume-by-correlation-ID port whose concrete adapters are deferred. Realizes UJ-3.

**Functional Requirements:**

#### FR-14: Suspend on an Await-Condition
An InProgress Work Item can suspend itself, recording the Await-Condition it is parked on.

**Consequences (testable):**
- `WorkItemSuspended` records the Await-Condition kind and its correlation key (child ID, target date, or external correlation ID).
- A Suspended item accepts no progress until resumed; it still participates in Roll-Up with its current Remaining.

#### FR-15: Resume on a matching trigger
The engine resumes a Suspended Work Item when its Await-Condition is satisfied.

Resume is driven by a **resume command** carrying a correlation key matching one of the item's Await-Conditions. The pure aggregate `Handle` never reads a clock or an outside system; date/timer and external signals arrive *as commands* issued by adapters (a timer/scheduler adapter for dates; an external adapter for webhooks/replies — Theme 3), keeping the domain pure.

**Consequences (testable):**
- **Child-completion:** a child's `WorkItemCompleted` raises a resume command to a parent parked on that child.
- **Date/timer:** a timer/scheduler adapter raises a resume command when a parked target date passes (the date is not read inside `Handle`).
- **External signal:** a resume command carrying the matching external correlation key resumes the item; the concrete external adapter (webhook/reply) is deferred (Theme 3). The correlation key is the contract Theme 3 fills.
- Resume emits `WorkItemResumed` and returns the item to `InProgress`; a resume whose key matches no current Await-Condition is a domain rejection, and a duplicate of an already-applied resume is an idempotent no-op. `[ASSUMPTION: resume is idempotent — a duplicate trigger is a no-op.]`

#### FR-16: Spawn child work
A Work Item can spawn one or more children, optionally suspending itself awaiting them.

**Consequences (testable):**
- `ChildSpawned` creates a child Work Item (FR-1 semantics) with a parent reference and emits on the parent.
- Spawning respects the Work Tree guard (FR-13).

### 4.5 Executor Binding — "Everything is a Party"

**Description:** System, user, and external doers collapse into one Executor Binding value object — `PartyId + Channel + AuthorityLevel`. Assign, reassign, and handoff are one operation across all three; the code path never branches on executor kind. This is the keystone and the single pluggable seam for new doer kinds. Realizes UJ-2 and the "zero branching / handoff = one operation" build signals.

**Functional Requirements:**

#### FR-17: Bind, reassign, and hand off via one uniform operation
A Work Item can be bound to an Executor, and reassigned/handed off to a different Executor, through a single operation regardless of executor kind.

**Consequences (testable):**
- Assigning to a system Party (Channel = MCP, machine authority), an internal-user Party, or an external Party (Channel = email) uses the identical command and emits `WorkItemAssigned`.
- Human→AI and AI→human handoff is the same reassign operation in either direction.
- No domain code branches on executor *kind*; the only variation is the binding's field values. *(This is a build-signal acceptance check — see SM-3.)*

#### FR-18: Push and Pull coexist
A Work Item can be pushed (assigned to a specific Executor) or pulled (placed in a shared queue and claimed), and can move between modes.

**Consequences (testable):**
- A `Queued` item can be claimed by an Executor, emitting `WorkItemClaimed` and transitioning the item to `InProgress` bound to that claimant.
- **Single claim wins:** when two Executors race to claim the same `Queued` item, exactly one succeeds; the loser receives a domain rejection (the item is no longer claimable), serialized by the aggregate's single-writer/optimistic-concurrency model (mechanism detailed at architecture; see §9).
- An `Assigned` item can be returned to `Queued` (requeue) and a `Queued` item can be directly assigned — both normal transitions; requeue re-emits `WorkItemQueued` (which thus marks every entry into the queue, whether from `Created` or `Assigned`). `[ASSUMPTION: claim "eligibility" filtering is a routing concern and is deferred to Theme 4; v1 allows any Executor of the tenant to claim.]`

#### FR-19: Carry AuthorityLevel on the binding
The Executor Binding carries an AuthorityLevel describing what the Executor may do; v1 stores it as part of the contract without enforcing gating.

**Consequences (testable):**
- The binding persists the AuthorityLevel through create/assign/reassign events.
- `[ASSUMPTION: proposed ordered AuthorityLevel set = { Read, Contribute, Coordinate, Administer }. Read = await/observe; Contribute = report progress, complete own work, answer an Expectation (covers external "confirm" and machine "auto-complete"); Coordinate = assign/reassign, reprioritize, spawn; Administer = approve spend, set caps, cancel. Enforcement is Theme 4/6; the set is additive-tolerant so it can grow without a V2 event.]`

### 4.6 Thin-Core Boundaries & Module Ports

**Description:** Works owns obligation + burn-down + schedule + executor binding + suspend/resume saga, and *references* everything else via Reference Value Objects resolved on demand. Cross-module concerns sit behind domain-owned Ports so the domain stays pure and LLM/cost/routing live in adapters. A written boundary decision record is a v1 deliverable. Realizes UJ-1.

**Functional Requirements:**

#### FR-20: Resolve a "what's next" ordering
The system exposes a read-side query returning a tenant's claimable/assigned Work Items ordered by Priority then Due Date.

**Consequences (testable):**
- The query returns `Queued` and `Assigned` items for a tenant ordered by Priority (then earliest Due Date, then creation order); items with neither sort last.
- The query is served by the substrate's query/projection infrastructure (not custom read routing) and applies query-side authorization/result filtering in addition to tenant scoping (§9).
- This is a Projection/query only — no routing, assignment, or ranking *engine* (that is Theme 4).

#### FR-21: Reference sibling modules, never copy them
Identities, dialogue, persistence, isolation, and IDs are Reference Value Objects resolved on demand from the owning module.

**Consequences (testable):**
- Identity → `Hexalith.Parties` (PartyId); dialogue → `Hexalith.Conversations` (correlation ID); persistence/events → `Hexalith.EventStore`; isolation → `Hexalith.Tenants`; IDs → `Hexalith.Commons`.
- The aggregate stores correlation IDs, not denormalized copies of referenced data.
- A Conversation correlation ID can be linked to a Work Item at creation or via a later command (and emitted on the corresponding event); it is optional and resolved on demand. The comment narrative and the event stream are two views of one history (FR-7); Works holds the correlation ID rather than its own comment store. `[ASSUMPTION: v1 references a Conversation by ID; it does not implement comment storage.]`

#### FR-22: Expose module ports as abstractions
The domain depends on `IExpectationResolver` and `IExecutorRouter` as ports, with a no-LLM `IExpectationResolver` implementation shipped in v1.

**Consequences (testable):**
- The domain assembly compiles and all v1 tests pass with only the no-LLM `IExpectationResolver` and without any `IExecutorRouter` implementation wired (routing is deferred; the port exists).
- No LLM, cost, routing, or infrastructure type is referenced from the domain assembly.

#### FR-23: Produce the boundary decision record
v1 includes a written owns-vs-references boundary decision record as a tracked artifact.

**Consequences (testable):**
- The record enumerates, for each sibling module, what Works owns vs. references and why, and is referenced by the architecture phase.

### 4.7 Aspire Test Host & Harness

**Description:** v1 ships a repository-specific .NET Aspire host that stands up the dependencies the kernel needs to run, so the full event-sourced lifecycle can be exercised by manual and automated tests. No production channel adapter (MCP/CLI/email) ships in v1.

**Functional Requirements:**

#### FR-24: Run the kernel under an Aspire host
An Aspire AppHost wires Works and its substrate dependencies for local manual and automated testing.

**Consequences (testable):**
- The end-to-end lifecycle (create → progress → spawn → suspend → resume → complete) runs under the Aspire host with correct Roll-Up.
- The host follows the existing `ServiceDefaults`/health/telemetry pattern; no production adapters are included.

#### FR-25: Exercise the command pipeline in tests
The kernel is exercisable through its command/event pipeline in automated tests without production adapters.

**Consequences (testable):**
- Tier-1 tests (aggregate `Handle`/`Apply`, projection handlers) run pure — no Dapr, network, browser, or containers.
- Integration tests use the substrate's testing fakes/builders or Aspire topology only where a real boundary is genuinely needed.

## 5. Non-Goals (Explicit)

- Works is **not** a task database or a system of record for the *content* of work; it owns coordination facts and references the rest.
- Works is **not** a workflow-diagram/BPMN engine; there are no authored process diagrams — the event log of ad-hoc work is the model.
- Works does **not** put AI in the system of record; interpretations are Projections over Raw-Act events.
- v1 builds **no** production channel adapters (email/magic-link, chatbot, MCP, CLI), **no** LLM-native interaction or NL parsing, **no** executor routing/escalation engine, **no** cost meter/spend governance, and **no** security-hardening enforcement (step-up auth, consent/residency routing, DoS guards). These are Themes 3–6 (§12). *(The brainstorm filed MCP-as-actor-channel and CLI-as-scriptable-work under foundation Theme 2; v1 deliberately defers even these non-LLM command surfaces — the kernel-only surface decision — while keeping the Channel seam so they attach later without core changes.)*
- Works does **not** re-implement identities, dialogue, persistence, isolation, or ID generation — those remain owned by their sibling modules.

## 6. MVP Scope

### 6.1 In Scope (v1 — Themes 1 & 2)

- The `WorkItem` aggregate: identity · obligation (+Expectation reference) · executor binding · unit-tagged effort burn-down (cost-ready) · schedule · status · parent/children refs · await-condition (FR-1–FR-5).
- The lifecycle state machine and the v1 raw-act Domain Event catalog (FR-6–FR-10).
- The recursive remaining-effort Roll-Up projection, tree-shape guard, and heterogeneous-unit safety (FR-11–FR-13).
- The suspend/resume saga: child-completion + date/timer native, generic external resume port (FR-14–FR-16).
- The Executor Binding (`PartyId + Channel + AuthorityLevel`), uniform assign/reassign/handoff, push+pull, AuthorityLevel carried-not-enforced (FR-17–FR-19).
- Thin-core boundaries: "what's next" query, Reference Value Objects, ports (`IExpectationResolver` no-LLM impl + `IExecutorRouter` abstraction), boundary decision record (FR-20–FR-23).
- The Aspire test host and test harness (FR-24–FR-25).

### 6.2 Out of Scope for MVP

- LLM-native interaction, AI-inferred Expectation, magic links, NL parsing, email-as-UI — **Theme 3**. *(Seam laid: `IExpectationResolver`, Await-Condition, Channel.)*
- Executor routing & escalation ladder, push/pull auto-assignment, explainable decision record — **Theme 4**. *(Seam laid: `IExecutorRouter` port, push/pull states, AuthorityLevel field, assignment events.)* The routing decision record (candidates/score/cost-estimate/confidence) needs **no v1 event placeholder**: because the substrate's schema evolution is additive and tolerant (§8), Theme 4 can introduce a new `WorkItemRouted`-style event and/or additive fields on `WorkItemAssigned` without a V2 type or a reshape. *(Resolves draft OQ-8.)*
- Cost as a second burn-down, spend caps, graceful degradation, cost roll-up, cost-aware scheduling — **Theme 5**. *(Seam laid: cost-ready burn-down + reusable roll-up.)*
- Trust/security hardening: single-use bound expiring links, step-up auth, NL-is-data enforcement, consent/residency routing, non-repudiation surfaces, cost-cap-as-DoS-guard — **Theme 6**. *(Seam laid: raw-act event model with actor + timestamp + verbatim payload.)*
- Any production channel adapter or end-user UI.

## 7. Why Now

Timing is load-bearing for Works and is documented in the brief/addendum: 2026 is framed as the year human+agent work surfaces go mainstream — Microsoft's Work Trend Index "agent boss"/"Frontier Firm" model (humans set direction, agents execute) and ~62% of organizations experimenting with or scaling AI agents as "digital coworkers." The enabling primitives (durable execution, model routing, HITL approvals, magic links) are all mature; the **whitespace is the synthesis** — one audited object where the backlog, the durable saga, and the effort+cost ledger are the same thing. Works claims **no moat on any single primitive; the advantage is coherence.** v1 builds that object's spine so the synthesis can be assembled on top while the operating model takes hold. *(Sources in `../briefs/brief-works-2026-06-14/addendum.md`.)*

## 8. Public Surface & Compatibility *(developer-product cluster)*

Works' v1 public surface is its **domain contract**, consumed by Hexalith builders: the Domain Events, commands, the Executor Binding value object, the Reference Value Objects, the lifecycle, and the ports. Because the substrate is event-sourced, the surface's compatibility rules are strict and inherited from the ecosystem:

- **Additive, serialization-tolerant evolution only.** No `V2` event types; every event ever produced must remain backward-compatibly deserializable. The v1 event catalog (FR-7) and the AuthorityLevel set (FR-19) are designed to grow additively.
- **Package boundaries** follow the ecosystem layout — `Contracts` (events/commands/models, low-dependency, no infrastructure), `Server` (domain behavior), `Projections` (roll-up + "what's next"), `Aspire`/AppHost, `Testing`. Contracts stay infrastructure-free.
- **Runtime targets** are inherited: .NET 10, C# nullable + warnings-as-errors, Dapr as the only permitted infrastructure abstraction in domain services, `System.Text.Json` conventions, `Hexalith.PolymorphicSerializations` for event payloads.

*(Detailed substrate constraints and a proposed event/port shape for architecture are in `addendum.md`.)*

## 9. Cross-Cutting NFRs

- **Tenant isolation (mandatory, every layer).** Every Work Item, aggregate identity, state key, projection key, query, and log is tenant-scoped per the substrate model (`{tenant}:{domain}:{aggregateId}`); managed tenant IDs live in payloads/read models, not in the EventStore envelope tenant. **Query-side authorization/result filtering is required in addition to command-side checks** — tenant scoping alone is not sufficient for read queries (FR-20). Negative-path tests cover both the cross-tenant and the query-side-authorization paths.
- **Event-sourcing invariants.** Persist-then-publish; aggregate `Handle(...)` is pure and returns domain results/events; projection/state `Apply(...)` mutates only in-memory state; domain rejections are events (`IRejectionEvent`), infrastructure failures are exceptions/dead-letter paths. Works returns event payloads only — EventStore owns envelope metadata.
- **Concurrency.** Commands against a single Work Item are serialized by the aggregate's single-writer/optimistic-concurrency model; concurrent conflicting commands (e.g., two claims on one `Queued` item — FR-18) resolve to one success and domain rejections for the rest. No lost updates. *(Mechanism is an architecture concern; the behavior is a v1 requirement.)*
- **Projections are rebuildable.** The Roll-Up and "what's next" read models are derivable purely from the event streams and can be rebuilt/replayed from scratch; they hold no authoritative state of their own.
- **Domain purity.** The domain assembly takes no direct infrastructure dependency and no LLM/cost/routing dependency; those sit behind ports/adapters (FR-22). The aggregate `Handle` reads no clock or external system — time/external triggers enter as commands (FR-15).
- **Observability.** Structured logging only; never log event payloads, personal data, secrets, or full command bodies. Errors use the ProblemDetails/RFC 9457 pattern with correlation/tenant context.
- **Performance (qualitative for v1).** The Roll-Up and "what's next" projections remain responsive for realistically deep/wide trees by updating incrementally (FR-11), without re-reading whole streams on each query. No numeric targets are pinned in v1 (acceptance is build-signal based — §11). `[ASSUMPTION: numeric performance budgets deferred to a later iteration.]`

## 10. Constraints & Guardrails *(seams laid in v1, enforced later)*

- **Audit / non-repudiation (model laid in v1).** Domain Events record the Raw Act — acting Party + timestamp + verbatim payload — so that later interpretation is a recomputable Projection and disputes resolve against the verbatim act. v1 lays this shape; the signed single-use link enforcement and auditor-facing query are Theme 6.
- **Idempotency (event-sourced).** v1 idempotency rests on two mechanisms: resume is idempotent against current state (a resume whose key no longer matches an Await-Condition is a no-op — FR-15), and the substrate's command/event handling dedups replays by stream offset so an already-applied act is not re-counted (FR-11). Explicit per-act idempotency tokens (Theme 6's single-use-bound links) build on this. `[ASSUMPTION: no explicit per-act token in v1.]`
- **Cost-ready burn-down.** The burn-down and roll-up are built so a second (Cost) meter reuses the identical machinery (Theme 5) — no schema reshape required to add it.
- **NL-is-data boundary (designed-for).** The Expectation/answer-space concept (the `IExpectationResolver` port) is the future prompt-injection boundary: when Theme 3 lands, the **answer-contract does triple duty — UX accelerator, input validator, and prompt-injection boundary** (free text is mapped onto the item's valid action space only, never executed as instructions). v1 ships only the no-LLM resolver, so no NL is interpreted as instructions in v1.

## 11. Success Metrics *(build-signal acceptance)*

*v1 is foundation software; acceptance is defined by build signals from the brief rather than usage metrics. Each SM cross-references the FR(s) it validates.*

**Primary (build signals)**
- **SM-1 — Full event-sourced lifecycle, durable across restart.** The sequence create → progress → spawn child → suspend-on-event → resume → complete runs end-to-end under the Aspire host; and a Work Item suspended mid-saga rehydrates from its event stream after a restart and resumes correctly (durability is a fact, not a claim). *Validates FR-1, FR-6–FR-8, FR-14–FR-16, FR-24.*
- **SM-2 — Correct roll-up.** For any constructed Work Tree, rolled Remaining equals own + recursive descendants' Remaining, and updates as descendants progress. *Validates FR-11–FR-13.*
- **SM-3 — Zero branching on executor kind.** Assign/reassign/handoff across system, user, and external bindings execute through the identical code path; a test (and code inspection) confirms no domain branch on executor kind. *Validates FR-17, FR-19.*
- **SM-4 — Pure domain assembly.** The domain assembly has zero technical/infrastructure layers; all cross-module concerns are behind Reference Value Objects and ports; green build + green tests under Aspire. *Validates FR-21–FR-25.*

**Secondary**
- **SM-5 — Handoff = one operation.** Reassigning between a human and an AI Executor in either direction is a single symmetric operation, demonstrated by test. *Validates FR-17.*

**Counter-metrics (do not optimize)**
- **SM-C1 — Don't grow the kernel.** Lines/surface of the Works domain assembly should *not* be maximized; capability that belongs in a sibling module migrating *out* of Works is success, not regression. Counterbalances the temptation to satisfy SM-1 by absorbing technical layers. *Counterbalances SM-1/SM-4.*
- **SM-C2 — Don't over-fit v1 to deferred themes.** Adding speculative routing/cost/security machinery to "prepare" beyond the named seams is a negative; the seams in §10 are sufficient. *Counterbalances the roadmap pressure in §12.*

## 12. Roadmap / Designed-For (Themes 3–6)

*Documented, not specified as v1 work. Each theme names the v1 seam it builds on so the architecture phase preserves it.*

| Theme | Scope | v1 seam it builds on |
| --- | --- | --- |
| **3 — LLM-native interaction** | AI-inferred Expectation, constrained-safe magic links, NL-always-accepted + confidence-gated auto-apply, status-driven re-inference, email-as-UI | `IExpectationResolver` port; Await-Condition; Channel field; raw-act events |
| **4 — Executor routing & escalation** | Auto-route + manual override, start-cheap-escalate ladder (small model → premium → human → external) as per-type data policy, explainable decision record (candidates/score/cost/confidence), push↔pull auto-assignment | `IExecutorRouter` port; push/pull states (FR-18); AuthorityLevel (FR-19); assignment events |
| **5 — Economics & cost governance** | Cost as a second burn-down, spend caps → graceful degradation, cost roll-up, cost-aware (debounced) scheduling | cost-ready burn-down + reusable Roll-Up (FR-11) |
| **6 — Trust, security & auditability** | Single-use bound expiring idempotent links, forwarding≠authority + step-up auth, NL-is-data enforcement, consent/residency routing, non-repudiation surfaces, cost-cap-as-DoS-guard | raw-act event model (FR-7); idempotency (§10); AuthorityLevel (FR-19) |

**One ladder, run both ways.** Theme 4's start-cheap-escalate routing (cheapest capable → premium → human → external) and Theme 5's budget-degrade (full LLM → plain links → static templates → human) are the *same* escalation ladder traversed in opposite directions — one driven by capability/confidence, the other by spend. They should share one policy mechanism, not two.

**Designed-for tensions to revisit at theme time** (from the brainstorm): status-driven re-inference (Theme 3) vs. cost-aware debounce (Theme 5); authored vs. AI-inferred Expectation contract representation (Theme 3); and Theme 5's cost-aware scheduling reaching into the kernel-owned schedule/priority — a boundary call to make so the thin core does not absorb a cost engine (guard with SM-C1/SM-C2).

## 13. Open Questions

*The draft's eight Open Questions (unestimated completion, Expectation representation, AuthorityLevel set, reject/expiry semantics, tree-depth guard, roll-up consistency, heterogeneous-unit roll-up, routing-decision placeholder) were **resolved on 2026-06-14** by user acceptance and now appear as confirmed assumptions in §14 and as FR consequences. The items below are deliberately left to the **architecture / solution-design phase** — they are mechanism decisions, not product-requirement gaps.*

1. **Aggregate identity derivation** — how the `aggregateId` portion of `{tenant}:{domain}:{aggregateId}` is generated (Commons ID helper) and whether it is caller- or system-assigned.
2. **Priority representation** — the concrete Priority type and ordering (enum vs. numeric band) backing FR-4/FR-20.
3. **Optimistic-concurrency mechanism** — the exact ETag/version strategy realizing the §9 concurrency requirement and the single-claim-wins rule (FR-18).
4. **Timer/scheduler adapter** — the component that raises date/timer resume commands (FR-15) and its delivery guarantees.
5. **Projection rebuild operations** — operational story for replaying/rebuilding the Roll-Up and "what's next" projections (§9).
6. **Validation domains** — bounds/validation for `ProgressReported` deltas, Unit immutability after first use, and Due-Date/TTL configuration source.

## 14. Assumptions Index

*Every `[ASSUMPTION]` in the document. All confirmed by the user on 2026-06-14 (accepted as drafted); they remain tagged so the architecture phase can challenge any that prove unworkable.*

- §4.1 FR-1 / FR-8 — An unestimated Work Item completes only by an explicit complete act, not by the Remaining=0 rule.
- §4.1 FR-2 — The Expectation is referenced/resolved on demand, not stored as an interpreted value on the aggregate.
- §4.1 FR-3 / §4.3 FR-12 — No implicit cross-Unit arithmetic; per-Unit subtotals exposed for mixed subtrees; no Unit conversion in v1.
- §4.1 FR-5 — A Suspended Work Item may hold multiple Await-Conditions and resumes on the first match.
- §4.1 FR-5 / §4.3 FR-13 — Single-parent, acyclic, **single-tenant** Work Tree enforced at spawn time; default max depth 32 (configurable); breadth uncapped.
- §4.2 FR-7 — The v1 event catalog closes the brainstorm's open-ended list; names follow ecosystem past-tense convention.
- §4.2 FR-10 — Reject defaults to requeue (`Queued`); Expire is Due-Date/TTL-driven and terminal; Cancel/Expire **cascade** to active descendants.
- §4.3 FR-11 — Roll-Up is an eventually-consistent, idempotent projection on the substrate's projection infrastructure.
- §4.4 FR-15 — Resume is a command keyed to an Await-Condition and is idempotent (duplicate trigger = no-op).
- §4.5 FR-18 — Claim "eligibility" filtering is deferred to Theme 4; in v1 any tenant Executor may claim a `Queued` item (single claim wins).
- §4.5 FR-19 — Proposed ordered AuthorityLevel set `{ Read, Contribute, Coordinate, Administer }`; carried-not-enforced in v1; additive-tolerant.
- §4.6 FR-21 — v1 references a Conversation by correlation ID; it does not implement comment storage.
- §9 NFRs — Numeric performance budgets deferred to a later iteration.
- §10 — v1 idempotency relies on resume-against-state + substrate offset dedup; explicit per-act idempotency tokens deferred.
