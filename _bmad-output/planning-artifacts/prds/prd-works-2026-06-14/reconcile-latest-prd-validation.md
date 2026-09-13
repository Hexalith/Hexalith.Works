# Reconciliation Extract — Latest PRD Validation

- **Input:** `validation-report.md`
- **Validation run:** 2026-09-12
- **Gate:** Poor — coordinated implementation is blocked; isolated pure-domain work may proceed only where the PRD is internally consistent.
- **Compared against:** `prd.md`, `addendum.md`, `.memlog.md` as updated 2026-09-08.
- **Scope of this extract:** unresolved findings that require a PRD/addendum decision or edit. Architecture/epic-only drift is omitted except where the PRD needs an explicit dependency or exit gate.

## Decision conflicts requiring confirmation

### 1. Actor authorization: membership-only versus executor-only acts — Critical

The latest explicit decision says that authenticated tenant membership is the only v1 action gate and that `AuthorityLevel` is carried but not read (`.memlog.md` line 35; PRD FR-19 and §9). FR-6, FR-8, FR-10, FR-18, FR-19, the journeys, and glossary still describe Claim, Reject, Progress, Complete, or observation as acts of the bound Executor. Those statements imply executor matching is enforced, which contradicts the membership-only decision and creates materially different audit and authorization behavior.

**Recommended change:** publish one v1 action-authorization matrix covering every command and both FR-20 query views. If the 2026-09-08 membership-only decision stands, state explicitly that any authenticated tenant member may perform every non-origin-restricted command, including Claim, Reject, ReportProgress, Complete, Assign/Reassign, Spawn reserve, Cancel, and Expire; remove or rephrase all executor-only normative language. Keep `SpawnChild`, reminder callbacks, replay, repair, and Reactor actions origin/role restricted by §9. If executor matching is intended for any act, that supersedes the memlog decision and must be logged as a new product decision.

### 2. Mid-work Channel change and handoff — High

The lifecycle decision rejects Assign and Queue from `InProgress` and `Suspended` (`.memlog.md` line 24; FR-6 table and FR-17), while the approved autofix and SM-5 promise a Channel change “mid-work” as one reassign with no Status change (`.memlog.md` line 36; PRD SM-5). Both cannot be accepted by the same state machine.

**Recommended change:** choose and log one contract: either narrow Vision, glossary, FR-17, and SM-5 to Channel changes while `Assigned`, or add an explicit active-state relinquish/handoff transition with its event, actor rule, and acceptance cases. Do not leave cancellation/completion as the implicit handoff mechanism while still promising mid-work handoff.

### 3. Correcting over-reported Done — High

The log and FR-8 say negative progress is rejected and an over-report is corrected with `ReEstimate` (`.memlog.md` line 26; PRD FR-8). But `ReEstimate` changes Estimated only; Done remains unchanged, it never reopens/completes, and it cannot repair an item already auto-completed by progress (`.memlog.md` line 30; FR-8/FR-9). This is a direct semantic conflict.

**Recommended change:** decide between (a) an additive, auditable correction act/event that can adjust Done, with terminal-state and Roll-Up rules, or (b) immutable Done, in which case remove the false correction guidance and explicitly accept that an erroneous auto-completing progress report cannot be repaired in v1. Log the choice because it changes the accepted September behavior.

### 4. Performance figures: qualitative NFR versus acceptance threshold — Medium

The original decision keeps v1 NFRs qualitative with no numeric targets (`.memlog.md` line 12), while the later update adds a 1,600-item fixture, `<200 ms` rolled read, and `5 s` convergence and uses them in SM-2 (`.memlog.md` lines 31 and 36; PRD §9 and SM-2). Calling the same values “provisional” and using them as acceptance conditions is ambiguous.

**Recommended change:** either make the figures binding and define environment, dataset, percentile, measurement window, and failure consequence, or label them non-gating benchmarks with an owner and decision date. Record whether the later provisional figures supersede the June no-target decision.

## Contract fixes already supported by the memlog

### 5. Registry-first child creation is bypassable and can leave an edge attached without a child — Critical

FR-16 and the memlog establish registry reserve as the only public child-attachment entry and `SpawnChild` as Reactor-only (`.memlog.md` line 22). FR-1 nevertheless allows an ordinary Create with a parent reference. FR-16 also orders `Attached` evidence before `WorkItemCreated`, so an accepted parent write followed by failed child creation can leave an authoritative edge pointing at no child.

