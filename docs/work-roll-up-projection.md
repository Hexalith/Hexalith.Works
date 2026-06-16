# Work Roll-Up Projection

Story 3.3 introduces recursive remaining-effort roll-up as eventual projection state. The aggregate
continues to own only its synchronous `WorkItemState.Remaining` value; consumers that need subtree
totals read the projection read model instead.

## Rules

- Projection input includes the dispatch tenant id, work item id, aggregate-local sequence, and the
  concrete work item event payload.
- Each node stores accepted event facts by aggregate-local sequence and rebuilds node effort in sequence
  order. Duplicate sequence deliveries are ignored, so repeated events converge to the same state.
- Parent totals are recomputed from latest child node state, never by applying additive child deltas.
- `OwnRemaining` and `RolledRemaining` are distinct contract types. `OwnRemaining` is the node's own
  effort only; `RolledRemaining` is eventual read-model state for the subtree.
- A rolled single value is exposed only when all numeric contributions share one unit. Mixed units are
  exposed through per-unit values and the single rolled field stays unavailable.
- `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemExpired`, and `WorkItemRejected` with
  `Requeue: false` make the node terminal and contribute zero to ancestors. `WorkItemRejected` with
  `Requeue: true` rests at `Queued` and does not zero contribution.
- Parent/child edges may be discovered from either `WorkItemCreated.Parent` or `ChildSpawned`. Replaying
  the same edge is idempotent.
- Tenant equality is checked at every traversal hop. Cross-tenant edges are ignored and cannot affect a
  parent roll-up, even when work item ids collide across tenants.

## Boundaries

The projection is pure code in `Hexalith.Works.Projections` and references only Works contracts. It does
not read EventStore, repositories, files, clocks, Dapr, runtime configuration, UI, routing, LLM services,
or cost-governance services. Runtime projection adapter wiring remains outside this story.
