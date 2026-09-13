---
name: Hexalith.Works
type: architecture-spine
purpose: build-substrate
altitude: feature
paradigm: event-sourced DDD with CQRS, actor single-writer aggregates, and a saga process manager
scope: Works domain module, EventStore SDK seams, and Hexalith.Platform host boundary
status: final
readiness: conditional
created: 2026-06-14
updated: 2026-09-12
binds:
  - FR-1..FR-26
  - Works security, recovery, data-integrity, and platform-migration NFRs
sources:
  - _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md
  - _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/.memlog.md
  - _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md
  - _bmad-output/planning-artifacts/architecture-validation-2026-09-12/ARCHITECTURE-VALIDATION.md
  - docs/eventstore-api-surface-constraints.md
  - references/Hexalith.AI.Tools/hexalith-llm-instructions.md
companions:
  - architecture/architecture-works-2026-09-08/.memlog.md
---

# Architecture Spine — Hexalith.Works

This spine is the binding consistency contract. The code owns implemented structure; the PRD owns
product behavior. Until the rendered PRD and addendum receive their pending refresh, the final four
override entries in the PRD memlog win over their contradictory prose. Historical architecture prose
was removed during the 2026-09-12 update because it mixed rationale, stale seed, and binding rules.
Decision rationale remains in the architecture memlog.

Decision tags separate commitment from delivery: **ADOPTED** means the decision is settled,
**CURRENT** means repository evidence verifies it today, **TARGET** means it is a binding migration
outcome, and **ASSUMPTION** means the Fast-path update chose a fail-closed default that remains
explicit until user direction or implementation evidence replaces it. A decision may be adopted
without being implemented.

## Design Paradigm

Hexalith.Works uses **event-sourced domain-driven design with CQRS**. A Dapr actor gives each aggregate
one logical writer; pure aggregate handlers emit raw-act events; projections build disposable read
models; and the **Reactor** is a Saga Process Manager that translates committed events into
cross-aggregate commands without making domain decisions.

The domain module owns business contracts and pure behavior. Hexalith.EventStore owns reusable
runtime mechanics. Hexalith.Platform owns topology, environment policy, and operations.

~~~mermaid
flowchart LR
    Edge[Authenticated edge] --> Gateway[EventStore gateway]
    Gateway --> Registry[Work-Tree Registry actor]
    Gateway --> Item[Work Item actor]
    Registry --> RE[Registry events]
    Item --> WE[Work events]
    RE --> Reactor[Works Reactor]
    WE --> Reactor
    RE --> Projection[Projection delivery]
    WE --> Projection
    Reactor --> Gateway
    Projection --> Reads[(Disposable read models)]
    Platform[Hexalith.Platform] -. composes .-> Gateway
    Platform -. composes .-> Reactor
    Platform -. operates .-> Reads
~~~

## Inherited Constraints

There is no parent spine. These repository-wide constraints are binding inputs.

| Constraint | Source | Consequence |
| --- | --- | --- |
| Domain modules are domain-centric | Hexalith LLM baseline | Reusable hosting, Dapr, projection, reminder, and recovery plumbing lands in EventStore first |
| EventStore is the persistence substrate | Hexalith LLM baseline | Commands in, events out; state reconstructs by replay; no parallel repository |
| Dapr is the infrastructure abstraction | Hexalith LLM baseline | No direct broker/database clients in domain contracts or behavior |
| Root-declared submodules only | Repository guidance | Architecture and build evidence never depend on nested-submodule initialization |
| .slnx, .NET 10+, C# 14+ | Repository guidance and build files | Build and test lanes use the checked solution and centrally pinned dependencies |

## Invariants & Rules

### AD-01 [ADOPTED] [CURRENT] — Event-sourced kernel on Hexalith.EventStore

- **Binds:** persistence, event envelopes, serialization, and error semantics for every Works aggregate.
- **Prevents:** parallel persistence, pre-persist publication, forged envelope metadata, and incompatible
  durable formats.
- **Rule:** Handle(state, command) is pure and returns success events, a rejection event, or no-op;
  Apply(event) mutates only in-memory state. EventStore assigns canonical envelope metadata and
  persists before publishing. Durable Works types use Hexalith.PolymorphicSerializations and the
  rollout contract in AD-29. Domain rejections implement IRejectionEvent; infrastructure failures do
  not masquerade as domain events.

### AD-02 [ADOPTED] [TARGET] — Aggregate identity is assigned at the edge

- **Binds:** identity creation for Work Items and registry commands.
- **Prevents:** replay-time or handler-local randomness and nondeterministic retries.
- **Rule:** current contracts accept existing non-whitespace AggregateIdentity-compatible IDs and
  handlers never generate them. The target authenticated command-creation edge assigns sortable ULIDs
  through Hexalith.Commons; existing IDs remain readable. No state transition, Reactor translation, or
  projection generates an aggregate ID. The literal Work-Tree Registry aggregate ID registry is the
  sole reserved system-aggregate exception. Cross-aggregate transport identity follows AD-26.

### AD-03 [ADOPTED] [CURRENT] [TARGET] — Priority and “what's next” ordering are total and deterministic

- **Binds:** FR-4 and FR-20 query filtering and ordering.
- **Prevents:** incompatible queue views and replay-dependent ties.
- **Rule:** Priority orders Critical > High > Normal > Low, absent last. The current tenant-scoped
  query returns all Assigned and Queued items. The adopted target optionally accepts an Executor
  PartyId and then returns that Party's Assigned items plus the tenant Queued pool. Both order by
  Priority, earliest Due Date (absent last), then ordinal WorkItemId. No routing score or creation
  coordinate participates.

