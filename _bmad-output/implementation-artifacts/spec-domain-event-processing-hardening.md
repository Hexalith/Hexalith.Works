---
title: 'Harden domain event completion and isolation'
type: 'bugfix'
created: '2026-09-05'
status: 'blocked'
baseline_revision: '547c6d896d256ba9630f713054ea2b073262f0ef'
baseline_commit: '547c6d896d256ba9630f713054ea2b073262f0ef'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: [multiple-goals]
deferred: []
---

<intent-contract>

## Intent

**Problem:** A successful handler dispatch can be acknowledged as `Processed` when the durable completion write fails, allowing a later delivery to run handlers again. The Works processor also accepts foreign-domain envelopes far enough to acquire markers and potentially dispatch them.

**Approach:** Extend the EventStore marker protocol with a durable post-dispatch phase and completion-only reacquisition, then consume it in both the generic and Works processors. Validate the Works domain before any marker operation.

## Boundaries & Constraints

**Always:** Preserve existing enum numeric values; use caller-token-independent finalization; make DAPR transitions monotonic and fail when persistence is not confirmed; preserve `Dispatched` and `Completed` markers on release; keep existing terminal-skip outcomes and endpoint mappings; use exact ordinal `work` domain matching.

**Never:** Acknowledge a completion failure as `Processed`; redispatch handlers from a completion-pending marker; let a foreign-domain envelope acquire or complete a Works marker; edit the deferred-work ledger; add a second persistence mechanism or a new processing-result enum.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Normal dispatch | `Acquired`, handler succeeds | Persist `Dispatched`, then `Completed`; return `Processed` | Persistence rejection/failure remains retryable |
| Completion failure | `Dispatched` persists, `Completed` fails | Do not release or acknowledge | Failure escapes to the existing HTTP 500 path |
| Completion redelivery | Existing `Dispatched` marker | Complete only; no decode or handler call; return `Duplicate` | Repeated completion failure remains retryable |
| Foreign domain | `Domain` is null, differently cased, or not `work` | Return `FailedInvalidPayload` before marker access | Terminal invalid-envelope handling |
| Existing/unknown marker | `Completed`, `InProgress`, or unknown state | Duplicate, retryable, or fail-closed respectively | Never reacquire unknown durable state |

</intent-contract>

## Code Map

- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/IEventStoreDomainEventMarkerStore.cs` -- public marker seam; add compatibility-safe post-dispatch transition.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/EventStoreDomainEventMarker{State,AcquisitionResult,Record}.cs` -- preserve current ordinals and append `Dispatched`/`CompletionPending` plus record factory.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/{Dapr,InMemory}EventStoreDomainEventMarkerStore.cs` -- implement monotonic completion; DAPR keeps acquisition/release lease-free and checks post-dispatch CAS results, while in-memory release removes only `InProgress`.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/EventStoreDomainEventProcessor.cs:108` -- branch completion-pending before decoding; persist post-dispatch before final completion and surface finalization failure.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Client.Tests/Subscriptions/{EventStoreDomainEventMarkerStoreTests,EventStoreDomainEventProcessorTests}.cs` -- protocol, failure, and completion-only regression coverage.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Integration/DomainEventMarkerLiveSidecarTests.cs` -- exercise the durable `Dispatched` to `Completed` path against the Redis-backed DAPR state component.
- `references/Hexalith.EventStore/docs/reference/{stream-replay-api,nuget-packages}.md` -- replace unsafe marker semantics and record coordinated-rollout constraint for the appended state.
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs:39` -- exact domain guard before acquisition; mirror completion-only and durable finalization flow. Reuse `WorkCommandSubmission.WorkDomain`.
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs:72` -- focused dispatch-once/redelivery and pre-acquisition foreign-domain proofs.
- `.bmad-loop/runs/20260905-130936-6924/bundles/domain-event-processing-hardening/intent.md` -- read-only source intent; do not modify it or the ledger.

## Tasks & Acceptance

**Execution:**
- [x] `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/*.cs` -- add the compatible protocol and checked, monotonic store transitions -- make post-handler progress durable and actionable on redelivery.
- [x] `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/EventStoreDomainEventProcessor.cs` -- finalize `CompletionPending` before payload decoding, preserve terminal-skip semantics, and propagate post-handler completion failure -- prevent false acknowledgement and duplicate side effects.
- [x] `references/Hexalith.EventStore/tests/Hexalith.EventStore.Client.Tests/Subscriptions/*.cs`, `references/Hexalith.EventStore/tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Integration/DomainEventMarkerLiveSidecarTests.cs`, and `references/Hexalith.EventStore/docs/reference/*.md` -- cover and document states, strong-consistency reads, CAS failure/conflict convergence, release safety, redelivery, durable Redis state, and safe rollout.
- [x] `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs` and `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` -- align Works, including persistent completion-only failure, and prove exact domain isolation at the outer processor boundary.

