---
title: 'Migrate Works Hosting to the Platform Boundary'
type: 'feature'
created: '2026-09-22'
status: 'draft'
baseline_commit: '18e94d388ab0abd8be363b361d0b2fee762359e7'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Works still owns an AppHost, ServiceDefaults, and generic runtime plumbing. Platform has only an Agents scaffold, and EventStore lacks migration-critical seams.

**Approach:** Complete prerequisite Stories 4.10–4.16, verify their named EventStore artifacts, Works consumers, full Platform R1–R11 parity matrix, and rollback evidence, then simplify the Works executable and remove obsolete Works hosting projects.

## Boundaries & Constraints

**Always:** Retain the Works host until Stories 4.10–4.16 are accepted and every AD-20 R1–R11 row has an equivalent passing Platform test, producer API/version, Works consumer, proof command, and rollback evidence. Re-run the shared-rebuild capture-through-Commit, restart, redelivery, and security proofs at cutover. Follow each repository's required Hexalith baseline before editing. Platform now declares `references/Hexalith.AI.Tools` as a root submodule for that baseline.

**Never:** Duplicate platform plumbing in Works, weaken parity, count the Agents scaffold as Works proof, remove the transitional host early, or deploy preview Dapr hosting to production without AD-20's exception.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
| --- | --- | --- | --- |
| Migration gate | Any R1–R11 row unproved | Works host remains usable | Report missing proof; retain hosting |
| Shared rebuild | Concurrent delivery through Commit | Old generation readable; catch-up after Commit | No partial promotion; degrade readiness |
| Redelivery/restart | Duplicate event, callback, or crash | One logical effect; persisted convergence | Quarantine conflicts; retry safely |
| Unauthorized request | Wrong tenant, origin, purpose, or digest | No effect or disclosure | Fail closed |

</frozen-after-approval>

## Code Map

- `src/Hexalith.Works/Runtime/WorksHost.cs` — final canonical SDK composition after consumer Stories 4.14–4.15.
- `src/Hexalith.Works.AppHost/Program.cs`, `src/Hexalith.Works.ServiceDefaults/` — transitional assets.
- `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — prerequisite Story 4.16's full parity and rollback gate.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/` — update build, dependency, runtime, and kernel guards for the new boundary.
- `Hexalith.Works.slnx`, `aspire.config.json`, `global.json`, `docs/boundary-decision-record.md` — remove old topology references only at cutover.

## Prerequisite Story Index

| Story | Deliverable | AD-20 rows |
| --- | --- | --- |
| [4.10](spec-4-10-publish-eventstore-projection-delivery-and-rebuild-fence.md) | EventStore projection delivery and rebuild fence | R3–R4 producer |
| [4.13](spec-4-13-publish-eventstore-trusted-effect-submission.md) | EventStore trusted effect submission | R11 producer |
| [4.11](spec-4-11-publish-eventstore-typed-reminder-reconciliation.md) | EventStore typed reminders | R6 producer |
| [4.12](spec-4-12-publish-eventstore-checkpointed-process-and-recovery-runtime.md) | EventStore process and recovery runtime | R7–R8 producer |
| [4.14](spec-4-14-adopt-sdk-projection-and-query-seams-in-works.md) | Works projection and query consumers | R3–R5 consumer |
| [4.15](spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md) | Works reminder, process, and command consumers | R6–R8/R11 consumer |
| [4.16](spec-4-16-prove-platform-works-parity-and-rollback.md) | Platform topology, security, parity, and rollback | R1–R11 proof |

## Tasks & Acceptance

**Execution:**
- [ ] Stories 4.10–4.16 and AD-20 R1–R11 evidence — verify accepted producer versions/contracts, Works consumers, passing Platform commands, persisted end states, and rollback proof without skipped required lanes.
- [ ] `src/Hexalith.Works/Runtime/WorksHost.cs` — reduce the executable to canonical `AddEventStoreDomainService(...)` and `UseEventStoreDomainService()` composition after prerequisite consumers are live.
- [ ] `src/Hexalith.Works.AppHost/`, `src/Hexalith.Works.ServiceDefaults/`, `Hexalith.Works.slnx`, `aspire.config.json`, and dependent tests/docs — remove obsolete hosting only after the Platform gate passes; add a fitness check that rejects its reintroduction.

