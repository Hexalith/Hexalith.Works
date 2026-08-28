---
title: 'Propagate caller cancellation from the prerequisite probe'
type: 'bugfix'
created: '2026-08-28'
status: 'done'
baseline_revision: '55766f1ab082545fa8e935de1f9936e57f405efb'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '_bmad-output/implementation-artifacts/spec-recovery-edge-case-test-hardening.md'
warnings: []
deferred:
  - summary: >-
      Sibling smoke-test prerequisite probes still collapse caller-requested cancellation into an unavailable result.
    evidence: |-
      `WorksCommandPipelineSmokeTests.IsPortReachableAsync` and `WorksReminderRecoveryPipelineSmokeTests.IsPortReachableAsync` catch every `OperationCanceledException` and return `false`. Both implementations pre-date this bundle and are outside DW-33's cited cascade-recovery probe.
    location: >-
      tests/Hexalith.Works.IntegrationTests/WorksCommandPipelineSmokeTests.cs:179; tests/Hexalith.Works.IntegrationTests/WorksReminderRecoveryPipelineSmokeTests.cs:387
    severity: medium
  - summary: >-
      The deterministic probe cases never run in the repository's habitual deterministic lane, because they live in a class that lane excludes by name.
    evidence: |-
      The routine deterministic command recorded across this repository's specs is
      `Hexalith.Works.IntegrationTests -class- "*SmokeTests"`, an exclude-by-class filter that
      drops every case in `WorksCascadeRecoveryPipelineSmokeTests`. Confirmed against the built
      Release assembly: `-list Tests` reports 15 `Port_probe_*`/`Prerequisite_gate_*` cases with no
      filter and 0 under `-class- "*SmokeTests"`. They still run in an unfiltered full-assembly run,
      and the spec's own verification command targets the class directly, so the coverage is not
      orphaned -- but a probe regression is invisible to the lane that is actually run by habit.
      Relocating them needs a new test class outside this file, which the intent's Block If fences off.
    location: >-
      tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:145
    severity: medium
---

<intent-contract>

## Intent

**Problem:** The cascade-recovery prerequisite probe converts both its own two-second timeout and caller-requested test cancellation into an unavailable-port result. External cancellation can therefore become a misleading xUnit skip instead of reaching the test runner.

**Approach:** Preserve `false` for socket failure and the probe-owned timeout, but allow cancellation requested through the caller token to propagate. Add deterministic tests that exercise the cancellation distinction without depending on network timing.

## Boundaries & Constraints

**Always:** Distinguish cancellation by the caller token from cancellation by the linked probe timeout; preserve sequential first-unavailable-port behavior, the two-second production probe limit, xUnit v3/Shouldly conventions, and `ConfigureAwait` policy.

**Block If:** The fix requires changing production code, AppHost topology, prerequisite port selection, live convergence assertions, or files outside the cascade-recovery smoke test and this spec.

**Never:** Convert caller-requested cancellation into `false`, `Assert.Skip`, or another unavailable-port result; use real network delays for deterministic cancellation tests; edit `_bmad-output/implementation-artifacts/deferred-work.md` or any `.bmad-loop` ledger; broaden the change to the similar probes in other smoke-test classes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Reachable port | Connection completes before either token cancels | Probe returns `true` | No error expected |
| Socket failure | Connection raises `SocketException` | Probe returns `false` | Port remains an unavailable prerequisite |
| Probe timeout | Only the probe-owned timeout cancels the linked operation | Probe returns `false` | Port remains an unavailable prerequisite |
| Caller cancellation | Caller token is cancelled before or during the operation | Cancellation propagates to the caller | No unavailable port or skip result is synthesized |

</intent-contract>

## Code Map

- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:42` -- `s_probeTimeout` names the two-second production probe limit; `s_deterministicHangGuard` bounds every deterministic await so a regression fails instead of hanging the assembly.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:53` -- the live test passes `TestContext.Current.CancellationToken` through the prerequisite gate and calls `Assert.Skip` only when a port number is returned; all live phase assertions remain unchanged.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:105` -- deterministic gate cases retain sequential first-unavailable and all-reachable probing behavior.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:147` -- deterministic connection-seam cases cover pending caller cancellation, pending probe timeout, pre-cancelled production-wrapper entry, simultaneous cancellation precedence, post-completion classification, and the socket-failure race without network timing; each cancellation case asserts the escaping `OperationCanceledException` carries the caller's token.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:363` -- a cancellation attributable to neither the caller token nor the probe deadline escapes unchanged, pinning the seam's `throw;` branch.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:379` -- real-socket cases drive the production wrapper end to end: an ephemeral listening loopback port is reachable and a closed one is not; both own their `TcpListener` with `using`.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:408` -- deadline cases prove the wrapper applies its own `TimeSpan` deadline to a never-completing connection and that the deadline constant is still two seconds.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:429` -- the gate composed with the real probe method group propagates caller cancellation instead of synthesizing an unavailable port.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:730` -- `FirstUnavailablePrerequisitePortAsync` returns the first `false` probe and propagates exceptions without synthesizing a port or skip result.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:752` -- the two-argument production wrapper rejects an already-cancelled caller before it allocates a socket, then owns the real `TcpClient` and passes `s_probeTimeout` to the deadline overload.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:762` -- the deadline overload turns a `TimeSpan` budget into the probe-owned cancellation token, which is what makes the limit testable without network timing.
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs:774` -- the core seam checks caller cancellation before and after connection, gives it precedence in socket and simultaneous-cancellation paths, rethrows caller cancellation under the caller's own token, maps only a probe-owned timeout or socket failure to `false`, and rethrows any other cancellation unchanged.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` -- affected xUnit v3 project; build once, then run the focused class from the Release test assembly.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs` -- refine the probe cancellation handling so only probe-owned timeout cancellation maps to `false`, and add deterministic coverage for caller cancellation, probe timeout, and socket failure while retaining the existing reachable/unavailable gate cases.

**Acceptance Criteria:**
- Given the live cascade test is cancelled while prerequisite probing is active, when the caller token requests cancellation, then `OperationCanceledException` reaches the test runner and the gate cannot generate an unavailable-port skip.
- Given the caller token is not cancelled, when the connection fails or the probe's two-second deadline expires, then the gate continues to receive `false` and reports the affected prerequisite as unavailable.
- Given all prerequisite probes succeed, when the live cascade lane runs, then its existing ordered probing and convergence assertions remain unchanged.
- Given cancellation escapes the probe because the caller requested it, when the caller inspects the `OperationCanceledException`, then its `CancellationToken` is the caller's own token rather than the internal linked token.
- Given a connection that never completes, when the probe is given a deadline budget, then the probe returns `false` once that budget elapses, and the production limit remains two seconds.

## Spec Change Log

## Review Triage Log

### 2026-08-28 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 10: (high 0, medium 7, low 3)
- defer: 1: (high 0, medium 1, low 0)
- reject: 6: (high 0, medium 0, low 6)
- addressed_findings:
  - `[medium]` `[patch]` Made caller cancellation dominant before connection invocation and after successful completion, including the pre-cancelled path.
  - `[medium]` `[patch]` Rechecked caller cancellation in the socket-failure catch so a race cannot become an unavailable-port result.
  - `[medium]` `[patch]` Classified a successful connection completed after the probe-only timeout as unavailable.
  - `[medium]` `[patch]` Replaced immediate caller cancellation with a genuinely pending linked-token operation.
  - `[medium]` `[patch]` Replaced the pre-cancelled timeout stub with a genuinely pending linked-token operation.
  - `[medium]` `[patch]` Added a pre-cancelled caller test at the production two-argument probe boundary.
  - `[medium]` `[patch]` Added simultaneous caller/timeout coverage that pins caller-cancellation precedence.
  - `[low]` `[patch]` Applied `.ConfigureAwait(true)` to the async Shouldly cancellation assertions.
  - `[low]` `[patch]` Refreshed stale Code Map anchors and the split production-wrapper/core-seam responsibilities.
  - `[low]` `[patch]` Replaced the incomplete fallback-build note with the independently rerun exact command result.

### 2026-08-28 - Follow-up review pass
- intent_gap: 0
- bad_spec: 0
- patch: 8: (high 0, medium 5, low 3)
- defer: 0
- reject: 9: (high 0, medium 2, low 7)
- addressed_findings:
  - `[medium]` `[patch]` Caller cancellation escaping the probe carried the linked probe token, so `ex.CancellationToken` never matched the caller's. Replaced the exception filter with a catch that rethrows through `cancellationToken.ThrowIfCancellationRequested()`, keeps probe-timeout mapping to `false`, and rethrows an unattributable cancellation.
  - `[medium]` `[patch]` The production two-argument wrapper's real connect path was exercised by no test -- replacing its connect lambda with a failing stub left the suite green. Added ephemeral-`TcpListener` cases proving a listening loopback port is reachable and a closed one is not.
  - `[medium]` `[patch]` The two-second deadline was unverified -- deleting it entirely left the suite green. Extracted a `TimeSpan` deadline overload plus `s_probeTimeout`, and added a never-completing-connection case and a case pinning the limit at two seconds.
  - `[medium]` `[patch]` The precedence test asserted only that some cancellation escaped, which the plain caller-cancellation test already proved. It now asserts the escaping token is the caller's and that the token handed to the connection is cancellable.
  - `[medium]` `[patch]` Every deterministic seam await was unbounded, so a propagation regression hung the assembly instead of failing. Bounded each with `WaitAsync(s_deterministicHangGuard, TestContext.Current.CancellationToken)`.
  - `[low]` `[patch]` `SetResult`/`SetCanceled` on the test `TaskCompletionSource` instances would throw from inside the connect delegate on a double signal; switched to `TrySet*`.
  - `[low]` `[patch]` Added caller-token assertions to the remaining cancellation-propagating cases so they cannot pass on an unrelated cancellation.
  - `[low]` `[patch]` Corrected the pre-cancelled boundary test's summary, which claimed endpoint validation the method does not perform.

### 2026-08-28 -- Second follow-up review pass
- intent_gap: 0
- bad_spec: 0
- patch: 7: (high 0, medium 0, low 7)
- defer: 1: (high 0, medium 1, low 0)
- reject: 17: (high 0, medium 0, low 17)
- addressed_findings:
  - `[low]` `[patch]` The change introduced a UTF-8 BOM on `WorksCascadeRecoveryPipelineSmokeTests.cs`, making it the only BOM-prefixed file in the test project and contradicting `.editorconfig` `charset = utf-8`. Stripped it.
  - `[low]` `[patch]` `Port_probe_production_boundary_propagates_pre_cancelled_caller` probed port `-1`, so moving the caller-cancellation guard after the connect -- the regression the test exists to catch -- would fail with `ArgumentOutOfRangeException` instead of a legible propagation failure. It now uses `PrerequisitePorts()[0]`.
  - `[low]` `[patch]` The two-argument production wrapper allocated a `TcpClient` and armed a deadline timer before the inner guard rejected an already-cancelled caller, contradicting that test's own summary ("before it opens a socket"). Hoisted `cancellationToken.ThrowIfCancellationRequested()` into the wrapper.
  - `[low]` `[patch]` Both real-socket cases called `listener.Stop()` without disposing the `TcpListener`, and the closed-port case left `Stop()` unguarded so a failing `LocalEndpoint` cast leaked it. Both now use `using var`.
  - `[low]` `[patch]` `Port_probe_gives_caller_cancellation_precedence_over_probe_timeout` consumed `connectionToken` in `TrySetCanceled` one line before asserting it was cancellable, so a never-invoked delegate produced a misleading cancellation before the diagnostic assertion ran. Assertion moved ahead of the use.
  - `[low]` `[patch]` The seam's `throw;` branch for cancellation attributable to neither the caller token nor the probe deadline was untested -- replacing it with `return false;` left the suite green. Added `Port_probe_rethrows_unattributable_cancellation`; the mutation now fails it.
  - `[low]` `[patch]` The Acceptance Criteria and Code Map still described only the first pass's contracts, omitting the caller-token identity and probe-deadline behaviors the second pass added and mutation-tested. Added two acceptance criteria and refreshed every Code Map anchor.

## Design Notes

The cancellation source must be classified against the original caller token. The linked token is cancelled for both sources, so checking only the caught exception or linked-token state cannot distinguish external cancellation from the probe-owned deadline.

Classification alone is not enough: the `OperationCanceledException` raised inside the connection carries the *linked* token, so rethrowing it verbatim would hand callers a token that never equals theirs. Caller cancellation is therefore re-raised through `cancellationToken.ThrowIfCancellationRequested()`, which is also what makes caller-vs-timeout precedence assertable.

The probe-owned deadline is expressed as a `TimeSpan` overload rather than a literal inside the wrapper. That is the only seam at which "the wrapper actually applies a deadline" can be proven without a real network delay -- a never-completing connection plus a short budget always resolves to `false`, whatever the machine's timing.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:MinVerVersionOverride=1.0.0` -- expected: succeeds with zero warnings and errors.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.WorksCascadeRecoveryPipelineSmokeTests` -- expected: deterministic probe cases pass; the live case either passes with prerequisites or skips only for an actually unavailable port.

**Results:**
- The exact prescribed Release build command succeeded with 0 warnings and 0 errors.
- `WorksCascadeRecoveryPipelineSmokeTests`: `Total: 19, Errors: 0, Failed: 0, Skipped: 1` -- 18 deterministic cases passed; the live case skipped because the actual first unavailable prerequisite was `localhost:50005`.
- Mutation checks (applied, built, run, then reverted; the file was restored and rebuilt afterwards):
  - Dropping the probe-owned deadline fails `Port_probe_deadline_reports_a_never_completing_connection_as_unavailable` (1 failed).
  - Replacing the production wrapper's real connect with a failing stub fails `Port_probe_production_boundary_reports_listening_port_as_reachable` (1 failed).
  - Restoring the original blanket `catch (OperationCanceledException) { return false; }` fails three caller-cancellation cases (3 failed).
  - All three mutants fail rather than hang; the pre-patch suite was green under the first two.

## Auto Run Result

Status: done

Summary: Second follow-up review pass over the committed cancellation fix. The classification logic and its deterministic coverage were already correct and needed no rework; this pass removed an accidental BOM, closed the last untested branch of the seam (unattributable cancellation), tightened three test-hygiene defects that could make a real regression report the wrong failure, made the production wrapper honour a pre-cancelled caller before allocating anything, and brought the Acceptance Criteria and Code Map up to what the code actually contracts. No intent gap and no spec defect were found.

Files changed:
- `tests/Hexalith.Works.IntegrationTests/WorksCascadeRecoveryPipelineSmokeTests.cs` -- stripped the UTF-8 BOM, added `Port_probe_rethrows_unattributable_cancellation`, guarded the production wrapper against an already-cancelled caller before it allocates a socket, replaced the `-1` probe port with a real prerequisite port, put both real-socket listeners under `using`, and asserted the captured connection token before consuming it.
- `_bmad-output/implementation-artifacts/spec-probe-cancellation-propagation.md` -- added two acceptance criteria, refreshed every Code Map anchor, recorded this pass's triage and the new deferral.

Review findings breakdown: 7 patches applied (high 0, medium 0, low 7); 1 new item deferred (deterministic probe cases are excluded from the habitual `-class- "*SmokeTests"` lane); 17 findings rejected.

Follow-up review recommendation: true (patched findings: high 0, medium 0, low 7; score `3 x 0 + 1 x 7 = 7`).

Verification performed:
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -p:MinVerVersionOverride=1.0.0` -- succeeded with 0 warnings and 0 errors.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.WorksCascadeRecoveryPipelineSmokeTests` -- `Total: 20, Errors: 0, Failed: 0, Skipped: 1` (the live lane, as before).
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class- "*SmokeTests"` -- `Total: 198, Errors: 0, Failed: 0, Skipped: 0`; the wider deterministic lane is unaffected.
- Mutation check: replacing the seam's `throw;` branch with `return false;` fails `Port_probe_rethrows_unattributable_cancellation` (1 failed). Reverted and rebuilt green.
- Mutation check: deleting the newly hoisted wrapper guard leaves the suite green -- recorded honestly below rather than claimed as coverage; the guard is an allocation-avoidance refinement, and the behavior it protects is still pinned by the inner guard.

