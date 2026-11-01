---
title: Restore Folders HTTP fixture composition with current SDK authorization
type: bugfix
created: '2026-11-01'
status: done
route: dispatch
review_loop_iteration: 0
baseline_commit: 7d3966971edededd0812754e02b404793874f788
context:
  - /tmp/works-folders-catalog-operation-1793515230698427047/Folders/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-git-instructions.md
---

<frozen-after-approval reason="User explicitly included Folders CI repair and waived human validation">

## Intent

Current published SDK authorization exposes broken Folders test-host composition: 57 HTTP cases reach authorization-bearing endpoints without correctly ordered authorization middleware, and 11 routing cases supply a synthetic default identity that cannot satisfy the explicit Human policy. Repair these test fixtures so existing real handler, cross-surface, persistence and denial assertions execute through the intended authentication and authorization pipeline.

## Boundaries & Constraints

Always preserve original HTTP status/body, state, event, tenant, ACL, idempotency, audit and cross-surface assertions. Retain the actual shared gateway double's HTTP `/process` roundtrip and rejection propagation, existing domain gates, and real in-memory repository event application. Keep production Human requirements, workload/channel policies and SDK endpoint inventory unchanged. Distinguish explicitly isolated synthetic fixture authentication from production credential proof.

Never modify production source or bypass SDK policy/catalog guards to make fixtures green. Never replace HTTP processing with direct aggregate calls, fabricate caller authority, relax meaningful expectations, skip tests, add delays/timeouts/concurrency padding, change dependencies, or initialize/update nested modules. Existing parent changes and the pre-existing dirty Builds gitlink are excluded from implementation ownership.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Historical handlers | Existing authorized v1 request | Original success and persisted state/event assertions | No missing-middleware500 |
| Candidate handlers | Same task through candidate v2 seam | Original outcome and independent authority revalidation | No exception converted to false503 |
| Routing identity | Existing supplied tenant/principal and intended transport credential | Named Human policy and original routing/domain behavior | Keep unavailable/missing credentials denied |
| Denials | Existing ACL, tenant mismatch, malformed request, conflict and absent authority cases | Original opaque status/body, no forbidden effect | No broadened permission or changed assertion |
| Cross surface | REST, generated SDK, CLI and MCP share task/key | Original ledger, cumulative state and rejection parity | Preserve genuine `/process` boundary |

</frozen-after-approval>

## Code Map

Owner: `/tmp/works-folders-catalog-operation-1793515230698427047/Folders`, main baseline7d396697. Relevant guidance uses its owning repository configuration and ordinary allowed root AI Tools baseline. No skills under references.

Exact hosted CI41330384463 artifact13093657196 (SHA25640b66ae928b14b244ecfa8af4768515b65fd0f7a5904eae0e93ae61c64693828) is retained under root `.artifacts/upgrade-release-orchestration-20261101/folders-7d396697-results/`. `failure-summary.json` records all68 HTTP failures; original TRX includes the missing authorization-middleware exception.

- `tests/Hexalith.Folders.IntegrationTests/Authorization/ContextReadAuthorityTests.cs:492`, `MixedSurfaceHandoff/MixedSurfaceHandoffTests.cs:1083`, and `EndToEnd/GoldenLifecycleParityTests.cs:3810,4174`: four slim-host sites explicitly call UseRouting without subsequent explicit UseAuthorization. Add proper middleware ordering at these sites.
- `src/Hexalith.Folders.Server/Pd10V2CandidateCompatibilitySeam.cs:805-825`: converts downstream exceptions to AuthorityUnavailable503, explaining candidate failures; read only, no change.
- `tests/Hexalith.Folders.IntegrationTests/Routing/RoutingModeTestHost.cs:177-199`: default RoutingTest authentication does not satisfy explicitly Bearer-bound Human policy, and fixed-domain-identity branch lacks a transport identity. Prefer genuine signed fixture Bearer credentials from the existing recipe below; retain anonymous/invalid credential cases. Any alternative explicitly isolated scheme must keep every requirement from SDK CreateHumanPolicy and remain solely in test composition.
- `tests/Hexalith.Folders.Server.Tests/FoldersServerHostCompositionTests.cs:179-240,504-510`: working signed Bearer configuration/token recipe.
- `src/Hexalith.Folders.Testing/Hosting/FoldersServerTestHostServiceCollectionExtensions.cs`: existing synthetic Human requirements for isolated handlers; full SDK PostConfigure overrides earlier Configure, so do not assume it fixes full routing hosts.
- `tests/shared/Parity/InProcessRejectionPropagatingGatewayClient.cs:87-101` and `src/Hexalith.Folders/Aggregates/Folder/InMemoryFolderRepository.cs:133-174`: preserve actual process HTTP/rejection and event application/ledger assertions.
- Production `FoldersServerHostComposition.cs:104-139` already orders authentication/routing/authorization correctly; do not weaken or change it.

