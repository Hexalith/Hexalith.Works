---
title: Retain safe diagnostics for the failing FrontComposer invalid-bearer proof
type: bugfix
created: '2026-11-01'
status: done
route: dispatch
review_loop_iteration: 0
baseline_commit: 98a382a33517084564ecaad3eb4ca313a728ef62
context:
  - /home/administrator/projects/hexalith/works/references/Hexalith.FrontComposer/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-git-instructions.md
---

<frozen-after-approval reason="User authorized fixing CI failures and waived human validation; no automated authentication assertion is waived">

## Intent

FrontComposer Quality fails its real invalid-bearer probe because all advertised EventStore endpoints time out. Cleanup currently deletes the second startup's diagnostics, so the underlying transport failure cannot be determined. Retain bounded, safe failure evidence before verified owned cleanup, then use a fresh hosted run to identify the actual defect.

## Boundaries & Constraints

Always require completed401/403 on all four original HTTP routes. Preserve the existing two-pass transport bound, all curl timeouts, refusal of other HTTP statuses and partial responses, exact AppHost ownership checks, lock ownership and cleanup. Diagnostics may describe only this verified owned invocation. Keep private scratch data private and delete it after producing sanitized evidence. Prefer allowlisted structural/numeric diagnostics over retaining arbitrary raw strings.

Never publish credentials, headers, bodies, raw environment/discovery data, arbitrary curl stderr, authentication tokens or private URLs. Never broaden retries/timeouts, accept transport failure as authentication proof, start/stop unrelated resources, alter production auth, initialize nested modules, or claim this diagnostic change fixes the underlying availability defect.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Completed denial | Four actual401/403 responses | Existing successful result and owned cleanup | Same assertions |
| Transport failure | Current all-endpoints curl28/000 | Nonzero result plus bounded actionable owned diagnostics | Retain safe evidence before deletion |
| HTTP/partial failure | Existing200/302/404/500 or partial401 | Original nonzero failure | No fallback success |
| Secrets in tools | Fixture credentials/private URL in startup/discovery/logs | No secret in output or retained artifact | Reject/omit unsafe fields |
| Ownership/cleanup failure | Changed/missing PID, failed stop, signals, lock contention | Original refusal and no unowned stop | Diagnostics cannot conceal original failure |

</frozen-after-approval>

## Code Map

Owner is the clean root-declared `/home/administrator/projects/hexalith/works/references/Hexalith.FrontComposer` checkout. Read its .editorconfig/.gitattributes, helper tests and Quality configuration before edits. The root AI Tools baseline is permitted by Works' declared root dependency. Do not load skills under references.

- `eng/verify-apphost-invalid-bearer.sh`: owns isolated start, exact PID checks, waits, endpoint discovery,16 bounded probes and cleanup. Its cleanup deletes all private startup/discovery/wait/curl data. Capture diagnostics before stop only after ownership is freshly established, and retain cleanup outcome afterward. Use bounded read-only CLI diagnostics with existing supported command syntax, never a second start.
- `eng/run-epic9-live-proof.sh`: existing redaction and bounded startup diagnostic patterns; read/reuse appropriate technical behavior without sourcing its executable or copying secrets into artifacts.
- `tests/e2e/scripts/verify-apphost-invalid-bearer.test.mjs`: existing Node fake tools already inject private-bearer/private-password/private-token and hostile URLs; preserve all original cases and exact cleanup/lock controls. Extend genuinely necessary failing-path and artifact confidentiality assertions.
- `.github/workflows/quality.yml` invalid-bearer step and Epic9 artifact upload around lines914–937: upload only the new sanitized diagnostic destination with existing evidence. Existing Counter-Web artifacts prove a different standalone fake-auth demo and contain no second-start transport diagnostics.
- Source Program.cs Counter-Web branch explicitly bypasses EventStore. Quality41334030457/job124930155126 has16curl28/000 results and zero HTTP responses; artifact13095515072 contains no missing diagnostics. Do not infer an authorization regression or a definite proxy/backend cause from it.

