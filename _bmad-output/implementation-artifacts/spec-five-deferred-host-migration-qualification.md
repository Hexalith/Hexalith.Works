---
title: 'Close five deferred host migration qualification gaps'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five related deferred host-migration findings: the retired Story 4.8 owned-control-plane verification path; configuration and caller-policy trust-domain assertions; routing remaining live starters through the shared harness; the retired mixed Release AppHost graph; and compatibility of the Works host with the SDK startup route inventory. Qualify the current replacement contracts, add missing focused regressions, and reconcile exactly these five ledger entries while preserving their original evidence. Distinguish superseded Works topology requirements, historical live qualification, and fresh deterministic checks. Independently review, then commit and push main as requested.

</frozen-after-approval>

## Implementation Notes

- Clean main baseline: `68dc24b32c3322fb8cc0667f10dbeb59c9d0ba9e`. Read root instructions, required LLM/Git baseline, EditorConfig, build/test configuration, architecture AD-19/AD-20 and cutover documentation. Read-only delegated investigation confirmed the five canonical entries and current owners. This is a small test/documentation change with no unresolved intent choices, new runtime API, dependency change, migration or production action; use the oneshot route. Commit and push are explicitly authorized.
- Code map: `WorksSdkDaprConfigCopyTests.cs` already compares fixture policy copies but omits actual trust values. Pin all four selected Platform YAML files: control-plane trust is localhost, workload ACL and every present caller policy are public. Assert expected caller counts to prevent vacuous success. Apply the same assertions to optional fixture copies.
- `WorksSdkProtectedHttpTests.cs` already starts the actual thin SDK host and exercises protected projection, delivery and actor discovery. Add startup refusal controls for unprotected canonical projection and delivery routes. Preserve existing authorized/denied HTTP checks.
- Reuse `PlatformHostingBoundaryTests`, `DependencyDirectionTests.P0_ExternalHexalithDependenciesUseDebugSourceAndReleasePackages`, `WorksAppHostSelectionTests`, `WorksAppHostSmokeHarnessTests` and discovery-isolation tests. Audit all six current live classes and seven explicit facts for shared-harness routing. Existing Release dependency checks distinguish production package references from legitimate source-only Platform test adapters.
- Record the replacement map in `docs/platform-hosting-cutover.md`, then add status/resolution annotations to exactly five matching ledger entries. Do not change frozen historical intent or claim a fresh live parity run. Existing immutable Story 4.9 packets document the historical transfer; current production admission and unrelated control-plane health findings retain their status.
- Verification: Debug builds and focused direct xUnit assembly invocations; trust-field mutation controls against isolated configuration copies; complete Architecture suite with any pre-existing blocker reported separately; formatting/whitespace and commitlint. Retain commands, exit codes, logs/XML and source hashes in a portable evidence packet. Fetch and inspect divergence before committing and pushing main.

- Initial negative-control correction: a plain duplicate /work/events endpoint has no Dapr topic metadata and is not a subscription for inventory purposes. Use the recognized /dapr/config sidecar-discovery route alongside /project; existing HTTP coverage still qualifies the real subscription channel. This changes only the test input, not the SDK or host policy.

- Implementation adds four parsed configuration/copy cases and two startup-refusal cases, plus the five-row hosting replacement map. All six current live classes are audited: twelve shared-harness calls; the existing selection test checks seven explicit facts. Exactly ten new ledger lines annotate five original entries; every original byte is preserved.
- Verification: both Integration Debug builds and the Architecture Debug build have zero warnings/errors; 121 policy/selection/harness/discovery cases, four actual-host HTTP/inventory cases, 39 host-boundary controls and one dependency-mode case pass (165 focused cases). Six isolated domain mutations each fail exactly one of four cases. Formatting exits zero with its generic workspace-load warning; the CRLF-aware whitespace check passes.
- The full Architecture assembly executes 404 cases: 403 pass; only P0_CurrentSourceRevisionMarkersMatchGitlinkAndConsumedCheckout fails. Both marker documents and the failing test are byte-identical to baseline; unchanged markers select 39a3d4dc98b786244e732d628a0828b9d19ad96e while the unchanged gitlink/checkout selects 36c78f3435d2d492a73f1b0707cf865c8735d740. No gate is weakened. Commands, exit codes, source/artifact hashes, compressed XML/logs and portable ledger-integrity verification are retained in [the packet](evidence/five-deferred-host-migration-2026-10-28/README.md).
- Conventional Commit validation passes. Fetch confirms zero divergence from origin/main before review completion. No runtime source, dependency, gitlink or production configuration changes are included.

## Review Triage Log

- Medium / patch: add the missing topic-metadata subscription startup refusal.
- Medium / patch: add the separate actor-callback startup refusal.
- Low / patch: assert exact caller identities/multiplicity and reject duplicate-caller mutations.
- Medium / patch: retain a passing valid configured-copy lane.
- Medium / patch: discover explicit facts across the entire assembly; bind/discover the complete Integration source and shared-harness caller inventory.
- Medium / patch: validate every command exit, including commands without XML.
- Medium / patch: require every qualifying lane and its exact expected case totals.
- Medium / patch: require exact mutation method, filename, assertion type/field/scope and three passing configurations.
- Medium / patch: bind all exercised Integration sources, spec and SDK inventory implementation; enforce recorded assembly/dependency provenance.
- Medium / patch: compare the broad-blocker sources against the baseline and require its exact failure diagnostics.

The complete review and evidence for each disposition are retained in [review.md](evidence/five-deferred-host-migration-2026-10-28/review.md). No finding is deferred. The caller-set expansion required the normal public-parameter null guard (CA1062); the intermediate failed build is retained and no analyzer severity changes.

## Verification

- Independent correction recheck confirms all ten original findings resolved; no findings are deferred. The reviewer executed the tightened verifier successfully and checked updated source, XML and provenance rules.
- Final Integration Debug build: zero warnings/errors. Final policy/selection/harness/discovery: 121/121; actual-host HTTP and inventory: 6/6; valid configured copies: 4/4. Together with the 39 boundary cases and one dependency-mode case, 171 focused cases pass. Eight final domain/caller-identity mutation controls each refuse exactly one of four configurations. No skips/errors/unexecuted cases qualify.
- Full Architecture remains 403/404 at the unchanged pre-existing marker mismatch. The packet verifies unchanged baseline inputs, exact failure identity and diagnostics; no runtime source, dependency, gitlink or gate changes.
- Final formatting, CRLF-aware whitespace, Conventional Commit validation and retained evidence integrity checks pass. Exactly five ledger closures add ten annotation lines; removing them restores the original bytes. The final main push is explicitly authorized by the user.
