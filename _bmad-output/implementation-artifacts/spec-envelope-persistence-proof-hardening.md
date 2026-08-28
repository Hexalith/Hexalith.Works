---
title: 'Harden envelope persistence proof'
type: 'refactor'
created: '2026-08-28'
status: 'done'
baseline_revision: 'af870e27330d386f3e153a8318eebd689debb89b'
baseline_commit: 'af870e27330d386f3e153a8318eebd689debb89b'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-envelope-canonical-sequencing.md'
warnings: [oversized]
deferred:
  - summary: >-
      Three untouched contract-flow test summaries still describe camelCase Web JSON as the real
      EventStore write path, contradicting the corrected claims this bundle landed elsewhere.
    evidence: |-
      tests/Hexalith.Works.IntegrationTests/WorkItemHandoffChainContractFlowTests.cs:13-14 and
      UniformExecutorBindingLifecycleFlowTests.cs:17-18 both say "the real write path ... -> concrete
      JsonSerializerDefaults.Web serialization"; WorkItemProgressContractFlowTests.cs:57 says the event
      "survives concrete EventStore serialization" while serializing with camelCase JsonOptions.
      EventPersister writes options-free PascalCase, so the repository now asserts two different things
      about the same persisted form. These files sit outside the intent's named sweep list, so the
      omission is deliberate for this story.
    location: >-
      tests/Hexalith.Works.IntegrationTests/WorkItemHandoffChainContractFlowTests.cs:13
    severity: medium
  - summary: >-
      The frozen WorkItemCannotReferenceParentFromAnotherTenant catalog sample carries a same-tenant
      parent, so its evidence contradicts the rejection it names.
    evidence: |-
      tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs:36,81 builds Parent as
      new ParentWorkItemReference(Tenant, new WorkItemId("parent-001")) with Tenant = "tenant-alpha",
      the event's own tenant. EnvelopeCanonicalSequencingTests sources "tenant-beta" from its own helper
      instead. WorkItemV1Catalog is untouched by this change, but both new corpora now freeze those
      bytes, so correcting the sample later means regenerating two fixtures.
    location: >-
      tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs:36
    severity: low
  - summary: >-
      Nothing binds the EventStore revision quoted in the maintained docs to the actual checked-out
      submodule gitlink, so the corrected pin can silently rot on the next bump.
    evidence: |-
      grep over tests/ finds no assertion on b43e963403efa848eda9621b5e3e7e446c7faa2d or
      c61739206fd89619b7d29dfb0812225a234066bb; both SHAs exist only as prose in
      docs/eventstore-api-surface-constraints.md and docs/boundary-decision-record.md. This is the same
      documentation-drift failure mode DW-42 recorded.
    location: >-
      docs/eventstore-api-surface-constraints.md:7
    severity: low
  - summary: >-
      The byte-exact corpus never freezes the PascalCase at-rest form of EffortEstimate,
      ObligationReference, or ConversationCorrelationId, because every catalog sample leaves them null.
    evidence: |-
      All 23 EventPersisterGolden fixtures are produced from WorkItemV1Catalog samples, and those samples
      leave InitialEffort, Obligation.Reference, and ConversationCorrelationId null on every event that can
      carry them; the camelCase Golden/WorkItemCreated.v1.json does freeze all three. Those nested contract
      records therefore have no frozen writer-side form anywhere, so a property rename or shape change
      inside them cannot turn the exact corpus red. Closing it means changing catalog sample values, which
      regenerates fixtures in both corpora -- the same coupling DW-62 records.
    location: >-
      tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/WorkItemCreated.v1.json
    severity: medium
  - summary: >-
      Story48Streams serializes stand-in EventStore stream bytes with camelCase Web options, the same
      contradiction DW-61 records for three contract-flow tests but at a helper DW-61 does not name.
    evidence: |-
      tests/Hexalith.Works.IntegrationTests/Story48Streams.cs:12,31 builds StreamReadEvent.Payload with
      new JsonSerializerOptions(JsonSerializerDefaults.Web) while standing in for real per-aggregate stream
      pages, and feeds the Story 4.8 recovery sources that decode through WorksEventDecoder. EventPersister
      writes options-free PascalCase. It is not currently an escape hatch, because the shared decoder is
      separately pinned against PascalCase bytes by WorksDomainEventProcessorTests, but the fixture now
      contradicts the persisted form the rest of this bundle established. Outside the intent's named sweep
      list and outside DW-61's three files.
    location: >-
      tests/Hexalith.Works.IntegrationTests/Story48Streams.cs:12
    severity: low
  - summary: >-
      Both corpus membership gates enumerate the copied build output, so a fixture deleted from source
      survives in bin/ and membership still passes on an incremental build.
    evidence: |-
      EventPersisterGoldenCorpusTests.cs:32 and SchemaEvolutionGoldenCorpusTests.cs:26 resolve their corpus
      directory under AppContext.BaseDirectory and enumerate it with SearchOption.AllDirectories, while
      Hexalith.Works.IntegrationTests.csproj copies both directories with CopyToOutputDirectory
      PreserveNewest, which never prunes. A fixture deleted from source therefore still satisfies the
      bidirectional set-equality check until a clean build. The gate fails closed only in CI. Pre-existing
      for the Web corpus; inherited by the new exact corpus.
    location: >-
      tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGoldenCorpusTests.cs:32
    severity: low
