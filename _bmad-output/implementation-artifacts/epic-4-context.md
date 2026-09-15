# Epic 4 Context: Assign, Discover, and Operate Work Reliably

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Executors and operators can bind, claim, query, dispatch, recover, and observe work through tenant-safe runtime paths with deterministic outcomes. The epic proves that assignment, queue discovery, live Reactor effects, and date reminders work across failures while keeping domain decisions pure and preserving the boundary between Works-owned domain behavior and platform-owned runtime infrastructure.

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

## Requirements & Constraints

- Represent every system, internal, or external executor with one `ExecutorBinding(PartyId, Channel, AuthorityLevel)`. Executor kinds differ only in field values; do not introduce kind-specific contracts or behavior. Preserve additive-tolerant AuthorityLevel values in events, state, and read models, but do not use them as a v1 authorization role system.
- Assignment and reassignment set the single current binding. Active work uses Handoff to change only that binding while preserving status, effort, schedule, and await conditions. Requeue returns work to the shared pool. Responsibility-bound acts must use the current binding, and terminal work must not mutate.
- Claim is the common transition into `InProgress`. A queued tenant member claims as themself; an assigned item is claimable only by its bound executor. Same-item serialization and bounded conflict retry must yield exactly one winner; an ordinary loser is re-handled against fresh state and receives a domain rejection. Retry exhaustion is an infrastructure conflict with no loser append or publication.
- Provide one tenant-scoped what's-next query with authorized executor and coordinator views. An executor sees their assigned items plus the tenant's queued pool; the all-work view is restricted to trusted internal origin. Order by present Priority (`Critical`, `High`, `Normal`, `Low`), earliest present Due Date, then ordinal WorkItemId; missing schedule values sort last. Do not add routing scores or creation-order coordinates.
- Treat delivery as at least once and potentially reordered. Persist events before publication, acknowledge only after durable state/effect/checkpoint or quarantine capture, and make projection, Reactor, reminder, and recovery processing idempotent. Reactor translations are mechanical; all business decisions return through aggregate `Handle`.
- Date-based suspension must register a deterministic, self-targeted durable reminder. Duplicate registration and repeated callbacks remain safe. Recovery scans a tenant-scoped pending-date-await index for discovery, verifies each item from its aggregate stream, reissues overdue resumes, and re-registers future awaits. Never use a tenant-wide null-aggregate stream read. Aggregate handling and Reactor translation remain clock-free.
- Keep all identities, keys, indexes, checkpoints, queries, and log context tenant-scoped. Authenticate actor and tenant outside payload data; deny unauthorized commands and queries before dispatch or disclosure. Logs and error responses must exclude event bodies, Raw Acts, personal or tenant-confidential data, secrets, tokens, and stack traces.
- Keep pure aggregate, projection-fold, and Reactor-translation tests free of Dapr, network, browser, containers, and Aspire. Runtime tests must assert persisted end state and cover persist-before-publish, projection convergence, claim conflicts, live cascade and child-completion resume, date resume, lost-firing recovery, checkpoint replay, and restart convergence.

## Technical Decisions

- Works is event sourced through Hexalith.EventStore: pure `Handle(state, command)` returns success events, a domain rejection, or a narrow no-op; `Apply` changes only in-memory state. EventStore owns envelope provenance, one-writer concurrency, durable publication ordering, and infrastructure outcome handling.
- A claimable pool is a read projection, not an authoritative queue aggregate. What's-next updates incrementally and is rebuildable; query authorization and result filtering supplement tenant keying.
- The Works Reactor is the sole cross-aggregate process manager. Cascade, child-completion resume, date resume, and recovery use deterministic command/effect identities and durable checkpoints so redelivery or restart cannot create another logical effect.
- Reminder streams are authoritative and pending indexes are discovery aids. The target design uses typed DateResume and Expiry intents carrying canonical target, UTC due instant, schedule token, source position, and typed payload. Works owns intent translation, EventStore owns generic registration and reconciliation, and Platform owns scheduler persistence, availability, backup, callback policy, and operational health.
- `Hexalith.Platform` is the target Aspire host. Works retains its contracts, server, projections, Reactor, testing support, and minimal domain-service executable using the canonical EventStore SDK composition. Do not duplicate service defaults, health, telemetry, Dapr wiring, subscriptions, generic projection/query actors, reminders, or recovery machinery in Works.
- Hosting removal is parity-gated. Transitional Works AppHost and ServiceDefaults remain until the platform topology proves equivalent or stronger behavior for topology, delivery, projections/rebuild, queries, reminders, process recovery, security, and command submission, with rollback evidence.

## UX & Interaction Patterns

The delivered version is headless. Builder-facing evidence should distinguish accepted, domain-rejected, authorization-denied, idempotent, and infrastructure-unknown outcomes, show current authoritative evidence and a safe next action, and never imply that acknowledgement means projection convergence. Future queue surfaces must preserve deterministic ordering, refresh safely after a lost claim, expose executor data through one Party treatment without executor-kind branching, and represent freshness or unavailable projections explicitly rather than as empty or zero data.

## Cross-Story Dependencies

Uniform binding and assignment semantics (4.1–4.2) underpin claim and queue discovery (4.3–4.4). The runtime proof (4.5) establishes the baseline expanded by recovery, live event-stream dispatch, and durable reminder registration (4.6–4.8); these rely on lifecycle, await-condition, tree, cascade, and child-resume behavior from earlier epics. Story 4.8 preserves its own reminder-runtime evidence and deferred follow-up, while platform-host migration remains exclusively owned by 4.9. Story 4.9 must reproduce the earlier pipeline, reminder, Reactor, restart, and rebuild guarantees before any Works-owned host is removed.
