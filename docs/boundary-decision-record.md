# Works Boundary Decision Record

<!-- EventStore-current-source: c5d1c280d8c1c1a207cd159d817f614e93c93130 -->

Current source characterization follows the EventStore revision in the marker above and its consumed checkout.
The architecture lane verifies this binding. Published package producers and historical proof revisions below retain their separate scope.

**Status:** Accepted — Epic 1 (Story 1.4).
**Tracks:** FR-21 (reference siblings, never copy them), FR-22 (expose module ports as
abstractions), FR-23 (produce the boundary decision record), AR-18 (ports realization), AR-19
(boundary decision record). Aligned with NFR-5 (domain purity) and NFR-11 (natural-language-is-data
boundary).

> **Platform cutover — 2026-10-20:** Story 4.9's pre-cutover gate passed all AD-20 R1–R11 persisted, negative, restart and complete-fleet rollback proofs on public EventStore **3.126.0**, before removal. Works now uses the canonical SDK host and owns only domain adapters. Hexalith.Platform owns topology, HTTP defaults, subscriptions, readiness and telemetry. The exact pre-cutover ledger is in `evidence/story-4-9/pre-cutover-2026-10-20`; the post-cutover evidence and historical rollback command are recorded in `docs/platform-hosting-cutover.md`.

> **Verified cutover — 2026-10-21:** The [complete post-cutover gate](../_bmad-output/implementation-artifacts/evidence/story-4-9/post-cutover-2026-10-21/README.md) passed all AD-20 R1–R11 proofs (44/44), final source/runtime closure and fresh complete-fleet restore followed by retained-host query/receipt replay. Works owns its canonical SDK executable and domain adapters; Platform owns topology and generic hosting. The exact historical rollback archive remains available through Platform's verifier. Earlier failures and source-drift refusals remain preserved, and the approved temporary watch quota was restored. This verifies disposable synthetic topology; production preview admission remains refused and story review remains separate.

> **Review-qualified cutover — 2026-10-21:** The [current full and CI qualification](../_bmad-output/implementation-artifacts/evidence/story-4-9/post-cutover-review-1-2026-10-21/README.md) passes all 44 proof kinds and complete rollback in each profile, including final immutable closure and the actual future-date callback with periodic workers disabled. CI selects Release/public-package consumers; full additionally checks source Debug. Exact owner overlays, the local Docker Desktop explicit-port qualification adapter and every rewritten owned-fixture request are preserved. All TCP/PING and domain assertions remain active; no hosted Actions or production admission is claimed. Both quotas were restored. The earlier packet below remains immutable historical evidence; final story review remains separate.

## Purpose

Works is the **coordination** bounded context. It owns the facts required to coordinate work —
the obligation, the executor binding (a reference value object), effort and schedule, parent/work
references, the tenant-scoped identity, an optional conversation correlation, and status — and it
references every other concern by stable ID, resolved on demand. It never denormalizes a copy of a
sibling's data into its own events or state.

This record makes the boundary **falsifiable**: it enumerates, per sibling module, what Works
**owns** versus what it **references**, explains **why**, and names the four capability **seams**
that are deliberately deferred and are **not v1 behavior**. Architecture-fitness tests assert this
file exists and mentions every module and seam below.

## Ownership principle

Works owns *coordination facts* only. Each sibling module remains the **system of record** for its
own concern. A reference is a stable identifier or correlation ID that is resolved on demand — never
a denormalized copy. This is the FR-21 no-copy rule: copying sibling data would create a second
source of truth that drifts, leak another context's payload into Works events, and couple Works to
schemas it does not own.

## Owns vs. references, per module

| Module | Works references (system of record stays in the sibling) | Works does **not** own |
| --- | --- | --- |
| **Parties** | Executor / actor **identity** via `PartyId` (a Works-owned reference value object holding the stable party id). | Party profiles, display names, contact details, party lifecycle. |
| **Conversations** | Optional **dialogue** correlation via `ConversationCorrelationId`, supplied at creation or linked later through `LinkConversation` → `ConversationLinked`. | Conversation content, messages, comments, threads, titles, participants, and relink policy outside the stored correlation fact. |
| **EventStore** | **Persistence and runtime SDK**: event append/replay, envelopes, aggregate identity (`AggregateIdentity`), Dapr ETag concurrency, projection/rebuild lifecycles, standard service defaults, and generic delivery/query/subscription plumbing. | The storage substrate, envelope/metadata shape, concurrency mechanism, rebuild orchestration, or platform runtime plumbing. |
| **Tenants** | **Isolation**: every Works fact is tenant-scoped via `TenantId`; cross-tenant parent references are rejected at the writer. | Tenant registration, tenant profile, tenant lifecycle/onboarding. |
| **Commons** | **ID generation** and shared primitives — supplied at the edge, outside the aggregate. | The identifier-generation algorithm itself; the kernel never generates IDs. |
| **PolymorphicSerializations** | **Payload (de)serialization** of event/command contracts in the wider ecosystem. | The serialization registry/policy. Works-owned reference value objects (`PartyId`, `ExpectationReference`) are plain `System.Text.Json` records and need no polymorphic registration. |

### Why Works owns coordination facts but not these concerns

