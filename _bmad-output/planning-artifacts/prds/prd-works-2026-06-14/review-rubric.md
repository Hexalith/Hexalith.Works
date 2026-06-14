# PRD Quality Review — Hexalith.Works (v1, Themes 1 & 2)

## Overall verdict

This is a strong, unusually disciplined developer-kernel PRD. It has a real thesis ("everything is a Party" + "AI in the loop, never in the system-of-record"), every feature traces to that thesis, and the FRs carry testable consequences that an engineer could turn into xUnit assertions tomorrow. The build-signal acceptance model (§11) is the right shape for an event-sourced domain kernel and is honestly counter-balanced (SM-C1/SM-C2). What is at risk is small and downstream-mechanical: one genuine unresolved decision is parked in an Open Question that some FRs already quietly depend on (unestimated completion, OQ-1/FR-1/FR-8), and a few Glossary terms (Effort/Cost meter, "Resumed" transient status) drift between definition and use. None of this blocks the architecture phase; all of it is cheap to close.

## Decision-readiness — strong

A decision-maker can act on this. Decisions are stated as decisions, not buried: the scope cut to Themes 1 & 2 is asserted with a date and a rationale (§0, "Scope decision (confirmed 2026-06-14)"), the keystone bet is named and defended ("The defining bet is 'everything is a Party'", §1), and the second bet ("AI in the loop but never in the system-of-record", §1) is given a structural consequence (raw act is canonical; interpretation is a Projection). Trade-offs name what was given up — the tree-depth guard (FR-13) explicitly overrides the brainstorm ("The brainstorm states no limit — proposing a guard to bound roll-up cost"), and SM-C1 admits the central tension of any kernel ("Don't grow the kernel … capability … migrating *out* of Works is success, not regression").

Open Questions are mostly genuinely open (AuthorityLevel ladder OQ-3, rejection/expiry OQ-4, roll-up consistency OQ-6). The one `[NOTE FOR PM]` (§6.2) sits at a real tension — whether Theme 4's routing decision-record needs a v1 event placeholder to stay additive — which is exactly where the callout earns its place, because getting it wrong later costs a non-additive event.

