# Story 4.15 implementation evidence — 2026-10-15

This document preserves the archived `.3` implementation proof. Review iteration 1 corrections and fresh `.4` producer/consumer/runtime results are recorded separately in [review-iteration-1/implementation.md](review-iteration-1/implementation.md); the gates below do not by themselves validate that corrected state.

Implementation handoff for review; this document does not mark the story done. The approved spec and its complete frontmatter context were loaded. Existing Story 4.9/spec/sprint edits were preserved. No commit, staging, push, publication, deployment, submodule initialization or real-data admission occurred.

## Result and ownership

Works now has one exclusive opt-in SDK recovery mode (`Works:Recovery:UseSdkRuntime=true`, requiring `Works:Projection:UseSdkDelivery=true`). EventStore owns actor registration, periodic reminder discovery/recovery, process leases/checkpoints, target receipts, retry/parking, quarantine and readiness. Works owns complete authoritative stream reads, exact await/relationship context, domain folds and pure translations. Default legacy composition remains available and unchanged in behavior for historical unwitnessed commands.

The producer correction adds bounded read-only contextual translation and typed transient evidence failure; authenticated, fully bound ephemeral effect origin; default-preserving reminder ordinal/committed causation; and optional SDK-owned reminder target discovery. The target catalog validates complete pages before indexing, bounds each read and each pass, and persists unfinished continuation for restart. Cancellation, malformed/repeating pages, missing initial registration and expiry-only targets are covered. No Works periodic writer or replacement persistence runtime was added.

Explicit upstream-resolved UTC expiry/token/revision is additive on existing create/reschedule/event/state types. Scheduled expiry requires its exact current witness. Cascade expiry requires the separately authenticated delegated purpose. The host validates trusted effect workload, purpose/kind, addressed payload identity and command family. Source identity always uses committed envelope positions/MessageId, including after rejection-induced payload sequence divergence. New fields are omitted when null/default so historical golden bytes and the forty-type durable catalog remain compatible.

`POST /work/processes/revalidate` is protected with the existing process workload policy and delegates to SDK authorization/audit/revalidation. Its defaults deny; an operator host must supply current authorization, durable audit and its Dapr route policy. Quarantine remains durable until that authorized pipeline reruns the same validator. Malformed callbacks retain SDK unresolved state and never acquire legacy completion markers.

## Producers

Published EventStore **3.122.0** is the verified base, source `1dac529c4690910c89d4090784a7d46cb9433a9d`; planning retains its source/archive comparison and public package hashes. The checked-out producer base is `6394d0500e6e277b8975cb397c9f0389f9a16929`. Selected local candidate: **3.122.0-story415.20261015.3**. It is not a published producer. Nine immutable archives are in `.artifacts/story-4-15/feed`; [producer-provenance.json](producer-provenance.json) retains their hashes and the changed producer-file hashes. The earlier `.1` and `.2` candidates are superseded, never overwritten. Platform.Identity remains the separate `1.0.0-story414.20261014.5` test candidate/feed; Parties.Client/Contracts remain published `1.2.1`.

`Directory.Build.props` carries the selected SDK version through project references so Debug does not accidentally bind a checked-out SDK's older default version. Release resolves the candidate packages. The cold isolated consumer uses a disposable NuGet cache, rejects project libraries, maps SDK identities to the intended feed, and compares restored SDK archives with that feed. It runs a persisted projection/checkpoint/conflict-restart host proof plus the upstream Contracts, Client and DomainService public consumers, including all new public seams. This verifies the nine-package consumer dependency subset, not the entire upstream release inventory.

```bash
bash scripts/build-story415-sdk-candidate.sh
bash scripts/verify-story415-sdk-candidate.sh
```

Packing refuses an existing candidate identity. To rebuild changed producer content, select a fresh local identity before invoking it; do not overwrite `.3`. All three isolated public consumers and the cold host proof passed. No package was published.

## Executed gates

All commands below run from the Works root. Builds used the explicit configuration shown, individual test projects and built xUnit assemblies with single-dash filters. Final builds were clean with zero warnings/errors. Sanitized result excerpts are retained under [verification/](verification/); [results-manifest.json](verification/results-manifest.json) records original local output paths and SHA-256. Raw Aspire environment/describe data and credentials are excluded.

