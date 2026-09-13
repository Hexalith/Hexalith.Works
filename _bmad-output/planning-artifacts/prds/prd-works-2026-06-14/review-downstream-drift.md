# Downstream Drift Review — Hexalith.Works PRD

- **Reviewer:** focused downstream-drift pass
- **Run date:** 2026-09-12
- **Product source:** `prd.md` + `addendum.md`, final, updated 2026-09-08
- **Compared consumers:** `../../architecture.md` (updated 2026-09-06) and `../../epics.md` (frontmatter `lastUpdated: 2026-09-05`, with partial 2026-09-06 additions)
- **Orientation:** `validation-orientation.md`

## Overall verdict

**BLOCKED FOR RELIABLE IMPLEMENTATION HANDOFF — material downstream drift.** The current PRD is substantially newer than both consumers. The Architecture Decision Register contains many of the September mechanisms, but one adopted rule contradicts the PRD and its narrative/coverage sections still describe the older product. The epic breakdown remains largely pre-update: it lacks an implementable Work-Tree Registry/FR-26 decomposition, omits the v1 security baseline from Story 4.9, and carries stale lifecycle, roll-up, query, idempotency, and acceptance semantics. A team implementing from `epics.md` can produce a system that violates the normative PRD while believing every mapped FR is covered.

**Finding counts:** Critical 2 · High 6 · Medium 4 · Low 2.

## Critical findings

### DD-C01 — Registry attachment and FR-26 have no implementable downstream decomposition

**Evidence.** PRD FR-13 makes the Work-Tree Registry the tree-shape authority (`prd.md` §4.3, lines 252–260); FR-16 makes registry reserve the public act and restricts `SpawnChild` to the Reactor (`prd.md` §4.4, lines 287–296); new FR-26 owns all cross-aggregate translations, their eventual window, recovery, and provenance (`prd.md` §4.4, lines 298–306). Architecture AD-21/AD-22 substantially agree (`architecture.md` AD-21, lines 272–316; AD-22, lines 318–342), but its Requirements Overview still claims 25 FRs and “a single event-sourced aggregate root owning … parent/children refs” (`architecture.md` lines 459–469), and its coverage validation still says all 25 FRs have homes (`architecture.md` lines 1062–1069).

The epic inventory stops at FR-25 (`epics.md` lines 37–157), still says the tree guard is “enforced at spawn” and `ChildSpawned` directly creates the child (`epics.md` FR-13/FR-16, lines 95–110), and its coverage map has no FR-26 (`epics.md` lines 362–388). Stories 3.1/3.2 make the Work Item path authoritative and caller-facing (`epics.md` lines 815–878). The only accommodation is a non-executable “pending additions” note whose predecessor, VAL-H10, is still open (`epics.md` lines 402–410).

**Impact.** The current public contract, aggregate boundary, write order, rejection catalog, idempotency behavior, and tree authority cannot be built faithfully from the accepted stories. Implementing Story 3.2 as written bypasses the Registry, permits the wrong caller to submit `SpawnChild`, and omits reserve/attach/release recovery.

**Fix.** First bind VAL-H10 at AD-20 R11. Then amend the architecture Requirements Overview/coverage claims and replace or supersede Stories 3.1/3.2 with Registry stories that cover: complete reserve payload; registry-owned single-parent/acyclic/depth/tenant decisions; `Reserved → Attached|Released`; dedicated spawn rejection; duplicate attached-pair no-op; assertion-only Work Item guard; direct-`SpawnChild` denial; attached-only roll-up/cascade; Unit inheritance; and reservation timeout. Add FR-26 to inventory, coverage, epic ownership, and acceptance across the Registry story plus Stories 3.5, 3.6, and 4.6–4.8.

### DD-C02 — The production identity/trusted-origin gate is absent from Story 4.9

