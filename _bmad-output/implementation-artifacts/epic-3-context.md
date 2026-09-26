# Epic 3 Context: Coordinate Durable Work Trees and Sagas

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Coordinators can decompose work into tenant-safe trees, see reliable recursive progress, suspend work until a matching condition arrives, and propagate cancellation or expiry through active descendants. Cross-aggregate steps are durable and eventually consistent so retries, reordered delivery, and process restarts do not create contradictory work or lose obligations.

## Stories

- Story 3.1: Guard Tenant-Safe Work Tree Shape
- Story 3.2: Spawn Child Work from a Parent
- Story 3.3: Maintain Recursive Roll-Up with Per-Child Sequence
- Story 3.4: Preserve Heterogeneous Unit Subtotals
- Story 3.5: Suspend and Resume on Await-Conditions
- Story 3.6: Cascade Terminal Work Through Active Descendants

## Requirements & Constraints

- Each Work Item has at most one parent. An authoritative tree is acyclic and single-tenant; invalid edges reject without changing topology or creating a child. Maximum depth defaults to 32 and is tenant-configurable. The domain imposes no separate breadth cap beneath the platform topology quota.
- Child creation requires an admitted reservation. A child belongs to its parent's tenant, holds the parent by reference, and enters authoritative tree traversal, Roll-Up, and cascade only after durable child-created evidence attaches the edge. Released or superseded reservations cannot be revived by delayed commands.
- Rolled Remaining is the sum of each attached descendant's own effective contribution, counted once. Terminal items contribute zero even if historical Remaining is retained. Active unestimated items contribute zero plus an explicit unestimated count. Own Remaining and eventual rolled Remaining must remain distinct.
- Roll-Up must converge under duplicate, reordered, and concurrent delivery; a later descendant stream position replaces its prior contribution. Preserve separate subtotals by Unit, never silently convert or add incompatible Units. An established item Unit is immutable; incompatible contributions fail closed rather than publishing a fresh mixed total.
- An InProgress item may suspend on multiple conditions. The first exact kind-and-key match resumes it and clears the whole set. Progress while Suspended and nonmatching resumes reject; replay of the consumed condition alone is a no-op. Child completion and date triggers enter as commands, with external-signal adapters deferred.
- Cancellation and expiry cascade only through Attached, still-active descendants. Each target applies its own transition rules; duplicate terminal commands add no duplicate events. Cross-aggregate effects remain eventual, including legitimate descendant activity before its cascade command arrives.

## Technical Decisions

- The Work-Tree Registry is the sole topology authority. Its edge lifecycle is `Reserved → Creating → Attached` or `Reserved → Released`; `Creating` is retained for recovery rather than timeout-released. Parent `SpawnChild` is restricted, idempotent bookkeeping after attachment, not the public creation act.
- Work Item aggregates keep pure, single-aggregate `Handle` logic and event-sourced state. The Works Reactor translates committed events and explicit process state into mechanical commands; EventStore supplies dispatch, effect receipts, projection merge, checkpoints, retry, and reminder mechanics. Platform supplies trusted tenant policy, identity, and runtime operations.
- Roll-Up uses absolute contributions from folded Work Item state, with descendant slots keyed by that item's EventStore envelope sequence and attachment watermarks from the Registry. EventStore merges concurrent slots durably; stale contributions and removed edges cannot resurrect totals. Repair or rebuild must label unavailable or stale results rather than expose partial data as fresh.
- Cross-aggregate commands need deterministic effect identity and durable target disposition so retries replay the original success, rejection, or no-op. Recovery must retain unresolved work, validate stream page boundaries, and degrade readiness while coordination is stranded. Aggregate handlers never read a clock or external adapter.

## UX & Interaction Patterns

- Future tree surfaces show only Attached edges. Reserved and Creating work appears as pending coordination evidence outside the authoritative tree; released, superseded, and rejected legs remain explainable.
- Show own Remaining, rolled per-Unit totals, unestimated count, and freshness separately. A missing trustworthy Roll-Up says unavailable rather than zero. Display every active Await-Condition and explain pending resume or cascade without claiming another aggregate has already changed.

## Cross-Story Dependencies

- Tree admission and attachment establish the edges used by Roll-Up, child-completion resume, and cascade; attachment must backfill the child's latest valid contribution when its events arrived first.
- Durable Reactor delivery, effect identity, reminders, topology reads, and recovery depend on published EventStore seams and Platform identity/policy integration. The later `F3-*` decompositions in the epics file remain non-executable planning candidates until given unique story IDs and validated traceability.
