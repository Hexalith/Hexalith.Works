---
title: 'Harden Dapr subscription operations'
type: 'feature'
created: '2026-08-28'
status: 'done'
baseline_revision: 'e4b1d9ebb4478aef1f2ee0aa8c72177c912350fb'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/implementation-artifacts/spec-dapr-subscription-topology-hardening.md'
warnings:
  - multiple-goals
  - oversized
deferred:
  - summary: >-
      The dead-letter capture parser's fixtures are hand-written literals rather than derived from the
      publisher type, so a rename on the producing side breaks capture in production while both parser
      tests stay green.
    evidence: |-
      DeadLetterEnvelopeParser requires data.messageId, tenantId, domain, aggregateId, correlationId and one
      of the eventTypeName/eventName/eventType aliases; a missing field collapses the identity to
      "unidentified-<hash>" and permanently disqualifies the item from replay. Every fixture
      (DeadLetterEnvelopeParserTests, DeadLetterCaptureBodyTests) is a UTF-8 literal typed into the test file,
      and nothing in either repository builds one by serializing the real producer envelope. This is not
      hypothetical: the first pass of this story shipped exactly that defect (the parser accepted only
      eventName/eventType while the publisher emits eventTypeName) and human review, not a test, caught it.
      Caused by this change but not trivially fixable: Hexalith.EventStore.Operations.Tests would need a
      reference to Hexalith.EventStore.Server to serialize EventEnvelope, which is a deliberate
      dependency-surface decision for a shared submodule rather than an in-pass patch.
    location: >-
      references/Hexalith.EventStore/src/Hexalith.EventStore.Operations/Capture/DeadLetterEnvelopeParser.cs
    severity: high
---

<intent-contract>

## Intent

**Problem:** The Works subscriber acknowledges unknown future processor results, Dapr discards the malformed state-store resiliency target, and `deadletter.work.events` has no durable, observable operator path. The current EventStore pin also supplies a pub/sub component whose unsupported Redis fallback can prevent the entire event/DLQ boundary from loading.

**Approach:** Fail closed on unknown processing results, correct and structurally prove the resiliency target, and add a separate reusable EventStore operations workload that captures the Works DLQ durably, exposes the existing authorized admin dead-letter workflow, and retries delivery without granting Works access to its own DLQ. Compose it through a Works-owned, explicitly scoped local pub/sub component and finish with regression and independent-review evidence against EventStore `2ae587024ec7dd7dfaca174bf22aa8d74b7a8dc1`.

## Boundaries & Constraints

**Always:** Acknowledge all seven pinned terminal/retry results exactly like the canonical EventStore mapper and return 500 for unknown enum values. Persist the raw dead-letter body and safe metadata before acknowledging capture; deduplicate by CloudEvent/EventStore message id and retain replay state across restart. Keep Works subscribed only to `work.events`; grant `eventstore-operations` component access and subscription access only to `deadletter.work.events`, with explicit publish denial. Keep payloads/raw bodies out of logs, traces, metrics, list responses, and errors; metric dimensions are bounded topic/status/reason codes only. Preserve Admin.Server JWT role/tenant authorization and make operations service invocation caller-scoped.

**Block If:** The pinned Dapr actor/state APIs cannot atomically retain a captured item plus its index, or Dapr cannot redeliver the captured structured CloudEvent to the configured target without exposing raw payload through an operator API.