- **Identity → Parties.** Who performs work is a party fact. Works stores only the reference
  (`PartyId`); it never copies a party profile, so a renamed or deactivated party is resolved live
  from Parties, not from a stale Works copy.
- **Dialogue → Conversations.** The conversation around a work item is dialogue, owned by
  Conversations. Works carries only a correlation id, so message content never lands in a Works event
  and the natural-language-is-data boundary (NFR-11) holds. The first correlation may be supplied at
  creation or added later; repeating the same ID is a no-op and a conflicting relink is rejected.
- **Persistence → EventStore.** Append/replay/envelopes are an infrastructure concern. The Works
  kernel stays pure (NFR-5): no clock, generated id, I/O, or Dapr in command handling or replay — the
  EventStore domain-service SDK and platform-owned host compose that machinery outside the kernel.
- **Isolation → Tenants.** Tenancy is the isolation boundary. Works scopes every fact by `TenantId`
  and treats a foreign-tenant parent as a distinct reference rather than coercing it.
- **ID generation → Commons.** Deterministic replay forbids the kernel from minting ids; ids are
  supplied at the edge via Commons so `Handle`/`Apply` stay deterministic.
- **Payload serialization → PolymorphicSerializations.** Cross-module payload polymorphism is owned
  centrally; Works does not re-implement it.

Counter-metrics **SM-C1** (do not grow the kernel) and **SM-C2** (do not over-fit to deferred themes)
are binding: this story ships the named seams, not the machinery behind them.

## Conversation-link ownership and lifecycle-neutral semantics

`ConversationCorrelationId` is a Works-owned reference value object whose target remains owned by
Conversations. `CreateWorkItem` and `SpawnChild` may supply it at creation. Story 1.5 implements
`LinkConversation` and `ConversationLinked` so an existing non-terminal item without a link can record
one later. The first accepted link is authoritative and replayable; Works never resolves or copies
conversation content while handling the command.

- Repeating the authoritative correlation returns `DomainResult.NoOp` in any lifecycle state,
  including after terminal closure.
- Supplying a different correlation after one is authoritative emits
  `WorkItemConversationLinkRejected` and preserves the original link.
- Adding a first link to an unlinked terminal item emits
  `WorkItemTransitionRejected(currentStatus, "LinkConversation")` and preserves closure.
- Handling the command before creation is domain-rejected and never creates either resource.

This additive change takes the durable polymorphic catalog from 37 to **40** types: 15 commands,
15 success events, and 10 rejection events. Existing payload bytes and type names remain unchanged.
The reader/writer direction, nullable read-model rollout, unknown-type behavior, and reader-first
deployment order are bound by the VAL-H11 compatibility matrix in
`docs/eventstore-api-surface-constraints.md`.

## Hosting and runtime ownership

Works owns the pure contracts, aggregate behavior, projections/query transformations, mechanical
reactor translations, tests, and the canonical minimal EventStore domain-service executable. That
executable uses `AddEventStoreDomainService(...)` and `UseEventStoreDomainService()` and does not fork
platform capabilities.

The designated platform/host repository — **`Hexalith.Platform`**, owned by the Platform
Maintainer (Hexalith), per architecture AD-20 (2026-09-06) — owns Aspire composition,
ServiceDefaults, Dapr components, health and telemetry, generic command/event delivery,
projection/query storage plumbing, subscriptions, reminders, and recovery orchestration. Story 4.9
migrates the historical Works-owned hosting topology only after the platform topology has
equivalent passing evidence. Migration may begin; a current host asset may be removed only after
the corresponding AD-20 migration-matrix row is green in the `Hexalith.Platform` conformance lane.

**Provenance of the two paragraphs above (recorded 2026-09-08).** The `Hexalith.Platform` ownership and the
"migration may begin" clause were both settled by the **2026-09-06 architecture gate** (decisions AD-01…AD-25;
AD-20 names the host repository and unblocks Story 4.9). They were written into this record while Story 4.8 was
editing the same file for its mTLS work, so they **restate an already-decided outcome** — they are not Story 4.9
implementation work, and Story 4.8 absorbed none of Story 4.9's scope. Human ruling 2026-09-08: keep them here,
with this provenance note so the text cannot be mistaken for 4.9 execution.

## Preserved deferred seams — explicitly NOT v1 behavior

These four capabilities have **named seams** so future themes attach without changing the kernel's
ownership model. None of them is implemented or wired in v1.

1. **AI-inferred expectations (expectation resolution).** Seam: `IExpectationResolver` +
   `ExpectationReference` (`Hexalith.Works.Contracts.Ports`). v1 ships **only** a no-LLM literal
   resolver (`LiteralExpectationResolver` in Server) that interprets nothing. The interpreted
   `Expectation` is resolved on demand and **never** stored in an event, command, or replayed state.
   This port is the future **prompt-injection boundary** (NFR-11): interpreted natural language is
   data, never trusted input. **Not v1 behavior:** any AI/LLM interpretation.

2. **Executor routing / selection (and escalation).** Seam: `IExecutorRouter`
   (`Hexalith.Works.Contracts.Ports`), **abstraction only**. No implementation, routing/selection
   engine, scoring model, or escalation policy ships in v1 — a fitness test asserts zero
   implementers in the Works kernel. This is **Theme 4**, which will also own claim/queue semantics
   (single-claim-wins, the tenant "what's next" queue). **Not v1 behavior:** routing, claiming queued
   work, escalation.

