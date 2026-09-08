# Validation Report — Hexalith.Works

- **PRD:** `_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md` (+ `addendum.md`)
- **Rubric:** `.agents/skills/bmad-prd/assets/prd-validation-checklist.md`
- **Run at:** 2026-09-08T12:25:00+02:00
- **Grade:** Fair

## Overall verdict

This is a genuinely good capability-spec PRD: the thesis is sharp and product-specific ("everything is a Party"; raw act canonical, interpretation a projection), scope is tightened honestly against its own brief, every inferred decision is tagged and indexed, and the two September amendments (FR-20 tie-break, FR-21 `LinkConversation`) are exemplary in precision. What is at risk is Done-ness at the seams between FRs: the lifecycle has no transition or event for an `Assigned` item starting work, FR-15 and §10 disagree on whether a non-matching resume is a rejection or a no-op, the actor of non-executor acts (assign, cancel, re-estimate) is not captured despite being the second bet, and an undefined "type" concept backs two configuration knobs. With Epics 1–4 built, these gaps have almost certainly been resolved in code without the PRD recording the answer — the practical risk is the PRD ceasing to be the system of record for the contract it claims to own.

The two extra reviewers sharpen that risk into a verdict. The adversarial pass argues that the purity rules (a clock-free, single-aggregate `Handle`) are incompatible with three behaviours the PRD also mandates — spawn-with-parent-link, child-completion resume, cascade-cancel — and that the single Executor Binding cannot hold the approver/observer roles the roadmap depends on; it rates both critical. The downstream-drift pass confirms the first of those was in fact answered on 2026-09-06 by the architecture (AD-21 Work-Tree Registry, a second aggregate that takes edge ownership and the public spawn entry away from `WorkItem`) with no PRD amendment and no decision-log entry, and that all six §13 Open Questions are already resolved by AD decisions the PRD does not cite. Grade is **Fair** on the rubric (all dimensions strong/adequate, two high findings). If the two adversarial criticals are accepted as written during triage, the grading rule yields **Poor**; if the AD-21 correct-course pass is run, the second critical collapses into the drift finding. Overall drift verdict: **material drift** — not superseded, but the PRD lags the architecture on one structural decision and its audit trail stops in June.

## Dimension verdicts

- Decision-readiness — adequate
- Substance over theater — strong
- Strategic coherence — strong
- Done-ness clarity — adequate
- Scope honesty — strong
- Downstream usability — strong
- Shape fit — strong

## Findings by severity

Counts after merging duplicates across reviewers: critical 2 · high 8 · medium 13 · low 15 (38).

### Critical (2)

**[Adversarial]** — The keystone binding cannot hold the roles the roadmap needs, and SM-3 cannot fail in v1 (§4.5 FR-17/FR-19, §11 SM-3, §12)
FR-17 gives a Work Item "its single Executor Binding"; FR-19's proposed set includes `Read = await/observe` and `Administer = approve spend, set caps, cancel`. A single slot cannot carry a Contributor doing the work and an Administer approving its spend, an observer, or an escalation ladder's next rung — exactly the Theme 4/6 shapes §12 claims this seam enables. "No domain code branches on executor kind" is trivially true of a kernel that stores three fields and never reads them.
Fix: Make the binding a set (one primary Executor plus role bindings) now, or state explicitly that approvers/observers are not bindings and name where Theme 6 will put them. Rewrite SM-3 to something that can fail (e.g. "adding a fourth Channel value requires zero changes in `Server`/`Projections`").

**[Adversarial]** — Three cross-aggregate behaviours are required but have no owner under the purity rules (§4.4 FR-15/FR-16, §4.2 FR-10 cascade, §4.3 FR-13, §9 Domain purity, §4.7 FR-24)
`Handle` "never reads a clock or an outside system," yet FR-16 creates a child and emits on the parent; FR-15 has a child's `WorkItemCompleted` raise a resume command to a parent; FR-10 cascades cancel to descendants; FR-13 rejects cycles and cross-tenant links, which requires reading the other aggregate. UJ-3 attributes this to "the engine," the addendum to "internally," and FR-24 forbids Works from owning "subscription plumbing." Two teams will build this differently and both claim compliance. Synthesis note: the architecture answered this on 2026-09-06 with AD-21 without amending the PRD.
Fix: Add an FR naming the process-manager/saga component, its owning package, and its consistency guarantees (write order on spawn; synchronous vs eventual cascade; child behaviour after parent is terminal but before cascade lands). In practice: run the AD-21 correct-course into the PRD.

