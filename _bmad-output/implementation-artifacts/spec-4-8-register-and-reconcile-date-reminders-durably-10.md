---
title: 'Close Story 4.8 AppHost Probe Review Gaps'
type: 'bugfix'
created: '2026-09-20'
status: 'ready-for-dev'
route: 'dispatch'
review_loop_iteration: 0
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
- [ ] `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs` and `tests/Hexalith.Works.IntegrationTests/ProcessSchedulerVolumeProbe.cs` -- centralize bounded kill/wait/read classification, make a final termination attempt, protect disposal, and expose only the minimum internal listener seam needed to test production bind settings.
- [ ] `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarnessTests.cs` and `tests/Hexalith.Works.IntegrationTests/HangingSchedulerVolumeProbe.cs` -- add mutation-resistant facts for every matrix row and reviewed assertion while keeping each C# type in its own file.
- [ ] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/deferred-work.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md`, and `_bmad-output/implementation-artifacts/sprint-status.yaml` -- check off the fifteen findings with evidence, cross-link duplicate ledger rows, replace stale arithmetic with observed totals, and make story status consistently `in-review` after verification.

**Acceptance Criteria:**
- Given a Docker probe completes, fails, stalls, resists termination, or is caller-cancelled, when the harness observes it, then the result is bounded, correctly classified, and no adapter-owned child is intentionally left running.
- Given the production exclusive-bind and settling paths, when deterministic tests run, then listener configuration and occupied-port retry are directly exercised rather than only mocked as available.
- Given all fifteen close-out patches are implemented, when the focused and broad gates run, then their actual pass/skip/failure totals are recorded consistently and Story 4.8 returns to review with no unchecked item from that block.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

Cleanup precedence is deliberate: caller cancellation wins after cleanup; otherwise a demonstrated termination failure wins; otherwise the original bounded timeout remains primary. Redirected-read faults after a nominal process exit are retryable probe failures, while expected cancellation or pipe closure during timeout cleanup must not mask the timeout.

## Verification

**Commands:**
- `DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false -v minimal` -- expected: 0 warnings and 0 errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class "*WorksAppHostSmokeHarnessTests"` -- expected: every probe lifecycle fact passes with zero skips.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class- "*SmokeTests"` -- expected: all deterministic integration tests pass with zero skips; use its observed total in the artifacts.
- Run the Unit, Property, and Architecture test binaries directly -- expected: all pass with zero skips.
- Run the four Story 4.8 reminder/mTLS Tier-3 facts when prerequisites are available, then `aspire describe --format json` -- expected: live facts pass without skips and no AppHost remains; otherwise record the exact prerequisite or runtime blocker without claiming live credit.
