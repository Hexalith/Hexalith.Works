# Test Automation Summary — Story 3.4 (Preserve Heterogeneous Unit Subtotals)

Workflow: `bmad-dev-story` followed by a `bmad-qa-generate-e2e-tests` QA gap-filling pass. Framework
reused: **xUnit v3 + Shouldly** for focused unit/architecture coverage and **FsCheck** for convergence
coverage. Story 3.4 hardens Story 3.3 roll-up behavior by making heterogeneous-unit subtotals explicit
and adding read-side fail-closed handling for already persisted unit-incompatible events. There is no
UI/HTTP surface for this story; the executable end-to-end path is command handling (write-side guard)
and projection delivery facts into the pure recursive roll-up strategy, then consumer read-model
inspection — that is the layer the QA pass targets.

Story 3.3 final baseline was **404** green tests (UnitTests 325, IntegrationTests 52,
ArchitectureTests 26, PropertyTests 1). The Story 3.4 dev-story pass added **+7** unit tests, **+1**
architecture fitness test, and extended the existing property test to generate mixed-unit trees plus
deterministic degraded-event convergence (subtotal **412**). The QA gap-filling pass then added **+3**
unit tests and **+1** architecture fitness test to close residual AC coverage gaps, raising the total to
**416** green tests.

## Gaps closed by the QA pass (`bmad-qa-generate-e2e-tests`)

All four were genuine, non-redundant gaps verified against the six acceptance criteria:

- [x] **AC #2 — within-unit summation vs cross-unit separation.** Every prior mixed-unit test had each
  Unit appear once. `Same_unit_children_sum_within_bucket_while_a_different_unit_child_stays_separate`
  proves two same-Unit children fold into one subtotal (5+3 ⇒ 8 hour) while a different-Unit child stays
  separate (4 point) and no coerced single value appears.
- [x] **AC #5 — sticky-degraded continuation.**
  `Degraded_node_refuses_only_the_bad_event_then_applies_a_later_matching_unit_event` proves fail-closed
  refuses *only* the unit-incompatible event, a later matching-unit progress still burns down from the
  last valid value, and the node stays degraded with exactly one re-derived diagnostic.
- [x] **AC #4 — write-side-to-read-side bridge.**
  `Rejected_unit_mismatched_command_emits_no_event_so_projection_stays_fresh_and_not_degraded` ties the
  aggregate rejection (no `ProgressReported` emitted) to the projection staying unchanged and **not**
  degraded — the end-to-end contrast with the AC #5 persisted-bad-event path.
- [x] **AC #5 — diagnostic metadata-only structural guard.**
  `P0_RollUpProjectionDiagnosticExposesOnlyMetadataNeverPayloadValues` locks `RollUpProjectionDiagnostic`
  to exactly `TenantId`, `WorkItemId`, `EventType`, `Sequence` so a future change cannot reintroduce a
  payload-bearing field — the drift guard mirroring the existing no-coerced-total guard.

## Gaps closed this run

### Contracts and projections

- [x] Added `RollUpProjectionDiagnostic` and additive `WorkItemRollUp.Degraded` /
  `ProjectionDiagnostics` read-model properties.
- [x] `WorkItemRollUpProjection` now refuses persisted `ProgressReported` and `ReEstimated` events whose
  unit disagrees with an established node unit, retaining the last valid value and surfacing
  metadata-only diagnostics.
- [x] Degraded state and diagnostics are re-derived during replay and remain pure data; no logging,
  clocks, ids, filesystem, Dapr, or runtime I/O were introduced.

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] Added explicit same-unit subtotal coverage: one unit produces one labeled subtotal and a non-null
  single `RolledRemaining`.
- [x] Added deeper three-unit mixed-tree coverage proving per-unit grouping and no coerced all-unit total.
- [x] Added same-unit progress/re-estimate mixed-tree coverage proving matching-unit updates leave other
  units untouched.
- [x] Added fail-closed coverage for unit-mismatched `ProgressReported` and `ReEstimated`, including
  retain-last-valid values, degraded marking, metadata-only diagnostics, and duplicate/out-of-order
  convergence.
- [x] Added establishment boundary and terminal precedence coverage.
- [x] Tightened command-side unit mismatch tests to assert no `ProgressReported`/`ReEstimated` event is
  emitted from rejected commands.

### Property tests (`tests/Hexalith.Works.PropertyTests`)

- [x] Extended the convergence property to generate heterogeneous-unit trees and compare
  `RolledRemainingByUnit`, `Degraded`, and `ProjectionDiagnostics` under duplicate/permuted delivery.
- [x] Included a deterministic post-establishment unit mismatch in generated scenarios and asserted the
  degraded outcome converges.

### Architecture and documentation

- [x] Updated `docs/work-roll-up-projection.md` with heterogeneous-unit semantics, command-side unit
  rejection, read-side fail-closed behavior, retain-last-valid degraded marking, and metadata-only
  diagnostic rules.
- [x] Architecture fitness remains inside the existing roll-up owned locations:
  `src/Hexalith.Works.Contracts/Models` and `src/Hexalith.Works.Projections`.

## Story 3.4 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **335/335** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **52/52** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **28/28** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed; FsCheck reported **100** generated cases.

### Story 3.4 Test Counts

| Suite | Story 3.3 Final | Story 3.4 dev-story | Story 3.4 + QA pass | Delta |
|-------|----------------:|--------------------:|--------------------:|------:|
| UnitTests | 325 | 332 | **335** | +10 |
| IntegrationTests | 52 | 52 | **52** | — |
| ArchitectureTests | 26 | 27 | **28** | +2 |
| PropertyTests | 1 | 1 | **1** | extended |
| **Total** | **404** | **412** | **416** | **+12** |

### Checklist

- [x] Same-unit and mixed-unit roll-up subtotals are explicitly verified.
- [x] Projection fail-closed behavior is covered for persisted incompatible progress and re-estimate
  events.
- [x] Degraded diagnostics expose only tenant, work item, event type, and sequence metadata.
- [x] Command-side unit mismatch guards are verified to emit no deliverable projection event.
- [x] Property coverage verifies heterogeneous-unit and degraded convergence under duplicate/permuted
  delivery.
- [x] Documentation, build, architecture tests, and all runtime test binaries passed.

---

# Test Automation Summary — Story 3.3 (Maintain Recursive Roll-Up with Per-Child Sequence)

