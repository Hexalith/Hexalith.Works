---
title: 'Reduce Works CI feedback time without losing release qualification'
type: 'bugfix'
created: '2026-11-17'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'f88f99573114fb112a61163dbbd6c88f03df413c'
context:
  - '{project-root}/references/Hexalith.AI.Tools/hexalith-llm-instructions.md'
  - '{project-root}/references/Hexalith.CiCd/AGENTS.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Hosted run 44049423249 spent 44m12s in Platform qualification while ordinary compilation and tests passed in 8m13s. The first persisted evidence record was malformed, but validation rejected it only after another approximately 33 minutes of successful tests.

**Approach:** Validate evidence between commands, expose timings, and apply the selected qualification cadence. Preserve complete qualification for the exact release source.

**Human decision (2026-11-17):** Option A. Ordinary push/PR CI targets the existing approximately eight-minute lane. Full parity runs nightly and on the exact release commit, replacing the previous requirement to run full parity on every push. Implement and verify locally without remote operations. Hosted durations remain measurements to obtain after publication of these changes.

## Boundaries & Constraints

**Always:** Platform owns verifier changes; CiCd owns workflow changes. Preserve ordinary blocking suites, Release/public packages, all eleven rows and 44 proof kinds, Agents Full refusal and green LocalScaffold, final integrity closure and release protection. Preserve the existing Works evidence-writer correction.

**Never:** Shorten recovery budgets, bypass real recovery, remove scenarios, reuse another source's qualification, change production resiliency, update dependencies, commit, push, publish, or edit workflows directly from Works.

</frozen-after-approval>

## Code Map

- `references/Hexalith.Platform/eng/verify_works_host.py` — `run_verification.run`, `bind_persisted_outcomes`, `required_phases`, final `require_unchanged_inputs`; canonical acceptance owner.
- `references/Hexalith.Platform/eng/{works_verifier_command,test_works_verifier_command,test_verify_works_host}.py` — owned process groups and orchestration regressions; reuse `run_until_live_lanes` fixtures.
- `references/Hexalith.CiCd/tools/repair_works_package_metadata.py` — precedent for finite source-bound owner proposals against unadopted Works; existing proposal is not reusable authorization.
- `scripts/{validate-release-source.sh,verify-platform-parity-ci.py}` and `scripts/tests/{test_release_tooling,test_platform_parity_ci}.py` — exact-source release proof and independent complete-gate admission.
- `tests/Hexalith.Works.IntegrationTests/WorksSdkRecoveryProfileTests.cs` — validated twelve-source/five-effect budgets; preserve them and all five recovery topology starts.

## Tasks & Acceptance

**Execution:**
- [x] `references/Hexalith.Platform/eng/verify_works_host.py` — validate evidence prefixes after each producing command; preserve list states and final binding. Record UTC boundaries and monotonic elapsed time, including failures.
- [x] `references/Hexalith.Platform/eng/test_verify_works_host.py` — exercise malformed first-record early refusal, valid incomplete prefixes, duplicates, list states, final missing-proof rejection and timing/failure retention.
- [x] `references/Hexalith.CiCd/tools/{repair_works_qualification_cadence.py,tests/test_repair_works_qualification_cadence.py}` — propose source-bound changes to Works `ci.yml`, `qualification.yml` and `release.yml`; preserve pins and unrelated jobs, run full qualification nightly/manually, and require exact-source qualification before release secrets/publication. Apply only through CiCd.
- [x] `scripts/validate-release-source.sh` and `scripts/tests/test_release_tooling.py` — require exact-source CI and qualification; cover stale, absent, failed and successful runs offline.
- [x] `README.md` and `references/Hexalith.Platform/docs/works-host-parity.md` — document cadence, qualification, timings and measured limits; add CiCd proposal documentation.

**Acceptance Criteria:**
- Given the failed run's first evidence envelope, when binary preflight finishes, then the verifier rejects it before telemetry, retained startup or recovery begins.
- Given valid partial records including list states, when commands complete, then execution continues; malformed JSON, missing envelopes and duplicates fail with file/line details. Final acceptance still rejects missing proofs.
- Given the selected cadence, when release runs, then exact-source CI and complete qualification are required before publishing.

## Implementation Notes

Implementation inspected across Works, Platform and CiCd. The owner materializer applied the three workflow changes; ordinary tests remain blocking and the parity job body/pins are preserved. A failed command retains its original exit failure before evidence parsing.

Verification: 102 Platform tests, 61 Works release/parity tests, nine cadence-owner tests and seven existing metadata-owner tests passed. Actionlint and available script syntax checks passed. The broader CiCd validator passed all 54 tests in a disposable standalone checkout against Builds v6.1.0; no nested submodule was initialized.

The canonical attempt passed 185 Platform and 405 supporting consumer tests, binary preflight, telemetry and retained startup, then exited 1 in recovery after 1021.531 seconds. Bound sources and manifest matched, but Works DLL/PDB bytes changed. Concurrent external commits advanced Works from 151ffb945a7017143010136fedc5762b5b67a18e through e50126cd86a0ea1f455060c3a9616a5b8139e19f and 23d7f1b1d742fa17feab3f9ff220715ce30e32f4 to 057345ab1572bf9dd22617d193d9d7ad3d00dad8, while Platform/CiCd revisions also advanced. Changing Git-generated metadata is the supported explanation; changed binaries were not retained for direct comparison. This is nonqualifying evidence, and later live lanes did not run. No integrity assertion was relaxed.