**Evidence.** PRD §9 requires verified claim→Tenant/actor derivation, deny-before-dispatch, query result filtering, workload identities with auditable tenant delegation, origin-restricted internal commands, mutually authenticated transport, and a production-ingress prohibition until enforcement is live (`prd.md` lines 446–449). Architecture AD-23/AD-24 binds OIDC, mTLS/access policy, broker ACLs, trusted `SequenceNumber` provenance, negative-origin tests, and a positive production-policy reminder-registration test, explicitly making them Story 4.9 acceptance (`architecture.md` lines 344–395; AD-20 R10 at line 269).

Yet the epic Overview classifies all “security enforcement” as Themes 3–6 that generate no v1 stories (`epics.md` lines 24–31), and Story 4.9 contains only hosting, topology scenarios, rebuild, and migration sequencing (`epics.md` lines 1335–1383). It has no OIDC claim derivation, identity mismatch denial, workload delegation, mTLS/trust-domain checks, broker-origin enforcement, direct-`SpawnChild` denial, forged-sequence rejection, or positive reminder registration under production policy.

**Impact.** Story 4.9 can be accepted while the PRD explicitly forbids production ingress. In a multi-tenant event-sourced system, this leaves forged actor/tenant/sequence inputs able to poison authoritative streams and LWW projections.

**Fix.** Narrow the epic Overview to defer only Theme-6 hardening, not the §9 platform baseline. Add PRD §9 and AD-23/AD-24 as Story 4.9 sources, then copy the full positive and negative acceptance matrix into Story 4.9, including deny-before-dispatch/query, tenant non-disclosure, workload identity/delegation, origin-restricted routes, mTLS/access policy, broker ACLs, forged high-sequence rejection, and successful reminder registration under production policy.

## High findings

### DD-H01 — AD-13 is an adopted rule that contradicts normative FR-15

**Evidence.** PRD FR-15 says a non-matching resume while Suspended is a domain rejection and only repetition of the consumed condition is a no-op (`prd.md` lines 275–285). The addendum’s lifecycle sketch agrees (`addendum.md` line 36). Architecture AD-13 instead binds “no current match is a no-op; a duplicate is a no-op” (`architecture.md` lines 159–164). `epics.md` is internally split: Story 3.5 matches the PRD (`epics.md` lines 979–987), while NFR-9 repeats the obsolete no-current-match no-op rule (`epics.md` lines 193–195).

**Impact.** The architecture declares its register authoritative over prose, so two compliant implementers can choose opposite externally observable results for the same command. Rejection streams, retries, and callers will diverge.

**Fix.** Amend AD-13 and every architecture occurrence to: current non-match = rejection; consumed-condition repeat = sole no-op; any other resume outside Suspended = rejection. Rewrite epics NFR-9 to the same rule and point both documents to normative PRD FR-6/FR-15 plus the mirrored lifecycle matrix.

### DD-H02 — Lifecycle and completion stories implement the pre-September semantics

**Evidence.** The PRD’s normative table makes `Claim` the only entry to `InProgress`, Reject legal only from `Assigned` with exactly two outcomes, active reassignment illegal, and exact duplicate terminal acts the only lifecycle no-ops (`prd.md` FR-6, lines 157–183). FR-8 permits explicit Complete from `InProgress` or `Suspended` at any Remaining, preserves Estimated/Done, and says `ReEstimated` never completes (`prd.md` lines 196–204).

The epic inventory still titles FR-8 “complete by Remaining=0” (`epics.md` lines 62–84). Story 2.1 permits “starts or claims” without binding Claim as the single act and does not constrain Reject to `Assigned` or spell out the one-event/two-outcome rule (`epics.md` lines 637–656). Story 2.5 tests explicit Complete only for unestimated work (`epics.md` lines 775–784). Story 4.2 does not prohibit reassignment from `InProgress`/`Suspended` (`epics.md` lines 1076–1107). The architecture Requirements Overview likewise reduces completion to “Remaining 0” (`architecture.md` line 464).

**Impact.** Downstream stories can add a second start act, incorrectly reject explicit completion at positive Remaining, mutate burn-down during Complete, or allow live handoff—all public behavioral incompatibilities.

