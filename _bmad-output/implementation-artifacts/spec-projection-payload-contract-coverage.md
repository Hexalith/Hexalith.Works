---
title: 'Projection payload contract coverage'
type: 'bugfix'
created: '2026-09-01'
status: 'in-progress'
baseline_revision: '0a8847963feda2360a084f2b0ac1969120902140'
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

**Problem:** Roll-up payload admission is gated against Contracts, but effect application is a second ungated switch; what’s-next duplicates both concerns without a Contracts-derived gate. A newly admitted non-rejection payload can therefore consume an envelope sequence and advance a freshness watermark without an explicit projection effect, or be silently omitted from what’s-next.

**Approach:** Give each projection one exact-type accepted-payload descriptor catalog that binds identity validation to a mandatory effect disposition and typed effect path. Gate both catalogs against the Contracts payload universe, with rejection exclusion and watermark-bearing intentional no-ops explicit.

## Boundaries & Constraints

**Always:** Preserve exact-type lookup, sorted replay, duplicate suppression, terminal/corrupt-effort guards, tenant isolation, current dispatcher persistence ordering, and current read-model/change-notification behavior. Keep `WorkItemRejected` classified as a successful raw act. Treat roll-up `WorkItemRescheduled` and what’s-next `ChildSpawned` as explicit intentional no-ops that still own accepted sequence slots; keep roll-up `ChildSpawned` as a topology effect. Use typed generic descriptor factories and production code without reflection.

**Block If:** Any concrete non-rejection Contracts payload cannot be assigned its current identity and projection effect without changing observable behavior, or preserving the roll-up versus what’s-next malformed-`ChildSpawned` policies is impossible with one descriptor per projection.

**Never:** Edit the deferred-work ledger or `.bmad-loop` run artifacts; change Contracts payloads, event decoding, dispatcher/state-store topology, projection output shapes, watermark semantics, or rejection exclusion; admit unknown/rejection payloads as no-ops; add a second hand-maintained admission/effect switch beside a descriptor catalog.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Classified effect | Matching non-rejection delivery | Exact descriptor validates identity, reserves the sequence once, and applies its typed topology/fold effect | No default payload fallthrough exists |
| Intentional no-op | Roll-up `WorkItemRescheduled` or what’s-next `ChildSpawned` | Visible model/change result is unchanged and `LatestAcceptedSourceSequence` advances | Descriptor marks the no-op explicitly |
| Refused payload | Rejection, unknown type, identity mismatch, or projection-specific malformed payload | No node/slot/watermark is consumed; a later valid same-sequence delivery succeeds | Fail closed without weakening tenant isolation |
| Contract growth | New concrete Contracts `IEventPayload` | Both architecture gates fail until each projection represents it exactly once with an effect disposition | Only `IRejectionEvent` may remain excluded |

</intent-contract>

## Code Map

- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs:16-190` -- current identity registry and delivery floor; retain configurable identity comparison but move payload authority to the roll-up descriptor.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:35-227` -- admission, one-shot topology switch, sorted rebuild, and fold switch currently drift independently; `Rebuild` advances the watermark before `ApplyPayload`.
- `src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs:48-214` -- independent 14-arm matcher and fold switch; `ChildSpawned` is the existing intentional watermark-only no-op.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:123-214` -- read-only behavior anchor: roll-up watermark guards persistence and tenant-index writes; descriptor work must not change ordering or storage semantics.
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs:18-21` -- read-only evidence that Contracts payload discovery is automatic, making projection catalog drift possible.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/WorkItemRollUpPayloadCoverageTests.cs` -- existing roll-up identity-only Contracts gate; replace with per-projection descriptor/effect coverage.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpTenantIsolationTests.cs` and `WorkItemRollUpProjectionTests.cs` -- supported fixtures, fail-closed slot tests, topology/replay behavior, and roll-up no-op watermark proof.
- `tests/Hexalith.Works.UnitTests/WhatsNextQueueProjectionTests.cs:261-310,643-657` -- mismatch and `ChildSpawned` behavior; add catalog-wide identity, rejection-slot, and explicit no-op watermark coverage.
- `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs` and `WhatsNextOrderingConvergencePropertyTests.cs` -- unchanged convergence regressions for descriptor-driven replay.
- `docs/work-roll-up-projection.md`, `docs/whats-next-projection.md`, `CHANGELOG.md` -- correct “state-changing” watermark wording and document descriptor-governed intentional no-ops.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- read-only; orchestration owns DW-53/DW-54 resolution.

