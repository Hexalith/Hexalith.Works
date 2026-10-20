---
title: 'Migrate Works Hosting to the Platform Boundary'
type: 'feature'
created: '2026-09-22'
status: 'in-progress'
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
- `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — canonical executable parity and complete-fleet rollback gate, now supplied by accepted Story 4.16.
- `src/Hexalith.Works/Runtime/WorksHost.cs` and `src/Hexalith.Works/Program.cs` — retain domain registrations; after 4.14–4.15, remove bespoke `/project`, Dapr, reminder, and recovery composition and use the canonical SDK path.
- `src/Hexalith.Works.AppHost/`, `src/Hexalith.Works.ServiceDefaults/`, `Hexalith.Works.slnx`, `aspire.config.json`, `global.json` — transitional topology and its registrations; keep until the Platform gate passes.
- `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj` and `WorksAppHost*` tests — replace old-host dependencies after Platform proof.
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/` — replace assertions requiring AppHost/ServiceDefaults in build, dependency, runtime, and kernel guards with rejection of Works-owned hosting and generic plumbing.
- `docs/boundary-decision-record.md` — record the accepted cutover, Platform proof, and rollback path.

## Tasks & Acceptance

**Execution:**
- [x] `_bmad-output/implementation-artifacts/sprint-status.yaml` and `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — require accepted 4.10–4.16 and every AD-20 row's producer contract/version, Works consumer, passing Platform command, persisted end state, and rollback proof before removal.
- [x] `src/Hexalith.Works/Runtime/WorksHost.cs` and `src/Hexalith.Works/Program.cs` — retain Works domain registrations and reduce composition to the canonical `AddEventStoreDomainService(...)` / `UseEventStoreDomainService()` SDK path after consumers are live.
- [x] `src/Hexalith.Works.AppHost/`, `src/Hexalith.Works.ServiceDefaults/`, `Hexalith.Works.slnx`, `aspire.config.json`, and `global.json` — remove the Works-owned topology and its references after the gate passes.
- [ ] `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj`, `tests/Hexalith.Works.ArchitectureTests/FitnessTests/`, and `docs/boundary-decision-record.md` — replace old-host test dependencies with Platform evidence, add a regression guard against Works-owned hosting, and record rollback.

**Acceptance Criteria:**
- Given all R1–R11 rows have named producer versions, consumers, passing Platform commands, and rollback evidence, when the cutover is reviewed, then Works has no AppHost/Aspire/ServiceDefaults project and its executable uses the canonical SDK composition.
- Given a shared rebuild and concurrent event delivery, when inventory through Commit completes or fails, then readers observe only committed generations and subsequent delivery converges without lost writes.
- Given the Platform topology runs the migrated scenarios, when restart, redelivery, authorization, and recovery proofs execute, then persisted end states match or strengthen the historical Works lanes.
- Given architecture fitness tests run, when a Works-owned host or generic plumbing returns, then they fail with a clear boundary violation.

## Implementation Notes

**Current, 2026-10-20:** Accepted prerequisite Stories 4.10–4.16 and the complete pre-cutover Platform R1–R11 gate unblocked implementation. The pre-gate passed all 44 proof kinds and complete-fleet rollback before removal. Works now has the canonical SDK host, domain adapters and regression guards; Platform owns the topology and HTTP/subscription defaults. The first post-cutover attempt failed at historical root discovery and a separate callbacks startup timeout; the corrected archive and unchanged callback fact have passed focused live validation with strict runtime byte identity. A fresh full post-gate is running from an immutable, explicitly bound Platform source export. Final acceptance and the fourth execution task remain pending. See the current Verification entry, `docs/platform-hosting-cutover.md`, and the checked-in `evidence/story-4-9/verification/` command/result and replacement audits.

The 2026-10-16 blocked state below is superseded historical evidence. At that time, 4.15/4.16 were not accepted and the executable verifier was absent; no host assets were removed in that attempt. The current cutover began only after acceptance and a new passing full pre-gate. The [2026-09-23 evidence archive](story-4-9-pre-cutover-evidence.md) also remains historical partial work, not current parity evidence.

## Spec Change Log

- 2026-09-23: Human chose to split the migration into prerequisite Stories 4.10–4.16. Story 4.9 now owns the final cutover and host removal; its full AD-20 R1–R11 gate remains required. Returned this spec to draft and sprint backlog for review.

## Review Triage Log

## Verification

**Historical 2026-10-16 prerequisite audit (superseded by the 2026-10-20 entry):**

- `rg -n '^  4-(9|10|11|12|13|14|15|16)-' _bmad-output/implementation-artifacts/sprint-status.yaml` — exit 0; 4.10–4.14 are `done`, 4.15 is `in-progress`, 4.16 is `backlog`, and 4.9 remains `in-progress`.
- `rg -n '^status:|^- \[[ x]\]' _bmad-output/implementation-artifacts/spec-4-15-adopt-sdk-reminder-process-and-command-seams-in-works.md _bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md` — exit 0; 4.15 is `in-progress` with its eight execution tasks checked; 4.16 is `draft` with all three execution tasks unchecked. Checked implementation tasks do not establish prerequisite acceptance or Platform parity.
- `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — exit 127: `/bin/bash: line 1: /home/administrator/projects/hexalith/platform/eng/verify-works-host.sh: No such file or directory`.
- `references/Hexalith.Platform/eng/verify-works-host.sh` — exit 127: `/bin/bash: line 1: references/Hexalith.Platform/eng/verify-works-host.sh: No such file or directory`.

