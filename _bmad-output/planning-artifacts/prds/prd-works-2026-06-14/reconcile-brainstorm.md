---
title: "Hexalith.Works — Input Reconciliation (Brainstorm → PRD)"
status: review
created: 2026-06-14
input: "../../brainstorming/brainstorming-session-2026-06-14-0910.md (44 ideas / 6 themes)"
targets:
  - "prd.md"
  - "addendum.md"
---

# Input Reconciliation — Brainstorming Session → PRD

**Source input:** `brainstorming-session-2026-06-14-0910.md` (44 ideas across 6 themes).
**PRD under review:** `prd.md` + `addendum.md`, scoped to **v1 = Themes 1 & 2**.

**Scope rule applied:** Themes 3–6 ideas are *expected* to be deferred to the §12 Roadmap, not promoted to v1 FRs. A Theme 3–6 idea is flagged only if it was **dropped entirely** (absent from §12 *and* its enabling v1 seam is also absent) or **mis-scoped**. Theme 1 & 2 ideas are checked one-by-one for representation in FRs / Glossary / NFRs or deliberate handling.

**Headline:** Coverage of Theme 1 & 2 is strong — most atoms and architecture ideas have a clearly traceable FR or Glossary entry. There are **no true omissions of a whole Theme 1/2 idea.** The issues are (a) a handful of requirement-bearing *details* the PRD weakened or quietly redefined, (b) two Channel ideas (MCP/CLI) that the session placed in the **foundation theme (Theme 2)** but the PRD pushed entirely into deferred Theme 3, and (c) one unresolved tension the session created (debounce vs. re-inference) that the PRD names but a second tension (cost-aware scheduling polluting the "thin core" scheduler) that it does not. Details below.

---

## 1. Theme 1 & 2 ideas — coverage matrix

Legend: ✅ represented · ⚠️ distorted/weakened/mis-scoped · ❌ dropped.

### Theme 1 — The Work Item Essence (Atoms #1–9)

| # | Idea | PRD location | Verdict |
| --- | --- | --- | --- |
| #1 | Burn-Down Truth (estimated/done/remaining; Done = remaining 0; progress is a fact) | Glossary (Burn-Down, Remaining, "Done = Remaining is 0"); FR-3, FR-8 | ✅ |
| #2 | Competes for Attention (priority + due date; "what next?" intrinsic) | Glossary (Schedule); FR-4, FR-20 | ⚠️ see D-1 |
| #3 | It Remembers (comments = append-only narrative; comment stream and event stream are two views of one history) | Glossary (Raw Act, Projection); FR-21 | ⚠️ see D-2 |
| #4 | One Work, Many Executors (executor is metadata on one contract, not a subtype) | Vision; Glossary (Executor); FR-17 | ✅ |
| #5 | Unit-Agnostic Progress (per-item pluggable unit/meter) | Glossary (Unit); FR-3 | ✅ |
| #6 | Long-Running ⟹ Resumable ("retry" = "continue the burn-down") | FR-8 consequence; Vision | ✅ |
| #7 | Work Spawns Work → Suspension (saga/continuation, durable, survives restarts) | Glossary (Saga, Await-Condition); FR-14–FR-16 | ⚠️ see D-3 |
| #8 | Remaining Rolls Up (recursive subtree aggregate) | Glossary (Roll-Up); FR-11–FR-12 | ✅ |
| #9 | "Done" Is a Signal Anyone Can Await (wait on something you didn't spawn; deps/timers/external triggers unify) | Glossary (Await-Condition); FR-14, FR-15 | ✅ |

### Theme 2 — Thin-Core Architecture & Boundaries (Architecture #1–6 + Channels #1–3)

