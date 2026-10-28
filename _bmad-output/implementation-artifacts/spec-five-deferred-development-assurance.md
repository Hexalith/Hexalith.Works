---
title: 'Close five deferred development assurance gaps'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five deferred development-verification items: reconcile the retired Story 6.1 evidence-project authority, unbounded public-package replay commands and unescaped rollback state-key findings; explicitly bind supported Debug source revisions to their separate Release package versions; and supply an executable local Debug source compile gate. Preserve historical findings and explain supersession precisely. Keep CI in the required Release/package mode, verify the local gate against the full source solution, independently review the result, and commit and push main as requested.

</frozen-after-approval>

## Implementation Notes

- Clean main starting revision is captured directly from Git in the verification packet. Read required LLM/Git baseline, root guidance, build/test configuration, architecture AD-19/20/27/28, current source-characterization guard and source/package adoption documents. The user explicitly requests a five-item batch and main push. This is a small development-tool/documentation change with no unresolved intent choices, public runtime API, dependency update, data migration or publication; use oneshot. There is no Works AppHost to start and no topology change.
- Code map: add `tools/source-debug-dependencies.json` naming the current EventStore and PolymorphicSerializations source revision/Release-version pairs. They are intentionally independent producers; exact source/package equality is not claimed. Add `scripts/verify-source-debug.py` to compare manifest, root gitlinks, selected checkouts and evaluated Release pins before restoring/building the full `.slnx` in explicit Debug/source mode. Pin these source roots in the command so ambient root overrides cannot escape preflight. Bound external commands, retain command logs/results, preserve failure codes, and never initialize dependencies automatically.
- Add executing Python controls in `scripts/tests/test_source_debug_verification.py` for valid pairings and independently changed version/gitlink/checkout, missing dependencies, failed/timed-out tools and actual selected Debug build arguments. Reuse existing C# source-marker drift and source API characterization tests. Update only maintained current revision markers and current-pin prose after actual source/API tests pass; historical proof identities remain untouched.
- README documents the reproducible local compile gate. CI remains Release/package-only per the required Hexalith baseline. The historical request for CI Debug compilation is replaced by separate explicit local source validation; do not claim a hosted Debug CI lane or current live parity.
- EventStore `6a0b6441f6d78a3007678a1d0ec336e9cb8ff961` deleted historical evidence and its replay machinery. Verify ancestry and exact removed paths before marking the three original Story 6.1 findings superseded; do not requalify deleted replay programs or change owner files. Retain a compact integrity-verifiable retirement report.
- Annotate exactly five canonical ledger items, preserving every original byte. Run full local Debug restore/build, source/API and Architecture tests, focused Python controls, appropriate script lint, whitespace and Conventional Commit validation. Retain exact commands/results and artifact hashes under `evidence/five-deferred-development-assurance-2026-10-28`. Distinguish any broad blocker from passing focused checks. Independent blind-hunter review precedes finalization; record all findings and dispositions. Fetch and inspect divergence before pushing main.

- Initial full local source gate passes restore and all solution projects with zero warnings/errors. Fresh Python tooling suite passes 104/104; focused source admission/lifecycle controls pass 6/6. Unit 597/597 and Property 3/3 pass. Architecture and deterministic Integration are running. The first attempted Integration invocation used unsupported `-notclass` and exited 3 before execution; runner help confirmed `-class-`, and the corrected command is retained separately.
- Source overlay recording now includes untracked files as well as tracked changes. Reusing an artifacts directory invalidates an earlier success before any new command starts. Current-marker reconciliation changes only the maintained markers/current-pin prose; historical producer identities remain untouched.

- To enforce producer drift on every push without compiling Debug in CI, the tooling job now invokes `--check-only`: only Release/package evaluation runs, after root EventStore/PolymorphicSerializations initialization. Its logs/result are uploaded even on failure. The new focused control verifies that this mode performs neither restore nor compile. Actual CI-profile preflight passes locally. No dependency revision changes.
- Complete .NET deterministic verification passes with no failures/errors/skips/unexecuted cases: Architecture 404/404, Unit 597/597, Property 3/3 and Integration 1043/1043. The source-characterization suite includes every current API and independent marker drift control. Live Platform/Smoke classes remain outside this deterministic lane. Five ledger closures add exactly ten annotation lines; deleting them restores the original baseline bytes.

## Review Triage Log

- Low / patch: malformed JSON containers/revisions could leave a running report. Strict manifest shape validation now precedes external commands; full-pipeline null/array/object/entry/revision controls require failed reports.
- Medium / patch: timed-out tools could leave descendants writing to the workspace. Isolated process groups and bounded tree termination replace direct-child-only timeout handling; an actually started child cannot write after the deadline. Windows taskkill logic is inspected, not executed on this Linux runner.
- Medium / patch: shell argument arrays do not escape MSBuild commas/semicolons. Property values now escape MSBuild syntax; an actual package-free MSBuild control round-trips a path containing comma, semicolon, percent and literal property syntax.
- Medium / patch: inherited Git selectors and replacement refs could redirect proof inspection. Both gate and retirement replay sanitize Git variables, disable replacements/lazy fetches and verify physical worktree roots. Actual poisoned-selector preflight/replay pass.
- Medium / patch: filenames alone did not identify source overlays. Tracked diff digests, untracked content hashes and source revisions are recorded and compared after compilation; a changed source snapshot refuses the result.
- Medium / patch: committed-only gitlinks prevented precommit verification and ignored staged drift. The gate now checks stage-0 indexed gitlinks, preserving a local verification path for proposed dependency pairings. Documentation distinguishes the separate committed-source Architecture marker check.
- Medium / patch: direct validator tests did not prove pipeline admission. Independent gitlink/checkout/version mutations in both modes now fail before restore/build; disabling the admission function produces six expected failing controls.
- Low / patch: the test helper substituted a valid manifest for falsey inputs. It defaults only on None; explicit empty-object and full-pipeline null controls exercise the supplied malformed values.

The independent correction recheck confirms all eight original findings corrected, executes 14/14 focused controls and the retirement replay with poisoned Git selectors, and finds no remaining issue in the bounded recheck. No findings are deferred.

## Verification

- Final local Debug/source solution gate passes all 22 bounded commands, including full restore/build with zero warnings/errors and matching before/after source snapshots. Actual Release-only CI identity preflight passes. Both governed source checkouts are clean; every source/package revision remains explicit.
- Complete fresh tooling suite: 112/112 passed. Focused review controls: 14/14 passed. Architecture 404/404, Unit 597/597, Property 3/3, final deterministic Integration 1043/1043: 2,159 tests total across the four .NET suites and Python tooling suite, with no failures/skips/unexecuted cases. Integration is rerun after the final source build; other test assemblies retain their original pre-execution timestamps. No fresh live Platform parity is claimed.
- Retirement normal and optimized replay pass with identical results; the removed-admission mutation triggers six expected failing controls. Five canonical ledger closures preserve every original ledger byte. Pinned Ruff, actionlint, CRLF-aware whitespace and Conventional Commit validation pass.
- Commands, results, compressed logs/XML, source/assembly hashes, retirement trees/ancestry, ledger preservation and independent review are retained in [the evidence packet](evidence/five-deferred-development-assurance-2026-10-28/README.md). Source-pair checks run in CI without Debug compilation; hosted CI execution begins only after the authorized push. Fetch confirmed zero divergence from origin/main before finalization. No dependency revisions, production configuration or publication changed.
