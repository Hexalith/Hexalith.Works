---
title: Preserve safe domain-service terminal refusals under committed catalog guards
type: bugfix
created: '2026-11-01'
status: done
route: dispatch
review_loop_iteration: 1
baseline_commit: 7ecd55c52d83f5950af7d6cf5d0ba9bc5d1e12cc
context:
  - /tmp/works-sdk-compat-qualification-20261101/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-state-instructions.md
---

<frozen-after-approval reason="User authorized SDK upgrades and CI repairs preserving authorization; temporary GitHub freeze was removed externally before this work">

## Intent

Strict catalog guards currently prevent two legitimate non-dispatch outcomes: an authenticated unknown-domain legacy projection404 and a module's typed rejection of an intentionally unsupported command. Preserve those terminal refusals without inventing executable capabilities or admitting any undeclared operation.

## Boundaries & Constraints

Always require real existing transport authorization, committed SDK authority, host identity, schema-three service surface and exact root/epoch/activation binding before any new refusal callback. Require the committed local process domain and tenant scope before a command refusal. Keep known-domain projection ownership/tenant/output checks unchanged. A terminal refusal never invokes the endpoint delegate, existing accepting admission chain, processor, reservation, protected state read, or successful event/state writer. Retain response-execution lease lifetime and disposal on every error/cancellation.

The public callback contract is finite, explicitly registered for exact domain/wire-command pairs, and can return only no-opinion or one typed rejection event. It exposes no acceptance, success event, no-op execution, arbitrary IResult, mutable live request, state, or administrator/source authority. No-opinion and missing rules always continue the unchanged normal guards. Trusted module rule code is not sandboxed; document its restricted refusal-only responsibility and permit its existing sanitized denial auditing, not precommit mutation.

Limit the new command path to ordinary implicit-V1 requests. Source-proof, version-aware, complete-source-required and asserted-admin requests retain existing precedence and validation. Bound detached request data and rejection serialization before allocation using existing SDK budgets/serializer profiles. No unbounded FromDomainResult allocation followed by a size check.

Never relax catalog trust, create fake routes/handlers for missing operations, alter ordinary successful dispatch, suppress tests, change timeouts/retries, mutate historical artifacts, initialize/update nested peers or touch unrelated resources.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Unknown local legacy domain | Valid transport/current generation; no local owned domain |404, no delegate/writes | Lease drains |
| Known named-only domain | Existing valid or invalid scoped metadata | Existing404 or503 respectively | No broad early404 |
| Explicit refusal | Exact registered unsupported command; valid local domain/tenant/current V1 authority | Bounded typed HTTP200 rejection; no dispatch/reservation/state effect | Lease held through response |
| No matching refusal | Unregistered command or NoOpinion | Original exact Resolve/admission/target path | Undeclared operation remains503 |
| Invalid authority | Anonymous/invalid transport, Pending/no generation, stale/duplicate bindings, wrong host/domain/tenant/surface | Original401/403/503 before callback | No effects, leases drain |
| Bad callback/result | Throw/cancel, null/empty/oversized/malformed rejection, attempted input mutation | Fail closed or propagate cancellation | No delegate or leaked lease |
| Nonordinary request | Source/admin/version-aware mode | Original validation precedence | No refusal escape |

</frozen-after-approval>

## Code Map

Owner `/tmp/works-sdk-compat-qualification-20261101`, HEADaaf9be1583acc55dee417242e523dc275d22d66b after normally merging current main1965ac06. Keep baseline frontmatter7ecd55 unchanged. Parent npm cleanup is already committed; seven pre-existing dirty physical peer gitlinks must never be staged, moved or initialized. Never load skills under references. Read tracked guidance/configuration before edits.

KEEP source, tests, docs, per-file hashes and exact receipts are preserved under Works `.artifacts/upgrade-release-orchestration-20261101/sdk-terminal-review1/keep-before-contract-correction/`. The complete `sdk-terminal-refusal-owned.patch` has SHA2560a945ad7cb645a0ebc187078b4c073d3a046294abaeada3c100630d788c4c3f0. Parent reverted only that owned patch before merging main. Reapply the preserved working implementation as the starting point, then derive the corrections below. Preserve every authority, V1/known-selection, old guard signature/direct-call, options, remote-ambiguity, private producer, serializer, ordinary serialization and no-effect control. Its latest99 focused cases pass; earlier parent919-routing/1319-domain checks predate review fixes and remain historical.

