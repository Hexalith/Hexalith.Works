# Security and Data-Integrity Architecture Review — 2026-09-12

**Artifact:** `_bmad-output/planning-artifacts/architecture.md` (1197 lines; AD-01…AD-25 at lines 26–455 is the binding spine).  
**Mode:** standalone Validate; security/data-integrity reviewer gate; no spine or source file was changed.  
**Evidence posture:** full spine, current Works implementation/spec evidence at `0cbc7c4`, the relevant `5c86eab` reminder-recovery change, and checked-out `references/Hexalith.EventStore` at `6b0247ac` were inspected before reconciliation with the 2026-09-05 and 2026-09-08 security reviews.

**Verdict: REJECT / NOT STORY-4.9-ACCEPTANCE OR PRODUCTION READY.** The decision register remains a useful conditional plan, and AD-23/AD-24 are materially better than the pre-2026-09-06 spine. It is not safe to accept the architecture's implementation-readiness claim, however: both recovery stream readers deterministically skip one authoritative event at every page boundary, while the previously open identity, recovery, rebuild-fence, privacy, registry, and idempotency obligations remain unenforced. The cursor defect is a silent loss of domain side effects, not merely an operational concern.

## Finding summary

| ID | Severity | Prior status | Disposition | Title |
|---|---|---|---|---|
| SEC-DI-0912-01 | **Critical** | Newly detected (pre-existing code) | **Autofix; block acceptance** | Cascade and child-completion recovery skip every page-boundary event |
| SEC-DI-0912-02 | **High** | Persistent residue of SEC-DI-01/02 and SEC-01/02/05/06/07/13 | **Discuss; block production** | Tenant delegation and trusted workload origin are specified but not operable end to end |
| SEC-DI-0912-03 | **High** | Persistent SEC-DI-05 / SEC-08; partially improved by `5c86eab` | **Autofix; block R6/R7/R8 acceptance** | Recovery exhausts or parks work while readiness remains healthy |
| SEC-DI-0912-04 | **High** | Persistent VAL-H08 | **Discuss + autofix; block R4 acceptance** | Atomic promotion has no capture-to-Commit fence, and reminder discovery is outside rebuild |
| SEC-DI-0912-05 | **High** | Persistent SEC-DI-06 / VAL-H06/H09; new bypass detail | **Discuss; block R6/R7 design acceptance** | Global registries and cascade checkpoints lack a governed integrity/ownership protocol |
| SEC-DI-0912-06 | **High** | Persistent SEC-DI-04 / SEC-04/10/11 | **Discuss; block real data** | Privacy, audit, retention, secrets, backup and disaster-recovery obligations are still unbound |
| SEC-DI-0912-07 | **High** | Persistent VAL-H10 / SEC-01 | **Discuss; block AD-21 story draft** | Internal command idempotency and tenant delegation do not survive the required replay horizon |
| SEC-DI-0912-08 | **High** | Persistent SEC-03 | **Autofix; block AD-25 story draft** | Expiry commands still lack the due-instant witness needed to reject stale reminder firings |
| SEC-DI-0912-09 | **Medium** | Persistent SEC-DI-07 / SEC-09; partially improved by `5c86eab` | **Autofix** | Malformed/unknown evidence still has incompatible terminal dispositions |
| SEC-DI-0912-10 | **Medium** | Newly detected | **Autofix** | Recovery stream readers incompletely validate returned stream and payload identity |
| SEC-DI-0912-11 | **Medium** | Persistent SEC-06/11/13 | **Discuss + defer to R10 only with a hard gate** | Production security posture is a policy assertion, not a fail-closed deployment condition |

Counts: **1 Critical, 7 High, 3 Medium, 0 Low**.

## Critical finding

### SEC-DI-0912-01 — Recovery skips every page-boundary event

**Status:** newly detected relative to both prior reviews; the faulty code predates them.