### AD-04 [ADOPTED] [TARGET] — Effort Unit is immutable and inherited only at first estimate

- **Binds:** FR-3 and FR-12 across aggregate, child creation, projection, and query paths.
- **Prevents:** silent conversion and independently chosen child units.
- **Rule:** the first estimate fixes Unit; later progress, correction, and re-estimation with another
  Unit reject. A child without an explicit Unit inherits its parent's Unit for its first estimate.
  Mixed-Unit trees require an explicit child Unit and expose per-Unit subtotals only.

### AD-05 [ADOPTED] [CURRENT] — WorkItemEffort is the burn-down contract

- **Binds:** effort vocabulary and shape.
- **Prevents:** a second meter abstraction diverging from the public contract.
- **Rule:** WorkItemEffort(Unit, Estimated, Done) is the sole v1 effort type; Remaining is derived and
  never stored as authoritative state. Future cost support must reuse these semantics under a distinct
  concern rather than introduce another effort type.

### AD-06 [TARGET] [ASSUMPTION] — Roll-up persists CAS-merged descendant slots

- **Binds:** FR-11, EventStore R4, persisted DTO, key, merge, and materialization.
- **Prevents:** whole-document lost updates, additive redelivery, cross-stream sequence comparison, and
  stale contributions after topology removal.
- **Rule:** the logical document
  works:v3:tenant:<base64url-tenant>:rollup:<base64url-ancestor> resolves through the tenant manifest
  to physical key works:v3:tenant:<base64url-tenant>:generation:<epoch>:rollup:<base64url-ancestor>.
  The manifest is the sole reader selector. The document contains:
  (a) topology watermarks per descendant, LWW by registry envelope sequence and carrying
  ReservationId plus Attached/tombstoned state; and (b) contribution slots per descendant, LWW only
  by that descendant's EventStore envelope sequence. EventStore is the sole durable writer and performs
  ETag read/merge/retry; Works supplies the pure merge that atomically preserves all slots and
  materializes ordinal Unit totals plus unestimated-descendant count. Equal sequence and equal canonical
  bytes is no-op; equal sequence and unequal bytes quarantines and degrades. A newer topology tombstone
  removes the contribution and rejects older attachment writes. The document's self slot is eligible by
  exact item identity without an attachment watermark; every non-self slot requires the matching Attached
  topology watermark.

### AD-07 [ADOPTED] [TARGET] — Own state and rolled state remain type-separated

- **Binds:** lifecycle authority, completion, correction, and consumer read shapes.
- **Prevents:** an eventual projection controlling aggregate state or ambiguous completion recovery.
- **Rule:** aggregate state owns Status and WorkItemEffort; projections own rolled totals with
  distinct types and serialized fields. Explicit Complete from InProgress or Suspended contributes
  zero without changing Estimated/Done. Progress reaching zero emits ProgressReported then
  WorkItemCompleted(CompletionKind=Progress); explicit completion writes
  CompletionKind=Explicit. CorrectProgress writes an absolute audited ProgressCorrected;
  when it restores positive Remaining after Progress completion it also emits WorkItemReopened and
  returns to InProgress. Explicit completion remains terminal. Historical completion kind is inferred
  deterministically from the immediately preceding progress-to-zero transition.

### AD-08 [ADOPTED] [CURRENT] — Claim concurrency is EventStore-owned

- **Binds:** FR-18 same-item command serialization and observable loser behavior.
- **Prevents:** caller versions, multi-item queue aggregates, and lost claim updates.
- **Rule:** Works commands expose no ETag or expected version. The Dapr actor turn lock serializes normal
  same-item handling. An injected state-store ETag conflict makes EventStore rehydrate and re-handle
  within its bounded retry; a losing claim becomes the existing domain rejection. Retry exhaustion is
  an infrastructure ConcurrencyConflict with no loser append or publication.

### AD-09 [ADOPTED] [CURRENT] — Delivery is at-least-once and order-independent

- **Binds:** every subscription, projection, Reactor, reminder, and recovery handler.
- **Prevents:** broker-order assumptions and acknowledgement before durable effect.
- **Rule:** delivery may duplicate or reorder. A handler acknowledges only after its durable state,
  checkpoint, side effect, or quarantine capture commits. Idempotency comes from per-stream positions,
  AD-26 effect IDs, and state-machine semantics, never from broker order.

### AD-10 [ADOPTED] [TARGET] — Reactor is the sole cross-aggregate process manager

- **Binds:** FR-26 child creation, attachment, cascade, child resume, and recovery translations.
- **Prevents:** shadow kernels and multiple cross-aggregate coordinators.
- **Rule:** Reactor maps committed event plus explicit state to commands mechanically and owns no
  business branch that a target aggregate should decide. Effects are eventual, checkpointed, and
  re-issued with AD-26 identities after crashes. Domain aggregates remain available while effects
  converge unless an explicit fence or repair state says otherwise.

### AD-11 [TARGET] [ASSUMPTION] — Time enters through durable typed reminder intents