- DomainService `DomainRouteCatalogGuard`, filter and decision: retain existing `IDomainRouteCatalogGuard.AcquireAsync` lease signature, including Parties friend-source compatibility. Only the SDK filter uses the private decision path. NoOpinion/unavailable refusal capability retains normal guards. Known command target/tenant/version/admission/step-up checks precede refusal; unknown commands never become executable.
- ServiceDefaults `CommittedRouteCatalogSnapshot`, `RouteCatalogInvocationHandler`, `RouteCatalogPeerBinding`: the existing codec computes legitimate complete activation-v1 digests with zero TechnicalHttpReceivers. Retain that digest internally on a snapshot constructed from actual full activation. ServiceDefaults already friends DomainService. Supply an optional SDK-owned binding header for ordinary process transport; match its exact singleton value privately for refusal eligibility. Never change codec preimages, fake technical membership, ordinary PeerBinding.Matches or public v2 readbacks. `RouteCatalogAuthority.ReadbackAsync` and Aspire `DevelopmentRouteCatalogCustody` require legacy AttemptId/ActivationBindingDigest to remain null.
- `DomainServiceTerminalRefusalContext`: add an ordinal immutable Extensions data snapshot. Preflight a finite128-entry ceiling and total UTF-8 key/value + existing scalar/payload bytes before copying, using the existing configured command budget. Preserve spelling, null/empty/malformed values and case ambiguity; do not normalize or treat data as caller authority. Retain immutable payload isolation.
- Registration/rule: retain pure singleton delegate API and private bounded serializer/producer. Add one typed generic overload accepting Func<TService,Context,CancellationToken,Task<Refusal>>; resolve an explicitly registered TService from current request scope only after authority/scope/binding/budget checks and inside the remote-call scope. Expose no public IServiceProvider, accepting admission stage or mutable request. Missing service fails closed. Keep finite exact pair/length/duplicate rules across both overloads and ordinary producers untouched.
- Existing public3.131.0 `DevelopmentDomainRouteCatalog.cs` demonstrates actual FiniteRouteCatalogReceiver capture/Observe and compiled executable-owner assembly hashes. New finite HTTP controls must use genuine observed schema3 ordinary receivers, zero technical membership, and SDK sender headers. Missing proof must not unlock callbacks; old ordinary behavior remains available. A synthetic technical inventory is forbidden.
- ChatBot is read-only consumer motivation: it needs surfaceOrigin/actorType/serviceClientId spelling/ambiguity and safe taskId for existing denial/audit semantics; its resolver/pipeline are scoped. Existing unsupported ExecuteAdminQueueOperation is deliberately not executable. Do not edit ChatBot here or use unpublished APIs as accepted public-package proof.

## Tasks & Acceptance

- [x] Preserve unknown-local-domain legacy404 while retaining all known-owner scope checks.
- [x] Add the smallest finite typed refusal contract, exact registration and detached bounded context, with one C# type per file.
- [x] Integrate its terminal result only after global/local scope checks and before exact command resolution; retain normal path on no-opinion and all nonordinary modes.
- [x] Exercise every matrix row with real committed authority/transport, bounded response and lease/effect assertions; retain original success, denial and guard-substitute tests.
- [x] Document the narrow trusted callback contract; return complete owned patch/hashes and exact focused commands/results. Parent owns broader verification, reviews, Git and release.

Given current committed authorization, when an exact refusal is returned, then only a bounded terminal rejection executes and no undeclared dispatch becomes possible. Given missing/stale authority or no opinion, when processing, then existing guards retain their outcome. Given unknown local projection domain, when no receiver owns it, then404 retains its original non-dispatch meaning.

## Implementation Notes

No Git mutations/remotes, SDK publication, broad20-project/239-case runs, native/container starts or peer checkout updates. Read-only Git inspection is permitted. Notify parent before substantial compilation: another owner currently uses a separate ChatBot build window. Finite serialized Release/public-package source verification is authorized; label it separately from source Debug and deployed/runtime acceptance. Generate synthetic credential-looking test values at runtime to preserve the unchanged SDK secret scanner. If actual public serializer/budget seams cannot satisfy the bounded contract, report the concrete missing seam rather than fabricating evidence.