| Gate | Executed result |
| --- | --- |
| Works UnitTests, Debug | 573/573, zero skipped |
| Works PropertyTests, Debug | 3/3, each with 100 generated cases |
| Works ArchitectureTests, Debug | 269/269, zero skipped |
| Works IntegrationTests, Debug, excluding `*SmokeTests` | 669/669, zero skipped |
| Works IntegrationTests, Release, excluding `*SmokeTests` | 670/670, zero skipped |
| SDK DomainService.Tests, complete Debug suite | 921/921, zero skipped |
| SDK trusted-effect actor/admission/retention/controller lane | 67/67, zero skipped |
| Cold package-only consumer and upstream public probes | Host proof plus Contracts/Client/DomainService all passed |
| Works SDK Redis/Dapr live recovery | 1/1, zero skipped, three AppHosts, 245.817 s |
| Isolated 4.14 projection + complete-store rollback runner | 27/27 plus 1/1, with Redis/Dapr fixtures enabled |
| Previously failing legacy future-await fact, isolated metadata-only fixture | 1/1, zero skipped, 278.594 s; legacy runtime/authentication retained |

```bash
dotnet build tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj -c Debug
dotnet tests/Hexalith.Works.UnitTests/bin/Debug/net10.0/Hexalith.Works.UnitTests.dll
dotnet build tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj -c Debug
dotnet tests/Hexalith.Works.PropertyTests/bin/Debug/net10.0/Hexalith.Works.PropertyTests.dll
dotnet build tests/Hexalith.Works.ArchitectureTests/Hexalith.Works.ArchitectureTests.csproj -c Debug
dotnet tests/Hexalith.Works.ArchitectureTests/bin/Debug/net10.0/Hexalith.Works.ArchitectureTests.dll
dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Debug
dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests'
dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj -c Release -m:1 -p:NuGetAudit=false
dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -class- '*SmokeTests'
dotnet build references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/Hexalith.EventStore.DomainService.Tests.csproj -c Debug
HEXALITH_TENANTS_DOMAIN_ROOT="$PWD/references/Hexalith.Tenants/src/Hexalith.Tenants" dotnet references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests.dll
dotnet references/Hexalith.EventStore/tests/Hexalith.EventStore.Server.Tests/bin/Debug/net10.0/Hexalith.EventStore.Server.Tests.dll -class 'Hexalith.EventStore.Server.Tests.Actors.TrustedEffect*' -class 'Hexalith.EventStore.Server.Tests.Commands.TrustedEffect*' -class Hexalith.EventStore.Server.Tests.Controllers.TrustedEffectsControllerTests
WORKS_SDK_PROOF_EVIDENCE_PATH=/tmp/story415-live-sdk-framing-state.jsonl WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS=150 dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class Hexalith.Works.IntegrationTests.WorksSdkRecoveryPipelineSmokeTests
WORKS_APPHOST_BOUND_METADATA_STARTUP=1 WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS=150 dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -method '*Recovery_re_registers_a_still_future_await_that_later_fires'
WORKS_SDK_RESULT_DIRECTORY=/tmp/story415-rollback-results bash tests/Hexalith.Works.IntegrationTests/run-sdk-rollback-restore.sh
git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check
git -C references/Hexalith.EventStore -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check
```

A producer-only SDK build can change shared source output assembly versions; rebuild the complete consumer graph before running consumer tests. A no-dependency rebuild over mixed copied output versions exposed missing-assembly failures and was replaced by complete graph builds. The successful final runs above are the evidence. The final producer evidence-framing correction was repacked under a fresh `.3` identity; its SDK suite, cold consumer proof, Works consumer suites and live SDK fixture were rerun.

## Matrix and task traceability

