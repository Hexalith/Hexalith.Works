---
baseline_commit: eaeaf2e
---

# Story 3.3: Maintain Recursive Roll-Up with Per-Child Sequence

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an objective owner,
I want a parent Work Item to expose rolled remaining effort across its subtree,
so that I can trust the all-in remaining effort of an objective as descendants progress.

## Acceptance Criteria

1. **Given** a Work Tree has parent and child Work Items
   **When** child progress, re-estimate, completion, or terminal events are projected
   **Then** the parent exposes own Remaining and subtree rolled Remaining
   **And** rolled Remaining equals own Remaining plus the recursive rolled Remaining of direct children.

2. **Given** child events are delivered more than once
   **When** the Roll-Up projection processes duplicates
   **Then** the projection does not double-count child contribution
   **And** the projected value converges to the same result as a single delivery.

3. **Given** child events arrive out of order
   **When** the Roll-Up projection compares child event sequences
   **Then** stale or lower-sequence contributions are ignored
   **And** the latest per-child contribution wins.

4. **Given** a child Work Item becomes terminal through completion, cancellation, rejection, or expiry
   **When** the Roll-Up projection processes the terminal child event
   **Then** that child contributes `0` Remaining to its ancestors
   **And** replaying the terminal event does not double-subtract the contribution.

5. **Given** roll-up state is exposed to consumers
   **When** read-model contracts are inspected
   **Then** own Remaining and rolled Remaining use distinct fields or types
   **And** no consumer can confuse eventual rolled Remaining with aggregate-authoritative own Remaining.

