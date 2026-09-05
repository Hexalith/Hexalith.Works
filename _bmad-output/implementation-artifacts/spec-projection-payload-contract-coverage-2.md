---
title: 'Projection payload contract coverage completion'
type: 'bugfix'
created: '2026-09-01'
status: ready-for-dev
baseline_revision: 01d527abcf7d8f5f2b279de56afcf6f9a4437f89
baseline_commit: 'df46f716565237074e3c1bd7f09e7eeaf411cc16'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/implementation-artifacts/spec-rollup-contract-drift-hardening.md'
warnings:
  - oversized
deferred: []
---

<intent-contract>

## Intent

**Problem:** The descriptor refactor at the baseline revision is incomplete: production does not compile, stale tests still reference the removed roll-up allowlist, and neither projection is fully gated and behavior-tested against the Contracts payload universe. Without the completed gate, a newly admitted payload can still consume a sequence and advance freshness without an explicit projection effect, or silently disappear from what's-next.

**Approach:** Complete the existing exact-type descriptor catalogs so each admitted payload has one identity reader and mandatory typed effect disposition, then replace stale coverage with Contracts-derived gates and focused refusal/no-op/reuse tests for both projections.

## Boundaries & Constraints

**Always:** Preserve exact-type lookup, sorted replay, duplicate suppression, terminal and corrupt-effort guards, tenant isolation, dispatcher persistence ordering, and current read-model/change-notification behavior. Keep `WorkItemRejected` as a successful raw act. Retain roll-up `WorkItemRescheduled` and what's-next `ChildSpawned` as explicit watermark-bearing no-ops, while roll-up `ChildSpawned` remains a topology effect. Preserve the projection-specific malformed-`ChildSpawned` policy: roll-up refuses a missing child id, while what's-next accepts a parent/header-consistent payload.

