---
title: "Hexalith.Works — Product Requirements Document"
status: draft
created: 2026-06-14
updated: 2026-09-13
---

# PRD: Hexalith.Works

### Amendment history

*Every post-final change to this PRD lands here, in the memlog (`.memlog.md`), and as an inline `(Amended …)` note at the changed text, in the same change.*

| Date | Approving proposal | Sections touched | Commit |
|---|---|---|---|
| 2026-06-14 | PRD finalized (Fast path; assumptions accepted) | all | `149d789`, `40d3603` |
| 2026-09-05 | `../../sprint-change-proposal-2026-09-05.md` §4.1 — hosting ownership + Conversation later-link | §1, §2.2, §4.7, FR-7, FR-21, FR-24, §6.1, §8, SM-1, SM-4; addendum package layout, event catalog, reference VOs | `7e7ef4e` |
| 2026-09-06 | `../../sprint-change-proposal-2026-09-06.md` §4.1 — VAL-H03 FR-20 tiebreak | FR-20 | `33a27e2` *(travelled in an unrelated `feat:` commit)* |
| 2026-09-08 | 2026-09-08 validation run (grade Fair, material drift) — this update, approved item by item in session | §0, §2.3, §3, §4.1, §4.2 (FR-6, FR-7, FR-8, FR-10), §4.3 (FR-3, FR-13), §4.4 (FR-15, FR-16, **FR-26 new**), §4.5 (FR-17, FR-18), §4.6 (FR-21), §4.7, §5, §6.1, §8, §9, §10, §11, §13, §14; addendum | *(uncommitted)* |
| 2026-09-13 | 2026-09-12 PRD, UX, and architecture validation reconciliation — approved recommendations in session | §0, §2.3, §3, §4.1–§4.6, §5–§6, §8–§14; addendum | *(uncommitted)* |

## 0. Document Purpose

This PRD specifies **v1** of Hexalith.Works — the work-item coordination kernel for the Hexalith ecosystem. v1 is **Theme 1 (Work Item Essence — the `WorkItem` aggregate, its lifecycle, Burn-Down, Executor Binding, and Saga) plus the kernel subset of Theme 2 (Thin-Core Architecture & Boundaries — ports, Reference Value Objects, the "what's next" query, the boundary record, and the platform-hosted harness)**; Theme 2's remaining items — the non-LLM command surfaces (MCP as an actor channel, CLI as scriptable work) — are deferred and carried as their own roadmap row in §12. *(Restated 2026-09-08; was "the foundation: Themes 1 & 2", which §5 contradicted.)* It is written for the architect who will turn it into a solution design, the developers who will implement the `WorkItem` aggregate, and the owners of the sibling Hexalith modules Works references. It builds on — and does not duplicate — the finalized **product brief** (`../../briefs/brief-works-2026-06-14/brief.md`), its **addendum** (foundation action plan, deferred-theme backlog, competitive digest), and the **brainstorming session** (`../../brainstorming/brainstorming-session-2026-06-14-0910.md`, 44 ideas / 6 themes). Vocabulary is anchored in §3 Glossary and used verbatim throughout; features group globally numbered FRs nested under them; and inferred decisions are tagged inline `[ASSUMPTION: …]` and collected in §14. Technical-how depth (detailed substrate constraints, a proposed event catalog, port-shape sketches, the registry attachment protocol) lives in `addendum.md` for the architecture phase. Post-final amendments are listed in the Amendment history above and marked inline where they land; the Architecture Decision Register (`../../architecture.md`) is cited by `AD-nn` where it has bound a mechanism this PRD deferred (§13).

Scope decision (confirmed 2026-06-14, restated 2026-09-08): v1 delivers Theme 1 and the kernel subset of Theme 2 as buildable requirements; the Theme 2 remainder and Themes 3–6 are captured as a forward-looking **Roadmap / Designed-For** section (§12) so the seams laid in v1 — ports, the signed-raw-act audit model, and a cost-ready Burn-Down — are documented but not specified as v1 work.

## 1. Vision

Hexalith.Works is the small, durable spine that owns a unit of *work to be done* and coordinates *who does it* — a system or AI agent, an internal user, or an external person reached by email — without becoming a task database or a workflow-diagram engine. A **Work Item** burns down toward zero remaining effort, competes for attention by priority and due date, spawns and suspends like a durable saga, and rolls its remaining effort up a parent→child tree so an objective's all-in remaining work is a single number when the tree shares a Unit — the default — and one number per Unit otherwise. Everything else — identities, dialogue, persistence, isolation, IDs — is a late-resolved reference to a sibling Hexalith module, never copied.

The defining bet is **"everything is a Party":** system, user, and external-by-email collapse into one **Executor Binding** (`PartyId + Channel + AuthorityLevel`), so assignment, reassignment, and human⇄AI handoff run identically for a bot, a colleague, or a customer — with zero branching on executor type. A second bet, laid structurally in v1 and realized later, is **AI in the loop but never in the system-of-record:** the canonical event is the raw act, and any interpretation of it is a recomputable projection.

v1 proves the spine. It delivers a pure, event-sourced domain assembly — the aggregate, its lifecycle, its raw-act events, the recursive (cost-ready) Roll-Up, the Executor Binding, and the module ports as abstractions — plus the canonical two-line EventStore domain-service host. A platform-owned Aspire AppHost composes Works with its substrate dependencies for integration testing, so the LLM-native interaction, executor routing, cost governance, and security-hardening themes can be built on top without reshaping the core or duplicating platform hosting inside the domain module.

## 2. Target User

### 2.1 Jobs To Be Done

- **As a Hexalith builder,** I need a single domain object to coordinate work across system/AI, internal, and external doers, so I stop stitching three systems (a task manager, a durable-execution engine, an approvals tool) together with bespoke glue.
- **As a builder,** I need to attach a new kind of doer (an MCP agent today, a Slack approver tomorrow) at one well-defined seam, without editing the core.
- **As a builder,** I need every state change to be an append-only, replayable fact in the Hexalith event-sourcing substrate, so audit and Roll-Up are derivable, not reconstructed by hand.
- **As a system/AI executor (a Party),** I need to claim, advance, suspend, and complete work through one uniform command surface, identically to how a human executor would.
- **As an end user (the brief's co-primary audience; in v1 served *through* builders),** my work must be capturable and advanceable from wherever it surfaces — inbox, chat, an assistant — instead of scattering into shadow lists because it never reached a task app. v1 makes the kernel channel-ready for this; the adapters that deliver it are Theme 3.
- **As an objective owner (served later, through builders),** I need one rolled-up number for the remaining effort of a whole Work Tree.

*This restates the brief's two-sided problem at the kernel altitude — builder "stop stitching three systems" pain and end-user "work never gets captured" pain. v1 directly relieves the builder side and lays the seams for the end-user side.*

### 2.2 Non-Users (v1)

*These are co-primary audiences of the **product** (per the brief) who are not **direct v1 consumers** because the surfaces they need are deferred — a v1 scoping cut, not a narrowing of the thesis.*

- **End users via channel surfaces** — people creating/advancing work from email, chatbot, MCP tools, or CLI. The brief commits to "omnichannel capture from day one" *as a product vision*; v1 lays the **Channel** seam so the model is channel-ready but ships no production channel adapter (Theme 3). v1's direct consumer is the builder, exercising the kernel through the platform-owned Aspire topology. *(This is the one place the PRD deliberately tightens the brief's "day one" language to "seam on day one, adapter later" — see §5.)*
- **Tenant admins setting escalation ladders / spend caps** — the policy surfaces they configure are Themes 4 & 5.
- **Auditors querying a signed non-repudiation record** — the raw-act event model is laid in v1; the auditor-facing query/UI and signed-link enforcement are Theme 6.

### 2.3 Key User Journeys

*Lean, capability-illustrating narratives for a v1 that is a headless domain kernel. UJ-1–UJ-3 are realized by v1; UJ-4 is the deferred end-user horizon the kernel is shaped to enable, shown to anchor the §12 roadmap.*

- **UJ-1. Capability scenario — a builder wires Works into a module.** A Hexalith builder references the Works domain assembly, resolves the reference value objects (Party, Conversation, Tenant) for their context, supplies the no-LLM `IExpectationResolver`, and issues a create command. They get back an event-sourced Work Item with correct identity under their tenant — no infrastructure code written, no sibling-module data copied.

- **UJ-2. Capability scenario — a system/AI executor burns down work through one uniform surface.** An authenticated service Party — channel = MCP, machine authority — is bound to a Work Item and reports progress in the item's Unit until Remaining reaches zero, at which point the item completes. The actor must match the bound Executor; the exact same commands and actor-to-binding rule advance a human or external Party, and the code path does not branch on executor kind. *Realizes the "everything is a Party / zero branching" build signal.*

