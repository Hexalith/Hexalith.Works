---
title: Preserve Folders idempotency through declared registry admission
type: bugfix
created: '2026-11-01'
status: done
route: dispatch
review_loop_iteration: 0
baseline_commit: 4de8e5d0e4ad16f33e7eae31f7323fe5bbf326b7
context:
  - /tmp/works-folders-catalog-operation-1793515230698427047/Folders/AGENTS.md
  - /tmp/works-folders-catalog-operation-1793515230698427047/Folders/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /tmp/works-folders-catalog-operation-1793515230698427047/Folders/references/Hexalith.AI.Tools/hexalith-git-instructions.md
---

<frozen-after-approval reason="User explicitly authorized Include Folders, CI repair and bypass of human validation; existing behavior settles this bounded migration">

## Intent

Folders CI stops at the obsolete two-argument registry constructor. Its18 production adapters also lack declared admission, so changing only the test constructor would conceal the runtime problem. Adopt the supported public3.130.2 custom declared-adapter interface and exercise actual authority admission while preserving canonical intent bytes and existing authorization.

## Boundaries & Constraints

Always preserve all18 discriminators, operation/adapter IDs, policy folders-contract-spine-v2, descriptor version1 and existing retention (Commit for commit, Mutation for17 others). Preserve CreateIntent, payload normalization/digests, target, null/omission, ordered batches, task/fence/credential scope and transport exclusions. Keep authoritative tenant/principal checks, ACL/provider/organization namespaces and receipt validation. Use real SDK authority, observer, codec and prepare/ready/commit, not a permissive replacement.

Never change canonical bytes, use the incompatible generic typed adapter, add permit-all authority, bypass hosted completeness, skip tests, weaken inventory, invent ingress/agent eligibility, publish/deploy or modify dependencies/gitlinks. This bounded schema-two registry migration grants no schema-three public/agent enrollment. No state/data migration or irreversible operation is required. Human checkpoints are waived by the existing explicit user instruction.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Failure |
|---|---|---|---|
| All18 commands | Real registered adapters and committed complete admission | Real production-DI registry resolves unchanged canonical descriptors | No encoder-only substitute |
| Native task | acquire/renew/release/complete; changed fence | Existing validation/admission; changed intent differs | Existing invalid message refusal |
| Preservation | Existing representative nulls, batches, scopes, payload guards | Exact legacy canonical bytes and policy/tier preserved | No schema-driven rewriting |
| Unavailable | Authority before activation or missing admission | Refuse before accepted command | No fabricated serving generation |
| Drift | Wrong tenant/domain/discriminator or declaration/policy/schema/tier | Real admission refuses | No fallback grants |
| Startup | Missing/duplicate/undeclared production adapter | Actual hosted completeness gate refuses | Keep hosted gate running |

</frozen-after-approval>

## Code Map

Owner /tmp/works-folders-catalog-operation-1793515230698427047/Folders, main4de8e5d0. The pre-existing Builds child checkout differs from its tracked gitlink after authorized parent fast-forward; do not initialize/update/stage that difference. Existing root-declared dependency copies may be read and registry restores are permitted. No Git remote actions or publication in this handoff. All new C# types belong in separate named files.

- EventStore/FoldersCanonicalIntentBuilder.cs: immutable legacy slash target, sorted semantic JSON, duplicate guard, semantic_payload_sha256, task agreement and fixed provider_binding scope. Preserve implementation.
- EventStore/FoldersIdempotencyIntentAdapterServiceCollectionExtensions.cs:16types/18registrations, including three organization ACL instances. Convert to IDeclaredIdempotencyIntentAdapter without changing CreateIntent.
- EventStore/FoldersIdempotencyIntentAdapterCatalog.cs: extend existing hosted completeness/duplicate check to validate complete matching declarations.
- EventStore/Program.cs: AddEventStoreServer registers real authority, encoder and registry; use this production DI plus ordinary Folders registration in the finite test.
- Server/FolderDomainProcessor*.cs: current discriminator dispatch and actual private payload types. Do not equate discriminator strings with payload CLR names or add speculative schema-three discovery.
- Server/FoldersDomainServiceRequestHandler.cs and FoldersServerHostComposition.cs: preserve custom process/workload/receipt/ACL authorization.
- tests/Hexalith.Folders.EventStore.Tests/NativeTaskAdmissionTests.cs: failing old constructor; retain validator and four native cases but use real prepared authority/DI.
- Existing exception-only host factory removes all IHostedService; it cannot prove the hosted admission gate. Use an actual gate-enabled composition.

Public SDK3.130.2 supports custom IDeclaredIdempotencyIntentAdapter and CommandIntentDeclaration. Actual constructor proof: /tmp/works-folders-idempotency-current-constructor.log. Source is available read-only at28e3f260bbaf4619d1a99d656fba04c0c532387b in /tmp/works-sdk-compat-qualification-20261101. That checkout has another active owner; do not edit it or rely on private future additions.

## Tasks & Acceptance

