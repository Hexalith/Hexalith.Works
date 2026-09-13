# Architecture Reviewer Gate — Rubric Walker

**Artifact:** ARCHITECTURE-SPINE.md  
**Reviewed:** 2026-09-12  
**Verdict:** **FAIL — the spine is substantially stronger and mechanically complete, but three unresolved correctness contracts can still produce duplicate effects, invisibly stale reads, or contradictory parent/child state.**

## Scope and evidence

This review applied the full good-spine checklist from the BMad architecture reviewer gate. Evidence included:

- the spine and its stable AD-01 through AD-29 decisions;
- the final PRD plus the four post-final overrides in its memlog;
- the current Works projects, recovery readers, host, architecture tests, lifecycle and boundary records;
- the root-tracked Hexalith.Builds and Hexalith.EventStore gitlinks, with the locally advanced checkout distinguished from reproducible root state;
- the deterministic spine linter; and
- current primary release sources for .NET, Aspire, CommunityToolkit Aspire Dapr hosting, and Dapr.

The deterministic pass is clean:

    uv run .agents/skills/bmad-architecture/scripts/lint_spine.py --workspace _bmad-output/planning-artifacts/architecture/architecture-works-2026-09-08

Result: ok=true, total_findings=0.

## Checklist result

| Good-spine criterion | Result | Evidence / judgment |
| --- | --- | --- |
| Fixes the real divergence points for the level below | **Fail** | Most major seams are explicit, but AD-16, AD-21, and AD-26 leave load-bearing failure windows undecided. |
| Every Rule is enforceable and prevents its stated divergence | **Fail** | AD-26 does not define an atomic receipt/outcome boundary or stable effect-ordinal allocation; AD-16 does not define safe abort/read behavior; AD-21 does not close the post-create parent-record race. |
| Deferred contains no unsafe divergence | **Pass** | Provider choice, stable hosting adapter, retention duration, cost-aware time, routing roles, and UI are guarded by explicit production or capability admission conditions. EventStore R4/R6/R7 consumers are blocked on producer/API proof. |
| Named technology is verified-current | **Pass** | Repository pins match the Stack table. Official sources confirm .NET SDK 10.0.401, Aspire.Hosting 13.5.3, CommunityToolkit.Aspire.Hosting.Dapr 13.5.0-preview.1.260825-0345 as preview, and Dapr runtime 1.18.4 as current on the review date. |
| Ratifies rather than contradicts brownfield reality | **Pass with caveat** | CURRENT/TARGET tags, the transitional host paragraph, R1-R11 availability, and the explicit cursor-fix gate distinguish current code from target architecture. AD-17 correctly requires the lifecycle catalog, matrix, code, folds, corpus, and tests to move atomically. |
| Covers the driving spec capabilities | **Pass with one gap** | All FR-1 through FR-26 groups, the security/recovery areas, and the four PRD memlog overrides are mapped. The PRD's provisional rolled-read performance fixture is neither adopted nor deferred in the spine. |
| Does not weaken a parent spine | **Pass** | No parent spine exists; repository constraints are recorded as inherited inputs. |
| Every owned dimension is decided, deferred, or open | **Fail** | Core deployment/security/DR/recovery ownership is strong, but topology capacity, full command authorization, rebuild abort semantics, and projection performance/telemetry remain silent or incomplete. |

## Critical findings

### C-1 — AD-26 does not make the durable effect receipt atomic with the target outcome

**Evidence:** AD-09 and AD-10 depend on AD-26 for crash-safe reissue (lines 168-183). AD-26 says the target persists a receipt for success or rejection, but does not name the storage/API boundary or require the receipt and target outcome to commit atomically (lines 433-447).

**Failure:** If the target append commits and the receipt write does not, a retry can execute the same logical effect again. If the receipt commits first and the append fails, a retry can return a remembered success for an effect that never happened. Actor serialization does not close a crash boundary. In addition, effect ordinal is part of EffectId but no per-family allocation rule is given; a reordered cascade or fan-out can mint a different ID for the same effect.

**Disposition:** **Autofix before finalization.** Bind receipt lookup and outcome persistence to one EventStore transaction/actor-state commit, state the physical owner and replay behavior for success/rejection/no-op, and define deterministic ordinal allocation for every effect family. Make golden vectors a prerequisite for AD-26 consumers, not just a future test note.