| # | Idea | PRD location | Verdict |
| --- | --- | --- | --- |
| Arch #1 (#19) | Conversation Delegated to Hexalith.Conversations (one correlation id) | FR-21 + its `[ASSUMPTION]` | ✅ |
| Channel #1 (#20) | Omnichannel Advancement (executor ⊥ channel; switch channel mid-work; one thread) | Glossary (Channel: "a Party may change Channel mid-work") | ⚠️ see D-4 |
| Channel #2 (#21) | MCP as a First-Class Actor Channel (agents list/claim/advance/complete; work is agent-addressable) | §12 Theme 3 (channel adapters); UJ-2 uses Channel=MCP | ⚠️ see G-1 |
| Channel #3 (#22) | CLI as Scriptable Work (scriptable infrastructure) | §12 Theme 3 (channel adapters) | ⚠️ see G-1 |
| Arch #2 (#23) | Coordination Kernel / thin core (scheduler/coordinator, not a data store) | Vision; §5 Non-Goals; Glossary (Work Item); SM-C1 | ✅ |
| Arch #3 (#24) | Everything Else Is a Late-Resolved Reference | Glossary (Reference Value Object); FR-21 | ✅ |
| Arch #4 (#25) | Executor Binding Is the One Pluggable Seam | Glossary (Executor Binding); FR-17 | ✅ |
| Arch #5 (#26) | "External Party" Isn't Special — Party + Channel | Glossary (Party: "there is no 'external party' type"); FR-17 | ✅ |
| Arch #6 (#27) | One Executor Concept — Everything Is a Party | Vision (the keystone bet); FR-17; SM-3 | ✅ |

**Conclusion on omissions:** Every Theme 1 & 2 idea is represented somewhere. There is **no idea dropped entirely.** The remaining findings are distortions/weakenings (§2) and one scoping miscall about which theme MCP/CLI belong to (§3).

---

## 2. Distortions / weakenings of requirement-bearing detail

### D-1 — "What next?" demoted from an intrinsic capability to a sort. **(Severity: Medium)**
- **Session intent:** Atom #2 says *"scheduling/ranking ('what next?') is intrinsic to `Works`, not external."* It is named as a defining property of the work-item essence.
- **PRD:** FR-20 delivers a read-side query "ordered by Priority then Due Date," then immediately fences it: *"This is a Projection/query only — no routing, assignment, or ranking *engine* (that is Theme 4)."*
- **Why it matters:** The narrowing from *"'what next?' is intrinsic"* to *"a 2-key sort projection"* is mostly defensible (true ranking is Theme 4), **but** the PRD's promise in §3/Projection — *"the 'what's next' query is a projection"* — and Atom #2's framing of `Works` as owning standing in a *contended* queue are slightly stronger than a sort. This is a reasonable v1 scoping call, not a true loss, but the architecture phase should be told explicitly that "what next" in v1 = deterministic sort, **not** the contended-queue arbitration the brainstorm implied. Flagged so it is a conscious decision, not silent drift.

### D-2 — Comment/event duality preserved as a *reference* but the "two views of one history" promise is softened. **(Severity: Medium)**
- **Session intent:** Atom #3 (It Remembers) is emphatic: *comments are an intrinsic append-only narrative of why the numbers moved*, and **"comment stream and event stream are two views of the same history."** This is listed as a Theme-1 *domain atom* — part of what a work item irreducibly *is*.
- **PRD:** FR-21 last bullet: *"The comment narrative and the event stream are two views of one history; Works holds a Conversation correlation ID rather than its own comment store. `[ASSUMPTION: v1 references a Conversation by ID; it does not implement comment storage.]"*
- **Why it matters / the distortion:** The session framed the narrative-of-*why* as intrinsic to the work item (Theme 1 essence). The PRD's decision to delegate the *store* to `Hexalith.Conversations` is correct and consistent with thin-core (Arch #1). **However**, two things were lost in the hand-off:
  1. The session's specific claim is that the **event stream itself** is one of the two views — i.e., the why-the-numbers-moved narrative is *partly already in the domain events* (`ProgressReported`, `ReEstimated` carry the reason). The PRD treats "the comment narrative" as wholly a Conversations concern and does not require that progress/re-estimate events carry a human-readable *why/note* field. The "narrative of why" risk being homeless: not in Works events (no note field specified), and only in Conversations if a builder wires it.
  2. There is **no FR** asserting the linkage invariant (a Work Item event correlates to a Conversation entry by the single correlation id). It lives only as a prose bullet + assumption under FR-21, not as a testable consequence. Recommend an explicit consequence: progress/re-estimate raw acts MAY carry a verbatim note, and the Conversation correlation id is recorded so the two views are reconstructable.

