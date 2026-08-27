---
title: 'Scope the shared Works pub/sub component'
type: 'bugfix'
created: '2026-08-27'
status: 'blocked'
baseline_revision: '8f77558472b4141ff2edbb52ef0723b8a1764012'
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** The Works AppHost currently lets the EventStore Aspire helper generate an unscoped Redis pub/sub component, so the `works` sidecar does not consume the tracked shared-component authorization policy.

**Approach:** Extend EventStore's authoritative local pub/sub YAML and its security tests with an explicit least-privilege Works grant, then compose that YAML from Works through `pubSubComponentPath` and advance the parent submodule pin.

## Boundaries & Constraints

**Always:** Include `works` in component `scopes`; list `works=` in `publishingScopes` to deny all publishing; grant exactly `works=work.events` in `subscriptionScopes`; keep `eventstore` absent from both metadata scope lists so its dynamic publishing remains unrestricted; preserve separate dead-letter authorization; validate both the EventStore policy and the Works composed resource model.

**Block If:** The checked-in EventStore hosting API cannot accept the shared YAML without changing its public contract, or an unrelated dirty change appears in either owning repository.

**Never:** Edit `_bmad-output/implementation-artifacts/deferred-work.md` or any deferred-work ledger; grant Works a dead-letter topic; scope EventStore publishing; modify production pub/sub YAMLs; initialize nested submodules; integrate unrelated remote commits.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Works consumes shared events | `works` uses local `pubsub` | It can subscribe only to `work.events` and cannot publish | Security tests fail on missing, broader, or default-open Works entries |
| EventStore publishes dynamic topics | `eventstore` uses local `pubsub` | It remains unrestricted because it is omitted from `publishingScopes` | Security tests fail if EventStore receives any publishing entry |
| Works AppHost composes pub/sub | AppHost builds the EventStore topology | The `pubsub` resource `LocalPath` is EventStore's tracked `pubsub.yaml` | Topology test fails if the helper falls back to generated metadata |

</intent-contract>

## Code Map

- `references/Hexalith.EventStore/src/Hexalith.EventStore.AppHost/DaprComponents/pubsub.yaml` -- authoritative local Redis component; current component scopes omit `works`, while metadata has explicit grants for sample/test/example/ops identities.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Server.Tests/Security/PubSubTopicIsolationEnforcementTests.cs` -- YAML parser and security regression suite; reuse `GetComponentMetadataValue`, `ShouldNotContainAppId`, and parsed `scopes` assertions.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.Aspire/HexalithEventStoreExtensions.cs` -- read-only hosting seam: existing `pubSubComponentPath` selects an `IDaprComponentResource` with `LocalPath` and validates file existence.
- `src/Hexalith.Works.AppHost/Program.cs` -- Works composition root; resolve the EventStore YAML from repository metadata and pass it to `AddHexalithEventStore`.
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` -- existing resource-model test already checks state-store/resiliency paths and should assert the exact pub/sub `LocalPath`.
- `references/Hexalith.EventStore` -- root-declared submodule whose committed revision must be advanced in the Works parent after focused validation.

## Tasks & Acceptance

**Execution:**
- `references/Hexalith.EventStore/src/Hexalith.EventStore.AppHost/DaprComponents/pubsub.yaml` -- add the documented Works component, publish-deny, and `work.events`-only subscription entries without adding dead-letter access or restricting EventStore.
- `references/Hexalith.EventStore/tests/Hexalith.EventStore.Server.Tests/Security/PubSubTopicIsolationEnforcementTests.cs` -- assert exact Works component membership, empty publishing grant, single regular-topic subscription, no dead-letter grant, and continued EventStore omission.
- `src/Hexalith.Works.AppHost/Program.cs` -- resolve the tracked EventStore pub/sub YAML and supply it as `pubSubComponentPath` alongside the existing isolated state store.
- `tests/Hexalith.Works.IntegrationTests/WorksAppHostTopologyTests.cs` -- prove the composed `pubsub` resource uses the exact tracked YAML `LocalPath`.
- `references/Hexalith.EventStore` -- commit the validated submodule changes, then record that revision in the Works parent without touching the deferred-work ledger.

**Acceptance Criteria:**
- Given the local EventStore pub/sub YAML, when its authorization metadata is parsed, then `works` is a component-scoped identity denied all publishing and allowed to subscribe only to `work.events`.
- Given the Works subscription grant, when topic names are inspected, then no dead-letter topic or second regular topic is authorized.
- Given EventStore dynamic publishing, when publishing and subscription metadata are parsed, then `eventstore` is absent from both lists and remains unrestricted.
- Given the Works AppHost resource model, when the `pubsub` component is inspected, then its type is `pubsub.redis` and its `LocalPath` is the tracked EventStore `pubsub.yaml`.
- Given focused EventStore and Works tests, when they run, then all existing and new assertions pass and the parent records the validated EventStore revision.

## Spec Change Log

## Review Triage Log

## Design Notes

Use `ProjectMetadataPaths.GetProjectPath(...)`, already used by the Works composition root for EventStore assets, so Aspire testing and local execution resolve the same submodule file. An explicit empty `works=` publishing entry is required because an identity omitted from Dapr scope metadata is default-open.

## Verification

**Commands:**
- `dotnet test tests/Hexalith.EventStore.Server.Tests/Hexalith.EventStore.Server.Tests.csproj --configuration Debug --no-restore` from `references/Hexalith.EventStore` -- expected: EventStore security suite passes.
- `dotnet test tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore` from the Works root -- expected: topology and integration tests pass.
- `dotnet build Hexalith.EventStore.slnx --configuration Debug --no-restore` and `dotnet build Hexalith.Works.slnx --configuration Debug --no-restore` in their owning repositories -- expected: both solutions build without warnings or errors.

## Auto Run Result

Status: blocked
Blocking condition: implementation verification failed. `dotnet test tests/Hexalith.EventStore.Server.Tests/Hexalith.EventStore.Server.Tests.csproj --configuration Debug --no-restore` ran 3,139 tests (3,113 passed, 25 skipped) but failed the unrelated pre-existing `DaprComponentValidationTests.DomainServiceSidecars_DoNotReferenceStateStoreOrPubSubComponents` marker check because the EventStore AppHost no longer contains `IResourceBuilder<ProjectResource> tenants =`. `dotnet build Hexalith.EventStore.slnx --configuration Debug --no-restore` also failed on missing restore assets/packages and pre-existing `references/Hexalith.Commons/.../UniqueIdHelper.cs` StyleCop errors. Focused fallback evidence passed: `PubSubTopicIsolationEnforcementTests` 12/12, `WorksAppHostTopologyTests` 4/4, Works integration 171 passed with four infrastructure-dependent skips, and the Works solution build completed with zero warnings and zero errors.
