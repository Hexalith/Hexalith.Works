# Story 4.15 final post-review verification

Story 4.15 is implemented and independently reviewed. All eight final patch findings are resolved and all selected verification gates pass. The selected immutable local producer is **3.122.0-story415.20261016.9**; it is **not published**. Frozen intent, the approved authenticated-origin scope, user Story 4.9 changes and earlier positive proofs are preserved. No staging, commit, push, publication, deployment, nested submodule initialization or real-data admission occurred.

## Producer and dependency modes

The verified published base remains EventStore 3.122.0, published source `1dac529c4690910c89d4090784a7d46cb9433a9d`, with checked-out producer baseline `6394d0500e6e277b8975cb397c9f0389f9a16929`. [Provenance](producer-provenance.json) binds nine fresh .9 archives and 66 changed producer files; [consumer bindings](consumer-files.json) bind 73 files. All [72 previous .1–.8 archives](historical-archives.json) match their retained hashes. Platform.Identity remains the separate Story 4.14 test candidate `1.0.0-story414.20261014.5`, and Parties.Client remains published `1.2.1`.

[Final dependency graphs](dependency-modes.json) establish all nine SDK source projects in ordinary Debug and all nine .9 SDK packages in ordinary Release. The source-only Contracts CPM exemption, other transitive pins and sibling policies remain intact. The final default Debug rebuild and narrow contract check normalize outputs after metadata-only runtime builds; the final ordinary Release build and current-source Works ABI probe normalize the restore drill's explicit MinVer override. Runtime inputs are preserved separately before those normalizations.

## Final review corrections

- Recovery-only validation rejects empty suspension sets, foreign qualified event names, and missing/non-boolean rejection discriminators before effects or decoded no-op acknowledgment. Actual Works full/short names, explicit historical booleans, singular suspension input and optional null expiry remain accepted. Legacy/4.14 decoders are unchanged.
- Persisted reminder entries now apply the existing optional committed-message rule: null remains historical, valid IDs remain usable, and whitespace receives durable stored-entry quarantine across reconstruction.
- The extended bounded live fixture proves delegated parent-to-child expiry with a child that has a later persisted schedule witness. The witnessless cascade uses its distinct purpose, commits once, loses its response, and replays the target receipt before advancing the parent checkpoint. Existing cancellation, creation-gap, child-resume and eleven-source proofs remain intact.
- An admitted trusted effect executes the actor's qualified-snapshot path. SnapshotRecord sequence 11, its accepted event, aggregate metadata and receipt appear in the same committed batch; replay appends no second event.
- SDK handoff/command prose now matches the current actor, protected mappings and witnessed expiry. The pack helper reports actual checkout/working-tree provenance and refuses immutable archive reuse.

[Targeted pre-.9 evidence](../review-iteration-4-patches/README.md) preserves the earlier source proof and exact 150 input files. It is explicitly distinct from .8 package acceptance and the final .9 gates here. All thirteen findings have individual verdict/evidence rows in the spec before grouping. Repeated findings retain their prior decisions. The pre-existing SDK synchronous-cancellation and oversized-registry validation issues were appended to the [deferred ledger](../../../deferred-work.md); the carried population-latency benchmark was not appended again. No new design loopback or frozen-intent gap was required.

## Verification

[Results](results-manifest.json) record exact commands, raw output paths, hashes, exit results and timing for **27 unique passing gates**. Every selected build has zero warnings/errors. Every selected test gate has zero failures, skips or unrun cases.

| Gate | Passing result |
| --- | --- |
| SDK DomainService full suite | 962 |
| Trusted-effect security | 85 |
| Qualified snapshot migration | 13 |
| SDK client control-index/epoch | 37 |
| Source Debug integration (`-class- '*SmokeTests'`) | 751 |
| Package Release integration, same selection | 751 |
| Unit / property / architecture | 576 / 3 / 269 |
| Enabled Redis/Dapr runtime | 2 |
| Complete-store projection/rollback restore | 27 plus 1 |
| Final normalized Debug contract check | 2 |
| Cold consumers and ABI | Nine .9 archives, package-only host/API probes, unchanged published SDK binary, unchanged baseline Works binary |

Both unchanged binaries retain their original constructor/deconstruction contracts and historical hashes. The Works ABI helper builds current selected Contracts before substitution; additive fields also pass actual polymorphic round-trips. Earlier failed attempts remain honestly retained in the linked iteration-4 and targeted-patch preliminary evidence; none is relabeled as final acceptance.

## Durable runtime proof and matrix audit

[Runtime bindings](runtime-manifest.json) preserve exact raw logs, all ten raw phases and the resource capture, plus 139 source and eleven binary/graph inputs in an immutable temporary archive. [Independent assertions](root-runtime-check.json) verify every phase. [Review states](persisted-states.json) redact seventeen payload/sensitive fields case-insensitively, including nullable receipt payloads. [The exact Works AppHost capture](live-resources.json) has all sixteen resources Running/Healthy.

The callback-only phase persists an armed expiry witness and process candidate with periodic recovery disabled. Expiry response loss retains parent checkpoint 3 and one CascadeExpire effect after a single child expiry; restart observes a Success receipt under `works.cascade-expire`, advances to checkpoint 4 and clears the plan. The child's actual stored schedule remains later than the cascade, and its committed cascade event has no scheduled witness.

The canonical missing-child gap retains parent checkpoint 6/failure 7, no plan, zero sibling cancellations, unresolved evidence and Degraded readiness. After separate child creation, a target commits while the parent retains all five effects at checkpoint 6. Restart yields five single cancellation events, five Success receipts and checkpoint 7 without a plan. An ordinary competing resume supplies the sole domain resume event; the trusted NoOp receipt loses its response at child checkpoint 3, then replay advances to checkpoint 4 and clears the plan. Success disposition 0 remains distinct from NoOp disposition 2. Final coverage is complete with eleven retained sources and zero unresolved work.

| Approved matrix row | Executed passing coverage |
| --- | --- |
| Duplicate/lost firing, restart, same-date re-suspension | SDK reminder suite, Works intent translation and enabled runtime |
| Reschedule/stale expiry | Pure-domain unit cases, protected HTTP, persisted transition cases and real scheduled-parent expiry |
| Cascade/child-resume target-commit crash | Both real cascade purposes and trusted no-op receipt replay before source checkpoints |
| Page boundary/corrupt or foreign evidence | Source/SDK process suites, closed event-shape tests, partial discovery and durable invalid evidence |
| Related-read outage/denied submission | Canonical missing-child repair, typed retry/parking, protected HTTP and audited trusted security |

[Root bindings](root-bindings.json) independently verify **405 current-file, log, archive, graph, runtime-input and cleanup bindings with zero mismatches**. This includes all 150 archived runtime inputs, not 405 distinct source files. [Cleanup](cleanup.json) confirms no owned AppHost or synthetic Redis container remains. Shell syntax and CRLF-aware diff checks pass in both owning repositories. The frozen intent hash remains unchanged and the user Story 4.9 diff is preserved exactly.

## Remaining gates

No approved implementation or patch remains incomplete. Public publication, production delegation/retention/expiry policy, real-data admission, Platform parity, transitional host removal and Story 4.16 remain separate gates. Permanently noncooperative providers retain finite admission until actual completion. The three recorded deferred SDK concerns remain separate work. Rollback continues to require the complete matching store and binaries.