- **UJ-3. A Work Item spawns a child, suspends, and resumes.** Ada's release-checklist item — driven by a system Party on the MCP channel — spawns a child for the sign-off it cannot do itself (through the Work-Tree Registry's reserve act — FR-16), parks itself on an Await-Condition (the child's completion), and the Reactor (FR-26) resumes it when the child completes — the parent's rolled-up Remaining reflecting the child's Burn-Down throughout. *Realizes the durable-saga + correct-Roll-Up build signal.* **Edge case:** if the item is also parked on a date that arrives first, the date trigger resumes it independently.

- **UJ-4. (Deferred — Theme 3 horizon, not built in v1.)** Mary captures a to-do in one line of email; an external supplier she only reaches by inbox advances it with a single tap and no login; the AI's reading of the supplier's reply is a Projection over the verbatim signed reply. Before that production journey ships, the action link must satisfy Theme 6's bound, expiring, single-use/idempotent, forwarding-safe, and step-up requirements, and a used or expired link must offer a channel-appropriate no-login recovery path. v1 lays the Executor Binding, Await-Condition, and Raw-Act Domain Event seams this depends on; the email-as-UI, magic links, recovery surface, and NL parsing are deferred.

## 3. Glossary

*Downstream workflows and readers must use these terms exactly. FRs, UJs, and SMs use Glossary terms verbatim.*

- **Work Item** — the irreducible coordination unit and *an* aggregate root: an obligation with a Burn-Down, a Schedule, a Status, an Executor Binding, references to its parent and children, and an optional Await-Condition. Owns those facts; references everything else. The parent→child *edge* itself is owned by the Work-Tree Registry. *(Amended 2026-09-08 — AD-21; was "the aggregate root … optional parent/children".)*
- **Work-Tree Registry** — the tenant-scoped, event-sourced aggregate that owns every parent→child edge of a tenant's Work Trees and is the sole authority for tree shape (single-parent, acyclic, bounded depth, single-tenant). Attachment is *reserved* in the registry first; the Work Items then record the edge. Its commands and events are part of the v1 domain contract (FR-7, FR-16). *(Added 2026-09-08 — AD-21.)*
- **Reactor** — the mechanical process manager that owns every cross-aggregate behaviour (registry reservation → spawn, child-completion resume, cascade termination, date/expiry reminders) by translating Domain Events into commands, outside any aggregate. It is what earlier drafts called "the engine" (FR-26). *(Added 2026-09-08.)*
- **Obligation** — what the Work Item commits to getting done: a human-readable description plus an optional **Expectation** reference. Not an implementation of the work.
- **Expectation** — a representation of "what is expected now, and from whom," resolved from the item's state by the `IExpectationResolver`. In v1, a structured, non-LLM value; later AI-inferred (Theme 3).
- **Burn-Down** — the trio of unit-tagged quantities **Estimated**, **Done**, **Remaining** describing progress toward completion. Progress is a fact (Remaining decreases), not a status flag. *Done* is always the quantity; the state is *Completed*. The v1 Burn-Down is the single Effort Meter; a second Cost Meter reuses the identical machinery later (Theme 5).
- **Meter** — one Burn-Down instance: a Unit plus its Estimated and Done, with Remaining derived. v1 has exactly one Meter per Work Item (Effort); Theme 5 adds a second (Cost) of the identical shape.
- **Effort** — the v1 Burn-Down dimension (work to do). **Cost** is the second, cost-ready-but-deferred dimension (Theme 5).
- **Unit** — the domain-chosen measure a Burn-Down is expressed in (e.g., hours, steps, tokens, interactions). Per-item; pluggable; not global.
- **Remaining** — Estimated minus Done for this item (never below 0), retained as the item's auditable Burn-Down quantity even after termination. **Completion invariant:** Remaining reaching 0 through progress completes the item; an explicit Complete may close an item whose reported Remaining is still above 0 (FR-8). Roll-Up uses the distinct terminal-aware contribution defined by FR-11. *(Amended 2026-09-13; separates retained Remaining from Roll-Up contribution.)*
- **Roll-Up** — the recursive Projection where a Work Item's rolled value is the sum of its own and each attached descendant's **effective contribution**: 0 for terminal items, 0 plus an unestimated count for active unestimated items, and Remaining for active estimated items (FR-11).
- **Work Tree** — a Work Item and its transitive children; acyclic, single-parent.
- **Executor** — the doer of a Work Item: a system/AI agent, an internal user, or an external person — all modeled as a Party.
- **Party** — an identity from `Hexalith.Parties`, referenced by `PartyId`. The single executor concept; there is no "external party" type.
- **Channel** — the delivery/interaction medium for an Executor (e.g., MCP, CLI, chatbot, email). Orthogonal to the Executor; a Party may change Channel mid-work through Handoff without changing Status.
- **AuthorityLevel** — what an Executor Binding is permitted to do. Carried on the binding in v1; enforcement is Theme 4/6. *(Proposed ordered set — see §4.5 / `[ASSUMPTION]`.)*
- **Executor Binding** — the value object `PartyId + Channel + AuthorityLevel`. The one pluggable seam where new doer kinds attach.
- **Schedule** — the Work Item's **Priority** and **Due Date**, giving it standing in a contended queue.
- **Status** — the resting lifecycle state (see §4.2): one of Created, Assigned, Queued, InProgress, Suspended, Completed, Cancelled, Rejected, Expired. Resumption is a *transition* back into InProgress, not a resting state.
- **Await-Condition** — an event a Suspended Work Item is parked on. A Work Item may hold **more than one** and resumes on the **first match** — a child completing, a date arriving, or an external signal correlated by ID. (Unifies dependencies, timers, and external triggers into "park until event X.")
- **Saga** — the spawn→suspend→resume continuation pattern a Work Item embodies; its cross-aggregate legs are driven by the Reactor (FR-26).
- **Raw Act** — the literal, attributable fact recorded as a domain event (who acted, when, and the verbatim payload), as opposed to any interpretation of it.
- **Domain Event** — a past-tense, additively-versioned record of a Raw Act, serialized via `Hexalith.PolymorphicSerializations` (e.g., `WorkItemCreated`).
- **Projection** — a recomputable read model derived from the event stream (the Roll-Up and the "what's next" query are projections).
- **Reference Value Object** — a correlation identifier (Party, Conversation, Tenant, etc.) resolved on demand; never a copy of the referenced data.
- **Port** — a domain-owned abstraction (`IExpectationResolver`, `IExecutorRouter`) that keeps the domain pure and pushes LLM/cost/routing concerns into adapters.
- **Tenant** — the isolation boundary from `Hexalith.Tenants`; every Work Item, key, projection, and query is tenant-scoped.

## 4. Features

*Each subsection is a coherent feature: behavioral description first, FRs nested, global FR numbering for stable downstream references. Glossary terms used verbatim. All requirements are tenant-scoped (see §9 NFRs) even where not restated.*

| Feature group | Stable requirement inventory |
| --- | --- |
| Work Item Aggregate & State | FR-1–FR-5 |
| Lifecycle State Machine & Domain Events | FR-6–FR-10 |
| Effort Burn-Down & Recursive Roll-Up | FR-11–FR-13 |
| Suspend / Resume Saga | FR-14–FR-16, FR-26 |
| Executor Binding | FR-17–FR-19 |
| Thin-Core Boundaries & Module Ports | FR-20–FR-23 |
| Platform-Hosted Runtime Test Harness | FR-24–FR-25 |

### 4.1 Work Item Aggregate & State

**Description:** The `WorkItem` aggregate is the irreducible coordination unit. It owns its identity, its Obligation (description + optional Expectation reference), its single Executor Binding, its Effort Burn-Down (unit-tagged Estimated/Done/Remaining), its Schedule (Priority + Due Date), its Status, its references to its parent and children, and its optional Await-Condition. It owns only these facts; identities, dialogue, persistence, and isolation are Reference Value Objects, and the parent→child edges are owned by the Work-Tree Registry (FR-5, FR-13, FR-16). Realizes UJ-1. *(Amended 2026-09-08 — AD-21.)*

**Functional Requirements:**

#### FR-1: Create a Work Item
A builder or Executor can create a root Work Item with at minimum an Obligation description and a Tenant context, optionally supplying an initial Estimated effort (+Unit), Schedule, and Executor Binding. Ordinary creation cannot supply a parent reference; child creation is an origin-restricted Reactor act under FR-16.

**Consequences (testable):**
- Creating a Work Item emits `WorkItemCreated` carrying the Obligation, Tenant, and any supplied Burn-Down/Schedule/parent/binding fields.
- A caller-facing Create carrying a parent reference is rejected. A child Create carrying a parent reference is accepted only from the Reactor with evidence of the matching active registry reservation; a released or superseded reservation produces the stable rejection defined by FR-16. *(Amended 2026-09-13 — closes the registry-first bypass.)*
- A created Work Item has a canonical identity consistent with the substrate's `{tenant}:{domain}:{aggregateId}` model and Status `Created`.
- **Creation with an Executor Binding still rests at `Created`** *(2026-09-08)*: the supplied binding is recorded on `WorkItemCreated` as the intended Executor, but the item enters `Assigned` only through an explicit Assign act (which may restate the same binding), so `WorkItemAssigned` is the single entry into `Assigned` and every assignment is its own Raw Act.
- Creation with no Estimated effort is valid; Remaining is then undefined-until-estimated and the item cannot be `Completed` via the Burn-Down path until an estimate exists `[ASSUMPTION: an unestimated item completes only by the explicit Complete act (FR-8), not by the Remaining=0 rule]`.

#### FR-2: Carry an Obligation with an optional Expectation reference
A Work Item holds a human-readable Obligation description and an optional reference to an Expectation resolved via the `IExpectationResolver` port.

**Consequences (testable):**
- The Obligation description is required, non-empty, and **bounded** at creation: longer content belongs in a linked Conversation (FR-21) or a referenced document, not in the aggregate — Works is not the system of record for the content of work (§5). `[ASSUMPTION: maximum 4,000 characters; exceeding it is a domain rejection. Added 2026-09-08.]`
- The Expectation is held as an optional **Expectation reference** — a Reference Value Object on the aggregate — and resolved on demand through the port; no interpreted value is stored. When no Expectation is resolved, the Work Item is fully valid (the no-LLM resolver may return an empty/structured default). `[ASSUMPTION: the Expectation is referenced/resolved on demand, not stored as an interpreted value on the aggregate — keeping interpretation a Projection.]`

#### FR-3: Hold a unit-tagged Effort Burn-Down
A Work Item carries Estimated, Done, and Remaining effort, each tagged with the item's Unit.

**Consequences (testable):**
- Estimated, Done, and Remaining are expressed in the same Unit for a given item; the Unit is set per-item, not globally.
- **The Unit is immutable once set** *(2026-09-08 product decision — was §13 OQ6, now AD-04)*: the first accepted estimate (at creation or by the first `ReEstimate`) establishes the item's Unit; a later `ReportProgress` or `ReEstimate` in a different Unit is a domain rejection that leaves Unit, Estimated, and Done unchanged.
- Estimated is never negative; Remaining is derived as Estimated − Done (never below 0). Roll-Up uses FR-11's terminal-aware effective contribution rather than raw Remaining.
- Mixed-Unit arithmetic across items is never performed implicitly; Roll-Up across differing Units is governed by FR-12. `[ASSUMPTION]`

#### FR-4: Carry a Schedule (Priority + Due Date)
A Work Item carries a Priority and an optional Due Date that establish its standing in a contended queue.

**Consequences (testable):**
- Priority and Due Date are settable at creation and changeable later (FR-9), each change emitting an event.
- Priority is a small ordered set — `Critical`, `High`, `Normal`, `Low` — ordered by urgency, higher urgency first (FR-20); it is additive-tolerant and carries no routing score, band, or weight (Theme 4). *(Committed 2026-09-08 — was deferred as §13 OQ2; AD-03.)*
- A Work Item with neither Priority nor Due Date is valid and sorts last in the "what's next" query (FR-20).

#### FR-5: Hold parent/children references and Await-Conditions
A Work Item references at most one parent and zero-or-more children, and may hold one or more Await-Conditions while Suspended. The edge behind each reference is owned by the Work-Tree Registry; Work Item events may record the attachment intent while the edge is `Reserved`, but only durable child-creation evidence makes the edge `Attached` and authoritative. *(Amended 2026-09-13 — supersedes the 2026-09-08 ChildSpawned-first attachment rule.)*

**Consequences (testable):**
- A Work Item has at most one parent; the Work Tree is acyclic (FR-13). The guarantee is enforced by the Work-Tree Registry at edge reservation, not by the Work Item at spawn time; the Work Item re-asserts the registry-supplied facts when it records the spawn. *(Amended 2026-09-08 — supersedes the June `[ASSUMPTION: enforced at spawn time]`.)*
- A Suspended Work Item may be parked on multiple Await-Conditions simultaneously and resumes on the first to fire (e.g., a child completing *or* a date arriving — realizing the UJ-3 edge case). `[ASSUMPTION — confirmed 2026-06-14.]` The resume clears the whole Await-Condition set of that suspension; a later trigger for one of the cleared conditions follows FR-15 (a domain rejection, unless it repeats the consumed condition, which is a no-op).
- Children are referenced by ID (Reference Value Objects), not embedded; the authoritative list of a Work Item's children is the registry's `Attached` edges, and the Work Item's own children references are a convenience mirror of them.

### 4.2 Lifecycle State Machine & Domain Events

**Description:** A Work Item moves through an explicit lifecycle, and every transition is a Raw-Act Domain Event recorded append-only via `Hexalith.PolymorphicSerializations`. Completion is a recorded act, not a flag: Remaining reaching 0 through progress completes the item, and an Executor may also complete it explicitly (FR-8). Realizes UJ-2, UJ-3.

**Functional Requirements:**

#### FR-6: Enforce the lifecycle state machine
The aggregate enforces a defined set of legal transitions. The forward path is `Created → Assigned | Queued → InProgress → Suspended → InProgress → Completed`; in addition, `Assigned ↔ Queued` is bidirectional (FR-18), Cancelled and Expired are reachable from every non-terminal state, and terminal Rejected is reachable only from `Assigned` with `Requeue = false` (FR-10).

**Legal-transition table** *(normative; added 2026-09-08 as the product-level answer to "which transitions exist" — the architecture's `docs/lifecycle-transition-matrix.md` mirrors it 1:1 and adds mechanism detail).* Legend: `→X` accept, resting at X and emitting the paired event; `R` domain rejection (`WorkItemTransitionRejected`, no state change); `NoOp` acknowledged duplicate, no event.

| From \ Act (event) | Assign (`WorkItemAssigned`) | Queue (`WorkItemQueued`) | Claim (`WorkItemClaimed`) | Suspend (`WorkItemSuspended`) | Resume (`WorkItemResumed`) | Complete (`WorkItemCompleted`) | Cancel (`WorkItemCancelled`) | Reject (`WorkItemRejected`) | Expire (`WorkItemExpired`) |
|---|---|---|---|---|---|---|---|---|---|
| **Created** | →Assigned | →Queued | R | R | R | R | →Cancelled | R | →Expired |
| **Assigned** | →Assigned *(rebind)* | →Queued *(requeue)* | →InProgress | R | R | R | →Cancelled | →Queued *(requeue, default)* / →Rejected *(non-requeue)* | →Expired |
| **Queued** | →Assigned | R | →InProgress | R | R | R | →Cancelled | R | →Expired |
| **InProgress** | R | R | R | →Suspended | NoOp *(consumed-match replay)* / R | →Completed | →Cancelled | R | →Expired |
| **Suspended** | R | R | R | R | →InProgress *(current match)* / R *(non-match)* | →Completed | →Cancelled | R | →Expired |
| **Completed** | R | R | R | R | NoOp *(consumed-match replay)* / R | NoOp | R | R | R |
| **Cancelled** | R | R | R | R | NoOp *(consumed-match replay)* / R | R | NoOp | R | R |
| **Rejected** | R | R | R | R | NoOp *(consumed-match replay)* / R | R | R | NoOp *(non-requeue dup)* / R *(requeue)* | R |
| **Expired** | R | R | R | R | NoOp *(consumed-match replay)* / R | R | R | R | NoOp |

Supplemental acts: `ReportProgress` is legal only from `InProgress` and may complete the item; `CorrectProgress` is legal from `InProgress` and from a `Completed` item whose completion was progress-driven, and may cross between those two states according to corrected Remaining (FR-8). `Handoff` is legal from `InProgress` and `Suspended` and preserves Status (FR-17). `ReEstimate` and `Reschedule` are legal from every non-terminal state and never change Status (FR-9); `LinkConversation` is lifecycle-neutral (FR-21). `SpawnChild` is legal on any non-terminal parent and never changes the parent's Status (FR-16).

**Consequences (testable):**
- Only transitions in the table and the two supplemental acts above are accepted; any other transition is a domain rejection (`IRejectionEvent`), not an exception. State-based no-ops are limited to an exact duplicate terminal act, replay of the consumed Resume match, and explicitly named semantic equivalents; transport-key replay returns the original outcome (§10).
- `Assigned` is the push entry (a specific Executor bound); `Queued` is the pull entry (claimable; see FR-18); an item may move `Assigned → Queued` (requeue) and `Queued → Assigned` (direct assign).
- **Starting work is a Claim** *(2026-09-08 product decision)*: `Claim` is the single entry into `InProgress` from both `Assigned` and `Queued`, emitting `WorkItemClaimed`. A pushed Executor *claims its own assignment* to start; there is no `WorkItemStarted` event, and the first `ProgressReported` is not a start act — progress on a non-`InProgress` item is rejected.
- **Reject has two outcomes** *(2026-09-08 product decision, resolving the "terminal state that isn't" ambiguity)*: `Reject` is legal only from `Assigned` (a bound Executor declining before starting). With `Requeue = true` (default) the item rests at `Queued` for reassignment — one `WorkItemRejected` event, no `WorkItemQueued`; with `Requeue = false` the item rests at terminal `Rejected`. The resting status is read from the event's `Requeue` flag. An Executor already `InProgress` cannot reject: it completes, suspends, or is cancelled.
- **Active work changes hands only through Handoff** *(amended 2026-09-13)*: `InProgress` and `Suspended` still reject ordinary `Assign` and `Queue`, but accept the dedicated, auditable `Handoff` act from the current bound Executor. Handoff changes the Executor Binding, emits `WorkItemHandedOff`, and preserves Status and Burn-Down (FR-17). This supersedes the 2026-09-08 prohibition on mid-work handoff.
- Resumption is a *transition* from `Suspended` back to `InProgress` (FR-15); there is no resting `Resumed` status.
- No transition is legal out of a terminal state other than the narrow correction rule: `CorrectProgress` may reopen only a progress-driven `Completed` item when corrected Remaining becomes positive (FR-8). Explicitly completed and abnormally terminated items remain terminal.

#### FR-7: Record raw-act domain events
Each state change and progress fact is recorded as a past-tense Domain Event capturing the acting Party, timestamp, and verbatim payload.

**Consequences (testable):**
- v1 `WorkItem` success-event catalog (additively extensible): `WorkItemCreated`, `WorkItemAssigned`, `WorkItemQueued`, `WorkItemClaimed`, `ProgressReported`, `ProgressCorrected`, `ReEstimated`, `WorkItemRescheduled`, `ChildSpawned`, `WorkItemSuspended`, `WorkItemResumed`, `WorkItemHandedOff`, `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`, `ConversationLinked`. `ProgressCorrected` and `WorkItemHandedOff` are additive 2026-09-13 contracts; every existing payload remains unchanged. `WorkItemRescheduled` carries Priority and/or Due-Date changes (FR-9), and `ConversationLinked` realizes FR-21's post-creation reference link. *(Amended 2026-09-13; the former fifteen-event catalog was explicitly additive.)*
- The Work-Tree Registry's events (edge reserved, attached, released) and a **dedicated spawn-rejection event** carrying `(Tenant, ParentWorkItemId, ChildWorkItemId)` — emitted by the parent when it refuses a reserved spawn so the Reactor can release the edge mechanically — join the catalog additively (FR-16); the existing `WorkItemTransitionRejected` shape stays frozen. Registry contracts are governed like every other contract (catalog + golden corpus, §8). *(Added 2026-09-08 — AD-21.)*
- Events store the Raw Act (verbatim reported values), not interpreted/derived values; the acting Party identity and timestamp are recorded in the EventStore envelope — Works does not populate envelope metadata.
- **The actor of every act is the authenticated identity, recorded by the substrate** *(2026-09-08 product decision)*: for every command — an Executor's progress as much as a coordinator's Assign, Reassign, Cancel, ReEstimate, or Reschedule — the envelope's acting party is set from the identity the platform authenticated (§9), never from a field the caller supplies, and never inferred from the Executor Binding (the binding says who is *responsible*, the envelope says who *acted*). Works adds no `ActorPartyId` payload field; a caller-supplied actor would be an assertion §9 forbids treating as authority. **System-originated acts** — a reminder-fired Expire or date Resume, a cascaded Cancel/Expire, a child-completion Resume, the Reactor's `SpawnChild` — record the originating **workload identity** as actor, together with its tenant-delegation context and a causation link to the Domain Event that triggered them, so the Raw Act reads "the Reactor, for tenant T, because of event E". An event with no authenticated actor is never admitted to a stream (negative test).
- The ordered event stream **is** the Work Item's narrative history (the brainstorm's "comment stream and event stream are two views of one history"); `ProgressReported`, `ProgressCorrected`, `ReEstimated`, and abnormal-termination events may carry an optional human-readable note — the act's own annotation, part of the Raw Act, **bounded** and never a comment thread. Conversational dialogue itself is delegated to `Hexalith.Conversations` by correlation ID (FR-21). `[ASSUMPTION: note ≤ 1,000 characters; a longer note is a domain rejection. Added 2026-09-08.]`
- Rejection outcomes implement `IRejectionEvent` and do not mix success and rejection payloads in one result.

#### FR-8: Report progress and complete
An Executor can report progress in the item's Unit; the item completes either when Remaining reaches 0 through progress or when the Executor explicitly completes it. *(Amended 2026-09-08 — product decision on completion semantics; was "complete by Remaining=0".)*

**Consequences (testable):**
- `ProgressReported` decreases Remaining by the reported Done delta (clamped at 0); progress is accepted only while `InProgress` (FR-6).
- **A progress delta is strictly positive** *(2026-09-08 product decision — was §13 OQ6, now AD-17)*: `ReportProgress` with a delta ≤ 0 is a domain rejection with no state change; there is no negative-progress report. Progress in a Unit other than the item's established Unit is likewise rejected (FR-3).
- **Progress correction is additive and auditable** *(added 2026-09-13)*: `CorrectProgress` records `ProgressCorrected` with the prior and corrected cumulative Done values and never deletes or rewrites history. It is responsibility-bound to the authenticated current Executor (§9), retains the established Unit, and rejects a corrected Done below 0. From `InProgress`, corrected Remaining > 0 preserves `InProgress`, while corrected Remaining = 0 emits `WorkItemCompleted` and rests at `Completed`. From a progress-driven `Completed` item, corrected Remaining > 0 reopens to `InProgress`; Remaining = 0 leaves it `Completed`. It is rejected for an explicitly completed or abnormally terminated item.
- **Two completion acts, one event** *(2026-09-08 product decision)*: (1) **by Burn-Down** — a `ReportProgress` whose delta drives Remaining to 0 completes the item in the same accepted act, emitting `ProgressReported` followed by `WorkItemCompleted`; (2) **explicitly** — `Complete` is legal from `InProgress` or `Suspended` at *any* Remaining, for estimated and unestimated items alike, emitting `WorkItemCompleted`. An explicit completion is a Raw Act stating the work is done: Estimated and Done stay exactly as last reported (the Burn-Down is not rewritten), and a `Completed` item contributes 0 Remaining to the Roll-Up (FR-11). Clients therefore never need a synthetic re-estimate-to-zero to finish work.
- `ReEstimated` never completes or reopens an item: a re-estimate below Done clamps Remaining to 0 and leaves the Status unchanged; Done exceeding Estimated is the visible record of an over-run (FR-9). It changes the plan, not the historical progress facts.
- Completion is terminal in v1 except for the narrow, provenance-preserving `CorrectProgress` rule above. There is no general Reopen act; an explicit Complete remains terminal.
- An **unestimated** item (no Estimated set) has no Remaining to reach 0 and completes only by the explicit act. `[ASSUMPTION — confirmed 2026-06-14.]`
- A crash or abandonment leaves Remaining > 0 and the item resumable — "retry" is "continue the Burn-Down," not a separate state.

#### FR-9: Re-estimate and reschedule
Any tenant member (authority carried-not-enforced in v1 — FR-19) can re-estimate remaining effort and change Priority/Due Date as first-class facts. *(Amended 2026-09-08; was "An authorized Executor".)*

**Consequences (testable):**
- `ReEstimated` records a new absolute Estimated (and therefore Remaining) and is a normal, expected event — over-run and partial progress are native, not errors. It is legal from every non-terminal Status, never changes Status, and never completes the item (FR-8); a re-estimate below Done clamps Remaining to 0 and leaves Done > Estimated as the record of the over-run. The first accepted estimate on an unestimated item fixes its Unit (FR-3). *(Amended 2026-09-08.)*
- Schedule changes emit events and update the "what's next" query ordering.

#### FR-10: Cancel, reject, expire
A Work Item can terminate abnormally via Cancel or Expire; a bound Executor can Reject an assignment.

**Consequences (testable):**
- `WorkItemCancelled` and `WorkItemExpired` are terminal; no further progress is accepted.
- **Cancel** is an explicit act by any tenant member (authority-gated when enforcement lands; carried-not-enforced in v1, per FR-19 — see its blast-radius note).
- **Reject** (`WorkItemRejected`) is a bound Executor declining its assignment while `Assigned`; by default the item rests at `Queued` for reassignment, and it reaches terminal `Rejected` only when the caller marks it non-requeuable (FR-6 table). `[ASSUMPTION — default = requeue; confirmed 2026-06-14.]`
- **Expire** (`WorkItemExpired`) fires when the currently effective Due Date passes (or, absent a Due Date, the currently effective configured per-tenant TTL intent) without completion; expiry is terminal with no auto-reactivation. Expire is legal from every non-terminal state, including `Suspended`, but is accepted only from the designated reminder/cascade origin (§9). A callback for a superseded schedule is an audited no-op that cannot mutate or expire the Work Item, and replay returns the same no-op outcome. The durable reminder adapter (§6.1) issues the command — `Handle` reads no clock; policy values are platform-host configuration, never read by the kernel (§13 item 6). *(Amended 2026-09-13 — stale schedule firings are harmless.)*
- **Cascade:** Cancelling or Expiring a Work Item cascades the same termination to its still-active descendants (a parent's death cancels its open subtree); already-terminal descendants are unaffected. The cascade is performed by the Reactor over the registry's `Attached` edges and is **eventual** (FR-26 states the window and the descendant's behaviour inside it). Terminal items contribute 0 to a parent's rolled Remaining (FR-11). `[ASSUMPTION — cascade-cancel chosen over orphaning; resolves the open "parent termination → children" question. Confirmed 2026-06-14; owner and consistency added 2026-09-08.]`
- **Cascade closure under concurrent attachment** *(added 2026-09-13)*: once parent termination is accepted, new reservations against that parent are rejected. Reservations accepted before the termination boundary must either release or finish attachment; a child that attaches afterward is still included in that termination's cascade. The cascade is quiescent only when every reachable `Attached` descendant is terminal or already terminal and every earlier `Reserved` edge is released or its resulting child has been terminated. Unresolved work remains discoverable, degrades readiness, and follows the configured timeout/escalation policy (§9–§10).

### 4.3 Effort Burn-Down & Recursive Roll-Up

**Description:** A Work Tree's rolled effort is the sum of each attached item's terminal-aware effective contribution, computed by a Projection over the event stream and built so the identical machinery serves a second (Cost) Meter later. Realizes UJ-3 and the "one number" build signal.

**Functional Requirements:**

#### FR-11: Maintain the recursive remaining-effort Roll-Up
The system maintains a Roll-Up projection where each Work Item exposes its own Remaining and its subtree-rolled Remaining.

**Consequences (testable):**
- For any Work Item, rolled Remaining = own effective contribution + Σ(effective contribution of every descendant reachable through `Attached` edges), stated flat so each item is counted once from its own stream. Effective contribution is 0 for every terminal item; 0 plus one unestimated-item count for an active unestimated item; otherwise it is the active estimated item's Remaining. The retained Burn-Down Remaining remains visible for audit and is never substituted for this terminal-aware contribution. *(Amended 2026-09-13 — resolves explicit-completion drift.)*
- **State-based, never delta-based** *(2026-09-08 product decision)*: the Roll-Up stores, per ancestor, each descendant's **last-known own contribution** keyed by that descendant's own stream position, and replaces it last-writer-wins; it never accumulates deltas. This is what makes the next two consequences compatible.
- A descendant's `ProgressReported` / `ProgressCorrected` / `ReEstimated` / `WorkItemCompleted` / terminal event is reflected incrementally in every ancestor's rolled Remaining (no whole-stream re-read per query).
- The Roll-Up is an eventually-consistent Projection, not a synchronous aggregate field; the aggregate's own Remaining and Status are authoritative and synchronous, and the rolled value never has the same type or shape as the own value, so no consumer can mistake one for the other. `[ASSUMPTION: eventual consistency via projection; a stale read is acceptable and converges — the brainstorm calls Roll-Up a "projection" but never states the consistency model.]`
- The Roll-Up is built on the substrate's projection infrastructure rather than custom read-side routing, and is **idempotent** under at-least-once, possibly out-of-order event delivery (replaying or reordering an event does not double-count, because a slot is only ever replaced by a later position of the same descendant). *(Mandated by the ecosystem projection rules.)*
- The Roll-Up may expose a subtree as **unavailable** during a declared repair window or rebuild staging; it never exposes a partial or stale-as-fresh value as if converged. *(Added 2026-09-08 — AD-22.)*
- An **active unestimated** Work Item contributes 0 Remaining and is counted in an *unestimated items* count exposed alongside the rolled figures, so a rolled 0 is distinguishable from "nothing estimated yet"; once terminal, it contributes neither Remaining nor an unestimated count. `[ASSUMPTION — added 2026-09-08; terminal behavior clarified 2026-09-13.]`
- The projection converges under every permitted cross-stream delivery order. If descendant state arrives before its registry edge becomes `Attached`, attachment incorporates the latest accepted descendant state rather than losing it; release, repair, replay, and reordered delivery cannot resurrect or double-count a contribution.
- Both projections (Roll-Up, "what's next") publish change notifications through the substrate's notifier seam so live surfaces (Theme 3) can update without polling; the notification carries no payload beyond the changed key. *(Added 2026-09-08.)*

#### FR-12: Roll up across heterogeneous units safely
The Roll-Up does not silently sum incompatible Units.

**Consequences (testable):**
- Same-Unit subtrees roll into a single number.
- **Unit inheritance at spawn** *(2026-09-08 product decision)*: a child spawned without an explicit Unit inherits its parent's Unit as the Unit of its first estimate; a child in a *different* Unit must be given that Unit explicitly at spawn. Mixed-Unit trees are therefore opt-in (e.g., an AI child metered in tokens under a parent in hours), and the default tree shares one Unit.
- For mixed-Unit subtrees, the Roll-Up exposes per-Unit subtotals rather than a coerced single figure. `[ASSUMPTION: no Unit conversion in v1; conversion policy (if any) deferred.]`

**Unit inheritance truth table** *(normative; added 2026-09-13):*

| Parent Unit at spawn | Child estimate at spawn | Explicit child Unit | Result |
| --- | --- | --- | --- |
| Any | Present | Present | Accept and establish the explicit child Unit. |
| Present | Present | Absent | Accept and establish the parent's Unit on the child. |
| Absent | Present | Absent | Reject; an estimate requires a Unit. |
| Any | Absent | Present | Accept as unestimated with that Unit fixed for the first estimate. |
| Present | Absent | Absent | Accept as unestimated with the parent's Unit captured for the first estimate. |
| Absent | Absent | Absent | Accept as unestimated and unitless; the first estimate must supply a Unit. |

Once an explicit or inherited Unit is captured at spawn, a later first estimate must use it; it cannot replace the pending Unit with a different one.

#### FR-13: Guard the Work Tree shape
Attaching a child enforces an acyclic, single-parent, single-tenant tree within a bounded depth. The guard is enforced by the **Work-Tree Registry** when the edge is reserved, evaluated against the registry's own state (no staleness window); the Work Item re-asserts the registry-supplied ancestry/depth facts when it records the spawn, as an assertion check, not as the authority. *(Amended 2026-09-08 — AD-21; was "Spawning enforces …".)*

**Consequences (testable):**
- Attaching a child that would create a cycle or a second parent is rejected as a domain rejection by the registry at reservation; no Work Item is written for a rejected attachment.
- Concurrent attempts to attach the same child serialize on the registry: exactly one edge is accepted, every other attempt receives a deterministic domain rejection.
- Caller-supplied tree facts (proposed ancestors, depth, limits) are never trusted as authority; a spawn whose asserted facts disagree with the registry's is rejected by the Work Item.
- A Work Tree is **single-tenant**: a parent and child must share a Tenant; a cross-tenant parent/child link is rejected (prevents a cross-tenant Roll-Up leak). `[ASSUMPTION — confirmed 2026-06-14.]`
- Tree depth is bounded by a configured maximum; exceeding it is rejected. `[ASSUMPTION: default max depth = 32; configurable per tenant (platform-host configuration — §13 item 6). The brainstorm states no limit — proposing a guard to bound runaway trees. Breadth/fan-out is not capped; the incremental Roll-Up (FR-11) keeps wide trees affordable.]`

### 4.4 Suspend / Resume Saga

**Description:** Suspension-on-an-event is the primitive: a Work Item parks on an Await-Condition and resumes when the matching event arrives. v1 handles child-completion and date/timer natively; external signals resume through a generic resume-by-correlation-ID port whose concrete adapters are deferred. Realizes UJ-3.

**Functional Requirements:**

#### FR-14: Suspend on an Await-Condition
An InProgress Work Item can suspend itself, recording the Await-Condition it is parked on.

**Consequences (testable):**
- `WorkItemSuspended` records the Await-Condition kind and its correlation key (child ID, target date, or external correlation ID).
- A Suspended item accepts no progress until resumed; it still participates in Roll-Up with its current Remaining.

#### FR-15: Resume on a matching trigger
The Reactor (FR-26) resumes a Suspended Work Item when its Await-Condition is satisfied.

Resume is driven by a **resume command** carrying an Await-Condition (kind + correlation key) matching one of the item's current Await-Conditions. The pure aggregate `Handle` never reads a clock or an outside system; child-completion, date/timer, and external signals arrive *as commands* issued by the Reactor and its adapters (the durable reminder adapter for dates — §6.1; an external adapter for webhooks/replies — Theme 3), keeping the domain pure.

**Consequences (testable):**
- **Child-completion:** a child's `WorkItemCompleted` raises a resume command to a parent parked on that child.
- **Date/timer:** the durable reminder adapter raises a resume command when a parked target date passes (the date is not read inside `Handle`).
- **External signal:** a resume command carrying the matching external correlation key resumes the item; the concrete external adapter (webhook/reply) is deferred (Theme 3). The correlation key is the contract Theme 3 fills. Matching compares kind *and* key: an external signal whose key text equals a child ID is not a child-completion match.
- Resume emits `WorkItemResumed`, records the one Await-Condition it consumed, clears the whole Await-Condition set of that suspension, and returns the item to `InProgress`.
- **One rule for non-matching and duplicate resumes** *(2026-09-08 product decision, aligning FR-15 and §10)*: while `Suspended`, a resume whose Await-Condition matches none of the current set is a **domain rejection** that leaves the set intact; after a resume, repeating the **consumed** Await-Condition is the only resume that is an **idempotent no-op** (so an at-least-once trigger firing twice is harmless); any other resume on a non-`Suspended` item is a domain rejection. The aggregate retains the last consumed Await-Condition to decide this — no unbounded history. Duplicate-command idempotency is the **aggregate's** job, not the substrate's (§10). `[ASSUMPTION — confirmed 2026-06-14 that duplicate triggers are no-ops; narrowed 2026-09-08 to "duplicate of the consumed condition".]`

#### FR-16: Spawn child work
A Work Item can gain one or more children, optionally suspending itself awaiting them. The **public act is the Work-Tree Registry's reserve command**, which carries the complete child-creation payload verbatim (Obligation, optional Estimated/Unit, Schedule, Executor Binding, Conversation reference, and whether the parent suspends awaiting the child). Once the edge is reserved, the Reactor (FR-26) drives the parent's internal `SpawnChild`, then the origin-restricted child Create (FR-1). `SpawnChild` is not a builder-facing command: it is accepted only from the Reactor's workload identity. The edge remains `Reserved` until durable child-creation evidence exists. *(Amended 2026-09-13 — child existence now precedes authoritative attachment.)*

**Consequences (testable):**
- A builder or Executor attaches a child by issuing the registry reserve command; a direct `SpawnChild` from an ordinary tenant-member identity is denied (§9 trusted origin).
- Write order on spawn: (1) the registry records the edge as `Reserved`; (2) the parent emits `ChildSpawned`; (3) the child's own stream durably accepts `WorkItemCreated` carrying the parent reference and matching active reservation evidence; (4) the registry records `Attached` from that durable creation evidence. Each step is its own aggregate write; none is atomic with the next. *(Supersedes the 2026-09-08 `Attached`-on-`ChildSpawned` decision.)*
- An edge is `Reserved → Attached` only on durable child-creation evidence, or `Reserved → Released` when the parent/child rejects the spawn or a bounded, platform-configured reservation timeout passes without that evidence. `Reserved` edges remain invisible to Roll-Up, cascade, and authoritative child enumeration. A failed or timed-out attempt has an auditable disposition and can never leave an `Attached` edge pointing to a missing child.
- Once a reservation is `Released` or superseded, a delayed saga leg is deterministically rejected: it cannot create or attach the child or mutate the parent/registry into a contradictory topology, and redelivery returns the same audited rejection. A duplicate submission using the original transport key replays its original outcome (§10).
- A duplicate child-spawn sequence for an already-`Attached` identical (parent, child) pair is a defined semantic no-op, never a release signal; any other rejection remains a failure requiring retry, compensation, or escalation.
- Only `Attached` edges exist for the Roll-Up (FR-11) and for cascade (FR-10); `Reserved` edges are invisible to both.
- Spawning respects the Work Tree guard (FR-13).

#### FR-26: Own cross-aggregate coordination in the Reactor *(added 2026-09-08)*
Every behaviour that spans more than one aggregate — registry reservation → parent spawn → child creation (FR-16), a child's completion resuming its parent (FR-15), cascade termination of a subtree (FR-10), and date/expiry reminders (FR-10, FR-15) — is performed by the **Reactor**, a mechanical process manager that translates Domain Events into commands and lives outside the `WorkItem` and Work-Tree Registry aggregates. The aggregates' `Handle` stays pure and single-aggregate (§9); the Reactor is the one place a cross-aggregate step may be taken. *(Resolves the "who owns the saga" gap: the Reactor is what UJ-3 and earlier drafts called "the engine".)*

**Consequences (testable):**
- **Ownership.** The Reactor is a Works-owned domain library (one of §8's optional domain-focused supporting libraries); the runtime that delivers events to it, checkpoints it, retries it, and fires reminders is substrate/platform-owned (FR-24). Works supplies the translations and the domain intents, never the plumbing.
- **Mechanical.** The Reactor contains no decision a pure `Handle` could not have produced: each translation is event → command(s) with no policy of its own; the same transport idempotency/causation key replays the original outcome for the supported redelivery horizon, and only explicitly defined equivalent target states may translate to success. An arbitrary fresh domain rejection is not proof that the original command succeeded. Cascade is driven from a checkpoint over a re-readable projection, never an in-memory loop.
- **Consistency guarantees.** Cross-aggregate effects are **eventual, not synchronous**: the parent's `ChildSpawned`, the child's `WorkItemCreated`, a parent's `WorkItemResumed` after its child completes, and each descendant's cascaded termination are separate writes that land after the triggering event, and every consumer must tolerate the window between them. Within that window: (a) a Suspended parent stays Suspended until the Reactor's resume lands; (b) a descendant of a Cancelled/Expired parent is a separate aggregate that reads no parent status inside `Handle`, so it continues to accept progress — or may complete — until its cascade command arrives; those acts are legitimate Raw Acts, remain in its stream, and a descendant that reached a terminal state first is left unaffected by the cascade (per-state table, FR-6). `[ASSUMPTION — 2026-09-08 product decision: a bounded post-termination window on descendants is accepted in exchange for a pure, single-aggregate `Handle`.]`
- **Durability.** A crash between the triggering event and the Reactor's command loses nothing: on recovery the Reactor resumes from its checkpoint and re-issues outstanding commands (including a missed child-completion resume and a half-finished cascade). Recovery cannot omit an accepted event at a page or checkpoint boundary. Unresolved spawn, cascade, resume, or reminder work remains durably discoverable and retried until resolved or explicitly disposed; stranded work degrades the affected capability's readiness and produces an operator-visible signal. Re-issue is safe across the supported retry/replay/restore horizon (§9, §10).
- **Provenance.** The Reactor acts under its own authenticated workload identity with an explicit tenant-delegation context, never a borrowed user identity (§9 identity provenance); it is the only originator permitted to submit `SpawnChild`.

### 4.5 Executor Binding — "Everything is a Party"

**Description:** System, user, and external doers collapse into one Executor Binding value object — `PartyId + Channel + AuthorityLevel`. Assign, reassign, and handoff are one operation across all three; the code path never branches on executor kind. This is the keystone and the single pluggable seam for new doer kinds. Realizes UJ-2 and the "zero branching / handoff = one operation" build signals.

**Functional Requirements:**

#### FR-17: Bind, reassign, and hand off via one uniform operation
A Work Item can be bound to an Executor and moved to a different Executor regardless of executor kind. `Assign`/reassign is the operation before work starts; the dedicated `Handoff` act is its auditable active-work counterpart.

**Consequences (testable):**
- Assigning to a system Party (Channel = MCP, machine authority), an internal-user Party, or an external Party (Channel = email) uses the identical command and emits `WorkItemAssigned`.
- Human→AI and AI→human movement uses the same binding shape in either direction: `Assign`/reassign while `Assigned`, or `Handoff` while `InProgress`/`Suspended`.
- `Handoff` is submitted by the authenticated current Executor, emits `WorkItemHandedOff` with the new Executor Binding, and preserves Status, Burn-Down, Schedule, and Await-Conditions. It is rejected from all other states and when the actor does not match the current binding. *(Added 2026-09-13.)*
- Changing Channel for the same Party uses reassign while `Assigned` and Handoff while `InProgress`/`Suspended`; both change only the binding and emit one event. There is no channel-specific act. *(Amended 2026-09-13 — see SM-5.)*
- **One binding, one doer** *(2026-09-08 product decision)*: a Work Item has exactly one Executor Binding — the Party currently responsible for doing the work. Approvers, observers, and the rungs of an escalation ladder are **not** Executor Bindings and never will be: routing candidates and ladder rungs belong to Theme 4's routing decision record behind `IExecutorRouter`; approval and observation grants belong to Theme 6 as Party-scoped participants recorded outside the aggregate. Both attach additively (new events/read models), with no reshape of the binding. The kernel therefore never needs a "second slot".
- No domain code branches on executor *kind*; the only variation is the binding's field values. *(This is a build-signal acceptance check — see SM-3, which states the two tests that can fail.)*

#### FR-18: Push and Pull coexist
A Work Item can be pushed (assigned to a specific Executor) or pulled (placed in a shared queue and claimed), and can move between modes.

**Consequences (testable):**
- A `Queued` item can be claimed by an authenticated tenant member only as themself, emitting `WorkItemClaimed` and transitioning the item to `InProgress` bound to that claimant. The same `Claim` act is how the authenticated bound Executor starts an `Assigned` item (FR-6) — one entry act into `InProgress` for both modes.
- **Single claim wins:** when two Executors race to claim the same `Queued` item, exactly one succeeds; the loser receives a domain rejection (the item is no longer claimable), serialized by the aggregate's single-writer/optimistic-concurrency model (mechanism detailed at architecture; see §9).
- An `Assigned` item can be returned to `Queued` (requeue) and a `Queued` item can be directly assigned — both normal transitions; requeue re-emits `WorkItemQueued` (which thus marks every entry into the queue, whether from `Created` or `Assigned`). `[ASSUMPTION: claim "eligibility" filtering is a routing concern and is deferred to Theme 4; v1 allows any Executor of the tenant to claim.]`

#### FR-19: Carry AuthorityLevel on the binding
The Executor Binding carries an AuthorityLevel describing what the Executor may do; v1 stores it as part of the contract without branching on its value. v1 nevertheless enforces the actor-to-binding and trusted-origin relationships in §9.

**Consequences (testable):**
- The binding persists the AuthorityLevel through create/assign/reassign events.
- **v1 relationship enforcement** *(amended 2026-09-13; supersedes membership-only authorization)*: authenticated tenant membership remains the baseline, but responsibility-bound Executor acts require the trusted acting Party to match the current binding, and system/Reactor acts require the designated workload origin. AuthorityLevel itself remains carried-not-read; planning/coordination acts that §9 assigns to any tenant member do not branch on its value. `[NOTE FOR PM: decide whether Coordinate/Administer gating on planning and termination acts lands before the first multi-team production tenant or at Theme 4 planning, whichever comes first. Owner: PM.]`
- `[ASSUMPTION: proposed ordered AuthorityLevel set = { Read, Contribute, Coordinate, Administer }, each describing what the *bound Executor* may do to *this* item. Read = may only await/observe the item it is bound to (e.g., a placeholder binding before real work is delegated); Contribute = report progress, correct progress, complete own work, answer an Expectation (covers external "confirm" and machine "auto-complete"); Coordinate = assign/reassign, reprioritize, spawn; Administer = approve spend, set caps, cancel. AuthorityLevel is never a role held by some *other* Party over the item — approvers and observers are not bindings (FR-17). Value-based enforcement is Theme 4/6; the set is additive-tolerant so it can grow without a V2 event. Confirmed 2026-06-14; relationship enforcement added 2026-09-13.]`

### 4.6 Thin-Core Boundaries & Module Ports

**Description:** Works owns Obligation + Burn-Down + Schedule + Executor Binding + suspend/resume Saga, and *references* everything else via Reference Value Objects resolved on demand. Cross-module concerns sit behind domain-owned Ports so the domain stays pure and LLM/cost/routing live in adapters. A written boundary decision record is a v1 deliverable. Realizes UJ-1.

**Functional Requirements:**

#### FR-20: Resolve a "what's next" ordering
The system exposes a read-side query returning a tenant's claimable/assigned Work Items ordered by Priority then Due Date, in two views: **an Executor's next work** and **the tenant's coordination view**. *(Consumer named 2026-09-08.)*

**Consequences (testable):**
- **Two views, one query** *(authorization amended 2026-09-13)*: called with an Executor's `PartyId`, the query requires that PartyId to match the authenticated Party and returns that Party's `Assigned` items plus the tenant's `Queued` pool ("what should *I* do next — my pushed work and what I could claim"). Called without one, it returns every `Assigned` and `Queued` item of the tenant only to a trusted internal origin; ordinary members cannot request the coordinator/all-work view until role enforcement exists. The filter predicate is: verified tenant membership (§9) → view entitlement → Status ∈ {`Assigned`, `Queued`} → for the Executor view, binding `PartyId` equals the authenticated Party or Status is `Queued`. No eligibility scoring, ranking, or routing input takes part (Theme 4).
- **Ordering** *(direction committed 2026-09-08)*: Priority in urgency order — `Critical`, then `High`, `Normal`, `Low`; an absent Priority sorts after every present one — then earliest Due Date first, an absent Due Date after every present one; then deterministic identity order (`WorkItemId` ordinal). Items with neither Priority nor Due Date therefore sort last by construction (FR-4). The identity tiebreak is a **deliberate product decision**: a pure function of identity is stable across rebuilds and immune to duplicate or out-of-order delivery, which a creation coordinate is not. Works records no cross-aggregate creation coordinate; edges that want the tiebreak to approximate true creation order should mint ids with the `Hexalith.Commons` sortable ULID generator (guidance, not an enforced contract). *(Amended 2026-09-06: was "creation order" — VAL-H03 resolved by approved correct-course.)*
- The query is served by the substrate's query/projection infrastructure (not custom read routing) and applies query-side authorization/result filtering in addition to tenant scoping (§9).
- This is a Projection/query only — no routing, assignment, or ranking *engine* (that is Theme 4).

#### FR-21: Reference sibling modules, never copy them
Identities, dialogue, persistence, isolation, and IDs are Reference Value Objects resolved on demand from the owning module.

**Consequences (testable):**
- Identity → `Hexalith.Parties` (PartyId); dialogue → `Hexalith.Conversations` (correlation ID); persistence/events → `Hexalith.EventStore`; isolation → `Hexalith.Tenants`; IDs → `Hexalith.Commons`.
- The aggregate stores correlation IDs, not denormalized copies of referenced data.
- A Conversation correlation ID can be supplied at creation or linked later with `LinkConversation`, which emits `ConversationLinked`; it is optional and resolved on demand. The first link is authoritative: repeating the same link is an idempotent no-op, while attempting to replace it with a different ID is a domain rejection that leaves state unchanged. A later link is accepted only while the Work Item is non-terminal; exact duplicate retries remain no-ops after terminal closure. Works stores only the correlation ID and never stores conversation content. The comment narrative and the event stream are two views of one history (FR-7). `[ASSUMPTION: v1 references a Conversation by ID; it does not implement comment storage.]` *(Amended 2026-09-05: `LinkConversation`/`ConversationLinked` later-link semantics — sprint-change-proposal-2026-09-05 §4.1.)*

#### FR-22: Expose module ports as abstractions
The domain depends on `IExpectationResolver` and `IExecutorRouter` as ports, with a no-LLM `IExpectationResolver` implementation shipped in v1.

**Consequences (testable):**
- The domain assembly compiles and all v1 tests pass with only the no-LLM `IExpectationResolver` and without any `IExecutorRouter` implementation wired (routing is deferred; the port exists).
- Both ports are deliberately present in v1 although one has no implementation and the other only a literal one: a C# port with no v1 caller has no event-schema cost, and these two are the *named* seams SM-C2 exempts from its "speculative machinery" rule — anything beyond them is not. *(Rationale added 2026-09-08.)*
- No LLM, cost, routing, or infrastructure type is referenced from the domain assembly.

#### FR-23: Produce the boundary decision record
v1 includes a written owns-vs-references boundary decision record as a tracked artifact.

**Consequences (testable):**
- The record enumerates, for each sibling module, what Works owns vs. references and why, and is referenced by the architecture phase.

### 4.7 Platform-Hosted Runtime Test Harness

**Description:** v1 ships the canonical EventStore domain-service host for Works. A platform-owned .NET Aspire AppHost stands up the shared dependencies needed to exercise the full event-sourced lifecycle in manual and automated tests. Works does not ship its own AppHost, Aspire, or ServiceDefaults project, and no production channel adapter (MCP/CLI/email) ships in v1. *(Amended 2026-09-05: was "an Aspire host that runs it under test" shipped by Works — sprint-change-proposal-2026-09-05 §4.1.)*

**Functional Requirements:**

#### FR-24: Run the kernel through the platform-hosted domain-service topology
Works exposes the canonical EventStore domain-service host, and a platform-owned Aspire AppHost composes Works with EventStore and shared infrastructure for local manual and automated testing. *(Amended 2026-09-05: was "Run the kernel under an Aspire host" owned by Works — sprint-change-proposal-2026-09-05 §4.1.)*

**Consequences (testable):**
- The end-to-end lifecycle (create → progress → spawn → suspend → resume → complete) runs under the platform-owned Aspire topology with correct Roll-Up.
- The Works repository contains no `*.AppHost`, `*.Aspire`, or `*.ServiceDefaults` project and does not duplicate platform health, telemetry, projection/query, Dapr, or subscription plumbing.
- The Works executable uses the EventStore domain-service SDK; a missing reusable runtime capability is added to the platform first and then consumed by Works.

#### FR-25: Exercise the command pipeline in tests
The kernel is exercisable through its command/event pipeline in automated tests without production adapters.

**Consequences (testable):**
- Tier-1 tests (aggregate `Handle`/`Apply`, projection handlers) run pure — no Dapr, network, browser, or containers.
- Integration tests use the substrate's testing fakes/builders or the platform-owned Aspire topology only where a real boundary is genuinely needed.

## 5. Non-Goals (Explicit)

- Works is **not** a task database or a system of record for the *content* of work; it owns coordination facts and references the rest.
- Works is **not** a workflow-diagram/BPMN engine; there are no authored process diagrams — the event log of ad-hoc work is the model.
- Works does **not** put AI in the system of record; interpretations are Projections over Raw-Act events.
- v1 builds **no** production channel adapters (email/magic-link, chatbot, MCP, CLI), **no** LLM-native interaction or NL parsing, **no** executor routing/escalation engine, **no** cost meter/spend governance, and **no Theme 6 user-facing hardening** (step-up auth, signed single-use links, consent/residency routing, DoS guards). These are the Theme 2 remainder and Themes 3–6 (§12). Platform identity/trusted-origin controls and the durable-data lifecycle, backup/restore, and disaster-recovery production-admission gates are cross-cutting platform readiness, not Theme 6 feature scope (§9–§10). *(Amended 2026-09-13.)* *(The brainstorm filed MCP-as-actor-channel and CLI-as-scriptable-work under foundation Theme 2; v1 deliberately defers even these non-LLM command surfaces — the kernel-only surface decision — while keeping the Channel seam so they attach later without core changes. They are the "Theme 2 remainder" row in §12.)*
- Works does **not** re-implement identities, dialogue, persistence, isolation, or ID generation — those remain owned by their sibling modules.

## 6. MVP Scope

### 6.1 In Scope (v1 — Theme 1 + the kernel subset of Theme 2)

- The `WorkItem` aggregate: identity · Obligation (+Expectation reference) · Executor Binding · Unit-tagged Effort Burn-Down (cost-ready) · Schedule · Status · parent/children refs · Await-Condition (FR-1–FR-5).
- The lifecycle state machine and the v1 raw-act Domain Event catalog (FR-6–FR-10).
- The recursive remaining-effort Roll-Up projection, tree-shape guard, and heterogeneous-unit safety (FR-11–FR-13).
- The **Work-Tree Registry** aggregate and its reserve/attach/release contract — the public attachment entry and the tree-shape authority (FR-13, FR-16). *(Added 2026-09-08 — AD-21.)*
- The suspend/resume saga: child-completion + date/timer native, plus the Resume command contract that a deferred external adapter can submit (FR-14–FR-16).
- The **Reactor** — the mechanical process manager for spawn, child-completion resume, cascade, and reminders (FR-26). *(Added 2026-09-08.)*
- The **durable reminder adapter** for date-resume and expiry (FR-10, FR-15). Owners *(2026-09-08 — AD-11/AD-25, AD-20 R6)*: Works supplies the domain intents (which reminders to register, cancel, or reschedule from which lifecycle events, and the `Resume`/`Expire` commands they fire); the generic durable-reminder and recovery-reconciliation seam is `Hexalith.EventStore`; scheduler persistence/HA/backup and the callback failure policy are `Hexalith.Platform` (residual openness: architecture VAL-H07). It is not a channel adapter (§4.7): it is the v1 source of the date-triggered resume SM-1 requires.
- The Executor Binding (`PartyId + Channel + AuthorityLevel`), uniform assign/reassign plus active-state Handoff, push+pull, actor-to-binding enforcement, and AuthorityLevel carried-not-read (FR-17–FR-19).
- Thin-core boundaries: "what's next" query, Reference Value Objects, ports (`IExpectationResolver` no-LLM impl + `IExecutorRouter` abstraction), boundary decision record (FR-20–FR-23).
- The canonical EventStore domain-service host plus a platform-owned Aspire integration harness (FR-24–FR-25).

### 6.2 Out of Scope for MVP

- LLM-native interaction, AI-inferred Expectation, magic links, NL parsing, email-as-UI — **Theme 3**. *(Seam laid: `IExpectationResolver`, Await-Condition, Channel.)*
- Executor routing & escalation ladder, push/pull auto-assignment, explainable decision record — **Theme 4**. *(Seam laid: `IExecutorRouter` port, push/pull states, AuthorityLevel field, assignment events.)* The routing decision record (candidates/score/cost-estimate/confidence) needs **no v1 event placeholder**: because the substrate's schema evolution is additive and tolerant (§8), Theme 4 can introduce a new `WorkItemRouted`-style event and/or additive fields on `WorkItemAssigned` without a V2 type or a reshape. *(Resolves the draft's eighth Open Question — the routing-decision placeholder — see the §13 preface.)*
- Cost as a second Burn-Down, spend caps, graceful degradation, cost Roll-Up, cost-aware scheduling — **Theme 5**. *(Seam laid: cost-ready Burn-Down + reusable Roll-Up.)*
- Trust/security hardening: single-use bound expiring links, step-up auth, NL-is-data enforcement, consent/residency routing, non-repudiation surfaces, cost-cap-as-DoS-guard — **Theme 6**. *(Seam laid: raw-act event model with actor + timestamp + verbatim payload.)*
- Any production channel adapter or end-user UI.

## 7. Why Now

Timing is load-bearing for Works and is documented in the brief/addendum: 2026 is framed as the year human+agent work surfaces go mainstream — Microsoft's Work Trend Index "agent boss"/"Frontier Firm" model (humans set direction, agents execute) and ~62% of organizations experimenting with or scaling AI agents as "digital coworkers." The enabling primitives (durable execution, model routing, HITL approvals, magic links) are all mature; the **whitespace is the synthesis** — one audited object where the backlog, the durable saga, and the effort+cost ledger are the same thing. Works claims **no moat on any single primitive; the advantage is coherence.** v1 builds that object's spine so the synthesis can be assembled on top while the operating model takes hold. *(Sources in `../../briefs/brief-works-2026-06-14/addendum.md`.)*

## 8. Public Surface & Compatibility *(developer-product cluster)*

Works' v1 public surface is its **domain contract**, consumed by Hexalith builders: the Domain Events, commands, the Executor Binding value object, the Reference Value Objects, the lifecycle, and the ports. Because the substrate is event-sourced, the surface's compatibility rules are strict and inherited from the ecosystem:

- **Additive, serialization-tolerant evolution only.** No `V2` event types; every event ever produced must remain backward-compatibly deserializable. The v1 event catalog (FR-7) and the AuthorityLevel set (FR-19) are designed to grow additively.
- **Compatibility is an exit contract, not only a principle.** Before any contract catalog ships, Works must publish and pass a versioned compatibility matrix and golden corpus covering N↔N+1 readers/writers, unknown additive fields, unknown enum/type values, field defaults, supported rollout order, and the downgrade stance. Unsupported combinations fail explicitly or quarantine safely; they never silently reinterpret or discard an accepted Raw Act (§10 gate G5). *(Added 2026-09-13.)*
- **Package boundaries** are `Contracts`, `Server`, `Projections`, optional domain-focused supporting libraries, a minimal EventStore domain-service executable, and `Testing`; topology and ServiceDefaults remain platform-owned. `Contracts` holds events/commands/models (low-dependency, no infrastructure); `Server` holds domain behavior — the `WorkItem` and Work-Tree Registry aggregates; `Projections` holds the Roll-Up and "what's next" read side; the Reactor (FR-26) is the v1 instance of a domain-focused supporting library. Contracts stay infrastructure-free. *(Amended 2026-09-05 — sprint-change-proposal-2026-09-05 §4.1; Reactor/registry placement added 2026-09-08.)*
- **Runtime targets** are inherited: .NET 10, C# nullable + warnings-as-errors, Dapr as the only permitted infrastructure abstraction in domain services, `System.Text.Json` conventions, `Hexalith.PolymorphicSerializations` for event payloads.

*(Detailed substrate constraints and a proposed event/port shape for architecture are in `addendum.md`.)*

## 9. Cross-Cutting NFRs

- **Tenant isolation (mandatory, every layer).** Every Work Item, aggregate identity, state key, projection key, query, and log is tenant-scoped per the substrate model (`{tenant}:{domain}:{aggregateId}`); managed tenant IDs live in payloads/read models, not in the EventStore envelope tenant. **Query-side authorization/result filtering is required in addition to command-side checks** — tenant scoping alone is not sufficient for read queries (FR-20). Negative-path tests cover both the cross-tenant and the query-side-authorization paths. *A governed control-plane exception for global recovery registries is under architecture review (VAL-H09); this NFR will record the exception and its audit condition when it closes.*
- **Identity provenance & trusted origin (platform-owned baseline).** *(Added 2026-09-08 — AD-23/AD-24.)* Every external caller is authenticated at the platform ingress; the authoritative Tenant and acting Party are **derived from verified claims** through `Hexalith.Tenants` membership — Works never authenticates and never treats a Tenant or actor field carried in a payload or envelope as authority. A payload/envelope identity that disagrees with the derived identity, or a caller with no tenant membership, is **denied before** aggregate dispatch, persistence, or query execution, without disclosing whether the tenant exists. Minimum authorization: commands require an authenticated member of the target Tenant; queries require membership plus result filtering (FR-20); rebuild/replay/repair require an individually audited platform-operator role. Internal originators — the Reactor, reminder callbacks and registration, replay, recovery — act under an authenticated **workload identity with an explicit, auditable tenant-delegation context**, never a borrowed user identity, and origin-restricted commands (`SpawnChild`, reminder callbacks, replay) are accepted only from their designated originator. In production, services reach each other only over mutually authenticated transport inside a declared trust domain behind deny-by-default access control, and an event's canonical position is trusted only after its origin is authenticated. Enforcement, its negative tests, and the operating policy are **platform-owned** (`Hexalith.Platform`); production ingress is prohibited until they are live. This baseline is distinct from Theme 6 hardening (§5, §12) and from AuthorityLevel, which stays carried-not-enforced (FR-19).
- **Identity provenance & trusted origin (platform-owned baseline).** *(Authorization amended 2026-09-13 — AD-23/AD-24.)* Every external caller is authenticated at the platform ingress; the authoritative Tenant and acting Party are **derived from verified claims** through `Hexalith.Tenants` membership — Works never authenticates and never treats a Tenant or actor field carried in a payload or envelope as authority. A payload/envelope identity that disagrees with the derived identity, or a caller with no tenant membership, is **denied before** aggregate dispatch, persistence, or query execution, without disclosing whether the tenant exists. Commands and queries then apply the relationship/origin matrix below. Rebuild/replay/repair require an individually audited platform-operator role. Internal originators act under an authenticated **workload identity with an explicit, auditable tenant-delegation context**, never a borrowed user identity. In production, services reach each other only over mutually authenticated transport inside a declared trust domain behind deny-by-default access control, and an event's canonical position is trusted only after its origin is authenticated. Enforcement, its negative tests, and the operating policy are **platform-owned** (`Hexalith.Platform`); production ingress is prohibited until they are live. This baseline is distinct from Theme 6 hardening (§5, §12) and from AuthorityLevel, which stays carried-not-read (FR-19).

**v1 action and query authorization matrix** *(normative; added 2026-09-13):*

| Act or view | Required relationship/origin after tenant verification |
| --- | --- |
| Root Create; Assign/reassign; Queue; ReEstimate; Reschedule; Cancel; LinkConversation; registry Reserve | Any authenticated member of the target Tenant; no AuthorityLevel branch in v1. |
| Claim a `Queued` item | An authenticated tenant member claiming as themself; the resulting binding PartyId is the trusted acting Party. |
| Claim an `Assigned` item; Suspend; ReportProgress; CorrectProgress; Complete; Reject; Handoff | The trusted acting Party must match the current bound Executor; Handoff is submitted by the outgoing Executor. |
| Expire; Resume; SpawnChild; cascade legs | The designated reminder/Reactor workload origin with explicit tenant delegation and causation. |
| Executor "what's next" view | The requested PartyId must match the authenticated Party; results also include the tenant's shared `Queued` pool. |
| Shared queue | Any authenticated tenant member, always tenant-filtered. |
| Coordinator/all-work view | Trusted internal origin only until role-based coordination enforcement exists. |
| Rebuild, replay, repair | Individually audited platform-operator role. |

Negative tests cover actor/binding mismatch, self-claim spoofing, forged internal origin, unauthorized coordinator view, membership denial, and cross-tenant result leakage.
- **Event-sourcing invariants.** Persist-then-publish; aggregate `Handle(...)` is pure and returns domain results/events; projection/state `Apply(...)` mutates only in-memory state; domain rejections are events (`IRejectionEvent`), infrastructure failures are exceptions/dead-letter paths. Works returns event payloads only — EventStore owns envelope metadata.
- **Concurrency.** Commands against a single Work Item are serialized by the aggregate's single-writer/optimistic-concurrency model; concurrent conflicting commands (e.g., two claims on one `Queued` item — FR-18) resolve to one success and domain rejections for the rest; if the substrate's bounded conflict retry is exhausted the outcome is an infrastructure failure surfaced to the caller, never a silent loss or a loser publication. No lost updates. *(Mechanism is an architecture concern — AD-08; the behavior is a v1 requirement. Amended 2026-09-08.)*
- **Recovery, replay, and rebuild are complete and observable.** The Roll-Up and "what's next" read models are derivable purely from event streams and hold no authoritative state of their own. Rebuild/recovery cannot omit accepted events at pagination or checkpoint boundaries; unresolved saga work remains durably discoverable and retried until resolved or explicitly disposed; stranded work degrades the affected capability's readiness and raises an operator-visible signal. A supported rebuild or restore does not lose writes accepted during the operation, and pending reminder/recovery discovery state is restored or deterministically reconciled. One logical internal effect executes at most once across the supported retry/replay/restore horizon. Numeric horizons and recovery objectives are bound at gate G8 before distributed acceptance.
- **Domain purity.** The domain assembly takes no direct infrastructure dependency and no LLM/cost/routing dependency; those sit behind ports/adapters (FR-22). The aggregate `Handle` reads no clock or external system — time/external triggers enter as commands (FR-15).
- **Observability.** Structured logging only; never log event payloads, personal data, secrets, or full command bodies. Errors use the ProblemDetails/RFC 9457 pattern with correlation/tenant context.
- **Durable-data lifecycle and disaster recovery (production-admission baseline).** Before non-synthetic shared data is admitted, durable fields—including verbatim Raw Acts—have an approved classification; retention/legal-hold and tenant offboarding/erasure disposition are defined; audit evidence and failure posture are testable; secret handling/rotation ownership is assigned; backup/restore and disaster-recovery objectives and restore order are approved; and a recovery drill supplies evidence. New irreversible durable catalog additions cannot bypass this gate. This platform baseline is not deferred Theme 6 feature scope. *(Added 2026-09-13.)*
- **Performance (qualitative for kernel acceptance).** The Roll-Up and "what's next" projections remain responsive for realistically deep/wide trees by updating incrementally (FR-11), without re-reading whole streams on each query. The depth-32 × fan-out-50 (≈1,600-item) fixture, rolled read under 200 ms, and convergence within 5 seconds are retained as **non-gating benchmarks**, not acceptance thresholds. Product + Architecture must define the environment, dataset, percentile, measurement window, failure policy, retry horizon, and binding recovery/performance budgets before distributed acceptance (§10 gate G8). *(Amended 2026-09-13.)*

## 10. Constraints, Guardrails & Exit Gates

- **Audit / non-repudiation (model laid in v1).** Domain Events record the Raw Act — acting Party + timestamp + verbatim payload — so that later interpretation is a recomputable Projection and disputes resolve against the verbatim act. The acting Party is the *authenticated* identity recorded by the substrate for every act, including non-executor acts, and system-originated acts carry the workload identity, its tenant-delegation context, and their causation link (FR-7). v1 lays this shape; the signed single-use link enforcement and auditor-facing query are Theme 6. *(Amended 2026-09-08.)*
- **Idempotency (event-sourced).** v1 idempotency rests on three layers. (1) **Semantic idempotency is explicit:** only named equivalent target states are no-ops—a Resume repeating the consumed Await-Condition (FR-15), an exact duplicate terminal act (FR-6), and an already-`Attached` identical child pair (FR-16). A fresh rejection is not evidence that an earlier attempt succeeded. (2) **Read-side idempotency is the Projection's job:** replaying an already-applied event never double-counts (FR-11). (3) **Transport idempotency replays the original outcome:** reusing the same message/idempotency key returns the original success, no-op, or rejection for at least the supported redelivery horizon instead of re-handling; Reactor and adapters share this internal submission contract. Theme 6's per-act signed single-use links build on all three. `[ASSUMPTION: the per-act signed token remains Theme 6; the transport key is v1.]` *(Amended 2026-09-13.)*
- **Cost-ready Burn-Down.** The Burn-Down and Roll-Up are built so a second (Cost) Meter reuses the identical machinery (Theme 5) — no schema reshape required to add it.
- **NL-is-data boundary (designed-for).** The Expectation/answer-space concept (the `IExpectationResolver` port) is the future prompt-injection boundary: when Theme 3 lands, the **answer-contract does triple duty — UX accelerator, input validator, and prompt-injection boundary** (free text is mapped onto the item's valid action space only, never executed as instructions). v1 ships only the no-LLM resolver, so no NL is interpreted as instructions in v1.

### 10.1 v1 dependency and exit gates

| Gate | Dependency and owner | Required evidence | Blocks |
| --- | --- | --- | --- |
| **G1** | Transport idempotency — EventStore SDK / Platform | Same key replays the original outcome across the supported redelivery horizon; causation keys are deterministic; arbitrary rejection is not translated to success. | Registry/Reactor distributed integration |
| **G2** | Durable reminders — EventStore SDK / Platform | Scheduler persistence/HA/backup, callback failure and recovery policy, current-schedule validation, stale-fire no-op, and positive production-policy registration test. | Distributed acceptance |
| **G3** | Reader-safe rebuild/recovery — EventStore SDK / Platform | Capture-through-commit safety, page/checkpoint boundary tests, concurrent-write preservation, pending-work rediscovery, degraded readiness, and restore reconciliation. | Distributed acceptance and production ingress |
| **G4** | Governed tenant control-plane exception — Platform Security | Namespace, authorizer, audit, operator-role, and cross-tenant negative-test evidence for any global recovery registry. | Production ingress |
| **G5** | Serialized-contract compatibility — Works + Polymorphic Serializations | Published N↔N+1 matrix and golden corpus covering additive fields, enum/type evolution, defaults, rollout order, quarantine/failure, and downgrade stance. | Contract catalog shipment |
| **G6** | Durable-data lifecycle and DR — Product + Platform | Classification, retention/legal hold, offboarding/erasure disposition, audit, secret rotation, backup/restore, approved recovery objectives/order, and recovery-drill evidence. | Non-synthetic shared production data |
| **G7** | Verified identity and trusted origin — Platform Security | Verified claims, membership denial, workload delegation, origin restrictions, mTLS/trust domain, broker ACLs, and forged-sequence negative tests. | Production ingress |
| **G8** | Binding performance/recovery budgets — Product + Architecture | Environment, dataset, percentile, measurement window, failure policy, retry/redelivery horizon, coordination deadlines, and recovery objectives. | Distributed acceptance |

## 11. Success Metrics *(build-signal acceptance)*

*v1 is foundation software; acceptance is defined by build signals from the brief rather than usage metrics. Each SM cross-references the FR(s) it validates.*

**Primary (build signals)**
- **SM-1 — Full event-sourced lifecycle, durable across restart.** The sequence create → progress → spawn child → suspend-on-event → resume → complete runs end-to-end under the platform-owned Aspire topology; and a Work Item suspended mid-saga rehydrates after restart and resumes correctly. The run proves: registry attachment waits for durable child creation; a released reservation rejects a delayed child leg without mutation; the authenticated bound Executor may hand off active work and correct progress; a stale expiry callback is an audited no-op; a claim race yields one success; and a cascade reaches every in-scope active descendant while leaving terminal ones untouched. Crash/restart cases cross page and checkpoint boundaries, and stranded coordination degrades readiness visibly. *Validates FR-1, FR-6–FR-8, FR-10, FR-14–FR-18, FR-24, FR-26, §9.* *(Amended 2026-09-13.)*
- **SM-2 — Correct Roll-Up (at quiescence).** For any constructed Work Tree, rolled Remaining equals the sum of each attached item's effective contribution (terminal = 0; active unestimated = 0 plus count; active estimated = Remaining), and updates as descendants progress, correct progress, complete, terminate, attach, release, or arrive out of order. Equality is asserted at **quiescence** once every relevant event has been acknowledged; tests await acknowledgement rather than sleeping. The 5-second figure for the indicative fixture is a non-gating benchmark until G8 binds a measured budget. *Validates FR-11–FR-13.* *(Amended 2026-09-13.)*
- **SM-3 — Zero branching on executor kind (falsifiable).** Two checks, both of which can fail: (a) adding a new `Channel` value or `AuthorityLevel` value requires **zero changes** in `Server`/`Projections` and **no new event type** — verified by an architecture-fitness test; (b) the identical assign → claim → progress → complete sequence, run over a system (MCP) binding, an internal-user binding, and an external (email) binding, yields event streams that **differ only in the binding's field values** — verified by a golden-corpus diff test. *Validates FR-17, FR-19.* *(Rewritten 2026-09-08; was "a test and code inspection confirm no domain branch on executor kind".)*
- **SM-4 — Pure domain assembly.** The domain assembly has zero duplicated technical/infrastructure layers; all cross-module concerns are behind Reference Value Objects, handlers, ports, and the EventStore domain-service SDK; green build + green tests under the platform-owned Aspire topology. *Validates FR-21–FR-25.*

**Secondary**
- **SM-5 — Channel change = one operation.** A Party changing Channel mid-work (email → chatbot, MCP → CLI) is one `Handoff` emitting one `WorkItemHandedOff`, with no channel-specific act and no change to Status, Burn-Down, Schedule, or Await-Conditions. While `Assigned`, the equivalent change remains one reassign/`WorkItemAssigned`. *Validates FR-17.* *(Amended 2026-09-13.)*
- **SM-6 — Rebuild from zero reproduces the read models.** Replaying every tenant stream into fresh Roll-Up and "what's next" Projections yields read models identical to the live ones at quiescence, and no event payload in the golden corpus carries a derived value. Evidence covers multi-page boundaries, checkpoint restart, item-before-edge delivery, concurrent writes during the supported rebuild, pending reminder/recovery rediscovery after restore, and atomic reader visibility; unresolved recovery produces degraded readiness. *Validates FR-7, FR-11, FR-20, §9 recovery — the "AI never in the system of record" bet.* *(Amended 2026-09-13.)*

**Counter-metrics (do not optimize)**
- **SM-C1 — Don't grow the kernel.** Lines/surface of the Works domain assembly should *not* be maximized; capability that belongs in a sibling module migrating *out* of Works is success, not regression. Counterbalances the temptation to satisfy SM-1 by absorbing technical layers. *Counterbalances SM-1/SM-4.* *(2026-09-08: the Work-Tree Registry aggregate and the Reactor are product-approved additions made for correctness — a second aggregate for edge authority, a mechanical translator for cross-aggregate steps — not kernel growth this counter-metric fires on; anything beyond their stated scope is.)*
- **SM-C2 — Don't over-fit v1 to deferred themes.** Adding speculative routing/cost/security machinery to "prepare" beyond the named seams is a negative; the seams in §10 are sufficient. *Counterbalances the roadmap pressure in §12.*

## 12. Roadmap / Designed-For (Theme 2 remainder, Themes 3–6)

*Documented, not specified as v1 work. Each theme names the v1 seam it builds on so the architecture phase preserves it.*

| Theme | Scope | v1 seam it builds on |
| --- | --- | --- |
| **2 (remainder) — Non-LLM command surfaces** *(added 2026-09-08)* | MCP as an actor channel (a system/AI Party issuing the uniform commands through MCP tools), CLI as scriptable work (builders and operators driving the same commands from a shell); no NL interpretation | Channel field on the Executor Binding (FR-17); the uniform command surface itself (FR-6, FR-17, FR-18); the "what's next" query (FR-20); the platform identity-provenance baseline (§9) |
| **3 — LLM-native interaction** | AI-inferred Expectation, constrained-safe magic links, NL-always-accepted + confidence-gated auto-apply, status-driven re-inference, email-as-UI; production no-login actions depend on Theme 6 binding/expiry/single-use/forwarding/step-up safeguards and must offer no-login used/expired-link recovery | `IExpectationResolver` port; Await-Condition; Channel field; raw-act events |
| **4 — Executor routing & escalation** | Auto-route + manual override, start-cheap-escalate ladder (small model → premium → human → external) as per-kind data policy (a Work Item *kind* discriminator is not in the v1 kernel and would be added additively when Theme 4 needs it), explainable decision record (candidates/score/cost/confidence), push↔pull auto-assignment | `IExecutorRouter` port; push/pull states (FR-18); AuthorityLevel (FR-19); assignment events |
| **5 — Economics & cost governance** | Cost as a second Burn-Down, spend caps → graceful degradation, cost Roll-Up, cost-aware (debounced) scheduling | cost-ready Burn-Down + reusable Roll-Up (FR-11) |
| **6 — Trust, security & auditability** | Single-use bound expiring idempotent links, forwarding≠authority + step-up auth, NL-is-data enforcement, consent/residency routing, non-repudiation surfaces, cost-cap-as-DoS-guard, **approver/observer participants** (Party-scoped grants outside the aggregate — FR-17) | raw-act event model (FR-7); idempotency (§10); AuthorityLevel (FR-19); the platform identity-provenance baseline (§9) |

**One ladder, run both ways.** Theme 4's start-cheap-escalate routing (cheapest capable → premium → human → external) and Theme 5's budget-degrade (full LLM → plain links → static templates → human) are the *same* escalation ladder traversed in opposite directions — one driven by capability/confidence, the other by spend. They should share one policy mechanism, not two.

**Designed-for tensions to revisit at theme time** (from the brainstorm): status-driven re-inference (Theme 3) vs. cost-aware debounce (Theme 5); authored vs. AI-inferred Expectation contract representation (Theme 3); and Theme 5's cost-aware scheduling reaching into the kernel-owned schedule/priority — a boundary call to make so the thin core does not absorb a cost engine (guard with SM-C1/SM-C2).

**Future human-surface quality floor (outside v1 acceptance).** Every production human-facing surface must meet WCAG 2.2 AA and provide equivalent keyboard and assistive-technology operation, non-color state meaning, zoom/reflow support, reduced-motion and forced-colors behavior, accessible validation/rejection recovery, and recoverable stale/offline/error states. Live queue and Meter changes preserve focus/context, avoid noisy announcements, and let users control non-essential continuous updating. Exact component semantics and test mechanisms belong in the UX specification.

**Roadmap open item — locale and bidirectional text.** PM owns the choice between an explicit English-only launch boundary and resource-backed localization/locale formatting/bidirectional-text handling. It must be resolved before Theme 3 external-email acceptance is written; current UX files do not decide it.

## 13. Architecture Bindings & Residual Gates

*The draft's eight Open Questions (unestimated completion, Expectation representation, AuthorityLevel set, reject/expiry semantics, tree-depth guard, Roll-Up consistency, heterogeneous-Unit Roll-Up, routing-decision placeholder) were **resolved on 2026-06-14** by user acceptance and appear as confirmed assumptions in §14 and as FR consequences. The six mechanism questions this section then deferred to the architecture phase have all been **bound by the Architecture Decision Register** (`../../architecture.md`, AD register added 2026-09-06); the table records where, and the residual openness the register itself keeps. Where a deferred item turned out to carry product semantics, the PRD now states them and the table points to the FR. (Converted 2026-09-08.)*

| # | Deferred question | Resolved by | Product-level answer | Residual openness |
|---|---|---|---|---|
| 1 | Aggregate identity derivation | **AD-02** | The `WorkItemId` is assigned at the command-creation edge with the `Hexalith.Commons` helper and carried on the create command; `Handle` never generates IDs. Edge assignment is not a transport idempotency contract (§10 layer 3). | Transport idempotency — **G1**, required before Registry/Reactor distributed integration. |
| 2 | Priority representation | **AD-03** | Priority is a small ordered enum (`Critical / High / Normal / Low`), additive-tolerant; FR-20 orders by Priority → Due Date → deterministic `WorkItemId` order (2026-09-06 correct-course). | None. |
| 3 | Optimistic-concurrency mechanism | **AD-08** | Same-item commands are serialized by the substrate's single-writer actor; the claim-race loser sees an ordinary domain rejection against freshly committed state; the store-level conflict path is a bounded substrate retry whose exhaustion is an *infrastructure* failure, never a silent loss (FR-18, §9). No Works command carries a version. | None. |
| 4 | Timer/scheduler adapter | **AD-11**, **AD-25** | Durable, self-targeted actor reminders fire `Resume` (date) and `Expire` (Due Date / TTL) commands; missed firings are reconciled by an indexed recovery pass; stale schedule firings are audited no-ops (FR-10). | End-to-end reminder durability — **G2**, required before distributed acceptance. |
| 5 | Projection rebuild operations | **AD-16** | Rebuild is reader-safe and atomically visible at commit: readers stay on the prior generation until promotion; relationship-aware projections (Roll-Up) rebuild over a sealed tenant inventory. | Capture-through-commit and recovery evidence — **G3**, required before distributed acceptance and production ingress. |
| 6 | Validation domains | **AD-17**, **AD-04** | Product semantics now in the PRD: progress delta strictly > 0 (FR-8); Estimated ≥ 0 and Remaining clamped ≥ 0 (FR-3); Unit immutable after first set (FR-3). Due-Date/TTL policy values are `Hexalith.Platform` host configuration consumed by the reminder adapter; the kernel never reads configuration (FR-10). | None in the PRD. *Downstream correction owed:* epics AR-6 states "delta ≥ 0" and must be aligned to "> 0" (handoff). |

*Two former validation findings are now explicit exit gates: any governed cross-tenant recovery-registry exception must satisfy **G4**, and immutable-event data lifecycle plus disaster-recovery readiness must satisfy **G6** before non-synthetic shared production data is admitted.*

## 14. Assumptions Index

*Every current `[ASSUMPTION]` plus material superseded assumptions retained for audit. The June entries were confirmed by the user on 2026-06-14; later entries record what validation or architecture replaced. Current tags stay in place so later phases can challenge any that prove unworkable.*

- §4.1 FR-1 / FR-8 — An unestimated Work Item completes only by an explicit complete act, not by the Remaining=0 rule. *(Confirmed 2026-06-14.)*
- §4.1 FR-2 — The Expectation is referenced/resolved on demand, not stored as an interpreted value on the aggregate. *(Confirmed 2026-06-14.)*
- §4.1 FR-2 — *(2026-09-08)* Obligation description bounded at 4,000 characters; longer content lives in a linked Conversation or referenced document.
- §4.2 FR-7 — *(2026-09-08)* The optional per-event note is bounded at 1,000 characters and is the act's own annotation, never a comment thread.
- §4.1 FR-3 / §4.3 FR-12 — No implicit cross-Unit arithmetic; per-Unit subtotals exposed for mixed subtrees; no Unit conversion in v1. *(Confirmed 2026-06-14.)*
- §4.1 FR-5 — A Suspended Work Item may hold multiple Await-Conditions and resumes on the first match. *(Confirmed 2026-06-14.)*
- §4.1 FR-5 / §4.3 FR-13 — Single-parent, acyclic, **single-tenant** Work Tree; default max depth 32 (configurable); breadth uncapped. **Superseded in part 2026-09-08 by AD-21:** enforcement moved from "at spawn time, by the Work Item" to "at edge reservation, by the Work-Tree Registry"; the Work Item re-asserts. The shape rules themselves stand.
- §4.2 FR-7 — The former fifteen-event v1 `WorkItem` catalog was always additive. **Superseded 2026-09-13:** `ProgressCorrected` and `WorkItemHandedOff` bring the current success catalog to seventeen without changing existing payloads; registry events remain additional contracts.
- §4.2 FR-10 — Reject defaults to requeue (`Queued`); Expire is Due-Date/TTL-driven and terminal; Cancel/Expire **cascade** to active descendants. *(Confirmed 2026-06-14; trigger and cascade owner named 2026-09-08 — AD-25, FR-26.)*
- §4.3 FR-11 — Roll-Up is an eventually-consistent, idempotent projection on the substrate's projection infrastructure. *(Confirmed 2026-06-14.)*
- §4.3 FR-11 — *(2026-09-08; clarified 2026-09-13)* An active unestimated Work Item contributes 0 Remaining and increments the unestimated-items count; every terminal item contributes zero to both.
- §4.4 FR-15 — Resume is a command keyed to an Await-Condition and is idempotent. **Narrowed 2026-09-08:** only a repeat of the *consumed* Await-Condition is a no-op; a non-matching resume is a domain rejection.
- §4.4 FR-26 — *(2026-09-08; boundedness gate added 2026-09-13)* A post-termination window on descendants — during which they still accept progress until the eventual cascade lands — is accepted in exchange for a pure, single-aggregate `Handle`; G8 must bind its numeric deadline and escalation policy before distributed acceptance.
- §4.5 FR-18 — Claim eligibility scoring is deferred to Theme 4; in v1 an authenticated tenant member may claim a `Queued` item only as themself, while an `Assigned` item may be claimed only by its bound Executor (single claim wins). *(Relationship enforcement amended 2026-09-13.)*
- §4.5 FR-19 — Proposed ordered AuthorityLevel set `{ Read, Contribute, Coordinate, Administer }`; carried-not-read and additive-tolerant. **Narrowed 2026-09-13:** v1 does enforce actor-to-binding and trusted-origin relationships without branching on AuthorityLevel.
- §4.6 FR-21 — v1 references a Conversation by correlation ID; it does not implement comment storage. *(Confirmed 2026-06-14; link semantics amended 2026-09-05.)*
- §9 NFRs — Numeric performance and recovery budgets remain deferred from kernel acceptance but are mandatory at G8 before distributed acceptance. *(Confirmed 2026-09-13.)*
- §9/§11 SM-2 — The depth-32 × fan-out-50 (≈1,600-item), under-200-ms rolled-read, and 5-second convergence figures are non-gating benchmarks until G8 defines their measurement contract. *(Amended 2026-09-13.)*
- §10 — v1 idempotency: duplicate-command idempotency is the aggregate's job, read-side idempotency the projection's, and a **transport idempotency key is v1 work** (VAL-H10); only the per-*act* signed token remains Theme 6. **Narrowed 2026-09-08** — was "no explicit per-act token in v1; resume-against-state + substrate offset dedup".
