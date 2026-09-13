# Reconciliation — current Works architecture

Updated: 2026-09-12

This note extracts UX-relevant constraints from the current architecture spine. It does not turn target implementation detail into product copy or duplicate volatile product overrides.

Source aliases:

- A: _bmad-output/planning-artifacts/architecture.md
- AM: _bmad-output/planning-artifacts/architecture/architecture-works-2026-09-08/.memlog.md
- O: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/reconcile-prd-memlog.md
- WD / WE: current Works DESIGN.md / EXPERIENCE.md in this directory

## Authority and status

The PRD plus O own product behavior; A owns compatible implementation constraints; code owns current implementation evidence. In A, ADOPTED is settled, CURRENT is verified today, TARGET is a binding migration outcome, and ASSUMPTION is a fail-closed default still awaiting confirmation/evidence. An adopted decision is not necessarily implemented. (A:28-38)

A is now status final/readiness conditional and explicitly binds **FR-1..FR-26** plus the security, recovery, integrity and platform-migration NFRs. Its capability map covers every FR group. (A:1-23, A:603-615)

A:29-30 and AM:12,33 say “final four” overrides, but the current PRD memlog contains five accepted entries because delayed released/superseded-leg rejection was appended as a decision. O is the canonical UX pointer for all five and records the rendered-source drift. (O:12-34)

## Architecture decisions that bind UX behavior

| Area | Current architectural binding | UX consequence |
|---|---|---|
| Lifecycle / correction / completion | Own state and rolled state remain separate. Explicit Complete preserves Estimated/Done but contributes zero. Progress completion carries a distinct completion kind. CorrectProgress is an absolute audited correction and can reopen only progress-completed work. (A:148-159, A:264-275) | Keep explicit completion, progress completion, correction and conditional reopen distinguishable. Volatile product meaning comes from O, not a copied protocol description. (O:24, O:34) |
| Active Handoff | HandoffWorkItem is the only active binding change; InProgress/Suspended retain Status, and Suspended retains Await-Conditions. AuthorityLevel is carried, not an authorization system. (A:216-225) | Present Handoff as an auditable responsibility change, never Assign/requeue or a Work Status transition. Use O for the product rule. (O:23, O:33) |
| Registry / durable attachment | Registry is the sole topology authority. The target lifecycle is Reserved → Creating → Attached or Reserved → Released; durable matching child-created evidence gates Attached, and only Attached topology is authoritative. (A:353-394; AM:17-18) | Reserved/Creating stay outside the authoritative tree, Roll-Up and cascade. Released/superseded delayed work must not become success. Keep exact evidence names/fields out of UX copy; use O. (O:20-21, O:30-31) |
| Reactor and eventuality | Reactor is the sole mechanical cross-aggregate coordinator; effects are checkpointed, eventual and re-issued after crashes while aggregates remain available unless explicitly fenced. (A:178-185) | Show pending, delayed, repairing and confirmed states; do not promise synchronous spawn, cascade, resume or roll-up. |
| Roll-Up | Per-descendant absolute contributions and Attached topology drive state-based per-Unit totals; partial repair/rebuild is explicitly stale/unavailable and key-only notifications carry no model payload. (A:131-159, A:396-415) | Separate own/rolled totals, Unit groups, unestimated count and freshness. Unavailable is not zero, and terminal contribution is zero even when explicit-completion history shows residual Remaining. |
| Actor responsibility / origin | The target authenticates actor and tenant before mutation, checks responsibility inside the serialized item turn, and origin-restricts system/Reactor work. A currently classifies ReportProgress, CorrectProgress, Complete, Suspend, Handoff and Reject as responsibility-bound; Assigned Claim also matches, while Queued Claim binds the admitted actor. Denial is authorization, not domain rejection. (A:417-443; AM:22, AM:65) | Action surfaces consume authoritative capability metadata and distinguish executor mismatch, tenant denial, invalid workload origin and domain rejection. Do not infer actor from the binding or style AuthorityLevel as permission. O remains the product-level authority. (O:22, O:32, O:39-40) |
| Resume / reminders | Exact current Await-Condition match resumes and clears the set; only replay of the consumed condition no-ops. Domain time arrives through typed durable intents and a persisted schedule witness. (A:187-214, A:458-471) | Render multi-await state, unmatched rejection, narrow duplicate confirmation, advisory due/overdue state, and delayed/recovered reminder outcomes. |
| Boundaries / Conversation | Works owns binding/link semantics but references PartyId and ConversationCorrelationId; it copies neither Party profiles nor Conversation content. (A:277-312) | Resolve sibling data independently and preserve its loading/error/authorization state. Work history and Conversation remain separate surfaces. |
| Recovery / privacy | Recovery is periodic, lossless, quarantine-aware and readiness-affecting. Payloads, secrets and personal data stay out of logs, traces and ProblemDetails; privileged repair/replay is audited. (A:501-530) | Keep safe last-known data with explicit degradation, expose support-safe recovery, and never surface raw envelopes, tokens, stack traces or hidden identifiers. |

## Current versus target

The architecture is a binding target, not a claim that the whole kernel or a UI is implemented. Platform migration rows R1–R11 remain partial/absent/transitional, implementation readiness is conditional, and the UI/Fluent stack is deferred until the first UI capability because v1 is headless. (A:326-351, A:617-646)

WD and WE are aligned with the architecture on all 26 FRs, active Handoff, CorrectProgress/reopen, durable attachment, actor responsibility, eventual Roll-Up, Conversation ownership and the headless-v1 horizon. (WD:71-77, WD:115-145; WE:99-180, WE:309-340)

## Still-open details

- O remains authoritative for the unresolved product details: final durable-evidence naming, the rendered executor-bound list, Handoff eligibility/confirmation/recovery, CorrectProgress validation/note policy, and Released-versus-Superseded presentation. (O:36-42)
- A supplies a target executor-bound list, but it has not been rendered into the PRD. UX must consume server-authoritative capability metadata rather than hard-code the architectural list. (A:430-442; O:39)
- Many registry, actor-delegation, roll-up, reminder, recovery and migration rules are TARGET/ASSUMPTION rather than CURRENT. UI states may describe the contract but must not claim deployed availability. (A:34-38, A:187-206, A:227-275, A:353-443)
- Exact action copy, retry affordances, warning hierarchy, projection-health component choice, web IA/default route and email-client behavior are not architecture decisions.
- Fluent UI and FrontComposer specifics are deliberately deferred by A; current UX inheritance comes from the active FrontComposer sources, not from architecture. (A:636-646)