**Recommended change:** split ordinary root Create from an origin-restricted Reactor child-Create carrying verifiable reservation evidence; reject parent references on ordinary Create. Define `Attached` as evidence that both parent and child accepted the relationship, or introduce a non-authoritative intermediate state until child creation succeeds. Specify deterministic compensation/recovery for every reserve → parent → child crash boundary and acceptance tests proving no permanent attached-to-missing-child state.

### 6. Resume table contradicts the already-decided FR-15 rule — Critical

FR-15 and `.memlog.md` line 25 define: current match accepted; replay of the consumed match after resume is the only no-op; all other non-matches rejected. The normative FR-6 table says every Resume from `InProgress` is rejected, and its consequence says exact duplicate terminal acts are the only no-ops.

**Recommended change:** make the FR-6 table represent the FR-15 rule explicitly, including consumed-match replay from `InProgress`, and correct the general no-op prose. Preserve the logged decision; this is reconciliation, not a new product choice. The downstream AD-13 and lifecycle matrix remain a separate handoff.

### 7. Roll-Up uses raw Remaining where the completion decision requires zero contribution — High

The user decision says explicit Complete preserves Estimated/Done but every Completed item contributes zero (`.memlog.md` line 30). FR-11 and SM-2 still calculate from each item’s own Remaining, which can be positive after explicit completion. The addendum repeats the retired raw-Remaining formula.

**Recommended change:** define an effective `RollUpContribution` distinct from the retained Burn-Down Remaining: `0` for every terminal item, `0 plus unestimated-count` for an unestimated active item, otherwise active Remaining. Use only that contribution in FR-11 formulas, LWW slots, read-model schemas, SM-2, and the addendum sketch. Keep the retained meter visible for audit.

### 8. Reactor redelivery cannot treat an arbitrary domain rejection as idempotent success — High

FR-26 and the addendum currently claim that a defined no-op **or rejection** makes recovery re-issue safe. A fresh rejection is not proof that the original command succeeded and is observably different from replaying the original outcome. The log already requires VAL-H10 transport idempotency to return the original result (`.memlog.md` lines 25–26, 34).

**Recommended change:** state that the same idempotency/causation key must replay the original outcome for at least the full redelivery horizon. Enumerate the few target states that are semantically equivalent and may be translated to success; all other rejections remain failures requiring retry, compensation, or escalation. Align FR-26, §10, and the addendum with VAL-H10.

### 9. Actor provenance in the addendum contradicts the trusted-envelope decision — High

The PRD and `.memlog.md` line 33 say actor identity comes only from authenticated substrate provenance and is never inferred from the Executor Binding. `addendum.md` line 30 says acting identity comes from “the binding + EventStore envelope.”

**Recommended change:** correct the addendum to trusted envelope provenance only and add a negative acceptance case where the authenticated actor differs from the bound Party; the audit record must preserve the actor while the binding continues to identify responsibility.

## Missing contract detail and release gates

### 10. Eventual coordination and cascade closure are not measurably bounded — High

FR-16/FR-26 use “bounded” and “eventual” without a maximum, completion acknowledgement, timeout state, or fixed-point definition. Concurrent spawning can add descendants after a cascade traversal begins. The 5-second SM-2 bound applies only to Roll-Up and does not close reservation, spawn, resume, reminder, or cascade behavior.

**Recommended change:** define observable quiescence, configured acceptance bounds, retry/timeout/escalation outcomes, and a subtree-closure rule for concurrent attach versus cascade. Add crash- and concurrency-boundary acceptance cases for reservation settlement, child creation, resume, reminders, and cascade.

### 11. FR-20 has result filtering but no entitlement model — High

FR-20 names tenant membership, statuses, and Party matching, while §9 additionally mandates query authorization/result filtering. It does not say whether an authenticated member may supply another Party’s ID or obtain the coordinator view, nor how shared-queue visibility is constrained. The membership-only action decision does not by itself resolve read entitlements.

**Recommended change:** include read permissions in the authorization matrix: who may request the Executor view, whether its PartyId must equal the authenticated Party, who may omit PartyId for the coordinator view, and who sees the shared queue. Add positive and negative result-filtering cases. If every tenant member may request every view, state that blast radius explicitly.