**Binding conflict.** AD-09 forbids ordering/delivery assumptions (`architecture.md:119-126`); AD-10 requires checkpoint recovery from re-readable evidence (`:128-135`); AD-13 requires first-match, idempotent child completion resume (`:159-164`); R7 explicitly requires cascade and missed-child-completion crash recovery (`:266`). The EventStore contract defines `StreamReadRequest.FromSequence` as an **exclusive** lower bound (`references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Streams/StreamReadRequest.cs:8-23`). Its controller test proves `FromSequence: 1` returns sequences 2 and 3, with last sequence 3 (`references/Hexalith.EventStore/tests/Hexalith.EventStore.Server.Tests/Controllers/StreamsControllerTests.cs:94-123`).

**Failure.** The correct pending-date reader advances with `from = lastSequence` and explains the exclusivity (`src/Hexalith.Works/Reminders/PendingDateAwaitStreamReader.cs:82-96`). In contrast:

- `StreamReadingCascadeDescendantSource` sets `from = lastSequence + 1` (`src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs:75-86`). With a 200-event page ending at 200, the next exclusive read begins after 201, so event 201 is never observed. A page-boundary `ChildSpawned` is omitted from the checkpoint; the code itself records that an incomplete descendant checkpoint permanently skips undiscovered targets (`:89-96`).
- `StreamReadingChildCompletionAwaitingParentSource` repeats the same double advance (`src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs:127-171`). A page-boundary `WorkItemCreated`, `WorkItemSuspended`, resume, or terminal event can therefore invent or erase the awaiting-parent decision.

The focused suites pass because they exercise single pages and the exhausted-page-budget branch, not a two-page boundary (`tests/Hexalith.Works.IntegrationTests/StreamReadingCascadeDescendantSourceTests.cs:34-120`; `tests/Hexalith.Works.IntegrationTests/StreamReadingChildCompletionAwaitingParentSourceTests.cs:30-149`). The EventStore controller test also contains a stale comment saying callers use `last + 1` (`StreamsControllerTests.cs:120-122`), contradicting both its own observed behavior and the public request contract; this is likely the propagation source.

**Impact.** The defect silently loses cancel/expire cascade targets or child-completion resumes under ordinary stream growth. No exception, retry, health signal, or rebuild reveals the skipped event. Redelivery cannot repair a checkpoint already created from the incomplete set.

**Disposition — Autofix, block acceptance.** Change both readers to require a last sequence on every truncated page and reuse that exact value as the next exclusive lower bound. Add contract tests with at least 201 events placing each state-affecting event class at sequence 201, assert all issued cursors, and delete/correct the contradictory EventStore test comment. Do not accept R7 or production recovery until the tests fail against the current code and pass against the fix.

## High findings

### SEC-DI-0912-02 — Tenant delegation and trusted workload origin remain non-operative

**Status:** persistent residue of 2026-09-05 SEC-DI-01/02 and 2026-09-08 SEC-01/02/05/06/07/13. AD-23/AD-24 closed the *invariant wording*, not the enforcement path.

**Evidence.** AD-23 correctly requires verified claim-to-tenant membership and an explicit auditable tenant delegation for internal actors (`architecture.md:344-368`); AD-24 requires mTLS, network isolation, exclusive producers/callers and provenance before trusting sequence (`:370-395`). External EventStore command ingress is now `[Authorize]`, takes actor identity only from JWT `sub`, and runs tenant/RBAC checks (`references/Hexalith.EventStore/src/Hexalith.EventStore/Controllers/CommandsController.cs:19-24,65-124`; `.../Authorization/ClaimsTenantValidator.cs:13-51`; `.../Pipeline/AuthorizationBehavior.cs:22-131`) — useful defense.

The internal path still violates the binding:

- allow-listed `dapr-caller-app-id` becomes `sub=system:<app>` **plus `global_admin=true`** (`references/Hexalith.EventStore/src/Hexalith.EventStore/Authentication/DaprInternalAuthenticationHandler.cs:9-45`); global administrators bypass all tenant membership checks (`.../Authorization/ClaimsTenantValidator.cs:22-25`). There is no per-submission `{workload, tenant, purpose, causation}` delegation evidence.
- `EventStoreGatewayWorkCommandSubmitter` sends tenant as a body field and no delegation context (`src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs:19-32`).
- Works maps `/project`, `/work/events`, and actor routes without application-channel authentication (`src/Hexalith.Works/Runtime/WorksHost.cs:103-135`); the subscription explicitly declares topology as its only protection (`src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:13-29`).
- `DateReminderActor.ScheduleResumeAsync` accepts a registration without comparing its `(tenant,item)` with the current actor id, and firing trusts stored registration (`src/Hexalith.Works/Reminders/DateReminderActor.cs:33-76`). Sidecar ACLs cannot by themselves authenticate pub/sub or local actor callbacks.

**Impact.** A compromised internal Works identity can act in every tenant; a direct app-port caller can inject a self-consistent high-sequence delivery or actor state if the promised network boundary is absent; audit sees `system:works`, not the tenant-scoped delegated authority. AD-22 amplifies one accepted forged contribution to all ancestors.

**Disposition — Discuss; block production.** Bind one delegation token/claim shape and issuer, prohibit `global_admin` for workloads, persist workload + delegated tenant + purpose + causation on the envelope/audit record, enforce `(caller, command type)` at the gateway, validate a Dapr app-channel token on every SDK-owned domain route, and enforce local-sidecar-only network reachability. Actor methods must verify the actor id equals the deterministic tenant/item target. R10 needs direct-port, wrong-app, cross-tenant delegation, actor-id mismatch and forged-high-sequence negatives on an attested production-like topology.

### SEC-DI-0912-03 — Recovery can stop permanently while the host remains Ready

**Status:** persistent SEC-DI-05/SEC-08; **partially improved, not closed**, by the `0527d12`/`5c86eab` parking sequence.

**Evidence.** R8 says exhaustion means durable evidence plus degraded readiness (`architecture.md:267`), while VAL-H07 leaves continuous retry/alerting to R6 (`:446`). The current reminder service makes five one-second startup attempts by default and returns after the final failure (`src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:30-61`; `src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs:47-51`). Cascade recovery is one startup pass whose top-level and per-entry errors are logged and swallowed (`src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:19-32`; `.../CascadeRecoveryReconciler.cs:35-99`). No recovery state contributes to readiness.

