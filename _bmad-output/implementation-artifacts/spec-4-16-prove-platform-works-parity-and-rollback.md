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

- `P/src/Hexalith.Platform.Composition/WorksPreviewModel.cs`, `P/apphost.cs` — source-only preview; reuse canonical SDK composition, Dapr mTLS and the public generated per-receiver secret app-channel token helper.
- `P/DaprComponents/`, new `P/config/works-production-profile.schema.json` and `P/src/Hexalith.Platform.Composition/WorksProductionProfile.cs` — trust, ACLs, admission and provider bindings.
- `W/tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs` — preserve retained fallback while selecting the actual Platform file host. `W/src/Hexalith.Works.AppHost/ProjectMetadataPaths.cs` resolves explicitly selected Works/SDK roots before immutable Aspire metadata defaults; sixteen focused cases preserve default paths, require valid explicit selections and refuse mixed consumer/actual-host/retained-host build configurations.
- `W/src/Hexalith.Works/Runtime/WorksHost.cs`, `W/tests/Hexalith.Works.IntegrationTests/WorksSdkProtectedHttpTests.cs` — let the canonical SDK return authenticated legacy-route absence in named mode; a custom 409 leaves current gateway legacy replay intents frozen and delays subsequent named delivery.
- `W/src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.Partitions.cs`, `W/tests/Hexalith.Works.IntegrationTests/WorksSdkPartitionedRebuildTests.cs` — consume public bounded partitioned rebuild and durable captured-owner identities across repeated generations.
- `W/references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Projections/ProjectionUpdateOrchestrator.LegacyReplay.cs` — prepared unpublished producer correction: keep named projection retries independent of an active legacy replay lease; retain the frozen original legacy intent.
- `W/references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Commands/TrustedEffectRouter.cs`, new `TrustedEffectRouterProjectionTests.cs` — preserve the released constructor, register durable target projection activation before trusted routing, and nudge after actor/retention completion without holding the receipt.
- `W/src/Hexalith.Works/Queries/WorksQueryPolicy.cs`, `W/tests/Hexalith.Works.IntegrationTests/WorksQueryPolicyDeadlineTests.cs` — bound current Parties authority resolution within the outer invocation budget, deny before reads on timeout and preserve caller cancellation.
- New `P/eng/verify-works-host.sh`, `P/docs/works-host-parity.md`, `P/tests/Hexalith.Platform.Works.Tests/` — executable ledger and lifecycle/security/restore proof.

## Tasks & Acceptance

**Execution:**
- [ ] `P/apphost.cs`, `P/src/Hexalith.Platform.Composition/WorksPreviewModel.cs` — verify public 3.124.0 artifacts, enable both SDK modes and bind exact consumers; preserve Agents composition.
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

The user approved the contract and continuing implementation on 2026-10-16: preserve Full Agents refusal for EXT-HOST-1, require LocalScaffold and independent complete Works parity/rollback. Preserve accepted 4.15 evidence, unrelated working changes and the retained rollback host. No Git staging, commits, pushes, branch changes, nested submodule work or external publication/deployment are authorized. Root owns final independent review and acceptance.

Story 4.15 remains accepted on public EventStore 3.123.0. All270 preserved evidence files still match their SHA-256 values at21:03UTC; the frozen 4.16 requirements also match. Story4.16 remains in progress with all execution tasks open. Story4.9 has not resumed and awaits4.16 acceptance.

Under the user's latest-version instruction, Works and Platform's authoritative Builds catalog select public EventStore3.124.0. All nine official indexes still report3.124.0 at21:46UTC, producer `0737c0befce5338e294a789092a0e223e8264a04`. Nine HTTP200 archives share that producer; official commit/tree qualification binds1,942 runtime/build source files, and independent archive/cache/Release-consumer comparisons match all nine SDK DLLs. Exact isolated source selection preserves the shared SDK checkout and unrelated work. Platform's306-entry central catalog, SDK/Toolkit compatibility and15 exception validators pass; historical Builds G4 qualification is not claimed.

Supporting current-public checks pass with zero skips: Platform92tests in each source Debug/package Release mode, Works162 focused public contract/query/rebuild/security cases,270 architecture cases,34 receiver-observation cases,16 retained-path/fleet-configuration cases and18 source/evidence-admission cases. Default Agents verification preserves exit1 `DependencyNotAvailable: EXT-HOST-1`; LocalScaffold exits0. These checks do not substitute for the live R1–R11 and complete rollback ledger.

Actual public3.124 R4 fails1/1, zero skips,283.125seconds: after two complete rebuilds, automatic post-Commit catch-up leaves selected epoch2/generation2 at owner3/global3 instead of committed owner4/global14. One diagnostic named dispatch advances it; the original test stays failed. The exact command/result is retained in `evidence/story-4-16/root-acceptance-2026-10-16/public-3.124.0/public-r4-blocker.json`.

