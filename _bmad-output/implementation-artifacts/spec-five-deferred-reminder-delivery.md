---
title: 'Close five deferred reminder delivery gaps'
type: 'bugfix'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Close five related deferred reminder items against the SDK runtime selected by the completed hosting cutover: discarded convergence counts; suspension discovery-write failure and redelivery; cross-tenant discovery preservation under CAS conflict; discovery before scheduling; and sequence-fenced removal of stale pending dates. The legacy processor, suspension scheduler and discovery writer were removed, so verify their current owners instead of restoring them.

Add executing Works regressions using the real SDK event processor, Works suspension wakeup handler, SDK process checkpoint store, authoritative reminder stream fold, and shared projection delivery. Verify failure releases the delivery marker, successful redelivery persists discovery before completion, duplicate delivery does not repeat convergence, competing tenant publication preserves persisted membership, and stale suspension cannot restore cleared awaits. Retained convergence counts are an SDK recovery outcome; acknowledgement is valid when that retained work is durable and discoverable. Verify the SDK's existing scheduling and discovery tests separately rather than duplicating its internal reminder runtime in Works.

Mark exactly these five ledger entries resolved only after the applicable checks pass, recording superseded legacy ownership and the evidence. Commit and push the completed change on main as requested. No production code, public API, dependency, submodule pointer or runtime profile changes are required.

</frozen-after-approval>

## Implementation Notes

- The change adds `WorksSdkReminderDeliveryTests.cs` with four tests (six cases), and resolves exactly five entries in `deferred-work.md`. No production source or submodule pointer changed. The tests reuse the shipped SDK in-memory store, processor, checkpoint store and shared projection coordinator.
- The retained-convergence test verifies the Works acknowledgement boundary using the registrar contract. The separate SDK tests verify actual persisted reminder witnesses, discovery, scheduler failure recovery and obsolete-reminder cleanup; a positive retained count is not itself a failed delivery.
- Debug broad build: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Debug --no-restore -m:1` exits 1 with six pre-existing external Platform compilation errors: missing `AddKubernetesEnvironment`, `SelectedTests`, `ResourceClaimStateDirectory`, `CompositionResourceClaimStore`, plus CA1062 in `PlatformHostedPublishing`. The same blocker is recorded in `spec-five-deferred-host-boundaries.md`. Output: `/tmp/works-five-reminders-debug-build.log`.
- Focused fallback: the same build with `-p:BuildProjectReferences=false` passes with zero warnings/errors, using available dependency outputs. This validates the current Works test compilation, not a complete current Platform source build. Output: `/tmp/works-five-reminders-focused-build.log`.
- Works regression command: `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class '*WorksSdkReminderDeliveryTests' -class '*WorksSdkRecoveryCompositionTests' -class '*WorksSdkIntentTranslationTests' -class '*WorksSdkProjectionQueryTests' -class '*WorksSdkProtectedHttpTests' -result-xml /tmp/works-five-reminders-regression.xml` passes 172/172 with zero errors, failures, skips or unexecuted tests. Console output: `/tmp/works-five-reminders-regression.log`.
- Upstream SDK source revision: `2361e7b6760a7d0065c67661c2e95c2af94bdb4d`. From `references/Hexalith.EventStore`, `dotnet build tests/Hexalith.EventStore.DomainService.Tests/Hexalith.EventStore.DomainService.Tests.csproj -c Debug -m:1 -p:UseHexalithProjectReferences=true -p:UseNuGetDeps=false -p:HexalithEventStoreVersion=3.126.1` passes with zero warnings/errors. No upstream tracked files changed.
- SDK regression command, from that repository: `dotnet tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests.dll -method '*RegistrationPersistsWitnessAndIndexBeforeArming' -method '*PendingStateSurvivesSchedulerFailure' -method '*RemovedIntentReleasesStateThenIndex' -method '*IndependentRegistrationsRetryDiscoveryCas' -result-xml /tmp/works-five-reminders-sdk.xml` passes 5/5 with zero errors, failures, skips or unexecuted tests. Console output: `/tmp/works-five-reminders-sdk.log`.
- Final post-review result supersedes the initial six-case suite: four tests now execute eight cases, including partial publication failures, reconstructed durable marker adapters, authoritative reminder intent folds and direct stale domain folds. The focused Debug build passes with zero warnings/errors (`/tmp/works-five-reminders-final-build.log`). The same Works regression command above, with result output `/tmp/works-five-reminders-final.xml`, passes **174/174**, zero errors/failures/skips/unexecuted tests; the SDK checks remain **5/5**. All five review findings are patched; nothing new is deferred.
- [Retained verification packet](evidence/five-deferred-reminder-delivery-2026-10-23/manifest.json) binds the final test source and assembly, baseline revisions, actual results and ten compressed logs/XML artifacts, including the unsuccessful broad build and its focused fallback. The two Works runs are retained separately to distinguish pre-review and final counts. Decompress any artifact with `gzip -cd <path>`.
- Commit message validation: `npm run commitlint -- --edit /tmp/works-five-reminders-commit.txt` exits 0; exact validated message and CLI output are retained in the verification packet. Whitespace verification: `git -c core.whitespace=cr-at-eol diff --check` passes while honoring the required CRLF endings.

## Review Triage Log

- **Medium, patched:** The original store outage occurred before any discovery publication. Added independent failures after the candidate page is persisted and after the candidate marker is persisted, before tenant-registry publication. Tests inspect the partial persisted state and pending-publication record, then reconstruct and redeliver without duplicate membership.
- **Medium, patched:** Journal deduplication prevented the stale-delivery test from executing the domain fold. Added a direct stale `FoldCatchUpAsync` invocation against the restarted active generation; it proposes no writes and preserves the cleared pending index and checkpoint.
- **Medium, patched:** Projection assertions alone did not prove the Works reminder source stops emitting the obsolete date intent. The real `WorksReminderIntentSource` now refolds the same committed histories after creation, suspension and resume/cancel, with explicit zero/one/zero intent assertions.
- **Medium, patched:** Processor reconstruction reused an in-memory marker authority. Failure/redelivery and retained-result tests now use the production `DaprEventStoreDomainEventMarkerStore` over a substituted Dapr transport backed by the SDK's serialized ETag store. Reconstruction replaces the client, adapter and processor, then verifies persisted completion prevents further convergence.
- **Low, patched:** Temporary logs would not accompany the closure. Retain compressed build logs, test output and XML in the repository evidence packet, with hashes and final source identity in its manifest.
