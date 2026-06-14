---
title: "Hexalith.Works — Brief Addendum (downstream depth)"
status: draft
created: 2026-06-14
updated: 2026-06-14
---

# Addendum — Hexalith.Works

Depth that supports the brief but belongs in the PRD / Architecture rather than the brief itself.
Source of record for the domain ideas: `_bmad-output/brainstorming/brainstorming-session-2026-06-14-0910.md`
(44 ideas, 6 themes). This addendum carries the **foundation action plan**, the **deferred-theme backlog**,
and the **competitive landscape digest** that grounds *What Makes This Different*.

## Foundation action plan (v1 — Themes 1 & 2)

Direct input for the PRD and the thin-core architecture decision record.

### Priority 1 — Work Item Essence (domain aggregate)

1. Define `WorkItem` aggregate state: identity · obligation (description + inferred-expectation ref) ·
   executor binding · burn-down (estimated/remaining/done, unit-tagged) · schedule (priority + due date) ·
   status · parent/children refs · await-condition.
2. Model the lifecycle state machine: `Created → Assigned|Queued → InProgress → Suspended(awaiting event)
   → Resumed → Completed | Cancelled | Rejected | Expired`; codify "Done = remaining 0".
3. Define domain events (raw acts): `WorkItemCreated`, `ProgressReported`, `ReEstimated`, `ChildSpawned`,
   `Suspended`, `Resumed`, `Completed`, … via `Hexalith.PolymorphicSerializations`.
4. Implement the recursive roll-up projection (remaining-effort; reused later for cost).

- **Resources:** `EventStore`, `Commons`, `PolymorphicSerializations`.
- **Success:** full event-sourced create → progress → spawn → suspend/resume → complete with correct roll-up.

### Priority 2 — Thin-Core Architecture & Boundaries

1. Write the boundary decision record (owns vs references: `Parties`, `Conversations`, `EventStore`,
   `Tenants`, `Commons`).
2. Model the executor binding as one value object: `PartyId + Channel + AuthorityLevel`; encode
   "everything is a Party".
3. Define reference value objects (correlation ids) resolved on demand.
4. Define module ports as abstractions: `IExpectationResolver` (no-LLM impl first), `IExecutorRouter`;
   keep the domain pure, LLM/cost in adapters.
5. Stand up the Aspire host to run manual + automated tests.

- **Success:** clean domain assembly, zero technical layers, references behind interfaces, green build +
  tests under Aspire.

## Deferred-theme backlog (built atop the core)

| Theme | Scope | Status |
| --- | --- | --- |
| 3 — LLM-native interaction | AI-inferred expectation, constrained-safe magic links, NL-always-accepted, confidence-gated auto-apply, email-as-UI | Deferred; `IExpectationResolver` port laid in v1 |
| 4 — Executor routing & escalation | Auto-route + manual override, push+pull, start-cheap-escalate ladder as per-type data policy, explainable decision record | Deferred; `IExecutorRouter` port laid in v1 |
| 5 — Economics & cost governance | Cost as a second burn-down, spend caps → graceful degradation, cost roll-up, cost-aware (debounced) scheduling | Deferred; burn-down built cost-ready in v1 |
| 6 — Trust, security & auditability | Single-use bound expiring links, forwarding≠authority + step-up, NL-is-data-not-instructions, consent/residency routing, non-repudiation, cost-cap-as-DoS-guard | Deferred; signed-act event/audit model laid in v1 |

### Breakthrough concepts (carry into PRD positioning)

1. **Everything is a Party** — three actors collapse into (Party ref + channel + authority). *The keystone.*
2. **Answer-contract does triple duty** — UX accelerator → input validation → prompt-injection boundary.
3. **Two parallel burn-downs** — effort and cost, sharing one tree roll-up.
4. **Start-cheap-escalate ↔ budget-degrade** — the same ladder run up or down.
5. **AI in the loop, never in the system-of-record** — raw act is the event; interpretation is a projection.

## Competitive landscape digest

Research date 2026-06-14. Grounds the *What Makes This Different* section. Net finding: the primitives are
all mature; the **synthesis** is the whitespace.

- **Durable execution (Temporal, Restate, DBOS):** own durable suspend/resume; Temporal's
  workflow/activity model mirrors the saga mechanic. No first-class "work item," no burn-down/cost roll-up,
  no party model, no human surface. ([Temporal docs](https://docs.temporal.io/workflow-execution))
- **BPM (Camunda 8 / Zeebe):** "human task" / "external task" pattern = wait-for-human-then-resume, parent/
  child process trees. Models predefined diagrams, not an event log of ad-hoc work.
  ([Camunda human tasks](https://docs.camunda.io/docs/guides/orchestrate-human-tasks/))
- **Task management (Jira Rovo, Asana AI Teammates, Linear):** as of 2025-26 you can assign tickets to AI
  agents like humans — closest competitor to "everything is a party," now table-stakes. But internal
  assignees only; no external-by-email peer, no shared pull queue, no cost meter, no event-sourced substrate.
  ([Asana Teammates](https://asana.com/resources/ai-teammates-overview))
- **Agentic orchestration (LangGraph, CrewAI, OpenAI Agents SDK, AutoGen):** multi-agent handoff and
  start-cheap-escalate routing are established; "durable execution for agents" is now a named category
  (Temporal × OpenAI Agents SDK, Jul 2025). These orchestrate *a run*, not a durable multi-tenant backlog;
  AI sits *in* the system of record.
  ([Temporal+OpenAI](https://www.businesswire.com/news/home/20250730783559/en/Temporal-and-OpenAI-Launch-Integration-for-Enterprises-Developing-Production-Agents))
- **Human-in-the-loop + magic links (HumanLayer):** direct comparable for approvals/escalations across
  Slack/email; magic links are mature auth. The fresh fusion: AI *composing* constrained single-use signed
  *answer* links per work item, with the raw act as the canonical event.
  ([HumanLayer](https://humanlayer.systems/index-en))

### Why now (sources)

- ~62% of orgs experimenting with or scaling AI agents; 2026 framed as agents becoming "digital coworkers."
  ([cflowapps](https://www.cflowapps.com/ai-workflow-automation-trends/))
- Microsoft 2026 Work Trend Index — "agent boss" / "Frontier Firm": humans set direction, agents execute.
  ([Microsoft WTI 2026](https://www.microsoft.com/en-us/worklab/work-trend-index/agents-human-agency-and-the-opportunity-for-every-organization))
- Model routing / LLM FinOps (cost caps, cheap→premium cascades) went mainstream in 2025-26, validating the
  cost burn-down + escalation design. ([Zylos](https://zylos.ai/research/2026-02-19-ai-agent-cost-optimization-token-economics))

### Terms of art to reuse

Saga · human task / external task pattern (BPMN) · agent boss / Frontier Firm (Microsoft) · durable
execution for agents · model routing / cascade · LLM FinOps / token budget. Closest single positioning:
*HumanLayer + Asana AI Teammates/Jira agents on Temporal-style durable execution, on an event-sourced
multi-tenant substrate.*

## Open questions for the user (drafted as `[ASSUMPTION]` in the brief)

1. **Problem / why-now:** whose pain, and what does the status quo concretely cost?
2. **First consumer:** which Hexalith module or scenario is the first real customer of the kernel?
3. **Success criteria:** which product/business signals matter beyond the foundation build signals?
4. **v1 scope line:** confirm Themes 1 & 2 in, Themes 3–6 out — or adjust.
