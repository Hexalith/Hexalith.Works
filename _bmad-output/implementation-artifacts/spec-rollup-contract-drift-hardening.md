---
title: 'Roll-up contract drift hardening'
type: 'bugfix'
created: '2026-08-28'
status: 'done'
baseline_revision: '0a62ad49c2b6bc9443e813b0998e98319e15939c'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '_bmad-output/planning-artifacts/architecture.md'
  - 'docs/work-roll-up-projection.md'
  - '_bmad-output/implementation-artifacts/spec-rollup-tenant-isolation-gate.md'
warnings: []
deferred:
  - summary: >-
      ExposedChildCount remains independently constructible from ChildWorkItemIds and can represent an inconsistent read model.
    evidence: |-
      WorkItemRollUp already accepted the count as an independent positional integer before this bundle. The approved DW-26
      decision preserves that behavior while renaming it, so deriving or validating the value would be a separate contract change.
    location: >-
      src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs:27
    severity: medium
  - summary: >-
      The Contracts-derived gate binds payload admission but not roll-up effect, so a new event registered only to
      green the gate is accepted, consumes its sequence slot, and advances the watermark with no state change.
    evidence: |-
      The fitness test compares Contracts payloads with WorkItemRollUpTenantIsolation's identity registry only.
      WorkItemRollUpProjection.ApplyPayload holds a second, ungated switch. Adding any concrete non-rejection
      Contracts payload turns the gate red, and the only way to green it is a registry entry -- which converts a
      fail-closed refusal into a silent no-op acceptance that still advances LatestAcceptedSourceSequence. The
      spec's Approach scopes the allowlist to the identity registry, so binding ApplyPayload is a separate change.
    location: >-
      src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:137
    severity: medium
  - summary: >-
      WhatsNextQueueProjection keeps a structurally identical hand-maintained payload allowlist over the same
      delivery envelope, with no Contracts-derived gate, so the drift this story closed for roll-up stays open there.
    evidence: |-
      WhatsNextQueueProjection.EventMatchesDelivery enumerates the same 14 payload types with a fail-closed
      `_ => false` fallthrough and is driven in production by WorkItemProjectionDispatcher.DispatchAsync. None of
      its 37 unit tests enumerate payload types, and no architecture test ties its accepted set to Contracts. A
      15th non-rejection event would be silently dropped from the tenant what's-next index with a green suite.
      The spec's Never clause forbids modifying unrelated projections, so this is out of scope for this bundle.
    location: >-
      src/Hexalith.Works.Projections/Strategies/WhatsNextQueueProjection.cs:91
    severity: medium
---

<intent-contract>

## Intent

**Problem:** The roll-up allowlist can silently drift from Contracts, child output order depends on delivery order, and the convergence oracle sorts away that defect. The public `ChildContributionCount` name also describes contribution semantics while the value actually counts exposed children.

**Approach:** Make the production payload identity registry the auditable allowlist, add a Contracts-derived fitness gate with rejection events explicitly excluded by category, sort exposed children ordinally, and compare canonical output without normalization. Deliberately rename the positional read-model member and serialized property to `ExposedChildCount` everywhere while preserving its value.

## Boundaries & Constraints

**Always:** Preserve the public parameterless projection API, fail-closed delivery behavior, tenant-isolation policy, positional record order, and existing count behavior; classify every concrete Contracts `IEventPayload` as supported or as an `IRejectionEvent`; use ordinal child-id ordering; keep Projections pure and Contracts-only; use xUnit v3 and Shouldly.

**Block If:** A supported non-rejection payload cannot supply the tenant/work-item identity required by delivery validation, or the rename would require retaining an ambiguous compatibility member instead of the explicitly approved API change.

