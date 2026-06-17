# What's-Next Queue Projection

Story 4.4 realizes the tenant "what's next" query (FR-20) as a **pure read projection + query-shaping**
over Works' own events — not an authoritative queue aggregate (AR-10/B1). It returns a tenant's eligible
work items, ordered, so assigned and claimable work can be presented without a routing engine. The
aggregate continues to own only synchronous `WorkItemState`; consumers that need the claimable pool read
this projection's read model (`WhatsNextItem`) instead.

## Eligible set

`WhatsNext(tenant)` returns **only** items whose current derived `Status` is `Queued` or `Assigned` for
that tenant. `Created`, `InProgress`, `Suspended`, and every terminal (`Completed`, `Cancelled`,
`Rejected`, `Expired`) are excluded. A requeued rejection (`WorkItemRejected` with `Requeue: true`) rests
at `Queued` and is eligible; a non-requeue rejection is terminal and excluded.

## Ordering rule (DC2)

A **total, deterministic, order-tolerant** comparator (`WhatsNextOrdering`) applied on read, in order:

1. **Priority rank** — present priority ranks `Critical(0) < High(1) < Normal(2) < Low(3)`; an **absent or
   `Unknown`** priority ranks **last (4)**.
2. **Due Date** — earliest first; an **absent** due date sorts **after** every present due date (treated as
   a max sentinel).
3. **Identity tiebreak** — `WorkItemId.Value` ordinal (`StringComparer.Ordinal`).

An item with **neither** Priority **nor** Due Date lands at the bottom by construction (rank 4 **and** the
due-date max sentinel) — FR-4 "neither sorts last". The identity tiebreak is chosen over first-seen arrival
order because it is a **pure function of identity** — stable across rebuilds and immune to out-of-order or
duplicate delivery (B2/NFR-4). Works has no creation timestamp in the kernel (`WorkItemState` carries only
a per-aggregate `Sequence`; envelope timestamps are EventStore-owned), so "creation order" is realized as
this rebuild-deterministic identity order. Within a tenant, inner ids are distinct, so the comparator is a
**strict total order** — no two distinct items ever compare equal.

## Read-model contract (`WhatsNextItem`)

A plain `System.Text.Json` `sealed record` (like `WorkItemRollUp`), **not** a `[PolymorphicSerialization]`
catalog type, not stream-appended, not in the golden corpus (DC3). It exposes only data Works owns, with
**no UI-specific types**:

- `TenantId`, `WorkItemId`, `WorkItemStatus Status`.
- `Priority? Priority`, `DateOnly? DueDate` — the ordering inputs (both nullable).
- `ExecutorBinding? ExecutorBinding` — `PartyId` + `Channel` + `AuthorityLevel` as **data**, with **zero
  executor-kind branching** (UX-DR4). A queued item keeps its **last** binding (the last raw act —
  `WorkItemQueued` carries no binding and does not clear it).
- `OwnRemaining? OwnRemaining` — the item's own burn-down, derived from its own events (UX-DR3).
- `RolledRemaining? RolledRemaining` + `IReadOnlyList<RolledRemaining> RolledRemainingByUnit` — **distinct
  types** from `OwnRemaining` (AR-9/B3), populated only **where a co-available roll-up read model is
  supplied** (DC7); `null`/empty otherwise. Per-unit subtotals are never coerced (UX-DR2).
- `IReadOnlyList<AwaitCondition> AwaitConditions` — kind + key, for a future "Waiting on…" pill (UX-DR5).
- `long LatestAcceptedSourceSequence` — a freshness watermark (mirrors `WorkItemRollUp`).

## Rolled-Remaining composition (DC7)

The what's-next projection is a single-item eligibility + ordering projection. It derives each item's
**own** status / schedule / binding / own-remaining / await-conditions from that item's own events and does
**not** rebuild the parent/child tree. `RolledRemaining` / `RolledRemainingByUnit` are nullable "where
available": pass an optional roll-up lookup
(`Func<TenantId, WorkItemId, WorkItemRollUp?>`) to `WhatsNext(...)` to compose them from the existing
`WorkItemRollUpProjection` read model; leave it null to return them empty. Reinventing the roll-up tree
walk here is the anti-pattern this avoids.

