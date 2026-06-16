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
