---
title: 'Implement envelope-canonical sequencing'
type: 'refactor'
created: '2026-08-27'
status: 'done'
baseline_revision: '3bb4fdced6fd857ffb62e5a1ab53ee794446ace2'
review_loop_iteration: 0
followup_review_recommended: true
context: []
warnings: []
deferred:
  - summary: >-
      Rejection payloads are now durable persisted bytes but have no entry in the frozen golden-payload
      corpus; their shape freeze lives only in an in-test signature table.
    evidence: |-
      tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/ holds only the 14 success events.
      RejectionShapeSignatures in EnvelopeCanonicalSequencingTests is a second, uncross-referenced
      freeze surface for the 9 v1 rejections, so the corpus rule (RR-6/NFR-12) does not cover them.
    location: >-
      tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden
    severity: medium
  - summary: >-
      Snapshot-backed rehydration after a persisted rejection is unproven.
    evidence: |-
      EventStreamReader reads the tail from snapshot.SequenceNumber + 1, and returns the snapshot alone
      when it already sits at the current sequence. A snapshot taken after a rejection envelope therefore
      folds the no-op away, and no test drives that path. The spec's I/O matrix covers only full replay.
    location: >-
      references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventStreamReader.cs:68-88
    severity: medium
  - summary: >-
      Several older documentation paragraphs still say the v1 catalog "stays 36" while the fitness-asserted
      count is 37.
    evidence: |-
      docs/lifecycle-transition-matrix.md:198, docs/whats-next-projection.md:120 and
      docs/boundary-decision-record.md:109/122/134/151 say 36; ScaffoldGovernanceTests asserts
      polymorphicCatalogCount.ShouldBe(37) and docs/eventstore-api-surface-constraints.md:112 says 37.
      Pre-existing staleness, surfaced while reconciling sequencing terminology in the same files.
    location: >-
      docs/lifecycle-transition-matrix.md:198
    severity: medium
  - summary: >-
      Mid-stream and repeated-rejection envelope/payload divergence is unproven at the persistence layer.
    evidence: |-
      EnvelopeCanonicalSequencingTests covers only pre-create rejections (the spec's I/O matrix rows).
      create(env 1) -> rejection(env 2) -> assign(env 3, payload ordinal 2), and two rejections before a
      create, are the cases where an off-by-one between the two counters would first show up.
    location: >-
      tests/Hexalith.Works.IntegrationTests/EnvelopeCanonicalSequencingTests.cs
    severity: medium
  - summary: >-
      The golden-payload corpus is camelCase while the bytes EventStore actually persists are PascalCase,
      yet both are documented as "the EventStore-persisted form".
    evidence: |-
      EventPersister.cs:71 serializes with JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType())
      -- no options, so PascalCase. SchemaEvolutionGoldenCorpusTests and WorkItemProjectionDispatcher's
      <remarks> both call the JsonSerializerDefaults.Web (camelCase) samples the persisted form; the 14
      Golden/*.json files start "aggregateId". Decoding survives only because Web options are
      case-insensitive, so a naming-policy change upstream would not turn the corpus red. Surfaced by the
      first byte-level persisted-form assertion, which this change added.
    location: >-
      tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs:14-16
    severity: medium
  - summary: >-
      No executable test proves that a rejection DomainResult routed through the EventStore command
      pipeline reaches persistence; only source-text characterization covers it.
    evidence: |-
      EnvelopeCanonicalSequencingTests calls EventPersister.PersistEventsAsync itself, presupposing the
      routing decision that AggregateActor.ProcessCommandCoreAsync actually makes. No Works test
      instantiates AggregateActor, and the three Aspire lanes submit only accepted commands. The always-on
      guard is now mutation-validated across the whole command path, but it is still a string match over a
      pinned submodule, not execution.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs
    severity: medium
  - summary: >-
      The claim tests point at a Story 4.5 Aspire lane for live ETag conflict/retry coverage that does not
      exist.
    evidence: |-
      WorkItemClaimConcurrencyTests' class XML doc says the live ETag-backed save / conflict-retry /
      retry-exhaustion path "is exercised under the Aspire runtime in Story 4.5". WorksCommandPipelineSmokeTests
      (the Story 4.5 lane) issues no ClaimWorkItem at all; the only runtime claims are single sequential
      submissions in WorksReminderRecoveryPipelineSmokeTests:185 and WorksCascadeRecoveryPipelineSmokeTests:165,194.
      Nothing anywhere issues two competing claims. Pre-existing pointer, re-asserted by this change's rewording.
    location: >-
      tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs:33-35
    severity: medium
  - summary: >-
      Three ScaffoldGovernanceTests fitness method names still end "AndCatalogStays36" while the assertion
      in the same methods is polymorphicCatalogCount.ShouldBe(37).
    evidence: |-
      ScaffoldGovernanceTests.cs:387, :455 and :524 declare ...AndCatalogStays36; the comment directly above
      the third says the wire surface "stays frozen at 37". Renaming is not free: roughly ten story-file and
      test-summary references quote those method names verbatim, so the rename and the reference sweep must
      land together. Distinct surface from the documentation-paragraph instance already tracked.
    location: >-
      tests/Hexalith.Works.ArchitectureTests/FitnessTests/ScaffoldGovernanceTests.cs:387
    severity: low
---

<intent-contract>

## Intent

**Problem:** Works documentation incorrectly says rejection events are not appended and conflates payload `Sequence` with the persisted stream position, although EventStore persists and replays rejections in gapless envelope order. This leaves rejection-then-create behavior undocumented and unguarded.

**Approach:** Define EventStore envelope `SequenceNumber` as the canonical persisted stream position and Works payload `Sequence` as the state-changing-event ordinal. Preserve rejection no-op replay and frozen v1 payloads, and prove the intentional divergence through real EventStore persistence/read/replay components.

## Boundaries & Constraints

**Always:** Keep all rejection constructors and serialized shapes unchanged; keep rejection `Apply` overloads as no-ops; use the pinned EventStore `EventPersister`, `EventStreamReader`, and aggregate replay path as the executable contract; distinguish `WorkItemRejected` state change from `IRejectionEvent` command refusal; retain envelope ordering for projections and replay.

**Block If:** The pinned EventStore no longer persists `IRejectionEvent`, no longer orders replay by envelope `SequenceNumber`, or the scenario cannot be proven without changing the EventStore dependency.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md`; add `AggregateId` or `Sequence` to rejection payloads; mutate state when applying a rejection; modify golden v1 JSON; edit files under `references/Hexalith.EventStore`; equate envelope position with Works payload ordinal after a rejection.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Missing obligation then valid create | Rejection persisted at envelope position 1, then valid create for the same aggregate | Create payload ordinal is 1, persisted envelope position is 2, replay ends `Created` with state ordinal 1 and last applied envelope position 2 | Rejection remains a no-op and is returned as rejected |
| Cross-tenant parent then valid create | Cross-tenant rejection persisted first, then valid parentless create | Same canonical envelope/payload divergence and replay result as above | Tenant rejection shape and parent evidence remain byte-compatible |
| Rejection-only replay | One persisted rejection envelope | Replay succeeds at envelope position 1 while state remains `Unknown` with ordinal 0 | No synthetic identity or state change |

</intent-contract>

## Code Map

- `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs` -- read-only evidence: every non-no-op `DomainResult`, including rejection, reaches persistence and publication.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventPersister.cs` -- read-only canonical envelope assignment from persisted metadata; assigns `SequenceNumber` independently of payload fields.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Aggregates/AggregateReplayer.cs` -- read-only replay sorts and gap-validates envelope positions before invoking all `Apply` overloads.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs` -- rejection overloads are no-ops; `Sequence` records the last state-changing Works payload ordinal.
- `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs` -- `NextSequence` derives the next state-changing ordinal; an `Unknown` rejection-only state therefore produces create ordinal 1.
- `src/Hexalith.Works.Contracts/Events/Rejections/*.cs` -- four maintained XML comments falsely claim rejection events are not appended; record parameters are frozen.
- `_bmad-output/planning-artifacts/architecture.md` -- canonical architecture contains conflicting payload/envelope and rejection-persistence statements.
- `docs/eventstore-api-surface-constraints.md` -- authoritative current-dependency decision record for persisted envelope sequencing.
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` and `WorkItemRawActAdditivityTests.cs` -- existing frozen catalog and envelope-field absence guards; reuse unchanged.
- `tests/Hexalith.Works.IntegrationTests/EnvelopeCanonicalSequencingTests.cs` -- new deterministic persistence/read/replay proof using EventStore testing state storage.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs` -- always-on guard that the pinned EventStore still persists rejection results and assigns metadata-derived envelope positions.

## Tasks & Acceptance

**Execution:**
- `_bmad-output/planning-artifacts/architecture.md`, `docs/eventstore-api-surface-constraints.md`, `docs/work-roll-up-projection.md`, `docs/whats-next-projection.md` -- reconcile terminology and identify envelope `SequenceNumber` as canonical for persisted ordering/replay/projection delivery.
- `src/Hexalith.Works.Contracts/State/WorkItemState.cs`, `src/Hexalith.Works.Server/Aggregates/WorkItemAggregate.cs`, `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs`, `WorkItemProgressRejected.cs`, `WorkItemReEstimateRejected.cs`, and `WorkItemInitialEffortRejected.cs` -- describe payload `Sequence` as a state-changing ordinal while preserving code and record shapes.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj`, `EnvelopeCanonicalSequencingTests.cs` -- reference EventStore test support and add a two-case committed persistence/read/replay theory with rejection-only timeline assertions.
- `tests/Hexalith.Works.IntegrationTests/WorkItemLifecycleContractFlowTests.cs` -- retain payload-field assertions while correcting the false non-persistence explanation.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs` -- pin the upstream persistence and envelope sequencing seams used by Works.

**Acceptance Criteria:**
- Given either ledger-named pre-create rejection, when it is committed and followed by a valid create, then persisted envelope positions are `[1, 2]` while `WorkItemCreated.Sequence` is `1`.
- Given the committed two-event stream, when replay uses EventStore envelope order, then rejection application leaves `Unknown`/ordinal `0`, final state is `Created`/ordinal `1`, and last applied envelope position is `2`.
- Given any v1 rejection payload, when serialized after the change, then its constructor fields are unchanged and no payload `AggregateId`, `Sequence`, or EventStore envelope metadata appears.
- Given current architecture and dependency documentation, when sequencing guidance is read, then no maintained statement says rejections are unpersisted or treats payload ordinal as the canonical full-stream position.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 13: (high 0, medium 11, low 2)
- defer: 0
- reject: 8: (high 0, medium 3, low 5)
- addressed_findings:
  - `[medium]` `[patch]` Canonical architecture still contained four claims that every domain/raw-act event carries payload `(AggregateId, Sequence)`; reconciled every maintained occurrence with the rejection exception and envelope canonical position.
  - `[medium]` `[patch]` What's-next documentation implied filtered rejection positions advanced its freshness watermark; documented latest-accepted state-changing envelope semantics instead.
  - `[medium]` `[patch]` Roll-up documentation implied rejection deliveries were retained in its event map; documented allowlist filtering and intentional watermark gaps.
  - `[medium]` `[patch]` Roll-up diagnostic documentation mislabeled payload ordinals as envelope positions; corrected the documentation and contract XML without changing behavior.
  - `[low]` `[patch]` `WorkItemRollUpEvent.Sequence` was ambiguous; pinned it as the EventStore envelope coordinate in the contract XML.
  - `[medium]` `[patch]` Lifecycle, boundary, unit, property, command, and architecture-test prose equated `WorkItemState.Sequence` with EventStore expected version; separated same-observed-state payload candidates from independent ETag save concurrency.
  - `[medium]` `[patch]` Projection coverage never diverged envelope and payload sequences; added a rejection/create/assign delivery case proving accepted freshness uses envelope positions while filtered rejections do not advance it.
  - `[medium]` `[patch]` Only the two ledger-named rejection `Apply` paths were exercised; added no-op Apply and replay coverage for all nine frozen v1 rejection types against unknown and populated state.
  - `[medium]` `[patch]` Rejection-only replay asserted only status and ordinal; now it proves the complete serialized state and aggregate identity remain unchanged.
  - `[medium]` `[patch]` The frozen rejection guard only blacklisted envelope field names; added exact serialized shape signatures for every v1 rejection payload.
  - `[medium]` `[patch]` The cross-tenant rejection evidence was checked only before persistence; now the persisted bytes are compared and deserialized back to the exact rejection value.
  - `[medium]` `[patch]` The later create manually applied a rejection to a fresh state; it now runs through `WorkItemEventStoreAggregate.ProcessAsync` with EventStore `DomainServiceCurrentState` built from the committed rejection stream.
  - `[low]` `[patch]` The EventStore source characterization used a missing no-op marker as an `IndexOf` start before asserting it existed; reordered the guard to fail diagnostically on dependency drift.

### 2026-08-27 — Review pass

- intent_gap: 0
- bad_spec: 0
- patch: 8: (high 0, medium 6, low 2)
- defer: 4: (high 0, medium 4, low 0)
- reject: 9: (high 0, medium 4, low 5)
- addressed_findings:
  - `[medium]` `[patch]` `docs/eventstore-api-surface-constraints.md` claimed `EventStreamReader` always reads envelope positions "from 1 through the metadata watermark"; corrected to the snapshot-aware tail read from `snapshot.SequenceNumber + 1`.
  - `[medium]` `[patch]` The new `LatestAcceptedSourceSequence` XML on `WorkItemRollUp` and `WorkItemView` claimed the value is always an EventStore envelope position, but `WorkItemRollUpProjection` floors spawn-derived children at a synthetic `1`; qualified both contracts.
  - `[medium]` `[patch]` `_bmad-output/implementation-artifacts/4-2-assign-reassign-and-hand-off-work.md` still stated a rejection is "returned to caller, not appended", leaving AC #4 unmet; corrected to the persisted-envelope wording.
  - `[medium]` `[patch]` `CHANGELOG.md` retained the retired "EventStore expected-version (ETag) concurrency" and "per-child-sequence" phrasings that the rest of this change swept; re-termed both to the ETag-backed save and the envelope-position key.
  - `[medium]` `[patch]` The pre-persist rejection guard asserted the absence of one exact spelling (`if (domainResult.IsRejection)`) and passed for any other short-circuit form; it now rejects the `IsRejection` token anywhere between the no-op guard and `PersistEventsAsync`.
  - `[medium]` `[patch]` The roll-up read model gained the strongest new watermark claims but only the what's-next index was asserted under envelope/payload divergence; the adapter test now also asserts `WorkItemRollUp.LatestAcceptedSourceSequence`.
  - `[low]` `[patch]` `RejectionShapeSignatures[...]` threw `KeyNotFoundException` for an unregistered rejection type; both call sites now assert the key first with a diagnostic naming the type.
  - `[low]` `[patch]` The Story 4.3 test renames left dangling references to `Two_claims_at_the_same_expected_version_collide…` in `tests/test-summary.md` and `4-3-claim-queued-work-with-single-claim-wins.md`; both now name the current tests.

### 2026-08-27 — Review pass

- intent_gap: 0
- bad_spec: 0
- patch: 11: (high 0, medium 5, low 6)
- defer: 4: (high 0, medium 3, low 1)
- reject: 13: (high 0, medium 5, low 8)
- addressed_findings:
  - `[medium]` `[patch]` The `CHANGELOG.md` single-claim-wins rewrite dropped the retry/re-handle step, so the loser's rejection read as an output of the save itself; restored the retry-and-re-handle wording that `architecture.md`:96/253, RR-3, and `docs/boundary-decision-record.md`:116 all carry.
  - `[medium]` `[patch]` `CHANGELOG.md` re-worded two existing bullets but recorded nothing for the sequencing contract this change establishes; added an Unreleased entry for the envelope/payload two-counter rule.
  - `[medium]` `[patch]` The new "Canonical Stream Sequencing" section pinned upstream `EventPersister`/`EventStreamReader`/`AggregateReplayer` behavior without naming the verified revision, unlike every other verified claim in that file (`:140`, `:151`); recorded EventStore commit `b43e963403efa848eda9621b5e3e7e446c7faa2d` and named the drift guard.
  - `[medium]` `[patch]` `tests/test-summary.md` still described the two claim candidates as colliding on "the expected-version collision — only one append can land", inside the exact bullet the previous pass edited; re-termed to equal state-changing payload ordinals resolved by the ETag-backed atomic save.
  - `[medium]` `[patch]` The rejection short-circuit guard scanned only from the no-op guard to the persist call, so a short-circuit injected anywhere earlier in `ProcessCommandCoreAsync` passed; widened it to a conditional-form scan across the whole command path, mutation-validated (injected branch caught by the new window, missed by the old one) with the narrow token check retained.
  - `[low]` `[patch]` Only one assertion in `P1_EventStorePersistsRejectionsAndUsesEnvelopeCanonicalSequencing` carried a diagnostic message despite the previous pass's claim that it fails diagnostically; every positional and `ShouldContain` assertion now names the drift it detects.
  - `[low]` `[patch]` `EnvelopeCanonicalSequencingTests.Replay` reversed the envelope list with no stated reason, reading as a bug; documented it as the deliberate proof that ordering comes from `AggregateReplayer`'s sort, and noted the single-envelope case makes it a no-op.
  - `[low]` `[patch]` `RejectionShapeSignatures` was only checked in one direction, so a signature could outlive a retired or renamed rejection type; the freeze test now asserts the table's keys match the frozen catalog exactly.
  - `[low]` `[patch]` The `CHANGELOG.md` roll-up bullet re-wrap left a ragged line ending in a dangling "and"; re-flowed the paragraph.
  - `[low]` `[patch]` `architecture.md`'s concurrency paragraph gained a line materially longer than the surrounding wrap; re-flowed it.
  - `[low]` `[patch]` The spec's `## Verification` listed only the Integration and Architecture suites although the change renames tests in `Hexalith.Works.UnitTests` and `Hexalith.Works.PropertyTests`; added both commands.

## Design Notes

Two counters intentionally coexist: envelope `SequenceNumber` is gapless across every persisted success or rejection event; Works payload/state `Sequence` advances only for state-changing events. A rejection at envelope 1 followed by create at envelope 2 therefore correctly carries create payload ordinal 1.

## Verification

**Commands:**
- `dotnet build Hexalith.Works.slnx --configuration Release -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: solution builds with warnings treated as errors.
- `dotnet test tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: persistence/replay and frozen-contract tests pass.
- `dotnet test tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: dependency characterization and architecture gates pass.
- `dotnet test tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj --configuration Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: the renamed claim-concurrency cases and the rest of the kernel suite pass.
- `dotnet test tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj --configuration Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: the renamed claim-convergence property passes.
- `git diff --check` -- expected: no whitespace errors.

## Auto Run Result

Status: done

### Summary

Third review pass over the envelope-canonical sequencing change (baseline `3bb4fdced6fd857ffb62e5a1ab53ee794446ace2`).
No intent gap and no spec defect: the diff still implements the contract's three I/O matrix rows literally,
keeps every rejection constructor and no-op `Apply` intact, and touches no golden JSON and no file under
`references/Hexalith.EventStore`. Eleven findings were patched — four maintained statements that lagged or
overstated the reconciled model, one mutation-validated widening of the rejection-persistence drift guard,
five test/documentation diagnostics and re-flows, and the spec's own incomplete verification list. Four
further gaps were deferred, the largest being that the change's central premise (the actor routes rejections
to persistence) is still proven by source characterization rather than execution.

### Files Changed (this pass)

- `CHANGELOG.md` -- restored the loser's retry/re-handle step, added the envelope-canonical sequencing entry, re-flowed the roll-up bullet.
- `docs/eventstore-api-surface-constraints.md` -- recorded the EventStore revision the new sequencing claims were verified against, and the guard that detects drift from it.
- `_bmad-output/planning-artifacts/architecture.md` -- re-flowed the over-long concurrency line.
- `_bmad-output/implementation-artifacts/tests/test-summary.md` -- re-termed the claim-collision sentence left contradictory by the previous pass's rename.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/EventStoreApiSurfaceCharacterizationTests.cs` -- widened the rejection short-circuit guard to the whole command path and gave every assertion a drift-naming message.
- `tests/Hexalith.Works.IntegrationTests/EnvelopeCanonicalSequencingTests.cs` -- documented the deliberate replay-order reversal and froze the shape-signature table in both directions.
- `_bmad-output/implementation-artifacts/spec-envelope-canonical-sequencing.md` -- added the two missing verification commands, this triage entry, the four new deferrals, and completion.

### Review Findings

- Patches applied: 11 (high 0, medium 5, low 6).
- Items deferred: 4 (high 0, medium 3, low 1) -- golden corpus casing vs. the actually-persisted bytes, no executable proof of rejection routing through `AggregateActor`, an unbacked Story 4.5 Aspire claim-conflict pointer, and the three `…CatalogStays36` fitness method names.
- Items rejected: 13 (high 0, medium 5, low 8) -- the `deferred-work.md` hunk and every critique of ledger entries (sweep-orchestrator bookkeeping, outside this run's authority); re-terming `epics.md` (the intent scopes reconciliation to architecture and dependency documentation); the dated `sprint-change-proposal-2026-07-21.md` line (a historical change record, not maintained guidance); a proposed `decoded == 0` early return in `WorkItemProjectionDispatcher` (the `/project` contract is a full replay and rejections decode successfully, so the scenario cannot arise and the guard would break correct index removal for a rejection-only stream); changing the `Math.Max(…, 1)` spawn-derived watermark floor (pre-existing read-model shape, already qualified in XML); further cosmetic test renames and the PascalCase/snake_case split (renames create fresh dangling references); the omitted `NuGetAudit=false` on the new `ProjectReference` (matches the `Hexalith.Tenants.IntegrationTests` house pattern; the solution builds with 0 warnings); the "three serializer configurations" concern (the byte assertion deliberately mirrors `EventPersister`'s options-free path, verified at `EventPersister.cs:71`); `ShapeOf`'s value-dependence for null/array members (no v1 rejection has either); sweeping rejection `Apply` across all statuses (the overloads are `ArgumentNullException.ThrowIfNull` only); and the spec-process findings (`review_loop_iteration: 0` is the workflow's prescribed reset for a follow-up review, the empty Spec Change Log reflects that no `bad_spec` loopback occurred, and the triage log itemizes addressed findings by design).
- Follow-up review recommendation: `true`; patched counts high 0, medium 5, low 6; score = `3 x 5 + 1 x 6 = 21` (>= 5).

### Verification

- `dotnet build Hexalith.Works.slnx --configuration Release -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- succeeded, 0 warnings, 0 errors.
- `tests/Hexalith.Works.ArchitectureTests` (xUnit v3 binary, Release) -- 117 total, 0 failed, 0 skipped.
- `tests/Hexalith.Works.IntegrationTests` (xUnit v3 binary, Release) -- 167 total, 0 failed, 4 skipped (pre-existing Aspire lanes needing Docker/Dapr placement/scheduler).
- `tests/Hexalith.Works.UnitTests` -- 522 total, 0 failed, 0 skipped. `tests/Hexalith.Works.PropertyTests` -- 3 total, 0 failed, 0 skipped.
- Targeted reruns: `EnvelopeCanonicalSequencingTests` 4/4 and `P1_EventStorePersistsRejectionsAndUsesEnvelopeCanonicalSequencing` 1/1, both passing with no skips.
- Mutation check on the widened drift guard: injecting `if (domainResult.IsRejection) { return … }` immediately before the no-op guard in an in-memory copy of `AggregateActor.cs` is caught by the new whole-command-path window and missed by the old no-op-guard window; the unmutated source trips neither.
- `git diff --check` -- clean.
- Protected-path audit: no changes under `references/Hexalith.EventStore`, no changes to `SchemaEvolution/Golden`, no rejection record shapes altered, and `deferred-work.md` left exactly as the sweep orchestrator wrote it.

### Residual Risks

- Rejection routing is still guarded by source characterization over the pinned submodule rather than by executing `AggregateActor`. The guard is now spelling-independent and covers the whole command path, and it names the verified revision, but it remains a source-text check (deferred above).
- The four Aspire runtime lanes stay environment-skipped in this sandbox, so no live claim-conflict or live rejection-persistence evidence exists here.
- Eight deferred items are now open. The golden-corpus gaps (rejections absent, and the casing mismatch between the corpus and the actually-persisted bytes) and the unproven snapshot-after-rejection replay path are the ones most likely to hide a real regression.
