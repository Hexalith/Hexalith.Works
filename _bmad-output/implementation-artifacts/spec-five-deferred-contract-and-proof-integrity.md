---
title: 'Close five deferred proof and runtime-contract gaps'
type: 'bugfix'
created: '2026-10-28'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Complete five root-owned deferred items with small, reversible corrections:

1. G3A-B7: keep every isolated SDK package-proof check effective under Python optimization; exercise the actual HASHES block against valid and invalid asset/cache/feed fixtures in both interpreter modes.
2. Remove unused release issue and pull-request write grants, verifying the locked semantic-release GitHub hooks against the committed disabled-comment/label configuration without publishing. Count duplicate ledger findings once.
3. Correct public query summaries: valid absence yields NotFound or an empty queue; unavailable projections yield projection-unavailable failure metadata. Preserve all runtime behavior.
4. Explicitly retain projection-change notification as inactive in the current canonical host, distinguishing active SDK persistence/query serving from the retained pure notification seam and historical adapter contract. Do not select a transport.
5. Carry the reminder workload requirement into Story 4.15's append-only implementation record, identifying the current explicit `works` registration and existing resolved-options coverage. Do not change frozen historical intent or require a redundant environment override.

Use existing implementation and test seams. Preserve release contents/OIDC authority, SDK pins, dependencies, schemas, and runtime contracts. Update the owning ledger entries with specific resolution evidence. Run relevant checks and independent review, then commit and push the completed changes to main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline revision: `6e6c6c1132ff73624c78ccc9a66727fa43bba990`; clean main with no submodule changes. No unresolved product choices, new public API, or irreversible implementation steps; this bounded batch uses the one-shot route.
- Baseline `aspire ps --format Json` returned `[]`. `aspire start --non-interactive --apphost /home/administrator/projects/hexalith/works/references/Hexalith.Platform/apphost.cs --format Json` exited 2 with NU1605 downgrades for Dapr hosting 13.6.2-beta.959 to 13.6.1-beta.935 and EventStore Aspire/ServiceDefaults 3.128.3 to 3.128.0; log `/tmp/works-five-contract-baseline.log`. The AppHost did not start. Focused deterministic checks remain available; dependency changes and fresh live qualification are outside this batch.
- Replaced all five HASHES assertions with explicit refusal paths. Executing the extracted production block on synthetic asset/cache/feed inputs demonstrated ten old-code failures under optimization levels 1/2; normal mode already refused the same corrupt inputs. Public and candidate success controls preserve all five archive hashes. Only public download transport is replaced in these offline fixtures; this does not qualify fresh public packages.
- Removed issue/PR write grants from the caller-owned release job. The actual locked GitHub plugin success/failure hooks execute with committed configuration and a client refusing issue/PR API access; sensitivity controls re-enable those hooks and reach the refused boundary. Contents publication and OIDC grants remain present. No release was dispatched.
- Corrected both public query summaries and current notifier status in adoption, queue and boundary documentation. Recorded the already explicit reminder workload registration in current configuration and Story 4.15's append-only implementation notes; no runtime registration or frozen historical intent changed.
- The hook regression requires the locked Node modules. Added Node 24 setup and `npm ci --ignore-scripts` before the tooling CI suite, and recorded the same local prerequisite in README. This executes existing locked dependencies without changing their versions.
- Closed five owning ledger items plus the duplicate release-scope finding, counted once. Historical evidence and all unrelated statuses remain intact.
- Independent review corrections: enabled-hook sensitivity now uses a valid template string; all applicable refusal cases execute against both public and candidate transports (33 total scenarios), and the offline public transport checks complete expected URLs. Query summaries explicitly preserve successful degraded queues that omit recorded parked members. Queue documentation labels legacy writers/rebuilds and describes current authenticated adapter authority.

## Review Triage Log

- Low, patched: `successCommentCondition: true` reached an enabled hook but bypassed the plugin's string-or-false validator. Replaced it with the supported `<%= true %>` template; enabled success comments, failure issue cleanup and failure issue creation still reach the refusing client boundary.
- Low, patched: initial refusal fixtures exercised common guards only through candidate archives. All four common corruptions now execute through both candidate and public download paths at optimization levels 0/1/2; the public-only existing-archive refusal remains covered in every mode.
- Medium, patched: the offline public downloader accepted a correct basename under an incorrect package/version directory. It now admits only complete independent fixture URLs for the selected package and version, preserving the requested timeout and denying other locations.
- Medium, patched: queue summary overgeneralized failure behavior when persisted parking evidence allows a successful partial result. Documented omitted parked members and the successful degraded `works-runtime-parked` response separately from unavailable required documents.
- Medium, patched: unlabelled pre-SDK `/project`, shared-rebuild and index-writer descriptions plus future UserId-based query wiring conflicted with the newly stated current status. Labeled historical adapter descriptions, linked current SDK ownership, and described authenticated gateway/Parties authority. Pure projection contracts remain available.

## Verification

- `python3 -m unittest discover -s scripts/tests`: 86 passed; `/tmp/works-five-contract-tooling.log`. The package-proof tests were subsequently expanded after review from 21 to 33 public/candidate scenarios over optimization levels 0/1/2. The pre-correction run failed all ten original optimized refusal controls; `/tmp/works-five-contract-proof-red.log`.
- `python3 -m unittest discover -s scripts/tests -p test_release_hook_permissions.py`: two passed against the final hook sensitivity controls; `/tmp/works-five-contract-permissions.log`.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1 --nologo`: passed, zero warnings/errors; `/tmp/works-five-contract-debug-build.log`.
- `dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj --configuration Debug -m:1 --nologo`: passed, zero warnings/errors; `/tmp/works-five-contract-architecture-build.log`.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -explicit off -class '*WorksSdkProjectionQueryTests' -class '*WorksDateReminderSchedulingTests' -class '*WorksSdkReminderRecoveryPolicyTests' -result-xml /tmp/works-five-contract-integration.xml`: 62 passed, zero failures/skips/not-run.
- `dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll -class '*CiCdConfigurationTests' -result-xml /tmp/works-five-contract-architecture.xml`: 17 passed, zero failures/skips/not-run.
- `bash scripts/lint-scripts.sh`: passed interpreter syntax checks; local shellcheck/ruff executables absent. `uv tool run --from ruff==0.13.2 ruff check scripts/*.py scripts/tests/*.py` separately passed the CI-pinned Python lint.
- Official checksum-verified actionlint 1.7.12 over `.github/workflows/*.yml`: passed; `/tmp/works-five-contract-actionlint.log`.
- `git -c core.whitespace=cr-at-eol diff --check`: passed. Fresh live Platform qualification remains blocked by the exact baseline command above. No release was dispatched and no new public-package qualification is claimed.
- Final reviewed source: expanded proof tests 4/4, hook permission tests 2/2, Debug integration build with zero warnings/errors, and the same 62 query/scheduling/recovery-policy facts all passed again; logs `/tmp/works-five-contract-proof-reviewed.log`, `/tmp/works-five-contract-permissions-reviewed.log`, `/tmp/works-five-contract-debug-build-reviewed.log`, and `/tmp/works-five-contract-integration-reviewed.xml`. CI-pinned ruff passed on both changed Python test modules after review. All five review findings were patched; none were deferred.
- Frozen Story 4.15 intent was compared with the baseline and is unchanged. `npm run commitlint -- --edit /tmp/works-five-contract-commit.txt` passed for `fix: close five deferred proof and contract gaps`.
