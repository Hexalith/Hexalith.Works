---
title: 'Roll-up tenant-isolation gate'
type: 'refactor'
created: '2026-08-27'
status: 'done'
baseline_revision: '7b1daa06a20bf68aa4e92ffb104101c6c86a3132'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '_bmad-output/planning-artifacts/architecture.md'
  - 'docs/work-roll-up-projection.md'
warnings:
  - 'oversized'
deferred:
  - summary: >-
      The 14-arm supported-payload allowlist has no drift guard, so a new work-item event type
      added to Contracts but omitted from the switch is silently refused with no failing test.
    evidence: |-
      TryGetPayloadIdentity enumerates 14 payload types and falls through to (null, null), which
      AllowsDelivery turns into an unconditional refusal. SupportedDeliveryPayloads in the unit
      tests restates the same 14 types by hand; nothing ties the two lists together or to the
      Contracts assembly. A newly introduced event would be dropped from every roll-up read model
      with a fully green suite. Pre-existing: the removed EventMatchesDelivery switch had the same
      shape. A fitness test enumerating IEventPayload implementations in Hexalith.Works.Contracts
      and asserting each is allowlisted or explicitly excluded would close it.
    location: >-
      src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs
    severity: medium
  - summary: >-
      Exposed child order follows HashSet insertion order, so replays that permute delivery order can
      expose the same children in different order, and the convergence property cannot observe it.
    evidence: |-
      ToReadModel iterates node.ChildKeys, a HashSet<NodeKey> that is only ever added to, so iteration
      order is insertion order and therefore delivery order. CollectDiagnostics in the same file sorts
      ordinal; ChildWorkItemIds does not. Both SameRollUp and the new ExpectedLocalChildren assertion in
      WorkItemRollUpConvergencePropertyTests sort before comparing, so the permutation replay the property
      exists to exercise cannot see an order divergence. Pre-existing: ChildKeys was already an unordered
      set and the previous ToReadModel loop iterated it the same way. Sorting outputChildren by
      WorkItemId.Value ordinal, plus an unsorted assertion in the property, would close it.
    location: >-
      src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:238
    severity: medium
  - summary: >-
      ChildContributionCount counts children that passed the output filter, not children that actually
      contributed effort, so the public contract's name overstates what the number means.
    evidence: |-
      ToReadModel derives both ChildWorkItemIds and ChildContributionCount from the same outputChildren
      list, which is filtered by AllowsOutput. A tenant-local child with no effort is counted, and under a
      policy where output and contribution differ the count tracks the wrong hop --
      Contribution_boundary_includes_local_effort_and_ignores_foreign_effort_from_permissive_edge asserts
      a count of 2 while RolledRemaining proves only one child contributed. In the shipped configuration
      the two filters are identical, so this is a naming/semantics mismatch rather than a leak.
      Pre-existing: the count came from the same tenant-filtered child list before this change.
    location: >-
      src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:256
    severity: low
---

<intent-contract>

## Intent

**Problem:** Roll-up tenant isolation is enforced by redundant delivery, edge, output, contribution, diagnostic, and degradation checks, but the edge guard prevents later checks from being exercised independently. Existing tests therefore remain green when an individual isolation hop is removed, and the convergence property accepts any child id with a `child-` prefix.

**Approach:** Route the six production decisions through testable internal seams, then add projection-level tests that isolate one hop at a time by making the other defenses permissive. Strengthen the property oracle to compare the exact generated tenant-owned child sequence and count.

## Boundaries & Constraints

**Always:** Preserve the public parameterless projection API and existing fail-closed behavior; use ordinal tenant equality and exact delivery payload/header identity; exercise production call sites rather than only testing helper predicates; keep `Hexalith.Works.Projections` pure and Contracts-only; follow one C# type per file, XML documentation for internal members, xUnit v3, Shouldly, and warnings-as-errors.

