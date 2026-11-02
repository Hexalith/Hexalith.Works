---
title: Restore ChatBot command and query admission through the published routing catalog
type: bugfix
created: '2026-11-01'
status: in-review
route: dispatch
review_loop_iteration: 0
baseline_commit: 2ac8c4b5bc87cc253c92ca60e806a86ab0948e99
context:
  - /tmp/works-chatbot-timesheets-adoption-20261101/AGENTS.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /home/administrator/projects/hexalith/works/references/Hexalith.AI.Tools/hexalith-state-instructions.md
---

<frozen-after-approval reason="User authorized latest SDK migration and CI repairs, preserving authorization, and waived human validation checkpoints">

## Intent

Published EventStore3.130.2 installs catalog guards that reject ChatBot's uncatalogued owned command, query and legacy projection paths before existing domain admission. Enroll the actual ChatBot capabilities through public SDK contracts and real owner/consumer observation so the existing domain outcomes execute again.

## Boundaries & Constraints

Always retain existing authorization, tenant isolation, typed policy denials, malformed-payload denial ordering, persisted state/event assertions, coarse idempotency reservations and exact60-second/5-minute/24-hour/permanent windows. Preserve the production admission stage before parsing. The four policy DTOs with no current aggregate handler remain refusal-only; new fallback handlers must produce no success events or mutations. Keep SDK admission configuration independent from ChatBot's existing coarse store; never describe SDK retention as equivalent. Domain owners declare their real capabilities; actual gateway consumers observe installed adapters, options and available digest keys.

Never fabricate ownership, signed authority, native-key support, handlers, production enrollment, publication evidence, or positive runtime qualification. Never bypass SDK guards, use permit-all policies, change meaningful test expectations, skip failing cases, or initialize/update nested submodules. Do not modify EventStore while its explicit main release freeze is active.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Owned commands | Existing signed workload and committed truthful catalog | Existing admission and aggregate outcomes and persisted effects | Original typed domain refusal remains |
| Policy-only DTOs | Four currently unimplemented DTOs, including malformed payload | Existing policy gate rejects before parsing; fallback also rejects without mutation | Same denial contract, no reservations/dispatch |
| Owned queries/projection | Actual eight queries and legacy receiver | Real handler inventory and existing responses/state | Preserve denial and output ownership |
| Catalog boundary | Missing/stale commit, unavailable keys or mismatched owner | Genuine SDK refusal and zero forbidden effects | No synthetic authority |
| Coarse replay | Existing retry/conflict/retention cases | Original reservations, receipts and windows unchanged | Original replay/conflict outcomes |

</frozen-after-approval>

## Code Map

Owner `/tmp/works-chatbot-timesheets-adoption-20261101`, clean baseline above. Read its build props, .editorconfig, .gitattributes, architecture and relevant tests before editing. No skills under references.

- `src/Hexalith.ChatBot.Server/Operations/GovernedOperationAggregate.cs`: sole aggregate processor. Four types have no Handle: SubmitEscalationPolicyChange, SubmitMailboxConfigurationChange, SubmitRetentionConfigurationChange, SubmitDataClassInventoryChange. Implement genuine typed refusal-only fallbacks using existing rejection contracts; retain the current admission stage first.
- `src/Hexalith.ChatBot.Server/Gateway/ChatBotDomainServiceAdmissionStage.cs` and `tests/Hexalith.ChatBot.Server.Tests/Gateway/PolicyPersistenceAdmissionTests.cs`: existing policy-persistence gate, signed-marker, pre-parse denial and zero-effect assertions are authoritative.
- Eight `ChatBotReadQueryHandler<T>` implementations require real static CatalogDeclaration metadata; `ChatBotDomainProjectionHandler` is a legacy IDomainProjectionHandler, so use LegacyProjectionCatalogRegistration and preserve `/project`, not invented v2 receivers.
- `ModuleMigration` and existing gateway/store composition: locate actual declarations and host wiring; retain coarse admission logic exactly. Use actual endpoint/handler and Contracts inventory for declarations, schema and eligibility, not reflection-generated fake capabilities.
- Read-only public API source `/tmp/works-sdk-compat-qualification-20261101`: DevelopmentDomainRouteCatalog.Targets enforces exact declared-vs-actual payload inventory; RouteCatalogAdmissionObservation.CaptureAsync reads installed adapters/options/key ring; RunScopedRouteCatalogGateway loads compiled owner factories. Investigated files match main7776e64; use only APIs also present in the actual selected public3.130.2 packages.
- Read-only Works `src/Hexalith.Works/Routing/WorksCatalogDeclarations.cs`, `WorksCatalogAdapters.cs` and host enrollment demonstrate owner declarations and adapters. Timesheets `HttpRouteCatalogFixture` demonstrates real prepare/ready/commit and headers, but projection-only bindings do not prove command key availability.

