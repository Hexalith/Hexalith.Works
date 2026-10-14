# Story 4.14 frozen-contract continuation

Investigated and verified 2026-10-14 from Works commit `1f33aa0b2b0f7273724569a6c7856c0d493b1e97` without changing runtime code, dependency pins, technical modules, frozen intent, or the two unchecked implementation tasks. The implementable Works consumer changes are already present. The two remaining acceptance gaps require technical-module capabilities described below; a passing characterization of a gap does not satisfy its acceptance criterion. The workflow remains in implementation.

## Producer-owned durable conflict disposition

The published EventStore 3.120.0 source revision is `9b26956f9e825fd6771b62a79c7b81323ba856cf`. Its owning source paths are:

- `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/SharedProjectionEpochCoordinator.cs`, `JournalAsync`: a different delivery digest at an existing journal position or durable receipt returns `SharedProjectionJournalResult.IdentityConflict` before a disposition write. The pre-existing receipt remains, but it is not evidence that the conflicting payload was quarantined.
- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/DomainProjectionDispatcher.cs`, the epoch-delivery path: `IdentityConflict` immediately returns `ProjectionDispatchReasonCodes.DeliveryIdentityConflict`. The `FoldCatchUpAsync` call is downstream and never receives that rejected delivery.
- `SharedProjectionEpochCoordinator` public package API: it exposes journal, catch-up, generation reads, status, checkpoints, and rebuild operations, but no consumer conflict-disposition callback or unresolved-conflict readiness signal. This was rechecked against `Hexalith.EventStore.Client@3.120.0` using `dotnet-inspect`, rather than assuming that the newer local source surface matches the frozen package.

Required producer behavior is an SDK-owned, tenant/family-scoped, fenced durable unresolved/quarantine disposition for every detected in-scope delivery identity conflict, plus a query-visible degraded or unavailable readiness signal that survives restart. The conflict must preserve the previously accepted family and checkpoint, and retries must converge on one logical disposition. An SDK-owned callback that persists Works parking through the same fenced writer could also supply this capability. Works cannot add physical-key writes, another writer, or a local ingress substitute to compensate.

`WorksSdkProjectionQueryTests.Restart_recovers_checkpoint_and_rejects_conflicting_redelivery` passed in this continuation. It explicitly observes `IdentityConflict`, restarts the coordinator, observes no pending delivery and no parking, repeats the conflict rejection, and obtains query metadata with lifecycle `Current`. Those observed results demonstrate the missing disposition/readiness behavior; they do not close the corrupt/gapped/conflicting matrix row.

The distinct `ConflictingSourceSequenceAtNewEnvelopePositionIsDurablyParkedAcrossRestart` test also passed. It covers a new SDK envelope that reaches the Works fold and conflicts with a previously folded source sequence. Its existing durable parking proof cannot substitute for the same-position SDK admission case above.

Consuming a changed EventStore producer requires an explicit revision of the frozen 3.120.0 constraint. No such revision or producer modification was made.

## Platform identity-reader admission contract

Current Platform revision `2594b89cb2efed226e6f24e5e3a7cdf4d993b5a8` retains the same reader-policy incompatibility. The relevant owning source paths and existing contracts are:

- `references/Hexalith.Platform/src/Hexalith.Platform.Identity/PlatformIdentityGatewayAdmission.cs`: `VerifiedWorkload` accepts only one authenticated `DaprInternal` identity and its `dapr_caller_app_id` claim. `AdmitAsync` requires that source in `PlatformIdentityOptions.ReaderSources`, successful tenant and RBAC validation, matching payload tenant/Party, and current target actor registry evidence before signing admission.
- `references/Hexalith.Platform/src/Hexalith.Platform.Identity/PlatformIdentityOptions.cs`: the explicit `ReaderSources` allowlist defaults to empty. A JWT `sub` alone does not enroll a reader.
- Frozen EventStore `src/Hexalith.EventStore/Controllers/QueriesController.cs`: canonical `/api/v1/queries` uses plain `[Authorize]`, requires a nonblank JWT `sub`, performs tenant/RBAC checks, and calls `IIdentityGatewayAdmission.AdmitAsync` for `ResolvePartyIdentity` before lookup. Its exact `IdentityAdmissionScope` contains tenant, domain, aggregate/Party, operation, correlation/logical request identity, and payload digest.
- Frozen EventStore `src/Hexalith.EventStore/Extensions/ServiceCollectionExtensions.cs`: `CreateHumanDefaultPolicy` authenticates the JWT bearer scheme only. A Dapr workload principal cannot satisfy that default policy.
- Frozen EventStore `src/Hexalith.EventStore.Server/Identity/IIdentityGatewayAdmission.cs`: the published seam is `RequiresAdmission(string domain, string operation)` plus `AdmitAsync(ClaimsPrincipal principal, IdentityAdmissionScope scope, byte[] payload, CancellationToken cancellationToken)` returning a signed proof or null.
- Parties.Client 1.2.1 source revision `34f57baccdf3e9fc2ec615f07499c6d274105851`, `src/Hexalith.Parties/Authorization/PartyIdentityAuthority.cs` and `src/Hexalith.Parties/Queries/PartyIdentityQueryService.cs`: Parties verifies the exact signed query proof before authoritative stream reads. A current human binding additionally requires admitted target actor, active compatible actor revision, current binding lifetime, and valid/readable custody. `ExpectedActorId` in the query payload supplies no authority on its own.

The missing supported composition must authenticate Works as an independently provisioned identity reader on a route the canonical gateway admits, enroll that reader under an explicit allowlist, retain tenant and RBAC checks, verify the exact target actor/Party/tenant against current registry evidence, and sign the exact request scope for Parties. This can use the existing published admission seam; a package-version change is not inherently required for this prerequisite. A JWT service credential without a matching admission policy remains insufficient, and a Dapr-only reader without a compatible canonical gateway route remains insufficient.

`WorksSdkProtectedHttpTests.ConfiguredServiceBearerAuthorizesProtectedExecutorQueriesWithSyntheticCurrentAuthority` passed: the protected Works SDK route, actual published Parties client, independent JWT service credential, and synthetic authenticated HTTP authority return only own Assigned and tenant Queued work. Its synthetic endpoint does not exercise Platform's real admission policy or Parties' authoritative source path. Missing, rejected, and expired authority, wrong actor/tenant/origin, and contaminated roll-ups also passed their denial checks. `FrozenSdkRejectsHumanBearerOnAuthenticatedQueryIngressBeforeWorksStateAccess` passed with HTTP 401 and no Works state access, preserving the frozen SDK's conflicting-credential rejection.

The query-authority task remains unchecked until the supported technical-module admission composition has its own successful consumer proof. No user/Party identity equality, payload privilege, manufactured Dapr principal, or Works-owned signer was introduced.

## Verification

The following Release package-mode integration build exited 0 with zero warnings and zero errors:

```bash
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Release --no-restore -warnaserror -m:1 -p:NuGetAudit=false -p:MinVerVersionOverride=1.0.0 -v:q
```

The focused SDK and complete context-named integration lane exited 0: 177 passed, zero failures/errors/skips/unrun cases.

```bash
dotnet tests/Hexalith.Works.IntegrationTests/bin/Release/net10.0/Hexalith.Works.IntegrationTests.dll -noLogo -class '*WorksSdkProtectedHttpTests' -class '*WorksSdkProjectionQueryTests' -class '*WorksSdkPackageContractTests' -class '*WorkItemSharedProjectionRebuildHandlerTests' -class '*WorkItemProjectionQueryAdapterTests' -class '*WorkItemReadModelGenerationQueryTests' -class '*IndexedPendingDateAwaitSourceTests' -class '*PendingDateAwaitIndexDispatcherTests' -class '*StreamReadingCascadeDescendantSourceTests' -class '*WorksSdkRecoverySourceTests' -result-xml /tmp/works-4-14-continuation-20261014.xml