3. **Cost meter / spend governance.** Seam: a cost-ready `Meter` reuse, **Theme 5**. Cost metering,
   spend caps, and cost burn-down/governance are not modeled in v1; no cost type is referenced by the
   kernel. **Not v1 behavior:** any cost meter or spend-governance enforcement.

4. **Trust / security hardening.** Seam: carried-not-enforced security facts — signed links, step-up
   authentication, audit query, and `AuthorityLevel` enforcement — **Theme 6**. `AuthorityLevel` is
   carried on `ExecutorBinding` today but is **not enforced**. **Not v1 behavior:** authority
   enforcement, signed links, step-up auth, or audit querying.

## Notes and cross-references

The Story 4.1–4.8 notes below preserve what each completed story proved at the time. References to a
Works-owned AppHost, ServiceDefaults, or host-edge runtime describe the historical topology and are
superseded by the approved 2026-09-05 boundary plus Story 4.9. Catalog counts of 37 are likewise
point-in-time evidence before Story 1.5's additive three contracts; the current implemented count is 40.

- Dependency direction is enforced by fitness tests: `Contracts` references only
  `EventStore.Contracts`; `Server`, `Projections`, and `Reactor` reference inward to `Contracts`
  only. The new `Ports/` (Contracts) and `Resolvers/` (Server) folders add no forbidden reference.
- **Story 4.1 (uniform executor binding).** Every executor — system agent, internal user, external
  party — is represented by one `ExecutorBinding` (`PartyId` + `Channel` + `AuthorityLevel`); the
  cases differ only by field values. Party identity is a **reference** (`PartyId`), `Channel` is an
  **interaction medium**, `AuthorityLevel` is **carried-not-enforced**, and there is **no
  executor-kind branch discriminator** in Works contracts or domain behavior (a fitness test asserts
  zero branching on channel/authority/party). The contract-level read model
  `WorkItemExecutorBindingView` (`Contracts/Models`) exposes only this binding data so future Party
  chips render any executor uniformly; "bot / person / external" is resolved outside the kernel.
- **Story 4.2 (assign / reassign / hand off).** Assign, reassign, human↔system hand-off, requeue, and
  claim are **one uniform vocabulary** — `AssignWorkItem` (bind/rebind/hand-off), `QueueWorkItem`
  (return-to-pool), `ClaimWorkItem` (`InProgress` entry) — with **no** executor-kind-specific command or
  event (`HandoffToBot`, `ReassignToHuman`, `WorkItemHandedOff`, …); reassignment and hand-off differ only
  by `ExecutorBinding` field values. Hand-off is symmetric in both directions and **auditable** through the
  ordered raw-act event history (each `WorkItemAssigned` is a distinct act, never collapsed). At Story 4.2
  completion the catalog stayed 37 — that story added no contract type (a fitness test asserts both the no-kind-vocabulary
  rule and the catalog size).
- **Story 4.3 (claim queued work / single-claim-wins).** Single-claim-wins is the **composition** of the
  Works kernel lifecycle (`Queued/Assigned → Claim = Accept(InProgress)`; all else `R`) and the
  **EventStore-owned ETag-backed atomic actor-state save** (see the EventStore row above — Works references
  persistence/concurrency, it does **not** own the mechanism): two claims may observe the same persisted
  actor state and independently produce domain candidates with the same state-changing payload ordinal.
  The shared persisted ETag, not that payload ordinal, permits one atomic save; the loser retries and
  re-handles against the now-`InProgress` state to the existing
  `WorkItemTransitionRejected(InProgress, "Claim")`. Works adds **no**
  claim-eligibility, routing, escalation, ranking, or AI-decision type, and **no** new
  `ClaimRejected`/`ConcurrencyRejected` rejection — claim is **unconditional** in v1 (any tenant Executor
  may claim a queued item; eligibility is the deferred Theme-4 executor-routing seam `IExecutorRouter`
  above), `AuthorityLevel` stays carried-not-enforced, and the catalog stayed 37 at Story 4.3 completion
  (fitness-asserted). The
  claimable pool itself is a read projection (Story 4.4), not an authoritative queue aggregate.
- **Story 4.4 (resolve the tenant's what's-next queue).** The tenant "what's next" queue is realized as a
  **pure read projection + query-shaping** over Works' own events (`WhatsNextQueueProjection` +
  `WhatsNextItem` in `Projections`/`Contracts`), **not** an authoritative queue aggregate (AR-10/B1, the
  EventStore row above — Works references persistence, it does not own a second source of truth). The
  eligible set is `{Queued, Assigned}`; ordering is **Priority → earliest Due Date → identity** (absent
  priority/due-date last, so an item with **neither** sorts last). Tenant key-scoping and the pure
  **query-side authorization** filter (`WhatsNextQueryAuthorization`, mirroring `Hexalith.Projects`
  `ProjectQueryTenantFilter`) are **distinct controls** (defense-in-depth, D2/NFR-1); `AuthorityLevel`
  stays carried-not-enforced (no `IExecutorRouter` impl — the Theme-4 routing/eligibility seam above is
  still abstraction-only). Works adds **no** routing/eligibility/escalation/ranking type and **no** durable
  catalog type — the catalog stayed 37 at Story 4.4 completion (fitness-asserted) and the golden corpus is byte-compatible. The
  notifier requirement is met by **referencing** the substrate seam
  `IProjectionChangeNotifier.NotifyProjectionChangedAsync("works-whats-next", tenantId, …)` (EventStore
  owns the SignalR broadcast); the live query/notifier runtime is the deferred Aspire wiring (Stories
  4.5/4.6), and **no web shell, DataGrid, MCP, chatbot, or email surface** ships in v1.