**Fix.** Replace the downstream lifecycle summaries with the normative table. Add ACs for creation-with-binding remaining `Created`; Claim-only entry; Reject-only-from-Assigned with `Requeue=true|false` and no extra queue event; active assignment/queue rejection; explicit Complete from both allowed states at any Remaining without rewriting Estimated/Done; `ReEstimated` never completing; and no Reopen.

### DD-H03 — Roll-up stories use the retired data model and omit new observable states

**Evidence.** PRD FR-11 requires flattened per-descendant own-contribution slots keyed by that descendant’s stream position, LWW replacement, `Attached`-edge ancestry, an unavailable state, and an unestimated-descendants count (`prd.md` lines 231–242). FR-12 adds child Unit inheritance (`prd.md` lines 244–250). Architecture AD-06/AD-22 carries the flattened mechanism, freshness witness, and unavailable state (`architecture.md` AD-22, lines 318–342), but its Requirements Overview still uses `rolled = own + Σ rolled(children)` (`architecture.md` line 465) and does not bind Unit inheritance into AD-21.

Epics FR-11/AR-8 and Story 3.3 remain recursive/per-child (`epics.md` lines 88–97, 238–240, 880–916). No story requires `Attached`-edge-only contribution, the unestimated count, unavailable-vs-fresh semantics, registry watermark/freshness behavior, child Unit inheritance, quiescence acknowledgement, or the provisional 5-second/~1,600-item/<200-ms fixture from PRD §9/SM-2 (`prd.md` lines 453 and 468).

**Impact.** A mathematically similar total can still be operationally wrong under reordering, multi-level sequences, projection lag, repair, and unestimated data. Consumers also cannot distinguish zero work from unknown work.

**Fix.** Rewrite architecture summary and Stories 3.3/3.4 around per-descendant own-contribution LWW slots and Registry-resolved `Attached` ancestry. Add availability, unestimated count, Unit inheritance, watermark/freshness, rebuild-fence, quiescence, and performance-fixture ACs. Keep recursive wording only as explanatory mathematics, never as the persisted slot model.

### DD-H04 — Transport idempotency is both a v1 requirement and an unresolved blocker

**Evidence.** PRD §10 explicitly distinguishes aggregate idempotency, read-side idempotency, and a v1 transport message/idempotency key needed by Registry translations (`prd.md` line 458). The addendum makes VAL-H10 a predecessor to the Registry story (`addendum.md` line 45). Architecture still marks retention/replay-result semantics open and required before drafting that story (`architecture.md` VAL-H10, line 449). Epics NFR-9 describes only old resume/offset behavior and ends “no explicit per-act idempotency token in v1” (`epics.md` lines 193–195), without distinguishing a transport command key from the deferred signed per-act token.

**Impact.** Reserve→spawn→release recovery has no stable redelivery contract. The wording also invites teams to omit the transport key on the mistaken ground that all tokens are deferred.

**Fix.** Resolve VAL-H10 before Registry planning: specify key source/scope, deterministic causation derivation, duplicate retention, result replay, expiration, and behavior after retention. Amend epics NFR-9 and R11 stories to state that the transport key is v1 while the signed per-act link token remains Theme 6.

### DD-H05 — Story 4.4 cannot implement FR-20’s two query views

**Evidence.** PRD FR-20 defines one query with two exact modes: with Executor `PartyId`, that Party’s Assigned items plus all tenant Queued items; without it, all tenant Assigned+Queued items, with the complete filter predicate and null ordering (`prd.md` lines 347–354). Epics FR-20 and Story 4.4 only describe a tenant-wide set of queued/assigned items (`epics.md` lines 128–132 and 1148–1178); there is no optional Executor parameter, binding filter, or paired acceptance case.

**Impact.** The named direct consumer view—“what should I do next?”—can be omitted while the story passes. Returning all assigned work to every executor would also leak within-tenant responsibility information.

