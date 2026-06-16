---
baseline_commit: 5c95d1e
---

# Story 3.4: Preserve Heterogeneous Unit Subtotals

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As an objective owner,
I want mixed-unit work trees to show separate subtotals,
so that Works never fabricates a misleading single remaining-effort number across incompatible Units.

## Acceptance Criteria

1. **Given** a Work Tree contains only one Unit
   **When** Roll-Up is projected
   **Then** the subtree exposes a single rolled subtotal for that Unit.

2. **Given** a Work Tree contains multiple Units
   **When** Roll-Up is projected
   **Then** the subtree exposes one rolled subtotal per Unit
   **And** no implicit conversion or summation across Units occurs.

3. **Given** a child changes effort through progress or re-estimate
   **When** the child Unit matches its established Unit
   **Then** the matching per-Unit subtotal updates incrementally.

4. **Given** a progress or re-estimate command carries a Unit incompatible with the child's established Unit
   **When** the command is handled
   **Then** the command is rejected before event emission
   **And** no Roll-Up projection update is produced from that invalid act.

5. **Given** replay or delivery exposes an already-persisted child event whose Unit violates the child's established Unit contract
   **When** the Roll-Up projection processes the event
   **Then** the projection fails closed by refusing the incompatible contribution, retaining the last valid projected value or marking that Work Item projection degraded
   **And** logs include only tenant, work item, event type, and sequence metadata, never payload values
   **And** no mixed-unit Roll-Up view is published as fresh.

6. **Given** future UI surfaces need burn-down and roll-up data
   **When** `RollUpView` or equivalent read models are inspected
   **Then** they expose labeled per-Unit subtotals
   **And** they do not expose a coerced all-unit total.

## Tasks / Subtasks

