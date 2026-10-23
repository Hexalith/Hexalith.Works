---
title: 'Close five deferred SDK recovery replacement gaps'
type: 'bugfix'
created: '2026-10-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** DW-56, DW-70, DW-71, DW-73 and DW-86 remain open against removed legacy delivery, projection and stream-reader paths. Current SDK replacements need explicit regression evidence for durable completion failure, side-effect-free projection folding, truthful replay outcomes, pending-await rebuild pruning, and exclusive stream cursors.

**Approach:** Close exactly those five entries by exercising current Works adapters and EventStore SDK seams against retained in-memory state. Add missing controls, run focused and deterministic integration checks, independently review, record evidence, and commit and push Works main as requested. Record DW-71 as superseded by explicit SDK dispatch outcomes and authoritative queries, without claiming preservation of the retired response shape.

</frozen-after-approval>

## Implementation Notes

- Baseline: `1db17ac2b84477343e4233bc6dca2d165d2ea4bb`; clean main. EventStore gitlink and checkout: `2361e7b6760a7d0065c67661c2e95c2af94bdb4d`. User authorizes the five-item batch and final push. No intent gaps, production API changes, migrations, dependency updates or external deployment; bounded test/evidence changes select oneshot. Works no longer owns an AppHost; no runtime change requires topology startup.
- Code map: `WorksSdkReminderDeliveryTests` already constructs the real SDK domain-event processor and Dapr marker adapter over serialized in-memory persistence. Fail completion after real suspension discovery/convergence, reconstruct services/adapter, and assert durable dispatched/completed states and no duplicate convergence.
- `WorkItemSharedProjectionRebuildHandler.FoldCatchUpAsync` reconstructs histories and returns batch operations; `WorkItemSharedRebuildManifestBuilder.BuildEpoch` creates new pending dictionaries. Prove an uncommitted resume fold leaves accepted bytes and loaded pending state untouched, then commits correctly through SDK catch-up.
- `WorksSdkProjectionQueryTests` supplies named dispatcher/provider and coordinator query helpers. Deliver newer state, redeliver older history through the actual dispatcher, and prove an explicit duplicate outcome, no computed-state echo and agreement with retained query state after reconstruction. Strengthen unsupported direct projection coverage too.
- `WorkItemSharedProjectionRebuildHandlerTests` supplies complete rebuild Begin/Accumulate/Finalize/Stage/Commit and queries. Seed two suspended histories, rebuild with one authoritative member and prove the selected pending index removes the absent member while retained legacy rollback keys remain untouched.
- `WorksSourceStreamReaderTests.EnvelopePositionsAndMessageIdsSurvivePageBoundaries` already checks exclusive cursor 2, envelope 3 and message identity. Strengthen multi-page coverage to more than one boundary and run the complete class.
- Validation: explicit Debug integration-project build (source mode); focused built-assembly xUnit classes then deterministic integration lane excluding `*SmokeTests` and `*Platform*`. Preserve results plus any exact blocker, and final source hashes in the evidence directory. Check whitespace and commitlint before committing. Independent blind-hunter review is required by the oneshot workflow; triage every finding.

- Broad Debug project build exited 1 with six pre-existing Platform.Composition errors: missing CompositionRunPlan.SelectedTests and ResourceClaimStateDirectory, missing CompositionResourceClaimStore, and CA1062 in PlatformHostedPublishing. Full log retained separately. Production dependency builds completed before that external blocker; the current test compilation uses the documented BuildProjectReferences=false fallback.
- The first focused compilation caught a nullable date comparison and xUnit1030 in the new rebuild test; corrected to the exact nonnullable instant and test-context-preserving ConfigureAwait(true). The final focused Debug compilation passed with zero warnings/errors. A test run started before compilation finished used the old assembly (87 cases); it is explicitly superseded by the subsequent current-assembly run.

- Initial current-assembly Debug checks passed 93 focused and 789 deterministic cases. Blind-hunter review identified five bounded proof gaps; all are patched: actual host legacy dispatcher absence, failed SDK batch/reconstruction, empty pending generation with nonempty authoritative membership, opaque continuation tokens, and paged child-completion/cascade translation. Review-expanded runs correctly exposed two fixture mistakes: SDK wraps batch failure as SharedProjectionWriteUnavailableException, and a captured history cannot regress a retained checkpoint. The final empty-pending control uses a neutral member instead of truncating accepted history.
- Release broad build has the same six external Platform.Composition errors. Release BuildProjectReferences=false additionally fails CS0006 because no Release Platform.Composition reference output exists. The retained reproduce-package.py builds the exact five changed adapter classes and their unchanged helpers in a separate Release test project, references the current Works host and public package pins, and excludes unrelated Platform fixtures. Its initial build passed with zero warnings/errors; final runtime verification follows the review corrections. This does not qualify a complete current Platform build or live deployment.

