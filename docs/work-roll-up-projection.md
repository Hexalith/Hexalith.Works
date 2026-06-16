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
- Same-unit subtrees therefore expose both `RolledRemaining` and a single labeled
  `RolledRemainingByUnit` entry. Heterogeneous subtrees expose one labeled `RolledRemainingByUnit`
  entry per unit and never coerce, convert, or sum incompatible units into an all-unit total.
- `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemExpired`, and `WorkItemRejected` with
  `Requeue: false` make the node terminal and contribute zero to ancestors. `WorkItemRejected` with
  `Requeue: true` rests at `Queued` and does not zero contribution.
- Parent/child edges may be discovered from either `WorkItemCreated.Parent` or `ChildSpawned`. Replaying
  the same edge is idempotent.
- Tenant equality is checked at every traversal hop. Cross-tenant edges are ignored and cannot affect a
  parent roll-up, even when work item ids collide across tenants.
- The write side rejects `ReportProgress` or `ReEstimate` commands whose unit disagrees with an
  established effort unit before any `ProgressReported` or `ReEstimated` event is emitted.

## Heterogeneous Unit Safety

The projection keeps the same unit-safety rule as a read-side defense-in-depth check. If a persisted
`ProgressReported` or `ReEstimated` event arrives after a node has an established unit and the event's
unit disagrees, the projection refuses that contribution. It retains the last valid projected effort,
marks the affected read model as `Degraded`, and exposes a deterministic `RollUpProjectionDiagnostic`.

Diagnostics are metadata only: tenant id, work item id, event type name, and aggregate-local sequence.
They deliberately exclude payload values such as done delta, estimate, unit, or note. A runtime adapter
can log those diagnostics later; the pure projection itself performs no logging or I/O.

A degraded read model means "last valid value retained and flagged", not "freshly converged". Degraded
state is re-derived during replay from ordered event facts, so duplicate and out-of-order delivery of the
same invalid event converges to the same retained value and diagnostics. Terminal state still takes
precedence for contribution: a terminal node contributes zero even if it previously refused an
incompatible event.

## Boundaries

The projection is pure code in `Hexalith.Works.Projections` and references only Works contracts. It does
not read EventStore, repositories, files, clocks, Dapr, runtime configuration, UI, routing, LLM services,
or cost-governance services. Runtime projection adapter wiring remains outside this story.
