---
title: 'Close five deferred process recovery policy gaps'
type: 'bugfix'
created: '2026-10-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred process recovery findings lack focused Works consumer qualification of current SDK fairness, publication admission and incomplete discovery behavior.

**Approach:** Execute the actual SDK reconciler and Works translator against serialized retained state and controlled adapter faults. Prove bounded healthy-source progress, safe publication saturation and subsequent acceptance, read-admission cursor retention, and degraded coverage with retry after a capped missing-page scan. Close exactly five ledger entries with retained evidence, independently review, verify, commit and push main as requested.

</frozen-after-approval>

## Implementation Notes

- Freeform work, no epic story key. Small consumer-test and operator-guidance change; no public API, runtime, dependency, topology, migration or unresolved human policy change. Commit and push are explicitly authorized. The repository has no AppHost; deterministic consumer qualification needs no running topology.
- Selected exact summaries: Fairness tests check only the end state under generous loop caps (12–30 passes), so a regression from 2 passes to 20 still passes; Pending-publication saturation is tested only at the store level; no reconciler test shows the catalog cursor held on the rejected key, Degraded readiness, and later acceptance; Read-admission failure is not tested across a reconciler pass; the cycle-9 rule "must not publish Healthy or lose the source cursor" is proven only for the runner and the pool; An unaccepted publication admission pins the catalog and freezes candidate service; A probe-capped scan with no tail still completes the cycle.
- Add `WorksSdkProcessRecoveryPolicyTests.cs` and a single-type `WorksProcessRecoveryFaultStore.cs`. Reuse SDK `ProcessReconciler`, `ProcessCheckpointStore`, `ProcessSweepStore`, `ProcessReadinessHealthCheck`, serialized `InMemoryReadModelStore`, real `WorksProcessTranslator` and `Story48Streams`. Catalog, gateway and effect transport are controlled external boundaries. Reconstruct providers over the same persisted bytes.
- Pin fairness to explicit pass bounds derived from the pass budget. Saturation keeps an unaccepted catalog key and Degraded readiness while still serving retained candidates; a repaired pending publication frees admission. This does not promise admission through a full queue. A capped missing-page scan may finish a cycle but must retain unresolved evidence, remain Degraded and retry from the head after reconstruction. Existing cross-cycle catalog-continuation and custom serializer policy findings remain outside this batch.
- Inject the actual internal SDK read-admission exception reflectively at the store boundary, proving reconciler handling without claiming global pool-load qualification. Validate the synthetic option profile explicitly. Assertions inspect persisted source checkpoints, candidate membership, pending identities, cursor tuples and reason counts.
- Architecture AD-09/27 and Hexalith state instructions apply. Update the current SDK process section of the operator runbook, preserve historical ledger evidence, and add exactly five dated resolutions. Validate Debug compilation and direct named xUnit classes, then deterministic integration regression; retain broad source-build failures separately from focused fallback evidence. Independently review before finalizing; validate the Conventional Commit, fetch and inspect divergence before pushing main.

- Starting revision: `10d89b3ce073abca00a0e50e49571dbc518dc38f`. The broad Debug project build exits 1 with the existing Platform `CA1062` errors in `PlatformHostedPublishing.Describe` and `ComposeNativeProbe`; focused compilation with `-p:BuildProjectReferences=false` succeeds with zero warnings/errors using available dependency outputs. This does not claim a clean full source build.
- Nine new cases exercise fairness across reconstruction, saturated admission with retained candidate progress and later acceptance, five precise read-admission boundaries, and no-tail linked-page repair. Saturated admission retains the current cycle's unresolved observation after membership is accepted; it does not promise the admitted source will execute before the other retained repair work drains. The no-tail fixture preserves the source and tenant markers from a partially missing discovery inventory.

- Initial related recovery qualification passes 83/83, zero errors/failures/skips/unexecuted. Corrected the probe fixture to retain a valid earlier source on page zero; an empty persisted page is itself invalid and would not isolate a missing-page fault. Strengthened its finite-probe and absent-tail assertions before final verification.

## Review Triage Log

