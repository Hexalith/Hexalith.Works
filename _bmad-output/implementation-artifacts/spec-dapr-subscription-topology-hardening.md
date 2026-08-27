---
title: 'Harden Dapr subscription and AppHost topology'
type: 'bugfix'
created: '2026-08-27'
status: 'done'
baseline_revision: '78700e10644a048ad3f96a61b7b27dbd2cf34bf4'
baseline_commit: '78700e10644a048ad3f96a61b7b27dbd2cf34bf4'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '_bmad-output/planning-artifacts/architecture.md'
warnings:
  - multiple-goals
deferred:
  - summary: >-
      Make endpoint result mapping fail retryably for unknown future EventStoreDomainEventProcessingResult values.
    evidence: |-
      The current endpoint mapping acknowledges every value except RetryableInProgress with HTTP 200. If the referenced EventStore SDK later adds a processing result, the Works endpoint would silently acknowledge that unrecognized outcome instead of retrying it. This behavior predates this bundle and is not caused by the DLQ/topology changes.
    location: >-
      src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:54
    severity: medium
  - summary: >-
      The resiliency CRD's statestore target declares retry/timeout/circuitBreaker at the top level instead of under inbound/outbound, so Dapr drops those policies.
    evidence: |-
      daprd parses `spec.targets.components.<name>` as inbound/outbound sections. Running daprd 1.18.1 against the committed file reduces the `statestore` target to `{"inbound":{},"outbound":{}}`, discarding `retry: defaultRetry`, `timeout: daprSidecar`, and `circuitBreaker: defaultBreaker`. The `pubsub` target uses the correct shape. This predates the bundle and is outside AC #4, which covers the actor state-store metadata/scopes and the inbound retry target only.
    location: >-
      src/Hexalith.Works.AppHost/DaprComponents/resiliency/resiliency.yaml:56
    severity: medium
  - summary: >-
      Nothing consumes, drains, alerts on, or documents the deadletter.work.events topic.
    evidence: |-
      The dead-letter topic is referenced only by the subscription endpoint and its regression test. The intent forbids subscribing Works to its own DLQ, so bounding redelivery necessarily trades an infinite retry loop for retained-but-unobserved messages. An operator drain/alert path and a runbook entry belong to a separate operational decision, not to this bundle.
    location: >-
      src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:50
    severity: low
---

<intent-contract>

## Intent

**Problem:** The Works event endpoint lets bodies that fail Minimal API binding escape before terminal invalid-payload handling, so Dapr can redeliver poison messages indefinitely. Its topology tests also accept annotation presence and source substrings instead of proving executable health, endpoint, component, topic, and configuration values.

**Approach:** Declare the Works programmatic subscription's dead-letter topic, make the host-owned route authoritative before SDK activation, and regress the actual Dapr discovery document. Replace topology proxies with typed Aspire-model, evaluated-environment, and structured Dapr-configuration assertions.

## Boundaries & Constraints

**Always:** Keep retries for transient processor failures, terminal processor acknowledgements for bindable invalid payloads, one `/work/events` POST route, one `dapr/subscribe` discovery route, `work.events` as the subscribed/published topic, and `deadletter.work.events` as the poison destination. Keep runtime changes at the runnable host/AppHost edge and use typed pinned SDK APIs.