### High (8)

**[Done-ness · Adversarial]** — No transition or event for `Assigned → InProgress`; Reject semantics under-specified (FR-6, FR-7, FR-10 Reject, FR-18)
The catalog has `WorkItemClaimed` for `Queued → InProgress` but nothing for a pushed item starting; whether `ProgressReported` is legal in `Created`/`Assigned`/`Queued` is unstated. `Rejected` is listed as terminal yet the default `WorkItemRejected` outcome is Status `Queued`; legal from-states and whether requeue-by-reject also emits `WorkItemQueued` are unstated.
Fix: Publish the full legal-transition table (from-state × command → to-state/event) in FR-6; add `WorkItemStarted` or state that the first `ProgressReported` on an `Assigned` item is the start act; split Reject into a requeue outcome and a terminal outcome.

**[Done-ness · Adversarial]** — FR-15 and §10 contradict on non-matching resume (FR-15, §10 Idempotency)
FR-15: "matches no current Await-Condition is a domain rejection"; §10: "no longer matches an Await-Condition is a no-op". The "duplicate … idempotent no-op" branch is undecidable without retained history, and §10's reliance on substrate stream-offset dedup covers event replay, not an at-least-once timer adapter issuing two distinct commands.
Fix: Pick one rule; if the no-op branch is kept, require retained consumed Await-Conditions (or "any resume on a non-`Suspended` item is a no-op"); align §10; state that duplicate-command idempotency is the aggregate's job.

**[Adversarial]** — "Done = Remaining is 0" is three completion paths, and the auto path is irreversible (§3, FR-8/FR-9)
FR-8 auto-completes at Remaining 0; unestimated items complete "only by an explicit complete act"; `ReEstimated` below Done silently completes. An executor who reports the full estimate early is auto-completed into a terminal state and can never re-estimate, contradicting "over-run … native, not errors". Every client will implement "Complete" as a synthetic re-estimate-to-done.
Fix: Decide whether Remaining=0 is a precondition for an explicit `Complete` (recommended) or an auto-trigger; if auto, add `Reopen` or drop "over-run is native"; forbid `ReEstimated` below Done or specify that it completes.

**[Adversarial]** — Roll-Up requirements are mutually underdetermined; the "single number" promise is withdrawn by FR-12 (§1, FR-11/FR-12, SM-2)
FR-12's per-Unit subtotals mean the single number exists only when every descendant shares a Unit, and nothing constrains Unit at spawn. FR-11 demands both "incrementally" and "idempotent under at-least-once, out-of-order delivery"; delta-accumulation fails the second. SM-2 has no quiescence condition, so any failing read is "stale but converging."
Fix: Require per-child last-known Remaining (state-based) storage; name SM-2's quiescence condition; constrain Unit inheritance at spawn or downgrade the Vision claim.

**[Adversarial · Done-ness · Scope honesty · Downstream usability]** — Expire smuggles a scheduler, an undefined "type" concept, and actor-less raw acts into a kernel that ships no adapters (FR-10, FR-15, §4.7, §6.1, §13 OQ-4/OQ-6, §10 Audit)
Who fires Expire? The timer/scheduler adapter is deferred by OQ-4 and excluded by §4.7, yet SM-1 requires date-triggered resume end-to-end and §6.1 lists "date/timer native" in scope. "Per-type TTL" and FR-13's "per tenant/type" reference a Work Item type that exists nowhere in §3/§4.1/FR-1. A timer-fired `WorkItemExpired` or cascaded `WorkItemCancelled` has no acting Party — a hole in the Raw Act model for every system-originated event. A `Suspended` item with a passed Due Date both resumes (FR-5) and expires (FR-10). (Architecture has since bound the adapter as AD-11; the PRD does not say so.)
Fix: Add the timer/scheduler adapter to §6.1 explicitly with an owner; add an `ExpireWorkItem` command and precedence rule; define "type" or replace with "per tenant"; specify how configuration enters `Handle`; define the actor for system-originated events; state whether Expire is legal from `Suspended`.

