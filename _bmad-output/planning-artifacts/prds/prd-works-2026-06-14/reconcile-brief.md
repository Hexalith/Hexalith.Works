---
title: "Hexalith.Works — PRD ↔ Brief Reconciliation (finalize input-pass)"
status: review
created: 2026-06-14
updated: 2026-06-14
---

# Reconciliation — Brief/Addendum → PRD/Addendum

**Inputs reconciled:**
- Source: `briefs/brief-works-2026-06-14/brief.md` + `briefs/brief-works-2026-06-14/addendum.md`
- Target: `prds/prd-works-2026-06-14/prd.md` + `prds/prd-works-2026-06-14/addendum.md`

**Scope rule applied:** v1 = Themes 1 & 2. Themes 3–6 are *expected* to be deferred and live in §12 Roadmap. A deferred item is only flagged if it was dropped **entirely** (not even in the roadmap) or **mis-scoped**. Qualitative/positioning material is flagged whenever the FR structure flattened or under-weighted it, regardless of theme.

**Overall verdict:** The PRD is a faithful, high-fidelity translation of the foundation scope. Coverage of Themes 1 & 2 is essentially complete, and the deferred themes are well-mapped to v1 seams. The gaps are concentrated in **positioning/spirit** that the FR structure under-weights, a few **distortions of emphasis** (most notably "omnichannel from day one"), and a handful of **specific brief claims/framings that fully disappeared**. Nothing the PRD added directly contradicts the brief, but two additions narrow the brief's intent.

---

## 1. GAPS — brief ideas missing from PRD AND addendum (not even in roadmap)

### G1. The "two problems, one root" framing is gone — and with it the *end-user pain* as a first-class problem. [SEVERITY: HIGH]
The brief's **Problem** section is built on a deliberate two-sided structure: (a) **end users** whose work "scatters into inboxes, chat threads, and shadow lists, or is lost" because they must "stop and log into a dedicated task app," and (b) **builders** juggling "three integrations, three audit trails, three scheduling models, and bespoke glue." The PRD's §2.1 Jobs-To-Be-Done captures the **builder** side thoroughly but the **end-user problem statement is absent as a problem** — it survives only as a deferred *journey* (UJ-4) and a *non-user* (§2.2). The "work that slips through the cracks" pain — arguably the more visceral, marketable half of the thesis — is not stated anywhere in the PRD as a problem the product exists to solve. This is the single biggest qualitative loss: the PRD reads as builder-infrastructure, not as the dual-audience product the brief describes.

### G2. The competitive "what we are NOT" contrasts are dropped. [SEVERITY: MEDIUM]
The brief's **Problem** and **What Makes This Different** sections carry sharp, specific competitor contrasts:
- Jira/Asana/Linear "are adding AI assignees — but only *internal* ones, with no external-person-by-email as a peer doer, no shared pull queue, and no notion of cost."
- Temporal/Camunda are "developer infrastructure for *code* or *BPMN diagrams*, not... a first-class, audited *work item* a business and an auditor can reason about."
- HumanLayer "bolts approvals onto an agent run — but the human is an interruption to a run, not a peer executor in a durable backlog."

The PRD §7 "Why Now" mentions "the whitespace is the synthesis" and the brief addendum's competitive digest is *referenced*, but none of these crisp competitor contrasts are reproduced in the PRD body. The PRD §5 Non-Goals states what Works is not (not a task DB, not BPMN, not AI-in-record) but never positions *against named competitors*. For a finalize pass this is acceptable (it lives in the brief addendum), but the positioning punch — "the fresh part is the external person reached by magic link as a peer executor in a shared pull queue" — is under-weighted.