Residual risks:
- The deterministic probe cases do not run under the repository's habitual `-class- "*SmokeTests"` deterministic command; confirmed empirically (`-list Tests` yields 15 probe cases unfiltered, 0 under that filter). They do run in an unfiltered full-assembly run and in the class-targeted command this spec prescribes. Relocating them needs a test class outside this file, which the intent's Block If fences off, so it is carried as a deferral instead.
- The two-second production limit is pinned as a constant (`s_probeTimeout`) and behaviorally at the `TimeSpan` deadline overload, but nothing pins that the two-argument wrapper still passes `s_probeTimeout`: replacing that reference with a literal would leave the suite green. Closing this needs either a real network delay or test-only instrumentation, both fenced off by the intent.
- The live Aspire convergence case still does not execute in this sandbox (Dapr placement unavailable on `localhost:50005`), so AC-1 and AC-3 remain proven at the gate and probe surfaces rather than inside the live lane body. Nothing detects the live method being rewired to pass `CancellationToken.None`.
- A connection that completes successfully after the probe-only deadline is still classified unavailable (`!probeTimeoutToken.IsCancellationRequested`). Two reviewers challenged this again; the decision from the previous pass stands, since both readings are defensible and the race window on a loopback probe is negligible.
- `Port_probe_production_boundary_reports_closed_port_as_unreachable` binds an ephemeral port, releases it, then probes it; another process claiming that port in the gap would fail the assertion. Accepted as inherent to testing a closed port.
- A `SocketException` thrown by the `TcpClient` constructor still escapes instead of mapping to an unavailable port, because construction sits outside the seam's try block. Only reachable under socket-handle exhaustion.
- The two sibling smoke-test probes remain separately deferred in this spec's frontmatter; DW-33 cites only the cascade-recovery probe.