## Tasks & Acceptance

- [x] Implement refusal-only fallback overloads and focused no-effect tests for the four existing gated types.
- [x] Declare all actual owned commands, eight queries, legacy projections and outputs, with real typed adapters and owner enrollment.
- [x] Wire genuine gateway consumer observation through existing public SDK composition, preserving coarse behavior and independent SDK options. Reuse SDK defaults where compatible; report any new user-visible capability or unsatisfied contract instead of inventing it.
- [x] Activate genuine committed-catalog test fixtures, retaining all original HTTP, authorization, denial, persistence, replay and conflict assertions. Add only necessary boundary controls.
- [x] Run affected existing classes and report exact source method/case reconciliation, complete diff, owned paths, commands, outcomes and remaining gates.

Given a truthful committed catalog and current authorized request, when an owned operation executes, then the original domain behavior is reached. Given unavailable policy or invalid authority, when the same HTTP boundary is called, then its original denial and zero-effect assertions hold.

## Implementation Notes

Parent owns Git, remote operations, dependency/gitlink synchronization, reviews and publication. Work only in the owning ChatBot checkout and this spec. No native/container/production lane; finite serialized Release/public-package checks are authorized and must be labeled distinctly from source Debug development checks. Send parent a message before substantial build/test work.

Separate known SDK blocker: `ServerBootstrapApiTests.ProjectEndpointShouldReturnNotFoundForUnknownDomain` sends domain folders. The generic SDK guard currently demands auxiliary ownership before missing-handler404, yielding503 for a truthful ChatBot catalog. Preserve that existing404 expectation and report it as pending generic SDK correction; do not claim complete ChatBot CI, invent Folders ownership, or edit SDK during the freeze. This task's focused acceptance concerns owned ChatBot enrollment; final repository/release acceptance remains parent-owned and requires the generic correction plus complete fresh hosted CI.

## Spec Change Log

## Review Triage Log

## Verification

Use supported built xUnit assembly single-dash selectors and serialized project builds. Keep initial failures, distinguish fixture/source support from actual deployed authorization, and report all skips. Return exact source diff including untracked files and baseline/current hashes, method counts, tests executed and outstanding risks. Existing65 hosted Server failures are recorded under the Works `.artifacts/upgrade-release-orchestration-20261101/chatbot-2ac-results/`; do not turn that historical diagnostic into a passing claim.


### Implementation receipt — 2026-11-01

Owned ChatBot enrollment is implemented in 20 paths in `/tmp/works-chatbot-timesheets-adoption-20261101`. Baseline and current HEAD remain `2ac8c4b5bc87cc253c92ca60e806a86ab0948e99`; changes are uncommitted and unstaged. The complete unified source diff, including all eight new files, is `/tmp/chatbot-routing-complete-diff-20261101.patch` (SHA256 `d073b37b6e3ee3084e345cdb22290db477f38c598d8a2e83c5265fb4ff471471`). `/tmp/chatbot-routing-receipt-20261101.json` records all owned paths and file hashes, exact commands, package assets, source-method counts, failures, and remaining gates. `/tmp/chatbot-routing-reconciliation-20261101.json` maps every historical failed case to its current result and exact source file/line. XML display-name quote escapes were normalized for that join; parameter values were preserved.

The owner inventory is 64 actual command payloads (60 previous handlers plus four unconditional refusal-only fallbacks), eight actual computed domain-service queries, and one legacy `/project` receiver with its actual `chatbot` summary output. Eight query handlers carry static metadata. Their existing dotted response tags remain unchanged and are not misrepresented as SDK projection-actor names. The compiled declaration and adapter attributes expose the existing public SDK owner-loading seam; all operation metadata stays internal and UI-only. The real gateway must independently configure its SDK admission options and digest key provider. Its Mutation retention selection is separate from ChatBot's unchanged coarse reservations and windows.

