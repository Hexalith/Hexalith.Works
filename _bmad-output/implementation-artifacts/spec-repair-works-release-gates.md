---
title: 'Restore the Works Release build and verification gates'
type: 'bugfix'
created: '2026-11-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 3
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Restore the Works build and checks identified by the November 23 verifier change. Use authenticated producer inputs compatible with the Platform dependency, exercise the current build policy and framework in the script fixtures, and verify the recorded source and package pairs. Run the affected checks against the actual committed dependencies and verify the result in CI.

</frozen-after-approval>

## Implementation Notes

- Verified the Builds 6.6.2 signed tool against its public release receipt and archive. Works main received the same dependency update in `6c988b09`.
- Updated the package-free policy fixtures to load the selected Builds catalog and target the Works framework. The Release fixture removes the caller’s Builds-root override.
- Reconciled the two Debug source revisions with the committed Works gitlinks and verified the effective Release package versions.
- The shared Release build reached two CA2007 errors in Platform’s asynchronous stream disposal after the tool update. The producer fix preserves async disposal and configures its continuation.
- All 127 existing script tests passed in a checkout at the committed dependency revisions. The additional catalog-authority regression passed with the other 12 Release-policy tests under conflicting environment settings. The source-pair preflight and all 18 EventStore architecture checks also passed.

- With the same source selection as hosted CI, 14 architecture tests failed because Debug references were missing. The CI owner’s `b86a24a` adds Commons, EventStore and PolymorphicSerializations before restore. The full architecture assembly then passed all 385 tests with zero skips. The complete Works script suite passed all 128 tests.
- Four near-capacity Platform custody cases depended on runner speed. Platform `c9da2de7` gives those tests a deterministic clock. The 143-case custody suite and the nine independent deadline tests passed. Platform `286a79c1` applies the same clock to four adjacent capacity checks, which all passed.
- The parity scaffold requires the Aspire 17.0.0 CLI bundle. The same scaffold built with the verified bundle after reproducing ASPIRE009 without it. CI owner `5190f87` selects that setup for the dedicated parity job.

- The Redis expiry integration test now uses the fleet fixture’s real Redis capture and restore phases. It compares the original absolute deadlines against Redis server time before and after expiry. Other cases exercise the complete file, Scheduler and Redis rollback.

- Works pins Platform `b975a77e`, which includes the fixture-disposal correction, deterministic capacity tests, and the concurrent correction routing the mTLS control plane to the selected Sentry.

## Review Triage Log

- High, fixed. A fresh Works checkout would still select the old Platform source. The Works gitlink includes Platform `1cf0321e`, which fixes async stream disposal.
- Medium, fixed. Inherited mapping settings allowed a missing source root to pass a negative test. The fixture now clears the mapping inputs and root selectors for every case.
- Medium, fixed. Both catalogs originally supplied the same version. A test now uses conflicting versions and fails when the guard is changed to read the Works catalog.
- Low, fixed. The API surface notes now date the older EventStore selections and identify `5.4.0` as the current Release selection.
- High, addressed through central delivery. Works commits `754b1162` and `642c5765` receive the three required source checkouts and the parity-only Aspire setup through the owner workflow.
- Medium, fixed. The paired capacity and readiness cases used wall-clock deadlines, which could satisfy a refusal assertion for the wrong reason. Platform `286a79c1` uses the deterministic clock for all four.
- Low, closed. Capacity fixtures complete synchronously or return an explicit fault. Caller cancellation, the 30-minute command bound and the independent real-deadline tests cover the demonstrated paths.
- Medium, deferred. Two successive full captures on the same fixture can save the previous `scheduler.tar` as a configuration file. The behavior predates the Redis change and is recorded in the deferred-work ledger.
- Medium, fixed. The live Redis branch checks the original deadline within a 45-second window and waits against server time. The expired-before-restore branch uses eight seconds.
- Medium, fixed. The live branch now checks the value and absolute expiry of the single field in the fully expiring hash.
- Medium, fixed. Whole keys use the server’s absolute deadline from `PEXPIRETIME`, restore it using `RESTORE ABSTTL`, and verify it exactly.
