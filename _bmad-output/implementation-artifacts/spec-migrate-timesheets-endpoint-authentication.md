---
title: Preserve Timesheets capability authorization under the current SDK inventory
type: bugfix
created: '2026-11-01'
status: done
route: dispatch
review_loop_iteration: 0
baseline_commit: 8b70017e13fa6f6d5af688b1cc40f7f41b24096a
context:
  - /tmp/works-timesheets-catalog-operation-20261101/AGENTS.md
  - /tmp/works-timesheets-catalog-operation-20261101/references/Hexalith.AI.Tools/hexalith-llm-instructions.md
  - /tmp/works-timesheets-catalog-operation-20261101/references/Hexalith.AI.Tools/hexalith-git-instructions.md
---

<frozen-after-approval reason="User authorized Timesheets CI repair and explicitly chose Require authenticated metadata; human validation waived">

## Intent

The current published SDK rejects Timesheets host startup because four opaque-token routes and metadata carry anonymous markers, two admin routes use unnamed policies, and external-contribution routes have no explicit policy. Migrate credential classification through supported named authentication policies while preserving actual domain authorization, durable retry semantics and protected internal surfaces. Require authenticated metadata using the existing named default human policy; no anonymous metadata exception.

## Boundaries & Constraints

Always preserve canonical token/hash/state/action checks, existing server gates and commit-time revalidation; anonymous network callers holding a valid opaque capability must retain their existing permitted link behavior without a human login. Keep denial bodies opaque, cache/referrer controls, cancellation/read budgets and diagnostic redaction. Keep all DenyAll/Unavailable defaults and the internal-port guard. Preserve management Process policy and existing human default semantics on other protected routes.

Never weaken SDK inventory, create permit-all/forged authentication, add human/party/NameIdentifier authority claims to a capability principal, relax tests or skip failures. No new host, state store, deployed configuration, dependency-version change or production call. This is source/authentication adapter maintenance, not deployed acceptance.

## I/O & Edge-Case Matrix

| Scenario | Input/state | Expected behavior | Error handling |
|---|---|---|---|
| Valid links | Existing confirm/adjust opaque token and canonical eligible state | Same existing display/dispatch and domain gates | Preserve current responses |
| Invalid credentials | Missing/unknown/malformed/expired/revoked/wrong-action token | No domain dispatch or authority disclosure | Same opaque403/privacy controls |
| Retry | Used/Issued capability plus valid existing idempotency receipt path | Existing AuthorizeRetryAsync and durable reconciliation remain reachable | No duplicate effect or false acceptance |
| Authority | Capability authentication succeeds | Authenticated capability only, no invented human/party actor | Existing outbound workload binding remains authoritative |
| Internal/admin | Untrusted port, unauthorized caller or unavailable migration authority | Existing refusal/default-deny behavior | No permission expansion |
| Metadata | No credentials / valid existing default credentials | Require authenticated access; preserve descriptor content for authorized callers | Existing configured challenge/forbid behavior, no metadata disclosure |
| Backend faults | Capability lookup/authorization times out or faults | Preserve existing bounded fault behavior and privacy headers/log redaction | No failure-to-success or fault-to-credential-denial laundering |

</frozen-after-approval>

## Code Map

Owner: /tmp/works-timesheets-catalog-operation-20261101, clean main8b70017.
Public DomainService3.130.1 source1de42b706f52af0da6513f0b7663002606d17156 and3.130.2 source28e3f260bbaf4619d1a99d656fba04c0c532387b share unchanged inventory/security files. Read ordinary SDK source under /tmp/works-sdk-compat-qualification-20261101 without editing that actively owned repository.

