# Adversarial Divergence Review — Hexalith.Works Architecture

**Target:** `_bmad-output/planning-artifacts/architecture.md`  
**Intent:** Validate only; the source architecture was not modified.  
**Lens:** Construct two independently built units one level below the architecture that obey every stated rule yet do not compose safely.  
**Verdict:** **FAIL — not implementation-convergent.** The document contains sound local patterns, but three system-defining invariants remain impossible or unowned at the seams: cross-aggregate tree mutation, live recursive roll-up, and runtime-host migration. Seven additional protocol gaps allow compliant builders to choose incompatible identity, recovery, ordering, and schema behaviors.

## Finding summary

| ID | Severity | Disposition | Finding |
| --- | --- | --- | --- |
| ADV-01 | Critical | discuss | No authoritative, atomic owner exists for single-parent/acyclic tree mutation. |
| ADV-02 | Critical | discuss | The promised incremental recursive roll-up is incompatible with the actual per-aggregate projection boundary. |
| ADV-03 | Critical | discuss | The runtime migration names neither the destination owner nor the seam-by-seam capability allocation. |
| ADV-04 | High | discuss | Cascade checkpoint correctness assumes a single writer but the architecture binds no lease, ETag transform, or monotonic protocol. |
| ADV-05 | High | discuss | Reminder recovery is declared resolved while its authoritative discovery source remains an explicit gap. |
| ADV-06 | High | discuss | Shared-rebuild quiescence/fencing is an aspiration, not an interoperable protocol. |
| ADV-07 | High | discuss | The universal identity-derived key rule conflicts with the live topic, reminder, projection, and notification namespaces. |
| ADV-08 | High | discuss | “Creation order” is required but absent from the contract; the live projection silently substitutes ID order. |
| ADV-09 | High | discuss | Edge-assigned aggregate IDs do not by themselves make create or reactor submissions idempotent. |
| ADV-10 | High | discuss | “Additive/tolerant” schema evolution does not bind compatibility direction, enum behavior, discriminator stability, or catalog rollout. |

## Critical findings

### ADV-01 — Tree invariants have no authoritative cross-aggregate mutation owner

**Severity:** Critical  
**Disposition:** discuss

The architecture says a single aggregate owns parent/children references and that the single-parent, acyclic, bounded, tenant-closed tree is enforced at spawn (`architecture.md:34-36`, `architecture.md:244`, `architecture.md:285`). It simultaneously requires a pure aggregate that performs no external read. It never identifies the authoritative owner that reserves a child identity, supplies a trustworthy current parent and ancestry, or serializes two parents trying to attach the same child.

The live contract exposes this gap rather than resolving it: `SpawnChild` accepts caller-fed `ProposedParentAncestors`, `ProposedParentDepth`, `MaxDepth`, and `ExistingChildParent`, all with permissive defaults (`src/Hexalith.Works.Contracts/Commands/SpawnChild.cs:10-16`, `src/Hexalith.Works.Contracts/Commands/SpawnChild.cs:19-32`). The guard only evaluates those supplied facts (`src/Hexalith.Works.Server/Aggregates/WorkTreeAttachmentGuard.cs:30-69`). The runtime documentation explicitly says callers supply the facts and the aggregate does not read the authoritative stores (`docs/work-tree-shape-guard.md:35-42`).

**Two-unit counterexample:** Parent aggregate A and parent aggregate B independently receive `SpawnChild` for the same child, each with the default `ExistingChildParent = null` and an incomplete ancestor list. Both pure handlers obey the architecture and accept, and both persist `ChildSpawned`. The downstream topology projection now has two parents for one child. The same construction accepts a cycle when the edge adapter supplies a stale/empty ancestor list. Neither unit violated a stated decision; the architecture failed to assign the invariant to an atomic writer.

**Binding required:** Name one authoritative attachment protocol and owner. It must atomically reserve `(tenant, childId) -> parentId`, derive ancestry/depth from authoritative state rather than caller claims, define conflict/retry behavior, and state which event is authoritative when parent-stream and child-stream evidence disagree. If this must remain single-aggregate, redesign the mutation so the child aggregate owns parent attachment and parents derive child lists.

### ADV-02 — The recursive roll-up promise cannot be delivered by the stated ordinary projection seam

**Severity:** Critical  
**Disposition:** discuss

The architecture binds an incremental, eventually consistent recursive roll-up, updated by child events with no query-time whole-stream read (`architecture.md:36`, `architecture.md:96`, `architecture.md:239`, `architecture.md:276`). It later claims performance and NFR coverage are fully addressed (`architecture.md:623-629`). The PRD makes the behavior testable: every descendant change must be reflected incrementally in every ancestor (`prds/prd-works-2026-06-14/prd.md:191-198`).