**[Adversarial · Strategic coherence]** — v1's own scope statement contradicts itself and strands work (§0, §5, §12)
§0: "v1 (the foundation: Themes 1 & 2)." §5: MCP/CLI were filed under Theme 2 and "v1 deliberately defers even these." So v1 ≠ Themes 1 & 2, the deferred Theme-2 items appear in no §12 roadmap row, and Themes 1 & 2 are never defined in the PRD itself.
Fix: Restate scope as "Theme 1 + the kernel subset of Theme 2", define both themes in one clause, and add a roadmap row for the non-LLM command surfaces.

**[Drift]** — Work-Tree Registry (AD-21) relocates tree-edge ownership and the spawn entry point without a PRD amendment (PRD §3, §4.1, FR-5, FR-13, FR-16 ↔ architecture.md AD-21, epics.md Epic 3 / Story 3.2)
PRD §3 defines the Work Item as "the aggregate root" that "owns … optional parent/children"; FR-16 says `ChildSpawned` "emits on the parent." AD-21 binds a tenant-scoped Work-Tree Registry aggregate owning every edge, with "the registry's reserve command" as the public entry and `SpawnChild` restricted to the reactor's workload identity. Stories 3.1/3.2 ACs still make `SpawnChild` caller-facing; architecture line 463 still says "a single aggregate root". A builder can no longer issue `SpawnChild`; a new command/event family enters the v1 contract; SM-C1 is engaged.
Fix: Run a correct-course pass for AD-21 amending §3, §4.1/FR-5, FR-13, FR-16, FR-7, §14; correct architecture.md line 463; update or supersede Stories 3.1/3.2.

**[Drift]** — Decision log has no record of either September amendment (`.decision-log.md` ↔ sprint-change-proposal-2026-09-05 §7, -2026-09-06 §4.1)
The log's last entry is the 2026-06-14 finalize. Git confirms both amendments landed (`7e7ef4e` 2026-09-05; `33a27e2` 2026-09-06), so the PRD changed twice after "final" with zero audit-trail entries; the 09-06 edit travelled in a commit titled "feat: Implement mTLS for Dapr Sentry …".
Fix: Append two dated entries (sections touched, approving proposal, commit hash); adopt the rule that any post-final PRD edit adds a log entry in the same change.

### Medium (13)

**[Decision-readiness]** — v1 authority blast radius unsurfaced (FR-9, FR-10 Cancel/Cascade, FR-18 assumption, FR-19)
"authorized Executor" (FR-9) has no v1 meaning; with cascade-cancel and open claiming, any tenant Party can terminate any subtree. No `[NOTE FOR PM]` marks this.
Fix: State in §10 or FR-19 that all acts are ungated in v1 and cascade is unguarded, with a `[NOTE FOR PM]`; replace "authorized Executor" with "any bound Executor (authority carried-not-enforced)".

**[Decision-readiness · Drift]** — Product semantics filed as architecture mechanism (§13 OQ6 ↔ epics AR-6 vs architecture AD-17)
Negative progress deltas and Unit immutability govern what an Executor may do, not how it is stored. Downstream now contradicts itself (AR-6 "delta ≥ 0" vs AD-17 "delta > 0") and the PRD has no text to arbitrate.
Fix: Decide both in FR-8/FR-3 consequences; leave only the configuration source in §13; fix AR-6.

**[Strategic coherence · Adversarial]** — Bet 2 and rebuildability have no success metric; SMs are a test plan (§1, FR-7, §9, §11)
FR-7's raw-act rule, §9 rebuildability, FR-10 cascade, FR-18 race and FR-20 ordering are unvalidated. SM-4 and SM-C1 have no measure, threshold or reviewer; nothing measures the JTBD (a builder other than the author wiring Works in).
Fix: Add SM-6 "Rebuild from zero reproduces identical read models; no event payload carries a derived value"; cover FR-18/FR-10 in SM-1 or a new SM; add one adoption signal and one shape budget so SM-C1 can fire.

**[Done-ness]** — Acting Party not captured for non-executor acts (FR-7, FR-9, FR-10, FR-17)
"capturing the acting Party … via the binding and the EventStore envelope" fails for Assign, Reassign, Cancel, ReEstimate, Reschedule; "Works does not populate envelope metadata". Bet 2 is not met.
Fix: Require every command to carry `ActorPartyId` and every Raw-Act event to record it; add a negative test.

**[Done-ness]** — Unestimated item's Roll-Up contribution undefined (FR-1, FR-11, FR-12)
Fix: "an unestimated item contributes 0 to rolled Remaining and increments an `UnestimatedDescendants` count exposed alongside the subtotals".

