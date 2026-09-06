# Security and Data-Integrity Architecture Review

**Artifact:** `_bmad-output/planning-artifacts/architecture.md`  
**Intent:** Validate only; the architecture and runtime sources were not changed.  
**Lens:** Multi-tenant event-sourced platform boundary: identity/authz, Dapr trust, pub/sub provenance, privacy, replay, sequence authority, tree/roll-up integrity, recovery/checkpoints, failure posture, and operational evidence.  
**Verdict:** **REJECT / NOT IMPLEMENTATION-READY.** The document has strong tenant-key, replay-order, roll-up, rebuild, and idempotency mechanics, but it does not fix who may assert tenant/actor identity or who may originate a trusted event. Those omissions let an internally reachable caller select another tenant or forge a self-consistent high-sequence event that can drive projections, reminders, and cascades. Four additional high-severity invariants are also missing at the persistence and recovery boundaries.

## Finding summary

| ID | Severity | Disposition | Missing invariant |
|---|---|---|---|
| SEC-DI-01 | Critical | Discuss | Authoritative tenant/actor provenance and action authorization |
| SEC-DI-02 | Critical | Discuss | Authenticated event/reminder/replay origin at the Dapr boundary |
| SEC-DI-03 | High | Discuss | Authoritative, concurrency-safe ownership of tree facts |
| SEC-DI-04 | High | Discuss | Privacy lifecycle for immutable raw-event data |
| SEC-DI-05 | High | Autofix | Recovery exhaustion must become durable unhealthy/alerted state |
| SEC-DI-06 | High | Discuss | Governed exception for cross-tenant recovery registries |
| SEC-DI-07 | Medium | Autofix | Uniform quarantine semantics for unknown/malformed schema |

Counts: **2 Critical, 4 High, 1 Medium, 0 Low**.

## Critical findings

### SEC-DI-01 — Tenant key equality is not tenant authorization

**Missing invariant.** The spine requires tenant-scoped commands, queries, keys, projections, and logs, and calls query authorization a separate control (`architecture.md:44`, `architecture.md:244-246`, `architecture.md:402-404`). It never names the authoritative source of `TenantId`/`UserId`, binds an authenticated principal or service delegation to that tenant, or defines per-action authorization. It simultaneously defers “real auth” to Theme 6 (`architecture.md:245-246`). This makes the mandatory-v1 isolation claim non-enforceable: two implementers can either trust the envelope/body or derive tenant identity from verified claims.

**Brownfield evidence.** Commands carry caller-provided tenant identity (`src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs:18-29`). `WhatsNextQueryHandler` uses `QueryEnvelope.TenantId` directly as the read key and supplies no caller predicate (`src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs:47-65`, `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs:102-105`). `GetWorkItemQueryHandler` does the same (`src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs:47-61`). The purported authorization helper explicitly treats a `UserId` policy as a future optional seam and authorizes when no predicate is provided (`src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs:6-13`, `src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs:21-33`). The integration test can freely construct both tenant and user values (`tests/Hexalith.Works.IntegrationTests/GetWorkItemQueryHandlerTests.cs:150-162`), proving only key separation, not authorization.

**Required architecture rule.** At every external ingress, the platform MUST authenticate the caller and derive the authoritative tenant and actor from verified claims; payload/envelope tenant and user fields are assertions to compare, never authority. A mismatch or missing binding MUST be denied before aggregate dispatch, persistence, query execution, or tenant existence disclosure. Internal reactor/reminder/replay commands MUST use an authenticated workload identity plus an explicit, auditable tenant-delegation context. The spine must assign policy ownership and minimum authorization for create/read/query/claim/admin/rebuild/replay actions; `AuthorityLevel` being carried-not-enforced cannot stand in for this policy.

**Why critical.** Tenant-prefixed keys stop accidental collisions only after the caller has been allowed to choose a tenant. Without provenance, a valid credential or internal caller can select another tenant and pass every key-equality check.

