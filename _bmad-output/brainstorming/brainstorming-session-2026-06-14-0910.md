---
stepsCompleted: [1, 2, 3, 4]
session_active: false
workflow_completed: true
technique_execution_complete: true
ideas_generated: 44
facilitation_notes: 'User is the architect; drove sharp first-principles and architecture decisions (thin core, everything-is-a-Party). Terse but decisive affirmations. Strongest energy on domain model, architecture boundaries, and LLM-native interaction.'
inputDocuments: []
session_topic: 'Works module — managing work items (tasks/work to be done) executable by the system, an internal user, or an external party (a person identified by email, declared in the Parties module). Part of the Hexalith ecosystem (event sourcing, multi-tenant).'
session_goals: 'Wide-open exploration ("tout azimut") — domain model & work item lifecycle, functional capabilities, external-party execution, and architecture/boundaries. Maximum divergence, no constraints, before any organization.'
selected_approach: 'ai-recommended'
techniques_used: ['First Principles Thinking', 'Role Playing', 'What If Scenarios', 'Reverse Brainstorming']
context_file: ''
---

# Brainstorming Session Results

**Facilitator:** Administrator
**Date:** 2026-06-14

## Session Overview

**Topic:** Works module — managing **work items** (tasks / work to be done). A work item can be executed by:

- the **system** (automated),
- an internal **user** (authenticated human),
- an **external party** (a person identified by email, declared in the **Parties** module).

Part of the **Hexalith** component ecosystem (event sourcing via `EventStore`, multi-tenant via `Tenants`, identities via `Parties`).

**Goals:** Wide-open exploration ("tout azimut") — maximum divergence across domain model & lifecycle, functional capabilities, external-party execution, and architecture/boundaries, before any organization or convergence.

### Session Setup

_Communication in English (per user request, 2026-06-14); user may reply in French. Document persisted in English. Canonical term adopted: **work item**._

## Technique Selection

**Approach:** AI-Recommended Techniques

**Analysis Context:** Software module `Works` (abstract, event-sourced, multi-tenant); defining trait = three executor types (system / internal user / external party by email). Goal: maximum divergence across domain, capabilities, external execution, and architecture. Target 100+ ideas with anti-bias domain pivots.

**Recommended sequence:**

1. **First Principles Thinking** (deep) — define what a work item irreducibly IS, before listing features; ground the event-sourced domain model.
2. **Role Playing** (collaborative) — embody each actor (system, internal user, external party, requester, tenant admin, auditor) to surface divergent needs and the external-by-email flow.
3. **What If Scenarios** (creative) — break constraints to push past CRUD into novel capabilities.
4. **Reverse Brainstorming** (creative/wild) — "how could Works fail or be abused?" to surface edge cases, escalations, accountability, and security.

**AI Rationale:** Mix of structured/deep and creative techniques matched to a technical user and an abstract domain; sequence moves from foundations → actor-driven generation → ambitious expansion → orthogonal failure lens, with flexibility to add Morphological Analysis for systematic coverage.

## Idea Log

### Phase 1 — First Principles Thinking

Goal: strip away existing tools and define what a work item irreducibly **is**, before listing features.

