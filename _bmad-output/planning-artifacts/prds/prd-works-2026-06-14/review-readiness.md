---
title: "Hexalith.Works PRD — Architecture-Readiness Review"
status: review
created: 2026-06-14
reviewer: "Architecture-Readiness Reviewer"
target: "prd.md + addendum.md (prd-works-2026-06-14)"
---

# Architecture-Readiness Review — Hexalith.Works PRD

## Overall Verdict: **READY-WITH-FIXES**

The PRD is unusually disciplined: it restates the substrate constraints accurately, anchors a verbatim Glossary, ties every FR to "Consequences (testable)", tags assumptions inline and indexes them, and explicitly names the v1 seam each deferred theme depends on. An architect could begin solution design from it. However, there is a cluster of **concrete gaps that would force an architect to guess** — the most consequential being **two missing v1 events for state transitions the FRs explicitly require** (reschedule, and the Assigned→Queued return), an **unspecified cascade rule for cancelling/expiring a parent with live children**, **no concurrency/ordering model on the aggregate**, and a **deferred-seam integrity gap for Theme 4's routing decision record** (the PRD itself flags this as an open question but ships no placeholder). These are fixable inside the PRD without re-architecting; hence ready-with-fixes, not ready.

Counts by severity: **Critical 2 · High 7 · Medium 9 · Low 6** (24 findings).

---

## 1. FR Testability & Unambiguity

### 1.1 [HIGH] FR-9 "Schedule changes emit events" names no event — catalog gap (also a §2 consistency defect)
FR-9 Consequences: *"Schedule changes emit events and update the 'what's next' query ordering."* FR-4 Consequences: *"Priority and Due Date are settable at creation and changeable later (FR-9), each change emitting an event."* The FR-7 catalog has **`ReEstimated`** (estimate) but **no event for a Priority/Due-Date change** (no `WorkItemRescheduled` / `ScheduleChanged`). An architect cannot implement "each change emitting an event" without inventing an event name — and since the catalog is declared *"names final for v1"* (FR-7), inventing one later risks an additive-but-unblessed event. **Fix:** add a reschedule event to the FR-7 catalog, or explicitly fold Priority/Due-Date changes into an existing event and say so.

### 1.2 [HIGH] FR-18 "An Assigned item can be returned to Queued" — no requeue event / ambiguous reuse of `WorkItemQueued`
FR-18 Consequences: *"An `Assigned` item can be returned to `Queued` … both as normal transitions."* The only queueing event is `WorkItemQueued`. It is unstated whether the same event represents (a) initial enqueue from `Created`, (b) requeue from `Assigned`, and (c) the requeue side of a `Reject` (FR-10 says Reject "returns the item to Queued or terminal"). One event covering three semantically distinct transitions makes projection/audit logic ambiguous and the transition non-testable as written. **Fix:** state explicitly that `WorkItemQueued` carries the prior state / reason, or split events.

### 1.3 [MEDIUM] FR-1 "canonical identity consistent with the substrate's `{tenant}:{domain}:{aggregateId}` model" — who mints the aggregateId is unspecified
FR-1 says identity is "consistent with" the model and FR-21 maps "IDs → `Hexalith.Commons`." But it is not testable as written *who* generates the aggregateId (caller-supplied vs. Commons-generated) nor whether create is idempotent on a caller-supplied ID. This intersects with the idempotency gap (§6.4). **Fix:** state the ID-origin rule in FR-1's Consequences.

### 1.4 [MEDIUM] FR-8 "completion is never set independently of the burn-down for an estimated item" vs FR-1's complete-without-estimate path — the explicit-complete act has no event and no FR
FR-1 `[ASSUMPTION]` and Open Question 1 posit an "explicit complete-without-estimate" act for unestimated items. FR-8 only specifies completion via Remaining=0. There is **no FR and no catalog event** for the explicit-complete path (it would still be `WorkItemCompleted`, but the *command* and its precondition are unspecified). An unestimated item therefore has **no testable route to `Completed`** in any FR. **Fix:** add the complete-without-estimate command/precondition to FR-8 (or a new FR), even if gated behind confirmation of Open Question 1.