### G3. The "answer-contract does triple duty" breakthrough concept is absent. [SEVERITY: MEDIUM]
The brief calls out, in both the Solution section and the addendum's **Breakthrough concepts** (#2), that the answer-contract is a **UX accelerator → input validator → prompt-injection boundary** — "triple duty." The PRD captures the prompt-injection-boundary facet (§10 "NL-is-data boundary... the future prompt-injection boundary") and the validation facet implicitly, but the **"triple duty" synthesis itself** — one of the five named keystone breakthroughs — is never stated as a concept. It is Theme 3 material, but the brief explicitly says to "carry into PRD positioning," and the PRD did not. (See also D-distortions: the brief addendum lists *five* breakthrough concepts to carry; the PRD surfaces three.)

### G4. "Start-cheap-escalate ↔ budget-degrade — the same ladder run up or down" is flattened. [SEVERITY: LOW-MEDIUM]
Brief addendum breakthrough #4 is the elegant observation that escalation (cheap→premium→human→external) and budget-driven graceful degradation are **the same ladder run in opposite directions**. The PRD §12 lists "start-cheap-escalate ladder" (Theme 4) and "spend caps → graceful degradation" (Theme 5) as **two separate, unrelated roadmap rows**. The unifying insight — that they are one mechanism — is lost. This is the kind of conceptual elegance the FR/table structure flattens. The PRD addendum §"Deferred-theme mechanism depth" does mention "start-cheap-escalate ↔ budget-degrade as one ladder run both ways" in passing, so it is not *entirely* gone — but it is buried in a parenthetical list rather than presented as the breakthrough the brief frames it as. (Marginal: present in PRD addendum, absent from PRD body and roadmap framing.)

### G5. The "no moat" honesty / humility framing is softened to a neutral observation. [SEVERITY: MEDIUM — spirit]
The brief's **What Makes This Different** opens with deliberate intellectual honesty: "The honest truth: every *primitive* here is mature..." and closes "We deliberately do *not* claim a moat on durable execution or model routing. The advantage is coherence." This is a distinctive *voice* and a strategic stance (don't overclaim). The PRD §7 keeps the factual half ("the enabling primitives... are all mature; the whitespace is the synthesis") but **drops the explicit "we do not claim a moat" stance entirely.** The self-aware humility — a deliberate positioning choice — is flattened into a neutral market observation. See also G1/G3: the PRD systematically loses the brief's *editorial voice* around its bets.

### G6. "Routing starts cheap and escalates... governed by per-type policy and cost caps" — the *cost-cap-as-DoS-guard* dual purpose. [SEVERITY: LOW — covered]
Minor/borderline: the brief addendum theme-6 row names "cost-cap-as-DoS-guard" (a cost cap doubling as a denial-of-service defense). The PRD §12 Theme 6 row **does** carry "cost-cap-as-DoS-guard," so this is preserved. Listed only to confirm it was checked and is NOT a gap.

### G7. "Spawns and suspends" → "a durable saga in event-sourcing terms" — the *ad-hoc vs. authored* distinction is partly lost. [SEVERITY: LOW]
The brief repeatedly contrasts Works' **event log of ad-hoc work** against Camunda/BPMN's **authored diagrams** ("Models predefined diagrams, not an event log of ad-hoc work"). The PRD §5 Non-Goals captures "no authored process diagrams — the event log of ad-hoc work is the model," so the *concept* survives. But the brief's framing that this ad-hoc-ness is a **competitive differentiator** (not just a non-goal) is weakened. Borderline — counted as covered but de-emphasized.

---

## 2. DISTORTIONS — meaning/scope/emphasis changed

### D1. "Omnichannel from day one" → "no production channel adapter ships in v1." [SEVERITY: HIGH — the headline distortion]
This is the most consequential reframing. The brief and addendum are emphatic and repeated:
- Brief Solution: "A work item can be **created and advanced from any channel** — a one-line email, the Hexalith Chatbot, an LLM tool over MCP, or the CLI — all converging on one item."
- Brief addendum "Resolved during the brief session" #2: "creation is **omnichannel from day one**; channel and executor are orthogonal."
- Brief Success Criteria: "**Capture in seconds, no app** — a user creates a work item from a one-line email or a single chatbot sentence in seconds."

