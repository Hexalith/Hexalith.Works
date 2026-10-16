# Story 4.15 resumed verification — 2026-10-16

The existing implementation and review follow-up are preserved. The user requested **“use latest version of eventstore and fix issues”** during this resumed run. Root owns the final independent workflow review and acceptance; the story remains in-progress pending that review. No publication, real-data admission, deployment, staging, commit, branch or nested submodule initialization occurred.

## Selected producer and dependency modes

Fresh official NuGet flat-container metadata confirms **3.122.0** is the latest stable and latest overall for all nine consumed SDK packages. [Public producer metadata](nuget-producer.json) records exact official index/archive URLs, timestamps, SHA-256 values and the common published repository commit `1dac529c4690910c89d4090784a7d46cb9433a9d`. The NuGet Gallery also identifies [DomainService 3.122.0](https://www.nuget.org/packages/Hexalith.EventStore.DomainService/3.122.0). The current SDK checkout `a2016e88f4d0da9b953fe6f4394f8b8fd4e27f72` descends from that published source.

The selected producer is fresh **unpublished local candidate `3.122.1-story415.20261016.10`**, packed from the current SDK checkout using the existing immutable-candidate helper. The public 3.122.0 package cannot compile this adoption: `IReminderTargetCatalog` is absent, confirmed by the compiler and dotnet-inspect. The already-approved current-source corrections supply the required seam; no Works persistence or runtime substitute was introduced.

The preliminary `3.122.0-story415.20261016.10` candidate packed successfully but NuGet correctly rejected it with NU1109: the preserved test-only Platform.Identity source project requires stable Gateway >=3.122.0, which outranks a 3.122.0 prerelease. The fresh next-patch identity satisfies that floor without disabling CPM or suppressing downgrade checks. Those nine preliminary archives and all [81 historical .1–.9 archives](archives-before.json) remain unchanged. [Candidate provenance](candidate-provenance.json) binds all nine selected archives to the exact pack-time checkout and 1,945 production source/project/configuration files. Test-only corrections made afterward have [separate hashes](corrected-sdk-test-sources.json); every production input remains unchanged.

Ordinary Debug uses all nine SDK source projects; ordinary Release uses all nine exact selected SDK packages. The user's existing test-only Platform.Identity sibling reference is preserved in both modes. Release continues to use published Parties.Client/Contracts 1.2.1; Debug uses their sibling source. This test-only Platform edge is explicitly distinguished from the SDK package-only graph and from the isolated package consumers. Historical .9 source/package/live evidence remains historical and is not relabeled.

## Corrections made during verification

- Parties' local SignalR.Common 10.0.13 floor duplicated the newly shared entry and failed restore with NU1506. A minimal Remove/Include pair keeps exactly one entry and preserves the security floor against both older and current shared catalogs; no version or warning policy changed.
- The Release-only package contract assertion used an unsupported Shouldly predicate overload. It now filters before `ShouldHaveSingleItem`, retains the Platform source expectation, and checks that every SDK library is a package at the exact selected candidate version.
- The SDK actor helper now forwards the full trusted invocation only for helper-created substitutes or explicitly opted-in caller substitutes. Six existing fixtures opt in; real invokers remain untouched. Existing receipt/event/snapshot/offboarding assertions remain intact. No production SDK source changed during this run.
- The Works ABI helper builds the current Contracts with its selected dependency closure and copies the selected SDK Contracts alongside it into the unchanged baseline consumer. The consumer binary still executes its original six constructor/deconstruction calls and historical payload hashes. This is necessary after the SDK assembly version advances from 3.122.0 to 3.122.1.
- Startup diagnostics now include Sentry, placement, scheduler and EventStore as well as Works/operations/admin. Each live log stream has a 500 ms slice inside the existing five-second overall budget; observed backlog is retained even when that slice expires, so a healthy first resource cannot hide later stderr.
- Current producer selection, provenance and the preserved test-only Platform source edge are recorded in the adoption/API/boundary documents and the spec's non-frozen change log. Frozen behavior and security intent, Story 4.9, Story 4.16 and deferred work remain unchanged.

## Verification

[Exact command results](results-manifest.json) bind 47 preliminary and final raw logs by SHA-256, with the command arguments, exit status and timing. Payload-bearing raw logs remain separately retained under `/tmp/story415-resume-20261016-re7solpv`; the review packet contains minimized manifests and redacted persisted states. [Executed test names](executed-tests.json) and [XML result bindings](test-results.json) establish zero failures, skips or unrun cases in every selected final lane.

| Final gate | Passing result |
| --- | --- |
| Current SDK DomainService full suite | 974 |
| Current SDK trusted-effect/security/snapshot lane | 105, including 13 snapshot migration cases |
| Current SDK client control-index/epoch | 37 |
| Source Debug integration, excluding live smoke classes | 786 |
| Selected-package Release integration, same selection | 786 |
| Unit / architecture / property | 589 / 270 / 3; 100 generated cases per property |
| Enabled Redis/Dapr callback and restart lane | 2 |
| Enabled isolated complete-store projection/rollback restore | 27 plus 1 |
| Cold package-only API consumers | Contracts, Client and DomainService, each with a fresh empty packages directory |
| Unchanged-binary compatibility | Published SDK reminder constructor/deconstruction and identity/hash; all six baseline Works constructors/deconstructions and historical hashes |

The final ordinary Debug/Release builds and suites pass after the guard-only correction; [dependency modes](dependency-modes.json) bind its exact nine selected SDK packages and the final Debug graph's nine SDK source projects. Selected builds and cold consumers have zero warnings/errors. [Consumer bindings](consumer-files.json) bind 430 current files. The actor test corrections are separately bound from immutable pack-time production inputs.

The raw log manifest intentionally preserves the corrected preliminary failures: public-package CS0246, duplicate CPM NU1506, prerelease/stable NU1109, the Release-only unsupported Shouldly overload, the full SDK suite's initially unset supported Tenants-root setting, six caller-substitute receipt fixture failures, and the ABI fixture's missing selected dependency closure. The two command-syntax mismatches were corrected using installed tool help. No failure was hidden by lowering an assertion, skipping a test, disabling CPM, changing a package security floor or suppressing a compiler/restore warning.

The baseline exact Works AppHost was started and inspected using Aspire, then stopped before builds. Its Sentry/placement/scheduler were healthy while the default profile waited for unhealthy Keycloak; that inspection is recorded in [baseline resource states](baseline-resources.json) and is not relabeled as a healthy default-profile proof. The enabled fixture supplies its existing explicit Development/synthetic setup. Current startup console snapshots were captured before teardown, and the fresh restart lane does not reproduce the previous unproven Sentry startup failure.

## Persisted runtime and matrix audit

The enabled lane passed both tests in 399.66 seconds before the final fixture-guard correction. Its exact source and binary inputs remain preserved; this runtime result is not relabeled as execution of the later guard. [Runtime input bindings](live-inputs.json) retain an immutable temporary source/configuration/binary archive with 434 inputs from the actual Debug run. [Persisted review states](persisted-states.json) contain all ten phases with fifteen payload fields redacted case-insensitively; safe payload-type/digest metadata remains available. [Runtime assertions](runtime-check.json) and the separately executed [root audit](root-runtime-check.json) assert every phase against the exact raw JSONL hash.

- Successful protected callbacks persist an armed reminder witness and process membership with both periodic writers disabled.
- A delegated expiry commits once against a child with a future scheduled witness while its parent remains at checkpoint 3 with a retained effect. Restart replays the `works.cascade-expire` Success receipt, advances checkpoint 4 and clears the plan.
- The canonical missing-child creation gap retains checkpoint 6/failure 7, no plan, zero sibling cancellations and Degraded readiness. After separate creation, five retained effects reach the target-commit boundary before source checkpointing.
- Restart produces five distinct Success receipts, one cancellation event per child, checkpoint 7 and no retained plan.
- An ordinary competing resume leaves one logical resume event. The trusted NoOp receipt loses its response at child checkpoint 3, then restart observes its replay, advances checkpoint 4 and clears the plan. Final discovery is complete for eleven retained sources with zero unresolved work.

[The matrix mapping](matrix.json) links each of the five approved rows to named tests that actually ran and passed. Existing early source/byte/decode/shape/witness/security checks remain covered by both current Debug and selected-package Release integration and the full SDK suites; the live lane supplies the persisted receipt/restart composition. Resource captures preserve actual startup transitions, not a fabricated all-healthy snapshot.

[Cleanup](cleanup.json) confirms no AppHost or owned synthetic Redis container remains. [Preservation checks](preservation.json) confirm unchanged frozen intent and preserved user-owned project/architecture files. All 81 historical archives, nine preliminary archives, nine selected archives and pack-time production/configuration inputs validate against their recorded hashes. CRLF-aware diff checks pass in Works, EventStore and Parties, and both modified shell helpers pass `bash -n`.

## Remaining gates

No implementation, current candidate verification or matrix runtime blocker remains in this dispatch. Root still owns independent workflow review and final acceptance, so the spec remains in-progress. Public producer publication, production authority/expiry/retention policy, real-data admission, Platform parity, transitional host removal and Story 4.16 remain separate gates. The three existing deferred SDK concerns remain separate work; this dispatch does not change that ledger.

The live fixture uses its existing explicit Development/synthetic asymmetric authority and dedicated disposable Redis. [Temporary configuration bindings](temporary-configs.json) preserve every committed ACL/mTLS line and add only the supported HotReload=false opt-out; the successful live run used those exact copies. The stricter post-live guard was separately exercised against all four unchanged actual copies and rejects four valid-YAML copies whose ACL has been dedented out of spec. No shared host limits or unrelated processes are changed.

## Post-live configuration-guard correction

Root reproduced an indentation bypass in the earlier trim-based copy guard: dedenting the ACL made valid YAML with no `spec.accessControl`, yet normalized lines still matched. The final guard compares ordinal lines without trimming or discarding blanks. It accepts either every original line unchanged, or exactly the three correctly indented HotReload lines immediately after `spec:`; every original ACL/mTLS line retains its exact scope. Eight focused cases use the actual preserved live copies and reject the four dedented ACL variants after parsing their changed YAML scope. [Post-guard bindings](post-guard-verification.json) distinguish this verification from the completed live run and preserve its exact [source delta](post-guard-live-delta.patch). No actual live configuration or SDK production input changed, and the final Debug/Release suites include these eight new cases. Concurrent unrelated tool-setting changes in other submodules are outside this dispatch and remain untouched.
