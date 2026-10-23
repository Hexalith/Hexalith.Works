---
title: 'Close five deferred SDK rebuild admission and capacity items'
type: 'bugfix'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five open deferred items question SDK rebuild safety: ordinal gaps in partition inventories, empty inventories, rejection of oversized owner history before finalization, operation-count coverage and the canonical manifest byte ceiling. The current 8 MiB manifest budget can be smaller than the admitted 16 MiB source history after canonical encoding.

**Approach:** Settle exactly these five entries with executing SDK regressions and measured capacity qualification. Preserve valid erased-owner ordinal gaps, prove empty inventories retain committed data and oversized history is refused during preparation, count actual multi-page plan operations, and align the registered manifest budget with the bounded rebuild capacity. Independently review, verify, commit and push Works main as requested.

</frozen-after-approval>

## Implementation Notes

- Baseline: `0e538a68bb7fad25e9cc6a424402675b14ddffe1`; clean Works main. The user requests five deferred items and the final push. No epic story key applies. This is a bounded change to existing rebuild tests and domain adapter capacity configuration plus the ledger/spec; no public API, dependency update, migration or topology change. No unresolved intent gap; use oneshot.
- Investigation: the SDK's `DomainSharedProjectionRebuildDispatcher.BuildPlanAsync` checks every accepted authoritative ordinal and omits chunks for erased owners. Thus Works receives valid gaps; stricter contiguity would reject supported erasure. Prove this through SDK dispatch and persist/read the surviving owner's rebuilt projection after reconstruction.
- `WorkItemSharedRebuildManifestBuilder.BuildEpoch` already refuses zero histories. Exercise the partitioned lifecycle with a previously committed readable generation and assert its persisted contents and generation survive refusal. Existing `Empty_authoritative_inventory_is_refused_instead_of_blanking_the_tenant` also covers the older candidate API.
- `WorkItemRuntimeHistoryStore.Serialize` already enforces `MaxItemBytes` during `PreparePartitionAsync`, before SDK partition staging. Prove an exactly admitted history and an oversized history through both preparation APIs. Keep its 64 KiB pages, 4 MiB owner and 16 MiB tenant ceilings.
- `WorksSdkPartitionedRebuildTests.cs` owns the executing SDK lifecycle and multi-page fixtures. Extend it for these cases and actual operation counts. Reuse the real `InMemoryReadModelStore` protocol, not a response-only mock; assert stored end state where persistence occurs.
- `WorksRebuildOperationBound.MaxOperations` is the production operation ceiling; `WorksDomainAdapters.AddWorksDomainAdapters` registers it with `ReadModelBatchOptions` but hardcodes an 8 MiB manifest cap. Measure the SDK canonical serializer against plans at the aggregate-count and tenant-byte ceilings, including long valid identifiers, and configure a conservative finite budget. Preserve operation-count, owner-size and tenant-size rejection; no blanket unlimited budget.
- Verify by building the integration project in explicit Debug source mode, running the focused partitioned rebuild and existing shared rebuild/query classes, then the deterministic integration lane if build/runtime permits. Use xUnit's built assembly single-dash filters. Capture exact commands, results and any separate broad-gate blocker. Works has no AppHost; this batch concerns deterministic domain rebuild planning and SDK persistence.
- Preserve original ledger evidence; add dated outcomes to exactly the five selected entries. Retain source hashes, build/test logs, XML results and measured plan statistics in an evidence packet. Check whitespace and validate the Conventional Commit message before committing. Fetch and inspect divergence before pushing main.

- Capacity investigation corrected the initial suspected defect: `DomainSharedProjectionRebuildDispatcher.FencedStageAsync` supplies the full logical plan to `SharedProjectionEpochCoordinator.StageAsync`, which chunks large plans before executing real store batches. A monolithic canonical manifest exceeding 8 MiB does not itself show a runtime failure. Keep the existing production budget and qualify actual writes at the 16 MiB history ceiling; no production change is justified without an executing failure.
- Added `WorksSdkRebuildAdmissionTests.cs` and a test-only `WorksSdkRebuildBatchObserver.cs`. The observer measures the SDK's actual canonical encoding through reflection without duplicating its frozen serializer, delegates to the real fake-store protocol and records thread-safe measurements. Tests cover both preparation APIs at exactly 4 MiB and one byte over; erased ordinal zero/live ordinal one; committed-state preservation after empty finalization; actual multi-page and 10,000-owner operation counts; and a full 16 MiB tenant rebuild.
- Baseline broad command `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1` exits 1 on two pre-existing Platform CA1062 errors in `PlatformHostedPublishing.Describe` and `ComposeNativeProbe`. The same project build with `-p:BuildProjectReferences=false` succeeds using already-built unchanged dependency outputs; no warnings are suppressed and no dependency files are edited. An initial test-helper compile omitted seven required ConfigureAwait calls; those are corrected.

## Verification

