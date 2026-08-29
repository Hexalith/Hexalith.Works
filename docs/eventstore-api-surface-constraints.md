# EventStore API Surface Constraints

Story 1.1 verified the live `Hexalith.EventStore` source surface before Works depends on domain behavior in later stories.

## Canonical Stream Sequencing

Verified against the current EventStore pin `c61739206fd89619b7d29dfb0812225a234066bb`
(`v3.98.0-10-gc6173920`). The characterized `EventPersister`, `EventStreamReader`, and shared payload
serialization files are unchanged from `b43e963403efa848eda9621b5e3e7e446c7faa2d`.
Two always-on guards cover that source, and neither covers the other's claims:
`EventStoreApiSurfaceCharacterizationTests.P1_EventStorePersistsRejectionsAndUsesEnvelopeCanonicalSequencing`
reads `AggregateActor`, `EventPersister`, and `AggregateReplayer` source text and guards the
envelope-sequencing claims only. The concrete-writer and shared-reader casing claims below are guarded
behaviourally instead, by `EventPersisterGoldenCorpusTests` (options-free PascalCase writer bytes, read
back through `EventStorePayloadSerialization.Options`) and `SchemaEvolutionGoldenCorpusTests` (camelCase
compatibility inputs read through the same shared options). `EventStreamReader`'s snapshot-at-current
behaviour is guarded by `EnvelopeCanonicalSequencingTests`.

EventStore envelope `SequenceNumber` is the canonical persisted position. `EventPersister` derives it
from `AggregateMetadata.CurrentSequence`, assigns one gapless position to every persisted success or
`IRejectionEvent`, and updates metadata independently of any similarly named payload field.
`EventStreamReader` reads those envelope positions from 1 through the metadata watermark on a full
replay, and only the tail from `snapshot.SequenceNumber + 1` when a snapshot is supplied (returning the
snapshot alone when it already sits at the current sequence). `AggregateReplayer` sorts and gap-validates
the positions it is given before invoking every matching `Apply` overload.

Works payload `Sequence` has a narrower meaning: it is the state-changing-event ordinal used by
`WorkItemState` and advances only when an event mutates aggregate state. Rejection payload shapes remain
frozen without `AggregateId` or `Sequence`; applying an `IRejectionEvent` is a no-op even though its
EventStore envelope occupies a persisted position. Therefore, after a rejection at envelope position 1,
a valid create is persisted at envelope position 2 while `WorkItemCreated.Sequence` is correctly 1.
Projection delivery, replay ordering, freshness watermarks, and deduplication use the EventStore
envelope `SequenceNumber`, not the Works payload ordinal.

## Concurrency

EventStore does not expose an explicit `expectedVersion` append argument. Optimistic concurrency is implemented through the Dapr state-store ETag used by `AggregateActor.SaveStateAsync()`, which raises `ConcurrencyConflictException` after configured retries. Later Works claim and single-writer stories must translate that infrastructure conflict into Works domain rejections instead of assuming an expected-version append API.

## Online Rebuild

EventStore's aggregate projection rebuild remains operator-initiated, checkpoint-per-aggregate, and pausable
through `IProjectionRebuildOrchestrator`, `ProjectionRebuildCheckpoint`, and `ProjectionRebuildStatus`; that
path is not a shadow-projection plus atomic-swap model. EventStore also exposes the internal, versioned
`/project/rebuild/shared/v1` lifecycle for projections that require a sealed set of co-available aggregate
histories. Its Begin/Accumulate/Finalize/Stage/Commit protocol validates bounded candidates and promotes one
single-store `IReadModelBatchStagingStore` manifest atomically. Works uses that shared lifecycle for tenant-wide
roll-up reconciliation while retaining the checkpoint path for independent aggregate projections.

## Story 4.5 — Command/Event Pipeline Under Aspire (runtime adapter proof)

Story 4.5 wired the first runnable adapter edge (`src/Hexalith.Works`) and the Works AppHost topology. The
verified EventStore domain-service surface is:

- **Discovery requires a concrete `EventStoreAggregate<TState>` subclass.** `AssemblyScanner` only discovers
  subclasses of `Hexalith.EventStore.Client.Aggregates.EventStoreAggregate<TState>` (and
  `EventStoreProjection<TReadModel>`). The pure static `WorkItemAggregate` is **not** discovered. The host
  therefore provides `WorkItemEventStoreAggregate : EventStoreAggregate<WorkItemState>` decorated
  `[EventStoreDomain("work")]` (the convention would otherwise derive `work-item-event-store`), declaring one
  `public static DomainResult Handle(TCommand, WorkItemState?)` wrapper per Works command that delegates verbatim
  to the pure kernel. No EventStore runtime inheritance leaks into `Server` — the `Server -> Contracts` direction
  is preserved (fitness-asserted).
- **Canonical host shape.** A domain module is two lines — `builder.AddEventStoreDomainService(assembly)` then
  `app.UseEventStoreDomainService()`. The SDK supplies the platform service defaults (health/OpenTelemetry),
  convention discovery/registration of aggregates + `IDomainQueryHandler`s, runtime activation, and the canonical
  `/process`, `/replay-state`, `/query`, `/project`, and `/admin/operational-index-metadata` endpoints. Per the
  EventStore domain-module contract a domain must **not** fork its own ServiceDefaults, so the host does not
  reference `Hexalith.Works.ServiceDefaults`.
- **Polymorphic registration is required at the host edge.** Commands arrive at `/process` through the shared
  static `PolymorphicSerializationResolver` registry, so the host calls
  `HexalithWorksContractsSerialization.RegisterPolymorphicMappers()` (plus the DI registration) at startup.
- **Persist-then-publish.** `AggregateActor` persists events (the `EventsStored` checkpoint via `SaveStateAsync`,
  raising `ConcurrencyConflictException` on the Dapr state-store ETag conflict) **before** `PublishEventsAsync`.
  Command status advances Received → Processing → EventsStored → EventsPublished → Completed; the smoke test polls
  `/api/v1/commands/status/{correlationId}` to a terminal status.
- **Concrete writer and shared reader casing.** `EventPersister` serializes normal concrete payloads with
  options-free `System.Text.Json`, producing compact PascalCase bytes at rest with no polymorphic `$type`.
  EventStore's shared `JsonSerializerDefaults.Web` reader options are property-name case-insensitive and accept
  both those PascalCase bytes and camelCase compatibility/client inputs.

### Projection-model reconciliation — what is wired and what is still deferred

- **Wired:** a bespoke async `/project` handler (mapped before `UseEventStoreDomainService`, so the SDK yields the
  route) decodes each `ProjectionEventDto` by `EventTypeName` through case-insensitive Web reader options. That
  reader accepts both the options-free PascalCase persisted form and camelCase compatibility fixtures, with no
  polymorphic `$type` in either concrete form. It feeds the pure
  `WhatsNextQueueProjection` and `WorkItemRollUpProjection`, and persists a tenant-scoped `works-whats-next` index
  plus per-item roll-up through `IReadModelStore` + `ReadModelWritePolicy` (idempotent per-item merge, ETag-guarded).
  A discovered `WhatsNextQueryHandler : IDomainQueryHandler` reads that index and applies the pure
  `WhatsNextOrdering` + `WhatsNextQueryAuthorization`.
- **Refuse stale persisted roll-ups (2026-08-27):** the EventStore `/project` contract delivers a **single
  aggregate's** event stream per call. A parent replay can reconstruct its own current effort and its accepted
  `ChildSpawned` facts, but cannot reconcile those children with their later, separately dispatched streams.
  The runtime adapter therefore refuses totals when `ExposedChildCount > 0` or when the request contains a
  `ChildSpawned` event type that could not be decoded or accepted. It persists/exposes both `RolledRemaining` and
  `RolledRemainingByUnit` as unavailable (`null` / empty). It preserves own effort, lifecycle status (including
  terminal status), parent/child structure, tenant identity, diagnostics, and the accepted-source watermark.
  Exposed child identities are emitted in ordinal `WorkItemId.Value` order; `ExposedChildCount` is that filtered
  sequence's size, not an effort-contribution count. Leaf roll-ups remain locally complete and available. The pure
  projection still supports recursive convergence when all contributing event streams are co-available; the per-aggregate
  runtime does not claim that convergence. Deterministic `WorkItemProjectionQueryAdapterTests` assert the
  persisted read-model end state and query representation.
