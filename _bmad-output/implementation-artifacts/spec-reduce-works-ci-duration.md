---
title: 'Reduce Works CI feedback time without losing release qualification'
type: 'bugfix'
created: '2026-11-17'
status: 'in-progress'
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
- [ ] `references/Hexalith.Platform/eng/verify_works_host.py` — validate evidence prefixes after each producing command; preserve list states and final binding. Record UTC boundaries and monotonic elapsed time, including failures.
- [ ] `references/Hexalith.Platform/eng/test_verify_works_host.py` — exercise malformed first-record early refusal, valid incomplete prefixes, duplicates, list states, final missing-proof rejection and timing/failure retention.
- [ ] `references/Hexalith.CiCd/tools/{repair_works_qualification_cadence.py,tests/test_repair_works_qualification_cadence.py}` — propose source-bound changes to Works `ci.yml`, `qualification.yml` and `release.yml`; preserve pins and unrelated jobs, run full qualification nightly/manually, and require exact-source qualification before release secrets/publication. Apply only through CiCd.
- [ ] `scripts/validate-release-source.sh` and `scripts/tests/test_release_tooling.py` — require exact-source CI and qualification; cover stale, absent, failed and successful runs offline.
- [ ] `README.md` and `references/Hexalith.Platform/docs/works-host-parity.md` — document cadence, qualification, timings and measured limits; add CiCd proposal documentation.

**Acceptance Criteria:**
- Given the failed run's first evidence envelope, when binary preflight finishes, then the verifier rejects it before telemetry, retained startup or recovery begins.
- Given valid partial records including list states, when commands complete, then execution continues; malformed JSON, missing envelopes and duplicates fail with file/line details. Final acceptance still rejects missing proofs.
- Given the selected cadence, when release runs, then exact-source CI and complete qualification are required before publishing.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Verification

- Baseline: `run-with-temp python3 -m unittest -q test_verify_works_host test_works_verifier_command` from Platform `eng/`: 97 tests passed in 10.061s before changes.
- Repeat affected Platform suites. Use `run-with-temp` and private TMPDIR outputs.
- Run owner proposal regressions, offline release-source tests, actionlint and applicable CiCd validation; do not add module YAML assertion tests.
- Run the complete canonical CI profile once on final unchanged source. Retain useful logs outside TMPDIR; report local and hosted evidence separately. No remote dispatch is authorized.
