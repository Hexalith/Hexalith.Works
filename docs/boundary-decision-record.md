# Works Boundary Decision Record

**Status:** Accepted — Epic 1 (Story 1.4).
**Tracks:** FR-21 (reference siblings, never copy them), FR-22 (expose module ports as
abstractions), FR-23 (produce the boundary decision record), AR-18 (ports realization), AR-19
(boundary decision record). Aligned with NFR-5 (domain purity) and NFR-11 (natural-language-is-data
boundary).

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
| **Conversations** | Optional **dialogue** correlation via `ConversationCorrelationId`. | Conversation content, messages, comments, threads. |
| **EventStore** | **Persistence**: event append/replay, envelopes, and aggregate identity (`AggregateIdentity`) come from EventStore.Contracts. | The storage substrate, envelope/metadata shape, concurrency mechanism, online-rebuild checkpointing. |
| **Tenants** | **Isolation**: every Works fact is tenant-scoped via `TenantId`; cross-tenant parent references are rejected at the writer. | Tenant registration, tenant profile, tenant lifecycle/onboarding. |
| **Commons** | **ID generation** and shared primitives — supplied at the edge, outside the aggregate. | The identifier-generation algorithm itself; the kernel never generates IDs. |
| **PolymorphicSerializations** | **Payload (de)serialization** of event/command contracts in the wider ecosystem. | The serialization registry/policy. Works-owned reference value objects (`PartyId`, `ExpectationReference`) are plain `System.Text.Json` records and need no polymorphic registration. |

### Why Works owns coordination facts but not these concerns

- **Identity → Parties.** Who performs work is a party fact. Works stores only the reference
  (`PartyId`); it never copies a party profile, so a renamed or deactivated party is resolved live
  from Parties, not from a stale Works copy.
- **Dialogue → Conversations.** The conversation around a work item is dialogue, owned by
  Conversations. Works carries only a correlation id, so message content never lands in a Works event
  and the natural-language-is-data boundary (NFR-11) holds.
- **Persistence → EventStore.** Append/replay/envelopes are an infrastructure concern. The Works
  kernel stays pure (NFR-5): no clock, generated id, I/O, or Dapr in command handling or replay — the
  command pipeline owns that decision in a later story.
- **Isolation → Tenants.** Tenancy is the isolation boundary. Works scopes every fact by `TenantId`
  and treats a foreign-tenant parent as a distinct reference rather than coercing it.
- **ID generation → Commons.** Deterministic replay forbids the kernel from minting ids; ids are
  supplied at the edge via Commons so `Handle`/`Apply` stay deterministic.
- **Payload serialization → PolymorphicSerializations.** Cross-module payload polymorphism is owned
  centrally; Works does not re-implement it.

Counter-metrics **SM-C1** (do not grow the kernel) and **SM-C2** (do not over-fit to deferred themes)
are binding: this story ships the named seams, not the machinery behind them.

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
  ordered raw-act event history (each `WorkItemAssigned` is a distinct act, never collapsed). The frozen v1
  catalog stays 36 — Story 4.2 adds no contract type (a fitness test asserts both the no-kind-vocabulary
  rule and the catalog size).
- **Story 4.3 (claim queued work / single-claim-wins).** Single-claim-wins is the **composition** of the
  Works kernel lifecycle (`Queued/Assigned → Claim = Accept(InProgress)`; all else `R`) and the
  **EventStore-owned expected-version (ETag) optimistic concurrency** mechanism (see the EventStore row
  above — Works references persistence/concurrency, it does **not** own the mechanism): two claims at the
  same expected version both target sequence `N+1`, one append wins, and the loser re-handles against the
  now-`InProgress` state to the existing `WorkItemTransitionRejected(InProgress, "Claim")`. Works adds **no**
  claim-eligibility, routing, escalation, ranking, or AI-decision type, and **no** new
  `ClaimRejected`/`ConcurrencyRejected` rejection — claim is **unconditional** in v1 (any tenant Executor
  may claim a queued item; eligibility is the deferred Theme-4 executor-routing seam `IExecutorRouter`
  above), `AuthorityLevel` stays carried-not-enforced, and the v1 catalog stays 36 (fitness-asserted). The
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
  catalog type — the v1 catalog stays 36 (fitness-asserted) and the golden corpus is byte-compatible. The
  notifier requirement is met by **referencing** the substrate seam
  `IProjectionChangeNotifier.NotifyProjectionChangedAsync("works-whats-next", tenantId, …)` (EventStore
  owns the SignalR broadcast); the live query/notifier runtime is the deferred Aspire wiring (Stories
  4.5/4.6), and **no web shell, DataGrid, MCP, chatbot, or email surface** ships in v1.