## Tasks & Acceptance

- [x] Correct all four isolated host middleware sites.
- [x] Repair routing test transport identity and callers using existing credential seams while retaining every original expected outcome.
- [x] Run the four affected test classes with supported xUnit assembly selectors and no unexplained skips; report every discovered downstream failure without silencing it.
- [x] Return exact owned paths, full patch, immutable source hashes and command/results receipt; update this spec's File List and matrix coverage.

Given the existing scenarios, when the repaired fixtures execute, then all original domain and persistence assertions remain and pass. Given unauthorized or mismatched input, when the same pipeline handles it, then original denials and no-effect assertions remain true. Given an unchanged production host, when inspecting the diff, then no production auth or routing guard has changed.

## Implementation Notes

No unresolved user-visible intent choices or irreversible action is needed. Parent owns Git/remote/release work and broader checks. Implement only in the owning Folders checkout and this spec. Parent currently owns six mechanical assertion files and regenerated pending conformance inventory; preserve them and do not include them as newly authored work. Preserve the existing dirty references/Hexalith.Builds pointer. In-process focused build/test window is open; no native/container lane. Use explicit finite Release/public-package verification with serialized builds; inspect owner build props for its package mode. Do not run solution-level dotnet test.

## Spec Change Log

- 2026-11-01: Corrected the two moved read-only source paths in the Code Map. Implemented explicit fixture authentication before the candidate seam and authorization after routing at all four sites. Routing callers now present signed fixture Bearer credentials through the unchanged production policy composition; removed the obsolete RoutingTest scheme.
- Initial middleware repair exposed 39 ContextReadAuthority 401 failures: automatic authentication cached the synthetic principal before the fixture installed its claims. Explicit authentication after fixture claim setup resolves the ordering issue without changing the helper or production code. The failed intermediate logs are retained.

## Review Triage Log

## Verification

Build the owning IntegrationTests project in Release/public-package mode, serialized with `-m:1`, and invoke its built xUnit assembly with single-dash `-class` selectors for ContextReadAuthorityTests, GoldenLifecycleParityTests, MixedSurfaceHandoffTests and routing tests. Keep all initial failed logs. Parent will execute complete affected project checks after return and require fresh hosted CI after commit. No in-process fixture result grants native/deployed/runtime acceptance.

## File List

All Folders paths below are relative to `/tmp/works-folders-catalog-operation-1793515230698427047/Folders`:

- `tests/Hexalith.Folders.IntegrationTests/Authorization/ContextReadAuthorityTests.cs` — explicit authentication after fixture claims, authorization after routing.
- `tests/Hexalith.Folders.IntegrationTests/EndToEnd/GoldenLifecycleParityTests.cs` — same ordering at both isolated hosts.
- `tests/Hexalith.Folders.IntegrationTests/MixedSurfaceHandoff/MixedSurfaceHandoffTests.cs` — same ordering at the shared four-surface host.
- `tests/Hexalith.Folders.IntegrationTests/Routing/RoutingModeTestHost.cs` — existing signed RSA Bearer recipe, static fixture issuer metadata, matching tenant/principal/consumer/permission claims and default authenticated client; both domain-identity branches use the same Bearer transport.
- `tests/Hexalith.Folders.IntegrationTests/Routing/FoldersApiRoutingModeTests.cs` — use the fixture's signed Bearer; add missing/invalid credential checks across v1/v2 with no tenant/read/gateway effects.
- `tests/Hexalith.Folders.IntegrationTests/Routing/RoutingModeAuthenticationHandler.cs` — deleted obsolete synthetic RoutingTest scheme.