**Never:** Edit the deferred-work ledger or `.bmad-loop`; subscribe Works to `deadletter.work.events`; put operations plumbing in the Works kernel; add broker-specific clients, package-version changes, EventStore-pin updates, blind malformed-payload republishing, payload logging, or unrestricted operator publish/subscribe scopes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Known processor result | Any of the seven pinned enum values | Six terminal values return 200; `RetryableInProgress` returns 500 | No value falls through implicitly |
| Future processor result | Undefined enum value | HTTP 500 preserves retry/DLQ behavior | Never acknowledge unknown outcomes |
| First DLQ delivery | Structured CloudEvent on `deadletter.work.events` | Raw bytes, hash, safe identity tuple, and pending state are durable before 200 | Persistence failure returns non-2xx |
| Duplicate DLQ delivery | Same message id/body redelivered | One durable item; duplicate metric increments | Conflicting bytes for the same id fail closed |
| Replayable item | Authorized operator retries a retained valid envelope | Durable replay-requested state precedes direct Dapr delivery to `/work/events`; success becomes replayed | Failure remains retryable and retained with a bounded reason code |
| Malformed/unidentified item | Body lacks safe replay identity | Retained and visible as replay-ineligible | Operator may archive; never blindly replay raw bytes |
| Restart during replay | Item is replay-requested/replaying | Actor reminder/activation resumes idempotently | Works marker dedup tolerates duplicate delivery |
| Unauthorized access | Wrong app-id, role, or tenant | No list/count/action access | Return opaque forbidden response; expose no item existence |

</intent-contract>

## Code Map

- `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:63-67` -- replace the binary result check with the pinned SDK's exhaustive switch; expose the mapper internally for direct tests.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainEventsEndpointExtensions.cs:47-62` -- read-only canonical mapping: six terminal values to 200, retryable and unknown values to 500.
- `src/Hexalith.Works.AppHost/DaprComponents/resiliency/resiliency.yaml:58-61` -- nest the state-store policies under `outbound`; Dapr 1.18 ignores the current top-level fields.
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs:195-231` -- reuse YAML helpers and add exact parent/child key assertions for the state-store outbound target.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Subscriptions/DaprEventStoreDomainEventMarkerStore.cs` -- reuse first-write/dedup semantics and safe marker-key conventions in the operations queue.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Admin.Server/{Controllers,Services}` and `src/Hexalith.EventStore.Admin.Abstractions` -- preserve existing admin contracts, role/tenant filters, and UI-facing surface while replacing unpopulated state indexes/nonexistent EventStore action routes with the operations workload.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.AppHost/DaprComponents/pubsub.yaml:98-135` -- read-only defect evidence: unsupported `{env:REDIS_HOST|...}` and default-open `ops-monitor`; do not edit the pin's topology file for Works.
- `src/Hexalith.Works.AppHost/Program.cs:8-107` -- compose the separate operations host/sidecar and root-owned pubsub resource without changing the Works runtime host.
- `src/Hexalith.Works.AppHost/DaprComponents/{pubsub.yaml,statestore.yaml,accesscontrol.works.yaml}` -- own literal local Redis values, exact three-layer topic scopes, operations state scope, and narrow operations-to-Works replay permission.

## Tasks & Acceptance