The PRD scopes all production channel adapters out of v1 (§2.2 Non-Users, §4.7, §5, §6.2): "no production channel adapter ships in v1 (Theme 3)." **This is a legitimate scope decision** (channels are Theme 3, and the PRD is honest that the *seams* — Channel field, executor binding, await-condition — are laid in v1). BUT: the brief addendum explicitly tagged channels as a **day-one** commitment, and the *brief's own v1 scope* (Themes 1 & 2) did not list channels. So there is a genuine tension *inside the brief itself*, and the PRD resolved it by deferring — without flagging that it is overriding the brief addendum's "from day one" language. **Recommendation:** the PRD should explicitly acknowledge it is reinterpreting "omnichannel from day one" as "omnichannel-*ready* from day one (the Channel seam), adapters in Theme 3," so a downstream reader doesn't see a silent contradiction. As written, a reader comparing the two documents will perceive a reversal.

### D2. "Capture in seconds, no app" demoted from Product Success Criterion to deferred journey. [SEVERITY: MEDIUM]
The brief lists four **Product signals** under Success Criteria as co-equal with the build signals: capture-in-seconds-no-app, external-one-tap-advance, one-number-(work+cost), handoff-as-one-operation. The PRD §11 Success Metrics keeps **build signals** (SM-1..SM-5) faithfully, including handoff-as-one-operation (SM-5) and one-number/roll-up (SM-2). But the two *user-facing* product signals — **capture-in-seconds-no-app** and **external-one-tap-advance** — are demoted to UJ-4 (deferred) with no metric counterpart. This is scope-correct (they need Theme 3 channels) but the PRD never states "these brief success criteria are deferred product signals." A reader checking the brief's success criteria against the PRD's metrics will find two of four silently missing from the metrics table. **Recommendation:** add a one-line note in §11 that the two end-user product signals are deferred to the Theme-3 horizon (cross-ref UJ-4), so the demotion is explicit, not silent.

### D3. "one-number: work + cost" narrowed to "one number: remaining effort." [SEVERITY: LOW — scope-correct but note]
The brief's signal is "**One number: work + cost** — any objective's remaining effort *and* spend is a single queryable, rolled-up number." The PRD SM-2 / FR-11 deliver effort roll-up only (cost is Theme 5, cost-ready). This is correct per v1 scope and the PRD is explicit that the roll-up is "cost-ready." Noted only because the brief's headline phrase is "work + cost" and the v1 metric is "work" — the *and cost* half is structurally deferred, which the PRD handles well (FR-11 reusable machinery, §10 cost-ready). Not a real gap; included for traceability.

### D4. "everything is a Party" reduced from three named executors with verbs to a type-collapse mechanic. [SEVERITY: LOW-MEDIUM — spirit]
The brief gives each executor a vivid role + verb: "*system / AI agents* (claim and advance work over MCP), *internal users* (do and oversee work via chatbot/CLI), *external parties* (confirm or answer from their inbox, no login)." The PRD glossary defines Executor/Party crisply and FR-17 nails the "zero branching" mechanic, but the **three executors as distinct *characters with their own channels and verbs*** are flattened into "a Party may change Channel." The mechanic is preserved perfectly; the **texture of the three doers** (esp. "external party confirms from their inbox, no login" as a *peer*) is under-weighted in the body (it survives in UJ-4 and §2.2 as deferred). Given that "external person as a peer executor" is the brief's most-emphasized novelty, this is worth a sentence in §4.5.

### D5. Auditors: "non-repudiable, signed event record" → audit *enforcement* deferred, but the brief's audience claim softened. [SEVERITY: LOW — scope-correct]
The brief lists **Auditors** as a served audience: "a non-repudiable, signed event record of who did what, when, against which item." The PRD §2.2 correctly defers the *auditor-facing query/UI and signed-link enforcement* to Theme 6 while laying the raw-act model in v1 (§10). Scope-correct. The only soft loss: the brief frames auditors as a *served audience* (positive), the PRD frames them as a *non-user* (negative framing). Same fact, opposite valence. Minor.

