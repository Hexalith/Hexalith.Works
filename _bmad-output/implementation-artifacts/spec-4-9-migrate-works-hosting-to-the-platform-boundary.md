---
title: 'Migrate Works Hosting to the Platform Boundary'
type: 'feature'
created: '2026-09-22'
status: 'ready-for-dev'
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

- `_bmad-output/planning-artifacts/architecture.md` (AD-20) and `_bmad-output/implementation-artifacts/sprint-status.yaml` — parity gate and story status.
- `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — Story 4.16 must supply the executable parity and rollback gate; currently absent.
- `src/Hexalith.Works/Runtime/WorksHost.cs` and `src/Hexalith.Works/Program.cs` — retain domain registrations; after 4.14–4.15, remove bespoke `/project`, Dapr, reminder, and recovery composition and use the canonical SDK path.
- `src/Hexalith.Works.AppHost/`, `src/Hexalith.Works.ServiceDefaults/`, `Hexalith.Works.slnx`, `aspire.config.json`, `global.json` — transitional topology and its registrations; keep until the Platform gate passes.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` and `WorksAppHost*` tests — replace old-host dependencies after Platform proof.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/` — replace assertions requiring AppHost/ServiceDefaults in build, dependency, runtime, and kernel guards with rejection of Works-owned hosting and generic plumbing.
- `docs/boundary-decision-record.md` — record the accepted cutover, Platform proof, and rollback path.

## Tasks & Acceptance

**Execution:**
- [ ] `_bmad-output/implementation-artifacts/sprint-status.yaml` and `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — require accepted 4.10–4.16 and every AD-20 row's producer contract/version, Works consumer, passing Platform command, persisted end state, and rollback proof before removal.
- [ ] `src/Hexalith.Works/Runtime/WorksHost.cs` and `src/Hexalith.Works/Program.cs` — retain Works domain registrations and reduce composition to the canonical `AddEventStoreDomainService(...)` / `UseEventStoreDomainService()` SDK path after consumers are live.
- [ ] `src/Hexalith.Works.AppHost/`, `src/Hexalith.Works.ServiceDefaults/`, `Hexalith.Works.slnx`, `aspire.config.json`, and `global.json` — remove the Works-owned topology and its references after the gate passes.
- [ ] `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj`, `tests/Hexalith.Works.ArchitectureTests/FitnessTests/`, and `docs/boundary-decision-record.md` — replace old-host test dependencies with Platform evidence, add a regression guard against Works-owned hosting, and record rollback.

**Acceptance Criteria:**
- Given all R1–R11 rows have named producer versions, consumers, passing Platform commands, and rollback evidence, when the cutover is reviewed, then Works has no AppHost/Aspire/ServiceDefaults project and its executable uses the canonical SDK composition.
- Given a shared rebuild and concurrent event delivery, when inventory through Commit completes or fails, then readers observe only committed generations and subsequent delivery converges without lost writes.
- Given the Platform topology runs the migrated scenarios, when restart, redelivery, authorization, and recovery proofs execute, then persisted end states match or strengthen the historical Works lanes.
- Given architecture fitness tests run, when a Works-owned host or generic plumbing returns, then they fail with a clear boundary violation.

## Implementation Notes

Cutover gate remains open: Stories 4.10–4.16 are backlog drafts and Platform has no `eng/verify-works-host.sh`. See the [2026-09-23 evidence archive](story-4-9-pre-cutover-evidence.md) for the earlier partial implementation and checks.

## Spec Change Log

- 2026-09-23: Human chose to split the migration into prerequisite Stories 4.10–4.16. Story 4.9 now owns the final cutover and host removal; its full AD-20 R1–R11 gate remains required. Returned this spec to draft and sprint backlog for review.

## Review Triage Log

## Verification

**Commands:**
- `dotnet restore Hexalith.Works.slnx -p:Configuration=Debug -p:UseHexalithProjectReferences=true -m:1` and `dotnet build Hexalith.Works.slnx -c Debug --no-restore -p:UseHexalithProjectReferences=true -m:1` — local source-backed build after cutover.
- `tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests` — boundary guard and existing fitness tests pass.
- `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — R1–R11 conformance and rollback checks pass with no skipped required lane.
