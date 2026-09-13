# Adversarial General Review — Hexalith.Works PRD

## Overall verdict

**Verdict: FAIL — not safe to use as the sole source for architecture or story generation yet.** The September amendments resolve most defects in the prior review, but two core contracts remain internally inconsistent: the spawn protocol can create authoritative edges to Work Items that never exist, and the authorization model simultaneously says only the bound Executor performs executor acts and that tenant membership is the only gate on any act. Six additional high-severity defects can produce divergent implementations or incorrect audit/read-model behavior.

| Severity | Count |
| --- | ---: |
| Critical | 2 |
| High | 6 |
| Medium | 5 |
| Low | 1 |
| **Total** | **14** |

## Findings

### ADV-C01 — The spawn protocol can bypass the registry and can permanently attach a nonexistent child

- **Severity:** Critical
- **Type:** Product-requirement defect with data-integrity consequences
- **Evidence:** FR-1 lets “**A builder or Executor**” create a Work Item while “optionally supplying … **parent reference**.” FR-16 instead says “the **public act is the Work-Tree Registry's reserve command**” and direct `SpawnChild` is denied. FR-16 then fixes the write order as registry `Reserved` → parent `ChildSpawned` and edge `Attached` → child `WorkItemCreated`; only a **Reserved** edge can be released on parent rejection or timeout. Addendum §Work-Tree Registry likewise says `Reserved → Attached` on `ChildSpawned`, while Roll-Up and cascade enumerate `Attached` edges.
- **Failure mode:** A caller can apparently create a child with a parent reference without a registry reservation, creating an asymmetric tree. Even on the intended path, the registry marks the edge `Attached` before child creation validates. An empty/oversized Obligation, invalid estimate, or permanent child-create rejection then leaves an authoritative `Attached` edge to no child. Roll-Up and cascade consume that corrupt edge, and the specified timeout cannot release it because it is no longer `Reserved`.
- **Concrete fix:** Make `parent reference` on `CreateWorkItem` origin-restricted to the Reactor and require reservation evidence; ordinary create must reject it. Change edge semantics so `Attached` means **both parent and child streams have accepted the relationship** (for example `Reserved → ParentRecorded → Attached` on `WorkItemCreated` evidence), with deterministic release/compensation for permanent child-create rejection. Add acceptance cases for invalid child payload, crash after each write, and direct-create bypass.

### ADV-C02 — Executor-only acts and tenant-member authorization contradict each other

- **Severity:** Critical
- **Type:** Product-requirement defect with authorization and audit consequences
- **Evidence:** FR-6 says a pushed Executor “**claims its own assignment**”; FR-8 says “**An Executor** can report progress” and complete; FR-10 says “a **bound Executor** can Reject.” FR-19 then says “the **only gate on any act** in v1” is tenant membership and explicitly allows any tenant member to “assign, reassign, **claim**, re-estimate, reschedule, cancel, or expire *any* Work Item.” §9 likewise defines minimum command authorization as membership only. FR-7 says the actor is never inferred from the binding and Works adds no actor payload.
- **Failure mode:** One implementation will enforce actor Party = bound Party for Claim/Reject/Progress/Complete; another will allow any tenant member because that is the stated only gate. The latter permits a tenant peer to claim another Party's pushed assignment, reject it, or potentially report/complete its work while the event trail merely records who did so. The former needs an authenticated-actor authorization seam that the PRD says is not part of Works state or payload. Tests cannot determine which behavior is compliant.
- **Concrete fix:** Publish a v1 action-authorization matrix with one row per command and explicit predicates. At minimum, decide whether Assigned-Claim, Reject, ReportProgress, and Complete require actor Party = current binding; separately define coordinator acts. Specify how the verified actor reaches pre-dispatch/aggregate authorization without becoming caller-supplied event data. If v1 intentionally permits every member, rewrite all “Executor/bound Executor/own assignment” requirements and accept the resulting audit semantics explicitly.

### ADV-H01 — The Roll-Up formula contradicts terminal-item semantics