| Approved scenario/task | Executed evidence and durable outcome |
| --- | --- |
| Duplicate/lost firing, restart, same-date re-suspension | Works `WorksSdkIntentTranslationTests.AllCurrentDatesAndExpiryHaveCommittedWitnesses` proves all current roles and a new suspension position/name at the same date. SDK `ReminderCoordinatorTests.DuplicateRegistrationKeepsOneReminder`, `CallbackReplayAfterRestartReturnsSameEffectAndReceipt`, callback-admission tests and ordinal tests execute in the 921-test suite. Live test suppresses initial wakeups and deletes a real scheduler reminder; actual accepted expiry/resume counts remain one. |
| Reschedule/stale expiry | `WorkItemExpiryWitnessTests` validates persisted create/reschedule/replay/current witness and rejects old/missing witnesses without clock reads. Ten protected HTTP origin/witness cases include stale-witness rejection. Live proof persists revision 1 then revision 2, asserts no expiry after the old due time and one expiry under the current schedule. SDK stale-callback and collision tests also execute. |
| Cascade/child-resume target commit before checkpoint | Actual Works adapters, real asymmetric delegation, protected origin handoff and real target actors run through three AppHosts. [persisted-states.json](verification/persisted-states.json) records parent checkpoint 2 with `effect-unknown`, receipt replay to 3; child checkpoint 3 with `effect-unknown`, receipt replay to 4. Target streams each contain exactly one accepted mutation. Receipt identity/purpose/causation is retained without result payloads or credentials. |
| Page boundary/corrupt/foreign evidence | `WorksSourceStreamReaderTests` covers gaps, metadata/count/head/format/payload/foreign failures and envelope/payload sequence divergence across pages. `WorksSdkRecoverySourceTests` covers selected-generation membership and terminal/expiry-only keyset discovery. `WorksSdkProcessRecoveryTests` persists checkpoint 1 and retry/quarantine for unavailable, unknown and foreign evidence; reconstructed runners retain it, then authorized audited revalidation uses the same reader/translator to reach checkpoint 2. SDK process/readiness/cancellation/catalog tests execute in the full suite. |
| Related-read outage/denied submission | Works reader uses the typed SDK transient contract; runner retry/parking and corruption paths execute. `TrustedEffectOriginTests` checks exact workload scheme/identity, tenant, target, type, bytes, causation, message binding, spoofed UserId/extensions, scope reset and concurrent isolation. Protected Works HTTP tests reject missing/foreign origin, wrong purpose/workload/kind/digest/target before emitting accepted facts. |
| Receipt confidentiality, old source floor, audit failure | Executed 67-test SDK lane includes `DeniedCallerCannotInspectReceipt`, `SemanticCollisionDoesNotHandleAgain`, `CollisionAuditFailureDoesNotMutateTarget`, `SourceBelowFloorDeniesBeforeSourceRead`, `UnknownFloorDeniesBeforeSourceRead`, `AttemptAuditFailurePreventsRetentionMutation`, `AuditFailurePreventsAuthorizedSubmission` and gateway proof/purpose/controller denial tests. Production admission is not weakened by the synthetic live authority. |
| Single runtime, protected callbacks, disposition and SDK ownership | Composition tests reject missing SDK projections and pre-registered legacy writers. SDK mode registers one effect transport and no legacy scheduler/dispatcher/handlers. Malformed callback tests preserve durable SDK quarantine without legacy marker calls; disposition ingress rejects absent/wrong transport/current authority. Works process-recovery tests prove failed audit prevents revalidation and healthy repair reruns the same validator. Architecture 269/269 preserves pure-kernel dependencies and forty-type catalog; only the pure SDK schedule-token codec is explicitly permitted. |
| 4.14 reader and complete restore preservation | Enabled isolated rollback runner executes its Redis projection lane, snapshots/restores the complete keyspace (including family/checkpoints/protocol markers), removes activated state and replays through the legacy writer. Deployment rollback still requires quiescing/restoring the complete store and matching fleet; in-place downgrade is not supported. |

## Live fixture boundary and startup triage

`WorksSdkProofHostingStartup` is test infrastructure loaded only through explicit `DOTNET_STARTUP_HOOKS` and hosting-startup settings, `WORKS_SDK_PROOF=1`, Development environment and the `works`/`eventstore` fixture roles. An ephemeral RSA issuer, semantic registry, retention/audit fakes accept only synthetic `sdk415-` tenants and `work-sdk415-` source/targets and the declared command/purpose families. Production workload/channel authentication and the real signed delegation verifier remain active. The fixture suppresses only synthetic event wakeups and injects one response loss after the actual target receipt. Its control writes use the SDK CAS policy. Fresh isolated Redis and a generated component file are reused across the three hosts and removed afterward; the shared development store is never protocol-activated or flushed.