The brownfield runtime has a different contract. EventStore invokes `/project` with one aggregate stream, so the adapter cannot reconcile later child changes and deliberately persists parent rolled values as unavailable (`src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:18-37`). It only obtains a coherent tree when the operator runs the sealed, all-tenant shared rebuild; after that, an ordinary aggregate dispatch again refuses child-dependent rolled shapes (`docs/work-roll-up-projection.md:67-84`, `docs/work-roll-up-projection.md:86-100`).

**Two-unit counterexample:** The Works projection team implements A5 as written and emits updated parent totals whenever a child event arrives. The EventStore/platform team implements the documented single-aggregate `/project` callback and never co-delivers the parent graph. Both obey their stated boundaries, but the first has no input on which to compute and the second persists `null` for the promised value. A consumer built against FR-11 expects a number while the runtime legitimately returns unavailable until an operator rebuild.

**Binding required:** Choose and record one live cross-aggregate propagation design: a relationship-aware named projection with durable child-to-ancestor fan-out, an explicit child-contribution event/topic consumed by parent projections, or a declared rebuild-only product contract that changes FR-11/SM-2. Bind ownership, delivery coordinates, state shape, write ordering, failure behavior, and freshness/unavailable semantics. Do not call the current ordinary path an incremental recursive roll-up.

### ADV-03 — The platform boundary has no concrete owner or capability allocation

**Severity:** Critical  
**Disposition:** discuss

The target architecture prohibits Works-owned AppHost, ServiceDefaults, delivery, scheduling, projection/query plumbing, and subscriptions, assigning them to a “designated platform/host repository” (`architecture.md:506-508`, `architecture.md:524-528`). Yet it explicitly leaves the repository and destination layout unnamed (`architecture.md:586-587`, `architecture.md:633-647`). The approved change proposal says Story 4.9 cannot begin until both target and owner are named and requires the architect to decide which current components become EventStore SDK capabilities (`sprint-change-proposal-2026-09-05.md:405-406`, `sprint-change-proposal-2026-09-05.md:427-431`).

This is not a mere deployment filename. The current supposedly minimal executable registers a bespoke durable event processor, cascade handlers, completion reactor, suspend-time reminder handler, reminder/cascade recovery, a custom `/project` endpoint, and Dapr actors (`src/Hexalith.Works/Runtime/WorksHost.cs:64-89`, `src/Hexalith.Works/Runtime/WorksHost.cs:98-121`). The existing AppHost owns exact topic, Dapr, Scheduler, state-store, dead-letter, and access-control composition (`src/Hexalith.Works.AppHost/Program.cs:54-70`, `src/Hexalith.Works.AppHost/Program.cs:88-136`). The architecture’s target tree reduces `Hexalith.Works` to `Program.cs` and an aggregate wrapper (`architecture.md:502-504`) without allocating these domain-specific runtime responsibilities.

**Two-unit counterexample:** The Works migration unit removes every adapter-edge component because delivery/checkpoints/reminders/projections are platform-owned. Independently, the platform-host unit composes only the published minimal Works executable because domain-specific handlers are Works-owned. Both obey the prose boundary, but no component registers reminders, consumes terminal events, maintains checkpoints, or serves the Works projection/query shape.

**Binding required:** Name the repository, owning team, package boundaries, and a seam-by-seam migration matrix for every current runtime type/configuration. For each capability, state whether it remains a Works domain handler behind an SDK interface, moves into EventStore as generic infrastructure, or belongs to the concrete platform host. Record package/API versions and the exact conformance test owned on each side before deletion.

## High findings

### ADV-04 — Cascade checkpoints are not safe under independently running platform replicas

**Severity:** High  
**Disposition:** discuss

The architecture requires checkpoint-driven resumable cascades and idempotent targets (`architecture.md:99-101`, `architecture.md:253`, `architecture.md:374-377`) but never binds checkpoint identity, status transitions, command message identity, transaction boundaries, concurrency control, or runtime cardinality. The live dispatcher chose `Pending -> Attempted -> submit -> Completed` (`src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs:170-209`) and the store claims last-write-wins is safe because there is one writer (`src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:7-13`). Its progress save is a read/check followed by an unconditional save, not one ETag-protected monotonic transform (`src/Hexalith.Works/Recovery/Cascade/ReadModelCascadeCheckpointStore.cs:39-80`).

**Two-unit counterexample:** Two platform replicas both run startup reconciliation, read the same incomplete checkpoint, and drive it. Replica A saves `Completed`; replica B, holding stale target state, saves an incomplete checkpoint after A’s read/check. The domain terminal command is semantically idempotent, but durable recovery state regresses and the global incomplete index can be reintroduced or stranded. Both replicas comply because the architecture neither elects one owner nor requires monotonic ETag writes.