The HTTP fixture retains the SDK authority, trust validator, receiver guards, and existing authenticated test policies. It observes actual installed owner targets through `DevelopmentDomainRouteCatalog.Targets`, captures independently installed typed consumer adapters/options and available disposable test keys through `RouteCatalogAdmissionObservation.CaptureAsync`, and performs prepare/ready/commit with both participant observations. It establishes in-process fixture support only. No deployed gateway, native key support, production authority, publication, or positive runtime qualification is claimed.

Verification used finite serialized Release/public-package checks, selecting immutable `/tmp/chatbot-routing-Directory.Packages-35b58049.props`, copied from Builds revision `35b58049b41816852ef3a0dede8afd39dfb6a7a3` (SHA256 `8600bcb15957efb24edebc611ed7f106fbbc4951a88f559d1f827fb226a6b6d5`). Assets confirm EventStore packages 3.130.2, Projects 1.2.2, and Memories 3.0.15. The subsequent parent-owned latest-package adoption is a different evidence lane.

- Initial unchanged-source build passed with zero warnings/errors. Eight malformed-policy HTTP cases plus the unknown-Folders projection case failed 503 (9/9), retained in `/tmp/chatbot-routing-initial-build-20261101.log`, `/tmp/chatbot-routing-initial-tests-20261101.log`, and its XML.
- Final command: `dotnet build tests/Hexalith.ChatBot.Server.Tests/Hexalith.ChatBot.Server.Tests.csproj -c Release -p:UseHexalithProjectReferences=false -p:DirectoryPackagesPropsPath=/tmp/chatbot-routing-Directory.Packages-35b58049.props -m:1 /nr:false`. Passed with zero warnings/errors; `/tmp/chatbot-routing-final-build-20261101.log`.
- Built xUnit assembly single-dash class selectors with `-parallel none` executed 988 existing cases across 554 source methods in eight affected/aggregate/coarse classes: 983 passed, five preserved failures, zero skipped/not-run. Exact selectors are in the receipt; results are `/tmp/chatbot-routing-existing-tests-20261101.xml` and its log.
- All 17 new cases across six source methods passed, including final post-build rerun: `/tmp/chatbot-routing-final-focused-tests-20261101.xml` and its log. Unique combined coverage is 1,005 cases across 560 methods: 1,000 passed, five preserved failures, zero skipped/not-run.
- All 65 historical failed cases across 26 methods are present in the new run: 60 now pass and five still fail. The historical hosted diagnostic itself is not reclassified as passing.
- Tracked `git diff --check` and each untracked-file `git diff --no-index --check -- /dev/null <file>` passed. One earlier mixed class/method selector invocation selected zero cases and was discarded; corrected class-only runs above provide the evidence.

Matrix audit, with every cited covering test executed and passed:

| Matrix row | Passing evidence |
|---|---|
| Owned commands | `ProcessEndpointShouldRunChatBotAdmissionBeforeSdkProcessor`, existing aggregate tests (138 cases), existing gateway persistence/outcome assertions |
| Policy-only DTOs | `UnsupportedPolicyFallbackMustRejectWithoutMutation` (8 cases); all original malformed-policy (8), signed policy denial (24 full method cases), and policy binding/tampering controls |
| Owned queries/projection | `DomainQueryDispatcherShouldDispatchRegisteredChatBotReadHandlers`, `SyntheticWorkloadAuthenticatedSdkRelayBindsServiceAccountAndClosedOrigin`, `ProjectEndpointShouldDispatchRegisteredChatBotProjectionHandler`, existing store end-state assertions |
| Catalog boundary | `InvalidCatalogAuthorityMustRefuseBeforeDomainEffects` (4), `MissingOrMismatchedDigestKeysCannotProduceAdmissionBindings` (2), `ActualOwnerObservationMustRejectMismatchedTarget` |
| Coarse replay | All 592 `CommandGatewayTests` cases and 12 `CoarseIdempotencyStateJsonTests` cases, including existing replay/conflict/receipt/retention assertions; production coarse-store code is unchanged |

Remaining repository/release gates, with expectations preserved:

1. `ServerBootstrapApiTests.ProjectEndpointShouldReturnNotFoundForUnknownDomain`: unknown `folders` projection returns SDK 503 instead of existing 404; generic SDK correction remains parent-owned after the freeze.
2. `TrustedAuthorityReviewTests.WorkloadSdkCannotEmitUnderMismatchedOrUnsupportedAggregate(unsupported: true)`: uncatalogued `ExecuteAdminQueueOperation` has no aggregate handler, so SDK 503 precedes the existing 200 typed-denial expectation. No capability or handler was fabricated. Its owned `RecordGovernedNote` case passes. Parent explicitly retained this compatibility gate.
3. `CommandGatewayAdmissionApiE2ETests.CommandGatewayApi_ShouldRejectUnauthenticatedSubmissionBeforeDispatch`: authorization-failure audit count is zero instead of one.
4. `ChatBotProjectConversationHubE2ETests.AiResponseProgressProjectionShouldBroadcastTenantScopedChangeSignalToHubSubscribers`: HTTP 401.
5. `ChatBotProjectConversationHubE2ETests.UnauthenticatedJoinShouldFailClosedWhenJwtAuthenticationIsConfigured`: HTTP 401.

No existing assertion, production admission ordering, coarse-store behavior, EventStore source, dependency pin, submodule, or incoming recovery patch was modified. Parent owns review, rebase/commit, latest-package adoption, gitlinks, publication, and complete fresh hosted CI. No source Debug, native/container, or production lane ran here.


## Parent Review1 Triage

All three layers returned final before triage; edge returned no findings. Each of the eight blind findings and the pre-verified bootstrap gap received an individual verdict before grouping. This is a patch-only route.

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | low | reject | The shared SDK CanonicalIntentPayload intentionally hashes every supplied raw JSON member with ordinal property names, alongside typed payload hashing. Case aliases can conservatively conflict under a new native key, but existing ChatBot gateway callers supply no native key and existing coarse intent behavior is unchanged. Generated serializers use stable casing. Fixing this requires a shared normalization/collision policy rather than a direct ChatBot correction. |
| blind-2 | medium | patch | CoarseIdempotencyComposer binds ActorId throughout its existing intent policies, while new declarations set RequiresAuthenticatedSubject=false. Use the existing SDK authenticated-subject flag and descriptor member to retain replay ownership; verify two subjects cannot yield the same intent and absence is refused. |
| blind-3 | low | reject | These declarations deliberately expose only internal workload/UI-only receiver contracts, with the actual generic SDK transport envelopes and no machine gateway surface. Concrete payload changes do not alter that opaque wire-envelope schema. Extending schema fingerprints to all response/event payload variants introduces additional contract design beyond a direct correction; no generated public response contract or current authorization failure was demonstrated. |
| blind-4 | medium | patch | Startup filter overwrites explicit caller root/epoch values. Preserve any explicitly supplied generation authority instead of repairing it, and verify stale/partial caller authority with automatic activation still enabled. |
| blind-5 | low | patch | 503 distinguishes the current catalog refusal from downstream typed200 denial, and existing988 cases prove legitimate domain execution. Add a directly paired valid-generation control using an existing usable principal and observable safe domain admission/denial evidence so this focused test cannot pass if every generation is refused. |
| blind-6 | low | patch | The common authority acquisition precedes all receiver branches, but the new fixture enrolls three receiver kinds. Extend the small existing invalid-generation matrix to query and legacy projection with valid wire payloads, retaining zero effects and drained leases. |
| blind-7 | low | reject | The actual observation path already reads installed typed adapters, enabled options and the real available key provider, and tests missing/mismatched keys. Existing ChatBot callers supply no native key; no native claim, coordinator execution or deployed acceptance is asserted. Recreating SDK coordinator/storage integration here adds a separate native-admission harness; unchanged SDK behavior remains covered in its own CI. |
| blind-8 | low | reject | The new control exercises the actual SDK observer against a changed owner under a valid committed generation. Other method/version/identity dimensions use the same unchanged SDK full target/declaration comparison. Extra mutation vectors duplicate SDK finite-inventory controls without a demonstrated ChatBot-specific divergent branch. |
| verify-1 | medium | patch | Pre-verified gap: all normal HTTP tests replace the production observer/bootstrap with their fixture, so deleting Program AddEventStoreDevelopmentDomainCatalog is undetected. Enable actual production Development bootstrap privately and assert emitted ChatBot identity, declarations/operations and receiver observation. |
