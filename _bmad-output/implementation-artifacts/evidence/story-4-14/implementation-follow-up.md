# Story 4.14 implementation follow-up

Verified 2026-10-13 against frozen EventStore 3.120.0 and Parties.Client 1.2.1. The story remains incomplete because the current gateway/identity-authority composition does not support the required live Executor authority.

## Changes

- Gateway-only query ingress now includes `/query/` and case-insensitive route spellings admitted by ASP.NET routing.
- SDK detail queries report unavailable for a foreign roll-up identity or a roll-up status/source watermark inconsistent with the queue item used to authorize the Executor.
- The published Parties client and Works bearer handler now have synthetic HTTP transport tests for valid current binding, missing/rejected bearer, binding lifetime, custody, scope/actor mismatch, inactive/restricted/erased identity, and stale/degraded identity responses. Valid requests read the persisted SDK family and checkpoint; denied requests make no Works store calls.
- The real protected named projection endpoint accepts a synthetic valid delivery. Missing channel/assertion, wrong-operation, and foreign-caller requests then preserve all six persisted family documents, the checkpoint, the selected generation, and an empty delivery journal.
- A Release consumer test checks exact package identities and `type: package` in the executable dependency manifest. Parties.Client package 1.2.1 embeds assembly informational version 1.0.0, so that assembly attribute is not a package-version authority.
- Ordinary delivery preserves pinned persisted parking records instead of resetting them while refolding the tenant family. The regression checks a sequence-gapped parked aggregate followed by healthy delivery on another aggregate, persisted checkpoint/queue/parking state, and healthy-only results with degraded warning metadata. A complete authoritative rebuild can still reset parking.
- A two-aggregate overlap regression uses the published named `DomainProjectionDispatcher` to concurrently deliver a parent assignment and child date suspension during a sealed rebuild. Promotion, a fresh coordinator/provider, catch-up, and duplicate named redelivery preserve the full family, relationship topology, pending intent, both histories/parking records, source watermarks, and checkpoints. The same test runs against Dapr/Redis in the live fixture.

## Published rebuild phase distinction

The frozen coordinator's `JournalAsync` calls `EnsureCheckpointAsync` on durable admission, so source checkpoints can advance to the newly journaled position while the serving family's fold watermarks remain at the captured position. `CatchUpAsync` returns without folding in `Building`/`Capturing`. During those phases, the regression verifies available prior-generation list and detail responses marked stale.

After commit, frozen EventStore 3.120.0 `ReadAsync` and `ReadManyAsync` explicitly return `Value=null`, `IsStale=true`, `IsAvailable=false` in `CatchingUp` (also `Aborting`). The regression therefore verifies truthful unavailable/stale list and detail metadata after promotion and coordinator restart, then current generation-pinned results after both deliveries are folded. It does not introduce a Works generation selector or read the prior generation around SDK unavailability. The speculative query-guard edits made during investigation were restored; production query selection behavior is unchanged by this test addition.

## Current authority composition blocker

`FrozenSdkRejectsHumanBearerOnAuthenticatedQueryIngressBeforeWorksStateAccess` sends the correct sidecar channel token and query workload assertion plus a human bearer to the real SDK route. It receives HTTP 401 and records no Works store call. The receiver treats the two credentials as conflicting. Without that bearer, the current Works Parties forwarding transport denies before state access. The producer also removes Authorization before canonical invocation.

This does not establish that genuine service credentials are impossible with the published APIs. Parties.Client accepts an injected `HttpClient`, which could attach a separate outbound service credential. EventStore publishes `IIdentityGatewayAdmission.AdmitAsync(ClaimsPrincipal, IdentityAdmissionScope, byte[], CancellationToken)` and signed target-actor evidence, so independent trusted-reader admission is mechanically expressible without changing package versions. The currently supported composition is missing:

- Frozen EventStore source commit `9b26956f9e825fd6771b62a79c7b81323ba856cf`, matching the published Server package repository metadata, maps canonical `/api/v1/queries` with plain `[Authorize]`. `CreateHumanDefaultPolicy` authenticates only `JwtBearer`; Dapr workload principals are excluded. The controller also requires a literal `sub` claim and a signed identity admission proof for `ResolvePartyIdentity`.
- Existing `Hexalith.Platform.Identity/PlatformIdentityGatewayAdmission.cs` accepts a trusted reader only when `VerifiedWorkload` finds an authenticated `DaprInternal` identity and the source is in `ReaderSources`, then checks tenant/RBAC and actor registry evidence. An ordinary JWT service bearer cannot satisfy this existing source policy; a Dapr workload cannot reach the frozen canonical query route under its default policy.
- Frozen Parties source commit `34f57baccdf3e9fc2ec615f07499c6d274105851`, matching Parties.Client 1.2.1 repository metadata, verifies the gateway's exact signed query admission proof before source reads. Human resolution requires admitted target actor, active current actor revision, current binding, and custody evidence. The payload's expected actor is insufficient.

No currently configured supported service-credential/admission path was found in Works. Composing an approved gateway authority path remains open; changing package versions is not inherently required.

Published-client success tests run at the adapter-policy boundary against synthetic HTTP authority. They do not prove successful protected SDK Executor ingress. No credentials are manufactured from actor/Party equality, no security policy is overridden, and no producer/package pin is changed. The query authority task remains unchecked.

## Verification

The final integration build passed with zero warnings/errors:

```bash
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release --no-restore -warnaserror -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -v:q
```

The Release solution build passed with zero warnings/errors:

```bash
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build Hexalith.Works.slnx --configuration Release --no-restore -warnaserror -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -v:q
```

After the parking and two-aggregate overlap regressions, the focused SDK and context-named classes passed 172/172:

```bash
timeout 60s dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -noLogo -class '*WorksSdkProtectedHttpTests' -class '*WorksSdkProjectionQueryTests' -class '*WorksSdkRecoverySourceTests' -class '*WorksSdkPackageContractTests' -class '*WorkItemSharedProjectionRebuildHandlerTests' -class '*WorkItemProjectionQueryAdapterTests' -class '*WorkItemReadModelGenerationQueryTests' -class '*IndexedPendingDateAwaitSourceTests' -class '*PendingDateAwaitIndexDispatcherTests' -class '*StreamReadingCascadeDescendantSourceTests'
```

After strengthening the real HTTP ingress proof, its final class rerun passed 25/25, including the new durable no-effect assertion:

```bash
dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -noLogo -class '*WorksSdkProtectedHttpTests'
```

After the parking and two-aggregate overlap regressions, the live isolated Dapr/Redis projection suite passed 15/15, including both regressions against Dapr/Redis, and complete restore/replay passed 1/1; the script exited 0 and cleaned up its own fixture:

```bash
timeout 180s tests/Hexalith.Works.IntegrationTests/run-sdk-rollback-restore.sh
```

Command output is archived with SHA-256 checksums in [verification-output.json](verification-output.json). The [matrix audit](matrix-audit.md) records the two outstanding acceptance gaps. Original session paths: `/tmp/works-4-14-integration-build.log`, `/tmp/works-4-14-solution-build.log`, `/tmp/works-4-14-focused-tests.log`, `/tmp/works-4-14-http-tests.log`, and `/tmp/works-4-14-live-restore.log`.

## Baseline and remaining limits

`aspire start --non-interactive --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` stalled without registering a running AppHost. Exact-target `aspire describe --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj --format Json` and `timeout 30s aspire stop --non-interactive --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` reported no running AppHost. The newly launched CLI process was terminated and exited 0 with “Stopping Aspire.” Existing unrelated resources were left untouched. No healthy Aspire baseline is claimed.

The previously recorded source Debug dependency failures and full unfiltered integration-run stall were not re-run in this bounded follow-up. The successful package Release build and focused/live tests above are separate evidence. Live Executor authority, deployed fleet/source-archive restore proof, Platform R3–R5 admission, and AD-28 gates remain open. Legacy mode remains the default.