**Fix.** Add explicit executor-view and coordinator-view ACs with the PRD’s exact predicate. Test present/absent Priority and Due Date separately, deterministic identity order, membership denial, and result filtering for each view.

### DD-H06 — “Everything is a Party” and SM-3/SM-5 are weakened by the epic contract

**Evidence.** PRD FR-17 fixes one binding/one doer, no executor-kind discriminator, Channel change as reassign, and no active reassignment (`prd.md` lines 314–323). SM-3 requires two falsifiable checks—new Channel/AuthorityLevel values cause zero Server/Projection/event changes, and three executor cases yield streams differing only in binding fields—while SM-5 makes same-Party Channel change a separate acceptance signal (`prd.md` lines 469 and 473).

Story 4.1 instead says read models expose “executor kind” as data (`epics.md` lines 1066–1069), which implies a discriminator the PRD deliberately rejects, and its fitness test checks only branching/imports (`epics.md` lines 1071–1074). Story 4.2 has no same-Party Channel-change case and does not state one binding/one doer or the exclusion of approvers/observers/escalation rungs (`epics.md` lines 1076–1107).

**Impact.** Downstream models can reintroduce executor subtypes and miss both falsifiable product signals while claiming the keystone is covered.

**Fix.** Remove “executor kind” from the read-model AC unless it is explicitly derived outside the domain from Party data. Add the exact SM-3 fitness and golden-corpus checks, a same-Party Channel-change AC, one-binding/one-doer acceptance, and explicit exclusion of approvers/observers/routing candidates from the aggregate.

## Medium findings

### DD-M01 — The current success-metric contract has not propagated

**Evidence.** The September PRD extends SM-1 with restart, claim race, and cascade; makes SM-2 quiescence/bound explicit; rewrites SM-3; repoints SM-5; and adds SM-6 rebuild identity/no-derived-payload evidence (`prd.md` lines 462–474). Architecture still frames acceptance as SM-1…SM-5 with no numeric budgets (`architecture.md` line 479), and its validation claims coverage of only 25 FRs (`architecture.md` lines 1062–1079). Epics has no updated SM inventory or trace from those signals to stories; Story 4.9’s broad scenario list (`epics.md` lines 1361–1370) does not require SM-3’s golden diff, SM-5’s channel-only reassign, or SM-6’s live-vs-rebuilt identity and no-derived-event-payload assertion.

**Impact.** The implementation can satisfy the story suite but fail the PRD’s explicit definition of success.

**Fix.** Add an SM coverage map and carry each changed signal into named ACs/test lanes. Update architecture’s requirements/validation summaries to 26 FRs and SM-1…SM-6 plus counter-metrics; retain “provisional” on numeric assumptions rather than claiming there are none.

### DD-M02 — Validation limits and configuration scope disagree downstream

**Evidence.** PRD FR-2 bounds Obligation at 4,000 characters (`prd.md` line 123); FR-7 bounds event notes at 1,000 (`prd.md` line 193); FR-8 requires progress delta strictly greater than zero (`prd.md` line 201); FR-10/FR-13 use per-tenant platform configuration (`prd.md` lines 222 and 260). Epics omits both length bounds, AR-6 permits `delta ≥ 0` and says per-work-type/tenant policy (`epics.md` lines 232–235), and Story 3.1 repeats tenant/type policy (`epics.md` line 841). Architecture AD-25 also still binds Due-Date/TTL defaults per work-type/tenant (`architecture.md` lines 397–416), although work type was removed from v1.

**Impact.** Zero-progress events and a nonexistent v1 work-type policy can enter the implementation, while public input limits remain untested.

**Fix.** Change AR-6 and all ACs to `delta > 0`; replace per-type/tenant with per-tenant in AD-25 and stories; add boundary tests at 4,000/4,001 and 1,000/1,001 with domain rejection and unchanged state.

### DD-M03 — Story 4.9’s gate/prerequisite model is stale and incomplete

