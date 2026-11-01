---
title: Admit the legitimate Aspire SDK service-default dependency
type: bugfix
created: '2026-11-01'
status: done
route: oneshot
baseline_commit: 331c528219e921e990402a6134fed9bfc50f0441
---

<frozen-after-approval reason="User authorized current package/CI repair and waived human validation">

## Intent

Tenants package CI rejects the public EventStore.ServiceDefaults dependency promoted from verified EventStore.Aspire3.131.0. Add an Aspire-specific exact normalized package-ID exception, retaining ordinary restore range/framework checks and every other layer/private/lookalike refusal. Do not change package references, manifest, transitive pinning, bootstrap behavior or runtime authorization.

</frozen-after-approval>

## Implementation Notes

Owner is the clean root-declared Tenants repository at baseline331c5282. The published EventStore.Aspire3.131.0 nuspec genuinely requires EventStore.ServiceDefaults3.131.0; source/archives match verified producer7776e64. Current CI41347795557 job124971250052 packs all five modules before this validator rejects Aspire.

Change only `scripts/validate-nuget-packages.py` ALLOWED_DEPENDENCIES_BY_PACKAGE and `tests/scripts/test_nuget_dependency_ranges.py`. Keep Server exceptions unchanged; allow only EventStore.ServiceDefaults for Aspire, not Commons.ServiceDefaults. Update only that negative matrix cell and add range/framework/case/promoted-dependency controls. Run the focused executable Python validator suite and validate an actual privately packed Aspire archive against its real restore assets and the exact unchanged producer-manifest entry. No nested pointers/checkouts, native starts, package publication or other source changes. Parent owns conventional Git and hosted CI.

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | low | patch | Original case tests used identical spelling across restore/nuspec. Add independently varied ID casing and uppercase owning package controls; production matching already uses casefold and remains unchanged. |
| blind-2 | low | patch | The new exception had no direct present/missing restore-boundary controls. Add both omitted-required and unexpected-allowed dependency checks for direct and promoted inputs; keep exact boundary validation unchanged. |

## Verification Evidence

The initial12 executable Python tests passed. Actual privately packed Aspire archive e5e484eb3e559cf03e66b258cbfb5f476be0e358ac12e5769083d7b6a1e8f7cc reproduces the baseline forbidden-project failure and passes the one-entry correction against the real unchanged5-entry release manifest and actual restore assets. Receipt: `.artifacts/upgrade-release-orchestration-20261101/tenants-aspire-validator-20261101/actual-archive-verification.json`. This is local package-boundary verification, not native or official release qualification.

Both review findings were patched with independently varied package/dependency casing and missing/unexpected dependency controls. The final12 executable cases pass (4.684s), retaining direct/promoted boundaries and all forbidden-layer checks. No production behavior beyond the exact classification entry changed.

Committed and pushed as a4447bb5049f4db26bca522cc17f632bee8a34f0. Hosted package/source CI remains the release gate; source bootstrap timeout is a separately tracked shared-SDK investigation.
