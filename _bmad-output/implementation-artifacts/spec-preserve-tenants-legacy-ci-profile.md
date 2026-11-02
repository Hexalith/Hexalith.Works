---
title: 'Preserve legacy topology test compilation with modern catalog controls'
type: 'bugfix'
created: '2026-11-02'
status: 'in-progress'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

The current Tenants Aspire job selects its frozen3.128.4 compatibility topology, but new owner-source tests unconditionally import modern Routing APIs. Preserve all existing legacy request/budget tests while compiling catalog-specific runtime assertions only when their qualified HEXALITH_DEVELOPMENT_CATALOG_SOURCE capability is enabled. Current public3.132.1 and source profiles must keep every modern case; no skipping, test assertion weakening, public behavior, timeout or authority changes. The separate source-runtime lease-drain failure remains open and is not addressed by this compile fix.

</frozen-after-approval>

## Implementation Notes

Owner private worktree `/tmp/works-tenants-ci-profile-repair-20261102`, baseline remote mainba4639fa462341281f89aa7a08b7398477fc1ba9; no nested initialization or original user changes. Three files: Authorization/OwnerSourceTargetHttpHandler.cs keeps legacy transport checks; guard only modern using/header assertions. Authorization/TenantOwnerSourceRuntimeTests.cs is entirely modern; guard whole file. Bootstrap/TenantBootstrapReadinessBudgetTests.cs retains all generic budget cases; guard Routing usings and exactly2 catalog-ready theory cases. Match existing production capability flag, not test-only symbol. Unit current and legacy suites must actually execute.

## Verification

Build actual Server.Tests in package Release with existing root Build catalog and other roots passed explicitly. Qualify3.128.4 and3.132.1 in separate intermediate/output directories or serial full rebuilds. Invoke built assembly directly via xUnit single-dash selectors. Legacy target-handler and generic budget cases pass; modern lane runs all7 owner-source runtime cases and both catalog budget cases. Exact case IDs/counts retained. No fakeCI, compatibility checkout rewrite or live topology locally.

Validation: both actual package Release Server.Tests builds passed; legacy selected10/10 and current19/19, with zero failed/skipped/errors. All7 modern owner-source runtime and2 catalog budget cases execute in current profile. Initial private invocation omitted the existing global ContainerProvenance.targets path; corrected invocation passes its actual root path. Legacy compilation also exposed unread optional modern parameters; their unsupported use is now explicitly refused by null assertions. Evidence retained under `/tmp/tenants-ci-profile-repair-verification-20261102/`.
