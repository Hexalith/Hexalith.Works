# Architecture Validation Report

> **Gate verdict: FAIL — the document is not a safe current architecture spine until five critical ownership, consistency, and trust-boundary decisions are resolved.**

## Scope and evidence

This Validate run assessed [`architecture.md`](../architecture.md), a **732-line legacy architecture decision document**, as an architecture spine. The source was not modified. Its SHA-256 is `431071620f48b4abc2e70b905ee633b22aff568b89aa301b80d680c7593aed7f`.

The gate combined deterministic lint with four independent review lenses. Raw counts overlap and therefore must not be added as if they represented distinct defects.

| Input | Critical | High | Medium | Low | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| [Rubric walker](reviews/review-rubric-walker.md) | 1 | 4 | 2 | 0 | Fail |
| [Adversarial divergence](reviews/review-adversarial-divergence.md) | 3 | 7 | 0 | 0 | Fail |
| [Technology/reality](reviews/review-technology-reality.md) | 0 | 4 | 3 | 0 | Fail |
| [Security/data integrity](reviews/review-security-data-integrity.md) | 2 | 4 | 1 | 0 | Reject |
| [Deterministic lint](lint-spine.json) | 0 | 0 | 0 | 8 | Eight false positives |
| **Deduplicated report** | **5** | **12** | **4** | **0** | **Fail** |

The eight lint flags are brace-token checks for `{tenant}`, `{domain}`, and `{id}` at source lines 44, 63, 234, 321, and 539. Those are intentional key-shape notation, not unfilled template placeholders, so all eight are false positives and are ignored. The linter also cannot assess the legacy decision blocks: the source has no `AD-n` entries with `Binds` / `Prevents` / `Rule` fields. Its mechanical output is therefore not evidence that the decision shape is sound.

## Critical findings

### VAL-C01 — Platform-host ownership and migration allocation are unbound