**Acceptance Criteria:**
- Given handlers ran once and the durable post-dispatch write succeeded, when completion fails and the same message is redelivered, then the first delivery is not acknowledged, the second performs completion only, and handler invocation count remains one.
- Given DAPR rejects or cannot confirm a marker transition, when processing reaches that transition, then the failure is surfaced and no success result is fabricated.
- Given a marker is already `Completed` or `InProgress`, when the message is delivered, then existing duplicate or retryable behavior remains unchanged.
- Given a Works envelope whose domain is not exactly `work`, when it is processed, then it is rejected before marker acquisition and no handler runs.

## Spec Change Log

### 2026-09-05 — Review repair 1

- Trigger: the first review pass found that delegating the new dispatch marker to legacy completion and then completing again was not compatibility-safe; it could double-call a non-idempotent third-party store, release after an ambiguous committed write, and made the intended two-phase guarantee unclear. The same pass found full-envelope validation ahead of completion-only reacquisition, strict completion on terminal skips, eventual-consistency DAPR reads, and insufficient durable-store/conflict coverage.
- Amended: the marker seam now returns whether `MarkDispatchedAsync` left a distinct completion-pending state. Its default completes through the legacy method and returns `false`; built-in durable stores override and return `true`. Processors disable release immediately after handlers succeed, call final completion only when the returned value is `true`, and keep strict failure propagation limited to post-handler and completion-only paths. Only message-ID syntax (plus exact Works domain) precedes marker lookup; remaining validation occurs after acquisition so malformed redelivery can still finalize without decode. DAPR reads use strong consistency and checked ETag/`FirstWrite` retries, with targeted unit, conflict, persistent-failure, and Redis live-sidecar coverage. Documentation must state state-store concurrency requirements and require draining old consumers before enabling the appended state.
- Known-bad avoided: no default method that silently performs completion followed by an unconditional second completion; no release after handlers ran; no new retry loops for existing terminal skips; no payload dependency in completion-only recovery; no eventual-consistency marker decision; no rollout wording that permits mixed old/new consumers.
- KEEP: explicit enum ordinals and appended `Dispatched`/`CompletionPending`; lease-free DAPR acquisition and no-op release; exact ordinal Works `work` guard before marker access; completion-only branching before decode/dispatch; caller-token-independent finalization; monotonic ETag CAS that accepts already-advanced markers; focused processor/store tests and coordinated-rollout documentation.

## Review Triage Log

