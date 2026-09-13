# Validation Report — Hexalith.Works

- **PRD:** `/home/administrator/projects/hexalith/works/_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md`
- **Rubric:** `/home/administrator/projects/hexalith/works/.agents/skills/bmad-prd/assets/prd-validation-checklist.md`
- **Run at:** 2026-09-12T10:33:47+02:00
- **Grade:** Poor
- **Gate:** Coordinated implementation blocked; isolated pure-domain work may proceed where the PRD is internally consistent.

## Overall verdict

This is a strategically coherent, unusually substantive technical PRD with explicit scope cuts, product bets, testable consequences, and useful counter-metrics. It is not yet safe as an unqualified implementation contract: normative sections disagree on core lifecycle, tree, authorization, and Roll-Up behavior, and the addendum contradicts the PRD's security-critical actor-provenance rule.

The independent adversarial, downstream-drift, and implementation-readiness reviews materially strengthen that caution. The PRD was amended after its prior validation, while `architecture.md` and `epics.md` still encode the earlier 25-FR design. Registry/Reactor work, distributed acceptance, platform migration, and production admission are blocked until the critical contract contradictions are resolved and downstream plans are rebaselined to FR-26 and the §9 security baseline. Under the workflow's grading rule, any critical finding makes the grade **Poor**.

Across the four source reviews there are 52 finding occurrences (8 critical, 23 high, 15 medium, 6 low). The list below consolidates overlapping findings into 5 critical, 13 high, 6 medium, and 3 low issues; reviewer-specific detail remains in the source files.

## Dimension verdicts

- Decision-readiness — adequate
- Substance over theater — strong
- Strategic coherence — strong
- Done-ness clarity — thin
- Scope honesty — adequate
- Downstream usability — thin
- Shape fit — strong

## Findings by severity

### Critical (5)

**[Done-ness clarity / Adversarial] — Registry-first child creation is bypassable and can attach a nonexistent child (§4.1 FR-1; §4.3 FR-13; §4.4 FR-16)**

FR-1 permits ordinary Create with a parent reference, while FR-16 makes registry reserve the only public attachment entry. The prescribed write order can also mark an edge `Attached` before child creation succeeds, leaving a permanent authoritative edge to no Work Item.

Fix: Reject parent references on ordinary Create; allow only an origin-restricted Reactor child-create command carrying reservation evidence. Make `Attached` mean both parent and child accepted the relationship, with deterministic compensation and crash-boundary acceptance tests.

**[Decision-readiness] — Resume has incompatible normative outcomes (§4.2 FR-6 table; §4.4 FR-15; architecture AD-13)**

The explicitly normative lifecycle table rejects Resume from `InProgress`, but FR-15 requires replay of the consumed Await-Condition to be a no-op after the successful Resume has returned the item to `InProgress`. AD-13 additionally retains the obsolete rule that every non-match is a no-op.

Fix: Make FR-15 the single rule; encode current match = accepted, consumed match after resume = no-op, and every other non-match = rejection in the PRD table, AD-13, epics NFR-9, and tests.

**[Adversarial] — Executor-only acts conflict with tenant-membership-as-the-only-gate (§4.2 FR-6/FR-8/FR-10; §4.5 FR-19; §9)**

Several requirements say the bound Executor claims, rejects, reports, or completes its own work, while FR-19 and §9 say any authenticated tenant member may perform any v1 act. Either behavior can appear compliant, with materially different authorization and audit consequences.

Fix: Add a v1 action-authorization matrix naming the actor predicate for every command and how verified actor identity reaches pre-dispatch or aggregate authorization. If membership alone is intentional, remove all executor-only wording and explicitly accept the blast radius.

**[Downstream drift] — Registry and FR-26 have no implementable epic decomposition (architecture Requirements Overview; epics inventory, coverage map, Stories 3.1–3.2)**

Architecture and epics still advertise 25 FRs and the older caller-facing, Work-Item-owned spawn design. FR-26, Registry state transitions, Reactor-only commands, reservation recovery, and current Roll-Up ancestry are absent from accepted stories.

Fix: Bind VAL-H10, then correct-course architecture and epics to 26 FRs. Replace Stories 3.1–3.2 with ordered Registry/Reactor slices and map FR-26 across the affected saga, cascade, reminder, and recovery stories.

**[Downstream drift / Readiness] — The mandatory identity and trusted-origin gate has no executable delivery story (§9; AD-23/AD-24; Story 4.9)**

The PRD forbids production ingress until verified claim derivation, membership denial, workload delegation, internal-command origin restrictions, mutually authenticated transport, broker ACLs, and forged-sequence protections exist. Story 4.9 can currently pass without any of them.

Fix: Add an owned platform-security story or explicit Story 4.9 slice with the complete positive/negative matrix, and keep production ingress and production-data admission closed until it passes.