## Tasks & Acceptance

- [x] Add bounded safe owned-start/resource/transport diagnostics and verified cleanup outcome to the helper, using a small separate helper file if needed for clear parsing/redaction.
- [x] Retain numeric connection/TLS/first-byte timing and peer classification without credentials, response contents or arbitrary stderr; preserve probe outcome parsing.
- [x] Wire the sanitized destination into the existing always-uploaded Quality artifact and preserve historical evidence.
- [x] Run existing and new Node helper tests, shell syntax and relevant workflow checks; report complete diff, paths, commands, results and remaining cause uncertainty.

Given a transport-only failure, when the helper exits, then its result remains nonzero and safe evidence survives verified cleanup. Given any injected secret or changed ownership, when diagnostics run, then no secret is retained and no unowned resource is stopped. Given completed401/403, when original tests run, then behavior remains unchanged.

## Implementation Notes

Parent owns Git/remote operations, review, real hosted retry and eventual underlying correction. Implement only the owning FrontComposer files and this spec. No native/container starts, deployment or production calls; Node/bash focused checks are authorized. No dependency or unrelated AppHost changes. Report missing diagnostic sources honestly instead of inventing observations. This is a diagnostic prerequisite, not final live authentication or release acceptance.

## Spec Change Log

## Review Triage Log

## Verification

Run `node --test tests/e2e/scripts/verify-apphost-invalid-bearer.test.mjs`, `bash -n eng/verify-apphost-invalid-bearer.sh`, and relevant existing script/workflow checks. Preserve original tests and reconcile new test counts. Parent will inspect the complete diff and obtain independent review before push, then require a fresh real CI attempt without relaxing any authentication assertion.

Implementation verification (2026-11-01), run from `references/Hexalith.FrontComposer`:

- `node --test tests/e2e/scripts/verify-apphost-invalid-bearer.test.mjs`: **53 passed, 0 failed, 0 skipped**. Preserves all 42 original cases and adds 11 cases covering persisted evidence, hostile metric/resource values, malformed curl records, unavailable/malformed/oversized/hanging diagnostic sources, ownership changes during diagnostics, historical evidence and a missing PID. Every fake-tool invocation checks both console output and the retained artifact for injected secrets, verifies private scratch deletion and checks artifact permissions.
- `node --test tests/e2e/scripts/validate-epic9-artifacts.test.mjs tests/e2e/scripts/run-epic9-live-proof.test.mjs`: **144 passed, 0 failed, 0 skipped**.
- `bash -n eng/verify-apphost-invalid-bearer.sh`: **passed**.
- `actionlint .github/workflows/quality.yml`: **passed**.
- `git diff --check`: **passed**.
- Additional existing check, `python3 tests/e2e/scripts/verify-isolated-readiness.test.py`: **77 passed, 1 environment error, exit 1**. `test_input_identity_includes_and_refuses_dirty_transitive_polymorphic_source` raises `FileNotFoundError` for `references/Hexalith.FrontComposer/references/Hexalith.AI.Tools/hexalith-git-instructions.md` under the Works workspace. No nested dependency was initialized or changed to bypass this blocker.

The complete FrontComposer implementation diff, including the new untracked sanitizer, was captured and read from `/tmp/fc-invalid-bearer-diagnostics-l0aw42iv.diff`. Changed owning paths are `eng/verify-apphost-invalid-bearer.sh`, `eng/sanitize-invalid-bearer-diagnostics.jq`, `tests/e2e/scripts/verify-apphost-invalid-bearer.test.mjs` and `.github/workflows/quality.yml`.

The helper retains unique `artifacts/invalid-bearer/diagnostics.*.json` records. They contain startup/readiness outcome codes, allowlisted resource state and endpoint classifications, fixed log-signal counts, bounded numeric probe timings and peer classifications, the original verification exit code and the final verified cleanup/lock outcome. Diagnostic commands are restricted to the freshly verified owned AppHost, bounded to 10 seconds plus a 2-second termination grace and 128 KiB per raw output file, with log reads limited to 40 records. Original curl connection/request limits and two-pass behavior remain unchanged. A full capped file is explicitly unavailable evidence even when its tool exits zero.

