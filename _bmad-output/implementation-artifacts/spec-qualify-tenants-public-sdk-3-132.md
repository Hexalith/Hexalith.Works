---
title: Qualify Tenants against the released EventStore3.132 cohort
type: bugfix
created: '2026-11-02'
status: done
route: dispatch
review_loop_iteration: 0
baseline_commit: 228418fbd4ef34200d4f1d0ded6f0934f7e30ddf
context:
  - /home/administrator/projects/hexalith/works/references/Hexalith.Tenants/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-state-instructions.md
---

<frozen-after-approval reason="User authorized latest stable packages and CI repairs preserving authorization; human checkpoints waived">

## Intent

Adopt verified public EventStore3.132.0 without changing Tenants authorization or runtime behavior. Reconcile fourteen legacy gateway fixtures with truthful declared operations and the existing internal bootstrap transport, preserving their meaningful success, denial, routing, subject and rejection assertions.

## Boundaries & Constraints

Use only the official fourteen-package cohort from producer bb1e93c57180c7aeb6093564608738ddd1a7c28e after strict public verification. Preserve explicit/source/legacy selections, finite stable aliases/refusals, frozen Testing.Integration3.128.4 and the source-only runtime cohort flag. Preserve current API30-second standard resilience, retries/circuit breaker and exact credential forwarding. Bootstrap remains internal; creator pre-send target refusal remains.

Never grant public bootstrap, relabel a human JWT as service authority, fabricate catalog bindings/private headers, weaken meaningful assertions, skip failures, alter proof/time limits, edit SDK/peer pointers, mutate historical evidence or start native/container resources. Local transport mocks are not native qualification.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Failure handling |
|---|---|---|---|
| Default | No override | Verified3.132 public APIs/declarations | One SDK cohort |
| Explicit/aliases | Supported old/source selection;3.132.0/.0/valid metadata | Exact selected identity | Existing publication restrictions |
| Invalid version/archive | Prerelease/nonzero revision/unknown/mixed producer | Existing refusal | No qualification |
| Public JWT/RBAC | Actual UpdateTenant route | Original202, subject/tenant/permission and routing assertions | Original negative controls |
| Internal bootstrap | Actual signed bound-workload request | Original409 conflict mapping or422 wrong-target rejection | No administrator grant |
| Forged internal input | Tampered proof/wrong caller/domain/missing channel | Refusal before effects | Creator wrong-target still sends nothing |

</frozen-after-approval>

## Code Map

Owner Works references/Hexalith.Tenants, clean local main at baseline, one unpublished npm-only child of pushed3a32. Parent owns Git/review/releases and native CI. Read tracked build/editor/auth/catalog guidance. No intent gaps/irreversible implementation; version gates and fixture transport warrant dispatch. Do not load skills under references or rerender the active workflow.

Gate: Works `.artifacts/upgrade-release-orchestration-20261101/sdk-3.132.0-public/verification.json` must show version3.132.0, commit above, signatureVerificationExitCode0 and14 packages. Initial propagation404 is not acceptance. SDK release41373402892 succeeded; qualified source/native checks remain distinct. SDK source in `/tmp/works-sdk-compat-qualification-20261101` is read-only.

- `Directory.Build.props`, `Directory.Build.targets`, `.github/workflows/ci.yml`: default/current public lane3.132.0; admit only exact stable normalization and applicable technical HTTP capability. Keep runtime cohort flag source-only and explicit floor3.129.0 lane.
- `tests/Hexalith.Tenants.Contracts.Tests/PackageGovernanceTests.cs`, `scripts/verify-catalog-package-consumer.py`, `tests/scripts/test_catalog_package_consumer.py`: extend finite positive identity/cohort controls; retain every negative and provenance assertion.
- `tests/Hexalith.Tenants.IntegrationTests/CommandApiRuntimeIntegrationTests.cs`: fourteen baseline failures reproduce identically with public3.131 on untouchedca93; receipt `/tmp/tenants-gateway-transport-20261102/verification.json`. Eleven JWT/tenant/raw-sub/permission positives use wrong-domain BootstrapGlobalAdmin. Reuse existing CreateUpdateTenantRequest; adjust exact permission to UpdateTenant and mocked status identity truthfully. Preserve every existing success/routing/auth assertion and negative case.
- Its domain-conflict mapping case must retain GlobalAdminAlreadyBootstrappedRejection,409,title/detail/reason through proper internal bootstrap. Its two AuthenticatedBootstrapCannotAssignDifferentTarget cases must retain422, validated actor, InsufficientPermissionsRejection and absence of GlobalAdministratorSet, including ordinal case mismatch.
- `Fixtures/TenantGatewayTestHost.cs` owns real gateway controllers/auth/SubmitCommandHandler with only external routing/persistence doubled. Extend test-only seams for genuine `/api/v1/commands/bound/workload`; keep ordinary public fixtures/authority intact. Separate new helper types into individual files.
- Reuse generic SDK BoundWorkloadCommandProof.Issue and BoundWorkloadCommandRequest with paired RSA keys. Bind PrincipalId to cryptographically validated humanJWTsub; its name remains untrusted. Issue a separate real IWorkloadAssertionIssuer assertion: azp=tenants, GatewayCommandSubmit, service surface and valid bounded lifetime. Configure actual APP_API_TOKEN/dapr-api-token, Authentication:DaprInternal audience/caller, trusted issuer/key, BoundCommand:AllowedDomains:tenants=global-administrators and BoundStreamRead:PublicKeys:tenants. The SDK verifies proof/caller/domain and stamps verified origin, UserId and AuthenticatedSubject itself. Generic Issue deliberately permits semantically wrong target; keep TenantBootstrapBoundCommand.CreateAsync's earlier local refusal. The real aggregate then rejects target!=actor; no business permission is added.
- README/current catalog docs and a new qualification record: state measured package evidence/limits; preserve dated3.131 evidence.

