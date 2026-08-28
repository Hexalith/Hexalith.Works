---
title: 'Characterize claim persistence conflicts at runtime'
type: 'bugfix'
created: '2026-08-28'
status: ready-for-dev
baseline_revision: 'cb75ba0d12c462e4b141793f6e1799170e0cc5a2'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Works' claim-concurrency documentation points to Story 4.5 Aspire coverage that never issued competing claims, leaving EventStore persistence-conflict retry, Works re-handle semantics, and retry exhaustion without Works-specific executable proof.

**Approach:** Add deterministic in-process integration coverage around the real EventStore `AggregateActor` and `EventPersister`, using two claims derived from one committed queued state. Inject atomic-save conflicts at the state-manager boundary, assert persisted end-state for retry and exhaustion, and replace the false Aspire statements with exact test pointers.

## Boundaries & Constraints

**Always:** Exercise `AggregateActor.ProcessCommandAsync`, the real `WorkItemEventStoreAggregate`, real persistence/rehydration, and the atomic `SaveStateAsync` conflict signal; verify committed stream contents, publication, retry count, and the existing `WorkItemTransitionRejected(InProgress, "Claim")`; keep the helper test-only and deterministic.

**Block If:** The pinned EventStore actor cannot be executed without changing the EventStore submodule, or a conflict-injecting state manager cannot distinguish an event-batch save from checkpoint/cleanup saves.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md` or files under `references/Hexalith.EventStore`; use threads or sleeps; use Aspire, Docker, Dapr sidecars, or network calls to implement or provide the claim-conflict proof; add production commands/events/rejections, change the public API, alter catalog/golden payloads, or describe the in-process proof as a live provider-ETag test.

**Broad Verification Only:** The Aspire/Docker/Dapr/network prohibition does not prevent running existing Aspire-backed tests as a broad regression gate. Every discovered test in that gate must pass; an Aspire host-start cancellation remains blocking and may be retried after environment recovery, but focused evidence never waives it.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Competing claim wins during retry | Claim A and claim B both derive `WorkItemClaimed` from the same committed `Queued` stream; A's save conflicts while B becomes committed | Actor clears cache, rehydrates B's `InProgress` state, re-handles A, persists/publishes `WorkItemTransitionRejected(InProgress, "Claim")`; stream is create, queue, B claim, A rejection | A's uncommitted claim is discarded; rejection apply is a state no-op |
| Retry budget exhausts | Same competing-claim setup, with the retry's rejection save also conflicting and `MaxPersistenceConflictRetries = 1` | Actor returns rejected `CommandProcessingResult` with `ConcurrencyConflict`, publishes nothing, and leaves only B's winning claim committed | Infrastructure conflict stays distinct from domain rejection and is not dead-lettered |

</intent-contract>

## Code Map

- `tests/Hexalith.Works.IntegrationTests/WorkItemClaimPersistenceConflictTests.cs` -- new Works-specific actor-pipeline scenarios; reuse `EventPersister`, `EventStreamReader`, `FakeDomainServiceInvoker.SetupHandler`, and `WorkItemEventStoreAggregate.ProcessAsync`.
- `tests/Hexalith.Works.IntegrationTests/ConflictInjectingActorStateManager.cs` -- new one-type test decorator over `InMemoryStateManager`; fail only saves with a staged event append, discard the losing batch, commit the competing winner once, and permit cleanup saves.
- `tests/Hexalith.Works.IntegrationTests/EnvelopeCanonicalSequencingTests.cs` -- reuse envelope creation, real persistence/rehydration, `DomainServiceCurrentState`, and replay patterns; do not duplicate its sequencing scope.
- `tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs` -- retain pure Tier-1 candidate/re-handle tests but replace the false Story 4.5 paragraph with exact integration-test method pointers.
- `docs/lifecycle-transition-matrix.md`, `docs/eventstore-api-surface-constraints.md` -- correct current concurrency/exhaustion guidance and identify the exact executable proof without claiming Aspire/provider coverage.
- `_bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md`, `_bmad-output/implementation-artifacts/4-5-prove-the-command-event-pipeline-under-aspire.md` -- add post-story corrections wherever they falsely assign this proof to Story 4.5; preserve historical counts and completed work.
- `_bmad-output/implementation-artifacts/tests/test-summary.md` -- replace false pointers and add the current characterization, final counts, commands, and catalog result.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs`, `Events/EventPersister.cs`, `Configuration/CommandConcurrencyOptions.cs` -- read-only pinned runtime contract: clear-cache retry/re-handle and `CompleteConcurrencyConflictAsync` exhaustion.
- `tests/Hexalith.Works.IntegrationTests/WorkItemV1Catalog.cs` and architecture/schema tests -- unchanged freeze gates: 37 catalog entries, 14 commands, 14 success events, 9 rejections, and unchanged golden bytes.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Hexalith.Works.IntegrationTests/ConflictInjectingActorStateManager.cs` -- implement a forwarding test state manager that injects a bounded number of event-batch save conflicts and commits the external winner on the first conflict.
- [x] `tests/Hexalith.Works.IntegrationTests/WorkItemClaimPersistenceConflictTests.cs` -- seed a real queued stream, drive the real actor/Works aggregate for both matrix rows, and assert domain attempts plus committed stream/replay/publication/status end-state.
- [x] `tests/Hexalith.Works.UnitTests/WorkItemClaimConcurrencyTests.cs`, `docs/lifecycle-transition-matrix.md`, and `docs/eventstore-api-surface-constraints.md` -- replace current false Story 4.5 claim-conflict assertions with exact new test names and distinguish in-process runtime characterization from live provider proof.
- [x] `_bmad-output/implementation-artifacts/4-3-claim-queued-work-with-single-claim-wins.md`, `_bmad-output/implementation-artifacts/4-5-prove-the-command-event-pipeline-under-aspire.md`, and `_bmad-output/implementation-artifacts/tests/test-summary.md` -- add explicit post-story corrections, update stale pointers, and record final executable coverage/counts without rewriting historical Story 4.5 results.

**Acceptance Criteria:**
- Given the retry and exhaustion tests, when their two initial claims are inspected, then both were computed from the same queued state with the same Works payload ordinal and only the configured winner is ever committed.
- Given the retry-success stream, when it is replayed, then it is gapless through envelope 4, remains `InProgress` bound to the winner, and contains the loser's existing transition rejection as the only published loser event.
- Given the exhaustion result, when status, stream, publication, and dead-letter evidence are inspected, then it is a `ConcurrencyConflict` with zero emitted events, no loser append/publication/dead letter, and the stream remains at the winner's envelope 3.
- Given the completed change, when the public/catalog and protected-path gates are checked, then no production/API or golden-payload file changed, catalog count remains 37, and neither the ledger nor EventStore submodule changed.
- Given maintained claim-concurrency documentation, when Story 4.5/Aspire references are searched, then none claims its smoke lane covers competing claims; every runtime-proof pointer names `WorkItemClaimPersistenceConflictTests` and accurately scopes it as in-process actor coverage.
- Given broad regression verification, when the full integration and architecture suites are run, then every discovered test passes; an Aspire host-start cancellation blocks completion and cannot be waived by focused claim-conflict evidence.

## Spec Change Log

- 2026-08-28 -- Resolution: Aspire remains outside implementation and claim-conflict proof, but existing Aspire-backed tests remain mandatory broad regression verification and must be fully green.

## Review Triage Log

## Design Notes

The test state-manager decorator is deliberately below the actor: `EventPersister` stages the loser's candidate normally, and only the atomic actor-state save conflicts. On the first conflict it clears that pending batch, commits the independently computed winner through the underlying real in-memory store, and throws the same `InvalidOperationException` the pinned actor translates into a persistence conflict. This preserves the runtime ordering under test while keeping provider/network behavior out of scope.

## Verification

**Commands:**
- `dotnet build Hexalith.Works.slnx --configuration Release -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0` -- expected: 0 warnings and 0 errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorkItemClaimPersistenceConflictTests` -- expected: both conflict scenarios pass with no skip.
- `tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests -class Hexalith.Works.UnitTests.WorkItemClaimConcurrencyTests` -- expected: existing pure claim cases remain green.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests` and `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- expected: every discovered test passes and catalog/schema gates remain green; any Aspire host-start cancellation blocks completion and may be retried after environment recovery, not waived.
- `git diff --check` -- expected: no whitespace errors; protected-path audit shows no ledger, EventStore submodule, production contract, or golden-payload change.