### SEC-DI-02 — Pub/sub and actor/replay callbacks have no trusted-origin contract

**Missing invariant.** B2 specifies delivery frequency and order but not producer authenticity (`architecture.md:251-254`). The target platform host is unnamed (`architecture.md:631-647`), so the security properties that must survive Story 4.9 migration are not part of the consistency contract.

**Brownfield evidence.** The subscription route deliberately has no application authentication and declares deployment topology to be its protection boundary (`src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:16-28`). The current Works Dapr configuration says callers are not mutually authenticated and production must add mTLS (`src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml:8-9`). Its identity checks prove only that attacker-controlled envelope and payload fields agree (`src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:117-127`), then trusted metadata is passed to handlers (`src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:129-145`). Those handlers include cascade, child-resume, and reminder side effects (`src/Hexalith.Works/Runtime/WorksHost.cs:68-82`). Pub/sub scopes are useful least privilege but are local Redis configuration, not message-origin attestation (`src/Hexalith.Works.AppHost/DaprComponents/pubsub.yaml:20-43`).

**Required architecture rule.** Production subscription, actor callback, projection, replay, and admin routes MUST accept traffic only from an authenticated sidecar/workload in the declared trust domain and namespace, with deny-by-default service and network policy; direct workload HTTP reachability MUST be prohibited or equivalently authenticated. Broker access MUST use TLS plus producer/consumer ACLs. Only EventStore may originate `work.events`; only the authorized operations service may replay a quarantined item; reminder callbacks must be bound to the Works actor identity. A valid event requires authenticated provenance as well as envelope/payload identity equality. Migration acceptance MUST include negative tests for direct-route calls, spoofed app IDs, wrong trust domain/namespace, unauthorized topic publication, and forged high `SequenceNumber` deliveries.

**Why critical.** A self-consistent forged event can carry an arbitrarily high canonical envelope sequence, poison last-write-wins watermarks, and trigger cancel/expire/resume side effects. Idempotency does not establish authenticity.

## High findings

### SEC-DI-03 — The acyclic/single-parent tree guarantee relies on caller-supplied proof

**Missing invariant.** The architecture promises an acyclic, single-parent, single-tenant bounded tree and says the server tree guard enforces it (`architecture.md:36`, `architecture.md:96`, `architecture.md:101`, `architecture.md:276`). It does not say who owns parent/ancestor/depth facts, how they are read consistently, or how two parents concurrently claiming the same child are serialized.

**Brownfield evidence.** `CreateWorkItem` documents ancestor, depth, and max-depth facts as caller-fed (`src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs:7-15`). `SpawnChild` additionally accepts `ExistingChildParent` from the caller (`src/Hexalith.Works.Contracts/Commands/SpawnChild.cs:6-16`, `src/Hexalith.Works.Contracts/Commands/SpawnChild.cs:19-32`). The aggregate passes those unverified facts directly to the pure guard (`src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:39-50`, `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:103-112`), while the guard can only compare what it was given (`src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs:30-65`).

**Required architecture rule.** Define one authoritative owner for a child’s parent edge and one concurrency boundary for attachment. Ancestor/depth/current-parent facts MUST be derived from authoritative tenant-scoped state at the adapter/process-manager boundary, carry a freshness/version proof, and be revalidated inside the write transaction or compensating protocol; arbitrary external callers MUST NOT supply trusted topology facts or policy limits. Concurrent attaches from different parents MUST deterministically yield one accepted edge. Rebuild MUST detect missing, cyclic, multiply-parented, or asymmetric parent/child evidence and suppress every dependent roll-up/cascade result until repaired.

**Disposition rationale.** Discuss: choosing child-owned conditional attachment, a tree registry, or a saga is a real cross-aggregate trade-off; the current spine cannot merely call the pure guard sufficient.

