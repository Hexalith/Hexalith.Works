# Story 4.14 planning evidence

Investigated 2026-10-13. This is planning evidence, not implementation or acceptance proof.

## Published producer

Story 4.10 records EventStore 3.108.1, source `b15ad59abca82d5980ef92a510c2379e05f4d46f`, [Release run 36025721331](https://github.com/Hexalith/Hexalith.EventStore/actions/runs/36025721331), 14/14 public packages, and a package-only R3–R4 test passing 1/1 with 13 library consumers plus one CLI consumer. Package-contract log SHA-256: `c28cc2ac186ef34390396ca8b6a9a4369a10063bfa92585e881d2e1c714ed2a7`. The producer story also records Dapr/Redis and 10,000-member proofs. AD-28 approval/restore gates remain independent.

Works imports `references/Hexalith.Builds/Props/Directory.Packages.props`, currently pinning EventStore 3.120.0 and Parties 1.2.1. Public DomainService 3.120.0 was downloaded: SHA-256 `7dc478d35a799dd9d9ad5079a67d48ed52c50936504d850a97cf6c23acdb27d4`, embedded source `9b26956f9e825fd6771b62a79c7b81323ba856cf`. It preserves the published epoch contract and strengthens query transport protection. Local EventStore source is newer: isolated package compilation must prevent unpublished API use. No producer edit is planned.

The following public-package inspections succeeded:

```bash
dnx dotnet-inspect -y -- type --package Hexalith.EventStore.DomainService@3.120.0 -t '*Projection*' -n 35
dnx dotnet-inspect -y -- member IAsyncDomainSharedProjectionEpochHandler --package Hexalith.EventStore.DomainService@3.120.0 -n 30
dnx dotnet-inspect -y -- member QueryEnvelope --package Hexalith.EventStore.Contracts@3.120.0 -k property -n 25
dnx dotnet-inspect -y -- member QueryResult --package Hexalith.EventStore.Contracts@3.120.0 -k property -n 40
dnx dotnet-inspect -y -- member SharedProjectionReadResult --package Hexalith.EventStore.Client@3.120.0 -n 25
dnx dotnet-inspect -y -- member IPartiesIdentityClient --package Hexalith.Parties.Client@1.2.1 -m ResolvePartyIdentityAsync -n 30
```

The installed tool rejects the inspection skill's older `--oneline` flag; use `-n` and table output.

## Implementation contracts

`IAsyncDomainSharedProjectionEpochHandler` supplies `EpochEnabled`, `OrdinaryWriterId`, `ControlIndexName`, `CreateScope`, and `FoldCatchUpAsync`. SDK dispatch validates `scope.Family == handler.ProjectionType` plus store/tenant/domain equality: use `works-runtime-v3` consistently. Registration comes from `AddEventStoreReadModelStore`. Named delivery supplies individual envelopes, not full replay. Read fold context through `SharedProjectionEpochCoordinator.ReadGenerationAsync` for the supplied generation, and return logical operations; never write physical keys or checkpoints.

`ReadAsync<T>`/`ReadManyAsync<T>` report selected generation, staleness, and availability. `ReadManyAsync<T>` has one value type, so related heterogeneous documents need a common serialization representation or an existing SDK pinned-generation facility. `GetControlIndexTenantsAsync` replaces Works' global pending-date tenant registry in SDK mode. Map query freshness into `QueryResponseMetadata`/lifecycle state, distinguishing unavailable from fresh empty/not-found.

FR20 at `epics.md:61` settles Executor and coordinator policy. Add an empty-by-default permitted coordinator workload list; arbitrary nonblank workloads, payload flags, and wire admin hints never grant access. SDK 3.120.0 protects `/query` using its route policy and verifies administrator hints. Gateway-stamped actor/workload/delegation fields are trustworthy only with gateway-only query ingress; route caller identity alone does not prove the original actor.

Use `IPartiesIdentityClient.ResolvePartyIdentityAsync(tenantId, new ResolvePartyIdentity(tenantId, requestedPartyId, verifiedActor), cancellationToken)` for current Party binding. Published client validates exact tenant/Party, binding actor, active/restricted state, lifetime, and custody. It calls canonical `/api/v1/queries`; its transport must preserve valid authenticated/delegated authority. Current Works Dapr invocation alone does not establish a human actor. Missing authority remains denied/unavailable. Do not duplicate the client's evidence validator or equate UserId with PartyId. The `AddPartiesClient` extension requires single-tenant configuration; prefer host-edge typed registration with immutable per-call tenant scope. Tests use synthetic authoritative fixtures; real authority provisioning and Platform proof belong to 4.16.

## Expanded code map

- `src/Hexalith.Works/Runtime/WorksHost.cs` maps custom `/project` before SDK activation. Select one writer and matching readers at startup, preserving legacy behavior and 4.15 reminder/Reactor composition.
- `src/Hexalith.Works/Projections/SharedRebuild/WorkItemSharedProjectionRebuildHandler.cs` replaces repeated aggregate histories but rejects ordinary delivery. Preserve its legacy route when adding the epoch path.
- `WorkItemSharedRebuildManifestBuilder.cs` in that directory currently writes queue/roll-up and deletes legacy keys. Extend the complete domain family to topology, pending-date tombstones, and parking. Epoch staging must retain pre-cutover rollback evidence.
- `src/Hexalith.Works/Projections/WorkItemProjectionEventDecoder.cs`, `WorkItemProjectionBoundarySanitizer.cs`, and pure `src/Hexalith.Works.Projections/Strategies/` folds provide payload/identity checks and transformations. Keep pure kernel dependency restrictions.
- `src/Hexalith.Works/Reminders/PendingDateAwaitProjection.cs` provides canonical pending-intent folding and clearing. Preserve source-watermark/tombstone semantics.
- `src/Hexalith.Works/Reminders/IndexedPendingDateAwaitSource.cs` reads unversioned registry/index/parking. Use SDK discovery and pinned reads, retaining authoritative candidate stream refolds and partial-failure diagnostics.
- `src/Hexalith.Works/Recovery/Cascade/StreamReadingCascadeDescendantSource.cs` separately reads membership/roll-up. Pin related reads, retaining conservative unavailable handling and exclusive stream pagination.
- `src/Hexalith.Works/Recovery/ChildCompletion/StreamReadingChildCompletionAwaitingParentSource.cs` reads authoritative streams only; preserve its existing cursor/identity fixes.
- `src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs` and `GetWorkItemQueryHandler.cs` select schema keys and return empty/not-found on missing data. Add authorization before reads, result checks, SDK selection, and truthful freshness.
- `src/Hexalith.Works.Projections/Strategies/WhatsNextQueryAuthorization.cs` supplies the pure result predicate seam. Extend it if needed without adding runtime dependencies.

## Verification commands

Before code edits, use the applicable Aspire lifecycle skill to start and inspect the exact Works AppHost. Do not initialize nested submodules. Record baseline blockers and use the Hexalith fallback ladder.

```bash
dotnet restore Hexalith.Works.slnx -p:Configuration=Debug
dotnet build tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj --configuration Debug --no-restore -m:1
dotnet tests/Hexalith.Works.IntegrationTests/bin/Debug/net10.0/Hexalith.Works.IntegrationTests.dll -class '*WorkItemSharedProjectionRebuildHandlerTests' -class '*WorkItemProjectionQueryAdapterTests' -class '*WorkItemReadModelGenerationQueryTests' -class '*IndexedPendingDateAwaitSourceTests' -class '*PendingDateAwaitIndexDispatcherTests' -class '*StreamReadingCascadeDescendantSourceTests'
```

Extend those existing classes; add `tests/Hexalith.Works.IntegrationTests/WorksSdkProjectionQueryTests.cs` for SDK mode and persisted provider proof. Add `tests/Hexalith.Works.IntegrationTests/WorksSdkPackageContractTests.cs` for an isolated package-only host consumer using the exact frozen producer versions. Assert durable roll-up/queue/topology/pending/parking/checkpoint state through capture/build/commit/abort, concurrent distinct aggregates, restart/redelivery, and corrupt/conflicting envelopes. Test commit between related reads, denied actor/Party/tenant/workload, spoofed privileges, and contaminated documents. Denied queries must access no Works state; denied projection ingress must have no durable effect. Exercise actual protected endpoints, not only direct dispatcher calls. Record one-writer and full-restore/replay rollback evidence in isolated synthetic stores.

```bash
dotnet restore Hexalith.Works.slnx -p:Configuration=Release
dotnet build Hexalith.Works.slnx --configuration Release --no-restore -warnaserror -m:1
dotnet test tests/Hexalith.Works.UnitTests --configuration Release --no-build
dotnet test tests/Hexalith.Works.ArchitectureTests --configuration Release --no-build
dotnet test tests/Hexalith.Works.PropertyTests --configuration Release --no-build
dotnet test tests/Hexalith.Works.IntegrationTests --configuration Release --no-build
git diff --check
```

Run each project individually. Repeat successful checks only for changes, failures, or unresolved concerns. No build/test results are claimed by this planning pass.

## Migration continuity

Preserve Works AppHost and ServiceDefaults until 4.16/4.9. EventStore's global v2 activation marker forbids in-place downgrade: rollback restores the complete pre-cutover store and fleet, then replays retained source. Keeping an old executable alone is insufficient. No production marker or deployment trigger is authorized by this story.

The prior draft noted Story 4.9's exclusive-cursor fixes; they remain required. Story 4.13's codec/target-receipt work and evidence remain unchanged; Story 4.15 adopts reminder/process/command seams separately. This is Works consumer proof and cannot claim Platform R3–R5 parity before 4.16.