- **Binds:** DateReached resume and expiry scheduling across Works, EventStore, and Platform.
- **Prevents:** clocks in handlers, incompatible actor families, and discovery indexes treated as truth.
- **Rule:** Handle never reads a clock. App works, actor type WorkItemReminderActor, actor ID
  wra-<digest> stores PendingWorkIntent with Kind DateResume or Expiry, canonical tenant/item, due
  UTC instant, ScheduleToken, source sequence, and typed payload. The digest is uppercase Crockford
  Base32 SHA-256 over the length-prefixed canonical tuple (reminder-actor, tenant, item); a stored full
  tuple mismatch is collision plus quarantine. EventStore owns
  generic registration and reconciliation; Works owns intent translation; Platform owns Scheduler
  persistence, HA, backup, and callback policy. Streams are authoritative; tenant and pending indexes
  are discovery aids governed by AD-15, AD-16, and AD-27.

### AD-12 [ADOPTED] [TARGET] [ASSUMPTION] — Deadlines are advisory until a witnessed timer command arrives

- **Binds:** overdue and expiry semantics.
- **Prevents:** wall-clock-dependent replay and local deadline interpretation.
- **Rule:** an item is not domain-expired merely because wall time passed. Only an AD-25 command carrying
  the current persisted schedule witness may cause expiry. Revalidate this rule before cost-aware
  scheduling introduces a logical clock.

### AD-13 [ADOPTED] [CURRENT] — Resume is exact-match, first-success, and narrowly idempotent

- **Binds:** FR-14 and FR-15 across domain, reminder, child-completion, and external-signal paths.
- **Prevents:** different adapters acknowledging the same nonmatching trigger differently.
- **Rule:** while Suspended, only an exact member of the current AwaitCondition set resumes and clears
  the set; a nonmatch is a rejection without mutation. Only replay of the exact condition consumed by
  the successful resume is a no-op. Every other post-resume or terminal condition rejects.

### AD-14 [ADOPTED] [TARGET] — One Executor Binding; AuthorityLevel remains carried

- **Binds:** FR-17 through FR-19 and active handoff.
- **Prevents:** binding lists, executor-kind branches, and AuthorityLevel becoming an accidental role
  system.
- **Rule:** an item has one ExecutorBinding(PartyId, Channel, AuthorityLevel). Assign and Claim set
  it under the lifecycle matrix. HandoffWorkItem is the only active-work binding change: in
  InProgress or Suspended it emits WorkItemHandedOff, changes only the binding, and preserves Status
  and AwaitConditions. AuthorityLevel values are stored but do not authorize v1 behavior; provenance
  and responsibility authorization are AD-23.

### AD-15 [ADOPTED] [TARGET] [ASSUMPTION] — Tenant isolation includes disjoint data and control namespaces

- **Binds:** identities, aggregate references, state/read-model keys, indexes, checkpoints, queries, and
  logs.
- **Prevents:** cross-tenant traversal, reserved-name collisions, non-atomic checkpoint regression, and
  tenant indexes with no offboarding path.
- **Rule:** validate canonical TenantId before key creation. Tenant state begins
  works:v3:tenant:<base64url(UTF-8 canonical TenantId)>; control state begins
  works:v3:control, which no TenantId can form. Parent/child references are tenant-closed and query
  authorization is independent of key prefixing. The control tenant index has one actor/CAS writer;
  indexes and checkpoints use ETag-protected monotonic merges, bounded cursor paging, audited
  tombstone/prune, and crash reconciliation. Legacy raw singleton keys and read/check/save checkpoint
  paths must migrate before their consumers switch. Canonical means the input is already a 1..64
  character lowercase ASCII slug matching ^[a-z0-9]([a-z0-9-]*[a-z0-9])?$; trust boundaries reject
  rather than transform other input. Claims, envelopes, references, keys, hashes, and equality reuse
  those exact UTF-8 bytes.

### AD-16 [TARGET] [ASSUMPTION] — Shared rebuild uses one tenant/family epoch

- **Binds:** capture-through-Commit consistency for roll-up, what's-next, topology, pending intents, and
  quarantine/parking.
- **Prevents:** acknowledged live writes omitted by promotion and rebuilt queries that cannot recover
  pending work.
- **Rule:** a CAS-created works-runtime-v3 epoch seals inventory and source high-watermarks for one
  tenant. EventStore is the sole epoch and journal writer; every family physical key includes the
  epoch. BeginBuilding fails until every active writer holds the required rebuild-protocol lease, and
  every write validates the current epoch and lease in its commit path; stale writers fail without
  acknowledgement. During Building, refreshed writers atomically append the live update and consumer
  checkpoint to the epoch's durable catch-up journal; they mutate neither active nor staging documents.
  Journal identity is (tenant, epoch, family, source stream, envelope position) with canonical-digest
  conflict detection. Stage contains the complete family; Commit atomically promotes the manifest as
  the sole reader selector, then drains catch-up only into the promoted generation. Readers report
  stale/unavailable from the first journaled write until every post-capture position is applied and the
  fence opens. Abort drains the journal into active before it deletes staging or clears stale state.
  Readers never see a partial generation. Process-effect checkpoints restore separately from backup
  and reconcile from source events plus AD-26 IDs before readiness.

### AD-17 [ADOPTED] [TARGET] — Lifecycle and numeric validation have one authority

- **Binds:** FR-6 through FR-10 validation and transition outcomes.
- **Prevents:** per-handler transition tables and projections inventing state.
- **Rule:** docs/lifecycle-transition-matrix.md is authoritative and must change atomically with
  lifecycle code/tests. Progress delta is positive; CorrectProgress absolute Done is within
  0..Estimated; Estimated is nonnegative; Unit follows AD-04; ReEstimate clamps Done and never
  completes. Terminal own contribution is zero; an unestimated item contributes zero plus count.
  Due/TTL policy is edge configuration, never kernel configuration. Handoff, correction, reopen,
  completion-kind, and Unit-inheritance changes ship atomically with this matrix, the durable catalog
  and validators, aggregate behavior, projection folds, golden corpus, and conflicting fitness, unit,
  and integration tests.

