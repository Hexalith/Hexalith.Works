---
title: 'Harden Story 4.8 projection replay and invalidation'
type: 'bugfix'
created: '2026-09-15'
status: 'done'
route: 'dispatch'
review_loop_iteration: 1
baseline_commit: 'bbfacbbf65ca2747258d7118c99fdee0fd7be0d2'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.8's projection path can mishandle cleared date-await replays, admit invalid source sequences, and omit useful foreign-identity diagnostics. The hardening must not reintroduce the reviewed cache-invalidation loss window caused by suppressing identical retry notification after a durable index commit.

**Approach:** Harden full-replay ordering, parking diagnostics, and event decoding while preserving retry-safe, at-least-once projection invalidation. Pin the behavior with focused deterministic projection and shared-rebuild tests and reconcile the corresponding Story 4.8 artifacts.

## Boundaries & Constraints

**Always:** Derive date-await history from the authoritative full replay; keep monotonic replay watermarks and equal-watermark repair. Preserve post-write `changed && indexAccepted` notification and propagate notifier failures: identical accepted redelivery must retry invalidation even if it duplicates a prior success. Log live `/project` foreign identity with bounded metadata and make logger-free shared rebuild mark that aggregate incomplete. Preserve catalog count 40 and historical live 4/4 evidence, which gives no live-verification credit to this patch.