**[Done-ness]** — Creation with an Executor Binding: `Created` or `Assigned`? (FR-1 consequence 2, FR-6 consequence 2)
Fix: "if an Executor Binding is supplied, creation emits `WorkItemCreated` followed by `WorkItemAssigned` and Status is `Assigned`" (or the alternative).

**[Done-ness]** — Performance NFR is an adjective (§9 Performance)
"remain responsive for realistically deep/wide trees" is unfalsifiable; SM-2 needs a fixture size.
Fix: Pin a v1 test-tree shape (e.g. depth 32 × fan-out 50) and a Roll-Up convergence bound, tagged `[ASSUMPTION]`.

**[Downstream usability]** — Undefined "type" backs two configuration knobs (FR-10 Expire, FR-13 depth, §12 Theme 4)
Fix: Add `Type`/`Kind` to the Glossary and FR-1 as an optional discriminator, or change both knobs to "per tenant".

**[Downstream usability · Adversarial · Drift]** — 2026-09-05 amendments unmarked; frontmatter inconsistent; June "all resolved" claims now false (frontmatter, FR-7, FR-20, FR-21, FR-24, §4.7, §13, §14)
Only FR-20 carries an "Amended" note (2026-09-06) while `updated: 2026-09-05`; the larger 09-05 amendment has no inline markers; §14 still says "All confirmed by the user on 2026-06-14". FR-21's first-link-immutable rule is unjustified when binding, schedule and estimate are all mutable.
Fix: Set `updated: 2026-09-06`; add an "Amendment history" block; add inline notes to FR-7, FR-21, FR-24, §4.7; update §14; justify FR-21 immutability or allow relink with `ConversationUnlinked`.

**[Drift]** — §13 Open Questions are all answered by architecture decisions but still listed as open (§13 items 1–6 ↔ AD-02, AD-03, AD-08, AD-11/AD-25, AD-16, AD-17/AD-04)
Fix: Convert §13 into a "Resolved by architecture" table, keeping residual openness only where the register says so (VAL-H07, VAL-H08).

**[Drift]** — Baseline security enforcement (AD-23/AD-24) is v1 scope the PRD says v1 does not build (§5, §6.2, §9 ↔ AD-23, AD-24, AD-20 R10)
PRD §5: "no security-hardening enforcement". Architecture binds OIDC ingress, deny-before-dispatch, Dapr mTLS and authenticated provenance as Story 4.9 acceptance. No PRD NFR covers identity provenance.
Fix: Add a §9 NFR "Identity provenance & trusted origin (platform-owned baseline)" citing AD-23/AD-24; narrow §5 to "no Theme 6 hardening".

**[Drift]** — §14 Assumptions Index holds assumptions the architecture has since invalidated (§14 bullets 5 and 14 ↔ AD-21, AD-02, VAL-H10)
"enforced at spawn time" vs registry-side enforcement; "per-act idempotency tokens deferred" vs VAL-H10 making transport idempotency v1 work.
Fix: Mark both "superseded by AD-21" / "narrowed by VAL-H10"; re-check the FR-7 "closes the event list" bullet.

**[Adversarial]** — The "what's next" query has no defined consumer; the September tiebreak is arbitrary and called a decision (FR-20, FR-4, §13 OQ-2)
One list mixes everyone's `Assigned` items with the shared `Queued` pool; the filter predicate is unnamed; Priority's direction is deferred so FR-4's "sorts last" is untestable.
Fix: Split into per-executor and per-tenant-pool queries (or add an executor parameter); name the filter predicate; commit to Priority ordering direction; record the id-ordinal tiebreak as a product decision.

**[Adversarial]** — The thin core leaks at four visible seams (FR-7, FR-21/FR-22, §3 Obligation, SM-C2)
A note-per-event is a comment store; Obligation's description is unbounded; `IExecutorRouter` and `IExpectationResolver` are dead abstractions in v1 that SM-C2 would call "a negative" elsewhere.
Fix: Bound the description; forbid notes on events or reconcile with FR-21; drop `IExecutorRouter` from v1 and defer `IExpectationResolver` until it has a caller.

### Low (15)

**[Decision-readiness]** — No `[NOTE FOR PM]` callouts anywhere (whole document)
Fix: Promote the §12 "Designed-for tensions" paragraph to `[NOTE FOR PM]` callouts with an owner.

**[Substance]** — SM-5 duplicates SM-3 (§11)
Fix: Fold SM-5 into SM-3, or repoint it at "a Party may change Channel mid-work".