### C-2 — AD-16 can acknowledge changes that no readable generation contains, especially on abort

**Evidence:** During Building, writers append only to the catch-up journal and mutate neither active nor staging documents; broker acknowledgement follows journal commit. Commit promotes staging and then drains catch-up, while abort merely deletes staging (lines 239-253).

**Failure:** The active generation becomes stale after the first journaled write, yet the spine only marks the promoted generation stale after Commit. Readers can therefore serve old results as complete during Building. More severely, after abort the acknowledged journaled writes are in neither the active nor staging generation and no rule drains them back into active before the rebuild fence clears.

**Disposition:** **Autofix before finalization.** Decide one protocol: either dual-apply ordinary writes to active plus journal, or mark the active manifest stale/unavailable from the first journal append. Define abort as durable journal replay into active (or another atomic recovery) before stale/readiness fences clear, and define crash ownership plus journal garbage collection.

### C-3 — AD-21 leaves an Attached edge whose parent may permanently reject its record

**Evidence:** WorkItemCreated is the sole attachment evidence; EdgeAttached then drives the parent's token-bearing SpawnChild (lines 342-357). The registry is sole topology truth, but the Work Item still records child references and SpawnChild is lifecycle-governed. A parent can transition to terminal after reservation and before its post-attachment SpawnChild command.

**Failure:** The registry can be authoritatively Attached while the parent stream lacks ChildSpawned. Separate implementations can plausibly keep the edge, detach it, quarantine it, or force a post-terminal event. That ambiguity reaches cascade, resume-on-child, parent narrative, migration equivalence, and repair.

**Disposition:** **Discuss, then encode one binding rule.** Safe options include: reserve a parent lifecycle fence until parent recording completes; make token-bearing post-attachment recording admissible regardless of ordinary lifecycle while preserving status; or explicitly make parent child references a derived/non-authoritative view and remove SpawnChild as topology evidence. Add the parent-terminal race and crash points to the registry proof matrix.

## High findings

### H-1 — The authorization contract is not exhaustive over the command surface

**Evidence:** AD-23 assigns executor-only rules to six acts, special Claim rules to two states, a member floor to Assign/ReEstimate/Reschedule/Cancel, and workload purposes to selected internal acts (lines 385-403). It does not decide root Create, Queue, LinkConversation, external-signal Resume, or clearly separate root Create from registry-authorized child Create. Child-completion Resume is also not named alongside timer Resume.

**Risk:** Edge, EventStore, and Works policy implementations can admit the same command under different principals or origins despite AD-23 claiming end-to-end authority.

**Disposition:** **Autofix.** Add a complete command × lifecycle/origin × minimum-principal matrix, including queries and denial timing, and make the negative security gate execute every row.

### H-2 — Tenant identity canonicalization is named but not defined

**Evidence:** AD-15 says to canonicalize TenantId before validation/key creation (lines 225-237); tenant values also enter AD-11/25/26 hashes, delegation claims, aggregate identity, registry identity, and projection keys. Current components do not expose one obviously identical rule: EventStore AggregateIdentity lowercases tenant IDs before validating its ASCII slug grammar, while shared tenant access validation checks already-trimmed NFC values.

**Risk:** Independently built units can authorize one spelling, route another, and hash/key a third. Canonicalizing an untrusted value before validating equality can also collapse a mismatch that should fail closed.

**Disposition:** **Autofix.** Name one owner and exact canonical grammar/function, require inputs to already equal canonical form at trust boundaries, and reuse its bytes for keys, hashes, claims, payload/envelope comparisons, and golden vectors.

### H-3 — The singleton tenant registry has no capacity or backpressure boundary

**Evidence:** AD-21 chooses one tenant-wide event-sourced Registry aggregate, default depth 32, and explicitly uncapped breadth (lines 342-366). The PRD's only scale fixture is provisional at roughly 1,600 items.

**Risk:** Every topology mutation serializes through one actor and the stream/state grows without a stated edge limit, snapshot strategy, admission budget, latency target, or shard escape. A wide tenant can turn correctness authority into a hotspot or denial-of-service boundary.