- **Story 4.5 (prove the command/event pipeline under Aspire).** The new runnable host (`src/Hexalith.Works`)
  and the Works AppHost are an **adapter-edge runtime proof only** — they consume the deferred Story 4.4 seams,
  they do not move behavior into the kernel. The boundary stays exactly as the EventStore row above states:
  **EventStore owns** persistence, the expected-version/ETag concurrency mechanism, envelopes/metadata, and the
  public command/query gateway (`/api/v1/commands`, `/api/v1/queries`); **Works owns** only domain behavior (the
  pure static `WorkItemAggregate`, wrapped at the edge by `WorkItemEventStoreAggregate : EventStoreAggregate<…>`
  for discovery) and the read-model transformations (the pure `WhatsNext`/roll-up projections, persisted by a
  host-edge `/project` adapter + `WhatsNextQueryHandler`). The kernel (`Contracts`, `Server`, `Projections`,
  `Reactor`) remains free of Dapr, ASP.NET hosting, EventStore runtime, clocks, I/O, and logging
  (fitness-asserted); the host is the **only** Works source project allowed those adapters. `AuthorityLevel`
  stays carried-not-enforced, there is still no `IExecutorRouter` implementation, and **no** production UI, MCP,
  chatbot, email, routing, cost, or security-hardening surface is composed. The v1 catalog stays **36** and the
  golden corpus is byte-unchanged (no durable type added by the adapter/query/read-model code).
- **Story 4.6 (prove reminder and reactor recovery).** Date-based resumes and terminal-cascade restart recovery
  are still **adapter-edge orchestration**, not new kernel behavior. Works owns only deterministic reminder
  naming, Dapr actor reminder registration/fire handling, startup reconciliation over configured tenant streams,
  terminal-cascade delivery sequencing, and bounded checkpoint records in the shared state store. **EventStore
  still owns** persistence, envelopes, optimistic concurrency, command submission/status, stream reads, and
  projection/read-model substrate; every reminder resume and cascade target command is submitted through the
  EventStore command gateway and round-trips through `WorkItemAggregate.Handle` for acceptance/no-op/rejection.
  The kernel (`Contracts`, `Server`, `Projections`, `Reactor`) remains free of Dapr actors, clocks, logging,
  network/filesystem I/O, command gateways, and checkpoint stores (fitness-asserted). The current recovery scan
  is bounded to configured tenants because the EventStore/Dapr surfaces do not expose cross-tenant enumeration;
  this is documented as a substrate limitation, not hidden in the domain. No reminder/checkpoint/read-model
  runtime type enters the polymorphic catalog, so the v1 catalog stays **36** and the golden corpus remains
  byte-compatible. **Recovery trigger decision:** reminders/resumes are reconciliation-on-recovery only —
  registered/reissued when the host (re)starts and scans pending date awaits — not registered at suspend time by
  an event-driven subscriber. This matches the ACs (AC #1 = fire behavior, AC #3 = reconciliation) and avoids a
  new steady-state subscriber; a host restart is the trigger that re-establishes pending reminders. The gated
  Tier-3 Aspire lane (`WorksReminderRecoveryPipelineSmokeTests`) proves this restart→reissue→exactly-once-resume
  path; because the gateway stream-read route requires a per-aggregate id (no tenant/domain-wide enumeration), the
  lane reissues through the adapter's own deterministic `DateResume` factory rather than tenant-wide
  auto-discovery, and the reconciliation decision logic is proven deterministically by
  `DateReminderRecoveryRuntimeTests`.
- **Story 4.7 (live reactor consumption and durable cascade discovery).** Works uses the EventStore SDK's
  subscription registration, handler contracts, and durable Dapr marker store, but maps an equivalent host-local
  endpoint instead of the SDK's generic processor. The checked-out generic processor uses default JSON options;
  real camel-case Works Web JSON silently binds to a zero-valued record and can be acknowledged as processed.
  The local processor reuses `WorksEventDecoder`, verifies envelope/payload tenant and aggregate identity, and
  preserves the SDK result contract: terminal skips are acknowledged, handler failures remain retryable, and a
  completed marker makes redelivery a duplicate. EventStore receives
  `EventStore__Publisher__TopicOverrides__work=work.events`, so every tenant's Works event reaches the one
  programmatic `pubsub` subscription without introducing a second component. The `WorkItemCompleted` handler
  re-reads the completed child's aggregate stream to find its same-tenant `WorkItemCreated.Parent`, then replays
  that parent's stream to rebuild only its current suspended await conditions; mismatch or read failure returns
  no candidate. The unchanged pure translator emits `ResumeWorkItem`, and the EventStore gateway plus aggregate
  `Handle` remain the acceptance and idempotency boundary. Internal recovery reads and commands route through
  the Works Dapr sidecar (`DAPR_HTTP_ENDPOINT` + EventStore service invocation), and EventStore explicitly
  allow-lists the `works` caller. This preserves the supported Dapr-internal authentication boundary instead of
  bypassing authorization with direct host calls. Recovery command payloads retain CLR property casing because
  the pinned EventStore aggregate adapter deserializes command payloads with default, case-sensitive JSON
  options; camel-case Web JSON would otherwise silently bind zero-valued tenant and work-item identities.

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
- Hexalith libraries are consumed as `ProjectReference` to the checked-out sibling source, never as
  NuGet `PackageReference` (see `CLAUDE.md`). Story 1.4 introduced no new sibling reference.
- EventStore API-surface constraints from Story 1.1 (ETag-based concurrency, checkpoint-per-aggregate
  online rebuild) are unchanged by this story; it adds no command-pipeline wiring.
- Sources: `epics.md` FR-2/FR-21/FR-22/FR-23, NFR-5/NFR-11, SM-C1/SM-C2; `architecture.md` AR-18
  (ports realization), AR-19 (boundary decision record), Architectural Boundaries, Deferred Decisions.