### 2026-09-05 — Review pass
- verdicts: 25 findings — high 4, medium 14, low 4, false 3, maybe-false 0
- findings:
  - `[high]` `[patch]` DAPR acquisition used default consistency and could read an obsolete pre-dispatch value — verified at `DaprEventStoreDomainEventMarkerStore.TryAcquireAsync`; the re-derivation amendment requires strong-consistency reads.
  - `[medium]` `[patch]` DAPR transition retries also used default consistency and could repeatedly observe stale state — verified in `TransitionAsync`; the re-derivation amendment requires strong reads on every ETag attempt and conflict-convergence tests.
  - `[low]` `[patch]` package documentation did not state that the configured DAPR store must support ETags and first-write concurrency — verified in both edited reference pages; the re-derivation amendment adds the deployment prerequisite.
  - `[medium]` `[bad_spec]` the default dispatch method completed legacy stores and processors then completed them a second time — verified from the interface default and both processor call sequences; the spec now requires a boolean compatibility result so legacy completion is called once.
  - `[high]` `[bad_spec]` an ambiguous legacy completion failure could enter the release path after handlers had already run — verified because the release flag remained true until the default dispatch call returned; the spec now disables release immediately after successful handler execution.
  - `[medium]` `[bad_spec]` strict completion on terminal-skip paths changed existing best-effort skip outcomes into retrying failures — verified across unsupported-format, unknown-type, decode, mismatch, and no-handler branches; the spec now limits strict completion to post-handler and completion-only paths.
  - `[medium]` `[bad_spec]` full-envelope validation before marker lookup prevented completion-only recovery from malformed redeliveries — verified in the generic processor; the spec now permits only message-ID validation before acquisition and delays all decode metadata checks.
  - `[medium]` `[patch]` rollout documentation allowed mixed old/new consumers despite old readers treating the appended state as acquirable — verified in the edited rollout note; the amendment requires draining old consumers before enabling new writers/readers.
  - `[false]` `[reject]` the superproject gitlink was allegedly omitted — disproved because review runs before the skill's mandated submodule-first commit and parent gitlink update; finalization owns that step.
  - `[medium]` `[bad_spec]` no live DAPR sidecar test proved the durable `Dispatched` to `Completed` state path — verified: only mocked client tests were added; the spec now includes a Redis-backed live-sidecar test.
  - `[medium]` `[patch]` DAPR tests omitted direct absent-to-completed and save-conflict-to-advanced-state cases — verified in marker-store tests; both sequences are now explicit acceptance tests in the amended plan.
  - `[low]` `[patch]` dispatch-marker failure lacked a structured processor log and CAS exhaustion lacked message/target context — verified in both processors and the DAPR exception; the amendment requires contextual diagnostics.
  - `[high]` `[patch]` edge-case review independently found eventual DAPR acquisition could redispatch after a stale read — verified at the same acquisition call; strong consistency is required by the amended design.
  - `[false]` `[reject]` mapping persisted `InProgress` to retryable would wedge a formerly acquirable DAPR state — disproved because the baseline DAPR store never persisted `InProgress`, while the intent explicitly requires existing `InProgress` to remain retryable.
  - `[low]` `[reject]` a stored JSON `null` with an ETag could be treated as absent — the condition is possible only through corrupt or external state, is unlikely in everyday use, and guarding it would add a new state branch without evidence that supported writers create it.
  - `[medium]` `[bad_spec]` edge-case review independently found generic completion-only recovery depended on otherwise irrelevant envelope metadata — verified by validation ordering; the amended processor flow reacquires after message-ID validation and completes before decode.
  - `[medium]` `[bad_spec]` Works completion-only recovery likewise depended on full envelope metadata — verified by validation ordering; the amended Works flow checks exact domain and message ID, then completes pending state before decode.
  - `[medium]` `[bad_spec]` edge-case review independently found the default interface method could double-complete non-idempotent stores — verified from the unconditional second completion; the boolean compatibility contract now prevents the second call.
  - `[medium]` `[patch]` unit coverage omitted a direct absent-to-completed DAPR transition used by terminal skips — accepted as pre-verified by the verification-gap layer; the amended test matrix adds it.
  - `[medium]` `[patch]` unit coverage omitted a failed-save followed by reread of an already `Dispatched` or `Completed` marker — accepted as pre-verified; the amended matrix requires both monotonic conflict outcomes.
  - `[medium]` `[patch]` neither processor tested repeated failure while reacquisition already returned `CompletionPending` — accepted as pre-verified; the amended matrix requires propagation, zero handlers, and no duplicate result for both processors.
  - `[high]` `[patch]` verification review independently identified default-consistency acquisition as a redispatch risk — accepted as pre-verified; the amended implementation and mock assertions require strong consistency.
  - `[false]` `[reject]` the compatibility implementation was said to violate a uniquely required universal-store reading — disproved because the intent permits a compatibility-safe fallback while requiring built-in durable stores to implement the distinct phase.
  - `[medium]` `[bad_spec]` the strongest joined durability proof did not exercise the DAPR-backed marker through a live state component — verified from the test surface; the spec now adds a focused Redis/DAPR sidecar protocol test.
  - `[low]` `[reject]` domain isolation lacked an external HTTP-level foreign-domain proof — the processor test exercises the exact ledger defect before marker access, while an additional transport test is unlikely to expose different everyday behavior and would add disproportionate fixture complexity.