---

## 3. QUALITATIVE / SPIRIT — keystone bets, framing, dual-horizon, two audiences

### S1. The five keystone "Breakthrough concepts" → only three carried. [SEVERITY: MEDIUM]
The brief addendum explicitly names **five** breakthrough concepts and says "carry into PRD positioning":
1. Everything is a Party — *the keystone* ✅ carried (PRD §1, §4.5, prominent).
2. Answer-contract triple duty — ❌ NOT carried as a concept (see G3).
3. Two parallel burn-downs (effort + cost, one tree) — ✅ carried (cost-ready burn-down, FR-11, §10).
4. Start-cheap-escalate ↔ budget-degrade, one ladder both ways — ⚠️ flattened to two roadmap rows (see G4).
5. AI in the loop, never in the system of record — ✅ carried strongly (PRD §1 "a second bet," §5, §10, glossary Raw Act/Projection).

So 3 of 5 are fully carried, 1 flattened, 1 dropped. The instruction "carry into PRD positioning" was only partially honored.

### S2. Dual-horizon vision — preserved but the *person* horizon is thinner. [SEVERITY: LOW-MEDIUM]
The brief's **Vision** is explicitly two-horizon: "**For the person:** your work, captured from anywhere... you never open a task app; you say what needs doing and watch it burn down." and "**For the ecosystem:** the **agent-addressable work queue of Hexalith**... a Frontier Firm hand any unit of work to the cheapest capable doer." The PRD §1 Vision carries the ecosystem/substrate horizon and the "agent boss" framing (also §7), but the **person horizon** ("watch it burn down," "say what needs doing," "from anywhere") is compressed to a single clause and not given its own paragraph. The evocative end-user promise — the emotional core of the consumer story — is under-weighted relative to the brief, consistent with G1's loss of the end-user *problem*. The brief gives the two horizons equal billing; the PRD gives the ecosystem horizon clear primacy.

### S3. Two primary audiences "both consuming the kernel directly" → effectively one primary audience in v1. [SEVERITY: MEDIUM]
Brief addendum "Resolved" #1 is unambiguous: "Hexalith builders **and** end users — **both primary, both consuming the kernel directly**." The PRD §2.2 reclassifies end users as **Non-Users (v1)**: "v1's direct consumer is the builder." This is **scope-honest** (no channel adapter in v1, so end users *can't* consume it yet) and is the correct v1 reading — but it is a real reframing of a decision the brief flagged as resolved. The PRD does preserve end users as the served-through audience and the UJ-4 horizon, so this is a defensible *temporal* narrowing (primary later, non-user now) rather than a contradiction. Worth making explicit that end users remain a *primary audience of the product*, merely not a *v1 consumer*, so the dual-audience thesis isn't read as abandoned.

### S4. Tenant admins as a served audience — preserved. [SEVERITY: none — confirmed]
Brief lists "Tenant admins — set escalation ladders, authority levels, and AI-spend caps." PRD §2.2 carries them as deferred (Themes 4 & 5). Correct and complete.

### S5. The "thin spine / small durable" identity — strongly preserved, even strengthened. [SEVERITY: none — positive]
The brief's insistence that Works is "thin," "small, durable spine," "not a product," "the bloat factored into surrounding modules" is preserved and *strengthened* by the PRD's counter-metrics SM-C1 ("Don't grow the kernel") and SM-C2 ("Don't over-fit v1 to deferred themes"). This is an example of the PRD adding value faithful to brief spirit. Noted as a positive.

---

## 4. ADDED by PRD that contradicts (or narrows) the brief