Workflow: `bmad-qa-generate-e2e-tests` (QA gap-filling pass) after `bmad-dev-story`. Framework reused:
**xUnit v3 + Shouldly** for focused projection/unit coverage and **FsCheck** for convergence coverage.
Story 3.3 adds explicit roll-up read-model contracts, a pure recursive projection strategy,
tenant-safe traversal, duplicate/out-of-order convergence, terminal zero-contribution behavior,
mixed-unit single-value protection, and a narrowed architecture guard that permits roll-up only in the
owned contracts/projections locations. There is no UI/browser surface for this story; the executable
end-to-end path is projection delivery facts into the pure recursive roll-up strategy and consumer
read-model inspection.

Story 3.2 final baseline was **386** green tests (UnitTests 307, IntegrationTests 52,
ArchitectureTests 26, PropertyTests 1). Story 3.3 adds **+18** unit tests and replaces the scaffold
property with a real FsCheck convergence property, raising the total to **404** green tests.

## Gaps closed this run

### Contracts and projections

- [x] Added `OwnRemaining`, `RolledRemaining`, and `WorkItemRollUp` read-model contracts under
  `src/Hexalith.Works.Contracts/Models`.
- [x] Added `WorkItemRollUpEvent` delivery facts and `WorkItemRollUpProjection` pure strategy under
  `src/Hexalith.Works.Projections`.
- [x] Projection derives own remaining from create/progress/re-estimate events, assigns terminal
  contribution to zero, recomputes recursive rolled remaining, and exposes mixed units as per-unit
  values without fabricating a single total.
- [x] Tenant equality is checked at edge creation and traversal so cross-tenant/colliding ids cannot
  leak child totals.

### QA gap-fill tests (`tests/Hexalith.Works.UnitTests`)

- [x] Added child-before-edge convergence coverage so progress delivered before parent edge
  materialization still rolls up when `ChildSpawned` later establishes the relationship.
- [x] Added stale-after-terminal coverage proving a lower-sequence non-terminal child event cannot
  resurrect contribution after completion.
- [x] Added non-roll-up lifecycle event coverage for assignment, queue, claim, suspend, resume, and
  reschedule events so they remain tolerated without changing effort totals.
- [x] Added invalid delivery-fact coverage for non-positive sequence and tenant/payload mismatches.

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemRollUpProjectionTests` covers one parent/child, nested descendants, own-vs-rolled type
  distinction, duplicate delivery, out-of-order delivery, all terminal zero-contribution events,
  `Requeue: true` non-terminal behavior, edge materialization from both `WorkItemCreated.Parent` and
  `ChildSpawned`, child-before-edge delivery, stale-after-terminal ordering, ignored non-roll-up
  lifecycle events, invalid delivery facts, tenant isolation, mixed units, and unestimated items.

### Property tests (`tests/Hexalith.Works.PropertyTests`)

- [x] Replaced the scaffold FsCheck availability test with `WorkItemRollUpConvergencePropertyTests`.
- [x] FsCheck generates bounded tenant-safe tree shapes with nested descendants, optional terminal child
  events, duplicate/permuted delivery, and a cross-tenant colliding id; each generated case converges to
  canonical sequence-order replay without leaking foreign-tenant totals.

### Architecture and documentation

- [x] Updated `ScaffoldGovernanceTests` so roll-up is allowed only in Contracts read-models and
  Projections implementation/input code while reminders remain deferred.
- [x] Added `docs/work-roll-up-projection.md` describing the per-child latest-state rule, tenant
  equality assertion, terminal zero contribution, and own-vs-rolled remaining distinction.

## Story 3.3 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **325/325** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **52/52** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed; FsCheck reported **100** generated cases.

### Story 3.3 Test Counts

| Suite | Story 3.2 Final | Story 3.3 Final | Delta |
|-------|----------------:|----------------:|------:|
| UnitTests | 307 | **325** | +18 |
| IntegrationTests | 52 | **52** | — |
| ArchitectureTests | 26 | **26** | — |
| PropertyTests | 1 | **1** | replaced scaffold |
| **Total** | **386** | **404** | **+18** |

### Checklist

- [x] Read-model contracts distinguish own remaining from eventual rolled remaining.
- [x] Projection tests cover recursive propagation, duplicates, out-of-order delivery, terminal events,
  non-terminal requeue, tenant isolation, mixed units, and unestimated items.
- [x] Property coverage verifies convergence under generated tree shapes with duplicate/permuted delivery
  and tenant-collision noise.
- [x] Architecture fitness and documentation updated for Story 3.3 roll-up scope.
- [x] All validation commands passed.

---

# Test Automation Summary — Story 3.2 (Spawn Child Work from a Parent)

Workflow: `bmad-qa-generate-e2e-tests` (QA gap-filling pass) after `bmad-dev-story`. Framework reused:
**xUnit v3 + Shouldly**, pure aggregate unit tests, contract-flow integration tests, schema-evolution
golden corpus, and existing architecture/property guardrails. Story 3.2 adds `SpawnChild`,
`ChildSpawned`, parent replay of spawned child references, and the minimal child-completion await
condition used when a spawn suspends its parent. This is a pure `Contracts` + `Server` + docs slice
with **no UI/browser surface**, so the executable end-to-end path is **command →
`WorkItemAggregate.Handle` → durable event → concrete `JsonSerializerDefaults.Web` JSON → replayed
`WorkItemState`**; browser/UI E2E is **not applicable**.

The dev-authored Story 3.2 baseline was **375** green tests (UnitTests 296, IntegrationTests 52,
ArchitectureTests 26, PropertyTests 1). This QA run mapped the dev coverage against AC #1–#5 and the
recorded design decisions (D1–D5), discovered **5 genuine branch-level gaps**, and auto-applied
**+11** unit test cases (5 new test methods; two are Theories), raising the total to **386** green.
**No production code was changed** — only `WorkItemSpawnChildTests.cs` was extended.
`WorkItemV1Catalog.Count` remains **36** (14 success events, 14 commands, 8 rejection events) and the
`ChildSpawned.v1.json` golden fixture is unchanged — the new tests exercise existing durable types
through the aggregate and replay, not new wire shapes.

## Gaps auto-applied this QA run

Mapped against AC #1–#5, the dev baseline already covered spawn from `Created`, spawn-with-await from
`InProgress`, suspended-parent progress rejection, the four tree-guard rejection paths, missing/terminal
parent rejection, await-requires-`InProgress`, replay determinism, caller-supplied child ids, and the
full serialization/golden/legacy round-trips. The genuine gaps were uncovered **branches** and one
explicit **AC clause** — each closed below in `tests/Hexalith.Works.UnitTests/WorkItemSpawnChildTests.cs`:

- [x] `SpawnChild_without_suspension_is_accepted_from_every_live_status` (AC #1 / Task 4, Theory ×5) —
  plain spawn is **accepted** from `Created`, `Assigned`, `Queued`, `InProgress`, **and** `Suspended`,
  each emitting one `ChildSpawned` at `Sequence + 1`, replaying the child reference, and leaving the
  parent's lifecycle status unchanged. The baseline only proved acceptance from `Created`; for the other
  live statuses it proved only the *negative* (await-requires-`InProgress`), never that a plain spawn
  succeeds.
- [x] `SpawnChild_without_obligation_returns_missing_obligation_rejection_for_the_child` (AC #1
  CreateWorkItem semantics, Theory `null`/`""`/`"   "`) — a missing obligation returns
  `WorkItemCannotBeCreatedWithoutObligation` raised against the **child** id, rejection-only, with no
  `ChildSpawned`. This Handle branch had zero coverage (every prior test supplied an obligation).
- [x] `SpawnChild_tree_guard_rejection_is_replay_safe_and_burns_no_parent_sequence` (AC #4 "the
  rejection is replay-safe") — proves a rejected spawn mutates no parent state and **consumes no
  sequence number**: re-handling is deterministic, and a subsequent valid spawn still receives the next
  contiguous sequence. The explicit AC #4 replay-safety clause was previously unverified for spawn.
- [x] `SpawnChild_duplicate_event_replay_is_idempotent_and_distinct_children_accumulate_in_order`
  (AC #5 / Task 3 determinism) — applying the same `ChildSpawned` twice yields a single child reference
  (exercises the `Apply(ChildSpawned)` `Contains` dedup branch), while two distinct events accumulate
  both ids in order. The dedup branch and multi-child accumulation were untested.
- [x] `SpawnChild_retry_with_existing_child_parent_equal_to_proposed_parent_is_accepted` (D3 guard
  idempotency) — a retry whose `ExistingChildParent` equals the proposed parent is **accepted** (not a
  second-parent rejection), exercising the guard's same-parent-idempotency branch. The baseline's
  second-parent case supplied a *different* existing parent, so the accept-on-same-parent branch was
  uncovered.

## Gaps closed by the dev-story baseline

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemSpawnChildTests` covers successful spawn from an existing parent, replay of parent-owned
  child id references, and construction of equivalent child `CreateWorkItem` facts with the same tenant
  and `ParentWorkItemReference`.