**Never:** Add a notification outbox/marker, notify before persistence, suppress accepted equal-watermark invalidation, remove the logical `changed` gate, or alter notifier contracts. Do not implement the split command-envelope or reminder recovery/scheduling bundles here. Do not rerun the reminder/mTLS live lane; update submodules or durable contracts; change kernel/Reactor behavior; rename durable keys; implement unpark/replay or unrelated shared-rebuild/cascade fixes; change `sprint-status.yaml`; or reopen DW-56.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Replay ordering | Cleared full replay contains a historical date await, then an older suspended replay arrives | The cleared replay writes a higher tombstone; the older replay cannot resurrect the await | A non-positive source sequence is refused before any write |
| Parking | A state-affecting event remains malformed through the retry budget | Terminal parking uses the correct sequence/count diagnostics and stable retry-race classification | Transient failures still propagate before the terminal disposition |
| Notification retry | Notifier fails after an accepted durable index write, then the replay is redelivered unchanged | Durable state remains correct and notification is attempted again; successful identical redelivery may notify idempotently again | Notifier failure propagates for redelivery; no outbox state is added |
| Stale replay | Replay watermark is strictly older than persisted projection state | Durable state is unchanged and no notification is emitted | Existing monotonic refusal remains authoritative |
| Foreign identity | Known event payload addresses another aggregate | Live `/project` emits bounded identity-mismatch telemetry; shared rebuild marks the affected roll-up incomplete even though its pure builder has no logger | Do not claim per-event shared-rebuild logging or widen its public API |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- add the pre-write positive-sequence gate, full-history date-await tombstones, corrected parking diagnostics/remarks, and stable retry-race flag; preserve `changed && indexAccepted`, equal-watermark acceptance, post-write notification, and propagated notifier failures.
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs`, `src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedRebuildManifestBuilder.cs` -- distinguish and bound live foreign-identity telemetry while retaining pure shared-rebuild incomplete degradation without logger threading.
- `src/Hexalith.Works/Runtime/WorksEventDecoder.cs` -- expose one handled-decode predicate reused by the projection decoder so the failure catalog cannot drift.
- `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs` -- seed an eligible item, fail terminal-removal notification after its index commit, redeliver the identical replay, and prove a second notification attempt plus stable terminal state.
- `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs`, `tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs` -- cover invalid sequences, tombstone ordering, bounded foreign identity, handled decode failures, and incomplete shared rebuild.
- `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/deferred-work.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md`, `docs/whats-next-projection.md` -- supersede duplicate-suppression wording, close only defect (4), and record exact current evidence.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs` -- add the sequence gate and full-replay tombstone rule; repair parking diagnostics/remarks/race classification; retain at-least-once notification behavior and strict stale refusal.
- [x] `src/Hexalith.Works/Runtime/WorksEventDecoder.cs`, `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs` -- share handled decode classification and emit bounded identity-specific telemetry on the live logger-equipped path.
- [x] `tests/Hexalith.Works.IntegrationTests/PendingDateAwaitIndexDispatcherTests.cs`, `tests/Hexalith.Works.IntegrationTests/WorkItemProjectionQueryAdapterTests.cs`, `tests/Hexalith.Works.IntegrationTests/WorkItemSharedProjectionRebuildHandlerTests.cs` -- prove every matrix row, including post-commit notification failure followed by identical successful redelivery.
- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/deferred-work.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md`, `docs/whats-next-projection.md` -- supersede the older duplicate-notification suppression finding, accurately close shared-rebuild defect (4), record exact deterministic/full-gate results, and preserve sprint/live-evidence boundaries.

**Acceptance Criteria:**
- Given a cleared replay with historical date-await evidence, when it wins before an older suspended replay, then the durable tombstone prevents resurrection.
- Given terminal removal commits and its notification throws, when the identical full replay is redelivered, then notification is attempted again and terminal persisted state remains stable.
- Given the focused and repository gates run, when verification completes, then changed deterministic tests pass, Release build has zero warnings/errors, all in-scope architecture constraints pass with no new failures beyond the recorded SDK-pin assertion, and catalog/historical-live-evidence claims remain accurate.

## Implementation Notes

The earlier broad implementation summarized below was reverted to `baseline_commit` after review exposed EC-03.
The current working tree reinstates only the projection/decoder bullets that remain in this spec's active scope;
command-envelope and reminder recovery/scheduling notes are retained solely as audit history for the two deferred
split bundles.

- Added `CommandEnvelope` to every aggregate `Handle` overload and centralized payload/envelope reserved-tenant
  checks; the complete command catalog is exercised through EventStore's reflection dispatcher.
- Made pending-date replay ordering depend on full historical evidence and rejected non-positive source sequences
  before any write. The prior durable-state-change notification gate is known-bad and must not return.
- Added countable parked skips to clean and incomplete scan outcomes, isolated parking-store read failures under
  EventId 4608, raised parked skips (4607) to Warning, and retained partial-result retry signalling.
- Added bounded EventId 4609 scheduling-failure context with exception propagation. Multiple distinct
  `DateReached` conditions remain independently and deterministically scheduled.
- Unified runtime/projection handled-decode classification, logged foreign identity on the live projection path, and pinned shared rebuild's
  deliberate incomplete degradation. Only deferred-work defect (4) was closed; defects (1)–(3) were preserved.
- Strengthened option, processor, retry-budget, parking-log, cleanup, and malformed-evidence regression tests.
  No durable contracts, keys, kernel/Reactor code, submodules, sprint tracking, or live topology were changed.

## Spec Change Log

- 2026-09-15 -- Completed the projection-only reimplementation after the EC-03 revert. Preserved retry-safe
  at-least-once invalidation, closed only shared-rebuild defect (4), and recorded the current deterministic gates.
- 2026-09-15 -- Implemented all execution tasks and recorded deterministic verification. Frozen intent,
  constraints, matrix, and acceptance criteria were not changed.
- 2026-09-15 -- EC-03 exposed a post-commit notifier failure window. The implementation was reverted. The human chose retry-safe at-least-once invalidation, accepting idempotent duplicate notifications instead of durable outbox state. The matrix, constraints, projection task, acceptance criteria, and design notes were amended accordingly. BH-08 was reconciled by requiring live-path identity telemetry and pure shared-rebuild incomplete degradation without falsely claiming that the logger-free builder logs. KEEP: full-replay tombstones, monotonic writes, partial-scan retry signalling, bounded warnings, deterministic reminder identity, and the review-surviving test patches.
- 2026-09-15 -- The human selected the token-risk split. This spec now owns only projection replay/invalidation and shared decoding. Reserved-tenant command-envelope hardening and reminder recovery/scheduling hardening were appended to `deferred-work.md`. KEEP: at-least-once notifier retry, full-replay tombstones, strict stale refusal, bounded live foreign-identity telemetry, pure shared-rebuild incomplete degradation, and accurate historical evidence.

## Review Triage Log

| ID | Layer | Verdict | Route | Evidence |
|---|---|---|---|---|
| BH-01 | blind-hunter | medium | defer | Verified: `EventStoreAggregate.ProcessAsync` does not compare payload identity with the envelope, so the fourteen non-`LinkConversation` handlers can emit foreign-identity events. This defect predates this story; the approved change only adds the reserved-envelope-tenant check. |
| BH-02 | blind-hunter | medium | defer | The 15-row theory reaches every new three-parameter reflection handler, disproving the successful-dispatch concern, but ordinary domain/tenant/aggregate mismatch behavior remains untested for the same pre-existing identity defect as BH-01. |
| BH-03 | blind-hunter | false | reject | Production EventStore delivery assigns positive sequences and `ProjectionDeliveryFingerprint.ComputeHistory` rejects non-positive or non-contiguous histories before delivery. A direct invalid `/project` request is intentionally refused before writes by the frozen matrix. |
| BH-04 | blind-hunter | false | reject | `DomainSharedProjectionRebuildDispatcher.ValidateEventHistory` rejects every shared-rebuild history not contiguous from sequence one before the Works handler can prepare relationship payloads. |
| BH-05 | blind-hunter | false | reject | Notification still requires `changed`, which comes only from `WhatsNextQueueProjection`'s eligibility/order signature. The new full-item `indexStateChanged` value is an additional conjunct and cannot make binding/remaining-only changes notify. |
| BH-06 | blind-hunter | low | reject | Duplicate equal conditions can make an extra scheduler call, but `DaprDateReminderScheduler` targets the same actor/reminder name and `DateReminderActor` overwrites it idempotently. The residual extra call is negligible and the frozen intent explicitly preserves duplicate-registration idempotence. |
| BH-07 | blind-hunter | medium | patch | Verified: the catch filter suppresses EventId 4609 for any exception whenever cancellation happens to be requested, including a concurrent non-cancellation scheduler failure. Only token-attributable cancellation should bypass the warning. |
| BH-08 | blind-hunter | medium | bad_spec | Verified: shared rebuild calls `WorkItemProjectionEventDecoder.Decode(..., logger: null)`, so foreign identity degrades rolled shapes but produces no skip telemetry on that path despite the task/ledger closure claiming logged and incomplete behavior. Fixing it requires threading a logger through the public rebuild handler rather than a trivial local correction. |
| BH-09 | blind-hunter | medium | patch | Verified: a namespace-qualified known event name can carry an arbitrarily long prefix, and the new foreign-identity branch logs the full value under the inaccurate generic “undecodable” reason. Bound the value and distinguish identity mismatch. |
| BH-10 | blind-hunter | medium | defer | Verified: the pre-existing parking document has no invariant validation, and `Parked=true` with default sequence/count suppresses the authoritative stream read. This corruption-hardening defect was not introduced by Story 4.8. |
| BH-11 | blind-hunter | false | reject | The pass count is exposed in `ReminderReconciliationOutcome`, and each parked candidate emits EventId 4607 at Warning. The approved matrix requires the skip to be counted and warned, not a second aggregate summary log. |
| BH-12 | blind-hunter | low | reject | Public alternate implementations can construct null/negative scan results, but the production indexed source cannot. This is a low-probability contract-hardening issue whose guard complexity is not justified in this review. |
| BH-13 | blind-hunter | medium | patch | Verified: no test combines a parked candidate with a failing candidate, so the incomplete-exception and EventId 4605 parked-count wiring can regress while clean-scan tests remain green. |
| BH-14 | blind-hunter | false | reject | All in-scope architecture constraints and the catalog guard pass 236/236; only the explicitly deferred SDK-pin assertion fails. The spec records that exact exception rather than claiming an unqualified 237/237 pass. |
| BH-15 | blind-hunter | false | reject | The frozen intent explicitly forbids rerunning the live lane, and the artifacts identify the retained 4/4 evidence as belonging to the older revision and state that current code was not live-reverified. |
| EC-01 | edge-case-hunter | medium | defer | Verified duplicate of BH-01: non-reserved payload/envelope identity mismatch remains possible for fourteen handlers, but it is a pre-existing broader identity-policy defect outside the approved reserved-tenant change. |
| EC-02 | edge-case-hunter | false | reject | Production projection histories are full and contiguous by EventStore contract and validation. Even a nonconforming direct request that leaves a stale discovery entry cannot resume cleared work because recovery re-folds the authoritative stream before acting. |
| EC-03 | edge-case-hunter | medium | intent_gap | Verified: the notifier can throw after the index commits; retry then observes identical durable state and suppresses the missed invalidation. Avoiding both missed invalidation and duplicate notification needs a durability/at-least-once policy that the frozen matrix does not choose. |
| EC-04 | edge-case-hunter | medium | patch | Verified duplicate of BH-07: concurrent token cancellation hides a real non-cancellation scheduler failure from required telemetry. |
| EC-05 | edge-case-hunter | medium | patch | Verified: the removed reserved-payload test was replaced only by reserved-envelope rows. The retained payload guard can regress without failing the new theory, contrary to the frozen “retain payload validation” constraint. |
| EC-06 | edge-case-hunter | maybe-false | defer | The new parking-read warning passes the provider exception to logging, which could carry unbounded or sensitive text, but the reachable provider exception shapes and sink redaction are not established. Inspect those runtime exception contracts to settle this potential medium privacy issue. |
| VG-01 | verification-gap | medium | patch | Pre-verified: terminal index removal is part of the new state-change gate, but no notifier test proves one accepted removal notification followed by suppression of its identical redispatch. |
| VG-02 | verification-gap | medium | patch | Pre-verified: clean parked scans and incomplete scans are tested separately, but no test proves an incomplete scan preserves and logs its nonzero parked-skip count. |
| VG-03 | verification-gap | low | patch | Pre-verified: the scheduler-failure test checks EventId/reason/secret omission but not the expected tenant, work item, and deterministic reminder name. Direct assertions close this diagnostic gap. |
| VG-04 | verification-gap | medium | patch | Pre-verified: the maximum-attempt test calls `StopAsync` as soon as attempt three begins, so cancellation can mask failure to complete the bounded loop naturally. Await `ExecuteTask` before stopping. |
| VG-05 | verification-gap | low | reject | Disposal-failure cleanup is untested, but forcing it needs an extra injectable test seam for an unlikely test-host-only branch. Under the low-finding rule, that complexity is not justified. |

Grouped survivors: EC-03 is the controlling `intent_gap`. BH-08 is `bad_spec`; BH-07/EC-04, BH-09, BH-13/VG-02, EC-05, VG-01, VG-03, and VG-04 are patch groups; BH-01/BH-02/EC-01, BH-10, and EC-06 are deferred groups. The controlling intent gap makes every lower route moot in this iteration.

Loopback resolution (2026-09-15): the human selected retry-safe at-least-once invalidation, resolving EC-03; BH-08 was reconciled as live-path logging plus logger-free shared-rebuild degradation. The later token-risk split moved command-envelope and reminder recovery/scheduling patch groups to `deferred-work.md`; only the projection/decoder groups control this pass.

### Review pass 2 (2026-09-15)

| ID | Layer | Verdict | Route | Evidence |
|---|---|---|---|---|
| R2-BH-01 | blind-hunter | medium | defer | `AddEventStoreDomainService` uses the Client registration path, which supplies no notifier implementation, and `WorksHost` resolves `IProjectionChangeNotifier` optionally; production therefore has no invalidation unless an external customization registers one. This seam-only behavior predates the patch. |
| R2-BH-02 | blind-hunter | false | reject | `DaprProjectionChangeNotifier` propagates PubSub publication and actor ETag regeneration failures. Only the later SignalR broadcast is deliberately fail-open after primary invalidation, which the documentation explicitly states. |
| R2-BH-03 | blind-hunter | false | reject | carried: prior BH-03 established that EventStore production delivery validates a contiguous one-based history through `ProjectionDeliveryFingerprint.ComputeHistory`; the bespoke handler's direct-request boundary remains outside that production path. |
| R2-BH-04 | blind-hunter | medium | defer | Unknown event types are skipped without setting live roll-up incomplete, unlike shared rebuild. This forward-version completeness defect is real but predates the patch. |
| R2-BH-05 | blind-hunter | medium | defer | The pending-date watermark uses the maximum raw sequence while state uses decoded events, so an unknown higher event can advance the watermark beyond understood evidence. This shares the pre-existing unknown-event handling defect with R2-BH-04. |
| R2-BH-06 | blind-hunter | false | reject | Equal-watermark repair in the approved intent applies to the what's-next index notification path. The deterministic pending-date index deliberately rejects equality, while strict older refusal and initially absent cleared tombstones satisfy its matrix behavior. |
| R2-BH-07 | blind-hunter | medium | defer | Generic unknown/malformed EventId 4504 logging still accepts raw event type and correlation text. That pre-existing bounded-logging defect is outside the new identity-specific branch. |
| R2-BH-08 | blind-hunter | false | reject | EventId 4505 identifies the requested tenant/work item, catalog-resolved type, bounded correlation, and identity-mismatch reason. “Identity-specific” describes the reason; the intent does not require logging attacker-controlled foreign identity or sequence. |
| R2-BH-09 | blind-hunter | low | reject | The shared predicate is directly used by both decoders and its failure catalog is tested. A structural test forbidding a future private predicate would add brittle implementation policing for an unlikely deliberate regression. |
| R2-BH-10 | blind-hunter | false | reject | The proposed distinguishing test requires a retry to observe `Parked=true` and then an unparked value. Parking is monotonic and there is no unpark/delete path, so that transition is unreachable; existing tests cover both reachable first-park and already-parked outcomes. |
| R2-BH-11 | blind-hunter | low | reject | `WorksEventIdentityTests` separately pins aggregate-id disagreement and the new integration tests prove the foreign branch. Component-by-component integration theories would duplicate the matcher contract for negligible added protection. |
| R2-BH-12 | blind-hunter | false | reject | The builder calls `MarkIncomplete(history.AggregateId)`, which scopes degradation to the current history. The single-aggregate comment does not claim a separately tested cross-aggregate invariant. |
| R2-BH-13 | blind-hunter | false | reject | The two split records use the exact three-field format mandated by the build workflow; adding DW identifiers/status or rewriting them would violate that instruction. |
| R2-BH-14 | blind-hunter | low | patch | The ledger wording implies this patch introduced shared-rebuild incomplete degradation, although that behavior existed at baseline. Clarify that the patch verifies the behavior and closes a stale defect record while adding live telemetry. |
| R2-BH-15 | blind-hunter | low | patch | The changed notification paragraph says an index-refused stale replay changed no persisted state, but its roll-up may already have been repaired. State precisely that the tenant index did not move. |
| R2-EC-01 | edge-case-hunter | false | reject | carried: duplicate of R2-BH-03/prior BH-03; production replay histories are validated upstream before `/project`, while this patch's direct-boundary rule is specifically non-positive no-write refusal. |
| R2-EC-02 | edge-case-hunter | medium | patch | `ConversationLinked` with null correlation is classified before identity, so a foreign payload misses bounded EventId 4505 and reaches generic EventId 4504. Identity must be checked first. |
| R2-EC-03 | edge-case-hunter | false | reject | Duplicate of R2-BH-10: the requested `Parked=true` then unparked retry transition cannot occur under the terminal monotonic parking model. |
| R2-VG-01 | verification-gap | low | patch | The no-write sequence theory covers zero/negative values only as the sole event; a valid leading event plus later invalid event directly protects the whole-list scan. |
| R2-VG-02 | verification-gap | false | reject | The filed demonstration relies on persisted parking moving from terminal `Parked=true` back to unparked between retries, which no production writer or operator path can do. Reachable parking classifications already have passing tests. |
| R2-VG-03 | verification-gap | low | patch | EventId 4505 coverage does not assert the requested tenant, work item, or exact retained 128-character correlation prefix; direct assertions protect those diagnostic arguments. |

## Design Notes

The full replay, not the current index document, determines whether an aggregate ever held a date await. This permits an initially absent cleared replay to write a higher tombstone watermark while aggregates with no date-await history still create no index document. Equal-watermark repair remains allowed. Notification follows the replay's logical what's-next change plus accepted index write, not a durable byte-delta gate: if notification throws after commit, identical redelivery attempts it again, so delivery is at-least-once and consumers must tolerate duplicates.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -m:1` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class <each changed focused class>` -- expected: all selected deterministic tests pass with no live topology start.
- `dotnet build Hexalith.Works.slnx -c Release -m:1` -- expected: zero warnings and errors.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` -- expected: no new failures; record the exact pre-existing `10.0.400` versus `10.0.401` SDK assertion separately, and require every other architecture test to pass.

**Current-run results (2026-09-15):**
- Post-review IntegrationTests project build: 0 warnings, 0 errors.
- Focused direct xUnit runs: `PendingDateAwaitIndexDispatcherTests` **23/23**,
  `WorkItemProjectionQueryAdapterTests` **31/31**, and `WorkItemSharedProjectionRebuildHandlerTests`
  **19/19** passed, 0 skipped (**73/73** combined).
- `dotnet build Hexalith.Works.slnx -c Release -m:1 -p:NuGetAudit=false --no-restore`: 0 warnings, 0 errors.
- Pre-follow-up broad runs: UnitTests **568/568**, PropertyTests **3/3**, and deterministic IntegrationTests
  excluding `*SmokeTests` **332/332** passed, 0 skipped.
- Full ArchitectureTests: **236/237**. The sole failure remains the pre-existing, explicitly deferred SDK
  assertion expecting `10.0.400` while `global.json` pins `10.0.401`; excluding only that method passes
  **236/236**. The green set includes the durable catalog guard at **40**, preserving Story 4.8 delta zero.
- Per the frozen boundary, the reminder/mTLS live lane was not rerun. Historical **4/4** evidence remains
  unchanged and gives this patch no live-verification credit.
