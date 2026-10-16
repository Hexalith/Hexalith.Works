# Story 4.16 implementation map

Supporting investigation only. The main 4.16 spec owns intent, acceptance, status and the pending Agents-contract decision.

## Code Map

Platform paths use `/home/administrator/projects/hexalith/platform/`; Works paths use this repository.

- Platform `apphost.cs`, `src/Hexalith.Platform.Composition/WorksPreviewModel.cs` — actual file host, source-only preview and SDK flags; reuse canonical `AddHexalithEventStore`, module composition and `DaprSelfHostedMtls`.
- Platform `DaprComponents/`, new `config/works-production-profile.schema.json`, new `src/Hexalith.Platform.Composition/WorksProductionProfile.cs` — current permissive EventStore/Admin ACLs, local stores, trust and startup admission.
- Platform `eng/verify-agents-host.sh`, `eng/verify-platform-pins.py`, `docs/ext-host-1-agents-composition.md` — authoritative mode/pin contracts; preserve closed Full behavior.
- Platform new `eng/verify-works-host.sh`, `docs/works-host-parity.md`, `tests/Hexalith.Platform.Works.Tests/` — lifecycle/profile validation, row ledger and target execution.
- Works `tests/Hexalith.Works.IntegrationTests/WorksAppHostSmokeHarness.cs`, `WorksAppHost*Tests.cs` — extract host selection; remove hardcoded old-host creation from reused fixtures without deleting fallback.
- Works `WorksSdkRecoveryPipelineSmokeTests.cs`, `WorksSdkProjectionQueryTests.cs`, `WorkItemSharedProjectionRebuildHandlerTests.cs`, `WorksSdkProtectedHttpTests.cs`, `run-sdk-rollback-restore.sh` — exact persisted baseline and security/rebuild scenarios.

## Tasks & Acceptance

**Execution:**
- [ ] Platform `apphost.cs`, `WorksPreviewModel.cs` — verify published EventStore 3.123.0 graph, enable both SDK modes and configure exact consumer identities; retain default Agents composition.
- [ ] Platform profile files and `DaprComponents/` — validate trust, named callers, broker/network policy, secret/delegation, Scheduler/state/audit bindings and production refusal; test missing/foreign inputs.
- [ ] Platform `WorksPreviewModel.cs`, `tests/Hexalith.Platform.Works.Tests/` — use SDK readiness/defaults, export bounded recovery telemetry/alerts and authorized disposition with durable audit-before-mutation; audit failure refuses.
- [ ] Works harness/test paths above plus Platform test fixture — reuse domain scenarios through the actual Platform file host, with owned synthetic profiles, complete runtime hashes and no required skips.
- [ ] Platform `eng/verify-works-host.sh`, `docs/works-host-parity.md` — execute R1–R11, bind artifacts and persisted/negative/restart evidence; R9 proves SDK consumption while final simplification stays in 4.9.
- [ ] Platform tests and Works restore fixtures — prove HA/lost firing, pages/reissue, capture-through-Commit, redelivery, receipt replay and complete Platform-write→restore→old-host replay rollback.
- [ ] Works story/evidence/sprint and Platform parity ledger — record owner-decision scope, every exact result and blocker; accept only complete passing Works parity plus the resolved Agents preservation contract.

**Acceptance Criteria:**
- Given named published producers and Works consumers, when the Platform verifier runs, then all applicable R1–R11 rows have passing persisted end-state, negative, restart and rollback evidence without skips.
- Given unauthorized traffic or missing production security configuration, when Platform starts/receives it, then it fails closed before effect or disclosure.
- Given Platform writes followed by a complete rollback drill, when the prior Works host resumes, then usable queries and replay converge without duplicate logical effects.