**Block If:** The installed Dapr SDK cannot advertise a dead-letter topic through programmatic discovery, or SDK activation cannot preserve the host-owned subscription route without changing `references/`.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md` or any `.bmad-loop` ledger; modify submodules; acknowledge unbindable bodies with 200 and silently discard them; subscribe Works to its dead-letter topic; add broker-specific retry machinery, durable domain types, production surfaces, or package-version changes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid event | Bindable envelope on `work.events` | Existing deduplication, decoding, validation, and handler dispatch remain unchanged | Processor result mapping remains authoritative |
| Bindable invalid event | Envelope binds but metadata/payload is invalid | Processor terminally acknowledges with 200 | Marker and safe logging behavior remain unchanged |
| Unbindable event | Malformed or structurally unbindable delivery | Dapr discovery routes exhausted delivery to `deadletter.work.events` after configured inbound retries | No infinite broker redelivery and no Works DLQ subscription |
| Transient processor failure | Marker in progress or handler/infrastructure failure | Non-2xx preserves retry behavior before DLQ exhaustion | Existing retryable result/exception semantics remain intact |

</intent-contract>

## Code Map

- `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs:30-42` -- host-owned Minimal API route; replace two-string `WithTopic` metadata with `Dapr.TopicOptions` carrying exact pub/sub, topic, and DLQ values.
- `src/Hexalith.Works/Program.cs` and `src/Hexalith.Works/Runtime/WorksHost.cs` -- keep the executable entry point thin and map bespoke `/project` and `/work/events` before `UseEventStoreDomainService`; let SDK activation own CloudEvents and the single discovery route while preserving host routes.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs:121-172` -- read-only evidence that SDK activation skips already-mapped routes and owns CloudEvents plus `dapr/subscribe`.
- `src/Hexalith.Works.AppHost/Program.cs:34-75` -- executable resource/environment contract: domain registration, topic override, caller, endpoint reference, waits, health, and Dapr module composition.
- `src/Hexalith.Works.AppHost/DaprComponents/statestore.yaml` and `src/Hexalith.Works.AppHost/DaprComponents/resiliency/resiliency.yaml` -- structured component values and isolated inbound retry policy; do not validate with substrings.
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` -- replace presence/type-name/source scans with typed annotations, evaluated environment, exact relationships, and structured YAML assertions.
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs` -- new executable route/discovery regression using the actual Works host surface.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` -- add only the existing centrally-versioned YAML test dependency if structured parsing requires a direct reference.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs` -- advertise `deadletter.work.events` through `TopicOptions` -- provide an end-to-end poison termination path.
- [x] `src/Hexalith.Works/Program.cs` -- make host subscription mapping precede canonical SDK activation and remove duplicate explicit SDK middleware/discovery mapping -- guarantee route uniqueness.
- [x] `tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs` -- assert one delivery route, one discovery route, exact typed topic/DLQ metadata, and exact `/dapr/subscribe` JSON -- regress executable configuration.
- [x] `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` -- assert exact health keys, HTTP endpoint values, typed sidecar/app-health/config/component references, resource relationships/waits, evaluated environment values, and parsed component/retry values -- eliminate presence/source proxies.
- [x] `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` -- reference `YamlDotNet` without an inline version only if used by the structured topology test.

**Acceptance Criteria:**
- Given the runnable Works host, when Dapr discovers subscriptions, then exactly one `pubsub`/`work.events` route targets `/work/events` and declares `deadletter.work.events`.
- Given SDK domain-service activation, when endpoints are enumerated, then `/work/events` POST and `dapr/subscribe` each exist exactly once and the host-owned Web-JSON processor remains selected.
- Given the AppHost model with Keycloak disabled, when topology is inspected, then exact health keys, HTTP endpoint semantics, Dapr app ids/config paths/app-health, component types/references, waits, topic override, gateway reference, registration values, and caller values match the executable topology.
- Given the committed Dapr component files, when parsed structurally, then the actor state-store metadata/scopes and inbound retry target/policy values equal the intended configuration rather than merely containing matching text.

## Spec Change Log

