---
title: 'Close five deferred planning and evidence gaps'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five distinct unresolved deferred items in the Works planning and verification records: restore the Epic 4 persisted end-state and platform lifecycle/recovery evidence requirement; restore terminal mutation rejection with defined idempotent no-op exceptions; distinguish idempotent no-op from accepted work in headless outcomes; label spec-10 verification as historical with its superseding spec-11 evidence; and include spec-10 in its dated parent-story closeout inventory. Preserve original historical results and frozen intent. Add focused regression coverage, update each ledger item with evidence, independently review, and commit and push main as requested. The aggregate eight-context-correction entry aliases its constituent findings and is not a sixth item.

</frozen-after-approval>

## Implementation Notes

- Clean main baseline: `2e331b01237a15686a7722a867a8edb956042dc3`. Small documentation correction plus one architecture-test class; no intent gaps, irreversible implementation operations, runtime APIs, dependency changes, migrations or production actions. The user explicitly authorizes commit and push. Use the oneshot route.
- Read root guidance, required Hexalith LLM/Git baseline, EditorConfig, SDK and build/test configuration, architecture spine and relevant epics/lifecycle authorities. There is no Works AppHost or runtime change; a live topology would not validate these documentation contracts.
- Independent read-only investigation confirmed all five omissions at baseline. Canonical ledger summaries: C12-BH9 runtime persisted-end-state/recovery evidence; C27-EC8 terminal/no-op rule; C29-BH4 distinct no-op outcome; and the two unstructured 2026-09-21 spec-11 findings about historical verification and the spec-10 inventory. The eight-correction aggregate contains three selected findings and five already resolved findings; close it only as an alias, counted once per canonical finding.
- Code map: `epic-4-context.md`, Requirements/UX sections; `spec-4-8-register-and-reconcile-date-reminders-durably-10.md`, Verification; parent `4-8-register-and-reconcile-date-reminders-durably.md`, dated spec-10 File List. Reuse the section-scoped documentation-test style of `LifecycleTransitionMatrixDocTests` and `SubscriberDeadLetterOperatorDocumentationTests` in the Architecture project.
- Authorities: epics NFR15 requires persisted end-state at integration boundaries and platform-hosted lifecycle/recovery; UX-DR1/18 requires a distinct idempotent outcome; lifecycle matrix terminal-row rule and pure `WorkItemLifecycle` preserve defined duplicate no-ops. Spec-11 and its parent closeout record 41 harness / 534 non-smoke passes, no repeated Tier-3 run and the preceding 0/4 result. Historical spec-10 33/526/4 values remain historical, never relabeled as fresh proof.
- Verification plan: run five new section-scoped facts before corrections to demonstrate all five gaps; correct documents and rerun; execute the full Architecture project and existing lifecycle unit class; retain commands, result XML, source hashes, exact revision and review triage in a portable evidence packet. Verify unchanged frozen blocks and exact ledger insertions, run whitespace and commitlint checks, fetch and inspect divergence, then commit and push main.

- Added five section-scoped Architecture facts. All five fail against the unmodified documents and pass after correction with the same assembly. Debug Architecture and Unit builds pass with zero warnings/errors; 144 existing lifecycle cases pass. No live topology or runtime acceptance is claimed.
- Full Architecture qualification executes 404 cases: 403 pass and one existing revision-marker gate fails. API/boundary markers still name `39a3d4dc98b786244e732d628a0828b9d19ad96e`, while the unchanged gitlink names `36c78f3435d2d492a73f1b0707cf865c8735d740`. The failing source and both marker files are byte-identical to baseline; retained integrity evidence establishes the pre-existing mismatch. The gate remains intact.
- Ledger update adds exactly twelve annotation lines: five canonical findings and one aggregate alias. Removing those insertions restores the original ledger byte-for-byte. All frozen-intent blocks remain unchanged. Commands, red/green XML, broad failure and source hashes are retained in [the portable packet](evidence/five-deferred-planning-evidence-2026-10-28/README.md).

## Review Triage Log

- Low / patch: review records were referenced before retention. Added the full nine-finding review and dispositions in the packet's review.md.
- Medium / patch: a generic 4/4 match did not protect the original live observation. Require its full dated-subsection row, duration and skip count. The subsidiary reviewer claim that 4/4 matches 0/4 is false; the explanatory 4/4 occurrence was the actual bypass.
- Medium / patch: terminal no-op coverage omitted the non-requeue Reject condition. Added an explicit requirement assertion.
- Medium / patch: runtime evidence coverage omitted restart/retry and the API/mock-only prohibition. Added both assertions.
- Medium / patch: historical evidence coverage omitted the current-tree boundary. Require that neither checkpoint qualifies the current tree.
- Medium / patch: link string assertions did not validate targets. Read the lifecycle and spec-11 targets; require the lifecycle heading and superseding counts.
- Low / patch: integrity.json was not included in artifact hashes. Bind it in the manifest.
- Medium / patch: outputs lacked command exit codes. Record exact argument arrays, process return codes and log/XML mappings; final invocations capture those values directly.
- Medium / patch: integrity checks were not reproducible. Retain verify.py and its exact invocation to recompute frozen blocks, source/artifact hashes, XML counts, five closures plus one alias and unchanged original ledger bytes.

- All nine review findings receive small in-scope corrections; no work is deferred. Final post-review build/focused/full/format results are retained separately from the initial red/green/full lanes. The final build and format check exit zero, final focused facts pass 5/5, and final Architecture remains 403/404 with exactly the existing marker mismatch.

## Verification

- Independent correction recheck confirms all nine findings resolved and no unresolved in-scope issues. The reviewer reran verify.py successfully and verified current source/artifact and assembly hashes. No new work is deferred.
- Final Debug Architecture build: zero warnings/errors. Final focused regressions: 5/5 passed. Existing lifecycle unit cases: 144/144 passed. Final full Architecture: 403/404 passed, with only the unchanged P0 revision-marker failure. There are no skips/errors/not-run. Final format check exits 0 with the generic workspace-load warning.
- Portable integrity verification exits 0: frozen intent unchanged, original ledger bytes preserved after twelve inserted lines, five canonical closures plus one alias, all source/artifact hashes and XML totals consistent. Exact commands, numeric exit codes and compressed raw results are in the packet manifest.
- Conventional Commit validation accepts `docs: close five deferred planning and evidence gaps`. Fetch at review time confirmed zero divergence from origin/main. Final push uses the reviewed main branch; no runtime source, dependency, gitlink or production configuration changes.