## Spec Change Log

- Parent acceptance bad_spec correction1: the initial plan confused technical-v2 digest exposure with complete activation binding, omits extension data required by the actual consumer, and retains only singleton callback lifetime. Non-frozen Code Map now uses the existing complete-v1 preimage internally, bounded immutable extension data, and a typed scoped callback overload. Frozen intent is unchanged. KEEP all working review corrections and controls from the preserved16-file patch, private bounded producer isolation, old guard compatibility, original legacy404 behavior, complete ordinary-event serialization controls, and existing public v2/custody semantics. Parent reverted only owned code and normally merged current security maintenance before rederivation.


- Pre-review acceptance audit: the first implementation used a domain-keyed refusal serializer, which changes ordinary router serialization and breaks nonempty events without matching serializers. Require a private rule-owned bounded producer configured through the new registration; retain all authority/effect/lease checks and matrix controls. Replace the NoOp-only success proof with nonempty success and admission-rejection compatibility controls. Frozen intent is unchanged.

## Review Triage Log

| parent-acceptance-1 | medium | bad_spec | Ordinary finite receivers have a legitimate complete v1 binding but current public v2-only fields stay null; requiring that field makes the new refusal seam unusable for actual ChatBot without fabricated technical membership. Reuse the existing digest internally and SDK-owned transport while preserving ordinary/v2 metadata semantics. |
| parent-acceptance-2 | medium | bad_spec | Actual consumer denial attribution reads raw relay/task extensions and scoped services; eight scalar fields plus a retained singleton callback cannot preserve it. Bound immutable extension data and a typed request-scoped evaluation overload satisfy the existing frozen refusal-only contract without exposing provider/state/authority. |


| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | medium | patch | The friend implementation in root Parties still implements Task<RouteCatalogDispatchLease?> AcquireAsync, so the changed internal return signature causes a source build error. Preserve that existing signature/direct-call behavior and use a private compatible decision path for the SDK filter; no new public API is necessary. |
| blind-2 | medium | patch | TryRefuse checks any same-domain V1 route before Resolve checks the requested declaration. A known implicit V2 command can therefore receive a terminal V1 rejection. Skip the refusal path for known non-V1 selections and retain their original target/tenant/admission/step-up checks; add a paired known-command control. |
| blind-3 | medium | patch | Schema-three legacy activations have null ActivationBindingDigest by construction. A matching registration currently throws before NoOpinion can fall back, although normal declared dispatch is valid. Refusal capability absence must skip the new callback and continue ordinary guards, never invoke an unbound callback. |
| blind-4 | medium | patch | AddEventStoreProcesses binds EventStore:Processes; ordinary domain-service/refusal registration does not. The new guard can silently use default input limits despite host configuration. Bind the existing options for this registration without enabling process runtimes or changing ordinary producer selection; verify configuration-only binding. |
| blind-5 | high | patch | The callback and serializer run inside guard acquisition before the filter enters RouteCatalogRemoteCall. An allowed denial-audit RPC using the existing helper has no receiver lease enrollment; ambiguous failure can release the generation fence. Enter the existing remote scope around actual callback/serialization work and verify completion versus ambiguity retention. |
| blind-6 | low | patch | The named finite-registration test covers only duplicate/name overflow. Add direct contract boundary assertions for the last allowed and first refused pair and domain UTF-8 limit. This needs test assertions only and adds no production complexity. |
| blind-7 | medium | patch | Current input tests only exceed the default independently with ASCII payload or metadata. Configured exact-byte and combined UTF-8 metadata boundaries remain unobserved at the new callback boundary. Add exact-limit and one-byte-over controls with non-ASCII scalar data and payload. |
| blind-8 | medium | patch | Cancellation and failure tests cover callback and response writer, but not the asynchronous bounded serializer between them. Add held/cancelled and faulted serializers to observe no escaped success payload, effect fences and authority release/retention according to actual remote ambiguity. |
| blind-9 | medium | patch | All current nonordinary tests use an undeclared command, so identical catalog503 masks whether original declared validation executes. Use declared matching-rule paired controls and add TrustedEffectOrigin and SnapshotCaptureProfileId cases, retaining original no-dispatch effects and expected baseline outcomes. |
| blind-10 | low | patch | The public delegate is retained by singleton rule registration, but XML/docs omit capture lifetimes and a complete serializer example. Directly document safe lifetime/concurrency expectations and existing activation/configuration requirements; no new behavior or public surface is needed. |
| verify-1 | medium | patch | Pre-verified regression gap: default-only tests cannot detect ignoring a configured lower MaxCommandBytesPerEnvelope. Add below/above configured-limit HTTP controls with zero callbacks/effects on refusal and drained leases; pair with blind-4/7 without dropping this individual verdict. |
| verify-2 | medium | patch | Pre-verified regression gap: removing TrustedEffectOrigin exclusion leaves the read tests green and lets an origin-bearing request acquire terminal200 behavior. Add its own nonordinary-mode case and no-callback/effect/lease assertions; pair with blind-9 after this individual verdict. |