No cutover checks or live parity lanes ran in that historical attempt: its prerequisite gate failed before implementation. The commands below describe the required checks; current results are recorded in the 2026-10-20 entry.

**Commands:**
- `dotnet restore Hexalith.Works.slnx -p:Configuration=Debug -p:UseHexalithProjectReferences=true -m:1` and `dotnet build Hexalith.Works.slnx -c Debug --no-restore -p:UseHexalithProjectReferences=true -m:1` — local source-backed build after cutover.
- `tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests` — boundary guard and existing fitness tests pass.
- `/home/administrator/projects/hexalith/platform/eng/verify-works-host.sh` — R1–R11 conformance and rollback checks pass with no skipped required lane.


**2026-10-20 cutover execution (post-gate pending):**

- Accepted prerequisites 4.10–4.16 and all eleven AD-20 rows were re-audited. The pre-cutover canonical verifier passed before host removal: public EventStore 3.126.0; 112 Platform source tests, 112 package tests, 257 supporting consumer tests, four recovery facts, historical lifecycle/security, two-host HA and focused shared rebuild, all with zero skips. All 44 persisted/negative/restart/rollback proofs passed and complete-fleet rollback was observed. The immutable packet is `evidence/story-4-9/pre-cutover-2026-10-20/`.
- Works now composes `AddEventStoreDomainService` / `UseEventStoreDomainService`, with SDK-only projection/reminder/process defaults and domain adapters. Active AppHost/ServiceDefaults projects, topology configuration and local event/reminder/cascade mechanics were removed only after that pre-gate passed. `docs/platform-hosting-cutover.md` records Platform ownership and the byte-bound archived topology rollback command.
- Current deterministic checks pass: unit 589/589, property 3/3 (300 generated cases), architecture 270/270, integration 728 executed facts, zero failures or skips. Six explicit parity facts plus a separate archived-host readiness preflight are assigned to the canonical verifier; it explicitly enables and requires them. New Platform-owned RFC 9457/declarative-subscription checks pass in the dedicated 114-test Works suite; Python admission/archive/isolation/snapshot checks pass 84/84. Retired implementation fixtures map to named passing replacements in `evidence/story-4-9/verification/retired-test-replacements.json`.
- The first post-cutover verifier in `/tmp/story49-post-cutover-20261020` is preserved as nonqualifying: all preparatory checks and 19 persisted writer/restart/rebuild/snapshot phases passed, but the historical bundle omitted the unchanged solution marker required by root discovery; callbacks-only startup separately reached its 180-second bound with EventStore sidecar health Unhealthy. The corrected 32-file bundle has passed actual retained readiness (1/1, 32.585 s), unchanged source and deployment hashes, and a new omission refusal test. Its preflight evidence is separate and cannot qualify as rollback. The final export-based focus also passed retained readiness (1/1, 46.783 s) and the unchanged callback-only fact (1/1, 161.057 s), with all six callback outcomes and unchanged loaded runtime/fleet/archive/export/launcher bytes. The subsequent full attempt in `/tmp/story49-post-cutover-20261020-attempt2` failed on filesystem ENOSPC during recovery and final save; its exact partial streams and zero-byte ledger/XML are preserved. A fresh complete gate is running under `/mnt/wsl/story49-gate-20261020/artifacts`, after stable executable local-filesystem/Docker-bind, free-space, inode and memory checks; acceptance remains pending.
- Original failures remain preserved separately: two pre-gate transient fixture failures and shared-output/foreign-port startup failures; candidate compatibility fixture failures corrected without weakening HTTP 403/no-access assertions; a broader unrelated Platform Composition compile failure against the fixed older Builds export; default shared output assembly conflict. Run-owned isolated source builds and the required fresh default restore pass. No dependencies, foreign fleets, Git history, staging or branches were changed by this implementation.

- The final immutable Platform export selects full revision `4f036ced45aa40700948c19949bb874881c960b5` plus exact working overlays, with 632 ordinary inputs and selected root catalog/dependency binding. Both host and consumer runtime build from that export. Complete export bytes and current relevant source/fixture/helper/config inputs remain strict; audited unexecuted recovery-contract/EXT-SECRETS-1 tool and Secrets runtime test boundaries record observed drift. New imports/Compile references into them change bound helpers/projects and refuse, as tested. Externally authored Platform commits were preserved.

- The required legacy mTLS fact was migrated to owned Redis/Scheduler/certificates and the existing synthetic SDK configuration before it could use shared defaults. Its focused execution passed 1/1 (36.468 s), with the original HTTP 403 assertion, unchanged complete family/checkpoint and no disclosure; selection/isolation contracts passed 24/24. All seven live entry points were audited for owned setup. The final ordinary suite now passes 728 facts with seven explicit facts not run (735 discovered), zero failures/skips, 78.998 s. The running complete gate is `/mnt/wsl/story49-gate-final-20261020-attempt2`. Generated debugger history is never source-exported; only its name/hash is preserved. The prior source snapshot is retained, and the new selected binding preserves every compiled input and physical path. Final acceptance remains pending.
