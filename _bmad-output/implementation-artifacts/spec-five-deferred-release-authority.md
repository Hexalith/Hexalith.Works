---
title: 'Close five deferred release authority gaps'
type: 'bugfix'
created: '2026-10-25'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred build and release findings leave ordinary restores exposed to local candidate feeds and ambient SDK overrides, and lack current proof of solution target preservation, assembly version stamping and release/CI behavior compatibility.

**Approach:** Require explicit candidate admission, reject unapproved Release SDK drift, and execute focused MSBuild, package-consumer and exact-action compatibility controls. Close exactly five original ledger entries with retained results, independently review, commit and push `main` as requested.

</frozen-after-approval>

## Implementation Notes

- Freeform, no epic story key. The user explicitly requests a five-item batch and push to main. No intent gaps, new API, data mutation, dependency update or topology changes; the narrow change qualifies for the oneshot route. No AppHost exists in Works and the verification uses build tooling only.
- Selected ledger summaries: Make published Works assemblies carry the released package version instead of the build-time MinVer height version; Preserve subsequent solution targets when serializing solution project builds; Prevent ordinary package restores from confusing local candidate archives with published producers; Verify that release preparation and exact-source CI use compatible Builds action behavior; Restore an unconditional default EventStore pin, or refuse a Release build whose effective HexalithEventStoreVersion differs from WorksPinnedEventStoreVersion without an explicit opt-in.
- `Directory.Build.props` currently injects both historical candidate feeds whenever their directories exist. Restrict those additions to `WorksAllowLocalEventStoreCandidate=true`, preserving explicitly supplied restore sources. Apply the same explicit opt-in to nondefault Release SDK versions before restore/compile. Keep source Debug behavior and approved public version unchanged.
- Reuse `WorksAllowLocalEventStoreCandidate`, already understood by package contract verification. Retain the public pin in `Directory.Packages.props` as authority rather than introduce another independently maintained version. Tests use actual MSBuild evaluation and target execution, command-line and ambient overrides, a fresh-cache candidate restore, and later `Build,Pack` solution target execution.
- `scripts/pack-release-packages.py` already compiles with the requested release version, ships symbols and SourceLink; `scripts/validate-consumer-package-references.py` already checks reflected informational version from each isolated package-only consumer. Execute the actual five-package pack and consumers to settle the historical finding.
- `Directory.Solution.targets`, which removed project-reference items after Build, is absent. Prove Build followed by Pack reaches each fixture project while the committed `Directory.Solution.props` retains serialized build settings. Do not restore the obsolete orchestration.
- `scripts/tests/test_release_builds_compatibility.py` compares the exact tracked CI and release preparation action chain from locally available Builds Git objects. The effective initialize-build action is currently identical; restore/build commands, SDK selection and accepted output must remain compatible. Negative controls must reject relevant changes.
- Add focused build-policy Python regressions and README guidance. Use root-owned files only. Run tooling lint/tests, exact package qualification and appropriate architecture checks; preserve exact broad blockers separately. Inspect all review findings and retain a verification packet before final ledger closeout. Validate the Conventional Commit, fetch and inspect divergence, then push main without force.

- Initial root revision was `3605124805d9fd51b1cebc6117bc8ffbc87651f2`. A concurrent user commit `dbbc658f11a72856013501b4ee0a704494775938` archives unrelated completed ledger rows. It is preserved; the five selected entries are unchanged by that commit. Final evidence binds the current parent and changed source hashes.
- Candidate boolean comparisons use prefixed string operands because MSBuild treats plain `yes` and `on` as `true`. The control rejects noncanonical admissions and verifies explicit source preservation, both historical feeds, fresh cache provenance and exact restored archive hashes.
- Initial complete architecture run found a stale assets timestamp after build-policy edits, plus an existing runbook fitness assertion expecting current-host wording in a row explicitly documenting the retired host. Re-restore refreshes assets. The narrow assertion correction requires the documented specific read/data repair and historical-host restart, preserving the historical ownership distinction and every other safety check. No operator guidance is changed.
- Initial package qualification succeeds: ten exact archives (five primary plus symbols), NuGet metadata checks and all five freshly restored isolated consumers at `0.0.0-deferred.20261025`, including reflected assembly informational-version checks. Full tooling suite passes 67/67; pinned Ruff passes. Shell syntax passes; shellcheck is unavailable.

## Review Triage Log

