---
title: 'Close Story 4.8 AppHost Probe Review Gaps'
type: 'bugfix'
created: '2026-09-20'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'c4c925b2d956859ff0832d3ebc368bbe71685c7c'
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8 has fifteen unchecked review patches: the Docker Scheduler-volume probe can leave a stubborn child alive or leak unclassified failures, its deterministic tests miss key lifecycle and real-bind branches, and the story artifacts disagree about status, test totals, and duplicate deferred-work records.

**Approach:** Harden the probe as a bounded cleanup state machine, prove every reviewed branch with deterministic tests using one type per C# file, and reconcile the Story 4.8 records from observed verification output.

## Boundaries & Constraints

**Always:** Preserve exact caller cancellation after cleanup; bound child termination and redirected-pipe observation; turn infrastructure probe failures into actionable retryable diagnostics; retain the IPv6-unavailable classification; follow the repository's one-C#-type-per-file rule; preserve append-only deferred-work history by adding canonical cross-links; report only tests actually run.

**Never:** Change reminder scheduling, reconciliation, projection/index formats, live topology, dependency pins, or submodules; reopen checked/deferred Story 4.8 findings; silently delete duplicate ledger history; claim Tier-3 live evidence if the lane skips or is not run.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Probe succeeds | Docker exits zero with empty or line-delimited owners | Return a trimmed owner list | None |
| Probe fails | Docker exits non-zero or redirected reads fail | Resource settling retries with a classified diagnostic | Wrap pipe/process details in `InvalidOperationException` |
| Probe times out | Child hangs, ignores a kill, or kill throws | Cleanup stays bounded, retries termination, and cannot accumulate a live adapter child | Preserve timeout unless termination itself fails; include `AggregateException`, `Win32Exception`, and `NotSupportedException` |
| Caller cancels | Caller token cancels during the probe | Attempt bounded cleanup, then propagate cancellation | Do not convert it into timeout or retry |
| Port is occupied | One fixed control-plane address cannot bind | Resource settling waits and retries until free or budget exhaustion | Diagnostic names the resource, address, and port |

</frozen-after-approval>

## Code Map

- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs` -- owns control-plane settling, exclusive-bind probing, Docker process classification, and bounded cleanup; reuse its injected delegates and keep reminder test bodies unchanged.
- `tests/Hexalith.Works.IntegrationTests/ISchedulerVolumeProbe.cs` -- narrow process seam used by deterministic lifecycle tests; avoid exposing it outside the test assembly.
- `tests/Hexalith.Works.IntegrationTests/ProcessSchedulerVolumeProbe.cs` -- real `Process` adapter; disposal must not leave a still-running Docker child.
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs` -- extend the six existing facts to cover actual listener configuration, occupied-port retry, command redirection, success/non-zero exit, pipe faults, kill faults, stubborn termination, and caller cancellation.
- `tests/Hexalith.Works.IntegrationTests/HangingSchedulerVolumeProbe.cs` -- new standalone configurable fake replacing the nested second type.
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md` -- close only the fifteen latest review patches and reconcile frontmatter/body/change-log status.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- link the four duplicate close-out rows to their earlier canonical entries without deleting history.
- `_bmad-output/implementation-artifacts/tests/test-summary.md` and `sprint-status.yaml` -- record observed counts and return the completed patch set to review.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs` and `tests/Hexalith.Works.IntegrationTests/ProcessSchedulerVolumeProbe.cs` -- centralize bounded kill/wait/read classification, make a final termination attempt, protect disposal, and expose only the minimum internal listener seam needed to test production bind settings.
- [x] `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs` and `tests/Hexalith.Works.IntegrationTests/HangingSchedulerVolumeProbe.cs` -- add mutation-resistant facts for every matrix row and reviewed assertion while keeping each C# type in its own file.
- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/deferred-work.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md`, and `_bmad-output/implementation-artifacts/sprint-status.yaml` -- check off the fifteen findings with evidence, cross-link duplicate ledger rows, replace stale arithmetic with observed totals, and make story status consistently `in-review` after verification.

**Acceptance Criteria:**
- Given a Docker probe completes, fails, stalls, resists termination, or is caller-cancelled, when the harness observes it, then the result is bounded, correctly classified, and no adapter-owned child is intentionally left running.
- Given the production exclusive-bind and settling paths, when deterministic tests run, then listener configuration and occupied-port retry are directly exercised rather than only mocked as available.
- Given all fifteen close-out patches are implemented, when the focused and broad gates run, then their actual pass/skip/failure totals are recorded consistently and Story 4.8 returns to review with no unchecked item from that block.

## Implementation Notes

- Probe cleanup now observes both redirected reads while making up to two bounded child-tree termination attempts;
  exact caller cancellation takes precedence after cleanup, otherwise termination failure takes precedence over the
  original timeout.
- `ProcessSchedulerVolumeProbe.Dispose` independently performs bounded two-attempt termination before disposing the
  process handle. The configurable fake lives in its own file and can model ignored kills and supported kill faults.
- The listener seam routes deliberately misconfigured real IPv4 and IPv6 listeners through production configuration
  and `Start()`, while an occupied real IPv4 listener proves the collision path. The harness class grew from 6 to 33 facts.
- The final build initially hit the shared `/tmp` inode ceiling. No shared temporary data was deleted; routing
  `TMPDIR` and the CLI home through `/var/tmp/story48-review.XleClp` allowed the build and temp-using tests to pass.

## Spec Change Log

- 2026-09-20 — Implemented all three execution tasks, closed the fifteen parent-story review patches, and recorded
  the observed deterministic and live verification totals.
- 2026-09-20 — Applied the final review patches for cancellation/cleanup precedence, timeout-boundary exit races,
  adapter disposal evidence, production wrapper coverage, portable IPv4 binding, and safe real-child test cleanup;
  reran every verification gate against the resulting tree.

## Review Triage Log

- BH-01 — `medium` → `patch`: a child still alive after both adapter disposal attempts is released and the outer
  settling loop catches the resulting `InvalidOperationException`, so another probe can start and accumulate a
  second child. The frozen matrix requires bounded cleanup without accumulating an adapter-owned child.
- BH-02 — `medium` → `patch`: both redirected-read calls occur before the guarded `try`; a synchronous failure from
  either bypasses read-failure classification, and a second-call failure can leave the first task unobserved.
- BH-03 — `low` → `patch`: supported process failures from the initial exit wait are not converted to the retryable
  `InvalidOperationException` used by the settling boundary. The fix is the same direct classification used for
  cleanup operations.
- BH-04 — `medium` → `patch`: the adapter's first `HasExited` read is outside its accumulation helper; if it fails,
  no termination is attempted before the process handle is disposed and the caller can retry with the child alive.
- BH-05 — `low` → `patch`: an exception from `Process.Dispose` replaces already-collected kill/wait/timeout failures.
  Accumulating that exception preserves the actionable cleanup evidence without changing behavior.
- BH-06 — `medium` → `patch`: the real-adapter fact proves only first-kill success, so deleting the second disposal
  attempt leaves every focused test green even though that attempt is an explicit task requirement.
- BH-07 — `low` → `reject`: the fake does not inject every helper-level `HasExited`, wait, and dispose fault, but the
  frozen review assertions name kill faults and bounded lifecycle outcomes rather than every internal catch; adding
  a general fault matrix would add disproportionate test machinery for rare adapter-only branches.
- BH-08 — `low` → `patch`: the kill-failure fact omits `InvalidOperationException` and proves only the first queued
  failure. Extending the existing theory and assertions is a direct mutation-resistance correction.
- BH-09 — `medium` → `patch`: every fake redirected read currently honors cancellation, so removing the new
  `WaitAsync` bound leaves all 19 facts green. This is the verification-gap layer's independently confirmed gap.
- BH-10 — `low` → `patch`: the direct IPv6 listener construction can fail on an IPv4-only host even though production
  intentionally classifies an unavailable IPv6 family as inapplicable; a small host-capability branch keeps the lane
  deterministic.
- BH-11 — `low` → `patch`: the real-child disposal fact lacks fail-safe cleanup, so an assertion or disposal failure
  can leave its 30-second child alive. A `finally` cleanup is a direct test-isolation correction.
- BH-12 — `low` → `patch`: the dated File List names unchanged `ISchedulerVolumeProbe.cs` and omits this close-out
  spec, while earlier dated File Lists include their implementing specs. Correcting the inventory is documentation-only.
- BH-13 — `false` → `reject`: `review` is the parent story and sprint board's defined status vocabulary; the build
  spec itself uses the workflow status `in-review`. The prose phrase does not require an unsupported parent status.
- BH-14 — `medium` → `patch`: the four new “Canonical entry” notes are prose locators, not resolvable links or stable
  identifiers, so they do not satisfy the explicit cross-link task as the ledger grows.
- BH-15 — `low` → `patch`: the evidence block and prose record different `aspire describe` argument forms. Recording
  the exact observed command is a direct evidence correction.
- EC-01 — `medium` → `patch`: confirmed duplicate of BH-04; a failing initial `HasExited` read skips both termination
  attempts before releasing the handle.
- EC-02 — `false` → `reject`: production supplies the fixed five-second termination wait, so `TimeSpan` addition
  overflow cannot occur through any product caller of this internal seam.
- EC-03 — `medium` → `patch`: if caller cancellation races with a redirected-read fault after nominal exit, the I/O
  wrapper currently wins. The frozen precedence requires the exact caller token to win after observation.
- EC-04 — `low` → `patch`: the adapter does not recheck `HasExited` after its second timed wait, so a just-exited child
  can be reported as a termination failure and provoke a needless settling retry.
- EC-05 — `low` → `patch`: confirmed duplicate of BH-10; the focused lane must remain portable to IPv4-only hosts.
- EC-06 — `false` → `reject`: confirmed duplicate of BH-13; `review` is the owning artifacts' valid status value.
- EC-07 — `medium` → `patch`: confirmed duplicate of BH-01; retrying after unconfirmed adapter termination can
  accumulate children despite the acceptance criterion.
- EC-08 — `medium` → `patch`: confirmed duplicate of BH-06; the adapter's second disposal attempt is not pinned.
- VG-01 — `medium` → `patch`: pre-verified; all existing read tasks settle on cancellation, so no test proves the
  explicit redirected-pipe observation bound or its diagnostic when a read ignores cancellation.
- VG-02 — `medium` → `patch`: pre-verified; exact caller cancellation is tested only when cleanup succeeds, so moving
  the cleanup-failure check ahead of cancellation would leave the suite green.
- VG-03 — `medium` → `patch`: pre-verified duplicate of BH-06/EC-08; no adapter-level test pins the second kill/wait.
- VG-04 — `medium` → `patch`: pre-verified; the test calls the configuration helper and production bind path
  separately, so deleting the helper call from `BindPortExclusively` leaves the advertised production-path fact green.
- R2-BH-01 — `medium` → `patch`: a synchronously completed probe can return owners after the caller token is already
  cancelled because the success path never observes that token. The frozen cancellation precedence requires an
  entry/success-path check and a direct fact.
- R2-BH-02 — `medium` → `patch`: a supported initial exit-wait failure leaves redirected reads unobserved and relies
  on later adapter disposal instead of the helper's bounded termination path. Cleanup must cancel, terminate, and
  observe those tasks while retaining the original diagnostic.
- R2-BH-03 — `low` → `reject`: the cancellation-ignoring fake leaves two non-faulting infinite test tasks after its
  one focused fact, but this does not affect production cleanup and settling them adds fake-only completion state.
- R2-BH-04 — `low` → `reject`: the internal adapter is owned and disposed exactly once by
  `SchedulerVolumeOwnersAsync`; making its second `Dispose` a no-op adds lifecycle state for no demonstrated caller.
- R2-BH-05 — `false` → `reject`: the extra bounded disposal waits are deliberate and the approved precedence says
  caller cancellation is propagated after cleanup, so a possible ten-second cleanup tail is not an unbounded overrun.
- R2-BH-06 — `false` → `reject`: `PrerequisiteGapAsync` converts the non-retryable cleanup exception into an exact
  prerequisite diagnostic rather than starting another probe; Tier-3 then skips without claiming live credit, which
  is the intended prerequisite contract.
- R2-BH-07 — `medium` → `patch`: the real-child fact disposes its original handle and reacquires by PID, so a rare PID
  reuse can target an unrelated process during fail-safe cleanup. Retain a process handle across adapter disposal.
- R2-BH-08 — `low` → `patch`: the real-child fail-safe has a `HasExited`/`Kill` race and catches only
  `ArgumentException`; cleanup must tolerate the normal just-exited `InvalidOperationException` path.
- R2-BH-09 — `medium` → `patch`: pre-verified; IPv4-only runs skip the only `ExclusiveAddressUse` assertion, so the
  portable fact must send an IPv4 listener through the production bind operation unconditionally.
- R2-BH-10 — `medium` → `patch`: pre-verified; every injected adapter fact supplies a no-op disposer, so removal of
  `_dispose` or loss of its exception leaves the lane green. Add one observable throwing-dispose fact with an earlier
  lifecycle failure and assert both causes remain classified.
- R2-BH-11 — `medium` → `patch`: pre-verified; no adapter fact makes a timed wait return false immediately before
  `HasExited` turns true, so the timeout-boundary recheck can regress unnoticed.
- R2-BH-12 — `low` → `reject`: carried from BH-07; a general adapter kill/wait exception matrix remains
  disproportionate to the frozen assertions after the specific disposal and boundary-race branches are pinned.
- R2-BH-13 — `low` → `reject`: one redirected-read failure is already actionable and preserving a simultaneous
  second pipe failure would add aggregation machinery for negligible operational benefit.
- R2-BH-14 — `low` → `reject`: the observed verification already records the `/var/tmp` workaround, and this
  finding's requested fix edits the current build spec, which review findings may not require.
- R2-BH-15 — `false` → `reject`: both post-attempt cleanup checks were observed during this run; a single reproducible
  command in the summary does not mean it was invoked only once, and the prose accurately reports both outcomes.
- R2-EC-01 — `medium` → `patch`: the harness cleanup path does not recheck `HasExited` after its second timed wait, so
  an exit at the timeout boundary can be misclassified as cleanup failure and trigger a needless settling retry.
- R2-EC-02 — `false` → `reject`: redirected-read observation is deliberately bounded to the two termination windows;
  a read settling after that explicit bound is correctly reported as cleanup failure.
- R2-EC-03 — `medium` → `patch`: the real-child test's PID reacquisition and narrow race handling can kill an unrelated
  reused PID or mask the primary assertion; this is the same root cause as R2-BH-07/R2-BH-08.
- R2-EC-04 — `false` → `reject`: a partially disabled IPv6 stack is already classified through the same production
  socket-error predicate; other bind failures should fail because production would also treat them as real occupancy.
- R2-EC-05 — `false` → `reject`: two bounded termination attempts are made and an unconfirmed exit stops probing with
  a non-retryable cleanup exception; the code does not intentionally abandon a child or accumulate another one.
- R2-EC-06 — `false` → `reject`: the test directly sends the listener through `BindPortExclusively`, so removing
  production configuration from that path makes its property assertions fail.
- R2-EC-07 — `medium` → `patch`: pre-verified; disposal-failure accumulation is not mutation-protected. This is the
  same missing adapter fact as R2-BH-10/R2-VG-01.
- R2-EC-08 — `false` → `reject`: carried from BH-13; `review` is the parent story/sprint vocabulary while this build
  spec correctly uses workflow status `in-review`.
- R2-VG-01 — `medium` → `patch`: pre-verified; no fact observes a throwing disposal delegate after an earlier
  lifecycle fault, so handle release and preservation of both diagnostics are unproven.
- R2-VG-02 — `medium` → `patch`: pre-verified; no fact pins the just-exited-after-false-wait adapter race, allowing a
  future mutation to abort AppHost settling after the child has actually exited.
- R2-VG-03 — `medium` → `patch`: pre-verified; apparent non-retry and cancellation facts bypass the production
  run-plus-dispose wrapper, so subtype preservation and cancellation-over-disposal precedence can both regress green.
- R2-VG-04 — `medium` → `patch`: pre-verified; the exclusive-setting assertion must run through an IPv4 production
  listener independently of IPv6 support.

## Design Notes

Cleanup precedence is deliberate: caller cancellation wins after cleanup; otherwise a demonstrated termination failure wins; otherwise the original bounded timeout remains primary. Redirected-read faults after a nominal process exit are retryable probe failures, while expected cancellation or pipe closure during timeout cleanup must not mask the timeout.

## Verification

**Commands:**
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false -v minimal` -- expected: 0 warnings and 0 errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class "*WorksAppHostSmokeHarnessTests"` -- expected: every probe lifecycle fact passes with zero skips.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class- "*SmokeTests"` -- expected: all deterministic integration tests pass with zero skips; use its observed total in the artifacts.
- Run the Unit, Property, and Architecture test binaries directly -- expected: all pass with zero skips.
- Run the four Story 4.8 reminder/mTLS Tier-3 facts when prerequisites are available, then `aspire describe --format json` -- expected: live facts pass without skips and no AppHost remains; otherwise record the exact prerequisite or runtime blocker without claiming live credit.

**Observed 2026-09-20 (final reviewed tree):**
- Release build: passed, 0 warnings and 0 errors (`TMPDIR=/var/tmp/story48-root/tmp`).
- `WorksAppHostSmokeHarnessTests`: 33/33 passed, 0 skipped.
- Non-smoke Integration: 526/526 passed, 0 skipped.
- Unit: 568/568 passed, 0 skipped.
- Property: 3/3 passed, 0 skipped; each property completed 100 cases.
- Architecture: 268/268 passed, 0 skipped.
- Story 4.8 reminder/mTLS Tier-3 aggregate: 4/4 passed, 0 skipped, 1020.742s.
- `aspire describe --format Json --non-interactive`: no running AppHost found after the final live run.
