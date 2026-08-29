---
title: 'Roll-up exposed count invariant'
type: 'bugfix'
created: '2026-08-29'
status: 'done'
baseline_revision: 'c9117748f321539ee144c7a9a3555336575bc7e7'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** `WorkItemRollUp.ExposedChildCount` is an independent positional constructor input, so callers and persisted JSON can disagree with `ChildWorkItemIds` and expose an internally inconsistent read model.

**Approach:** Remove the count constructor input and make the public getter derive from `ChildWorkItemIds`. Preserve the `exposedChildCount` Web JSON output, make incompatible incoming count values non-authoritative, and update every constructor, consumer, compatibility assertion, and public explanation.

## Boundaries & Constraints

**Always:** Treat `ChildWorkItemIds` as the sole count source; preserve the other positional parameters and record `with` behavior; continue emitting the numeric `exposedChildCount` Web JSON property; keep existing child membership, ordering, tenant filtering, roll-up, and stale-total refusal semantics unchanged; use xUnit v3 and Shouldly.

**Block If:** The repository's existing System.Text.Json Web options cannot serialize the derived getter or safely ignore an incoming `exposedChildCount` without a custom converter or a broader wire-format redesign.

**Never:** Add another count constructor, writable count property, stored count field, or compatibility alias; rename or remove `exposedChildCount`; change event contracts, projection topology, child-selection semantics, submodules, or `_bmad-output/implementation-artifacts/deferred-work.md`; edit the deferred-work ledger for any reason.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|---------------------------|----------------|
| Constructed roll-up | Two `ChildWorkItemIds`, no count argument | `ExposedChildCount` is `2` | No error expected |
| Reconciled children | A record `with` update replaces an empty child list with two merged ids | The getter immediately changes from `0` to `2` | No separate count update is possible |
| Web JSON output | A roll-up with two child ids is serialized with `JsonSerializerDefaults.Web` | JSON contains `"exposedChildCount": 2` and the existing `childWorkItemIds` shape | No compatibility alias is emitted |
| Inconsistent Web JSON input | `childWorkItemIds` contains two ids while `exposedChildCount` is `99` | Deserialization yields count `2`; reserialization emits `2` | The incoming count is ignored as non-authoritative |
| Empty child list | `ChildWorkItemIds` is empty | `ExposedChildCount` is `0` | No error expected |

</intent-contract>

## Code Map

- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs:19-52` -- remove the positional `int ExposedChildCount` and its `<param>` documentation; add a documented getter derived only from the child list, treating a missing/null deserialized list as empty, so JSON still sees a public numeric property.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:235-278` -- `ToReadModel` currently supplies `outputChildren.Count`; remove that redundant constructor argument while preserving the ordinal filtered child sequence.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:150-177` -- the persisted-child reconciliation `with` expression sets both the merged ids and count; retain only `ChildWorkItemIds = merged` so the getter follows the canonical list.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs:28-82` -- direct constructor and the established Web JSON compatibility test; assert the constructor has no independent count input and inconsistent incoming wire count normalizes to the child-list size.
- `tests/Hexalith.Works.UnitTests/WhatsNextQueueProjectionTests.cs:153-181,657-681` -- two direct roll-up fixtures must drop their explicit zero count.
- `tests/Hexalith.Works.IntegrationTests/GetWorkItemQueryHandlerTests.cs:96-122`, `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs:845-875`, `tests/Hexalith.Works.IntegrationTests/StreamReadingCascadeDescendantSourceTests.cs:328-345`, `tests/Hexalith.Works.IntegrationTests/WorkItemReadModelGenerationQueryTests.cs:277-290`, and `tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs:799-816` -- direct fixtures must adopt the shorter constructor; existing reconciliation assertions already prove merged count behavior.
- `docs/work-roll-up-projection.md:30-35` and `CHANGELOG.md:97-103` -- state that the count is derived/non-constructible and revise the earlier positional/transitional wording while preserving the wire-property contract. `docs/eventstore-api-surface-constraints.md:90-100` and `docs/whats-next-projection.md:63-85` already describe the count as the filtered sequence size and are read-only verification evidence unless implementation reveals stale wording.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- orchestrator-owned read-only ledger; do not edit it.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs` -- replace the positional count with a documented derived getter -- makes inconsistent construction impossible while preserving the public/wire property.
- [x] `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` and `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- remove redundant count arguments/assignments -- makes production construction and reconciliation depend on the canonical child list.
- [x] `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` -- update constructors and extend the Web JSON test for derived, empty, inconsistent-input, and normalized-output behavior -- locks the CLR and wire invariants.
- [x] `tests/Hexalith.Works.UnitTests/WhatsNextQueueProjectionTests.cs` and the five integration-test files in the Code Map -- update direct constructor fixtures without changing their behavioral assertions -- keeps all consumers compiling against the approved surface.
- [x] `docs/work-roll-up-projection.md` and `CHANGELOG.md` -- document the removed constructor input, derived invariant, preserved wire output, and corrected compatibility behavior -- keeps public guidance accurate.

**Acceptance Criteria:**
- Given any valid `WorkItemRollUp`, when callers inspect `ExposedChildCount`, then it equals `ChildWorkItemIds.Count` and no public constructor or setter can supply a competing value.
- Given an inconsistent Web JSON document with the preserved `exposedChildCount` property, when it is deserialized and serialized with Web defaults, then the model and normalized output use the child-list count while the property name remains unchanged.
- Given projection replay and persisted-child reconciliation, when child identities are produced or merged, then existing membership/order/tenant behavior is unchanged and count follows the resulting list without an explicit assignment.
- Given the completed bundle, when repository changes are inspected, then the deferred-work ledger and every submodule remain byte-for-byte untouched.