- [x] Define immutable complete owner metadata for18 commands and matching custom declarations; preserve every legacy CreateIntent output byte.
- [x] Describe existing encoded intent components with legal descriptor labels/types: target:string (CanonicalTarget), semantic-json:string (UTF8 SemanticPayload), semantic-payload-sha256:string (existing semantic option), delegated-task-scope:string and credential-scope:string (existing encoder empty representation when absent). Use per-operation schema IDs/version1 and preserve RequiresAuthenticatedSubject=false equivalence. These labels are metadata, never new JSON keys or re-encoding. Validate the mapping explicitly because the custom SDK adapter does not enforce values against descriptor fields.
- [x] Extend hosted completeness and build a real finite schema-two catalog using isolated IRouteCatalogStateStore, observer/trust validator, independently captured installed adapters/key policy, codec seal and prepare/ready/commit. Resolve the registry from production SDK DI.
- [x] Exercise every matrix row, all18 admissions and existing canonical/ACL/task/credential/receipt controls; keep original assertions and classify failures honestly.
- [x] Report changed paths, method discovery/deltas and exact checks; provide a full current unified diff including untracked source. Parent owns review/commit/push. No broad unrelated suites or native/container launches.

## Implementation Notes

Schema types are string/int64/boolean/string-list; legal field names use lowercase hyphen segments, so preserve legacy underscore/dotted semantic JSON and describe it through the complete string representation. The generic adapter would alter legacy target/task/credential and nullable encoding and is forbidden.

Before activation the real authority must refuse No complete catalog generation is serving. A schema-two fixture does not make GET /api/v1/routing/catalog available: that endpoint still requires schema three and returns503. Full compiled discovery still requires Operations and approved eligibility. Preserve those independent refusals; do not represent this repair as full enrollment. No user-visible choice is required for the bounded existing18-adapter migration.

## Spec Change Log

## Review Triage Log

## Verification

Build/run only affected actual source tests, preferring narrow Debug/source or explicitly identified finite Release/public-package checks against public3.130.2. Registry/source reads are allowed; no nested initialization. Run relevant existing adapter/canonical/native admission and hosted completeness tests without disabled services. Preserve failed logs and report blockers rather than weaken them. Parent runs broader affected project and hosted CI after review; do not claim deployment/runtime/public eligibility from the finite registry test.

## Implementation and Parent Verification

All16 adapter types/18registrations now expose immutable complete owner
declarations through the public custom interface. Actual CreateIntent tails and
FoldersCanonicalIntentBuilder are byte-identical to baseline. Hosted completeness
validates full declaration/descriptor/policy/tier/subject equality. The native
fixture resolves the real committed authority/registry from production SDK DI.
All18 complete pre-migration canonical descriptors match exactly after admission;
no schema-three eligibility or serving deployment is claimed.

Owner finite public3.130.2 Release builds pass0warnings/errors. Focused EventStore
106cases and existing Server119auth/receipt/workload plus5wire/namespace controls
pass with0failures/skips. Parent runs the complete actual EventStore assembly:
114pass/0fail/0skip in6.152s. Method discovery34->44, +10methods, no removals;
focused legacy62cases preserved, +44 admission/hosted cases. Parent removed one
duplicate XML summary only; no runtime/test behavior changed afterward.
Exact commands/logs /tmp/folders-declared-verification-commands.txt and
/tmp/folders-declared-parent-full-eventstore.log. Source preservation receipt
/tmp/folders-declared-source-evidence.json. Original failed fixture runs remain
retained and are described in the command receipt. Full Server/project CI and
release gates remain separate.

The complete current diff includes the pre-existing local Builds checkout
difference against its tracked gitlink. That dependency was neither initialized,
updated, staged nor owned by this repair; it is explicitly identified as a
pre-existing exclusion. All28 implementation paths are included separately and
source-frozen for independent review, with untracked files included.

## File List

- `src/Hexalith.Folders.EventStore/ArchiveFolderIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/AuthorizeContentErasureIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/BindRepositoryIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/ChangeNativeTaskIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/CommitWorkspaceIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/ConfigureBranchRefPolicyIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/ConfigureProviderBindingIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/CreateFolderIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/CreateRepositoryBackedFolderIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/FoldersCommandIntentDeclarations.cs`
- `src/Hexalith.Folders.EventStore/FoldersIdempotencyIntentAdapterCatalog.cs`
- `src/Hexalith.Folders.EventStore/GrantFolderAccessIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/LockWorkspaceIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/MutateFilesIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/OrganizationAclIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/PrepareWorkspaceIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/ReleaseWorkspaceLockIdempotencyIntentAdapter.cs`
- `src/Hexalith.Folders.EventStore/RevokeFolderAccessIdempotencyIntentAdapter.cs`
- `tests/Hexalith.Folders.EventStore.Tests/DeclaredFoldersIntentAdapter.cs`
- `tests/Hexalith.Folders.EventStore.Tests/Fixtures/legacy-idempotency-descriptors.json`
- `tests/Hexalith.Folders.EventStore.Tests/FoldersDeclaredAdmissionFixture.cs`
- `tests/Hexalith.Folders.EventStore.Tests/FoldersDeclaredAdmissionHostedTests.cs`
- `tests/Hexalith.Folders.EventStore.Tests/FoldersDeclaredAdmissionTests.cs`
- `tests/Hexalith.Folders.EventStore.Tests/FoldersRouteCatalogStateStore.cs`
- `tests/Hexalith.Folders.EventStore.Tests/Hexalith.Folders.EventStore.Tests.csproj`
- `tests/Hexalith.Folders.EventStore.Tests/LegacyFoldersIntentDescriptor.cs`
- `tests/Hexalith.Folders.EventStore.Tests/NativeTaskAdmissionTests.cs`
- `tests/Hexalith.Folders.EventStore.Tests/UndeclaredFoldersIntentAdapter.cs`

