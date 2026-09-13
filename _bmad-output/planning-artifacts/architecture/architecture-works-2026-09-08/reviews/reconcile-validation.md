# September 12 validation reconciliation — Architecture Spine

## Verdict

**FAIL — targeted corrections are required before this draft becomes the final consistency
contract.** The rewrite closes most of the September 12 architecture findings, and it correctly
keeps implementation readiness conditional. One critical implementation defect remains live and
is only recorded as a gate. Five architecture-level closures are incomplete or unsafe: the new
transport identifier is rejected by the current gateway, permanent effect deduplication is not
actually specified, the registry abandonment path is not fenced at command admission, rebuild
generation addressing conflicts with the fixed roll-up key, and reminder/delegation identities
still contain independently implementable placeholders. The executable-story/proof allocation also
remains an external high-severity handoff rather than completed work.

## Scope and method

This reconciliation compared the current
`architecture/architecture-works-2026-09-08/ARCHITECTURE-SPINE.md` against
`architecture-validation-2026-09-12/ARCHITECTURE-VALIDATION.md` and all five Markdown lens reports
under that validation workspace. Current code was read only where the validation report made it the
acceptance authority. No spine or source file was changed by this reconciliation.

## Findings

### R-VAL-01 — CRITICAL — The page-boundary loss is still present; the spine records but cannot close it

**Validation finding:** VAL3-C05 / SEC-DI-0912-01 / CONF-0912-01.

**Evidence**

- AD-27 now states the correct contract: `FromSequence` is exclusive and the next request must reuse
  `LastSequenceReturned` without adding one (`ARCHITECTURE-SPINE.md:403-414`).
- The Readiness Gates correctly require both readers to be fixed and successful multi-page/restart
  tests (`ARCHITECTURE-SPINE.md:513-516`).
- The implementation still performs `lastSequence + 1` in
  `StreamReadingCascadeDescendantSource.cs:83-86` and
  `StreamReadingChildCompletionAwaitingParentSource.cs:152-159`.

**Assessment**

The architecture wording is repaired, but the critical validation finding is not closed. That is
acceptable only while `readiness: conditional` remains visible and no R7, recovery, Story 4.9, or
production acceptance is claimed. It must not be summarized as an architecture-update closure.

**Required reconciliation**

Keep the explicit readiness gate. The implementation handoff must change both cursors, require a
last sequence on a truncated page, add successful 201+-event boundary and restart tests, and re-run
the focused recovery suites before the gate can turn green.

### R-VAL-02 — HIGH — AD-26 mandates a `MessageId` that EventStore rejects and leaves “forever” dedup undefined

**Validation finding:** VAL3-H03 / ADV-0912-08 / SEC-DI-0912-07.

**Evidence**

- AD-26 defines `EffectId` as unpadded base64url and requires both `MessageId` and `IdempotencyKey`
  to be `wrk_<EffectId>` (`ARCHITECTURE-SPINE.md:388-401`).
- EventStore's current `SubmitCommandRequestValidator` requires `MessageId` to match
  `^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?$` and rejects underscores
  (`references/Hexalith.EventStore/src/Hexalith.EventStore/Validation/SubmitCommandRequestValidator.cs:23-40,105`).
  The mandatory `wrk_` prefix therefore makes every such message invalid; base64url itself can also
  contain `_`.
- AD-26 says target state or “natural transition semantics” deduplicates forever, but it does not
  bind which effects persist an effect receipt, which transitions are intrinsically idempotent, or
  how an effect replay after state cycling is recognized (`ARCHITECTURE-SPINE.md:397-401`). The
  90-day gateway record is expressly not the correctness boundary.

**Risk**

The first implementation of AD-21/R6/R7 using this rule cannot pass gateway validation. If the
syntax is repaired without repairing the second clause, independently built targets can still
choose incompatible permanent-dedup mechanisms and reapply a logical effect after the gateway
record expires or a backup is restored.

**Required reconciliation**

Use a gateway-valid deterministic encoding for `MessageId` (for example Crockford base32/ULID-safe
characters with a hyphen prefix) while retaining the opaque `IdempotencyKey` contract. Then enumerate
the target effect families and bind either a durable effect receipt/event field or the exact natural
state transition that proves permanent idempotency for each. Add validator contract tests plus
post-retention and restore replay proof to R11.

### R-VAL-03 — HIGH — AD-21's abandonment escape reopens the late-command race it was meant to close

**Validation finding:** VAL3-C03/C04 and VAL3-H01/H02 / ADV-0912-03 through ADV-0912-07.

**Evidence**

- The rewrite safely makes matching persisted `WorkItemCreated` the sole attachment evidence,
  forbids timeout release after `Creating`, carries a `ReservationId` and monotonic `FencingToken`,
  and makes all topology consumers use `IWorkTreeTopologyReader`
  (`ARCHITECTURE-SPINE.md:309-330`). Those changes close the original attach/freshness ambiguity.
