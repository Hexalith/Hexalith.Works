---
title: Repair Timesheets release tooling audit
status: done
baseline_commit: 4181f242bc879a2c547a5a7390692f8c7dfde6b9
review_loop_iteration: 0
context:
  - /tmp/works-timesheets-catalog-operation-20261101/AGENTS.md
  - /tmp/works-timesheets-catalog-operation-20261101/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /tmp/works-timesheets-catalog-operation-20261101/references/Hexalith.AI.Tools/hexalith-git-instructions.md
  - /tmp/works-timesheets-catalog-operation-20261101/package.json
  - /tmp/works-timesheets-catalog-operation-20261101/.releaserc.json
  - /tmp/works-timesheets-catalog-operation-20261101/scripts/repair-release-tooling-bundles.mjs
  - /tmp/works-timesheets-catalog-operation-20261101/scripts/tests/test_bundle_repair.py
  - /tmp/works-timesheets-catalog-operation-20261101/scripts/tests/test_release_tooling.py
---

The user requested current packages, working CI and releases, waived human validation, and explicitly included Timesheets. The catalog-only repair is already pushed as4181f24 and ChatBot adopted it as48a1dfe. Current Timesheets CI41306602617 fails its release-tooling npm audit with36 moderate findings. The separate integration failure remains open; this story must not claim to fix it.

<frozen-after-approval>
Repair the actual npm audit failure while preserving the configured NuGet-only release process and all meaningful automated security/release checks. Use a supported locked dependency graph and a fresh normal installation. The release configuration explicitly selects commit-analyzer, release-notes-generator, exec and github; it does not use semantic-release/npm. Preserve that behavior and the existing NuGet release commands. Do not downgrade semantic-release, suppress audit severity, omit development dependencies from the audit, skip checks, or modify npm's implementation to hide vulnerable bundled dependencies. Keep the SDK, .NET/Dapr/application dependency selection and runtime/domain/auth code unchanged in this story.
</frozen-after-approval>

## Owner and code map

Work only in /tmp/works-timesheets-catalog-operation-20261101. It is a separate sparse clone on main, clean at4181f24, with scripts hydrated and ordinary AI.Tools guidance copied into its root-declared location. No nested Git dependency has been initialized. Preserve concurrent/user changes and existing sparse state. Read relevant tracked guidance/config/test files before editing. Needed source paths can be hydrated from this repository; do not initialize nested submodules.

package.json pins commitlint21.2.2, semantic-release25.0.9 and a postinstall bundle-repair helper. The helper replaces integrity-pinned ip-address/undici dependencies under npm, but current audit still rejects npm's other bundled dependencies. package.json and .releaserc.json explicitly define the NuGet-only process. Inspect the actual current audit before choosing the smallest coherent fix. Root Works already uses the supported exact override semantic-release -> @semantic-release/npm: npm:@semantic-release/error@4.0.0; its fresh install audits0 and the actual semantic-release loader resolves all4 intended plugins/16 lifecycle steps. That is a relevant option, not permission to weaken existing checks. If the unused npm graph is removed, retire or adapt its now-obsolete repair hook coherently and preserve meaningful effective-configuration/security tests.

## Scope

Allowed: package.json, package-lock.json, scripts/repair-release-tooling-bundles.mjs and its directly related scripts/release-tooling-bundle-repair data, scripts/tests/test_bundle_repair.py, scripts/tests/test_release_tooling.py, and one small verification helper or CI wiring change only if needed to preserve an actual mandatory security check. Preserve historical evidence. Do not change .releaserc.json publishing commands, runtime projects, global.json, CPM, gitlinks, story gates, deployment rules, or unrelated workflows. Record new/removed paths and test method deltas explicitly; do not delete tests solely to obtain green counts.

## Tasks & Acceptance

- [x] Confirm the current audit cause and preserve its exact scope before edits.
- [x] Implement the smallest supported locked graph change and coherent installation/security checks.
- [x] Run fresh normal npm ci, then full npm audit with development dependencies; require0 findings.
- [x] Resolve the actual semantic-release effective configuration in a fresh installation and demonstrate all4 intended plugins/lifecycle hooks load. No publication or external release lifecycle may execute. NuGet release commands and configuration bytes must match baseline.
- [x] Run the actual affected release-tooling tests, including meaningful negative controls that survive the new graph. Use real execution results and report baseline/current method counts with phase and cumulative deltas.
- [x] Preserve normal commit validation: owning pinned CLI/config21.2.2 unless an authorized ordinary tooling update deliberately selects the current21.2.4 together; validate exact candidate messages before any eventual parent commit. Do not commit or push in this implementation handoff.
- [x] Return all changed paths, before/after graph/audit evidence, commands/results and remaining limitations. This is not full Timesheets CI, SDK acceptance or a release.

## Execution

Registry reads/installations required for this repair are authorized. Do not perform Git remote operations, commits, pushes, merges, releases or package publication. Do not touch SDK/Root/ChatBot worktrees. Keep artifacts in a dedicated ignored workspace evidence directory or task-specific /tmp directory and preserve hashes. Run sequentially within this story. Stop with a concrete explanation if an essential fact cannot be determined; do not ask for a human validation already waived by the user.