**Block If:** A deterministic per-hop test cannot be constructed without changing public contracts or adding an infrastructure/runtime dependency, or the six named boundaries cannot remain independently observable through the projection read model.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md` or any deferred-work ledger; add Stryker or another mutation dependency for this bundle; expose test controls publicly; remove defense-in-depth checks; modify submodules, event contracts, persistence, runtime topology, or unrelated projections.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Delivery | Local envelope carrying foreign-tenant or wrong-item payload | Delivery is refused before node allocation | No phantom node or mutation |
| Edge | Foreign child names a local parent while downstream hops are permissive | Edge is not admitted | Parent has no foreign child |
| Output | A deliberately admitted local and foreign child edge | Only the local child id is returned and counted | Foreign id is omitted |
| Contribution | Local and deliberately admitted foreign child effort | Parent total includes only local effort | Foreign effort is ignored |
| Diagnostic | Local and deliberately admitted foreign children both have diagnostics | Parent receives only local diagnostics | Foreign metadata is ignored |
| Degradation | A deliberately admitted foreign child is degraded | Parent remains non-degraded unless a local child is degraded | Foreign state is ignored |
| Property oracle | Generated tenant-owned children plus a colliding foreign child | Actual child ids and count equal the generated local sequence exactly | Duplicate/colliding leakage fails |

</intent-contract>

## Code Map

- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:35` -- `Project` applies the delivery decision before allocation; `AddEdge` at line 98 is the graph-admission guard.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs:109` -- `TryGetPayloadIdentity` owns the supported-payload allowlist; `EventMatchesDelivery` at line 136 performs the exact delivery payload/header identity check.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:234` -- `ToReadModel`, `CalculateRolled`, `CollectDiagnostics`, and `IsDegraded` contain the output, contribution, diagnostic, and degradation tenant checks currently masked by edge refusal.
- `src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj:3` -- existing `InternalsVisibleTo` group exposes only ArchitectureTests; reuse it for UnitTests.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs:194` -- existing end-to-end cross-tenant behavior and delivery tests are regression coverage, but do not isolate downstream hops.
- `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs:35` -- relative convergence oracle and prefix-only child assertion; `BuildScenario` already owns the exact local `children` list and injects a colliding foreign child.
- `_bmad-output/planning-artifacts/architecture.md:104` -- D2/RR-4 requires tenant equality at every roll-up hop and a negative test that fails when a check is deleted.
- `docs/work-roll-up-projection.md:24` -- read-only behavioral contract: edge discovery is idempotent and every traversal hop is tenant-closed.

## Tasks & Acceptance

**Execution:**
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs` -- add one internal, immutable six-decision isolation policy with secure defaults and test-only configurability -- gives each production hop an independent seam without widening the public API.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` -- keep the public default constructor, add an internal policy constructor, and route delivery, edge/edge-diagnostic, output, contribution, diagnostic, and degradation decisions through the corresponding policy methods -- makes call-site deletion observable while preserving behavior.
- `src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj` -- grant internals access to `Hexalith.Works.UnitTests` beside the existing architecture-test grant -- confines seam control to tests.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpTenantIsolationTests.cs` -- add six focused projection-level tests; make every non-target hop permissive in each downstream case and assert both positive local behavior and negative foreign behavior -- kills a deletion or bypass at each named call site.
- `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs` -- carry expected local children in `RollUpScenario`, compare sorted sequences exactly, and assert the exact count without set deduplication -- prevents colliding or duplicate foreign ids from satisfying the property.

