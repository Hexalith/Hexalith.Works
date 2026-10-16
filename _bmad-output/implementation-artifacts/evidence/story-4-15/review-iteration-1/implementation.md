# Story 4.15 review iteration 1 implementation evidence

The approved frozen intent is unchanged. Both frontmatter context files were read completely; the two KEEP bundles were SHA-256 verified and restored once before corrections. Their restoration instruction is now historical. User Story 4.9, sprint and planning edits were preserved. No staging, commit, push, publishing, submodule initialization, production deployment or real-data admission occurred.

The selected producer is immutable local **3.122.0-story415.20261015.4**, based on verified published 3.122.0 and checked-out source `6394d0500e6e277b8975cb397c9f0389f9a16929`. It is not published. [producer-provenance.json](producer-provenance.json) binds all nine archive hashes and 43 changed producer files. Archived `.1`/`.2`/`.3` packages and `.3` evidence remain unchanged. Platform.Identity retains the separately versioned Story 4.14 test candidate; Parties.Client remains published 1.2.1.

## Corrections

- BH1/EC1: persisted reminder quarantine recognizes produced `source-message-invalid`; repeated convergence and reconstructed coordinators preserve the same reason, digest, timestamp and record count.
- BH2: Works selects a finite five-effect, 15-second source-quantum profile. The SDK validates `Q >= (4 + 5F) × T`, including the new plan CAS, and the existing coupled population/freshness inequality. Five direct children converge; six quarantine the entire terminal envelope before any target submission, retaining its source failure position. Explicit inconsistent overrides fail validation.
- BH3/BH4: both contextual and process adapters enforce metadata V1 and payload version null/1 before decoding no-ops or effects, accepting JSON case-insensitively.
- BH5/VG2: additive SDK `ProcessSourcePage.IsIncomplete` permits validated healthy membership while retaining durable unresolved discovery. SDK process/reminder sweep cursors survive restart and restart at the head after available membership ends; healthy additions before an old cursor remain discoverable. Reconstructed Works catalogs cover identity conflicts, parking, mixed tenants and offboarding without resurrecting excluded scopes.
- BH7: the SDK durably retains a copied, bounded original `ProcessEnvelopePlan` inside its source CAS state before submission. Replay validates immutable source semantics and current effect/byte limits, acquires fresh delegation and replays every terminal target receipt before checkpoint. The checkpoint clears the plan atomically. Works no longer reconstructs plans from target event causation; its translator retains exact current await validation.
- BH9: contextual reads charge every encoded envelope against a whole-stream 4 MiB budget before decoding/retaining domain context, with validated 1 KiB..16 MiB configuration. Raw payload and metadata sizes are bounded before serialized sizing. One SDK whole-read deadline covers pages and decoding (500 ms default, 100 ms..30 seconds). SDK `ReadWithAdmissionAsync` retains each of sixteen provider slots through actual completion, even after timeout or cancellation; a seventeenth abandoned read is refused without invoking its provider. Byte/time/admission exhaustion is retryable, leaves no partial context or checkpoint advance, and recovers through the same validator.
- BH10: ordinary and trusted clients share one validated effective address; blank Dapr/configured addresses follow the same fallback.
- VG1: the new enabled Redis/Dapr callback fixture uses real protected delivery, registrar and state store with both periodic reconcilers disabled; it must persist a reminder witness and process membership before acknowledgment is counted as accepted.

The SDK retains mechanics, codecs, receipts, quarantine, readiness and periodic recovery. Works retains authoritative contextual reads and pure domain translation. Additive expiry witnesses, authenticated trusted origin and its security boundaries, historical serialization/hash goldens, envelope coordinates, fresh delegation, exclusive composition, protected callbacks/disposition, complete rollback and legacy default are preserved.

## Executed verification

All corrected-state gates below passed. [results-manifest.json](results-manifest.json) retains each exact command, original log path, SHA-256 and sanitized result summary. [runtime-manifest.json](runtime-manifest.json) separately binds the final live outputs, unchanged callback proof and preserved preliminary failures. Prior `.3` proof remains historical.