- **Monotonic projection writes (2026-08-29):** delayed full replays compare the accepted EventStore envelope
  position carried by `LatestAcceptedSourceSequence` before mutating either persisted model. The adapter writes
  the per-item roll-up first through `ReadModelWritePolicy`; a strictly newer stored roll-up makes the stale
  dispatch skip its tenant-index write. The tenant index has its own ETag-guarded per-item `LastSequences`
  watermark, retained after eligibility removal, and falls back to an eligible legacy item's own sequence only
  when that aggregate id has no `LastSequences` entry; a retained entry is the sole authority for that id, so a
  stored item cannot outrank its own tombstone. Equal-sequence redispatches may refresh deterministic
  documents. An empty or rejection-only replay has no accepted model and writes neither roll-up nor what's-next
  index. These two keys remain separate, non-atomic read models: each is independently monotonic and they
  converge on replay rather than claiming a cross-key transaction. The pending date-await index maintained by
  the same dispatch is outside this guarantee: it keeps its pre-existing raw stream-sequence watermark.
- **Shared roll-up reconciliation (2026-08-29):** Works registers the public
  `WorkItemSharedProjectionRebuildHandler` for EventStore's internal `/project/rebuild/shared/v1` route. The
  operator supplies a sealed authoritative tenant inventory; the handler folds all complete histories through
  one pure roll-up and what's-next graph, discovers edges from both `ChildSpawned` and
  `WorkItemCreated.Parent`, and applies the same boundary sanitizer as normal `/project`. Missing, malformed,
  unknown, cross-tenant, cyclic, or multiply-parented relationship evidence keeps reliable local fields but
  makes both rolled shapes unavailable. Stage writes only a bounded candidate envelope. Commit atomically
  promotes a schema-v2 tenant membership index and its per-item documents while deleting the candidate-known
  unversioned keys. Until that commit, readers and ordinary dispatch continue using existing legacy keys; once
  the v2 index exists, membership is authoritative and makes any unlisted historical document unreachable.
  The Dapr ACL admits this route only from EventStore, and EventStore's candidate/manifest limits fail closed
  before any live mutation.
  Operational consistency requires ordinary projection delivery to remain quiesced from inventory capture
  through Commit, or an equivalent platform fence that excludes live writers. Delivery resumes and catches up
  after Commit. The Works handler does not itself prevent a concurrent ordinary `/project` writer from
  overwriting, or being overwritten by, a `LastWrite` manifest operation.

### Build reconciliation

Aspire package pins were reconciled from 13.4.3 to **13.4.6** (and `Aspire.AppHost.Sdk` to 13.4.6) to match the
checked-out `Hexalith.EventStore` submodule, which `Hexalith.EventStore.Aspire` requires. This is a submodule-drift
alignment forced by the ProjectReference rule, not a discretionary upgrade.

## Story 4.6 — Reminder and Reactor Recovery (adapter-edge proof)

Story 4.6 keeps the Works kernel clock-free and infrastructure-free while proving two recovery concerns at the
runnable host edge:

- **Date resumes use Dapr actor reminders.** `src/Hexalith.Works` registers a `DateReminderActor` through
  `AddActors`/`MapActorsHandlers`; reminder names are deterministic from `(tenantId, workItemId,
  AwaitCondition.CorrelationKey)`. A fired reminder rebuilds `ResumeWorkItem(TenantId, WorkItemId,
  AwaitCondition.DateReached(instant))` and submits it through the EventStore command gateway. Duplicate
  reminder registration targets the same actor/reminder name; duplicate firings reissue the same deterministic
  command and converge through EventStore/aggregate idempotency.
- **Scheduler/state-store dependency is explicit.** Local proof uses the existing Redis-backed `statestore`
  component with `actorStateStore: "true"` and `works` in scope. Dapr Scheduler, placement, and Redis are
  prerequisites for the live Tier-3 lane; deterministic tests cover the adapter logic without those services.
- **Reminder reconciliation is bounded by the per-aggregate stream-read route.** _(Superseded by Story 4.8 — see
  below; at 4.6 the host rescanned a hand-configured `Works:Recovery:Tenants` scope with a tenant-wide read.)_ The
  gateway's `POST /api/v1/streams/read` route **requires an `AggregateId`** — `StreamReadRequest.AggregateId` is
  contract-optional ("omit only for domain-wide rebuild reads") but `StreamsController` rejects a null id today —
  so neither tenant-wide nor domain-wide enumeration is available. Story 4.8 keeps every reminder read per-aggregate
  but drops the hand-configured tenant scan for durable-index discovery (below). The reconciliation decision logic
  is proven deterministically by `DateReminderRecoveryRuntimeTests`.