### D-3 — Saga durability ("survives restarts") stated in the brainstorm, not asserted as a requirement. **(Severity: Medium)**
- **Session intent:** Atom #7: *"a saga/continuation in event-sourcing terms — **durable, survives restarts**."* Atom #6 likewise: a crash mid-flight just means remaining > 0. The durability-across-process-restart property is the *novelty* of the suspend/resume idea.
- **PRD:** FR-14–FR-16 model suspend/resume and Await-Conditions well, and FR-8 says a crash leaves the item resumable. But **no FR or NFR states the durability-across-restart guarantee as a testable property.** §11 SM-1 exercises create→…→complete under Aspire but does not include a restart/replay-mid-saga step. The §9 "Event-sourcing invariants" NFR (persist-then-publish) implies durability but does not name "a suspended saga survives a host restart and resumes" as an acceptance signal.
- **Why it matters:** The single most distinctive claim of Atom #7 (durable continuation surviving restart) has no acceptance test. SM-1 could be strengthened to suspend, restart the host (or rehydrate the aggregate from the event stream), then deliver the trigger and confirm resume — otherwise the headline saga property is asserted in prose only.

### D-4 — Omnichannel "one converging thread" reduced to "may change Channel." **(Severity: Low–Medium)**
- **Session intent:** Channel #1: same work item advances via chatbot / MCP / CLI / email, **"all converging on one thread"**; *"a party can switch channels mid-work."* Two claims: (a) channel-switching, and (b) **convergence onto a single thread** (which ties back to the one Conversation correlation id of Arch #1).
- **PRD:** Glossary (Channel) captures only (a): *"a Party may change Channel mid-work."* The "all converging on one thread" half — i.e., that channel switches do **not** fork the narrative because all channels resolve to the same Conversation correlation id — is not stated anywhere.
- **Why it matters:** The convergence guarantee is a v1-relevant *boundary* property (it constrains how the Conversation reference and Channel field relate), even though the channel adapters themselves are Theme 3. It should at least appear as a designed-for note: Channel is per-binding metadata, but the narrative thread is the single Conversation correlation id regardless of Channel. As written, a reader could implement per-channel threads and not violate any FR.

### D-5 — "Resumed" as a first-class lifecycle state vs. transient. **(Severity: Low)**
- **Session intent:** Action Planning lists the lifecycle `Created → Assigned|Queued → InProgress → Suspended(awaiting event) → **Resumed** → Completed | Cancelled | Rejected | Expired` — `Resumed` appears as a peer state.
- **PRD:** Glossary marks Status `Resumed (transient)`; FR-6 makes it `Suspended → (Resumed →) InProgress`. The PRD demotes `Resumed` to a transient pass-through.
- **Why it matters:** This is a *reasonable refinement* (a durable item rests in `InProgress`, not `Resumed`), and the PRD flags it as a deliberate `[ASSUMPTION]`-adjacent choice. Low severity — noted only because the brainstorm's enumerated state list was altered; the change is defensible and should simply be confirmed (it is implicitly covered, but not called out among the §13 open questions).

