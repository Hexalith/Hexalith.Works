---
title: 'Close Story 4.8 review gaps'
type: 'bugfix'
created: '2026-09-16'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '06d64b007b01f01c34b7796a49a83c96bf243263'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Six open review findings leave Story 4.8 with stale operator documentation, unpinned multi-tenant and cancellation behavior, missing recovery-path scheduling diagnostics, and a reserved-tenant guard that validates unrelated envelope fields.

**Approach:** Apply the narrow production fixes, add deterministic regression coverage at each affected boundary, and reconcile the authoritative documentation and story evidence without widening reminder or identity policy.

## Boundaries & Constraints

**Always:** Preserve stream-as-truth reminder discovery, deterministic reminder identity, partial-result reconciliation, and exception propagation for at-least-once retry. Attribute cancellation to the caller only when both the caller token is canceled and the caught `OperationCanceledException` carries that exact token. Keep 4609 fields bounded while attaching the original exception through the structured exception slot.

**Never:** Add unpark/replay behavior, periodic reconciliation, a new exhaustion policy, durable key or contract changes, general envelope identity validation for the fourteen ordinary adapters, kernel/Reactor changes, submodule updates, topology changes, or a live reminder-lane rerun.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Multi-tenant parking | Two tenants each contain parked reminder candidates | Counts aggregate across tenants and remain visible on clean/incomplete outcomes | Parked streams stay unread and non-retryable |
| Foreign cancellation | Tenant-index, parking, or stream dependency throws with a non-caller token while the caller token becomes canceled | Failure is classified at the correct boundary | Return typed incomplete-scan evidence; do not misreport caller cancellation |
| Recovery scheduling fault | A future await cannot be scheduled during startup reconciliation | Emit bounded Warning 4609 with tenant, item, deterministic name, reason type, and attached exception | Rethrow the original exception; exact caller cancellation emits no warning |
| Malformed unrelated envelope field | Ordinary command uses a non-reserved tenant with malformed domain or aggregate id | Reserved-tenant guard does not validate unrelated fields | Preserve ordinary kernel delegation; `LinkConversation` retains explicit identity checks |
| Parked aggregate operations | A terminally parked aggregate still has a date-await index entry | Documentation states that its reminder will not recover until separate operator remediation exists | 4605/4607/4608/4609 guidance gives operators a concrete response |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs` -- three cancellation filters and cross-tenant parked-skip accumulation; keep the scan/result model unchanged.
- `src/Hexalith.Works/Reminders/DateReminderReconciler.cs`, `src/Hexalith.Works/Runtime/WorksRecoveryLog.cs` -- mirror the established steady-state 4609 catch/log/rethrow policy; no new event definition is needed.
- `src/Hexalith.Works/WorkItemEventStoreAggregate.cs` -- make the shared reserved-id guard normalize only `CommandEnvelope.TenantId`; preserve `LinkConversation` checks.
- `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs` -- pin cross-tenant addition and all three foreign-cancellation classifications using existing stores/loggers.
- `tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs`, `LinkConversationRuntimeAdapterTests.cs` -- cover recovery scheduling diagnostics/cancellation and tenant-only envelope guarding.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/SubscriberDeadLetterOperatorDocumentationTests.cs` -- govern the terminal parked-reminder consequence and actionable 4605/4607/4608/4609 responses.
- `docs/boundary-decision-record.md`, `docs/eventstore-api-surface-constraints.md`, `docs/operations/subscriber-dead-letter-operator.md` -- replace stale index/catalog/adapter statements and document parked-reminder operational consequences plus EventIds 4605/4607/4608/4609.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs`, `tests/Hexalith.Works.IntegrationTests/IndexedPendingDateAwaitSourceTests.cs` -- require exact-token cancellation attribution and prove tenant, parking, and stream failures plus cross-tenant parked-count aggregation.
- [x] `src/Hexalith.Works/Reminders/DateReminderReconciler.cs`, `tests/Hexalith.Works.IntegrationTests/DateReminderRecoveryRuntimeTests.cs` -- add recovery-path 4609 parity and pin ordinary, caller-canceled, and foreign-canceled scheduler failures.
- [x] `src/Hexalith.Works/WorkItemEventStoreAggregate.cs`, `tests/Hexalith.Works.IntegrationTests/LinkConversationRuntimeAdapterTests.cs` -- remove full-identity construction from the reserved-id helper and prove malformed unrelated envelope fields do not change ordinary kernel results.
- [x] `docs/boundary-decision-record.md`, `docs/eventstore-api-surface-constraints.md`, `docs/operations/subscriber-dead-letter-operator.md` -- document current full-replay history gating, catalog 40, all-envelope-aware adapter signatures, terminal parking impact, and operator actions for 4605/4607/4608/4609.
- [x] `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md`, `_bmad-output/implementation-artifacts/tests/test-summary.md` -- close the six findings and record only verification produced by this run.

**Acceptance Criteria:**
- Given parked candidates across multiple tenants, when discovery completes or reports partial failure, then the total parked count is exact and no parked stream is read.
- Given a dependency cancellation not carrying the caller token, when the caller token is also canceled, then the failure remains a tenant/candidate incomplete-scan result with the correct log classification.
- Given recovery scheduling fails, when reconciliation processes a future await, then Warning 4609 contains bounded identity/reason fields, attaches the cause, and the original exception remains retryable.
- Given an ordinary non-reserved command envelope has malformed unrelated identity fields, when reflection dispatch invokes the adapter, then the reserved-id guard does not throw `ArgumentException` and kernel behavior is preserved.
- Given an operator reads the authoritative docs, when an aggregate is parked or reminder recovery warns, then the permanent reminder consequence and next action are explicit and current.

## Implementation Notes

- All three discovery cancellation filters now treat an `OperationCanceledException` as caller cancellation only
  when the caller token is canceled and the exception carries that exact token. Foreign cancellations remain
  typed incomplete-scan failures at the tenant-index, parking, or stream boundary. When caller cancellation races
  a foreign failure, discovery stops at that boundary so a later iteration cannot mask the typed evidence.
- Recovery scheduling mirrors steady-state scheduling: deterministic reminder identity is computed before the
  call, non-caller failures emit bounded Warning 4609 with the original structured exception, and the original
  failure is rethrown. Exact caller cancellation remains warning-free, including through the hosted startup
  service; foreign cancellation is not silently classified as shutdown.
- The shared reserved-tenant helper normalizes only `CommandEnvelope.TenantId`; the fourteen ordinary adapters
  no longer construct `AggregateIdentity` or validate unrelated envelope fields. `LinkConversation` keeps its
  explicit full-identity validation.
- Authoritative docs now describe full-replay history gating, catalog 40 / Story 4.8 delta zero, all fifteen
  envelope-aware wrappers, and operator actions for 4604 through 4609. They distinguish recovery's inability to
  repair a missing parked reminder from an already-durable reminder that may still fire. An architecture fitness
  fact governs that distinction and an actionable response in each warning row.

## Spec Change Log

- 2026-09-16 — Review patches preserved typed incomplete-scan evidence across cancellation races, enforced
  exact-token shutdown attribution in the hosted service, covered both partial-scan scheduling branches, and
  corrected the registry-ordering and operator-warning documentation. Frozen spec content was not changed.
- 2026-09-16 — Matrix-audit follow-up added an architecture fitness test for the parked-reminder operator
  contract and refreshed Architecture totals. Frozen spec content was not changed.
- 2026-09-16 — Implemented all five execution tasks and reconciled the six Story 4.8 review findings. No frozen
  intent, constraint, or matrix entry changed.

## Review Triage Log

- No new scope was accepted during implementation. Periodic reconciliation, replay/unpark, scheduler exhaustion,
  durable contracts, topology, submodules, and the live reminder lane remain outside this patch.

| Finding | Verdict | Evidence | Route |
|---|---|---|---|
| BH-1 | medium | After a foreign tenant-index cancellation is classified, a concurrently canceled caller token reaches the next tenant's `ThrowIfCancellationRequested` outside the catch and replaces the typed incomplete result with raw caller cancellation. | patch |
| BH-2 | medium | After a foreign parking or stream cancellation is classified, scanning another candidate with the now-canceled caller token can likewise replace the accumulated candidate failure with raw caller cancellation. | patch |
| BH-3 | medium | `ReminderReconciliationService` treats any `OperationCanceledException` as shutdown whenever its stopping token is canceled, even when the exception carries a different token, so it bypasses failure telemetry and retry handling. | patch |
| BH-4 | low | The changed incomplete-scan filter correctly lets exact caller cancellation escape, but no test combines an incomplete scan with that scheduling cancellation, leaving this shutdown branch unpinned. | patch |
| BH-5 | medium | No test combines incomplete scan evidence with a foreign scheduler cancellation; reverting the changed outer filter would leak the raw foreign cancellation while all current tests remained green. | patch |
| BH-6 | low | A deliberately constructor-bypassed or `with`-mutated envelope can now carry a malformed tenant through an ordinary adapter, but normal construction and HTTP deserialization validate the tenant and the approved change intentionally limits this helper to reserved-id guarding. The scenario is unlikely and independent validation would add policy outside this narrow helper. | rejected |
| BH-7 | false | The active follow-up spec is intentionally `in-review` while the parent story and sprint remain `in-progress`; final synchronization occurs only after review acceptance, so this is workflow transition state rather than contradictory completion state. | rejected |
| BH-8 | low | The documentation's unconditional registry-before-index guarantee does not describe an initially absent cleared-history tombstone, which can write a non-actionable index watermark without registering the tenant. | patch |
| BH-9 | medium | Parking blocks recovery from repairing a missing reminder but does not remove a durable reminder registered earlier by the steady-state handler; the new permanent-consequence wording can mislead operators about an existing reminder that may still fire. | patch |
| BH-10 | low | EventId 4605 is emitted before partial results are processed, and a later submit or schedule failure can stop that processing, so the runbook must say partial results are attempted rather than already processed. | patch |
| BH-11 | low | The new 4605 response sends operators to 4604 and 4606 without defining either warning or its concrete response anywhere in the runbook. | patch |
| BH-12 | false | The architecture test selects each exact EventId row and asserts the matrix-required terminal-reminder and actionable-response terms; hypothetical contradictory prose is not present, and the additional 4604/4606 gap is already captured separately. | rejected |
| EC-1 | medium | Independent tracing confirms both cross-tenant and same-tenant continuation can replace an already-classified foreign failure with raw caller cancellation after the caller token becomes canceled. | patch |
| EC-2 | medium | Independent tracing confirms the hosted service's cancellation filter checks only stopping-token state and can silently classify a foreign scheduler cancellation as shutdown. | patch |
| VG-1 | medium | The verification-gap review demonstrated that no test covers incomplete scan plus foreign scheduling cancellation, so typed scan counts and both causes can regress without a failing test. | patch |

## Verification

**Commands:**
- `aspire start --non-interactive -- --EnableKeycloak=false`, `aspire wait works --non-interactive`, `aspire describe --non-interactive`, then `aspire stop --non-interactive` -- expected: record the required pre-change topology baseline honestly; no live reminder credit.
- `DOTNET_CLI_HOME=/tmp dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1 -v minimal && DOTNET_CLI_HOME=/tmp dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1 -v minimal` -- expected: zero warnings and errors.
- `for test_class in IndexedPendingDateAwaitSourceTests DateReminderRecoveryRuntimeTests LinkConversationRuntimeAdapterTests; do tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class "*${test_class}" || exit; done` -- expected: all focused deterministic facts pass.
- `tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests -class "*ReminderReconciliationServiceTests"` and the focused operator-documentation Architecture class -- expected: all review regressions pass.
- Run UnitTests, PropertyTests, deterministic IntegrationTests (`-class- "*SmokeTests"`), and ArchitectureTests as direct Release binaries -- expected: no new failures; isolate and report the pre-existing SDK-pin assertion if it remains.

**Results (2026-09-16):**

- Pre-change Aspire baseline: `aspire start` succeeded; `aspire wait works` did not reach readiness because
  `dapr-sentry` exited and dependent resources remained waiting; `aspire describe` captured that state and
  `aspire stop` completed. This run receives no live reminder credit.
- Solution restore succeeded. Release solution build succeeded with 0 warnings and 0 errors.
- Focused deterministic classes passed 102/102: `IndexedPendingDateAwaitSourceTests` 23/23,
  `DateReminderRecoveryRuntimeTests` 13/13, and `LinkConversationRuntimeAdapterTests` 66/66.
- Review regression coverage passed: `ReminderReconciliationServiceTests` 3/3 and
  `SubscriberDeadLetterOperatorDocumentationTests` 3/3.
- Direct Release suites passed: Unit 568/568, Property 3/3, and deterministic non-smoke Integration 416/416.
- Architecture passed 237/238; the sole failure is the pre-existing SDK-pin mismatch (`10.0.400` expected,
  checked-in `global.json` pins `10.0.401`). Excluding exactly that assertion passed 237/237.