**Disposition:** **Discuss or move to an explicit open item.** For v1, bind an admitted maximum active/total edge budget with overload behavior and snapshot/rehydration proof. If unbounded scale is required, decide a partitioned registry and cross-shard acyclicity protocol before implementation.

### H-4 — Reminder and schedule-token byte encodings are not cross-implementation complete

**Evidence:** AD-11 and AD-25 use a “length-prefixed canonical tuple” with textual names and, for ScheduleToken, UTC ticks plus a schedule revision (lines 185-196 and 418-431). Unlike AD-26, neither fixes prefix width, numeric width/endianness, text normalization, Crockford padding/length, or field-tag bytes.

**Risk:** Works, EventStore, and Platform can compute different actor IDs, reminder names, and stale-fire witnesses from the same domain values.

**Disposition:** **Autofix.** Reuse the exact AD-26 binary codec or specify an equally exact versioned codec, including digest rendering, and require shared golden vectors before R6 turns green.

### H-5 — “Never lifecycle-pruned” receipts conflict with offboarding/erasure and are operationally unbounded

**Evidence:** AD-26 says receipts are never lifecycle-pruned (lines 443-447), while AD-28 makes EventStore responsible for tenant-keyed retention/erasure and offboarding approval (lines 463-478).

**Risk:** One team can treat receipts as immortal, another can erase them with tenant data, and a third can retain them outside tenant deletion. Unlimited receipts also make the target aggregate or receipt store grow without a compaction/storage model.

**Disposition:** **Autofix.** Clarify that receipts survive Work Item lifecycle but remain within tenant legal-hold/offboarding policy; define storage partitioning, retention horizon or archived dedup proof, and behavior when an old effect is replayed after permitted erasure.

## Medium findings

### M-1 — The PRD's provisional read-performance fixture is not decided or deferred

The PRD records an indicative depth-32 × fan-out-50 fixture, five-second roll-up convergence, and a rolled read under 200 ms. AD-22 carries only the convergence bound (lines 368-383); neither Readiness Gates nor Deferred addresses read latency or fixture size. **Disposition: autofix** by adopting the provisional fixture as a non-production proof or explicitly deferring it with owner/revisit trigger.

### M-2 — Projection freshness observability is under-specified outside recovery

AD-27 has bounded reason metrics, alerts, and degraded readiness for recovery, but AD-16/22 do not name freshness watermark, catch-up depth/age, CAS retry, quarantine count, or convergence telemetry. **Disposition: autofix** with a minimal metric/readiness set owned by EventStore/Platform.

### M-3 — Assumption acceptance is a finalization blocker, not merely metadata

AD-06, AD-11/12, AD-15/16, and AD-21 through AD-29 remain tagged ASSUMPTION (lines 35-37 and their headings). These are load-bearing data, security, and recovery choices. **Disposition: discuss.** Keep status draft/readiness conditional until the user accepts or changes them; then remove ASSUMPTION without falsely adding CURRENT.

### M-4 — The target topology has proof rows but no single release/rollback dependency graph

The ordering gate gives AD-26 → registry → fan-out → expiry → recovery, while AD-20 has R1-R11 and AD-29 has reader-first schema rollout. Dependencies among schema rollout, tenant migration, Platform parity, data owner approval, and rollback are distributed. **Disposition: autofix** with a short ordered migration DAG or named release phases so separate teams cannot cut over in incompatible order.

## Low findings

### L-1 — The Stack is current, but evidence links are absent from the artifact