- [x] Spawn with await intent emits `ChildSpawned` then `WorkItemSuspended`, advances parent sequences
  monotonically, and replays to `Suspended` with `AwaitCondition(childWorkItemId)`.
- [x] Suspended parents reject `ReportProgress` through the existing lifecycle rejection path.
- [x] Tree-guard rejection paths cover cross-tenant ancestor facts, self/cycle facts, second-parent
  facts, and max-depth overflow, each returning rejection-only results.
- [x] Missing/terminal parent states reject `SpawnChild`; await intent is accepted only from
  `InProgress`.
- [x] Replay determinism verifies sequence, status, child references, and await conditions reconstruct
  identically; caller-supplied child ids are preserved.

### Integration tests (`tests/Hexalith.Works.IntegrationTests`)

- [x] `WorkItemSpawnChildContractFlowTests` round-trips `SpawnChild`, `ChildSpawned`,
  `WorkItemSuspended.AwaitCondition`, and legacy `WorkItemSuspended` JSON without the new optional field.
- [x] `WorkItemV1Catalog` now includes `SpawnChild`, `ChildSpawned`, and an await-condition sample on
  `WorkItemSuspended` for polymorphic registration coverage.
- [x] `SchemaEvolutionGoldenCorpusTests` now freezes and round-trips `ChildSpawned.v1.json`, including
  additive unknown-field tolerance.

### Documentation

- [x] `docs/work-tree-shape-guard.md` records that `SpawnChild` reuses the pure caller-fed tree guard and
  stores only lightweight parent-owned child references.

## Story 3.2 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **307/307** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **52/52** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 3.2 Test Counts

| Suite | Story 3.1 Final | Dev 3.2 Baseline | QA Final | QA Delta |
|-------|----------------:|-----------------:|---------:|---------:|
| UnitTests | 278 | 296 | **307** | +11 |
| IntegrationTests | 46 | 52 | **52** | — |
| ArchitectureTests | 26 | 26 | **26** | — |
| PropertyTests | 1 | 1 | **1** | — |
| **Total** | **351** | **375** | **386** | **+11** |

### Checklist

- [x] API/contract tests cover spawn command/event flow, await-condition payloads, and envelope omission.
- [x] E2E/UI tests marked not applicable (Story 3.2 is pure Contracts + Server + docs; no UI/browser
  surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly).
- [x] Tests cover happy paths (spawn, spawn with await, replay, child-create facts).
- [x] Tests cover critical error/edge cases (missing/terminal parent, await from non-`InProgress`,
  cross-tenant ancestor, self/cycle, second parent, depth overflow).
- [x] All generated tests run successfully (386/386).
- [x] Tests use clear descriptions and semantic assertions.
- [x] No hardcoded waits or sleeps.
- [x] Tests are independent and arrange their own facts/state.
- [x] Tests saved to the appropriate existing project directories.
- [x] Test summary includes coverage metrics.

## Notes

- This QA run is **gap-filling only** — no production code was changed; only
  `tests/Hexalith.Works.UnitTests/WorkItemSpawnChildTests.cs` was extended (+5 test methods / +11
  cases). No golden fixture or catalog change was needed (`Count` stays **36**).
- The five gaps were each an uncovered branch or explicit AC clause: (1) plain-spawn acceptance from the
  four live statuses beyond `Created`; (2) the missing-obligation child rejection branch; (3) AC #4's
  replay-safety clause (rejected spawn burns no sequence); (4) the `Apply(ChildSpawned)` dedup branch
  plus multi-child accumulation; (5) the guard's same-parent idempotency accept branch on retry.

---

# Test Automation Summary — Story 3.1 (Guard Tenant-Safe Work Tree Shape)

Workflow: `bmad-qa-generate-e2e-tests` after `bmad-dev-story`. Framework reused: **xUnit v3 +
Shouldly**, pure domain unit tests, contract-flow integration tests, and existing
architecture/property guardrails. Story 3.1 adds a pure caller-fed tree attachment guard, specific
rejection payloads for second-parent/cycle/depth failures, create-path delegation for parent
validation, and documentation of the work-tree shape rules.