### AD-18 [ADOPTED] [TARGET] — Dependencies and sibling boundaries point inward

- **Binds:** project references, domain ports, and owns-versus-references boundaries.
- **Prevents:** Projections/Reactors depending on Server and domain modules duplicating reusable runtime.
- **Rule:** Server -> Contracts, Projections -> Contracts, Reactor -> Contracts, and
  Testing -> pure Works units. The executable may reference EventStore SDK and inward Works units.
  The current architecture suite enforces its four production-project allowlist; the expanded diagram
  below is the target and each added edge needs an explicit fitness rule. Contracts owns
  IExpectationResolver and IExecutorRouter; v1 ships the no-LLM expectation
  resolver and leaves the router unwired. Generic behavior is contributed to EventStore before Works
  consumes it. Works stores sibling IDs only and resolves owned data on demand.

~~~mermaid
flowchart RL
    Server --> Contracts
    Projections --> Contracts
    Reactor --> Contracts
    Testing --> Contracts
    Testing --> Server
    Testing --> Projections
    Testing --> Reactor
    Executable --> Server
    Executable --> Projections
    Executable --> Reactor
    Executable --> EventStoreSDK
    Platform --> Executable
    Platform --> EventStoreSDK
~~~

| Sibling | Works owns | Works references |
| --- | --- | --- |
| Hexalith.Parties | ExecutorBinding semantics | PartyId; no Party profile copy |
| Hexalith.Conversations | optional link semantics | ConversationCorrelationId; no content copy |
| Hexalith.Tenants | tenant-closed domain rules | membership and tenant policy truth |
| Hexalith.Commons | Works value-object validation | ULID generation/encoding helpers |
| Hexalith.EventStore | aggregate and projection behavior | envelope, persistence, delivery, and generic runtime seams |

### AD-19 [ADOPTED] [CURRENT] — Version authority follows current ownership

- **Binds:** SDK, package, runtime image, and target-host version changes.
- **Prevents:** updating the wrong file or describing target ownership as current reality.
- **Rule:** before R1, Works global.json owns .NET/Aspire SDK pins,
  src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj owns the matching explicit AppHost SDK pin,
  and BuildConfigurationTests enforces their equality;
  references/Hexalith.Builds/Props/Directory.Packages.props owns package pins, and
  DaprSelfHostedMtls.cs owns Dapr runtime images. After R1, Platform owns host SDK, runtime images,
  components, and topology pins. Versions change through dependency work with restore, Release build,
  focused integration, and Platform parity evidence; prose never overrides those sources.

### AD-20 [ADOPTED] [TARGET] — Platform host migration is parity-gated

- **Binds:** repository ownership, current-to-target transition, preview policy, and R1-R11 removal.
- **Prevents:** Works and Platform each assuming the other owns live behavior.
- **Rule:** Hexalith.Platform, accountable to the Platform Maintainer, is the target host. Works
  AppHost and ServiceDefaults remain until every applicable matrix row is green from Platform and
  rollback is proved. Core Aspire 13.5.3 is stable; the selected Dapr hosting integration is preview
  and is development/test-only unless the Platform Maintainer records a time-bounded production
  exception with owner and stable-upgrade trigger. An absent producer seam cannot be marked green.

| Row | Seam | Target owner | Availability on 2026-09-12 | Proof before Works removal |
| --- | --- | --- | --- | --- |
| R1 | Aspire/Dapr topology, components, ACLs | Platform | Target absent | verify-works-host and parity scenarios |
| R2 | Service defaults, health, telemetry | EventStore SDK + Platform | Core available | no forked defaults; readiness proof |
| R3 | event subscription/delivery | EventStore SDK; Works handlers | Partial | origin, retry, quarantine tests |
| R4 | projection, fan-out, rebuild fence | EventStore SDK; Works merge | Fan-out/fence absent | API contract, convergence, fenced rebuild |
| R5 | Works queries | Works behind SDK | Partial; tenant query exists, Executor filter absent | authorization and result-filter tests |
| R6 | typed durable reminders/reconciliation | EventStore SDK; Works intents | Generic seam absent | HA/backup/stale-fire/recovery proof |
| R7 | checkpointed process runner | EventStore SDK; Works translations | Generic seam absent | crash, page-boundary, reissue proof |
| R8 | recovery health and operations | Platform + SDK | Target absent | degraded-readiness and alert proof |
| R9 | minimal Works domain-service executable | Works | Partial/transitional | remove custom host plumbing; canonical two-line SDK composition |
| R10 | mTLS, policy, production profile | Platform | Transitional Works only | AD-24 negative and positive tests |
| R11 | gateway command submission | EventStore SDK; Works identity policy | Gateway + trusted-intent API available; Works adapter/effect receipt/retention binding absent | AD-26 conflict/replay tests |

Each absent seam requires a named producer artifact, minimum published package/API contract test,
consumer story, and proof command before the row can turn green.

### AD-21 [TARGET] [ASSUMPTION] — The Work-Tree Registry is the sole topology authority

