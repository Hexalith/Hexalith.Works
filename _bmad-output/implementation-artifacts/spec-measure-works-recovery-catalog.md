---
title: 'Measure Works recovery catalog enumeration at supported populations'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Measure complete keyset sweeps against the real Dapr Redis state store for both the default Works process-recovery population and a valid larger process profile. Check whether rereading membership on every page exhausts the selected SDK catalog operation budget.

</frozen-after-approval>

## Implementation Notes

- The dedicated local fixture uses the production Works catalog and SDK coordinator. It promotes full histories across three tenants, including terminal candidates and a pending date in every tenant, and checks exact ordered coverage, continuation and completeness in five sweeps at each size.
- Run `run-with-temp python3 scripts/measure-projection-delivery.py --scenario recovery-catalog --results PATH` after the Release IntegrationTests build. The launcher requires both workloads and all samples in the xUnit report.

## Verification

The default six-source profile returned five complete one-page sweeps at 42–82 ms per page. The valid 45-source profile across the same three tenant scopes returned five complete three-page sweeps at 119–144 ms per page. All 20 direct catalog calls completed within the 500 ms operation budget. This measures the catalog itself and its real state-store work. Worker scheduling and catalogs distributed across more than one 16-tenant read batch remain outside these measurements.

## Review Triage Log

- **Medium, scoped.** The measured boundary is the actual catalog call, compared with the configured SDK operation budget. The test reports neither worker admission nor full readiness latency.
- **Medium, scoped.** The profile records three tenants and a 45-member population. Broader tenant distributions can require additional read batches.
- **Medium, patched.** Every tenant now contains active date-await evidence, requiring both index families to be read.
- **Low, patched.** Validate the exact requested page limit and continuation on every observed page.