### 2026-09-05 — Review pass 2
- verdicts: 24 findings — high 0, medium 8, low 14, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` DAPR transitions could overwrite a persisted `InProgress` marker owned by another attempt — verified in `ClassifyExistingState`; the state is now rejected contextually and covered by a focused test.
  - `[medium]` `[patch]` unknown durable acquisition state was collapsed to an undocumented `-1` result — verified in `TryAcquireAsync`; it now throws with message ID and stored-state context, and its test asserts fail-closed behavior.
  - `[low]` `[patch]` Works returned retryable for an unsupported acquisition result without a diagnostic — verified in the unmatched-result branch; a structured warning now records the result and message ID.
  - `[low]` `[reject]` caller-independent finalization had no additional processor-owned deadline — custom stores can theoretically hang, but DAPR supplies transport timeout behavior and introducing a new timeout policy/configuration is disproportionate to this bundle's caller-cancellation requirement.
  - `[low]` `[reject]` raw DAPR read/save exceptions lacked store-level message and target wrapping — processor-level failure logs already correlate the message, while wrapping every transport/cancellation exception would alter public exception semantics for negligible additional everyday value.
  - `[low]` `[patch]` Works marker-failure logs omitted the marker message ID — verified in the generated event; `MessageId` is now present at every call and asserted by the logging test.
  - `[low]` `[patch]` documentation named ETags and first-write concurrency but omitted the required strong-consistency capability — both reference pages now name all three state-component prerequisites.
  - `[low]` `[patch]` `stream-replay-api.md` still described the DAPR store as recording only terminal completion — the text now accurately says it omits a pre-handler lease while persisting `Dispatched` and `Completed`.
  - `[medium]` `[patch]` rollout guidance omitted rollback safety while `Dispatched` markers may remain — both pages now require the new cohort to finish pending markers before old consumers restart.
  - `[low]` `[patch]` compatibility documentation did not state that default-method stores retain their prior durability limits — the reference now states the limitation and the override needed for recoverable completion-only delivery.
  - `[medium]` `[patch]` unit coverage omitted the central ETag-checked `Dispatched` to `Completed` transition — a focused test now asserts the existing ETag, `FirstWrite`, and `Completed` replacement.
  - `[low]` `[reject]` the live Redis test omitted a forced replica/concurrent-writer race — monotonic conflict behavior is deterministically covered at the store seam, while a timing-dependent live race would be unreliable and the documented protocol intentionally provides no pre-handler exclusion.
  - `[low]` `[reject]` processor exceptions were not repeated through full generic and Works HTTP hosts — both unchanged endpoint delegates directly await the processor without catching, existing endpoint tests pin retryable result mappings, and duplicating two host fixtures would be disproportionate.
  - `[medium]` `[patch]` post-acquisition invalid-envelope tests did not prove release permits a corrected redelivery — both processor suites now run a corrected delivery through the same in-memory marker and assert one handler invocation.
  - `[medium]` `[patch]` the final convergence read after five rejected DAPR writes was not explicitly asserted strong — accepted as pre-verified; a sequenced test now observes the advanced marker on the sixth strong read.
  - `[medium]` `[patch]` Works did not exercise the legacy-store `MarkDispatchedAsync == false` branch — accepted as pre-verified; a compatibility test now proves exactly one completion and no release.
  - `[medium]` `[patch]` Works did not prove post-handler transitions ignore a canceled request token — accepted as pre-verified; a handler now cancels the request and the test asserts `CancellationToken.None` for both marker calls.
  - `[low]` `[reject]` the production Works durability path is tested compositionally instead of as one joined delivery fixture — direct Works failure/redelivery tests plus the fresh-client Redis test cover the changed seams without another high-cost host/broker fixture.
  - `[low]` `[reject]` non-acknowledgement is not asserted through an HTTP response — the unchanged endpoint does not catch the verified processor exception, so ASP.NET's existing failure path supplies the non-success response.
  - `[low]` `[reject]` redelivery is simulated by a second processor call rather than a broker — the behavioral contract is at marker reacquisition and is directly covered with malformed retry payload and one handler invocation.
  - `[low]` `[reject]` the live durability test does not include the Works processor — it intentionally proves persistence across fresh DAPR clients, while the Works suite independently proves the processor's state-machine behavior.
  - `[low]` `[reject]` carried: foreign-domain isolation is not repeated through the HTTP subscription route — the processor test still exercises the exact defect before marker access and a second transport fixture remains disproportionate.
  - `[false]` `[reject]` carried: built-in-only completion-pending support was said to diverge from a universal-provider reading — the compatibility-safe reading remains defensible, and documentation now makes the legacy limitation explicit.
  - `[false]` `[reject]` carried: the review-stage parent gitlink had not yet advanced — submodule-first commit and parent pointer finalization occur after review, so the interim dirty marker was not an omitted deliverable.

### 2026-09-05 — Review pass 3 (follow-up)
- verdicts: 37 findings — high 0, medium 4, low 20, false 13, maybe-false 0
- findings:
  - `[medium]` `[patch]` carried: the appended `Dispatched` state serializes as a bare number, so a pre-change consumer reads it as non-`Completed`, returns `Acquired`, and redispatches handlers — carried from pass 1 (`rollout documentation allowed mixed old/new consumers`) and pass 2 (`rollout guidance omitted rollback safety`); the code still relies on the drained-rollout documentation rather than a wire guard, so the logged route and verdict stand and no new action was taken.
  - `[low]` `[reject]` no test pins the persisted JSON shape of `EventStoreDomainEventMarkerRecord` — the change does not alter serialization: the record has no converter and DAPR keeps its default options, so the encoding is unchanged from the two-state form, and the cross-version hazard is already governed by the drained-rollout requirement patched in earlier passes.
  - `[false]` `[reject]` `InMemoryEventStoreDomainEventMarkerStore.TryAcquireAsync` fabricates `(EventStoreDomainEventMarkerAcquisitionResult)(-1)` instead of failing closed like the DAPR store — the claimed unbounded redelivery cannot occur: `_markers` is a private `ConcurrentDictionary` written only by this class with `InProgress`, `Dispatched`, and `Completed`, so the `_` arm is unreachable; there is no external corruption channel as there is for a durable DAPR record.
  - `[low]` `[reject]` `InMemoryEventStoreDomainEventMarkerStore.Transition` retries in an unbounded `while (true)` while `DaprEventStoreDomainEventMarkerStore.TransitionAsync` caps at five attempts — marker states advance monotonically (`InProgress` → `Dispatched` → `Completed`), so every failed `TryUpdate`/`TryAdd` re-reads a strictly advanced state and the loop terminates within a bounded number of iterations; an attempt cap would add a branch and a new failure mode for a livelock monotonicity already prevents.
  - `[low]` `[reject]` the DAPR transition retry has no backoff or jitter — the `for` body returns on the first successful save, so the loop is entered repeatedly only under genuine same-`messageId` contention, which is rare; adding a delay policy introduces new configuration for a bounded five-attempt loop.
  - `[low]` `[reject]` carried: post-handler finalization has no processor-owned deadline — carried from pass 2 (`caller-independent finalization had no additional processor-owned deadline`); `CancellationToken.None` finalization is unchanged and DAPR still supplies transport timeout behavior, so the rejection stands.
  - `[low]` `[patch]` `MarkCompletedSafelyAsync` and `MarkCompletedStrictAsync` emitted the identical `complete-{ExceptionType}` reason code although one acknowledges and one fails the delivery — fixed: the swallowing helper now emits `complete-best-effort-` and the rethrowing helper `complete-strict-` in `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs`.
  - `[low]` `[patch]` the Works completion-pending recovery branch logged the routine `Duplicate` event with envelope fields the redelivery tests deliberately blank — fixed: the branch now emits the new `WorksDomainEventLog.CompletionRecovered` (EventId 4805, Information) carrying only the marker message id.
  - `[low]` `[patch]` the generic processor logged the same recovery at `Debug`, which is off in production where the rollback runbook needs it — fixed: `EventStoreDomainEventProcessor` now logs that branch at `Information`.
  - `[low]` `[reject]` the rollback runbook cannot be executed because nothing reports outstanding `Dispatched` markers — a marker-inventory metric or admin query is substantial new surface for an exceptional, operator-supervised rollback; the drain requirement is documented and the recovery log patched above now makes each completion-only recovery visible.
  - `[false]` `[reject]` `EventStoreDomainEventMarkerState`'s declaration order no longer matches its lifecycle and carries no ordinal comment — no code compares these values relationally: both stores and both processors `switch` on exact members, and each member already carries an XML summary stating its lifecycle meaning.
  - `[false]` `[reject]` a Works domain mismatch is reported as `FailedInvalidPayload` rather than `SkippedNoHandlers` — the intent's I/O matrix requires exactly `FailedInvalidPayload` before marker access for a foreign domain; changing it would violate the contract.
  - `[low]` `[reject]` carried: the live-sidecar test drives the marker store rather than a processor, and rebuilds the marker key by hand — carried from pass 2 (`the live durability test does not include the Works processor`, `the production Works durability path is tested compositionally`); `BuildMarkerKey` is private, and a format change would fail the test loudly at its `ShouldNotBeNull` reads rather than silently.
  - `[false]` `[reject]` the new live test's method-level `[Trait("Category", "Integration")]` conflicts with the project's class-level `LiveSidecar` convention — neither workflow filters on those values (`ci.yml` filters only `--filter-not-trait "Category=HeavyweightContainerPublish"`, `integration.yml` selects by class), and `ReadModelBatchLiveSidecarTests` already carries a second method-level `Category` trait, so no selection changes.
  - `[low]` `[reject]` `AddDaprEventStoreDomainEventMarkerStore` does not validate that the configured component supports ETags and first-write concurrency — DAPR exposes no state-component capability metadata at registration time; the prerequisite is a deployment concern already documented on both reference pages.
  - `[low]` `[patch]` the in-memory default store now holds `Dispatched` in process memory only, and no documentation stated that limitation while a paragraph was added for legacy third-party stores — fixed: `nuget-packages.md` and `stream-replay-api.md` now state that a restart between dispatch and completion loses the in-memory marker and redispatches handlers.
  - `[low]` `[reject]` the reviewed range advances the `FrontComposer`, `Projects`, and `Tenants` gitlinks with no accompanying content — verified that commit `b5bda98` carries them; they record pre-existing local submodule drift in this umbrella workspace rather than a dependency upgrade made by this change, and reverting the pointers would desynchronize the recorded state from the checked-out submodules with no demonstrated defect.
  - `[false]` `[reject]` carried: a durable `InProgress` or unknown state now retries forever with no path to clear it — carried from pass 1 (`mapping persisted InProgress to retryable would wedge a formerly acquirable DAPR state`); the baseline DAPR store never persisted `InProgress`, the intent requires existing `InProgress` to remain retryable, and unknown state fails closed by design.
  - `[low]` `[reject]` a state store returning a record with an empty ETag would have every checked write rejected and leave the marker stuck at `Dispatched` — that requires a component violating the documented ETag prerequisite; the code already converges to a loud `InvalidOperationException` after the final strong read rather than fabricating success, which is the specified fail-closed behavior.
  - `[low]` `[reject]` `ReleaseSafelyAsync` swallows a failure on the post-acquisition invalid-envelope path, so a wedged `InProgress` marker would retry forever — with the deployed DAPR store `TryAcquireAsync` persists no in-progress lease and `ReleaseAsync` is a no-op, so no marker exists to wedge; the proposed fix would also convert a terminal invalid-payload outcome into a retry, which the intent forbids.
  - `[false]` `[reject]` `MarkDispatchedAsync` returning `false` could acknowledge `Processed` with no durable marker — in both built-in stores `false` is returned only when the marker is already `Completed`, and the interface default returns `false` strictly after awaiting `MarkCompletedAsync`; the documented contract makes `false` mean "no completion remains".
  - `[false]` `[reject]` `InMemoryEventStoreDomainEventMarkerStore.ReleaseAsync` no longer clears `Dispatched`/`Completed`, so a reused message id is now reported as a duplicate — that is the intent's explicit "preserve `Dispatched` and `Completed` markers on release" constraint; removing a completed marker on release is the duplicate-side-effect hole this change closes.
  - `[false]` `[reject]` terminal-skip branches still route through the swallowing `MarkCompletedSafelyAsync`, said to violate AC2 — pass 1 amended the spec specifically to limit strict completion to the post-handler and completion-only paths; a skipped delivery has no handler side effects, so a swallowed completion failure only causes the same terminal skip to repeat. A test now pins this (see below).
  - `[false]` `[reject]` carried: mapping a persisted `InProgress` to retryable changes AC3's "existing behavior unchanged" — same carried refutation as above; the baseline DAPR store never wrote `InProgress`.
  - `[medium]` `[patch]` no deterministic test proved the new pre-acquisition domain gate *accepts* production traffic; every envelope in the suite is built in-process with a literal `Domain = "work"`, so a drifted or dropped wire field would silently acknowledge and discard every Works event — accepted as pre-verified from the verification-gap layer; fixed by `Works_processor_accepts_the_submitted_work_domain_through_the_wire_envelope_shape`, which stamps `WorkCommandSubmission.WorkDomain`, round-trips the envelope through the web-cased JSON the endpoint binds, asserts the `"domain":"work"` wire property, and asserts `Processed` with one handler invocation.
  - `[low]` `[reject]` the reviewed range carries unreviewed dependency movement in three unrelated submodules — same evidence and rejection as the gitlink row above.
  - `[low]` `[patch]` the swallowing and rethrowing Works completion helpers are indistinguishable in telemetry — same root cause as the reason-code row above; fixed by the same prefix change.
  - `[low]` `[reject]` carried: "failure escapes to the existing HTTP 500 path" is asserted as `Should.ThrowAsync` rather than through a host — carried from pass 2 (`processor exceptions were not repeated through full generic and Works HTTP hosts`, `non-acknowledgement is not asserted through an HTTP response`); the endpoint delegate still awaits the processor without catching.
  - `[false]` `[reject]` a failed `Dispatched` write leaves no marker at all, so redelivery reacquires and redispatches handlers — the intent's matrix specifies that a persistence rejection or failure "remains retryable", and the Design Notes deliberately exclude a pre-handler lease to avoid stranding an unbounded `InProgress` marker; no completion-pending marker exists on that path, so neither `Never` clause is violated.
  - `[low]` `[reject]` relocating envelope validation behind acquisition lets a blank-metadata envelope acquire and release a marker where it previously touched no store — same evidence as the `ReleaseSafelyAsync` row: the deployed DAPR store persists nothing on acquisition and releases as a no-op; pass 1 mandated the relocation so completion-only recovery does not depend on decodable metadata.
  - `[false]` `[reject]` the `InProgress` matrix row describes a state the deployed DAPR store cannot produce — the intent explicitly requires existing `InProgress` to remain retryable, and the mapping is reachable through the in-memory store and any third-party store that does take a lease; unreachability under one store is not a defect.
  - `[false]` `[reject]` fail-closed for unknown state is implemented in two shapes (DAPR throws, in-memory returns `-1`) and tested with a third — same refutation as the in-memory `-1` row: the in-memory `_` arm is unreachable because the dictionary is private and only this class writes it.
  - `[low]` `[patch]` no test covered a completion failure on a terminal-skip path, leaving the deliberate best-effort behavior unpinned across three review passes — fixed by `Works_processor_terminal_skip_keeps_its_outcome_when_completion_persistence_fails`, which asserts the skip still returns `FailedInvalidPayload`, releases nothing, and invokes no handler when every completion write throws.
  - `[low]` `[reject]` carried: the central completion-only scenario is proven against fakes rather than a processor over the DAPR store — carried from pass 2 (`the production Works durability path is tested compositionally instead of as one joined delivery fixture`).
  - `[medium]` `[patch]` carried: the coordinated-rollout safety property exists only as documentation prose with nothing in code enforcing the sequencing — same carried row as the numeric-enum finding above.
  - `[medium]` `[patch]` the domain gate is asserted against a field the DTO documents as optional (`Domain` is a nullable init property), and absence is terminal — same root cause as the accept-side gap above and fixed by the same test, which proves the wire property is present and bound.
  - `[false]` `[reject]` the working tree carries deferred-work ledger status flips that were filtered out of the reviewed diff — the ledger is owned by the orchestrator and explicitly excluded from this run's writable surface; no ledger edit is part of this change.

## Design Notes

Use explicit values `Acquired=0`, `Completed=1`, `InProgress=2`, `CompletionPending=3` and `InProgress=0`, `Completed=1`, `Dispatched=2`. Add `Task<bool> MarkDispatchedAsync`: the default awaits legacy `MarkCompletedAsync` once and returns `false` (already terminal), while built-in stores persist `Dispatched` and return `true` (completion remains). Immediately after handlers succeed, disable release, invoke that method, and call strict `MarkCompletedAsync` only when it returns `true`. A failed post-handler marker write must be logged and escape; terminal-skip branches retain their existing best-effort completion helper.

Before marker acquisition, validate only a syntactically valid message ID; Works must additionally require `string.Equals(envelope.Domain, WorkCommandSubmission.WorkDomain, StringComparison.Ordinal)` before any marker call. After `Acquired`, perform the remaining existing envelope validation; on failure release the in-progress marker safely and return the existing invalid-payload result. Handle `CompletionPending` immediately after acquisition, using `CancellationToken.None` for strict completion and returning `Duplicate` only after it succeeds, so payload and unrelated metadata are never decoded on this path.

DAPR acquisition remains lease-free to avoid stranding an unbounded `InProgress` marker after a process crash, and release remains a no-op. Every acquisition and transition read uses `ConsistencyMode.Strong`. Post-dispatch transitions read record+ETag, save with `FirstWrite`, verify the returned boolean, retry bounded conflicts with a fresh strong read, accept already-target/`Completed`, and never regress `Completed`; exhaustion errors identify message ID and target state. The state component must support ETags and first-write concurrency. Deployment is two phase: drain or stop all old consumers before any consumer can write or encounter `Dispatched`, then deploy the compatible new cohort; do not run old and new consumers concurrently.

Tests must cover explicit enum ordinals; in-memory release preserving `Dispatched`/`Completed`; strong-consistency DAPR acquisition and transition calls; absent-to-`Completed`; save rejection followed by an already-advanced reread; persistent completion failure from `CompletionPending` in each processor; successful dispatch followed by completion failure and malformed-payload redelivery with exactly one handler invocation; legacy default completion called once; post-handler dispatch-marker failure escaping without release and with a structured log; and exact Works domain rejection before marker access. Add a live-sidecar test that creates a uniquely keyed DAPR marker store against `statestore`, persists `Dispatched`, observes `CompletionPending` from a fresh store/client view, completes it, observes `Completed`, and cleans up only that unique key.

## Verification

**Commands:**
- `dotnet build tests/Hexalith.EventStore.Client.Tests/Hexalith.EventStore.Client.Tests.csproj --configuration Debug -p:UseHexalithProjectReferences=true` (from `references/Hexalith.EventStore`) -- expected: clean build.
- `dotnet tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests.dll -class Hexalith.EventStore.Client.Tests.Subscriptions.EventStoreDomainEventMarkerStoreTests -class Hexalith.EventStore.Client.Tests.Subscriptions.EventStoreDomainEventProcessorTests` (from `references/Hexalith.EventStore`) -- expected: focused protocol tests pass.
- `dotnet build tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Hexalith.EventStore.Server.LiveSidecar.Tests.csproj --configuration Debug -p:UseHexalithProjectReferences=true` (from `references/Hexalith.EventStore`) -- expected: live-sidecar project builds.
- `dotnet tests/Hexalith.EventStore.Server.LiveSidecar.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.LiveSidecar.Tests.dll -class Hexalith.EventStore.Server.LiveSidecar.Tests.Integration.DomainEventMarkerLiveSidecarTests` (from `references/Hexalith.EventStore`) -- expected: Redis-backed marker protocol passes when the documented DAPR prerequisites are available.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -m:1 -p:MinVerVersionOverride=1.0.0` -- expected: clean cross-repository build.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.WorksDomainEventProcessorTests` -- expected: focused Works tests pass.

## Auto Run Result

Status: blocked
Blocking condition: patch verification failed — the Works half of the `## Verification` section cannot be executed because `src/Hexalith.Works.Projections` does not compile at `HEAD`, for reasons that predate this story's baseline.