The unchanged AppHost was inspected before edits: Dapr infrastructure was healthy, security unhealthy, dependent services waiting. A legacy reminder/cascade run completed **20/21**, zero skipped; `WorksReminderRecoveryPipelineSmokeTests.Recovery_re_registers_a_still_future_await_that_later_fires` hit the existing 600-second startup/readiness budget. EventStore's advisory admin metadata startup awaited absent sample/tenants and the not-yet-started Works binding before its listener. This old broad live lane is not claimed green. Its bounded failure summary is retained separately.

The synthetic fixture removes only absent sample/tenants metadata registrations and bounds each advisory metadata request at two seconds, mapping only its own deadline into the existing advisory-failure path. It retains the real Works binding and named projection discovery. It parameterizes the old test readiness helper's actor name to `WorksSdkReminderActor`. It activates projection writer protocol v2 only in its fresh Redis before commands. An initial fault-control rearm used first-write semantics over an existing key; the corrected SDK CAS fixture then passed the complete live scenario. No production startup/authentication behavior was changed to resolve fixture readiness. The previously failing legacy future-await fact was then rerun alone with `WORKS_APPHOST_BOUND_METADATA_STARTUP=1`: the startup hook installed only the advisory metadata setup (no RSA/delegation/retention/audit replacement and no SDK recovery flag). It passed 1/1, zero skipped, 278.594 s, including future re-registration and one later accepted resume across restart. This qualifies the fixture evidence; it does not claim the original unbounded advisory startup path is repaired.

The initial full SDK suite's two existing test-fixture failures were corrected without weakening assertions: a substituted projection handler now explicitly declares `OrdinaryDeliveryEnabled=true`; an explicit `HEXALITH_TENANTS_DOMAIN_ROOT` test override uses the already initialized root-declared Tenants project instead of initializing a nested submodule. Both isolated failures and the full 921-test rerun passed. Historical unwitnessed expiry keeps ordinary wrapper delegation; witnessed/authenticated paths retain strict addressed identity checks.

## Review correction: optional evidence framing

The reviewer supplied a byte-encoding ambiguity: nonzero ordinal `(4L << 32) | 0x41424344` with no source message previously appended the same bytes as zero ordinal with source message `"ABCD"`. Before the fix, two new real-runtime regressions failed: malformed intents collapsed into one retained quarantine record, and restored malformed entries at the same collection position shared their digest. `ReminderCoordinator` now tags each present optional field (`ordinal`, `source-message-id`) in both intent and entry evidence. Zero/null appends no extension bytes. No existing receipt or reminder identity changes.

`ReminderEvidenceFramingTests` executes both ambiguity cases across reconstructed coordinators, checks separate retained evidence/audit subjects and no target submission, and pins the two historical zero/null digest goldens. The old implementation passes the historical 2/2 cases and fails the ambiguity 2/2 cases; the correction passes all 4/4 and the complete SDK suite passes 921/921, zero skipped. The local candidate was advanced from `.2` to immutable `.3`; earlier archives remain intact. Commands and sanitized results are retained in `verification/` (the red run is expected pre-correction evidence, not a final failed gate).

```bash
dotnet build references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/Hexalith.EventStore.DomainService.Tests.csproj -c Debug -m:1 -p:NuGetAudit=false
dotnet references/Hexalith.EventStore/tests/Hexalith.EventStore.DomainService.Tests/bin/Debug/net10.0/Hexalith.EventStore.DomainService.Tests.dll -class '*ReminderEvidenceFramingTests'
```

## Remaining gates and limits

Implementation tasks and the approved synthetic matrix are implemented and exercised, pending independent review. The original broad legacy startup timeout remains separately recorded; its previously failing future-await scenario now passes under the explicitly bounded advisory-metadata fixture; no skipped fixture is counted as new SDK acceptance. Public producer publication, production authority/retention/expiry policy, real-data admission, Platform parity, transitional host removal and Story 4.16 remain separate gates. Complete rollback must include the new SDK reminder/catalog/process/receipt keys and all 4.14 family state. Root files preserve their tracked newline convention (new C# files/props use CRLF); EventStore C#/Markdown honor its explicit LF `.gitattributes`. Both CRLF-aware diff checks pass.

Final `.3` fixture cleanup confirmed `aspire ps --format Json` returned `[]` and no owned `works-sdk415-` or `works-rollback-` Redis container remained. All recorded original-log hashes, sanitized artifacts, producer source hashes and nine archive hashes were revalidated.