**Acceptance Criteria:**
- Given all R1–R11 rows have named producer versions, consumers, passing Platform commands, and rollback evidence, when the cutover is reviewed, then Works has no AppHost/Aspire/ServiceDefaults project and its executable uses the canonical SDK composition.
- Given a shared rebuild and concurrent event delivery, when inventory through Commit completes or fails, then readers observe only committed generations and subsequent delivery converges without lost writes.
- Given the Platform topology runs the migrated scenarios, when restart, redelivery, authorization, and recovery proofs execute, then persisted end states match or strengthen the historical Works lanes.
- Given architecture fitness tests run, when a Works-owned host or generic plumbing returns, then they fail with a clear boundary violation.

## Implementation Notes

2026-09-23 checkpoint (partial, cutover gate open):

- Works recovery readers now use the exclusive `FromSequence` cursor correctly and reject a foreign stream identity or non-advancing page. The existing Works AppHost and ServiceDefaults remain in place.
- EventStore has an opt-in shared-projection epoch API with durable delivery, generation selection, catch-up, control-index discovery, and retry/parking behavior. Large stage manifests, capture maps, and ordinary delivery payloads use digest-checked chunks; a persisted test stages and promotes 10,000 aggregate mutations. Works does not enable or consume this API. `StreamReadPageValidator` is available as a shared contract, but recovery readers have not been migrated to it.
- Platform has a development-only, explicitly enabled Works topology. All 11 declared resources reached healthy state in a local run; this is topology evidence only. Platform's existing Agents verification still passes. The Dapr mTLS helper is in its own included C# file.
- R4 remains open: ordinary delivery is still on Works' bespoke `/project`; the full Works baseline and generation-selected query/recovery readers are not implemented. Captured positions stay unacknowledged while Building or Aborting so an aborted rebuild can receive them again. A pending chunk reservation requires source redelivery after a crash, oversized fold-prepared mutations fail closed, and chunk tombstone retention/offboarding needs further proof.
- R2-R3 and R5-R11 remain unproved; producer contracts, Works consumers, Platform parity commands, and rollback evidence are missing where applicable. No `eng/verify-works-host.sh` or complete R1-R11 matrix exists. No hosting project has been removed.
- The new EventStore contracts have no published producer version, and Works' Release package dependency does not expose them. The local EventStore source tests establish a development seam only; they cannot turn an AD-20 row green without a named artifact, a Works consumer, a Platform proof command, and rollback evidence.
- The EventStore control-index tombstone carries an audit identifier; external authorization, audit-sink evidence, retention approval, and restore drill remain outside this partial API.

## Spec Change Log

- 2026-09-23: Human chose to split the migration into prerequisite Stories 4.10–4.16. Story 4.9 now owns the final cutover and host removal; its full AD-20 R1–R11 gate remains required. Returned this spec to draft and sprint backlog for review.

## Review Triage Log

## Design Notes

AD-20 makes host removal the last operation. Preserve the prior composition for rollback. EventStore already exposes query, projection, read-model, and shared-rebuild contracts; add the missing runtime mechanics.

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx -p:NuGetAudit=false -m:1` and `dotnet build Hexalith.Works.slnx -c Release --no-restore -m:1` — clean restore/build after cutover.
- `tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests` — boundary guard and existing fitness tests pass.
- `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — R1–R11 conformance and rollback checks pass with no skipped required lane.

**Partial checks run on 2026-09-23:** Works Release solution restore/build passed with zero warnings and errors. Works recovery reader classes passed 18/18 tests, and the existing ArchitectureTests runner passed 268/268 tests. EventStore Client projection suite passed 167/167 tests after the chunked durability changes, DomainService fenced integration passed 2/2, and Contracts page-validator tests passed 9/9. EventStore test-project builds reported existing MSB3277 dependency-version warnings. Platform AppHost Release build and `eng/verify-agents-host.sh` passed after separating the mTLS helper; `aspire describe` showed all 11 opt-in Works resources healthy before `aspire stop`. These checks do not satisfy the cutover verification commands above.