| Gate | Result |
| --- | --- |
| SDK DomainService full suite | 927 passed, zero skipped |
| SDK trusted actor/admission/retention/controller security | 67 passed, zero skipped |
| Works source Debug integration excluding live Smoke classes | 688 passed, zero skipped |
| Works package Release integration excluding live Smoke classes | 689 passed, zero skipped |
| Works unit / property / architecture | 573 / 3 / 269 passed, zero skipped |
| Complete Redis/Dapr rollback restore | 27 plus 1 passed, zero skipped |
| Final five-child/NoOp three-AppHost runtime fact | 1 passed, zero skipped, 353.143 seconds |
| Unchanged callback-only runtime fact | Passed in the retained two-fact green run, zero skipped, 297.430 seconds |
| Debug, Release, SDK and security builds | Succeeded with zero warnings/errors |
| Immutable producer and cold package-only consumers | Nine `.4` archives; isolated host and Contracts/Client/DomainService probes passed |

[persisted-states.json](persisted-states.json) retains sanitized persisted evidence. The first target cancellation commits before the parent checkpoint while the SDK retains all five effects at checkpoint 6. Restart proves exactly one cancellation event and a persisted Success receipt for each of five children, then parent checkpoint 7 with no retained plan. The competing ordinary resume emits the sole logical resume before the trusted command commits its NoOp receipt; the lost receipt leaves the original plan retained before child checkpoint 4. A second restart observes actual receipt replay and clears that plan with checkpoint 4. The complete SDK sweep retains exactly eleven supported sources and zero unresolved work. The independent callback proof persists a reminder witness and process candidate with both periodic reconcilers disabled. Receipt disposition 0 (Success/event) and 2 (NoOp/no trusted event) remain distinct.

The final live log is `/tmp/story415-remediation-live.log`, SHA-256 `3b8f0291eb96bd2a0def1f1a5730bbefc637cf81e114ebe024d12b400083b26a`; its raw persisted-state log is `/tmp/story415-remediation-live-state.jsonl`, SHA-256 `3a92846e490b5454bf694e960e4411670991a97b011f9c24f32a294a02c65966`. All final/preliminary live result and state hashes are in the runtime manifest. [consumer-files.json](consumer-files.json) binds the 62 changed consumer source/test/script/documentation files. All 43 producer source hashes and nine immutable archive hashes were rechecked unchanged after the final test-only fixture edit.

Final cleanup found no registered AppHosts (`aspire ps --format Json` returned `[]`) and no owned Story 4.15 or rollback Redis containers.

Known temporary validation issues were corrected before final gates: the five-child fixture initially ignored the SDK requested page size; a translator-only replay expectation was moved to SDK plan/receipt coverage; null protection metadata retains historical acceptance; exact quantum fixtures now charge the retained-plan CAS; shell verifiers preserve LF line endings. A concurrent local build attempt caused a shared output file lock and was replaced by sequential builds. No build gate was weakened.

The five-child live refinement retains both preliminary 503 setup failures and the deliberately cancelled diagnostic-only attempt. The ordinary-command retry is confined to the synthetic Story 4.15 fixture: it buffers the exact request bytes once, preserves message ID and authentication on every attempt, permits at most three retries of HTTP 503, honors the server’s finite 30-second Retry-After, and bounds the enclosing request to two minutes. Sanitized resource diagnostics on the first 503 establish the concrete cause: EventStore's `sidecar:verified` rate limiter returned HTTP 429 to the Dapr AggregateActor invocation, and `DaprSidecarUnavailableHandler` mapped that nested `DaprApiException` to the external 503. [admission-diagnostic.json](admission-diagnostic.json) retains the trace. The limiter and finite process profile remain unchanged; the contract-correct 30-second backoff recovered the exact request. These preliminary setup failures do not indicate unavailable delegation authority or a failed SDK receipt replay. Shared legacy harness behavior is unchanged. The callback-only fact remains covered by its retained final green run.

## Remaining gates

Public producer publication, production delegation/retention/expiry policy, real-data admission, Platform parity, transitional host removal and Story 4.16 remain separate gates. Permanently non-cooperative providers retain their bounded SDK slots until they actually finish; they require provider repair to restore capacity. BH6's larger-population catalog latency remains deferred without a benchmark; the documented synthetic supported population is finite. Complete rollback restores all Works family generations, SDK reminder discovery/witnesses, process source/sweep/plans/disposition and target receipts together with matching binaries.
