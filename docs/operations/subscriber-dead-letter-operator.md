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
`deadletter.work.events` -- the subscriber queue. The AppHost therefore sets the prefix to `commanddeadletter`
and grants the `eventstore` app id publish on exactly that topic. If you change either the prefix or the domain
topic override, change the publish scope in `pubsub.yaml` with it: a mismatch either merges the two queues or
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
(`pubsubRetryInbound` in `src/Hexalith.Works.AppHost/DaprComponents/resiliency/resiliency.yaml`, exponential,
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

## Payload redaction rules

Never place raw bodies, event payloads, body hashes, or full command/event objects in logs, traces, metrics, list
responses, ProblemDetails, tickets, or chat. Operator responses expose only the established safe identity tuple and
bounded status/reason values. Treat state-store access as privileged incident response: the raw bytes exist solely so
the actor can replay the original envelope, not as an inspection surface.