Story 2.5 final baseline was **332** green tests (UnitTests 260, IntegrationTests 45,
ArchitectureTests 26, PropertyTests 1). Story 3.1 adds **+18** unit tests and **+1** integration test,
raising the total to **351** green. `WorkItemV1Catalog.Count` is now **34** (13 success events, 13
commands, 8 rejection events). No durable success event shape changed, so no golden fixtures were added.

## Gaps closed this run

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkTreeAttachmentGuardTests` covers valid root items, valid first parent attachment,
  idempotent same-parent validation, same-tenant casing normalization, second-parent rejection,
  self-parent cycle rejection, ancestor-chain cycle rejection, cross-tenant parent rejection,
  cross-tenant ancestor rejection, default max-depth boundary acceptance, one-over-depth rejection, and
  uncapped breadth.
- [x] QA-generated gap tests cover same-tenant ancestor casing normalization, same-parent idempotency
  with different tenant casing, policy override acceptance above the default depth, and policy override
  rejection below the default depth.
- [x] `WorkItemCreateTests.CreateWorkItem_with_self_parent_reference_returns_cycle_rejection_without_mutating_state`
  proves create-time self-parenting returns a rejection-only result and leaves replay state at sequence
  `0`.
- [x] `WorkItemCreateTests.CreateWorkItem_with_existing_different_parent_returns_second_parent_rejection_and_leaves_state_unchanged`
  proves supplied current child state with a different parent rejects without advancing sequence or
  replacing the stored parent.

### Integration tests (`tests/Hexalith.Works.IntegrationTests`)

- [x] `WorkItemCreateContractFlowTests.CreateWorkItem_with_invalid_tree_shape_serializes_specific_rejection_payloads_without_envelope`
  round-trips the new rejection payloads and proves EventStore envelope metadata remains absent.
- [x] `WorkItemV1Catalog` now includes the three new public rejection payloads so polymorphic
  registration and concrete additivity tests cannot drift.

### Documentation

- [x] `docs/work-tree-shape-guard.md` records single-parent, acyclic, single-tenant, max-depth default
  `32`, policy-supplied override, uncapped breadth, and Story 3.1 exclusions.

## Story 3.1 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **278/278** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **46/46** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 3.1 Test Counts

| Suite | Story 2.5 Final | Story 3.1 Final | Delta |
|-------|----------------:|----------------:|------:|
| UnitTests | 260 | **278** | +18 |
| IntegrationTests | 45 | **46** | +1 |
| ArchitectureTests | 26 | **26** | — |
| PropertyTests | 1 | **1** | — |
| **Total** | **332** | **351** | **+19** |

### Checklist

- [x] API/contract tests generated for guard decisions and public rejection payload serialization.
- [x] E2E/UI tests marked not applicable (Story 3.1 is pure Contracts + Server + docs; no UI/browser
  surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly).
- [x] Tests cover happy paths (root, first parent, same-parent idempotency, same-tenant casing,
  at-limit depth, policy override above default, uncapped breadth).
- [x] Tests cover critical error/edge cases (second parent, self cycle, ancestor cycle, cross tenant,
  one-over-depth, smaller policy override).
- [x] All generated tests run successfully (351/351).
- [x] Tests use clear descriptions and semantic assertions.
- [x] No hardcoded waits or sleeps.
- [x] Tests are independent and arrange their own facts/state.
- [x] Tests saved to the appropriate existing project directories.
- [x] Test summary includes coverage metrics.

---

# Test Automation Summary — Story 2.5 (Complete, Cancel, Reject, and Expire Work)

Workflow: `bmad-qa-generate-e2e-tests`. Framework reused: **xUnit v3 + Shouldly**, Tier-1 domain and
contract-flow tests. Story 2.5 required no new production payloads or lifecycle table changes: the
existing aggregate and `docs/lifecycle-transition-matrix.md` already matched the terminal-state
semantics. This run strengthened focused verification around completion, cancel, terminal rejection,
planning-act guards, and expiry purity.

Story 2.4 final baseline was **289** green tests (UnitTests 217, IntegrationTests 45,
ArchitectureTests 26, PropertyTests 1). Story 2.5 adds **+43** unit test cases and keeps integration,
architecture, and property counts stable, raising the total to **332** green. (The automated review
added 8 cases on top of the original 35 to close a reject-disambiguation coverage gap; see below.)

## Gaps closed this run

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemLifecycleTests.Cancel_from_each_non_terminal_status_emits_cancelled_and_replay_rests_terminal`
  — covers cancel from `Created`, `Assigned`, `Queued`, `InProgress`, and `Suspended`, asserting
  `WorkItemCancelled`, `Sequence + 1`, and replay to terminal `Cancelled`.
- [x] `WorkItemLifecycleTests.Expire_from_each_non_terminal_status_emits_expired_and_replay_rests_terminal`
  — covers expiry from `Created`, `Assigned`, `Queued`, `InProgress`, and `Suspended`, asserting
  `WorkItemExpired`, `Sequence + 1`, and replay to terminal `Expired` without aggregate clock input.
- [x] `WorkItemLifecycleTests.Commands_after_cancel_are_rejected_and_leave_state_unchanged`
  — covers post-cancel progress, reschedule, assign, queue, claim, suspend, resume, complete, reject,
  and expire; every command returns `WorkItemTransitionRejected` and preserves status, effort,
  schedule, binding, and sequence.
- [x] `WorkItemLifecycleTests.Planning_acts_from_terminal_statuses_are_transition_rejections`
  — proves `ReEstimate` and `RescheduleWorkItem` reject from `Completed`, `Cancelled`, `Rejected`,
  and `Expired` without sequence advancement.
- [x] `WorkItemLifecycleTests.Noop_results_have_no_events_and_rejections_never_mix_success_payloads`
  — confirms terminal no-op results carry no events and illegal terminal commands return rejection-only
  payloads.
- [x] `WorkItemLifecycleTests.Reject_without_explicit_requeue_uses_default_requeue_and_rests_at_queued`
  — proves the public default `RejectWorkItem(TenantId, WorkItemId)` path emits `WorkItemRejected`
  with `Requeue = true`, advances sequence once, and replays to `Queued` for reassignment.
- [x] `WorkItemProgressTests.Progress_driven_completion_rejects_later_non_idempotent_terminal_commands`
  — after Remaining reaches zero and `WorkItemCompleted` replays, later progress, assignment,
  reschedule, and suspend commands are rejected and do not advance sequence.
- [x] `WorkItemProgressTests.Progress_driven_completion_noops_only_exact_duplicate_complete`
  — confirms exact duplicate completion is the idempotent no-op after progress-driven completion.