- **Severity:** High
- **Type:** Product-requirement defect
- **Evidence:** The Glossary defines Remaining as `Estimated − Done` and says explicit completion may leave reported Remaining above zero, while a Completed item “always contributes 0.” FR-8 confirms explicit Complete leaves Estimated and Done unchanged. FR-10 says terminal descendants contribute 0. FR-11 nevertheless defines rolled Remaining as “**own Remaining + Σ(own Remaining of every descendant)**,” and SM-2 repeats “own + descendants' Remaining.”
- **Failure mode:** For an item explicitly completed at Estimated 10 / Done 6, its authoritative own Remaining is 4, but its required contribution is 0. The normative formula yields 4; the terminal rule yields 0. Cancelled and Expired items have the same contradiction. Implementers can produce different totals while each satisfies part of the PRD, and SM-2 has no unambiguous oracle.
- **Concrete fix:** Define a separate term, for example `ActiveRemainingContribution = 0` when terminal, `0 + unestimated flag` when unestimated, otherwise `max(Estimated − Done, 0)`. Define Roll-Up and SM-2 exclusively as the sum of contributions, while exposing reported Burn-Down Remaining separately. State whether terminal-unestimated items remain in the unestimated count.

### ADV-H02 — The prescribed correction for over-reported progress cannot correct Done

- **Severity:** High
- **Type:** Product-requirement defect with ledger-integrity consequences
- **Evidence:** FR-8 rejects non-positive progress and says “correcting an over-report is a `ReEstimate`.” FR-9 says `ReEstimated` changes the absolute **Estimated** value; it does not change Done. Completion is terminal with no Reopen.
- **Failure mode:** If Estimated = 10 and a mistaken progress act changes Done from 2 to 8, re-estimating cannot restore Done to 2; it only changes the plan around the false historical quantity. If the error drives Remaining to zero, the item auto-completes and no correction act is legal. The append-only Raw Act ledger therefore has no auditable way to correct a common input error, despite claiming it does.
- **Concrete fix:** Add an explicit additive correction act/event referencing the erroneous `ProgressReported` event and recording signed adjustment/reason, with rules for terminal correction and Roll-Up recomputation; or state that reported Done is immutable and correction is out of scope, removing the false `ReEstimate` guidance. Do not overload re-estimation, which is a planning fact, as a historical-progress correction.

### ADV-H03 — v1 promises mid-work handoff and Channel change while the state machine forbids both

- **Severity:** High
- **Type:** Product-requirement contradiction
- **Evidence:** The Vision promises human⇄AI handoff “identically.” The Channel glossary says a Party “may change Channel **mid-work**.” FR-17 says reassignment/handoff is one operation and asserts human→AI and AI→human handoff. But FR-6 rejects Assign and Queue from both `InProgress` and `Suspended`, explicitly noting that mid-work handoff requires cancellation or completion. SM-5 still requires a Channel change “**mid-work**” as one reassign with no Status or Burn-Down change.
- **Failure mode:** SM-5 cannot pass for an `InProgress` or `Suspended` item under the normative transition table. More importantly, the keystone handoff claim only works before work begins; cancelling the old item loses continuity, while completing it falsely records that the work is done.
- **Concrete fix:** Either narrow the Vision, Glossary, FR-17, and SM-5 to **pre-start rebinding** and defer live handoff explicitly, or add a relinquish/handoff transition that preserves Burn-Down and moves responsibility from an active item under clear actor rules. Name the exact states used by the SM-5 test.

### ADV-H04 — Eventual cascade does not guarantee subtree closure under concurrent spawning

- **Severity:** High
- **Type:** Product-requirement defect; the barrier mechanism belongs to architecture
- **Evidence:** FR-10 promises cancellation/expiry of the “whole open subtree.” FR-16 allows `SpawnChild` on any non-terminal parent. FR-26 explicitly allows a descendant of a terminal ancestor to continue accepting acts until its cascade command lands and calls this a “**bounded post-termination window**,” but gives no bound. Cascade enumerates a re-readable projection of `Attached` edges.
- **Failure mode:** During the allowed window, a still-active descendant can reserve and attach a new child. A cascade traversal that has already passed that descendant can miss the new edge, leaving live work below a cancelled/expired ancestor. “Re-readable projection” does not define a closure fence or fixed-point condition, and no acceptance metric bounds the window or proves no descendant escapes.
- **Concrete fix:** Add product semantics for cascade closure: once ancestor termination is accepted, either the registry rejects new reservations anywhere below that ancestor, or the Reactor iterates to a declared fixed point/epoch before the cascade is complete. Define a measurable maximum convergence bound and an observable `cascade pending/complete` state. Test concurrent spawn, progress, and completion at every cascade boundary.

### ADV-H05 — “What's next” requires authorization filtering but defines no authorization rule