**Never:** Edit the deferred-work ledger; modify submodules, event payload contracts, persistence topology, unrelated projections, or roll-up contribution semantics; add reflection to production projection execution; preserve `ChildContributionCount` as a serialized alias.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Contract payload added | New concrete non-rejection `IEventPayload` in Works Contracts | Fitness test fails until the production identity registry supports it | Unknown runtime payload remains refused fail-closed |
| Rejection payload present | Concrete Contracts type implementing `IRejectionEvent` | Fitness test recognizes the intentional exclusion | Rejection is not admitted into roll-up state |
| Permuted replay | Same child facts delivered in different orders and with duplicates | `ChildWorkItemIds` is identical ordinal output in every replay | Duplicate facts remain idempotent |
| Public count serialization | Roll-up read model with exposed children | JSON contains `exposedChildCount` with the unchanged value and omits `childContributionCount` | Old property is deliberately absent |

</intent-contract>

## Code Map

- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs:58-157` -- `AllowsDelivery` consumes the private 14-arm identity switch; replace that duplicated shape with one production registry whose keys expose actual supported coverage to tests.
- `tests/Hexalith.Works.ArchitectureTests/` -- architecture project already references Contracts and Projections and has internals access; add the Contracts-derived payload coverage fitness test here.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs:235-259` -- `ToReadModel` currently iterates `HashSet<NodeKey>` insertion order and derives both child ids and the count from the filtered `outputChildren` list.
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs:19-36` -- public positional member and default JSON property source for the deliberate `ExposedChildCount` rename.
- `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs:39-121` -- both the scenario oracle and `SameRollUp` currently sort child ids, masking replay-order divergence.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:205-212` -- runtime stale-total refusal consumes the count; semantics remain “exposed children.”
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpProjectionTests.cs` -- core read-model tests and the narrow home for a Web-JSON property-name assertion.
- `tests/Hexalith.Works.UnitTests/WorkItemRollUpTenantIsolationTests.cs` and `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` -- count consumers that must adopt the new name without changing assertions.
- `docs/work-roll-up-projection.md`, `docs/eventstore-api-surface-constraints.md`, `docs/whats-next-projection.md` -- published behavioral descriptions of the runtime refusal predicate.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- read-only; the orchestrator owns resolution recording.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs` -- replace the hand-only identity switch with one exact-type identity registry and expose its key set internally -- makes tested coverage identical to runtime acceptance.
- [x] `tests/Hexalith.Works.ArchitectureTests/WorkItemRollUpPayloadCoverageTests.cs` -- enumerate concrete Contracts `IEventPayload` types and compare non-rejections to the registry while proving rejections are the intentional excluded category -- turns future contract drift red.
- [x] `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` -- ordinally sort filtered output children before creating the read model -- makes output deterministic without changing membership or count.
- [x] `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs` and all source/test consumers -- rename `ChildContributionCount` to `ExposedChildCount`, preserving positional order and value; add a Web-JSON assertion for the new property and old-property absence -- applies DW-26 across API and serialization.
- [x] `tests/Hexalith.Works.PropertyTests/WorkItemRollUpConvergencePropertyTests.cs` -- compare actual child sequences directly to canonical and expected ordinal output, and use the renamed count -- makes permutation failures observable.
- [x] `docs/work-roll-up-projection.md`, `docs/eventstore-api-surface-constraints.md`, `docs/whats-next-projection.md` -- document the renamed exposed-child predicate and deterministic ordinal output -- keeps public semantics accurate.
- [x] Follow the workflow review stage as the independent DW-27 pass; patch every confirmed in-scope defect and record triage in this spec without changing the ledger.

**Acceptance Criteria:**
- Given all concrete Works Contracts event payload types, when the architecture fitness gate classifies them, then every non-rejection type exactly matches the runtime-supported registry and every excluded type implements `IRejectionEvent`.
- Given local children whose deliveries are permuted or duplicated, when canonical and permuted projections are compared, then their unsorted `ChildWorkItemIds` sequences are identical and ordinal by `WorkItemId.Value`.
- Given a roll-up with exposed children, when its public contract is compiled, consumed, and serialized with Web JSON defaults, then `ExposedChildCount` retains the prior numeric behavior, `exposedChildCount` is emitted, and `ChildContributionCount`/`childContributionCount` are absent from current source and JSON.
- Given the completed implementation, when the independent review runs, then confirmed in-scope defects are patched and all required verification remains green without editing the deferred-work ledger.