**[Done-ness]** — Channel change mid-work has no FR (§3 Channel, FR-17)
Fix: "changing Channel on the same Party is a reassign that emits `WorkItemAssigned` with the new binding".

**[Done-ness]** — Fate of remaining Await-Conditions after first match unstated (FR-5, FR-15)
Fix: "resume clears all Await-Conditions; later triggers follow the FR-15 non-matching rule".

**[Done-ness]** — `ChildSpawned` vs the child's own `WorkItemCreated` (FR-16)
Fix: State both streams' events and the command shape — fold into the AD-21 correct-course.

**[Done-ness]** — "Reference to an Expectation" is unlocated (FR-1, FR-2, addendum)
Fix: Name the stored field or say "resolved on demand from state".

**[Downstream usability]** — "Done" collides (§3, §4.2, FR-8)
Fix: Rename the invariant to "Completed ⇔ Remaining = 0"; reserve "Done" for the quantity.

**[Shape fit]** — UJ-3 has no protagonist and UJ-4 sits among v1 journeys (§2.3)
Fix: Give UJ-3 a builder or Party actor; move UJ-4 under §12 with a back-reference.

**[Adversarial]** — Why Now argues for the synthesis and v1 ships none of it (§7)
Fix: State the v1-specific timing pressure or move §7 to the brief.

**[Drift]** — Three approved 2026-09-05 sentences did not land verbatim (FR-7, FR-21, §8 ↔ proposal §4.1)
"never stores conversation content", "preserves every existing payload", and "optional domain-focused supporting libraries" were approved but landed looser or not at all.
Fix: Apply the three approved phrases verbatim.

**[Drift]** — Claim-race loser outcome narrowed downstream but PRD §9/FR-18 not amended (↔ epics NFR-3, AR-10; AD-08)
Fix: "the loser receives a domain rejection; if the substrate's bounded conflict retry is exhausted the outcome is an infrastructure failure, never a silent loss."

**[Drift]** — FR-11 recursive phrasing "retired" by AD-06; AD-22 adds an "Unavailable" roll-up state the PRD lacks
Fix: Add an FR-11 consequence allowing "unavailable" during repair windows/rebuild staging, never partial or stale-as-fresh.

**[Drift]** — Unspecified scope: read-model change notifications (no FR ↔ epics UX-DR1, Story 4.4)
Fix: Add a consequence to FR-20/FR-11 or tag UX-DR1 as Theme 3 with no v1 AC.

**[Drift]** — Open governed exception to "tenant isolation at every layer" negotiated without the PRD (§9 ↔ AD-15, VAL-H09)
Fix: Amend §9 when VAL-H09 closes; add a pointer now.

**[Drift]** — FR-7 "names final for v1" will be outgrown by AD-21 contracts
Fix: Fold into the AD-21 correct-course pass.

## Mechanical notes

- Assumptions Index roundtrip: 19 inline `[ASSUMPTION]` tags, all indexed in §14. One §14 entry has no inline tag (FR-5 multiple Await-Conditions) — tag it or remove it.
- ID continuity: FR-1–FR-25, UJ-1–UJ-4, SM-1–SM-5, SM-C1–SM-C2 contiguous and unique. §6.2 "(Resolves draft OQ-8.)" is a dangling reference — OQ-8 no longer exists.
- Cross-references: all §/FR/SM references resolve; the only semantic mismatch is FR-15 ↔ §10.
- Glossary drift: 21 lowercase occurrences (`burn-down`, `roll-up`, `await-condition`, `executor binding`) against §3's verbatim rule; "meter" used but not defined; "engine" (UJ-3, FR-15) undefined and misleading; "type" undefined; "Done" collision; "Themes 1 & 2" undefined.
- UJ protagonists: UJ-1 builder; UJ-2 service Party; UJ-3 none; UJ-4 Mary (deferred).
- Required sections present; missing an amendment log. "*Working title — confirm.*" still under the H1 of a `status: final` document.
- Frontmatter: `updated: 2026-09-05` vs FR-20 "Amended 2026-09-06"; `status: final` through two amendments.
- Addendum: package layout includes `Client (…)` which PRD §8 omits; port sketches and `LinkConversation` semantics otherwise match.

## Reviewer files

- `review-rubric.md`
- `review-adversarial-general.md`
- `review-downstream-drift.md`
