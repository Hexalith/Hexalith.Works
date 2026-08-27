---
title: 'Refuse stale persisted roll-ups'
type: 'bugfix'
created: '2026-08-27'
status: 'done'
baseline_revision: 'ae8972565131eea04b7c1dbf57953dc1249c1021'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      A delayed older full-replay request can overwrite newer persisted work-item projection state.
    evidence: |-
      WorkItemProjectionDispatcher writes the tenant index and per-item roll-up without comparing the incoming LatestAcceptedSourceSequence to the stored item, so an older request completing later can replace newer status, effort, structure, and availability state. This behavior predates the stale-roll-up refusal change and needs a focused ordering/concurrency design.
    location: >-
      src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:152
    severity: medium
  - summary: >-
      Per-item roll-up persistence does not use the documented optimistic-concurrency write policy.
    evidence: |-
      PersistRollUpAsync calls IReadModelStore.SaveAsync directly, while docs/eventstore-api-surface-constraints.md describes per-item persistence as ReadModelWritePolicy/ETag guarded. The mismatch predates this bundle and should be reconciled separately without expanding the adapter refusal patch.
    location: >-
      src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:191
    severity: medium
  - summary: >-
      Roll-ups and tenant-index entries persisted before the refusal change keep their stale child-dependent totals until their own aggregate is dispatched again.
    evidence: |-
      ToBoundarySafeRollUp is applied only on the write path of a dispatch, and the dispatcher is the only writer of WorksReadModelKeys.RollUpKey and the what's-next tenant index. A child-only dispatch never rewrites the parent's keys, WhatsNextQueryHandler returns stored values verbatim with no read-side sanitization, WorksReadModelKeys carries no schema/version token, and no startup replay, rebuild, or invalidation path exists. A parent that appends no further events of its own therefore keeps serving its spawn-time total indefinitely. Every adapter test starts from a fresh InMemoryReadModelStore, so no test observes a pre-change document. Closing this needs a re-projection/backfill or read-side guard, which the approved adapter-boundary approach ("whenever the dispatched item has child contributions") does not cover.
    location: >-
      src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:123
    severity: medium
  - summary: >-
      A parent whose children were attached by a parented create still publishes a rolled total that silently omits them.
    evidence: |-
      CreateWorkItem accepts a Parent, and WorkItemRollUpProjection.Project adds the parent->child edge from WorkItemCreated.Parent on the child's stream. That create emits nothing on the parent's stream, so the parent's own dispatch sees ChildContributionCount == 0 and no ChildSpawned event name, ToBoundarySafeRollUp does not fire, and the parent is persisted as a leaf with an available rolled total that excludes those children. This predates the refusal change; detecting it from a single dispatch would require a cross-aggregate store read or merge protocol, which the intent's Block If excludes.
    location: >-
      src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:58
    severity: medium
---

<intent-contract>

## Intent

**Problem:** The runtime `/project` adapter replays one aggregate at a time, so a parent's persisted and query-visible rolled totals retain each child's spawn-time effort and cannot converge when the child later progresses. Those values look authoritative despite being stale.

**Approach:** At the runtime adapter boundary, expose and persist rolled totals as unavailable whenever the dispatched item has child contributions that cannot be reconciled. Preserve the complete local aggregate evidence—own effort, lifecycle status, parent/child structure, tenant identity, diagnostics, and freshness—and retain compatible `null`/empty query shapes.

## Boundaries & Constraints

**Always:** Apply the refusal before both what's-next composition and per-item persistence; clear both `RolledRemaining` and `RolledRemainingByUnit`; keep tenant-scoped keys and query authorization unchanged; preserve reliable own effort and terminal status; verify persisted read-model end state with deterministic tests.

**Block If:** The fix requires a new public contract shape, cross-aggregate store reads/merge protocol, or an EventStore/submodule change; these materially expand the approved adapter-boundary decision.

**Never:** Edit the deferred-work ledger; weaken the pure recursive roll-up projection or its convergence tests; fabricate a substitute total; mark substrate unavailability as corrupt-event degradation; add infrastructure to the Works kernel.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Parent progress | Parent replay contains own progress and one or more `ChildSpawned` facts | Persist/index current own effort, status, structure, and watermark; rolled fields are `null` and `[]` | Refuse stale totals without throwing |
| Separate child progress | Child is replayed after its parent was persisted | Child keeps current local effort/status; parent remains unavailable, never spawn-time stale | No cross-aggregate read or rewrite |
| Leaf item | Complete replay contains no child contribution | Locally reliable own-equals-rolled values remain available | No refusal needed |
| Terminal parent with children | Parent replay is terminal but has child facts | Terminal status and own evidence remain; child-dependent totals stay unavailable | Do not infer subtree convergence from terminal status |
| Cross-tenant collision | Same inner work-item id exists under another tenant | Existing tenant-scoped persistence and query filtering remain isolated | Fail closed as today |