**Acceptance Criteria:**
- Given the secure default policy, when mismatched deliveries or cross-tenant edges with colliding work-item ids are projected, then no foreign node, child, contribution, diagnostic, or degraded state crosses into the local parent read model.
- Given a focused test with all non-target defenses permissive, when any one of the six production isolation calls is removed or bypassed, then that hop's deterministic test fails.
- Given any generated convergence scenario, when canonical and permuted/duplicated deliveries are replayed, then the parent exposes exactly the generated tenant-owned child ids and exact contribution count while the foreign tenant retains its separate colliding node.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 7: (high 1, medium 1, low 5)
- defer: 2: (high 0, medium 1, low 1)
- reject: 23: (high 0, medium 1, low 22)
- addressed_findings:
  - `[high]` `[patch]` `Delivery_boundary_uses_ordinal_work_item_identity_and_preserves_rejected_sequence_slot` could not fail. Both sequence-2 deliveries were `WorkItemAssigned`, whose apply arm ignores payload identity, and `RollUpNode.Accept` is first-write-wins -- so an accepted case variant and a refused one left byte-identical state. Mutating `SameIdentity` to `OrdinalIgnoreCase` left the suite green. The test now observes the refusal (`LatestAcceptedSourceSequence == 1`, `Status == Created`) before the genuine delivery arrives; the mutant is killed.
  - `[medium]` `[patch]` The well-formedness floor validated only the two identities it compares, not the one `Project` dereferences: a `ChildSpawned` with a null `ChildWorkItemId` -- an unguarded positional record parameter -- passed the floor and reached `NodeKey.From`, throwing during replay. Added a fail-closed switch guard and a covering malformed-delivery case; removing the guard now fails the suite. (`WorkItemCreated.Parent` needs no guard: `ParentWorkItemReference`'s constructor rejects null members.)
  - `[low]` `[patch]` `AllowsDelivery` documented a null payload as refused, but `Project` ran `ArgumentNullException.ThrowIfNull(delivery.Payload)` first, so the branch was unreachable and a corrupted stream threw instead of failing closed -- contradicting the prior pass's own fail-closed fix. Dropped the guard so the policy decides, and rewrote the stale `Project` comment.
  - `[low]` `[patch]` The floor is documented as ungoverned by the delivery flag, but every case exercising it ran with `delivery: true`. Added `Well_formedness_floor_refuses_malformed_deliveries_with_delivery_isolation_off`, which also admits an identity mismatch to prove the flag really is off; making the floor flag-governed now fails the suite.
  - `[low]` `[patch]` `AllowsEdge(string, string)` used a bare ordinal comparison, so two absent tenant keys read as the same tenant -- the opposite of `SameTenant`'s fail-closed contract in the same class. Made it fail closed on a missing key.
  - `[low]` `[patch]` `ProjectionWithOnlyIsolationAt` forwarded six adjacent bools positionally into a six-bool constructor, so reordering the constructor would silently re-point every isolation test at the wrong hop. Switched to named arguments.
  - `[low]` `[patch]` The new test file broke the assembly's naming convention (257 of 268 methods use `Snake_case_sentence`; the 11 outliers were all in this file, renamed to PascalCase by the previous pass). Renamed back, and tightened `Envelope(object)` to `Envelope(IEventPayload)`.

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 13: (high 3, medium 7, low 3)
- defer: 0
- reject: 6: (high 0, medium 0, low 6)
- addressed_findings:
  - `[high]` `[patch]` Made malformed delivery tenant/work-item identities fail closed instead of throwing during replay.
  - `[high]` `[patch]` Added projection-level acceptance coverage for every supported delivery payload arm, including assignment.
  - `[medium]` `[patch]` Added an unknown-payload delivery case that locks the allowlist's fail-closed default.
  - `[medium]` `[patch]` Proved a rejected mismatch cannot consume an existing node's sequence slot before a valid same-sequence delivery.
  - `[high]` `[patch]` Added a case-only work-item identity mismatch to lock ordinal aggregate identity matching.
  - `[medium]` `[patch]` Used distinct local and foreign child ids so the output test cannot pass with an inverted tenant filter.
  - `[medium]` `[patch]` Made the output test prove the permissive edge admitted foreign contribution before output filtering.
  - `[medium]` `[patch]` Made the contribution test expose both admitted children before asserting foreign effort exclusion.
  - `[medium]` `[patch]` Made the diagnostic test prove both source diagnostics and the contaminated edge before asserting tenant-closed propagation.
  - `[medium]` `[patch]` Made the degradation test prove the foreign source is degraded before asserting it does not taint the parent.
  - `[low]` `[patch]` Renamed focused isolation tests to PascalCase.
  - `[low]` `[patch]` Reused the property test's actual projection instance for the foreign-node assertion.
  - `[low]` `[patch]` Corrected the spec Code Map after moving delivery identity matching into the isolation policy.

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 6: (high 1, medium 0, low 5)
- defer: 1: (high 0, medium 1, low 0)
- reject: 15: (high 0, medium 4, low 11)
- addressed_findings:
  - `[high]` `[patch]` The shipped isolation defaults were unpinned: mutating `enforceOutput`, `enforceContribution`, `enforceDiagnostic`, or `enforceDegradation` to `false` in the policy constructor left the entire suite green, because with edge enforcement on no foreign child ever reaches those four hops. The change had moved the six decisions onto a defaults surface that reproduced the exact "delete a hop, nothing goes red" failure the story exists to eliminate. Added `SecureDefaultPolicyEnforcesEveryBoundary`, pinning `new WorkItemRollUpTenantIsolation()` at all six predicates, and `DefaultConstructedProjectionKeepsTheParentTenantClosed`, exercising the production call sites through the public parameterless constructor. All twelve mutants (six call-site removals plus six default flips) are now killed.
  - `[low]` `[patch]` `ToReadModel` dereferenced `_nodes[key]` for every child key before filtering by tenant, diverging from the `TryGetValue` guards used by the contribution, diagnostic, and degradation traversals in the same file. Rewrote it as a guarded loop matching those three.
  - `[low]` `[patch]` `AddEdge` allocated two regex-validating `TenantId` instances per call, including on the refusal path, where the previous code did a plain ordinal `string.Equals`. Added an `AllowsEdge(string, string)` overload that compares the already-normalized node-key values.
  - `[low]` `[patch]` `AllowsDelivery`'s XML documentation described the whole method as flag-governed, but its null and unknown-payload rejections fail closed unconditionally. Documented the always-on well-formedness gate and scoped the flag to the identity comparison.
  - `[low]` `[patch]` `EventMatchesDelivery` was a pure pass-through to `SameIdentity` with an identical signature. Collapsed it.
  - `[low]` `[patch]` `TryGetPayloadIdentity`'s `out` parameters lacked `[NotNullWhen(true)]`, forcing null-forgiving `!` operators at the call site inside a tenant-isolation class. Annotated them and removed the operators.

## Design Notes

The tests must inject contamination through the production projection and observe the public read model. Direct tests of policy methods are insufficient because they remain green if a projection call site stops invoking the policy. For downstream hops, permit edge admission deliberately and leave only the target hop restrictive. Compare sorted child sequences plus count rather than sets: the generated foreign node intentionally reuses `child-0`, so set equality would hide duplicate leakage.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx` -- expected: restore succeeds without dependency changes.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore` -- expected: zero warnings and errors.
- `dotnet test tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Release --no-build` -- expected: all unit tests pass, including all six focused isolation-hop tests.
- `dotnet test tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj -c Release --no-build` -- expected: all property tests pass with the exact child-set oracle.
- `dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Release --no-build` -- expected: projection purity and repository fitness gates pass.

## Auto Run Result

Status: done

**Implemented change.** The six roll-up tenant-isolation decisions (delivery, edge/edge-diagnostic, output, contribution, diagnostic, degradation) are routed through one internal, immutable `WorkItemRollUpTenantIsolation` policy with all-enforcing defaults and test-only per-hop configurability. Six focused projection-level tests each leave one hop restrictive and the rest permissive, so deleting or bypassing any single production call site turns that hop's test red. Two further tests pin the shipped composition itself: the default policy's six predicates, and the parameterless-constructor projection end to end. The convergence property now compares the exact generated tenant-owned child sequence and count instead of accepting any `child-` prefix.

This review pass repaired the isolation suite's own blind spots: one test could not fail at all, and the delivery well-formedness floor was both incomplete and unverified.

**Files changed.**
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs` -- the six-decision isolation policy; this pass added a fail-closed guard for `ChildSpawned.ChildWorkItemId`, made the raw-string `AllowsEdge` overload fail closed on a missing key, and corrected the well-formedness remark.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` -- public parameterless constructor preserved, internal policy constructor added, all seven isolation call sites routed through the policy; this pass removed the payload null-throw so a corrupted stream is refused rather than wedging replay.
- `src/Hexalith.Works.Projections/Hexalith.Works.Projections.csproj` -- `InternalsVisibleTo` extended to `Hexalith.Works.UnitTests`.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpTenantIsolationTests.cs` -- the per-hop, default-policy, and delivery-allowlist tests; this pass repaired the ordinal-identity test, added the delivery-isolation-off floor test and two malformed-delivery cases, switched to named policy arguments, and restored the assembly's snake_case naming.
- `tests/Hexalith.Works.UnitTests/UnknownRollUpEventPayload.cs` -- unsupported-payload test double locking the allowlist's fail-closed default.
- `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs` -- exact expected-child sequence and count oracle.

**Review findings breakdown.** 7 patches applied (1 high, 1 medium, 5 low); 2 items deferred (1 medium, 1 low); 23 rejected (1 medium, 22 low). No intent gaps and no spec repairs.

**Follow-up review recommendation:** `true`. Patched severities this pass: high 1, medium 1, low 5. A high-severity patch forces `true` on its own; the score `3 x 1 + 1 x 5 = 8` also clears the threshold of 5.

**Verification performed.**
- `dotnet restore Hexalith.Works.slnx` -- succeeded, no dependency changes.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore` -- succeeded, 0 warnings, 0 errors.
- `Hexalith.Works.UnitTests` (xUnit v3 binary) -- 521 passed, 0 failed (520 before this pass; +1 floor test).
- `Hexalith.Works.PropertyTests` -- 3 passed, 0 failed.
- `Hexalith.Works.ArchitectureTests` -- 114 passed, 0 failed (projection purity and repository fitness gates green).
- Mutation spot-checks, each applied and reverted: `SameIdentity` ordinal to `OrdinalIgnoreCase` fails only the repaired ordinal test; making the well-formedness floor flag-governed fails only the new floor test; removing the `ChildSpawned.ChildWorkItemId` guard fails only the malformed-identity test. All three survived the suite before this pass.

**Residual risks.**
- Four of the six defaults (output, contribution, diagnostic, degradation) remain unobservable end to end while edge enforcement is on, so `Secure_default_policy_enforces_every_boundary` is a direct predicate assertion rather than a read-model one. That is the intent's own chosen approach, but it means those four defaults are guarded by one test rather than by behavior.
- The permissive branches ship inside `Hexalith.Works.Projections`. They are internal and unreachable through the public API, but they live inside the tenant-isolation type itself.
- Three deferred items stand: the 14-arm allowlist has no drift guard, exposed child order follows set-insertion order and the property oracle sorts past it, and `ChildContributionCount` counts exposed rather than contributing children.
