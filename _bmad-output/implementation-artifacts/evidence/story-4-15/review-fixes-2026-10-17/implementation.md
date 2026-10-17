# Story 4.15 review corrections — 2026-10-17

The initial sixteen reopened integration-test/harness corrections were implemented without changing production code, dependency pins, SDK source, frozen intent or historical evidence. The scoped review follow-up below subsequently tightens one recovery-only predicate; the original verification retains its exact source scope. The existing selection remains current SDK source in Debug and all nine published EventStore 3.124.0 packages in Release; source binaries are not described as published producers.

The HTTP cases now prove deferred disposition is actually applied at the retained generation, all four malformed-request 400 paths and unavailable-audit 503 preserve durable state, both delegated resume purposes admit a real resume, sidecar routing wins over a configured direct gateway, expired service credentials have a valid but expired lifetime, and unauthenticated callers create no origin-denial audit. Ineffective local-object assertions were removed.

The smoke fixture preserves coordinated batch writes/replay, asserts the committed revision-2 expiry witness, bounds each poll invocation, preserves caller cancellation and appends startup logs once. Reminder identity and paged message-id assertions use distinct committed facts. The synchronous provider blocker has a five-second limit. Redis authority material and topology certificates are deleted even when earlier cleanup/setup fails; the existing ownership-negative test now asserts directory removal.

The retained synthetic harness now reuses its existing application/sidecar port isolation. Fixed control-plane ports, exact ownership checks and all authentication remain active. An initial attempt timed out because its gateway /alive proxy used localhost:8080, owned by a pre-existing FrontComposer DCP, while the gateway process listened on a different target port. The unrelated owner was untouched. [Port attribution](port-attribution.json) preserves the observations without treating the repeated metadata 500s from waiting Works as their cause.

| Executed gate | Result |
| --- | --- |
| Full non-live Debug integration | 889 passed, zero failures/skips |
| Full non-live Release integration | 889 passed, zero failures/skips |
| Affected HTTP/topology/harness, both modes | 134 passed each, zero failures/skips |
| Current unit / architecture / property | 589 / 269 / 3 passed, zero failures/skips |
| Exact protected Dapr copies and ACL negatives | 8 passed |
| Final Debug / Release builds | Zero warnings/errors |
| Final current-source restart method | 1 passed, 359.625 seconds |
| Fresh final callback plus persisted batch replay | 2 passed, 31.569 seconds |

[Exact commands and raw-log hashes](results-manifest.json), [XML results](test-results.json), [source/assembly hashes](final-consumer-bindings.json) and [all-nine dependency modes](dependency-modes.json) bind the evidence. The complete 889-case runs preceded the later smoke-only bounded parent-read, application-port-isolation and failure-diagnostic adjustments. Their selected methods are unchanged; both final builds, the 134-case focused checks and the final live executions cover the affected paths. The last change added only a timeout diagnostic to the restart method; no timeout, assertion or authentication gate was weakened.

The selected final live attempts remain separate:

- Restart XML/log: `/tmp/story415-review-20261017/live-sdk-diagnostic.{xml,log}`; raw phases: `/tmp/story415-review-20261017/live-diagnostic-state.jsonl`.
- Fresh callback/batch XML/log: `/tmp/story415-review-20261017/live-final-callback-batch.{xml,log}`; raw callback phase: `/tmp/story415-review-20261017/live-final-callback-state.jsonl`.

The [eleven redacted persisted phases](persisted-states.json) and their [raw-state bindings](runtime-bindings.json) prove exact revision-2 expiry, expiry receipt replay, canonical missing-child checkpoint/failure sequence 6/7 with degraded readiness and no sibling cancellation, five single target cancellations and receipts, source checkpoint 7 after replay, and one parent resume with a trusted NoOp receipt replayed before source checkpoint 4. Complete coverage retains eleven candidates with zero unresolved work. The [five-row matrix](matrix.json) names executed passing cases in both modes and the selected live phases.

A preceding isolated full-class attempt passed callback/batch and the expiry phases but timed out observing the delayed-child reason. Its XML/log, partial raw states and pre-diagnostic bindings remain separate. The diagnostic rerun observed and passed the exact missing-child transition under unchanged budgets, then completed every remaining phase; no cause is inferred for the preceding timeout. The root independently audited all eleven selected phases and the final selected XML exits in the [final root audit](root-runtime-audit.json). Its [earlier callback audit](root-runtime-audit-prior-callback.json) retains its original binding separately. Earlier fixture-construction failures and the zero-test filter mistake are recorded as preliminary evidence, never passing gates.

[Cleanup](cleanup.json) confirms no Works AppHost or current-run Redis fixture remains. The unrelated Redis container created on 2026-10-16 remains untouched. The owned read-only source observer was stopped. Whitespace validation passes with the repository's CRLF-aware command.

The fresh Redis/Dapr proof exercises the current Debug source graph. Release received the complete non-live suite and clean final build; a separate package-host live run and complete-store rollback were not rerun for these test/harness corrections. Their historical evidence retains its original producer scope. Platform parity, host removal, production authority/data admission, and the previously recorded SDK deferrals remain separate gates. No staging, commit, push, publication, deployment or submodule update occurred.

## Scoped review follow-up

Recovery now rejects undefined numeric `WorkItemTransitionRejected.FromStatus` values through the existing recovery-only predicate. The new runner cases persist quarantine at checkpoint 0/failure sequence 1 without effects for -1 and 999, while defined `Unknown`, `Created` and `Expired` checkpoint normally. The unchanged historical decoder is explicitly checked for every case.

The retained-plan helper now passes its existing linked four-minute deadline to store acquisition and bounds the returned task with that same deadline. The existing timeout diagnostic covers acquisition as well as the polling delay. A nonresponsive-store regression proves cancellation ends the helper even when the store ignores its token; no timeout was extended.

Only focused checks ran for these edits: the Debug build passed with zero warnings/errors, all 38 `WorksSdkIteration4Tests` cases passed, and the one retained-plan helper regression passed. Both XML results have zero failures/skips. [Current follow-up bindings](review-corrections-bindings.json) remain distinct from the earlier full-suite/live bindings; broader verification is left to the root workflow.

Every raw log, XML result and state stream referenced by the result/runtime manifests is now retained as a discoverable gzip artifact under `raw-evidence/selected`, `raw-evidence/preliminary` or `raw-evidence/supporting`. The [artifact index](raw-artifacts.json) preserves original absolute locations and manifest pointers, compressed and decompressed SHA-256 digests, byte counts and classification. The original provenance fields are unchanged; result/runtime entries additionally link their durable archives. All 47 compressed/raw hash pairs and their original bindings pass verification. Independent mutations of compressed bytes and decompressed contents are both rejected in [archive verification](archive-verification.json).

Run `python3 verify-raw-artifacts.py` from this evidence directory to verify the retained artifacts without consulting `/tmp`. The initial fixture failures, zero-test selection and failed live attempts remain separate from selected passing attempts. The original full-suite and live evidence is not relabeled as verification of the new decoder predicate.