**Binding required:** Specify the checkpoint key (including parent terminal event identity), a monotonic state machine, deterministic target `MessageId`/causation derivation, ETag/fencing behavior, and whether exactly one reconciler runs per partition. Require the target-status and index updates to converge under concurrent replicas, not only process restart.

### ADV-05 — Timer recovery is “resolved” without an authoritative discovery contract

**Severity:** High  
**Disposition:** discuss

C2 declares Dapr reminders durable and says reconciliation covers lost firings (`architecture.md:261-264`), while the same document lists “define the reminder reconciliation-on-recovery re-scan query” as an important unresolved gap (`architecture.md:649-655`). The later epic exists precisely because tenant-wide stream reads were rejected and configured tenants missed work (`epics.md:1290-1299`). The live solution had to invent a durable global tenant registry, per-tenant pending-await index, and per-aggregate stream re-fold because neither Dapr state nor the gateway can enumerate the required truth (`src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:11-27`, `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs:41-79`).

**Two-unit counterexample:** The Works handler team assumes `WorkItemSuspended` subscription plus Dapr durability is sufficient. The platform recovery team implements the architecture’s unspecified “re-scan” as a tenant-wide EventStore request. The first loses a reminder registration in the publish/register crash window; the second receives the known gateway rejection and discovers nothing. Both satisfy the words “durable reminder + reconciliation-on-recovery,” but a due item never resumes.

**Binding required:** Promote the successful brownfield discovery protocol to an invariant: authoritative stream truth, registry/index as discovery only, exact keys and owner, update ordering, partial-scan behavior, cursor/page limits, tenant lifecycle cleanup, and recovery retry/exhaustion/alerting. Allocate it in the Story 4.9 migration matrix.

### ADV-06 — Rebuild quiescence/fencing has no shared protocol

**Severity:** High  
**Disposition:** discuss

E1 permits delivery to be “quiesced … or excluded by an equivalent platform fence” and promises atomic promotion (`architecture.md:265`, `architecture.md:47`, `architecture.md:105-106`). It does not define who acquires the fence, which writers it covers, how capture and fence epochs relate, how a late old-generation write is rejected, or which watermark proves catch-up complete. The implemented handler explicitly cannot arbitrate a live writer against manifest `LastWrite` operations (`docs/work-roll-up-projection.md:75-84`; also `docs/whats-next-projection.md:73-82`).

**Two-unit counterexample:** The rebuild unit considers the Dapr subscription paused once its local consumer stops and captures inventory. A second ordinary projection replica, unaware of that local pause, writes a newer legacy/current document while Stage/Commit proceeds. Both units obey “quiesced or equivalent fence” according to their local interpretation, but Commit can promote a manifest missing the live write and subsequent last-write ordering can overwrite either side.

**Binding required:** Define a platform-owned fence protocol with epoch/token, admitted writer set, capture watermark, write rejection/queuing rules, atomic commit scope, abort, resume, and catch-up completion. “Equivalent” may name multiple implementations only if they satisfy the same testable protocol.

### ADV-07 — The key/topic namespace rule contradicts the live integration contract

**Severity:** High  
**Disposition:** discuss

The architecture says *everything* derives from `{tenant}:work:{workItemId}`, explicitly including state/projection keys, pub/sub topics, reminder names, SignalR groups, and log scopes; it then defines reminder identity as `(workItemId, awaitConditionKey)` (`architecture.md:321-324`). It also says all event, state, and projection keys are under `{tenant}:work:{id}` (`architecture.md:539-540`). Brownfield choices are materially different: events use one shared `work.events` topic (`src/Hexalith.Works.AppHost/Program.cs:57-69`); reminder and actor identities hash tenant + work item + condition (`src/Hexalith.Works/Reminders/DateReminderName.cs:30-53`); projection keys use `projection:works:*` generations (`src/Hexalith.Works/Projections/WorksReadModelKeys.cs:24-59`); notifications use `{projectionType}:{tenantId}` (`docs/whats-next-projection.md:137-145`).

**Two-unit counterexample:** An EventStore publisher follows the brownfield override and publishes all Works events to `work.events`; a newly built platform subscriber follows the architecture and subscribes to a per-aggregate `{tenant}:work:{id}` topic. No events arrive. Independently, a reminder implementer follows the stated two-field identity and collides identical work IDs/await keys across tenants, while another includes tenant as the live implementation does.

**Binding required:** Replace the universal derivation claim with a namespace table: logical aggregate identity, actor ID, state key, read-model generation key, shared topic, dead-letter topics, reminder name, checkpoint key, notifier group, and log scope. For each, bind fields, delimiters/escaping or hashing, tenant placement, versioning, and owner.

### ADV-08 — “Creation order” has no representable source and already diverged

**Severity:** High  
**Disposition:** discuss

