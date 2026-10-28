---
title: 'Qualify five deferred runtime and build safeguards'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five current deferred verification items together, as requested:

1. DW-60: independently qualify caller-cancellation and probe-deadline behavior in the current shared AppHost harness, including deterministic-lane reachability.
2. DW-72: independently qualify the current SDK projection ordering replacement, including stale redelivery, conflicting identities and persisted state after reconstruction.
3. DW-74: independently qualify authoritative roll-up reconciliation through the current SDK lifecycle, including child membership, refusal and atomicity.
4. DW-83: independently qualify current evaluated MSBuild dependency discovery, including imported/configuration-dependent references and fail-closed canonical identity.
5. The 2026-10-22 Story 4.9 follow-up: prove that transitive external source-reference chains do not create duplicate MSBuild nodes with divergent global properties.

Use current source and executing controls rather than treating historical findings as current defects. Add missing regression coverage and make only small corrections supported by reproduced failures. Preserve original evidence, SDK/Platform ownership and existing build gates. Close only the five selected ledger entries after independent review and focused verification. Commit with validation and push to main as requested.

</frozen-after-approval>

## Implementation Notes

- Clean main baseline: e53439bf08b87ea8041e5147a18a706055ad30db. Explicit user scope is five deferred items and push to main. Intent gaps: none. Irreversible implementation operations: none. Footprint: root test controls and qualification evidence, spec and ledger; no new public API, dependency update, data mutation or production operation.
- Read root guidance, Hexalith LLM/Git baseline, EditorConfig, build/test configuration and architecture ownership constraints. The four numbered entries request deliberate follow-up review of older implementations; current SDK/harness replacements must be reviewed and exercised.
- `WorksAppHostSmokeHarnessTests` is outside the excluded `*SmokeTests` classes; its existing TCP race controls cover caller, timeout, simultaneous and foreign cancellation. `WorksSdkProjectionQueryTests`, `WorksSdkPartitionedRebuildTests` and `WorkItemSharedProjectionRebuildHandlerTests` exercise current durable ordering/reconciliation. `DependencyDirectionTests` exercises current evaluated MSBuild imports, paths and build lanes.
- Extend `scripts/tests/test_transitive_project_reference_globals.py`: its existing control queries SDK-added metadata but does not execute builds and count node identities. Use an isolated package-free graph and actual root targets, explicit/default version cases and Debug/Release lanes. Keep temporary outputs outside ordinary repository outputs.
- Required baseline command `aspire start --non-interactive --apphost /home/administrator/projects/hexalith/works/references/Hexalith.Platform/apphost.cs --format Json` exited 2 with NU1605 Dapr hosting and EventStore Aspire/ServiceDefaults downgrades. Raw output: `/tmp/works-five-deferred-proof-audit-baseline.log`. `aspire ps --format Json` reported no running hosts. This batch does not claim live Platform requalification.
- A line-ending normalization error truncated three owned tracked edits and this new spec. Restored the three tracked files directly from the clean canonical baseline and re-applied the exact intended diff; recreated the identical intent block. Final rebuild and diff inspection verify the corrected files.

- Independent runtime and MSBuild reviewers found no surviving production defect in the four requested follow-up reviews. Added three missing coverage controls: real default TCP timer wiring; terminal stale replay preserving the complete persisted family and served queue; both inherited-global removal metadata spellings. The fifth item adds actual parallel diamond builds and a guard-removal sensitivity control. No production runtime or build target changed.
- Preserved the ledger's existing LF style and limited its changes to the five selected resolutions. Historical specs and review recommendations remain intact as evidence.

## Review Triage Log

- Medium, patched: positive graph builds inherited ambient BuildInParallel; pass `-p:BuildInParallel=true` alongside `-m:4` explicitly.
- Medium, patched: eventual timer cancellation did not reject an immediate-cancel mutation. Execute the actual default two-second timer and require at least 1900 ms elapsed, with a ten-second hang guard.
- Medium, patched: supplying an arbitrary test budget bypassed production budget wiring. The socket wrapper and injected connection now share the port overload that owns the default timer; the test checks the selected port as well as cancellation.
- Low, patched: stale-redelivery comparisons omitted other runtime-family documents. Compare topology, pending dates, parking, history manifest and every referenced history page, as well as queue/roll-up bytes and generation.
- Medium, patched: terminal replay did not execute the served queue query. Assert successful what's-next results with zero terminal items and one nonterminal item.
- Low, patched: the intermediate evidence snapshot referenced files not yet copied. Final packet includes the manifest, compressed logs and both xUnit XML results; validate every evidence hash before commit.
- Low, patched: ledger normalization introduced unrelated line-ending churn. Restored its original LF encoding; the diff contains only the five intended resolutions.
- Independent correction recheck confirmed all five code findings resolved with no new material code problem. No new work was deferred.

## Verification

- Debug Integration and Architecture project builds with `--no-restore -m:1 --nologo`: zero warnings/errors.
- Five selected Integration classes: 149 passed, zero failures/errors/skips/not-run, including both stale-replay theory cases and the default timer control. Four selected Architecture classes: 274 passed with no failures/errors/skips/not-run, including both global-removal spellings.
- Final `python3 -m unittest discover -s scripts/tests`: 98 passed, including all three graph tests. Four actual parallel positive solution builds plus the temporary guard-removal negative pass their intended oracles.
- Script syntax, pinned Ruff, commitlint and whitespace checks pass. Before/after test-assembly hashes match. Exact commands, source/assembly hashes, class counts, raw logs and XML are retained in [the evidence packet](evidence/five-deferred-guard-qualification-2026-10-28/README.md).
- Required exact Platform startup exited 2 with pre-existing NU1605 downgrades; this is separate from the focused passing evidence. No fresh live Platform qualification is claimed.
