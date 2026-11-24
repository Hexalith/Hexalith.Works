---
title: 'Measure sharded Works detail and discovery reads'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Measure the sharded projection reads behind the authorized detail query and the recovery catalog, comparing actual Dapr/Redis timings with the Works and SDK budgets. Retain the selected item and the exact ordered recovery page as positive controls.

</frozen-after-approval>

## Implementation Notes

- Stage a 2,048-owner projection with both queue and date-await shards, reopen the selected generation and measure five detail reads and five recovery pages. The detail handler uses the normal five-second Works read budget. For discovery, compare a raw read with direct cancellation at the SDK's configured 500 ms operation interval.
- This is a projection stress population. The measured six- and 45-source recovery profiles cover the supported multi-tenant process configuration. [Recovery catalog evidence](spec-measure-works-recovery-catalog.md).
- Build IntegrationTests in Release and run `run-with-temp python3 scripts/measure-projection-delivery.py --scenario sharded-reads --results PATH`.

## Verification

The real Dapr/Redis fixture reopened 89 index shards across 2,048 retained owners. All five authorized detail reads returned the selected queued item in 84–159 ms. Unbounded discovery returned the exact ordered recovery page in 539–950 ms. All five direct discovery calls with a 500 ms budget observed cancellation in 500–506 ms. The results measure the catalog read and cancellation path, with the reconciler's worker scheduling outside the measurement.

## Review Triage Log

- **Medium, patched.** Record raw discovery timing and explicit budget cancellation in the same real-store fixture.
- **Medium, scoped.** The 2,048-owner case is projection stress evidence. The separate supported recovery profiles establish operational timing for their lower candidate populations.
- **Medium, patched.** A detail failure only qualifies as an observed deadline when the handler emits its timeout diagnostic in the bounded interval. The report retains all detail and discovery timing samples.
