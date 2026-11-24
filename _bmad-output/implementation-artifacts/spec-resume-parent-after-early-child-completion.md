---
title: 'Resume a parent that suspends after its child already completed'
type: 'bugfix'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Deferred finding R29R-B3. When a child completes while its parent is not waiting on it, the child-completion translation checkpoints with no effect. If the parent then suspends on `ChildCompleted(child)`, nothing resumes it: the parent's own `WorkItemSuspended` wakes its process source, but the translator ignores that event.

**Approach:** Apply the adopted "reconcile in either order" rule (architecture, *Child Resume bound to the installed wait*). When a parent's current suspension awaits a child that it spawned, and that child has already committed `WorkItemCompleted`, translate the suspension into the same `ChildCompletionResume` effect the child would have produced. Leave the full installed-wait witness to Forward Candidate F3-G.

</frozen-after-approval>

## Implementation Notes

- `WorksProcessTranslator` now translates `WorkItemSuspended`, and logs it under its own kind. A suspension without a `ChildCompleted` condition returns before any stream read.
- `SuspensionChildResumeAsync` returns no effect when a later suspension, resume or terminal act exists on the parent, because that act owns the parent's state. It reads each distinct awaited child, in condition order and skipping null conditions, but only children that the parent `ChildSpawned` before the suspension.
- This path never resumes without the parent's reciprocal fact. Parent-only relationships stay governed by the child-side refusal; their semantics remain open as R29R-B2.
- The path only reconciles. A missing child stream, a child whose creation was rejected (no `WorkItemCreated`), a child with no parent, and a child naming another parent all yield no effect, without retrying or quarantining the parent's source. The cascade and child-completion paths keep their refusals for inconsistent relationships, so a parent's later cascade translations are not blocked by this path.
- The first completed awaited child produces exactly one `ResumeWorkItem(parent, ChildCompleted(child))` through `ChildCompletionResumeTranslator`. Its effect identity is (parent, suspension sequence, `ChildCompletionResume`, parent, ordinal 0).
- Aggregate admission (`WorkItemEventStoreAggregate.ValidateCommand`) checks command, effect kind, workload and purpose, but not the source aggregate. The parent-sourced effect therefore uses the same delegated purpose as the child-sourced one.
- When both translations run after both facts exist, both emit a resume with different effect identities. The aggregate turns the second into a domain no-op while the parent is `InProgress` with that condition last consumed. If the parent has already moved on, an ordinary transition rejection is recorded.
- Cost: a child-awaiting suspension re-reads the parent stream and one stream per awaited child, sequentially. With `SpawnChild` plus suspend, that is usually a 404 child read, because the child is created afterwards.
- Limits: suspensions checkpointed before this change are not re-translated. Production admission is still closed (AD-28), so no real parent is affected. The installed-wait witness belongs to F3-G.
- Documentation: `docs/eventstore-api-surface-constraints.md` and `docs/platform-hosting-cutover.md` describe the parent-side reconciliation and its tests.

## Verification

- `WorksSdkIntentTranslationTests.SuspensionResumesFromAnAlreadyCompletedAwaitedChild` has 13 cases. A completed child resumes the parent, with page sizes 1, 2 and 256. The second of two awaited children resumes when only it has completed. A repeated condition is read once.
- The same theory covers the cases that produce no effect: an open child, a missing child, a rejected creation, a foreign parent, no parent, an unspawned child, and a later resume, re-suspension or cancellation. Every case asserts the exact number of child reads.
- `SuspensionWithoutChildAwaitsReadsNoRelationshipContext` checks that a date-only suspension makes no gateway call.
- `WorksSdkIteration4Tests.ChildCompletionCheckpointedBeforeSuspensionResumesFromTheParentSource` uses the real SDK `CheckpointedProcessRunner`. The child checkpoints at 2 with no submission. The parent then submits exactly one effect, (parent, 3, `ChildCompletionResume`, parent, 0), with the expected `ResumeWorkItem`, and checkpoints at 3.
- Release, uncommitted change on Works `5a53f71`: `dotnet …/Hexalith.Works.IntegrationTests.dll` with `-class` set to `WorksSdkIntentTranslationTests`, `WorksSdkIteration4Tests`, `WorksSdkProcessRecoveryTests`, `WorksWhatsNextRelatedReadTests`, `WorksProjectionQueryCancellationTests` and `WorksSdkProjectionQueryTests` passed 215/215. The full suite is run before push.

## Review Triage Log

- **Medium, patched.** A child stream with only a rejected creation wedged the parent; it now yields no effect, with a dedicated case.
- **Medium, patched.** An inconsistent child relationship could quarantine the parent source. This path now yields no effect, and the existing paths keep their refusals.
- **Medium, patched.** The spec claimed the unspawned case matched the child-side refusal. It now says this path does not resume, and that R29R-B2 remains open.
- **Low, patched.** Suspension translations were logged as "other"; they now have their own kind.
- **Medium, patched.** Only one later act was tested. Later resume, re-suspension and cancellation are now covered.
- **Medium, patched.** Multiple awaited children were untested. Coverage now includes a second completed child and a repeated condition (read once).
- **Low, patched.** Page sizes and hostile relationships were untested. Page sizes 1, 2 and 256 are covered, plus no parent, a foreign parent and a rejected creation.
- **Low, patched.** The foreign-parent assertion was weak. It is now a no-effect case with an exact read count.
- **Low, patched (documented).** Duplicate resumes and their rejection noise are now described.
- **False.** "The end-to-end test does not prove authorization": aggregate admission is source-independent (`ValidateCommand`), so the parent-sourced effect passes exactly the check the child-sourced one does.
- **Low, patched (documented).** Previously checkpointed suspensions are not re-translated; production admission is closed.
- **Low, patched (documented).** The read cost is now stated.
- **Low, patched.** The docs described only the child-to-parent reconciliation; both now describe the parent side.
- **Medium, patched.** The ledger was not closed. R29R-B3 is now resolved.
- **Low, patched.** Null conditions were not handled; they are now skipped.
- **Low, patched.** The verification was not reproducible. The command, classes and commit are now recorded, and the full suite runs before push.