- **Binds:** FR-13, FR-16, FR-26, wire identity, lifecycle, fencing, and topology reads.
- **Prevents:** two parents, cycles from stale caller facts, ghost edges, late commands after release,
  and multiple topology truths.
- **Rule:** the registry is EventStore aggregate
  (tenant, domain=work-tree, aggregateId=registry), publishes work-tree.events, and is consumed
  at /work-tree/events. Its lifecycle is Reserved -> Creating -> Attached or Reserved -> Released;
  only Reserved may release before create authorization. EdgeReserved carries the complete child-create payload;
  ChildCreateAuthorized mints stable ReservationId plus monotonic FencingToken. A Creating
  reservation cannot timeout-release. Reactor submits token-bearing CreateWorkItem; ordinary
  parent-bearing CreateWorkItem is prohibited. WorkItemCreated durably carries and exactly matches
  tenant, parent, child, ReservationId, FencingToken, and the reserved-payload digest produced by the
  AD-26 codec; it is the sole attachment evidence. EdgeAttached then drives the parent's token-bearing
  SpawnChild bookkeeping act, which idempotently emits ChildSpawned in every parent status. When the
  reserved suspend flag is true, it also suspends an InProgress parent on ChildCompleted(child), or
  unions that condition into an already Suspended parent's current AwaitConditions. Terminal parents
  remain terminal; for Cancelled or Expired parents, attachment processing durably schedules matching
  child-cascade catch-up before acknowledgement. The Attached edge remains authoritative even if that
  parent act arrives after the terminal transition. Released tokens are terminal. Creating never releases:
  an operator may quarantine it while preserving the token, then repair attaches verified child
  evidence or marks an orphan only after proving a permanent conflict. EventStore validates the
  reservation token immediately before CreateWorkItem dispatch.

Before EdgeReserved, EventStore obtains a ParentAdmissionWitness inside the serialized parent turn and
validates it immediately before registry dispatch. It binds tenant, parent, child, AD-26 reserved-payload
digest, suspend flag, and parent envelope sequence. suspend=false requires a nonterminal parent;
suspend=true requires InProgress or Suspended, so Created, Assigned, and Queued reject before reserve.
If the parent changes after admission, the attachment rules above cover the reachable
InProgress/Suspended/terminal states; retry of the identical witness is idempotent.

All fan-out, cascade, child-completion recovery, rebuild, and repair code uses one Works-owned
IWorkTreeTopologyReader over the registry read model. It exposes exact-token
ResolveAttachedAncestry, stable cursor-paged EnumerateAttachedDescendants, and attachment lookup.
Reserved or missing attachment is retryable and never acknowledged. Works owns contracts, aggregate,
and pure Reactor translations; EventStore owns delivery, CAS projection, relationship adapter,
process/schedule primitives; Platform resolves trusted MaxDepth and timeout policy for the target
tenant before admission. The v1 MaxDepth default is 32 and breadth is uncapped. Consumers switch only
  after a per-tenant migration proves registry equivalence. Production admission also requires a
  Platform-supplied tenant topology quota; missing or exhausted quota fails before EdgeReserved.
  Breadth has no domain cap beneath that operational quota. EventStore snapshots the registry, bounds
  per-command replay, exposes saturation/backpressure, and proves latency at the configured quota.

### AD-22 [TARGET] [ASSUMPTION] — Live roll-up fans out absolute folded contributions

- **Binds:** FR-11 and SM-2 steady-state recursive roll-up.
- **Prevents:** interpreting delta events as totals, acknowledging empty ancestry before attachment,
  and EventStore learning Works domain payloads.
- **Rule:** after each accepted state-changing event, Works folds descendant state and emits
  WorkContributionSnapshot(tenant, item, sourceEnvelopeSequence, status,
  ownRemainingByUnit, unestimatedCount). A nonterminal unestimated item contributes numeric zero and
  count one; a terminal item contributes zero and count zero. EventStore treats the snapshot as opaque
  and CAS-merges it into the item's self slot plus every ancestor resolved through the exact AD-21
  Attached chain. A Reserved, missing, stale, or mismatched attachment is not acknowledged; an Attached
  transition triggers deterministic backfill. Shared rebuild is repair/migration, never routine fan-out.
  Partial results are never served as complete; repair/rebuild exposes an explicit unavailable/stale
  state. After a durable roll-up or what's-next change commits, the EventStore notifier seam publishes
  only the changed key, never the model payload. WorkContributionSnapshot is a non-domain projection
  message derived only from a committed EventStore envelope; it is never appended to Work Item or
  Registry streams and never advances payload Sequence. Works.Projections owns the fold and snapshot,
  EventStore owns the generic merge/relationship SPI without referencing Works, and only the Works
  executable composes their adapter. At quiescence, after all tree events are acknowledged, the
  Platform lane proves convergence within the PRD's provisional five-second bound.

### AD-23 [TARGET] [ASSUMPTION] — Identity and tenant delegation are end-to-end authority

- **Binds:** external ingress, internal effects, acting Party, and responsibility authorization.
- **Prevents:** tenant assertion as authority, global-admin workload bypass, and a compromised service
  invoking unrelated commands.
- **Rule:** Platform authenticates external OIDC identity and derives tenant/actor through
  Hexalith.Tenants. The Hexalith.Platform Workload Delegation Service is the sole issuer of a
  short-lived asymmetric-signed WorkloadDelegation JWT. It binds the eventstore-gateway audience,
  full AD-26 tuple and EffectId, target, command type, canonical command digest, tenant, purpose,
  causation, issued time, and expiry. EventStore validates signature/audience/expiry, recomputes every
  binding, and compares the target app to the mTLS/ACL-attested dapr-app-id before enforcing
  CommandOriginPolicy[(domain, commandType)]; allow-listed workloads receive no global-admin bypass.

