# Story 4.14 implementation continuation

Verified 2026-10-14 against published EventStore 3.120.0 and Parties.Client 1.2.1. The story remains in progress. This continuation closes the Works transport's inability to use independently configured service authority and adds executable conflict evidence; it does not close the real Platform authority or SDK journal-conflict acceptance gaps.

## Implemented consumer changes

`WorksQueryOptions.PartiesGatewayBearerToken` configures an independently provisioned service credential on the published Parties typed HttpClient. Supply it through the host's secret configuration, for example `Works__Query__PartiesGatewayBearerToken`; there is no default credential. `WorksPartiesGatewayAuthorizationHandler` preserves an already configured outbound bearer and otherwise requires the inbound bearer. Query payloads supply no credentials or privilege. Gateway-only SDK ingress and its rejection of conflicting inbound human credentials remain enforced.

`ConfiguredServiceBearerAuthorizesProtectedExecutorQueriesWithSyntheticCurrentAuthority` exercises the actual Works host registration, protected SDK `/query`, published Parties client, and a synthetic gateway using real JWT authentication. It returns only the Executor's own Assigned and tenant Queued work in deterministic order, hides foreign Assigned detail, and verifies the persisted queue and checkpoints. Missing, rejected, and expired service authority deny both queries before Works state access; absent authority never invokes the identity endpoint, and rejected authority never reaches its authenticated handler. Synthetic current binding, lifetime, custody, and contamination tests remain covered. This proves the supported existing-client transport composition, not real Platform identity admission or provisioning.

`ConflictingSourceSequenceAtNewEnvelopePositionIsDurablyParkedAcrossRestart` submits conflicting evidence for folded source sequence 2 at SDK envelope position 3. It proves durable parking, preserves all prior queue/roll-up/topology/pending/history documents, retains visible source watermark 2, and advances the delivery checkpoint to 3 only after the parking disposition is persisted. A fresh coordinator and, in the live run, a fresh Dapr client read that disposition and return degraded query metadata without disclosing the parked item.

The existing restart test now also characterizes the separate SDK same-position digest-conflict limitation. After `IdentityConflict`, restart retains no pending delivery or parking, repeated conflict is still rejected, and Works queries still report Current. These passing characterization assertions describe a missing safety behavior; they are not matrix acceptance evidence.

## Remaining technical-module prerequisites

1. **Identity admission composition:** the current Platform `VerifiedWorkload` accepts only an authenticated `DaprInternal` principal with `dapr_caller_app_id`, while frozen EventStore `/api/v1/queries` uses the JWT bearer default policy. Rechecked Platform commit `416878a74975b1023b3a00ab5d35aad4ce6365d9` after the concurrent gitlink update: this reader policy is unchanged. A technical-module admission implementation must authorize an independently authenticated service reader under an explicit reader allowlist, tenant and RBAC checks, verify current target actor registry evidence, and sign the exact tenant/Party/actor query admission scope. EventStore already publishes `IIdentityGatewayAdmission`, and Parties already consumes its signed evidence; this can be composed at the technical-module host edge without inherently changing EventStore 3.120.0. Alternatively, retaining Dapr-only readers requires a supported gateway route/policy composition. Neither policy change nor real actor/custody provisioning was made in Works.
2. **Producer-owned conflict disposition:** published `SharedProjectionEpochCoordinator.JournalAsync` returns `IdentityConflict` before saving a conflicting disposition, and the named dispatcher returns `delivery_identity_conflict` before invoking `FoldCatchUpAsync`. Its public coordinator API exposes no consumer quarantine hook; its status/read API exposes no unresolved-conflict readiness signal. The producer must durably retain the conflicting identity and expose degraded/unavailable readiness, or provide an SDK-owned conflict callback that persists domain parking through the same fenced writer. Works cannot compensate with a second writer or physical-key writes. Consuming a changed producer API would require renegotiating the frozen 3.120.0 constraint; no such change is assumed here.

Both unchecked tasks and the original acceptance gaps remain open. Legacy-default hosting, the one-writer selection, package pins, original baseline, frozen intent, and production/AD-28 gates are preserved.

