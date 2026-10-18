# Story 4.15 post-reopen patch verification — 2026-10-18

All six remaining direct review patches are implemented. This addendum preserves the original candidate packet and records fresh deterministic evidence for Works `110a912` and SDK `1aaa173715305a08aff30c4b9cd6354a7073e541` plus the changes snapshotted in [report.json](report.json). The report preserves full Git identifiers, exact command arguments, compressed raw logs/XML and their compressed/uncompressed hashes, source snapshots and assembly/dependency bindings. Original `/tmp` paths are command provenance; all retained verification inputs use packet-relative paths.

## Changes

- Release package contracts compare the selected SDK version with separately emitted static public pin `3.124.0`. `WorksAllowLocalEventStoreCandidate=true` explicitly admits only a `-story415.` version. An architecture assertion keeps this static metadata synchronized with both repository pins. Default package verification passes; selecting the unchanged candidate without the opt-in fails at this guard, as intended.
- Recovery additionally requires `WorkItemTreeDepthExceeded.MaxDepth` and `WorkItemExpired.Sequence` / `WorkItemCompleted.Sequence`. Reader/runner cases cover a MaxDepth-only omission and absent/present terminal sequences, preserving explicit zero, case-insensitive binding and historical readers. Invalid records retain quarantine/checkpoint zero and never submit effects.
- SDK typed, raw and TTL create failure regressions prove original failure preservation for absent proof, read failure, caller cancellation and update attempts; cancellation during read-back remains cancellation. The strong read-back charges exactly one state RPC, including failed probes, with persisted bytes unchanged on a proved conflict.
- Boundary/API/adoption documents identify reopened source/candidate evidence, the unfixed default public package, the store contract and the candidate's three unpublished base commits. Public 3.124.0 is the faithful-replay control; the same-source SDK regression isolates the correction's causal effect.

## Verification

| Lane | Passed | Failed / skipped |
| --- | ---: | --- |
| SDK changed store class, source Debug | 30 | 0 / 0 |
| Full current SDK Client | 1,666 | 0 / 2 |
| Works focused recovery/reader/intent/package classes, source Debug | 83 | 0 / 0 |
| Works full non-live integration, source Debug | 853 | 0 / 0 |
| Works architecture, source Debug | 269 | 0 / 0 |
| Default public 3.124.0 Release package contracts, without opt-in | 2 | 0 / 0 |
| Unchanged candidate Release integration, explicit opt-in | 853 | 0 / 0 |
| Negative candidate override without opt-in | 1 | 1 expected / 0 |

Every build completes with zero warnings and errors. The expected negative result is exactly `ReleaseConsumerLoadsSelectedSdkPackagesAndPartiesContracts` rejecting the candidate selection against static public `3.124.0`; it is separate from all accepting lanes. The two SDK skips are upstream `PendingBatchRecoveryLiveTests.RestartRecoversInterruptedConditionalBatchAndPreservesLaterSharedIndex` rows, each reporting `Requires isolated Dapr endpoints.` They do not cover the changed adapter tests and are not claimed as passing live proof.

The source Debug graph contains all nine SDK project libraries. Default Release binds all nine public 3.124.0 packages and the official Client hash `c17fe1d05477308e5ff61d9dc6a7c5fac686c310e923190f07ec0130ac8a49d5`. Explicit candidate Release binds all nine unchanged `3.124.1-story415.20261018.1` packages and Client hash `ebbe78ff670eeb4caca68cbbee0c86458ea685738e9e6fbf00da20b9aaea982d`. The nine feed archive hashes are unchanged before and after verification. The candidate was not repacked; the new RPC diagnostic is verified in source Debug only, not attributed to those archived candidate DLLs.

## Runtime scope and cleanup

Before edits, `aspire start --non-interactive --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` exits 0. Exact-target `aspire describe ... --format Json` observes healthy Sentry, Placement and Scheduler; applications are waiting on initially unhealthy Keycloak. Keycloak logs complete startup. `aspire wait works --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj --timeout 30` exits 17 after 30 seconds. No final Works-health claim is made. Exact-target `aspire stop --non-interactive --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` exits 0 before editing. Unrelated AppHosts are preserved.

No new live restart or callback proof was run for current SDK `1aaa1737`, which contains upstream changes after the separately requalified `216b7906`. Earlier live acceptance remains historical and bound to its recorded source/candidate. Current deterministic lanes have no upstream-caused failures. Publication, default-pin qualification, production admission, Platform parity and host removal remain separate gates. No dependency pins, Git history or immutable earlier evidence was changed.

Run `python3 verify.py` from this directory, or pass its full path from elsewhere, to verify the portable packet. `python3 verify.py --current` additionally requires every recorded current source file to retain its capture-time hash; later source/spec edits may intentionally invalidate that optional comparison. The report records a retained-artifact scan with no private-key PEM blocks or compact JWT credentials.