ReportProgress, CorrectProgress, Complete, Suspend, Handoff, and Reject additionally require the
authenticated acting Party to equal the current Executor. Claim from Assigned requires the same match;
Claim from Queued binds the authenticated acting Party. Target Claim carries no caller-provided
ExecutorBinding: EventStore derives PartyId from AuthorizedCommandContext and Channel/AuthorityLevel
from trusted tenant policy. Assign, ReEstimate, Reschedule, and Cancel stay
at the authenticated tenant-member floor, as do root Create, Queue, and LinkConversation. Every
responsibility-bound authorization executes inside the serialized Work Item actor turn after
rehydration, using an immutable EventStore-issued AuthorizedCommandContext; caller payloads, read
models, and edge-only checks never authorize mutation. Denial occurs before Handle or append as an
audited authorization denial, not a domain rejection. Registry-authorized child Create/SpawnChild,
timer and child-completion Resume, external-signal Resume, Expire, cascade, repair, replay, and rebuild
each require a distinct named workload/operator purpose. Every unlisted (origin, command) pair is
denied. Missing identity or payload/envelope mismatch denies before state, query, or tenant-existence
disclosure.

### AD-24 [ADOPTED] [TARGET] — Production routes and origins fail closed

- **Binds:** production ingress, internal routes, broker origin, operator paths, and deployability.
- **Prevents:** direct workload calls, spoofed app IDs, forged high sequence numbers, and permissive
  development controls escaping into production.
- **Rule:** production uses Dapr mTLS with declared trust domain/namespace, deny-by-default app access
  control and network policy, TLS broker connections, and producer/consumer ACLs. Only EventStore
  publishes Works/registry events; only named identities invoke reminder, Reactor, replay, rebuild,
  repair, and quarantine paths. A machine-readable production profile is required; startup/admission
  refuses missing trust, ACL, delegation, secret-store, Scheduler, state-store, or audit configuration.
  Direct-port, wrong-app, wrong-trust-domain, unauthorized publish, forged sequence, wrong tenant or
  purpose, and ordinary-user SpawnChild tests must fail; valid production-policy reminders must pass.

### AD-25 [TARGET] [ASSUMPTION] — Expiry carries a persisted schedule witness

- **Binds:** FR-10 expiry, Due Date/TTL policy, reschedule safety, and recovery.
- **Prevents:** stale reminders expiring rescheduled work and different hosts choosing different policy.
- **Rule:** Platform resolves tenant-specific Due-Date/TTL policy and supplies an effective expiry
  instant before domain admission; create and reschedule facts persist that instant and a deterministic
  ScheduleToken additively. The token is wrs-<digest>, where digest is uppercase Crockford Base32
  SHA-256 over the length-prefixed canonical tuple (schedule, tenant, item, effective-expiry UTC ticks,
  WorkItem state-change schedule revision). Reminder names are date-<token> or expiry-<token>; a stored
  full tuple mismatch is collision plus quarantine. One AD-11 Expiry intent mirrors them.
  ExpireWorkItem echoes both; the aggregate rejects a mismatch and applies the
  lifecycle matrix for a match. Registration, cancellation, and reschedule derive mechanically from
  committed lifecycle events. Policy never enters Handle; changing policy affects future commands,
  not historical replay.

### AD-26 [TARGET] [ASSUMPTION] — Cross-aggregate effects have deterministic durable identity

- **Binds:** R11 and every Reactor, reminder, cascade, child-resume, and recovery submission.
- **Prevents:** duplicate logical effects after redelivery, ownership migration, status expiry, backup
  restore, or command-ID truncation.
- **Rule:** EffectId is uppercase Crockford Base32 SHA-256 over a fixed-order binary tuple:
  each text field is canonical UTF-8 preceded by a four-byte big-endian length; numeric fields are
  eight-byte big-endian. Tuple order is tenant, source domain, source aggregate, source envelope
  sequence, effect kind, target domain, target aggregate, effect ordinal. One versioned EventStore-owned
  codec defines canonical text, semantic command fields, effect-kind identifiers, tuple encoding, and
  every AD-11/25/26 digest. Text uses the length rule above; integers are signed eight-byte big-endian;
  SHA-256 renders most-significant-bit first through alphabet 0123456789ABCDEFGHJKMNPQRSTVWXYZ with
  final zero-bit padding as exactly 52 uppercase characters and no separator padding. Effect ordinal is
  an immutable catalog value per (source event, effect kind, target role), never traversal order.
  MessageId and IdempotencyKey both reuse wrk-<EffectId>, satisfying gateway-safe identifiers.

  EventStore exclusively owns a private target-scoped effect inbox in the target actor partition.
  Receipt lookup and a receipt containing EffectId, full tuple, semantic command digest, success,
  rejection, or no-op disposition, workload, delegation purpose, and causation commit atomically with
  target events and metadata; Works domain streams contain no receipt event. The same key and digest
  returns its recorded disposition without redispatch; the same key with different tuple or semantics
  is conflict plus quarantine. Receipts are not
  Work Item lifecycle-pruned and share the tenant's legal-hold/offboarding retention class; receipt and
  corresponding source/target evidence erase only as one authorized tenant operation. Replays older
  than the retained source floor reject or quarantine rather than execute. Gateway terminal records use
  EventStore Commit retention and are an optimization, not the correctness boundary. Golden vectors
  cover every effect family before its producer is registered.