### D-6 — Tree depth: brainstorm says **no limit**; PRD imposes a cap. **(Severity: Low — but it is a real reversal)**
- **Session intent:** Atom #8 (Remaining Rolls Up) and the aggregate state plan describe unbounded recursive trees; the brainstorm states **no depth limit.** FR-13's own assumption admits: *"The brainstorm states no limit — proposing a guard…"*
- **PRD:** FR-13 imposes a configurable default max depth 32 and rejects deeper spawns.
- **Why it matters:** The PRD is *transparent* about this reversal (assumption + open question #5), so it is not a silent distortion — but it **does contradict** the source intent, so it is logged here for completeness. The justification (bound roll-up cost / prevent runaway trees) is sound; this is correctly surfaced for confirmation rather than buried.

---

## 3. Mis-scoped: MCP and CLI placed in v1's foundation theme, deferred by the PRD to Theme 3

### G-1 — Channels #2 (MCP) and #3 (CLI) belong to the session's **Theme 2** but the PRD treats them purely as Theme 3 deferrals. **(Severity: Medium)**
- **Session classification:** In the session's own Thematic Organization, *"Theme 2 — Thin-Core Architecture & Boundaries: Architecture #1–6 **+ Channels #1–3**."* So MCP-as-actor-channel and CLI-as-scriptable-work are catalogued as **foundation (Theme 2)** ideas, not interaction (Theme 3). The user's v1-relevant list (this reconciliation's mandate) explicitly includes MCP-as-actor-channel (#21) and CLI-as-scriptable-work (#22).
- **PRD treatment:** The §12 Roadmap places "channel adapters (email/magic-link, chatbot, MCP, CLI)" under **Theme 3 — LLM-native interaction**, and §5 Non-Goals + §6.2 state *"no production channel adapter (MCP/CLI/email) ships in v1."* So MCP/CLI were re-filed from foundation (Theme 2) into deferred interaction (Theme 3).
- **Is this a true drop?** No — the *enabling seam* is present: Channel is a Glossary term and a binding field; UJ-2 explicitly drives a Work Item with `Channel = MCP`; the Executor Binding is the pluggable seam (FR-17). So the v1 seam for MCP/CLI **is** laid. This is therefore **mis-scoping, not omission**: the *concrete adapter* deferral is correct (an MCP server / CLI binary is technical surface, fairly Theme-3-ish), but the PRD silently re-themed two ideas the session deliberately put in the v1 foundation theme, and never acknowledges the reclassification.
- **Recommendation:** Add a one-line note in §12 (or §5) acknowledging that MCP/CLI were Channel ideas the session grouped under Theme 2, that v1 lays their seam (Channel field + Executor Binding + uniform command surface), and that only the *production adapter* is deferred. Without this, the brief→PRD trace looks like two foundation ideas vanished into a different theme. Also worth confirming whether the **uniform command surface** that MCP/CLI/chatbot all target (Channel #1's "converging" + System #3's "one resolver, all executors") is itself a v1 deliverable — FR-17 implies a single command surface, but no FR states "the command surface is channel-agnostic / one command set serves all channels" as the explicit Theme-2 property the session intended.

---

## 4. Requirement-bearing details from the session the PRD weakened or lost

Beyond the per-idea distortions above, these specific named invariants / enums / edge-cases / promises deserve explicit call-out:

### S-1 — Named invariant "Done = remaining 0." ✅ Preserved.
- Carried verbatim in Glossary (Remaining: *"Done = Remaining is 0 is the completion invariant"*) and FR-8. No loss. Good.

### S-2 — The enumerated state machine. ✅ Mostly preserved (see D-5 for `Resumed`).
- Brainstorm: `Created → Assigned|Queued → InProgress → Suspended(awaiting event) → Resumed → Completed | Cancelled | Rejected | Expired`. PRD FR-6 + Glossary Status reproduce all states; only `Resumed` was demoted to transient (D-5). No state was dropped. ✅

### S-3 — The event list (closed from the open-ended brainstorm "…"). ✅ Handled well, transparently.
- Brainstorm action plan listed `WorkItemCreated, ProgressReported, ReEstimated, ChildSpawned, Suspended, Resumed, Completed…` (open-ended). FR-7 closes it to a 13-event catalog and **flags** that it is closing the open list. This is a strengthening, correctly surfaced. ✅ One nuance: the brainstorm's bare `Suspended`/`Resumed` became `WorkItemSuspended`/`WorkItemResumed` (naming-convention fix) — fine.

### S-4 — The "what next" promise. ⚠️ See D-1. Weakened to a sort with an explicit Theme-4 fence.

### S-5 — Comment/event duality. ⚠️ See D-2. The "event stream is one of the two views" half lost its teeth; no note/why field required on progress events; linkage invariant only in prose.

### S-6 — Saga semantics (durable, survives restarts). ⚠️ See D-3. Modeled, but the survives-restart guarantee has no acceptance test.

### S-7 — "Await on something you didn't spawn." ✅ Preserved.
- Atom #9's key novelty (wait on a webhook/date/external reply you didn't create, not just child completion) is captured: Await-Condition enumerates `ChildCompleted | DateReached | ExternalSignal` (Glossary + addendum + FR-14/FR-15). The "didn't spawn it" generality is intact via DateReached and ExternalSignal. ✅

### S-8 — AuthorityLevel enum. ⚠️ Added detail beyond the session — acceptable, but note it is invented.
- The session said authority "rides on the binding" (Capability #2, a *Theme 4* idea) but proposed **no enum.** The PRD invents `{ Read, Contribute, Coordinate, Administer }` (FR-19) and carries it in v1 (carried-not-enforced). This is *additive enrichment*, transparently flagged as an assumption + open question #3. Not a distortion of the session (the session had nothing to distort here) — flagged only so reviewers know the enum is a PRD invention, not a session decision. Mild tension with counter-metric SM-C2 ("don't over-fit v1 to deferred themes"): carrying a Theme-4 authority enum in v1 is borderline pre-building for a deferred theme. Worth a conscious confirm.

### S-9 — Edge case: parked on multiple conditions (child *and* date). ✅ Added, good.
- UJ-3 adds an edge case the session did not explicitly cover (item parked on both a child and a date; the date arriving first resumes it independently). This is a *good* elaboration consistent with Atom #9. ✅ But note FR-14 records *"the Await-Condition"* (singular) — the UJ-3 multi-condition edge case implies a Work Item can hold **more than one** Await-Condition simultaneously, while Glossary defines Await-Condition as *"the event a Suspended Work Item is parked on"* (singular) and FR-5 says *"one Await-Condition while Suspended."* **Internal inconsistency:** UJ-3's "also parked on a date that arrives first" contradicts the single-await-condition model in FR-5/Glossary. Either the model must allow a set of await-conditions, or UJ-3's edge case is unbuildable as specified. **(Severity: Medium — a genuine spec contradiction the reconciliation surfaces.)**

---

## 5. Open tensions the session flagged — resolved / acknowledged?

The session surfaced several tensions. Checking each against the PRD:

### T-1 — Status-driven re-inference (System #2) vs. cost-aware debounce (Economics #4). ✅ Acknowledged, correctly deferred.
- PRD §12 "Designed-for tensions to revisit at theme time" explicitly lists *"status-driven re-inference (Theme 3) vs. cost-aware debounce (Theme 5)."* Both sides are Theme 3/5, so deferral is correct and the tension is named. ✅

### T-2 — Authored vs. AI-inferred Expectation contract (External #4 / System #1 vs. External #2's contract). ✅ Acknowledged.
- PRD §12 lists *"authored vs. AI-inferred Expectation contract representation (Theme 3)"* and Open Question #2. The session's two competing models (schema-less AI-inferred vs. authored answer-contract) are both deferred and the duality is flagged. ✅

### T-3 — Start-cheap-escalate ↔ budget-degrade as one ladder (Breakthrough #4). ✅ Deferred coherently.
- §12 Themes 4 & 5 both reference the ladder; the "same ladder run both ways" framing is preserved in the addendum's "deferred-theme mechanism depth." ✅ (Both sides Theme 4/5 — correctly out of v1.)

### T-4 — Cost-aware scheduling vs. the thin-core scheduler. ⚠️ NOT acknowledged. **(Severity: Low–Medium)**
- **The unflagged tension:** Arch #2 declares the kernel owns *"schedule/priority"* and is *"a scheduler/coordinator."* Economics #4 (Cost-Aware Scheduling) says the **scheduler** coalesces/debounces re-inferences and "prefers cheap channels" — i.e., scheduling logic that is cost-aware lives *in the scheduler the kernel owns.* This creates a latent boundary question: does the v1 thin-core scheduler (FR-20 "what's next") have to be shaped now so that Theme 5 cost-aware scheduling can bolt on, or is scheduling itself going to migrate partly out? The PRD's §12 defers cost-aware scheduling to Theme 5 (correct) but does **not** flag that Theme 5 wants to modify the *kernel-owned* scheduler — unlike re-inference (T-1) which lives outside the kernel. This is the one tension where a deferred theme reaches back into a Theme-2-owned concern (the schedule), and the PRD does not acknowledge the reach-back. Recommend adding it to the §12 designed-for tensions list.

### T-5 — Self-inflicted attack surface (magic links + AI-in-loop + auto-routing) → defenses. ✅ Deferred as Theme 6.
- All Defense #1–6 are Theme 6; §12 + §10 lay the raw-act seam. The session's own note that defenses "mostly reused existing mechanisms" is honored by §10 mapping each to a v1 seam. ✅

---

## 6. Summary of findings by severity

**Medium**
- **D-1** "What next?" demoted from intrinsic capability to a 2-key sort (defensible scoping, but should be a conscious architecture-phase note, not drift).
- **D-2** Comment/event duality: "event stream is one of two views" lost its teeth; no why/note field required on progress/re-estimate events; linkage invariant only in prose, not a testable FR.
- **D-3** Saga "durable, survives restarts" — modeled but no acceptance test (SM-1 has no restart/rehydrate-mid-saga step).
- **G-1** MCP (#21) & CLI (#22) silently re-filed from the session's foundation **Theme 2** into deferred **Theme 3**; seam is laid (Channel + binding) so not a drop, but the reclassification is unacknowledged and "channel-agnostic uniform command surface" is not asserted as a v1 property.
- **S-9** Spec contradiction: UJ-3's multi-await edge case (parked on child *and* date) vs. single Await-Condition model in FR-5/Glossary.

**Low–Medium**
- **D-4** Omnichannel "all converging on one thread" reduced to "may change Channel"; convergence-onto-one-Conversation guarantee not stated.
- **T-4** Cost-aware scheduling (Theme 5) reaches into the kernel-owned scheduler (Theme 2 concern); tension not flagged in §12.
- **S-8** AuthorityLevel enum is a PRD invention carried in v1 — mild tension with counter-metric SM-C2 (over-fitting v1 to a deferred theme).

**Low**
- **D-5** `Resumed` demoted from enumerated peer state to transient (defensible; not in open questions).
- **D-6** Tree-depth cap contradicts brainstorm's "no limit" (transparently flagged via assumption + OQ#5, so logged for completeness only).

**No true omissions:** every Theme 1 & 2 idea is represented in FRs, Glossary, or NFRs.

---

## 7. Recommended PRD edits (smallest set to close the gaps)

1. **D-2 / S-5:** Add an FR-7 or FR-21 consequence: progress/re-estimate Raw-Act events MAY carry a verbatim human-readable note (the "why the numbers moved"), and the Conversation correlation id is recorded so the comment-stream and event-stream views are reconstructable from one history. Make the linkage a testable consequence, not just prose.
2. **D-3 / S-6:** Strengthen SM-1 (or add SM-6) with a suspend → host-restart/rehydrate → deliver-trigger → resume step, so the "durable saga survives restart" property has acceptance evidence.
3. **S-9:** Resolve the single-vs-multiple Await-Condition contradiction — either change FR-5/Glossary to "zero-or-more Await-Conditions while Suspended" (matching UJ-3) or remove the UJ-3 multi-park edge case. Add to §13 open questions.
4. **G-1:** Add a §12/§5 note acknowledging MCP/CLI were Channel ideas the session grouped under Theme 2; state that v1 lays their seam and defers only the production adapter; consider an FR asserting the command surface is channel-agnostic (one command set serves all channels).
5. **D-4:** Add a designed-for note: Channel is per-binding metadata, but the narrative thread is the single Conversation correlation id regardless of Channel (channel switching does not fork the thread).
6. **T-4:** Add to §12 "designed-for tensions": cost-aware scheduling (Theme 5) modifies the kernel-owned scheduler/"what's next" — confirm the v1 schedule shape can host it.
7. **D-1, D-5, S-8:** Confirm-only — already partially surfaced; just ensure each is a conscious decision (add D-1's "what next = sort, not arbitration" note for the architect; add D-5 `Resumed` to open questions if desired; re-check S-8 against SM-C2).

---

_Reconciliation completed 2026-06-14 against `brainstorming-session-2026-06-14-0910.md`._
