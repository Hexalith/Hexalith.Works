---
title: "Hexalith.Works — Product Brief"
status: final
created: 2026-06-14
updated: 2026-06-14
---

# Product Brief: Hexalith.Works

## Executive Summary

Hexalith.Works is a thin **work-item coordination kernel** for the Hexalith ecosystem: an event-sourced,
multi-tenant domain module that tracks *work to be done* and coordinates *who does it* — the system (an AI
agent or service), an internal user, or an external person reached by email. It is not a task database and
not a workflow-diagram engine. It is the small, durable spine that owns an item's obligation, burn-down,
schedule, executor, and suspend/resume lifecycle, and references everything else — identities, dialogue,
persistence, isolation — rather than copying it.

The defining bet is **"everything is a Party."** System, user, and external-by-email collapse into one
executor binding — `PartyId + Channel + AuthorityLevel` — so assignment, reassignment, human⇄AI handoff,
and escalation run identically for a bot, a colleague, or a customer answering from their inbox. A work
item burns down along two meters at once — **effort** and **cost/tokens** — and both roll up a
parent→child tree, so the all-in effort and spend of a top-level objective is a single number. AI sits
*in the loop but never in the system-of-record*: the canonical event is the raw signed act — a one-tap
link click or a verbatim reply — and the AI's interpretation is a recomputable projection.

Why now: 2026 is the year human+agent work surfaces go mainstream. Microsoft's Work Trend Index names the
"agent boss" and the "Frontier Firm" — humans set direction, agents do tactical execution — and a majority
of organizations are already scaling AI agents as "digital coworkers." The primitives to build this
(durable execution, model routing, human-in-the-loop approvals) are mature. What is missing is a single
coordination object where a shared backlog of humans and AI agents, a durable saga, and an effort+cost
ledger are the *same thing*. That is Works.

## The Problem

Two problems share one root. **End users** who need to capture a task, a to-do, or a unit of work must stop
and log into a dedicated task app — even though the work just surfaced in their inbox, a chat, an AI
assistant, or a terminal. And **builders** coordinating that work across people and AI agents must juggle
three kinds of doer — automated system tasks (increasingly LLM agents), internal users, and external people
who will never log into the app — each served today by a different tool with a different model:

- **Task/work managers** (Jira, Asana, Linear) assume a human behind a login and a UI. They are adding AI
  assignees — but only *internal* ones, with no external-person-by-email as a peer doer, no shared pull
  queue, and no notion of cost.
- **Durable-execution / workflow engines** (Temporal, Camunda) handle long-running, suspend-and-resume work
  beautifully — but as developer infrastructure for *code* or *BPMN diagrams*, not as a first-class, audited
  *work item* a business and an auditor can reason about.
- **Human-in-the-loop tooling** (HumanLayer and peers) bolts approvals onto an agent run — but the human is
  an interruption to a run, not a peer executor in a durable backlog.

For end users, the friction means work never gets captured — it scatters into inboxes, chat threads, and
shadow lists, or is lost. For builders, the result is three integrations, three audit trails, three
scheduling models, and bespoke glue every time work moves between a bot, a person, and a customer. Effort
burn-down lives in one place, AI token spend in another, and "who actually did this, and when" is
reconstructed by hand. The cost of the status quo is work that slips through the cracks, duplicated
technical layers, and no single trustworthy answer to *what is the remaining work and cost of this
objective, and who is on the hook for the next step.*

## The Solution

Works makes those three doers one model. A **work item** is the irreducible unit:

- **Burns down** — estimated / done / remaining work in a pluggable unit (hours for a human, steps/tokens
  for an LLM task, interactions for an external party); "done" means remaining = 0. Progress is a fact, not
  a status flag.
- **Competes for attention** — priority + due date give it standing in a contended queue; "what's next?" is
  intrinsic to Works, not external.
- **Remembers** — its comment narrative and its event stream are two views of one append-only history.
- **Spawns and suspends** — it can create child work and park itself awaiting an event (a child completing,
  a webhook, a date, an external reply), then resume: a durable saga in event-sourcing terms.
- **Rolls up** — a parent's remaining effort *and* cost are its own plus its descendants'.

Everything else is a **late-resolved reference**: identities → `Parties`, dialogue → `Conversations`,
persistence/events → `EventStore`, isolation → `Tenants`, ids → `Commons`. The one pluggable seam is the
**executor binding** (`PartyId + Channel + AuthorityLevel`) — the single place new doer kinds (MCP agent,
Slack approver, IoT device) plug in. Work flows by **push** (assign to a specific executor) or **pull**
(claim from a shared queue) and moves between modes, so AI fleets and human teams draw from the same
backlog. Routing starts cheap and escalates (small model → premium → human → external) on failure or low
confidence, governed by per-type policy and cost caps.

A work item can be **created and advanced from any channel** — a one-line email, the Hexalith Chatbot, an
LLM tool over MCP, or the CLI — all converging on one item; channel and executor are orthogonal. Interaction
is LLM-native but trust-preserving: for an external party, the email *is* the UI. The AI reads the item and
embeds constrained-safe, single-use, expiring signed links (one tap = work progressed), with a "none of
these — answer in my words" escape hatch that maps free text onto the item's valid action space. The signed
act is the event; the interpretation is a projection. The answer-contract does triple duty: UX accelerator,
input validator, and prompt-injection boundary.