- The same rule permits audited abandonment to release `Creating` after “proving no child evidence”
  and merely says released tokens are terminal (`ARCHITECTURE-SPINE.md:318-322`). It does not bind
  EventStore/Platform admission to verify that the presented reservation token is still current
  immediately before dispatching `CreateWorkItem` or `SpawnChild`.
- Absence of persisted child evidence cannot rule out an already-issued but delayed command. A
  release can therefore commit, followed by that delayed command creating the child. Neither the
  target aggregate nor AD-23's command-origin policy is told to consult the registry fence.
- AD-21 also fixes the registry aggregate id to literal `registry`
  (`ARCHITECTURE-SPINE.md:314-316`), while AD-02 and the IDs convention still say aggregate IDs are
  edge-assigned sortable ULIDs (`ARCHITECTURE-SPINE.md:84-90,449-452`). The intended reserved-identity
  exception is not stated.

**Risk**

The late-command ghost/asymmetric topology construction from ADV-0912-05 remains possible on the
manual-abandonment path, and builders can reject or accept the fixed registry identity depending on
which binding rule they follow.

**Required reconciliation**

Bind token validation to the EventStore gateway/admission stage immediately before aggregate
dispatch; a released or superseded token must be rejected/quarantined and never reach `Handle`.
Define abandonment as revoking/fencing the token before release, with an explicit late-command/orphan
outcome. Add an explicit reserved-system-aggregate exception to AD-02/the ID convention (or select a
stable ULID-compatible registry identity) and require stale-token race tests.

### R-VAL-04 — HIGH — AD-06's fixed roll-up key cannot compose with AD-16's staged generation protocol

**Validation finding:** VAL3-C01 and VAL3-H04 / ADV-0912-01/09 / SEC-DI-0912-04.

**Evidence**

- AD-06 now binds one exact roll-up document key per `(tenant, ancestor)`:
  `works:v3:tenant:<tenant>:rollup:<ancestor>` (`ARCHITECTURE-SPINE.md:117-130`). This is a material
  improvement over the validated document.
- AD-16 requires a separately staged complete tenant/family candidate and atomic manifest promotion
  (`ARCHITECTURE-SPINE.md:223-234`).
- The AD-06 key has no epoch/generation segment, and neither rule says that the value contains
  generation partitions or that readers resolve a physical key through the manifest. Consequently
  the live and staged roll-up documents cannot coexist under the bound key as written.
- AD-16 also says stale writers refresh and retry, but does not say whether a refreshed writer targets
  the old live generation, the staging generation, both, or waits. Those choices change whether an
  acknowledged post-capture update survives promotion.

**Risk**

Works and EventStore can each implement a locally coherent reading of AD-06/AD-16 that cannot
compose. Promotion can overwrite an acknowledged live slot, or readers can expose a partial staged
family, recreating the loss/visibility construction the rebuild fence was intended to prevent.

**Required reconciliation**

Bind logical versus physical key resolution: include the generation/epoch in physical roll-up and
other family keys, make the manifest the sole reader selector, and state exactly where refreshed live
writes land during Begin-through-Commit. Require prior-generation visibility or explicit
unavailable/stale status until catch-up completes, and prove the live-writer race plus reminder-index
restore in R4.

### R-VAL-05 — HIGH — Reminder and expiry identities still use undefined hash/token contracts

**Validation finding:** VAL3-H07 / ADV-0912-12 / SEC-DI-0912-08.

**Evidence**

- AD-11 binds the app and actor type but defines the actor id only as
  `wrk_<hash(tenant,item)>` (`ARCHITECTURE-SPINE.md:171-180`). It does not bind the hash algorithm,
  tuple framing, canonical byte encoding, output encoding, or collision behavior.
- AD-25 requires a “deterministic ScheduleToken” but likewise does not define its derivation or state
  whether it reuses AD-26's EffectId (`ARCHITECTURE-SPINE.md:377-386`).
- Scheduler registration, reconciliation, callback handling, and aggregate validation are owned by
  different units. They therefore can make incompatible but superficially compliant choices for
  both identifiers.

**Risk**

One unit can register an intent under an actor/token that another unit cannot rediscover or match.
That preserves the stale-fire and split-actor failure even though the DTO now carries the right
fields.

**Required reconciliation**

Define one canonical length-prefixed tuple and a named encoding for the actor id and ScheduleToken,
or explicitly derive both from the corrected AD-26 function with distinct effect-kind/domain
separation. Bind collision handling and require registration/reconciliation/stale-fire golden tests.

### R-VAL-06 — HIGH — The delegation carrier still has no named issuer or verifiable command binding

**Validation finding:** VAL3-H06 / ADV-0912-11 / SEC-DI-0912-02.

**Evidence**