### Follow-up review pass (2026-09-05, pass 3)

This run re-reviewed the completed change with four independent layers (blind, edge-case, verification-gap, intent-alignment) over the combined superproject + `Hexalith.EventStore` diff since `547c6d896d256ba9630f713054ea2b073262f0ef`.

**Summary of change under review (unchanged from the original run):** a durable post-dispatch marker phase (`Dispatched` / `CompletionPending`) added to the EventStore marker protocol and consumed by both the generic and Works processors, plus an exact-ordinal `work` domain guard ahead of any Works marker operation.

**Patches applied in this pass:**
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventLog.cs` — added `CompletionRecovered` (EventId 4805, Information) so a completion-only recovery is distinguishable from routine dedup.
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventProcessor.cs` — the completion-pending branch now emits `CompletionRecovered` with the marker message id instead of the routine `Duplicate` event (whose envelope fields are legitimately blank on that path); the swallowing and rethrowing completion helpers now emit distinct `complete-best-effort-` / `complete-strict-` reason codes.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/EventStoreDomainEventProcessor.cs` — the same recovery branch now logs at `Information` rather than `Debug`.
- `references/Hexalith.EventStore/docs/reference/{nuget-packages,stream-replay-api}.md` — state that the default in-memory marker store holds `Dispatched` in process memory only, so a restart between dispatch and completion redispatches handlers.
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventProcessorTests.cs` — added `Works_processor_accepts_the_submitted_work_domain_through_the_wire_envelope_shape` (proves the new domain gate accepts an envelope stamped with `WorkCommandSubmission.WorkDomain` after a round trip through the web-cased JSON the endpoint binds) and `Works_processor_terminal_skip_keeps_its_outcome_when_completion_persistence_fails` (pins the deliberate best-effort completion on terminal-skip paths), plus the `CompletionAlwaysFailingMarkerStore` fixture they use.