## Spec Change Log

## Review Triage Log

### 2026-08-28 — Review pass (follow-up)
- intent_gap: 0
- bad_spec: 0
- patch: 6: (high 0, medium 1, low 5)
- defer: 2: (high 0, medium 2, low 0)
- reject: 16: (high 1, medium 3, low 12)
- addressed_findings:
  - `[medium]` `[patch]` A breaking public contract change shipped with no `CHANGELOG.md` record, and the existing
    Unreleased entry still described the refusal predicate in the retired "accepted child contributions" terms.
    Added a `### Changed` section covering the rename, the ordinal child ordering, and the Contracts-derived gate,
    and corrected the stale predicate wording.
  - `[low]` `[patch]` The new fitness test sat at the ArchitectureTests project root while every other architecture
    test lives in `FitnessTests/` under `Hexalith.Works.ArchitectureTests.FitnessTests`. Moved and renamespaced it.
  - `[low]` `[patch]` `SupportedDeliveryPayloads` had regressed from `TheoryData<IEventPayload>` to an untyped
    `IEnumerable<object[]>`, losing xUnit v3 typed theory rows. Restored typed theory data projected from the
    shared fixture list.
  - `[low]` `[patch]` The de-normalized property oracle compared against `ExpectedLocalChildren` in generation
    order, which only coincides with ordinal order while generated ids stay single-digit. The expectation is now
    explicitly ordinal-sorted; the actual sequence stays unsorted, so order divergence is still observable.
  - `[low]` `[patch]` The composed obsolete-property-name literal in the Web JSON test had no explanation. Added a
    comment stating why the retired name is not written as a single literal.
  - `[low]` `[patch]` The `docs/eventstore-api-surface-constraints.md` edit left two ragged short lines mid-paragraph.
    Reflowed the paragraph to the file's wrap width.

Rejected with reasons worth preserving: the claim that pre-rename persisted roll-ups deserialize
`ExposedChildCount` as `0` and make `ToBoundarySafeRollUp` fail open is false — that predicate is only ever applied
to a freshly replayed in-memory model (`WorkItemProjectionDispatcher.cs:123`), never to a document read back from
the store, and the only store readers ignore the count. The ordinal child comparator omits `TenantId`, but ties can
only occur between children whose emitted `WorkItemId` values are identical, so the public sequence and count are
the same under either tie order. The `s_` static-field prefix used elsewhere in `src` was not restored because
`.editorconfig` mandates `_camelCase` for every private field, and the tracked configuration is authoritative.


### 2026-08-28 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 6: (high 0, medium 3, low 3)
- defer: 1: (high 0, medium 1, low 0)
- reject: 12: (high 1, medium 4, low 7)
- addressed_findings:
  - `[medium]` `[patch]` The Contracts fitness gate covered registry keys but not their runtime identity extractors. Centralized representative fixtures now match the registry's exact type set, and the existing theory drives every fixture through matching delivery acceptance.
  - `[medium]` `[patch]` Lowercase generated ids could not distinguish ordinal from case-insensitive ordering, and one focused test still normalized output. Added opposite-order mixed-case replay coverage and made the permissive-edge assertion compare the public sequence directly.
  - `[medium]` `[patch]` Rejection exclusion was classification-only. A real Works `IRejectionEvent` now proves fail-closed refusal, no node allocation, and no sequence-slot consumption before a valid same-sequence delivery.
  - `[low]` `[patch]` The new JSON test proved only the emitted property name. It now round-trips the new Web JSON shape and asserts the renamed count and child identities survive deserialization.
  - `[low]` `[patch]` New private static fixture and registry fields did not consistently follow the repository `_camelCase` convention. Both now do.
  - `[low]` `[patch]` The implementation agent added a redundant `baseline_commit` field beside the workflow-owned `baseline_revision`; the duplicate metadata was removed.