---

<intent-contract>

## Intent

**Problem:** The envelope-canonical proof omits snapshot-backed and mid-stream rejection cases, while the frozen compatibility corpus excludes all nine durable rejection payloads and incorrectly presents camelCase Web fixtures as EventPersister bytes. The outstanding independent review also confirmed stale serializer and EventStore-pin claims in maintained documentation.

**Approach:** Extend the real EventPersister/EventStreamReader proof across the missing counter-divergence paths, split Web-reader compatibility fixtures from a byte-exact EventPersister corpus, bind both corpora bidirectionally to the frozen v1 event catalog, and correct only confirmed maintained documentation.

## Boundaries & Constraints

**Always:** Preserve all existing `SchemaEvolution/Golden/*.json` bytes; preserve every v1 rejection constructor, property, discriminator, and no-op `Apply`; use `WorkItemV1Catalog.All.OfType<IEventPayload>()` as the authoritative 23-type corpus membership; drive exact persisted bytes through the real pinned `EventPersister` with no payload protection; assert envelope positions and Works payload ordinals separately; describe a rejection-position snapshot as the manual/current-sequence snapshot path.

**Block If:** The pinned EventStore no longer writes normal concrete payloads with options-free serialization, no longer reads snapshot-at-current without a tail, or any required proof would need an edit under `references/Hexalith.EventStore` or a frozen v1 payload-shape change.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md`; edit `references/Hexalith.EventStore`; rewrite the 14 existing camelCase fixtures; add envelope fields to rejection payloads; make rejection `Apply` mutate state; mix success and rejection events in one `DomainResult`; sweep historical story/spec wording outside the maintained source, tests, and current decision documents named below.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Snapshot after rejection | Persist rejection at envelope 1, materialize its unchanged `Unknown/0` state as a sequence-1 snapshot, then issue valid create through snapshot-backed current state | Reader returns snapshot with zero tail and `LastSnapshotSequence == CurrentSequence == 1`; create carries payload ordinal 1 and persists at envelope 2 | Snapshot must not be passed to the full-stream-only `AggregateReplayer`; command rehydration uses `DomainServiceCurrentState` |
| Mid-stream rejection | Create, illegal complete, then assign, committing and re-reading after every command | Envelopes are `[1,2,3]`; success payload ordinals are `[1,2]`; replay states are `Created/1`, `Created/1`, `Assigned/2` | Rejection remains persisted and state-neutral |
| Repeated pre-create rejections | Commit missing-obligation rejection, then cross-tenant-parent rejection, then valid create | Envelopes are `[1,2,3]`; replay states are `Unknown/0`, `Unknown/0`, `Created/1`; create payload ordinal is 1 | Both rejection evidence values survive persistence independently |
| Dual corpus | Frozen catalog contains 14 success and 9 rejection event payloads | Web corpus has exactly 23 camelCase compatibility fixtures; exact corpus has exactly 23 compact PascalCase files whose raw bytes equal EventPersister payload bytes | Missing, extra, duplicate, renamed, or byte-drifted fixtures fail diagnostically |

</intent-contract>

## Code Map

- `tests/Hexalith.Works.IntegrationTests/EnvelopeCanonicalSequencingTests.cs` -- existing real persister/reader/replay harness; reuse its state manager, command-state conversion, ledger evidence, and state assertions; replace the duplicate rejection signature table with corpus coverage.
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` -- authoritative frozen samples: 14 commands are excluded, while 14 success plus 9 rejection `IEventPayload`s define both corpus memberships.
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` and `SchemaEvolution/Golden/` -- retain the existing Web/camelCase reader-compatibility lane, add nine rejection fixtures, bidirectional membership, equality, round-trip, and unknown-field tolerance.
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/` and `EventPersisterGoldenCorpusTests.cs` -- new 23-file compact, no-BOM, no-trailing-newline exact corpus sourced from catalog samples and compared to `PersistEventsAsync(...).PersistedEnvelopes.Single().Payload` raw bytes.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` -- copy both corpus directories verbatim to test output.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventPersister.cs:64`, `EventStreamReader.cs:61`, and `Contracts/Serialization/EventStorePayloadSerialization.cs:11` -- read-only writer, snapshot, and case-insensitive reader contracts.
- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`, `Runtime/WorksEventDecoder.cs`, `Runtime/WorksHost.cs`, `Runtime/Events/WorksDomainEventProcessor.cs`, `Runtime/Events/WorksDomainEventEndpointExtensions.cs`, and `Runtime/IWorkCommandSubmitter.cs` -- maintained XML/comments that must distinguish actual PascalCase persisted bytes from accepted camelCase compatibility inputs.
- `tests/Hexalith.Works.IntegrationTests/WorkItemRawActAdditivityTests.cs`, `UniformExecutorBindingSerializationTests.cs`, `WorkItemProjectionQueryAdapterTests.cs`, `GetWorkItemQueryHandlerTests.cs`, and `WorksDomainEventProcessorTests.cs` -- correct persisted-form claims; use options-free payload serialization in helpers that claim to model actual persistence while retaining explicit Web compatibility tests.
- `docs/eventstore-api-surface-constraints.md` and `docs/boundary-decision-record.md` -- update the verified EventStore pin to `c61739206fd89619b7d29dfb0812225a234066bb`; record that the characterized sequencing files are unchanged from `b43e963...`; remove contradicted case-sensitive-reader/zero-binding claims and retain the Works-local processor only for identity and terminal-handling behavior.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Hexalith.Works.IntegrationTests/EnvelopeCanonicalSequencingTests.cs` -- add the three matrix proofs through real persistence/read/command rehydration, generalize command-envelope construction, and remove `RejectionShapeSignatures`, its standalone shape test, and `ShapeOf`.
- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/` plus `SchemaEvolutionGoldenCorpusTests.cs` -- add the nine catalog rejection fixtures and make filenames match all 23 catalog event types in both directions; prove Web deserialization, equality, round-trip, and additive-field tolerance.
- [x] `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/`, `EventPersisterGoldenCorpusTests.cs`, and the integration `.csproj` -- add/document/copy the exact 23-file corpus and compare raw file bytes to the real persister output for one catalog payload per isolated `DomainResult`.
- [x] `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs`, `src/Hexalith.Works/Runtime/WorksEventDecoder.cs`, `src/Hexalith.Works/Runtime/WorksHost.cs`, `src/Hexalith.Works/Runtime/IWorkCommandSubmitter.cs`, `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs`, and `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs` -- re-term Web JSON as a case-insensitive compatibility/reader form without changing runtime decoding behavior.
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemRawActAdditivityTests.cs`, `tests/Hexalith.Works.IntegrationTests/UniformExecutorBindingSerializationTests.cs`, `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs`, `tests/Hexalith.Works.IntegrationTests/GetWorkItemQueryHandlerTests.cs`, and `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` -- correct maintained persisted-form claims, make actual-path projection helpers serialize options-free, and retain explicit Web compatibility proof.
- [x] `docs/eventstore-api-surface-constraints.md` and `docs/boundary-decision-record.md` -- apply the confirmed DW-42 provenance and shared-reader corrections without rewriting historical story artifacts.