## What Makes This Different

The honest truth: every *primitive* here is mature — durable suspend/resume (Temporal/Restate/DBOS),
wait-for-human-then-resume (Camunda human tasks), start-cheap-escalate model routing (RouteLLM/cascades),
HITL approvals (HumanLayer), signed single-use magic links. **The whitespace is the synthesis**, not any
one part.

- **Dual burn-down on one tree** — effort *and* cost/tokens as first-class quantities rolling up the same
  parent→child tree. FinOps token budgeting and effort burn-down exist separately everywhere; unifying them
  on a work tree appears genuinely novel.
- **AI in the loop, never in the system-of-record** — the signed human/AI act is the canonical event; AI
  interpretation is a recomputable projection. Durable-execution engines and HITL tools keep state and
  decisions inline; this clean separation makes disputes resolvable against the verbatim act.
- **One uniform Party executor** spanning system, internal user, *and external-by-email*, with push and pull
  coexisting. Assigning to an AI or a human is now table-stakes (Jira Rovo, Asana AI Teammates); the fresh
  part is the *external person reached by magic link as a peer executor in a shared pull queue.*
- **A thin event-sourced domain kernel, not a product** — Works is roughly the union of HumanLayer (HITL),
  Asana/Jira AI-teammate assignment, and Temporal-style durable execution, distilled to a small domain
  aggregate on a multi-tenant event-sourced substrate, with the bloat factored into surrounding Hexalith
  modules.

We deliberately do *not* claim a moat on durable execution or model routing. The advantage is coherence:
one audited object where the backlog, the saga, and the effort+cost ledger are the same thing.

Two primary audiences consume the kernel directly:

- **Hexalith builders** — application and module developers who coordinate work across humans, AI agents,
  and external people without stitching three systems together.
- **End users** — people who create and advance tasks, to-dos, and work items *from wherever they already
  are*, never through a separate task-app login.

Served through them:

- **The three executors** — *system / AI agents* (claim and advance work over MCP), *internal users* (do
  and oversee work via chatbot/CLI), *external parties* (confirm or answer from their inbox, no login).
- **Tenant admins** — set escalation ladders, authority levels, and AI-spend caps per work type and tenant.
- **Auditors** — a non-repudiable, signed event record of who did what, when, against which item.

## Success Criteria

**Foundation (v1) — build signals:**

- A full event-sourced lifecycle runs end-to-end: create → progress → spawn child → suspend-on-event →
  resume → complete, with **correct effort roll-up** across the tree.
- The domain assembly is **pure**: zero technical/infrastructure layers, all cross-module concerns behind
  references and ports (`IExpectationResolver`, `IExecutorRouter`), with a green build and tests under the
  Aspire host.
- "Everything is a Party" holds: assign / reassign / handoff work identically across system, user, and
  external bindings with **zero branching on executor type.**

**Product signals — Works is working when:**

- **Capture in seconds, no app** — a user creates a work item from a one-line email or a single chatbot
  sentence in seconds, never opening a task app.
- **External one-tap advance** — an external person (reached by email) advances or completes work in a
  single tap from their inbox, with no login.
- **One number: work + cost** — any objective's remaining effort *and* spend is a single queryable,
  rolled-up number across its whole tree.
- **Handoff = one operation** — reassigning between a human and an AI agent, in either direction, is a
  single symmetric operation, not a bespoke integration.

## Scope

**In for v1 (foundation-first — Themes 1 & 2):**

- The `WorkItem` aggregate: identity · obligation · executor binding · burn-down (effort first, cost-ready) ·
  schedule · status · parent/child refs · await-condition.
- The lifecycle state machine and raw-act domain events (via `PolymorphicSerializations`).
- The recursive roll-up projection (effort; reused later for cost).
- The thin-core boundary decision record, reference value objects, and module ports as abstractions
  (no-LLM `IExpectationResolver` first).
- The Aspire host to run manual + automated tests.

**Explicitly out of v1 (designed-for, built later):**

- LLM-native interaction / magic links / NL parsing (Theme 3).
- Executor routing & escalation ladder (Theme 4).
- Cost burn-down & spend governance (Theme 5) — the burn-down is built to carry it.
- Security hardening: step-up auth, consent/residency routing, DoS guards (Theme 6) — the event/audit model
  is laid in v1.

## Vision

Two horizons, one substrate.

**For the person:** your work, captured from anywhere and done by whoever is best for it — you, a colleague,
an AI agent, or someone you only reach by email. You never open a task app; you say what needs doing and
watch it burn down.

**For the ecosystem:** Works becomes the **agent-addressable work queue of Hexalith** — the durable
substrate where human teams and AI fleets pull from one backlog, every objective's remaining effort and
spend is a single rolled-up number, and every act (human tap, agent step, external reply) is a signed,
non-repudiable event. As the "agent boss" operating model takes hold, Works is the coordination layer that
lets a Frontier Firm hand any unit of work to the cheapest capable doer, escalate seamlessly to a human, and
keep a clean audit of the whole burn-down — without ever putting the AI in the system of record.