- **Issue:** The document removes Works-owned AppHost, ServiceDefaults, delivery, scheduling, projection/query plumbing, and subscriptions, but names only a “designated platform/host repository.” It does not name that repository, an accountable owner, or allocate the current runtime capabilities seam by seam.
- **Divergence/risk:** The Works team can delete adapter-edge behavior believing it is platform-owned while the platform team composes only the minimal domain executable believing domain-specific handlers remain Works-owned. Reminders, cascades, checkpoints, subscriptions, or projections then have no runtime owner. Story 4.9 is explicitly blocked.
- **Source:** [`architecture.md:151–179`](../architecture.md#L151), [`architecture.md:506–508`](../architecture.md#L506), [`architecture.md:524–528`](../architecture.md#L524), [`architecture.md:586–591`](../architecture.md#L586), [`architecture.md:631–647`](../architecture.md#L631).
- **Corroborating evidence:** The mandatory boundary forbids domain-owned platform plumbing ([`hexalith-llm-instructions.md:121–134`](../../../references/Hexalith.AI.Tools/hexalith-llm-instructions.md#L121)); the live solution still contains both hosting projects ([`Hexalith.Works.slnx:50–58`](../../../Hexalith.Works.slnx#L50)); the current host registers Works-specific event, cascade, reminder, recovery, actor, and projection behavior ([`WorksHost.cs:64–121`](../../../src/Hexalith.Works/Runtime/WorksHost.cs#L64)); the AppHost owns exact Dapr/topic/store composition ([`Program.cs:54–136`](../../../src/Hexalith.Works.AppHost/Program.cs#L54)); the approved change proposal requires the target and owner before implementation ([`sprint-change-proposal-2026-09-05.md:377–406`](../sprint-change-proposal-2026-09-05.md#L377), [`:427–431`](../sprint-change-proposal-2026-09-05.md#L427)). The live Aspire graph also mixes AppHost SDK 13.4.6 with hosting packages 13.5.3; [Aspire.Hosting 13.5.3](https://www.nuget.org/packages/Aspire.Hosting/13.5.3) is the official package line cited by the technology review.
- **Found by:** Rubric walker RW-01; adversarial ADV-03; technology TECH-02.
- **Disposition:** **Discuss.** Name the repository, team/owner, package boundaries, supported Aspire family, topology/conformance lane, and a type-by-type migration matrix before Story 4.9 or deletion begins. No autofix was applied.

### VAL-C02 — Tree mutation has no authoritative atomic owner

- **Issue:** The architecture promises a single-parent, acyclic, bounded, tenant-closed tree while keeping aggregates pure, but it does not assign an authoritative owner that reserves a child edge, derives ancestry/depth, or serializes two parents attaching the same child.
- **Divergence/risk:** Two compliant parent aggregates can accept the same child with stale or empty caller-supplied facts, creating two parents or a cycle. Projection checks happen too late to make the write invariant true.
- **Source:** [`architecture.md:34–36`](../architecture.md#L34), [`architecture.md:96`](../architecture.md#L96), [`architecture.md:244`](../architecture.md#L244), [`architecture.md:276`](../architecture.md#L276), [`architecture.md:285`](../architecture.md#L285).
- **Corroborating evidence:** `SpawnChild` accepts caller-fed ancestors, depth, policy limit, and existing parent with permissive defaults ([`SpawnChild.cs:10–32`](../../../src/Hexalith.Works.Contracts/Commands/SpawnChild.cs#L10)); the guard evaluates only those claims ([`WorkTreeAttachmentGuard.cs:30–69`](../../../src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs#L30)); runtime documentation confirms the aggregate does not read authoritative stores ([`work-tree-shape-guard.md:35–42`](../../../docs/work-tree-shape-guard.md#L35)).
- **Found by:** Adversarial ADV-01; security/data-integrity SEC-DI-03.
- **Disposition:** **Discuss.** Choose a child-owned conditional attachment, authoritative tree registry, or saga/process-manager; bind the concurrency boundary, authoritative facts, version/freshness proof, conflict behavior, and repair semantics. No autofix was applied.

### VAL-C03 — Incremental recursive roll-up cannot run through the stated projection seam

- **Issue:** The source binds descendant changes to incremental recursive ancestor roll-up, yet the live EventStore `/project` path supplies one aggregate stream and deliberately cannot reconcile later child changes.
- **Divergence/risk:** A Works projection built to update ancestors has no graph input; an EventStore implementation built to the aggregate-local callback persists rolled values as unavailable. Consumers expecting FR-11 receive data only after an operator-driven shared rebuild.
- **Source:** [`architecture.md:36`](../architecture.md#L36), [`architecture.md:96`](../architecture.md#L96), [`architecture.md:239`](../architecture.md#L239), [`architecture.md:276`](../architecture.md#L276), [`architecture.md:623–629`](../architecture.md#L623).
- **Corroborating evidence:** FR-11 requires every descendant change to reach every ancestor incrementally ([`prd.md:191–198`](../prds/prd-works-2026-06-14/prd.md#L191)); the dispatcher documents and enforces the aggregate-local limitation ([`WorkItemProjectionDispatcher.cs:18–37`](../../../src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs#L18)); the operating contract states that only shared rebuild obtains a coherent tree and that ordinary delivery again marks child-dependent shapes unavailable ([`work-roll-up-projection.md:67–100`](../../../docs/work-roll-up-projection.md#L67)).
- **Found by:** Adversarial ADV-02.
- **Disposition:** **Discuss.** Select a relationship-aware named projection with durable fan-out, explicit child-contribution protocol, or a rebuild-only product contract; bind ownership, inputs, delivery, ordering, failure, freshness, and unavailable semantics. No autofix was applied.

### VAL-C04 — Tenant keys are not an authorization or identity-provenance contract

- **Issue:** The document requires tenant scoping and query authorization but never names the authoritative source of tenant/actor identity, binds it to an authenticated principal or delegation, or sets action-level policy; it defers “real auth.”
- **Divergence/risk:** An external or internal caller can choose another tenant while satisfying every key-equality check. Tenant prefixes prevent accidental collisions only after an untrusted tenant assertion has already been accepted.
- **Source:** [`architecture.md:44`](../architecture.md#L44), [`architecture.md:244–246`](../architecture.md#L244), [`architecture.md:402–404`](../architecture.md#L402).
- **Corroborating evidence:** Commands carry caller-provided tenant identity ([`CreateWorkItem.cs:18–29`](../../../src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs#L18)); query handlers use envelope tenant values directly ([`WhatsNextQueryHandler.cs:47–65`](../../../src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs#L47), [`GetWorkItemQueryHandler.cs:47–61`](../../../src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs#L47)); the authorization helper permits access when no predicate is supplied ([`WhatsNextQueryAuthorization.cs:6–33`](../../../src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs#L6)).
- **Found by:** Security/data-integrity SEC-DI-01.
- **Disposition:** **Discuss.** Bind authentication, claim-to-tenant/actor derivation, assertion mismatch behavior, service delegation, policy ownership, and minimum authorization for command/query/admin/rebuild/replay actions. No autofix was applied.

### VAL-C05 — Events, reminders, replay, and callbacks lack trusted-origin rules

- **Issue:** Delivery rules cover duplication and ordering, not producer authenticity or direct-route protection at Dapr-facing boundaries.
- **Divergence/risk:** A self-consistent forged event with a high canonical sequence can poison projection watermarks and trigger cascade, cancellation, expiry, or resume side effects. Idempotency proves repetition safety, not authenticity.
- **Source:** [`architecture.md:251–254`](../architecture.md#L251), [`architecture.md:631–647`](../architecture.md#L631).
- **Corroborating evidence:** The subscription endpoint has no application authentication and treats topology as its boundary ([`WorksDomainEventEndpointExtensions.cs:16–28`](../../../src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs#L16)); current access-control configuration says production must add mTLS ([`accesscontrol.works.yaml:8–9`](../../../src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml#L8)); processing checks only agreement among supplied identity fields before side effects ([`WorksDomainEventProcessor.cs:117–145`](../../../src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs#L117)); registered handlers include cascade and reminder paths ([`WorksHost.cs:68–82`](../../../src/Hexalith.Works/Runtime/WorksHost.cs#L68)).
- **Found by:** Security/data-integrity SEC-DI-02.
- **Disposition:** **Discuss.** Bind authenticated workload/sidecar identity, trust domain/namespace, deny-by-default route/network policy, TLS and broker ACLs, exclusive event/replay/reminder originators, and negative migration tests. No autofix was applied.

## High findings

### VAL-H01 — Automatic expiry has no trigger, policy authority, or recovery design

- **Issue:** C2 schedules `ResumeWorkItem` only for `DateReached`; E2 names a Due-Date/TTL policy but leaves its source unresolved. No component is responsible for scheduling, canceling, rescheduling, emitting, or recovering `ExpireWorkItem`.
- **Divergence/risk:** Separate implementers can deliver no automatic expiry, Due-Date-only expiry, TTL-only expiry, or incompatible recovery behavior while claiming conformance to FR-10.
- **Source:** [`architecture.md:261–267`](../architecture.md#L261), [`architecture.md:649–655`](../architecture.md#L649).
- **Corroborating evidence:** FR-10 requires expiry when Due Date or configured TTL passes ([`prd.md:175–183`](../prds/prd-works-2026-06-14/prd.md#L175)); the command declares policy/scheduling out of scope ([`ExpireWorkItem.cs:6–10`](../../../src/Hexalith.Works.Contracts/Commands/ExpireWorkItem.cs#L6)); the live host consumes an expiry event but has no root-expiry producer ([`WorksHost.cs:64–89`](../../../src/Hexalith.Works/Runtime/WorksHost.cs#L64)).
- **Found by:** Rubric walker RW-02.
- **Disposition:** **Discuss.** Decide policy authority, trigger owner, registration identity, reschedule/cancel semantics, at-least-once delivery, idempotency, and missed-firing recovery—or explicitly change FR-10. No autofix was applied.

### VAL-H02 — The dependency rule contradicts the machine-enforced project graph

- **Issue:** `Contracts ← Server ← Projections` says Projections depends on Server; the live, tested rule is that Server, Projections, and Reactor each depend directly on Contracts.
- **Divergence/risk:** An implementation following the architecture introduces a forbidden project reference and fails the repository’s architecture test.
- **Source:** [`architecture.md:326–334`](../architecture.md#L326), [`architecture.md:530–532`](../architecture.md#L530).
- **Corroborating evidence:** Projections references Contracts, not Server ([`Hexalith.Works.Projections.csproj:8–10`](../../../src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj#L8)); the allowlist codifies the direct-to-Contracts graph ([`DependencyDirectionTests.cs:7–25`](../../../tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs#L7)); the accepted boundary record agrees ([`boundary-decision-record.md:138–140`](../../../docs/boundary-decision-record.md#L138)).
- **Found by:** Rubric walker RW-03.
- **Disposition:** **Autofix in Update.** Replace the chain with an explicit graph: `Server → Contracts`, `Projections → Contracts`, `Reactor → Contracts`; the executable may reference the inward units plus SDK. Validation applied no change.

### VAL-H03 — “Creation order” is absent from the model and replaced by ID order

- **Issue:** A2 and FR-20 require Priority → Due Date → creation order, but no immutable cross-aggregate creation coordinate is present. The live comparer uses ordinal WorkItem ID.
- **Divergence/risk:** Producers using timestamp/global position and consumers using ID order return different items for equal priority and due date; arbitrary valid IDs do not imply creation order.
- **Source:** [`architecture.md:232–240`](../architecture.md#L232), [`architecture.md:614–621`](../architecture.md#L614).
- **Corroborating evidence:** The PRD and epics require creation order ([`prd.md:281–287`](../prds/prd-works-2026-06-14/prd.md#L281), [`epics.md:1141–1157`](../epics.md#L1141)); the live comparator uses `WorkItemId.Value` ([`WhatsNextOrdering.cs:62–71`](../../../src/Hexalith.Works.Projections/Strategies/WhatsNextOrdering.cs#L62)); the read model has no creation field ([`WhatsNextItem.cs:27–38`](../../../src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs#L27)); runtime documentation calls ID ordering a substitute ([`whats-next-projection.md:16–32`](../../../docs/whats-next-projection.md#L16)).
- **Found by:** Rubric walker RW-04; adversarial ADV-08.
- **Disposition:** **Discuss.** Add a replay-stable globally comparable creation coordinate with tie rules, or obtain product approval to change the PRD/epics/architecture to deterministic identity order. No autofix was applied.

### VAL-H04 — The 732-line legacy shape has unstable decision identity

- **Issue:** Analysis, rationale, seed, repeated rules, and self-validation coexist without stable `AD-n` entries or `Binds` / `Prevents` / `Rule`. Labels collide (`D-1` versus `D1`, `D-2` versus `D2`).
- **Divergence/risk:** Downstream work can cite the wrong decision; repeated prose drifts independently; volatile structure and version seed obscure durable invariants. The dependency and version defects are concrete examples.
- **Source:** [`architecture.md:22–24`](../architecture.md#L22), [`architecture.md:90–129`](../architecture.md#L90), [`architecture.md:209–287`](../architecture.md#L209), [`architecture.md:444–591`](../architecture.md#L444), [`architecture.md:593–732`](../architecture.md#L593).
- **Corroborating evidence:** The deterministic linter can only perform placeholder checks because there are no `AD-n` decision blocks; see [`lint-spine.json`](lint-spine.json) and [RW-05](reviews/review-rubric-walker.md#rw-05--high--legacy-document-shape-makes-decision-identity-and-enforcement-ambiguous).
- **Found by:** Rubric walker RW-05.
- **Disposition:** **Discuss.** Distill accepted invariants into a current spine with stable IDs and legacy-label mapping; retain rationale/history separately and subordinate code-owned seed to authoritative files. No autofix was applied.

### VAL-H05 — Active technology/version claims are stale and conflate Dapr runtime with SDK

- **Issue:** The document contains .NET 10.0.300 and 10.0.301, Dapr 1.18.4, xUnit 3.2.2, Fluent UI rc.3, and an unsupported “mutually compatible/current” statement. Live pins are .NET 10.0.400, Dapr .NET SDK 1.18.5, xUnit 4.0.0, Fluent rc.5, and a mixed Aspire 13.4.6/13.5.3 graph. Dapr runtime and SDK are not separate fields.
- **Divergence/risk:** Builders can downgrade or test the wrong runtime/package combination; the document asserts compatibility that neither configuration nor a gate proves.
- **Source:** [`architecture.md:61`](../architecture.md#L61), [`architecture.md:189–202`](../architecture.md#L189), [`architecture.md:263`](../architecture.md#L263), [`architecture.md:453–455`](../architecture.md#L453), [`architecture.md:595–601`](../architecture.md#L595).
- **Corroborating evidence:** Root SDK ([`global.json:1–11`](../../../global.json#L1)); effective central packages ([`Directory.Packages.props:109–146`](../../../references/Hexalith.Builds/Props/Directory.Packages.props#L109), [`:226–227`](../../../references/Hexalith.Builds/Props/Directory.Packages.props#L226), [`:318–321`](../../../references/Hexalith.Builds/Props/Directory.Packages.props#L318)). Official sources corroborate [.NET SDK 10.0.400](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [Dapr .NET SDK releases](https://github.com/dapr/dotnet-sdk/releases), [xUnit 4.0.0 and its MTP v2 change](https://xunit.net/releases/v3/4.0.0), [Fluent UI package history](https://www.nuget.org/packages/Microsoft.FluentUI.AspNetCore.Components), [Aspire.Hosting 13.5.3](https://www.nuget.org/packages/Aspire.Hosting/13.5.3), and current [Dapr runtime releases](https://github.com/dapr/dapr/releases).
- **Found by:** Rubric walker RW-06; technology TECH-01, TECH-02, TECH-03.
- **Disposition:** **Autofix in Update** for the factual pins: make `global.json` and central packages authoritative, label June values historical if retained, and separate Dapr runtime from .NET SDK. **Discuss** the supported Aspire family and runtime deployment pin. Validation applied no change.

### VAL-H06 — Cascade checkpoints are not concurrency-safe or operationally fail-safe

- **Issue:** The architecture requires resumable checkpointed cascades but binds neither checkpoint identity/state machine nor monotonic concurrency protocol, message identity, replica cardinality, durable retry, degraded health, or alerting.
- **Divergence/risk:** Two reconcilers can regress `Completed` to incomplete through last-write-wins behavior, reintroduce or strand the global index, and exhaust retries while the service still reports healthy.
- **Source:** [`architecture.md:99–101`](../architecture.md#L99), [`architecture.md:253`](../architecture.md#L253), [`architecture.md:374–377`](../architecture.md#L374), [`architecture.md:623–629`](../architecture.md#L623).
- **Corroborating evidence:** Dispatch uses `Pending → Attempted → submit → Completed` ([`CascadeDispatcher.cs:170–209`](../../../src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs#L170)); the store assumes one writer and performs read/check plus unconditional save ([`ReadModelCascadeCheckpointStore.cs:7–13`](../../../src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs#L7), [`:39–80`](../../../src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs#L39)); recovery logs and swallows top-level and per-entry failures ([`CascadeRecoveryService.cs:19–32`](../../../src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs#L19), [`CascadeRecoveryReconciler.cs:43–96`](../../../src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryReconciler.cs#L43)).
- **Found by:** Adversarial ADV-04; security/data-integrity SEC-DI-05.
- **Disposition:** **Discuss.** Bind checkpoint key, monotonic states, deterministic command IDs/causation, ETag/CAS or lease/fencing, replica ownership, index convergence, durable retry, degraded readiness, metrics, alerts, and audited operator disposition. No autofix was applied.

### VAL-H07 — Reminder recovery and end-to-end durability are underspecified

- **Issue:** C2 calls reminders durable and recovery resolved while the authoritative discovery query is still listed as a gap. Scheduler persistence does not itself guarantee successful domain resume after callback retry exhaustion.
- **Divergence/risk:** Registration can be lost in the publish/register crash window; a tenant-wide scan can be rejected; a one-shot callback can exhaust retries and remain unrecovered until another successful startup, without durable unhealthy evidence.
- **Source:** [`architecture.md:103`](../architecture.md#L103), [`architecture.md:261–264`](../architecture.md#L261), [`architecture.md:649–655`](../architecture.md#L649), [`architecture.md:706–710`](../architecture.md#L706).
- **Corroborating evidence:** The later epic documents the rejected tenant-wide scan ([`epics.md:1290–1299`](../epics.md#L1290)); the live source introduced registry/index discovery plus stream re-fold ([`IndexedPendingDateAwaitSource.cs:11–79`](../../../src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs#L11)); reconciliation is a bounded startup pass ([`ReminderReconciliationService.cs:9–60`](../../../src/Hexalith.Works/Reminders/ReminderReconciliationService.cs#L9), [`WorksRecoveryOptions.cs:19–29`](../../../src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs#L19)). Official Dapr sources confirm Scheduler became the default and persists jobs ([Scheduler 1.15 documentation](https://v1-15.docs.dapr.io/concepts/dapr-services/scheduler/), [actor reminder documentation](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-timers-reminders/)); they also make the guarantee conditional on Scheduler persistence/topology and callback failure policy.
- **Found by:** Adversarial ADV-05; technology TECH-03; security/data-integrity SEC-DI-05.
- **Disposition:** **Discuss.** Bind authoritative stream truth, discovery-index ownership and update order, scan bounds/cursors/partial failure, Scheduler HA/backup, callback policy, continuous retry/alerting, tenant cleanup, and the exact end-to-end guarantee. No autofix was applied.

### VAL-H08 — Shared rebuild has atomic visibility but no capture-through-Commit fence

- **Issue:** The platform API supports generation staging and marker-gated reader promotion, but it exposes no fence token, writer epoch, capture watermark, or ordinary-delivery coordinator.
- **Divergence/risk:** A live writer can mutate after sealed inventory capture and before Commit; the promoted manifest can omit or later overwrite that write despite readers never seeing a partial generation.
- **Source:** [`architecture.md:47`](../architecture.md#L47), [`architecture.md:105–106`](../architecture.md#L105), [`architecture.md:265`](../architecture.md#L265), [`architecture.md:623–629`](../architecture.md#L623).
- **Corroborating evidence:** Works documentation says the handler cannot arbitrate live writers ([`work-roll-up-projection.md:75–84`](../../../docs/work-roll-up-projection.md#L75), [`whats-next-projection.md:73–82`](../../../docs/whats-next-projection.md#L73)); EventStore exposes the lifecycle route ([`EventStoreDomainServiceExtensions.cs:374–400`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs#L374)) and atomic visibility ([`ReadModelBatchProtocol.cs:281–327`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/ReadModelBatchProtocol.cs#L281)), but its dispatcher has no writer-fence contract ([`DomainSharedProjectionRebuildDispatcher.cs:20–99`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/DomainSharedProjectionRebuildDispatcher.cs#L20), [`:346–403`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/DomainSharedProjectionRebuildDispatcher.cs#L346)).
- **Found by:** Adversarial ADV-06; technology TECH-04.
- **Disposition:** **Discuss.** Define the platform-owned fence: admitted writers, epoch/token, capture watermark, rejection/queueing, atomic scope, abort, resume, and catch-up completion. No autofix was applied.

### VAL-H09 — One universal key derivation conflicts with live namespaces and privileged registries

- **Issue:** The source says every state key, projection key, topic, reminder, group, and log scope derives from `{tenant}:work:{id}`, yet the live system uses a shared topic, hashed reminder identity, generation-based projection keys, notifier groups, and global recovery indexes.
- **Divergence/risk:** Subscribers can listen on incompatible topic names; reminder identities can collide across tenants; global tenant-bearing indexes violate the claimed tenant-only boundary and lack a governed control-plane exception.
- **Source:** [`architecture.md:321–324`](../architecture.md#L321), [`architecture.md:402–404`](../architecture.md#L402), [`architecture.md:539–540`](../architecture.md#L539).
- **Corroborating evidence:** Shared topic ([`Program.cs:57–69`](../../../src/Hexalith.Works.AppHost/Program.cs#L57)); hashed tenant/item/condition reminder name ([`DateReminderName.cs:30–53`](../../../src/Hexalith.Works/Reminders/DateReminderName.cs#L30)); generation and global registry keys ([`WorksReadModelKeys.cs:24–59`](../../../src/Hexalith.Works/Projections/WorksReadModelKeys.cs#L24)); notifier group contract ([`whats-next-projection.md:137–145`](../../../docs/whats-next-projection.md#L137)); global tenant registry and checkpoint index ([`PendingDateAwaitTenantRegistry.cs:3–11`](../../../src/Hexalith.Works/Projections/PendingDateAwaitTenantRegistry.cs#L3), [`ReadModelCascadeCheckpointStore.cs:20–23`](../../../src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs#L20)).
- **Found by:** Adversarial ADV-07; security/data-integrity SEC-DI-06.
- **Disposition:** **Discuss.** Replace the universal rule with a namespace/ownership table and decide whether discovery is tenant-partitioned or a narrow isolated, ACL-protected, audited control-plane exception. No autofix was applied.

### VAL-H10 — Aggregate ID assignment is not command idempotency

- **Issue:** A1 claims retry idempotency from edge-assigned aggregate IDs, but the spine does not bind transport message/idempotency-key reuse, scope, request digest, retention, replay result, or deterministic reactor/reminder submission identity.
- **Divergence/risk:** One client retries with a new message ID and receives a transition rejection; another reuses the original and receives a deduplicated result. Cascade adapters can derive incompatible identities from payload sequence versus envelope message ID.
- **Source:** [`architecture.md:234–235`](../architecture.md#L234), [`architecture.md:253`](../architecture.md#L253), [`architecture.md:374–377`](../architecture.md#L374).
- **Corroborating evidence:** The domain create command contains no message identity ([`CreateWorkItem.cs:17–29`](../../../src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs#L17)); a second create is a domain rejection ([`WorkItemAggregate.cs:18–25`](../../../src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs#L18)); EventStore separately requires `MessageId` and offers `IdempotencyKey` ([`SubmitCommandRequest.cs:5–26`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Commands/SubmitCommandRequest.cs#L5)).
- **Found by:** Adversarial ADV-09.
- **Disposition:** **Discuss.** Bind tenant-scoped transport identity, retry reuse, digest collision behavior, retention, exact-result replay, and deterministic IDs for every internally emitted command; distinguish transport deduplication, semantic no-op, and projection offset deduplication. No autofix was applied.

### VAL-H11 — Schema evolution and quarantine behavior do not form a compatibility contract

- **Issue:** “Additive/tolerant” and “no V2” do not define JSON names, required/default/null behavior, compatibility direction, discriminator stability, unknown enum/event handling, rollout order, or consistent quarantine behavior across replay, projection, subscription, and rebuild.
- **Divergence/risk:** A new enum or event can break an old reader; one surface can acknowledge malformed state-changing data while another halts; a rebuild can promote a candidate whose evidence was silently skipped.
- **Source:** [`architecture.md:63–65`](../architecture.md#L63), [`architecture.md:309–315`](../architecture.md#L309), [`architecture.md:359–361`](../architecture.md#L359), [`architecture.md:409–426`](../architecture.md#L409).
- **Corroborating evidence:** A current string enum treats unknown values as fatal ([`Channel.cs:5–18`](../../../src/Hexalith.Works.Contracts/ValueObjects/Channel.cs#L5)); the claimed 40-type catalog differs from the frozen 37-type corpus ([`WorkItemV1Catalog.cs:9–18`](../../../tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs#L9), [`EventShapeGovernanceTests.cs:18–44`](../../../tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventShapeGovernanceTests.cs#L18)); decoding returns null for unknown/malformed input ([`WorksEventDecoder.cs:24–44`](../../../src/Hexalith.Works/Runtime/WorksEventDecoder.cs#L24)); subscription can answer 200 while projection throws ([`WorksDomainEventEndpointExtensions.cs:69–81`](../../../src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs#L69), [`WorkItemProjectionDispatcher.cs:108–117`](../../../src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs#L108)).
- **Found by:** Adversarial ADV-10; security/data-integrity SEC-DI-07.
- **Disposition:** **Discuss.** Bind an old/new reader-writer matrix, wire-name/discriminator rules, expand-migrate-contract order, catalog authority, and a durable fail-safe quarantine/replay contract. No autofix was applied.

### VAL-H12 — Immutable event data has no privacy lifecycle

- **Issue:** “Do not log payloads” is the only concrete privacy rule. The source does not classify durable fields or bind minimization, encryption/key ownership, access audit, retention, legal hold, tenant offboarding, erasure/crypto-shredding, or equivalent controls for DLQs, backups, and snapshots.
- **Divergence/risk:** Raw events permanently retain free text, executor identity, correlation, and progress notes without a governed production lifecycle; event-sourcing immutability turns this into an architectural decision, not later adapter hardening.
- **Source:** [`architecture.md:49`](../architecture.md#L49), [`architecture.md:226–230`](../architecture.md#L226), [`architecture.md:406–407`](../architecture.md#L406).
- **Corroborating evidence:** Durable contracts contain the relevant fields ([`WorkItemCreated.cs:8–18`](../../../src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs#L8), [`ProgressReported.cs:8–15`](../../../src/Hexalith.Works.Contracts/Events/ProgressReported.cs#L8), [`WorkItemAssigned.cs:8–13`](../../../src/Hexalith.Works.Contracts/Events/WorkItemAssigned.cs#L8)); golden payloads persist them verbatim ([`WorkItemCreated.v1.json:1`](../../../tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/WorkItemCreated.v1.json#L1), [`ProgressReported.v1.json:1`](../../../tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/ProgressReported.v1.json#L1)).
- **Found by:** Security/data-integrity SEC-DI-04.
- **Disposition:** **Discuss.** Establish field classification/minimization, opaque-reference rules, encryption and key lifecycle, least-privilege access/replay/export, audited admin reads, retention/hold/offboarding, and supported erasure before production data is admitted. No autofix was applied.

## Medium tail

These four deduplicated items are important but do not outrank the decision blockers above. Full evidence remains in the linked reviews.

| ID | Finding | Source and evidence | Lens | Disposition |
| --- | --- | --- | --- | --- |
| VAL-M01 | “Build-time” architecture enforcement is not wired and the focused architecture-test project currently fails compilation before tests run. | [`architecture.md:102–105`](../architecture.md#L102), [`:391–399`](../architecture.md#L391), [`:423–427`](../architecture.md#L423), [`:584–589`](../architecture.md#L584); [`Directory.Build.targets:1–2`](../../../Directory.Build.targets#L1), [`BuildConfigurationTests.cs:10–20`](../../../tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs#L10). | RW-07 | Discuss: wire an explicit CI/test lane or describe the actual gate; repair the lane. |
| VAL-M02 | “Dapr pub/sub is not ordered” is too absolute. At-least-once is portable; ordering depends on component/configuration, so Works should simply forbid relying on it. | [`architecture.md:252`](../architecture.md#L252), [`:668–669`](../architecture.md#L668); [Dapr pub/sub overview](https://docs.dapr.io/developing-applications/building-blocks/pubsub/pubsub-overview/), [GCP message ordering](https://docs.dapr.io/reference/components-reference/supported-pubsub/setup-gcp-pubsub/). | TECH-05 | Autofix in Update; not applied. |
| VAL-M03 | Same-actor command races are normally serialized by the actor turn lock; ETag conflict is a fallback path, not the required loser mechanism. | [`architecture.md:97`](../architecture.md#L97), [`:251`](../architecture.md#L251), [`:397–400`](../architecture.md#L397); [Dapr actor concurrency](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-features-concepts/), [`AggregateActor.cs:1024–1121`](../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs#L1024). | TECH-06 | Autofix in Update; not applied. |
| VAL-M04 | Default `AddProblemDetails` registration proves a standard shape, not the full RFC 9457 plus tenant/correlation and no-leakage contract claimed by the source. | [`architecture.md:49`](../architecture.md#L49), [`:250`](../architecture.md#L250), [`:367`](../architecture.md#L367); [`WorksHost.cs:45–48`](../../../src/Hexalith.Works/Runtime/WorksHost.cs#L45), [`WorksDomainEventSubscriptionTests.cs:159–166`](../../../tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs#L159); [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html), [ASP.NET Core error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0), [ASP.NET Core RFC 9457 tracking issue](https://github.com/dotnet/aspnetcore/issues/52414). | TECH-07 | Autofix in Update for wording, then add conformance tests; not applied. |

## Good-spine checklist scorecard

**Score: 0 of 8 dimensions pass; 5 fail and 3 remain concerns.** The strong local rules—pure event-sourced handling, envelope sequencing, tenant-hop checks, order-tolerant projection posture, mechanical reactor, and atomic read-model promotion—are worth retaining, but they do not close the cross-unit seams.

| Dimension | Result | Assessment |
| --- | --- | --- |
| Real divergence points fixed | **Fail** | Host ownership, trust, tree attachment, live roll-up, expiry, recovery, and ordering remain open or contradictory. |
| Rules enforceable and preventive | **Fail** | Legacy blocks lack stable `AD-n`/Binds/Prevents/Rule shape; claimed build gates are not currently operational. |
| Deferred items safe | **Concern** | Themes 3–6 are mostly cleanly deferred, but auth/privacy, host ownership, and expiry are current safety/capability requirements. |
| Named technology current | **Fail** | Multiple active pins conflict with authoritative files and Dapr runtime/SDK are conflated. |
| Brownfield ratification | **Fail** | Dependency graph, namespace rules, ordering, projection seam, runtime layout, and test-gate claims differ from live code. |
| Spec capability coverage | **Concern** | The FR map is broad, but FR-10 expiry initiation, FR-11 incremental recursion, and FR-20 creation order are not delivered by the chosen seams. |
| Inherited constraints honored | **Fail** | The domain/platform boundary is stated but cannot be executed without a named destination, owner, and migration allocation. |
| Structural dimensions complete | **Concern** | Domain/data/test dimensions are rich; deployment ownership, security trust, operations/recovery, and privacy lifecycle are incomplete. |

## Prioritized update plan

1. **Decision workshop — unblock the spine first.** Resolve VAL-C01 through VAL-C05: platform owner/allocation, atomic tree authority, live recursive roll-up protocol, authenticated tenant/actor authority, and trusted callback/event origins. In the same workshop settle the product-affecting expiry and creation-order forks.
2. **Correct document/live-code contracts.** Apply the dependency graph and authoritative version-source fixes; define cascade/reminder/rebuild protocols, namespace/control-plane exceptions, command idempotency, schema/quarantine behavior, privacy lifecycle, and the exact architecture-test/CI gate. Distill accepted decisions into stable `AD-n` entries with Binds/Prevents/Rule and a legacy-label map.
3. **Reconcile and re-run the gate.** Update PRD/epics where product decisions changed, verify Story 4.9’s migration matrix and negative/conformance tests against the named platform host, then rerun deterministic lint and all four review lenses.

## Review artifacts

- [Deterministic lint JSON](lint-spine.json)
- [Rubric walker review](reviews/review-rubric-walker.md)
- [Adversarial divergence review](reviews/review-adversarial-divergence.md)
- [Technology/reality review](reviews/review-technology-reality.md)
- [Security/data-integrity review](reviews/review-security-data-integrity.md)

This report is a validation artifact only. No finding was applied to [`architecture.md`](../architecture.md).