**Acceptance Criteria:**
- Given either corpus directory, when its JSON filenames are compared to the v1 catalog event types, then the sets are identical, distinct, and contain all 23 durable payload types including all nine rejections.
- Given any exact-corpus fixture, when its catalog payload is persisted by the real EventPersister, then the envelope payload equals the fixture byte-for-byte and the fixture uses compact PascalCase concrete JSON with no `$type` or trailing newline.
- Given any maintained statement changed by this bundle, when checked against the pinned source, then it says writers persist options-free PascalCase bytes and shared Web reader options accept both PascalCase and camelCase; no maintained statement claims camelCase is required to avoid zero binding.
- Given the final diff, when protected paths are audited, then the ledger, EventStore submodule, existing 14 Web fixtures, and all frozen rejection contract shapes are unchanged.

## Spec Change Log

## Review Triage Log

### 2026-08-28 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 7: (high 0, medium 3, low 4)
- defer: 3: (high 0, medium 1, low 2)
- reject: 34: (high 0, medium 5, low 29)
- addressed_findings:
  - `[medium]` `[patch]` `docs/eventstore-api-surface-constraints.md` had been rewritten by this bundle to name `EventPersister`, `EventStreamReader`, and the shared payload serialization as "the characterized files", while still calling one characterization test "the always-on drift guard over that source". That test reads only `AggregateActor`/`EventPersister`/`AggregateReplayer` source text and asserts nothing about the reader or about casing. The paragraph now names each guard against the claim it actually covers, and points the casing claims at the two corpus tests that pin them behaviourally.
  - `[medium]` `[patch]` `docs/boundary-decision-record.md` still introduced the corrected paragraph with the bolded phrase "This casing fix is not new to Story 4.7's own command paths" while the body immediately below established that no casing defect ever existed and that case-preserving output is a consistency preference. Reworded the heading to "The command-payload casing change".
  - `[medium]` `[patch]` The snapshot-backed rehydration proof was vacuous: the rejection-position snapshot state is byte-identical to a default `WorkItemState`, and the pinned rehydrator maps a missing snapshot with no tail to a null state, from which the aggregate reads the same `Unknown` status and the same next ordinal 1 — so every assertion held whether or not the snapshot reached the aggregate. Added a second rehydration through a snapshot carrying established `Created/1` state, where a dropped snapshot flips the outcome from rejection to success. Verified by mutation: setting `ToDomainServiceCurrentState`'s `SnapshotState` to `null` now fails the test and did not before.
  - `[low]` `[patch]` `EnvelopeCanonicalSequencingTests.CommandFor` stopped deriving envelope identity from the command when it was widened to `object`, pinning every envelope to the fixture's own tenant/work-item constants. Restored command-derived identity by name, so a mistargeted command cannot be silently re-addressed by the harness that proves addressing.
  - `[low]` `[patch]` Web-corpus additive-field tolerance was injected only at the payload root. It now recurses into every nested object, including objects inside arrays, where contract records are most likely to grow.
  - `[low]` `[patch]` The exact corpus's BOM gate compared `fixture.Take(3)` against a collection literal, whose binding between Shouldly's generic and enumerable `ShouldNotBe` overloads is not obvious; a reference comparison there can never fail. Replaced with explicit per-byte checks.
  - `[low]` `[patch]` `EventPersisterGolden/README.md` named neither the pin the bytes were produced at nor a regeneration recipe, unlike its Web sibling. Added both, plus the `.editorconfig` coupling that protects the no-trailing-newline invariant.