## Verification

Build affected SDK test projects serially and invoke built xUnit single-dash class/method selectors. Verify every new matrix row, existing relevant receiver/native wire controls and unchanged secret scan for edited fixtures. Keep all failed attempts and exact counts/skips. Parent later runs complete affected routing suites and fresh exact-source hosted CI before merge/publication; no source fixture result grants native production qualification.

## Implementation Evidence

- Complete 17-file owned patch SHA-256 `2c6d14b8fe54dea753c3d8e4a0ea93449d6a801e2a9eb5b2e93e847251ba166a`; both affected Release builds have zero warnings/errors.
- All 207 distinct focused cases pass with zero skips, including the 65-case authority/refusal/lease/ordinary serialization matrix. Four nonempty compatibility controls demonstrably failed before the private-producer remedy and pass afterward.
- Exact receipts: `/tmp/sdk-terminal-refusal-verification.json` and `/tmp/sdk-terminal-refusal-matrix-audit.json`. No native/runtime/publication qualification is claimed. Parent verification and fresh exact-source CI remain required.

## Corrected Matrix Verification

Add real ordinary finite HTTP controls with zero technical membership, independently committed source/owner observations, SDK sender proof, and invalid/missing/duplicate bindings. Prove extension count/UTF-8 budgets, raw case ambiguity/null data and immutable copying. Prove scoped service lifetime/isolation/disposal, missing-service failure, and zero service resolution before invalid authority or budget refusal. Retain all KEEP tests and original outcomes. Run only affected serialized Release builds/classes; parent owns complete routing/domain regression, all independent reviews and hosted/publication gates.

## Corrected Implementation Evidence

Fresh derivation complete:22 owned files, patch SHA25661d29ec2412a045e397e5b1111f12f8b412c0a5a3d3671301be03e43d6c2233a. Both affected Release builds have zero warnings/errors. All203 distinct focused cases pass without skips; matrix audit covers genuine ordinary receiver/SDK transport, immutable extension limits and scoped-service lifetime plus preserved controls. Current metadata, old guard interface/direct semantics, ordinary router, codec, authority readback and Aspire custody remain unchanged. Exact receipts `/tmp/sdk-terminal-refusal-rederive1-verification.json` and `/tmp/sdk-terminal-refusal-rederive1-matrix-audit.json`; parent broader regression and fresh review remain required.