**Execution:**
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs` and `tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs` -- implement/test exhaustive current and future result mapping -- resolve DW-29.
- `src/Hexalith.Works.AppHost/DaprComponents/resiliency/resiliency.yaml`, `src/Hexalith.Works.AppHost/Program.cs`, and `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` -- correct `statestore.outbound`, update rationale, and assert exact nesting/policies -- resolve DW-30.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Operations/**` and its focused test project/solution entries -- add a non-packable reusable web/actor workload with options validation, raw capture endpoint, durable per-topic drain actor, dedup/hash conflict handling, replay/archive state machine, reminder recovery, redacted diagnostics, and caller-scoped internal query/action endpoints -- provide the Platform DLQ operator core without changing package inventory.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Admin.Server/Services/DaprDeadLetter{Query,Command}Service.cs` plus focused Admin.Server tests -- route the authorized admin facade to `eventstore-operations`, forward operator context, and remove reliance on phantom indexes/routes -- make existing list/count/retry/skip/archive surfaces operational.
- `src/Hexalith.Works.AppHost/{HexalithEventStoreOperations.cs,Program.cs,Hexalith.Works.AppHost.csproj,DaprComponents/**}`, `Hexalith.Works.slnx`, and topology tests -- compose `eventstore-operations`, literal/scoped pubsub, state/resiliency references, deny-by-default caller policies, waits, and environment values -- resolve the current-pin pubsub defect confirmed by DW-32 while Works remains DLQ-blind.
- `docs/operations/subscriber-dead-letter-operator.md` and `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs` -- document and govern Dapr subscriber DLQ versus EventStore command dead letters, alert thresholds, triage/fix/retry/verify/archive, retention, restart recovery, and payload-redaction rules -- make the path operable.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/**`, `tests/Hexalith.Works.IntegrationTests/EventStoreOperationsTests.cs`, `WorksAppHostTopologyTests.cs`, and `WorksDomainEventSubscriptionTests.cs` -- cover durable-before-ack, duplicate/conflicting delivery, restart/replay convergence, scoped topology, redacted metric dimensions, Works non-subscription, and a live Dapr poison-to-backlog-to-retry lane where available -- resolve DW-31 and record deterministic evidence when live infrastructure is unavailable.

**Acceptance Criteria:**
- Given any current or future `EventStoreDomainEventProcessingResult`, when Works maps the outcome, then only the six known terminal results return 200 and every retryable/unknown result returns 500.
- Given the committed resiliency CRD, when parsed and loaded by Dapr 1.18, then `statestore` contains only an `outbound` policy target with the exact retry, timeout, and circuit-breaker references.
- Given the composed topology, when Dapr evaluates component scopes and subscription discovery, then only `eventstore-operations` subscribes to `deadletter.work.events`, it cannot publish, Works subscribes only to `work.events`, and all Redis endpoints are executable literals.
- Given a DLQ delivery, when the operations endpoint acknowledges it, then a restart-safe deduplicated backlog record already exists and observability reveals count/age/outcome without payload or identifier dimensions.
- Given an authorized tenant-scoped operator action, when a replayable entry is retried, then durable state records the request before the configured Works delivery, retries converge after restart, and unauthorized/cross-tenant actions reveal nothing.
- Given the current EventStore pin and the completed independent follow-up review, when the focused and broad verification lanes run, then confirmed mapping, resiliency, pubsub, admin-facade, security, and replay defects are fixed without submodule dependency updates or ledger edits.

## Spec Change Log

## Review Triage Log

### 2026-08-28 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 25: (high 14, medium 8, low 3)
- defer: 0
- reject: 3: (high 0, medium 3, low 0)
- addressed_findings:
  - `[high]` `[patch]` The application port trusted forgeable caller headers; added required non-Development Dapr app-channel-token validation, constant-time middleware, host evidence, and private-port deployment guidance.
  - `[high]` `[patch]` Chunked capture could allocate beyond `MaxBodyBytes`; added bounded streaming and exact-boundary/oversize tests.
  - `[high]` `[patch]` Arbitrary JSON with identity-like fields could become replayable; required a valid structured CloudEvent and complete matching EventStore identity.
  - `[high]` `[patch]` Duplicate case-insensitive identity properties could create authorization ambiguity; ambiguous envelopes now remain replay-ineligible with adversarial coverage.
  - `[medium]` `[patch]` Retained identity and actor-key values were unbounded; added shared length validation at parsing, capture, query, and action boundaries.
  - `[medium]` `[patch]` One failing replay head-of-line blocked later requests; the drain now persists the failure and continues independently.
  - `[high]` `[patch]` Reminder-registration failure looked successful and could strand durable work; it now emits bounded telemetry and propagates retryably after persistence.
  - `[high]` `[patch]` A `Replaying` item could be ignored after an outcome-state save failure; reminder recovery now normalizes and retries it.
  - `[low]` `[patch]` Replay-attempt increments could overflow; the counter now saturates at `int.MaxValue`.
  - `[medium]` `[patch]` Filtered offsets skipped entries after queue mutation; pagination now uses the append-only raw-index cursor.
  - `[medium]` `[patch]` Extreme continuation offsets could overflow; cursor arithmetic and tests now remain bounded.
  - `[medium]` `[patch]` Unknown future replay states were implicitly visible/actionable; list and action paths now fail closed.
  - `[high]` `[patch]` Backlog metrics depended on operator list calls; current-value observable gauges are refreshed on activation and state changes.
  - `[medium]` `[patch]` Tenant-filtered queries could overwrite global metric observations; telemetry is now computed from the full actor backlog only.
  - `[high]` `[patch]` Whitespace tenant filters silently became global queries; the Admin facade and operations actor now reject them.
  - `[high]` `[patch]` Tenant-scoped read-only users could see the global backlog count; the explicitly global endpoint now requires the Admin policy.
  - `[medium]` `[patch]` Operator batches were unbounded; added validated `MaxActionItems` limits at endpoint and actor boundaries.
  - `[medium]` `[patch]` A whitespace-only Bearer parameter passed the internal predicate; authorization now parses and validates the scheme and token parameter.
  - `[high]` `[patch]` The real replay transport had no boundary test; tests now pin target app/method, exact bytes, CloudEvent content type, and non-2xx propagation.
  - `[high]` `[patch]` Dapr subscription discovery was unverified; a host test now observes the exact `/dapr/subscribe` mapping and capture route.
  - `[high]` `[patch]` Restart coverage bypassed actor activation; focused tests now exercise activation normalization, reminder registration, recovery, and failure propagation.
  - `[high]` `[patch]` Admin action tests did not pin app id, routes, body, or token; retry/skip/archive and query tests now assert the full operations invocation contract.
  - `[high]` `[patch]` The new operations suite was absent from normal CI; added it to EventStore workflow and local Tier-1 inventories.
  - `[low]` `[patch]` The pub/sub component carried inert `enableDeadLetter` metadata; removed it and pinned the actual subscription discovery contract.
  - `[low]` `[patch]` The runbook claimed terminal state was list-visible; verification now uses disappearance, count reduction, replay metrics, and Works outcome.

### 2026-08-28 — Review pass (follow-up)
- intent_gap: 0
- bad_spec: 0
- patch: 11: (high 4, medium 3, low 4)
- defer: 0
- reject: 18: (high 0, medium 6, low 12)
- addressed_findings:
  - `[high]` `[patch]` The capture parser accepted only `eventName`/`eventType`, but the publisher emits `eventTypeName` (`EventEnvelope.EventTypeName`, camel-cased on the wire), so every real Works dead letter parsed as unidentified and was permanently replay-ineligible; the alias set now includes the producer's actual name and a producer-shaped envelope test pins it.
  - `[high]` `[patch]` `publishingScopes` gave the `works` app id an empty publish grant while `deadletter.work.events` is a protected topic, and Dapr's poison-message forwarding publishes from the *subscribing* sidecar under its own app id -- daprd 1.18.1 rejected it with `403 ERR_PUBSUB_FORBIDDEN`, so nothing could ever reach the operations workload; Works is now granted exactly that one topic and the probe returns 204.
  - `[high]` `[patch]` `/alive`, `/health`, and `/ready` sat behind the app-channel token middleware, so in every non-Development environment -- exactly where the token is mandatory -- the orchestrator and Dapr app health probes would have received 401 and the workload would never have reported healthy; the health paths are now outside the token boundary, with host coverage in Production.
  - `[high]` `[patch]` A permanently rejected replay was re-delivered every reminder period forever because `ReplayAttempts` was incremented but never read; a validated `MaxReplayAttempts` now archives the entry as `replay-exhausted:<reason>` on exhaustion.
  - `[medium]` `[patch]` Oversize, empty, and hash-conflicting captures returned non-2xx on a topic that has no dead-letter destination of its own, so Dapr looped them forever while nothing was retained; these permanent conditions are now acknowledged and counted on the bounded capture metric, and only resolvable failures stay non-2xx.
  - `[medium]` `[patch]` Neither access-control document had any content assertion, so the replay grant or the deny-by-default action could be removed or inverted with every test still green; both are now asserted structurally, and the pub/sub scope test pins the invariant that the Works publish grant equals the topic the operations workload drains.
  - `[medium]` `[patch]` The deleted admin service tests took the 401/403 -> `Unauthorized` canonicalization (DW11 AC4) with them; the mapping is restored as a theory over 401, 403, and 422.
  - `[low]` `[patch]` `ToAdminEntry` reused the reserved tenant sentinel as filler for domain, aggregate, correlation, and event type; non-tenant slots now use a distinct placeholder so an operator can tell them apart.
  - `[low]` `[patch]` `ObserveBacklogAsync` ran once per drained item and rescans the whole backlog, making a drain quadratic in retained items; it is now observed once per drain.
  - `[low]` `[patch]` `accesscontrol.works.yaml` still carried a header telling the reader to adopt the deny-by-default posture the file already had; the header now states what the file does and keeps only the genuine mTLS caveat.
  - `[low]` `[patch]` The runbook omitted the body-size limit and permanent-condition outcomes, the all-or-nothing batch semantics and item cap, the replay-attempt ceiling, the reserved `unidentified` tenant scope, the Admin-policy requirement on the global count, and the health-probe token exemption; all are now documented.

### 2026-08-28 — Review pass (follow-up 2)
- intent_gap: 0
- bad_spec: 0
- patch: 10: (high 4, medium 3, low 3)
- defer: 1: (high 1, medium 0, low 0)
- reject: 14: (high 0, medium 5, low 9)
- addressed_findings:
  - `[high]` `[patch]` The scoped Works-owned pub/sub component broke EventStore's own command dead letters. `EventPublisherOptions` derives that topic as `{DeadLetterTopicPrefix}.{GetPubSubTopic(identity)}`, and this AppHost overrides the work domain's topic to `work.events` -- so with the shipped default prefix the command dead-letter topic is the literal string `deadletter.work.events`, the subscriber DLQ, which `eventstore` has no publish grant on. daprd 1.18.1 answers `403 ERR_PUBSUB_FORBIDDEN` and `DeadLetterPublisher` only logs it, so every command dead letter was silently lost; had the grant been added instead, the two queues the runbook insists are separate would have merged. The AppHost now sets the prefix to `commanddeadletter`, `eventstore` is granted exactly that one extra topic, and a topology test derives the topic the same way the publisher does so neither setting can move alone.
  - `[high]` `[patch]` The capture design rested on "Dapr redelivers a non-2xx forever", but the composed resiliency CRD gives every sidecar `pubsubRetryInbound` with `maxRetries: 10`, so a refused delivery is dropped with nothing retained -- and the retryable failure path emitted no telemetry at all while the workload emits no log output by design. A permanent actor-side rejection also went down that same path. The endpoint now counts `capture-failed`, the actor reports an unretainable delivery as an outcome instead of throwing (a Dapr proxy does not carry the remote exception type), and the remarks, options docs, and runbook state the real bounded budget.
  - `[high]` `[patch]` `MaxReplayAttempts` was a per-item lifetime cap, not a per-request budget: an operator retry did not reset it and an item archived as `replay-exhausted` could never be retried again, so a target outage longer than ten reminder periods stranded the entry permanently -- the exact failure the operator surface exists to recover from. Retry now resets the counter and re-opens an exhaustion-archived entry, while an entry an operator skipped or archived stays that way.
  - `[high]` `[patch]` `GET /api/v1/admin/dead-letters` with no `tenantId` resolved a non-Admin caller with no tenant claim to the global scope. The Operator role is granted by `eventstore:permission=command:replay` alone and needs no tenant claim, and `AdminTenantAuthorizationFilter` skips a request that names no tenant -- latent while the backing index was never populated, live now that the operations workload returns real items. The unscoped listing is restricted to the Admin role, as the global count already was.
  - `[medium]` `[patch]` The rewritten query facade ended in `EnsureSuccessStatusCode`, so a 403 from the operations workload reached the operator as the 503 "temporarily unavailable, retry shortly" the outage path uses. The command facade's DW11 AC4 canonicalization is now mirrored on list and count, with the controller mapping it to Forbidden, plus the missing internal-timeout test.
  - `[medium]` `[patch]` The backlog gauges published a placeholder topic and zero until the drain actor was first activated, and nothing activates it for a restarted host holding a captured-but-never-retried backlog -- so the runbook's count and age alerts could not fire for exactly the backlog nobody is watching. The topic is seeded from configuration and a bounded startup reconciliation activates the actor once.
  - `[medium]` `[patch]` `ToAdminEntry`, the only operator-visible projection, was executed by no test: `DeadLetterEntry` is a positional record of five consecutive strings that rejects a null in every slot, so a transposed field compiles and a dropped placeholder makes the whole page 500 for an unidentified item. A host-level list test now pins the projection and the paging envelope for an identified and an unidentified entry.
  - `[low]` `[patch]` The list page bound was a magic `500` duplicated between the endpoint clamp and the actor guard; it is now a validated `MaxListItems` option both ends read.
  - `[low]` `[patch]` The continuation cursor was set at the raw index where the page filled, so a page whose tail is terminal or another tenant's handed the operator a token yielding an empty page. The cursor now points at the next entry the same request would return.
  - `[low]` `[patch]` The runbook promised seven-day retention "using the platform retention job when available"; no purge or compaction operation exists on the workload and archiving only flips state. It now says so plainly, and the stale trust-boundary remarks on the Works subscription endpoint -- still claiming only sidecar loopback reaches it, when `eventstore-operations` now invokes it -- name the access-control policy that actually holds the boundary.

## Design Notes

The operator is a separate EventStore-owned workload because subscriber DLQ capture/replay is reusable platform plumbing, while Works remains a domain service. The operations actor is the durable serialization point: capture saves the body/hash plus index before ACK; retry first saves `ReplayRequested`, then re-delivers the original structured CloudEvent through Dapr service invocation, and only then marks `Replayed`. A crash can repeat delivery, which is safe because Works' durable marker store deduplicates by message id. Malformed entries remain inspectable only through redacted metadata and may be archived, never automatically replayed.

## Verification

**Commands:**
- `dotnet build references/Hexalith.EventStore/Hexalith.EventStore.slnx --configuration Release` -- expected: platform workload/admin changes compile with zero warnings.
- `dotnet test references/Hexalith.EventStore/tests/Hexalith.EventStore.Operations.Tests/Hexalith.EventStore.Operations.Tests.csproj --configuration Release` -- expected: operations capture, durability, replay, and redaction suite passes.
- `dotnet test references/Hexalith.EventStore/tests/Hexalith.EventStore.Admin.Server.Tests/Hexalith.EventStore.Admin.Server.Tests.csproj --configuration Release` -- expected: admin dead-letter facade routing/auth suite passes.
- `dotnet build Hexalith.Works.slnx --configuration Release` -- expected: Works composition compiles with zero warnings.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorksDomainEventSubscriptionTests` -- expected: mapping and discovery tests pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorksAppHostTopologyTests` -- expected: exact resource/scoping/YAML assertions pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class- "*SmokeTests"` -- expected: deterministic integration lane passes.
- `timeout 10s ~/.dapr/bin/daprd --app-id operations-probe --resources-path src/Hexalith.Works.AppHost/DaprComponents/resiliency` -- expected: resiliency configuration loads; timeout 124 after startup is acceptable.
- `timeout 12s ~/.dapr/bin/daprd --app-id works --resources-path <dir holding only pubsub.yaml> --config src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml` -- expected: `Component loaded: pubsub (pubsub.redis/v1)` with no scope or access-control error.
- `curl -X POST http://127.0.0.1:3999/v1.0/publish/pubsub/deadletter.work.events` against that sidecar -- expected: 204. With the works publishing scope emptied it must return `403 ERR_PUBSUB_FORBIDDEN`, which is the regression this proves against.
- `git diff --check` in both owning repositories -- expected: no whitespace errors.

## Auto Run Result

Status: done
Blocking condition: none

### Implemented change

Second follow-up review pass over the committed story (`3fbfc4a` in Works, `39928962` in the EventStore
submodule). No spec amendment and no re-derivation were needed. Ten patch findings were fixed in place and
re-verified; one real finding was deferred because its fix is a dependency-surface decision on a shared
submodule rather than an in-pass patch.

Two of the four high-severity findings were latent kills of paths the story claims to protect rather than edge
cases. The Works-owned pub/sub component, added by this story to replace the pin's default-open one, silently
forbade EventStore's own command dead letters: `EventPublisherOptions` derives that topic from the domain topic
this same AppHost overrides, so the shipped default prefix resolved it to `deadletter.work.events` -- the
subscriber DLQ -- which `eventstore` may not publish to. daprd answers 403 and `DeadLetterPublisher` only logs
it. And `MaxReplayAttempts`, added by the previous review pass, behaved as a per-item lifetime cap: an operator
retry inherited the counter and an exhausted entry was archived beyond recovery, so a target outage longer than
ten reminder periods permanently stranded exactly the entries the operator surface exists to recover.

### Files changed

Works:
- `src/Hexalith.Works.AppHost/Program.cs` -- set `EventStore__Publisher__DeadLetterTopicPrefix` so command dead letters do not resolve onto the subscriber DLQ topic.
- `src/Hexalith.Works.AppHost/DaprComponents/pubsub.yaml` -- grant `eventstore` publish on `commanddeadletter.work.events`, add it to the allowed/protected topics, and record why the two queues must stay distinct.
- `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs` -- remarks now name the access-control policy that holds the trust boundary, which is no longer sidecar loopback alone.
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` -- updated scope literals, exact `eventstore` publish set, and a test that derives the command dead-letter topic the way the publisher does.
- `docs/operations/subscriber-dead-letter-operator.md` -- command dead-letter topic and the two settings that pin it, the bounded inbound retry budget and the loss it implies, `capture-failed` and `unretainable` outcomes, the replay budget's per-request semantics, the absent purge operation, and the list page bound.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs` -- govern the loss and recovery bounds an operator can learn nowhere else.

EventStore submodule:
- `Endpoints/DeadLetterOperationsEndpointExtensions.cs` -- count `capture-failed`, acknowledge the `Unretainable` outcome, clamp the page size to `MaxListItems`, correct the redelivery remarks.
- `Actors/DeadLetterDrainActor.cs` -- reset the replay budget on operator retry, re-open an exhaustion-archived entry, report an unretainable capture instead of throwing, cursor points at the next matching entry, list bound from options.
- `Actors/DeadLetterBacklogReconciler.cs` (new) -- bounded startup activation so the backlog gauges reflect a restarted host.
- `Models/DeadLetterCaptureOutcome.cs`, `Configuration/EventStoreOperationsOptions.cs`, `EventStoreOperationsOptionsValidator.cs`, `Telemetry/EventStoreOperationsTelemetry.cs`, `Program.cs` -- `Unretainable` outcome, validated `MaxListItems`, telemetry topic seeded from configuration, reconciler registration.
- `Admin.Server/Services/DaprDeadLetterQueryService.cs`, `Controllers/AdminDeadLettersController.cs` -- canonicalize 401/403 on the read surfaces, and refuse to widen a non-admin caller to the global backlog.
- Operations and Admin.Server test suites -- 10 new tests covering each fix, including the first execution of the operator list projection.

### Review findings breakdown

Patches applied: 10 (high 4, medium 3, low 3). Items deferred: 1 (high 1) -- the capture parser's fixtures are
hand-written rather than derived from the publisher type, which is the exact defect class that already shipped
once in this story; fixing it properly means giving the operations test project a reference to
`Hexalith.EventStore.Server`, a dependency-surface decision on a shared submodule that should not be made
inside a review pass. Items rejected: 14 (high 0, medium 5, low 9). The substantive rejections:

- The claim that the `/internal/dead-letters/**` access-control glob cannot match the bare list route. Traced
  against daprd 1.18.1 `pkg/config/acl_trie.go`: `Trie.Search` reaches the final segment with `isEnd` true,
  finds no data on the `/dead-letters` node, and then calls `findSubNode("/*", true)`, whose
  `findNodeWithWildcard` returns any `MultiStageWildcard` node unconditionally. `/internal/dead-letters` and
  `/internal/dead-letters/count` both resolve to the allow action.
- The intent-alignment reading that "no EventStore-pin updates" freezes the submodule gitlink. This story has
  followed the dependency-hygiene reading since its first pass; no package or nested-submodule pin moved.
- `APP_API_TOKEN` documented but not composed, no `ILogger` output, `unidentified` remaining a representable
  tenant id, and the capture read allocating its bound per request -- all previously logged and re-rejected on
  the same grounds.
- The `defaultAction` flip on `accesscontrol.works.yaml` breaking an unenumerated caller: the only Dapr
  service-invocation callers of `works` in either repository are `eventstore` and `eventstore-operations`, both
  of which hold explicit policies.
- The `*.*.projection-changed` subscription being denied by the scoped component: that path is gated on
  `ProjectionChangeTransport.PubSub`, which this composition does not enable, and the component is deliberately
  narrow.
- Inert `WithReference` URL injection, the removed `CreateFallbackRequest` null guard, ordinal tenant
  comparison, unchecked actor id, the triple backlog scan per drain, and CI inventory ordering.

Follow-up review recommendation: true. Patched severities were high 4, medium 3, low 3; a high-severity patch
alone sets the flag, and the score is 3x3 + 1x3 = 12, above the threshold of 5.

### Verification performed

- `dotnet build references/Hexalith.EventStore/Hexalith.EventStore.slnx --configuration Release` -- succeeded, 0 warnings, 0 errors.
- `dotnet test .../Hexalith.EventStore.Operations.Tests` -- 76/76 passed (was 66).
- `dotnet test .../Hexalith.EventStore.Admin.Server.Tests` -- 737 total, 719 passed, 18 pre-existing ATDD skips, 0 failed (was 730/712).
- `dotnet build Hexalith.Works.slnx --configuration Release` -- succeeded, 0 warnings, 0 errors.
- Works lanes: `WorksAppHostTopologyTests` 7/7, `WorksDomainEventSubscriptionTests` 9/9, `EventStoreOperationsTests` 2/2, `-class- "*SmokeTests"` 189/189, `ArchitectureTests` 207/207, `UnitTests` 528/528, `PropertyTests` 3/3.
- daprd 1.18.1 resiliency probe -- `Loading Resiliency configuration: resiliency`, no error.
- daprd 1.18.1 component probe under both access-control documents -- `Component loaded: pubsub (pubsub.redis/v1)`, no scope or access-control error for `works` or for `eventstore-operations`.
- Live publish probe, `works` sidecar: `deadletter.work.events` -> 204, `work.events` -> `403 ERR_PUBSUB_FORBIDDEN`.
- Live publish probe, `eventstore` sidecar: `work.events` -> 204, `commanddeadletter.work.events` -> 204, `deadletter.work.events` -> `403 ERR_PUBSUB_FORBIDDEN`. This is the direct evidence for the command dead-letter fix: before it, the derived command dead-letter topic *was* `deadletter.work.events`, so that 403 was the publisher's own result.
- `git diff --check` -- clean in both repositories.

### Residual risks

- The deferred parser-fixture gap is the highest remaining risk: the capture path can go silently inert for
  every real dead letter with the whole suite green, and that has already happened once in this story.
- Dapr actor reminders still do not fire in this sandbox. Replay recovery, exhaustion, and the retry-budget
  reset are proven by direct activation and reminder invocation, not by an elapsed-timer run.
- The end-to-end poison -> capture -> retry lane through a live Works subscriber remains unexercised. Both
  publish legs are now proven live; capture and replay are still deterministic-only.
- The startup backlog reconciliation is a bounded best-effort attempt: if the sidecar and actor placement are
  not ready within five attempts it gives up, and the gauges stay at zero until something else activates the
  actor. The runbook tells the operator how to recognize that.
- Retention remains entirely manual. Archived entries keep their raw bodies and index positions indefinitely,
  and the per-drain scan cost grows with everything ever captured.
- `APP_API_TOKEN` still has no composition that sets it, so the app-channel token path is exercised only by
  tests and the workload would refuse to start outside Development as composed.