### 2026-08-28 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 10: (high 0, medium 2, low 8)
- defer: 0
- reject: 12: (high 0, medium 2, low 10)
- addressed_findings:
  - `[low]` `[patch]` Added an `.editorconfig` exception so editors preserve the exact corpus's intentional no-final-newline bytes.
  - `[low]` `[patch]` Made both corpus membership gates reject nested and non-`.v1` JSON files instead of inspecting only top-level versioned names.
  - `[low]` `[patch]` Added recursive camelCase property-name assertions to the Web compatibility corpus.
  - `[medium]` `[patch]` Switched the Web corpus to production `EventStorePayloadSerialization.Options` and compared all nine new rejection fixtures, including unknown-field reads, with their catalog evidence values while preserving the richer historical success samples.
  - `[low]` `[patch]` Proved each exact PascalCase fixture is readable as its catalog payload through the production shared reader.
  - `[medium]` `[patch]` Added explicit Works-local processor coverage for a camelCase compatibility payload without weakening the all-consumed PascalCase lane.
  - `[low]` `[patch]` Deserialized the persisted mid-stream rejection and compared its durable evidence to the aggregate result.
  - `[low]` `[patch]` Updated the sequencing test summary to include snapshot, mid-stream, and repeated-rejection coverage.
  - `[low]` `[patch]` Corrected the decision record's reader/writer terminology for the historical command-casing characterization.
  - `[low]` `[patch]` Corrected projection-dispatcher prose to describe its locally constructed Web-compatible options rather than claiming the EventStore shared instance.