### A1. Tree-depth guard (max depth 32) — ADDED; mildly contradicts brief's implied unboundedness. [SEVERITY: LOW]
FR-13 / §14 introduces a configurable max tree depth (default 32). The PRD itself flags this honestly: "The brainstorm states no limit — proposing a guard to bound roll-up cost." The brief's roll-up ("a parent's remaining effort *and* cost are its own plus its descendants'") implies an unbounded tree. This is a reasonable engineering addition, surfaced as an assumption and an open question (§13 Q5) — **not a silent contradiction**. Listed for completeness; the PRD handled it correctly by flagging it.

### A2. Unestimated-item completion rule — ADDED; no brief basis but flagged. [SEVERITY: LOW]
FR-1 / FR-8 add the rule that an unestimated item completes only via explicit complete-without-estimate (not Remaining=0). The brief says only "'done' means remaining = 0" / "Progress is a fact, not a status flag." The PRD's addition is a sensible edge-case resolution, explicitly tagged `[ASSUMPTION]` and raised as Open Question #1. Faithful handling, no hidden contradiction.

### A3. AuthorityLevel ladder `{ Read, Contribute, Coordinate, Administer }` — ADDED; consistent with brief. [SEVERITY: none]
The brief mentions "AuthorityLevel" and "authority levels" but never enumerates them. The PRD proposes a concrete ordered set (FR-19, §14, Q3), explicitly additive-tolerant and flagged for confirmation. Consistent with brief intent; no contradiction.

### A4. "Resumed (transient)" as an explicit status + idempotent resume — ADDED; consistent. [SEVERITY: none]
Brief lists "resume" as a lifecycle step; the PRD formalizes `Resumed` as a transient transition and resume as idempotent (FR-15). Consistent elaboration, flagged as assumption. No contradiction.

**No PRD addition was found that hard-contradicts a brief decision.** All additions are flagged assumptions/open questions — good discipline.

---

## 5. Prioritized fix list (for the finalize step)

| # | Item | Type | Severity | Suggested action |
|---|------|------|----------|------------------|
| 1 | End-user problem dropped from §2 (only builder JTBD remains) | Gap (G1) | HIGH | Add the end-user problem ("work scatters into inboxes / never captured") to §2 or a Problem section; mark its *solution* as the Theme-3 horizon, but state the problem now. |
| 2 | "Omnichannel from day one" silently became "no v1 adapters" | Distortion (D1) | HIGH | Add one line reconciling brief's "from day one" → "channel *seam* day one, adapters Theme 3." Prevents perceived reversal. |
| 3 | Two of four brief Product success signals vanished from §11 | Distortion (D2) | MEDIUM | Note in §11 that capture-in-seconds and external-one-tap are deferred product signals (cross-ref UJ-4). |
| 4 | "Two primary audiences, both direct consumers" → end users now Non-User | Spirit (S3) | MEDIUM | Clarify end users are a *primary product audience*, not a *v1 consumer* — dual-audience thesis intact. |
| 5 | Only 3 of 5 keystone breakthroughs carried (triple-duty dropped, ladder-both-ways flattened) | Gap/Spirit (G3,G4,S1) | MEDIUM | Add answer-contract triple-duty and the one-ladder-both-ways insight to §12 framing (still deferred). |
| 6 | "No moat / coherence is the advantage" honesty stance dropped | Spirit (G5) | MEDIUM | Restore the explicit "we don't claim a moat; the advantage is coherence" stance in §7. |
| 7 | Person-horizon of the vision thinner than ecosystem-horizon | Spirit (S2) | LOW-MED | Give the end-user vision its own clause/sentence in §1 to match brief's equal billing. |
| 8 | External-party-as-peer-executor texture under-weighted | Distortion (D4) | LOW-MED | One sentence in §4.5 naming the external-by-email peer as the brief's headline novelty. |

**Bottom line:** No silent hard contradictions; deferred-theme mapping is excellent. The real losses are (1) the **end-user half of the problem/audience/vision** systematically under-weighted in favor of the builder/infrastructure framing, and (2) the **"omnichannel from day one"** language reinterpreted as deferral without an explicit reconciliation note. Both are fixable with short additions; neither requires rescoping v1.