### High (13)

**[Done-ness clarity] — Completed and terminal Roll-Up contribution has two definitions (§3 Remaining; FR-8; FR-11; SM-2)**

Explicit Complete may retain `Estimated − Done > 0`, while terminal items must contribute zero; FR-11 and SM-2 still sum “own Remaining.” Fix: distinguish retained meter Remaining from an effective `RollUpContribution`, define terminal and unestimated behavior, and use only the contribution in formulas and schemas.

**[Done-ness clarity / Adversarial] — Eventual coordination and cascade closure are not measurably bounded (FR-16; FR-26; SM-1)**

Reservation settlement, child creation, resume, cascade, and reminder recovery are “bounded” or “eventual” without a maximum, acknowledgement predicate, fixed-point rule, or failure state. Concurrent spawning can escape a cascade traversal. Fix: define observable quiescence, configured acceptance bounds, timeout/retry outcomes, and a subtree-closure rule; test every crash and concurrency boundary.

**[Adversarial] — Domain rejection is incorrectly treated as idempotent success (FR-26; §10; VAL-H10)**

A rejection is observably different from replaying the original success and cannot safely prove that a prior Reactor command completed. Fix: require the same idempotency/causation key to replay the original outcome for the redelivery horizon; classify only named equivalent states as successful translation outcomes.

**[Adversarial] — `ReEstimate` cannot correct over-reported Done (FR-8; FR-9)**

The PRD tells callers to correct erroneous progress through `ReEstimate`, but that command changes Estimated, not Done, and cannot repair a progress event that already completed the item. Fix: either add an auditable additive correction act with terminal rules, or declare Done immutable and remove the false correction guidance.

**[Adversarial / Readiness] — Mid-work handoff and Channel change are promised but forbidden (§1; Glossary; FR-6; FR-17; SM-5)**

The Vision, Channel definition, and SM-5 promise state-preserving mid-work reassignment, but the lifecycle rejects Assign/Queue from `InProgress` and `Suspended`. Fix: narrow the claim and SM-5 to `Assigned`, or define a distinct active-state relinquish/handoff transition.

**[Adversarial] — “What's next” requires authorization filtering without an entitlement model (FR-20; §9)**

FR-20 simultaneously specifies an exact tenant/status/Party filter and requires further authorization/result filtering, but v1 contains no coordinator grant, visibility policy, or enforced AuthorityLevel. Fix: define caller entitlement for executor and coordinator views, PartyId binding to authenticated identity, shared-queue visibility, and positive/negative tests.

**[Decision-readiness] — Release dependencies are scattered rather than expressed as one gate (§9; §13; addendum handoff)**

Open R4/R6/R7/R11 and VAL-H09–H12 obligations block different phases, but the `final` PRD has no actionable dependency/exit-gate table. Fix: name every dependency, owner, evidence, and whether it blocks kernel implementation, integration acceptance, catalog shipment, production ingress, or production data.

**[Downstream drift] — Lifecycle and completion stories retain pre-amendment semantics (architecture overview; epics Stories 2.1, 2.5, 4.2)**

Downstream artifacts omit Claim-only entry, Reject's exact states/outcomes, active reassignment rejection, Complete-at-positive-Remaining, and `ReEstimated` never completing. Fix: mirror the normative transition matrix and add explicit acceptance cases before resuming lifecycle implementation.

**[Downstream drift] — Roll-Up stories retain the retired recursive/per-child model (AD-22 summary; Stories 3.3–3.4)**

Stories omit flattened per-descendant LWW slots, `Attached` ancestry, Registry freshness, unavailable state, unestimated count, Unit inheritance, and quiescence. Fix: rewrite the stories around the current projection contract and acceptance evidence.

**[Downstream drift] — Current query views and product success signals are not covered (FR-20; SM-1–SM-6; Stories 4.1, 4.2, 4.4, 4.9)**

The executor/coordinator query split, SM-3 golden stream diff, SM-5 channel-only change, and SM-6 rebuild identity are absent or weakened; one story even reintroduces “executor kind.” Fix: add exact view ACs and a success-metric coverage map with named test lanes.

**[Readiness] — Rebuild fencing and the tenant control-plane exception remain placeholders (§9; §13 VAL-H08/H09; AD-16/AD-22)**

Reader-safe rebuild and universal tenant scoping cannot be accepted while capture-through-Commit fencing and the only governed cross-tenant namespace are unbound. Fix: publish the namespace/owner/authorizer table and rebuild fence state machine, then test cross-tenant mutation and reader/writer races.

**[Readiness] — Serialized-contract rollout lacks a compatibility matrix (§8; VAL-H11; Story 1.5/NFR-12)**