A2 orders “what’s next” by Priority, Due Date, then creation order (`architecture.md:238`), matching the PRD and epic acceptance criteria (`prds/prd-works-2026-06-14/prd.md:281-286`, `epics.md:1154-1157`). No read model or event contract binds a cross-aggregate creation-order coordinate. The live projection explicitly substitutes ordinal `WorkItemId` because per-stream sequence and EventStore-owned timestamps cannot provide stable cross-item creation order (`docs/whats-next-projection.md:26-32`), and the comparator implements that substitution (`src/Hexalith.Works.Projections/Strategies/WhatsNextOrdering.cs:62-71`).

**Two-unit counterexample:** A producer/query unit interprets creation order as EventStore envelope timestamp/global position; the pure projection unit uses ordinal WorkItem ID to remain replay-deterministic. For equal priority/due-date items they return opposite orders. Both obey the architecture because it never names the coordinate or tie behavior.

**Binding required:** Decide whether the requirement is true creation order or stable deterministic identity order. If creation order, expose one immutable, globally comparable, replay-stable field/coordinate and define ties; if ID order, correct architecture/PRD/epics and name it explicitly.

### ADV-09 — Aggregate identity is not a command idempotency contract

**Severity:** High  
**Disposition:** discuss

A1 says assigning the aggregate ID at the edge “enables idempotent create (client retry -> same ID)” (`architecture.md:234-235`), but no rule binds transport `MessageId` or `IdempotencyKey` reuse, collision scope, retention, or replay result. The domain command contains no message identity (`src/Hexalith.Works.Contracts/Commands/CreateWorkItem.cs:17-29`), and a second create on established state is a domain rejection, not an idempotent replay (`src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs:18-25`). EventStore’s gateway separately exposes required `MessageId` and optional `IdempotencyKey` (`references/Hexalith.EventStore/src/Hexalith.EventStore.Contracts/Commands/SubmitCommandRequest.cs:5-26`). The architecture’s reactor section likewise says target commands are idempotent without binding the deterministic submission identity the live adapters had to invent.

**Two-unit counterexample:** Edge A retries the same `CreateWorkItem` aggregate ID with a freshly generated message ID and receives a persisted transition rejection. Edge B retries with the original message ID and EventStore deduplicates to the prior outcome. Both satisfy A1, but callers see incompatible retry semantics. The same split occurs if one cascade adapter derives message identity from the parent payload sequence while another uses the envelope message ID.

**Binding required:** Define command idempotency at the transport seam: tenant-scoped key/message identity, which retries must reuse it, request-digest collision behavior, retention/expiry, exact-result replay, and deterministic IDs for every reactor/reminder emission. Separate semantic no-op from transport dedup and from projection offset dedup.

### ADV-10 — Schema evolution is a slogan rather than a reader/writer contract

**Severity:** High  
**Disposition:** discuss

The architecture requires additive, serialization-tolerant evolution, no `V2` event types, polymorphic registration, and golden fixtures (`architecture.md:63-65`, `architecture.md:359-361`, `architecture.md:409-426`). It does not bind wire property names/casing, required/default/null semantics, discriminator/type-name stability, unknown enum handling, compatibility direction, or mixed-version rollout order. A2 calls `Priority` “additive-tolerant” (`architecture.md:238`), while a sibling wire enum explicitly treats unknown strings as fatal and calls extension a versioned change (`src/Hexalith.Works.Contracts/ValueObjects/Channel.cs:5-18`). The rendered architecture already claims a 40-type catalog (`architecture.md:272-275`, `architecture.md:309-315`) while the live governance/corpus remains frozen at 37 (`tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs:9-18`, `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventShapeGovernanceTests.cs:18-44`).

**Two-unit counterexample:** A new producer additively emits `Channel = "Slack"` or a new `Priority` member because the architecture promises additive tolerance. An old consumer using the current closed string enum fails deserialization before it can ignore or preserve the value. Alternatively, one mapper registers the three Story 1.5 types before the other; neither the deployment order nor unknown-type behavior is specified.

**Binding required:** Add a compatibility matrix for old-reader/new-writer and new-reader/old-writer behavior; freeze discriminator/type names and JSON names; define required/default/null rules; define unknown enum and unknown event handling per consumer; require expand-migrate-contract deployment order; and make catalog/golden-corpus versions factual rather than aspirational.

## Gate conclusion

The architecture is rich in rationale and local implementation advice, but it does not yet serve as a consistency contract between independently built Works, EventStore, and platform-host units. The three critical findings must be resolved before Story 4.9 or any additional tree/roll-up work. The high findings should be converted into explicit, testable cross-boundary rules in the same update; otherwise the host migration will preserve code coverage while silently changing runtime semantics.

**Counts:** 3 Critical · 7 High · 0 Medium · 0 Low.