- The failed-batch control also characterizes the SDK checkpoint correctly: JournalAsync durably advances the admission checkpoint before application. After rejection, that admitted position remains 3, the pending journal count remains 1 and the selected pending document remains at 2; successful retry drains the journal and updates the selected document to 3. It does not treat admission as proof that the projection fold committed.

## Review Triage Log

- **Medium, patched — legacy projection fallback:** Named dispatch alone did not cover the retired /project response. Actual WorksHost services now prove there is no synchronous legacy projection handler; the canonical Project dispatcher returns null. Named replay independently returns AlreadyCompleted with null compatibility state and the newer authoritative query state.
- **Medium, patched — rejected SDK write:** An independently deserialized object is not evidence of reference aliasing. The test now additionally injects an actual batch failure, reconstructs the coordinator and proves selected pending bytes, accepted domain watermark and admitted journal ownership remain intact until retry. The old mutable dispatcher is absent; the current seam returns canonical serialized operations.
- **Medium, patched — final pending removal:** The rebuild control now replaces every dated member with a neutral authoritative member and proves both pending dictionaries empty while membership remains nonempty. Prior checkpoints are never rewound. Historical rollback records remain byte-equivalent.
- **Low, patched — opaque page tokens:** Multi-page controls now return non-null opaque continuation tokens on truncated pages and assert sequence-based requests never forward them. The original DW-86 is the exclusive-cursor defect; this also preserves current token behavior.
- **Medium, patched — process-adapter paging:** Existing child-completion/resume and expiry-cascade translation controls now execute page sizes 1, 2 and 256, placing relationship and await evidence across boundaries. They assert exact effects and consumed-await suppression, alongside the reminder and raw-envelope checks.
- Independent follow-up inspected all five corrections and reported every original finding resolved, with no outstanding finding or additional deferred work. Runtime results are recorded separately below.

## Verification

- Final source Debug fallback build: `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug -m:1 -p:BuildProjectReferences=false` passes with zero warnings/errors. It reuses available dependency outputs; the broad source build remains blocked by the six external Platform errors above.
- Final focused Debug run: `dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class '*WorksSdkReminderDeliveryTests' -class '*WorksSdkProjectionQueryTests' -class '*WorkItemSharedProjectionRebuildHandlerTests' -class '*WorksSourceStreamReaderTests' -class '*WorksSdkIntentTranslationTests' -class '*WorksSdkRecoverySourceTests' -result-xml /tmp/works-five-sdk-recovery-final-regression.xml` passes **119/119**, zero failures/errors/skips/unexecuted.
- Final deterministic Debug run: the same built DLL with `-class- '*SmokeTests' -class- '*Platform*' -result-xml /tmp/works-five-sdk-recovery-final-deterministic.xml` passes **793/793**, zero failures/errors/skips/unexecuted. An earlier concurrent run passed 792/793: unchanged TombstoneOutageAllowsHealthyMembershipAndDurableRecoveryAcrossRestart(outage) returned no healthy member. The fresh focused class and subsequent full deterministic run both pass; the earlier failure is retained separately rather than hidden. This does not establish the cause of that transient failure.
- Final package reproduction: `python3 _bmad-output/implementation-artifacts/evidence/five-deferred-sdk-recovery-replacements-2026-10-23/reproduce-package.py --work-root /tmp/works-five-sdk-recovery-package` passes. The fresh isolated Release build has zero warnings/errors; all **104/104** exact-source cases pass without skips/errors/unexecuted cases. Its runtime dependency manifest confirms all six consumed EventStore libraries, including Client, Contracts, DomainService and Testing, are **package** references at public **3.126.1**. The test-only MinVer override supplies version 1.0.0 for this temporary build; no dependency version changed.
- Final formatter verification for the five changed C# files passes; it emits the existing generic workspace-load warning. `git -c core.whitespace=cr-at-eol diff --check`, reproducer AST parsing, and `npm run commitlint -- --edit /tmp/works-five-sdk-recovery-commit.txt` pass. Only DW-56, DW-70, DW-71, DW-73 and DW-86 change status. No production source, submodule pointer or package pin changes.
- [Retained evidence](evidence/five-deferred-sdk-recovery-replacements-2026-10-23/manifest.json) records exact commands, source/assembly hashes, package identities, final build/test logs and XML, the broad blockers and the earlier transient failure. Full Platform build and live topology qualification are not claimed.