The external /pushall operation committed this implementation and unrelated work; this build session performed no Git mutations or remote dispatch/publication. Preserve those commits and all unrelated changes. Hosted duration remains unmeasured. Complete local stable-source qualification subsequently passed as recorded below.

Post-review verification passed: 102 Platform tests, 61 Works release/parity tests and 17 CiCd owner tests; all owner whitespace checks pass. A fresh complete gate is running from detached private clones of Works and ten required direct dependencies. Its input revisions/overlays and durable evidence are under `/home/administrator/.codex/artifacts/works-ci-duration-stable-20261117T113744Z`; no original repository is mutated.

Final verification: the complete Release/package canonical gate passed from detached private copies of Works and ten required direct dependencies, with DOTNET_PROCESSOR_COUNT=4 matching the hosted test parallelism. All eleven rows and 44 proof kinds passed, complete rollback and final integrity closure passed, and all 603 xUnit tests passed with zero skips. Initial and final Works DLL/PDB hashes are identical. Durable evidence and input revisions/overlays: `/home/administrator/.codex/artifacts/works-ci-duration-stable4-20261117T114900Z`.

The first immutable attempt used fourteen test threads and stopped after 404/405 supporting tests because one Redis fixture exceeded its unchanged startup deadline. Its evidence is retained separately under `/home/administrator/.codex/artifacts/works-ci-duration-stable-20261117T113744Z`. The four-processor retry changed only verification environment parallelism; every test, proof, timeout and product budget remained enforced. Earlier run logs were relocated outside the source checkout under `/home/administrator/.codex/artifacts/works-ci-duration-prior-20261117T115656Z`; every tracked file committed by external activity was preserved.

Review is complete: the real-owner guard, exact monotonic durations, launch-error retention and durable README evidence example are covered. Final regression checks passed: 102 Platform tests, 61 Works release/parity tests, 17 CiCd owner tests, and 54 CiCd validator tests. Only owner-reviewed cleanup of diagnostics committed by external synchronization remains deferred. No commit, push, release dispatch or publication was performed by this build session; local review follow-ups remain uncommitted under the approved no-commit constraint.

## Spec Change Log

## Review Triage Log

| Finding | Verdict | Evidence and route |
|---|---|---|
| Blind 1: scalar/null state | false | Prefix admission deliberately validates envelopes; field types and cross-phase predicates remain final-binding responsibilities. No row can qualify from an invalid scalar, and the prior decoder allowed these values. |
| Blind 2: no newly appended record | false | The mTLS command receives the shared evidence path but intentionally proves transport via its XML/host starts rather than appending a phase. Requiring a new record per command would reject the preserved gate. |
| Blind 3: permissive JSON constants/duplicate keys | low | Inherited json.loads behavior; trusted System.Text.Json writers emit neither form. Final predicates remain enforced. Reject additional decoder callbacks for an unobserved producer path. |
| Blind 4: partial writes after I/O failure | low | Sequential writes can leave a partial one-shot local migration if a later write fails; pre-write input validation is present. Reject rollback machinery for this uncommon local I/O failure. |
| Blind 5: receipt collision/symlink | low | Normal prepare creates a fresh proposal directory, and a second apply refuses changed workflows before receipt writing. External receipt tampering is outside the ordinary path; reject added collision machinery. |
| Blind 6: incomplete canonical run | medium | Verified exit 1 at retained rollback, before later lanes. Concurrent external commits advanced source identities during execution. That attempt stays nonqualifying; the deferred stable-source qualification is now resolved by the complete passing immutable run below. |
| Blind 7: bulk committed evidence | medium | External /pushall committed generated diagnostics while the run was active; this session performed no commit. Preserve those external commits per user instructions and defer owner-reviewed artifact relocation. |
| Blind 8: missing retained logs | false | Direct filesystem checks find 22 durable .log files, including run.log, platform-recovery.log (4339 bytes) and agents-full-refusal.log. Git/ripgrep ignore rules do not imply absence. |
| Blind 9: README deletes evidence | medium | The example uses only TMPDIR and exits without copying output. Patched the example; stubbed statuses 0/17 retain root/gate evidence, clean scratch and preserve the verifier status. |
| Blind 10: launch-error regression absent | low | Current tests cover nonzero and timeout exceptions but omit FileNotFoundError. Patched existing command-outcome coverage with FileNotFoundError, an empty log and absent exitCode. |
| Edge 1: partial writes after I/O failure | low | Same verified one-shot I/O behavior as Blind 4; reject added transaction/rollback complexity for the uncommon local failure. |
| Verification 1: official-owner guard stubbed | medium | Pre-verified mutation removes the actual origin guard while all nine owner tests remain green. Patched a real-owner fixture: foreign origin refuses before workflows or receipt change. All ten cadence-owner tests pass. |
| Verification 2: elapsed duration can be zero | medium | Pre-verified mutation replaces elapsedSeconds with zero without failing existing >=0 assertions. Patched controlled-clock assertions for exact 2.5/7.25-second deltas across success, nonzero, timeout and launch error. |

## Verification

- Baseline: `run-with-temp python3 -m unittest -q test_verify_works_host test_works_verifier_command` from Platform `eng/`: 97 tests passed in 10.061s before changes.
- Repeat affected Platform suites. Use `run-with-temp` and private TMPDIR outputs.
- Run owner proposal regressions, offline release-source tests, actionlint and applicable CiCd validation; do not add module YAML assertion tests.
- Run the complete canonical CI profile once on final unchanged source. Retain useful logs outside TMPDIR; report local and hosted evidence separately. No remote dispatch is authorized.