- AD-23 binds JWT claims and makes EventStore the validation/origin-policy locus
  (`ARCHITECTURE-SPINE.md:346-362`).
- It says internal calls “carry” the JWT but does not name the component that issues/signs it, even
  though issuer ownership was a required closure in the validation report. The update memlog says
  Platform issues it, but that decision did not survive distillation.
- The token binds tenant, purpose, causation, and EffectId, but not the canonical command digest or
  explicit target. AD-26's EffectId includes the target only in a derivation whose source tuple is not
  carried for gateway recomputation. A stolen/misissued valid token can therefore authorize altered
  bytes on their first admission; the same-key/different-digest conflict only helps after a first
  record exists.

**Risk**

Platform and EventStore can each assume the other is the delegation issuer, and the gateway cannot
unambiguously verify that the presented delegation was minted for the exact command it dispatches.

**Required reconciliation**

State that Platform's named workload-delegation service is the sole issuer and owns signing-key
rotation/discovery. Bind the token to target domain, target aggregate, command type, and canonical
command digest (or carry the complete AD-26 tuple so EventStore can recompute and compare EffectId).
Name the attested Dapr caller signal EventStore compares with the app-id claim and retain the negative
matrix in AD-24.

### R-VAL-07 — HIGH — Executable story and proof allocation remains unfinished, though the spine no longer hides it

**Validation finding:** VAL3-H10/H14 / RW12-04 / CONF-0912-02 through CONF-0912-04 and
CONF-0912-08.

**Evidence**

- AD-20 correctly marks R4, R6, and R7 absent, keeps the Works host until parity, and requires a
  producer artifact, package/API test, consumer story, and proof command before a row turns green
  (`ARCHITECTURE-SPINE.md:282-307`).
- The Readiness Gates correctly preserve ordered delivery, platform, security, production-data, and
  CI gates (`ARCHITECTURE-SPINE.md:509-520`).
- The spine still names no actual producer issue/artifact, consumer story, or proof command for the
  absent rows. It also does not update Story 4.9 or create the Registry, fan-out, automatic-expiry,
  and Reactor-recovery stories required by the validation disposition.

**Assessment**

The architecture now represents the risk honestly, so this is not a reason to weaken its target
decisions. It is nevertheless an unclosed high-severity handoff: the update cannot claim that
VAL3-H10/H14 are closed or that implementation is ready.

**Required reconciliation**

Keep `readiness: conditional`; do not promote the assumptions to adopted implementation reality.
Run the requested correct-course/epics update before affected implementation and map every R-row and
AD-23/24 proof to a named producer artifact, consumer story, exact command, owner, and acceptance
evidence.

### R-VAL-08 — MEDIUM — Architecture-fitness truth and CI execution remain prospective but are labelled adopted

**Validation finding:** VAL3-M03/M04 / RW12-09/10 / CONF-0912-09/13.

**Evidence**

- AD-18 `[ADOPTED]` says architecture fitness owns the complete allowlist and fails every new project
  edge (`ARCHITECTURE-SPINE.md:246-253`). The current dependency test's exact allowlist governs only
  Contracts, Server, Projections, and Reactor; it does not govern the executable, Platform, or the
  testing edges drawn immediately below the decision
  (`tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs:7-26`).
- The CI gate says the architecture-test assembly must be explicitly executed, but names neither the
  owning workflow nor the exact command (`ARCHITECTURE-SPINE.md:520`). The validated repository had
  build evidence and a manual direct run, not a wired CI execution.

**Risk**

An automated consumer can treat an unimplemented target assertion as ratified machine truth, while
new executable/test edges or a compile-only CI lane drift without failing.

**Required reconciliation**

Either narrow AD-18 to the four projects currently governed and add a conditional readiness item for
the future edges, or remove `[ADOPTED]` until the complete source-project policy is executable. Name
the workflow/owner and exact architecture-test execution command in the CI proof handoff.

## Validation findings safely closed at architecture level

The current spine materially and safely closes the architecture wording for VAL3-C02/C04, H02's
topology-reader authority, H05's canonical namespaces/CAS ownership, H08's resume contradiction,
H09's 26-FR metadata and core cross-unit rules, H11's periodic degraded recovery contract, H12's
privacy/audit/DR ownership and real-data gate, H13's current/target version authority, and M01/M02,
M05-M09. VAL3-C01/C03, H01/H03/H04/H06/H07 are only partially closed for the reasons above.
VAL3-C05, H10, and H14 remain explicit implementation/planning gates rather than architecture-text
closures.

## Finalization condition

Do not set `status: final` until R-VAL-02 through R-VAL-06 are corrected or explicitly deferred with
a revisit condition that precedes every dependent story. R-VAL-01, R-VAL-07, and R-VAL-08 may remain
external readiness gates only if the final spine continues to say `readiness: conditional` and the
handoff names their accountable work and proof.