- [x] `WorkItemLifecycleTests.Default_reject_from_any_non_assigned_status_is_a_transition_rejection_and_never_reopens`
  — **(added by automated review)** proves a default `RejectWorkItem` from `Created`, `Queued`,
  `InProgress`, `Suspended`, `Completed`, `Cancelled`, `Rejected`, and `Expired` returns a
  `WorkItemTransitionRejected` (never a `WorkItemRejected`, never a no-op) and leaves status/sequence
  unchanged. Closes the gap where the data-driven matrix Theory excludes the `Reject` act, so the
  Task 4 claim that a requeue reject of an already-`Rejected` item never reopens it was unverified.

### Architecture tests (`tests/Hexalith.Works.ArchitectureTests`)

- [x] `ScaffoldGovernanceTests.P0_WorkItemKernelRemainsPure` now scans `Contracts`, `Server`, and
  `Projections`, and explicitly bans clock/timer APIs, generated IDs, Dapr, HTTP, and filesystem
  calls in the domain kernel. This covers expiry as an adapter-fired signal with no aggregate clock.

## Story 2.5 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors**.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **260/260** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **45/45** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 2.5 Test Counts

| Suite | Story 2.4 Final | Story 2.5 Final | Delta |
|-------|----------------:|----------------:|------:|
| UnitTests | 217 | **260** | +43 |
| IntegrationTests | 45 | **45** | — |
| ArchitectureTests | 26 | **26** | — |
| PropertyTests | 1 | **1** | — |
| **Total** | **289** | **332** | **+43** |

## Notes

- No production code or documentation matrix changes were required; Story 2.5 behavior was already
  encoded in `WorkItemLifecycle`, `WorkItemAggregate`, `WorkItemState`, and
  `docs/lifecycle-transition-matrix.md`.
- `WorkItemV1Catalog.Count` remains **31**. Terminal events and commands already existed in the v1
  catalog and golden corpus, so no new fixtures were generated.

### Checklist

- [x] API/contract tests generated (work-item command -> aggregate -> event/rejection -> replay; JSON
  contract-flow coverage already exists for terminal success and rejection payloads).
- [x] E2E/UI tests marked not applicable (Story 2.5 is pure Contracts + Server + Tier-1; no UI/browser
  surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly).
- [x] Tests cover happy paths (complete, cancel, reject default requeue/non-requeue, expire).
- [x] Tests cover critical error/edge cases (post-terminal rejections, closed no-op list, post-cancel
  rejection, planning-act terminal guards, expiry purity).
- [x] All generated tests run successfully (332/332).
- [x] Tests use clear descriptions and semantic assertions.
- [x] No hardcoded waits or sleeps.
- [x] Tests are independent and arrange their own replayed state.
- [x] Test summary includes coverage metrics.

---

# Test Automation Summary — Story 2.4 (Re-Estimate and Reschedule Work)

Workflow: `bmad-qa-generate-e2e-tests`. Role: QA automation engineer (test generation only — no code
review or story validation). Framework detected and reused: **xUnit v3 + Shouldly**, Tier-1 (no
Dapr/Aspire/containers/network/clock). Story 2.4 is a pure `Contracts` + `Server` domain slice with **no
UI, MCP, public route, AppHost, Dapr, projection, or adapter surface**, so browser/UI E2E is **not
applicable**; the executable end-to-end path is **command → `WorkItemAggregate.Handle` → raw-act event →
concrete `JsonSerializerDefaults.Web` JSON → replayed `WorkItemState`**, exercised by the unit +
contract-flow tests below.

The dev-authored Story 2.4 baseline was **285** green tests (UnitTests 213, IntegrationTests 45,
ArchitectureTests 26, PropertyTests 1). This QA run mapped the dev coverage against AC #1–#5 and the five
recorded Key Design Decisions (D1–D5), discovered four genuine gaps, and auto-applied **+4** unit tests,
raising the total to **289** green. **No production code was changed** — only test files were extended.

## Gaps auto-applied this run

