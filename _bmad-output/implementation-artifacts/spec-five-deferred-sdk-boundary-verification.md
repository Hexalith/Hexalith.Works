---
title: 'Close five deferred SDK boundary verification gaps'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five distinct deferred findings: terminal-event recovery handler composition; test-host builder disposal; query denial observation amid background reads; SDK actor route mapping; and projection failure accounting after successful replay. Exercise current SDK replacements, fix the reachable fixture-configuration disposal leak, and close the five original ledger findings with precise replacement evidence. Preserve historical evidence and independent outstanding SDK/production policies. Independently review, verify, commit and push main as requested.

</frozen-after-approval>

## Implementation Notes

- Clean main baseline is captured directly from Git in the evidence packet. Read root guidance, required LLM/Git/state instructions, EditorConfig, build/test configuration, architecture AD-18/19/20/27 and current SDK adoption/cutover documentation. User explicitly authorizes the five-item batch and main push. This is a small test-harness/consumer-verification change: no unresolved intent choice, new production API, dependency change, data migration, deployment or publication. Use oneshot.
- Code map: `WorksSdkRecoveryCompositionTests.cs` resolves every sequenced handler already; add explicit cancellation, expiry and completion execution with retained process candidate/checkpoint and reminder convergence assertions. Use real production host registration and serialized SDK persistence.
- `WorksAppHostSmokeHarness.cs` disposes app and builder, but captures/configuration/logging precede its cleanup try. Move all post-creation preparation into the owned cleanup boundary; add deterministic failure-path controls through a minimal reusable test-harness seam if needed. Preserve primary exceptions and report disposal failure. The retired topology fact itself was removed by the Platform cutover.
- `WorksSdkProtectedHttpTests.cs` already counts all store interfaces only in an AsyncLocal HTTP request scope. Add a gated simultaneous background-read/control request to prove actual middleware isolation and a positive request-read control. Qualify actual SDK actor endpoint registration, protected rejection and configured actor discovery without a real sidecar.
- `WorksSdkProjectionQueryTests.cs` and SDK coordinator journal receipt/failure state supply replay controls. Exercise a bounded transient fold failure, successful real Works fold, reconstruction and identical duplicate receipt; assert persisted failure removal, no parking, stable selected documents and no refold. Also require a new delivery's failure accounting to start independently. Do not change immutable-evidence or parking/reset policy.
- Ledger targets: the unstructured three-terminal-handler composition bullet, unstructured topology-builder disposal bullet, unstructured QueryWindowStore post-start observation bullet, unstructured MapActorsHandlers SDK mapping bullet, and structured projection-parking reset question. Append dated status/resolution fields to these five original findings only; duplicates are not counted. Preserve every original ledger byte.
- Validation: Debug Integration build, direct xUnit class filters and deterministic Integration lane excluding live Smoke/Platform classes; Architecture boundary suite as relevant; whitespace and Conventional Commit checks. Retain exact commands, exit codes, compressed raw results, source/assembly hashes and the required Aspire baseline outcome. A deterministic host is not fresh live Platform or public producer qualification. Fetch and inspect divergence before final push.

- Required Platform baseline start exited 2 with pre-existing NU1605 EventStore/Dapr package downgrade errors; no AppHost was running. Full Architecture build and 404/404 cases pass. Initial Integration builds exposed two new-test compile mistakes (journal outcome name and explicitly implemented configuration builder API); corrected before runtime qualification. A full source build compiled all dependencies; focused test-only recompilation reuses those fresh outputs.
- The disposal fix extracts only the post-acquisition lifecycle to an internal helper, preserving startup/caller tokens and all existing cleanup. A substituted testing builder executes the failure path without starting resources; four combinations cover clean teardown, actual builder disposal failure, release-probe failure and both, requiring original exception identity, exactly one disposal, zero builds and precise diagnostics. A standalone actual Aspire test builder cannot be created without AppHost metadata; its failed initial control is retained.
- Terminal wakeup controls cover each of cancellation, expiry and completion with both absent process state and a persisted nonzero checkpoint. Duplicate deliveries retain one candidate; reconstructed persisted checkpoint state is unchanged. A synthetic identity gateway gate proves simultaneous out-of-request store reads cannot contaminate actual HTTP denial observation, with an authorized HTTP positive control. Initial query controls omitted service credential/caller-authority bindings; those nonqualifying attempts are retained separately.

## Review Triage Log

- Medium / patch: actor deactivation alone does not qualify method and reminder callbacks. Add actual endpoint inventory assertions requiring PUT and the SDK sidecar-channel policy for both routes.
- Medium / patch: primary-exception preservation did not prove cleanup failure diagnostics. Inject builder disposal and release-probe failures independently and together; assert exactly identified reported inner exceptions and preserve the original fixture error.
- Medium / patch: duplicate replay comparisons omitted pending-date and topology documents. Include all six emitted document families.
- Medium / patch: the next delivery's first failure lacked pre-retry state assertions. Read the pinned generation after reconstruction and compare all six documents before successful retry.
- Medium / patch: a gate assertion could leave the pending HTTP request undrained. Release and drain it in finally, cancel and dispose on failure while preserving the original error.
- Low / patch: documentation conflated retryable journal failures with terminal parking. Rename the row and retain the separate covering-repair requirement.
- Medium / patch: evidence packet was pending. Complete exact commands/results, final source/assembly/dependency hashes and ledger-preservation verification before accepting closure; the blocked live baseline remains separate.

All seven findings are addressed within this batch; no work is deferred. The reviewer also identified and rechecked the initially synthesized checkpoint and missing exact candidate membership assertions while review was running; both are corrected by explicit retained-checkpoint and single-page membership controls.

## Verification

- Complete current-source Debug Integration build passes with zero warnings/errors. Focused consumer/harness classes pass 268/268; final deterministic Integration passes 1,055/1,055 after the complete build. Architecture passes 404/404. Qualifying results have zero failures/errors/skips/not-run; focused cases are included in the deterministic total.
- Formatting and CRLF-aware whitespace checks pass. Commitlint accepts `test: close five deferred SDK boundary verification gaps`. All dependency checkouts are unchanged and clean, and origin/main equals the starting main revision after fetch.
- The [retained evidence packet](evidence/five-deferred-sdk-boundary-2026-10-28/README.md) includes exact commands/exits, compressed logs/XML, source/assembly hashes, full dependency revisions and integrity verification. Five additive ledger annotations preserve every original ledger byte. The existing NU1605 Platform launch blocker remains separate from passing deterministic qualification; no fresh live parity or producer publication is claimed.

- Independent correction recheck confirms all seven findings resolved, no new material corrections and no deferred work. The reviewer executed verify.py, independently inspected all three XML results and all thirteen named regression cases, and checked 421 source hashes, 49 exercised DLL hashes, the full Debug build and exactly five additive ledger closures.