## Change Log

2026-11-01: scoped the actual audit repair at4181f24 after explicit Timesheets inclusion. Implementation and verification pending.

Parent verification: full source-hydrated npm test:release-tooling passes105 tests,0failures/errors. Original sparse misses were environment failures; no test was skipped or changed to accommodate missing files. Evidence copied unchanged to .artifacts/upgrade-release-orchestration-20261101/timesheets-tooling-evidence, with parent-verification.json. Six tracked paths changed; original NuGet configuration SHA741d578d63845bb3aa78f9928a65ba1aa2c3d1805345fd5c8a5667ca4e4a2d2d is unchanged. Implementation remains uncommitted pending independent review.

## Review Triage Log

All three fresh review layers returned final before classification. Each finding keeps its own verdict before grouping. Only two direct test corrections survive; no specification loopback or runtime/dependency-source changes are required.

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | low | reject | A privately inserted nested installed npm is not produced by the valid locked npm-ci path: all npm lock paths are already rejected, and full audit/signatures run after installation. No actual installation divergence was shown. A recursive installed-tree guard adds complexity for manual corruption beyond this maintenance. |
| blind-2 | low | reject | Jointly replacing both compared integrity values requires changing the committed locked source; real npm-ci and npm audit signatures validate artifact integrity independently. The verifier compares the alias to the canonical locked package, not a second hardcoded registry catalogue. Reject a new private-corruption guard. |
| blind-3 | low | reject | The genuine installed selected plugins are locked, restored with npm-ci, and verified with npm audit signatures. Replacing private plugin payload/identity with fabricated exports bypasses those independent stages rather than showing a wrong real loader result. Do not duplicate npm identity/signature verification. |
| blind-4 | low | reject | Actual .releaserc.json remains byte-identical to baseline (SHA741d578d63845bb3aa78f9928a65ba1aa2c3d1805345fd5c8a5667ca4e4a2d2d), and the real loader preserves its exact plugin options. The deliberate arbitrary-command fixture verifies no lifecycle execution. Adding fixed command literals to this graph verifier would newly govern unrelated future release configuration. |
| blind-5 | low | reject | Branches and tag convention are unchanged in the same baseline-identical release configuration. No branch/tag regression was caused or exposed by removing an unused npm dependency. Reject new configuration-policy guards against hypothetical source rewrites. |
| blind-6 | low | patch | Confirmed validator uses commands.index and protects only the first exact installation. Direct correction: iterate every matching installation, preserving the existing required sequence and add a later-install negative control. |
| blind-7 | low | reject | The complete current workflow inventory has exactly the two relevant npm installations in ci.yml and release.yml, both covered. Dynamic future-workflow discovery changes test governance for files not yet present; no current uncovered installation exists. Reject the additional future-input guard. |
| blind-8 | low | reject | Both actual installations and their verify/full-audit/signature steps are in the same job. The demonstrated cross-job rewrite is a hypothetical future workflow transformation, not current reachability. Job-aware YAML parsing would add a new guard beyond the direct all-occurrences correction. |
| blind-9 | low | reject | Actual audit/signature steps are unconditional and blocking. if:false and continue-on-error mutations are hypothetical unrelated future workflow changes; no skipped or ignored real audit was shown. Reject broader workflow-policy parsing. |
| blind-10 | medium | patch | Confirmed writable links into the owning installation are excluded by owned_payloads, so the changed preservation assertion can miss a verifier mutation and expose real dependencies. Use fully isolated dependency payload copies and snapshot actual files/structure; include a mutation control for a selected plugin. |
| edge-1 | low | patch | Independently confirms the same later-install hole as blind-6; preserve this individual verdict before grouping. Iterate every existing exact install occurrence and refuse a later unguarded install. |
| verify-1 | medium | patch | Pre-verified gap: private 86MB copy demonstrates a selected exec-plugin append while all12 checks pass. Protect all installed dependency payloads with private copies and a direct mutation negative control, preserving the real installation. |

## Final Repair and Parent Verification

The same implementation owner applied both direct review corrections only in
scripts/tests/test_bundle_repair.py. Every matching installation now receives
its own required verification/audit/signature sequence. Fixtures copy all
dependency payloads privately and snapshot contents, entry types, permissions
and symlink targets. A selected-plugin mutation control proves detection and
checks that the owning installation remains unchanged. The focused suite passed
14 tests with no skips, phase +2 / cumulative +8 affected methods.

Parent command npm run test:release-tooling passed all107 tests in12.453s,
with no skips or failures; /tmp/works-timesheets-parent-final-tooling.log retains
the complete run. The earlier normal npm-ci, full audit0 and317 signature/56
attestation checks remain valid because no production graph/configuration file
changed during this repair. The six-path source scope and byte-identical
.releaserc.json are preserved. git diff --check and own-pinned21.2.4 commitlint
pass before the normal commit. No runtime/domain/authentication/gitlink change
is included. The separate existing endpoint-inventory integration failure
remains open and prevents claiming full Timesheets CI/release acceptance.
Human validation and push were explicitly authorized by the user.
