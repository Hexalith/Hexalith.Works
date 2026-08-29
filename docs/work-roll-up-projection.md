# Work Roll-Up Projection

Story 3.3 introduces recursive remaining-effort roll-up as eventual projection state. The aggregate
continues to own only its synchronous `WorkItemState.Remaining` value; consumers that need subtree
totals read the projection read model instead.

## Rules

- Projection input includes the dispatch tenant id, work item id, canonical EventStore envelope
  `SequenceNumber`, and the concrete work item event payload.
- Each node stores accepted event facts by envelope `SequenceNumber` and rebuilds node effort in that
  persisted order. Duplicate envelope positions are ignored, so repeated deliveries converge to the
  same state. A success payload's `Sequence` is only its state-changing ordinal and is not the
  projection delivery key. Rejections still occupy positions in the persisted stream, but the Works
  roll-up allowlist filters them as no state change, so they are not retained in the node event map and
  do not advance `LatestAcceptedSourceSequence`. That watermark is the latest accepted state-changing
  delivery's envelope position, not the full stream high-watermark; gaps from filtered rejections are
  expected.
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
- Exposed `ChildWorkItemIds` are ordered ordinally by `WorkItemId.Value`, so replay permutations and
  duplicate deliveries produce the same public sequence. `ExposedChildCount` is a derived getter, not
  an independent constructor input, and is always the size of that filtered sequence, including children
  that expose no numeric effort contribution. Web JSON continues to emit it as `exposedChildCount`.
- Tenant equality is checked at every traversal hop. Cross-tenant edges are ignored and cannot affect a
  parent roll-up, even when work item ids collide across tenants.
- The write side rejects `ReportProgress` or `ReEstimate` commands whose unit disagrees with an
  established effort unit before any `ProgressReported` or `ReEstimated` event is emitted.

## Heterogeneous Unit Safety

The projection keeps the same unit-safety rule as a read-side defense-in-depth check. If a persisted
`ProgressReported` or `ReEstimated` event arrives after a node has an established unit and the event's
unit disagrees, the projection refuses that contribution. It retains the last valid projected effort,
marks the affected read model as `Degraded`, and exposes a deterministic `RollUpProjectionDiagnostic`.

Diagnostics are metadata only: tenant id, work item id, event type name, and the state-changing Works
payload `Sequence` ordinal that the projection refused. They do not claim to be EventStore envelope
positions.
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
or cost-governance services. Its recursive convergence is valid when all contributing event streams are
co-available to the same projection instance.

The runtime `/project` adapter receives only one aggregate stream per dispatch. A parent's `ChildSpawned`
facts can establish spawn-time child contributions, but later child progress is delivered separately and cannot
reconcile that parent instance. At this adapter boundary, a model with `ExposedChildCount > 0`—or a request
containing a `ChildSpawned` event type that could not be decoded or accepted—keeps its own effort, lifecycle
status, parent/child structure, tenant, diagnostics, and freshness watermark, while `RolledRemaining` and
`RolledRemainingByUnit` are exposed and persisted as unavailable (`null` / empty). This 2026-08-27 refusal
decision does not weaken or change the pure projection's co-available recursive behavior.

The operator-triggered shared rebuild supplies a sealed tenant inventory to one co-available projection
instance through EventStore's internal `/project/rebuild/shared/v1` lifecycle. It reconstructs relationships
from both `ChildSpawned` and `WorkItemCreated.Parent`, sanitizes incomplete evidence conservatively, and stages
schema-v2 tenant-index and per-item documents as one bounded single-store manifest. Commit exposes that manifest
atomically and retires candidate-known legacy keys; abort and retry never expose staged values. Readers continue
using legacy keys until the v2 membership index is committed, then refuse every per-item document not listed by
that authoritative index.
Operators must keep ordinary projection delivery quiesced from authoritative inventory capture through Commit,
or provide an equivalent platform fence, and resume/catch up delivery afterward. The Works rebuild handler does
not arbitrate a concurrent live writer against the manifest's `LastWrite` operations.

After Commit, an ordinary single-aggregate dispatch cannot re-observe a child that only named its parent in
`WorkItemCreated` — that child appends nothing to the parent's stream. The adapter therefore reads the
aggregate's persisted document for the active generation, retains any reconciled child identities the current
replay cannot derive, and refuses both rolled shapes for that dispatch. A reconciled document is never
overwritten with a single-aggregate substitute total, and no Works event detaches a child, so retained
structure can only become more complete. A known Works event that cannot be decoded is treated the same way:
incomplete evidence refuses the rolled shapes instead of publishing an unprovable number.

Persisted roll-ups accept only an incoming model whose `LatestAcceptedSourceSequence` is not older than the
currently stored model. The adapter applies that comparison inside `ReadModelWritePolicy`'s ETag reload/retry
loop; it has no unconditional-save fallback. Equal-sequence replay remains allowed so deterministic documents
written before a projection-shape correction can be refreshed. The roll-up is written before the separate
tenant what's-next index and acts as the first ordering guard: when the policy returns a strictly newer stored
roll-up, that stale dispatch skips its index write. This ordering does not make the two keys atomic; each key is
independently monotonic and converges through later replay.