### AD-27 [TARGET] [ASSUMPTION] — Recovery is lossless, periodic, and readiness-affecting

- **Binds:** R7/R8 stream paging, checkpointing, retry, parking, and operations.
- **Prevents:** page-boundary event loss, startup-only recovery, silent parking, and Ready while work is
  stranded.
- **Rule:** all recovery readers share one strict page validator. EventStore FromSequence is an
  exclusive lower bound, so the next page reuses LastSequenceReturned without adding one. Every
  page validates domain, canonical tenant, aggregate, strictly increasing envelope positions, and
  complete decoding of state-affecting evidence. Bounded hot retry transitions to a durable
  tenant-scoped unresolved/quarantine record. The same validator and quarantine path govern live,
  replay, repair, and rebuild. works-recovery readiness remains degraded, bounded
  kind/reason metrics and alerts remain active, and periodic retry continues until authenticated,
  audited disposition. A delivery acknowledges only after side effect/checkpoint or quarantine capture.

### AD-28 [TARGET] [ASSUMPTION] — Durable data has explicit privacy, audit, and recovery owners

- **Binds:** durable fields, privileged audit, secrets, retention/offboarding, backup, and DR.
- **Prevents:** irreversible cross-repository storage, erasure, or restore choices after real data lands.
- **Rule:** Obligation, notes, await/correlation values, and durable bodies are confidential tenant
  data; identifiers and causation are restricted operational metadata. Works owns classification and
  minimization. EventStore owns encryption in transit/at rest, tenant-keyed retention/erasure
  mechanisms, and immutable stream storage. Platform owns secret-store rotation, backup/restore, DR,
  and an append-only privileged-audit sink. Repair, replay, rebuild, quarantine, offboarding, and
  authorization denial are audited; audit-sink failure blocks privileged mutation. Payloads never enter
  logs, metrics, traces, or ProblemDetails.

Production objectives are RPO at most 15 minutes, RTO at most four hours, and quarterly restore drills.
No non-synthetic shared data or new durable catalog type is admitted until the accountable data owner
approves retention/legal hold/offboarding and a restore drill proves stream, registry, projection,
pending-intent, checkpoint, audit, and tenant-key order.

### AD-29 [TARGET] [ASSUMPTION] — Durable schema and quarantine evolve fail-closed

- **Binds:** AD-01, AD-21, AD-25, mixed-version rollout, and malformed/unknown evidence.
- **Prevents:** old writers bypassing fences, renamed durable types, and incompatible subscriber
  dispositions.
- **Rule:** rollout order is reader/validator/catalog/golden-corpus first and producer second; rollback
  retains readers while new bytes exist. Known types accept additive fields; required
  security/fencing fields fail closed when absent in the operation that needs them; unknown enum
  values never coerce. Malformed or unknown state-affecting evidence is durably captured in one
  tenant-scoped quarantine and degrades the affected capability. Replay/disposition is authenticated,
  audited, and re-enters the same validation path. Child streams without reservation evidence require
  the AD-21 one-time migration and never become newly Attached by inference. No V2 command/event
  type or renamed discriminator replaces an existing durable type.

## Consistency Conventions

| Concern | Convention |
| --- | --- |
| IDs | Existing non-whitespace IDs remain readable; the target edge emits ULIDs; infrastructure keys encode canonical IDs instead of raw concatenation |
| Time | DateTimeOffset/UTC at edges; domain behavior consumes persisted instants, never wall clocks |
| Envelopes | EventStore envelope sequence is canonical; Works payload Sequence is state-change ordinal only |
| Serialization | Options-free PascalCase concrete writer; shared case-insensitive tolerant reader; no polymorphic marker at rest |
| Errors | Domain rejection event for expected invalid acts; infrastructure exception/dead letter; RFC 9457 at HTTP edges |
| Logging | Source-generated structured logs; bounded metadata only; never payloads, secrets, or personal data |
| Configuration | Typed options at host/runtime edges; aggregates do not read configuration |
| Tests | xUnit v3, Shouldly, NSubstitute; property tests for convergence; persisted end-state assertions at integration boundaries |

## Stack

This is seed observed on 2026-09-12; AD-19 names the live authorities.

| Name | Version |
| --- | --- |
| .NET SDK | 10.0.400 floor with latestPatch; 10.0.401 resolves locally |
| .NET runtime | 10.0.12 current servicing reference |
| C# | 14 |
| Aspire core | 13.5.3 |
| CommunityToolkit Aspire Dapr hosting | 13.5.0-preview.1.260825-0345 |
| Dapr runtime | 1.18.3 in transitional Works host; 1.18.4 current release |
| Dapr .NET SDK packages | 1.18.5 at the root-tracked Builds gitlink; 1.18.7 in the locally advanced Builds checkout |
| Hexalith.EventStore packages | 3.103.0 |
| Hexalith.PolymorphicSerializations | 1.19.2 |
| Hexalith.Commons | 2.30.0 |
| xUnit v3 | 4.0.0 |

Source provenance is explicit: the root tracks EventStore at 6b0247ac and Builds at a32cb422; the
local checkouts are advanced to a568af4e and fa647278 respectively. Reviewed EventStore APIs are
unchanged across those two EventStore commits. The Builds advance changes Dapr SDK packages from
1.18.5 to 1.18.7, so the table reports both rather than treating local submodule drift as root state.