</intent-contract>

## Code Map

- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:91` -- Creates fresh pure projections per single-aggregate request; raw `rollUp.Get` currently leaks spawn-derived totals into the tenant index at lines 121–126 and per-item store at lines 184–196. Add one boundary-safe model transformation and reuse that same model for both outputs.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:57` -- `ChildSpawned` synthesizes child initial effort for valid co-delivered pure projection scenarios. Read-only for this change.
- `src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs:234` -- Copies lookup-provided rolled fields; its existing null/empty convention is the compatibility seam.
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs:21` and `src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs:24` -- Existing nullable/list shapes already represent unavailable totals; no schema change.
- `src/Hexalith.Works/Queries/GetWorkItemQueryHandler.cs:71` -- Maps only reliable status, own effort, parent, and watermark; preserve this behavior.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs:30` -- Deterministic in-memory adapter/store/query harness; add parent and child progress persisted-end-state regressions here.
- `docs/eventstore-api-surface-constraints.md:63` -- Replace the false runtime-convergence claim with the refuse-stale boundary decision and update cascade wording at line 178.
- `docs/work-roll-up-projection.md:56` and `docs/whats-next-projection.md:53` -- Clarify pure co-available convergence versus runtime per-aggregate unavailability and its query representation.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- derive a boundary-safe `WorkItemRollUp` after replay; when child contributions exist, copy it with both rolled fields unavailable, and pass exactly that model to what's-next composition and persistence.
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` -- add deterministic parent-progress and separately-dispatched child-progress regressions covering persisted roll-up, tenant index/query JSON, reliable own effort/status/structure, leaf behavior, and terminal preservation.
- [x] `docs/eventstore-api-surface-constraints.md`, `docs/work-roll-up-projection.md`, `docs/whats-next-projection.md` -- record the 2026-08-27 refusal decision and remove claims that per-aggregate runtime replay reconciles parent totals.

**Acceptance Criteria:**
- Given a parent replay with child facts, when `/project` persists or returns its read models, then rolled totals are explicitly unavailable while own effort, lifecycle status, structure, watermark, and tenant remain accurate.
- Given that parent's safe model, when the child later progresses or terminates in a separate dispatch, then the parent never exposes its spawn-time total and the child's reliable local state is preserved.
- Given a leaf replay, when it is projected, then its locally complete rolled value remains available.
- Given colliding work-item ids in different tenants, when either query executes, then no cross-tenant model or total is returned.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 7: (high 1, medium 2, low 4)
- defer: 2: (high 0, medium 2, low 0)
- reject: 9: (high 0, medium 3, low 6)
- addressed_findings:
  - `[high]` `[patch]` Conservatively refuse rolled totals when a `ChildSpawned` event type is present but cannot be decoded or accepted, with a malformed-event regression.
  - `[medium]` `[patch]` Add mixed-unit adapter coverage proving stale per-unit buckets are cleared as well as the scalar total.
  - `[medium]` `[patch]` Add an initially effortless-child regression proving child contribution presence drives refusal.
  - `[low]` `[patch]` Preserve tenant and work-item identity checks in the what's-next roll-up lookup.
  - `[low]` `[patch]` Document the runtime-unavailable `null`/empty semantics on both read-model contracts.
  - `[low]` `[patch]` Replace the adapter test summary's obsolete runtime-convergence wording with refusal behavior.
  - `[low]` `[patch]` Qualify documentation and design notes so shared sanitization does not imply atomicity across separate persistence writes.

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 2: (high 0, medium 0, low 2)
- defer: 2: (high 0, medium 2, low 0)
- reject: 18: (high 0, medium 4, low 14)
- addressed_findings:
  - `[low]` `[patch]` Record the refuse-stale-roll-ups behaviour change in `CHANGELOG.md`, which had no entry for it although the change alters observable read-model and query values and the immediately preceding sweep story added its own entry.
  - `[low]` `[patch]` Rename the eight new adapter test methods to the file's and repository's dominant snake_case convention (the touched file was 3 snake / 0 Pascal before this change; the repository's integration suite is 115 snake / 22 Pascal).

## Design Notes

Use `ChildContributionCount > 0` as the conservative adapter predicate: even an initially effortless child may later acquire effort. Also refuse rolled totals when the request contains a `ChildSpawned` event type that cannot be decoded or accepted, because an incomplete local model cannot prove leaf completeness. Do not set `Degraded` or add diagnostics; this is known substrate unavailability, not invalid event data. Compute one safe model and reuse it so the roll-up document, `WhatsNextItem`, returned projection state, and tenant index apply the same sanitization policy within a dispatch; their separate persistence writes are not an atomic transaction.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -p:NuGetAudit=false -m:1 -v minimal` -- expected: focused test project builds with zero warnings/errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorkItemProjectionQueryAdapterTests` -- expected: focused adapter regressions pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.GetWorkItemQueryHandlerTests` -- expected: query compatibility and tenant isolation pass.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemRollUpProjectionTests` -- expected: pure co-delivered recursive behavior remains green.

## Auto Run Result

Status: done

Summary: Follow-up review pass over the refuse-stale-persisted-roll-ups change. The runtime projection adapter persists and exposes child-dependent rolled totals as unavailable whenever its single-aggregate replay cannot prove reconciliation, while preserving own effort, lifecycle status, structure, tenant identity, diagnostics, and freshness; leaf totals stay available and the pure recursive projection is unchanged. This pass applied two low-severity patches and recorded two pre-existing medium residuals.

Files changed:
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- applies one identity-scoped boundary-safe model to response/index composition and per-item persistence, including conservative refusal for undecodable `ChildSpawned` events.
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs` -- documents runtime-unavailable rolled-total semantics without changing the contract shape.
- `src/Hexalith.Works.Contracts/Models/WhatsNextItem.cs` -- documents compatible `null`/empty query semantics.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` -- deterministic persisted-state/query regressions for parent and child progress, malformed child facts, mixed units, effortless children, leaves, terminal parents, and tenant collisions; this pass renamed the new tests to the file's snake_case convention.
- `docs/eventstore-api-surface-constraints.md` -- records the 2026-08-27 refusal decision and corrects the runtime-convergence limitation.
- `docs/work-roll-up-projection.md` -- distinguishes pure co-available recursion from per-aggregate runtime availability.
- `docs/whats-next-projection.md` -- documents boundary sanitization and separate-write semantics.
- `CHANGELOG.md` -- adds the refuse-stale-persisted-roll-ups entry (this pass).
- `_bmad-output/implementation-artifacts/spec-refuse-stale-persisted-rollups.md` -- captures planning, review triage, verification, and completion evidence.

Review findings breakdown (this pass): 2 patches applied (low 2); 2 pre-existing residuals deferred in this spec's frontmatter (medium 2); 18 findings rejected. Notable rejections: terminal-parent "over-refusal" is what the intent's edge-case matrix explicitly mandates ("Do not infer subtree convergence from terminal status"); the inert tenant/id guard in the what's-next lookup lambda is a deliberate prior-pass defence with no consumer consequence; the missing refusal diagnostic is forbidden by the spec's Design Notes; the blind `SaveAsync` / ETag-policy findings duplicate the already-recorded deferrals; and the removed Aspire Tier-3 pointer remains documented in the same file at `docs/eventstore-api-surface-constraints.md:103` and `:199-206`. The deferred-work ledger was not edited.

Follow-up review recommendation: false. Patched findings this pass: high 0, medium 0, low 2. Score = `3 x 0 + 1 x 2 = 2`, below 5, and no high-severity patch.

Verification performed (re-run after this pass's patches):
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -p:NuGetAudit=false -m:1 -v minimal`: 0 warnings, 0 errors.
- `WorkItemProjectionQueryAdapterTests`: 12 passed, 0 failed/skipped/not-run.
- `GetWorkItemQueryHandlerTests`: 3 passed, 0 failed/skipped/not-run.
- `WorkItemRollUpProjectionTests` (Release rebuild of `Hexalith.Works.UnitTests`, 0 warnings/0 errors): 34 passed, 0 failed/skipped/not-run.
- `git diff --check`: clean.

Residual risks:
- Documents persisted before this change keep stale child-dependent totals until their aggregate is dispatched again; there is no backfill, re-projection, or read-side sanitization (deferred, medium).
- A parent whose children were attached by a parented create (`CreateWorkItem.Parent`) still publishes a rolled total omitting them, because its own stream carries no child fact (deferred, medium).
- Delayed older full-replay writes and the direct per-item `SaveAsync` / documented ETag-policy mismatch remain recorded in frontmatter `deferred`; neither is introduced by this bundle.