**Block If:** Any concrete non-rejection Contracts payload cannot retain its current identity/effect behavior, or the two malformed-`ChildSpawned` policies cannot remain distinct without adding a second admission/effect switch.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md`, `.bmad-loop/**`, submodule contents or pointers, Contracts payload shapes, decoder/dispatcher topology, projection output shapes, watermark semantics, or rejection exclusion. Never admit unknown or rejection payloads as no-ops, add production reflection, or restore a parallel hand-maintained payload switch.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Classified effect | Matching non-rejection delivery | Its exact descriptor validates identity, reserves the sequence once, and applies the typed topology/fold effect | No default payload fallthrough exists |
| Intentional no-op | Roll-up `WorkItemRescheduled` or what's-next `ChildSpawned` | Visible model/order remains unchanged and `LatestAcceptedSourceSequence` advances | Descriptor explicitly declares `IntentionalNoOp` |
| Refused delivery | Rejection, unknown type, identity mismatch, or roll-up malformed `ChildSpawned` | No node, slot, state, or watermark is consumed; a later valid same-sequence delivery succeeds | Fail closed without weakening isolation |
| What's-next malformed child | Parent/header-consistent `ChildSpawned` with a missing child id | The accepted no-op owns its sequence and leaves the queue unchanged | Preserve the existing projection-specific policy |
| Contract growth | New concrete Contracts `IEventPayload` | Both architecture gates fail until both catalogs represent it exactly once with a specified effect | Only `IRejectionEvent` types may remain excluded |

</intent-contract>

## Code Map

- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpPayloadDescriptor.cs:17-253` -- 14-entry exact-type roll-up catalog; repair nullable `TryResolve` and keep identity, topology, fold, and intentional-no-op ownership here.
- `src/Hexalith.Works.Projections/Strategies/WhatsNextPayloadDescriptor.cs:17-246` -- matching what's-next catalog; repair nullable `TryResolve` and retain `ChildSpawned` as its sole no-op.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:35-374` -- descriptor-driven acceptance/rebuild is present but nested `NodeKey`/`RollUpNode` accessibility currently causes 19 compile errors; expose only the internal seams needed by the descriptor and containing projection.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs:43-190` -- delivery validation now requires a resolved descriptor; preserve the configurable identity-comparison flag and unconditional well-formedness floor.
- `src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs:48-214` -- descriptor-driven accept-before-signature fold; retain sorted replay and change semantics.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:123-214` and `WorkItemProjectionEventDecoder.cs:18-21` -- read-only anchors for persistence ordering and automatic Contracts payload discovery.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/WorkItemRollUpPayloadCoverageTests.cs` -- stale roll-up-only registry gate; replace with dual descriptor-catalog coverage.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpTenantIsolationTests.cs`, `WorkItemRollUpProjectionTests.cs`, and `WhatsNextQueueProjectionTests.cs` -- stale APIs plus the focused identity, refusal, no-op watermark, and same-sequence reuse coverage.
- `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs`, `WhatsNextOrderingConvergencePropertyTests.cs`, and `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` -- unchanged replay and persistence-order regression anchors.
- `docs/work-roll-up-projection.md`, `docs/whats-next-projection.md`, projection/read-model XML comments, and `CHANGELOG.md` -- stale “state-changing”/identity-registry wording that must describe accepted descriptor-governed watermarks.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpPayloadDescriptor.cs`, `WhatsNextPayloadDescriptor.cs`, and `WorkItemRollUpProjection.cs` -- repair definite assignment and nested accessibility with the narrowest internal seams so the committed descriptor design compiles without changing behavior.
- [x] `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ProjectionPayloadCoverageTests.cs` -- replace the stale file with Contracts-derived equality, uniqueness, rejection-only exclusion, non-`Unspecified` disposition, and exact no-op-set assertions for both catalogs.
- [x] `tests/Hexalith.Works.UnitTests/WorkItemRollUpTenantIsolationTests.cs` and `WorkItemRollUpProjectionTests.cs` -- resolve descriptors at the isolation seam, retarget fixture coverage to the catalog, prove malformed-child same-sequence reuse, and pin the roll-up `WorkItemRescheduled` no-op watermark.
- [x] `tests/Hexalith.Works.UnitTests/WhatsNextQueueProjectionTests.cs` -- add all-catalog matching identity coverage; identity mismatch, rejection, and unknown refusal with same-sequence reuse; and explicit `ChildSpawned` no-op watermark coverage including the accepted malformed-child policy.
- [x] `docs/work-roll-up-projection.md`, `docs/whats-next-projection.md`, affected source XML/comments, and `CHANGELOG.md` -- document accepted-payload freshness and the two projection-specific intentional no-ops without changing public contracts.

**Acceptance Criteria:**
- Given all concrete Works Contracts payload types, when each architecture gate inspects its catalog, then every non-rejection type appears exactly once with a non-`Unspecified` effect and every excluded type implements `IRejectionEvent`.
- Given any admitted delivery, when its sequence is accepted, then the same descriptor supplies identity validation and the typed topology/fold effect or explicit no-op.
- Given either intentional no-op, when its identity matches, then the sequence watermark advances while roll-up state or what's-next eligibility/order remains unchanged.
- Given rejection, unknown, mismatched, or projection-specific malformed input, when it precedes a valid delivery at the same sequence, then the refused input allocates/changes nothing and the valid delivery is accepted.
- Given natural, duplicate, and permuted replay plus dispatcher persistence tests, when the existing unit/property/integration lanes run, then read models, tenant isolation, convergence, and independent watermark ordering remain unchanged.

## Spec Change Log

## Review Triage Log

## Design Notes

The catalogs are runtime dispatch tables, not test allowlists. Roll-up retains topology and sorted-fold phases because creation/spawn topology is applied once only after sequence acceptance, while node state is replayed in envelope order. What's-next uses only the sorted fold. Frozen dictionary enumeration is not ordered, so coverage comparisons must sort exact payload types before asserting equality.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx` -- expected: restore succeeds without dependency changes.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1` -- expected: zero warnings and errors.
- `dotnet test tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release --no-build` -- expected: descriptor/isolation/projection tests pass.
- `dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release --no-build` -- expected: both Contracts-derived catalog gates pass.
- `dotnet test tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj -c Release --no-build` -- expected: both convergence suites pass.
- `dotnet test tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release --no-build` -- expected: persisted projection behavior passes; infrastructure-only tests may report their established environment skips.
- `git diff --check` -- expected: no whitespace errors.