## Review Triage Log

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 8: (high 0, medium 1, low 7)
- defer: 0
- reject: 17: (high 0, medium 4, low 13)
- addressed_findings:
  - `[medium]` `[patch]` `ResiliencyComponentHasExactInboundRetryTargetAndPolicy` pinned the node shape of only `spec.policies.timeouts` — the one section that broke last pass. daprd unmarshals every `spec.policies` section into string-valued Go structs and rejects the whole document if any leaf is a mapping, so turning `retries.pubsubRetryOutbound.maxInterval` into `{general: 10s}` made daprd 1.18.1 discard every policy (`pubsubRetryInbound` included) while the test still passed. The assertion now walks the entire policy tree (`timeouts` scalar-valued; `retries` and `circuitBreakers` mappings of scalars). Mutation-verified: that edit now fails the test, and the restored file still logs `Loading Resiliency configuration: resiliency`.
  - `[low]` `[patch]` The resiliency fan-out enumerated three sidecars by hand, recreating the silent-omission failure the surrounding comment claims to prevent. It now derives the set from every composed `ProjectResource` carrying a `DaprSidecarAnnotation`, and the topology test asserts every sidecar in the model references `resiliency` rather than naming three.
  - `[low]` `[patch]` `SidecarOf` used `.Single()`, so a project with zero or two sidecar annotations failed AppHost composition with an exception naming neither the resource nor the expectation. It now returns null for a sidecar-less project and throws a message naming the resource and the annotation count otherwise.
  - `[low]` `[patch]` Nothing kept `DaprComponents/resiliency/` isolated to the one CRD, even though all three sidecars now receive that directory on `--resources-path`. Added `ResiliencyResourceDirectoryContainsOnlyTheCommittedPolicyDocument`, which also asserts no `kind: Resiliency` document returns to the `DaprComponents` root.
  - `[low]` `[patch]` `ResolveDaprConfigPath` and the test's `LoadYaml` still named their parameter `fileName` after both started receiving a relative path with a directory segment. Renamed to `relativePath` and documented.
  - `[low]` `[patch]` The subscription test deleted its Data Protection key directory on the success path only, leaking a temp directory on every failing run. The body is now wrapped in `try`/`finally`.
  - `[low]` `[patch]` `CapturingHandler` retained the live `HttpRequestHeaders` of a request the test disposes before asserting on them, and both gateway tests bound a response they never checked. Headers are now snapshotted into a dictionary inside `SendAsync`, and each test asserts the `202 Accepted`.
  - `[low]` `[patch]` The gateway tests covered sidecar-with-token and no-sidecar, but not sidecar-without-token — the shape the AppHost actually composes, since it sets no `DAPR_API_TOKEN`. Added `RecoveryGatewayClientRoutesThroughTheSidecarWithoutAnApiToken`, asserting `dapr-app-id` is still sent and no `dapr-api-token` header appears.
  - `[low]` `[patch]` `ReferencedResources(works).ShouldBe([EventStoreName, EventStoreName])` asserted a duplicate relationship as if intended, with nothing explaining where the second one comes from. Commented, and `resiliency.Type` now compares against a literal instead of the resource-name constant.

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 8: (high 1, medium 2, low 5)
- defer: 2: (high 0, medium 1, low 1)
- reject: 9: (high 0, medium 2, low 7)
- addressed_findings:
  - `[high]` `[patch]` The committed resiliency CRD declared `spec.policies.timeouts.daprSidecar` as a mapping (`general: 5s`). daprd unmarshals that node as `map[string]string` and rejects the entire Resiliency document, so `pubsubRetryInbound` (maxRetries 10) and every other policy shipped inert — while the new structured test reported the intended values as verified. Reproduced against daprd 1.18.1 (`Could not parse resiliency file resiliency.yaml … Found 0 resiliency configurations`), fixed to the duration scalar the two upstream EventStore copies use, and re-verified (`Loading Resiliency configuration: resiliency`). The test now pins the node shape, not only the values.
  - `[medium]` `[patch]` Moving the CRD into `DaprComponents/resiliency/` removed it from the `eventstore` and `eventstore-admin` sidecars, which previously loaded it because `--resources-path` is derived from the referenced state-store component's directory. The publisher-side `pubsubRetryOutbound`, `apps.eventstore`, and `components.statestore` policies would have silently fallen back to Dapr defaults. All three sidecars now reference the resiliency component, and the topology test asserts that composition instead of pinning the omission.
  - `[medium]` `[patch]` The removed `WorksRecoveryExtensions.cs` source-substring proxies (`DAPR_HTTP_ENDPOINT`, `AddEventStoreDaprServiceInvocation`) were deleted rather than replaced, leaving the recovery gateway's sidecar routing untested repo-wide. Added `WorksRecoveryGatewayRoutingTests`, which observes the composed `HttpClient`: the sidecar endpoint wins over the direct address and the outbound request carries `dapr-app-id: eventstore` / `dapr-api-token`, and without a sidecar it falls back to the configured address with no Dapr headers.
  - `[low]` `[patch]` The dead-letter topic was a bare literal while the subscribed topic came from options, so overriding `TopicName` would silently desynchronize the poison destination. It is now derived from the subscribed topic and still resolves to `deadletter.work.events`.
  - `[low]` `[patch]` The AppHost rationale comment claimed the move avoided loading unrelated Configuration CRDs and a duplicate state-store component; `DaprComponents/` is on the sidecar's resources path either way, so the claim was false. Corrected, and the sidecar is now resolved from its `DaprSidecarAnnotation` rather than the `"works-dapr"` magic string.
  - `[low]` `[patch]` The `Program.cs` → `WorksHost.cs` move dropped every rationale comment from the composition root (kernel-purity boundary, RFC 9457 rule, Web-JSON poison-loop reason, Story 4.8 reminder trigger, `/project` route-yielding note, actor handler mapping). Restored.
  - `[low]` `[patch]` Nothing regressed the canonical SDK routes surviving the new host-first mapping order. The subscription test now also asserts a single `/project` POST with no topic metadata, plus `/process`, `/query`, and `/replay-state`.
  - `[low]` `[patch]` The subscription test booted a real host that persisted Data Protection keys into `~/.aspnet/DataProtection-Keys`. Keys now go to a temp directory that is removed after the run, and the host is stopped explicitly.