## Review1 Triage Log

All three layers returned final before classification. Edge returned no findings; verification found no gaps. Ten individual rows precede grouping; five direct patch corrections, no specification loopback.

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | low | patch | The server target inventory is constructed for the finite schema-two fixture; only installed consumer adapters/key policy are observed through production SDK DI. No full server handler/endpoint or live enrollment acceptance is claimed. Directly clarify that boundary in fixture comments/names without expanding to full server/schema-three capture or changing the spec intent. |
| blind-2 | low | reject | The actual changed Folders hosted completeness gate runs through IHost.StartAsync with every production adapter and no removed service. SDK authority/registry composition is separately exercised through AddEventStoreServer. Starting all unchanged SDK/Dapr hosted services adds a new composition/runtime scope; no broken changed interaction was shown. |
| blind-3 | medium | patch | Golden MemberData is solely JSON-driven and has no exact-set assertion. Removing a row would silently omit that command byte check while the production set still contains18. Add a direct uniqueness and exact production discriminator-set check using the actual independent registered adapter inventory. |
| blind-4 | low | reject | Confirmed strict production payload parsing, while the broad legacy vectors intentionally exercise unchanged canonical encoder projections. This is finite registry/byte compatibility evidence, not accepted-handler/schema-three ingress evidence; existing native valid vectors and124server controls remain. Building a second18-command handler-valid corpus expands beyond the demonstrated adapter migration and is not needed to claim actual handler execution, which is not claimed. |
| blind-5 | low | reject | Existing unchanged adapter tests already cover null/omission, set/batch ordering and transport exclusions, and all18 complete before/after encoded descriptors match. Adding vector IDs and a second larger golden format for every relationship is broader test machinery without a current changed-function defect. Preserve existing tests and byte fixtures. |
| blind-6 | low | patch | The new gate explicitly compares adapter properties independently from declarations, but the drift helper couples those values. Add direct independent adapterId/operationId/version/tier mismatch cases in the existing helper/hosted test; do not change production policy or add new guards. |
| blind-7 | low | reject | The test is honestly named ObserverRejectsCandidatePolicy and proves failure during Prepare plus refusal of subsequent unprepared transitions. Readiness/commit revalidation is unchanged SDK behavior; no claim or source change requires re-testing every SDK transition here. Reject new unrelated lifecycle machinery. |
| blind-8 | low | reject | Actual installed key availability is captured by the genuine SDK observer using the fixture configured ring; the changed owner metadata does not alter key-loading logic. Missing/rotating key-reader behavior is unchanged SDK coverage. Adding further SDK key lifecycle fixtures is outside this bounded changed integration; no false available-key claim is made. |
| blind-9 | low | patch | Malformed payloads are an ordinary adapter exception path newly reached while holding a real catalog lease. Add a focused actual registry test showing the parse exception releases the lease and a subsequent valid resolution succeeds; no production cleanup change unless the test demonstrates one. |
| blind-10 | low | patch | The actual new startup mismatch gives no discriminator, making the one bad registration hard to identify among18. Include its already validated nonsecret command discriminator in the existing exception, with an assertion; preserve the error type and fail-closed checks. |

## Patch Completion and Final Parent Verification

The same owner completed all five direct patches. Receiver fixture names/comments
now explicitly describe synthetic target facts; actual consumer admission/key
facts remain independently observed. Golden discriminators are unique and
exactly match production; all18 original byte captures remain unchanged. Four
independent property mismatch cases, a real post-admission malformed-payload
lease/recovery case, and command-specific startup diagnostics are covered.
Targeted58cases passed with0failures/skips. Parent complete EventStore project
then passed120/120,0skip in6.541s; exact command is the built Release assembly
with -noColor -result-xml /tmp/folders-declared-parent-final-eventstore.xml.
The earlier124existing Server controls remain valid supporting evidence.
Parent proceeds through normal pinned commit validation and push; fresh hosted
CI and any subsequent release remain separate required gates. No runtime
permissions, canonical bytes, dependency gitlinks or schema-three claims changed.
