---
title: 'Verify five deferred projection replay and query cases'
type: 'bugfix'
created: '2026-11-23'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Five Works ledger findings describe data that could remain visible, be overwritten, or be read at the wrong time. Verify the current SDK paths for parent replay after child progress, a rebuilt member with no roll-up, rejected or duplicate relationship evidence, an ordinary writer spanning generation promotion, and denied queries with a connected state store. Update the ledger with the observed outcomes.

</frozen-after-approval>

## Implementation Notes

| Ledger finding | Executable evidence |
| --- | --- |
| Parent replay at the same watermark | `ParentRedeliveryPreservesChildProgressAtTheSameParentWatermark` replays a parent while a child commit is paused and again after progress is visible. The parent still lists the child and its served total falls from 14 to 13 hours. |
| Member without a replacement roll-up | `RejectionOnlyReplacementRetiresPreviouslyServedRollUp` retires the owner that supplied the child's spawn facts. The child's rejection history stays identical across generations. Its old estimate becomes unavailable, and the queue is empty. |
| Relationship evidence that was refused | `RejectedParentChangeCannotIntroduceAnEdgeIntoTheCommittedTopology` checks the persisted topology after a rejected reparent. The duplicate-spawn case in `InvalidHistoryCannotPublishPartialProjection` checks that the original generation survives a conflicting envelope. |
| Writer crossing a generation change | `Sdk_rebuild_stages_without_visibility_then_atomically_converges_and_preserves_rollback_keys` submits the same owner-sequenced delivery under stale and refreshed leases. Only the refreshed lease advances the checkpoint. All three retained documents keep their ETags. |
| Authorization before storage access | `Wrong_actor_tenant_and_origin_are_denied_before_any_Works_read` counts zero reads across four denial cases in each handler. Authorized calls to the same handlers reach the observed store. The permitted coordinator test also serves a populated queue. |

Built the Debug integration project using .NET 11.0.100 and the dependency commits pinned by Works `e0d01a4fef62a4f77becd5f23054ea0be52e0fed`. Platform compiled in a standalone checkout at the same pinned revision. The umbrella layout applies Works' CA2007 rule to two async disposals in Platform. The standalone checkout uses Platform's own analyzer configuration.

Ran the integration assembly with the xUnit selectors `-class Hexalith.Works.IntegrationTests.WorkItemSharedProjectionRebuildHandlerTests -class Hexalith.Works.IntegrationTests.WorksSdkProjectionQueryTests`. All 85 cases passed with zero skips. The store-backed cases use the SDK in-memory implementation.

## Review Triage Log

- Medium, patched. The ordinary delivery now includes its owner sequence, and the test checks the resulting persisted checkpoint.
- Medium, patched. The stale lease leaves both the pending count and the existing checkpoint unchanged before the retry.
- Medium, patched. A store hook pauses the child commit while the stale parent delivery runs. A second replay checks the state after progress is visible.
- Low, patched. The table and test selectors identify the evidence for each closure.
