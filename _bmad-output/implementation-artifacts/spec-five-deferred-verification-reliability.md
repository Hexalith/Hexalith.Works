---
title: 'Close five deferred verification reliability gaps'
type: 'chore'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred verification records lack a current, explicit closeout: DW-59 deterministic prerequisite discovery, Platform-only fact skip semantics, QueryWindowStore bulk/batch observation (T109), real Docker-probe redirected-read cancellation, and mapped HTTP cancellation forwarding (C11-VG2).

**Approach:** Qualify the existing discovery and skip repairs, pin all three query store bindings and observation paths with a regression, exercise the real child-process adapter with redirected streams on timeout and caller cancellation, and verify the SDK's mapped query/replay/projection delegates forward and observe RequestAborted. Close exactly these five records using executed evidence, then independently review, commit and push Works main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline: clean Works main at `e27a19837ea78f1c8883d4ba9f9e6c54f2a41a5e`. The user explicitly delegated five deferred items and the final push. This is a small test-only batch: three C# files, this spec, ledger status/resolution fields, and retained evidence. No dependency or production API change, data migration or unresolved intent gap. Use oneshot.
- Repository guidance: read root instructions, shared LLM/Git baselines, README, dependency/build/test configuration and architecture spine. Works has no AppHost; these checks use route delegates, in-memory stores and owned child processes, without a distributed deployment.
- DW-59's removed `WorksCascadeRecoveryPipelineSmokeTests` has current TCP controls in `WorksAppHostSmokeHarnessTests`, which the deterministic `*SmokeTests` exclusion retains. Verify actual runner discovery and execute current harness controls rather than resurrect retired code.
- Platform-only Historical, HA and Projection facts already call Assert.Skip outside their selected artifact lane (T17). Invoke those explicitly without Platform selection and verify XML has only failed skips under failSkips=true; preserve that expected nonzero exit separately from passing deterministic evidence.
- `WorksSdkProtectedHttpTests.QueryWindowStore` already wraps IReadModelStore, IReadModelBulkStore and IReadModelBatchStore, and BuildProtectedHost replaces all three registrations. Add one regression to resolve each through DI and prove each operation increments only in an observed request scope; run existing authorized and denied HTTP controls.
- `WorksAppHostSmokeHarnessTests` owns deterministic probes. Add real redirected stdout/stderr controls through ProcessSchedulerVolumeProbe and RunSchedulerVolumeProbeAsync, with bounded completion, exact caller-token checks, process-exit assertions and fail-safe cleanup. Cover timeout as well as cancellation.
- Add `WorksSdkEndpointCancellationTests.cs` against the consumed SDK, registering substitutes for query, projection and keyed async replay handlers. Invoke the actual mapped RequestDelegate with a real JSON body and live RequestAborted, verify success with the exact token, and cancellation triggered inside each handler prevents success. This tests mapping/binding, not authentication, which existing protected HTTP controls cover.
- Validation: build the IntegrationTests project in Debug source mode, run focused classes, inspect discovery under deterministic filters, execute expected failed-skip controls separately, then run the deterministic integration lane if focused checks pass. Run whitespace checks and commitlint before commit. Preserve exact commands, logs/XML and source hashes in a compact evidence packet.

- The broad Debug build reproduced the six known Platform.Composition failures (missing Builds tooling members and two CA1062 errors). The focused `BuildProjectReferences=false` fallback compiled current tests with zero warnings/errors, reusing available dependency outputs. Initial focused run passed 131/133; the new real-process tests exposed NSubstitute's inability to proxy the internal ISchedulerVolumeProbe. Replaced that proxy with a small ObservedSchedulerVolumeProbe fixture in its own file, without changing visibility or assembly proxy permissions. Endpoint and store controls already passed.

- Current focused source Debug tests pass **133/133**; the full deterministic integration lane passes **802/802**, zero failures/errors/skips/unexecuted. Actual filtered discovery includes all three current TCP prerequisite controls. The explicitly selected Platform absence controls produce exactly **3 FAIL_SKIP** results and no passes (expected exit 1). The formatter check for the two new files exits 0 with the usual workspace-loading warning; the focused build itself has zero warnings/errors.

## Verification

- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1` exits 1: six existing Platform.Composition errors (CS0117/CS0246/CS1061 tooling member mismatches, plus CA1062). Full broad failure retained independently.
- The same command with `--no-restore -p:BuildProjectReferences=false` exits 0, zero warnings/errors; it compiles current tests against available dependency outputs.
- Built Debug assembly with `-class '*WorksAppHostSmokeHarnessTests' -class '*WorksSdkEndpointCancellationTests' -class '*WorksSdkProtectedHttpTests'` passes 133/133. The same assembly with `-class- '*SmokeTests' -class- '*Platform*'` passes 802/802. Both have zero errors, failures, skips or unexecuted cases.
- Filtered runner discovery retains all current TCP prerequisite controls. Explicit Historical/HA/Projection selection with no Platform artifact gives exactly three FAIL_SKIP results and zero passes, expected exit 1. This verifies absence semantics only.
- New-file whitespace formatter exits 0, with a workspace-load warning. `git -c core.whitespace=cr-at-eol diff --check` passes. Existing LF files retain their original line endings to keep the diff focused.
- [Retained evidence](evidence/five-deferred-verification-reliability-2026-10-23/manifest.json) binds exact commands and exit codes, compressed logs/XML, final source and consumed DLL hashes. Real-child execution is qualified on Linux; complete Platform build, live topology and parity are not claimed.

- Review expands DW-59 closure beyond discovery: the current TCP helper had lost caller precedence after successful/failed connections when the old smoke helper was retired. Restored the original semantics in the current test harness and added eight controlled timeout/in-flight/success/socket/foreign/pre-cancellation controls. The retired multi-port gate is not part of current topology. No production runtime changed.
- Review hardening also removes timing-sensitive pending assertions from the timeout case, bounds every mapped delegate invocation, covers pre-aborted requests and cancellation across suspended query/replay handlers, compares deserialized requests and successful response contracts, exercises all five store operations in every observation window, and asserts underlying batch/conditional-write end state. Initial review-fix compilation caught CA1062 on the theory scenario string; added its required null check.

## Review Triage Log

1. **Medium, patched:** 200 ms timeout could expire before transient pending-state assertions. Removed those assertions from the timed-out branch; caller-cancelled reads are checked before explicit cancellation. Terminal task/exit assertions remain in both branches.
2. **Medium, patched:** Three TCP controls did not preserve retired cancellation-race coverage. Restored current-helper caller precedence and added eight deterministic race controls in the included harness class. The removed multi-port gate remains obsolete.
3. **Medium, patched:** Endpoint delegate waits lacked deadlines. Every invocation and async handshake now has a five-second bound plus the runner cancellation token.
4. **Medium, patched:** Completed handler tasks omitted suspended async cancellation. Query/replay now have controlled pending-handler cancellation cases with a release gate and bounded cleanup.
5. **Low, patched:** Added pre-aborted requests for all three mapped routes, requiring zero handler invocations and the original cancellation token.
6. **Low, patched:** Captured and compared complete deserialized requests, including nonempty opaque query bytes.
7. **Low, patched:** Success controls now deserialize each route's typed response and assert its successful state and expected payload.
8. **Low, patched:** Store observation now executes GetAsync, SaveAsync, TrySaveAsync, bulk and batch operations through their actual host bindings.
9. **Low, patched:** Every operation now runs in disabled, background, active and disposed observation windows, asserting each incremental count.
10. **Low, patched:** Batch success and underlying batch/conditional-write persisted values are required. No finding is deferred.

- Independent follow-up confirmed nine findings resolved and requested proof that caller/deadline cancellation actually reaches the connection token. Added an explicit IsCancellationRequested assertion in every relevant controlled scenario, with the foreign-cancellation control remaining uncancelled. The review-expanded focused run before this final assertion passed 146/146; final verification follows.

- Final post-review Debug fallback build passes with zero warnings/errors. The complete current deterministic assembly passes **815/815**, including all **146** cases in the focused classes, with zero failures/errors/skips/unexecuted. Discovery retains all three real TCP controls and the controlled race method; execution proves all eight race cases. Final Platform absence again has exactly three expected FAIL_SKIP results and zero passes. Earlier 133/802 and 146 focused results are superseded stages, not final-assembly evidence.
- The independent reviewer confirmed all ten findings addressed after checking the final connection-token assertion. No new deferred work was introduced. Exactly the five selected ledger records are closed. Commitlint passes for `test: close five deferred verification gaps`; CRLF-aware Git whitespace validation passes. All submodule revisions and production domain/runtime code remain unchanged.