### 2026-08-28 — Review pass (follow-up 2)

- intent_gap: 0
- bad_spec: 0
- patch: 4: (high 0, medium 0, low 4)
- defer: 0
- reject: 21: (high 0, medium 5, low 16)
- addressed_findings:
  - `[low]` `[patch]` The new exposed-child comparator ordered by `WorkItemId.Value` alone, which is only a partial order over `outputChildren`: an isolation-ablated output policy can expose two tenants' children whose ids collide, and `List<T>.Sort` is unstable. Tenant now breaks the tie, so the comparison is a total order. (The tie is not observable in today's output, because `ChildWorkItemIds` projects each node to its id alone; this closes the latent partial order rather than a live defect.)
  - `[low]` `[patch]` Each of the 14 registry entries wrote its key and its payload cast independently, so a key that disagreed with its cast would compile. Entries are now built by a generic `For<TPayload>` helper that derives both from one type argument, following the existing `ConsumedEventDescriptor.For<T>` shape in `WorksDomainEventProcessor`.
  - `[low]` `[patch]` The new ordinal-ordering guarantee was documented in `docs/` but not on the contract consumers read. The `ChildWorkItemIds` XML doc on `WorkItemRollUp` now states it.
  - `[low]` `[patch]` The `ExposedChildCount` clarification was spliced into the middle of the refusal-policy paragraph in `docs/whats-next-projection.md`, between the `ChildSpawned` refusal sentence and the sanitized-model sentence. It is now its own paragraph after that argument closes.

Rejected as contradicted by tracked repository configuration: a finding claimed `_payloadIdentityRegistry` and
`_supportedPayloadFixtures` break an `s_` prefix convention observed on 17 other `private static readonly` fields
in `src`. `.editorconfig` line 30 scopes `private_field` to all private fields with no `required_modifiers` and
requires `_camelCase`, so the current names are correct and the 17 `s_` fields are the pre-existing deviation.
The preceding "Review pass" entry already made and recorded this same decision; the rename was applied and
reverted within this pass.

## Design Notes

The production identity registry, not a second test list, is the source of supported types. The fitness test derives the universe from `typeof(WorkItemCreated).Assembly`, subtracts the explicit contract category `IRejectionEvent`, and compares the remaining exact types with the registry keys. Runtime lookup stays exact-type and fail-closed. Sorting occurs only after output tenant filtering, so no foreign child can influence exposed membership.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx` -- expected: restore succeeds without dependency changes.
- `dotnet build Hexalith.Works.slnx -c Release --no-restore` -- expected: zero warnings and errors.
- Run the built xUnit v3 assemblies for `Hexalith.Works.UnitTests`, `Hexalith.Works.PropertyTests`, `Hexalith.Works.ArchitectureTests`, and `Hexalith.Works.IntegrationTests` -- expected: all tests pass, including persisted read-model end-state and serialization assertions.
- `rg -n -i 'ChildContributionCount|childContributionCount' src tests docs` -- expected: no matches.

## Auto Run Result

Status: done

**Implemented change.** Roll-up delivery support comes from one exact-type runtime identity registry guarded
against Contracts drift by an architecture fitness gate that admits only `IRejectionEvent` as an intentional
exclusion. Exposed child ids are tenant-filtered and then sorted by ordinal `WorkItemId.Value` (with tenant as
the tie-break that keeps the comparison a total order), and the convergence property compares the public
sequence without normalizing the actual output. The public positional member and Web JSON property are
deliberately renamed to `ExposedChildCount` / `exposedChildCount` with unchanged value semantics and no
compatibility alias. This follow-up review pass applied four further patches; the deferred-work ledger was not
edited.

**Files changed (this pass).**
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs` -- tenant tie-break added to the
  exposed-child comparator, with a comment stating why the tie can arise.
