# Architecture Validation Report — Hexalith.Works — 2026-09-12

> **Gate verdict: FAIL — UPDATE REQUIRED BEFORE AD-21, AD-22, AD-25, OR STORY 4.9 IMPLEMENTATION.** The unchanged spine is not a safe consistency contract for the next cross-repository work because five critical and fourteen high gaps still permit silent recovery loss, incompatible roll-up persistence, and divergent tree, security, recovery, and platform behavior.

## Scope and evidence

This standalone Validate run assessed [`architecture.md`](../architecture.md) as an architecture spine. The target is **1,197 lines**, with the binding Architecture Decision Register and open-findings register at lines 26–453. Its review-time SHA-256 is `2eff9c10fdd92c3929af71389139b304a2f02690deee9272dd93b0ad56d66e7d`; repository `HEAD` is `0cbc7c442da23f0565966fecf43d2d2046855630` (`0cbc7c4`). The working copy's Git blob `b37a127d413857978bca4639126532a45e41554c` equals `HEAD:_bmad-output/planning-artifacts/architecture.md`, `git diff` is empty for the target, and the source was **not modified** by this run.

The gate combined deterministic lint with five independent reviews against that same source. Raw lens counts overlap and must not be summed. Code and runtime observations are architecture-validation findings **only** when they demonstrate a missing, ambiguous, or violated spine rule; an unrelated implementation defect is outside this report. This test is why the page-cursor defect is included: it directly violates AD-09, AD-10, and R7's re-readable recovery contract.

| Input | Critical | High | Medium | Low | Lens verdict |
| --- | ---: | ---: | ---: | ---: | --- |
| [Rubric walker](reviews/review-rubric-walker.md) | 0 | 5 | 5 | 2 | **Fail — update required**; score 1 pass, 3 concerns, 4 fails |
| [Adversarial divergence](reviews/review-adversarial-divergence.md) | 4 | 9 | 3 | 0 | **Fail**; AD-21/AD-22 unsafe from the register alone |
| [Technology reality](reviews/review-technology-reality.md) | 0 | 2 | 2 | 1 | Pass with high concerns |
| [Security & data integrity](reviews/review-security-data-integrity.md) | 1 | 7 | 3 | 0 | **Reject** for Story 4.9 acceptance / production |
| [Code & spec conformance](reviews/review-code-spec-conformance.md) | 0 | 4 | 6 | 4 | **Fail**; 12 ADs ratified, 7 drifted, 6 target/unbuilt |
| [Deterministic lint](lint-spine.json) | 0 | 0 | 0 | 10 flags | Mechanical result `ok: false`; all ten flags are false positives |
| **Deduplicated report** | **5** | **14** | **9** | **3** | **Fail — update required** |

### Lint disposition

All ten lint flags are intentional brace-token notation, not unfilled template content: `{tenant}` / `{id}` at line 448, `{tenant}` / `{domain}` at lines 473 and 492, `{tenant}` at lines 666 and 756, and `{tenant}` / `{id}` at line 984. **Disposition: ignore.** The linter reports no duplicate AD IDs, missing `Binds` / `Prevents` / `Rule` sections, unpinned-stack finding, or other mechanical defect. The lint result remains linked as evidence rather than being rewritten.

### Changes since the 2026-09-08 gate

The [2026-09-08 report](../architecture-validation-2026-09-08/ARCHITECTURE-VALIDATION.md) is comparison-only. The architecture source and SHA-256 are unchanged, so **no architecture-text finding was closed** by this interval, and a newly observed defect is not necessarily newly introduced.

| Delta | 2026-09-12 assessment |
| --- | --- |
| Architecture build/test evidence improved | Release solution/build evidence is green; the technology reviewer directly executed **237/237** architecture tests, and the conformance reviewer reports **93/93** focused integration tests. The build compiles but does not execute the architecture-test assembly, so the prose claim that fitness tests “run in the build” remains false. |
| VAL-H11 gained external closure evidence | [`docs/eventstore-api-surface-constraints.md`](../../../docs/eventstore-api-surface-constraints.md) now records the reader/writer compatibility matrix and calls VAL-H11 closed. The spine still calls it open, so reconciliation remains. |
| EventStore substrate advanced | The gateway client (R11), current query/projection/read-model seams, `/project/v2`, and shared-rebuild primitives exist. R4 fan-out, R6 durable-reminder/reconciliation, and R7 process-runner APIs still do not. |
| Reminder poison handling improved locally | `5c86eab` added bounded per-aggregate parking, copy-on-write ETag transforms, and reserved-tenant defenses. It did not add unpark/replay, durable periodic recovery, degraded readiness, cursor correctness, or complete registry canonicalization; no prior gate finding fully closes. |
| New critical evidence | Both recovery stream readers double-advance an exclusive cursor and silently skip one event at every page boundary (C05). This is newly detected, not shown to be newly introduced. |
| Existing protocol risk sharpened | The `registry watermark >= reservation sequence` predicate can acknowledge empty ancestry while the edge is still `Reserved` (C04). The contribution-source and child-creation ambiguities now have more concrete current-code/spec evidence (C02/C03). |
| Conformance worsened | The AD map moved from **15 ratified / 4 drifted / 6 target** to **12 / 7 / 6**: AD-10, AD-11, and AD-19 are now classified as drifted. |
| Version reality moved | The repository pins remain compatible, but newer .NET and Dapr servicing patches exist; AD-19's current-to-target ownership mismatch is now operationally visible (H13). |
| Overdue planning gates strengthened | VAL-H10's “before the registry story is drafted” condition is now plainly due because the amended PRD/addendum define the registry contract, while no executable registry story exists. |

## Critical findings