### SEC-DI-04 — “Do not log payloads” is not a privacy architecture for immutable raw acts

**Missing invariant.** The only privacy rule forbids logging payloads and personal data (`architecture.md:49`, `architecture.md:406-407`), while security hardening is deferred (`architecture.md:226-230`). There is no data classification, field minimization, encryption/key ownership, access audit, retention, legal hold, tenant offboarding, or erasure/crypto-shredding rule for event streams, read models, dead letters, snapshots, backups, and telemetry.

**Brownfield evidence.** Durable raw events contain free-text obligation, executor identity, conversation correlation, and progress notes (`src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs:8-18`, `src/Hexalith.Works.Contracts/Events/ProgressReported.cs:8-15`, `src/Hexalith.Works.Contracts/Events/WorkItemAssigned.cs:8-13`). Golden payloads demonstrate those values are persisted verbatim (`tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/WorkItemCreated.v1.json:1`, `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/ProgressReported.v1.json:1`). Event sourcing and the promise that every historical event remains readable (`architecture.md:63-65`) make retention and erasure a load-bearing decision now, not a Theme-6 adapter concern.

**Required architecture rule.** Classify every durable field and prohibit secrets/sensitive conversation content in raw acts; store only opaque references where the owning module can enforce lifecycle. Require encryption in transit and at rest, tenant-aware key ownership/rotation, least-privilege read/replay/export access, audited administrative reads, and equivalent controls for DLQs/backups/snapshots. Define retention/legal-hold/offboarding and erasure behavior (including whether crypto-shredding is the supported answer) before production data is admitted.

### SEC-DI-05 — Recovery can exhaust and remain healthy with incomplete durable work

**Missing invariant.** C1/C2 promise checkpoint-driven cascade and reconciliation of lost reminders (`architecture.md:253-265`), and validation claims the recovery NFR is addressed (`architecture.md:623-629`). The spine does not define the fail-open/fail-closed posture once bounded retries are exhausted, readiness behavior, persistent retry cadence, operator alerts, or evidence required to prove no tenant is stranded.

**Brownfield evidence.** Reminder startup reconciliation stops after its configured attempts and returns without failing the host (`src/Hexalith.Works/Reminders/ReminderReconciliationService.cs:37-59`); defaults are five attempts and a feature switch can disable it (`src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs:19-29`). Cascade recovery logs and swallows its top-level failure (`src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryService.cs:19-32`), while individual entry failures are also logged and the pass continues (`src/Hexalith.Works/Recovery/Cascade/CascadeRecoveryReconciler.cs:43-96`). A restart is therefore the only guaranteed later attempt described by the code.

**Required architecture rule.** Exhausted reminder/cascade/checkpoint recovery MUST create durable unresolved-work evidence, mark a named readiness/health signal degraded, increment tenant-scoped metrics, and page/alert under an SLO. Recovery MUST retry on a durable schedule independent of process restart until success or explicit operator disposition. The platform may keep unrelated tenants available, but affected tenant/capability status must not report healthy or complete. Every operator disposition must be authenticated, authorized, and audited.

**Disposition rationale.** Autofix: the spine can add this fail-safe invariant without choosing implementation details; Story 4.9 must carry it into the platform-owned host acceptance gate.

### SEC-DI-06 — Global recovery registries contradict “every key is tenant-scoped”

**Missing invariant.** The spine states every key and every state boundary is tenant-scoped (`architecture.md:321-324`, `architecture.md:402-404`, `architecture.md:539-540`) but says nothing about cross-tenant discovery/control-plane state.

**Brownfield evidence.** The pending-await tenant registry has one global key (`src/Hexalith.Works/Projections/WorksReadModelKeys.cs:52-59`) and explicitly contains all tenant IDs (`src/Hexalith.Works/Projections/PendingDateAwaitTenantRegistry.cs:3-11`). The cascade checkpoint index is also one global key (`src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:20-23`) containing tenant-bearing identities; index and checkpoint are deliberately separate non-atomic writes (`src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:60-80`). The current Dapr state-store component is shared by Works, EventStore, Admin, and Operations (`src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml:27-31`).