### 2026-08-28 — Review pass

- intent_gap: 0
- bad_spec: 0
- patch: 5: (high 0, medium 1, low 4)
- defer: 3: (high 0, medium 1, low 2)
- reject: 29: (high 0, medium 4, low 25)
- addressed_findings:
  - `[medium]` `[patch]` Both maintained documents attributed the reader-casing correction to the EventStore pin bump ("at the current pin", "now shares"), contradicting the same file's statement that the characterized sources are unchanged from `b43e963...`. Verified in the submodule that `EventPersister.cs`, `EventStreamReader.cs`, `EventStorePayloadSerialization.cs`, `EventStoreAggregate.cs`, and `EventStoreDomainEventProcessor.cs` are byte-identical across the two revisions, and reworded both records to say the withdrawn characterization was wrong at both pins and that no live casing defect ever shipped from Stories 4.5/4.6.
  - `[low]` `[patch]` Restored the "(STOP and escalate — see the story Critical Decision)" maintainer instruction dropped from the `WorkItemRawActAdditivityTests` summary; only the stale casing prose was in scope.
  - `[low]` `[patch]` `CamelCaseWebCompatibilityPayloadIsAcceptedByTheProjectionReader` asserted only an entry count, so a zero-bound payload could have passed the very test that disproves it; it now asserts the projected `workItemId` like its PascalCase sibling.
  - `[low]` `[patch]` Froze the snapshot record's `CreatedAt` instead of `DateTimeOffset.UtcNow`, keeping every input of the deterministic Tier-1 sequencing proof reproducible.
  - `[low]` `[patch]` Made the duplicate-sample guard reachable in both corpus membership tests: the distinctness assertion sat after `ToDictionary`, which would have thrown an opaque duplicate-key `ArgumentException` first. It now runs before the dictionary is built and names the offending types.