This spec's completion and verification record is the only owned workspace documentation change. Production source, SDK policies/catalogs, dependency files, existing assertion bodies, the shared `/process` gateway and repository event application are unchanged. Parent-owned six mechanical assertion files, pending inventory and the pre-existing dirty Builds gitlink remain excluded.

## Verification Results

- `dotnet build tests/Hexalith.Folders.IntegrationTests/Hexalith.Folders.IntegrationTests.csproj --configuration Release -m:1 -p:UseHexalithProjectReferences=false -p:NuGetAudit=false` — passed with 0 warnings and 0 errors. Explicit public-package mode follows the owner's Release configuration.
- Built assembly execution used four repeated single-dash `-class` selectors: `Hexalith.Folders.IntegrationTests.Authorization.ContextReadAuthorityTests`, `Hexalith.Folders.IntegrationTests.EndToEnd.GoldenLifecycleParityTests`, `Hexalith.Folders.IntegrationTests.MixedSurfaceHandoff.MixedSurfaceHandoffTests`, and `Hexalith.Folders.IntegrationTests.Routing.FoldersApiRoutingModeTests`.
- Before: 170 total, 102 passed, 68 failed, 0 skipped/not-run. Intermediate: 171 total, 132 passed, 39 failed, 0 skipped/not-run. Final: **171 passed, 0 failed, 0 skipped/not-run**, including every original 170 case and the added credential check.
- Final per class: ContextReadAuthority 47/47; GoldenLifecycleParity 97/97; MixedSurfaceHandoff 8/8; FoldersApiRoutingMode 19/19.
- Focused `dotnet format whitespace ... --verify-no-changes --no-restore --include <five changed existing source files>` and `git diff --check` — passed.
- Retained the initial 68-failure log, intermediate 39-failure log, and first implementation compile error (incorrect RouteCatalogSurfacePolicy namespace, corrected before tests), alongside final successful outputs.
- Full patch: [owned.patch](../../.artifacts/upgrade-release-orchestration-20261101/folders-http-fixtures/owned.patch). SHA-256: `a6c44bce229827bc201d608a4413a31bf6bdf30456d8340f716c33e7786837a0`.
- [receipt.json](../../.artifacts/upgrade-release-orchestration-20261101/folders-http-fixtures/receipt.json) records exact commands/exit codes, baseline/current source SHA-256 values, unchanged production/gateway/repository hashes, package versions, artifact hashes and case-level results/coverage.

These are isolated in-process fixture results. Existing Development warnings for incomplete internal credentials and absent Dapr scratch-cleanup service remain visible in logs. They grant no native, deployed, container, external issuer or runtime acceptance. Parent owns complete affected-project checks, independent review and fresh hosted CI.

## Matrix Coverage

All cases below ran and passed in `tests-auth-ordered.xml`; the receipt expands each theory into its exact executed cases.