### 1.5 [MEDIUM] FR-20 "claimable/assigned … ordered by Priority then Due Date" vs Consequences "Priority (then earliest Due Date, then creation order)" — and Priority ordering direction undefined
The body says "Priority then Due Date"; the Consequences add "then creation order" (fine), but **the direction/encoding of Priority is never defined** (is higher-numeric = more urgent? is Priority an enum or an int? FR-4/Glossary "Schedule" never says). "Ordered by Priority" is not testable without the comparator. **Fix:** define Priority's type and ordering direction in FR-4 or the Glossary.

### 1.6 [MEDIUM] FR-13 "bounded depth … default max depth = 32" — guard is depth-only; breadth (fan-out) and total subtree size are unbounded
The roll-up cost (and the NFR perf claim, §9) depends on *node count*, not just depth. A depth-32 tree with high fan-out still has unbounded roll-up cost, so the guard does not actually bound the worst case the §9 `[ASSUMPTION]` claims it bounds ("the tree-depth guard FR-13 bounds worst-case roll-up cost"). **Fix:** either bound subtree node count / fan-out, or weaken the §9 claim.

### 1.7 [LOW] FR-15 external-signal resume "delivered through the generic resume port" — the port has no name/FR
FR-15 and FR-22 name `IExpectationResolver` and `IExecutorRouter` as the v1 ports. The "generic resume-by-correlation-ID port" (described in §4.4 prose and FR-15) is **never named as a port in FR-22** nor listed among the ports. See §5.1 (this is the v1 seam Theme 3 depends on, so it must be a first-class v1 artifact). **Fix:** name it (e.g., `IResumeSignalReceiver` / inbound command) and add it to FR-22.

### 1.8 [LOW] FR-19 AuthorityLevel "carried-not-enforced" — testable, but FR-9/FR-17 say "authorized Executor" / "eligible Executor"
FR-9 ("An authorized Executor"), FR-17/FR-18 ("eligible Executor"), FR-10 ("authority-gated") imply enforcement, while FR-19 says authority is **carried, not enforced** in v1. If nothing is enforced, "authorized"/"eligible"/"authority-gated" are not testable predicates in v1. **Fix:** state that these qualifiers are descriptive-only in v1 (no gating), consistent with FR-19, or name the minimal check that *is* enforced.

---

## 2. Internal Consistency

### 2.1 [HIGH] State machine (FR-6) omits `Queued`/`Resumed` reachability detail and contradicts FR-18's bidirectional Assigned↔Queued
FR-6's diagram: `Created → Assigned | Queued → InProgress → Suspended → (Resumed →) InProgress → Completed`. This is a **forward-only** sketch. FR-18 requires `Assigned → Queued` (backward) and `Queued → Assigned` (cross), and FR-10's Reject requires `Assigned/InProgress → Queued`. None of these back/cross edges appear in FR-6's machine. The state machine as drawn is **inconsistent with FR-10 and FR-18**. An architect implementing FR-6 literally would reject the transitions FR-18 mandates. **Fix:** give the complete edge list (or a transition table) in FR-6.

### 2.2 [MEDIUM] `Resumed` is both a "Status" (Glossary, FR-6) and "transient" — is it ever a persisted state?
Glossary lists `Resumed` among Status values "(transient)"; FR-6 calls it "a transient transition back into `InProgress`." If transient, a Work Item is **never observed in `Resumed`** and `WorkItemResumed` simply lands the item in `InProgress`. Then `Resumed` is an event/transition, not a Status, yet it sits in the Glossary Status enum. This will confuse projection state-enum design. **Fix:** remove `Resumed` from the Status enum (keep it as an event only) or define exactly when it is persisted.

### 2.3 [MEDIUM] Event ↔ state coverage: `ChildSpawned`, `ProgressReported`, `ReEstimated` are non-transition events; `WorkItemClaimed` transition target is implicit
Of the 13 events, three (`ChildSpawned`, `ProgressReported`, `ReEstimated`) do not change Status (correct, they are progress/structure facts) — good. But `WorkItemClaimed` (FR-18) "transitioning toward `InProgress`" is vague: does claim go `Queued → InProgress` directly, or `Queued → Assigned → InProgress`? FR-6 has no `Claimed` edge. All 13 events are otherwise reachable/used (verified: Created/Assigned/Queued/Claimed/Suspended/Resumed/Completed/Cancelled/Rejected/Expired map to transitions; Progress/ReEstimated/ChildSpawned are facts). **Fix:** state the exact post-`Claimed` Status.