The dev baseline already covered, per AC: same-Unit re-estimate up + replay (AC #1), the
created-with-effort Unit-mismatch rejection (AC #2), negative-estimate rejection, the below-Done clamp
with no completion (D5), first-estimate establishment on an unestimated item (D2), terminal/Unknown
rejection, schedule replacement with Priority + Due Date (AC #3), both-null "sorts last" acceptance
(AC #4), acceptance from every live status, the `WorkItemEffort.ReEstimate` value-object contract, the
AC #5 reflection guard, the golden-corpus freeze/round-trip/additive trio for both new events, and the
JSON contract-flow convergence for both. The genuine gaps were the **D2→AC #2 interaction** (Unit
immutability after establishing the first estimate *via* re-estimate), the **zero re-estimate boundary**
(lower bound of "non-negative" + D5), the **D3 whole-replacement clear** (distinct from a per-field
patch), and the **due-date-only partial schedule** — each closed below.

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemReEstimateTests.ReEstimate_after_establishing_first_estimate_rejects_a_different_unit_and_preserves_it`
  (D2 → AC #2) — establishes the Unit on a previously-unestimated item via `ReEstimate`, then a
  subsequent different-Unit re-estimate is rejected with `WorkItemReEstimateRejected`, the established
  Unit + Estimated are preserved, and `Sequence` does not advance. The story mandates that the AC #2
  immutability rejection "still applies once a Unit is established"; the baseline only covered the
  created-with-effort path, not the D2 establish-then-reject path.
- [x] `WorkItemReEstimateTests.ReEstimate_to_zero_clamps_done_and_remaining_without_completing`
  (AC #1 boundary + D5) — zero is the lower boundary of the AC #1 "non-negative value": it is accepted,
  Done clamps to 0, Remaining derives to 0, and the act emits **only** `ReEstimated` — never
  `WorkItemCompleted`. The baseline covered a below-Done clamp (4 of done 6) but not the exact-zero
  boundary.
- [x] `WorkItemRescheduleTests.Reschedule_with_empty_schedule_clears_a_previously_set_schedule_whole`
  (D3) — replacing a fully-populated schedule with an empty one clears **both** `Priority` and `DueDate`,
  proving whole-replacement rather than the rejected per-field-patch alternative (where null would mean
  "leave unchanged"). The baseline's both-null test started from an item that never had a schedule, so it
  did not exercise the clear-an-existing-schedule semantic.
- [x] `WorkItemRescheduleTests.Reschedule_with_only_a_due_date_is_accepted_and_replayed`
  (AC #3/#4 partial schedule) — a due date with no priority is accepted and replayed without coercing the
  missing priority into a default band. The inverse partial (priority, no due date) was already exercised
  by the live-status theory; the due-date-only partial was untested.

### Pre-existing coverage (dev-authored, verified green — not regenerated)

- [x] `WorkItemReEstimateRescheduleContractFlowTests` — command → `WorkItemAggregate.Handle` → concrete
  `JsonSerializerDefaults.Web` JSON → replay for both `ReEstimated` and `WorkItemRescheduled`, asserting
  convergence of effort/schedule and absence of `$type` plus EventStore envelope fields.
- [x] `WorkItemV1Catalog` — catalog count **26 → 31** (13 success events, 13 commands, 5 rejection
  events); new payloads resolve through the empty `Polymorphic` base.
- [x] `SchemaEvolutionGoldenCorpusTests` — frozen deserialize + round-trip + additive unknown-field
  tolerance for `ReEstimated.v1.json` and `WorkItemRescheduled.v1.json`.
- [x] `docs/lifecycle-transition-matrix.md` + `LifecycleTransitionMatrixDocTests` — both new commands
  documented as non-lifecycle planning acts and gated in `RequiredCommands`.

## Story 2.4 Validation

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -v minimal` — passed with
  **0 warnings and 0 errors** (warnings-as-errors).
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **217/217** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **45/45** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 2.4 Test Counts

| Suite | Story 2.3 Final | Dev 2.4 Baseline | QA Final | QA Delta |
|-------|----------------:|-----------------:|---------:|---------:|
| UnitTests | 188 | 213 | **217** | +4 |
| IntegrationTests | 39 | 45 | **45** | — |
| ArchitectureTests | 26 | 26 | 26 | — |
| PropertyTests | 1 | 1 | 1 | — |
| **Total** | **254** | **285** | **289** | **+4** |

### Checklist

- [x] API/contract tests generated (re-estimate/reschedule command → aggregate → JSON → replay).
- [x] E2E/UI tests marked not applicable (Story 2.4 is pure Contracts + Server + Tier-1; no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`, Moq, or FluentAssertions).
- [x] Tests cover happy path (re-estimate up/establish; schedule replace + partial schedule replay).
- [x] Tests cover critical error/edge cases (D2→AC #2 Unit immutability; zero boundary; whole-replacement clear).
- [x] All generated tests run successfully (289/289).
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps (pure in-memory + JSON; no clock/RNG/IO).
- [x] Tests are independent (each arranges its own state via replay; no order dependency).
- [x] Test summary updated with coverage metrics.

## Notes

- This run is **QA gap-filling only** — no production code was changed; only `WorkItemReEstimateTests.cs`
  and `WorkItemRescheduleTests.cs` were extended (+2 tests each).
- Four genuine, AC/design-decision-anchored gaps were closed: (1) Unit immutability (AC #2) reached via
  the D2 establish-then-reject path, not just created-with-effort; (2) the zero lower boundary of the
  AC #1 "non-negative" estimate, reinforcing D5 (no completion on a planning act); (3) D3
  whole-schedule-replacement proven by clearing an existing schedule (distinguishes it from the rejected
  per-field patch); (4) the due-date-only partial schedule (the priority-only partial was already
  covered).
- The new tests need no golden fixture or catalog change (`Count` stays **31**) — they exercise existing
  durable event types through the aggregate and replay, not new wire shapes.

---

# Test Automation Summary — Story 2.3 (Report Progress with Unit-Tagged Burn-Down)

Workflow: `bmad-qa-generate-e2e-tests`. Role: QA automation engineer (test generation only — no code
review or story validation). Framework detected and reused: **xUnit v3 + Shouldly**, Tier-1 (no
Dapr/Aspire/containers/network). Story 2.3 is a pure `Contracts` + `Server` domain slice with **no UI,
MCP, public route, or host surface**, so browser/UI E2E is **not applicable**; the executable
end-to-end path is **command → `WorkItemAggregate.Handle` → raw-act event → concrete JSON transport
shape → replayed `WorkItemState`**, exercised by the unit + contract-flow tests below.

The dev-authored Story 2.3 baseline was **247** green tests (UnitTests 183, IntegrationTests 37,
ArchitectureTests 26, PropertyTests 1). This QA run discovered AC-aligned coverage gaps and
auto-applied **+7** tests, raising the total to **254** green. **No production code was changed** —
only test files were added/extended.

## Gaps auto-applied this run

Mapped against AC #1–#5, the dev baseline already covered single-report progress, over-progress
clamping, completion event order, the rejection paths (non-positive delta, Unit mismatch, unestimated,
out-of-`InProgress`), explicit completion for unestimated work, schema-evolution freeze/round-trip, and
the auto-complete JSON flow. The genuine gaps were the **multi-report burn-down ("burns down as a
fact")** behavior, the **exact-zero completion boundary**, the **`WorkItemEffort.Report` value-object
contract**, and the **non-completing JSON round-trip** — each closed below:

### Unit tests (`tests/Hexalith.Works.UnitTests`)

- [x] `WorkItemProgressTests.ReportProgress_applied_repeatedly_accumulates_done_and_burns_down_remaining`
  (AC #2) — two sequential reports (3 then 2 of 8) each append a `ProgressReported` at the next
  sequence, accumulate replayed `Done` (3 → 5) and burn down `Remaining` (5 → 3), and do **not**
  auto-complete while `Remaining` stays positive (asserts a single event per report, omitted `Note`
  replays as `null`). Previously only a single report was tested.
- [x] `WorkItemProgressTests.ReportProgress_with_delta_exactly_equal_to_remaining_completes_in_order`
  (AC #3 boundary) — a delta that lands `Remaining` **exactly** on zero (not over) completes
  synchronously, emitting `ProgressReported` then `WorkItemCompleted` at `+1`/`+2`. The pre-existing
  unit test only covered the over-shoot (clamp) path; exact-equal was implicit (integration only).
- [x] `WorkItemEffortTests.WorkItemEffort_report_accumulates_done_and_re_derives_remaining`
  (AC #1/#2) — chained `Report(3).Report(2)` advances `Done` and re-derives `Remaining` without
  storing it.
- [x] `WorkItemEffortTests.WorkItemEffort_report_rejects_non_positive_delta` (AC #2/#5, theory `-1`/`0`)
  — the value object's own positive-delta guard throws `ArgumentOutOfRangeException`. The guard existed
  but was only enforced indirectly via the aggregate; it is now directly gated.

### Integration / contract-flow tests (`tests/Hexalith.Works.IntegrationTests`)

- [x] `WorkItemProgressContractFlowTests.Partial_progress_round_trips_through_json_and_burns_down_without_completing`
  (AC #1/#2) — a single sub-completion report (3 of 8) survives concrete `JsonSerializerDefaults.Web`
  serialization and replays to `Done=3` / `Remaining=5` / `InProgress` / `Sequence=4`, with `Note`
  preserved and **no** `WorkItemCompleted`. The only prior integration test exercised the auto-complete
  path (8 of 8); the non-completing burn-down path through JSON was untested.
- [x] `WorkItemProgressContractFlowTests.Repeated_progress_round_trips_through_json_and_accumulates_then_completes`
  (AC #2/#3) — two reports delivered through concrete JSON accumulate `Done` across reports (3 then 5),
  and the report that lands `Remaining` on zero round-trips with its paired `WorkItemCompleted` to a
  `Completed` / `Sequence=6` replay that matches the write side.

## Story 2.3 Validation

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` —
  passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  passed with **0 warnings and 0 errors** (warnings-as-errors).
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — **188/188** passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` —
  **39/39** passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` —
  **26/26** passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — **1/1**
  passed.

### Story 2.3 Test Counts

| Suite | Story 2.2 Baseline | Dev 2.3 Baseline | QA Final | QA Delta |
|-------|-------------------:|-----------------:|---------:|---------:|
| UnitTests | 166 | 183 | **188** | +5 |
| IntegrationTests | 34 | 37 | **39** | +2 |
| ArchitectureTests | 26 | 26 | 26 | — |
| PropertyTests | 1 | 1 | 1 | — |
| **Total** | **227** | **247** | **254** | **+7** |

### Story 2.3 Coverage Notes

- Unit tests cover positive progress, **cumulative multi-report burn-down**, over-progress clamping,
  **exact-zero completion boundary**, progress-driven completion event order, negative/zero delta
  rejection, Unit mismatch rejection, unestimated progress rejection, status-based rejection outside
  `InProgress`, explicit completion for unestimated `InProgress`/`Suspended` work, and the
  `WorkItemEffort.Report` accumulation/guard contract.
- Integration tests cover command → aggregate → concrete JSON → replay for **partial (non-completing)**,
  **repeated/accumulating**, and **auto-complete** progress, keeping EventStore envelope fields out of
  concrete payloads.
- Schema tests cover frozen `ProgressReported.v1.json`, round-trip deserialization, and additive unknown
  field tolerance.
- Architecture tests verify the lifecycle matrix mentions `ReportProgress`, kernel purity still passes,
  and roll-up/reminder terms remain banned from `src`.

### Checklist

- [x] API/contract tests generated (partial-burn-down + repeated-accumulate JSON round-trips).
- [x] E2E/UI tests marked not applicable (Story 2.3 is pure Contracts + Server + Tier-1; no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`, Moq, or FluentAssertions).
- [x] Tests cover happy path (single + cumulative burn-down; partial + completing JSON replay).
- [x] Tests cover critical error/edge cases (exact-zero boundary; non-positive `Report` delta guard).
- [x] All generated tests run successfully (254/254).
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps (pure in-memory + JSON; no clock/RNG/IO).
- [x] Tests are independent (each arranges its own state via replay; no order dependency).
- [x] Test summary updated with coverage metrics.

---

# Historical Test Automation Summary — Story 2.2 (Record Raw-Act Events and Replay State)

Workflow: `bmad-qa-generate-e2e-tests`. Role: QA automation engineer (test generation only — no code
review or story validation). Baseline before this run (dev-authored, green): **221** tests
(UnitTests 166, IntegrationTests 28, ArchitectureTests 26, PropertyTests 1).
Framework detected and reused: **xUnit v3 + Shouldly**, Tier-1 (no Dapr/Aspire/containers/network).
All tests auto-applied this run; all green.

## Generated Tests (gaps auto-applied this run)

### API / Contract & Serialization Tests

- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/*.v1.json` — **+7 frozen golden
  fixtures**, completing the RR-6 / NFR-12 back-compatibility corpus. The dev started the corpus with
  3 of the 10 durable success events (`WorkItemCreated`, `WorkItemAssigned`, `WorkItemCompleted`), yet
  the corpus README states *"Every event ever produced must remain deserializable forever."* The seven
  unfrozen events — including the two that carry distinguishing payload (`WorkItemClaimed` → executor
  binding, `WorkItemRejected` → the `Requeue` resting-status flag) — were not gated. Added frozen v1
  fixtures for `WorkItemQueued`, `WorkItemClaimed`, `WorkItemSuspended`, `WorkItemResumed`,
  `WorkItemCancelled`, `WorkItemRejected`, `WorkItemExpired`. **Generated from the production serializer**
  (a temporary emitter, run once then deleted — not hand-authored), so camelCase, enum-name casing, and
  property order are byte-accurate to the EventStore-persisted concrete form (no `$type`).
- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` —
  **+5 tests** wiring the new fixtures into the gate (now 10/10 durable success events):
  - `WorkItemClaimed_DeserializesFromFrozenBytesAndRoundTrips` — deserialize-from-frozen asserts every
    field incl. the binding (`partyId` / `channel=Mcp` / `authorityLevel=Coordinate`); re-serialize →
    deserialize round-trips to an equal record.
  - `WorkItemRejected_DeserializesFromFrozenBytesAndRoundTrips` — asserts the frozen `requeue: false`
    discriminator survives exactly (it steers replay to `Rejected` vs `Queued`).
  - `Base_shape_lifecycle_events_deserialize_from_frozen_bytes_and_round_trip` — the five
    `(AggregateId, Sequence, TenantId, WorkItemId)` events (`Queued`/`Suspended`/`Resumed`/`Cancelled`/
    `Expired`), each frozen independently so a future per-event field addition is gated by its own entry.
  - `WorkItemClaimed_ToleratesAdditiveUnknownField` / `WorkItemRejected_ToleratesAdditiveUnknownField` —
    inject an unknown `futureField` into the frozen bytes; the enriched events still deserialize (additive,
    no-`V2` tolerance). Vacuous-pass guard: `File.Exists(path)` inside `ReadGolden` reports a missing
    fixture as the root cause before any value assertion.
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs` — **+1 test** closing
  the AC #2 *"order-tolerant projections"* gap (previously only implicit — every existing replay test
  applied events in arrival order):
  - `Out_of_order_event_stream_replays_to_completed_when_resorted_by_sequence` — persists the six-event
    lifecycle stream through JSON, delivers it **out of order** (deterministic reverse — no RNG), then a
    projection recovers the canonical order purely from `Sequence` and replays into an independent
    `WorkItemState`, converging to `Completed` / `Sequence = 6` identical to the write side. Guards: the
    delivered sequences are asserted to be the contiguous, gap-free `1..6` and the stream count `== 6`
    before the order-tolerance claim is made.

### E2E Tests

- [x] Browser/UI E2E is **not applicable** to Story 2.2: the slice is pure `Contracts` + Tier-1 tests —
  serialization registration and the raw-act event catalog, with no UI, MCP, public route, or host
  surface (host/Dapr/Aspire wiring is deferred to Stories 4.5/4.6). The executable end-to-end path is
  **command → `WorkItemAggregate.Handle` → raw-act event → JSON transport shape → replayed
  `WorkItemState`**, exercised end-to-end by the contract-flow + golden-corpus tests above.

### Pre-existing coverage (dev-authored, verified green — not regenerated)

- [x] `WorkItemSerializationRegistrationTests` — AC #5: every one of the 23 v1 types resolves through the
  empty `Polymorphic` base, emits `$type` == type name (no version suffix), and round-trips to the
  concrete type. Two vacuous-pass guards (catalog count == 23; resolver reports ≥23 derived types).
- [x] `WorkItemRawActAdditivityTests` — AC #1/#2/#3 regression guard: concrete-type serialization emits
  **no** `$type` and **no** EventStore envelope fields, and a concrete `WorkItemCreated` still replays to
  `Created` (proves the polymorphic registration is purely additive).
- [x] `WorkItemCreateContractFlowTests` / `WorkItemLifecycleContractFlowTests` — create + full-lifecycle
  serialized write → persist → replay, reference-only payloads, the requeue flag steering replay, and
  rejection-only results (`WorkItemTransitionRejected`, cross-tenant-parent, missing-obligation) that
  carry context but no `sequence`.

## Coverage

Mapped against the Story 2.2 surface (10 success events + 10 commands + 3 rejection events; the
PolymorphicSerializations registration; the golden corpus):

| AC | What it requires | Status |
|----|------------------|--------|
| #1 | Accepted act → past-tense v1 event carrying verbatim replay values | Pre-existing (create/lifecycle flow + additivity); **reinforced** by 7 new frozen fixtures |
| #2 | Event carries `(AggregateId, Sequence)` for **order-tolerant** projections; no envelope spoofing | **Gap closed** — added out-of-order-resort-by-`Sequence` replay test; envelope-absence already guarded |
| #3 | In-order replay through `Apply` reconstructs state deterministically; no interpreted/AI/sibling data | Pre-existing (full-lifecycle serialized replay; reference-only payloads) |
| #4 | Rejection → `IRejectionEvent`; result never mixes success + rejection payloads | Pre-existing (per-path `IsRejection`/`IsSuccess` exclusivity; mixed-payload throw guarded in EventStore lib) |
| #5 | Catalog registered & resolvable by PolymorphicSerializations; golden corpus started, additive/no-`V2` | Pre-existing registration + **gap closed**: corpus completed to **10/10** durable success events |

**Durable-event corpus coverage: 10 / 10** success events frozen (was 3 / 10).
**Not corpus candidates (by design):** the 10 commands and 3 rejection events are not appended to the
event stream (commands are transient inputs; rejections are returned to the caller with no `Sequence`),
so they are not part of the persisted-bytes back-compat gate — they remain covered by the resolution and
contract-flow tests.

### Test counts (built Release, warnings-as-errors → 0 warnings / 0 errors)

| Suite | Before | After | Δ |
|-------|-------:|------:|--:|
| UnitTests | 166 | 166 | — |
| IntegrationTests | 28 | **34** | +6 |
| ArchitectureTests | 26 | 26 | — |
| PropertyTests | 1 | 1 | — |
| **Total** | **221** | **227** | **+6** |

## Validation

- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` —
  **0 warnings, 0 errors** (warnings-as-errors).
- Generated xUnit v3 executables run directly (Microsoft.Testing.Platform named-pipe is blocked in this
  sandbox, per the established pattern):
  - **UnitTests: 166/166** (unchanged)
  - **IntegrationTests: 34/34** (was 28/28 → **+6**)
  - **ArchitectureTests: 26/26** (unchanged — the `DependencyDirectionTests` update was dev-authored)
  - **PropertyTests: 1/1** (unchanged)
  - Total: **227/227**, 0 failures.

```bash
DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal
DOTNET_CLI_HOME=/tmp dotnet build  Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal
tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests
tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests
tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests
tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests
```

## Checklist

- [x] API/contract tests generated (golden-corpus completion + order-tolerance replay).
- [x] E2E/UI tests marked not applicable (Story 2.2 is pure Contracts + Tier-1; no UI/browser surface).
- [x] Tests use standard project framework APIs (xUnit v3 + Shouldly; no raw `Assert.*`, Moq, or FluentAssertions).
- [x] Tests cover happy path (full out-of-order lifecycle replay to `Completed`; all 10 durable events deserialize from frozen bytes).
- [x] Tests cover critical error/edge cases (additive unknown-field tolerance on enriched events; out-of-order delivery recovered by `Sequence`).
- [x] All generated tests run successfully (227/227).
- [x] Tests use semantic assertions and clear descriptions.
- [x] No hardcoded waits or sleeps (pure in-memory + JSON; the corpus reads copied-to-output fixtures only).
- [x] Tests are independent (no order dependency; each builds its own state; registration is an idempotent static ctor).
- [x] Test summary created with coverage metrics.

## Notes

- This run is **QA gap-filling only** — no production code was changed; only test files and frozen
  fixtures were added/modified.
- Two genuine gaps were discovered and auto-applied: (1) the back-compat golden corpus gated only 3 of
  the 10 durable success events despite its own "every event ever produced must remain deserializable"
  rule — completed to 10/10; (2) AC #2's *order-tolerant projections* claim was only implicit (all replay
  tests applied events in arrival order) — added a shuffle-then-resort-by-`Sequence` replay proof.
- Golden fixtures were generated from the production serializer (temporary emitter, deleted after the run)
  rather than hand-authored, matching the dev's established methodology so casing/ordering are exact. The
  existing `SchemaEvolution\Golden\**\*.json` `<None>` glob copies the new files to output — no csproj
  change was needed.
- The 10 commands and 3 rejection events are intentionally **not** in the persisted-bytes corpus
  (transient inputs / caller-returned, never stream-appended); they stay covered by the polymorphic
  resolution test and the contract-flow rejection tests.
