# Story 4.15 post-review verification — 2026-10-17

Story 4.15 remains **in review and incomplete**. All sixteen reopened test/harness corrections and three direct independent-review patches are implemented. Final broad checks pass, but the required current-source live restart proof fails. No commit, push, dependency change, submodule update, or production cutover was performed in this run.

The production correction rejects undefined `WorkItemTransitionRejected.FromStatus` values only in recovery; defined values and historical readers remain supported. The test harness now bounds retained-plan acquisition by its existing four-minute deadline. The preceding [review correction packet](../review-fixes-2026-10-17/implementation.md) also retains its 47 raw evidence files with compressed/decompressed hashes. These are separate from this final post-patch packet.

| Final gate | Result |
| --- | --- |
| Debug integration build | Passed, zero warnings/errors |
| Debug integration, smoke classes excluded | 894 passed; zero failed/skipped |
| Release integration build | Passed, zero warnings/errors |
| Release integration, smoke classes excluded | 894 passed; zero failed/skipped |
| Unit / architecture / property | 589 / 269 / 3 passed; zero failed/skipped |
| Focused live restart | Exit 1; 1 failed, zero skipped; 523.833 seconds reported by xUnit |

[Result manifest](results-manifest.json) contains every exact command, environment override, exit, elapsed time and raw-log/XML hash, including unsuccessful attempts. [Matrix audit](matrix-audit.json) verifies 130 passing test-case observations. The [runtime audit](root-runtime-audit.json) verifies the three observed expiry phases and explicitly records the missing required phases; it does not claim full runtime acceptance. The final callback/helper gate was not run because the restart gate failed. A previous class retry passed its callback and two helpers but failed restart; its overall gate remains preliminary.

The final failing command, run from the repository root, was:

```bash
dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -method '*SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart' -longRunning 45 -result-xml /tmp/story415-root-final-zid_b9w1/final-restart.xml
```

The existing four-minute condition deadline expired while waiting for the canonical delayed-child source to report `evidence-unavailable`. Its last persisted source value was null. The test retained phase `canonical-related-child-creation-gap-timeout` and raised `TimeoutException: Delayed-child evidence was not observed. Checkpoint= Status= Reason= FailureSequence=.` Budgets and domain/security assertions were unchanged.

Read-only tracing captured 22 atomic Redis snapshots from this exact labelled fixture. The date delivery at owner sequence 5/global position 11 has an applied model batch, exact canonical model bytes, source checkpoint and matching terminal receipt, yet remains at the runtime journal head. Parent and sibling entries wait behind it. A nonempty journal makes this family stale; periodic discovery reports `catalog-unavailable`, retaining only the original three candidates. Direct callbacks are intentionally discarded in this method to prove periodic recovery, so their absence does not establish a callback admission defect. [Diagnostic record](live-restart-discovery-diagnosis.json) preserves the source paths, counterevidence and underlying closeout uncertainty. The exception, read or compare-and-swap outcome preventing journal dequeue is **not established**, and no speculative SDK fix was made.

The first live class attempt also failed before domain assertions because Sentry could not allocate an inotify watch (`no space left on device`). Disk and inode space were available; the host watch limit was almost fully consumed by other processes. Later attempts temporarily raised `fs.inotify.max_user_watches` from 1048576 to 2097152, then restored 1048576 in `finally`. Both restoration records are retained. No unrelated applications were stopped. The second full-class attempt and final focused attempt both reached expiry replay and timed out at delayed-child discovery; neither is selected as a passing live proof.

[Source and binary bindings](source-and-binary-bindings.json) distinguish nine current sibling SDK project outputs in Debug from nine 3.124.0 packages in Release. All 2443 captured source/build inputs remained byte-identical throughout final verification. Dependency pins remain unchanged. The four retained sidecar configurations permit only the approved three HotReload opt-out lines; authentication and control-plane ownership remain enabled. Package-host live, complete rollback, historical 3.123.0 acceptance and Platform parity retain their separately recorded scope.

[Cleanup](cleanup.json) confirms the exact owned Redis container and Scheduler-volume label have no remaining resources and the kernel watch limit is restored. The observer was stopped. The raw trace secret-pattern inspection found no JWT-shaped values, bearer headers or private-key PEMs, including decoded JSON fields; these are disposable synthetic domain records.

The independent review classified all twelve findings: three patches, five separate pre-existing/separate-gate follow-ups, two rejected claims and two carried instances of an existing unresolved claim. Review iteration remains 4; frozen intent and the original baseline commit are preserved. The five new follow-ups are in the deferred-work ledger. This live failure is an acceptance blocker, not a deferred completion item.

Raw logs, XML, state exports, diagnostics and runner scripts are retained as gzip archives. `raw-artifacts.json` maps original provenance paths to repository-local archives and verifies compressed and decompressed bytes. Original `/tmp` paths describe provenance; verification does not read them. Run from any checkout:

```bash
python3 _bmad-output/implementation-artifacts/evidence/story-4-15/post-review-verification-2026-10-17/verify-raw-artifacts.py
```

A fresh-directory copy passed verification and a modified compressed archive was rejected. This packet records the exact blocker for SDK journal closeout investigation. Under the bmad-build review rule, failed verification without an established fix halts before marking the spec done, changing sprint status to review, or committing.