The values are correct on the review date: [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [Aspire.Hosting 13.5.3](https://www.nuget.org/packages/Aspire.Hosting/13.5.3), [CommunityToolkit Aspire Dapr](https://www.nuget.org/packages/CommunityToolkit.Aspire.Hosting.Dapr/), and [Dapr 1.18.4](https://github.com/dapr/dapr/releases/tag/v1.18.4). **Disposition: optional autofix** by placing the release evidence in the memlog or source list so the next currentness audit is reproducible.

### L-2 — “Every listed project exists now” can be misread for target-only content

The directories exist, but Registry behavior and generic EventStore R4/R6/R7 seams do not. The next sentence corrects this. **Disposition: ignore or tighten prose** to “every listed project container exists” for scanning clarity.

## What the spine gets right

- The document is concise for its breadth and gives every decision a stable identifier plus Binds, Prevents, and Rule.
- CURRENT/TARGET/ASSUMPTION labels prevent the largest brownfield failure mode: presenting desired architecture as implemented fact.
- The final PRD memlog overrides are carried: WorkItemCreated-before-attach, executor responsibility checks, active handoff, and CorrectProgress/reopen semantics.
- The roll-up model separates self/topology/contribution positions and uses absolute LWW slots instead of additive deltas.
- Recovery names the actual exclusive cursor contract and exposes the two current double-advance defects as a readiness blocker.
- Platform migration has owners, per-seam availability, proof requirements, rollback, and a fail-closed preview policy.
- Production security, audit, quarantine, backup/restore, RPO/RTO, and real-data admission are materially stronger than the preceding architecture validation.

## Gate conclusion

Do not mark the spine final yet. Apply C-1 and C-2 directly; resolve C-3 as a product/architecture choice; then close the high findings or place only genuinely deferrable ones behind explicit admission gates. After assumption acceptance, rerun the deterministic linter and the configured technology-reality and adversarial reviewers.

---

## Recheck — 2026-09-12 after reviewer-fix pass

**Recheck verdict:** **FAIL — the previous receipt atomicity, rebuild abort, terminal-parent consistency, authorization coverage, capacity, codec, and retention gaps are materially closed, but one PRD-critical suspension path and three high ambiguities remain.**

The deterministic linter remains clean with zero findings.

### Prior critical/high closure

| Prior finding | Recheck | Evidence |
| --- | --- | --- |
| C-1 receipt atomicity and ordinal allocation | **Closed except replay disposition below** | AD-26 now gives EventStore a private target-partition inbox, commits receipt with target events/metadata, catalogs immutable ordinals, and gates producers on golden vectors. |
| C-2 rebuild invisible/aborted writes | **Closed** | AD-16 now makes EventStore the epoch/journal owner, leases all writers, marks readers stale from the first journaled write, drains to promoted on commit, and drains to active before abort cleanup. |
| C-3 Attached edge versus terminal parent | **Closed for topology consistency; new capability regression below** | AD-21 makes the token-bearing parent record idempotently admissible in every status, keeps Registry authoritative, and schedules Cancelled/Expired cascade catch-up. |
| H-1 incomplete command authorization | **Mostly closed; Claim binding remains below** | AD-23 now covers the previously omitted external and internal commands, denies unlisted pairs, and places responsibility checks inside the serialized actor turn. |
| H-2 tenant canonicalization | **Contract added, but contradictory lead sentence remains below** | AD-15 now fixes length, grammar, reject-not-transform behavior, and exact bytes. |
| H-3 registry capacity | **Closed** | AD-21 requires a Platform tenant quota, fail-closed admission, snapshots, bounded replay, saturation/backpressure, and load proof. |
| H-4 reminder/token codec | **Closed** | AD-26 now owns one exact codec for AD-11/25/26, including integer/text encoding and 52-character Crockford rendering. |
| H-5 receipt retention versus erasure | **Closed** | AD-26 now scopes receipt survival to Work Item lifecycle, couples it to tenant retention/offboarding, and rejects or quarantines replay below the retained source floor. |

### Remaining critical

#### RC-1 — The terminal-race fix removes FR-16's suspend-parent behavior

AD-21 says the post-attachment SpawnChild bookkeeping act is accepted in every parent status **without changing status or AwaitConditions**. The PRD's UJ-3 and FR-16 require the reserved child payload to optionally suspend the parent awaiting that child's completion; no separate token-bearing suspension effect is defined.

**Required fix:** Make the always-admissible bookkeeping append ChildSpawned in every status, and when the reservation requested suspension, atomically append WorkItemSuspended with ChildCompleted(child) if the current parent state remains suspension-eligible. Define the durable disposition when it is no longer eligible, so Reactor recovery cannot silently report that the requested saga was established.

### Remaining high

#### RH-1 — AD-15 simultaneously says canonicalize before validation and reject transformation

The first AD-15 sentence still instructs implementations to “canonicalize TenantId before validation,” while its new closing rule says trust boundaries reject rather than transform noncanonical input. Those are distinct security behaviors and separately built edges can follow either.

**Required fix:** Replace the lead instruction with “validate that TenantId already equals canonical form before authorization, comparison, or key creation”; normalization is never an admission operation.

#### RH-2 — Duplicate effect lookup does not explicitly return the recorded disposition

AD-26 atomically stores success, rejection, or no-op disposition, but then says the same key and digest “is a no-op.” If a retry of an originally rejected child-create or parent-bookkeeping effect receives generic no-op instead of the recorded rejection, the Reactor can take the wrong recovery branch and strand a reservation.

**Required fix:** On an exact receipt hit, perform no new mutation and return the originally recorded disposition/result bytes; “no-op” describes persistence only, not the observable command outcome.

#### RH-3 — Claim authorization does not fully bind the event's ExecutorBinding

AD-23 binds Queued Claim to the authenticated Party and requires an Assigned claimant to match the current Executor, but Claim carries a complete ExecutorBinding and AD-14 says Claim sets it. The rule does not say whether Assigned Claim preserves the current binding or whether payload Channel/AuthorityLevel/PartyId are derived and validated, so Claim can become an unintended rebind or provenance bypass.

**Required fix:** Assigned Claim derives and preserves the current ExecutorBinding; Queued Claim derives PartyId from AuthorizedCommandContext and admits Channel/AuthorityLevel only through named validated edge policy. The command payload is never authoritative for PartyId.

### Recheck conclusion

Keep the gate failed until RC-1 is restored and RH-1 through RH-3 are made unambiguous. The previous topology, rebuild, codec, capacity, and retention blockers need no further design changes under this lens.

---

## Focused final recheck — 2026-09-12

**Scope:** revised AD-15, AD-21, AD-23, and AD-26 only.  
**Verdict:** **CONDITIONAL FAIL — AD-15, AD-23, and AD-26 close their remaining findings; AD-21 restores the normal suspend path but leaves one high-severity admission state gap.**

### Closed

- **RH-1 closed:** AD-15 now says validate canonical input before key creation and consistently requires reject-not-transform behavior.
- **RH-2 closed:** AD-26 now returns the recorded disposition without redispatch on an exact receipt hit.
- **RH-3 closed:** AD-23 removes caller-provided ExecutorBinding from target Claim and derives PartyId plus Channel/AuthorityLevel from trusted context/policy.
- **RC-1 closed for InProgress, Suspended, and terminal races:** AD-21 now emits ChildSpawned in every state, suspends an InProgress parent, unions the child condition into an already Suspended parent, and defines terminal behavior.

### Remaining high

#### RFH-1 — Suspend-request admission is undefined for Created, Assigned, and Queued parents

AD-21 defines suspend=true bookkeeping for InProgress and Suspended parents and defines terminal behavior, but SpawnChild is otherwise admissible for every parent status. A suspend=true reservation can therefore create and attach a child while its parent is Created, Assigned, or Queued, with no specified rejection, fence, quarantine, or durable “suspension not established” disposition.

**Required fix:** Before ChildCreateAuthorized, a suspend=true reservation must obtain a token-bound parent preparation accepted only from InProgress; that preparation survives a later transition to Suspended and is consumed by post-attachment bookkeeping. Without matching preparation, child creation is not authorized; after authorization, terminal races retain the already-defined attach/cascade behavior.

No other critical or high finding remains in the focused AD-15/21/23/26 scope.

---

## Final AD-21 admission recheck — 2026-09-12

**Verdict:** **PASS — no critical or high finding remains in the ParentAdmissionWitness and suspend-admission contract.**

RFH-1 is closed. AD-21 now obtains and revalidates a ParentAdmissionWitness before EdgeReserved; the witness binds tenant, parent, child, reserved-payload digest, suspend flag, and parent envelope sequence. suspend=true admits only InProgress or Suspended parents, while suspend=false requires any nonterminal state. The remaining post-admission transitions are exactly the InProgress, Suspended, and terminal cases already governed by the attachment rule, and identical-witness retries are explicit no-ops.

No critical or high finding remains under this focused lens.