## Spec Change Log

## Review Triage Log

### 2026-08-29 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 1: (high 0, medium 1, low 0)
- defer: 0
- reject: 19: (high 0, medium 1, low 18)
- addressed_findings:
  - `[medium]` `[patch]` The derived getter dereferenced `ChildWorkItemIds`, so JSON that omitted or explicitly nullified the child list could materialize a null positional value and throw while reading or serializing `ExposedChildCount`. The getter now treats a missing/null list as empty, and Web JSON tests prove both input shapes ignore an incoming count and normalize output to `0`.

## Design Notes

System.Text.Json serializes public getter-only properties by default and does not bind an input value to a getter with no constructor parameter or setter. Keeping `ChildWorkItemIds` positional/init-only preserves current record construction and `with` updates; the derived getter therefore follows both projection output and dispatcher reconciliation without a second write path. A missing/null deserialized child list is treated as empty for count compatibility.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx` -- expected: restore succeeds without dependency changes.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore` -- expected: all production and test constructor consumers compile with zero warnings/errors.
- `dotnet test tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release --no-build` -- expected: all unit tests pass, including the CLR/Web JSON invariant coverage.
- `dotnet test tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release --no-build` -- expected: all available integration tests pass; any infrastructure-only skips are reported exactly.
- `git diff --check` -- expected: no whitespace errors.
- `git diff -- _bmad-output/implementation-artifacts/deferred-work.md references` -- expected: no output.

## Auto Run Result

**Implemented change.** `WorkItemRollUp.ExposedChildCount` is no longer an independent positional input or writable value. It derives from `ChildWorkItemIds`, treats missing/null deserialized lists as empty, remains emitted as `exposedChildCount`, and ignores/normalizes conflicting incoming wire values. Projection construction, persisted-child reconciliation, direct consumers, compatibility tests, and published documentation now use that invariant. The deferred-work ledger and submodules were not edited.

**Files changed.**
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs` -- removed the count constructor parameter and added the null-compatible derived getter.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` -- removed the redundant projection count argument.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- removed the redundant count assignment during persisted-child reconciliation.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` -- added CLR surface, derived-count, `with`, preserved-wire, inconsistent-input, missing/null-list, and normalized-output coverage.
- `tests/Hexalith.Works.UnitTests/WhatsNextQueueProjectionTests.cs` -- updated direct roll-up fixtures for the shorter constructor.
- `tests/Hexalith.Works.IntegrationTests/GetWorkItemQueryHandlerTests.cs` -- updated the legacy roll-up fixture constructor.
- `tests/Hexalith.Works.IntegrationTests/StreamReadingCascadeDescendantSourceTests.cs` -- updated cascade-source roll-up fixtures.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` -- updated adapter fixtures; existing reconciliation assertions continue to prove merged count behavior.
- `tests/Hexalith.Works.IntegrationTests/WorkItemReadModelGenerationQueryTests.cs` -- updated generation-query fixtures.
- `tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs` -- updated shared-rebuild fixtures.
- `docs/work-roll-up-projection.md` -- documented the derived/non-constructible count and preserved Web JSON property.
- `CHANGELOG.md` -- corrected the breaking-contract and persisted-document compatibility wording.
- `_bmad-output/implementation-artifacts/spec-rollup-exposed-count-invariant.md` -- recorded the implementation contract, review triage, and run evidence.

**Review findings breakdown.** One medium patch was applied, no items were deferred, and nineteen findings were rejected as already covered, contradicted by the complete diff/repository, dependent on arbitrary serializer settings, or outside the approved count invariant. Patched severities: high 0, medium 1, low 0; follow-up score `3 × 1 + 1 × 0 = 3`, below the threshold of 5. Follow-up review recommendation: `false`.

**Verification performed.**
- Exact root `dotnet restore`, `dotnet build`, and `dotnet test` front doors are environment-blocked because `global.json` requests SDK `10.0.301` while only `10.0.400` is installed.
- Installed-SDK fallback `dotnet restore /home/administrator/projects/hexalith/works/Hexalith.Works.slnx` from `/tmp` succeeded without dependency changes.
- Installed-SDK fallback Release solution build succeeded with 0 warnings and 0 errors.
- Installed-SDK fallback `dotnet test` reached the known Microsoft.Testing.Platform guard and failed because the legacy VSTest target is unsupported under .NET 10; repository-prescribed direct xUnit v3 binaries were used instead.
- Full direct `Hexalith.Works.UnitTests` run: 529 passed, 0 failed, 0 skipped.
- Focused direct `WorkItemRollUpProjectionTests` run: 37 passed, explicitly including both invariant/compatibility tests that cover every matrix row.
- Direct affected integration classes (`GetWorkItemQueryHandlerTests`, `StreamReadingCascadeDescendantSourceTests`, `WorkItemProjectionQueryAdapterTests`, `WorkItemReadModelGenerationQueryTests`, `WorkItemSharedProjectionRebuildHandlerTests`): 60 passed, 0 failed, 0 skipped.
- Broad direct integration attempt reached 270 tests: two reminder-recovery Aspire startup tests timed out at their five-minute cancellation boundary, one following topology startup was canceled when the stalled runner was terminated, and two tests skipped because Dapr placement/scheduler prerequisites were unavailable. These failures are outside the changed deterministic surfaces.
- `git diff --check`, deferred-ledger diff, and submodule status/diff checks passed; the ledger and all `references/` pointers/content remained unchanged.

**Residual risks.** The constructor removal is the approved breaking CLR surface, so compiled consumers must adopt the shorter signature. Full Aspire/Dapr reminder-recovery verification still requires the pinned SDK plus a working local topology with Redis, placement, and scheduler; deterministic affected integration coverage is green in this environment.