- SDK ServiceDefaults/Authentication/EventStoreHttpEndpointInventory.cs:60-115 requires authenticated named policy with exactly one registered scheme. A real custom capability scheme is structurally supported. AD-16 prohibits ungoverned anonymous exceptions.
- src/Hexalith.Timesheets/Program.cs: registration, middleware, public metadata. Authentication scheme registration must preserve existing Bearer defaults.
- Endpoints/MagicLinks/MagicLinkConfirmationCapabilityEndpoints.cs: existing four link routes, Process-protected management and private denial/diagnostic helpers.
- Server/MagicLinks/MagicLinkConfirmationCapabilityCommandService.cs: canonical action/state authorization and AuthorizeRetryAsync (Issued/Used handling); reuse, never reimplement or simplify.
- Server/MagicLinks/CryptographicMagicLinkTokenGenerator.cs: random32-byte opaque secrets, persisted SHA256 hashes; tokens are not signed JWTs.
- Runtime/EventStoreGatewayWorkloadAssertionSource.cs: actor extraction from User; preserve authority by avoiding human identity claims in capability authentication.
- Endpoints/ExternalContributionEndpoints.cs: name existing EventStoreDomainServicePolicies.AnyWorkload, exactly preserving public3.128.4 any-workload fallback and legitimate signed workload calls.
- Endpoints/MagicLinks/MagicLinkIndexMigrationEndpoints.cs: retain the existing internal operator/server-authority semantics with the named Human policy and deny defaults.
- tests/Hexalith.Timesheets.IntegrationTests/MagicLinkConfirmationHttpBoundaryTests*.cs: actual privacy, budgets, management, internal ports, tenant isolation and durable retries. HostMetadataEndpointTests has source/serialization checks, not approved HTTP anonymous acceptance.

## Tasks & Acceptance

- [x] User explicitly chose authenticated metadata; preference recorded and draft gate resolved.
- [x] Implement the smallest real capability authentication adapter and named policies using existing canonical validation, with one C# type per file.
- [x] Migrate actual endpoint metadata and preserve existing named/default protections and every domain gate.
- [x] Exercise every matrix row plus existing HTTP boundary suites; all existing/new cases must pass without replacing meaningful expectations to fit code.
- [x] Report exact changed paths, baseline/current method discovery, phase/cumulative deltas and exact executed commands/counts. Preserve initial failed evidence.

## Implementation Notes

The metadata decision is resolved: Require authenticated metadata (user reply). Begin implementation from clean8b70017. No new user-visible authorization policy is invented. No irreversible data/deployment operation is part of this source change. Parent owns remote operations, commits and publication. No nested initialization; ordinary tracked sparse files may be hydrated in the owning checkout. Existing Works checkout is read-only. Local focused source builds use Debug; any finite Release/public-package verification must be separately identified. Do not claim broader runtime/deployment qualification from in-process tests.

## Spec Change Log

## Review Triage Log

## Verification

First run the actual focused modified integration test source against the selected published SDK, with supported single-dash xUnit assembly selectors and zero unexplained skips. Then parent runs the complete affected integration project and relevant existing auth/architecture tests; fresh hosted CI is required after commit. No competing heavy local lane while SDK qualification owns its native runtime.

## User Decision and Execution Details

The user selected Require authenticated metadata (Recommended) in response to
the exact metadata behavior question. This explicitly authorizes replacing its
legacy anonymous marker with the existing named EventStoreHuman default policy.
Human validation checkpoints remain waived; no approval/signature is fabricated.

No duplicate production token checks: inspect existing canonical loader/service
APIs and request lifetimes. Any request-local caching must preserve action/state/
retry validation, cancellation/read budgets and commit-time revalidation, never
create cross-request authority or add human/party actor claims. Keep underlying
storage/timeout failures distinguishable from invalid credentials exactly as
the existing privacy/error tests require. Register the capability scheme only
for capability routes; preserve existing Bearer/default policy configuration.
Admin and external-contribution named policies must be equivalent to their
existing default/fallback, not new grants.

Work only in /tmp/works-timesheets-catalog-operation-20261101 and this spec.
No Git remote operations, commits, pushes, release/publication, nested module
initialization or shared-root source edits. Registry restore and local affected
verification are authorized. Read relevant tracked .editorconfig/build/architecture
configuration before changes, hydrate only owned sparse files if needed.
Source/API facts must use actual selected public SDK3.130.1/3.130.2; no reliance
on unpublished private SDK APIs. Broad toolchain/directory updates are excluded.