- `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpTenantIsolation.cs` -- the 14 registry entries are
  now produced by a generic `For<TPayload>` helper, so each entry's key and cast come from one type argument.
- `src/Hexalith.Works.Contracts/Models/WorkItemRollUp.cs` -- `ChildWorkItemIds` XML doc now states the ordinal
  ordering guarantee.
- `docs/whats-next-projection.md` -- the `ExposedChildCount` clarification moved out of the middle of the
  refusal-policy paragraph into its own paragraph.

Files changed by earlier passes (unchanged here): the payload identity registry contents, the positional rename
in `WorkItemRollUp.cs`, the renamed consumer in `WorkItemProjectionDispatcher.cs`, the fitness gate in
`tests/Hexalith.Works.ArchitectureTests/FitnessTests/WorkItemRollUpPayloadCoverageTests.cs`, the renamed and
added assertions across `Hexalith.Works.UnitTests`, `Hexalith.Works.PropertyTests`, and
`Hexalith.Works.IntegrationTests`, `CHANGELOG.md`, `docs/work-roll-up-projection.md`, and
`docs/eventstore-api-surface-constraints.md`.

**Review findings breakdown.** 4 patches applied (0 high, 0 medium, 4 low); 0 items deferred; 21 findings
rejected (0 high, 5 medium, 16 low). No intent gaps or bad-spec loopbacks. One patch was applied and then
reverted within this pass after `.editorconfig` proved the finding wrong (see the triage-log note).

The five medium rejections were all already-recorded or pre-existing, not new: the gate binds payload admission
rather than roll-up effect (recorded `deferred`, DW-53); the sibling `WhatsNextQueueProjection` allowlist is
still ungated (recorded `deferred`, DW-54); `ExposedChildCount` is independently constructible and legacy
documents deserialize it as `0` (recorded `deferred`, DW-26/DW-52); the `/project` boundary refusal keys on the
output-filtered count while its concern is contribution (pre-existing, and identical under the production
default policy where both filters are tenant equality); and the count duplicates `ChildWorkItemIds.Count`
(recorded `deferred`). None is caused by this change, and the intent explicitly preserves the count's value.

**Follow-up review recommendation:** `false`. Patched severities: high 0, medium 0, low 4;
score `3 x 0 + 1 x 4 = 4`, below the threshold of 5.

**Verification performed.**
- `dotnet build Hexalith.Works.slnx -c Release --no-restore` -- succeeded with 0 warnings and 0 errors.
- `Hexalith.Works.UnitTests` built xUnit v3 assembly -- 526 passed, 0 failed, 0 skipped.
- `Hexalith.Works.PropertyTests` -- 3 passed, including the 100-case permuted/duplicated delivery property.
- `Hexalith.Works.ArchitectureTests` -- 205 passed, including the Contracts-derived gate.
- `Hexalith.Works.IntegrationTests` -- 184 total, 0 failed; 4 Aspire/Dapr smoke tests skipped because Redis,
  placement, and scheduler are unavailable in this sandbox.
- `rg -n -i 'ChildContributionCount|childContributionCount' src tests docs` -- no matches. The retired name now
  appears only in `CHANGELOG.md`, where documenting the rename requires it, outside the scanned scope.
- `git diff --check` -- clean.

**Residual risks.** Unchanged from the implementation pass. The rename is the approved deliberate breaking
CLR/JSON contract change with no compatibility alias; roll-up documents persisted before it deserialize
`ExposedChildCount` as `0` until the item is next re-projected. That is harmless today -- the stale-total
refusal predicate reads only freshly replayed models, and the query-side readers ignore the count -- but it is a
real transitional property of the stored documents and is stated in the changelog. `ExposedChildCount` remains
independently constructible from `ChildWorkItemIds`, the fitness gate binds payload admission rather than
roll-up effect, and the sibling what's-next allowlist is still ungated; all three are recorded in `deferred`.
Four infrastructure smoke tests still require Redis and Dapr placement/scheduler.