6. **Given** roll-up correctness is tested
   **When** property-style tests permute and duplicate child events
   **Then** all permutations converge to the same projection result
   **And** tenant equality is asserted at every traversal hop.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile existing state, contracts, and projection boundaries before editing (AC: #1-#6)**
  - [x] Read `src/Hexalith.Works.Contracts/State/WorkItemState.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemCreated.cs`,
    `src/Hexalith.Works.Contracts/Events/ChildSpawned.cs`,
    `src/Hexalith.Works.Contracts/Events/ProgressReported.cs`,
    `src/Hexalith.Works.Contracts/Events/ReEstimated.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemCompleted.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemCancelled.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemRejected.cs`,
    `src/Hexalith.Works.Contracts/Events/WorkItemExpired.cs`,
    `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs`,
    `src/Hexalith.Works.Projections/WorksProjectionsAssembly.cs`,
    `docs/eventstore-api-surface-constraints.md`, and `docs/work-tree-shape-guard.md`.
  - [x] Preserve the aggregate boundary: `WorkItemState.Remaining` remains aggregate-authoritative own
    Remaining only; do not add rolled Remaining, ancestor totals, projection caches, or query state to
    `WorkItemState` or `WorkItemAggregate`.
  - [x] Preserve Story 3.2 parent/child semantics: parent state stores only spawned child ids, child
    aggregate creation stores `ParentWorkItemReference`, and the projection asserts tenant equality even
    though spawn already guards the edge.
  - [x] Do not add Dapr, EventStore.Server, repository reads, filesystem, clocks, generated ids, runtime
    configuration, UI, routing, LLM, or cost-governance dependencies to `Contracts`, `Server`, or
    `Projections`.

- [x] **Task 2 - Add explicit roll-up read-model contracts (AC: #1, #5)**
  - [x] Add consumer-facing read-model types under `src/Hexalith.Works.Contracts/Models` or the closest
    existing Contracts read-model folder if one is introduced during implementation.
  - [x] Expose own Remaining and rolled Remaining as distinct properties and preferably distinct value
    types, for example `OwnRemaining` and `RolledRemaining`, each carrying `Unit` with nullable or absent
    state for unestimated items.
  - [x] Include enough metadata for safe consumers: `TenantId`, `WorkItemId`, `Status`, optional
    `Parent`, child contribution count or child ids if needed for tests, and the latest accepted source
    sequence for the node.
  - [x] Keep the contract ready for Story 3.4 without implementing full heterogeneous subtotals here:
    never expose a single fabricated value that can mix incompatible units. If the implementation sees
    mixed units before Story 3.4, keep them separate or mark rolled single-value unavailable rather than
    coercing them.
  - [x] Do not decorate read-model contracts as polymorphic command/event payloads unless the existing
    contract pattern requires it. Commands/events/rejections stay the v1 durable payload catalog.

- [x] **Task 3 - Implement a pure per-child-sequence roll-up strategy in Projections (AC: #1-#4, #6)**
  - [x] Add the roll-up projection implementation under `src/Hexalith.Works.Projections`, using folders
    such as `Handlers`, `Strategies`, and `Models` only as needed.
  - [x] Model projection input with enough delivery facts to enforce idempotency: dispatch `TenantId`,
    aggregate `WorkItemId`, aggregate-local event `Sequence`, and the concrete event payload.
  - [x] Maintain node state for own effort from `WorkItemCreated.InitialEffort`, `ProgressReported`,
    `ReEstimated`, and terminal events. Own Remaining is `null` or absent when the item is unestimated,
    and terminal success events force contribution to `0`.
  - [x] Maintain child contribution state as `(child tenant, child id, last observed child sequence,
    child rolled contribution with Unit)`. A duplicate or lower sequence from the same child must be
    ignored, not applied as a delta.
  - [x] Recalculate rolled Remaining for unit-compatible values as
    `own Remaining + sum(latest direct child rolled Remaining)`, recursively propagating recalculated
    child contributions to ancestors. If units differ, preserve them separately or mark single-value
    roll-up unavailable; do not coerce units.
  - [x] Establish parent/child edges from `WorkItemCreated.Parent` and `ChildSpawned` without embedding
    child state in the parent aggregate. If the same edge is seen twice, treat it as idempotent.
  - [x] If child events arrive before the edge or before the parent node exists, store enough pending
    node state to converge when the edge is later observed. Do not require delivery order to be parent
    first.
  - [x] Fail closed on tenant mismatch at every edge traversal: no cross-tenant child contribution may
    affect a parent projection. Tests must prove colliding work ids in two tenants cannot leak totals.
  - [x] Unknown or irrelevant events should be ignored only when they cannot affect roll-up. Do not throw
    on current non-roll-up lifecycle events such as assignment, queue, claim, suspend, resume, or
    reschedule.

- [x] **Task 4 - Handle lifecycle and terminal contribution rules precisely (AC: #1, #4)**
  - [x] `ProgressReported` applies only if its sequence is newer for that child and its unit matches the
    established effort unit; use the existing `WorkItemEffort.Report` semantics for clamping to zero.
  - [x] `ReEstimated` applies only if newer; for unestimated items it establishes the first estimate/unit,
    and for estimated items it preserves unit and re-derives Remaining using existing
    `WorkItemEffort.ReEstimate` semantics.
  - [x] `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemExpired`, and
    `WorkItemRejected(Requeue: false)` make that work item terminal and contribute zero to ancestors.
  - [x] `WorkItemRejected(Requeue: true)` is raw-act evidence that rests at `Queued`; it is not terminal
    and must not zero contribution.
  - [x] A terminal event replayed twice or delivered after a lower-sequence non-terminal event must leave
    the same zero contribution. A stale non-terminal event after a terminal event must not resurrect
    contribution.

- [x] **Task 5 - Add projection and contract-flow tests (AC: #1-#5)**
  - [x] Add focused unit tests under `tests/Hexalith.Works.UnitTests` or a new projection-focused test
    file to cover one parent with one child, parent with nested descendant, own vs rolled Remaining
    distinction, and propagation to every ancestor.
  - [x] Cover duplicate delivery: applying the same child event twice yields the same result as applying
    it once.
  - [x] Cover out-of-order delivery: a higher child sequence wins, lower child sequences are ignored, and
    replay converges when events are later presented in natural order.
  - [x] Cover all terminal zero-contribution events: completed, cancelled, expired, rejected with
    `Requeue: false`; cover `Requeue: true` as non-terminal.
  - [x] Cover edge materialization from both `WorkItemCreated.Parent` and `ChildSpawned` so Story 3.2's
    parent-emitted spawn fact and child creation fact both converge on the same tree.
  - [x] Cover tenant isolation with same/colliding work ids in different tenants and an attempted
    cross-tenant edge. The projection must not leak totals across tenants.
  - [x] Cover unestimated items: an unestimated child contributes no numeric Remaining until
    `ReEstimated` establishes an effort; terminal unestimated children contribute zero.

- [x] **Task 6 - Add property-style convergence coverage (AC: #2, #3, #6)**
  - [x] Replace or extend `tests/Hexalith.Works.PropertyTests/ScaffoldPropertyTests.cs` with a real
    FsCheck property for roll-up convergence.
  - [x] Generate small tenant-safe work trees with bounded depth/fan-out, event sequences for child
    progress/re-estimate/terminal events, and injected duplicates.
  - [x] Assert every permutation and duplicate-expanded delivery set converges to the same projected
    rolled result as canonical sequence-order replay.
  - [x] Include at least one generated or fixed case with nested descendants so the property proves
    recursive propagation, not only direct child summing.
  - [x] Keep the property deterministic enough for CI: fixed seed or bounded generator sizes, no sleeps,
    no clocks, no random generation inside production projection code.

- [x] **Task 7 - Update architecture fitness and documentation (AC: #1-#6)**
  - [x] Update `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`:
    `P0_WorkItemSliceDoesNotIntroduceDeferredBurnDownRollUpOrReminderBehavior` currently treats
    `RollUp` as deferred. Story 3.3 must revise the guard so roll-up is allowed only in the intended
    Contracts/Projections/tests/docs locations while reminders remain deferred.
  - [x] Keep `P0_WorkItemKernelRemainsPure` green; if new projection files introduce banned terms or
    namespaces, fix the implementation rather than weakening purity.
  - [x] Add or update a concise doc, for example `docs/work-roll-up-projection.md`, recording the
    per-child-sequence LWW rule, tenant equality assertion, terminal zero contribution, and the own vs
    rolled Remaining distinction.
  - [x] Update `_bmad-output/implementation-artifacts/tests/test-summary.md` after implementation with
    final test counts and commands.
  - [x] Preserve Hexalith dependency policy: use `ProjectReference` for Hexalith libraries through root
    submodule path variables, do not add Hexalith package references, and do not initialize nested
    submodules.

- [x] **Task 8 - Build and verify the slice (AC: #1-#6)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
  - [x] Run direct xUnit v3 binaries if `dotnet test` is blocked by Microsoft.Testing.Platform named-pipe
    permissions:
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    and `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.

## Dev Notes

### Scope Boundary

Story 3.3 owns the first real recursive remaining-effort roll-up projection. It realizes FR-11 and the
roll-up portion of FR-5/FR-13 by deriving eventual read-model state from Work Item events. It must not
move rolled totals into the aggregate, because own Remaining and status remain synchronous,
aggregate-authoritative values while rolled Remaining is eventual projection state. [Source:
_bmad-output/planning-artifacts/epics.md#Story 3.3: Maintain Recursive Roll-Up with Per-Child Sequence;
_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-11]

**In scope:** pure projection strategy/handler, explicit roll-up read-model contracts, per-child
sequence last-writer-wins accounting, duplicate/out-of-order convergence, terminal zero contribution,
tenant-equality guardrails, architecture-fitness update for roll-up now being owned scope, docs, and
unit/property coverage.

**Out of scope:** full heterogeneous-unit subtotal UX and contract hardening beyond "never coerce"
(Story 3.4), suspend/resume matching (Story 3.5), cascade traversal/runtime command emission (Story
3.6), query-side authorization endpoints, "what's next" ordering, production SignalR/UI/channel
adapters, Dapr projection actors inside Works kernel, EventStore runtime wiring, cost roll-up, routing,
LLM, and cost governance. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.4; #Story 3.5;
#Story 3.6]

### Current State (files this story modifies or verifies - read before editing)

- `src/Hexalith.Works.Projections/WorksProjectionsAssembly.cs` is only a marker type today; there is no
  roll-up implementation yet.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` stores own aggregate state: `TenantId`,
  `WorkItemId`, `Status`, monotonic success-event `Sequence`, `InitialEffort`, derived own
  `Remaining`, optional `Parent`, spawned child ids, and await conditions. Its comment explicitly says
  roll-up was deferred. Do not turn this state into a read model.
- `WorkItemCreated` carries initial own effort and optional `ParentWorkItemReference`. Child creation
  from Story 3.2 should produce this event on the child stream with the parent's tenant/id reference.
- `ChildSpawned` is emitted on the parent stream and carries the child id plus child creation facts. It
  is useful for discovering intended edges early, but the child's own stream remains the source of truth
  for child progress/re-estimate/terminal state.
- `ProgressReported` carries `DoneDelta` and `Unit`, not a precomputed Remaining. Projection code must
  maintain own effort state to derive Remaining, using the same clamping semantics as `WorkItemEffort`.
- `ReEstimated` carries a new absolute estimate and `Unit`; the existing aggregate preserves unit and
  clamps done when estimates shrink.
- `WorkItemCompleted`, `WorkItemCancelled`, `WorkItemExpired`, and `WorkItemRejected(Requeue: false)`
  are terminal for projection contribution. `WorkItemRejected(Requeue: true)` is non-terminal and rests
  at `Queued`.
- `tests/Hexalith.Works.PropertyTests/ScaffoldPropertyTests.cs` only proves FsCheck is available. Story
  3.3 should replace or extend it with real convergence properties.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` currently forbids
  `RollUp` in `src` because earlier stories deferred it. Update this guard deliberately as part of this
  story.
- `docs/eventstore-api-surface-constraints.md` records that EventStore rebuild is checkpoint-based and
  pausable, not shadow-projection plus atomic swap. Do not reintroduce the older architecture wording as
  implementation truth.

### Key Design Decisions

- **D1 - Per-child sequence LWW, never additive deltas.** Store each direct child's latest contribution
  by child id and child aggregate sequence. Recompute totals from latest known values. This prevents
  duplicates and stale deliveries from corrupting totals. [Source:
  _bmad-output/planning-artifacts/architecture.md#Architectural Risk & Assumption Stress-Test]
- **D2 - Roll-up is eventual projection state.** The aggregate owns own Remaining and status. The
  projection owns rolled Remaining. A projection must never complete, cancel, resume, or otherwise drive
  aggregate lifecycle decisions. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-11]
- **D3 - Terminal child contribution is assignment to zero.** Do not subtract a terminal child's previous
  contribution. Set the child's latest contribution to zero at the terminal sequence, then propagate the
  recalculated value. This is the only replay-safe approach under duplicate terminal events. [Source:
  _bmad-output/planning-artifacts/epics.md#Story 3.3]
- **D4 - Tenant equality is checked again on the read side.** Story 3.1/3.2 guard writes, but projection
  traversal must still assert same-tenant parent/child hops so rebuild cannot leak totals from malformed
  or legacy data. [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-13;
  docs/work-tree-shape-guard.md#Rules]
- **D5 - Heterogeneous unit coercion is forbidden.** Story 3.4 owns full per-unit subtotal behavior, but
  Story 3.3 must not create a consumer-visible field that silently sums hours with points/interactions.
  Keep values unit-tagged and separate or mark single rolled value unavailable until Story 3.4 expands it.
  [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-12;
  _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Behavioral]

### Technical Requirements

- Keep `Contracts`, `Server`, and `Projections` infrastructure-free and pure. No Dapr, EventStore.Server,
  HTTP, filesystem, timers/clocks, generated IDs, UI, LLM, routing, or cost-governance dependency in the
  kernel. [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries]
- `Projections` may reference `Contracts` only. If a runtime adapter later needs EventStore.DomainService
  `IDomainProjectionHandler`, that wiring belongs outside this story unless implemented without violating
  the existing dependency direction fitness tests. [Source:
  tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs]
- Use xUnit v3 + Shouldly for unit/integration tests and FsCheck for property tests. Keep tests pure and
  deterministic; direct xUnit v3 executables are the reliable sandbox verification path when `dotnet test`
  is blocked. [Source: _bmad-output/implementation-artifacts/3-2-spawn-child-work-from-a-parent.md#Debug Log References]
- Do not add new package dependencies for roll-up. The repository already pins .NET SDK `10.0.301`,
  xUnit v3 `3.2.2`, Shouldly `4.3.0`, and FsCheck `3.3.3`. [Source: global.json;
  Directory.Packages.props]
- Additive schema evolution still applies if any public contract is introduced: no field removals, no
  event/command renames, no `V2` durable payloads. Read-model contracts should be serialization-friendly
  but should not be added to the polymorphic command/event catalog unless required by existing patterns.
  [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-12]

### Project Structure Notes

- New read-model contracts belong in `src/Hexalith.Works.Contracts/Models` unless the implementation
  first establishes a more specific existing-pattern folder under Contracts.
- New projection implementation belongs under `src/Hexalith.Works.Projections`, likely `Handlers`,
  `Strategies`, and small projection-owned model folders.
- Aggregate code in `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` should not need changes
  for roll-up. If implementation pressure suggests changing it, re-check the scope boundary first.
- Focused projection unit tests belong under `tests/Hexalith.Works.UnitTests`; convergence properties
  belong under `tests/Hexalith.Works.PropertyTests`.
- Architecture fitness changes belong under
  `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` and should narrow the
  old deferred-roll-up ban rather than weakening kernel purity.
- Do not create `.UI`, `.Mcp`, portal, `.Security`, production channel adapter, database, Dapr actor,
  repository, or runtime host projects for this story.

### Previous Story Intelligence

- Story 3.2 completed in commit `eaeaf2e` and introduced `SpawnChild`, `ChildSpawned`,
  `AwaitCondition`, parent replay of spawned child ids, and child creation facts equivalent to
  `CreateWorkItem`.
- Story 3.2's final QA pass ended at **386** green tests: UnitTests 307, IntegrationTests 52,
  ArchitectureTests 26, PropertyTests 1. Use that as the baseline when updating test-summary after 3.3.
- Story 3.2 proved `ChildSpawned` replay is idempotent for duplicate child ids and that spawn/tree guard
  rejections burn no parent sequence. Roll-up should build on those events, not revalidate tree shape by
  reading EventStore or projections from the aggregate.
- The current reliable verification path remains restore/build followed by direct xUnit v3 binaries:
  UnitTests, IntegrationTests, ArchitectureTests, then PropertyTests.
- The working tree already has unrelated modified submodule pointers (`Hexalith.FrontComposer`,
  `Hexalith.Parties`) and a story-automator orchestration file. Do not revert or depend on those changes
  for Story 3.3.

### Git Intelligence Summary

- Recent commits show focused story slices with additive contracts and tests:
  `eaeaf2e feat(story-3.2): Spawn child work from a parent`,
  `5792291 feat(story-3.1): Guard tenant-safe work tree shape`,
  `c1ba6bb test(story-2.5): Verify terminal work decisions`,
  `1814301 feat(story-2.4): Re-estimate and reschedule work`.
- Story work has consistently updated the story file, sprint status, test summary, docs, contract/unit
  tests, architecture fitness tests, and integration/golden tests only when public durable payloads change.

### Latest Technical Information

- No new third-party library or framework version is required for Story 3.3. Use the repository-pinned
  stack: .NET SDK `10.0.301` with `rollForward: latestPatch`, Aspire `13.4.3`, Dapr packages `1.18.2`,
  xUnit v3 `3.2.2`, Shouldly `4.3.0`, and FsCheck `3.3.3`.
- Do not upgrade SDK, Dapr, Aspire, FsCheck, xUnit, Shouldly, or EventStore dependencies as part of this
  story.
- Local EventStore source shows the platform `/project` seam is stateless full-replay per aggregate via
  `IDomainProjectionHandler`, while `docs/eventstore-api-surface-constraints.md` records checkpoint-based
  rebuild behavior. Implement the Works roll-up as pure domain projection logic first; only add runtime
  adapter wiring if it fits the existing dependency-direction tests without pulling infrastructure into
  the kernel. [Source: Hexalith.EventStore/src/Hexalith.EventStore.DomainService/IDomainProjectionHandler.cs;
  docs/eventstore-api-surface-constraints.md]

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.3: Maintain Recursive Roll-Up with Per-Child Sequence]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-11]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-12]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-13]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#Cross-Cutting NFRs]
- [Source: _bmad-output/planning-artifacts/architecture.md#Architectural Risk & Assumption Stress-Test]
- [Source: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Behavioral]
- [Source: docs/eventstore-api-surface-constraints.md#Online Rebuild]
- [Source: docs/work-tree-shape-guard.md#Rules]
- [Source: _bmad-output/implementation-artifacts/3-2-spawn-child-work-from-a-parent.md#Previous Story Intelligence]
- [Source: AGENTS.md#Hexalith library references]
- [Source: AGENTS.md#Submodule rules]

## Dev Agent Record

### Agent Model Used

GPT-5

### Debug Log References

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — passed with 0 warnings and 0 errors.
- `DOTNET_CLI_HOME=/tmp tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — 325/325 passed.
- `DOTNET_CLI_HOME=/tmp tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` — 52/52 passed.
- `DOTNET_CLI_HOME=/tmp tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` — 26/26 passed.
- `DOTNET_CLI_HOME=/tmp tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — 1/1 passed; FsCheck executed 100 generated cases.
- `DOTNET_CLI_HOME=/tmp dotnet build tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj -c Release -p:NuGetAudit=false -m:1 -v minimal` — passed after review auto-fix.

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- Added explicit `OwnRemaining`, `RolledRemaining`, and `WorkItemRollUp` read-model contracts so consumers can distinguish aggregate-authoritative own effort from eventual subtree roll-up state.
- Implemented a pure `WorkItemRollUpProjection` strategy that accepts delivery facts, handles duplicate and out-of-order aggregate events by sequence, discovers edges from `WorkItemCreated.Parent` and `ChildSpawned`, and recomputes recursive rolled remaining without additive child deltas.
- Enforced terminal zero contribution, requeued rejection non-terminal behavior, unit-safe roll-up exposure, unestimated item behavior, and tenant-equality checks at traversal.
- Added focused unit coverage and an FsCheck convergence property; updated the architecture guard and documentation for Story 3.3 roll-up scope.
- Review auto-fix strengthened the FsCheck property so it generates bounded tenant-safe tree shapes, nested descendants, duplicate/permuted delivery, terminal child cases, and cross-tenant colliding ids.

### File List

- `_bmad-output/implementation-artifacts/3-3-maintain-recursive-roll-up-with-per-child-sequence.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `docs/work-roll-up-projection.md`
- `src/Hexalith.Works.Contracts/Models/OwnRemaining.cs`
- `src/Hexalith.Works.Contracts/Models/RolledRemaining.cs`
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs`
- `src/Hexalith.Works.Projections/Models/WorkItemRollUpEvent.cs`
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`
- `tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj`
- `tests/Hexalith.Works.PropertyTests/ScaffoldPropertyTests.cs` (deleted)
- `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs`
- `tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj`
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs`

### Change Log

- 2026-06-16 — Implemented Story 3.3 recursive roll-up projection, contracts, tests, architecture guard, and documentation; validated 404/404 tests passing after QA gap-fill coverage.
- 2026-06-16 — Senior Developer Review auto-fixed property coverage gap for generated bounded tenant-safe trees; revalidated restore/build and 404/404 tests passing.

## Senior Developer Review (AI)

### Reviewer

Administrator on 2026-06-16

### Findings

- **Fixed [CRITICAL]** Task 6 claimed generated small tenant-safe work trees with bounded depth/fan-out, but `WorkItemRollUpConvergencePropertyTests` used one fixed tree and generated only duplicate/permuted delivery order. Updated the property to generate bounded child/grandchild tree shapes, child event sequences, duplicate/permuted delivery, optional terminal children, and a cross-tenant colliding child assertion.

### Outcome

Approved after auto-fix. No critical issues remain.

### Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — passed with 0 warnings and 0 errors.
- `DOTNET_CLI_HOME=/tmp tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — 325/325 passed.
- `DOTNET_CLI_HOME=/tmp tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` — 52/52 passed.
- `DOTNET_CLI_HOME=/tmp tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` — 26/26 passed.
- `DOTNET_CLI_HOME=/tmp tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — 1/1 passed; FsCheck executed 100 generated cases.