### Findings
- **medium** OQ-1 is not actually open — it is a decision FR-1/FR-8 already encode (§13 #1, §4.1 FR-1, §4.2 FR-8) — The PRD asserts the rule as an `[ASSUMPTION]` inside FR-1 ("an unestimated item completes only by explicit complete-without-estimate, not by the Remaining=0 rule") and FR-8 builds on it ("completion is never set independently of the burn-down for an estimated item" — note the load-bearing qualifier *estimated*). Yet OQ-1 re-asks it as if undecided. This is the one place the rubric warns against: a rhetorical question with the answer already in the FR. The risk is that downstream story creation treats it as blocked. *Fix:* Either promote the assumption to a stated decision and demote OQ-1 to "confirm" wording, or, if it is genuinely undecided, mark FR-1/FR-8 as provisional pending OQ-1 — do not have it both ways.

## Substance over theater — strong

Very little furniture here. There are no personas (correct for a headless kernel; JTBD statements in §2.1 stand in and each one drives an FR cluster). The Vision (§1) could not be swapped into another PRD — it names the specific aggregate behaviors (burn-down to zero, durable-saga spawn/suspend, parent→child roll-up to "a single number") and the specific architectural bet. The "Why Now" section (§7) is the one place to watch: it leans on external macro framing ("agent boss"/"Frontier Firm", "~62% of organizations") that is genuinely brief-sourced, and it earns its keep by landing on a product-specific claim ("the whitespace is the synthesis — one audited object where the backlog, the durable saga, and the effort+cost ledger are the same thing") rather than generic AI-market boilerplate.

NFRs (§9) avoid the usual theater: instead of "must be scalable/secure", they pin product-specific invariants (persist-then-publish, `Handle` pure, rejections-are-events, `{tenant}:{domain}:{aggregateId}` keying, "never log event payloads"). The one soft spot is performance, handled honestly below.

### Findings
- **low** Performance NFR is qualitative by design but the adjective is unbounded (§9, "Performance (qualitative for v1)") — "must remain responsive for realistically deep/wide trees" is exactly the kind of adjective the rubric flags, but here it is consciously deferred with a named mitigation (FR-13 depth guard) and an `[ASSUMPTION]` that numeric budgets are deferred. Acceptable for a build-signal v1; noted only so the architecture phase knows it owns the threshold. *Fix:* none required for v1; carry "define roll-up/what's-next latency budget" into the architecture phase's NFR list.

## Strategic coherence — strong

The PRD has a clear thesis and the features serve a unified arc rather than a backlog. The arc is: prove the spine so the deferred themes can be assembled on top without reshaping the core (§1, §0). Feature ordering follows the thesis, not ease — the keystone (Executor Binding, §4.5) is presented as "the keystone and the single pluggable seam", and the roll-up "one number" (§4.3) is tied directly to a JTBD ("one rolled-up number for the remaining effort of a whole work tree", §2.1).

Success Metrics validate the thesis rather than measuring activity: SM-3 ("Zero branching on executor kind") directly tests the "everything is a Party" bet, and SM-4 ("Pure domain assembly") tests the thin-core bet. Counter-metrics exist and are pointed (SM-C1 counterbalances the kernel-growth temptation that SM-1 itself creates — a sophisticated, honest move). The §12 Roadmap table is the coherence backbone: every deferred theme names the exact v1 seam it builds on, so the v1 scope is legible as "the minimum spine that makes 3–6 additive."

MVP scope kind is correctly a *platform/capability* spec, and the scope logic matches (in-scope = the aggregate + its events + the seams; out-of-scope = everything that consumes a seam).

## Done-ness clarity — strong

This is the dimension the PRD invests in most, and it holds. Every FR (FR-1 through FR-25) carries a "Consequences (testable)" block, and the consequences are genuinely verifiable, not restated intent. Examples worth citing: FR-6 specifies illegal transitions are "rejected as domain rejections (`IRejectionEvent`), not exceptions"; FR-8 gives the arithmetic ("`ProgressReported` decreases Remaining by the reported Done delta (clamped at 0)"); FR-11 states the roll-up formula precisely ("rolled Remaining = own Remaining + Σ(rolled Remaining of direct children), recursively"); FR-22 gives a compile-time acceptance ("The domain assembly compiles and all v1 tests pass with only the no-LLM `IExpectationResolver` and without any `IExecutorRouter` implementation wired"). The event catalog is enumerated and frozen-for-v1 (FR-7), which removes a major downstream ambiguity.

The SMs (§11) cross-reference the FRs they validate, closing the loop from acceptance back to requirement. I found no "handles X gracefully" / "user-friendly" hand-waving in the FRs themselves.

### Findings
- **medium** FR-10 abnormal-termination semantics are an `[ASSUMPTION]` with "Exact reactivation rules deferred" — done-ness is partial (§4.2 FR-10, §13 #4) — The happy distinctions are testable (Cancel/Reject/Expire are terminal; "no further progress is accepted"), but Reject's outcome is ambiguous: "returns the item to Queued or terminal per caller policy" gives an engineer two valid behaviors with no decision between them, and Expire conflates "Due Date or a configured TTL" without saying which (or both) v1 implements. *Fix:* For v1, pin Reject's default outcome (Queued vs terminal) and state whether Expire is Due-Date-driven, TTL-driven, or both; leave only reactivation deferred.
- **low** FR-13 depth guard is testable but its bound is a proposed default still under OQ-5 (§4.3 FR-13, §13 #5) — "default max depth = 32; configurable" is testable as written, but OQ-5 still asks whether to bound at all. Minor: if OQ-5 resolves to "unbounded," FR-13's third consequence ("exceeding it is rejected") becomes void. *Fix:* resolve OQ-5 before the guard is implemented, or the test is built against a decision that may be reversed.

## Scope honesty — strong

Omissions are explicit and do real work. §5 Non-Goals is substantive, not perfunctory — it states what Works is *not* (task database, BPMN engine, AI-in-system-of-record) and lists every deferred capability with its theme. §6.2 Out-of-Scope goes further and names, per deferred item, the v1 seam already laid ("Seam laid: `IExpectationResolver`, Await-Condition, Channel") — this is the rare scope section that proves the deferral is safe rather than just asserting it. §2.2 Non-Users is an additional, well-judged honesty layer (end-users-via-channel, tenant admins, auditors all explicitly out for v1).

Open-items density is appropriate to the stakes. The 13 `[ASSUMPTION]` tags + 8 Open Questions + 1 `[NOTE FOR PM]` is high in absolute terms, but this is a Fast-path draft with assumptions accepted by the user, and the count is concentrated on genuinely contestable design choices (AuthorityLevel ladder, consistency model, unit-conversion policy, depth bound) rather than scattered to pad thoroughness. For a *spec-the-spine* PRD that explicitly hands technical-how to the architecture phase, this density is defensible. The one caveat is the OQ-1 double-counting noted under Decision-readiness, where an "open" item hides a decision already made.

### Findings
- **low** Assumptions Index (§14) collapses two distinct inline tags into one entry (§14 line "§4.1 FR-5 / §4.3 FR-13") — FR-5's assumption (single-parent acyclic enforced at spawn) and FR-13's (default max depth 32) are merged into one index line, so the roundtrip is 13 inline tags → 12 visually distinct index lines. Both inline tags are represented, so nothing is lost, but a mechanical roundtrip check will flag a count mismatch. *Fix:* split into two index entries to keep one-inline-tag-to-one-index-line.

## Downstream usability — strong (this PRD is chain-top: feeds architecture → stories)

This PRD is explicitly written to be source-extracted (§0: "Vocabulary is anchored in §3 Glossary and used verbatim throughout"), and it largely delivers. FR/UJ/SM IDs are contiguous and unique (FR-1…FR-25, UJ-1…UJ-4, SM-1…SM-5 + SM-C1/C2). Cross-references resolve (FR-13 referenced from FR-5 and FR-16; FR-20 referenced from FR-4; SMs cite their FRs; §12 cites the seam FRs). The Glossary (§3) is rich and each entry is a real definition. The addendum cleanly separates architecture-phase depth (substrate constraints, non-binding event/port sketch) from the PRD contract, and is explicit that "the PRD's FRs are the contract; these shapes are a starting point, not a decision" — exactly the right boundary so the architect cannot mistake the sketch for a spec.

Each section largely stands alone via Glossary terms rather than "see above." UJs each have an implied or named protagonist (UJ-1/2/3 "a builder"/"a system/AI executor"; UJ-4 "Mary").

### Findings
- **medium** Glossary defines an "Effort meter" / "Cost meter" framing only in the addendum, not the PRD Glossary — risk of term drift downstream (§3, addendum §"Burn-Down") — The PRD Glossary defines **Effort** as "the v1 Burn-Down dimension" and **Cost** as "the second … dimension," but the *meter* abstraction that FR-3/FR-11 and SM-2 depend on ("the identical machinery serves a second (Cost) meter", §4.3) is only named as `Meter(Unit, Estimated, Done)` in the addendum's non-binding sketch. A story author reading only the PRD will not find "meter" as a defined noun. *Fix:* add a one-line Glossary entry for the Effort/Cost *meter* concept, or change §4.3/§8 to use "dimension" consistently with the Glossary.
- **low** "Resumed (transient)" Status is in the Glossary and FR-6 but is a transition, not a resting state — could confuse a state-machine implementer (§3 Status, §4.2 FR-6) — Status is defined as "one of Created, Assigned, Queued, InProgress, Suspended, Resumed (transient), Completed, Cancelled, Rejected, Expired," and FR-6 confirms "`Resumed` is a transient transition back into `InProgress`." Listing a transient transition alongside resting states in the same enum invites an implementer to model it as a persistable status. *Fix:* in the Glossary note Resumed is emitted as an event/transition but is not a persisted resting Status (or mark it parenthetically as not a stored state).

## Shape fit — strong

The PRD is correctly shaped for what it is: a developer/infrastructure capability spec for an event-sourced domain kernel. It is not over-formalized — it has no persona theater, and it explicitly justifies keeping UJs "lean, capability-illustrating" because v1 is "a headless domain kernel" (§2.3). It is not under-formalized — for a kernel feeding architecture and stories, the FR-with-testable-consequences shape is exactly the load-bearing structure, and it is present throughout.

The acceptance model fits the shape: build-signal SMs (§11) with no usage metrics is correct for foundation software, and the PRD says so explicitly ("v1 is foundation software; acceptance is defined by build signals … rather than usage metrics"). UJ-4 is appropriately flagged as the deferred end-user horizon (a named-protagonist consumer journey) shown only "to anchor the §12 roadmap" — a deliberate, labeled exception to the otherwise-headless shape, not a category confusion. The strict additive-evolution / package-boundary / runtime-target constraints (§8, addendum) are the constraint-traceability that an ecosystem-embedded module needs, and they are accurately inherited rather than invented.

No shape-fit findings.

## Mechanical notes

- **ID continuity:** FR-1…FR-25 contiguous and unique; UJ-1…UJ-4; SM-1…SM-5 + SM-C1/SM-C2; Themes 3–6 in §12. No gaps or duplicates found. Cross-references checked (FR-13 ← FR-5/FR-16; FR-20 ← FR-4; FR-18 ← FR-6; SM→FR map in §11) all resolve.
- **Assumptions Index roundtrip:** 13 inline `[ASSUMPTION]` tags; §14 lists them but merges FR-5 + FR-13 into one line (see Scope-honesty low finding), giving 12 visual lines for 13 tags. All inline tags are represented; no index entry lacks an inline source. Split the merged line for a clean 1:1.
- **Glossary drift:** Two items (see Downstream findings): (a) "meter" abstraction defined only in addendum; (b) "Resumed (transient)" listed among resting Statuses. Otherwise domain nouns (Work Item, Burn-Down, Remaining, Executor Binding, Roll-Up, Await-Condition, Party, Channel, AuthorityLevel) are used verbatim across FRs/UJs/SMs.
- **Cross-doc references:** PRD points to brief, brief addendum, brainstorm, and this addendum; addendum points back to PRD FRs and to `Hexalith.Projects/_bmad-output/project-context.md`. Paths are relative and consistent; not file-existence-verified in this review.
- **Required sections for stakes/type:** Vision, Target User/JTBD, Glossary, Features+FRs, Non-Goals, MVP Scope, Why Now, Public Surface, NFRs, Constraints, Success Metrics, Roadmap, Open Questions, Assumptions Index all present — complete for a chain-top developer-kernel PRD.
