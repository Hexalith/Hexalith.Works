---
title: 'Qualify five deferred reminder recovery outcomes'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five deferred reminder recovery items together, as requested. Qualify the current SDK replacement for: parked aggregate repair through a covering authoritative rebuild; independent scheduler failures among multiple date awaits; bounded due-now submission failure diagnostics; retained fired reminders until an acceptable durable target receipt; and retained incomplete evidence when candidate failure precedes caller cancellation. Exercise actual Works intent/catalog composition and SDK persistence over serialized state, reconstruct services, and inspect durable outcomes. Add missing consumer controls and only small corrections justified by executing failures. Preserve current SDK ownership, existing retry/disposition policy and original historical ledger evidence. Count canonical findings once, marking duplicate observations as aliases. Independently review, verify, commit and push main as requested.

</frozen-after-approval>

## Implementation Notes

- Clean main baseline: 427d1ddbc560a81a0e1e7fc36ad9f1275f4bde5b. Freeform batch explicitly includes five deferred items and push to main. Intent gaps: none. Irreversible implementation operations: none. Small footprint: existing reminder consumer test classes, scheduler test proxy, operator guidance if a qualification boundary needs clarification, spec, ledger and evidence. No new runtime API or mechanics, dependency change or production operation.
- Read root repository guidance, LLM/Git/state baselines, EditorConfig, build/test configuration and architecture AD-01/09/27. Existing helpers in WorksDateReminderSchedulingTests execute ReminderCoordinator via the real production service registration with controlled gateway, scheduler, submitter and clock. WorksSdkReminderRecoveryPolicyTests provides durable catalog/reconciler and rebuild setup. Reuse WorksReminderRecoveryLogCapture and InMemoryReadModelStore; record current supported retry behavior rather than inventing an exhaustion policy.
- Selected canonical entries: story-4-8-canonical-unpark-replay; story-4-8-canonical-scheduler-exhaustion; story-4-8-canonical-due-now-telemetry; story-4-8-canonical-in-tenant-cancellation; and the fired-reminder terminal-outcome summary from the 4.8 closeout. Dated alias resolutions do not count as additional work.
- Required baseline: aspire start --non-interactive --apphost /home/administrator/projects/hexalith/works/references/Hexalith.Platform/apphost.cs --format Json exited 2 with NU1605 Dapr hosting and EventStore Aspire/ServiceDefaults downgrades. Raw log: /tmp/works-five-deferred-reminder-baseline.log. Aspire ps initially reported no running hosts. Focused consumer evidence does not claim live Platform requalification.
- Validation: build the Debug Integration project and invoke named xUnit v3 classes directly with -class; then run the relevant deterministic regression lane. Preserve exact commands, counts, source/assembly hashes and baseline blocker in a portable evidence packet. Read and follow the one-shot review step using an independent blind reviewer before closure. Validate Conventional Commit with commitlint; fetch and inspect divergence before push.

- Added eleven executing cases: persistent per-await arm isolation, due-now fault diagnostics and sibling progress, seven callback receipt/cancellation cases, parked-source covering repair and exact-cancellation failure retention. The full Debug Integration build passes with zero warnings/errors; focused classes pass 27/27, and the deterministic integration lane passes 1,026/1,026 with zero errors/failures/skips/not-run. The first build found two nullable dereferences in new assertions; these were corrected before successful validation.
- Parked-source qualification executes the real rebuild dispatcher through Begin/Accumulate/Finalize/Stage/Commit, then verifies promotion alone remains degraded. Only the explicit SDK ResolveDeliveryConflictsAsync maintenance call clears the covered audit; new discovery then persists/arms a reminder and reconstruction does not duplicate it. No new operator endpoint is claimed.
- Documented current retry and receipt policy in the current-configuration section of docs/sdk-reminder-process-adoption.md. Preserved five original canonical findings, appended their dated resolutions and marked five structured aliases as counted once. Historical narrative remains intact.

## Verification

- Executed commands, full dependency commits, source/assembly SHA-256, compressed logs and xUnit results are retained in [the evidence packet](evidence/five-deferred-reminder-outcomes-2026-10-28/README.md).
- Final post-review Debug build: zero warnings/errors. Focused classes: 29 passed. Deterministic integration: 1,028 passed. No failures, errors, skips or not-run in either test lane; live Smoke/Platform classes excluded explicitly.
- Whitespace format verification exits 0 with the generic workspace-load warning; no formatting changes required. Git whitespace validation uses core.whitespace=cr-at-eol to honor repository CRLF. Platform startup remains separately blocked by the pre-existing NU1605 downgrades.

## Review Triage Log

- Medium / patch: the due-now diagnostic test required 200219 but not 200203. Require the same actor/reminder, submission-uncertain reason and attempt 1 on the retry event.
- Medium / patch: the due-now synthetic target overwrote receipts and reported every result as new. Reuse the persisted receipt; compare sibling submission identity/payload, require its replayed audit and one write per effect.
- Medium / patch: audit-write failure was absent despite the documented release prerequisite. Add an audit-only faulting store and callback case; preserve the registration audit and retry witness, then settle by receipt replay after reconstruction.
- Medium / patch: backoff suppression was only tested at zero elapsed time. Exercise one tick before the 30-second deadline, require no submission or byte change, then exercise the exact deadline.
- Medium / patch: arm isolation did not assert registered readiness. Require a completed degraded pass with unresolved=1 while failed work remains, then Healthy after successful indexed repair.
- Medium / patch: covering repair had no refusal controls. Attempt resolution before promotion and after a promoted rebuild that omits the parked source, retaining exact unresolved parking, incomplete discovery and zero arms.
- Medium / patch: cancellation failure happened only before external mutation. Add loss of response after removal, assert the external removal happened and the SDK restored a retry registration, then settle after reconstruction.

- All seven findings are verification gaps introduced or exposed by the new qualification; each has a small test-only correction. No runtime API, SDK mechanism or new policy is needed, and no new work is deferred. Post-correction validation is pending.

- Post-correction validation passes: full Debug Integration build has zero warnings/errors; focused reminder classes pass 29/29 and the deterministic integration lane passes 1,028/1,028, with no errors, failures, skips or not-run. The two additional theory rows exercise audit-write outage and cancellation response loss. Parking controls also refuse premature and insufficient repair. Formatting verification passes with the same generic workspace-load warning.
- Independent correction recheck confirms all seven findings are addressed with meaningful assertions and finds no material regression. The earlier eleven-case/27/1,026 notes above preserve the initial pre-review results; final evidence supersedes those totals. No work was deferred.
- Conventional Commit validation accepts test: close five deferred reminder recovery gaps. Final fetch confirms origin/main equals baseline HEAD with zero divergence before commit. The portable evidence packet now binds final post-review sources, assembly, dependency revisions, raw logs and both XML results. No gitlink or production runtime changes.