- **Cascade checkpoints are host-edge read-model state.** The terminal-cascade runtime uses the pure
  `TerminalCascadeTranslator`, persists bounded checkpoint records in the shared state store via
  `IReadModelStore`, and submits descendant terminal commands through the EventStore command gateway. Checkpoint
  state is written before each target attempt and again after dispatch; replay reuses the persisted checkpoint,
  not an in-memory descendant list. If a process stops after submit but before completion is recorded, replay
  resubmits the same deterministic command, which remains safe under aggregate idempotency.
- **Descendant discovery limitation.** Production discovery reads direct children from the parent stream. Already
  terminal descendants can be skipped when the re-readable candidate source marks them terminal; otherwise a
  duplicate terminal command remains safe because domain acceptance still round-trips through `Handle`. A richer
  subtree/status projection would improve skip-before-dispatch fidelity without changing the kernel boundary.

No Story 4.6 reminder, checkpoint, or read-model runtime record is a durable polymorphic command/event/rejection
catalog type. `WorkItemV1Catalog.Count` remains **37** and the golden corpus is byte-compatible.

## Story 4.8 — Register and Reconcile Date Reminders Durably

Story 4.8 closes the runtime-wiring gap the 2026-07-21 audit found: date resumes must execute in the live topology
in steady state and on recovery, without per-tenant hand configuration. It changes the stream-read usage and the
recovery-discovery model while keeping every read per-aggregate.

