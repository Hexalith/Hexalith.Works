---
title: 'Close five deferred runtime safeguard gaps'
type: 'bugfix'
created: '2026-10-22'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five deferred Works gaps weaken recovery and operational failure evidence: T207 accepts nonpositive domain sequences, R3B3 accepts non-HTTP Parties addresses until requests fail, B9 omits serverAuth certificate purpose, and R3B7/R3B8 discard query and tenant-discovery failure causes.

**Approach:** Reject nonpositive sequenced recovery payloads while preserving rejection events and separate envelope positions. Validate HTTP/HTTPS for the effective Parties address, including fallback. Require serverAuth in the custom trust chain with local certificate controls. Emit bounded, structured diagnostics for failed projection queries and incomplete tenant discovery without exception objects, exception text, payloads, credentials, or actor identifiers. Preserve fail-closed responses, caller cancellation, healthy-tenant progress, and incomplete discovery retry. Add focused regressions, resolve these five ledger entries, independently review, validate, commit and push Works main. Preserve the pre-existing EventStore and Platform commits and gitlinks, as the user confirmed.

</frozen-after-approval>

## Implementation Notes

- Investigated at Works revision `8a10f1da505e207c1f417363d712b62aa3c98422`. The bounded corrections reuse current validators, SDK coordination, DI logging, and existing integration fixtures. No migrations, dependency changes, new public service seams, or unresolved intent choices are needed. The requested commit/push is authorized. This is the oneshot route: localized guards and diagnostics in existing adapters, with deterministic tests.
- Code map: `Recovery/WorksRecoveryEventDecoder.cs`; `Runtime/WorksDomainAdapters.cs`; `Queries/GetWorkItemQueryHandler.cs` and `WhatsNextQueryHandler.cs`; `Recovery/WorksProcessSourceCatalog.cs`; test-only `WorksPlatformTrustChecks.cs`. Reuse source-generated logging conventions from `WorkItemProjectionEventDecoder`, `Story48RecordingLogger`, SDK projection/source fixtures, and loopback TLS. Keep current query constructors available. Do not revive retired reminder handlers or edit submodules.
- Validation: IntegrationTests Debug build and focused direct-xUnit classes, followed by deterministic IntegrationTests excluding live `*SmokeTests` and `*Platform*`; separately execute the deterministic Platform trust controls. Run the architecture lane if runtime governance requires it, and whitespace checks. Works has no AppHost; no Platform deployment or live parity rerun is needed for these bounded adapter and probe corrections.
- Implemented the five guards/diagnostics using source-generated events 4510 (query) and 4511 (discovery). Public query constructors remain available, and host DI can supply the new logger overload. Logged tenant/correlation values are limited to 128 single-line characters; messages contain fixed categories and no exception object/text.
- Initial compilation caught a missing test import, which was corrected. The subsequent full Debug IntegrationTests build passed with zero warnings/errors. The first focused run passed 117/117; the full architecture lane passed 308/308. The first full deterministic run passed 683 and failed 17: the failures were existing explicit-zero recovery expectations in `WorksSdkIteration4Tests`. Those tests now distinguish positive required sequences from legal zero scalar values, while continuing to assert the unchanged historical decoder behavior and persisted process quarantine/checkpoint state.
- Review corrections add real SDK outage classification, global acquisition diagnostics, structurally invalid document diagnostics, logger-outage isolation, loopback TLS purpose tests, and full paged-reader rejection tests. No submodules were edited.

## Review Triage Log

- **medium / patch — wrapped SDK dependency failures:** verified `ReadAsync` wraps transport exceptions in `SharedProjectionReadUnavailableException` while `ReadManyAsync` can expose raw errors. Category mapping now recognizes both and Dapr errors; tests inject real store failures through both adapters.
- **medium / patch — global discovery acquisition:** verified the initial control-index read precedes tenant catches. It now logs bounded global context and rethrows the original failure; a regression proves the failure contract and caller cancellation remain intact.
- **low / patch — structurally invalid query documents:** verified valid JSON with invalid membership/rollup can return unavailable without an exception. Those validation refusals now emit fixed-category diagnostics, with duplicate-membership regressions through both adapters.
- **medium / patch — throwing diagnostic providers:** verified a provider exception could replace the protected response or fault tenant enumeration. Diagnostics are now best effort; an exporting logger failure is exercised through both query handlers and the catalog while preserving unavailable responses, healthy tenants, and retry.
- **medium / patch — certificate helper-only coverage:** the original purpose test bypassed `ProbeAsync`. Matching-SPIFFE loopback TLS controls now exercise that exact callback with client-only and server-purpose certificates.
- **medium / patch — decoder-only rejection coverage:** the original ordinal cases bypassed complete source acquisition. Full reader regressions now reject a bad ordinal after valid evidence within one page and across a boundary, with no returned partial context.

- **low / patch — regression fixture precision:** the structurally invalid manifest control uses an explicitly null required membership list, which the existing shape validator rejects. Duplicate membership is handled separately by discovery and is not the query shape validator's contract. Persistence assertions compare SDK-stored snapshots before and after query execution, preserving canonical JSON property ordering. The earlier duplicate-membership description above is superseded by this clarification.
- Follow-up independent review confirmed the six original corrections and reported no remaining concrete bugs. No additional work was deferred.
- Final verification: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1` passed with zero warnings/errors. `tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests -class- '*SmokeTests' -class- '*Platform*'` passed 713/713, including the real loopback TLS controls; the separate `-class '*WorksPlatformHostingCompositionTests'` run passed 12/12. The full Debug architecture executable passed 308/308. Every run had zero failures, errors, skips, or unexecuted tests. Logs and xUnit XML are retained under `/tmp/works-five-runtime-*`.
- Resolved exactly T207, B9, R3B3, R3B7 and R3B8 in the ledger. Existing EventStore (`5de936416fdf7c76263e7b6d40e527892d51f504`) and Platform (`57527e3ec2e32fef354af09e6a31f615c50f621f`) checkouts and the parent gitlinks are preserved. No dependency changes or live Platform deployment were performed.