- **Severity:** High
- **Type:** Product-requirement defect with confidentiality consequences
- **Evidence:** FR-20 calls its filter predicate “**exactly**” tenant membership → status → optional PartyId match/Queued pool. Without a PartyId it returns **every** Assigned and Queued item in the tenant. The same FR then requires “query-side authorization/result filtering in addition to tenant scoping”; §9 says tenant scoping is insufficient. No coordinator role, grant, owner, team boundary, or visibility field exists in v1, and AuthorityLevel is a property of the bound Executor that is not enforced.
- **Failure mode:** There is no data with which to perform the additional filtering. An implementation can either expose all work descriptions to any tenant member, contradict §9, or invent an entitlement source and filtering policy, contradicting FR-20's exact predicate. Passing a different PartyId also appears sufficient to inspect another Party's assigned work.
- **Concrete fix:** Define the v1 read-entitlement model: who may call the coordinator view; whether Executor view PartyId must equal the authenticated actor; which fields are visible for the shared queue; and the external policy/grant source if any. Then make FR-20's predicate include those rules and add positive/negative tests. If all tenant members may see all v1 work, state that plainly and remove the unsupported “additional filtering” claim.

### ADV-H06 — Domain rejection is incorrectly treated as idempotent success

- **Severity:** High
- **Type:** Product-requirement defect; key storage/retention is an architecture detail
- **Evidence:** FR-26 says every Reactor command is idempotent because redelivery reaches “defined **no-op or domain rejection**.” Addendum §Reactor repeats that NoOp **or rejection** makes crash-recovery safe. §10 separately says transport idempotency must return the **original result**, but §13 still leaves VAL-H10 as residual work. FR-15 retains only the last consumed condition, so a sufficiently late duplicate of an older successful Resume becomes a rejection after a later suspension cycle.
- **Failure mode:** A rejection is observably different from the original success and may itself be an `IRejectionEvent`; it is not idempotency. A Reactor cannot safely infer from an arbitrary rejection whether the original command succeeded, the target advanced independently, or the command was invalid. This is especially dangerous for reserve→spawn→release: treating a redelivery rejection as failure can release a healthy relationship; treating every rejection as success can conceal a real defect.
- **Concrete fix:** Define semantic idempotency as “same idempotency/causation key returns the original outcome and emits no additional domain fact” for the full retention horizon of Reactor redelivery. Define how Reactor classifies target outcomes per translation; only named equivalent-state results may count as success. Keep storage/checkpoint mechanics in architecture, but make the externally observable outcome and retention requirement normative before registry stories proceed.

### ADV-M01 — Unit inheritance is undefined for unestimated parents and estimate-less children

- **Severity:** Medium
- **Type:** Product-requirement ambiguity
- **Evidence:** FR-3 says the “first accepted estimate” establishes an item's Unit. FR-12 says a child spawned without Unit “inherits its parent's Unit as the Unit of its first estimate.” FR-16 allows Estimated and Unit to be independently optional in the reserve payload.
- **Failure mode:** The PRD does not say what happens when the parent is unestimated and has no Unit, when a child supplies Unit without Estimated, or when an estimate-less child under an hours parent later submits its first estimate with tokens. Different answers change event state, validation, and mixed-Unit opt-in semantics.
- **Concrete fix:** Add a truth table for `(parent Unit present/absent) × (child Estimated present/absent) × (child Unit present/absent)`. State whether Unit may exist without an estimate, when inheritance is materialized, and whether a later explicit Unit may override an inherited-but-unused default.

### ADV-M02 — Out-of-order Roll-Up guarantees cover item positions but not relationship-event ordering

- **Severity:** Medium
- **Type:** Product acceptance gap; buffering/index mechanics belong to architecture
- **Evidence:** FR-11 claims idempotence under “possibly out-of-order event delivery” because each descendant slot is keyed by that descendant's stream position. But ancestry is established by registry `Attached` events from a **different stream**, and FR-16 makes parent, registry, and child writes separately publishable. SM-6 requires an identical rebuild but does not state that cross-stream permutations are exercised.
- **Failure mode:** `WorkItemCreated`, progress, completion, or terminal events can be observed before the projection observes the corresponding `Attached` edge. A last-writer rule within the child stream does not say whether those facts are buffered, replayed, or lost when ancestry arrives. The implementation can be idempotent per stream yet permanently wrong across streams.
- **Concrete fix:** Make convergence invariant to all permitted interleavings of registry-edge and Work Item events. Add a success scenario that permutes `Reserved`, `ChildSpawned`/`Attached`, `WorkItemCreated`, progress, and completion deliveries, then asserts identical quiescent Roll-Up and rebuild output. Leave the buffering/index design to architecture.