## Tenant scoping and query-side authorization (D2 / NFR-1)

Two **distinct controls**, defense-in-depth:

1. **Tenant key-scoping (in the projection).** Each item is keyed by `(tenant, id)`; reads compare tenant
   by ordinal string. Items from different tenants with **colliding inner ids** stay distinct and never
   cross (`WorkItemId.Value` is the raw inner id — not tenant-composed — so the explicit tenant key is what
   keeps them apart).
2. **Query-side authorization filter (`WhatsNextQueryAuthorization`).** A pure filter (mirroring
   `Hexalith.Projects` `ProjectQueryTenantFilter`) that re-applies an **authoritative tenant id** check on
   top of the projection's scoping, plus an optional caller-supplied authorization predicate — the seam a
   future `IDomainQueryHandler` fills from `QueryEnvelope.UserId`. It is **fail-closed**: a null/empty
   authoritative tenant returns an empty result. `AuthorityLevel` stays carried-not-enforced (D1/FR-19);
   no `IExecutorRouter` impl, eligibility scoring, or routing input is involved.

## Idempotent + order-tolerant (DC5)

Each item keeps its accepted events in a per-item `SortedDictionary<long, IEventPayload>` keyed by
aggregate-local sequence and re-derives status / schedule / binding / own-remaining / await-conditions on
each change. Replay, duplicate delivery, and out-of-order delivery converge to the same read model and the
same ordering (NFR-4/NFR-9/B2). Rebuild is per-tenant and event-derived (the projection holds no
authoritative state), aligning to EventStore's **checkpoint-per-aggregate** online rebuild (see
`docs/eventstore-api-surface-constraints.md`), **not** the superseded shadow+swap wording.

## Notifier seam and deferred runtime (DC1 / AC #4)

`Project(delivery)` returns a `WhatsNextProjectionChange(Changed, TenantId)`: did this delivery change the
tenant's what's-next **eligibility set** or **ordering**? The signature compares the ordered list of each
eligible item's ordering key (id + priority rank + due-date key), so membership and ordering-input changes
flip it while binding- or remaining-only updates do not. The pure kernel ships this decision plus a stable
`projectionType` token, `"works-whats-next"`.

Live notification is the **deferred runtime wiring** (Stories 4.5/4.6): the adapter calls
`IProjectionChangeNotifier.NotifyProjectionChangedAsync("works-whats-next", tenantId, …)` (EventStore.Client)
only when `Changed` is set; that flows `DaprProjectionChangeNotifier → IProjectionChangedBroadcaster →
SignalRProjectionChangedBroadcaster` (group `{projectionType}:{tenantId}`, fail-open). The Works kernel
cannot reference Client (dependency direction), so v1 ships the seam, not the surface. **No web shell,
DataGrid, MCP, chatbot, or email surface ships in v1** (UX-DR1).

## Privacy (AC #5 / NFR-6)

The projection and read model perform **no logging** — the kernel references no `ILogger` or log sink, so
payloads, PII, and obligation text are never logged from the pure core (a fitness test asserts this). Read
models carry data for consumers; they must not be *logged* by the kernel. A runtime adapter owns structured
logging later.

## Boundaries

The projection, comparator, filter, and change-signal are pure code in `Hexalith.Works.Projections` (read
model `WhatsNextItem` in `Hexalith.Works.Contracts`), referencing only Works contracts (+
`EventStore.Contracts`). They do not read EventStore, repositories, files, clocks, Dapr, runtime
configuration, UI, routing, LLM, or cost-governance services. Story 4.4 adds **no** event, command, or
rejection type — the v1 catalog stays **36** and the golden corpus stays byte-compatible (DC3). The live
`IDomainQueryHandler` / `/query` endpoint, `IReadModelStore` persistence, and the
`IProjectionChangeNotifier` / SignalR broadcast are the deferred runtime adapters (Stories 4.5/4.6), gated
on the EventStore projection-model reconciliation.