- **Story 4.5 (historical command/event pipeline proof under Aspire).** The runnable host
  (`src/Hexalith.Works`) and former Works AppHost were an **adapter-edge runtime proof only** — they consume the deferred Story 4.4 seams,
  they do not move behavior into the kernel. The boundary stays exactly as the EventStore row above states:
  **EventStore owns** persistence, ETag-backed concurrency on the atomic actor-state save,
  envelopes/metadata, and the public command/query gateway (`/api/v1/commands`, `/api/v1/queries`);
  **Works owns** only domain behavior (the
  pure static `WorkItemAggregate`, wrapped at the edge by `WorkItemEventStoreAggregate : EventStoreAggregate<…>`
  for discovery) and the read-model transformations (the pure `WhatsNext`/roll-up projections, persisted by a
  host-edge `/project` adapter + `WhatsNextQueryHandler`). The kernel (`Contracts`, `Server`, `Projections`,
  `Reactor`) remains free of Dapr, ASP.NET hosting, EventStore runtime, clocks, I/O, and logging
  (fitness-asserted); at that point the host was the **only** Works source project allowed those adapters.
  Story 4.9 removes that exception and moves generic plumbing to the platform. `AuthorityLevel`
  stays carried-not-enforced, there is still no `IExecutorRouter` implementation, and **no** production UI, MCP,
  chatbot, email, routing, cost, or security-hardening surface is composed. The catalog stayed **37** at
  Story 4.5 completion and the
  golden corpus is byte-unchanged (no durable type added by the adapter/query/read-model code).
- **Story 4.6 (historical reminder and reactor recovery proof).** Date-based resumes and
  terminal-cascade restart recovery are **adapter-edge orchestration**, not new kernel behavior. The
  historical Works host implemented deterministic reminder
  naming, Dapr actor reminder registration/fire handling, startup reconciliation over configured tenant streams,
  terminal-cascade delivery sequencing, and bounded checkpoint records in the shared state store. **EventStore
  still owns** persistence, envelopes, optimistic concurrency, command submission/status, stream reads, and
  projection/read-model substrate; every reminder resume and cascade target command is submitted through the
  EventStore command gateway and round-trips through `WorkItemAggregate.Handle` for acceptance/no-op/rejection.
  The kernel (`Contracts`, `Server`, `Projections`, `Reactor`) remains free of Dapr actors, clocks, logging,
  network/filesystem I/O, command gateways, and checkpoint stores (fitness-asserted). The current recovery scan
  is bounded to configured tenants because the EventStore/Dapr surfaces do not expose cross-tenant enumeration;
  this is documented as a substrate limitation, not hidden in the domain. No reminder/checkpoint/read-model
  runtime type enters the polymorphic catalog, so the catalog stayed **37** at Story 4.6 completion and the golden corpus remains
  byte-compatible. **Recovery trigger decision (SUPERSEDED by Story 4.8, see below):** at 4.6, reminders/resumes
  were reconciliation-on-recovery only — registered/reissued when the host (re)starts and scans pending date
  awaits — not registered at suspend time. Story 4.8 reverses this per architecture decision C2 (reaffirmed by the
  2026-07-21 correct-course): a reminder is now registered at suspend time on the live event stream, and recovery
  discovers pending awaits from a durable index rather than a hand-configured tenant scan. The gated
  Tier-3 Aspire lane (`WorksReminderRecoveryPipelineSmokeTests`) proves this restart→reissue→exactly-once-resume
  path; because the gateway stream-read route requires a per-aggregate id (no tenant/domain-wide enumeration), the
  lane reissues through the adapter's own deterministic `DateResume` factory rather than tenant-wide
  auto-discovery, and the reconciliation decision logic is proven deterministically by
  `DateReminderRecoveryRuntimeTests`.