- `patch` -- strengthened the executable route regression to send malformed JSON, a bindable-invalid envelope, and a structured CloudEvent whose marker state forces the processor's retryable 500 result.
- `patch` -- modeled the committed resiliency CRD as an isolated Dapr resource referenced by the Works sidecar, so the asserted inbound retry policy participates in the executable topology.
- `patch` -- removed redundant EventStore reference/wait annotations and made topology assertions count duplicates rather than hiding them with `Distinct`.
- `patch` -- tightened YAML node-shape, timeout, relevant environment-key, and forbidden UI assertions.

### 2026-08-27 — Review pass
- intent_gap: 0
- bad_spec: 0
- patch: 7: (high 0, medium 1, low 6)
- defer: 1: (high 0, medium 1, low 0)
- reject: 8: (high 0, medium 2, low 6)
- addressed_findings:
  - `[medium]` `[patch]` Moved the resiliency CRD into its own resource directory and updated executable-model assertions so the Works sidecar reference contributes an isolated Dapr resource path.
  - `[low]` `[patch]` Removed only the unrelated cascade-recovery hosted service from the live-host subscription test to eliminate real Dapr calls and connection-refused noise.
  - `[low]` `[patch]` Proved bindable-invalid delivery is acknowledged without consulting the marker store.
  - `[low]` `[patch]` Locked malformed JSON to HTTP 400 with the problem-details media type.
  - `[low]` `[patch]` Kept exact discovery values while allowing additive Dapr discovery properties.
  - `[low]` `[patch]` Made semantically unordered health-key and Dapr-scope assertions compare stable sorted sets.
  - `[low]` `[patch]` Renamed the new test methods to the repository-required PascalCase convention.

## Design Notes

The DLQ bounds the previously unhandled binding-failure class while preserving non-2xx retry behavior for transient failures. It intentionally does not add a Works consumer for `deadletter.work.events`; operator handling of retained dead letters remains outside this bundle.

The dead-letter path is only as bounded as the inbound retry budget that feeds it, so the resiliency CRD is part of this bundle's contract rather than adjacent configuration. Two properties are load-bearing and are now asserted rather than assumed: the document must be one daprd accepts (`spec.policies.timeouts` entries are duration scalars, not mappings), and every sidecar that enforces a policy must receive the CRD's directory on `--resources-path`. The CommunityToolkit derives those paths from each referenced component's `LocalPath` directory, which is why moving the file into its own folder requires an explicit component reference from each sidecar instead of relying on it sitting beside `statestore.yaml`.

## Verification