| Frozen matrix scenario | Executed coverage |
| --- | --- |
| Historical handlers | `GoldenLifecycleAllNineStepsDriveOverRealRestTransportToTransportTerminalClass`; `ProductionValidatorChecksTheDistinctSignedDelegator` (including both legacy cases). |
| Candidate handlers | `CandidateTranslatesChangeFileRequestDiscriminatorThroughTheRealSeam`; `SensitivityAwaitCannotReuseEarlierActorOrDelegatorAuthority` (both cases); candidate signed delegator cases. |
| Routing identity | `AuthenticationRunsBeforeTheSeamWithTheRealClaimsBasedAccessors` (Coexistence/V2Only); `CoexistenceRoutesV1AndV2`; `MissingAndInvalidBearerCredentialsDenyBeforeProtectedReads`. |
| Denials | `CrossSurfaceAclDeniedArchiveSurfacesParitySafeDenialOnEverySurface`; `CandidateAuthorizationEvaluatesEveryRepeatedClientIdentityValue` (all four mismatch cases); `CandidateRejectsAmbiguousHeadersMediaTypesAndDuplicateJsonBeforeDispatch`; conflicting idempotency case below. |
| Cross surface | `OneTaskMovesAcrossFourSurfacesPreservingIdentityAndStateAndCorrelationEchoOnEverySurface`; `SameIdempotencyKeyReplayedAcrossFourSurfacesProducesAggregateLedgerInvariantAndNoConflict`; `SameIdempotencyKeyWithConflictingPayloadAcrossFourSurfacesSurfacesCanonicalConflictAndAggregateLedgerInvariant`. |


## Parent verification and mechanical maintenance

The complete candidate also includes parent-owned stale assertion updates in four Contracts deployment test files, FoldersProductionAuthenticationTests and ScaffoldContractTests, matching the existing pinned CI producer, actual pipeline call, technical ServiceDefaults edge and .NET10.0.403. The canonical pending source inventory/allowlist is refreshed, removing the deleted test handler and including current required sources; production-disabled/pending approval markers remain. The pre-existing dirty Builds gitlink is excluded from ownership, staging and publication. Parent complete IntegrationTests executed854:850passed,4failed,0skip. All4failures are AspireTopologyTests requiring the absent direct nested Memories checkout. Local nested initialization is prohibited, and hosted CI initializes the standalone repository roots; retain this environment limitation and require fresh hosted CI. All68original HTTP failures and the new credential control passed.


## Review1 Triage Log

All three fresh layers returned final before triage. Edge returned empty; verification found no gaps. Each finding is classified before grouping. Patch-only route.

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | low | patch | The fixture now supplies Bearer on every created client, so the existing retired-mode alive check lost its original anonymous transport. Use a separate client with no Authorization header for that existing public liveness assertion. |
| blind-2 | low | reject | This fixture now consumes the unchanged genuine SDK Bearer validation and existing production authentication tests already exercise signed credential requirements. Additional issuer/audience/lifetime vectors duplicate unchanged framework validation; no new acceptance bug is demonstrated by the malformed-token control. |
| blind-3 | low | reject | The routing fixture no longer substitutes Human policy or its requirements. Existing real SDK Human credential-kind controls remain, and production hosting/auth controls already cover them. Adding mixed-claim vectors to this fixture duplicates unchanged enforcement without a demonstrated regression. |
| blind-4 | low | reject | The new zero-read assertion explicitly selects the claims-backed branch. The other fixture branch deliberately supplies fixed domain authority independently of HTTP identity; any pre-auth read there is an isolated fixture behavior, while endpoint authorization still refuses and no domain effect is admitted. No new production authority expansion was shown; do not redesign the fixed-authority fixture. |
| blind-5 | low | reject | All modes retain the same actual Human policy and credential validation, and existing mode behavior assertions remain. The only changed auth composition is fixture Bearer enrollment; additional all-mode credential combinations would mirror unchanged framework enforcement without a demonstrated ordinary failure. |
| blind-6 | low | reject | Actual mutation handlers retain unchanged named authorization and existing no-effect/domain-denial tests. The new credential control deliberately probes read access; its empty gateway assertion does not claim a complete mutation-security qualification. Additional mutation credential vectors would duplicate unchanged endpoint authorization, with no introduced bypass identified. |

## Completion Evidence

Fresh hosted CI41339714082 and architecture spine41339713924 passed at 0eabc23856fde13dd76cab5d7ac2cb45739df8c5. Release41341229270 succeeded for v1.3.3, and all five public packages passed independent source/identity/signature verification in `.artifacts/upgrade-release-orchestration-20261101/folders-1.3.3-public/verification.json`. The earlier four local missing-nested-source failures remain recorded; no nested checkout was initialized to mask them.
