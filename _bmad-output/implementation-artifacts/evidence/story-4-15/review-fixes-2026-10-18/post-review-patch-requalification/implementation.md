# Story 4.15 deterministic requalification — 2026-10-18

All local code, test and documentation patches pass. Source Debug is freshly rebuilt and verified at EventStore `f43b121c6c7539487b1f38b3c28508e3ab6264cc`. The local EventStore tag `archive/works-c0ed219` preserves `575c8070355ff9b6afb17521d6eb529a85327e4d`. **Pushing that archive tag remains pending** because the active bmad-build step-03 prohibits remote operations. No staging, commit, push, remote query, package repack or dependency-pin change was performed. Story status remains in-progress for the enclosing review workflow.

## Changes

Recovery now uniformly requires the JSON `Sequence` member whenever the decoded payload contract declares it. Explicit zero remains accepted, and command-rejection contracts without `Sequence` keep their historical meaning. The compiled inventory contains fifteen sequenced state-changing event types. A reflection-backed completeness assertion compares every sequenced contract with the fixture inventory, preventing a future event from silently losing coverage. Thirty sequence absence/default rows and six other required-scalar rows execute through both the canonical reader and SDK runner. Missing values retain durable invalid-evidence quarantine, checkpoint zero, no pending plan and no submissions; present defaults keep historical reader and active-runner behavior. Historical decoding now asserts non-null, exact type and an exhaustive assertion branch. The depth-rejection theory explicitly accepts `ResultingDepth=1, MaxDepth=0`.

The adoption guide links the 865-case post-reopen-review packet and binds earlier restart/callback results to their recorded decoder and producer. It distinguishes the later current-source deterministic packet. Boundary/API documents attribute the additional read-back RPC diagnostic specifically to SDK source `575c8070` / its identical rebase `f43b121c`; the archived candidate DLLs do not contain that diagnostic. The reopened store contract is a subsection of the Story 4.15 producer history; Story 1.1 retains its original introductory context.

## Verification

| Lane | Passed | Failed / skipped / not run |
| --- | ---: | --- |
| Source Debug focused recovery class | 87 | 0 / 0 / 0 |
| Source Debug integration, SmokeTests/Platform excluded | 887 | 0 / 0 / 0 |
| Source Debug architecture | 269 | 0 / 0 / 0 |
| Source Debug unit | 589 | 0 / 0 / 0 |
| Source Debug property | 3 | 0 / 0 / 0 |
| Unchanged candidate Release integration, same exclusions | 887 | 0 / 0 / 0 |

All accepting builds report zero warnings and errors. The first integration build found six test-compilation/analyzer errors: C# 14 treats `field` as a keyword inside a property accessor, and the new externally visible test parameter required a null guard. Both were corrected before the focused and full passing runs. Its exact failed log is retained separately as `debug-build`; `debug-build-final` is the accepting source build.

The Debug dependency graph contains all nine EventStore libraries as projects. Release explicitly selects unpublished `3.124.1-story415.20261018.1` with `WorksAllowLocalEventStoreCandidate=true`; its graph contains all nine packages. Every restored SDK consumer DLL matches its candidate archive DLL, and all nine archive hashes are unchanged before/after verification. This does not qualify default public 3.124.0, which remains unchanged and lacks the earlier journal-closeout correction.

All 65 existing deterministic matrix cases pass in each mode, giving 130 checked observations. Each mode also runs the complete 36 scalar-presence cases, the inventory assertion and all ten failure-condition rows including explicit-zero `MaxDepth`. [report.json](report.json) retains exact commands, test counts, 2,580 source/build/document hashes, selected dependency graphs, consumer assembly hashes and compressed raw XML/logs. Its changed-source snapshots and raw archives preserve both compressed and original SHA-256 values.

## Baseline and runtime scope

The required unchanged-AppHost inspection preceded edits. `aspire ps --format Json` initially returned `[]`; exact-target `aspire start` exited 0. Resource inspection found Sentry, Placement, Scheduler and Keycloak healthy, EventStore Running/Unhealthy, and Works Waiting. The requested 30-second Works wait ended with exit 18 and the CLI's failed-start result. The exact-target host stop exited 0 before editing; final `aspire ps` returned `[]`. This inspection makes no Works-health or live-acceptance claim. Raw start/description output containing ephemeral environment/dashboard credentials is excluded; the packet retains only safe resource state and the wait diagnostic.

No current-source restart or callback was rerun. The approved deterministic-only closure defers live requalification at the final decoder/current SDK to Story 4.16. Historical live packets keep their recorded decoder/producer bindings. No upstream-caused deterministic failure occurred. Publication, corrected public-pin qualification, production authority/admission, Platform parity and transitional host removal remain separate gates. Original baseline `8679d7d700925a870e9931b3c43c745c063e459b`, review iteration 4 and frozen intent are unchanged.

Run `python3 verify.py` to check the portable packet, or `python3 verify.py --current` to compare its source/producer/archive bindings with this checkout. The portable verifier is also exercised from a copy outside the repository. Retained artifacts were checked for private-key PEM blocks and compact JWT credentials; none were retained.