**[Domain Atom #1]: Burn-Down Truth** — A work item intrinsically carries **estimated work**, **work done**, and **remaining work**; progress is a quantity that burns down, not a status flag. "Done" = remaining 0. _Novelty:_ every progress report is a first-class fact/event; partial progress, re-estimation, over-run are native.

**[Domain Atom #2]: It Competes For Attention** — **Priority + due date** mean a work item has standing relative to others; it lives in a contended queue. _Novelty:_ scheduling/ranking ("what next?") is intrinsic to `Works`, not external.

**[Domain Atom #3]: It Remembers** — **Comments/notes** are an intrinsic append-only narrative of *why* the numbers moved. _Novelty:_ comment stream and event stream are two views of the same history.

**[Domain Atom #4]: One Work, Many Executors** — A system task (e.g. with LLM steps) can be long-running and estimable just like human effort; the executor (system / user / external party) is metadata on a single contract, not a subtype. _Novelty:_ justifies one `Works` module instead of three — burn-down/scheduling/audit are shared, only the executor binding differs.

**[Domain Atom #5]: Unit-Agnostic Progress** — Estimated/remaining/done measured in a domain-chosen unit: hours (human), stages/steps/tokens (LLM system task), interactions (external party); same engine, pluggable meter. _Novelty:_ one progress bar over heterogeneous work; long system tasks stream real progress.

**[Domain Atom #6]: Long-Running ⟹ Resumable** — Because system tasks can be long, a crash mid-flight just means remaining > 0; pause/resume/checkpoint is the same partial-progress lifecycle a human uses when stepping away. _Novelty:_ failure handling collapses into normal progress semantics; "retry" = "continue the burn-down."

**[Domain Atom #7]: Work Spawns Work → Suspension** — A work item can create child work item(s) and enter a **Suspended/Waiting** state blocked on them; the child's completion event reactivates the parent, which resumes. _Novelty:_ a saga/continuation in event-sourcing terms — durable, survives restarts.

**[Domain Atom #8]: Remaining Work Rolls Up** — A parent's remaining work = its own + recursive remaining of descendants; progress aggregates up the tree. _Novelty:_ a top-level progress bar is the rolled-up burn-down of an entire subtree, identical math across executor types.

**[Domain Atom #9]: "Done" Is a Signal Anyone Can Await** — Suspension-on-an-event is the primitive; a work item can wait on something it didn't spawn (webhook, date arriving, external reply). _Novelty:_ dependencies, timers, and external triggers all unify into "park until event X."

### Phase 2 — Role Playing

Embodying each actor (system, internal user, external party, requester, tenant admin, auditor) to surface divergent needs.

**[External #1]: AI-Suggested Quick Answers as Magic Links** — An LLM reads the work item + what's expected and embeds the most likely answers as **signed magic links** in the email; one click = work progressed, zero typing. _Novelty:_ the email body becomes the UI; the LLM does the comprehension labor so the human just confirms.

**[External #2]: Constrained-Safe Generation** — The LLM may only emit answers valid against the work item's expected-answer contract; links are pre-validated so a click can never submit an illegal value. _Novelty:_ AI creativity bounded by domain contract — safe by construction.

**[External #3]: One-Tap, Then Escape Hatch** — Always include a *"None of these — answer in my words"* link that degrades gracefully to a minimal account-less web view or a parsed free-text reply. _Novelty:_ progressive disclosure from one-tap to full interaction, never a login wall.

**[System #1 / External #4]: Schema-Less, AI-Inferred Interaction** — No authored answer contract; the LLM derives "what's expected now" from description + status + other fields, per interaction. _Novelty:_ interaction model is computed not configured; work-creation friction drops to ~zero.

**[System #2]: Status-Driven Re-Inference** — Every state-transition event re-derives the expectation and regenerates quick-actions / notifications. _Novelty:_ actionable prompts stay auto-synced with state; re-inference is an event projection.

**[System #3]: One Resolver, All Executors** — The same inference ("given this state, what's expected and from whom?") serves system (execute), user (do), external party (confirm); executor type only changes the delivery channel. _Novelty:_ a single "expectation resolver" is the brain of `Works`; the rest is transport.

**[External #5]: Natural Language Is Always a First-Class Answer** — Magic links are a fast-path; the party/user can always reply in free-form NL, which the LLM maps onto the inferred expectation. _Novelty:_ structure is optional everywhere; flexibility is default, speed is opt-in.

**[Auditor #1]: Raw Act Is the Event, Interpretation Is the Projection** — The literal act (link id or verbatim message + timestamp + signed identity) is the immutable event; the LLM's mapping is a recomputable projection. _Novelty:_ AI in the loop but not in the system-of-record; disputes resolve against the verbatim act.

**[Interaction #1]: Confidence-Gated Auto-Apply** — High-confidence NL parses auto-apply; low-confidence ones reply "I understood X — confirm?" with a one-tap link. _Novelty:_ autonomy scaled by confidence; merges NL freedom with audit safety.

**[Architecture #1]: Conversation Delegated to Hexalith.Conversations** — `Works` owns obligation + burn-down; `Hexalith.Conversations` owns the dialogue; one correlation id links them. _Novelty:_ sharp separation of concerns; `Works` never reinvents messaging.

**[Channel #1]: Omnichannel Advancement** — Same work item advances via chatbot, Works MCP, Works CLI, or email, all converging on one thread. _Novelty:_ executor and channel are orthogonal; a party can switch channels mid-work.

**[Channel #2]: MCP as a First-Class Actor Channel** — `Works` over MCP lets AI agents/tools list, claim, advance, complete work items; work becomes agent-addressable. _Novelty:_ any MCP-capable agent can be the "system executor"; autonomous agents pull from the queue.

**[Channel #3]: CLI as Scriptable Work** — A Works CLI drives work items in scripts, pipelines, runbooks. _Novelty:_ work management becomes scriptable infrastructure, not just a UI.

**[Architecture #2]: Works Is a Coordination Kernel** — The aggregate owns only obligation + burn-down + schedule/priority + executor binding + suspend/resume saga; it's a scheduler/coordinator, not a data store. _Novelty:_ tiny aggregate, huge leverage; typical task-system bloat lives in other modules. **(Confirmed by user: "yes it's a thin core".)**

**[Architecture #3]: Everything Else Is a Late-Resolved Reference** — Identities (Parties), dialogue (Conversations), events (EventStore), isolation (Tenants) are correlation ids resolved on demand, never copied. _Novelty:_ no duplication/staleness; a work item is a thin index into the ecosystem.

**[Architecture #4]: Executor Binding Is the One Pluggable Seam** — The system/user/external distinction is a single polymorphic slot; new executor kinds (MCP agent, Slack approver, IoT device) plug in without touching the core. _Novelty:_ all extensibility concentrated in one place.

**[Architecture #5]: "External Party" Isn't Special — It's a Party + Channel** — No "external party" type; it's a Parties reference whose channel is email and auth level is "no app login." Actors collapse into (Party ref + channel + authority level). _Novelty:_ the hardest actor becomes a coordinate in a small space.

**[Architecture #6]: One Executor Concept — Everything Is a Party** — System, user, external all collapse into (Party ref + channel + authority); a system task is a service/robot Party with an MCP channel and machine authority. _Novelty:_ radical uniformity — assignment/reassignment/delegation/escalation run identically for humans, bots, externals; zero branching on executor type. **(Confirmed by user.)** Aligns with repo responsibility: Works = domain code only; identity→Parties, persistence→EventStore, ids→Commons.

**[Capability #1]: Human ⇄ AI Handoff Is Symmetric** — Reassign from human to bot or escalate bot's work to a human with the same operation. _Novelty:_ human↔AI handoff is first-class and symmetric, not a bespoke integration.

**[Capability #2]: Authority Rides on the Binding** — Per-binding authority gates actions (external party confirms but can't reprioritize; machine auto-completes but can't approve spend). _Novelty:_ work-scoped permissions unify with Parties identity; security expressed in the domain.

**[Economics #1]: Cost Is a Second Burn-Down** — Alongside effort, a work item tracks cost + token usage as a first-class accumulating quantity; every inference debits it. _Novelty:_ two parallel meters (work vs. coordination cost) optimizable independently. **(Confirmed by user.)**

**[Economics #2]: AI-Spend Policies → Graceful Degradation** — Tenant Admin sets caps per item/type/tenant/period; on breach Works degrades to no-LLM plain links / static templates / human handling. _Novelty:_ built-in "cheap mode"; a budget-exceeded event drives the degrade.

**[Economics #3]: Cost Rolls Up the Tree** — Token cost aggregates up the subtree like remaining effort (Atom #8). _Novelty:_ all-in cost of a top-level objective is visible; same roll-up machinery as effort.

**[Economics #4]: Cost-Aware Scheduling (Debounce the Brain)** — Scheduler coalesces/debounces re-inferences instead of one per micro-status-change, batches expensive work, prefers cheap channels. _Novelty:_ scheduling optimizes cost/value, not just deadlines; tames naive re-inference (System #2).

### Phase 3 — What If Scenarios (Executor Routing & Escalation)

**[Routing #1]: Auto-Route, Manual Override** — Auto-assign to the optimal executor (cost/quality/speed); human can reassign anytime; the auto-route choice is a logged event. _Novelty:_ assisted assignment — autonomous by default, overridable. **(Confirmed by user.)**

**[Routing #2]: Push and Pull Coexist** — Items can be assigned (push) to a specific executor or placed in a shared queue for capable executors to claim/bid (pull), and move between modes. _Novelty:_ one model spans "assigned task" and "open work pool"; AI fleets and human teams pull from the same queue. **(Confirmed by user.)**

**[Routing #3]: Start Cheap, Escalate on Failure/Low-Confidence** — Begin with cheapest capable executor (small model); escalate → premium model → human → external party on failure/timeout/low confidence. _Novelty:_ escalation ladder as a first-class path; the inverse of Economics #2 degradation. **(Confirmed by user.)**

**[Routing #4]: The Binding Is an Explainable Decision Record** — Each assignment records candidates, policy score, cost estimate, confidence. _Novelty:_ routing is auditable (pairs with Auditor #1); decision is the event, rationale a projection.

**[Routing #5]: Escalation Ladder Is Per-Type Policy (Data, Not Code)** — The cheap→premium→human→external ladder is a policy on the work-item type/template, tunable by Tenant Admin. _Novelty:_ behavior varies by work type without code changes.

### Phase 4 — Reverse Brainstorming (Failure → Defenses)

Adversary lens: "how do we make Works fail, leak, or get abused?" — each attack becomes a requirement. User: defend against **all**.

**[Defense #1]: Single-Use, Bound, Expiring Links** — Each link is single-use, bound to (work item + party + action + expiry), idempotent (2nd click = no-op); old links expire. _Novelty:_ replay + double-act die on the same idempotency key, native to event sourcing.

**[Defense #2]: Forwarding ≠ Authority** — A link is a proposal to act, not authority; act attributed to whoever authenticates; sensitive actions need step-up (OTP); real delegation is a logged reassign. _Novelty:_ splits possession-of-link from authority; forwarded links can't impersonate.

**[Defense #3]: NL Is Data, Never Instructions** — Reply parser maps text onto the item's valid action space only; cross-item commands un-emittable; high-impact = confirm. _Novelty:_ the answer-contract constraint becomes the prompt-injection security boundary.

**[Defense #4]: Consent- & Residency-Aware Routing** — Router filters candidates by data-classification + residency + consent before cost; degrade rather than violate. _Novelty:_ optimization gated by a compliance predicate first, cost second.

**[Defense #5]: Everything Attributable & Non-Repudiable** — Raw act = signed event with identity + timestamp; repudiation answered by the signed event. _Novelty:_ the audit model already built is the anti-repudiation defense.

**[Defense #6]: Cost Caps Double as DoS Guard** — Per-party/per-item rate limits + cost caps bound re-inference floods; anomalies trip a circuit breaker. _Novelty:_ the budget is the rate limiter — one mechanism, two jobs.

## Idea Organization and Prioritization

**Total:** 44 ideas across 4 techniques (First Principles → Role Playing → What If → Reverse Brainstorming).

### Thematic Organization

- **Theme 1 — The Work Item Essence (domain core):** Atoms #1–9. A work item is a measurable, scheduled, narrated, executor-agnostic, spawnable, suspendable unit of effort.
- **Theme 2 — Thin-Core Architecture & Boundaries:** Architecture #1–6 + Channels #1–3. `Works` owns only obligation + burn-down + schedule + executor binding + saga; everything else is a reference.
- **Theme 3 — LLM-Native Interaction (email is the UI):** External #1–5, System #1–3, Interaction #1. AI infers the expectation and proposes one-tap answers; NL always accepted.
- **Theme 4 — Executor Routing & Escalation:** Routing #1–5, Capability #1–2. Binding as an optimizing routing decision: auto-route, push+pull, start-cheap-escalate.
- **Theme 5 — Economics & Cost Governance:** Economics #1–4. A second burn-down (cost/tokens) with caps, degradation, roll-up, cost-aware scheduling.
- **Theme 6 — Trust, Security & Auditability:** Auditor #1, Defense #1–6. Raw act = signed event; defenses fall out of the design.

### Breakthrough Concepts

1. **Everything Is a Party** — three actors collapse into (Party ref + channel + authority); one uniform executor model. *The keystone.*
2. **The answer-contract does triple duty** — UX accelerator → input validation → prompt-injection security boundary.
3. **Two parallel burn-downs** — effort and cost, sharing the same tree roll-up.
4. **Start-cheap-escalate ↔ budget-degrade** — the same ladder run up or down.
5. **AI in the loop, never in the system-of-record** — raw act is the event; interpretation is a recomputable projection.

### Prioritization Results (user-selected)

**Top priority (foundation-first): Theme 1 + Theme 2.** Build the irreducible domain core and the thin-core boundaries first; interaction, routing, economics, and security bolt onto that spine.

### Action Planning

**Priority 1 — Theme 1: Work Item Essence (domain aggregate)**
1. Define the `WorkItem` aggregate state: identity · obligation (description + inferred-expectation ref) · executor binding · burn-down (estimated/remaining/done, unit-tagged) · schedule (priority + due date) · status · parent/children refs · await-condition.
2. Model the lifecycle state machine: `Created → Assigned|Queued → InProgress → Suspended(awaiting event) → Resumed → Completed | Cancelled | Rejected | Expired`; codify "Done = remaining 0".
3. Define domain events (raw acts): `WorkItemCreated`, `ProgressReported`, `ReEstimated`, `ChildSpawned`, `Suspended`, `Resumed`, `Completed`… via `Hexalith.PolymorphicSerializations`.
4. Implement the recursive roll-up projection (remaining-effort, reused later for cost).
- Resources: `EventStore`, `Commons`, `PolymorphicSerializations`. Success: full event-sourced create→progress→spawn→suspend/resume→complete with correct roll-up.

**Priority 2 — Theme 2: Thin-Core Architecture & Boundaries**
1. Write the boundary decision record (owns vs references: Parties, Conversations, EventStore, Tenants, Commons).
2. Model the executor binding as one value object: `PartyId + Channel + AuthorityLevel`; encode "everything is a Party."
3. Define reference value objects (correlation ids) resolved on demand.
4. Define module ports as abstractions: `IExpectationResolver` (no-LLM impl first), `IExecutorRouter`; keep the domain pure, LLM/cost in adapters.
5. Stand up the Aspire host to run manual + automated tests.
- Success: clean domain assembly, zero technical layers, references behind interfaces, green build + tests under Aspire.

## Session Summary and Insights

**Key Achievements:**

- 44 collaboratively-developed ideas for the `Works` module across 4 techniques.
- A unifying domain model: a work item is a measurable, scheduled, narrated, executor-agnostic, spawnable, suspendable unit of effort.
- The keystone architectural decision — **"Everything Is a Party"** — collapsing system / user / external-party into one executor model (Party ref + channel + authority).
- A thin-core boundary definition aligned with the repo's stated responsibility (domain-only; technical concerns factored to shared Hexalith modules).
- Two prioritized foundations with concrete, event-sourced action plans.

**Creative Breakthroughs:**

- The answer-contract serving triple duty (UX accelerator → validation → prompt-injection security boundary).
- Two parallel burn-downs (effort + cost) sharing one tree roll-up.
- Start-cheap-escalate ↔ budget-degrade as the same ladder run up or down.
- AI in the loop but never in the system-of-record (raw act = event; interpretation = recomputable projection).

**Session Reflections:**

- Divergence held well across orthogonal domains (domain → architecture → interaction → economics → security) via deliberate anti-bias pivots.
- The user acted as architect, driving decisive first-principles calls; the facilitator's role was to push each principle to its breaking point and harvest the consequences.
- Reverse Brainstorming converted a self-inflicted attack surface (magic links + AI-in-loop + auto-routing) into a coherent set of defenses that mostly reused existing mechanisms.

### Suggested Follow-ups

1. Implement Priority 1 & 2 action steps (domain aggregate + boundary decision record).
2. Promote into BMAD artifacts: Product Brief / PRD → Architecture (lock thin-core boundaries).
3. Treat Themes 3–6 (interaction, routing, economics, security) as the feature backlog atop the core.

---

_Session completed 2026-06-14. Facilitated brainstorming via BMAD (AI-recommended techniques)._