The SDK owner temporarily owns an actual selected capture lane (PTY2441 at
handoff). Do source inspection/implementation first; do not launch local native/
container work. Send parent a message before substantial compilation/testing so
it can coordinate resource use. No tests may be skipped or expectation changed
merely to pass. Return a complete source diff including untracked files, exact
commands/outcomes, unchanged original method inventory plus phase/cumulative
deltas, and remaining acceptance risks. Parent owns independent reviews and CI.

## Technical Map Correction — Preserved Workload Admission

Public3.128.4 source717a06e16c3afd81b904eaf4de646b74b13262d2 explicitly
uses CreateAnyWorkloadPolicy(EventStoreWorkload) as fallback for unclassified
external routes. Current3.130.1 exposes the equivalent already registered named
EventStoreDomainServicePolicies.AnyWorkload policy. The previous Code Map's
external-Human equivalence was false; correcting that non-frozen implementation
assumption preserves the approved intent, rather than changing authorization.
Use AnyWorkload on the external contribution group, keep its original signed
workload fixture calls/expectations, and preserve every server gate. No new
permission is invented. Human remains the selected admin/metadata policy.
Any newly added human-only external test must be corrected to the actual legacy
workload policy contract; no original test or expected effect may be weakened.


## Implementation Handoff — 2026-11-01

Owner remains clean-baseline `8b70017e13fa6f6d5af688b1cc40f7f41b24096a`
plus the implementation diff; no commit, remote operation, dependency-version
change, nested initialization, shared-root source edit, native/container lane,
or production call was performed.

The capability scheme loads canonical token state once and retains it only in
request-local features. Its authenticated identity has no claims. Existing
canonical action/state/disclosure checks, `AuthorizeRetryAsync`, commit-time
revalidation, default-deny services and the internal-port guard remain intact.
Submit commands use the framework JSON reader once before credential lookup to
preserve binding400/415, privacy and cancellation behavior; the endpoint reuses
the typed command. Credential denials retain opaque403, backend faults retain
opaque500, and request cancellation propagates. Metadata/admin name the existing
Human policy; external contributions name the existing AnyWorkload policy and
retain all original signed-workload requests and stored-effect expectations.
The SDK shared JWT handler/configuration completes the previously absent Bearer
scheme without changing authentication defaults.

Existing catalog-fixture APIs were verified in the actual public3.130.1 NuGet
source identity `1de42b706f52af0da6513f0b7663002606d17156`, including
`RouteCatalogHostOptions.InvocationTransportFactory` and
`RouteCatalogDomainObservation`. Only stale source-only conditional compilation
was removed: the fixture retains real prepare/ready/commit, authority, receiver
and transport checks. Its11 existing refusal cases now run in package mode.

The explicit finite Release/public-package build against the parent's exact
exported f0 catalogue passed with0warnings/errors. Final supported single-dash
assembly method selectors ran all170 attributed methods in14 boundary/persistence
files: **918passed,0failed,0errors,0skipped,0not-run** in236.210seconds. Every one
of the seven matrix rows has passed method coverage recorded in the receipt.
`git diff --check` passed. The complete affected project and auth/architecture
gates remain parent-owned; the acceptance checkbox for all existing/new cases
stays pending that broader verification and fresh hosted CI after commit.

Original attributed source methods:247; current:253, with all247 retained.
Final compiled discovery:253methods/1022cases. Compared with the parent's actual
original CI998cases (631passed/362failed/5existing skips), the cumulative delta is
24cases:13 from6new methods and11 from1newly enabled existing method.
Before fixture enablement, actual public discovery was252methods/1011cases.
No original local compiled discovery or full original CI log is claimed; the
attempted local original log was empty.

Evidence paths (outside the source tree):

- Complete18-file patch, including all5untracked source files:
  `/tmp/timesheets-auth-complete-source-final.diff`, SHA256
  `2f9c4900b27adbfc6010b12c0cdbf84d1f90776d37378b32d377150cd82439cb`.
- Immutable ownership/path/hash/command/count/matrix receipt:
  `/tmp/timesheets-auth-final-handoff-receipt.json`.
- Exact final argv and full selected-source/method inventory:
  `/tmp/timesheets-auth-final-focused-command.json` and
  `/tmp/timesheets-auth-final-focused-methods.json`.
- Final passed output:
  `/tmp/timesheets-auth-final-focused.xml` and
  `/tmp/timesheets-auth-final-focused.log`.
