# Subscriber dead-letter operator guide

## Scope and queue distinction

`deadletter.work.events` retains Dapr subscriber deliveries that exhausted the Works `/work/events` retry policy.
These are structured CloudEvents captured by the separate `eventstore-operations` workload. They are not EventStore
command dead letters: command dead letters describe failures inside command processing, are published by the
EventStore command pipeline to `commanddeadletter.work.events`, and remain owned by it. Never replay one queue
through the other workflow.

The two topic names are one edit apart. EventStore derives its command dead-letter topic as
`{EventStore__Publisher__DeadLetterTopicPrefix}.{domain topic}`, and this composition overrides the work domain's
topic to `work.events`; with the shipped default prefix (`deadletter`) that derivation lands on
`deadletter.work.events` -- the subscriber queue. The Platform-owned Works topology therefore sets the prefix to `commanddeadletter`
and grants the `eventstore` app id publish on exactly that topic. If you change either the prefix or the domain
topic override, change the publish scope in Platform’s `DaprComponents/pubsub.yaml` with it: a mismatch either merges the two queues or
makes the scoped component reject every command dead letter, and `DeadLetterPublisher` only logs that failure.

Works has no component or subscription access to `deadletter.work.events`, and the Works application never
publishes to it. The Works *sidecar* holds a publish grant for exactly that topic, because Dapr's poison-message
forwarding is performed by the subscribing sidecar under its own app id; without that grant the queue would never
be fed. Only `eventstore-operations` subscribes to the topic, and that workload has an explicit publish denial. Authorized operators continue to use the JWT-protected
Admin.Server dead-letter list, count, retry, skip, and archive endpoints; Admin.Server invokes the operations workload
through its caller-scoped Dapr route. The global dead-letter count is not tenant-scoped and therefore requires the
Admin policy; read-only operators see tenant-scoped lists instead of the global count tile.

The operations application port is an internal app channel, not an ingress surface. Keep it private to the sidecar.
Production deployments must configure the same secret as Dapr `APP_API_TOKEN` and the application's
`APP_API_TOKEN`; Dapr supplies it to the app as `dapr-api-token`. The workload refuses to start outside Development
without that secret. The platform health endpoints (`/alive`, `/health`, `/ready`) stay outside that token boundary
so the orchestrator and the Dapr sidecar app health check can still probe the workload; they expose no retained
item, identity, or payload. Every other path on the app channel requires the token, and the secret must never be logged, copied into tickets, or exposed through diagnostics.

## Capture limits and non-retryable deliveries

`deadletter.work.events` is the last queue in the chain: it has no dead-letter destination of its own. A delivery
the capture endpoint does not acknowledge is redelivered under the sidecar's inbound pub/sub retry policy
(`pubsubRetryInbound` in `Hexalith.Platform/DaprComponents/resiliency/resiliency.yaml`, exponential,
`maxRetries: 10`) and is then **dropped with nothing retained**. The retry budget is finite, so refusing a
delivery only postpones the same loss unless the refusal is one a retry can actually clear.

Capture therefore acknowledges the conditions redelivery can never change and records them on the bounded capture
metric instead:

| Condition | `status` dimension | Retained? | Acknowledged? |
|-----------|--------------------|-----------|---------------|
| Body larger than `EventStoreOperations__MaxBodyBytes` (default 1 MiB) | `oversize` | No | Yes |
| Empty body | `empty-body` | No | Yes |
| Same message id already retained with different bytes | `hash-conflict` | The first body only | Yes |
| Identity, topic, size, or hash outside the retained bounds | `unretainable` | No | Yes |
| State-store or actor fault | `capture-failed` | No | No -- redelivered until the budget runs out |

Only failures a retry can actually resolve -- a state-store fault, for example -- return a non-2xx and keep the
delivery queued. A non-zero `oversize`, `empty-body`, or `unretainable` count means events are being dropped:
treat it as an incident, raise `MaxBodyBytes` only after confirming the publisher's envelope size is legitimate,
and note that the limit must stay at or below the broker's own message size.

`capture-failed` is the most urgent of the five. The workload emits no log output by design, so this counter is
the only signal that captures are failing; a fault that outlasts the inbound retry budget loses the dead letter
permanently and silently. Treat any `capture-failed` as an active loss condition, not a warning.

## Alerts and retention

Alert when the open backlog count is non-zero for 15 minutes, when the oldest open item exceeds 30 minutes, or when
any capture hash conflict, oversize, empty, unretainable, or failed capture, exhausted replay, or replay failure is
observed. Page immediately on `capture-failed`. Page the owning service team when the oldest item exceeds
two hours or the backlog grows continuously for 30 minutes. Metrics use only bounded `topic`, `status`, and `reason`
dimensions; message, tenant, aggregate, and correlation identifiers must never become metric dimensions.

