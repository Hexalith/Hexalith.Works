---
title: 'Prove Platform Works Parity and Rollback'
type: 'feature'
created: '2026-09-23'
updated: '2026-10-16'
status: 'in-progress'
baseline_commit: '11628b7198e79def65f43f3c8cdf876184417830'
platform_baseline_commit: 'b2890d20575356239555df5b113e3100c1d594c1'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '_bmad-output/implementation-artifacts/epic-4-context.md'
  - '_bmad-output/implementation-artifacts/evidence/story-4-16/planning-2026-10-16/implementation-map.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Platform has Works topology evidence, but AD-20 R1–R11 lack executable target-host parity and rollback proof.

**Approach:** Complete Platform composition, health, telemetry, operations and security; run historical scenarios through its verifier with named published producers and exact Works consumers. Story 4.9 removes the old host only after acceptance.

## Boundaries & Constraints

**Always:** Preserve the Agents contract. The user approved on 2026-10-16: keep default Full verification refusing unavailable EXT-HOST-1, require green LocalScaffold, and require complete independent Works parity/rollback for 4.16 acceptance. Full Agents qualification is not a 4.16 prerequisite. Bind every row to producer/API/version/hash, consumer, actual Platform command, persisted outcome, negative case and rollback. Keep source Debug/package Release and explicit test-only Platform.Identity provenance. Require AD-24 mTLS, deny ACLs, broker/network policy and validated production configuration.

**Never:** Count old-host tests, scaffold builds, absent seams or skipped lanes as parity. Retain Works AppHost/ServiceDefaults. Preview hosting stays development/test without the Maintainer's time-bounded exception. Preserve synthetic scope; AD-28 approval/restore gates remain before real-data/new-catalog admission. No external deployment/publication or invented authority.

## I/O & Edge-Case Matrix

| Scenario | Expected behavior |
| --- | --- |
| Startup/restart | SDK readiness/telemetry and persisted recovery converge. |
| Historical scenarios | Lifecycle, tree, claim/query, reminders/processes and fenced rebuild preserve durable outcomes. |
| Wrong origin/policy | Wrong port/app/trust/tenant/purpose/sequence/publish and missing configuration refuse before effect/disclosure. |
| Complete fleet rollback | Restore matching store/configuration/binaries; retained host queries/replay converge without duplicate effects. |

</frozen-after-approval>

## Code Map

`P/` means `/home/administrator/projects/hexalith/platform/`; `W/` means this Works repository. The context implementation map expands exact fixtures and row evidence.

- `P/src/Hexalith.Platform.Composition/WorksPreviewModel.cs`, `P/apphost.cs` — source-only preview; reuse canonical SDK composition and Dapr mTLS helpers.
- `P/DaprComponents/`, new `P/config/works-production-profile.schema.json` and `P/src/Hexalith.Platform.Composition/WorksProductionProfile.cs` — trust, ACLs, admission and provider bindings.
- `W/tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs` — hardcoded old host; preserve fallback while selecting actual Platform file host.
- New `P/eng/verify-works-host.sh`, `P/docs/works-host-parity.md`, `P/tests/Hexalith.Platform.Works.Tests/` — executable ledger and lifecycle/security/restore proof.

## Tasks & Acceptance

**Execution:**
- [ ] `P/apphost.cs`, `P/src/Hexalith.Platform.Composition/WorksPreviewModel.cs` — verify public 3.123.0 artifacts, enable both SDK modes and bind exact consumers; preserve Agents composition.
- [ ] `P/DaprComponents/` and profile paths above — validate required trust/caller, broker/network, secrets/delegation, Scheduler/state/audit configuration; test refusal branches.
- [ ] `P/src/Hexalith.Platform.Composition/WorksPreviewModel.cs`, `P/tests/Hexalith.Platform.Works.Tests/` — SDK readiness/metrics/alerts and scoped disposition; durable audit precedes mutation and outage refuses.
- [ ] `W/tests/Hexalith.Works.IntegrationTests/` and Platform tests — reuse fixtures under actual Platform; execute redelivery, HA/lost firing, pages/reissue, capture-through-Commit and receipt replay without skips.
- [ ] `P/eng/verify-works-host.sh`, `P/docs/works-host-parity.md` — bind R1–R11 artifacts/outcomes and complete Platform-write→restore→old-host replay. R9 proves SDK consumption; final simplification stays in 4.9.
- [ ] This spec, evidence and sprint — record resolved contract, exact results and owner-decision scope; accept only complete parity and rollback.

**Acceptance Criteria:**
- Given named published producers and Works consumers, when Platform verification runs, then all applicable R1–R11 rows pass with persisted, negative, restart and rollback evidence and zero skips.
- Given unauthorized traffic or missing production configuration, when startup/requests occur, then admission fails closed before effect/disclosure.
- Given Platform writes and complete rollback, when the prior Works host resumes, then usable queries and replay converge without duplicate logical effects.

## Implementation Notes

User approved the contract and continuing implementation on 2026-10-16. Preserve accepted 4.15 work and evidence, historical immutable archives, unrelated dirty submodules and the retained rollback host; no Git staging, commits, pushes, branch changes, nested submodule work or dependency changes beyond the approved scope. Root owns final independent review/acceptance. Exact required Hexalith baseline and repository guidance apply in each owning repository.

4.15 is accepted on public 3.123.0. The preview still selects legacy modes and liveness probes. Preparatory inline pin alignment now passes eight checks and LocalScaffold; Full retains the recorded unavailable dependency. All execution tasks remain open. Historical eleven-resource health is not parity. See context for exact commands/results.

## Design Notes

The user resolved the Agents preservation contract; no intent question remains. Changes are reversible and use disposable synthetic resources. Reuse SDK recovery/defaults/disposition; retain complete stream, family/journal, reminder/catalog, process/disposition, receipt/audit/key state and compatible fleet for rollback.

## Spec Change Log

## Review Triage Log

## Verification

- `P/eng/verify-agents-host.sh` — preserve the resolved Full contract; never relabel unavailable qualification as passed.
- `P/eng/verify-agents-host.sh --mode LocalScaffold --artifacts-path <isolated-directory>` — checks/build pass.
- `P/eng/verify-works-host.sh` — all required rows/rollback pass, zero skips, exact evidence retained.
- Build/test projects individually; use built xUnit assemblies and single-dash filters; verify Debug/Release graphs.
