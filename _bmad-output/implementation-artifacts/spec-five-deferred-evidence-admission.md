---
title: 'Qualify five deferred projection and recovery evidence safeguards'
type: 'chore'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five open deferred entries describe historical evidence-admission gaps in readers replaced by the SDK migration: unknown projection events, unbounded decoder skip diagnostics, duplicate or gapped projection sequences, malformed stream-page cursor metadata, and unvalidated child/cascade recovery identities. Their current replacements need executing qualification before these entries can close.

**Approach:** Extend focused integration tests to prove fail-closed projection and recovery behavior, bounded EventId 4504 diagnostics, and preservation of committed generations after refused rebuilds. Verify existing valid redelivery, gap recovery and paged translation controls alongside the new negative controls. Record dated resolutions for exactly these five entries, independently review, validate, commit and push Works main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline: `9964499645dca28254a16b77dd14a5124cebd7dc`, clean Works main. No epic story key applies. This is a small test-only qualification with ledger/evidence updates, no new runtime API, migration, dependency change or unresolved intent choice; use oneshot. The requested final commit and push are authorized.
- Current owners: `WorkItemSharedRebuildManifestBuilder.ValidateHistory` rejects noncontiguous histories; `BuildEpoch` rejects unknown/malformed events before producing a replacement plan. `WorkItemSharedProjectionRebuildHandler` distinguishes identical source redelivery from conflicting equal-sequence DTOs. Keep these production contracts unchanged.
- Extend `WorkItemSharedProjectionRebuildHandlerTests` using its actual SDK dispatcher and `RefuseRebuildAsync` fixture. Cover unknown evidence after a valid prefix and duplicate/gapped histories, retaining the seeded generation and exact served document bytes across coordinator reconstruction. Existing `WorksSdkProjectionQueryTests` covers ordinary idempotent redelivery, durable conflicting-sequence parking and gap convergence.
- Extend `WorksSourceStreamReaderTests` with inconsistent cursors, truncated pages, missing heads, duplicate/nonpositive positions, and foreign page domain/aggregate. Assert no partial context or unsafe follow-up acquisition; retain valid multi-page controls.
- Extend `WorksSdkIntentTranslationTests` with related-parent and related-child page/payload faults and malformed relationship evidence. Prove both translation paths return no effects from invalid evidence and still produce the expected effect after a corrected read.
- Add `WorkItemProjectionDecoderTelemetryTests` using `Story48RecordingLogger` to execute unknown and malformed decoder paths. Require structured EventId 4504 fields to be bounded and single-line with payloads and exception details absent, preserving decode classifications.
- Verify explicit Debug integration build, focused named classes, then deterministic integration tests excluding live Smoke/Platform lanes. If the dependency build is blocked by known upstream Platform analyzer errors, record the exact blocker separately and use `BuildProjectReferences=false` only with existing dependency outputs; do not change or suppress upstream checks. Works has no AppHost and this test-only batch requires no topology change.
- Retain exact commands, source/dependency identities, test logs/XML and hashes in an evidence packet. Preserve original ledger evidence, validate whitespace and the Conventional Commit message, fetch and inspect divergence, then push main.