The backlog gauges are refreshed from the drain actor, which the workload activates once shortly after start so a
host that restarts holding an untouched backlog still reports it. If the gauges read zero while the Admin.Server
list shows retained items, the actor is unreachable -- check the sidecar and the actor placement service rather
than trusting the gauge.

Retention is manual today. Archiving flips an item's state; it does not remove the record or its index entry, and
**no purge or compaction operation exists on the operations workload**. Retained bodies of archived entries stay
in the actor state store indefinitely and the per-drain scan cost grows with everything ever captured. Review the
backlog weekly, keep pending and replay-requested items for 30 days and replayed or archived records for seven
days as a policy target, and record any deletion as a deliberate privileged state-store operation -- there is no
job that will do it for you. Operator list pages are bounded by `EventStoreOperations__MaxListItems` (default
500); a larger requested page size is clamped, not refused.

## Triage, fix, retry, verify, archive

1. Triage the Admin.Server list using safe identity, capture time, replay eligibility, state, and bounded reason code.
   Do not retrieve or copy the raw body through an operator API; no such API exists by design.
2. Fix the subscriber or configuration defect. For malformed or unidentified entries, stop here: they are
   replay-ineligible and must never be blindly republished.
3. Retry only the selected tenant-scoped entries through the authorized Admin.Server action. The actor durably writes
   `ReplayRequested` before delivery and sends the original structured CloudEvent directly to Works through Dapr
   service invocation. An action is all-or-nothing and tenant-scoped: if any requested id is unknown, already
   terminal, or belongs to another tenant, the whole batch is refused as not-found and nothing is mutated. Keep a
   batch within `EventStoreOperations__MaxActionItems` (default 100) entries.
4. Verify the entry disappears from the open list, the open count falls, the replay-success metric increments, Works
   reports its normal terminal processing outcome, and no new retryable reason appears. Terminal entries are excluded
   from the open-list response; there is no terminal-history API. A duplicate delivery is safe because the Works
   marker store deduplicates the EventStore message id.
5. Archive repaired, replayed, or permanently invalid entries when the incident record no longer needs them. `skip`
   is an explicit archive decision, not deletion.

## Restart and failure recovery

Capture returns success only after the actor atomically commits both the raw item and its topic index. A persistence
failure returns non-2xx so Dapr retains delivery. Replay saves `ReplayRequested`, then `Replaying`, and only marks
`Replayed` after Works acknowledges. A crash in the delivery window may repeat the request; actor activation and its
durable reminder resume requested or in-flight work after restart, and Works message markers make the duplicate safe.
Failed delivery remains retained in `ReplayRequested` with a bounded reason code, and is retried on every reminder
period until it succeeds or reaches `EventStoreOperations__MaxReplayAttempts` (default 10). On exhaustion the entry
becomes archived with reason `replay-exhausted:<last failure reason>` so a permanently rejected item stops consuming
the drain, and it leaves the open list and count at that point.

The attempt budget is per operator-requested replay, not per item lifetime. An explicit retry resets the counter,
and an entry archived by exhaustion can be retried again once the target is fixed -- it reappears in the open list
when you do. An entry you archived or skipped yourself is not re-openable by retry: that was your decision, and
undoing it means capturing the message again. So a target outage that outlasts the budget costs you a second retry,
not the entry.

Entries whose envelope carries no complete safe identity are filed under the reserved tenant scope `unidentified`
rather than a real tenant. Query that scope to review them. Treat `unidentified` as reserved: do not provision a
real tenant with that id, or its operators would share a scope with unidentified envelopes.

## Works date-reminder recovery warnings

### Current SDK recovery

The SDK reconciles periodically for the lifetime of the host. There is no five-attempt startup budget.
An incomplete dependency scan uses the shorter of `EventStore:Reminders:RetryInitialDelay` and
`ReconciliationInterval`; capacity-only degradation keeps the normal interval. Disabling
`ReconciliationEnabled` emits 200213 and leaves reminder readiness degraded. Repair the dependency and
verify a later complete pass and retained-work progress; a restart is not required to resume periodic recovery.
Observe the `eventstore-reminders-unresolved` readiness entry, including `passCompleted`, `passPending`,
`incompleteScans`, `unresolved`, and `quarantined`. HTTP 200 can still contain Degraded status.