## Tasks & Acceptance

**Execution:**
- `src/Hexalith.Works.Projections/Strategies/ProjectionPayloadEffectDisposition.cs`, `WorkItemRollUpPayloadDescriptor.cs`, and `WhatsNextPayloadDescriptor.cs` -- add focused descriptor types/factories that derive exact keys and casts from one payload type and require either typed effects or an explicit intentional-no-op disposition.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs` and `WorkItemRollUpProjection.cs` -- resolve one roll-up descriptor before allocation, validate its identity through the isolation policy, accept once, and dispatch descriptor-owned topology/fold effects during sorted replay.
- `src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs` -- replace the independent matcher/fold switches with one descriptor lookup and descriptor-owned replay effect while preserving accept-before-signature behavior.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/ProjectionPayloadCoverageTests.cs` -- replace the roll-up-only test with Contracts-derived equality, uniqueness, rejection-exclusion, non-unspecified-disposition, and pinned intentional-no-op assertions for both projections.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpTenantIsolationTests.cs`, `WorkItemRollUpProjectionTests.cs`, and `WhatsNextQueueProjectionTests.cs` -- retarget fixture coverage to descriptors and prove both explicit no-ops, all matching identities, mismatches, rejection/unknown exclusion, and same-sequence reuse.
- `docs/work-roll-up-projection.md`, `docs/whats-next-projection.md`, and `CHANGELOG.md` -- describe accepted-payload watermarks and the two explicit projection-specific no-ops without changing public semantics.

**Acceptance Criteria:**
- Given all concrete Works Contracts payloads, when architecture coverage inspects each projection catalog, then every non-rejection type appears exactly once with a non-unspecified effect disposition and every excluded type implements `IRejectionEvent`.
- Given any admitted payload, when a projection accepts its envelope position, then the same descriptor supplies identity validation and its typed effect or explicit no-op; no independent payload switch can silently advance the watermark.
- Given the two intentional no-op payloads, when they are delivered with matching identities, then their sequence is retained while roll-up state or what’s-next eligibility/order remains unchanged.
- Given rejection, unknown, mismatched, or projection-specific malformed input, when it is delivered before a valid event at the same sequence, then the refused input allocates/changes nothing and the valid event is accepted.
- Given natural, duplicate, and permuted replay, when the existing roll-up and what’s-next convergence suites run, then read models and change semantics remain identical to the pre-change behavior.

## Spec Change Log

## Review Triage Log

## Design Notes

Descriptors are runtime dispatch tables, not test allowlists. Roll-up retains two explicit effect phases because `WorkItemCreated`/`ChildSpawned` topology is applied only after sequence acceptance, while node state is re-folded in sorted envelope order. What’s-next has only the sorted fold phase. Rejections remain outside both catalogs, so they never become watermark-bearing no-ops.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx` -- expected: restore succeeds without dependency changes.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore` -- expected: zero warnings and errors.
- `dotnet test tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release --no-build` -- expected: all focused descriptor, isolation, and projection tests pass.
- `dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release --no-build` -- expected: both Contracts-derived descriptor gates pass.
- `dotnet test tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj -c Release --no-build` -- expected: roll-up and what’s-next convergence properties pass.
- `dotnet test tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release --no-build` -- expected: persisted projection behavior passes; infrastructure-only smoke tests may report their existing environment skips.
- `git diff --check` -- expected: no whitespace errors.