dnx dotnet-inspect -y -- member SharedProjectionEpochCoordinator --package Hexalith.EventStore.Client@3.120.0 -n 100
dnx dotnet-inspect -y -- member SharedProjectionEpochCoordinator --package Hexalith.EventStore.Client@3.120.0 -m JournalAsync -v:d -n 200
```

Executed names and results are retained in [2026-10-14-continuation-focused.xml.gz](2026-10-14-continuation-focused.xml.gz). Compressed SHA-256: `abc87ecb9a265b54a318fa039e8fdd09e994e79507d4a8b112fbb84e6b17d9e3`. Uncompressed XML SHA-256: `7c0d53d23eedc046140c96817e9415a29dc670dd67530902017aa132d9694e01`.

The required baseline command `timeout 40s aspire start --non-interactive --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` exited 124 after printing `Starting Aspire AppHost in the background...` and `Stopping Aspire.` Both subsequent exact-target commands reported that no Works AppHost was running:

```bash
timeout 15s aspire describe --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj --format Json
timeout 15s aspire stop --non-interactive --apphost src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj
```

No healthy AppHost baseline, new live Redis result, source Debug result, full integration result, or technical-module admission success is claimed by this continuation. Existing production/AD-28 and deployed restore gates remain independent. The pre-existing Conversations gitlink and architecture test edits were preserved.

HEAD advanced externally during verification to `de94386f170245802c39626ebffcca5d33b0ea18` (`build: sync local changes via /pushall`). This agent performed no staging, commit, push, branching, or submodule update. Its only additions are this evidence note and the focused XML archive.