## Design Notes

The exact corpus intentionally uses `WorkItemV1Catalog` samples even where an older rich Web fixture uses different representative values: membership and sample construction then have one authority, while the existing Web files remain byte-untouched compatibility history. Raw exact files contain the persister payload bytes only; README files are documentation and are excluded from JSON membership.

## Verification

**Commands:**
- `dotnet build Hexalith.Works.slnx --configuration Release -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: 0 warnings and 0 errors.
- `dotnet test tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: all configured integration tests pass; environment-only Aspire tests may retain their established skips.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.EnvelopeCanonicalSequencingTests` -- expected: all envelope/snapshot counter proofs pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.SchemaEvolution.SchemaEvolutionGoldenCorpusTests` -- expected: Web compatibility and membership proofs pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.SchemaEvolution.EventPersisterGoldenCorpusTests` -- expected: exact corpus membership and byte comparisons pass.
- `git diff --check` -- expected: no whitespace errors.
- `git diff --exit-code -- _bmad-output/implementation-artifacts/deferred-work.md references/Hexalith.EventStore` -- expected: no ledger or submodule changes.
- `git diff --exit-code -- tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/{ChildSpawned,ProgressReported,ReEstimated,WorkItemAssigned,WorkItemCancelled,WorkItemClaimed,WorkItemCompleted,WorkItemCreated,WorkItemExpired,WorkItemQueued,WorkItemRejected,WorkItemRescheduled,WorkItemResumed,WorkItemSuspended}.v1.json` -- expected: no change to any pre-existing Web fixture.

## Auto Run Result

Status: done

### Summary

Second independent follow-up review over the committed bundle (`af870e27` -> working tree). No intent gap and no spec defect: the three counter-divergence proofs, the dual 23-fixture corpora, and the corrected maintained documentation all implement the contract as written. Seven findings were patched -- three medium (two maintained-document accuracy defects and one vacuous proof) and four low test/doc hardening fixes -- and three further pre-existing issues were deferred.

The substantive finding this pass is that the snapshot-backed rehydration proof, which the story exists to add, could not observe whether the snapshot was used at all: its `Unknown/0` snapshot state is byte-identical to a default `WorkItemState`, and the pinned rehydrator maps a missing snapshot with no tail to a null state, from which the aggregate reads the same `Unknown` status and the same next ordinal. It is now backed by a second rehydration through a `Created/1` snapshot, and the fix was validated by mutation rather than by assertion count.

### Files Changed (this pass)

- `docs/eventstore-api-surface-constraints.md` -- separates the two always-on guards: the characterization test covers the envelope-sequencing claims over `AggregateActor`/`EventPersister`/`AggregateReplayer` source text, while the concrete-writer and shared-reader casing claims are guarded behaviourally by the two corpus tests.
- `docs/boundary-decision-record.md` -- the bolded heading no longer calls a withdrawn defect a "casing fix".
- `tests/Hexalith.Works.IntegrationTests/EnvelopeCanonicalSequencingTests.cs` -- added the established-state snapshot rehydration that makes the snapshot path falsifiable; restored command-derived envelope identity in `CommandFor`.
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/SchemaEvolutionGoldenCorpusTests.cs` -- additive-field tolerance now injects into nested objects and array elements, not only the payload root.
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGoldenCorpusTests.cs` -- BOM gate compared per byte instead of through an overload-ambiguous sequence comparison.
- `tests/Hexalith.Works.IntegrationTests/SchemaEvolution/EventPersisterGolden/README.md` -- records the pin the bytes were produced at, the regeneration recipe, and the `.editorconfig` coupling.
- `_bmad-output/implementation-artifacts/spec-envelope-persistence-proof-hardening.md` -- triage log, three new deferred entries, and this result.

### Review Findings

- Patches applied: 7 (high 0, medium 3, low 4).
- Items deferred: 3 (medium 1, low 2) -- the exact corpus never freezes the PascalCase at-rest form of `EffortEstimate`, `ObligationReference`, or `ConversationCorrelationId` because every catalog sample leaves them null; `Story48Streams` serializes stand-in stream bytes as camelCase; both membership gates enumerate the `PreserveNewest` build output, so a source-deleted fixture survives an incremental build.
- Items rejected: 34 (medium 5, low 29). Five were checked against the code and found false: the "lost 9-rejection invariant" claim (`EnvelopeCanonicalSequencingTests.cs:321` still asserts `rejections.Length.ShouldBe(9)`), the "Web success fixtures are unvalidated" claim (nine per-event value facts plus `Base_shape_lifecycle_events...` pin them), the "`GetWorkItemQueryHandlerTests` and `UniformExecutorBindingSerializationTests` lost their camelCase lane" claim (both still read through Web options, and the camelCase-input lane lives in the Golden corpus and `WorkItemProjectionQueryAdapterTests.WebDto`), the unused-`Web`-field claim (still used at `GetWorkItemQueryHandlerTests.cs:125`), and the "generic processor is never fed PascalCase" claim (`WorksProcessorDispatchesEveryConsumedPersistedEventOnce` does exactly that). Findings about `deferred-work.md` -- that the diff edits a Never-listed path, that DW-38 is closed while DW-61 is open, duplicated `decision:` lines, and identical `resolution-undo` tokens -- were rejected as out of this story's authority: that file is orchestrator sweep bookkeeping, not story scope. The rest were cosmetic, duplicated an already-recorded deferral, or asked for proofs the intent assigned elsewhere.
- Follow-up review recommendation: `true`; patched score = `3 x 3 medium + 1 x 4 low = 13`.

### Verification Performed

- `dotnet build Hexalith.Works.slnx --configuration Release -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- 0 warnings, 0 errors.
- `EnvelopeCanonicalSequencingTests` 6/6, `SchemaEvolutionGoldenCorpusTests` 20/20, `EventPersisterGoldenCorpusTests` 2/2 -- all passed, 0 skipped.
- Full deterministic lane (`Hexalith.Works.IntegrationTests -class- "*SmokeTests"`) -- 205 passed, 0 failed, 0 skipped. `Hexalith.Works.UnitTests` 528, `Hexalith.Works.ArchitectureTests` 207, `Hexalith.Works.PropertyTests` 3 -- all passed.
- Mutation check on the snapshot patch: with `ToDomainServiceCurrentState`'s `SnapshotState` forced to `null`, `SnapshotAfterRejectionRehydratesAtCurrentSequenceWithoutTail` now fails on the established-state assertion; before the patch that same mutation passed. The mutation was reverted and the build re-verified clean.
- `git diff --exit-code -- references/Hexalith.EventStore` -- unchanged. All 14 pre-existing Web fixtures unchanged (the only `Golden/` changes since `af870e27` are the nine new rejection fixtures and the README). All 23 exact-corpus fixture bytes unchanged by this pass.
- `git diff --check` -- clean.

### Residual Risks

The environment-gated Aspire smoke lane was not run in this pass; it was excluded deliberately (its `WorksReminderRecoveryPipelineSmokeTests` AppHost start-up failure is a known environment limitation, and nothing in this pass touches that surface). The three nested contract records now recorded as deferred have no frozen writer-side form in either corpus, so a rename inside `EffortEstimate`, `ObligationReference`, or `ConversationCorrelationId` would not turn the exact corpus red.

### Note on the working tree

`_bmad-output/implementation-artifacts/deferred-work.md` carried uncommitted orchestrator sweep bookkeeping before this run began (DW-34/35/37/38/42 flipped to `done`, DW-61/62/63 appended). This run neither read it as story scope nor modified, reverted, nor committed it, so the spec's `git diff --exit-code` ledger gate is expected to report it and the working copy stays dirty on that one orchestrator-owned file after finalization.
