# Reconciliation — amended PRD

Updated: 2026-09-12

This is a compact product-source extraction for the Works UX spines. It is not a second product contract.

Source aliases:

- P: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md
- PA: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md
- O: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/reconcile-prd-memlog.md
- WD / WE: current Works DESIGN.md / EXPERIENCE.md in this directory

## Authority and drift

For conflicts, the five accepted PRD-memlog decisions isolated in O govern; the rendered PRD and addendum govern all unchanged product behavior. The brief supplies qualitative intent only. O records the exact entry anchors, UX implications, rendered-PRD drift, and still-open details, so volatile behavior is not duplicated here. (O:12-42; P:21-25)

The rendered PRD's no-active-Handoff, no-Reopen, membership-only authorization, and ChildSpawned-first attachment text is superseded. Use O for durable attachment and delayed-leg rejection, executor-bound actor/origin rules, active Handoff, and CorrectProgress/conditional reopen. (O:20-34)

## Scope and horizons

| Horizon | Governing scope | UX disposition |
|---|---|---|
| **v1 — Theme 1 plus the kernel subset of Theme 2** | Headless Work Item kernel, lifecycle, Effort Burn-Down, Registry/Reactor, Executor Binding, projections/queries, ports and platform-hosted test harness. No production end-user UI or channel adapter. (P:21-25, P:409-428) | Treat behavior as the v1 contract, but do not depict a web, MCP, CLI, chatbot, or email surface as shipped. |
| **Theme 2 remainder** | Exact MCP actor tools and CLI commands; no natural-language interpretation. (P:480-486) | Future command-shaped channel surfaces. |
| **Theme 3** | AI-inferred Expectation, confidence-gated natural language, email-as-UI and magic-link interaction. (P:487) | Future email/chatbot/NL experience. |
| **Theme 4** | Routing, escalation, auto-assignment and explainable routing record. (P:488) | Future policy/eligibility UX; not AuthorityLevel enforcement in v1. |
| **Theme 5** | Cost Meter, caps, Cost Roll-Up and cost-aware scheduling. (P:489) | Future Cost experience, visually distinct from Effort by labels and placement. |
| **Theme 6** | Bound/single-use/expiring/idempotent links, forwarding≠authority, step-up, consent/residency, non-repudiation and participant grants. (P:490) | Future link-security, participant and audit UX; distinct from Theme 3 email entry. |

Theme 4 escalation and Theme 5 degradation are the same ladder traversed in opposite directions. (P:492-494)

## Exact user journeys

The source titles and protagonists are:

1. **UJ-1. A builder wires Works into a module.** Protagonist: **A Hexalith builder**; no proper name is supplied. (P:56-60)
2. **UJ-2. A system/AI executor burns down work through one uniform surface.** Protagonist: **A service Party — channel = MCP, machine authority**; no proper name is supplied. (P:62)
3. **UJ-3. A Work Item spawns a child, suspends, and resumes.** Protagonist: **Ada's release-checklist item**, driven by a system Party on MCP. (P:64)
4. **UJ-4. (Deferred — Theme 3 horizon, not built in v1.)** Protagonists: **Mary** and **an external supplier she only reaches by inbox**. (P:66)

Named Dana/Sam/Atlas material may remain only as clearly labelled non-source reference flows; it must not replace UJ-1–UJ-4.

## Stable UX-visible product contract