`5c86eab` usefully converts repeat projection poison into a durable per-aggregate parking record and makes ETag transforms copy-on-write (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:143-159,590-655`). Current reminder discovery skips a parked aggregate as a clean candidate, not an incomplete scan (`src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:105-141`). There is no unpark/replay path; the project ledger records both the permanent five-second exhaustion and missing unpark path (`_bmad-output/implementation-artifacts/deferred-work.md:860-865,870-875`). This preserves unrelated-item liveness but can strand the parked item's date await forever while reporting a healthy pass.

**Disposition — Autofix; block R6/R7/R8 acceptance.** Run recovery on a durable schedule independent of restart; after bounded immediate retries, create tenant/item/capability-scoped unresolved-work evidence, degrade a named readiness signal, meter and alert it, and require authenticated audited disposition. A parked item must remain explicitly degraded until repaired and replayed; skipping it must not convert it into a healthy reconciliation result.

### SEC-DI-0912-04 — Rebuild promotion is atomic locally but not consistent with live writers or reminder discovery

**Status:** persistent VAL-H08 and the prior shared-rebuild residual risk.

**Evidence.** AD-16 promises a fence from inventory capture through Commit but admits that the concrete token/epoch/watermark/catch-up protocol is absent (`architecture.md:184-194`); R4 blocks acceptance on fenced rebuild scenarios (`:263`). The handler only accumulates and emits a plan (`src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs:26-90`); the manifest uses `LastWrite` for current documents (`.../WorkItemSharedRebuildManifestBuilder.cs:127-156`). The boundary record explicitly says the handler cannot prevent a concurrent ordinary `/project` writer (`docs/eventstore-api-surface-constraints.md:137-152`), and the completed spec calls quiescence an unenforced precondition with only in-process atomicity proof (`_bmad-output/implementation-artifacts/spec-shared-rollup-reconciliation.md:253-265`).

The promoted manifest covers current what's-next and roll-up data only. Pending-date index, global tenant registry, and parking records are outside it (`spec-shared-rollup-reconciliation.md:15-29`; `WorkItemSharedRebuildManifestBuilder.cs:127-156`). Yet reminder recovery returns empty when the registry is lost or empty (`IndexedPendingDateAwaitSource.cs:47-55`), and EventStore exposes no cross-tenant enumeration. Thus raw streams are called authoritative, but the discovery structures required to find those streams cannot be reconstructed by this rebuild.

**Impact.** A concurrent ordinary writer can be overwritten by promotion or overwrite the promoted generation. Restore/rebuild can produce query-consistent roll-ups while silently losing the ability to discover pending reminders or preserving orphaned parked/index entries.

**Disposition — Discuss + autofix; block R4 acceptance.** Bind a concrete fence protocol with epoch, admitted-writer validation, capture watermark, atomic promotion and post-commit catch-up proof. Add a separate atomic/reconcilable generation and rebuild/restore procedure for reminder registry/index/parking, or prove an alternate authoritative enumeration. Test live-writer races and a full state-store loss/restore, not only an in-process batch.

### SEC-DI-0912-05 — Global recovery control-plane state has no complete ownership or concurrency protocol

**Status:** persistent SEC-DI-06 / VAL-H06/H09; the mixed-case poisoning route is newly identified.

**Evidence.** The reminder tenant registry is a global append-only singleton containing every tenant id (`src/Hexalith.Works/Projections/PendingDateAwaitTenantRegistry.cs:3-11`) at `projection:works:pending-date-await:tenants` (`WorksReadModelKeys.cs:98-105`). The cascade index is another global singleton (`src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:20-22`). Per-cascade checkpoints are read, checked and then saved without an ETag/CAS (`:39-80`); the assertion that the dispatcher is the single writer is not enforced. Two replicas can race target state or completion even though deterministic commands limit duplicate domain effects (`CascadeDispatcher.cs:170-209`). AD-20 correctly leaves CAS/replica ownership open at R7 and namespace ownership at design review (`architecture.md:445-448`).

`5c86eab` added a reserved tenant defense, but `/project` checks the raw string before `TenantId` canonicalizes it (`WorkItemProjectionDispatcher.cs:111-117`; `src/Hexalith.Works.Contracts/ValueObjects/TenantId.cs:10-14`). `IsReservedTenantId` is ordinal (`WorksReadModelKeys.cs:65-69`). A handcrafted `TENANTS` request passes the first guard, normalizes to `tenants`, writes roll-up/index state, registers `tenants` in the global registry, and only later throws when the per-tenant pending index key is derived (`WorkItemProjectionDispatcher.cs:240-287,510-578`; `WorksReadModelKeys.cs:89-96`). Every later reminder scan then encounters the poisoned registry entry and reports an incomplete pass; SEC-DI-0912-03 converts that into a five-second, host-lifetime failure.

**Disposition — Discuss; block R6/R7 design acceptance.** Canonicalize once before every admission check; mutation-test all case variants. Partition discovery by tenant where possible. Any global exception needs an isolated store/ACL or enforceable key ownership, CAS on every mutation, replica leadership or monotonic merge, crash-window reconciliation, bounded enumeration, offboarding/pruning, and immutable audit of reads/writes/dispositions.

### SEC-DI-0912-06 — Privacy, audit, secrets and DR are still not architecture decisions

**Status:** persistent SEC-DI-04 and 2026-09-08 SEC-04/10/11.

**Evidence.** The spine says only “never log payloads/personal data/secrets” (`architecture.md:843-848`) and defers classification, retention and erasure to VAL-H12 before production (`:451`). Raw acts persist verbatim (`:782-786`), including free text and envelope subject identity. “Audited” is required for delegation/operator action (`:356-362`) and registry repair (`:303-308`) without defining record shape, immutable sink, failure posture, retention or legal hold. Scheduler backup is named but no RPO/RTO, restore ordering, key restore/rotation, or drill evidence is bound (`:146-149,265`). The checked-out EventStore now exposes crypto-shredding and restored-backup admission primitives, but Works/Platform has not selected key granularity, classified Works fields, or bound those primitives to read models, DLQs, snapshots and backups.

The repository's Redis passwords are intentionally empty **local-development** configuration (`src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml:1-31`; `.../pubsub.yaml:1-43`); that is not itself a production defect. The architecture nevertheless names no production secret-store, rotation or restore owner. Root `Directory.Build.props:15` still disables NuGet audit, with no repository CI workflow supplying a compensating vulnerability gate.

**Disposition — Discuss; block non-synthetic shared data.** Before the next durable catalog addition or any real shared-environment data: classify every durable field; decide field/reference minimization and tenant/subject key granularity; bind encryption and crypto-shredding across streams/snapshots/read models/DLQs/backups; specify offboarding, retention/legal hold and backup deletion; define an immutable audit event `{actor/workload, delegation, tenant, action, target, causation, outcome, time}` and sink; name secret stores/rotation; set RPO/RTO and prove restore/DR drills. Re-enable NuGet audit or add a documented CI-equivalent gate.

### SEC-DI-0912-07 — Transport idempotency is shorter-lived and less explicit than recovery

**Status:** persistent VAL-H10 and SEC-01; already declared a blocker before the AD-21 story (`architecture.md:449`).

**Evidence.** R11 requires a binding for MessageId/IdempotencyKey reuse, retention and replay result (`architecture.md:270`). Works sends deterministic causation as `MessageId` but leaves the now-available `IdempotencyKey` null (`src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs:19-32`; `references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Commands/SubmitCommandRequest.cs:14-26`). EventStore's default terminal idempotency retention and command-status TTL are both 24 hours (`references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/IdempotencyRetentionOptions.cs:3-12`; `.../Commands/CommandStatusOptions.cs:3-11`). Reminder and recovery redelivery can occur beyond that horizon. Current resume/terminal target commands remain aggregate-idempotent, which limits present damage; AD-21's reserve→spawn→release saga cannot rely on that incidental property.

**Disposition — Discuss; block AD-21 draft.** Define one opaque stable key derivation per logical effect, the minimum retention against Scheduler/backup/DR replay horizon, expired-key behavior, conflict behavior when payload/tenant/target differs, and the replayed terminal result. Persist delegation identity in the admitted intent. Acceptance must replay each internal effect after the configured retention boundary and through backup/restore.

### SEC-DI-0912-08 — Stale expiry firings remain capable of expiring rescheduled live work

**Status:** persistent 2026-09-08 SEC-03; architecture.md has not changed since that review.

**Evidence.** AD-25 makes reminder registration/cancel/reschedule separate adapter actions and says firing emits `ExpireWorkItem`, but idempotency is defined only against already-terminal state (`architecture.md:397-416`). The reminder identity includes the correlation key/instant (`:756-762`), so rescheduling creates a different registration. Best-effort unregister explicitly treats cleanup failure as safe only because **resume** is idempotent (`src/Hexalith.Works/Reminders/DateReminderActor.cs:73-89`). AD-25's future expiry command has no due-instant witness for pure `Handle` to compare with current state.

**Impact.** Old reminder R1 can fire after a due-date move created R2; the still-live item expires, then a perfectly durable cascade propagates the incorrect terminal decision.

**Disposition — Autofix; block AD-25 draft.** Require `ExpireWorkItem(DueInstant)` and a pure equality check against current due state; stale/foreign fires must no-op or reject with durable evidence. Add reschedule→old-fire, cancel-failure, redelivery and restore-replay scenarios to R6. Bind the analogous `DateReached(instant)` witness explicitly.

## Medium findings

### SEC-DI-0912-09 — Quarantine dispositions remain inconsistent

**Status:** persistent SEC-DI-07/SEC-09; partially improved by `5c86eab` on `/project` only.

Subscription decoding or identity failures are completed and returned as HTTP 200 (`src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:99-138,304-320`; `WorksDomainEventEndpointExtensions.cs:69-82`). Projection state-affecting failures retry, then create a small parking document and acknowledge (`WorkItemProjectionDispatcher.cs:143-159,590-655`). Recovery silently excludes that parked item (`IndexedPendingDateAwaitSource.cs:115-126`); shared rebuild uses a third “incomplete relationship” shape. The register still contains no uniform quarantine rule (`architecture.md:436-453`).

**Disposition — Autofix.** Bind state-affecting malformed evidence to durable tenant-scoped quarantine with source hash/sequence/type/reason, degraded capability state, no reactor side effect, and authenticated audited replay/disposition. An allowlisted non-state-affecting unknown may be skipped, but it must still leave durable evidence. Preserve `5c86eab`'s reduced blast radius while adding lifecycle and health.

### SEC-DI-0912-10 — Recovery stream evidence is not uniformly identity-checked

**Status:** newly detected.

The pending reader verifies returned tenant/domain/aggregate and every payload identity (`PendingDateAwaitStreamReader.cs:47-73`). Cascade discovery checks neither the returned page identity nor decoded `ChildSpawned` tenant/aggregate before creating descendants (`StreamReadingCascadeDescendantSource.cs:52-72`). Child-completion recovery verifies page tenant/aggregate but omits domain and silently ignores every undecodable event (`StreamReadingChildCompletionAwaitingParentSource.cs:129-149`). Restored/corrupt/wrong-route evidence can therefore drive commands or suppress them differently across recovery surfaces.

**Disposition — Autofix.** Share one strict stream-page validator/fold: exact tenant/domain/aggregate; monotonic sequence; payload identity; state-affecting decode failure is not skipped. Add foreign-domain, foreign-payload, malformed-state-event and wrong-page-identity tests for both sources.

### SEC-DI-0912-11 — Production controls are not fail-closed configuration

**Status:** persistent 2026-09-08 SEC-06/11/13.

The local Works ACL is deny-by-default and explicitly labels its self-hosted trust values local-only (`src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml:1-50`) — positive sandbox hygiene. But AD-23 merely says production ingress is prohibited until controls/tests are live (`architecture.md:366-368`); AD-24 permits named dev relaxations but provides no machine-readable production profile or startup/admission gate (`:389-395`). No evidence in this repository proves production trust-domain/namespace values, network policy, broker TLS ACL, app-channel token, secret store or the negative suite.

**Disposition — Discuss + conditional defer to R10.** `Hexalith.Platform` must own a machine-readable production security profile and refuse deployment/startup when any required control is absent; the conformance result must be an admission artifact, not a comment. Run the negative suite in an attested topology, not only self-hosted Sentry.

## Reconciliation with the 2026-09-05 SEC-DI gate

| Prior finding | 2026-09-12 status |
|---|---|
| SEC-DI-01 tenant/actor provenance | **Closed at invariant level by AD-23; enforcement residue persistent** as SEC-DI-0912-02/07. |
| SEC-DI-02 trusted event/reminder/replay origin | **Closed at invariant level by AD-24; enforcement residue persistent** as SEC-DI-0912-02/11. |
| SEC-DI-03 authoritative tree ownership | **Closed in planning by AD-21, not implemented.** Direct `SpawnChild`/registry origin and transport idempotency remain blocked by SEC-DI-0912-02/07. |
| SEC-DI-04 privacy lifecycle | **Persistent** as SEC-DI-0912-06; no binding was added after 2026-09-08. |
| SEC-DI-05 recovery exhaustion | **Persistent, partially improved locally** as SEC-DI-0912-03. |
| SEC-DI-06 global recovery registries | **Persistent; risk clearer** as SEC-DI-0912-05 and the DR half of 0912-04. |
| SEC-DI-07 uniform quarantine | **Persistent, partially improved on `/project` only** as SEC-DI-0912-09. |

## Reconciliation with the 2026-09-08 security review

| Prior finding | 2026-09-12 status |
|---|---|
| SEC-01 delegation shape/global-admin internal identity | **Persistent** → 0912-02/07. |
| SEC-02 origin rules not expressible by sidecar ACL alone | **Persistent** → 0912-02/11. |
| SEC-03 stale expiry reminder | **Persistent** → 0912-08. |
| SEC-04 privacy decision too late | **Persistent** → 0912-06. |
| SEC-05 membership/identity transport/global-admin ambiguity | **Persistent** → 0912-02. EventStore gateway defenses improved in the current submodule but do not supply Works workload delegation. |
| SEC-06 sandbox mTLS is not production attestation | **Persistent** → 0912-11. |
| SEC-07 forged high-sequence projection | **Persistent** → 0912-02; AD-22 fan-out would increase blast radius. |
| SEC-08 recovery fail-open | **Persistent** → 0912-03. |
| SEC-09 dropped quarantine | **Partially improved, still persistent** → 0912-09. |
| SEC-10 audit shape/sink absent | **Persistent** → 0912-06. |
| SEC-11 supply chain/secrets silent | **Persistent** → 0912-06/11; `NuGetAudit=false` remains. |
| SEC-12 cross-tenant registries | **Persistent and raised to High with new integrity evidence** → 0912-05. |
| SEC-13 production prohibition unenforced | **Persistent** → 0912-11. |

## `5c86eab` reminder-recovery disposition

The change deserves credit for containing poisoned projection evidence: known deserialization/identity faults reach bounded parking rather than an infinite `/project` 500 loop; already-parked dispatches are not rewritten; registry/index transforms use distinct projection identities and copy-on-write ETag-safe transforms; exact normalized `tenants` command ingress is rejected. Those are real liveness and data-integrity improvements.

It does **not** close the reviewer-gate findings. Parked candidates are excluded from reminder truth without readiness degradation or an unpark/audited replay lifecycle (0912-03/09); raw mixed-case `/project` tenant admission occurs before canonicalization and can poison the global registry (0912-05); reminder reconciliation itself remains one startup budget; neither recovery cursor was corrected; rebuild and DR still omit reminder/parking state. Classification: **partially closes poison-loop facets, leaves the architecture findings persistent; no finding is fully closed by this commit.**

## Positive controls retained

- AD-23/AD-24 now state that identity equality is insufficient without authenticated provenance, name platform/Tenants owners, and bind negative tests. Keep those rules and make their mechanisms concrete.
- EventStore's current external command gateway authenticates, derives `UserId` from `sub`, sanitizes extensions, and applies tenant/RBAC validation; actor-level tenant identity validation also exists. These are credible substrate controls, not a substitute for workload delegation.
- Pending-date stream folding is strict about exclusive cursors, page identity, payload identity and undecodable state-affecting evidence (`PendingDateAwaitStreamReader.cs:38-108`). Reuse it as the recovery-reader standard.
- `5c86eab`'s copy-on-write ETag updates and durable per-aggregate parking reduce global poller blast radius. Preserve that isolation while adding degraded health and repair lifecycle.
- Shared rebuild keeps readers on the previous generation until one atomic batch and fails closed on incomplete relationship evidence; the missing part is the live-writer fence and complete recovery-state restore, not local batch atomicity.
- Logs inspected are structured and metadata-oriented; payload redaction conventions are present in Works and EventStore.

## Verification and gate action

The focused existing suites were run with:

```text
dotnet test tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~StreamReadingCascadeDescendantSourceTests|FullyQualifiedName~StreamReadingChildCompletionAwaitingParentSourceTests|FullyQualifiedName~ReminderReconciliationServiceTests'
```

Result: **17 passed, 0 failed**. This is negative evidence for coverage, not proof of cursor safety: none exercises a successful transition from page 1 to page 2.

Minimum gate sequence: fix and mutation-test SEC-DI-0912-01 immediately; adopt explicit mechanisms for 0912-02/03/04/05 before Story 4.9 R4/R6/R7/R8/R10/R11 acceptance; resolve 0912-06 before real data; resolve 0912-07 before AD-21 and 0912-08 before AD-25 are drafted. Until then, the spine may guide corrective work but must not be represented as implementation-ready or production-safe.
