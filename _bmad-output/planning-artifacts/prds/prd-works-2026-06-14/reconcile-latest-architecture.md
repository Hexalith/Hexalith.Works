# Reconciliation extract — latest architecture and architecture validation

## Input and authority

- Input 1: [`architecture.md`](../../architecture.md), last updated **2026-09-06**.
- Input 2: [`ARCHITECTURE-VALIDATION.md`](../../architecture-validation-2026-09-12/ARCHITECTURE-VALIDATION.md), dated **2026-09-12**.
- Compared against this workspace's [`prd.md`](prd.md), [`addendum.md`](addendum.md), and [`.memlog.md`](.memlog.md), all current through **2026-09-08**.

The architecture source did not change between its 2026-09-08 and 2026-09-12 validation gates. The latest gate is **FAIL — update required**, with five critical and fourteen high findings. Because the PRD is newer than the architecture, the register is not a source for reversing the 2026-09-08 product decisions. It is useful here only where validation exposes an incomplete, ambiguous, or unsafe product contract.

## Product-level gaps and conflicts

### A1 — Phase blocker: `Attached` currently precedes durable proof that the child exists

**Sources:** validation VAL3-C03 and VAL3-C04; PRD FR-5/FR-16/FR-26; architecture AD-21/AD-22.

The current PRD and architecture say that an edge becomes `Attached` on the parent's `ChildSpawned`, then the child's stream begins with `WorkItemCreated`. The latest validation demonstrates that this permits an authoritative `Attached` edge with no child aggregate, and that AD-22 can acknowledge empty ancestry while the edge is only `Reserved`. The phrase “timeout with no creation evidence” also conflicts with attachment having already occurred on the earlier parent event.

The product contract needs one explicit invariant: an edge must not be usable as `Attached` for Roll-Up, cascade, or authoritative child enumeration until durable evidence proves that the corresponding child exists. It must also state the externally observable disposition of an incomplete attempt after recovery or timeout. The exact event chosen as evidence, registry sequence/witness fields, ordering fence, payload changes, and backfill mechanism are **addendum/architecture-only technical-how**.

**Conflict with canonical memory:** this cannot be silently autofixed. The 2026-09-08 memlog decision for item 1 explicitly adopted `Reserved → Attached` on `ChildSpawned`, followed by `WorkItemCreated`. Changing the attachment evidence supersedes that user-approved decision and requires an override/decision entry.

### A2 — Released reservations and stale saga legs have no product outcome

**Source:** validation VAL3-H01; PRD FR-16 and §10 idempotency.

FR-16 defines timeout release as a routine race and defines duplicate `SpawnChild` only for an already-`Attached` pair. It does not define what happens when a delayed, otherwise authenticated Reactor command arrives after the reservation has been released. As written, late delivery can create parent/child facts that contradict terminal registry topology.

The PRD needs a capability-level outcome: once a reservation is released or superseded, a late saga leg must not create or attach the child or mutate the parent/registry into a contradictory topology; redelivery must be harmless and auditable. Product should decide whether the observable domain outcome is a rejection or a no-op. Reservation IDs, epochs/fencing tokens, gateway validation, and compensation/orphan mechanics are **addendum/architecture-only technical-how**.

### A3 — A stale expiry firing can terminate a rescheduled item

**Source:** validation VAL3-H07; PRD FR-9, FR-10, §6.1, and architecture AD-25.

The PRD requires reminders to be rescheduled when the Due Date changes, but does not say how an already-issued old firing is treated. The validation shows that the current command carries no current-schedule witness, so an October firing could expire work rescheduled to December.

FR-10 needs the product invariant that expiry applies only to the currently effective Due Date/TTL intent; a firing for a superseded schedule must not expire or otherwise mutate the Work Item. The PRD should bind the visible no-op/rejection result and add a stale-fire acceptance case. Due-instant fields, schedule tokens, reminder identity, actor type, indexes, and reconciliation wire shape are **addendum/architecture-only technical-how**.

### A4 — “Crash loses nothing” lacks recovery-horizon, readiness, and rebuild acceptance

**Sources:** validation VAL3-C05, VAL3-H03, VAL3-H04, and VAL3-H11; PRD FR-26, §9 rebuildability, §10 idempotency, SM-1, and SM-6.

The PRD promises that a Reactor crash loses nothing and that read models rebuild from zero, but its acceptance signals do not cover page boundaries, checkpoint restart, recovery that remains unresolved after startup, concurrent writes during rebuild, recovery indexes, or retry after transport-dedup retention. The latest evidence found both recovery readers skipping one event at every multi-page boundary and recovery services able to stop or park work while the host remains Ready.

The product contract needs measurable reliability behavior, without prescribing mechanics:

- recovery and replay must not omit an accepted event at pagination/checkpoint boundaries;
- an unresolved cascade, child resume, or reminder must remain durably discoverable and retried until resolved or explicitly disposed;
- the affected capability must report degraded readiness while such work is stranded, with an operator-visible signal;
- rebuilding or restoring projections must not lose writes accepted during the supported operation, and the pending reminder/recovery discovery state must be restored or deterministically reconciled;
- one logical internal effect must not execute twice anywhere within the supported retry/replay/restore horizon, including after ordinary deduplication retention expires.

SM-1/SM-6 should gain boundary/restart/restore scenarios. Exact cursor arithmetic, page size, checkpoint DTO, idempotency hash/encoding, retention store, rebuild epoch/fence, CAS, alert transport, and readiness implementation are **code/addendum/architecture-only technical-how**. The 2026-09-12 cursor defect itself is an implementation fix, not a new PRD mechanism.

**Memory implications:** the accepted 2026-06-14 decision kept NFRs qualitative and set no numeric targets. A concrete retry horizon, RPO, RTO, or recovery latency therefore requires a new user decision; until then the PRD can state the invariant and mark the numeric budget open rather than inventing it.

### A5 — Data lifecycle and disaster recovery are only a footnote, despite being a production gate

**Source:** validation VAL3-H12; PRD §5, §9, §10, and §13 closing note.

The PRD already says the privacy lifecycle for immutable event data is required before production data is admitted, but it does not turn that sentence into requirements or acceptance. Raw Acts persist verbatim content, so the gap exists before the deferred Theme 6 auditor surface.

The PRD needs an explicit production-readiness NFR and gate covering durable-field classification, retention and legal hold, tenant offboarding/erasure disposition for immutable history, audit evidence and failure posture, secret handling/rotation ownership, backup/restore, disaster-recovery objectives, restore order, and proof by a recovery drill. Numeric retention/RPO/RTO values can remain open pending owner decisions, but non-synthetic shared data and new irreversible durable catalog additions must not pass the gate without them.

**Scope tension requiring confirmation:** the memlog records “no Theme 6 hardening” in v1. The validation treats privacy/retention/backup/DR as a substrate/platform production baseline, not the deferred Theme 6 end-user security surfaces (signed links, step-up authentication, auditor UI). The PRD should make that boundary explicit rather than silently widening Theme 6.

## Validated downstream corrections that do not call for a PRD change

- **Roll-Up contribution and persistence (VAL3-C01/C02/M05):** PRD FR-7, FR-11, and SM-6 already require Raw-Act payloads without derived totals and state-based, per-descendant LWW contribution. Architecture AD-22's claim that the contribution is computable “from the event alone” is incompatible with delta-only progress and terminal-aware folded state. Correct the architecture to derive an absolute contribution from folded descendant state. Persisted slot DTOs, keys, merge ownership, tombstones, atomic visibility, and materialization are **addendum/architecture-only**.
- **Resume semantics (VAL3-H08):** the PRD, lifecycle matrix, live kernel, addendum, and 2026-09-08 memlog agree: a nonmatching resume while Suspended is rejected; only replay of the consumed condition after success is a no-op. Architecture AD-13 is stale and must be corrected. Do not weaken FR-15.
- **Identity/delegation carrier (VAL3-H06):** PRD §9 already states verified identity, explicit tenant delegation, origin restriction, and deny-before-dispatch. The signed carrier shape, issuer, gateway locus, and command-origin matrix belong downstream.
- **Registry wire namespace/control-plane concurrency (VAL3-H02/H05), schema rollout/quarantine (M06–M08), version ownership and preview dependencies (H13/H14), and binding-altitude/frontmatter issues (M01–M04):** these are architecture, implementation, operations, or planning corrections. They should not become PRD implementation detail.
- **Architecture coverage (VAL3-H09/H10):** the current 26-FR PRD, including FR-26, remains the driving product source. Architecture/epics must catch up; the PRD must not be rolled back to their older 25-FR inventory.

## Suggested PRD edit boundary for the parent update

Subject to resolving A1 and the visible outcomes in A2/A3 with the user, the requirement-level changes belong in:

- FR-16/FR-26: child-existence attachment invariant and harmless late-saga behavior;
- FR-10: stale-expiry protection;
- §9/§10: recovery/readiness/rebuild/idempotency invariants and the production data-lifecycle gate;
- SM-1/SM-6: multi-page boundary, checkpoint restart, stranded-work readiness, rebuild/restore, and stale-expiry evidence;
- §13/§14: replace stale validation identifiers and record any newly accepted assumptions/open numeric budgets.

All DTOs, event/witness fields, key formats, transport identities, TTLs, actor/app names, projection merge algorithms, fencing/CAS protocols, and deployment/version choices remain in the addendum or architecture.