## Structural Seed

~~~text
src/
  Hexalith.Works.Contracts/     # durable contracts, values, boundary ports
  Hexalith.Works.Server/        # pure Work Item and Work-Tree Registry behavior
  Hexalith.Works.Projections/   # pure folds, contribution snapshots, merge strategies
  Hexalith.Works.Reactor/       # mechanical cross-aggregate translations
  Hexalith.Works/               # minimal EventStore domain-service executable
tests/
  Hexalith.Works.UnitTests/
  Hexalith.Works.PropertyTests/
  Hexalith.Works.IntegrationTests/
  Hexalith.Works.ArchitectureTests/
~~~

Every listed project exists now. The AppHost and ServiceDefaults, bespoke /project route, Dapr
actor/reminder wiring, and local recovery are transitional migration sources, not target topology.
Registry behavior and the generic EventStore R4/R6/R7 seams are target additions within these
boundaries, not separate repositories.

## Capability → Architecture Map

| Capability / area | Lives in | Governed by |
| --- | --- | --- |
| FR-1..FR-5 Work Item state | Contracts + Server | AD-01, AD-02, AD-04, AD-05, AD-07 |
| FR-6..FR-10 lifecycle/terminal behavior | Server + reminder/Reactor edges | AD-07, AD-12, AD-17, AD-23, AD-25 |
| FR-11..FR-13 tree and roll-up | Registry + Projections + EventStore R4 | AD-06, AD-15, AD-16, AD-21, AD-22 |
| FR-14..FR-16 durable await/child | Server + Reactor + reminder runtime | AD-10, AD-11, AD-13, AD-21, AD-25, AD-26, AD-27 |
| FR-17..FR-20 executor/queue/query | Server + Projections | AD-03, AD-08, AD-14, AD-23 |
| FR-21..FR-23 module boundaries | Contracts + fitness tests | AD-01, AD-18, AD-29 |
| FR-24..FR-26 host and Reactor | Works executable + EventStore + Platform | AD-10, AD-19, AD-20, AD-23, AD-24, AD-26, AD-27 |
| Security and tenant isolation | Platform + EventStore + all Works paths | AD-15, AD-23, AD-24, AD-28, AD-29 |
| Rebuild, recovery, operations | EventStore SDK + Platform | AD-16, AD-20, AD-27, AD-28 |

## Readiness Gates

The target architecture is finalizable while implementation readiness remains conditional.

| Gate | Required evidence |
| --- | --- |
| Recovery correctness | Fix both double-advanced stream cursors; use the AD-27 validator on every path; prove multi-page, restart, retry, quarantine, disposition, alert, and degraded-readiness behavior |
| Ordered delivery work | AD-26 transport identity before registry; then registry, fan-out, typed expiry, Reactor recovery |
| Platform migration | Every AD-20 R1-R11 row has producer/API version, consumer story, and Platform proof |
| Runtime compatibility | Upgrade or record accountable time-bounded risk acceptance for the exact .NET/Aspire/CommunityToolkit/Dapr tuple, then prove restore, Release build, focused integration, and Platform parity |
| Security | AD-23/24 negative matrix plus positive production reminder path |
| Production data | AD-28 owner approval, retention/offboarding policy, secret rotation, restore drill |
| Lifecycle migration | Transition matrix, catalogs/validators, durable contracts, aggregate/projection logic, golden corpus, and conflicting tests change atomically |
| CI architecture fitness | Works Maintainer adds a tracked step that runs dotnet tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests.dll after the Release build; compilation alone is not proof |

docs/eventstore-api-surface-constraints.md must be refreshed against the root-tracked EventStore
gitlink before it can close the former VAL-H11 schema matrix. New registry, reminder, handoff,
correction, and fencing types extend that evidence under AD-29.

## Deferred

| Item | Why it can wait | Revisit condition |
| --- | --- | --- |
| Stable Aspire/Dapr hosting adapter | Transitional Works host remains; preview is not production-approved | Before R1 production acceptance |
| EventStore generic R4/R6/R7 APIs | Target ownership is fixed; APIs do not yet exist | Producer artifact and minimum package before each consumer story |
| Final legal retention/hold durations | AD-28 prevents real-data admission meanwhile | Accountable data-owner approval |
| Cost-aware logical time | No v1 cost scheduling | Before Theme 5 |
| AuthorityLevel enforcement/routing roles | Provenance and executor responsibility are already enforced independently | Before first multi-team production tenant or Theme 4 |
| UI stack, including Fluent UI V5 | Works v1 is headless | First UI capability |
| Provider-specific production infrastructure | Platform owns provider selection; security/DR contracts already bind it | Platform deployment design |

## Legacy Decision Map

Existing downstream citations remain valid.

| Legacy | Stable ID | Legacy | Stable ID |
| --- | --- | --- | --- |
| A1 | AD-02 | C1 | AD-10 |
| A2 | AD-03 | C2 | AD-11 |
| A3 | AD-04 | C3 | AD-12 |
| A4 | AD-05 | C4 | AD-13 |
| A5 | AD-06 | D1 | AD-14 |
| B1 | AD-08 | D2 | AD-15 |
| B2 | AD-09 | E1 | AD-16 |
| B3 | AD-07 | E2 | AD-17 |
| D-1 | AD-10 | D-3 | AD-12 |
| D-2 | AD-08 | D-4 | AD-09 |
