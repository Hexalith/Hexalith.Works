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
updated: 2026-11-21
binds:
  - FR-1..FR-26
  - Works security, recovery, data-integrity, and platform-migration NFRs
sources:
  - _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md
  - _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/.memlog.md
  - _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/addendum.md
  - _bmad-output/planning-artifacts/architecture-validation-2026-09-12/ARCHITECTURE-VALIDATION.md
  - _bmad-output/planning-artifacts/architecture-validation-2026-11-21/ARCHITECTURE-VALIDATION.md
  - docs/platform-hosting-cutover.md
  - docs/sdk-projection-query-adoption.md
  - docs/sdk-reminder-process-adoption.md
  - docs/lifecycle-transition-matrix.md
  - tools/source-debug-dependencies.json
  - docs/eventstore-api-surface-constraints.md
  - references/Hexalith.AI.Tools/hexalith-llm-instructions.md
companions:
  - architecture/architecture-works-2026-09-08/.memlog.md
---

# Architecture Spine — Hexalith.Works

The November 20 PRD and addendum define the product behavior. This spine binds the Works, EventStore and
Platform implementations to the same decisions. The architecture memlog records their history. Current
implementation claims use local Works `a4a91332`. AD-19 identifies the catalog used by each parity check.
The registry admission and child Resume protocols in AD-21 are approved architecture targets as of
21 November 2026. Works and EventStore own implementation and joint qualification.

Decision tags separate commitment from delivery: **ADOPTED** means the decision is settled,
**CURRENT** means repository evidence verifies it today, **TARGET** means it is a binding migration
outcome, and **ASSUMPTION** means the Fast-path update chose a fail-closed default that remains
explicit until user direction or implementation evidence replaces it. A decision may be adopted
without being implemented. Where a protocol is marked **proposed**, its required outcome is settled
and the named owners still need to agree on the mechanism.

## Design Paradigm

Hexalith.Works uses **event-sourced domain-driven design with CQRS**. A Dapr actor gives each aggregate
one logical writer; pure aggregate handlers emit raw-act events; projections build disposable read
models; and the **Reactor** is a Saga Process Manager that translates committed events into
cross-aggregate commands without making domain decisions.

Works owns business contracts, pure behavior and SDK adapters. EventStore owns reusable runtime
mechanics. Platform owns topology, environment policy and operations.

The solid paths are implemented. The dashed branch is the proposed Work-Tree Registry flow under AD-21.

~~~mermaid
flowchart LR
    Edge[Authenticated gateway] --> Item[Work Item aggregate]
    Item --> WE[Committed work events]
    WE --> Process[SDK process runner and Works Reactor]
    Process --> Edge
    WE --> Projection[Works fold through SDK shared-family writer]
    Projection --> Reads[(Selected tenant generation)]
    Edge -. reserve .-> Registry[Target Work-Tree Registry]
    Registry -. request parent admission .-> Process
    WE -. durable parent admission via Reactor .-> Registry
    Registry -. attachment events .-> Process
~~~

## Inherited Constraints

There is no parent spine. These repository-wide constraints are binding inputs.

| Constraint | Source | Consequence |
| --- | --- | --- |
| Domain modules are domain-centric | Hexalith LLM baseline | Reusable hosting, Dapr, projection, reminder, and recovery plumbing lands in EventStore first |
| EventStore is the persistence substrate | Hexalith LLM baseline | Commands in, events out; state reconstructs by replay; no parallel repository |
| Dapr is the infrastructure abstraction | Hexalith LLM baseline | No direct broker/database clients in domain contracts or behavior |
| Root-declared submodules only | Repository guidance | Architecture and build evidence never depend on nested-submodule initialization |
| SDK and dependency selection | Works solution, build files and recorded Builds catalog | Debug uses reviewed root sources. CI validates Release packages. AD-19 governs their pairing. |

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
  query returns Assigned and Queued items. The Works query policy grants all-work scope only to an allow-listed
  non-delegated coordinator workload authorized for the tenant. A delegated caller must supply their
  own verified PartyId and receives their Assigned items plus the tenant Queued pool. A missing or
  mismatched PartyId is forbidden. A caller-supplied all-work flag cannot expand scope. Both views order by
  Priority, earliest Due Date (absent last), then ordinal WorkItemId. No routing score or creation
  coordinate participates. The current comparator maps a missing date to `DateOnly.MaxValue`, so
  an undated item can precede a dated one on 9999-12-31. Compare date presence before date value.
  Prove the dated item wins at equal priority with either ordinal ID assignment. [Queue comparator](../../src/Hexalith.Works.Projections/Strategies/WhatsNextOrdering.cs)

  Authorization runs before Works reads state. The gateway supplies authenticated actor and workload
  claims. Works resolves the requested Party through the Parties client with separate service authority.
  Missing, expired or mismatched authority denies the query. Related documents come from one selected
  generation.

  **Single-item detail and Roll-Up (approved target, 20 November 2026).** The current Works detail handler permits
  ordinary-member reads for Queued items and the member's own Assigned items. Extend individual
  lookup to that member's own InProgress and Suspended items. A handoff transfers this access to
  the incoming Executor. Other ordinary-member lookups return the same result as an absent item.
  The coordinator's tenant-scoped all-work entitlement still applies. The list filters above are
  unchanged. Establish the current Status and Executor independently of stale list membership. Withhold
  both detail and Roll-Up when that binding cannot be established. Test Claim, suspension, terminal
  status and a delayed projection after handoff that still names the former Executor.