- Medium / patch: property-only Release checks permitted a previously restored candidate graph during builds without restore. Reuse the SDK assets reader and reject every mismatched EventStore package, including packages with no compile assets; execute actual Build controls after candidate restoration.
- Medium / patch: admitted archives could populate an ordinary warm cache. Candidate admission now defaults to a separate cache, ordinary forced restore cannot reuse it, and ordinary compilation refuses remaining candidate-cache assets even at the approved version. Explicit custom cache/source choices remain caller-owned; arbitrary pre-existing global-cache authentication is not claimed.
- Medium / patch: tooling CI and temporary fixtures did not select the pinned SDK. Add pinned setup-dotnet to the tooling job and copy global.json into each MSBuild fixture.
- Medium / patch: compatibility checks ignored caller solution/global-json inputs. Resolve values from each scoped caller and producer defaults; differing overrides fail, matching explicit overrides pass.
- Medium / patch: selected build steps ignored activation and execution context. Compare normalized complete bodies, reject disabled/failure-ignoring steps and inherited override contexts, and exercise environment, working-directory and extra SDK selection controls.
- Medium / patch: comparison reconstructed an assumed step order. Require actual dependency order through initialization, SDK setup, restore and build; reject Build-before-Restore.
- Medium / patch: approved Builds checkout comparison omitted repository/path. Require the checkout action, repository, path, exact ref, safe submodule/credential settings, activation and ordering.
- Medium / patch: generic candidate test names could imply wider producer qualification than the existing integration contract permits. Positive admission controls now use the -story415. convention and README explicitly distinguishes build admission from complete producer qualification.

- All eight original review findings are addressed. Focused build policy now has nine tests and action compatibility has fourteen; the complete Python tooling suite passes 76/76. New controls exercise both actual target hooks, both historical feeds, forced ordinary restore after candidate admission and builds without restore from candidate-version and approved-version candidate-cache assets. Pinned Ruff, shell syntax and official checksum-verified actionlint 1.7.12 pass. The Go launcher is unavailable, so actionlint was run from its official standalone archive instead.

- Independent follow-up reports no remaining blockers, reruns all 23 focused tests successfully and confirms every original compatibility control now rejects altered behavior. Nothing is deferred by this batch.

## Verification

- Final `python3 -m unittest discover -s scripts/tests -v` passes **76/76**. Final Release architecture build succeeds with **zero warnings/errors**; direct execution of its assembly passes **398/398**, with zero errors, failures, skips or unexecuted cases.
- Final solution restore: `dotnet restore Hexalith.Works.slnx -p:Configuration=Release -p:NuGetAudit=false -m:1 --force`. Final architecture build: `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Release --no-restore -m:1`. Final execution: `dotnet tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests.dll -result-xml /tmp/works-five-deferred-architecture-reviewed.xml`.
- `python3 scripts/pack-release-packages.py /tmp/works-five-deferred-packages-reviewed-20261025 0.0.0-deferred.20261025` creates exactly five primary and five symbol archives. Both `validate-nuget-packages.py` and `validate-consumer-package-references.py` pass for that directory. Five freshly restored isolated consumers execute reflected informational-version checks. All five portable PDB symbol archives contain SourceLink custom debug information. These are local verification packages; no package publication or live topology qualification is claimed.
- Pinned Ruff 0.13.2 checks all Python tooling; actionlint 1.7.12 checks all workflow files from a checksum-verified official standalone archive. Script syntax passes. Shellcheck is unavailable and optional in the repository script; no shell files changed. The scoped `dotnet format whitespace --verify-no-changes` command passes with the generic workspace-load warning.
- Original current-parent ledger bytes are preserved after removing exactly five newly inserted status/resolution pairs. The concurrent user archive commit remains intact. No dependencies, submodule gitlinks or runtime/domain code change. All new code/spec files and the touched C# fitness test use CRLF; existing Markdown retains LF.
- `npm run commitlint -- --edit /tmp/works-five-deferred-commit.txt` accepts `fix: close five deferred release authority gaps`. Final fetch shows current parent `dbbc658f11a72856013501b4ee0a704494775938` one commit ahead of `origin/main` at `3605124805d9fd51b1cebc6117bc8ffbc87651f2`, with zero remote-only commits. The user-authorized normal push includes that preserved parent and this batch.
- The [retained packet](evidence/five-deferred-release-authority-2026-10-25/manifest.json) binds final commands/results, raw compressed logs and XML, package hashes, producer/dependency revisions and source hashes. Earlier failed fixture/format/architecture attempts are retained separately as pre-correction history; final gates pass.