- **Story 4.7 (live reactor consumption and durable cascade discovery).** Works uses the EventStore SDK's
  subscription registration, handler contracts, and durable Dapr marker store, but maps an equivalent host-local
  endpoint instead of the SDK's generic processor. Both processors use case-insensitive Web reader options that
  bind options-free PascalCase persisted bytes and camelCase compatibility inputs. That held at the
  historical EventStore revision `c61739206fd89619b7d29dfb0812225a234066bb` and equally at the earlier
  `b43e963403efa848eda9621b5e3e7e446c7faa2d`, whose processor and aggregate sources are byte-identical: the
  withdrawn zero-valued-binding characterization was wrong at both pins, not a behaviour the bump changed. The
  local processor remains for
  stricter envelope/payload tenant and aggregate identity checks and explicit terminal malformed/unhandled
  delivery behavior; terminal skips are acknowledged, handler failures remain retryable, and a completed marker
  makes redelivery a duplicate. EventStore receives
  `EventStore__Publisher__TopicOverrides__work=work.events`, so every tenant's Works event reaches the one
  programmatic `pubsub` subscription without introducing a second component. The `WorkItemCompleted` handler
  re-reads the completed child's aggregate stream to find its same-tenant `WorkItemCreated.Parent`, then replays
  that parent's stream to rebuild only its current suspended await conditions; mismatch or read failure returns
  no candidate. The unchanged pure translator emits `ResumeWorkItem`, and the EventStore gateway plus aggregate
  `Handle` remain the acceptance and idempotency boundary. Internal recovery reads and commands route through
  the Works Dapr sidecar (`DAPR_HTTP_ENDPOINT` + EventStore service invocation), and EventStore explicitly
  allow-lists the `works` caller. This preserves the supported Dapr-internal authentication boundary instead of
  bypassing authorization with direct host calls. Recovery command payloads retain CLR property casing, while
  the aggregate adapter's shared case-insensitive reader also accepts camelCase compatibility input.

  **The command-payload casing change is not new to Story 4.7's own command paths** — `CascadeCommands.BuildSubmission` (new this
  story) and `DateResume.BuildSubmission` (Story 4.6, unchanged signature, only its serialization options
  changed here) both switched from `JsonSerializerDefaults.Web` to options-free serialization. Because
  `DateResume.BuildSubmission` is also called from the pre-existing `DateReminderReconciler` and
  `DateReminderActor` (Stories 4.5/4.6, not touched by this diff), this means **date-resume command submissions
  from those already-shipped stories did not match the then-characterized command-payload reader contract — but
  that characterization was itself wrong. `EventStoreAggregate` binds command payloads through the shared
  case-insensitive reader at both `b43e963403efa848eda9621b5e3e7e446c7faa2d` and
  `c61739206fd89619b7d29dfb0812225a234066bb`, so no live casing defect ever shipped from those stories. Case-preserving
  output is retained for consistency with the options-free persisted event convention rather than as a
  reader-compatibility requirement. No other consumer of either `BuildSubmission` method was found
  (verified by repo-wide search); the change remains confined to these two command-payload builders.

  The live AppHost gate distinguishes host liveness from command-path readiness. Aspire waits on `/alive` for
  both HTTP hosts, then the tests inspect EventStore's `/ready` response until the load-bearing
  `dapr-actor-placement` check is healthy and allow one Dapr app-health probe interval for Works. EventStore's
  aggregate `/ready` status may remain 503 because it also includes the independently operated
  `projection-delivery-writer-protocol` cutover; that unrelated check does not gate aggregate commands.

  Incomplete cascade checkpoints are discoverable after restart through one ETag-updated singleton index in the
  same `statestore`; Dapr key enumeration is neither assumed nor emulated. An incomplete identity is indexed
  before its checkpoint write, while a completed identity is removed only after the completed checkpoint is
  durable, making crash windows fail safe. Checkpoints persisted before Story 4.7 have no index entry and are not
  auto-discovered; this is acceptable because the dispatcher had no production caller before the live
  subscription added by this story. The checkpoint record shape, key, replay signature, false-on-missing result,
  and attempt-before-submit ordering remain unchanged.