| EventId | Meaning | Operator response |
|---------|---------|-------------------|
| 200210 | A pass completed with explicit candidate, armed, submitted, cancelled, unresolved, quarantined and incomplete counts. | Check these counts and readiness together. A completed pass can still be incomplete or retain unresolved work; it is not an unconditional success or retry promise. |
| 200211 / 200212 | A candidate could not converge, or an indexed/catalog scan was incomplete. Healthy candidates can continue. Provider cancellation while the host token remains active is an isolated failure, with earlier and later completed counts retained. | Correlate the actor digest for 200211, or scan scope for 200212, with the bounded reason and exception type. Restore the named dependency and verify subsequent complete discovery and convergence. Once the host token is cancelled, a cancellation exception can end the pass regardless of the exception's token, before its completion summary; do not infer counts from its absence. |
| 200201 / 200203 / 200219 | Scheduling failed with pending state retained, a witness remains retryable, or submission outcome is uncertain. | Check Scheduler/placement health or gateway authority and availability as applicable. Retained witnesses and deterministic effect identity support retry; verify a durable submission outcome rather than treating an attempted command as completion. |
| 200207 / 200217 | Evidence is quarantined, or its audit disposition could not be written. | Escalate through the protected operator procedure. A repeated log is not evidence that quarantine was repaired; never erase witness or quarantine state as routine remediation. |