### ADV-M03 — The PRD hard-codes a tenant-wide single-writer topology without a tenant scale requirement

- **Severity:** Medium
- **Type:** Architecture/implementation risk, not intrinsically a product defect
- **Evidence:** The Glossary makes the Work-Tree Registry “the tenant-scoped, event-sourced aggregate that owns **every** parent→child edge.” Addendum §Work-Tree Registry is stronger: “**One** tenant-scoped registry aggregate per tenant, its own single-writer actor.” FR-13 leaves breadth uncapped, while §9 says numeric performance budgets are deferred and offers only a provisional ≈1,600-item fixture.
- **Failure mode:** Every independent Work Tree in a large tenant serializes through one ever-growing stream/state object. That may be acceptable for intended v1 scale, but the PRD supplies no tenant edge count, attach throughput, rehydration, or recovery objective against which the trade-off can be judged. A mechanism decision is being treated as product truth before scale is known.
- **Concrete fix:** Keep the product invariant “one authoritative, serializable edge decision per child” but move “one aggregate per tenant” to architecture unless it is truly mandatory. Add target tenant cardinality, edge-creation rate, and recovery/rebuild envelope; then validate the chosen partitioning against them.

### ADV-M04 — The addendum reintroduces actor inference from the Executor Binding

- **Severity:** Medium
- **Type:** Documentation contradiction likely to mislead architecture
- **Evidence:** PRD FR-7 says acting identity is “never inferred from the Executor Binding”; the binding says who is responsible, while the envelope says who acted. Addendum §Non-binding event/port sketch says acting-Party identity and timestamp come from “the **binding + EventStore envelope**.”
- **Failure mode:** An architect following the handoff sketch can stamp the bound Party onto events triggered by a coordinator or Reactor, corrupting the Raw Act/audit model. Calling the sketch non-binding does not remove the risk because it is explicitly handed to architecture as technical depth.
- **Concrete fix:** Change the addendum to say the actor and timestamp come exclusively from authenticated envelope/context metadata; Executor Binding is event payload/state describing responsibility and is never an actor source. Add a negative audit test where actor Party differs from bound Party.

### ADV-M05 — Immutable Raw-Act data has no in-scope privacy/data-minimization contract

- **Severity:** Medium
- **Type:** Product/NFR gap with a recognized architecture dependency
- **Evidence:** FR-7 records verbatim payloads and permits notes; FR-2 stores up to 4,000 characters of Obligation text. §8 requires every historical event remain deserializable. Theme 6 defers consent/residency controls, while §13 only says VAL-H12 privacy lifecycle “is required before production data is admitted.” This gate is not present in §5 Non-Goals, §6 scope, §9 NFR acceptance, or §11 metrics.
- **Failure mode:** Builders can put personal, regulated, secret, or deletion-subject data into immutable events during v1 integration without a normative prohibition or retention/redaction behavior. A buried architecture note is easy for story generation to miss, particularly because the PRD repeatedly markets verbatim Raw Acts as an audit advantage.
- **Concrete fix:** Promote the production-data prohibition and VAL-H12 dependency into §9 as a release gate with an owner and testable operating control. Define allowed data classes for Obligation/note/verbatim payload in v1, minimization guidance, retention/deletion semantics, and behavior on prohibited content. If only synthetic data is allowed, state and enforce that in the harness.

### ADV-L01 — FR-6 prose overstates where terminal Rejected is reachable

- **Severity:** Low
- **Type:** Product-document consistency defect
- **Evidence:** FR-6 says terminal states `Cancelled | Rejected | Expired` are “reachable from **any non-terminal state** per FR-10.” Its normative table and consequences allow Reject only from `Assigned`; requeue Reject does not even rest in `Rejected`.
- **Failure mode:** A story or generated test based on the prose can require rejection from Created, Queued, InProgress, or Suspended, directly conflicting with the normative table.
- **Concrete fix:** Rewrite the sentence to say Cancelled and Expired are reachable from every non-terminal state, while terminal Rejected is reachable only from Assigned through `Reject(Requeue=false)`.

## Gate recommendation

Resolve ADV-C01 and ADV-C02 before generating or amending implementation stories. Resolve ADV-H01 through ADV-H06 before declaring the PRD architecture-ready; each changes observable domain behavior or security semantics. The medium findings can be addressed in the same PRD amendment or converted into explicitly owned architecture gates, but should not remain as implicit assumptions.
