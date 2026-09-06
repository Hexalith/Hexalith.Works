# Epic 4 Context: Shared Work Execution and Builder Runtime Validation

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Enable teams, agents, and external parties to share one executor model for assigning, reassigning, claiming, and handing off tenant-scoped work, while proving the complete Works command/event lifecycle, projections, reminders, and reactor recovery in a platform-owned Aspire topology. This keeps Works a focused domain module and gives builders runtime evidence without Works shipping duplicated platform infrastructure.

## Stories

- Story 4.1: Bind Work to a Uniform Party Executor
- Story 4.2: Assign, Reassign, and Hand Off Work
- Story 4.3: Claim Queued Work with Single-Claim-Wins
- Story 4.4: Resolve the Tenant's What's Next Queue
- Story 4.5: Prove the Command/Event Pipeline in the Platform Topology
- Story 4.6: Prove Reminder and Reactor Recovery
- Story 4.7: Trigger Reactor Translators from the Live Event Stream
- Story 4.8: Register and Reconcile Date Reminders Durably
- Story 4.9: Migrate Works Hosting to the Platform Boundary

## Requirements & Constraints

All executor types use one `ExecutorBinding` carrying `PartyId`, `Channel`, and `AuthorityLevel`; assign, reassign, handoff, and claim behavior must not branch on executor kind. Authority is preserved in commands, events, state, and read models but is carried rather than enforced in v1. Any executor in the tenant may claim queued work; routing scores, eligibility rules, escalation, and production channel adapters remain deferred.

Queued work supports pull-based claiming alongside direct assignment and requeueing. Concurrent claims must yield exactly one accepted owner, with the loser re-handled against fresh state and domain-rejected. The tenant's "what's next" read model includes eligible `Queued` and `Assigned` items, orders them by Priority, earliest Due Date, then creation order, places items with neither Priority nor Due Date last, and updates incrementally through the platform notifier seam.

Tenant isolation applies to commands, identities, state and projection keys, queries, result filtering, and logs. Query-side authorization is required independently of tenant-prefixed keys. Logs must be structured and exclude event payloads, personal data, secrets, tokens, and full commands; failures expose correlation and tenant context through RFC 9457 Problem Details.

Runtime evidence must cover the command/event pipeline and its failure-sensitive boundaries: persist before publish; create, progress, child spawn, suspend, child/date resume, completion, cascade, claim conflict, queries, ordinary projections, and shared rebuild. Date reminders and reactor processing must converge after restart, duplicate delivery, and interrupted cascade execution. Tier-1 domain tests remain pure and require no Dapr, network, browser, containers, or Aspire; topology tests are reserved for real platform boundaries.

Works remains headless in v1. It ships no UI, MCP, chatbot, email, routing, cost, security adapter, AppHost, Aspire, ServiceDefaults, or duplicate health, telemetry, Dapr, projection/query, delivery, scheduling, and subscription plumbing.

## Technical Decisions

`Hexalith.Works` is a minimal EventStore domain-service executable using `AddEventStoreDomainService(...)` and `UseEventStoreDomainService()`. The designated platform repository owns the Aspire AppHost, ServiceDefaults, Dapr components, and generic runtime composition. Domain-specific handlers use documented EventStore interfaces; missing reusable runtime behavior belongs in EventStore or the platform, not in Works.

Claims are single-aggregate operations serialized by the EventStore single-writer actor and Dapr state-store ETag concurrency. Commands expose no expected version or ETag. A conflicting save is retried from rehydrated state and follows the existing transition-rejection path; retry exhaustion is an infrastructure `ConcurrencyConflict` with no losing append or publication. The claimable pool is a read projection, not an authoritative queue aggregate.

Event delivery is at-least-once and unordered. Reactor translators stay outside the kernel and perform only pure, mechanical event-to-command translation; domain decisions always round-trip through aggregate `Handle`. Target commands are idempotent, cascade progress is durably checkpointed, and recovery discovers unfinished work from re-readable projections rather than in-memory loops.

Date resumes use self-targeted durable Dapr actor reminders with names deterministically derived from the work item and await-condition key. Recovery reconciles a tenant-scoped pending-date-await index plus per-aggregate streams, reissues overdue resumes idempotently, and re-registers future reminders. The aggregate and reactor never read a clock; deadlines remain advisory until the adapter fires.

Shared projection rebuilds keep readers on the prior committed generation while normal delivery is quiesced or fenced from inventory capture through atomic commit, then resume delivery and catch up. Architecture fitness checks enforce the platform boundary, dependency direction, clock-free kernel, and absence of executor-kind branching.

## Cross-Story Dependencies

The uniform binding contract underpins assignment, claim, and queue read models; assignment and requeue transitions supply the eligibility changes consumed by claiming and "what's next." Runtime pipeline proof depends on the lifecycle, work-tree, await-condition, and pure reactor behavior established by earlier epics. Reminder/recovery proof, live event-stream triggering, and durable reminder reconciliation must all be preserved by the hosting migration. Before removing any Works-owned hosting project, the Solution Architect must name the platform host and owner, and the replacement topology must pass equivalent or stronger runtime evidence; the prior composition remains the rollback path until acceptance.