### AD-04 [ADOPTED] [TARGET] — Child Unit is captured at spawn and remains immutable

- **Binds:** FR-3 and FR-12 across aggregate, child creation, projection, and query paths.
- **Prevents:** silent conversion and independently chosen child units.
- **Rule:** At spawn, persist the explicit child Unit if supplied, otherwise the parent's Unit at
  admission. Capture that choice even for an unestimated child. An estimate at spawn requires a Unit
  from one of those sources. If both are absent, the child may start unestimated and its first
  estimate must supply the Unit. A later change to the parent cannot alter the captured choice.
  The first estimate of a root establishes its Unit. Once fixed, a different Unit on an estimate,
  report or correction rejects. Mixed-Unit trees expose per-Unit subtotals. [FR-12 truth table](prds/prd-works-2026-06-14/prd.md#fr-12-roll-up-across-heterogeneous-units-safely)

### AD-05 [ADOPTED] [CURRENT] — WorkItemEffort is the burn-down contract

- **Binds:** effort vocabulary and shape.
- **Prevents:** a second meter abstraction diverging from the public contract.
- **Rule:** WorkItemEffort(Unit, Estimated, Done) is the sole v1 effort type; Remaining is derived and
  never stored as authoritative state. Future cost support must reuse these semantics under a distinct
  concern rather than introduce another effort type. The raw event retains the full positive reported
  delta. The fold adds up to the remaining estimate and preserves an existing overrun. ReEstimate
  changes only Estimated. An accepted InProgress report that leaves Remaining at zero emits
  ProgressReported followed by WorkItemCompleted in the same act, including a report on an existing
  overrun.

### AD-06 [CURRENT] [TARGET] [ASSUMPTION] — The SDK owns roll-up writes

- **Binds:** FR-11, EventStore R4, persisted DTO, key, merge, and materialization.
- **Prevents:** whole-document lost updates, additive redelivery, cross-stream sequence comparison, and
  stale contributions after topology removal.
- **Rule:** the current SDK writer applies only changed logical operations after Works refolds the
  bounded tenant histories. Capacity exhaustion leaves work pending and the affected reads stale or
  unavailable. AD-16 governs the checkpoint and retry. Registry-based incremental merge is the target
  below and depends on AD-21. Current readers and writers use the lookup in AD-15.

  Under that target, the logical document
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
  zero without changing Estimated/Done. An accepted report leaving zero Remaining emits ProgressReported then
  WorkItemCompleted(CompletionKind=Progress); explicit completion writes
  CompletionKind=Explicit. In the target contract, CorrectProgress records the submitted cumulative Done
  in ProgressCorrected. From InProgress, positive Remaining preserves status and zero Remaining appends
  WorkItemCompleted with progress provenance. From a progress-completed item, positive Remaining appends
  WorkItemReopened and returns it to InProgress. At zero the correction alone is recorded and it stays
  Completed. Explicitly completed and abnormally terminated items reject correction. For legacy
  completions without a kind, replay the item stream. Classify an immediately preceding accepted
  `ProgressReported` and `WorkItemCompleted` from the same act as progress completion when the folded
  Remaining is zero. An explicit Complete at zero remains explicit. Exercise both histories with
  Estimated 4 and Done 7. A later correction to Done 3 reopens only the progress-completed item.

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

### AD-10 [ADOPTED] [CURRENT] [TARGET] — Reactor is the sole cross-aggregate process manager

- **Binds:** FR-26 child creation, attachment, cascade, child resume, and recovery translations.
- **Prevents:** shadow kernels and multiple cross-aggregate coordinators.
- **Rule:** Reactor maps committed event plus explicit state to commands mechanically and owns no
  business branch that a target aggregate should decide. Effects are eventual, checkpointed, and
  re-issued with AD-26 identities after crashes. Domain aggregates remain available while effects
  converge unless an explicit fence or repair state says otherwise.

  Today the SDK runs the checkpointed cascade and child-completion processes and delivers the Works
  translations through the gateway. Date callbacks use the SDK reminder path. Registry attachment and
  reconciliation of a later reopen remain targets.

  **Approved FR-10 reopening rule (20 November 2026).**
  If a progress correction reopens
  an attached descendant after a Cancel or Expire cascade skipped it while terminal, keep the correction
  and reopen events. The Reactor rediscovers the terminal ancestor, durably schedules the matching
  termination for this reopening and retries it after restart. The active contribution is visible
  until termination commits, then the item contributes zero at quiescence. Derive the AD-26 effect for
  this reopening from the committed reopen occurrence and retain the ancestor termination as provenance.
  Retries reuse that key. A receipt for an earlier termination cannot discharge a later reopen.

### AD-11 [ADOPTED] [CURRENT] — Time enters through durable typed reminder intents

- **Binds:** DateReached resume and expiry scheduling across Works, EventStore, and Platform.
- **Prevents:** clocks in handlers, incompatible actor families, and discovery indexes treated as truth.
- **Rule:** Works folds committed work events into SDK `ReminderIntent` values for date resume and
  expiry. The domain handler receives commands and never reads a clock. The SDK owns the persistent
  actor, registration, scheduling callbacks and reconciliation. The default actor type is
  `WorksSdkReminderActor`. Use the SDK reminder codec for the `wra-` actor ID and the `wrs-` schedule
  token. AD-25 binds expiry and AD-26 binds transport identity. Platform owns Scheduler persistence,
  availability and restore. Recovery uses the control index and retained membership for discovery and
  verifies candidates against their streams.

### AD-12 [ADOPTED] [CURRENT] — Deadlines are advisory until a witnessed timer command arrives

- **Binds:** overdue and expiry semantics.
- **Prevents:** wall-clock-dependent replay and local deadline interpretation.
- **Rule:** scheduled expiry takes effect only through an authenticated timer command carrying the
  current AD-25 witness. The SDK retires superseded callbacks as audited no-ops. An authenticated
  ancestor cascade uses the `CascadeExpire` effect kind and `works.cascade-expire` purpose and a
  witness-free command. The kernel reads no clock.

### AD-13 [ADOPTED] [CURRENT] [TARGET] — Resume matches the active wait and preserves the winning disposition

- **Binds:** FR-14 and FR-15 across domain, reminder, child-completion, and external-signal paths.
- **Prevents:** different adapters acknowledging the same nonmatching trigger differently.
- **Rule:** an InProgress item may suspend on a nonempty ordered collection of valid, non-null
  conditions. Canonicalize each kind and key and record every member in submitted order, including
  duplicates. Read legacy singular evidence as one member. While Suspended, the first exact kind/key
  match committed in the Work Item turn records the consumed condition and clears the collection.
  A nonmatch while Suspended produces a domain rejection and leaves the conditions intact. Upon return
  to InProgress, a fresh command for the last
  consumed condition is a domain no-op. Exact transport redelivery replays the original disposition
  under AD-26. Other fresh triggers and all fresh Resume commands on terminal items reject.
  Today's Resume carries tenant, item and condition. For trusted child reconciliation, the approved AD-21 target requires
  an additional target-turn check of the wait occurrence before issuing the domain Resume. The
  public command retains its existing match and rejection rules. [Resume contract](../../src/Hexalith.Works.Contracts/Commands/ResumeWorkItem.cs)

### AD-14 [ADOPTED] [TARGET] — At most one Executor Binding per Work Item

- **Binds:** FR-17 through FR-19 and active handoff.
- **Prevents:** binding lists, executor-kind branches, and AuthorityLevel becoming an accidental role
  system.
- **Rule:** an item has at most one ExecutorBinding(PartyId, Channel, AuthorityLevel).
  Creation with or without an intended binding replays to Created. Assigned, InProgress and Suspended
  each require one responsible Party. Assign and Claim establish the binding under the lifecycle matrix. HandoffWorkItem is the only active-work binding change: in
  InProgress or Suspended it emits WorkItemHandedOff, changes only the binding, and preserves Status
  and AwaitConditions. AuthorityLevel values are stored but do not authorize v1 behavior; provenance
  and responsibility authorization are AD-23.

### AD-15 [ADOPTED] [CURRENT] [TARGET] [ASSUMPTION] — Tenant isolation includes disjoint data and control namespaces

- **Binds:** identities, aggregate references, state/read-model keys, indexes, checkpoints, queries, and
  logs.
- **Prevents:** cross-tenant traversal, reserved-name collisions, non-atomic checkpoint regression, and
  tenant indexes with no offboarding path.
- **Rule:** today Works passes the authenticated query's tenant and the `works-runtime-v3` family
  in a `SharedProjectionScope`. `WorksReadModelKeys.RuntimeRollUpKey(item)` supplies the logical key
  `works-runtime-v3:rollup:<item>`. The SDK coordinator reads queue, roll-up and parking together
  in its selected generation and derives the physical keys. [Current lookup](../../src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs) · [Key definitions](../../src/Hexalith.Works/Projections/WorksReadModelKeys.cs)

  The registry migration targets separate namespaces `works:v3:tenant:<base64url(UTF-8 canonical TenantId)>`
  and `works:v3:control`. Before switching formats, prove a new reader can open the existing writer's
  selected generation, then qualify rebuild, catch-up and promotion with both sides of the migration.
  Parent/child references stay tenant-closed and queries require independent authorization. In the
  target design the control tenant index has one actor/CAS writer.
  Indexes and checkpoints use ETag-protected monotonic merges, bounded cursor paging, audited
  tombstone/prune, and crash reconciliation. Legacy raw singleton keys and read/check/save checkpoint
  paths must migrate before their consumers switch. Canonical means the input is already a 1..64
  character lowercase ASCII slug matching `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$`; trust boundaries reject
  rather than transform other input. Claims, envelopes, references, keys, hashes, and equality reuse
  those exact UTF-8 bytes. The current Works dispatcher requires domain `work`, an ordinal aggregate ID
  match, a case-insensitive payload/envelope tenant match and consistency with any established state. It
  refuses reserved tenants. The isolated adapter accepts `tenant-a` against `TENANT-A`. Target
  production ingress rejects the noncanonical envelope before dispatch.

### AD-16 [CURRENT] [TARGET] [ASSUMPTION] — Shared rebuild uses one tenant/family epoch

- **Binds:** capture-through-Commit consistency for roll-up, what's-next, topology, pending intents, and
  quarantine/parking.
- **Prevents:** acknowledged live writes omitted by promotion and rebuilt queries that cannot recover
  pending work.
- **Rule:** EventStore exclusively owns the `works-runtime-v3` tenant/family manifest, generation
  selection, write leases, journal and checkpoints. A rebuild seals its inventory and source positions.
  Reads of related documents use one selected generation. Only a complete staged family can be promoted
  atomically. Return stale or unavailable while applicable catch-up, parking or conflict prevents a
  current answer.
  A writer fenced during capture or owner handoff returns retry without advancing the checkpoint or
  acknowledging delivery. When the SDK accepts a delivery into the journal, it durably binds source
  position and content to that disposition before acknowledgement. After promotion, apply accepted work
  to the promoted generation. On abort, account for accepted work against the active generation before
  releasing the fence. Never skip an over-capacity delivery. AD-06 and AD-22 govern the target
  registry-aware contents of this family. Process-effect checkpoints recover separately from source
  evidence and AD-26 receipts.

### AD-17 [ADOPTED] [CURRENT] [TARGET] — Lifecycle and numeric validation have one authority

- **Binds:** FR-6 through FR-10 validation and transition outcomes.
- **Prevents:** per-handler transition tables and projections inventing state.
- **Rule:** docs/lifecycle-transition-matrix.md is authoritative and must change atomically with
  lifecycle code and tests. Reports carry a positive delta. CorrectProgress replaces cumulative Done
  with a nonnegative value in the established Unit. ReEstimate replaces only the nonnegative estimate.
  Both folds derive Remaining as max(Estimated − Done, 0) and preserve visible overrun. The report
  saturation and re-estimation rules are implemented. Audited correction and conditional reopen remain
  delivery targets under AD-07.
  ReEstimate never completes or reopens the item. Unit follows AD-04. Terminal items contribute zero and
  add nothing to the unestimated count. An active unestimated item contributes zero and increments that
  count by one.
  Due/TTL policy is edge configuration, never kernel configuration. Handoff, correction, reopen,
  completion-kind, and Unit-inheritance changes ship atomically with this matrix, the durable catalog
  and validators, aggregate behavior, projection folds, golden corpus, and conflicting fitness, unit,
  and integration tests.

### AD-18 [ADOPTED] [CURRENT] — Dependencies and sibling boundaries point inward

- **Binds:** project references, domain ports, and owns-versus-references boundaries.
- **Prevents:** Projections/Reactors depending on Server and domain modules duplicating reusable runtime.
- **Rule:** Contracts is the inward dependency for Server, Projections, Reactor and Testing. The
  executable composes the four kernel projects with EventStore.DomainService and the Parties client. Its
  adapters supply domain behavior to the SDK. Generic runtime capabilities belong in EventStore. The
  five published libraries are listed in `tools/release-packages.json`. Platform composes the
  executable. Only the explicit synthetic qualification profile admits a Platform runtime project into
  that executable. Integration fixtures can reference Platform Identity and Composition sources.

~~~mermaid
flowchart RL
    Server --> Contracts
    Projections --> Contracts
    Reactor --> Contracts
    Testing --> Contracts
    Executable --> Contracts
    Executable --> Server
    Executable --> Projections
    Executable --> Reactor
    Executable --> EventStoreSDK
    Executable --> PartiesClient
    Platform --> Executable
~~~

| Sibling | Works owns | Sibling authority |
| --- | --- | --- |
| Hexalith.Parties | ExecutorBinding and the filter on Works queries | Party identity, resolved for the exact tenant and actor |
| Hexalith.Conversations | Optional ConversationCorrelationId | Conversation content |
| Hexalith.Tenants | Tenant-closed domain rules | Membership and tenant policy |
| Hexalith.Commons | Works value-object validation | Shared identifier helpers |
| Hexalith.EventStore | Aggregate behavior, read-model shapes and effect translations | Persistence, delivery, scheduling, receipts and process infrastructure |
| Hexalith.McpCli | Eligible operations declared in released Works contracts | Shared MCP/CLI exposure after enrollment, authorization and live conformance |

Contracts owns `IExpectationResolver` and `IExecutorRouter`. v1 supplies a non-LLM expectation resolver
and leaves routing unwired. The November PRD records zero enrolled Works operations in McpCli.

### AD-19 [ADOPTED] [CURRENT] — Version authority follows current ownership

- **Binds:** SDK, package, runtime image, and target-host version changes.
- **Prevents:** updating the wrong file or describing target ownership as current reality.
- **Rule:** Works `global.json` selects the .NET SDK. The root Builds gitlink and imported Builds
  catalog select Release dependencies. Platform owns host composition, Dapr components and topology. The
  Works-owned parity bootstrap links the Works root Builds checkout into the Platform snapshot and exports
  it as `HexalithBuildsRoot`. The Works verifier selects its SDK and fleet toolchain from that linked
  catalog. Its separate Agents check materializes Platform's recorded Builds gitlink and verifies
  its catalog against the published binding. Record each input's revision and catalog digest under
  its consumer. Exercise the two paths with different catalog pins. [Works bootstrap](../../scripts/verify-platform-parity-ci.py) · [Platform verifier](../../references/Hexalith.Platform/eng/verify_works_host.py)
  A Debug source pairing requires the same source revision in the reviewed manifest,
  Works gitlink and checkout, plus the matching evaluated Release version. An independently advanced
  checkout needs a fresh source review. Package-only Release and exact-source Platform parity each
  require their own evidence.

### AD-20 [ADOPTED] [CURRENT] — Platform owns the host and qualifies each cutover

- **Binds:** ownership of the executable, topology and the R1–R11 qualification contract.
- **Prevents:** a second active host or a dependency change inheriting an older release's approval.
- **Rule:** Works starts through `AddEventStoreDomainService` and `UseEventStoreDomainService`.
  Hexalith.Platform owns the active Aspire/Dapr topology. The former Works AppHost and ServiceDefaults
  survive only in a byte-verified rollback archive. Every candidate must bind its release package,
  producer, Works/Platform sources and launched artifacts to all eleven rows, 44 proof kinds and
  complete-fleet rollback. The CI profile uses Release packages. The full profile also proves source
  Debug conformance.

| Row | Who supplies the seam | What qualification exercises |
| --- | --- | --- |
| R1 | Platform AppHost and Dapr components | Live topology and component policy |
| R2 | Platform and SDK service defaults | Health and telemetry |
| R3 | Platform subscription, SDK delivery and Works handlers | Origin, retry and quarantine |
| R4 | SDK epoch writer and Works family folds | Convergence, rebuild, catch-up and generation selection |
| R5 | Works query policy behind SDK ingress | Parties authorization and scoped results |
| R6 | SDK reminders, Works intents and Platform Scheduler | Restart, stale callback and recovery |
| R7 | SDK process runner and Works translations | Paging, crash recovery and receipt replay |
| R8 | SDK readiness and Platform operations | Degradation, alerting and audited disposition |
| R9 | Works domain-service executable | Canonical SDK composition and protected routes |
| R10 | Platform security policy | mTLS, origin refusals and production admission controls |
| R11 | SDK gateway and Works delegated effects | Target receipt replay and identity conflicts |

The refreshed Platform Development preview requires an owner-provisioned `Platform:Works:DevelopmentCatalogProfile`. When receipts are retained, restore the exact private profile, digest key ring, SDK catalogue custody and matching Redis/Scheduler fleet together. [Development profile](../../references/Hexalith.Platform/docs/works-development-catalog-profile.md)

The October cutover established this boundary with a passing 11-row, 44-proof packet and fleet restore on EventStore 3.126.0. The archived topology serves rollback qualification. The Platform preview refuses production admission. Historical proofs, the local identity preflight and the current CI result have distinct scopes. See [the cutover record](../../docs/platform-hosting-cutover.md).

### AD-21 [TARGET] [ASSUMPTION] — The Work-Tree Registry is the sole topology authority

- **Binds:** FR-13, FR-16, FR-26, wire identity, lifecycle, fencing, and topology reads.
- **Prevents:** two parents, cycles from stale caller facts, ghost edges, late commands after release,
  and multiple topology truths.
- **Rule:** today Create and SpawnChild validate the supplied ancestry and depth against local evidence.
  Separate parent streams cannot arbitrate competing claims on the same child. Until registry admission
  and recovery pass the two-parent and restart cases, restrict parented operations to a trusted
  single-writer workflow wherever stale ancestry could corrupt the tree.

  The target registry is EventStore aggregate
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
  appends the child condition to an already Suspended parent's ordered collection if absent, preserving
  its prior members and their order. A cleared suspend flag preserves status and conditions.
  Redelivery of the same attachment cannot install a second wait. Terminal parents
  remain terminal; for Cancelled or Expired parents, attachment processing durably schedules matching
  child-cascade catch-up before acknowledgement. The Attached edge remains authoritative even if that
  parent act arrives after the terminal transition. Released tokens are terminal. Creating never releases:
  an operator may quarantine it while preserving the token, then repair attaches verified child
  evidence or marks an orphan only after proving a permanent conflict. EventStore validates the
  reservation token immediately before CreateWorkItem dispatch.

**Parent admission order [ADOPTED] [TARGET].** The public entry remains
the registry reserve command. The Reactor submits the request to the parent. In its serialized turn,
the parent appends an admission keyed by tenant, parent and stable request ID. Persist the verbatim
child-create payload, child ID, suspend flag and FR-12 Unit choice together so recovery can reconstruct
the command. Verify the persisted digest on delivery and recovery. Reuse of the request ID with
different caller input is a conflict. An exact retry reuses the original captured Unit.
This parent append is the FR-10 termination cutoff. A new request after termination rejects. With
suspend requested, only InProgress and Suspended can admit. On replay, propagate the captured Unit
through reservation and child creation even if the parent subsequently acquired a different estimate.

The Reactor resumes from that record after a crash. In one registry turn, check the parent record,
tenant quota, single parent, cycles and depth against attached and outstanding edges before appending
`EdgeReserved`. Only this append creates a reservation. A competing or inadmissible edge receives a
durable refusal. Exact retries of the same request resolve its existing outcome even after termination.
A fresh delivery duplicating an identical attached pair is a semantic no-op. It leaves the edge,
wait and cascade intact. Conflicting input, a competing parent or a released token receives its
recorded refusal or a new refusal as applicable. A later Cancel or Expire must track every earlier parent admission until the registry refuses it,
releases it, or attaches the child and the matching cascade reaches closure. This includes a request
still in transit when termination commits. A restart at either append boundary must give the same
result. Only attached edges enter topology reads.

**Child Resume bound to the installed wait [ADOPTED] [TARGET].** Retain child completion and wait installation across
restart and reconcile in either order. Identify the intended wait by tenant, parent, the parent
envelope sequence that installed the child condition, and its canonical kind/key. An additional child
condition on an already suspended parent uses its own installation sequence. A duplicate that adds
nothing preserves the original occurrence. Pass that witness in the authenticated internal effect
and bind it to the AD-26 digest. In the serialized parent turn, compare it with the active installation
before applying Resume. Retry until exactly one effective Resume commits for an eligible wait or a
competing trigger durably closes it. A consumed or superseded installation,
including one replaced by the same child condition, gets a durable obsolete no-op receipt and leaves
current state intact. Redelivery returns that disposition after restart. An earlier nonmatching
attempt cannot discharge a later installation.

All fan-out, cascade, child-completion recovery, rebuild, and repair code uses one Works-owned
IWorkTreeTopologyReader over the registry read model. It exposes exact-token
ResolveAttachedAncestry, stable cursor-paged EnumerateAttachedDescendants, and attachment lookup.
Reserved or missing attachment is retryable and never acknowledged. Works owns contracts, aggregate,
and pure Reactor translations; EventStore owns delivery, CAS projection, relationship adapter,
process/schedule primitives; Platform resolves trusted MaxDepth and timeout policy for the target
tenant before admission. The v1 MaxDepth default is 32 and breadth is uncapped. Consumers switch only
  after a per-tenant migration proves registry equivalence. Production admission also requires a
  Platform-supplied tenant topology quota; missing or exhausted quota fails before EdgeReserved.
  Breadth has no domain cap beneath that operational quota. The current SDK process profile admits five
  direct-child effects per terminal envelope. A sixth quarantines the whole envelope before target
  submission. Qualify coherent process budgets for larger fan-out. EventStore snapshots the registry,
  bounds
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
  Partial results are never served as complete. Repair or rebuild exposes the applicable unavailable or
  stale state. The current SDK adapter emits no invalidations. A future notification transport may
  publish only the changed key after durable commit. Its activation is deferred.
  WorkContributionSnapshot is a non-domain projection
  message derived only from a committed EventStore envelope; it is never appended to Work Item or
  Registry streams and never advances payload Sequence. Works.Projections owns the fold and snapshot,
  EventStore owns the generic merge/relationship SPI without referencing Works, and only the Works
  executable composes their adapter. At quiescence, after all tree events are acknowledged, the
  Platform lane verifies totals and records convergence time and authorized read latency. The
  indicative fixture uses five seconds and 200 ms as benchmarks. G8 binds performance acceptance
  to an approved dataset, environment, percentile, measurement window and failure policy.

### AD-23 [TARGET] [ASSUMPTION] — Identity and tenant delegation are end-to-end authority

- **Binds:** external ingress, internal effects, acting Party, and responsibility authorization.
- **Prevents:** tenant assertion as authority, global-admin workload bypass, and a compromised service
  invoking unrelated commands.
- **Rule:** the current gateway checks a trusted effect's scope and Works verifies command, workload and
  purpose before delegation to the kernel. The synthetic Platform fixture can issue effect authority.
  Production issuance and the target actor-turn responsibility check require their own evidence. In the
  production design, Platform authenticates external OIDC identity and derives tenant/actor through
  Hexalith.Tenants. The Hexalith.Platform Workload Delegation Service is the sole issuer of a
  short-lived asymmetric-signed WorkloadDelegation JWT. It binds the eventstore-gateway audience,
  full AD-26 tuple and EffectId, target, command type, command digest, tenant, purpose,
  causation, issued time, and expiry. EventStore validates signature/audience/expiry, recomputes every
  binding and enforces the origin policy for the domain and command. Domain-service ingress requires the
  receiving sidecar's app-channel token and a separately signed workload assertion binding caller,
  receiver and operation. If supplied, `dapr-caller-app-id` must agree with the signed caller.
  `dapr-app-id` selects the destination. Delegated effects independently bind their purpose, causation,
  target and command digest. Allow-listed workloads remain tenant-scoped.

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
  The canonical host defaults to denying privileged process revalidation until an authorized operator
  and durable audit provider are configured. Platform's synthetic development authorities are confined
  to qualification. Direct-port, wrong-app, wrong-trust-domain, unauthorized publish, forged sequence,
  wrong tenant or
  purpose, and ordinary-user SpawnChild tests must fail; valid production-policy reminders must pass.

### AD-25 [CURRENT] [TARGET] [ASSUMPTION] — Expiry carries a persisted schedule witness

- **Binds:** FR-10 expiry, Due Date/TTL policy, reschedule safety, and recovery.
- **Prevents:** stale reminders expiring rescheduled work and different hosts choosing different policy.
- **Rule:** Platform resolves tenant-specific Due-Date/TTL policy and supplies an effective expiry
  instant before domain admission; create and reschedule facts persist that instant and a deterministic
  ScheduleToken additively. The token is wrs-<digest>, where digest is uppercase Crockford Base32
  SHA-256 over the canonical SDK tuple (schedule, tenant, item, effective-expiry UTC ticks, schedule
  revision). A supplied expiry revision is positive and each replacement strictly increases it. Reminder
  names are date-<token> or expiry-<token>; a stored
  full tuple mismatch is collision plus quarantine. One AD-11 Expiry intent mirrors them.
  Scheduled ExpireWorkItem echoes the witness. A missing or mismatched witness on a witnessed item is
  refused, while a match follows the lifecycle matrix. Separately authenticated CascadeExpire follows
  AD-12. Registration, cancellation, and reschedule derive mechanically from
  committed lifecycle events. Policy never enters Handle; changing policy affects future commands,
  not historical replay.

### AD-26 [CURRENT] [TARGET] [ASSUMPTION] — Cross-aggregate effects have deterministic durable identity

- **Binds:** R11 and every Reactor, reminder, cascade, child-resume, and recovery submission.
- **Prevents:** duplicate logical effects after redelivery, ownership migration, status expiry, backup
  restore, or command-ID truncation.
- **Rule:** EffectId is uppercase Crockford Base32 SHA-256 over a fixed-order binary tuple:
  each text field is canonical UTF-8 preceded by a four-byte big-endian length; numeric fields are
  eight-byte big-endian. Tuple order is tenant, source domain, source aggregate, source envelope
  sequence, effect kind, target domain, target aggregate, effect ordinal. EventStore's versioned effect
  and reminder codecs own these encodings. For implemented delegated effects, the command digest is
  SHA-256 over the exact submitted payload bytes. Retain or reproduce identical bytes for retries. The
  target G1 semantic command encoding requires a versioned field contract and shared byte vectors before
  producers switch. Text uses the length rule above; integers are signed eight-byte big-endian;
  SHA-256 renders most-significant-bit first through alphabet 0123456789ABCDEFGHJKMNPQRSTVWXYZ with
  final zero-bit padding as exactly 52 uppercase characters and no separator padding. Single-target
  process effects use the SDK primary ordinal. DateResume derives each ordinal from the zero-based
  position of a distinct DateReached condition sorted by ordinal correlation key within its source
  suspension. Preserve these assignments on replay. New effect families must define their ordinal before
  enrollment. In the approved AD-21 child reconciliation target, use the parent installation envelope
  as the source and the parent as target. For a single installation event containing several child
  keys, assign ordinals by zero-based ordinal sort of the distinct canonical child keys in that event.
  Use the installation envelope's message ID as the effect's CausationId. When a child has completed
  more than once, select the earliest matching completion by child stream sequence after verifying
  the ordered history through that evidence. Freeze its envelope reference and the effect payload
  durably before first submission. Retries after a reopen, reverse delivery or restart reuse both.
  A later wait gets its own effect ID.
  MessageId and IdempotencyKey both reuse wrk-<EffectId>, satisfying gateway-safe identifiers.

  EventStore exclusively owns a private target-scoped effect inbox in the target actor partition.
  Receipt lookup and a receipt containing EffectId, full tuple, command digest, success,
  rejection, or no-op disposition, workload, delegation purpose, and causation commit atomically with
  target events and metadata. Works domain streams contain no receipt event. A retry with the same key,
  full tuple and command digest returns its recorded disposition without redispatch. Reusing
  the key with a different tuple or digest creates an identity conflict and quarantine entry and leaves
  the receipt and target unchanged. Receipts survive the Work Item lifecycle and share the tenant's
  legal-hold/offboarding retention class. Receipt and
  corresponding source/target evidence erase only as one authorized tenant operation. Replays older
  than the retained source floor reject or quarantine rather than execute. Gateway terminal records use
  EventStore Commit retention and are an optimization, not the correctness boundary. Golden vectors
  cover every effect family before its producer is registered.

### AD-27 [CURRENT] [TARGET] [ASSUMPTION] — Recovery is lossless, periodic, and readiness-affecting

- **Binds:** R7/R8 stream paging, checkpointing, retry, parking, and operations.
- **Prevents:** page-boundary event loss, startup-only recovery, silent parking, and Ready while work is
  stranded.
- **Rule:** all recovery readers share one strict page validator. EventStore FromSequence is an
  exclusive lower bound, so the next page reuses LastSequenceReturned without adding one. Every
  page validates domain, canonical tenant, aggregate, strictly increasing envelope positions, and
  complete decoding of state-affecting evidence. Bounded hot retry transitions to a durable
  tenant-scoped unresolved/quarantine record. The same validator and quarantine path govern live,
  replay, repair, and rebuild. Works exposes SDK recovery status through `/ready` and bounded
  `eventstore.domain.recovery.*` telemetry. Incomplete discovery remains retryable. Unresolved work
  keeps readiness degraded until recovery or authenticated, audited disposition. A delivery acknowledges
  only after side effect/checkpoint or quarantine capture.

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

The proposed production objectives are RPO at most 15 minutes, RTO at most four hours, and quarterly
restore drills. G6 and G8 require accountable owners to approve and demonstrate these objectives.
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
  type or renamed discriminator replaces an existing durable type. Story 5.5's 4,000-character
  Obligation limit applies at new Create admission when delivered. Replay must still accept longer
  historical WorkItemCreated payloads.

## Consistency Conventions

| Concern | Convention |
| --- | --- |
| IDs | Existing Work Item IDs accepted by EventStore `AggregateIdentity` remain readable (up to 256 ASCII characters, alphanumeric at both ends, with interior dots, hyphens and underscores permitted). The target edge emits ULIDs. Infrastructure keys encode canonical IDs. |
| Time | DateTimeOffset/UTC at edges; domain behavior consumes persisted instants, never wall clocks |
| Envelopes | EventStore envelope sequence is canonical; Works payload Sequence is state-change ordinal only |
| Serialization | Options-free PascalCase concrete writer; shared case-insensitive tolerant reader; no polymorphic marker at rest |
| Errors | Domain rejection event for expected invalid acts; infrastructure exception/dead letter; RFC 9457 at HTTP edges |
| Logging | Source-generated structured logs; bounded metadata only; never payloads, secrets, or personal data |
| Configuration | Typed options at host/runtime edges; aggregates do not read configuration |
| Tests | xUnit v3, Shouldly, NSubstitute; property tests for convergence; persisted end-state assertions at integration boundaries |

## Stack

The following values come from the local Works `a4a91332` tree and its recorded Builds catalog. The Platform
host and its release evidence own the deployed runtime versions.

| Name | Version | Authority |
| --- | --- | --- |
| .NET SDK / Works target | 11.0.100 with latestPatch / net11.0 | `global.json` and `Directory.Build.props` |
| Compatibility SDK | 10.0.404 | README compatibility fixtures and Platform checks |
| EventStore Release packages | 5.4.0 | Recorded Builds catalog and Works source-pair manifest |
| PolymorphicSerializations Release package | 2.0.0 | Recorded Builds catalog and Works source-pair manifest |
| Shared build catalog | 6.4.4 | Works gitlink `fe68718b` and `.github/hexalith-ci.json` |

| Debug dependency | Works gitlink at `a4a91332` | Reviewed source manifest | Checked-out revision |
| --- | --- | --- | --- |
| EventStore | `7435254cc` | `607072c68` | `7435254cc` |
| PolymorphicSerializations | `c6d0dfce` | `c6d0dfce` | `c6d0dfce` |

Works records Builds 6.4.4 (`fe68718b`), selecting EventStore 5.4.0 and Dapr runtime 1.19.1.
The local Builds checkout (`e1380b81`) selects EventStore 5.4.0 and Dapr runtime 1.19.2 for Works.
Platform `abb62da8` binds the Agents check to `fe68718b`. Dapr CLI is 1.19.1 in both catalogs.
These values describe inputs. The parity bootstrap stopped before launch. [Platform producer verification](../../references/Hexalith.Platform/eng/bound_builds.py) · [Reviewed source pairs](../../tools/source-debug-dependencies.json)

## Structural Seed

The Works solution contains four pure kernel libraries under `src/` (Contracts, Server, Projections and Reactor), the `Hexalith.Works` domain-service executable and the infrastructure-free `Hexalith.Works.Testing` package under `tests/`. Unit, property, integration and architecture tests exercise these boundaries. The sample host is a consumer. The Work-Tree Registry in AD-21 is proposed. [Solution inventory](../../Hexalith.Works.slnx)

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

Readiness is conditional. Each gate applies to the source, package and environment it verifies.

| Gate | Evidence required |
| --- | --- |
| Dependency identity and CI | At local `a4a91332`, the source preflight refuses EventStore revision drift. PolymorphicSerializations matches its reviewed source. Reconcile EventStore and rerun both pair checks under AD-19. Run the four ordinary Release test projects, including ArchitectureTests, on the candidate revision. At Works `07020c84` the shared CI job failed at “Stage 3 unit tests” and skipped `custom-platform-parity`. [CI run](https://github.com/Hexalith/Hexalith.Works/actions/runs/44874235428) |
| Platform and recovery | The bootstrap exits with `Parity requires a stable public EventStore version` because it reads the Works package wrapper literally. Qualify evaluated catalog selection in bootstrap and downstream consumer-pin checks and verify the producer. Pass all AD-20 rows, 44 proof kinds and fleet restore for the selected public package and exact source. Exercise page boundaries, restart, retries, conflict and audited disposition. Production requires the AD-23/24 origin and policy evidence and AD-28 data-owner approval. |
| Runtime compatibility | Qualify the exact .NET, Aspire, Dapr hosting adapter and Dapr runtime tuple with restore, Release build, integration and Platform parity. Upgrade an unsupported or preview dependency, or record a time-bounded production exception with an accountable owner and upgrade trigger. |
| Work-tree integration | Establish AD-26 receipts before registry effects. Works and EventStore must record their joint review and qualify both approved protocols before rollout. Pause after parent admission, terminate, then resume registry delivery. Reverse the order, race two parents and restart between durable acts. Deliver a child Resume for the first time after another trigger won and the same child wait was reinstalled, then replay its receipt after restart. Require per-tenant migration proof before concurrent registry-backed writes. |
| Effort migration (G3) | Deploy aggregate and projection changes together. Identify affected snapshots and rehydrate from raw events on a restored copy. Rebuild into a fresh epoch, drain catch-up and verify aggregate, Roll-Up and eligible/ineligible queue results before reopening traffic. A snapshot that discarded Done requires its original events. |
| Lifecycle and schema | Update the transition matrix, catalog, validators, folds, golden corpus and affected tests together. Cover handoff, correction/reopen, child-before-wait, termination after reopen and active-item detail authorization. Apply AD-29 to new wire fields and G5 to mixed-version readers. |
| Capacity and recovery budgets (G8) | Product and Architecture bind an environment, realistic history sizes, latency percentiles, measurement window and failure policy. Exercise journal capacity and recovery at that load. |

The PRD's G1–G8 exit criteria apply separately. G4 requires authorization and audit for the tenant control index. G6 requires approved retention and a restore drill. G7 requires verified identity and trusted ingress. G2 requires a successful production-policy reminder test. [Product gates](prds/prd-works-2026-06-14/prd.md#101-v1-dependency-and-exit-gates)

## Deferred

| Item | Owner or decision already in force | Revisit condition |
| --- | --- | --- |
| Production host admission | Platform owns host policy, workload identity, secrets, privileged audit and restore | Accountable owners qualify the production configuration and AD-28 data lifecycle |
| Incremental roll-up and changed-key notifications | Works supplies folds and the SDK owns delivery | Registry equivalence and G8 measurements for fan-out, plus a transport and post-commit delivery decision for notifications |
| Final legal retention and hold durations | Accountable data owner under AD-28 | Before non-synthetic shared data admission |
| Cost-aware time and routing roles | The kernel preserves AuthorityLevel as data | Before the routing or cost-scheduling themes |
| UI and production channel adapters | v1 is headless. Shared McpCli owns eligible MCP/CLI operations | First approved surface with released contracts and conformance evidence |
| Provider-specific infrastructure | Platform owns provider selection | Production deployment design |

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