### 12. External dependencies are not expressed as one actionable release gate — High

The PRD scatters VAL-H07–H12 and architecture rows R4/R6/R7/R11 across §6, §9, §10, §13, and addendum handoffs. It does not expose a single owner/evidence/blocking-phase contract even though production ingress and production data are prohibited before some dependencies land.

**Recommended change:** add a v1 dependency and exit-gate table naming each dependency, owner, required evidence, and the phase it blocks: kernel implementation, Registry/Reactor integration, distributed acceptance, contract/catalog shipment, production ingress, or production-data admission. At minimum include:

- VAL-H10 / R11 transport idempotency before Registry/Reactor delivery;
- VAL-H07 / R6 reminder durability and failure policy;
- VAL-H08 / R4 reader-safe capture-through-commit rebuild fencing;
- VAL-H09 governed tenant control-plane exception and namespace/authorizer/audit table;
- VAL-H11 serialized-contract compatibility matrix and golden corpus;
- VAL-H12 immutable-event data classification, retention/erasure behavior, and owner;
- AD-23/AD-24 verified claims, membership denial, workload delegation, internal-origin restriction, mTLS/trust domain, broker ACLs, and forged-sequence negative tests before production ingress.

The architecture/epic work needed to deliver these gates is downstream; the PRD change is to make the product gate unambiguous.

### 13. Schema compatibility is a principle, not an acceptance contract — High

§8 requires additive tolerant evolution but does not define unknown enum/type behavior, field defaulting, rollout order, downgrade stance, or N↔N+1 reader/writer combinations for the new Registry, Reactor, and read-model contracts.

**Recommended change:** bind or explicitly gate on a compatibility matrix and golden-corpus cases before catalog shipment. Keep mechanism detail downstream, but state the accepted compatibility outcomes in the PRD.

### 14. Unit inheritance has uncovered states — Medium

FR-12 says a child without an explicit Unit inherits its parent’s Unit for its first estimate, but does not cover an unestimated parent with no Unit, an estimate-less child, when inheritance materializes, or whether a later explicit Unit may replace a pending inherited default.

**Recommended change:** add a compact truth table for parent Unit present/absent × child Estimated present/absent × child Unit present/absent, including materialization time and immutability consequences.

### 15. Cross-stream event ordering is not covered — Medium

FR-11’s LWW rule orders events within a descendant stream but does not define item events arriving before the Registry `Attached` edge, nor edge release/repair interleavings.

**Recommended change:** require convergence under every permitted cross-stream order of reserve/attach/create/progress/completion/repair events, and add reordered-delivery acceptance cases. The read model must not silently lose pre-attachment state.

### 16. Registry scale and partitioning are mixed — Medium

The PRD needs a product load envelope, while the addendum hardens “one tenant-scoped registry aggregate per tenant” without edge-cardinality, attach-throughput, rehydration, or recovery objectives. Without those objectives, the architectural partition choice cannot be validated.

**Recommended change:** put binding workload/recovery objectives in the PRD (or mark them as a named non-gating benchmark with owner/date) and keep the one-aggregate partition choice explicitly non-binding until architecture validates it.

## Mechanical PRD/addendum corrections

- Replace “generic external resume-by-correlation-ID port” in §4.4/§6.1 with “Resume command contract plus deferred external adapter”; reserve **Port** for FR-22 abstractions.
- Name protagonists for UJ-1 and UJ-2, or relabel them as capability scenarios.
- Keep stable ID FR-26, but add a feature-to-FR inventory so range shorthand cannot omit it.
- Correct the brief/addendum source paths from the PRD workspace.
- Correct §4.2 prose: Cancelled and Expired are reachable from every non-terminal state; terminal Rejected is reachable only from `Assigned` with `Requeue=false`.
- Normalize singular/plural Await-Condition wording where the contract permits a set.

## Downstream-only validation findings excluded from this PRD extract

The report also requires architecture and epics to be rebaselined from 25 to 26 FRs; Registry/Reactor slices to replace stale Stories 3.1–3.2; lifecycle, Roll-Up, query, boundary, and success-metric ACs to be rewritten; and Story 4.9 to carry the §9 security baseline. Those are real blockers, but they do not require additional PRD text beyond the explicit exit-gate and feature-inventory changes above.