- Focused build: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1 -p:BuildProjectReferences=false` succeeds with zero warnings/errors. The broad dependency build remains separately blocked as recorded above; the focused lane consumes existing dependency binaries and does not claim to rebuild Platform.
- Final regression run: `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.WorksSdkRebuildAdmissionTests -result-xml /tmp/works-five-rebuild-final.xml` succeeds: 8/8, zero failures/errors/skips/unexecuted, after the review additions.
- Related run using the same executable with `-class Hexalith.Works.IntegrationTests.WorksSdkPartitionedRebuildTests -class Hexalith.Works.IntegrationTests.WorkItemSharedProjectionRebuildHandlerTests -class Hexalith.Works.IntegrationTests.WorksSdkProjectionQueryTests -result-xml /tmp/works-five-rebuild-related.xml` succeeds: 59/59.
- The pre-review broad deterministic run using that executable with `-class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/works-five-rebuild-deterministic.xml` succeeds: 857/857, zero failures/errors/skips/unexecuted. A separate final run covers the review additions. Live smoke and Platform lanes are excluded explicitly; no new live parity qualification is claimed.
- Final plans with 64-character owner identifiers use 21 operations for three multi-page owners and 40,035 for 10,000 owners with 16 multi-page histories, below the configured 40,259. Exactly 16,777,216 bytes of tenant history produce a 29,927,485-byte logical canonical manifest; the real lifecycle executes 416 batches, at most 32 operations and 238,018 canonical bytes each, below the unchanged 8,388,608-byte cap. A monolithic execution is refused before any state write; the real chunked lifecycle commits and reconstructs all four histories byte-for-byte.
- These controls settle the five recorded claims for erased inventory, empty finalization, owner-size preparation, actual operation counts and the tenant-byte ceiling. They do not promise that every possible graph or projected document shape fits every independent SDK limit.

## Review Triage Log

- Medium, verified existing limitation, deferred: the 10,000-owner plan's single queue document is 1,360,142 canonical value bytes and real SDK staging refuses it at the independent 256 KiB mutation bound before writing a candidate. A seeded generation remains readable and unchanged. Keep this population/document-size limitation tracked separately; increasing the canonical batch cap cannot fix a single oversized mutation.
- Medium, patched: independent count/page cases left the additional-page allowance unprotected. The 10,000-owner fixture now includes 16 multi-page histories: 10,032 pages, 40,035 operations and 10,347,008 tenant history bytes. It fails if the registered ceiling is reduced to 40,003.
- Medium, patched: the valid erased-owner gap lacked a negative authoritative-ordinal control. SDK accumulation at ordinal one before zero returns Retryable/DeliveryGap without advancing accepted inventory or creating scratch keys; finalization cannot promote it and a cold reader retains the seeded generation.
- Medium, patched: direct preparation tests did not execute oversized SDK accumulation. A history one byte over 4 MiB now also traverses SDK dispatch; Inspect proves zero accepted owners and the original empty inventory fingerprint, no scratch keys appear, and the seeded generation remains readable. The initial ordinal negative assertion expected Failed; the actual documented Retryable/DeliveryGap outcome is now asserted.
- Low, patched: retained empty-refusal history was checked only by event count. The test now compares its complete serialized bytes before and after refusal.
- Low, clarified: the investigation bullet proposing a conservative manifest-budget adjustment is superseded by the final acceptance below and the earlier chunking correction. The frozen goal is satisfied by qualifying the existing budget, not changing it without evidence.

## Final Acceptance and Scope Correction

The initial budget-adjustment investigation proposal is superseded. Keep all production limits unchanged. Require the exact 4 MiB preparation boundary, empty and refused-accumulation persisted-state preservation, valid erased-owner gaps plus an invalid ordinal control, combined 10,000-owner/multi-page operation counting, and successful full-16-MiB-history staging with actual bounded store batches. The 10,000-owner plan qualifies the operation ceiling only: its large queue document is deliberately tested as a safe staging refusal and retained as separate capacity work. These executing results do not qualify all possible graph/document shapes or newly rebuild upstream Platform.

- Independent follow-up checked all six review findings and their controls, confirmed each addressed and reported no concrete new code regression. One verified pre-existing queue-document capacity limitation is deferred; the five selected ledger entries are closed.
- Final deterministic command: `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/works-five-rebuild-validated-deterministic.xml` exits 0: **859/859 passed**, zero errors/failures/skips/unexecuted. The final focused run is **8/8**, and its build has zero warnings/errors. The final formatting verification passes; its generic workspace-load warning is retained separately from the clean compile.
- [Retained evidence](evidence/five-deferred-rebuild-admission-2026-10-23/manifest.json) binds final test/owner source hashes, dependency revisions/gitlinks, output binary hashes, exact commands, compressed build/test logs and XML, measurements, the initial review assertion failure and the separate upstream broad-build blocker. Production sources, configuration, dependency versions and submodule pointers are unchanged.
- `npm run commitlint -- --edit /tmp/works-five-rebuild-commit.txt` passes for `test: close five deferred rebuild admission gaps`. The pre-commit fetch reported no divergence from origin/main.
