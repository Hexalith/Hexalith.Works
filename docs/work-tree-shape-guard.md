# Work Tree Shape Guard

Story 3.1 defines the domain guard for attaching a Work Item to a parent. The guard is pure and
synchronous: callers supply the current parent fact, the proposed parent reference, the proposed
parent's ancestor chain, and the depth policy. The guard does not read EventStore, projections,
configuration stores, clocks, files, HTTP, databases, Dapr, or generated IDs.

## Rules

- A Work Item may be a root item with no parent.
- A Work Item may have at most one parent.
- Revalidating the same parent is accepted as idempotent.
- A parent link stores only `ParentWorkItemReference` (`TenantId` + `WorkItemId`); it does not embed
  child state, descendant lists, roll-up totals, Party data, Tenant profiles, Conversation data, or
  EventStore envelope metadata.
- Parent and child tenant ids must be equal using `TenantId` normalization. Cross-tenant links fail
  closed with a domain rejection.
- Self-parenting is rejected as a cycle.
- An attachment is rejected as a cycle when the proposed child appears in the proposed parent's
  supplied ancestor chain.
- The default maximum resulting tree depth is `32`.
- Depth is counted with a root item at depth `1`; a child attached to a parent at depth `31` reaches
  depth `32` and is accepted by the default policy, while a child attached to a parent at depth `32`
  reaches depth `33` and is rejected.
- The maximum depth is supplied to the guard as a value, so future tenant/type policy can override the
  default without wiring runtime configuration into the domain kernel.
- Breadth is not capped by this guard. Multiple different children may reference the same parent.

## Out of Scope

Story 3.1 does not implement `ChildSpawned`, roll-up, await conditions, cascade traversal,
projections, query-side authorization, reactor behavior, runtime reminders, or AppHost wiring. Later
stories must supply their own caller facts and reuse this guard before writing new tree edges.

## Spawned Child Work

`SpawnChild` reuses the same guard before the parent records a `ChildSpawned` event. The command
supplies the child id, proposed parent depth, proposed parent ancestor chain, max-depth policy, and
any known existing child parent fact; the aggregate does not read EventStore or projections to decide
whether the child edge is valid. When accepted, the parent records only the child work item id and the
facts needed for the command pipeline to create the child with
`ParentWorkItemReference(parentTenant, parentWorkItemId)`.

When `SuspendParentUntilChildCompletes` is true, the parent emits `ChildSpawned` followed by
`WorkItemSuspended` with a `ChildCompleted(childId)` await condition. Resuming follows the same
first-match policy as direct suspend/resume: a matching child-completion resume consumes that condition,
clears the full suspension condition set, and records the consumed condition in `WorkItemResumed` so a
duplicate resume can no-op after replay. The reactor translation is mechanical; it can turn a completed
child event plus explicit awaiting-parent input into a parent `ResumeWorkItem` intent, but the aggregate
decides whether that intent is current, accepted, rejected, or duplicate.

## Terminal Cascade Through Active Descendants

Story 3.6 realizes the pure-domain and pure-reactor portion of FR-10 cascade semantics: when a parent
Work Item is cancelled or expired, the still-active descendant subtree must not keep burning down. The
cascade is split across two pure layers, with the runtime layer deferred.

- **Tenant-safe descendant discovery is supplied to the translator, not performed by it.** A caller
  hands the pure `TerminalCascadeTranslator` a list of `CascadeDescendant` candidates (each carrying only
  `TenantId`, `WorkItemId`, and an `IsTerminal` marker). The translator never reads EventStore,
  projections, files, Dapr state, clocks, or in-memory globals, and never walks the tree itself.
- **The translator emits mechanical command intents only.** A parent `WorkItemCancelled` maps to
  descendant `CancelWorkItem` intents and a parent `WorkItemExpired` maps to descendant `ExpireWorkItem`
  intents. Input order may determine output order. The translator chooses the terminal command *kind*
  from the parent event but makes no domain outcome decision.
- **Tenant equality fails closed.** A candidate whose tenant differs from the parent terminal event's
  tenant produces no command, even when work item ids collide across tenants. Key-prefixing is not
  enough; the equality check is explicit in the pure selection step — the same cross-tenant fail-closed
  rule that governs parent attachment (see [Rules](#rules)).
- **Target aggregates decide outcomes.** Whether a descendant accepts, rejects, or no-ops the cascade
  command is owned by `WorkItemAggregate.Handle` through the lifecycle table — never by the translator.
  An active descendant transitions to the matching terminal status; an already-terminal descendant that
  receives the same-kind terminal command returns `DomainResult.NoOp` (no duplicate terminal event, no
  sequence burn); a cross-terminal command is a `WorkItemTransitionRejected`. These outcomes are the
  per-state Cancel/Expire decision (AR-13) and the idempotent no-op list in
  `docs/lifecycle-transition-matrix.md`, which remains the single source of truth — Story 3.6 adds no
  second cascade-specific transition table.
- **Idempotency lives on both sides.** The translator skips candidates explicitly marked terminal, but
  duplicate or redelivered intents stay safe regardless because the target terminal commands are
  idempotent. The roll-up projection reuses the descendant `WorkItemCancelled`/`WorkItemExpired` events
  to zero the now-terminal subtree, so the open subtree drops out of ancestors' rolled Remaining with no
  cascade-specific subtraction path.

Runtime cascade dispatch, checkpoint persistence, retry loops, durable continuation, AppHost restart
recovery, reminder reconciliation, and Aspire crash/recovery proof are **out of scope** for Story 3.6
and deferred to Story 4.6.