These SDK logs carry actor/reminder digests, finite reasons, exception type names and counts. They do not
carry raw tenant/item identifiers, payloads, credentials, provider messages or stack traces. Use authorized
source inventories to correlate a digest; do not expect a parking key in the log. Discovery restarts after
an empty sweep and can find projection membership published later, even without an event wakeup. This
requires retained Works family/control-index membership; it does not prove recovery from a restore that
omits every discovery inventory. See the [SDK configuration and ownership guide](../sdk-reminder-process-adoption.md#current-configuration-after-story-49).

Process recovery reports `eventstore-processes-unresolved`. Inspect `complete`, `unresolved`,
`pendingPublications`, `pendingObservations` and `reasonCounts` together. A full publication queue
retains the unaccepted catalog key and Degraded readiness; already retained candidates can still run.
Repair the publication dependency so a pending identity can finish and free admission for the held key.
Do not remove pending identities to make room. A failed publication that already has a durable intent
can yield to later catalog entries while its own repair remains pending.

`read-worker-exhausted` during candidate discovery retains the precise discovery cursor for a later
pass. It is worker pressure, not proof of a corrupt or missing page. A scan without tail evidence
uses a finite missing-page probe budget; its cycle can become complete with unresolved reasons and
must remain Degraded. The qualification restores captured inventory page bytes through a synthetic
store; it does not qualify an in-place page-repair API. Ordinary catalog republication cannot rebuild
a missing page when candidate and tenant markers survive. Escalate to the data recovery owner for
consistent authoritative inventory restoration, then verify the next cycle starts at the head,
reaches the recovered source and clears unresolved coverage. The [complete-fleet rollback procedure](../../references/Hexalith.Platform/docs/works-host-parity.md#complete-fleet-rollback)
qualifies the disposable synthetic profile; production restore still requires its AD-28 owner decision.
These deterministic SDK consumer guarantees do not establish production throughput, custom serializer
compatibility or recovery when every discovery inventory is absent.

### Historical host and rollback investigations

The warning IDs below describe the retained historical host only. Since Story 4.9, use the active [Platform recovery runbook](../../references/Hexalith.Platform/docs/works-host-parity.md#readiness-telemetry-and-privileged-operations). Inspect Works `/ready` and its Healthy/Degraded/Unhealthy status rather than interpreting HTTP 200 as healthy. Correlate the bounded `eventstore.domain.recovery.*` gauges, including unresolved and pending counts, completeness, availability, readiness and `alert`; `alert=1` includes degraded, unavailable or stale recovery. Protected `POST /work/processes/revalidate` requires current source-scoped operator authority and durable attributed audit before mutation. The runbook identifies the synthetic proof's limits and the production provider requirements. The [current SDK settings](../sdk-reminder-process-adoption.md#current-configuration-after-story-49) list ignored legacy recovery controls. Historical warnings remain documented for archived rollback investigations.

Works reminder recovery logs bounded identity/count fields and attaches the original cause through the structured
exception slot. Do not copy exception details into tickets or metrics; use them only in the protected log system.
The following warnings require distinct responses:

| EventId | Meaning | Operator response |
|---------|---------|-------------------|
| 4603 `RecoveryStepFailed` | A recovery operation identified by the structured `Reason` did not complete, and the original cause is attached. `startup-reminder-reconciliation` identifies a bounded startup retry loop; `startup-cascade-recovery` identifies one startup pass; `startup-cascade-replay`, discovery/read, and unregister reasons identify narrower operations and do not alone prove a whole startup attempt failed. | Filter by `Reason`, inspect the protected cause and adjacent subsystem warnings, and check the named state-store, EventStore, Dapr, or actor dependency. For `startup-reminder-reconciliation`, a later 4603 with the same `Reason` proves another failed attempt; absence of one can mean success, host shutdown, or retry-budget exhaustion, so inspect host lifecycle, configured attempt count, and concrete progress. `startup-cascade-recovery` promises no in-process repeat; restart Works after dependency repair if that pass must run again. Verify reminder/cascade/actor progress and that warnings with the same `Reason` stop; there is no universal recovery-success event. |
| 4604 `PendingDateAwaitTenantScanFailed` | The named tenant's pending-date index could not be read, so that tenant contributed no candidates to this pass. | Restore state-store availability and authorization for the tenant index. If the host remains running and the retry budget remains, confirm a later attempt scans the named tenant; shutdown can prevent that later attempt. Restart Works after remediation if shutdown ended the pass or startup retries were exhausted. |
| 4605 `PendingDateAwaitScanIncomplete` | One or more tenant/index, parking, or candidate-stream reads failed. Partial results are attempted after this warning; a later non-cancellation processing failure produces 4603, and scheduling failure also emits 4609. Exact caller cancellation that propagates directly ends quietly without 4603. If cancellation instead arrives at a later tenant boundary after earlier scan evidence, that evidence is preserved through the typed incomplete-result path and 4603 can still follow unless a partial operation propagates the cancellation. The warning includes failed-tenant, failed-candidate, and parked-skip counts. | Check the attached cause and applicable 4604/4606/4608 row, restore the failing state-store or EventStore dependency, and correlate any later 4603 with the same structured `Reason`. Watch for repeated warnings after the configured delay and concrete 4600–4602 recovery activity. If warnings stop, distinguish successful progress, host shutdown, and retry-budget exhaustion using host lifecycle and the configured attempt count before restarting Works. |
| 4606 `PendingDateAwaitCandidateScanFailed` | The named candidate's EventStore stream could not be read or folded, so that candidate was omitted and the scan remains incomplete. | Restore EventStore gateway and stream availability and resolve malformed lifecycle evidence if reported. If the host remains running and the retry budget remains, confirm a later reconciliation includes the named work item; shutdown can prevent that later attempt. Restart Works after remediation if shutdown ended the pass or startup retries were exhausted. |
| 4607 `PendingDateAwaitParkedCandidateSkipped` | Startup recovery deliberately skipped the terminally parked aggregate without a stream read, so it cannot recreate a missing reminder registration or issue an overdue resume. Parking does not cancel an already durable reminder, which may still fire. | Escalate the named tenant/work item to the Works service owner. If its registration is missing, do not expect restart or repeated reconciliation to repair it: no supported unpark/replay path exists. Track the item until a separate remediation capability is delivered or an approved manual incident procedure resolves the underlying projection evidence. |
| 4608 `PendingDateAwaitParkingLookupFailed` | The parking document could not be read or interpreted, so the candidate stream was not touched and the scan remains incomplete. Causes include availability, authorization, malformed data and other non-caller failures. | Use the logged tenant/work-item tuple to locate historical `projection:works:parked:{tenant}:{workItemId}` through authorized state-store inspection; the record key is not emitted. This is the retired host's key, not SDK generation parking state. Inspect the protected cause and repair the specific read or data failure. If the host remains running and the retry budget remains, confirm a later reconciliation reads the candidate; shutdown can prevent that later attempt. Restart the historical host after remediation if shutdown ended the pass or startup retries were exhausted. |
| 4609 `DateReminderSchedulingFailed` | Dapr reminder scheduling failed in steady-state delivery or startup recovery. The warning carries tenant, work item, deterministic reminder name, bounded exception type, and the attached exception. | Check Works actor, placement, Scheduler, and state-store health; repair the failing dependency and verify at-least-once redelivery or startup retry schedules the same deterministic reminder. If startup retries were exhausted, restart Works after remediation. Exact caller cancellation intentionally emits no 4609. |

A 4607 is a terminal recovery exclusion, not proof that no reminder can fire: an already durable reminder may
still fire, but recovery cannot repair a missing registration while the aggregate remains parked. A 4603 or 4609
can originate outside startup recovery and does not alone prove a startup retry will occur. Interpret 4603 by its
structured `Reason`; only `startup-reminder-reconciliation` participates in the bounded reminder-retry policy.
Warnings 4604, 4605, 4606, and 4608 are startup-scan evidence subject to that policy unless shutdown stops the pass
before another attempt. Never delete parking or reminder state
directly as routine remediation; any privileged state-store intervention requires an incident record and
service-owner approval.

## Payload redaction rules

Never place raw bodies, event payloads, body hashes, or full command/event objects in logs, traces, metrics, list
responses, ProblemDetails, tickets, or chat. Operator responses expose only the established safe identity tuple and
bounded status/reason values. Treat state-store access as privileged incident response: the raw bytes exist solely so
the actor can replay the original envelope, not as an inspection surface.