## Tasks & Acceptance

- [x] Verify public cohort gate; update default, finite qualification and current CI/profile documentation.
- [x] Repair only the fourteen malformed/obsolete fixture transports while preserving their original meaningful outcomes and approved authorization.
- [x] Run every matrix control, governance/package consumer tests, original command/runtime and controller inventory; return complete owned diff/hashes, commands/counts and blockers.

Given verified binaries, normal builds discover the real catalog. Given invalid/mixed inputs, existing refusal remains. Given authentic internal wrong-target bootstrap, the original real-domain rejection and zero administrator grant remain; invalid authority never reaches protected work.

## Implementation Notes

No Git/remotes or peer updates. Notify parent before substantial compilation. Use serialized Release/public-package builds with explicit workspace Builds root and existing source flags false. Run individual built xUnit assemblies with single-dash selectors; retain failed attempts. Parent retains prior87 gateway/controller controls, three independent reviews and fresh exact-head hosted/native gates.

## Spec Change Log

## Review Triage Log

## Verification

Require original165 command-runtime cases,66 generated-controller cases, current gateway controls, governance and Python consumer positives/refusals with zero skips. Run existing bootstrap creator/bound/transport/hosted controls and new proof/channel/caller/domain negatives. Pack actual Contracts+Server into a fresh private output and verify real producer/SDK discovery. No public/native acceptance from isolated mocks.

### Parent verification — 2026-11-02

Parent read the full owned source/configuration/test diff, audited the qualification data against real public package receipts, verified all14 final file hashes, and independently parsed default176, controllers66, gateway46, governance380 and bootstrap288 passing xUnit cases. Additional parent execution exposed two real frozen3.128.4 token-shape assertion failures; retained failed XML/logs remain in `.artifacts/tenants-sdk31320-parent-20261102`. Same owner repaired only the added assertion: require service surface for catalog profiles and actual absent surface for the authentic frozen issuer, preserving every actor/rejection/zero-grant assertion. Full runtime rechecks independently parsed: legacy173, explicitfloor176, default176, all passing with zero skips; default3.132 outputs restored. Final current review diff `/tmp/tenants-sdk132-parent-review-20261102/review-final.diff`, SHA256a05868b11af2080191dadc9dc464527a8dbf34a8a3f64b1e5dcd692e2a51a3be. Review not launched yet. Incoming upstream2d5bd163 only records SDK0ace; preserve it on eventual owning rebase.

### Independent review triage — 2026-11-02

Three fresh reviewers were launched together and all finalized before this triage. Edge returned no findings. Every other finding receives its own verdict before grouping.