**Required architecture rule.** Either partition recovery discovery by tenant, or declare a narrow cross-tenant control-plane exception owned by the platform. For an exception, require an isolated component/store or equivalently enforced key-level ACL, minimize entries to opaque identifiers, protect every mutation with ETag/CAS, define index-record reconciliation for every crash window, constrain enumeration to a dedicated recovery identity, and audit reads/mutations/deletions. Tenant deletion/offboarding must remove registry, index, checkpoint, and reminder state without exposing other tenant identifiers.

**Disposition rationale.** Discuss: partitioning versus a privileged global registry is an architectural trade-off that Story 4.9 cannot inherit implicitly.

## Medium finding

### SEC-DI-07 — Unknown/malformed events lack one durable quarantine contract

**Missing invariant.** The spine requires additive tolerant evolution and a golden corpus (`architecture.md:359-361`, `architecture.md:420-426`) but does not define consistent behavior when an old/new/invalid schema reaches aggregate replay, ordinary projection, shared rebuild, reactor subscription, or operator replay.

**Brownfield evidence.** The event decoder returns `null` for unknown or malformed input (`src/Hexalith.Works/Runtime/WorksEventDecoder.cs:24-44`). The subscription endpoint maps unknown, mismatch, and invalid-payload results to HTTP 200 (`src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:69-81`), and terminal skip processing marks the message complete best-effort (`src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:293-320`). By contrast, the projection dispatcher throws for undecodable state-affecting events (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:108-117`). These can all be defensible choices, but they are incompatible defaults unless the spine fixes which surfaces may skip, degrade, quarantine, or halt.

**Required architecture rule.** Unknown or malformed state-affecting events MUST never be acknowledged as successfully applied. They must enter a durable tenant-scoped quarantine with immutable source bytes/hash, reason code, sequence, schema/type, and authenticated replay audit. Aggregate replay halts; ordinary projections retain the last proven state and expose degradation; shared rebuild cannot Commit a candidate with unresolved state-affecting evidence; reactors emit no side effect. Explicitly non-state-affecting unknown types may be skipped only under a versioned allowlist and still leave operational evidence. Golden tests must cover reader compatibility and each failure disposition, not only byte stability.

**Disposition rationale.** Autofix: the cross-surface fail-safe rule belongs in the spine; implementation-specific quarantine plumbing remains platform-owned.

## Positive controls retained

- Envelope `SequenceNumber` versus payload `Sequence` is explicitly separated (`architecture.md:352-357`), and persisted ordering/replay is documented against the EventStore envelope (`docs/eventstore-api-surface-constraints.md:19-33`). Keep this rule; add authenticated provenance before accepting the envelope as canonical.
- Roll-up delivery checks compare payload and delivery tenant/item identities and fail closed on missing shapes (`src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs:43-68`). Keep all six isolation checks and the mutation tests.
- Shared rebuild rejects an empty candidate rather than promoting an empty tenant (`src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs:25-31`) and marks incomplete relationship evidence (`src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs:159-194`). Keep atomic Commit and delivery fencing; add authorized operator identity and quarantine evidence.
- Cascade dispatch persists attempted state before command submission and reuses deterministic target commands (`src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs:170-209`). Keep this crash boundary; add the exhausted-recovery health contract in SEC-DI-05.

## Gate decision

Do not mark the architecture implementation-ready until SEC-DI-01 and SEC-DI-02 are adopted as explicit cross-platform invariants with named owners and negative acceptance tests. SEC-DI-03, SEC-DI-04, SEC-DI-05, and SEC-DI-06 should be resolved before production data or Story 4.9 platform migration is accepted. SEC-DI-07 may be folded into the same migration as a platform quarantine contract.