Matrix audit: completed 401/403, all-transport failure, disallowed HTTP/partial responses, injected credentials/private URLs, changed/missing identity, stop failure, signals and lock contention all have passing helper coverage. The standalone Counter-Web evidence remains unchanged and separate from the new diagnostic destination.

No native/container AppHost start, production call, hosted retry, Git mutation or dependency update was performed. Independent review, the fresh hosted Quality attempt and any underlying availability correction remain with the parent task. These local diagnostic tests do not establish a transport cause or live authentication/release acceptance.


## Parent Review1 Triage

All three layers returned final before triage. Every finding has an individual verdict; this is a patch-only route. Parent independently passed all53 helper tests before patches.

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | medium | patch | The newly added final ownership refresh calls unbounded capture_processes after bounded diagnostics. Reuse the existing bounded owner-check path and test a hung final discovery; preserve refusal of unverified stop. |
| blind-2 | medium | patch | The launch contract exposes the actual detached log through logFile; launch stdout primarily contains CLI metadata. Retain a bounded allowlisted summary of the verified invocation-owned log using existing ownership/redaction patterns, with explicit unavailable status rather than arbitrary file capture. |
| blind-3 | medium | patch | Invocation-local startup stdout is safe to summarize independently of a live resource. Its current capture sits only inside the live-owned branch, so exited failed starts lose these observations. Move this small private-output summary outside that branch; keep resource/log reads bound to verified ownership. |
| blind-4 | low | patch | After final ownership mismatch, cleanup marks ownership-changed while diagnosticsOwnership retains verified. Set the existing ownership outcome to rejected/unavailable as appropriate without discarding already valid capture-time facts. |
| blind-5 | low | patch | An exited resource loses its numeric process exitCode. Retain a bounded validated integer resourceExitCode separate from the diagnostic CLI exit code, with a focused exited-resource fixture. |
| blind-6 | low | patch | Aspire logs expose isError as a safe boolean. Add the bounded stderr-record count so unrecognized error text is not indistinguishable from no error; retain no raw messages. |
| blind-7 | low | patch | A failed artifact copy can leave a partial upload-matching JSON file. Publish through a private pending filename outside the upload glob and atomic completed-file operation; remove failed pending files and preserve historical artifacts. |
| edge-1 | medium | patch | Independent reproduction of blind-1: the new post-diagnostic unbounded aspire ps can block cleanup/lock/artifact removal. Same bounded final owner-check correction; verdict retained independently. |
| verify-1 | medium | patch | Pre-verified regression gap: no test asserts startup.consoleSignals, so replacing the startup sanitizer with an empty object would pass. Emit recognizable startup failures alongside private text and assert numeric counts/confidentiality. |


## Parent Completion

All three review layers returned final and nine findings were individually triaged. The same implementation owner applied direct patches; parent independently ran all67 helper cases successfully with no skips, plus shell syntax and whitespace checks. The earlier144 other Epic9 Node cases remain passed and untouched by patches. Source commit ee014e5982ab8364d91d404d8bc45aa41ef3486f is pushed after normal rebase, preserving incoming ac7ed9f7491f43b43a57fead472c7e20d295553c source-version selection fix and7fd0d09f36e38dfeb044bf54402cf360fe7a11b6 docs. This closes only the diagnostic prerequisite; fresh hosted CI and actual live authentication remain separate parent-owned gates. Full parent log: /tmp/works-front-invalid-bearer-parent-patched.log.

## Completion Evidence

The reviewed diagnostics change was pushed as ee014e5982ab8364d91d404d8bc45aa41ef3486f after preserving upstream source-graph fixes ac7ed9f and 7fd0d09. Current follow-up07089dcbdad784a1d53037e9cea78d1d7becd8d6 preserves those four source files; CI41346492563 and the formerly failing live acceptance job124967478786 passed. No transport error was reclassified as an authentication response.