- **Suspend-time registration on the live event stream (AC #1).** A new
  `IEventStoreDomainEventHandler<WorkItemSuspended>` on Story 4.7's `work.events` subscription re-folds the
  suspended aggregate's per-aggregate stream through the pure `PendingDateAwaitProjection` and registers one durable
  Dapr reminder per pending `DateReached` await. Registration is derived from the folded **current** pending set,
  never a raw event in isolation, so a suspend redelivered after the item resumed registers nothing. The
  subscription (immediate on publish) — not the `/project` dispatch (delivered by EventStore's
  `ProjectionPollerService` on a per-domain refresh cadence, so poll-interval latency) — is the steady-state
  trigger.
- **Durable pending-date-await index replaces the hand-configured scan (AC #2/#3).** The `/project` dispatcher now
  also maintains, alongside the what's-next and roll-up read models, a per-tenant pending-date-await index document
  (`projection:works:pending-date-await:{tenantId}`) plus one well-known tenant-registry document
  (`projection:works:pending-date-await:tenants`), both plain host-edge `System.Text.Json` read models upserted via
  `ReadModelWritePolicy.UpdateAsync` (registry written before index so a crash strands only an empty read, never a
  hidden entry). The registry is what removes per-tenant configuration: Dapr state stores expose no key enumeration
  and the gateway exposes no tenant-wide read, so the durable registry is the substrate-compatible enumeration.
- **Index is discovery, stream is truth.** The recovery source enumerates the registry, reads each tenant's index,
  and re-folds every candidate's per-aggregate stream (`AggregateId` always set) before acting — a stale index
  entry whose stream has resumed contributes nothing. The `StreamsController` null-`AggregateId` 400 rejection
  (verified against submodule `6a8f3866`) is therefore no longer load-bearing for reminders; the tenant-wide
  null-aggregate scan is retired.
- **On by default, no hand configuration (AC #3).** `WorksRecoveryOptions.Tenants` and its AppHost
  `Works:Recovery:Tenants` forwarding are removed; `ReminderReconciliationService` runs whenever
  `RunReconciliationOnStartup` (default `true`). The whole pass stays crash-safe by idempotency (deterministic
  `DateReminderName`/correlation ids), not checkpoints.
- **Catalog unchanged.** The index and registry records are host-edge STJ, not `[PolymorphicSerialization]` types;
  `WorkItemV1Catalog.Count` stays **37** and the golden corpus is byte-compatible.

## Story 4.7 — Live Domain-Event Consumption and Cascade Recovery

Story 4.7 verified the checked-out subscription and publisher surfaces at EventStore commit `440ff4c`. The
workspace intentionally advanced the `references/Hexalith.EventStore` pin from `c6b72caa` to `440ff4c` during the
story; the delta is hot-reload readiness/diagnostics test infrastructure that Works does not consume, so every
surface documented below is unchanged from the original `c6b72caa` verification.

- **Tenant topic composition must be resolved explicitly.** Without an override, the publisher composes
  `{tenantId}.work.events`, while `EventStoreDomainEventsOptions.ForDomain("work")` subscribes to the static
  `work.events` topic. The AppHost therefore injects
  `EventStore__Publisher__TopicOverrides__work=work.events` into EventStore. Works continues to use the existing
  shared `pubsub` component and declares one programmatic subscription through `MapSubscribeHandler`.
- **The generic processor shares the case-insensitive payload reader.** `EventStoreDomainEventProcessor` binds
  both EventPersister's PascalCase bytes and camelCase Web compatibility inputs through
  `EventStorePayloadSerialization.Options`. That file is byte-identical between `b43e963403efa848eda9621b5e3e7e446c7faa2d`
  and the current pin `c61739206fd89619b7d29dfb0812225a234066bb`, so the withdrawn "not Web-JSON compatible /
  silently binds a zero-valued record" characterization was wrong at both revisions rather than something the pin
  bump changed. Works retains its host-local endpoint for stricter tenant/work-item/aggregate identity validation
  and its explicit terminal handling of malformed or unhandled deliveries; casing compatibility was never the
  reason for the local processor.
- **Durable markers provide restart dedup, not broker ordering.** The Dapr marker key includes the configured
  topic, subscription route, and EventStore message id. A completed marker makes a redelivery `Duplicate` across
  host restarts. Handler failures do not write completion and remain retryable; after handlers finish, marker
  completion is the durable side-effect boundary. Dapr pub/sub remains at-least-once and unordered, so target
  commands retain deterministic ids and aggregate `Handle` remains authoritative.
- **Stream re-reads remain per aggregate.** Child-completion recovery reads the child stream for its parent
  reference, then reads that parent stream to rebuild current await conditions. Cascade discovery reads the
  parent stream and consults each child's persisted roll-up only for terminal status; parent roll-up totals with
  child contributions are explicitly unavailable and are never used for cascade decisions.
- **Command payload binding uses the same shared reader contract.** The pinned EventStore aggregate adapter
  deserializes inner command payloads with `EventStorePayloadSerialization.Options`, so both case-preserving CLR
  property names and camelCase Web inputs bind correctly. `EventStoreAggregate` is also byte-identical across the
  two revisions above, so the withdrawn case-sensitive command-reader characterization was never true of either
  pin. Works recovery submissions keep their existing case-preserving writer form for consistency with the
  options-free persisted event convention.
- **Internal Works gateway calls use Dapr authentication.** Under Aspire, the EventStore gateway client targets
  `DAPR_HTTP_ENDPOINT` and applies `AddEventStoreDaprServiceInvocation("eventstore")`; EventStore explicitly
  allow-lists `works` through `Authentication:DaprInternal:AllowedCallers`. Direct configured HTTP remains only
  the fallback for hosts composed without Dapr. This gives child-completion reads, cascade reads, and generated
  commands the supported `dapr-caller-app-id=works` system identity.
- **Readiness is dependency-specific.** Aspire's EventStore and Works resources expose `/alive` health checks so
  their HTTP processes and Dapr app channels are established before tests submit commands. The live lanes then
  wait for the `dapr-actor-placement` entry inside EventStore's `/ready` response and one Works app-health probe
  interval. They intentionally do not require overall `/ready`=200 because the same response contains the
  independent `projection-delivery-writer-protocol` cutover, which can remain unhealthy while aggregate command
  actors are ready.
- **The live gate passes at this revision.** After explicit Release builds of both suppressed EventStore hosts,
  all three Tier-3 lanes completed without skips. The reactor lane proved the shared `work.events` delivery path
  for both translators: child completion resumed its awaiting parent, parent cancellation dispatched its first
  descendant, and a fresh AppHost replayed the durable incomplete checkpoint to cancel the outstanding
  descendant with exactly one terminal event on each.

No Story 4.7 subscription, source, index, or checkpoint type enters the durable polymorphic catalog;
`WorkItemV1Catalog.Count` remains **37** after the prior correct-course addition.