**Commands:**
- `dotnet build Hexalith.Works.slnx --configuration Release` -- expected: zero warnings and errors.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release` -- expected: focused test assembly builds cleanly.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorksDomainEventSubscriptionTests` -- expected: subscription discovery and route tests pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorksAppHostTopologyTests` -- expected: typed topology/configuration tests pass without Docker or Dapr.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class Hexalith.Works.IntegrationTests.WorksRecoveryGatewayRoutingTests` -- expected: recovery gateway sidecar routing and fallback tests pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class- "*SmokeTests"` -- expected: the deterministic integration lane passes with no skips.
- `~/.dapr/bin/daprd --app-id probe --resources-path <dir containing resiliency.yaml>` -- expected: `Loading Resiliency configuration: resiliency`, not a parse error.
- `git diff --check` -- expected: no whitespace errors.

## Suggested Review Order

**Subscription ownership and failure semantics**

- Host mapping precedes SDK activation, preserving one route and canonical CloudEvents middleware.
  [`WorksHost.cs:74`](../../src/Hexalith.Works/Runtime/WorksHost.cs#L74)

- Topic metadata advertises the exact bounded poison-message destination.
  [`WorksDomainEventEndpointExtensions.cs:35`](../../src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs#L35)

- Executable entry point delegates to the independently startable host composition.
  [`Program.cs:1`](../../src/Hexalith.Works/Program.cs#L1)

**Executable Dapr topology**

- The resiliency CRD is modeled as an explicit Dapr resource and referenced from every composed sidecar, so no
  end of the pipeline falls back to Dapr defaults.
  [`Program.cs:20`](../../src/Hexalith.Works.AppHost/Program.cs#L20)

- Sidecars are resolved from their own annotation rather than the toolkit's `<appId>-dapr` naming convention.
  [`Program.cs:129`](../../src/Hexalith.Works.AppHost/Program.cs#L129)

**Regression evidence**

- Live-host test proves discovery, malformed, terminal, and transient CloudEvent outcomes.
  [`WorksDomainEventSubscriptionTests.cs:33`](../../tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs#L33)

- Typed Aspire assertions lock health, endpoints, sidecars, relationships, and evaluated environment.
  [`WorksAppHostTopologyTests.cs:29`](../../tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs#L29)

- Structured YAML checks lock actor storage and bounded inbound retry policy values.
  [`WorksAppHostTopologyTests.cs:140`](../../tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs#L140)

- Direct central dependency enables structured YAML parsing without an inline version.
  [`Hexalith.Works.IntegrationTests.csproj:19`](../../tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj#L19)

## Auto Run Result

Status: done

### Implemented change

The Works subscription now advertises `deadletter.work.events` through typed `Dapr.TopicOptions`, the host-owned
`/work/events` route is mapped before `UseEventStoreDomainService` so SDK activation yields to it while still owning
CloudEvents and the single `dapr/subscribe` discovery route, and the composition root moved from `Program.cs` into a
startable `WorksHost.Build`. The resiliency CRD that bounds the retry budget feeding the DLQ moved into its own
resource directory, was corrected to the duration-scalar timeout shape daprd accepts, and is referenced from every
composed sidecar. Topology tests were rewritten from annotation-presence and source-substring proxies to typed
Aspire-model, evaluated-environment, and structured YAML assertions, plus a live-host route/discovery regression.

### Files changed

- `src/Hexalith.Works/Runtime/Events/WorksDomainEventEndpointExtensions.cs` — typed `TopicOptions` carrying the
  pub/sub, topic, and a dead-letter topic derived from the subscribed topic.
- `src/Hexalith.Works/Runtime/WorksHost.cs` — new startable host composition; host routes precede SDK activation.
- `src/Hexalith.Works/Program.cs` — thin entry point delegating to `WorksHost.Build`.
- `src/Hexalith.Works.AppHost/Program.cs` — resiliency modeled as an isolated Dapr resource, referenced from every
  sidecar derived from the composed model; sidecars resolved from their own annotation with a named failure.
- `src/Hexalith.Works.AppHost/DaprComponents/resiliency/resiliency.yaml` — moved into its own resource directory;
  `timeouts.daprSidecar` corrected to a duration scalar.
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` — typed topology assertions, whole-policy-tree
  YAML shape checks, and the resiliency-directory isolation guard.