Unknown enum/type behavior, defaulting, rollout order, downgrade stance, and reader/writer N↔N+1 cases are not decided for the new Registry/event/read-model contracts. Fix: bind the compatibility matrix and golden-corpus cases before any catalog change ships.

**[Downstream usability / Readiness] — Actor provenance and immutable-data handling are unsafe at handoff (addendum event sketch; FR-7; §9; VAL-H12)**

The addendum says actor identity comes from “the binding + EventStore envelope,” contradicting FR-7's trusted-envelope-only rule. Raw Acts also lack an owned production privacy lifecycle. Fix: correct the addendum; add a negative actor≠bound-Party audit test; define data classification, retention/erasure behavior, and production-admission ownership.

### Medium (6)

**[Adversarial] — Unit inheritance is undefined for unestimated parents and estimate-less children (FR-3; FR-12; FR-16)**

Fix: add a truth table for parent Unit, child Estimated, and child Unit, including when inheritance materializes and whether a later Unit can override an inherited default.

**[Adversarial] — Out-of-order guarantees omit Registry-edge versus Work-Item event ordering (FR-11; SM-6)**

Per-stream LWW does not define what happens when item events arrive before `Attached`. Fix: require convergence under all permitted cross-stream interleavings and test reordered edge/create/progress/completion delivery.

**[Adversarial] — Tenant-wide single-writer Registry lacks a scale envelope (Glossary; FR-13; addendum Registry sketch)**

Fix: add target edge cardinality, attach throughput, rehydration, and recovery objectives, or move the one-aggregate-per-tenant partition choice back to architecture.

**[Scope honesty] — Performance thresholds are simultaneously provisional and acceptance-shaped (§9; SM-2; §14)**

Fix: either make the 1,600-item, `<200 ms`, and `5 s` figures binding with environment/percentile definitions, or label them non-gating benchmarks with an owner and decision date.

**[Downstream usability] — Resume's external seam is inconsistently called a port (§4.4; §6.1; addendum Await-Condition)**

Fix: call it a “Resume command contract plus deferred external adapter” and reserve Port for FR-22 abstractions.

**[Readiness / Downstream drift] — Cross-repository sequencing and validation limits are not executable (§6.1; §8; AD-20; epics AR-6/Story 4.9)**

Provider/consumer seams lack a versioned dependency ledger, while downstream text permits `delta ≥ 0`, omits 4,000/1,000 character bounds, and retains per-work-type policy. Fix: create the dependency ledger and align all boundary/configuration ACs to the current PRD.

### Low (3)

**[Mechanical] — UJ-1 and UJ-2 lack named protagonists (§2.3)**

Name them or relabel them as capability scenarios; this is minor for a headless technical product.

**[Mechanical] — Stable FR-26 appears between FR-16 and FR-17 (§4.4)**

Keep the stable ID, but add an explicit feature-to-FR inventory and avoid range shorthand that can omit FR-26.

**[Mechanical] — Source links and Rejected-state prose are stale (§0; addendum intro; FR-6 prose)**

Correct the brief/brainstorm relative paths, and state that Cancelled/Expired are reachable from any non-terminal state while terminal Rejected is reachable only from `Assigned` with `Requeue=false`.

## Mechanical notes

- FR, UJ, and SM identifiers are unique. FR-1 through FR-26 all exist, but FR-26 is presented out of sequence.
- UJ-1 through UJ-4 and SM-1 through SM-6 plus SM-C1/SM-C2 are contiguous in their own schemes.
- FR/UJ/SM cross-references resolve; range shorthand remains risky around FR-26.
- The Assumptions Index roundtrips the inline assumption tags, including grouped, superseded, and narrowed entries.
- Glossary drift remains around singular “Await-Condition” versus the normative set of multiple Await-Conditions.
- The prior validation artifacts predated final September edits; this report and all four reviewer files were regenerated on 2026-09-12.

## Required unblocking sequence

1. Freeze product semantics for ordinary Create versus registry-backed child Create, Resume duplicate/non-match behavior, executor action authorization, and completed-item Roll-Up contribution.
2. Bind VAL-H10 first, then turn R4/R6/R7/R11 and VAL-H09/H11/H12 into owned, testable gates.
3. Correct-course architecture and epics to the 2026-09-08 PRD, including FR-26 and §9 NFR coverage.
4. Reslice Registry/Reactor/platform work along provider-consumer boundaries and add deterministic acknowledgement, recovery, security, compatibility, and privacy tests.
5. Re-run PRD validation and implementation readiness after every critical/high issue has an owner and no story depends on stale prose.

## Reviewer files

- `review-rubric.md` — primary seven-dimension rubric
- `review-adversarial-general.md` — adversarial product-contract review
- `review-downstream-drift.md` — architecture/epics drift review
- `review-readiness.md` — implementation-readiness review