### 2.4 [MEDIUM] Glossary "Saga" and "Domain Event"/"Raw Act" terms — `Saga` is defined but never used in any FR; potential dead term
Glossary defines **Saga** ("the spawn→suspend→resume continuation pattern"). It appears in §4.4's *title*/prose ("durable saga") and the Vision, but **no FR uses the term "Saga"** as a requirement noun. Minor, but the §3 preamble claims "Glossary terms used verbatim … throughout." Either acceptable (descriptive term) or tighten. Conversely, domain nouns **"answer-space"** (§10 NL-is-data) and **"meter"** (addendum) appear without Glossary entries. **Fix:** drop Saga from the strict-use claim or use it in FR-14/16; add "Meter" if it becomes contract vocabulary.

### 2.5 [MEDIUM] Roll-Up definition mismatch: Glossary/FR-11 say "own + recursive Remaining of all descendants"; FR-11 Consequence says "Σ(rolled Remaining of direct children)"
Glossary Roll-Up: *"parent's Remaining … equals its own plus the **recursive Remaining of all descendants**."* FR-11 Consequence: *"rolled Remaining = own Remaining + **Σ(rolled Remaining of direct children)**, recursively."* These are mathematically equivalent **only if** "rolled Remaining of direct children" is itself recursive (it is, per "recursively"). This is fine but the two phrasings will read as two formulas to an implementer. **Fix:** state one canonical recurrence and reference it everywhere (the addendum's `rolledRemaining(item) = item.Remaining + Σ rolledRemaining(child)` is the cleanest — point FR-11 and the Glossary at it).

### 2.6 [LOW] FR-12 heterogeneous-unit roll-up vs FR-11 "each Work Item exposes … its subtree-rolled Remaining" (a single number) — surface-shape contradiction
FR-11 says each item exposes "its subtree-rolled Remaining" (singular). FR-12 says mixed-unit subtrees expose "per-Unit subtotals rather than a coerced single figure." So the roll-up read model's shape is *sometimes a scalar, sometimes a map*. The projection contract (a v1 public surface, §8) is therefore polymorphic and underspecified. **Fix:** define the roll-up read model as always a per-Unit map (degenerating to one entry for same-unit subtrees) so the surface is uniform.

### 2.7 [LOW] Open Questions duplicate Assumptions without a resolution owner/date
All 8 Open Questions restate `[ASSUMPTION]`s already in §14 (e.g., OQ1=FR-1 assumption, OQ5=depth-32, OQ6=eventual consistency). Several are **load-bearing for architecture** (OQ6 consistency model, OQ4 reject/expire reactivation, OQ8 routing placeholder). Leaving them open means the architect inherits product decisions. Not a contradiction, but a readiness risk: **at least OQ1, OQ4, OQ6, OQ8 should be resolved before architecture, not during.**

---

## 3. Boundary & Port Soundness

### 3.1 [MEDIUM] Owns-vs-references is coherent, but "Conversation correlation ID" is the only dialogue seam and no FR records *when* it is set
FR-21 `[ASSUMPTION]`: "v1 references a Conversation by ID; it does not implement comment storage." Good and substrate-conformant. But no FR creates/attaches a `ConversationId` to a Work Item (FR-1 lists Obligation/Tenant/Burn-Down/Schedule/parent/binding — **not** ConversationId). The reference value object exists (addendum) but **no v1 FR populates it**, so "the comment narrative and the event stream are two views of one history" (FR-21) has no v1 mechanism. **Fix:** either add ConversationId to FR-1's createable fields or state it is resolved-not-stored in v1.

### 3.2 [MEDIUM] `IExecutorRouter` port: domain-purity claim is sound, but FR-22 says "without any `IExecutorRouter` implementation wired" while FR-18 (push/pull/claim) needs *some* eligibility decision
FR-22: tests pass "without any `IExecutorRouter` implementation wired (routing is deferred; the port exists)." FR-18: a `Queued` item "can be claimed by an eligible Executor." If no router is wired, **who decides eligibility** for a claim in v1? Either claim is unconditional in v1 (anyone may claim) or it needs a default. This is a soundness gap between the port-deferral claim and FR-18's eligibility predicate. **Fix:** state that v1 claim is unconditional (eligibility = Theme 4) — aligns with §1.8.

### 3.3 [LOW] Ports list is incomplete vs the seams the PRD itself relies on
FR-22 enumerates exactly two ports (`IExpectationResolver`, `IExecutorRouter`). But the PRD relies on at least one more domain-owned abstraction in v1: the **generic resume-signal port** (FR-15, §4.4) which is a *built* v1 seam (Theme 3 depends on it). Declaring "the domain depends on `IExpectationResolver` and `IExecutorRouter` as ports" understates the v1 port surface. **Fix:** add the resume port to FR-22 (see §1.7, §5.1).

### 3.4 [LOW] Reference value objects are coherent and sufficient for what is owned; domain-purity claim (FR-22/§9) is internally consistent
No defect — recording as a positive: the owns set (obligation + burn-down + schedule + binding + saga + tree refs + await-condition) and the references (Party/Conversation/Tenant/EventStore/Commons) are clean, and "no LLM/cost/routing/infra type referenced from the domain assembly" (FR-22) is consistent with the port design. The only purity tension is the resume *trigger* delivery (date/timer + external signal) which must originate outside the pure aggregate — see §6.5.

---

## 4. Substrate Conformance

### 4.1 [POSITIVE] The PRD correctly restates the substrate
Verified against `project-context.md`: persist-then-publish (§9, addendum), additive/no-V2 (§8, §14-adjacent, addendum), Dapr-only infra abstraction (§8, addendum), tenant isolation at every layer (§9, with negative-path tests required), pure `Handle`/`Apply` (§9, addendum), EventStore owns envelope / Works returns payloads only (FR-7, §9, addendum), `{tenant}:{domain}:{aggregateId}` (FR-1, §9), past-tense events / imperative no-suffix commands (§8 naming, addendum), Contracts-low-dependency package layout (§8). No restatement errors found. This is a strength.

### 4.2 [HIGH] Roll-Up as a projection vs substrate read-side: PRD ignores the mandated EventStore read-side infrastructure
`project-context.md` (line 56): *"Use EventStore query/projection infrastructure, including `CachingProjectionActor`, ETag actors, and projection notifiers where available, before inventing custom read-side routing."* The PRD specifies the Roll-Up and "what's next" as projections (FR-11, FR-20) and even forbids "re-reading whole streams on each query" (§9), but **never references the EventStore projection actors/notifiers it is required to build on.** An architect could legitimately invent a custom roll-up store, violating the rule. **Fix:** state that the roll-up/what's-next projections must use EventStore's projection/caching/notifier infrastructure.

### 4.3 [MEDIUM] Tenant-scoping of cross-tree roll-up and "what's next" — cross-parent/child tenancy is unstated
§9 mandates tenant isolation at every layer and FR-13 enforces single-parent acyclic trees, but **no FR forbids a parent and child in different tenants.** Since roll-up crosses parent→child edges and FR-20 queries per-tenant, a cross-tenant edge would either leak roll-up across tenants or silently drop. `project-context.md` makes tenant isolation mandatory at aggregate identity and projection keys. **Fix:** add a Consequence to FR-13/FR-16 that parent and child must share a Tenant (cross-tenant spawn rejected).

### 4.4 [MEDIUM] Query-side authorization not addressed (substrate requires it for some queries)
`project-context.md` (lines 59, 80, 143): Tenants/query handlers must apply **query-side authorization/result filtering** — "command-side RBAC and API JWT checks are not enough." FR-20 ("what's next") is a query returning a tenant's items; the PRD only asserts tenant-scoping, not query-side authorization/result filtering. For v1-as-kernel this may be acceptable, but it should be acknowledged as a downstream requirement so it is not silently dropped. **Fix:** note that consumer-facing query authorization is the integrator's/Theme-6's responsibility and that the projection exposes tenant-scoped data only.

### 4.5 [LOW] "ProblemDetails/RFC 9457" (§9) vs substrate "RFC 7807 or RFC 9457" — fine, but no FR produces an error surface in a headless v1
§9 mandates ProblemDetails for errors. v1 is a headless domain kernel whose rejections are `IRejectionEvent`s, not HTTP problem responses. The RFC-9457 requirement only bites at a host/API boundary not built in v1. Not a conflict; flag only so the architect knows the §9 ProblemDetails clause is dormant until a channel/API adapter (Theme 3) exists.

---

## 5. v1/Deferred Seam Integrity

For each deferred theme (§12 table), checking whether the named v1 seam is **actually specified in a v1 FR** (not merely asserted in prose).

### 5.1 [HIGH] Theme 3 seam "Await-Condition; Channel field; `IExpectationResolver`; raw-act events" — three of four are real FRs; the **resume-signal port is asserted but not in any FR**
- `IExpectationResolver` → FR-22 ✔ (no-LLM impl shipped). 
- Await-Condition → FR-5/FR-14 ✔.
- Channel field → FR-17/Glossary ✔ (extensible). 
- **But the external-signal resume path** (the seam UJ-4's "external supplier advances by inbox/magic link" depends on) is the **generic resume-by-correlation-ID port**, which FR-15 *describes* and §4.4 prose names, yet **FR-22 does not declare it as a v1 port** and no FR fixes its command/contract shape. Theme 3's magic-link/email-reply adapter therefore has **no specified v1 seam to plug into** — it would have to add the inbound resume command later, which is additive but currently *unspecified*, risking a non-additive reshape if FR-15's correlation-ID contract is wrong. **Fix:** promote the resume-signal port to FR-22 and fix its correlation-ID payload in FR-15's Consequences. (Mirrors §1.7, §3.3.)

### 5.2 [CRITICAL] Theme 4 seam "explainable decision record (candidates/score/cost/confidence)" — **no v1 event/field placeholder exists, and the PRD knows it (OQ8, §5 NOTE FOR PM)**
§12 says Theme 4 builds an "explainable decision record (candidates/score/cost/confidence)." The §12 seam column lists `IExecutorRouter` port + push/pull states + AuthorityLevel + assignment events — but **none of those carries the decision-record fields.** The PRD flags this exact risk twice: §5 `[NOTE FOR PM: confirm the routing decision record fields … need no v1 event placeholder.]` and OQ8 ("does Theme 4's explainable decision record need any v1 event/field placeholder to stay additive later?"). Because **no v1 event has an additive slot reserved for routing rationale**, attaching the decision record later means either (a) adding a *new* event (additive-OK) or (b) extending `WorkItemAssigned` with rationale fields. (b) is additive-tolerant per substrate rules, so this is *probably* survivable — **but it is unresolved**, and if the decision is that rationale must be correlated to the assignment event atomically, a missing field on `WorkItemAssigned` v1 could force awkward joins. This is the single most important seam-integrity question and it is **left open, not specified.** **Fix:** resolve OQ8 now: either reserve an optional `routingDecision` slot on `WorkItemAssigned`/`WorkItemClaimed` in v1, or explicitly decide the decision record will be a *new* additive event and record that decision so it is not a surprise.

### 5.3 [MEDIUM] Theme 4 seam "push↔pull auto-assignment" relies on FR-18 — but FR-18's requeue/claim events are themselves underspecified (see §1.2, §2.1, §2.3)
The push/pull *states* exist (FR-18), satisfying the seam at the state level. But the **events** that move between them are ambiguous (overloaded `WorkItemQueued`, vague `WorkItemClaimed` target). Theme 4's auto-assignment will consume these events; ambiguity now becomes a Theme-4 integration cost. Lower severity because the seam (states) is technically present. **Fix:** resolve §1.2/§2.3 to harden the seam.

### 5.4 [LOW] Theme 5 seam "cost-ready burn-down + reusable Roll-Up" — genuinely present and well-specified
FR-3/FR-11 + addendum `Meter(Unit, Estimated, Done)` design make Cost a parallel meter reusing roll-up. This seam is real and additive (a second meter is additive data + a second projection). **Strongest seam in the doc.** One caveat: FR-12's mixed-unit subtotaling must generalize to a second meter dimension — confirm the read-model shape (see §2.6) anticipates `(meter, unit) → subtotal`.

### 5.5 [MEDIUM] Theme 6 seam "raw-act event model (actor + timestamp + verbatim payload); idempotency; AuthorityLevel" — raw-act and AuthorityLevel are real FRs; **idempotency is asserted but not mechanized** (see §6.4)
FR-7 (raw act: actor via binding + timestamp via envelope + verbatim payload) ✔; FR-19 (AuthorityLevel carried) ✔. But the idempotency seam Theme 6's single-use-bound-link feature depends on is, per §10 and FR-15 `[ASSUMPTION]`, *"relies on event-sourcing/command dedup; explicit per-act idempotency tokens deferred."* There is **no v1 field for an idempotency/correlation key on inbound acts** (resume/progress). If Theme 6 needs the link's single-use token correlated to the act, and v1 events carry no such field, adding it later is additive-OK — but the FR-15 external-signal correlation ID is the closest thing and its contract is unspecified (§5.1). **Fix:** decide whether v1 inbound commands carry an optional idempotency key; if yes, reserve it now; if no, state explicitly that Theme 6 will add it as a new additive field.

---

## 6. Missing Requirements (an architect would have to guess)

### 6.1 [CRITICAL] Parent cancel/expire cascade to live children is completely unspecified
FR-10 makes `Cancelled`/`Rejected`/`Expired` terminal with "no further progress accepted." FR-5/FR-13/FR-16 build parent→child trees, and FR-15 has parents suspended *awaiting* children. **Nothing states what happens to in-flight children when their parent is Cancelled or Expired**: Are children auto-cancelled? Orphaned (re-parented)? Left running with a dangling parent ref? Does the parent's roll-up still include a child of a cancelled parent? This is a core aggregate-tree behavior with audit, roll-up, and saga implications, and an architect cannot proceed without it. (Open Question 4 covers reject/expire *reactivation* but **not the cascade direction parent→children**.) **Fix:** add an FR (or FR-10 Consequence) specifying the cascade: e.g., cancelling a parent cancels/detaches its open subtree, with the event(s) emitted, and the roll-up effect.

### 6.2 [HIGH] No concurrency / optimistic-conflict model on the aggregate
The PRD never states how concurrent commands on one Work Item are reconciled (expected-version / ETag / optimistic concurrency), despite the substrate exposing "ETag actors" (project-context.md line 56) and despite obvious races: two Executors claiming the same `Queued` item (FR-18); progress + reject arriving together; a date-resume and a child-resume racing (the PRD even calls out this race in UJ-3 but only resolves *which trigger wins by time*, not *concurrent command serialization*). Event-sourced aggregates need a stated concurrency rule. **Fix:** state the optimistic-concurrency/expected-version contract for commands and the claim race resolution (first-writer-wins → others get `IRejectionEvent`).

### 6.3 [HIGH] Event ordering / causal guarantees for roll-up across aggregates are unstated
Roll-Up (FR-11) is a projection spanning *multiple aggregates* (parent + descendants), fed by persist-then-publish pub/sub. The PRD says it is eventually consistent (good) but says nothing about **out-of-order or duplicate event delivery** to the roll-up projection (e.g., a child `WorkItemCompleted` arriving before the `ChildSpawned` that registers the child to the parent). For correctness the projection must be order-tolerant/idempotent. **Fix:** require the roll-up projection to be idempotent and tolerant of out-of-order/at-least-once delivery (consistent with §10 idempotency intent and §6.4).

### 6.4 [HIGH] Idempotency is asserted but not mechanized (no key, no dedup scope)
§10 and FR-15 `[ASSUMPTION]` claim resume/progress are idempotent via "event-sourcing/command dedup," but **no FR defines the dedup mechanism**: what key dedups a duplicate `ProgressReported` (a client-supplied command ID? content hash? expected-version?)? FR-15's "resume is idempotent (duplicate trigger = no-op)" is testable only if "duplicate" is defined (same correlation ID? same await-condition already satisfied?). As written, "idempotent" is an aspiration, not a spec. This is also the Theme-6 seam (§5.5). **Fix:** define the idempotency key/scope for inbound acts (resume, progress, claim) — even minimally (e.g., claim is idempotent on PartyId; resume is idempotent on await-condition correlation key; progress dedups on command ID if supplied).

### 6.5 [MEDIUM] The date/timer resume trigger source is unspecified — and it cannot live in the pure aggregate
FR-15 "a parked target date arriving resumes the item." The pure aggregate (`Handle`) cannot observe wall-clock time (and §9 + substrate forbid infra in the domain; testing rules forbid wall-clock sleeps). **What component fires the date trigger** (a Dapr actor reminder/timer? a scheduler projection?) is unspecified. This is an architecture concern, but the PRD should at least name it as a required adapter/seam so it is not missed and so the aggregate's `Handle` receives a "DateReached" command rather than reading time. **Fix:** state that timer/date resume is delivered as an inbound command from a host-side scheduler (not computed in `Handle`), preserving purity.

### 6.6 [MEDIUM] Projection rebuild / replay is never mentioned
The roll-up and "what's next" are projections (FR-11, FR-20) over an append-only stream. Standard event-sourcing readiness requires a stated **rebuild-from-zero / catch-up** story (cold start, projection schema change, poison event). The PRD's "no V2 events / always deserializable" rule (§8) makes replay possible, but **no FR requires the projections to be rebuildable.** **Fix:** add a Consequence (FR-11/FR-20) that projections are fully rebuildable by replay and that rebuild is part of the test harness (ties to SM-2).

### 6.7 [MEDIUM] Unit immutability / change policy is unspecified
FR-3 sets Unit per-item; FR-9 allows re-estimation. **Can an item's Unit change after creation?** If yes, Done/Estimated already recorded are in the old unit (roll-up corruption); if no, it must be stated. FR-12's mixed-unit handling depends on Unit being stable per item. **Fix:** state Unit is immutable after first estimate (or define a unit-change event and its roll-up consequence).

### 6.8 [MEDIUM] `ProgressReported` over-completion and negative/zero deltas
FR-8 clamps Remaining at 0 on the Done delta. Unspecified: are **negative deltas** (correction) allowed? Zero-delta progress? Progress after Remaining already 0 but before `WorkItemCompleted` is observed (race with §6.2)? Re-opening a Completed item is implicitly forbidden (FR-6 illegal transition) but "correct an over-report" has no path except `ReEstimated`. **Fix:** state the allowed delta domain for `ProgressReported` and that corrections go via `ReEstimated`.

### 6.9 [LOW] Await-Condition multiplicity — FR-5 says "one Await-Condition" but UJ-3 parks on child-completion *and* a date simultaneously
FR-5: "may hold **one** Await-Condition while Suspended." UJ-3 edge case: "if the item is **also** parked on a date that arrives first." A single Await-Condition slot cannot represent "child OR date, whichever first." Either the Await-Condition is a composite (set with first-wins) or UJ-3's edge case is unbuildable under FR-5. **Fix:** allow an Await-Condition to be a set / first-of, or restrict UJ-3 to one condition.

### 6.10 [LOW] No FR for detaching/moving a child (re-parenting) though cancel-cascade (§6.1) and tree edits imply it
If §6.1's cascade chooses "detach/orphan" over "cancel," a re-parent operation is needed; none exists. Even absent cascade, builders may need to move a child. v1 may legitimately exclude this, but it should be an explicit non-goal if so. **Fix:** add to §5 Non-Goals or add an FR.

---

## Consolidated Findings Table

| # | Sev | Area | Finding (FR/§) |
|---|-----|------|----------------|
| 6.1 | CRITICAL | Missing req | Parent cancel/expire → live-children cascade unspecified (FR-10/FR-13/FR-16) |
| 5.2 | CRITICAL | Seam integrity | Theme 4 routing decision record has no v1 event/field placeholder (OQ8, §5 NOTE) |
| 1.1 | HIGH | Testability | FR-9 "schedule changes emit events" — no reschedule event in FR-7 catalog |
| 1.2 | HIGH | Testability | FR-18 Assigned→Queued requeue — no event / overloaded `WorkItemQueued` |
| 2.1 | HIGH | Consistency | FR-6 forward-only state machine contradicts FR-18/FR-10 back/cross edges |
| 4.2 | HIGH | Substrate | Roll-Up/what's-next projections ignore mandated EventStore projection infra |
| 5.1 | HIGH | Seam integrity | Theme 3 resume-signal port asserted but not an FR-22 port (contract unspecified) |
| 6.2 | HIGH | Missing req | No concurrency/optimistic-conflict model (claim race, etc.) |
| 6.3 | HIGH | Missing req | No event-ordering/idempotency guarantee for cross-aggregate roll-up |
| 6.4 | HIGH | Missing req | Idempotency asserted but no key/scope mechanized (§10, FR-15) |
| 1.3 | MED | Testability | FR-1 aggregateId origin (caller vs Commons) unspecified |
| 1.4 | MED | Testability | Complete-without-estimate path has no FR/event |
| 1.5 | MED | Testability | FR-20/FR-4 Priority type & ordering direction undefined |
| 1.6 | MED | Testability | FR-13 depth guard doesn't bound fan-out/node count (breaks §9 claim) |
| 2.2 | MED | Consistency | `Resumed` is both a Status enum value and "transient" |
| 2.3 | MED | Consistency | `WorkItemClaimed` post-state vague; no `Claimed` edge in FR-6 |
| 2.4 | MED | Consistency | `Saga` defined-but-unused in FRs; `meter`/`answer-space` undefined |
| 2.5 | MED | Consistency | Two phrasings of the roll-up recurrence (Glossary vs FR-11) |
| 3.1 | MED | Boundary | No v1 FR populates the Conversation correlation ID |
| 3.2 | MED | Boundary | Claim "eligibility" needs router, but router deferred/unwired |
| 4.3 | MED | Substrate | Cross-tenant parent/child not forbidden (tenant-leak risk in roll-up) |
| 4.4 | MED | Substrate | Query-side authorization (mandated) not addressed for FR-20 |
| 5.3 | MED | Seam integrity | Push↔pull seam present but its events underspecified |
| 5.5 | MED | Seam integrity | Theme 6 idempotency seam asserted, no v1 key |
| 6.5 | MED | Missing req | Date/timer resume trigger source unspecified (must not be in `Handle`) |
| 6.6 | MED | Missing req | Projection rebuild/replay never required |
| 6.7 | MED | Missing req | Unit immutability/change policy unspecified |
| 6.8 | MED | Missing req | `ProgressReported` negative/zero/over-complete deltas undefined |
| 1.7 | LOW | Testability | Generic resume port unnamed |
| 1.8 | LOW | Testability | "authorized/eligible/authority-gated" not testable under carried-not-enforced |
| 2.6 | LOW | Consistency | Roll-up read model scalar-vs-map polymorphic (FR-11 vs FR-12) |
| 2.7 | LOW | Consistency | Open Questions duplicate Assumptions; load-bearing ones left open |
| 3.3 | LOW | Boundary | FR-22 port list incomplete (missing resume port) |
| 3.4 | LOW (POS) | Boundary | Owns/references set & purity claim coherent — positive |
| 4.1 | — (POS) | Substrate | Substrate restatement is accurate — positive |
| 4.5 | LOW | Substrate | RFC-9457 clause dormant in headless v1 |
| 5.4 | LOW (POS) | Seam integrity | Theme 5 cost-meter seam genuinely present — strongest seam |
| 6.9 | LOW | Missing req | One Await-Condition (FR-5) vs UJ-3 "child OR date" simultaneous park |
| 6.10 | LOW | Missing req | No re-parent/detach operation (ties to §6.1) |

---

## Recommended Pre-Architecture Fix Set (minimum to reach "ready")

1. **Resolve the parent-cancel/expire cascade** (6.1) — add the rule + event(s) + roll-up effect.
2. **Resolve OQ8 / Theme-4 decision-record seam** (5.2) — reserve an optional slot on assignment events or formally choose "new additive event."
3. **Complete the FR-6 transition table** to include FR-18/FR-10 back/cross edges (2.1), and define the post-`Claimed` state (2.3).
4. **Add the missing reschedule event** to FR-7 and disambiguate `WorkItemQueued`'s three uses (1.1, 1.2).
5. **State the concurrency model, event-ordering/idempotency guarantees, and the idempotency key** (6.2, 6.3, 6.4) — the cluster an event-sourced aggregate cannot be built without.
6. **Bind the roll-up/what's-next projections to EventStore projection infrastructure** and require rebuild-by-replay (4.2, 6.6).
7. **Name the resume-signal port + its correlation/idempotency contract** in FR-22/FR-15 (5.1, 1.7, 6.5).
8. **Forbid cross-tenant parent/child** (4.3) and **fix the roll-up read-model shape** as a per-(meter,unit) map (2.6, 5.4).

With these eight resolved, the remaining medium/low findings are normal solution-design latitude and the PRD would be **ready**.