Two producer corrections are prepared in separately versioned unpublished `3.124.1-story416-gateway` source. Named projection delivery now proceeds independently of a frozen active legacy replay lease. Trusted-effect routing now registers durable target activation before the actor call and nudges projection after actor/retention completion, preserving the released three-argument constructor and receipt response budget. The combined patch applies cleanly to the public producer. Latest-source SDK verification passes138/138 with zero skips; all six new receipt/replay regressions fail against independently qualified unchanged public runtime source. Candidate source correctly fails public admission for exactly the two changed runtime files.

Candidate full3 was cancelled after actual process maps exposed Release children behind Debug inventories; the fixture now refuses mixed configurations before startup. Matched Debug full4 failed initial projection/history equality. Full5's natural exact checkpoint gate then exposed three truly stale trusted-effect targets and led to the second producer correction. Full6 used both corrections and matched actual Debug binaries: all eleven exact owner/global heads converged naturally in one poll/116ms; both shared rebuilds, concurrent automatic catch-up, durable replay, operations and alert clearing passed. Eighteen persisted phases across four Platform starts reached a complete snapshot of678 Redis keys and1,211 runtime binary entries. Restore then failed with `A restored synthetic value differs from its backup.` The fact remains failed1/1, zero skips,560.690seconds; the retained host was not started and rollback is unaccepted.

A real isolated Redis reproduction shows unordered hash/set DUMP serialization can differ after RESTORE while every complete logical value matches. The restore verifier now retains original restoration bytes and independently validates full logical values/types, complete key inventory and absolute expiry. All15 focused real-Redis regressions pass with zero skips in55.449seconds, including complete stream consumer-group/pending state, exact hash-field deadlines and14 mutation refusals. Full7 then passed full store/configuration/Scheduler/binary restore and retained readiness, but failed strict post-start configuration equality before replay: retained fixture startup overwrote the captured pubsub ownership comment. A capture-once helper now preserves existing component bytes; two focused regressions pass with zero skips in0.092seconds. Full8 now passes1/1 with zero skips in603.330seconds: all19 persisted phases across four Platform starts and one retained start, complete677-key/1,211-binary restoration, exact post-start configuration, usable retained queries, original NoOp receipt replay, eleven-source convergence and no duplicate resume/cancellation/expiry effects. Independent actual mappings verify all four restored Debug entries and SDK Server with zero mismatches. All four final candidate live facts now pass with zero skips and unchanged consumer/host/runtime bytes: full8 restore603.330seconds, callback152.915seconds, historical131.962seconds and actual two-host HA95.223seconds. HA observes a real stale callback, removes the reminder and preserves one resume. The final canonical verifier passes all supporting preparation: Platform92tests per mode, Works Debug/Release builds,193Release supporting tests and18Python admission tests, all with zero skips. It then exits1 because the selected SDK differs from publicproducer0737c0b in exactly TrustedEffectRouter.cs and ProjectionUpdateOrchestrator.LegacyReplay.cs. Its earlier generic1.0source identity conflict was corrected by using the named3.124producer version; no Parties/Commons changes were made. The exact canonical command/result is in the root packet canonical-admission/final/result.json. The temporary inotify increase has been restored after all owned fleets stopped. Configuration/authority/certificate/key bytes, modes, offline Scheduler inventory and full runtime deployment restoration remain strict. Historical, callback and HA facts must run against the final candidate graph; prior candidate passes remain historical supporting evidence.

The unpublished candidate does not satisfy named-published-producer acceptance. Fresh complete published R1–R11 verification, full retained-host rollback and three independent review lenses remain required. The approved scope excludes publication; no current evidence permits marking4.16 done or starting4.9. Root and implementation-targets evidence packets preserve exact commands, failed attempts and scoped supporting results.

## Design Notes

The user resolved the Agents preservation contract; no intent question remains. Changes are reversible and use disposable synthetic resources. Reuse SDK recovery/defaults/disposition; retain complete stream, family/journal, reminder/catalog, process/disposition, receipt/audit/key state and compatible fleet for rollback.

## Spec Change Log

## Review Triage Log

## Verification

- `P/eng/verify-agents-host.sh` — preserve the resolved Full contract; never relabel unavailable qualification as passed.
- `P/eng/verify-agents-host.sh --mode LocalScaffold --artifacts-path <isolated-directory>` — checks/build pass.
- `P/eng/verify-works-host.sh` — all required rows/rollback pass, zero skips, exact evidence retained.
- Build/test projects individually; use built xUnit assemblies and single-dash filters; verify Debug/Release graphs.
