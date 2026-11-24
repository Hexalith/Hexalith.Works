---
title: 'Measure queue reads over retained terminal membership'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Measure how retained terminal work affects the authorized what's-next query with the real SDK state store. Isolate the cost of reading the retained membership from the amount of data the authorized result returns.

</frozen-after-approval>

## Verification

The Release test passed all three fixtures against real Dapr 1.19.2 and the catalog-pinned Redis image. Each fixture retains exactly eight eligible owners. All requested terminal members and a terminal roll-up were verified from the selected generation before timing.

| Terminal owners | Authorized results | Observed handler time |
| ---: | --- | ---: |
| 0 | All three reads returned eight items | 42–55 ms |
| 1,024 | All three reads returned eight items | 1,490–3,029 ms |
| 4,096 | All three reads returned unavailable with a timeout diagnostic | 4,999–5,005 ms |

The 4,096-terminal-owner fixture reproduced the five-second query deadline while the SDK epoch stayed healthy and the authorized result remained small. Measurements cover the Works handler and real store. A separate transport test measures response bytes. Run `run-with-temp python3 scripts/measure-projection-delivery.py --scenario terminal-queue --results PATH` after the Release IntegrationTests build.

## Review Triage Log

- **Medium, patched.** Verify the handler's exact timeout diagnostic and a bounded elapsed window before counting a refusal as a deadline.
- **Medium, patched.** Reopen the real selected generation and verify terminal membership, a terminal roll-up, and the fixed eligible inventory before timing.
- **Medium, scoped.** The largest fixture reproduced the failure inside the admitted projection population. Results state the tested sizes and keep the full 10,000-owner population outside the claim.
