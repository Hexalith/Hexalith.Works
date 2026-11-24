---
title: 'DW-68: Shard Works runtime index watermarks'
type: 'bugfix'
created: '2026-11-24'
status: 'done'
baseline_commit: 'ae9dbe8eccd58ff1e9c0276cc17ed0a2e70d03bf'
route: 'dispatch'
review_loop_iteration: 0
context: ['docs/sdk-projection-query-adoption.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem.** Works keeps queue membership, accepted source sequences, and cleared date-await sequences in two tenant-wide index documents. The SDK refuses the queue document in the existing 10,000-owner fixture because its canonical value exceeds 256 KiB.

**Approach.** Retain every ordering watermark in bounded tenant-scoped index shards. Read existing generations in their current format and use the SDK's staged rebuild and promotion to move them to the sharded format. New generations use the sharded format from their first write.

## Boundaries & Constraints

- Preserve queue filtering, order, parking, pending-date entries, cleared-await sequences, and tenant-local recovery discovery.
- Keep ordinary delivery, per-document size, and tenant history budgets. Count any additional shard operations in the rebuild operation ceiling. EventStore owns all physical writes, activation and rollback.
- Version and validate the shard inventory. Readers require a complete view of the selected generation. A damaged tenant must remain unavailable or incomplete while healthy tenants remain discoverable.

## I/O & Edge-Case Matrix

| Scenario | Input or state | Expected outcome |
| --- | --- | --- |
| Existing generation | A flat index and accepted checkpoints are serving | Ordinary delivery and reads continue in the flat format until a complete authoritative rebuild promotes shards. |
| Bounded rebuild | The existing 10,000-owner fixture, including multi-page histories | Actual SDK staging and promotion succeed. Every index mutation including key overhead fits its budget, and accepted histories and watermarks reopen after reconstruction. |
| Interrupted promotion | A rebuild is aborted or a reader overlaps promotion | The old consistent view remains available where allowed by the SDK. Cross-generation combinations and missing referenced shards are refused. |
| Terminal replay | A retained queue watermark and a cleared date await receive older evidence after restart | Neither the queue item nor the cleared await reappears. Accepted sequence and checkpoint evidence remain intact. |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs` builds both indexes from authoritative history. `WorksRebuildOperationBound.cs` accounts for the materialized documents.
- `src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs` reloads the selected generation's membership, refolds source history and emits only changed canonical documents.
- `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs` and `GetWorkItemQueryHandler.cs` combine queue membership with parking and roll-up documents. `src/Hexalith.Works/Recovery/WorksProcessSourceCatalog.cs` uses SDK-protected discovery reads for each tenant.
- `tests/Hexalith.Works.IntegrationTests/WorksSdkRebuildAdmissionTests.cs` has the executing oversized-queue control. `WorksSdkProjectionQueryTests.cs`, `WorksSdkRecoverySourceTests.cs` and `WorksSdkReminderDeliveryTests.cs` exercise existing generation and replay contracts.

## Tasks & Acceptance

- [x] `src/Hexalith.Works/Projections/WorksReadModelKeys.cs` and `SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs` under the same folder define versioned keys and write bounded, deterministic queue and pending-date shards. Validate coverage, unique membership and byte limits before staging.
- [x] `src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs` selects the format of its pinned generation and preserves byte-based delta writes. Include shard costs in `WorksRebuildOperationBound.cs`.
- [x] `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs`, `GetWorkItemQueryHandler.cs` in the same folder, and `src/Hexalith.Works/Recovery/WorksProcessSourceCatalog.cs` read the supported formats through the SDK. Discovery uses admitted reads with generation and shard-inventory validation.
- [x] `tests/Hexalith.Works.IntegrationTests/WorksSdkRebuildAdmissionTests.cs` and the projection, reminder, and recovery classes in the code map exercise the scenarios above, including a damaged shard and tenant isolation. Update `docs/sdk-projection-query-adoption.md` with the rebuild procedure and close DW-68 in `deferred-work.md` in the same Conventional Commit.

Given a populated flat tenant index, when an exact-source rebuild is staged and promoted while deliveries and reads proceed, then a fresh coordinator reads all expected members, watermarks and eligible items from one consistent generation, and queued deliveries converge under the existing SDK fence.

## Implementation Notes

- Existing flat generations use their original format until promotion. New generations store bucketed index shards and validate the two inventories and their canonical digests when serving queries or discovering recovery work.

## Spec Change Log

## Review Triage Log

| Finding | Verdict and evidence |
| --- | --- |
| Blind 1 — prepared first delivery | High, patch. The SDK checks the digest when it re-folds an interrupted prepared delivery. Drain old journals before replacing writers. |
| Blind 2 — mixed fleet | High, patch. An old reader cannot decode a new manifest. Quiesce new tenant initialization during replacement. |
| Blind 3 — recovery round trips | Maybe-false, deferred measurement. Per-shard SDK reads add provider round trips. The guarded staging case cannot establish actual recovery throughput. |
| Blind 4 — point lookup round trips | Maybe-false, deferred measurement. The detail path now checks the shard inventory. Qualification against the actual caller deadline requires a protected real-store run. |
| Blind 5 — concurrent same-generation roots | Medium, patch verification. A related read must retain a consistent snapshot or report unavailable. The same-generation control verifies the observable result during owner admission. |
| Blind 6 — substituted index root | Medium, patch. A valid empty pending manifest previously met queue validation. Verify the manifest's root identity and require initialized queue membership. |
| Blind 7 — missing generation-zero roots | Medium, deferred. The old fold also treated missing queue evidence as initial state. Repair of lost membership with surviving owner history needs its own SDK-backed recovery case. |
| Blind 8 — oversized single owner | Medium, deferred. A single owner can already exceed the SDK document limit with many projected date awaits. The watermark shard fix preserves that limit. |
| Blind 9 — obsolete page keys | Low, reject. The inventory determines the readable pages, and each bucket has a fixed bounded key range. The SDK owns physical data retention across epochs. |
| Blind 10 — nested record shape | False for the sharded writer. The manifest binds the bytes produced by folding verified histories. Replacing a record and its digest would require a separate authority that this path does not have. |
| Blind 11 — bucket split coverage | Medium, patch. The original 10,000-owner fixture fits without splitting. Stage a separate skewed bucket above the mutation ceiling. |
| Blind 12 — active cutover delivery | Medium, patch. Submit a named delivery during the flat-to-shard fence. Keep the independent SDK abort case. |
| Edge 1 — prepared delivery on upgrade | High, patch. The same replay-digest contract applies before first promotion. Drain legacy prepared deliveries before fleet replacement. |
| Edge 2 — discovery latency | Maybe-false, deferred measurement. The 500 ms catalog policy is real. The assumed 8 ms per read must be checked on the composed provider. |
| Edge 3 — related and shard snapshots | Medium, patch. A related lookup can start after an index shard changes without a root write. Include and validate shard evidence in the related snapshot. |
| Edge 4 — invalid membership metadata | Medium, patch. Preserve the SDK-selected generation and stale flag when validation fails. |
| Verification 1 — byte split | Medium, patch. The 24,486-byte maximum does not execute oversize bucket splitting. |
| Verification 2 — list overlap | Medium, patch. A promotion-only overlap does not cover ordinary writes within an unchanged generation. |
| Verification 3 — recovery overlap | Medium, patch. Complete a held shard read across an ordinary mutation and prove discovery remains incomplete until retry. |
| Verification 4 — stale generation metadata | Medium, patch. The zero-generation malformed fixture did not verify the selected stale generation. |


## Verification

- Build `Hexalith.Works.slnx` and run the DW-68 matrix and affected integration classes for this commit. Run all four test projects and the Python tooling suite on the final batch before pushing.
- Run the Platform parity lane on the final batch so persisted runtime artifacts and the rollback exercise bind the exact source selected for the requested push.

- Release solution build passed. The focused integration run passed 256 cases with zero failures or skips. The migration, overlapping reader, abort, damaged shard and terminal replay controls passed.
- The 10,000-owner case promoted 40,101 operations and reopened all 10,032 history pages after reconstruction. Its 66 index shards had a maximum canonical value size of 24,486 bytes.

- Review verification passed 84 targeted cases. Eight more cases passed in the focused rerun, which verified the request-scoped gateway assertions and all tenant-local recovery scenarios. All 128 Python tooling tests passed.
- The broad Release run passed the unit, architecture and property suites. The SDK proof-preparation fixture hit its existing 285-second child-process deadline both in the integration suite and on the isolated rerun. The other two integration failures passed in the focused rerun after fixing the gateway test to count only request-scoped store calls. The final batch still requires a passing full suite.
