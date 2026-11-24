---
title: 'Bound SDK projection-store reads for Works queries'
type: 'bugfix'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem.** The query adapters pass cancellation into the SDK projection coordinator, but an authorized query can otherwise wait indefinitely for a state-store provider that ignores cancellation.

**Approach.** Bound the complete authorized projection read with the existing SDK admission and deadline helper. Preserve a caller's exact cancellation token and the normal unavailable response on an expired read budget. Exercise the real Dapr read transport and a noncooperative provider for both supported query adapters.

</frozen-after-approval>

## Implementation Notes

- Apply the existing five-second Platform invocation budget from query entry through authorization and snapshot reads. Keep authorization before projection admission and hold the SDK slot until the admitted provider task finishes. Check cancellation before entering another Works read phase.

## Review Triage Log

- **Medium, patched at the Works boundary.** A completed late read may cause calls inside an SDK method to finish, so the adapter now checks cancellation before the next Works phase and after the final status read. Cancellation within a noncooperative provider or a coordinator method remains subject to the SDK provider contract.
- **Medium, patched.** A separate two-second projection limit would shorten the existing Platform request budget. The deadline now starts at query entry and uses the established five seconds. Large-tenant throughput remains covered by the existing sharded-read benchmark item.
- **Medium, patched.** Admission pressure now has a distinct safe diagnostic category.
- **Medium, patched.** New controls block the related read after serving a valid index and verify that time already spent during authorization cannot reset the deadline. The Dapr client fixture checks caller cancellation and the deadline.
- **Medium, patched.** The noncooperative control verifies an abandoned call retains its slot while held and releases admission before the test collection exits.

## Verification

The Release integration build passed. All 206 selected integration cases passed, including noncooperative provider admission, caller-token fidelity, and the actual Dapr client against a controlled loopback state endpoint.