## Verification

All commands ran from the Works repository. Archived outputs, XML executed-test names, exact package identities, and SHA-256 hashes are indexed in [verification-2026-10-14.json](verification-2026-10-14.json).

```bash
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release --no-restore -warnaserror -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -v:q

MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build Hexalith.Works.slnx --configuration Release --no-restore -warnaserror -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -v:q

dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -noLogo -class '*WorksSdkProtectedHttpTests' -class '*WorksSdkProjectionQueryTests' -class '*WorksSdkPackageContractTests' -result-xml /tmp/works-4-14-service-authority-focused.xml

dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -noLogo -class '*WorkItemSharedProjectionRebuildHandlerTests' -class '*WorkItemProjectionQueryAdapterTests' -class '*WorkItemReadModelGenerationQueryTests' -class '*IndexedPendingDateAwaitSourceTests' -class '*PendingDateAwaitIndexDispatcherTests' -class '*StreamReadingCascadeDescendantSourceTests' -class '*WorksSdkRecoverySourceTests' -result-xml /tmp/works-4-14-current-context.xml

dotnet tests/Hexalith.Works.UnitTests/bin/Release/net10.0/Hexalith.Works.UnitTests.dll -noLogo -result-xml /tmp/works-4-14-current-unit.xml
dotnet tests/Hexalith.Works.ArchitectureTests/bin/Release/net10.0/Hexalith.Works.ArchitectureTests.dll -noLogo -result-xml /tmp/works-4-14-current-architecture.xml
dotnet tests/Hexalith.Works.PropertyTests/bin/Release/net10.0/Hexalith.Works.PropertyTests.dll -noLogo -result-xml /tmp/works-4-14-current-property.xml

tests/Hexalith.Works.IntegrationTests/run-sdk-rollback-restore.sh
git diff --check
```

Both Release builds passed with zero warnings/errors. The focused SDK/HTTP/package lane passed 47/47, the context/recovery lane 130/130, UnitTests 568/568, ArchitectureTests 268/268, and PropertyTests 3/3, with no failures, skips, or unrun tests. The Dapr/Redis script passed all 16 SDK projection tests and isolated complete restore/replay 1/1; this run enabled the optional Dapr fixture, including the new conflict parking test. The tracked working-tree whitespace check passed. No source Debug or full unfiltered integration result is claimed by this continuation; their earlier blockers remain recorded in the spec.

The mandatory Aspire baseline attempt `timeout 50s aspire start --non-interactive --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` exited 124 after printing `Starting Aspire AppHost in the background...` and `Stopping Aspire.` The subsequent exact-target `aspire describe --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj --format Json` reported no running Works AppHost. Other workspace applications were left untouched. The focused/package/isolated-store checks above followed the baseline fallback.

## Concurrent workspace change

HEAD advanced externally during this run from `1cca626746f41091c2db48ac48e6bd5644b9a4c6` to `65d4e1ff665e796e849ea102022422a45377ff0d`. That external commit already contains the initial protected-ingress denial and projection conflict test edits plus a Platform gitlink update. Later service transport and documentation changes remain in the working tree. The implementation agent performed no staging, commit, push, branching, dependency update, or submodule operation and preserved the original spec baseline.

## Parent acceptance audit

The parent read the complete baseline diff, checked the unchanged frozen requirements, and verified all eight original archived-output hashes plus 177 executed passing integration cases in XML. The rollback snapshot record was moved to its own `RedisSnapshotValue.cs` file to follow the Hexalith single-type rule. After that code change, `tests/Hexalith.Works.IntegrationTests/run-sdk-rollback-restore.sh` exited 0: the Release integration build had zero warnings/errors, all 16 live Dapr/Redis projection tests passed, and complete isolated restore/replay passed 1/1. The rerun is archived in `2026-10-14-parent-live-restore.log.gz` and indexed in the verification manifest.

The authority and complete matrix-test tasks remain unchecked. Build has not advanced to step-04 review: real Platform reader admission and durable SDK journal-identity conflict disposition remain unsatisfied under the frozen constraints.