- [x] **Task 1 - Reconcile what Story 3.3 already delivers before writing any new code (AC: #1-#6)**
  - [x] Read `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs`,
    `src/Hexalith.Works.Projections/Models/WorkItemRollUpEvent.cs`,
    `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs`,
    `src/Hexalith.Works.Contracts/Models/OwnRemaining.cs`,
    `src/Hexalith.Works.Contracts/Models/RolledRemaining.cs`,
    `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs`,
    `src/Hexalith.Works.Contracts/ValueObjects/Unit.cs`,
    `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` (the `ReportProgress` and `ReEstimate`
    handlers), `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs`, and
    `docs/work-roll-up-projection.md`.
  - [x] Confirm and DO NOT re-implement what already exists from Story 3.3: `RemainingBuckets` already
    keys rolled contributions by `Unit`; `WorkItemRollUp.RolledRemainingByUnit` already exposes one
    `RolledRemaining` per Unit; the single `WorkItemRollUp.RolledRemaining` is already populated **only**
    when exactly one unit bucket exists (mixed-unit subtrees already get `null`, never a coerced total).
    AC #1, AC #2, AC #3, and most of AC #6 are already satisfied by the projection — this story
    **verifies them with explicit tests** and closes the two real gaps below.
  - [x] Confirm and DO NOT re-implement what already exists for AC #4: `WorkItemAggregate.Handle(ReportProgress)`
    already returns `WorkItemProgressRejected("Progress unit must match the established effort unit.")`
    and `Handle(ReEstimate)` already returns `WorkItemReEstimateRejected("Re-estimate unit must match the
    established effort unit.")` before any event is emitted. Rejections are not appended to the stream, so
    no projection delivery is produced. AC #4 is already satisfied — this story **verifies it**, it does
    not add a new rejection path.
  - [x] Identify the two genuine gaps this story closes: (a) the projection currently **silently no-ops**
    a unit-mismatched `ProgressReported`/`ReEstimated` instead of failing closed and signalling a
    degraded node (AC #5); (b) there is no consumer-visible signal that a node refused an incompatible
    contribution, so a degraded mixed-unit view cannot be distinguished from a fresh one (AC #5, AC #6).
  - [x] Preserve every Story 3.3 invariant: per-child-sequence last-writer-wins (never additive deltas),
    terminal child contributes zero, tenant-equality asserted at every traversal hop, own Remaining stays
    aggregate-authoritative while rolled Remaining stays eventual projection state, and the projection
    never flips aggregate status.

- [x] **Task 2 - Make the projection fail closed on an incompatible-unit event (AC: #5)**
  - [x] In `WorkItemRollUpProjection.ApplyPayload`, replace the silent skip for unit-mismatched
    `ProgressReported` (the current `node.OwnEffort.Unit == progress.Unit` guard that simply falls
    through) with explicit fail-closed handling: do not apply the contribution, **retain the last valid
    own-effort value**, and mark the node degraded.
  - [x] Apply the same fail-closed rule to `ReEstimated` whose `Unit` differs from the node's established
    effort `Unit`. Today an established item silently keeps its old unit via `WorkItemEffort.ReEstimate`
    (which preserves `Unit` and ignores the event's `Unit`); a mismatched re-estimate must instead mark
    the node degraded and retain the last valid value, not silently reinterpret the new estimate under the
    old unit.
  - [x] An unestimated node has no established unit yet; the **first** `ReEstimated` (or
    `WorkItemCreated.InitialEffort` / spawn facts) legitimately establishes the unit and must NOT be
    treated as a mismatch. Only events that arrive **after** a unit is established and that disagree with
    it are incompatible.
  - [x] "Retain last valid projected value" must survive rebuild: because the projection rebuilds node
    state from the full ordered event set on every relevant delivery (`Rebuild`/`ResetProjectionState`),
    re-derive the degraded flag deterministically during replay so a duplicate or out-of-order delivery of
    the same bad event converges to the same degraded result (idempotent, order-tolerant — same guarantee
    Story 3.3 holds for valid events).
  - [x] A node that becomes terminal contributes zero regardless of degraded state; do not let a degraded
    flag resurrect a terminal contribution. Keep terminal-zero precedence from Story 3.3.

- [x] **Task 3 - Surface degraded state and metadata-only diagnostics as pure data, never via I/O (AC: #5, #6)**
  - [x] Add a degraded indicator to the read model so consumers can tell a refused-contribution node from
    a fresh one. Prefer extending `WorkItemRollUp` additively (for example a `bool Degraded` property, or
    a richer `RollUpProjectionHealth`/diagnostics value) under
    `src/Hexalith.Works.Contracts/Models`. Keep `OwnRemaining`/`RolledRemaining`/`RolledRemainingByUnit`
    semantics unchanged; degraded nodes still expose their last valid per-Unit subtotals (never a fresh
    fabricated value) and still expose **no** coerced all-unit total.
  - [x] AC #5 requires "logs include only tenant, work item, event type, and sequence metadata, never
    payload values." The projection is pure and `Hexalith.Works.Projections` may reference **Contracts
    only** (enforced by `DependencyDirectionTests` and the doc boundary), so DO NOT inject `ILogger`,
    `Console`, `File.*`, or any I/O into the projection. Instead expose the refusal as **pure data**: a
    diagnostics record/collection carrying only `TenantId`, `WorkItemId`, the event **type name**
    (e.g. `nameof(ProgressReported)`), and the `Sequence` — no `DoneDelta`, no `Estimated`, no `Unit`
    value, no `Note`. A runtime adapter (out of scope here) is what eventually logs that metadata.
  - [x] Make the diagnostic deterministic and replay-safe: it must carry no clocks, no generated ids, and
    no payload values, and must re-derive identically on rebuild.
  - [x] "No mixed-unit Roll-Up view is published as fresh" — ensure a degraded node's read model is clearly
    marked so a publisher/consumer does not treat the stale-but-retained subtotals as a freshly converged
    value. Document the contract: degraded ⇒ last valid value retained + flagged, not silently fresh.

- [x] **Task 4 - Add focused projection unit tests (AC: #1-#6)**
  - [x] Extend `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` (reuse its `Tenant`,
    `Hour`, `Point`, `Created`, `Project`, `Envelope` helpers — do not create a parallel harness).
  - [x] AC #1: a single-Unit subtree exposes exactly one rolled subtotal and a non-null single
    `RolledRemaining` for that Unit (the existing same-unit tests partially cover this; add an explicit
    "single unit ⇒ single subtotal" assertion if not already unambiguous).
  - [x] AC #2: a multi-Unit subtree (e.g. parent in `hour`, child in `point`) exposes one
    `RolledRemainingByUnit` entry per Unit, the single `RolledRemaining` stays `null`, and no entry is the
    sum of two units (no implicit conversion). Add a three-unit / deeper-tree case so per-Unit grouping is
    proven beyond two units. (Builds on `Mixed_units_are_exposed_as_per_unit_values_without_fabricated_single_rollup`.)
  - [x] AC #3: progress and re-estimate that **match** the established Unit update the matching per-Unit
    subtotal incrementally and leave other Units' subtotals untouched in a mixed tree.
  - [x] AC #5: a unit-mismatched `ProgressReported` (and a separate unit-mismatched `ReEstimated`) on an
    established node leaves the node's last valid own/rolled value unchanged, marks the node degraded, and
    produces a metadata-only diagnostic (assert tenant, work item, event type name, sequence present;
    assert no payload value is exposed). Cover duplicate and out-of-order delivery of the bad event
    converging to the same degraded result.
  - [x] AC #5 boundary: the **first** estimate that establishes a unit on an unestimated node is NOT
    degraded; a later same-unit event is NOT degraded; only a post-establishment disagreeing unit is.
  - [x] AC #5 + terminal precedence: a degraded child that then becomes terminal contributes zero and is
    not resurrected by the degraded flag.
  - [x] AC #6: assert `WorkItemRollUp` exposes labeled per-Unit subtotals (each entry's `Unit` is
    populated) and exposes no coerced all-unit total field.

- [x] **Task 5 - Verify the command-side guard already satisfies AC #4 (AC: #4)**
  - [x] Confirm existing coverage proves AC #4 end to end: `WorkItemProgressTests.ReportProgress_with_unit_mismatch_returns_progress_rejection_and_leaves_state_unchanged`,
    `WorkItemReEstimateTests.ReEstimate_with_different_unit_returns_rejection_and_leaves_effort_unchanged`,
    and `WorkItemReEstimateTests.ReEstimate_after_establishing_first_estimate_rejects_a_different_unit_and_preserves_it`.
  - [x] If a clause of AC #4 is not already asserted ("no Roll-Up projection update is produced from that
    invalid act"), add one focused test that ties a rejected command to "no `ProgressReported`/`ReEstimated`
    event emitted ⇒ nothing to deliver to the projection." Do not duplicate the already-green rejection
    tests; add only the missing assertion.

- [x] **Task 6 - Extend property coverage for heterogeneous-unit convergence (AC: #2, #3, #5)**
  - [x] Extend `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs` to generate
    trees whose nodes carry **more than one Unit**, and assert that every permutation/duplicate-expanded
    delivery converges to the same per-Unit `RolledRemainingByUnit` map as canonical sequence-order replay
    (extending the existing single-unit convergence property — do not regress it).
  - [x] Include at least one generated or fixed case that injects a post-establishment unit-mismatch event
    and asserts the degraded outcome is identical under permutation/duplication (fail-closed is also
    order-tolerant and idempotent).
  - [x] Keep the property deterministic for CI: bounded generator sizes or fixed seed, no clocks, no
    sleeps, no random generation inside production projection code.

- [x] **Task 7 - Update documentation and architecture fitness (AC: #1-#6)**
  - [x] Update `docs/work-roll-up-projection.md` with a heterogeneous-unit section: same-Unit subtree ⇒
    single subtotal; mixed-Unit subtree ⇒ per-Unit subtotals with no coercion; command-side unit-mismatch
    rejection (write guard); projection fail-closed on a persisted incompatible-unit event (read-side
    defense-in-depth, analogous to the tenant-equality re-check); degraded marking + retain-last-valid;
    metadata-only diagnostics (tenant, work item, event type, sequence — never payload values) surfaced as
    pure data, not via I/O.
  - [x] Keep `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs` green:
    `P0_WorkItemSliceAllowsRollUpOnlyInProjectionAndContractReadModelsAndKeepsRemindersDeferred` already
    permits roll-up code only under `src/Hexalith.Works.Contracts/Models` and `src/Hexalith.Works.Projections`
    — keep all new code in those locations so the guard still passes. Keep `P0_WorkItemKernelRemainsPure`
    green by adding no clocks/timers/ids/Dapr/`File.`/`Directory.`/`HttpClient` to the projection or
    contracts.
  - [x] Optional fitness hardening (only if cheap and unambiguous): a test asserting `WorkItemRollUp` (and
    any new read-model) exposes no single coerced all-unit total property name, so a future change cannot
    silently reintroduce a fabricated total.
  - [x] Update `_bmad-output/implementation-artifacts/tests/test-summary.md` with Story 3.4 final counts
    and commands (Story 3.3 baseline = **404** green: UnitTests 325, IntegrationTests 52,
    ArchitectureTests 26, PropertyTests 1).
  - [x] Preserve Hexalith dependency policy: `ProjectReference` for Hexalith libraries through root
    submodule path variables, no Hexalith package references, and do not initialize nested submodules.

- [x] **Task 8 - Build and verify the slice (AC: #1-#6)**
  - [x] `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal`
  - [x] `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal`
    (warnings-as-errors: must be 0 warnings / 0 errors)
  - [x] Run direct xUnit v3 binaries (the reliable path when `dotnet test` is blocked by
    Microsoft.Testing.Platform named-pipe permissions):
    `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests`,
    `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests`,
    `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests`,
    and `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests`.

## Dev Notes

### Scope Boundary

Story 3.4 hardens the heterogeneous-unit safety of the roll-up that Story 3.3 introduced. It realizes
FR-12 (and the unit-safety portion of FR-11) by guaranteeing that mixed-Unit subtrees always expose
per-Unit subtotals and never a coerced single figure, and by making the projection **fail closed** when a
persisted child event disagrees with a node's established Unit. [Source:
_bmad-output/planning-artifacts/epics.md#Story 3.4: Preserve Heterogeneous Unit Subtotals;
_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-12]

**In scope:** explicit per-Unit subtotal verification (AC #1-#3, #6), projection fail-closed handling of
already-persisted incompatible-unit `ProgressReported`/`ReEstimated` events with degraded marking and
retain-last-valid behavior (AC #5), a consumer-visible degraded indicator plus metadata-only diagnostics
surfaced as pure data (AC #5, #6), and unit/property/doc/fitness coverage. Command-side unit-mismatch
rejection (AC #4) already exists and is **verified, not re-implemented**.

**Out of scope:** Unit conversion of any kind (explicitly deferred — `[ASSUMPTION: no Unit conversion in
v1]`), the second (Cost) meter dimension (Theme 5), suspend/resume matching (Story 3.5), cascade
traversal / runtime command emission (Story 3.6), the runtime projection adapter that actually emits log
lines or wires EventStore, SignalR/UI rendering of subtotals, query-side authorization endpoints, and the
"what's next" projection. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.5; #Story 3.6;
_bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-12]

### Current State (files this story modifies or verifies — read before editing)

- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` already does most of the
  per-Unit work. `RemainingBuckets` keys decimal contributions by `Unit` and emits one `RolledRemaining`
  per Unit ordered by unit value. `ToReadModel` sets the single `RolledRemaining` only when `byUnit.Count
  == 1`, so mixed-Unit subtrees already get `null` for the single value (no coercion). **Gap:**
  `ApplyPayload` handles `ProgressReported` with the guard `when ... node.OwnEffort.Unit == progress.Unit`
  — a mismatched unit falls through and is **silently ignored** (no degraded marker, no diagnostic). A
  mismatched `ReEstimated` on an established node silently keeps the old unit via `WorkItemEffort.ReEstimate`.
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs` is the read model: `OwnRemaining?`,
  `RolledRemaining?` (single, same-unit only), `IReadOnlyList<RolledRemaining> RolledRemainingByUnit`,
  child ids, contribution count, and `LatestAcceptedSourceSequence`. It has **no degraded indicator** yet.
- `src/Hexalith.Works.Contracts/Models/RolledRemaining.cs` and `OwnRemaining.cs` are
  `record(decimal Value, Unit? Unit)` — already labeled by Unit.
- `src/Hexalith.Works.Contracts/ValueObjects/WorkItemEffort.cs` — `Report(doneDelta)` clamps `Done` to
  `Estimated`; `ReEstimate(newEstimated)` **preserves the immutable Unit** and clamps `Done` to the new
  estimate. There is no unit parameter on either method — the value object cannot change unit, which is
  why mismatched units must be caught before they reach it.
- `src/Hexalith.Works.Contracts/ValueObjects/Unit.cs` — `record(string Value)`, non-empty; equality is
  value-based and ordinal-string in the buckets. Unit is per-item and immutable after first estimate.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` — `Handle(ReportProgress)` rejects a
  mismatched unit with `WorkItemProgressRejected` (lines ~246-249); `Handle(ReEstimate)` rejects a
  mismatched unit with `WorkItemReEstimateRejected` (lines ~292-295), and the first estimate on an
  unestimated item establishes the unit. AC #4 write-side guard is complete here.
- `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemProgressRejected.cs` and
  `WorkItemReEstimateRejected.cs` carry `(TenantId, WorkItemId, Reason)` and are returned to the caller,
  never appended to the stream (no `Sequence`).
- `src/Hexalith.Works.Contracts/Events/ProgressReported.cs` carries `(…, DoneDelta, Unit, Note?)`;
  `ReEstimated.cs` carries `(…, Estimated, Unit, Note?)`. Both are durable v1 payloads — **do not change
  their shape** (additive-only schema policy).
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` already covers parent/child, nested
  descendants, duplicate/out-of-order, terminal zero, requeue non-terminal, edge convergence, tenant
  isolation, mixed units (per-unit, no fabricated single), unestimated items, child-before-edge,
  stale-after-terminal, ignored lifecycle events, and invalid delivery facts.

### Key Design Decisions

- **D1 - AC #4 is already done; AC #5 is the new defense-in-depth.** The write side rejects mismatched
  units before emission (AC #4). AC #5 is the **read-side** mirror: even a persisted/legacy/malformed
  event that disagrees with an established Unit must not corrupt the roll-up. This is exactly analogous to
  Story 3.3's D4 (tenant equality re-checked on the read side even though spawn already guards writes).
  [Source: _bmad-output/implementation-artifacts/3-3-maintain-recursive-roll-up-with-per-child-sequence.md#Key Design Decisions;
  _bmad-output/planning-artifacts/architecture.md#A3]
- **D2 - Fail closed = refuse + retain + flag, never coerce and never crash.** A unit-incompatible event
  must not be applied (no reinterpretation under the wrong unit), the node retains its last valid value,
  and the node is marked degraded. The projection must not throw on bad data — it stays available and
  loud-but-safe. [Source: _bmad-output/planning-artifacts/epics.md#Story 3.4 (AC #5)]
- **D3 - Diagnostics are pure data, not I/O.** `Hexalith.Works.Projections` references Contracts only
  (DependencyDirectionTests + kernel-purity fitness). AC #5's "logs include only … metadata, never
  payload values" is satisfied by exposing a metadata-only diagnostic record (tenant, work item, event
  type name, sequence) that a future runtime adapter logs — never by adding a logger/Console/File to the
  pure projection. [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs;
  docs/work-roll-up-projection.md#Boundaries]
- **D4 - Per-Unit map is the uniform shape; the single value is a same-Unit convenience.** The roll-up
  read model is always a per-Unit list (`RolledRemainingByUnit`) that degenerates to one entry for
  same-Unit subtrees; the scalar `RolledRemaining` is populated only in that degenerate case. There is
  never a coerced all-Unit total. This resolves the FR-11 (scalar) vs FR-12 (map) surface tension already
  flagged in PRD review §2.6. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/review-readiness.md#2.6;
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-12]
- **D5 - Establishment is not a mismatch.** The first effort (`WorkItemCreated.InitialEffort`, spawn
  facts, or the first `ReEstimated`) legitimately sets the Unit. Only events arriving after a Unit is
  established and disagreeing with it are incompatible. Re-derive this deterministically during the
  existing sequence-ordered rebuild so degraded state is idempotent and order-tolerant. [Source:
  _bmad-output/planning-artifacts/architecture.md#A3; #A5]
- **D6 - Terminal-zero precedence over degraded.** Story 3.3's terminal-zero rule wins: a terminal child
  contributes zero regardless of any degraded flag; degraded must not resurrect a terminal contribution.
  [Source: _bmad-output/implementation-artifacts/3-3-maintain-recursive-roll-up-with-per-child-sequence.md#Key Design Decisions (D3)]

### Technical Requirements

- Keep `Contracts` and `Projections` infrastructure-free and pure: no Dapr, EventStore.Server, HTTP,
  filesystem, timers/clocks, generated IDs, logging I/O, UI, LLM, routing, or cost-governance dependency.
  `Projections` references `Contracts` only. [Source:
  _bmad-output/planning-artifacts/architecture.md#Architectural Boundaries;
  tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs;
  tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs#P0_WorkItemKernelRemainsPure]
- New roll-up code must live only under `src/Hexalith.Works.Contracts/Models` or
  `src/Hexalith.Works.Projections` so `P0_WorkItemSliceAllowsRollUpOnlyInProjectionAndContractReadModelsAndKeepsRemindersDeferred`
  stays green; do not introduce any `Reminder` term in `src`. [Source:
  tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs]
- Additive schema evolution only: do not change `ProgressReported`, `ReEstimated`, or any durable event
  shape; do not add `V2` payloads; do not rename events/commands. A new read-model field is additive and
  is **not** a durable stream payload, so it needs no golden-corpus fixture and must not be added to the
  polymorphic command/event catalog unless the existing pattern requires it. [Source:
  _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-12;
  _bmad-output/implementation-artifacts/3-3-maintain-recursive-roll-up-with-per-child-sequence.md#Technical Requirements]
- Use xUnit v3 + Shouldly for unit/integration tests and FsCheck for property tests; keep tests pure and
  deterministic. Direct xUnit v3 executables are the reliable sandbox verification path when `dotnet test`
  is blocked. [Source: _bmad-output/implementation-artifacts/3-3-maintain-recursive-roll-up-with-per-child-sequence.md#Debug Log References]
- No new package dependencies. Repo pins .NET SDK `10.0.301`, xUnit v3 `3.2.2`, Shouldly `4.3.0`,
  FsCheck `3.3.3`. Do not upgrade. [Source: global.json; Directory.Packages.props]

### Project Structure Notes

- Projection fail-closed logic belongs in `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs`
  (and a small projection-owned model under `Models/` only if needed for the diagnostic).
- The degraded indicator / diagnostics contract belongs in `src/Hexalith.Works.Contracts/Models`
  (extend `WorkItemRollUp` additively; add a sibling record only if it reads cleaner).
- Projection unit tests go in `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs`;
  convergence properties extend `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs`.
- AC #4 verification reuses `tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs` and
  `WorkItemReEstimateTests.cs`.
- Do not change `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` for AC #4 unless a genuinely
  missing assertion forces a tiny addition — the rejection paths already exist.
- Do not create `.UI`, `.Mcp`, portal, `.Security`, channel-adapter, database, Dapr-actor, repository, or
  runtime-host projects for this story.

### Previous Story Intelligence

- Story 3.3 completed in commit `5c95d1e`, introducing `OwnRemaining`, `RolledRemaining`, `WorkItemRollUp`,
  `WorkItemRollUpEvent`, and `WorkItemRollUpProjection`, and narrowing the architecture guard so roll-up
  is permitted only in Contracts `Models` and `Projections`. Final state: **404** green tests (UnitTests
  325, IntegrationTests 52, ArchitectureTests 26, PropertyTests 1). Use 404 as the 3.4 baseline.
- Story 3.3 already added `Mixed_units_are_exposed_as_per_unit_values_without_fabricated_single_rollup`
  and an FsCheck convergence property over generated tenant-safe trees — extend these, do not duplicate.
- Story 3.3's review showed the property test must genuinely **generate** varied shapes (a fixed tree was
  flagged CRITICAL and auto-fixed). For 3.4, generate varied **unit combinations**, not just one mixed
  pair, and include the degraded-event case.
- The working tree already carries unrelated modified submodule pointers (`Hexalith.FrontComposer`,
  `Hexalith.Parties`) and a story-automator orchestration file. Do not revert or depend on those.
- The reliable verification path remains restore → build → direct xUnit v3 binaries (UnitTests,
  IntegrationTests, ArchitectureTests, PropertyTests).

### Git Intelligence Summary

- Recent commits are tight additive story slices with paired tests/docs:
  `5c95d1e feat(story-3.3): Maintain recursive roll-up with per-child sequence`,
  `eaeaf2e feat(story-3.2): Spawn child work from a parent`,
  `5792291 feat(story-3.1): Guard tenant-safe work tree shape`,
  `c1ba6bb test(story-2.5): Verify terminal work decisions`.
- Story slices update the story file, sprint status, test summary, docs, and the relevant
  contract/unit/property/architecture tests; golden corpus and the v1 catalog change only when a durable
  payload changes (this story adds none).

### Latest Technical Information

- No new third-party library or framework version is required. Use the repo-pinned stack: .NET SDK
  `10.0.301` (`rollForward: latestPatch`), Aspire `13.4.3`, Dapr `1.18.2`, xUnit v3 `3.2.2`, Shouldly
  `4.3.0`, FsCheck `3.3.3`. Do not upgrade any of them.
- No web/API research is required: this is pure in-process domain projection code; the only external
  contracts in play are the already-checked-out Hexalith EventStore `IEventPayload` interface (referenced
  by the existing `WorkItemRollUpEvent`) and Works' own contracts.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.4: Preserve Heterogeneous Unit Subtotals]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-11]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#FR-12]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/prd.md#NFR-12]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/review-readiness.md#2.6]
- [Source: _bmad-output/planning-artifacts/prds/prd-works-2026-06-14/review-readiness.md#6.7]
- [Source: _bmad-output/planning-artifacts/architecture.md#A3]
- [Source: _bmad-output/planning-artifacts/architecture.md#A5]
- [Source: _bmad-output/planning-artifacts/architecture.md#B3]
- [Source: _bmad-output/planning-artifacts/ux-designs/ux-works-2026-06-14/EXPERIENCE.md#Burn-Down meter; #Roll-Up "one number"; #Heterogeneous Units]
- [Source: docs/work-roll-up-projection.md#Rules]
- [Source: _bmad-output/implementation-artifacts/3-3-maintain-recursive-roll-up-with-per-child-sequence.md#Key Design Decisions]
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs]
- [Source: tests/Hexalith.Works.ArchitectureTests/FitnessTests/DependencyDirectionTests.cs]
- [Source: CLAUDE.md#Hexalith library references — ALWAYS use ProjectReference]
- [Source: CLAUDE.md#Submodule rules]

## Dev Agent Record

### Agent Model Used

GPT-5 Codex

### Debug Log References

- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal` — passed.
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` — passed with 0 warnings and 0 errors.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests` — 335/335 passed.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` — 52/52 passed.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` — 28/28 passed.
- `tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests` — 1/1 passed; FsCheck reported 100 generated cases.
- Final verified totals: UnitTests 335, IntegrationTests 52, ArchitectureTests 28, PropertyTests 1 = **416** green (332 unit / 27 arch reported by the initial dev-story pass were raised to 335 / 28 by the QA gap-filling pass; see tests/test-summary.md).

### Completion Notes List

- Ultimate context engine analysis completed - comprehensive developer guide created.
- Confirmed Story 3.3 already exposed per-unit roll-up buckets and scalar `RolledRemaining` only for a single unit; Story 3.4 did not reimplement those paths.
- Added read-side fail-closed handling for already-persisted unit-incompatible progress/re-estimate events: retain last valid effort, mark degraded, and emit deterministic metadata-only diagnostics.
- Extended projection, command guard, property, and architecture tests for heterogeneous units, degraded convergence, terminal precedence, and no coerced all-unit total surface.
- Updated roll-up documentation and test summary with Story 3.4 behavior and final validation counts.

### File List

- `_bmad-output/implementation-artifacts/3-4-preserve-heterogeneous-unit-subtotals.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/tests/test-summary.md`
- `docs/work-roll-up-projection.md`
- `src/Hexalith.Works.Contracts/Models/RollUpProjectionDiagnostic.cs`
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs`
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs`
- `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemProgressTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemReEstimateTests.cs`
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs`

### Change Log

- 2026-06-16: Implemented Story 3.4 heterogeneous-unit fail-closed roll-up behavior, diagnostics, tests, docs, and verification.
- 2026-06-16: Senior Developer Review (AI) — auto-fix pass. Corrected stale Debug Log References (332→335 unit, 27→28 arch) and removed a duplicated heading in tests/test-summary.md. 0 CRITICAL / 0 HIGH; status → done.

## Senior Developer Review (AI)

**Reviewer:** Jérôme Piquot · **Date:** 2026-06-16 · **Mode:** adversarial, auto-fix

**Outcome:** Approved (status → done). 0 CRITICAL, 0 HIGH.

**Scope verified (live build + test run):**

- `dotnet build Hexalith.Works.slnx -c Release` — 0 warnings, 0 errors (warnings-as-errors honored).
- UnitTests **335/335**, IntegrationTests **52/52**, ArchitectureTests **28/28**, PropertyTests **1/1**
  (FsCheck 100 generated cases) = **416** green, run via the direct xUnit v3 binaries.
- Story File List matches `git status` (source/test/doc); `_bmad-output/*` and submodule pointers
  correctly excluded.

**Acceptance Criteria:** all six implemented and tested. AC #1 (single-unit single subtotal), AC #2
(per-unit, no cross-unit summation incl. a within-unit-folds / across-unit-stays-separate case and a
three-unit deeper tree), AC #3 (matching-unit incremental update leaves other units untouched), AC #4
(command-side rejection emits no event ⇒ no projection delivery), AC #5 (read-side fail-closed: refuse +
retain-last-valid + degraded + metadata-only diagnostic, idempotent/order-tolerant on replay), AC #6
(labeled per-unit subtotals, no coerced all-unit total, structurally guarded by two new fitness tests).

**Task audit:** all 8 tasks marked `[x]` are genuinely done with file-level evidence.

**Findings (all resolved or accepted):**

- 🟡 MEDIUM (fixed) — Debug Log References under-reported the final verified state (claimed 332 unit /
  27 arch; actual 335 / 28). Updated to the verified counts; `tests/test-summary.md` was already correct.
- 🟢 LOW (fixed) — duplicated `## Gaps closed this run` heading in `tests/test-summary.md` removed.
- 🟢 LOW (accepted, not changed) — the read model recomputes rolled value, `Degraded`, and diagnostics
  via three independent full-subtree traversals per `Get`/`Snapshot` (O(N·subtree)). Acceptable at the
  current bounded-tree, in-memory scale and consistent with the explicitly deferred runtime/perf scope;
  memoization would add regression risk for no present benefit.
