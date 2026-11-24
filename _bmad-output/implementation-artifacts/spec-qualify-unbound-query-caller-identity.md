---
title: 'Qualify forged caller identity on unbound Works query assertions'
type: 'verification'
created: '2026-11-24'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Settle deferred finding R29R-B1 ("Qualify whether unbound gateway query assertions can carry forged caller identity outside the trusted issuer path", unverified high). Trace how the gateway mints the assertion for a Works query. Then run protected-HTTP controls that mutate the actor and workload under missing-binding and bound assertions. Do not change compatibility; record the result and the owning closure decision.

</frozen-after-approval>

## Implementation Notes

- `WorksSdkProtectedHttpTests.UnboundQueryAssertionsLeaveTheCallerNamedByTheEnvelope` drives the real protected SDK host (in-process Kestrel, real JWT workload assertion and Dapr channel token, Works query policy, in-memory SDK store). A cooperating Parties gateway confirms whichever binding each request names. Every refusal is checked to precede Works state reads. Bound refusals are also checked to precede Parties calls.
- Rows that return another caller's work record the open gap. Their assertion message names R29R-B1, and a fix must turn them into refusals.
- `PublicGatewayBindsWorksQueryAuthorityOnlyWhenTheDomainRequiresIt` drives the real public gateway controllers, authorization and `HandlerAwareQueryRouter` into the Works host. It observes the identity fields the gateway stamps and its `CallerAuthority` capture. The domain invocation is a test double that signs the outbound assertion with the query digest only when authority was captured; this mirrors `DaprDomainQueryInvoker`'s binding rule by code reading, but the Dapr transport itself is not exercised. `WorksGatewayHttpFixture.Build` gains an optional configuration overlay, and existing callers are unchanged.
- `docs/sdk-projection-query-adoption.md` now states the confirmed unbound-query limit. No production code changed.

## Verification

`dotnet …/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.WorksSdkProtectedHttpTests` passed **137/137** in Release on Works `64919bc` with the EventStore 5.5.1 packages. The worktree's EventStore submodule (`5257363c`, used only by Debug builds) was not involved.

| Scenario (valid gateway assertion and channel token) | Result |
| --- | --- |
| Unbound, own actor and party | Own queue (`mine`, `queue`) |
| **One unbound assertion replayed** with another human's actor, user and `partyId` | **That human's queue** (`foreign`, `queue`) |
| Same forgery on `get-work-item` for `foreign` | **Found** |
| Unbound, another tenant named, Parties confirms there | Authorized; the read reaches that tenant's projection (`projection-unavailable` because it is empty) |
| Unbound, `partyId` not bound to the named actor | `Forbidden` after the Parties check, with no state read |
| Unbound, non-delegated permitted coordinator workload (`scheduler`) | **All work**, without a Parties call |
| Unbound, unpermitted workload (`rogue`) | `Forbidden`, with no Parties call and no state read |
| Bound: actor forged, workload alone changed, or a coordinator envelope | `safe-denial`, with no Parties call and no state read |
| Digest claim present, `CallerAuthority` removed | `safe-denial` |
| Public gateway, default configuration | Stamps the authenticated caller as user and original actor, workload `eventstore`, delegated; **no `CallerAuthority`**, so the query goes out unbound |
| Public gateway, `RequiredQueries:work` lists `whats-next` | Captures `CallerAuthority` (with `query:read`); Works verifies the bound query and serves the caller |

**Conclusion.** R29R-B1 is confirmed. Canonical Works queries are unbound, and nothing in the assertion binds them to tenant, actor, party or body. No replay cache was found in the EventStore workload authentication. Any holder of one valid gateway `domain-service:query` assertion for Works plus the app channel token can therefore replay it, naming another tenant, actor and party that the directory confirms. The checks that still hold are the Parties binding match and the coordinator allow-list.

The coordinator exposure is off by default: `PermittedCoordinatorWorkloads` is empty and no composition sets it. No gateway path produces a coordinator query, since the public controller always stamps the original actor. A bound coordinator envelope fails binding verification, which requires `OriginalActorId` to equal `UserId`.

Unauthenticated access remains refused by existing tests: `Query_and_named_projection_require_channel_and_exact_operation_assertions`, `FrozenSdkRejectsHumanBearerOnAuthenticatedQueryIngressBeforeWorksStateAccess`, and the `unapproved-gateway` 401 in `ConfiguredServiceBearerAuthorizesProtectedExecutorQueriesWithSyntheticCurrentAuthority`. The closure decisions are recorded in `deferred-work.md`.

## Review Triage Log

- **Medium, patched.** The ledger was not updated. R29R-B1 is now resolved, and the closure items are new entries.
- **Medium, patched (scoped).** The mint-path test simulates the invoker's binding rule. The notes now say the Dapr transport is not exercised, and the test now asserts every gateway-stamped identity field.
- **Medium, patched.** Fresh assertions do not prove replay. One unbound assertion is now reused for the forged request.
- **Medium, patched.** The tenant was not forged. The forged-tenant row shows authorization passes and the read reaches the other tenant.
- **Low, patched.** `get-work-item` was not covered. A forged detail read is now a row, and the closure entry names both query types.
- **Medium, patched.** The bound rows did not isolate the digest. A workload-only change and a bound coordinator envelope are now rows.
- **Low, patched.** There were no negative controls. Rows now show that a mismatched party and an unpermitted workload are `Forbidden` before any state read.
- **Medium, patched.** The workload exposure was presented without its default-off context; the conclusion now gives it.
- **Low, patched.** Identity fields stamped by the gateway were not asserted; they are now.
- **Medium, patched.** The evidence was not tied to an EventStore version. The record now gives the Works commit and the package version; Release does not use the drifted submodule.
- **Low, patched.** "No unauthenticated path" now cites the existing tests that establish it.
- **Low, patched.** The gap rows gave no hint when they would need updating. Their assertion message now names R29R-B1.
- **Low, patched.** A failure inside the invoker surfaced as an opaque 500. The invoker now records the domain status, which is asserted after the gateway call.
- **Medium, patched.** The docs did not mention the limit. `docs/sdk-projection-query-adoption.md` now states it.