**Review findings breakdown:** 37 findings — 0 high, 4 medium, 20 low, 13 false. Patched entries this pass: 1 medium (the domain-gate accept-side proof, grouped with the optional-DTO-field finding) and 4 low (reason-code ambiguity, recovery-log observability in both processors, in-memory durability documentation, terminal-skip completion-failure coverage). 0 items deferred. Every rejected finding and its recorded reason is in the `## Review Triage Log` entry for pass 3 above; 8 rows were carried unchanged from passes 1 and 2.

**Follow-up review recommendation:** `false`. On a follow-up pass only a patched `high` warrants another round, and this pass patched none.

**Verification performed:**
- `dotnet build tests/Hexalith.EventStore.Client.Tests/Hexalith.EventStore.Client.Tests.csproj --configuration Debug -p:UseHexalithProjectReferences=true` — succeeded, 0 warnings, 0 errors.
- `dotnet tests/Hexalith.EventStore.Client.Tests/bin/Debug/net10.0/Hexalith.EventStore.Client.Tests.dll -class ...EventStoreDomainEventMarkerStoreTests -class ...EventStoreDomainEventProcessorTests` — 43 tests, 0 failed, 0 skipped.
- `dotnet build tests/Hexalith.EventStore.Server.LiveSidecar.Tests/Hexalith.EventStore.Server.LiveSidecar.Tests.csproj --configuration Debug -p:UseHexalithProjectReferences=true` — succeeded.
- `dotnet tests/Hexalith.EventStore.Server.LiveSidecar.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.LiveSidecar.Tests.dll -class ...DomainEventMarkerLiveSidecarTests` — 1 test, 0 failed (the Redis-backed `Dispatched` → `Completed` path ran live).
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release -m:1 -p:MinVerVersionOverride=1.0.0` — **FAILED**: 21 × `CS0122` in `src/Hexalith.Works.Projections/Strategies/WorkItemRollUpProjection.cs`. `RollUpNode.Key`, `RollUpNode.ChildKeys`, and `RollUpNode.ParentKey` are declared `private` inside the nested `RollUpNode` class but are read by the enclosing `WorkItemRollUpProjection`, which C# does not permit.
- `dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class ...WorksDomainEventProcessorTests` — **not run** (no test assembly could be produced).

**Blocker detail.** The failure is not caused by this change or by this pass's patches. `git stash` of the three modified working-tree files reproduces the identical 21 errors at pristine `HEAD`, and `git log` attributes the file's last change to `df46f71` ("Refactor WorkItem Roll-Up Projection and Tenant Isolation", 2026-09-01), which is an ancestor of this story's baseline `547c6d89`. `src/Hexalith.Works/Hexalith.Works.csproj` has a `ProjectReference` to `Hexalith.Works.Projections`, so neither `Hexalith.Works` nor any Works test project has been buildable since that commit — which also means this spec's recorded "clean cross-repository build" verification could not have passed while the spec was moved to `done`.

The mechanical repair is to widen those three `RollUpNode` members from `private` to `public` (matching every other member of that internal nested class), but that is another story's code and outside this change's scope, so the decision is handed back rather than taken here.

**Residual risks:**
- The five patches above compile only as far as the EventStore submodule proves; the two new Works tests and the two Works source edits are unbuilt and unrun until the `Hexalith.Works.Projections` break is resolved.
- The working tree is intentionally left dirty with those patches (the `bad_spec`/`intent_gap` revert rules do not apply to a patch pass), and nothing was committed.
- The coordinated-rollout safety property (drain old consumers before `Dispatched` can exist; drain `Dispatched` to `Completed` before rollback) remains documentation-only, as carried from passes 1 and 2.
