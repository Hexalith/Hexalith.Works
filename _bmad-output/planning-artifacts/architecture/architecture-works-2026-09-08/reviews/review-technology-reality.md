# Technology-Reality Review — Hexalith.Works Architecture Spine

- **Reviewed:** 2026-09-12
- **Target:** `ARCHITECTURE-SPINE.md` in this run folder
- **Lens:** Verify committed technology and brownfield-current claims against official current sources, the root-tracked repository state, and locally advanced submodule worktrees.
- **Verdict:** **FAIL pending two high factual corrections.** The named stack exists and is broadly coherent, and the spine now distinguishes most target work from current implementation. However, AD-03/R5 claim an executor-filtered query is current when the handler does not implement it, and AD-19 names only one of two live Aspire SDK pins. Dapr production fit and source/gitlink provenance also remain under-specified.

## Evidence Baseline

The root repository is `main` at `0cbc7c4`. The root tracks:

- `references/Hexalith.Builds` at `a32cb422749352cce8dec948aa3e78c8f00eb4cf`, while the checked worktree is locally advanced to `fa6472788c14301c2c91c6beb85a8215aae1022c`.
- `references/Hexalith.EventStore` at `6b0247acc0b3ef60eb00c0f9ac9cbd367f85da20`, while the checked worktree is locally advanced to `a568af4ec963d017ed4343a518d4fbd7d444ad84`.

The EventStore commits after the tracked gitlink modify planning documents, one host test, and sibling submodule pointers; they do not change the R11 idempotency, gateway, stream-read, or projection APIs used by this review. The Builds delta does change effective dependency versions, including Dapr .NET packages from `1.18.5` to `1.18.7` and .NET servicing packages from `10.0.11` to `10.0.12`.

Official-source checks:

| Claim | Reality check | Result |
| --- | --- | --- |
| .NET SDK `10.0.401`, runtime `10.0.12`, C# 14 are current | Microsoft's [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) lists SDK 10.0.401, runtime 10.0.12, and C# 14; the local `dotnet --version` resolves to 10.0.401 through the root's `latestPatch` policy | **Verified** |
| Aspire `13.5.3` exists, is stable, and fits net10 | [Aspire.Hosting 13.5.3](https://www.nuget.org/packages/Aspire.Hosting/13.5.3) is stable and compatible with net10; both root pins are 13.5.3 | **Verified, subject to H2's dual-source correction** |
| `CommunityToolkit.Aspire.Hosting.Dapr 13.5.0-preview.1.260825-0345` exists and fits Aspire 13.5 | The [package page](https://www.nuget.org/packages/CommunityToolkit.Aspire.Hosting.Dapr/13.5.0-preview.1.260825-0345) confirms it is prerelease, targets net10, and depends on Aspire.Hosting >= 13.5.0 | **Exists and compiles; production combination remains unconfirmed (H3)** |
| Dapr runtime `1.18.4` is current | The official [Dapr 1.18.4 release](https://github.com/dapr/dapr/releases/tag/v1.18.4) is marked Latest; Works still pins three `1.18.3` images in `DaprSelfHostedMtls.cs` | **Verified mismatch, already disclosed by the spine** |
| Dapr.Client `1.18.7` is current on NuGet | The [official package page](https://www.nuget.org/packages/Dapr.Client) lists stable 1.18.7, published 2026-09-11, and net10 compatibility | **Verified; tracked/advanced split is accurately stated** |
| xUnit v3 `4.0.0` exists and fits net10 | [xunit.v3 4.0.0](https://www.nuget.org/packages/xunit.v3) is stable and compatible with net10 | **Verified** |
| `Hexalith.Platform` exists and owns a current Works topology | The public [Hexalith.Platform repository](https://github.com/Hexalith/Hexalith.Platform) exists, but its [AppHost](https://raw.githubusercontent.com/Hexalith/Hexalith.Platform/main/apphost.cs) is a nine-line empty Aspire 13.4.6 scaffold and its [global.json](https://raw.githubusercontent.com/Hexalith/Hexalith.Platform/main/global.json) uses SDK 10.0.302 | **Existence verified; Works topology absent, correctly represented as target work** |
| EventStore R11 gateway and trusted-intent APIs exist at the tracked gitlink | `IIdempotencyIntentAdapter`, `IdempotencyReplayRetentionTier.Commit`, `SubmitCommandRequest.IdempotencyKey`, and gateway command submission all exist at `6b0247ac`; Commit retains terminal replay state for seven years | **Verified; Works does not yet bind an adapter or send an IdempotencyKey, as R11 states** |
| EventStore `FromSequence` is exclusive | Contract docs and `AggregateActor.ReadEventsRangeAsync` at `6b0247ac` start at `fromSequence + 1`; two Works readers still advance to `last + 1` | **Verified; AD-27 and the Recovery correctness gate state the correct target and defect** |

## Findings

### H1 — AD-03 and R5 overstate executor-filtered query availability

**Severity:** High  
**Disposition:** Autofix

`ARCHITECTURE-SPINE.md:102-109` marks AD-03 `[CURRENT]` and says an optional Executor PartyId narrows Assigned work while retaining the tenant's Queued pool. `ARCHITECTURE-SPINE.md:331` consequently marks R5 `Available`.

The live `WhatsNextQueryHandler.ExecuteAsync` (`src/Hexalith.Works/Queries/WhatsNextQueryHandler.cs:47-108`) reads only `QueryEnvelope.TenantId`; it never reads a query payload or an Executor PartyId. `AuthorizedItems` applies tenant filtering and ordering only. The current response is therefore the tenant-wide Assigned+Queued list for every request.

This is not merely missing proof: the claimed branch does not exist. Keeping `[CURRENT]` lets downstream units assume a request contract and behavior that the running adapter does not provide.

**Required correction:** Split AD-03 into verified-current ordering/tenant-wide behavior and target Executor PartyId filtering, or change the decision to `[ADOPTED] [TARGET]`. Mark R5 `Partial` until request parsing, assigned-party filtering, queued-pool inclusion, authorization, and result-filter tests exist.

### H2 — AD-19's Aspire version authority omits the pin that actually selects the AppHost SDK

**Severity:** High  
**Disposition:** Autofix

`ARCHITECTURE-SPINE.md:305-313` says Works `global.json` owns the current Aspire SDK pin. In reality the version is duplicated:

- `global.json:10` sets `Aspire.AppHost.Sdk` to `13.5.3`.
- `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj:1` independently selects `Aspire.AppHost.Sdk/13.5.3`.
- `BuildConfigurationTests.cs:23-44` explicitly says the project `Sdk` attribute is what decides the AppHost build and enforces equality because `global.json` alone can drift.

The two pins currently agree, but the authority rule is factually incomplete and directs a dependency updater to touch only one of them.

**Required correction:** Name the root `global.json` and AppHost project `Sdk` attribute as an atomic dual pin, with `P0_AppHostProjectPinsTheSameAspireSdkAndCliBundle` as the current drift guard. After R1, name the equivalent Platform pin set rather than the generic phrase “host SDK.”

### H3 — The selected Dapr/Aspire combination compiles locally but is not web-confirmed as a supported production combination

**Severity:** High  
**Disposition:** Gate, then defer implementation to R1

The spine correctly discloses that CommunityToolkit's Aspire 13.5 Dapr adapter is preview and normally development/test-only. The remaining problem is the escape hatch in AD-20: a time-bounded owner exception is not technology-fit evidence.

The toolkit package page identifies the selected build as prerelease and says its published compatibility note only covers an older Dapr runtime. Dapr's [support policy](https://docs.dapr.io/operations/support/support-release-policy/) states that only its documented runtime/SDK release combinations are tested together. The repository currently combines runtime `1.18.3`, root-tracked Dapr .NET SDK `1.18.5` or locally advanced `1.18.7`, Aspire `13.5.3`, and the preview toolkit adapter. Existing local tests establish project-specific evidence, but no Platform conformance lane exists and `Hexalith.Platform` contains no Dapr resources.

This does not invalidate local development. It does mean a production exception must require evidence for the exact four-part version set, not only an owner and expiry. Dapr runtime 1.18.4 is also a directly relevant servicing release: its notes include Scheduler/reminder recovery fixes, while Works' target architecture relies heavily on Scheduler-backed intents.

**Required correction:** Add exact runtime/SDK/Aspire/toolkit compatibility and the runtime 1.18.4 servicing decision to the R1/R6 proof before any production exception or Works-host removal. Keep the current `Target absent`/`Generic seam absent` statuses.

### M1 — The Stack section is reproducible for Builds but not for EventStore source

**Severity:** Medium  
**Disposition:** Autofix

`ARCHITECTURE-SPINE.md:518` distinguishes the tracked and advanced Builds states, but `:519` gives only the EventStore package pin `3.103.0`. Works source projects use the checked-out EventStore through unconditional `ProjectReference`s, and the root-tracked EventStore commit describes as `v3.103.0-40-g6b0247ac`; the local worktree describes as `v3.103.0-42-ga568af4e`.

No reviewed R11 API differs between those two commits, so this is provenance rather than behavioral drift. Still, a clean checkout and this local checkout are not identical, and the package label alone cannot reproduce the source evaluated for current availability.

**Required correction:** Record the root-tracked Builds and EventStore gitlink hashes plus the locally advanced hashes in the Stack seed or a short provenance note. Distinguish “published package pin 3.103.0” from “source used by project references.”

### M2 — Current servicing gaps are disclosed but not assigned a close condition

**Severity:** Medium  
**Disposition:** Add to readiness gate

The Stack accurately says root SDK `10.0.400` / current `10.0.401` and Works Dapr runtime `1.18.3` / current `1.18.4`. `latestPatch` makes the local .NET selection 10.0.401, but a clean environment can still satisfy the explicit 10.0.400 floor. Dapr images are exact and cannot roll forward.

Because Microsoft's current .NET page labels 10.0.12 a security patch and Dapr 1.18.4 is a current hotfix release, the architecture should not leave the refresh as an unowned observation.

**Required correction:** Add a dependency/readiness close condition that upgrades or explicitly risk-accepts the exact .NET and Dapr runtime patch set through AD-19's restore, Release build, focused integration, and parity evidence. Do not change versions only in prose.

## Confirmed Brownfield Fit

- All projects in the Structural Seed exist. The omitted AppHost, ServiceDefaults, bespoke `/project`, actor/reminder adapter, and local recovery implementation also exist and are accurately described as transitional.
- The current four-project governed-kernel allowlist in AD-18 is exact: Contracts, Server, Projections, and Reactor. The target dependency graph is not falsely described as implemented.
- `IExpectationResolver` and `IExecutorRouter` live in Contracts; `LiteralExpectationResolver` exists in Server; no concrete executor router exists. AD-18 matches the repository.
- R4 fan-out/rebuild epoch, R6 generic typed reminders, R7 generic process runner, AD-21 registry, AD-23 delegation service, and AD-26 durable target receipts remain absent target work and are visibly tagged as such.
- R11's present/absent split is accurate: EventStore provides the gateway and trusted idempotency-intent seam, while `EventStoreGatewayWorkCommandSubmitter` leaves `IdempotencyKey` null and Works has no durable effect receipt.
- The Dapr gateway identifier grammar accepts the `wrk-` plus uppercase Crockford Base32 shape in AD-26 and its maximum length is below the 128-character MessageId limit.
- `Hexalith.Platform` is a real, public, non-archived repository with the named maintainer role, but has no Works implementation; AD-20 does not overstate that current state.

## Gate Decision

The spine is technologically plausible and substantially better aligned with current reality than the prior architecture. It should pass this lens after H1 and H2 are corrected and H3 is made an explicit production-acceptance proof. M1 and M2 are low-cost provenance/readiness improvements and should land in the same update so the next review is reproducible from a clean checkout.

## Recheck — 2026-09-12

**Current verdict: PASS. No remaining Critical or High technology-reality findings.** This verdict supersedes the initial FAIL above for the revised 650-line spine. The official-source evidence baseline remains current on the recheck date, and the relevant live project and submodule states remain unchanged.

| Prior finding | Revised-spine evidence | Recheck result |
| --- | --- | --- |
| H1 — executor filtering overstated as current | AD-03 now separates the current tenant-wide Assigned+Queued query from the adopted target Executor PartyId filter (`:102-110`); R5 is `Partial; tenant query exists, Executor filter absent` (`:341`) | **Closed** |
| H2 — incomplete Aspire version authority | AD-19 now names both `global.json` and `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`, plus the equality fitness test (`:313-323`). Both observed pins remain `13.5.3` | **Closed** |
| H3 — exact Dapr/Aspire production fit unconfirmed | AD-20 still treats the toolkit adapter as preview and development/test-only; the Runtime compatibility gate now requires upgrade or accountable, time-bounded risk acceptance for the exact .NET/Aspire/CommunityToolkit/Dapr tuple, followed by restore, Release build, focused integration, and Platform parity (`:325-350`, `:613`) | **Closed as an explicit readiness gate; implementation evidence remains intentionally pending** |
| M1 — source provenance incomplete | The Stack now records EventStore tracked/local `6b0247ac`/`a568af4e` and Builds tracked/local `a32cb422`/`fa647278`, distinguishes package `3.103.0` from project-referenced source, and identifies the Builds Dapr SDK delta `1.18.5` to `1.18.7` (`:548-567`) | **Closed** |
| M2 — servicing observations lacked a close condition | The Stack retains the observed .NET `10.0.400` floor / local `10.0.401`, runtime `10.0.12`, and Dapr `1.18.3` / current `1.18.4` split; the Runtime compatibility gate now makes upgrade or explicit risk acceptance and verification mandatory (`:552-558`, `:613`) | **Closed** |

No newly revised statement introduces an unsupported current-version, named-technology, project-structure, or tracked-versus-advanced-submodule claim at Critical or High severity.