- Full current discovery:
  `/tmp/timesheets-auth-final-discovery.json`.

Initial failures remain retained: phase1 focused913total/371failed; phase2
new/binding14total/5failed; phase3 focused917total/250failed from disabled
catalog activation. The phase2 mixed class/method selector ran0tests and supplies
no validation evidence. The intermediate phase4 broad-class run was deliberately
interrupted after the external Human assumption was disproved:141executed,
139passed,1real assertion failure plus1interruption cancellation; it is not final
evidence. Phase4's initial stale-else compile error is also retained. Their exact
logs/results and explanations are indexed in the immutable receipt.


## Review1 Triage Log

All three fresh reviewers returned final before triage. Every finding is individually classified before grouping; this is a patch-only route.

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| blind-1 | medium | patch | Confirmed accepted trailing slash matches the same endpoint but selects Adjust body and leaves Confirm feature absent. Select from matched canonical endpoint metadata, preserving valid confirm/adjust behavior. |
| blind-2 | medium | patch | Removing typed body parameters removed inferred body metadata. Restore actual confirm/adjust request schemas and JSON metadata using the smallest framework-supported composition that also preserves invalid-media/empty-body and credential responses; do not reintroduce the earlier synthetic415-route authorization regression. |
| blind-3 | medium | patch | Confirmed original required-body binder returns400 for bodyless POST with no Content-Type while the new pre-check returns415. Preserve framework body-presence ordering before media validation; add both submit cases. |
| blind-4 | medium | patch | SDK startup validates a named workload JWT contract that can bind dedicated Authentication:WorkloadJwtBearer. Timesheets newly binds human/default options from Authentication:JwtBearer without eager startup resolution, so valid dedicated workload configuration can conceal missing human settings until the new Human endpoint is called. Eagerly validate the human options with the already registered SDK validator, preserving its environment rules. |
| blind-5 | low | patch | New credential-denial diagnostics switch directly on case-sensitive raw request path; matched route identity is canonical even for casing/trailing slash. Use existing matched endpoint metadata for operation selection without logging caller path/query. |
| blind-6 | low | defer | Reviewer established unsupported-charset InvalidOperationException is inherited from the original framework body binder. No new regression or credential bypass; changing that shared binding policy is pre-existing work, separate from preserving current400/415 semantics. |
| blind-7 | low | reject | Existing genuine POST workload-assertion/bound gateway and durable submission controls remain; the same authentication handler always creates a claims-empty principal, verified from the host. The suggestion adds duplicate mechanism coverage without a demonstrated invented actor path. The accepted trailing-slash patch must still cover successful confirm and adjust POST binding. |
| blind-8 | medium | patch | README currently describes public routes as anonymous although metadata now deliberately requires Human and capability routes use opaque named credentials. Correct the consumer instructions and human/workload JWT sections, retaining dated history and deployed acceptance limits. |
| edge-1 | medium | patch | Independent reproduction of blind-1: route-suffixed confirm POST selects the wrong feature. Same minimal canonical matched-endpoint correction, with its own verdict retained. |
| edge-2 | medium | patch | Independent reproduction of blind-3: no-body/no-content-type changes400 to415. Same framework body-presence ordering correction. |
| edge-3 | low | patch | Independent diagnostic reproduction of blind-5: case-variant/slash-suffixed confirm/display storage faults are labeled adjust-submit. Bind fixed diagnostic operation to matched route metadata. |
| verify-1 | medium | patch | Verification Other finding independently compiled the unmodified handler and reproduced200 canonical vs500 trailing-slash against the same endpoint. Correct route-derived body selection and verify actual HTTP boundary requests. |

## Completion Evidence

Exact-source CI41343760954 passed at e186deca9371d2c50c572908aeb9084e26883b43, including the original 1,051 integration cases and the corrected current-SDK readiness assertion. Release41346432275 published v2.0.3; its publication-only retry passed after feed propagation. All six public archives independently passed HTTP200, exact ID/version/owning repository/producer and signature verification: `.artifacts/upgrade-release-orchestration-20261101/timesheets-2.0.3-public/verification.json`. This completes the authorization migration; later package-currency adoption remains separate.
