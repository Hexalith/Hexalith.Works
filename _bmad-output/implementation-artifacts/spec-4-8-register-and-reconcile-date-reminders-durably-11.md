---
title: 'Close remaining Story 4.8 spec-10 review patches'
type: 'bugfix'
created: '2026-09-20'
status: 'in-progress'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8 still has six unchecked spec-10 review items: two deterministic AppHost tests contain platform-sensitive observation races, one existing probe-failure path lacks direct wrapper coverage, and the owning tracking artifacts remain inconsistent or non-portable.

**Approach:** Harden only the affected tests, prove that the existing probe wrapper already preserves classified failures, and reconcile the parent story, spec-10, sprint status, File List, and five deferred-work locators from observed verification evidence.

</frozen-after-approval>

## Implementation Notes

- The Aspire pre-change baseline started through `aspire start`; Dapr Sentry, Placement, and Scheduler became healthy, but `eventstore` exited and left `works` waiting. The resource graph was captured with `aspire describe`, then the AppHost was stopped cleanly before editing.
- Investigation disproved the production probe-wrapper finding: `RunAndDisposeSchedulerVolumeProbeAsync` already rethrows `probeFailure` with `ExceptionDispatchInfo`. Added the missing wrapper-level non-zero-exit regression instead of changing correct exception precedence.
- Hardened the IPv6 fallback against constructor-time unavailable-family failures and changed the real-child assertion to observe exit through the retained independent process handle.
- Normalized the five new deferred-work locators and completed the historical spec-10 File List without deleting or replacing ledger history.
- Verification passed: solution restore/build, focused harness 41/41, Unit 568/568, Property 3/3, non-smoke Integration 534/534, and Architecture 268/268, all with zero skips or failures.
- Reconciled all six findings in the parent story and spec-10, set the parent workflow status to `in-review`, returned sprint tracking to `review`, and recorded that no fresh Tier-3 credit was claimed.
- The required Blind Hunter review produced no accepted patch or deferred-work item; all findings were checked against the one-shot workflow, historical artifact scope, or the exercised code paths below.

## Review Triage Log

- BH-01 — `false`: `in-progress` was the required implementation/review state; this finalization changes the one-shot spec to `done`.
- BH-02 — `false`: parent frontmatter uses the build workflow's recognized `in-review` value, while the legacy story body and sprint board intentionally use `review`; the resolved finding documents both schemas.
- BH-03 — `false`: spec-10's 33/526/4-of-4 block is dated historical evidence for its own checkpoint, not a current-tree claim; spec-11 and `test-summary.md` record the superseding 41/534/no-new-live-credit evidence.
- BH-04 — `false`: the edited File List block is explicitly the historical spec-10 close-out inventory. The new one-shot workflow artifact is tracked by this spec and the resulting commit, not retroactively inserted into that dated block.
- BH-05 — `false`: the `oneshot` route requires the template's task, acceptance, and Code Map sections to be deleted when there are no intent gaps, irreversibles, or large footprint; implementation notes carry the observed evidence.
- BH-06 — `low`, rejected: the pre-edit AppHost state is ancillary baseline evidence for a deterministic test-only patch. Persisting environment-specific resource output and external log excerpts would not improve the implemented assertions and could capture sensitive runtime metadata.
- BH-07 — `false`: the fallback executes only after the production IPv6 bind has already classified that address family as unavailable. IPv6-capable hosts take the primary branch and assert both listener settings; unavailable hosts cannot make a meaningful IPv6 assertion.
- BH-08 — `false`: the new regression targets the reported null-return path by requiring the classified non-zero failure and successful disposal. `ExceptionDispatchInfo.Capture(probeFailure).Throw()` directly establishes identity/stack preservation; brittle stack-trace assertions are outside the defect.
- BH-09 — `false`: `source_spec` entries conventionally identify a repository-relative artifact, while each row's summary and evidence identify the originating concern. The five rows are distinct from the existing canonical anchored items and require no duplicate cross-link.

### Review Findings (2026-09-21, bmad-code-review, spec-11 close-out `origin/main...HEAD`)

_Scope: `origin/main...HEAD` (HEAD `0639a69`). Layers: blind-hunter, verification-gap, and acceptance-auditor reported; edge-case-hunter returned empty and is recorded as failed. 8 raw findings triaged to 0 decision, 2 patch, 2 defer, 4 rejected._

- [ ] [Review][Patch] The parent File List has no dated spec-11 inventory, so this increment’s tree is unlisted while earlier close-outs added their own dated blocks [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:709]
- [ ] [Review][Patch] Dev Agent Record Completion Notes were not prepended for spec-11, so the latest bullet still reports focused harness 40/40 and non-smoke Integration 533/533 against this increment’s Change Log and `test-summary.md` totals of 41 and 534 [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:490]
- [x] [Review][Defer] Spec-10 Verification Observed is still titled “final reviewed tree” with harness 33/33, Integration 526/526, and live 4/4 [_bmad-output/implementation-artifacts/spec-4-8-register-and-reconcile-date-reminders-durably-10.md:212] — deferred: fix edits another spec
- [x] [Review][Defer] The dated **2026-09-20 spec-10 five-patch close-out** File List still omits `spec-4-8-register-and-reconcile-date-reminders-durably-10.md` [_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:711] — deferred: pre-existing spec-10 BH-12 inventory gap, not one of spec-11’s six named patches

**Rejected:**
- `false` — real-child disposal still races because `WaitForExitAsync` starts after adapter `Dispose` and never sets `EnableRaisingEvents`: the independent `GetProcessById` handle is retained before dispose, and `WaitForExitAsync` waits on that process handle rather than the disposed `Exited` event.
- `false` — IPv6 fallback `DualMode = true` before production configuration lets IPv6-capable CI skip assertions: that block runs only after production bind classified IPv6 as inapplicable; capable hosts take the primary branch.
- `false` — the five deferred-work rows still need `#story-4-8-canonical-*` fragment ids: they are distinct spec-10 concerns, not re-observations of the existing canonical items.
- `false` — the wrapper non-zero regression is incomplete without `using var probe` and `ShouldNotContain("process observation failed")`: the wrapper already disposes, `InnerException.ShouldBeNull()` already rejects a typical wrap, and identity/stack pins were left out of spec-11 defect scope.