### VAL3-C01 — Persisted roll-up slots, keys, merge ownership, and materialization are unbound

- **Issue:** AD-06 fixes a logical LWW slot per descendant and AD-22 assigns generic fan-out to EventStore, but the spine never binds the persisted DTO, state-store key layout, atomic visibility, equal-sequence rule, writer ownership, merge interface, or query materialization. The live model still has one document watermark and whole-document replacement.
- **Divergence/risk:** Works can build one ancestor document with an in-process merge while EventStore builds one key per slot; both satisfy the logical formula yet cannot compose. Whole-document replacement can silently erase another descendant slot or the ancestor's own fields.
- **Source:** [`architecture.md:82–95`](../architecture.md#L82) (AD-06), [`architecture.md:263`](../architecture.md#L263) (R4), [`architecture.md:318–342`](../architecture.md#L318) (AD-22).
- **Corroborating evidence:** [`WorkItemRollUp.cs:29`](../../../src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs#L29) has a single watermark and no slot map; [`WorkItemProjectionDispatcher.cs:440`](../../../src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs#L440) replaces the document; [`work-roll-up-projection.md:94`](../../../docs/work-roll-up-projection.md#L94) documents the document-level guard; EventStore's [`IAsyncDomainProjectionHandler.cs:8`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IAsyncDomainProjectionHandler.cs#L8) does not define the Works slot contract.
- **Found by:** ADV-0912-01; CONF-0912-06.
- **Disposition:** **Discuss, then autofix in an Architecture Update.** Choose one persisted shape and owner split; bind exact DTO/key/merge/materialization rules, forbid a document-level watermark from arbitrating descendant slots, and assign a replacing story before AD-22 implementation.

### VAL3-C02 — AD-22's “event alone” contribution source is not computable

- **Issue:** AD-22 says every ancestor contribution is computable from the event alone, but `ProgressReported` is delta-only and `ReEstimated` lacks prior Done; terminal contribution also depends on folded lifecycle state. Raw-Act rules prohibit smuggling the derived total into those domain payloads.
- **Divergence/risk:** One unit can interpret a raw domain event, another can fold descendant state, and a third can add derived payload fields. They write different totals after repeated progress, re-estimation, unestimated work, or terminal transitions, corrupting every ancestor while each cites AD-22.
- **Source:** [`architecture.md:82–103`](../architecture.md#L82) (AD-06/07), [`architecture.md:320–337`](../architecture.md#L320) (AD-22), [`architecture.md:782–800`](../architecture.md#L782) (Raw-Act / sequence rules).
- **Corroborating evidence:** [`ProgressReported.cs:8`](../../../src/Hexalith.Works.Contracts/Events/ProgressReported.cs#L8), [`ReEstimated.cs:8`](../../../src/Hexalith.Works.Contracts/Events/ReEstimated.cs#L8), [`WorkItemState.cs:176`](../../../src/Hexalith.Works.Contracts/State/WorkItemState.cs#L176), and [`WorkItemRollUpProjection.cs:222`](../../../src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs#L222) show that absolute, terminal-aware contribution needs folded state.
- **Found by:** ADV-0912-02; RW12-03; CONF-0912-06.
- **Disposition:** **Autofix in Update after confirming the contract.** Bind a Works-owned absolute contribution snapshot computed by the descendant's folded projection, including sequence/watermark, Unit subtotals, terminal zero, and unestimated count; R4 transports it and never interprets Works event payloads.

### VAL3-C03 — Child creation, attachment evidence, and the freshness carrier have no single authoritative act

- **Issue:** AD-21 moves `Reserved -> Attached` on `ChildSpawned`, but repair asks whether the child stream contains creation evidence; the PRD requires a later `WorkItemCreated`. AD-22 names a reservation/attachment witness without naming its event, field, or whether it is an envelope sequence. Current events carry no reservation identity or registry sequence.
- **Divergence/risk:** One unit attaches topology from parent intent while another waits for child creation. The result can be an attached edge with no child stream, a child unrecognized by the registry, or replay/timeout decisions based on different evidence.
- **Source:** [`architecture.md:272–316`](../architecture.md#L272) (AD-21), [`architecture.md:327–333`](../architecture.md#L327) (AD-22 witness), [`prd.md:287–296`](../prds/prd-works-2026-06-14/prd.md#L287), [`addendum.md:38–47`](../prds/prd-works-2026-06-14/addendum.md#L38).
- **Corroborating evidence:** [`WorkItemAggregate.cs:71`](../../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs#L71) emits `ChildSpawned` but creates no child; [`WorkItemCreated.cs:8`](../../../src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs#L8), [`ChildSpawned.cs:8`](../../../src/Hexalith.Works.Contracts/Events/ChildSpawned.cs#L8), and [`CreateWorkItem.cs:18`](../../../src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs#L18) carry no reservation/witness field.
- **Found by:** ADV-0912-03; RW12-02/RW12-05; CONF-0912-03/CONF-0912-08.
- **Disposition:** **Discuss, then autofix the spine and reconcile the PRD addendum.** Recommended chain: parent intent triggers a fenced Reactor `CreateWorkItem(ReservationId, ReservationEnvelopeSequence)`; matching `WorkItemCreated` is the sole attach evidence; the registry envelope sequence is the named witness.

### VAL3-C04 — The AD-22 freshness predicate can acknowledge a still-Reserved edge as empty ancestry

- **Issue:** AD-22 declares ancestry resolved when registry watermark is at least the reservation sequence, while AD-21 enumerates only `Attached` edges. Immediately after `EdgeReserved`, that predicate is true but attached ancestry is empty.
- **Divergence/risk:** Fan-out can acknowledge the child's only delivery as a no-op before attachment arrives. No later descendant event is guaranteed, so ancestor contribution can remain permanently absent; a different arrival order produces a different read model from the same facts.
- **Source:** [`architecture.md:293–302`](../architecture.md#L293) (only attached edges enumerate), [`architecture.md:327–335`](../architecture.md#L327) (freshness/ack rule).
- **Corroborating evidence:** No registry types currently exist to supply a stronger hidden convention; current ordinary projection remains single-aggregate at [`WorkItemProjectionDispatcher.cs:96`](../../../src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs#L96), while shared rebuild infers edges from stream facts at [`WorkItemSharedRebuildManifestBuilder.cs:159`](../../../src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs#L159).
- **Found by:** ADV-0912-04; corroborated by RW12-03's unresolved witness/fence analysis.
- **Disposition:** **Autofix before drafting the registry/fan-out stories.** Resolve ancestry only from the exact matching `Attached` transition and its sequence; `Reserved` or missing attachment must remain retryable/not acknowledged, or attachment must perform a deterministic contribution backfill.

### VAL3-C05 — Recovery readers silently skip every multi-page boundary event

- **Issue:** EventStore defines `FromSequence` as an exclusive lower bound. Both Story 4.7 recovery readers set the next cursor to `LastSequenceReturned + 1`, double-advancing and skipping one event per page.
- **Divergence/risk:** A boundary `ChildSpawned`, `WorkItemCreated`, suspend, resume, or terminal event silently disappears from cascade or child-completion recovery. Checkpoints can then become permanently incomplete without an exception, retry, or health signal.
- **Source:** [`architecture.md:119–135`](../architecture.md#L119) (AD-09/AD-10), [`architecture.md:266`](../architecture.md#L266) (R7 crash recovery). This current-code defect is in this architecture report **only because it violates those adopted rules**.
- **Corroborating evidence:** EventStore's [`StreamReadRequest.cs:8`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Streams/StreamReadRequest.cs#L8) defines exclusivity; [`StreamReadingCascadeDescendantSource.cs:75`](../../../src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs#L75) and [`StreamReadingChildCompletionAwaitingParentSource.cs:127`](../../../src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs#L127) add one; the correct pattern exists in [`PendingDateAwaitStreamReader.cs:82`](../../../src/Hexalith.Works/Reminders/PendingDateAwaitStreamReader.cs#L82). Existing suites omit a successful two-page boundary.
- **Found by:** SEC-DI-0912-01; CONF-0912-01.
- **Disposition:** **Autofix code/tests immediately, then revalidate.** Reuse the returned last sequence as the next exclusive lower bound and add >200-event boundary/restart tests for both readers. No code change is made by this Validate run.

## High findings

### VAL3-H01 — Reservation timeout does not fence an already-issued late SpawnChild

- **Issue:** AD-21 permits timeout to release a reservation, but no reservation ID/lease/epoch reaches the parent-side command or aggregate admission path.
- **Divergence/risk:** A compliant registry releases the edge while a delayed, authenticated `SpawnChild` is later accepted, leaving parent/child stream facts that contradict terminal registry topology.
- **Source:** [`architecture.md:278–316`](../architecture.md#L278), especially timeout and Reactor-only submission.
- **Corroborating evidence:** [`SpawnChild.cs:19`](../../../src/Hexalith.Works.Contracts/Commands/SpawnChild.cs#L19), [`ChildSpawned.cs:8`](../../../src/Hexalith.Works.Contracts/Events/ChildSpawned.cs#L8), and [`WorkItemAggregate.cs:71`](../../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs#L71) carry/check no reservation fence.
- **Found by:** ADV-0912-05.
- **Disposition:** **Discuss, then update.** Carry a stable reservation ID plus monotonic fencing token on every saga leg; validate it before aggregate dispatch; define released tokens as terminal and bind explicit orphan/compensation behavior.

### VAL3-H02 — Registry wire identity, runtime ownership, and sole topology authority are incomplete

- **Issue:** The spine binds no exact registry `(tenant, domain, aggregateId)`, topic, route, payload identity, reserved-ID rule, or domain registration. The migration matrix also allocates no owner for reserve/spawn/release translation, timeout scheduling, or trusted `MaxDepth` enrichment. Fan-out, cascade, child-completion recovery, and rebuild can still enumerate different stream/registry evidence.
- **Divergence/risk:** Independently built Works, EventStore, and Platform units can subscribe to different topics, collide actor identities, or each assume another owns translation/policy. Runtime consumers can operate on different trees even while each follows part of AD-21.
- **Source:** [`architecture.md:254–270`](../architecture.md#L254) (matrix), [`architecture.md:272–316`](../architecture.md#L272) (AD-21), [`architecture.md:448`](../architecture.md#L448) (VAL-H09).
- **Corroborating evidence:** [`WorksHost.cs:66`](../../../src/Hexalith.Works/Runtime/WorksHost.cs#L66) subscribes only to `work.events`; [`WorksEventIdentity.cs:11`](../../../src/Hexalith.Works/Runtime/WorksEventIdentity.cs#L11) assumes WorkItem identity; current cascade, child completion, and rebuild infer topology from different facts in [`StreamReadingCascadeDescendantSource.cs:17`](../../../src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs#L17), [`StreamReadingChildCompletionAwaitingParentSource.cs:37`](../../../src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs#L37), and [`WorkItemSharedRebuildManifestBuilder.cs:159`](../../../src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs#L159).
- **Found by:** ADV-0912-06/07; RW12-05; CONF-0912-08.
- **Disposition:** **Discuss, then autofix/update.** Bind the exact wire namespace and one registry enumeration interface for every topology consumer; allocate translator, timeout, and trusted-policy ownership; require a one-time tenant migration before switching consumers.

### VAL3-H03 — Transport idempotency is overdue and shorter-lived than recovery

- **Issue:** VAL-H10 remains open at the exact R11 seam AD-21 depends on. Originators derive identifiers differently; Works maps causation to `MessageId`, leaves `IdempotencyKey` null, and EventStore's default terminal retention/status TTL is 24 hours—shorter than plausible scheduler, backup, or DR replay.
- **Divergence/risk:** One logical reserve/spawn/release effect can be admitted twice after retry, ownership migration, or retention expiry, or rejected because raw concatenated IDs exceed transport syntax/length.
- **Source:** [`architecture.md:49–55`](../architecture.md#L49) (AD-02 exclusion), [`architecture.md:270`](../architecture.md#L270) (R11), [`architecture.md:313–316`](../architecture.md#L313), [`architecture.md:449`](../architecture.md#L449) (VAL-H10).
- **Corroborating evidence:** [`EventStoreGatewayWorkCommandSubmitter.cs:18`](../../../src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs#L18) leaves semantic idempotency unset; EventStore's [`IdempotencyRetentionOptions.cs:3`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/IdempotencyRetentionOptions.cs#L3) and [`CommandStatusOptions.cs:3`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Commands/CommandStatusOptions.cs#L3) default to 24 hours.
- **Found by:** ADV-0912-08; SEC-DI-0912-07; RW12-04.
- **Disposition:** **Discuss, then add a stable AD before AD-21 story work.** Bind canonical tuple/hash/encoding, `MessageId` and `IdempotencyKey` semantics, retention/replay/conflict behavior, and the persisted delegation identity.

### VAL3-H04 — Shared rebuild has no capture-through-Commit protocol for every writer or recovery index

- **Issue:** AD-16 allows “quiesced or platform-fenced” but does not choose an epoch/token, admitted writer set, capture watermark, nack behavior, catch-up proof, or abort path; AD-22 nevertheless says fan-out follows that fence. Reminder discovery/parking/global indexes are outside the promoted manifest.
- **Divergence/risk:** Live writes can be acknowledged then omitted or overwrite a promoted generation; state-store restore can yield query-consistent roll-ups while losing the ability to discover pending reminders.
- **Source:** [`architecture.md:184–194`](../architecture.md#L184) (AD-16), [`architecture.md:263`](../architecture.md#L263) (R4), [`architecture.md:327–335`](../architecture.md#L327), [`architecture.md:447`](../architecture.md#L447) (VAL-H08).
- **Corroborating evidence:** [`work-roll-up-projection.md:75`](../../../docs/work-roll-up-projection.md#L75) and [`eventstore-api-surface-constraints.md:137`](../../../docs/eventstore-api-surface-constraints.md#L137) state that current handlers cannot arbitrate concurrent ordinary writers; [`WorkItemSharedRebuildManifestBuilder.cs:127`](../../../src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs#L127) excludes reminder discovery and parking state.
- **Found by:** ADV-0912-09; SEC-DI-0912-04; RW12-03.
- **Disposition:** **Discuss with EventStore/Platform, then update.** Bind one epoch protocol per tenant/projection family, every writer, inventory/catch-up watermarks, stale-epoch retry behavior, and an atomic or separately reconcilable recovery-index restore procedure.

### VAL3-H05 — Global control-plane registries and cascade checkpoints lack enforceable concurrency/namespace rules

- **Issue:** VAL-H06/H09 defer CAS, replica ownership, and namespaces. Current global tenant/cascade registries use singleton keys; cascade checkpoints use read/check/save without CAS. Admission checks can inspect raw casing before `TenantId` canonicalization.
- **Divergence/risk:** Replicas can regress completed target state, real tenants can collide with `tenants`/`system`, and a mixed-case reserved ID can poison discovery so later reminder scans fail while the host stays healthy.
- **Source:** [`architecture.md:174–182`](../architecture.md#L174) (AD-15), [`architecture.md:266–267`](../architecture.md#L266) (R7/R8), [`architecture.md:445–448`](../architecture.md#L445) (VAL-H06/H09).
- **Corroborating evidence:** [`WorksReadModelKeys.cs:57`](../../../src/Hexalith.Works/Projections/WorksReadModelKeys.cs#L57) defines global/reserved keys; [`ReadModelCascadeCheckpointStore.cs:29`](../../../src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs#L29) performs non-atomic read/save; [`WorkItemProjectionDispatcher.cs:111`](../../../src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs#L111) checks before all canonicalized key mutations.
- **Found by:** ADV-0912-10/15; SEC-DI-0912-05.
- **Disposition:** **Autofix the canonicalization hole; discuss and update the protocol.** Bind canonical reserved tokens, key/topic ownership, CAS or a fenced single owner, monotonic merge, bounded enumeration, pruning/offboarding, audit, and crash reconciliation.

### VAL3-H06 — Tenant delegation and command-origin rules have no operable end-to-end carrier

- **Issue:** AD-23/24 state the invariant but not the signed delegation shape, issuer, gateway validation locus, or command-type policy. The internal EventStore authentication path grants allow-listed workloads `global_admin`; Works sends tenant as body data without delegation evidence, while app-level routes rely on topology.
- **Divergence/risk:** A compromised workload can act across tenants or invoke the wrong internal command family; another conforming deployment can deny valid Reactor/reminder calls because caller and receiver chose different carriers.
- **Source:** [`architecture.md:344–395`](../architecture.md#L344) (AD-23/24), [`architecture.md:269–270`](../architecture.md#L269) (R10/R11).
- **Corroborating evidence:** EventStore's [`DaprInternalAuthenticationHandler.cs:9`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore/Authentication/DaprInternalAuthenticationHandler.cs#L9) grants global administration; [`EventStoreGatewayWorkCommandSubmitter.cs:19`](../../../src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs#L19) carries no delegation; [`WorksHost.cs:103`](../../../src/Hexalith.Works/Runtime/WorksHost.cs#L103) maps SDK/domain routes without an app-channel authentication contract.
- **Found by:** ADV-0912-11; SEC-DI-0912-02; CONF-0912-04.
- **Disposition:** **Discuss, then update; block production.** Bind a server-derived signed workload/tenant/purpose/causation context, prohibit global-admin workload bypass, enforce `CommandOriginPolicy[(domain, commandType)]` before dispatch, and require direct-port/wrong-app/wrong-tenant negatives.

### VAL3-H07 — Reminder/expiry ownership, typed intent, and stale-fire rejection are undefined and unbuilt

- **Issue:** AD-11/R6 do not name the actor type/app/ID/state owner. AD-25 reuses an index that currently has no intent kind and defines idempotency only for already-terminal work. `ExpireWorkItem` carries no due-instant/schedule token, so a rescheduled old reminder cannot be rejected purely. No story owns automatic expiry.
- **Divergence/risk:** Two hosts can register different actor families; reconciliation can translate an expiry as a date resume; a stale October reminder can expire an item rescheduled to December and cascade the wrong terminal state.
- **Source:** [`architecture.md:137–149`](../architecture.md#L137) (AD-11), [`architecture.md:265`](../architecture.md#L265) (R6), [`architecture.md:397–416`](../architecture.md#L397) (AD-25).
- **Corroborating evidence:** [`DaprDateReminderScheduler.cs:17`](../../../src/Hexalith.Works/Reminders/DaprDateReminderScheduler.cs#L17) targets a dedicated actor; [`PendingDateAwait.cs:10`](../../../src/Hexalith.Works/Reminders/PendingDateAwait.cs#L10) has no kind; [`DateReminderReconciler.cs:101`](../../../src/Hexalith.Works/Reminders/DateReminderReconciler.cs#L101) always creates a date resume; [`ExpireWorkItem.cs:12`](../../../src/Hexalith.Works.Contracts/Commands/ExpireWorkItem.cs#L12) has no witness.
- **Found by:** ADV-0912-12; SEC-DI-0912-08; CONF-0912-02.
- **Disposition:** **Discuss, then update before AD-25 story drafting.** Bind one actor/app/ID, typed pending-intent DTO and indexes, reconciliation owner, and `ExpireWorkItem(DueInstant/ScheduleToken)` equality rule; add the automatic-expiry story and stale-fire/restore cases.

### VAL3-H08 — AD-13 contradicts AD-17, the PRD, transition matrix, and live kernel

- **Issue:** AD-13 says no current match is a no-op; AD-17 makes the lifecycle matrix authoritative, and the matrix/kernel say a nonmatching suspended resume is rejected. Only replay of the consumed condition after success is a no-op.
- **Divergence/risk:** Reminder, external-signal, and child-completion builders can record different completion/checkpoint/audit outcomes for the same delivery; the register's precedence rule cannot resolve conflict inside the register.
- **Source:** [`architecture.md:159–165`](../architecture.md#L159) (AD-13), [`architecture.md:196–202`](../architecture.md#L196) (AD-17), [`architecture.md:818–820`](../architecture.md#L818) (contradictory prose).
- **Corroborating evidence:** [`lifecycle-transition-matrix.md:165`](../../../docs/lifecycle-transition-matrix.md#L165), [`prd.md:275–285`](../prds/prd-works-2026-06-14/prd.md#L275), [`WorkItemAggregate.cs:223`](../../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs#L223), and [`WorkItemState.cs:146`](../../../src/Hexalith.Works.Contracts/State/WorkItemState.cs#L146).
- **Found by:** RW12-01; ADV-0912-13; CONF-0912-07.
- **Disposition:** **Autofix in Update.** Keep AD-13 stable but state: nonmatch while suspended rejects without changing the set; only the exact consumed condition after successful resume is a no-op; other post-resume conditions reject. Propagate to lines 466, 688, and 818–820.

### VAL3-H09 — The spine does not cover the current 26-FR product contract

- **Issue:** The 2026-09-08 PRD adds FR-26 and binds explicit completion, terminal/unestimated roll-up contribution/count, Unit inheritance, exact resume outcomes, Reactor consistency window, two “what's next” views/predicate, and provisional performance acceptance. The spine remains dated 2026-09-06, claims 25 FRs, and omits these cross-unit calls.
- **Divergence/risk:** Registry, projection, query, and platform authors can independently choose incompatible semantics while the older register claims precedence over newer requirements.
- **Source:** [`architecture.md:1–20`](../architecture.md#L1), [`architecture.md:457–469`](../architecture.md#L457), [`architecture.md:988–999`](../architecture.md#L988), [`architecture.md:1062–1069`](../architecture.md#L1062).
- **Corroborating evidence:** [`prd.md:196–205`](../prds/prd-works-2026-06-14/prd.md#L196), [`prd.md:231–260`](../prds/prd-works-2026-06-14/prd.md#L231), [`prd.md:275–306`](../prds/prd-works-2026-06-14/prd.md#L275), [`prd.md:347–354`](../prds/prd-works-2026-06-14/prd.md#L347), and [`addendum.md:55–72`](../prds/prd-works-2026-06-14/addendum.md#L55).
- **Found by:** RW12-02; CONF-0912-03/CONF-0912-10.
- **Disposition:** **Discuss, then autofix/update.** Confirm the 2026-09-08 PRD as driving input and distill only its load-bearing cross-unit calls into stable ADs; refresh FR mapping/count without copying the PRD.

### VAL3-H10 — Acceptance/revisit obligations are not executable work, and VAL-H10 is overdue

- **Issue:** VAL-H06–H09 and AD-23/24 pin proof to Story 4.9 rows its ACs do not contain. AD-21/22/25 have no implementing stories; sprint status has Story 4.9 in backlog. VAL-H10's pre-registry deadline has passed at the requirements level.
- **Divergence/risk:** Story 4.9 can satisfy every written AC while leaving identity, origin, checkpoint, reminder, namespace, and fence obligations open; the register can then imply closure with no proof artifact.
- **Source:** [`architecture.md:344–395`](../architecture.md#L344), [`architecture.md:436–453`](../architecture.md#L436), [`architecture.md:1177–1197`](../architecture.md#L1177).
- **Corroborating evidence:** [`epics.md:1335–1383`](../epics.md#L1335) (Story 4.9), [`epics.md:402–410`](../epics.md#L402) (pending additions), and [`sprint-status.yaml:47`](../../implementation-artifacts/sprint-status.yaml#L47).
- **Found by:** RW12-04; CONF-0912-03/04; SEC-DI-0912-07.
- **Disposition:** **Discuss / correct-course before affected implementation.** Resolve VAL-H10; create ordered registry, fan-out, expiry, and Reactor recovery stories; map every R-row/open item to a story and named proof artifact; expand or split Story 4.9 acceptance.

### VAL3-H11 — Recovery can stop or park work while the host remains Ready

- **Issue:** R8 requires durable evidence plus degraded readiness, but reminder recovery stops after bounded startup attempts and cascade recovery is a single swallowed pass. A parked projection causes reminder discovery to skip the authoritative stream; no unpark/replay or readiness contribution exists.
- **Divergence/risk:** Date waits, cascades, or child-completion work can remain stranded for a host lifetime while health says ready, turning derived projection failure into a veto over authoritative stream recovery.
- **Source:** [`architecture.md:137–149`](../architecture.md#L137) (stream authority), [`architecture.md:267`](../architecture.md#L267) (R8), [`architecture.md:446`](../architecture.md#L446) (VAL-H07).
- **Corroborating evidence:** [`ReminderReconciliationService.cs:30`](../../../src/Hexalith.Works/Reminders/ReminderReconciliationService.cs#L30), [`CascadeRecoveryService.cs:19`](../../../src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs#L19), [`IndexedPendingDateAwaitSource.cs:105`](../../../src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs#L105), and [`deferred-work.md:860`](../../implementation-artifacts/deferred-work.md#L860).
- **Found by:** SEC-DI-0912-03; CONF-0912-05.
- **Disposition:** **Autofix/update the recovery contract and later implementation.** Require durable periodic retry, unresolved-work evidence, named degraded readiness/meter/alert, and authenticated audited repair/replay; parked work remains degraded until disposition.

### VAL3-H12 — Privacy, audit, retention, secrets, backup, and DR remain unbound

- **Issue:** The spine persists Raw Acts verbatim and repeatedly says “audited,” but binds no durable-field classification, immutable audit shape/sink/failure posture, encryption/crypto-shredding, retention/legal hold/offboarding, secret-store/rotation, RPO/RTO, restore order, or DR drill. VAL-H12 waits until production review, after AD-21/25 would add durable contracts.
- **Divergence/risk:** Works, EventStore, and Platform can make incompatible irreversible storage and erasure choices; real data or new durable types can ship before a recoverable privacy/DR model exists.
- **Source:** [`architecture.md:303–308`](../architecture.md#L303), [`architecture.md:344–362`](../architecture.md#L344), [`architecture.md:451`](../architecture.md#L451), [`architecture.md:782–786`](../architecture.md#L782), [`architecture.md:843–848`](../architecture.md#L843).
- **Corroborating evidence:** [`Obligation.cs`](../../../src/Hexalith.Works.Contracts/ValueObjects/Obligation.cs), [`EventPersister.cs:104`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventPersister.cs#L104), the existing but unreferenced [`subscriber-dead-letter-operator.md`](../../../docs/operations/subscriber-dead-letter-operator.md), and root [`Directory.Build.props:15`](../../../Directory.Build.props#L15) (`NuGetAudit=false`).
- **Found by:** SEC-DI-0912-06; RW12-07.
- **Disposition:** **Discuss now; defer implementation only behind a hard gate.** Move the revisit to before the next durable catalog addition and any non-synthetic shared data; bind owners, classifications, audit event/sink, secrets, retention, RPO/RTO, and restore/drill acceptance.

### VAL3-H13 — AD-19 does not describe live version ownership, and servicing patches moved

- **Issue:** AD-19 says `global.json` and central package props are the only pins and Platform owns Dapr runtime, but Works source currently hard-codes runtime images and Platform has no Dapr pin. As of the technology review, `.NET SDK/runtime 10.0.401/10.0.12`, Dapr runtime 1.18.4, and Dapr.Client 1.18.7 supersede the checked 10.0.400/10.0.11, 1.18.3, and 1.18.5 lines.
- **Divergence/risk:** Builders can update the wrong authority or assume `latestPatch` crosses the 10.0.400 feature band; migration acceptance can claim Platform ownership that does not exist. The current same-minor Dapr combination is compatible—this is provenance/currentness, not a proven runtime incompatibility.
- **Source:** [`architecture.md:216–224`](../architecture.md#L216) (AD-19), [`architecture.md:226–242`](../architecture.md#L226) (AD-20).
- **Corroborating evidence:** [`global.json`](../../../global.json), [`DaprSelfHostedMtls.cs:47`](../../../src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs#L47), and [`Directory.Packages.props`](../../../references/Hexalith.Builds/Props/Directory.Packages.props); primary-source currentness is documented with links in the [technology review](reviews/review-technology-reality.md#tech-01--high--ad-19s-authority-model-is-not-the-live-version-topology-and-current-securitybug-fix-patches-have-moved).
- **Found by:** TECH-01; RW12-09; CONF AD-19 map.
- **Disposition:** **Discuss/update before Story 4.9 acceptance.** Bind current Works ownership until R1 lands and target Platform ownership afterward; inventory every pin location; refresh versions through normal dependency work with build/integration proof.

### VAL3-H14 — Platform composition depends on preview integration and unavailable target seams

- **Issue:** Core Aspire 13.5.3 is stable, but the selected `CommunityToolkit.Aspire.Hosting.Dapr` 13.5 line is preview. `Hexalith.Platform` remains an empty 13.4.6 scaffold with no Works/Dapr topology, and EventStore lacks R4 fan-out, R6 durable reminders, and R7 process runner; only R11 is available.
- **Divergence/risk:** AD-20 reads like a supported, near-ready target, but Story 4.9 can begin against an unaccepted preview dependency and capabilities with no producer/version. Removing Works hosting would abandon Sentry/mTLS, placement, Scheduler persistence, Redis, ACL, resilience, and recovery behavior.
- **Source:** [`architecture.md:226–270`](../architecture.md#L226) (AD-20/matrix), [`architecture.md:1096–1111`](../architecture.md#L1096) (gap/handoff claims).
- **Corroborating evidence:** [`Directory.Packages.props`](../../../references/Hexalith.Builds/Props/Directory.Packages.props) pins `13.5.0-preview.1.260825-0345`; official Platform state and package evidence are linked in the [technology review](reviews/review-technology-reality.md#tech-02--high--the-single-aspire-135x-family-conceals-a-preview-only-dapr-integration-and-no-platform-proof); current EventStore source exposes R11 but no R4/R6/R7 equivalent.
- **Found by:** TECH-02/TECH-03; CONF-0912-04; RW12-04.
- **Disposition:** **Discuss and gate.** Choose explicit preview acceptance with owner/upgrade trigger, a stable compatible family, or a different integration; mark R4/R6/R7 absent with producer issue/minimum package/version tests; keep Works hosting until a Platform-owned R1–R11 parity lane is green.

## Medium tail

| ID | Finding | Source | Lens IDs | Disposition |
| --- | --- | --- | --- | --- |
| VAL3-M01 | Binding altitude is ambiguous: the preamble makes prose seed, while the handoff makes all Implementation Patterns binding. | [`architecture.md:28–36`](../architecture.md#L28), [`architecture.md:1177–1187`](../architecture.md#L1177) | RW12-06; CONF-0912-12/14 | **Autofix:** name exact binding subsections or promote durable rules to ADs. |
| VAL3-M02 | Frontmatter says complete/2026-09-06 while the document is conditional target state with known interim exceptions. | [`architecture.md:1–20`](../architecture.md#L1), [`architecture.md:1096–1111`](../architecture.md#L1096) | RW12-08/11 | **Autofix:** expose current-vs-target readiness and interim exceptions. |
| VAL3-M03 | AD-12/17/18 Rules do not enforce all their Binds/Prevents; AD-18's “machine truth” scope exceeds its allowlist. | [`architecture.md:151–158`](../architecture.md#L151), [`architecture.md:196–214`](../architecture.md#L196) | RW12-09 | **Autofix:** make rules falsifiable and narrow/extend machine truth. |
| VAL3-M04 | VAL-H11 is closed in external evidence but open in the spine; VAL-M01's compilation claim is stale, while explicit test execution is not wired into build/CI. | [`architecture.md:449–452`](../architecture.md#L449), [`architecture.md:1030–1036`](../architecture.md#L1030) | RW12-10; TECH-04; CONF-0912-13 | **Autofix:** reconcile evidence and name the explicit CI command/owner. |
| VAL3-M05 | Slot removal/tombstoning after release or quarantine has no independent attachment sequence space. | [`architecture.md:82–95`](../architecture.md#L82), [`architecture.md:303–308`](../architecture.md#L303) | ADV-0912-14 | **Autofix with C01:** key attachment lifecycle separately from descendant contribution LWW. |
| VAL3-M06 | “Additive tolerant” does not bind old/new reader/writer order, unknown enums/types, or rollout of required security/fence fields. | [`architecture.md:38–47`](../architecture.md#L38), [`architecture.md:450`](../architecture.md#L450) | ADV-0912-16; RW12-10 | **Discuss/update:** adopt the existing matrix and extend it for AD-21/25 contracts. |
| VAL3-M07 | Subscription, projection, and rebuild paths use incompatible quarantine dispositions. | [`architecture.md:436–453`](../architecture.md#L436) | SEC-DI-0912-09 | **Autofix:** bind durable tenant-scoped quarantine, degraded capability state, and audited replay/disposition. |
| VAL3-M08 | Cascade/child-completion stream readers do not uniformly validate page domain, aggregate, payload identity, monotonicity, or state-affecting decode failures. | [`architecture.md:119–135`](../architecture.md#L119) | SEC-DI-0912-10 | **Autofix:** share the strict pending-date stream validator/fold and negative tests. |
| VAL3-M09 | Production controls are prose, not a fail-closed deployment profile; current fitness protects the interim Works host rather than the target boundary. | [`architecture.md:366–395`](../architecture.md#L366), [`architecture.md:1030–1038`](../architecture.md#L1030) | SEC-DI-0912-11; CONF-0912-09 | **Discuss/defer to R10 only with a hard gate:** machine-readable profile, startup/admission refusal, and two-phase fitness migration. |

## Low tail

| ID | Finding | Source | Lens IDs | Disposition |
| --- | --- | --- | --- | --- |
| VAL3-L01 | AD-05's `Meter` vocabulary differs from public `WorkItemEffort` names. | [`architecture.md:75–80`](../architecture.md#L75) | CONF-0912-11 | **Discuss:** compatibility-aware rename/alias or ratify public names. |
| VAL3-L02 | Inputs include a dangling `Hexalith.Projects` path, old review tokens require guesswork, and the cold-start tree lists absent folders. | [`architecture.md:7–16`](../architecture.md#L7), [`architecture.md:888–960`](../architecture.md#L888) | RW12-11; CONF-0912-12 | **Autofix:** refresh links and label/remove historical target seed. |
| VAL3-L03 | Future Fluent UI V5 remains prerelease, but UI is outside Works v1. | [Technology review](reviews/review-technology-reality.md#tech-05--low--future-fluent-ui-v5-remains-prerelease) | TECH-05 | **Defer:** revalidate at first UI theme. |

No formal parent spine was inherited. The mandatory Hexalith baseline is honored at target state; add an Inherited Constraints traceability section in Update, but this is **informational, not a defect** (RW12-12).

## Good-spine checklist scorecard

| Checklist item | Result | Assessment |
| --- | --- | --- |
| Fixes all real divergence points for the level below | **FAIL** | Roll-up persistence/contribution and registry saga/wire/freshness protocols remain underbound (C01–C04, H01–H02). |
| Every AD Rule is enforceable and prevents its stated divergence | **FAIL** | AD-13 is wrong; AD-12/17/18 have Rule/Binds mismatches; AD-19 misstates live authority (H08, H13, M03). |
| Nothing Deferred/open can allow divergence before revisit | **FAIL** | VAL-H10 is due; R4/R6/R7/R8/R10 obligations and AD-21/22/25 have no safe executable allocation (H03–H05, H07, H10–H11). |
| Named technology is verified-current | **CONCERN** | Core claims fit, but patches moved, the Dapr integration is preview, Platform proof is absent, and three target APIs do not exist (H13–H14). |
| Ratifies rather than contradicts brownfield code | **CONCERN** | Core kernel is strong; AD-10/11/19 drift, AD-13 contradicts live code, and AD-21/22/25 are honest but under-gated target state. |
| Covers driving spec capabilities | **FAIL** | Current PRD has 26 FRs and newer cross-unit contracts; architecture and epics still stop at 25 (H09–H10). |
| Honors inherited constraints | **PASS, TRANSITIONAL** | No parent spine; target state honors the Hexalith domain/platform boundary and stack, with Works hosting explicitly transitional. |
| Every owned structural dimension is decided, deferred, or open | **CONCERN** | Domain/data/testing/security intent is broad, but privacy, secrets, DR/backup, environment/promotion, supply-chain, and runbook ownership are incomplete (H12). |

**Score: 1 pass, 3 concerns, 4 fails.** The spine does not pass the standalone validation gate.

## Prioritized Update plan

1. **Immediate correctness fix outside the spine:** fix C05 in both stream readers and add page-boundary/restart tests; re-run the focused recovery suites. This Validate run does not modify code.
2. **Resolve the two load-bearing architecture discussions:** choose the persisted roll-up/merge/materialization contract (C01/C02/M05) and the single child-creation/attachment/freshness/fencing chain (C03/C04/H01).
3. **Update the binding register with stable AD IDs:** fix AD-13; bind registry wire identity/sole topology interface/runtime owners (H02), transport idempotency (H03), rebuild epoch (H04), control-plane CAS/namespaces (H05), delegation/origin enforcement (H06), and typed expiry/reminder witnesses (H07).
4. **Correct-course the executable baseline:** adopt the 2026-09-08 PRD, add FR-26 and changed cross-unit decisions, create registry/fan-out/expiry/Reactor stories, and turn Story 4.9 R1–R11 plus AD-23/24 into named acceptance/proof artifacts (H09/H10/H14).
5. **Make recovery and production gates fail closed:** periodic durable recovery and degraded readiness (H11), privacy/audit/secrets/retention/DR decisions before durable-type or real-data gates (H12), and a machine-readable production profile/two-phase fitness lane (M09).
6. **Reconcile truth and hygiene:** current/target version ownership (H13), availability status for R4/R6/R7/R11, frontmatter/readiness, VAL-H11/VAL-M01, schema/quarantine rules, binding altitude, and low-tail references (M01–M08, L01–L03).

## Review artifacts

- [`reviews/review-rubric-walker.md`](reviews/review-rubric-walker.md) — Good-spine checklist and 12 findings.
- [`reviews/review-adversarial-divergence.md`](reviews/review-adversarial-divergence.md) — two-unit constructions; 16 findings.
- [`reviews/review-technology-reality.md`](reviews/review-technology-reality.md) — current primary-source/repository technology checks; 5 findings.
- [`reviews/review-security-data-integrity.md`](reviews/review-security-data-integrity.md) — security/recovery/data-integrity review; 11 findings.
- [`reviews/review-code-spec-conformance.md`](reviews/review-code-spec-conformance.md) — AD map, test evidence, code/spec/story audit; 14 findings.
- [`lint-spine.json`](lint-spine.json) — deterministic lint output and ten ignored false positives.
- Target: [`../architecture.md`](../architecture.md), unchanged.
- Prior comparison: [`../architecture-validation-2026-09-08/ARCHITECTURE-VALIDATION.md`](../architecture-validation-2026-09-08/ARCHITECTURE-VALIDATION.md) and [HTML](../architecture-validation-2026-09-08/ARCHITECTURE-VALIDATION.html).

This is a Validate report only. Neither `architecture.md` nor source code was changed. The next architecture action is to roll accepted findings into a BMad Architecture **Update**, preserving AD IDs.