| Area | Governing semantics and UX consequence |
|---|---|
| Create / Expectation | Create requires non-empty Obligation plus Tenant; a creation-time binding does not enter Assigned; unestimated is valid; Obligation is at most 4,000 characters; optional Expectation is a reference resolved outside the aggregate. (P:110-124) |
| Effort / schedule | The first estimate fixes Unit; wrong-Unit progress or re-estimate rejects without mutation; Estimated is nonnegative and Remaining clamps at zero. Priority is Critical, High, Normal, Low; Priority and Due Date are optional. (P:126-141) |
| Lifecycle | Claim is the sole entry to InProgress from Assigned or Queued; there is no Start event. Reject is Assigned-only and defaults to Queued; only its non-requeue form is terminal. Resumed is a transition, not a resting state. Invalid acts reject; only exact duplicate terminal acts no-op. Active Handoff is governed by O, not the rendered restriction. (P:157-183; O:23, O:33) |
| Completion / correction | ReportProgress is InProgress-only and positive; progress-to-zero and explicit Complete are distinct completion paths. Explicit Complete is legal from InProgress or Suspended at any Remaining, preserves Estimated/Done, and contributes zero. ReEstimate never completes. Correction and conditional reopen are governed by O. (P:196-206; O:24, O:34) |
| Planning / termination | ReEstimate and Reschedule are lifecycle-neutral in non-terminal states. Cancel and Expire are terminal. Only Cancel/Expire cascade to active descendants; Reject does not. (P:208-223) |
| Roll-Up | Own and rolled Remaining are distinct. Roll-Up is eventual, idempotent, per Unit, and may be explicitly unavailable; unestimated descendants contribute zero plus a count. Never show stale/partial data as fresh. Only authoritative Attached edges contribute. (P:231-260; O:20) |
| Suspend / resume | Suspension holds a set of Await-Conditions. First exact kind+key match resumes, records the consumed condition, clears the set and returns to InProgress. Only replay of that consumed condition no-ops; other nonmatches reject. (P:268-285) |
| Registry / Reactor | The Registry owns tree topology and Reactor owns mechanical cross-aggregate coordination. Durable attachment and released/superseded delayed work are governed exclusively by O until the PRD is rendered again. (P:287-306; O:20-21, O:30-31) |
| Executor responsibility | One binding identifies one responsible Party; Channel and AuthorityLevel do not create executor kinds. Claim is push/pull's shared entry and one racer wins. Current actor matching, origin restrictions, and Handoff are governed by O; AuthorityLevel remains carried, not enforced. (P:314-339; O:22-23) |
| What's next | Party view is that Party's Assigned items plus tenant Queued items; coordinator view is all Assigned and Queued. Sort present Priority Critical→High→Normal→Low, then earliest present Due Date, then ordinal WorkItemId; absent values sort last. (P:347-354) |
| Conversation / history | Raw Acts are the Work Item narrative; optional notes are at most 1,000 characters and are not comments. Conversation is a first-link-wins correlation ID only; Works stores no conversation content. (P:185-194, P:356-362) |
| Failures / privacy | Verified tenant and actor claims are required before dispatch/query. Domain rejection, idempotent confirmation, authorization denial, claim loss, projection unavailable/stale, concurrency exhaustion and infrastructure failure are distinct outcomes. (P:444-453) |

## Current spine result and still-open UX details

WD and WE now reflect the horizon split, exact journeys, lifecycle/action semantics, eventual Roll-Up, Conversation ownership, FrontComposer inheritance, and the O overrides. (WD:71-77, WD:115-127, WD:134-145; WE:22-40, WE:99-180, WE:309-400)

Product-source-open details remain those recorded in O: exact durable-evidence naming, the final executor-bound command list, Handoff eligibility/confirmation/recovery, CorrectProgress validation and note policy, and whether Released versus Superseded needs distinct treatment. (O:36-42)

The PRD does not define web IA/default routes, email-client support, locale/date/number/Unit formatting, translation policy, WCAG target, focus/live-region behavior, keyboard shortcuts, responsive breakpoints, or rejection copy. These are UX/design-system decisions. Works defines no accent setting: it inherits the active FrontComposer accent. (WD:71-77, WD:151)

Retain the qualitative anchors: **Everything is a Party**, **Progress is a fact**, **AI in the loop, never in the system of record**, calm eventuality, and Works as a coordination kernel rather than a task database, comment store, or BPMN engine. (P:29-33, P:75-82, P:185-193, P:399-405)