- `tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs` — live-host route, discovery-document,
  and delivery-outcome regression.
- `tests/Hexalith.Works.IntegrationTests/WorksRecoveryGatewayRoutingTests.cs` — observed-`HttpClient` regression for
  sidecar routing, sidecar routing without an API token, and direct-address fallback.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` — centrally-versioned `YamlDotNet`.

### Review findings breakdown

- Patches applied: 8 (medium 1, low 7) — see the 2026-08-27 triage entry above.
- Items deferred: 0 new this pass; the three pre-existing entries in `deferred` are unchanged.
- Items rejected: 17. The notable rejections: the change was reported as breaching the intent's "Never edit
  `deferred-work.md`" clause — that ledger is orchestrator-owned and out of this session's authority, so it was left
  untouched; an end-to-end daprd/broker proof that an unbindable delivery actually lands on `deadletter.work.events`
  is a live-lane requirement the intent does not ask for and the sandbox cannot run; a claim that malformed bodies
  never reach the DLQ is wrong (they return 400, which daprd retries and then dead-letters); and the StyleCop,
  central-package, and `InternalsVisibleTo` concerns are all contradicted by a zero-warning Release build.

### Follow-up review recommendation

`true`. Patched findings this pass: high 0, medium 1, low 7. Score = (3 × 1) + (1 × 7) = 10, which is ≥ 5.

### Verification performed

- `dotnet build Hexalith.Works.slnx --configuration Release` — Build succeeded, 0 warnings, 0 errors.
- `dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release`
  — Build succeeded, 0 warnings, 0 errors.
- `Hexalith.Works.IntegrationTests -class …WorksAppHostTopologyTests` — Total 4, Failed 0, Skipped 0.
- `Hexalith.Works.IntegrationTests -class …WorksDomainEventSubscriptionTests` — Total 1, Failed 0, Skipped 0.
- `Hexalith.Works.IntegrationTests -class …WorksRecoveryGatewayRoutingTests` — Total 3, Failed 0, Skipped 0.
- `Hexalith.Works.IntegrationTests -class- "*SmokeTests"` — Total 150, Failed 0, Skipped 0 (deterministic lane).
- Mutation check on the new whole-policy-tree assertion: rewriting `retries.pubsubRetryOutbound.maxInterval` as a
  mapping now fails `ResiliencyComponentHasExactInboundRetryTargetAndPolicy`; before this pass it passed.
- `daprd --app-id probe --resources-path src/Hexalith.Works.AppHost/DaprComponents/resiliency` (daprd 1.18.1) —
  `level=info msg="Loading Resiliency configuration: resiliency"`, no parse error.
- `git diff --check` — no whitespace errors.

### Residual risks

- The DLQ guarantee is still proven as two declarations verified separately: the host serves a discovery document
  naming `deadletter.work.events`, and the committed CRD declares `maxRetries: 10` on the inbound pub/sub target in a
  document daprd accepts. Nothing in the deterministic lane joins them by observing a real broker redelivery reaching
  the poison topic; that needs a live Dapr lane the sandbox cannot run.
- `deadletter.work.events` still has no consumer, drain, or alert (recorded in `deferred`), so bounding redelivery
  trades an infinite retry loop for retained-but-unobserved messages.
- `spec.targets.components.statestore` still declares its policies at the target's top level instead of under
  `inbound`/`outbound`, so daprd silently discards them (recorded in `deferred`). The new shape assertions cover the
  policy tree, which is what makes daprd reject the whole document; they deliberately do not assert the target-shape
  rule, because the committed file would fail it today.
- The topology test pins framework-generated values (health-check key format, `DaprHttpPort` 3501, the toolkit's
  duplicate reference relationship). That exactness is what AC #3 asks for, but it will need updating on an Aspire or
  CommunityToolkit upgrade for reasons unrelated to this topology.