- **Story 4.8 (register and reconcile date reminders durably).** This story supersedes the Story 4.6 M1
  reconciliation-only posture per architecture decision C2 (reaffirmed by the 2026-07-21 correct-course). A durable
  Dapr date reminder is now registered at **suspend time** on the live `work.events` subscription (Story 4.7's
  surface): a new `IEventStoreDomainEventHandler<WorkItemSuspended>` re-folds the suspended aggregate's
  per-aggregate stream through the pure `PendingDateAwaitProjection` (derived from the current pending set, never a
  raw event in isolation) and schedules one reminder per pending `DateReached` await, so a date-suspended item
  resumes when the date fires with no host restart. The subscription — not the poller-latency `/project` dispatch —
  is the steady-state trigger, mirroring Story 4.7's other live translators. Recovery no longer needs per-tenant
  hand configuration: the `/project` dispatcher maintains a durable **pending-date-await index** (a per-tenant
  index document plus one well-known tenant-registry document, both plain host-edge `System.Text.Json` read models,
  so Story 4.8 added no durable catalog type and the current catalog remains **40**), and the startup reconciler discovers tenants from that registry, reads each
  tenant's index, and re-folds each candidate's per-aggregate stream for truth before acting — the index is
  discovery, the stream is authoritative, so a stale entry can never cause a wrong reissue. The removed
  `Works:Recovery:Tenants` gate (and its AppHost forwarding) means reconciliation runs on by default
  (`RunReconciliationOnStartup`). Every stream read carries an `AggregateId`; the tenant-wide null-aggregate scan
  the gateway 400-rejects is retired. Idempotency is unchanged: deterministic `DateReminderName`/correlation ids
  make suspend-time registration and recovery reissue converge to a single accepted `WorkItemResumed`. The
  `WorkItemAggregate.Handle`/reactor kernel stays clock-free (AC #4, fitness-asserted).
  **Accepted limitation — append-only date-history index/registry growth (documented 2026-09-05, not fixed):**
  `PendingDateAwaitTenantIndex.LastSequences` gets one permanent watermark entry per work item whose authoritative
  full replay ever contained a `DateReached` await, even after that await is cleared, with no pruning, and
  `PendingDateAwaitTenantRegistry.Tenants` is append-only forever (a tenant is never removed once it has had a
  pending date await). This is the mirror-image gap of the cascade-checkpoint index, which got a
  `CascadeCheckpointIndexStaleAfterHours` retention knob (`WorksRecoveryOptions`, consumed and clamped by
  `CascadeRecoveryReconciler`) precisely because unbounded growth there was judged worth fixing; the pending-
  date-await index has no analogous mechanism. Accepted as a documented tradeoff rather than building pruning
  now: both documents are read wholesale (no Dapr key enumeration), so the cost of the growth is O(n) storage
  and one extra cheap read per stale registry tenant on recovery — not a correctness or availability risk at
  the item/tenant volumes this module currently serves. Revisit if/when tenant or work-item cardinality makes
  either document's read/write cost material; a stale-after retention knob mirroring
  `CascadeCheckpointIndexStaleAfterHours` is the natural fix.
  **2026-09-08 review remediation (bounds the growth above, and four related decisions).**
  (1) The index write is now **guarded by authoritative full-replay history**: `/project` touches the pending-date-
  await index only when the current fold holds pending date awaits or the replay itself contains a historical
  `DateReached` suspension. It deliberately does not consult persisted index state to decide whether a tombstone
  is needed, because that read is not atomic with an older dispatch's later update. This bounds
  the growth documented above to items that ever held an await, and removes the singleton-key write contention
  that could exhaust `ReadModelWritePolicy`'s bounded retry and turn an ordinary dispatch into a 500. Tombstone
  semantics are unchanged for items that ever held an await; an item that never held one simply has no entry,
  and a stale index entry remains harmless because the stream is truth.
  (2) Recovery now **degrades per candidate, not per tenant**: one unreadable work-item stream no longer
  discards the awaits already collected for its tenant. `PendingDateAwaitScanIncompleteException` carries a
  second `FailedCandidateCount` field alongside the unchanged `FailedTenantCount`, and the pass is still
  signalled incomplete so it is retried.
  (3) The tenant id `tenants` is **reserved and refused at the host edge** (`/project` and the `work.events`
  processor): its index key would be byte-identical to the well-known registry key and would silently disable
  reminder recovery for every tenant. The durable registry key is deliberately unchanged — no migration.
  (4) Index/what's-next **removal is reachable again** when the roll-up yields no model despite real
  (non-rejection) state evidence, using the delivered stream's own watermark against the persisted
  `LastSequences` entry. Empty and rejection-only replays still write nothing, as they must.
  (5) A permanently undecodable state-affecting event now **parks its aggregate** after
  `Works:Projection:MaxUndecodableEventDispatchesBeforeParking` (default 5) consecutive failures on the same
  sequence: the dispatch is acknowledged with a distinct error log and recorded at
  `projection:works:parked:{tenantId}:{workItemId}`, so the projection poller stops redispatching it forever.
  Decoding stays fail-closed for every attempt inside the budget, so transient causes still retry.
  (6) Parking is a **terminal recovery disposition** today. Recovery skips a parked pending-date-await candidate
  without reading its stream, so it cannot recreate a missing reminder registration or issue an overdue resume.
  Parking does not cancel an already durable steady-state reminder, which may still fire. If registration is
  absent or lost, restart reconciliation cannot repair it until a separately delivered operator remediation/
  unpark capability exists. Warning 4607 makes each skipped item visible; the operator guide documents the
  corresponding response together with scan and scheduling warnings 4604/4605/4606/4608/4609.
- Hexalith libraries are consumed as `ProjectReference` to the checked-out sibling source, never as
  NuGet `PackageReference` (see `CLAUDE.md`). Story 1.4 introduced no new sibling reference.
- EventStore API-surface constraints from Story 1.1 (ETag-based concurrency and
  checkpoint-per-aggregate rebuild) are unchanged by this story; shared relationship-aware rebuilds
  additionally require capture-through-Commit delivery quiescence/fencing.
- Sources: `epics.md` FR-2/FR-21/FR-22/FR-23, NFR-5/NFR-11, SM-C1/SM-C2; `architecture.md` AR-18
  (ports realization), AR-19 (boundary decision record), Architectural Boundaries, Deferred Decisions.

### Story 4.14 runtime projection boundary and technical-module remediation — 2026-10-14

EventStore owns generation fencing, persistence, checkpointing and selected reads for the opt-in `works-runtime-v3` family; Works owns domain folds, runtime document content and query authorization. Parties.Client `1.2.1` supplies the current Party binding verdict for Executor reads; Works does not equate a user or actor identifier with a Party identifier. One startup mode selects one projection writer and matching readers. The pre-cutover keys and host remain available for complete store-and-fleet restore; no production activation is part of this decision.

EventStore owns durable delivery-conflict evidence, selected-generation readiness, evidence capacity, and explicit repair through its fenced epoch state; Works adds no quarantine writer or durable catalog. Platform owns opt-in independently authenticated JWT service-reader admission and exact signed query scope, preserving current actor registry, tenant/RBAC, and legacy Dapr semantics. Parties owns the authoritative source fold, current human binding, finite policy and custody checks; Works consumes the published Parties client at its host edge.

The Story 4.14 EventStore pin was local candidate `3.120.1-story414.20261014.10` (historical; later Story 4.15 selections are recorded below), with isolated package-only consumer proof and retained hashes. At that verification, test-only Platform.Identity used local candidate `1.0.0-story414.20261014.5` in Release and its sibling project in Debug; the resumed Story 4.15 graph is recorded below. No public release is claimed. The synthetic authority test links existing non-packable Parties runtime source unchanged and keeps published client/Contracts `1.2.1`; it does not provision a real authority. The legacy-default writer and complete pre-cutover restore/replay remain required until the independent Story 4.16 and AD-28 gates are satisfied. See [sdk-projection-query-adoption.md](sdk-projection-query-adoption.md) for configuration and evidence limits.

### Story 4.15 reminder/process boundary — 2026-10-15

The exclusive opt-in recovery runtime delegates mechanics to EventStore while Works retains authoritative stream context, exact await matching and pure decisions. Explicit expiry witnesses are additive durable fields; no host clock or inferred TTL enters the kernel. Scheduled expiry matches its current witness; distinct authenticated cascade authority admits delegated expiry. SDK immutable effect coordinates use envelope positions and receipts before checkpoints. The producer correction, local-only provenance, synthetic authority and complete restore requirements are recorded in [sdk-reminder-process-adoption.md](sdk-reminder-process-adoption.md). The legacy runtime remains default pending Story 4.16 and production gates.


Story 4.15 review iteration 1 retains discovery incompleteness and original bounded effect plans exclusively in the SDK. Healthy tenant discovery proceeds while another family is degraded, and completed available scans restart at the catalog head. Offboarded scopes are excluded. The source CAS retains the original plan before trusted submission and clears it with the checkpoint after all receipts, including no-op receipts without domain events. Works does not own a process plan, checkpoint, receipt, or periodic writer. The selected fresh local producer is `3.122.0-story415.20261015.4`; archived `.1`/`.2`/`.3` proof remains historical. Five-child fan-out and the coupled budgets, contextual byte/deadline limits, strict V1 decoding and shared gateway URI are specified in [sdk-reminder-process-adoption.md](sdk-reminder-process-adoption.md).

Story 4.15 review iteration 2 selects fresh immutable local producer `3.122.0-story415.20261015.6`. SDK reminder discovery now follows indexed convergence under a small page/time budget with durable continuation and incompleteness, retries omitted scopes on fresh sweeps, and admits healthy later tenants despite publication failure. Authoritative convergence prevents terminal candidates occupying scarce capacity; pending passes immediately degrade readiness. The SDK preserves the original reminder constructor/deconstruction ABI, binds every retained effect into the private plan digest, and durably audits direct origin-binding denial through a bounded rolling digest/count record before acknowledgment. Works adds direct expiry-child mapping and reciprocal relationship quarantine evidence. Existing `.4` proofs remain historical; current commands, package/archive/source hashes, runtime and complete-restore evidence are recorded in [iteration-2 implementation evidence](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-iteration-2/implementation.md). Publication and production gates remain separate.

Story 4.15 review iteration 3 selects immutable local producer `3.122.0-story415.20261015.7`. SDK partial-availability membership excludes unvalidated tenant tombstones while healthy scopes proceed; owned reminder discovery proofs fence stale progress, completion and cleanup. Indexed and initial discovery receive separate bounded slices with durable continuation, and local pruning requires uninterrupted complete coverage across replicas. The common SDK boundary audits binding and adapter authorization denials once before empty 403 responses, returning empty retryable 503 when audit is unavailable. Works preserves all six original CLR contract shapes, validates historical witnesses before later facts can hide corruption, and validates final writer composition after customization and at startup. Current hashes, commands, compatibility probes and persisted proof are recorded in [iteration-3 implementation evidence](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-iteration-3/implementation.md). All earlier candidate archives and evidence remain historical and immutable; publication and production gates are separate.

Story 4.15 review iteration 4 selects immutable local producer `3.122.0-story415.20261016.8`. SDK discovery bounds complete tenant-family acquisition and coalesces outstanding registry/tombstone/family reads independently of ordinary contextual admission. Works validates complete membership atomically, required committed contract shape and pure persisted expiry transitions before no-op acknowledgment. Canonical missing related-child creation remains retryable; missing source history remains authoritative corruption. The generic host lifecycle checks actual hosted writer instances, including factories, before any hosted writer starts. Transport response and retained context allowances remain independent and documented. Current compatibility, source/package, durable runtime and restore evidence is recorded in [iteration-4 implementation evidence](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-iteration-4/implementation.md); earlier `.7` evidence remains historical. Publication, production authority/policy, Platform parity and host removal remain separate gates.

Story 4.15 post-review verification selects fresh immutable local producer `3.122.0-story415.20261016.9`. Recovery rejects foreign qualified event names, empty suspension sets and missing rejection discriminators before no-op acknowledgment. Restored reminder entries validate committed message IDs. The added proofs execute qualified trusted snapshots with event/receipt atomicity and delegated cascade expiry with an existing child schedule, response loss and receipt-before-checkpoint recovery. The pack helper reports actual checkout/working-tree provenance, while exact archive/source bindings and final selected-artifact verification are recorded in [post-review evidence](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-iteration-5/implementation.md). Earlier `.8` and other archives remain immutable; publication and production gates remain separate.

Story 4.15 resumed verification on 2026-10-16 follows the user's instruction to use the latest EventStore and fix issues. Official NuGet metadata confirms 3.122.0 remains the latest published complete nine-package line; its public Client lacks the required reminder catalog seam. Current SDK source is therefore consumed as fresh unpublished local candidate `3.122.1-story415.20261016.10`, with source Debug and package Release verified separately. The next-patch identity satisfies the preserved test-only Platform.Identity sibling reference's stable Gateway floor without a downgrade suppression. Earlier candidate selections and their proofs above are historical. The Parties source catalog uses an idempotent same-version SignalR floor to avoid duplicate central package entries. Current provenance, preliminary failures and final results are recorded in [resumed verification evidence](../_bmad-output/implementation-artifacts/evidence/story-4-15/resume-2026-10-16/implementation.md).

Historical Story 4.15 acceptance on 2026-10-16 used published EventStore `3.123.0`, producer commit `f78916d9534a72ba1515d97cfd51408e6eb809f1`. All nine exact package archives, source/package integration, independent review patches, persisted ten-phase recovery, compatibility and complete-store rollback pass. [Final public-release evidence](../_bmad-output/implementation-artifacts/evidence/story-4-15/public-3.123.0-final-2026-10-16/implementation.md) supersedes earlier pending/publication statements without relabeling historical candidate proof. Platform parity remains Story 4.16; the transitional host remains until gated Story 4.9.

Story 4.16 refreshed the latest-version check at 19:23 UTC on 2026-10-16: all nine public EventStore packages now select `3.124.0`, producer `0737c0befce5338e294a789092a0e223e8264a04`. The current dependency pins follow the user's latest-version instruction. This release retains the same legacy replay lease branch, while other retry/source-guard changes require fresh actual Platform verification before judging the earlier defect. Prepared corrections and candidate qualification remain separate from published acceptance. Story 4.15's immutable 3.123.0 acceptance remains preserved as historical evidence; the reopened verification below governs the journal-closeout correction. Story 4.16 and 4.9 are not accepted by this update.

Story 4.16 refreshed the latest-version check at 17:43 UTC on 2026-10-18: all nine public EventStore packages select `3.125.1`, tag `v3.125.1`, producer `55432c50d4b6f1666b4bd6d4a60bfc263b2d0e0b`. It contains both prepared Story 4.16 producer corrections (`9dbb3aaf`) and the Story 4.15 journal-closeout correction (`216b7906`). The Works and Platform dependency pins now select it under the user's latest-version instruction. The `3.124.1-story416-gateway` candidate and its passes remain historical supporting evidence. Story 4.16 and 4.9 are not accepted by this update.

Story 4.15 reopened verification on 2026-10-18 uses corrected source Debug and explicitly selected unpublished candidate `3.124.1-story415.20261018.1`. The candidate was built from `77d9af18a54d92506e310fe5a45021a395e66f60` (`v3.124.0-3`, three unpublished commits after public 3.124.0) plus the journal-closeout correction. The [reopened evidence](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-fixes-2026-10-18/implementation.md) records both lanes and ten-phase persisted restart proofs; the [source requalification addendum](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-fixes-2026-10-18/sdk-rebase-requalification/implementation.md) separately binds SDK `216b7906`. Later SDK source revisions require their own verification. Current-source deterministic results at SDK `f43b121c6c7539487b1f38b3c28508e3ab6264cc` are recorded in the [requalification packet](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-fixes-2026-10-18/post-review-patch-requalification/implementation.md), the [final review closeout](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-fixes-2026-10-18/post-review-patch-closeout/implementation.md) and its [review-patch addendum](../_bmad-output/implementation-artifacts/evidence/story-4-15/review-fixes-2026-10-18/closeout-review-patches/implementation.md); no live restart was rerun at that revision. The default public 3.124.0 packages do not contain the correction and are not qualified by candidate/source acceptance.

The corrected store contract is provider-neutral: after a failed create-only (empty ETag) `TrySaveAsync`, `TrySaveWithTimeToLiveAsync` or raw `TryWriteAsync`, a strong read of the same key must prove a nonempty ETag before the operation returns `false` as a CAS conflict. Missing or unreadable proof preserves the original write failure; caller cancellation remains failure, including during read-back. Nonempty-ETag updates retain their existing behavior. SDK source commit `575c8070355ff9b6afb17521d6eb529a85327e4d`, rebased unchanged as `f43b121c6c7539487b1f38b3c28508e3ab6264cc`, counts the read-back RPC in delivery-cost diagnostics. This source-only diagnostic is absent from the archived `3.124.1-story415.20261018.1` candidate DLLs; their verification covers the create-conflict behavior. Public publication, subsequent default-pin qualification, production authority/policy and Platform parity remain separate gates; Story 4.16 must account for the unfixed public pin.

Story 4.9 final review qualification is recorded in the [final review packet](../_bmad-output/implementation-artifacts/evidence/story-4-9/post-cutover-review-2-2026-10-21/README.md): the fresh actual CI5 entry point passed all 44 proof kinds and complete rollback after root-only tooling setup, bounded artifact retention and coordinator identity corrections. The full4 source inventories remain byte-identical. Parent tooling and all 275 architecture tests pass; the watch quota is restored. Nine pre-existing or unverified follow-ups remain separately recorded.