- Medium / patch: the two-unit fairness fixture permits an unnecessary third pass and omits the one-unit repair cadence. Qualify exact bounds of two passes for two units and three for one unit, including provider reconstruction.
- Medium / patch: initial saturation progress could conceal starvation after the first retained candidate. Add a second retained candidate and require its next-pass progress after reconstruction while all 128 pending slots remain occupied.
- Medium / patch: acceptance assertions could conceal deleted unrelated publication intents. Require exactly three actual successful repairs, durable exact candidate markers for each repaired source, 125 remaining original identities and complete identity conservation.
- Medium / patch: zero-page cursor fixtures do not prove link/probe retention. Restore valid two-page tenant and candidate chains, inject at nonzero cursors, compare page/offset/link-expectation/probe tuples before and after reconstruction, and require fresh Healthy coverage after recovery.
- Medium / patch: operator guidance could imply ordinary publication repairs missing pages when markers survive. State the synthetic authoritative-page restoration boundary, lack of an in-place repair qualification, escalation to the data recovery owner and the published synthetic fleet procedure's AD-28 limit.
- Medium / patch: the evidence link was absent at review. Create a preliminary packet, then replace it with final post-review commands, hashes, executed XML and blocker logs before acceptance. Nothing is deferred.

- All six review findings are patched. Strengthened focused qualification passes 10/10 with zero errors/failures/skips/unexecuted. One-unit passes serve the later source by pass three; two-unit passes by pass two. A second retained candidate advances after reconstruction with the publication map still full. Three successful publication repairs conserve all 128 original identities, with 125 still pending and exact durable membership for the repaired three. Five nonzero cursor cases preserve all eight cursor fields, including expected-page and probe state, before and after reconstruction. The missing linked-page control counts the initial absent page and exactly four fallback probes, while a valid earlier source advances; restoring the captured page produces a new Healthy cycle.

- Independent follow-up confirms the six corrections, finds no remaining code or operator-guidance blockers, and independently reruns all 10 policy cases successfully. The earlier "Nine new cases" note records the pre-review suite; the final strengthened suite contains ten. Evidence finalization remains the last verification step.

## Verification

- Final post-review Debug build succeeds with zero warnings/errors using `-p:BuildProjectReferences=false`. Related recovery qualification passes **84/84**; deterministic integration qualification passes **936/936**, both with zero errors/failures/skips/unexecuted. Live Smoke/Platform classes are explicitly excluded from the deterministic run; this does not claim live or public-package qualification.
- Commands: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1 -p:BuildProjectReferences=false`; `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class '*WorksSdkProcessRecoveryPolicyTests' -class '*WorksSdkProcessRecoveryTests' -class '*WorksSdkRecoverySourceTests' -class '*WorksSdkRecoveryCompositionTests' -result-xml /tmp/works-process-policy-related.xml`; `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/works-process-policy-deterministic.xml`.
- The broad command `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1` exits 1 with two existing `CA1062` errors in Platform `PlatformHostedPublishing.Describe` and `ComposeNativeProbe`. The successful focused fallback consumes available dependency outputs and does not qualify a complete fresh Platform source build. The unsuccessful broad log is retained separately.
- Exactly five two-line status/resolution pairs are inserted into the ledger. Removing those exact pairs reproduces the original ledger bytes. Original evidence is preserved. All six independent review findings are patched; no new deferred work. New C# and spec files use CRLF; existing Markdown keeps LF.
- `npm run commitlint -- --edit /tmp/works-process-policy-commit.txt` accepts `test: close five deferred process recovery gaps`. Fetch confirms HEAD and origin/main both `10d89b3ce073abca00a0e50e49571dbc518dc38f`, zero divergence. No dependency, gitlink or runtime changes.
- [Retained packet](evidence/five-deferred-process-recovery-policy-2026-10-24/manifest.json) binds final commands/results, compressed raw XML/logs, source/binary hashes and full dependency revisions. The ten executing policy cases use controlled external adapters and serialized retained state; the existing two Platform analyzer failures remain explicitly recorded.

- Final `dotnet format whitespace Hexalith.Works.slnx --no-restore --verify-no-changes --include tests/Hexalith.Works.IntegrationTests/WorksSdkProcessRecoveryPolicyTests.cs tests/Hexalith.Works.IntegrationTests/WorksProcessRecoveryFaultStore.cs` exits 0 with the generic workspace-load warning; formatting requires no changes.
