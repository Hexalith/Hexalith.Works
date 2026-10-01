# Epic 4 Context: Assign, Discover, and Operate Work Reliably

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Tenant participants can bind or claim work uniformly, discover the next eligible work, and run the command, projection, reminder, and Reactor pipeline with deterministic outcomes on the shared platform host. System, internal, and external parties share one binding. The delivered kernel ships no production channel, routing, or end-user surface, and the transitional Works host stays until platform parity and rollback are proved.

## Stories

- Story 4.1: Bind Work to a Uniform Party Executor
- Story 4.2: Assign, Reassign, and Hand Off Work
- Story 4.3: Claim Queued Work with Single-Claim-Wins
- Story 4.4: Resolve the Tenant's What's Next Queue
- Story 4.5: Prove the Command/Event Pipeline Under Aspire
- Story 4.6: Prove Reminder and Reactor Recovery
- Story 4.7: Trigger Reactor Translators from the Live Event Stream
- Story 4.8: Register and Reconcile Date Reminders Durably
- Story 4.9: Migrate Works Hosting to the Platform Boundary
- Story 4.10: Publish EventStore Projection Delivery and Rebuild Fence
- Story 4.11: Publish EventStore Typed Reminder Reconciliation
- Story 4.12: Publish EventStore Checkpointed Process and Recovery Runtime
- Story 4.13: Publish EventStore Trusted Effect Submission
- Story 4.14: Adopt SDK Projection and Query Seams in Works
- Story 4.15: Adopt SDK Reminder, Process, and Command Seams in Works
- Story 4.16: Prove Platform Works Parity and Rollback

## Requirements & Constraints

- One Executor Binding per item: PartyId, Channel, and AuthorityLevel. Do not branch on executor kind. A new Channel or AuthorityLevel needs no new event type and no Server or Projections branch. Equivalent journeys differ only in binding field values. AuthorityLevel is stored and replayed and does not authorize behavior. The additive set is Read, Contribute, Coordinate, Administer. The executor-router port stays unwired. Approvers and observers are not a second binding.
- Create may record an intended binding and still leaves the item Created. Assigned begins only through Assign; reassign uses that same path. Handoff, while InProgress or Suspended, changes only the binding and preserves Status, Burn-Down, Schedule, and Await-Conditions.
- Claim is the only entry to InProgress. Requeue returns Assigned work to Queued. A queued claim is an authenticated tenant member acting only for themselves. Assigned claim, progress, correction, completion, suspend, reject, and handoff require the trusted acting Party to match the current binding. Assign, queue, re-estimate, reschedule, cancel, root create, and conversation link stay at the authenticated tenant-member floor.
- The acting party comes from platform-authenticated identity, never from a caller field or the binding. Reminder and Reactor acts use a named workload, tenant delegation, and causation. Authorization denial happens before handling or append and is not a domain rejection. Query authorization and result filtering are separate from tenant key prefixing.
- What's-next is a projection, not a router. The executor view requires the requested PartyId to match the caller and returns that party's Assigned items plus the tenant Queued pool. The all-work view is trusted-internal only. Order by Priority (Critical, High, Normal, Low; absent last), then earliest Due Date (absent last), then WorkItemId ordinal. Expose Status, own and rolled Remaining, binding fields, and Await-Condition data without UI types. Publish only the changed key.
- Commands expose no expected version or ETag. One claim wins; the loser gets the existing not-claimable domain rejection. Retry exhaustion is an infrastructure concurrency conflict with no loser append or publication.
- Delivery may duplicate or reorder. Acknowledge only after durable state, checkpoint, side effect, or quarantine commits. Resume matches an exact current Await-Condition. Handling and the Reactor stay clock-free. Reminder names are deterministic and idempotent. Recovery must not skip a source envelope, must rediscover pending date awaits without a tenant-wide null-aggregate read, and must leave stranded work readiness-degraded. Readers stay on the last committed rebuild generation until commit. One logical cross-aggregate effect runs at most once.
- Tier-1 tests stay free of Dapr, network, browser, and containers. No production web, MCP, CLI, chatbot, email, LLM, cost, or routing adapter ships. Logs and problem details carry correlation and tenant context only. Works stores PartyId only. Generic hosting, delivery, reminders, checkpoints, and submission belong in EventStore or Platform before Works copies them.

## Technical Decisions

- Server, Projections, and Reactor depend on Contracts. Only the executable composes the EventStore domain-service SDK. Projections own what's-next. The Reactor is the sole mechanical cross-aggregate translator.
- Claim conflicts are EventStore-owned: actor-turn serialization, then bounded rehydrate-and-rehandle. Tenant keys are tenant-closed. A canonical tenant id is a lowercase ASCII slug; other input is rejected.
- Works supplies reminder and Reactor intents. EventStore owns registration, reconciliation, checkpointed process running, and deterministic effect identity with target-partition receipts. Identical replay returns the prior outcome; conflicting replay quarantines. Platform owns Aspire topology, service defaults, mutual TLS, access control, scheduler persistence, and the production fail-closed profile. The selected Dapr hosting integration stays development or test only unless Platform records a time-bounded exception.
- Target composition uses the canonical EventStore domain-service registration. The current Works AppHost and ServiceDefaults remain until every host-migration seam has a published producer, a Works consumer, persisted parity, and a proved rollback. Version pins follow the current owning files. Expected invalid acts are domain rejection events; infrastructure failures are exceptions; contracts grow by additive fields only.

## UX & Interaction Patterns

- The delivered experience is headless. Group accepted, domain-rejected, authorization-denied, and infrastructure-unknown outcomes with current domain evidence and a next safe action, readable as text.
- Present Party, Channel, and AuthorityLevel as data, without inferring party kind or treating AuthorityLevel as permission. Assignment does not start work. A claim loss names the other party and points to the next item. Do not present Handoff as Assign or requeue. A live notification is a changed key. Web, email, chatbot, and MCP surfaces are later horizons.

## Cross-Story Dependencies

- This epic operates the kernel, lifecycle, and saga from Epics 1–3. Live cascade and child-completion resume consume Epic 3's pure command intents; this epic owns at-least-once dispatch, checkpoints, reminders, and restart proof.
- Stories 4.5–4.8 record historical Works-host evidence and that record stays valid. Stories 4.10–4.13 publish the EventStore seams; 4.14 and 4.15 adopt them while the old host remains for rollback; 4.16 proves platform parity and rollback. Story 4.9 cannot remove the Works host until 4.10–4.16 are accepted and the replacement is at least as strong.
- Forward candidates labeled F4 are non-executable and are not part of this story list.