**Evidence.** Architecture says Story 4.9 may enter because AD-20 has already named `Hexalith.Platform` and its owner (`architecture.md` lines 1081–1090), while its open register says R6/R7/R4 items block acceptance and VAL-H09/10/12 have explicit revisit gates (`architecture.md` lines 436–453). Story 4.9 still carries the obsolete prerequisite “must name the platform/host repository and owner” (`epics.md` lines 1381–1383), but does not enumerate the open R4/R6/R7/R11 acceptance dependencies, governed namespace exception, or production-data privacy gate.

**Impact.** Planning shows a closed gate as open and real acceptance/production gates as invisible. Teams cannot tell when Story 4.9 may start, when it may pass, or when production data may be admitted.

**Fix.** Mark the naming prerequisite resolved by AD-20. Add a gate table to Story 4.9: entry (AD-20 resolved), pre-Registry (VAL-H10/R11), acceptance (VAL-H06/R7, H07/R6, H08/R4, H09 design review), catalog shipment (H11), and production-data admission (H12), with accountable owners.

### DD-M04 — The PRD addendum gives a contradictory actor source

**Evidence.** PRD FR-7 says the actor comes from platform-authenticated envelope identity, never caller payload and never the Executor Binding (`prd.md` lines 185–193); PRD §9 and architecture AD-23/AD-24 reinforce that rule (`prd.md` line 447; `architecture.md` lines 344–395). The addendum’s non-binding event sketch instead says “acting-Party identity + timestamp come from the binding + EventStore envelope” (`addendum.md` line 30).

**Impact.** Although labeled non-binding, this is the detailed handoff an architect is likely to copy. Inferring actor from the responsible Executor would falsify Raw-Act attribution for coordinator and Reactor acts.

**Fix.** Amend only the addendum sketch in a later PRD Update to say actor/timestamp come exclusively from authenticated EventStore envelope provenance; the Executor Binding records responsibility, not action. Until then, downstream documents must cite PRD FR-7/§9 as controlling.

## Low findings

### DD-L01 — Architecture metadata and readiness statements advertise stale completeness

**Evidence.** `architecture.md` is marked complete and updated 2026-09-06, two days before the PRD update. It says all 25 FRs are covered, no critical gaps are open, and requirements-to-structure mapping is complete (`architecture.md` lines 1062–1069, 1096–1099, 1148–1159). `epics.md` is stamped 2026-09-05 (`epics.md` line 12) despite partial 2026-09-06 content.

**Impact.** Automated or human readiness checks can trust a green/complete marker that predates the contract they are supposed to implement.

**Fix.** After repairing drift, update document timestamps/status, rerun architecture and sprint-plan validation, and make completeness claims derive from the 26-FR/SM-6 inventories.

### DD-L02 — Source links and existing validation artifacts are mechanically stale

**Evidence.** From the PRD workspace, `../briefs/...` and `../../brainstorming/...` do not resolve; the actual relative paths are `../../briefs/...` and `../../../brainstorming/...`. The memlog records that reviewer re-validation was not rerun after the 2026-09-08 update, so the existing `validation-report.*` and other `review-*.md` files describe the earlier artifact.

**Impact.** Source extraction can silently fail, and reviewers can mistake historical findings/verdicts for current validation evidence.

**Fix.** Correct source paths in a later PRD Update and stamp/archive old reports as pre-update evidence. Generate a fresh consolidated report only from reviews produced against the September PRD.

## Checks with no finding

- Architecture AD-21/AD-22, AD-23/AD-24, and AD-25 provide substantial mechanism depth for the PRD additions; the main failures are propagation, one conflicting adopted rule, and unclosed gates—not wholesale absence of architecture.
- Epics Story 3.5 already matches the current multi-Await-Condition clearing and consumed-condition-only no-op behavior; it should be treated as the correct downstream wording when repairing AD-13 and NFR-9.
- Claim conflict retry exhaustion is already distinguished from an ordinary domain-rejection loser in epics NFR-3 and Story 4.3 (`epics.md` lines 172–176 and 1122–1146), matching PRD §9.