- Added negative controls to the five selected test classes. The first run passed 129/133 facts; four new rebuild cases expected refusal at finalization but the SDK correctly refuses duplicate, conflicting, gapped and reordered histories during request validation. The tests now require that exact internal exception type/reason and inspect unchanged accepted inventory before checking the committed generation. Unknown contiguous evidence reaches finalization and fails there. No runtime defect was found.
- The refused-rebuild helper now compares queue, roll-up, pending-date, topology, history header and parking bytes through a reconstructed coordinator. The ordinary-delivery control compares all materialized views and history header, retains the folded source watermark, checks durable parking, and proves queries unavailable. The SDK journal checkpoint deliberately advances over parked evidence; this is distinct from the retained projection watermark.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1` fails with the pre-existing Platform `CA1062` errors in `PlatformHostedPublishing.Describe` and `ComposeNativeProbe`. The focused compile with `-p:BuildProjectReferences=false` succeeds with zero warnings/errors using existing dependency outputs, without suppressing warnings or editing dependencies.

- Final qualification strengthens unknown/gapped ordinary-delivery coverage with an existing persisted date await: its nonempty entries, pending watermark, queue watermark and exact materialized documents survive refusal, and both queue and detail queries report unavailable. Child/cascade controls use one-envelope contextual pages and prove valid subsequent recovery for every injected fault.
- The focused five-class run passes 133/133 with zero errors, failures, skips or unexecuted tests. The initial four sequence cases needed a second fixture correction: SDK Inspect requires the original capture fingerprint. Both initial failure runs are retained as test-development evidence, not qualification results.

- Scope correction from independent review: the initial test-only assessment is superseded for the selected cursor item. A later page can regress its declared head while individually passing the SDK validator. The executing red control fails exactly that case (14/15 pass); the valid growing-head control passes. `WorksSourceStreamReader` now remembers the highest observed authoritative head and refuses regression before decoding/returning context. This is a local guard in the existing domain reader, with no public API, dependency or topology change. It requires a focused Works runtime rebuild before rebuilding tests.
- Initial deterministic run before review corrections passed 898/898. Review adds durable process-runner controls, a valid first cascade child before a corrupt later child, exact recovered command assertions, full retained history reconstruction, the default five-attempt unknown-event policy, and EventId 4505 parity. Final counts below supersede this intermediate result.

## Review Triage Log

- Medium / patch: cross-page head regression is reachable through two individually valid pages and hides an already-observed tail. The new red test reproduces it; retain and compare the observed head and keep a valid growing-head control.
- Medium / patch: translator-only refusals did not qualify durable process outcomes for page/payload faults. Extend `InconsistentRelatedStreamsRetainQuarantine` through the SDK runner for both recovery paths, reconstruct checkpoints/runner, and assert quarantine, failure sequence, retained checkpoint, no pending plan and no submissions.
- Medium / patch: one-child cascade negatives did not prove earlier valid descendants cannot become a partial plan. Each cascade matrix case now includes an ordinally earlier valid child and asserts its contextual acquisition before the later-child refusal; corrected reads return both targets.
- Low / patch: corrected translator controls checked routing but omitted command semantics. Assert command type, ordinal, decoded tenant/target and the exact completed-child await.
- Medium / patch: retained history manifest comparisons alone omit separate page bytes. Reconstruct complete histories through `WorkItemRuntimeHistoryStore` before/after refusal and compare serialized bytes, including all referenced pages.
- Medium / patch: parking-at-one omitted the default retry interval. Add the default five-attempt unknown-event case; reconstruct after each retry and assert retained documents/history/watermarks, pending failure counts, unchanged checkpoint and unavailable/degraded queries until parking.
- Low / patch: EventId 4505 sanitization parity lacked an executing control. Add a foreign-identity event with the same long multiline correlation and verify exact structured field bounds, classification and no payload/exception disclosure alongside 4504.

- Independent follow-up confirms six original corrections and no concrete production regression. It identified two incorrect default-retry assertions, both patched: SDK journal admission already advances its checkpoint, so compare against the checkpoint observed after admission while folded watermarks remain at two; served reads are unavailable during pending delivery, so compare raw persisted documents with generation-pinned reads and separately assert unavailable query responses. A nullable metadata assertion compile error was also corrected without changing behavior.
- Medium / patch (follow-up): the default-retry fixture conflated the SDK journal checkpoint with folded state. Capture the admitted checkpoint and assert it remains stable across retries.
- Medium / patch (follow-up): pending journal reads correctly return no served values. Assert that unavailability, then use `ReadGenerationAsync` for persisted end-state preservation checks at the prior generation.

- Independent final follow-up confirms all seven original findings and both follow-up fixture corrections are addressed; no further concrete regression found. Final runtime and integration Debug builds succeed with zero warnings/errors using the explicitly recorded focused fallback. Whitespace verification succeeds with only the generic workspace-load warning. Commitlint validates `fix: close five deferred evidence admission gaps`. Fetch shows no divergence from origin/main, and no submodule gitlink changed.


## Verification

- Final focused runtime build: `dotnet build src/Hexalith.Works/Hexalith.Works.csproj --configuration Debug --no-restore -m:1 -p:BuildProjectReferences=false` exits zero with zero warnings/errors.
- Final focused test build: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1 -p:BuildProjectReferences=false` exits zero with zero warnings/errors. Both consume existing dependency outputs; the separate broad Platform analyzer blocker above remains unchanged.
- Direct xUnit executable, six named classes: **152/152 passed**, zero errors/failures/skips/unexecuted. Classes: WorkItemProjectionDecoderTelemetryTests, WorksSourceStreamReaderTests, WorksSdkIntentTranslationTests, WorksSdkProcessRecoveryTests, WorkItemSharedProjectionRebuildHandlerTests and WorksSdkProjectionQueryTests.
- Final deterministic command: `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/works-five-evidence-validated-deterministic.xml` exits zero: **906/906 passed**, zero errors/failures/skips/unexecuted. Live Smoke/Platform lanes are excluded explicitly; this does not claim new live parity or public-package qualification.
- The exact before-fix head-regression control fails 1/15 as expected; both monotonic-head controls pass after the fix. The [retained packet](evidence/five-deferred-evidence-admission-2026-10-23/manifest.json) binds final source/dependency revisions and binary hashes to exact commands, compressed logs/XML, intermediate fixture failures and the known broad-build blocker.
- Whitespace verification and `git -c core.whitespace=cr-at-eol diff --check` pass. `npm run commitlint -- --edit /tmp/works-five-evidence-commit.txt` passes for `fix: close five deferred evidence admission gaps`.
- Exactly five selected ledger entries are resolved with their original evidence retained. All seven review findings and two follow-up fixture findings are patched; no new deferred item. Production change is limited to the four-line stream-head guard. No dependency version or submodule pointer is changed.