| Finding | Verdict | Evidence and route |
|---|---|---|
| Blind1: published matrix omits runtime/bootstrap controls | medium | Matrix currently executes generated controllers/owner catalog only; full class is in reusable Aspire lane after frozen3.128.4 selection. Manual current/floor runs do not prevent future current-package regressions. Patch recurring current/floor bound/actor/conflict controls with positive minimum counts and skip refusal. |
| Blind2: correctly addressed public bootstrap prohibition untested | medium | Migrated public positives now correctly exercise UpdateTenant; old remaining Bootstrap requests use wrong domain or missing permissions. Generated controller404 is a different route. Add direct generic public endpoint negative with otherwise sufficient human claims and true global-administrators addressing; preserve actual existing authorization and report any contradiction rather than change it. |
| Blind3: legitimate internal matching-target success untested here | medium | New internal cases reach409/conflict or real422/wrong-target; neither proves the empty-state real aggregate can emit its intended administrator event through this receiver. Patch one matching-subject success through the same boundary with real aggregate event and actor assertions. |
| Blind4: no additional consumer RSA wrong-key case | low | This change supplies no custom proof verifier. BoundWorkloadCommandProof.Verify delegates to BoundStreamReadProof.Verify; SDK/controller tests exercise signed exact scope and verify the cryptographic primitive. New fixture has real matching keypair, not a bypass, and negative malformed/principal controls. Reject duplicated SDK crypto-harness complexity; no new consumer verification branch is owned here. |
| Blind5: no additional consumer proof freshness controls | low | Proof issuance uses unchanged SDK Issue with UtcNow; SDK verification owns lifetime/skew. No clock or proof limits changed. Reject duplicated SDK freshness harness/branches rather than invent new authority or expiry policy. |
| Blind6: no additional workload JWT wrong-key/expiry controls | low | Real installed DaprInternal handler is used without changes. SDK InvalidInternalCredential_IsDeniedWithBoundedTelemetryAndNoWork explicitly includes wrong-key, expired, long-lived, wrong-issuer and malformed. Human-signature controls test the new fixture's distinct responsibility. Reject additional duplicate signer/clock hooks for unchanged SDK behavior. |
| Blind7: no additional workload JWT wrong-audience/operation controls | low | Same SDK handler has wrong-audience/wrong-operation controls and distinct per-route policies; fixture calls actual issuer for eventstore/GatewayCommandSubmit. Reject duplicate configurable JWT generation/hook complexity; no new receiver auth implementation. |
| Blind8: consumer exact-proof binding lacks payload/aggregate/message mutation cases | low | SDK WorkloadGatewayControllerTests.Bound_command_requires_exact_signed_requester_and_domain already mutates principal/domain/payload/extensions and the shared proof Scope includes message/aggregate identity. Consumer adds no scope converter. Reject duplicated SDK guard matrix complexity. |
| Blind9: independent fixture does not compose production request producer | medium | Producer has separately verified body/proof/channel serialization and native prior-source evidence, but current public fixture independently rebuilds those credentials. Patch a current isolated composition using actual TenantBootstrapBoundCommand.CreateAsync and the real receiver, preserving proof/serialized body and only doubling Dapr delivery. No production authorization/early wrong-target check changes. |
| Blind10: provenance hash command not explicit | low | Parent reproduced7f7413… with git diff --full-index HEAD; ordinary diff is a05868… due abbreviated indices. Direct documentation correction: record exact full-index protocol, public manifest/verifier commands and their local-candidate limits. |
| Verify1: current-SDK bound bootstrap recurring CI gap | medium | Preverified gap: default package lane selects only command-status/project patterns; full class uses frozen topology. Patch exact affected methods with minimum counts/skip refusal, independently retaining Blind1. |

Survivors grouped after verdicts: recurring current/floor published coverage (Blind1/Verify1), direct public-bootstrap refusal (Blind2), legitimate internal aggregate success (Blind3), production-producer/receiver composition (Blind9), and reproducible provenance commands (Blind10). These are small tests/configuration/docs patches on existing surfaces. Generic SDK-owned crypto/lifetime duplication findings are rejected as low; they do not create deferred work. No spec/intent loopback or second review loop is required.

### Parent final source qualification — 2026-11-02

The same implementation owner applied all small review corrections; no new review loop. Parent verified the complete source/configuration patch and all14 frozen file hashes. Parent full runtime after applicable modern guard: legacy174/174, floor178/178, default178/178, zero failures/skips; exact original legacy173 and modern176 expanded case multisets preserved, with only applicable added controls. Default3.132 outputs restored. Evidence `.artifacts/tenants-sdk31320-parent-20261102/review-final-applicable/verification.json`. Previously verified controllers66/gateway46/governance380/bootstrap288 remain source-identical; CI current/floor explicitly executes13+2+1 bound/actor/conflict controls with skip refusal. The unsuccessful175/174/1 legacy run is retained and is not acceptance. Only the newly added catalog-specific public negative is compiled out of the unsupported frozen catalog profile; its exact403/no-effects assertions remain required in both supported modern lanes. Normal source commit5da5670e records all14 paths after pinned21.2.4 candidate/range validation, atop npm-only228418fb. Source implementation is complete; exact-head hosted/native/public release gates and later mechanical latest SDK3.132.1 adoption remain orchestration work.
