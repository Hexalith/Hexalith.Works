---
title: 'Close five deferred hosting verification items'
type: 'bugfix'
created: '2026-11-22'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem.** Works has five deferred checks around Platform hosting verification. A report can overwrite its proof inputs, the rebuild batch ceiling can pass without observations from staging, retired recovery tests need a scenario map, and runtime project inspection needs evidence for inherited pipes and support for evaluated output layouts.

**Approach.** Protect proof inputs on every report path, qualify bounded stage writes and recovered histories, map the retired recovery behaviors to their current owners and tests, execute the inherited-pipe control, and use MSBuild's evaluated output directory for the supported .NET 11 target. Record outcomes in the deferred-work ledger.

</frozen-after-approval>

## Implementation Notes

- Selected R29R-B8, R29B-D3, R30C-D2, and the two `spec-release-dependency-refresh-2026-11-16.md` runtime metadata entries. The work is limited to the Works verification harness, tests, and documentation.
- The proof CLI protects the resolved destination and the report directory's identity before atomically replacing an ordinary report. If input metadata cannot establish a safe destination, exit 2 identifies the missing fresh report. The CLI tests exercise source, configuration, links, malformed metadata, and redirected directories.
- The rebuilt tenant fixture measures successful writes between Stage and Commit. It compares their payloads with the finalized plan and compares every logical document and history after reconstructing the coordinator.
- The runtime metadata tests use actual MSBuild evaluations for standard, custom, and RID output paths. The pipe fixture asserts a normal launcher exit and bounded inspection while the child owns the redirected streams.
- Verification passed 45 tests via `python3 -m unittest discover -s scripts/tests -p 'test_platform*.py'`, 204 focused integration tests and 11 `TerminalCascadeTranslatorTests` using the compiled Debug test assemblies. Ruff and Git whitespace checks passed. The final rebuild case used a 16 MiB tenant and observed 416 bounded batches across its lifecycle.
- The integration build used the dependencies pinned by Works in a disposable checkout. Both the updated and pinned Platform source hit two existing CA2007 errors in `PlatformFixtureToolRelease.cs`. The disposable checkout configured the two async disposals for the focused build. Live Platform qualification is outside this verification result.
- The clean baseline `python3 references/Hexalith.Builds/check.py` stopped at the mandatory anonymous fetch of `delivered/Hexalith.Works` from Hexalith.CiCd. The direct anonymous Git diagnostic could not connect to GitHub on port 443.

## Review Triage Log

- **Patch.** Refuse writes when the protected scope cannot be established, flag prior reports as stale, and cover malformed ledgers and escaped auxiliary targets.
- **Patch.** Bind and recheck the output directory identity, with an executing directory-redirect control.
- **Patch.** Match successful Stage payloads to the plan and read the promoted documents by logical key.
- **Patch.** Map retired checkpoint, scheduling, cancellation, and remoting behavior explicitly. State the child-resume coverage at the translation boundary.
- **Patch.** Verify that the pipe fixture's launcher actually exits successfully. Record the five dispositions and test results.