## Review 2 Triage

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | medium | patch | The new options setup appends a whole-section Bind after earlier AddEventStoreProcesses configure callbacks, so explicit OperationTimeout and other runtime overrides are overwritten. Preserve original binding/configure precedence and test both registration orders and ordinary Configure-before/after cases. |
| blind-2 | low | patch | A registered refusal can start with a zero or over1MiB configured input budget, then every matching request503s. Add a narrow fail-fast check for the required command-byte range when refusal rules are registered; do not validate unrelated process settings or change hosts without refusal rules. |
| blind-3 | low | patch | The same guide still says no extensions, contradicting the actual immutable ordinal Extensions property and later text. Correct that sentence directly. |
| blind-4 | medium | patch | The upgrade checklist names RequireAuthorization(Project), while the real startup validator requires RequireEventStoreDomainServicePolicy metadata/filter. Following the changed guide fails startup; correct the supported helper. |
| blind-5 | low | patch | The changed upgrade checklist categorically rejects authority resource binding, but JwtWorkloadAssertionIssuer supports explicitly enrolled EnableResourceBindingRequests plus confidential client credentials. Document the conditional support and existing enrollment requirements. |
| blind-6 | low | patch | The known V1 eligible branch has NoOpinion and invalid-selection controls but no positive rejection. Add one direct matching-rule terminal-rejection control with all no-effect assertions; no production complexity. |
| blind-7 | low | patch | Request-scoped service disposal has successful rejection coverage but no direct callback/serializer/cancellation failures. Add focused failure controls to observe disposal and existing durable-fence outcomes without changing lifetime behavior. |
| blind-8 | low | patch | The new terminal writer test fails only its first small write. Add a multi-window bounded rejection and fail after an actual successful write, checking client connection failure/partial response and lease drainage; retain existing writer/status assertions. |
| blind-9 | low | reject | The reported absence of a new liveDapr case is true, but changed sender injection/private matcher are exercised through the real SDK transport and actual HTTP receiver; the remaining Dapr forwarding hop is unchanged. Adding a native host/sidecar harness exceeds a direct correction for this residual verification dimension. No new native/runtime qualification is claimed; existing exact-source hosted integration gates remain mandatory. |
| blind-10 | medium | patch | The newly added event5541 logs decoded target data with no bound/control sanitation. TryGetDomainServiceTarget accepts these strings, and unavailable issuance logs them. Bound/sanitize only the diagnostic audience field and verify encoded-control/oversized input; retain routing/issuance behavior and avoid credential/exception text. |
| edge-1 | medium | patch | Independently verified same options-order defect: whole-section setup after explicit process configure overwrites timeout/lease settings. Preserve precedence rather than merely skipping required budget binding; group with blind-1 only after this separate verdict. |


## Final Review2 Patch Evidence

Same implementation owner applied the accepted patch-only findings. Final26-file owned patch SHA256 `b6a87a7dbc689327fd74c338ce475fa61d20fb4a9806c52ee51e647e0a80d053`; parent read the complete37,047-byte delta and checked every current file hash. Exact receipt `/tmp/sdk-terminal-refusal-review2-verification.json` records77 distinct focused passing cases, zero skips, clean affected Release builds and preserved failed attempts. No fresh review loop was run for these direct patches. Normal Conventional commit `3df326592a09bff7d665df23fdaa809dcaf2131e` records exactly26 owned files, with pinned commitlint21.2.4 before and after plus normal hooks. Seven existing dirty physical peer gitlinks remain untouched.

Parent then normally integrated independent upstream command-stage diagnostics `fa978a283b29deb05a9c988ff54e3299de9606fc` in merge `990d4ee3820c88da6be0e40490ef8d8faaeba534`; broader affected checks now run against that combined source. Fresh hosted CI and public publication remain pending.


## Parent Final Verification

At exact combined/pushed source `990d4ee3820c88da6be0e40490ef8d8faaeba534`, both affected Release builds pass with zero warnings/errors. The complete affected routing/command/pipeline selection passes1372 cases with zero skips, workload handler passes28/28, and complete DomainService executes1356 cases with1355 passing and one pre-existing skip. No failures or not-run cases. Exact commands, timings, XML/log hashes and skip identity are retained beside review2/parent-final-combined. Implementation/review is complete; fresh hosted CI, final merge and public release remain parent orchestration gates.


## Hosted Legacy-Schema Eligibility Correction

Hosted native run41353778900/job124989363937 exposed two preserved schema-two security503 controls returning the newly added404. Individual verdict `medium`, route `patch`: the unknown-local legacy404 shortcut must be eligible only for schema-three authority; prior schema1/2 validation stays intact. Same owner applied a one-line predicate and paired authenticated schema2→503/schema3→404 control; both native line391 assertions are byte-identical. Parent read the complete two-file patch SHA256b631ce5217e25c7cd1fe930d70e953e6ba8e74009ef13477de1b8fa52b6f361a and verified current hashes.17 focused cases pass with zero skips and clean Release build. Normal pinned commitlint/hooks recorded/pushedc1952e97ac8b33b1578207e91c1c574ef69ede47. No new review loop or semantic test-expectation change; fresh hosted native/publication gates remain pending.
