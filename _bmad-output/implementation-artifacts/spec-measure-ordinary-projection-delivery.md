---
title: 'Measure ordinary Works projection delivery near admitted history limits'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Ordinary delivery loads and refolds retained projection history. Measure delivery time and throughput using the actual SDK coordinator, Dapr state client and Redis at short histories and close to the per-owner and tenant history ceilings. Establish whether delivery at this volume fits the current Platform budget without modifying projection behavior.

</frozen-after-approval>

## Implementation Notes

- Measure five sequential deliveries in three fixtures. The short-history baseline has 32 owners. The near-limit fixture has the same population and four histories above 90% of the 4 MiB per-owner ceiling, with total history above 90% of the 16 MiB tenant ceiling. The wider fixture has 512 owners and short histories.
- The real `DomainProjectionDispatcher` runs against the SDK coordinator and `DaprReadModelStore`. Each sample includes dispatch, journaling, catch-up and response construction. The measured append targets a near-limit owner in the large-history case. Reconstructed coordinators verify the exact changed history and checkpoint, unchanged unrelated parking bytes and ETag, the same selected generation and an empty delivery queue.
- Build the IntegrationTests project in Release, then run `run-with-temp python3 scripts/measure-projection-delivery.py --results PATH`. The launcher uses the catalog's Dapr version and exact Redis image, enables append-only persistence, and verifies that the xUnit report contains every sample and both image IDs.

## Verification

All three scenarios passed against Dapr 1.19.2 and the catalog-pinned Redis 7.4 Alpine image.

| Owners | Retained history bytes | Largest owner history bytes | Range per delivery | Deliveries per second |
| ---: | ---: | ---: | ---: | ---: |
| 32 | 21,728 | 679 | 287–358 ms | 3.26 |
| 32 | 15,474,344 | 3,863,833 | 1,744–1,979 ms | 0.55 |
| 512 | 348,060 | 680 | 2,248–2,450 ms | 0.43 |

Every measured SDK dispatch finished under five seconds. The wider fixture consumed roughly half of that budget before the Platform HTTP and service-invocation overhead. The results quantify the full-family cost at the measured populations. They leave the 10,000-owner case and Platform routing overhead to separate measurements.

## Review Triage Log

- **Medium, patched.** Time the real SDK dispatcher and identify the measured boundary independently of the Platform transport.
- **Medium, patched.** Append to the near-limit owner and reopen the resulting full history.
- **Medium, patched.** Add the 512-owner population and bound the conclusion to measured cases.
- **Medium, patched.** Use the catalog-pinned Redis digest and append-only persistence.
- **Medium, patched.** Put image identities and exact samples in xUnit output and require all three cases in the retained report.
- **Medium, patched.** Check the unrelated parking ETag as well as value bytes to detect a same-value rewrite.
- **Low, patched.** Remove anonymous Redis volumes with the owned containers. The successful runner left no labelled measurement container or network.
