# Architecture Spine Review — Technology Reality lens

- **Target:** `_bmad-output/planning-artifacts/architecture.md` (1197 lines; register AD-01…AD-25 binding, prose subordinate)
- **Reviewed:** 2026-09-08 · reviewer lens: "every committed decision web-researched or reality-checked, not asserted from training data"
- **Repo truth sources:** `global.json`, `references/Hexalith.Builds/Props/Directory.Packages.props` (Builds `v4.27.2-10-ga32cb42`), `Directory.Build.props`, `src/**/*.csproj`, EventStore submodule `v3.103.0-7-g7598f67c`, Commons `v2.30.0-19`, Tenants `v5.7.0-31`, `/home/administrator/projects/hexalith/platform` (`a66cdf3`, 2026-08-09)
- **Web truth sources:** nuget.org, github.com/dapr releases, docs.dapr.io, dotnet.microsoft.com, xunit.net, rfc-editor.org (URLs in the table)

## Verdict

**PASS WITH CONCERNS** — every named technology exists, every pinned version is current on the web as of today, and every EventStore route/seam the register *asserts as available* does exist in the submodule; the concerns are (1) AD-20's "single 13.5.x family" silently depends on a preview-only Dapr Aspire integration and an empty platform host on a different SDK band, (2) three AD-20 target seams (R4 fan-out, R6 reminders, R7 process-runner) do not exist in EventStore today and the register does not say so per row, and (3) the register itself breaks AD-19 by stating Aspire versions as "current".

## Claims table

| # | Claim | Where in doc | Repo truth | Web truth (URL) | Status |
|---|---|---|---|---|---|
| 1 | .NET 10, SDK pinned by `global.json`, `rollForward: latestPatch` | AD-19 (l.218), l.490, l.626 (historical `10.0.301`) | `global.json`: `10.0.400`, `latestPatch`, `test.runner = Microsoft.Testing.Platform` | 10.0.400 is the latest SDK band (Aug 11 2026); no 10.0.5xx — https://dotnet.microsoft.com/en-us/download/dotnet/10.0 | verified (prose table correctly labelled historical) |
| 2 | Platform host on the same stack | AD-20 | Platform `global.json`: `10.0.302` (≠ Works 10.0.400); Builds pins Roslyn 5.9.0 "must remain compatible with the compiler host pinned by global.json" | — | stale/unstated divergence (TECH-10) |
| 3 | Dapr runtime and Dapr .NET SDK are distinct fields; runtime pin platform-owned | AD-19, AD-20 R1, l.620, l.697 | .NET SDK: `Dapr.*` **1.18.5** (Builds props). Runtime: `daprio/dapr` / `daprio/sentry` **1.18.3** hard-coded in `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs:47,90,111`; Platform repo has **no** Dapr pin at all | Runtime latest stable **1.18.3** (Aug 14 2026), 1.18.4-rc.3 in RC, no 1.19 runtime — https://github.com/dapr/dapr/releases · .NET SDK latest stable **1.18.5**, plus **1.19.0-preview.2** (`Dapr.Actors.Next` rewrite) — https://github.com/dapr/dotnet-sdk/releases, https://www.nuget.org/packages/Dapr.Actors | verified pins; ownership statement not yet true (TECH-04, TECH-01) |
| 4 | Actor reminders Scheduler-backed by default since Dapr 1.15 | l.697 | AppHost composes an mTLS placement+scheduler pair at 1.18.3 | `SchedulerReminders` default true since v1.15 — https://blog.dapr.io/posts/2025/02/27/dapr-v1.15-is-now-available/ , https://github.com/dapr/dapr/issues/8045 , https://docs.dapr.io/concepts/dapr-services/scheduler/ | verified |
| 5 | Reminder durability conditional on Scheduler persistence/HA/backup | AD-11, R6 | Story 4.8 record (superseded 2026-09-08): live 4/4 incl. scheduler-fire resume; earlier lane skipped in plain `dapr init` sandbox | Scheduler: embedded etcd, HA = 3 replicas, scaling replica count risks data loss, periodic backup recommended — https://docs.dapr.io/concepts/dapr-services/scheduler/ | verified as stated; HA/backup still unbound (TECH-01) |
| 6 | Actor turn lock serializes same-item commands | AD-08, l.526 | `AggregateActor.cs` (EventStore.Server) — single actor per aggregate | Turn-based concurrency: no other method/timer/reminder runs until the current completes — https://docs.dapr.io/developing-applications/building-blocks/actors/actors-overview/ | verified |
| 7 | ETag conflict on atomic actor-state save is the fallback; bounded retry → `ConcurrencyConflict` | AD-08 | `AggregateActor.cs:973-1275` (`persistenceConflictRetryCount`, `maxPersistenceConflictRetries`, `ConcurrencyConflictException`); `ETagActor.cs` | Actor state stores must be transactional and ETag-capable — https://docs.dapr.io/developing-applications/building-blocks/state-management/state-management-overview/ | verified |
| 8 | Pub/sub at-least-once; ordering component-specific, never portable | AD-09 | `WorksDomainEventProcessor` + `s_consumedEvents` dedup (Story 4.7) | At-least-once for all components; no ordering guarantee stated — https://docs.dapr.io/developing-applications/building-blocks/pubsub/pubsub-overview/ | verified |
| 9 | Dapr mTLS with trust domain + namespace; Sentry | AD-24 | `accesscontrol.works.yaml`: `mtls.enabled: true`, `controlPlaneTrustDomain: "localhost"`, `trustDomain: "public"`, `namespace: "default"`; dev-only banner present | SPIFFE `spiffe://<trustdomain>/ns/<ns>/<appid>`, default trust domain `public` — https://docs.dapr.io/operations/security/mtls/ , https://docs.dapr.io/concepts/security-concept/ | verified |
| 10 | Deny-by-default app-level access control | AD-24 | `accesscontrol.works.yaml`: `defaultAction: deny`, per-caller allow list (`/process`, `/query`, `/project`, `/project/rebuild/shared/v1`, `/replay-state`, `/work/events`) | Policy matches trustDomain/namespace/appId from SPIFFE ID; mismatch → `defaultAction` — https://docs.dapr.io/operations/configuration/invoke-allowlist/ | verified |
| 11 | Aspire single 13.5.x family; Platform "currently 13.4.6" | AD-20 (l.238-242), l.628, l.1048 | Works: `global.json` `Aspire.AppHost.Sdk 13.5.3`, `Aspire.Hosting*` 13.5.3, `CommunityToolkit.Aspire.Hosting.Dapr` **13.5.0-preview.1.260825-0345**. Platform: `apphost.cs` `#:sdk Aspire.AppHost.Sdk@13.4.6`, empty builder, no packages | Aspire.Hosting / Aspire.AppHost.Sdk latest **13.5.3** (Aug 25 2026), no 13.6/14 — https://www.nuget.org/packages/Aspire.Hosting , https://www.nuget.org/packages/Aspire.AppHost.Sdk · Toolkit Dapr: stable **13.0.0**, only **13.5.0-preview.1** tracks 13.5 — https://www.nuget.org/packages/CommunityToolkit.Aspire.Hosting.Dapr | verified numbers; **missing-capability** for a stable 13.5 Dapr integration (TECH-02) |
| 12 | `verify-agents-host.sh` exists; `verify-works-host` to mirror it | AD-20 | `platform/eng/verify-agents-host.sh` exists (builds `apphost.cs`, fails closed on Agents hosting dirs). No `verify-works-host`, no Works mention in `apphost.cs`/README | — | verified / obligation honestly stated |
| 13 | xUnit v3 + Microsoft.Testing.Platform; "current v3 line defaults to MTP v2" | l.629, l.775 | `xunit.v3` **4.0.0**, `xunit.runner.visualstudio` 4.0.0, `Microsoft.NET.Test.Sdk` 18.9.0, `coverlet.collector` still referenced | xunit.v3 4.0.0 (Aug 15 2026) latest; requires MTP v2, drops MTP v1 — https://www.nuget.org/packages/xunit.v3 , https://xunit.net/releases/v3/4.0.0 | verified (TECH-05 note) |
| 14 | Shouldly, NSubstitute, FsCheck | l.775, l.957 | Shouldly 4.3.0, NSubstitute 6.2.0, FsCheck 3.4.0 (+ `FsCheck.Xunit.v3` 3.4.0 pinned but not referenced by PropertyTests) | Shouldly 4.3.0 latest stable (Jan 2025; 5.0 previews exist); NSubstitute 6.2.0 (Aug 2026); FsCheck 3.4.0 (Aug 2026) — https://www.nuget.org/packages/Shouldly , https://www.nuget.org/packages/NSubstitute , https://www.nuget.org/packages/FsCheck | verified |
| 15 | Fluent UI Blazor `5.0.0-rc.3` (historical), unused in v1 | l.631 | Pin `Microsoft.FluentUI.AspNetCore.Components` **5.0.0-rc.5-26219.1**; not referenced by any Works project | Stable 4.14.4; 5.0.0 **not GA**, rc.5 exists — https://www.nuget.org/packages/Microsoft.FluentUI.AspNetCore.Components | verified (TECH-06 note) |
| 16 | `AddEventStoreDomainService` / `UseEventStoreDomainService` | AD-20 R9, l.599, l.945 | `EventStoreDomainServiceExtensions.cs` (EventStore.DomainService); used in `src/Hexalith.Works/Runtime/WorksHost.cs:44,130` | — | verified |
| 17 | `/project` endpoint | R4, AD-22 | Mapped at `EventStoreDomainServiceExtensions.cs:233`; **also** `/project/v2` (l.242) and `/project/v2/reconcile` (l.265) exist and are not mentioned; Works access-control allows only `/project` | — | verified; doc names the legacy route only (TECH-03) |
| 18 | `/project/rebuild/shared/v1` Begin/Accumulate/Finalize/Stage/Commit | AD-16, l.700 | Route at l.375-380; `DomainSharedProjectionRebuildAction` = Begin, Accumulate, Finalize, Stage, Commit, Verify, Abort; phases Prepared/Accumulating/Finalized/Committed/Aborted; inventory fingerprint tracked (`ExpectedInventoryFingerprint`) | — | verified (Verify/Abort omitted, harmless) |
| 19 | Capture-through-Commit fence "not yet supplied by the EventStore API" | AD-16 | No rebuild-time fence; but per-delivery fenced reservations exist: `ProjectionDeliveryReservation.FencingToken`, `ProjectionDeliveryCompletion.Fenced`, `ProjectionDeliveryAdmissionResult` (EventStore.Server/Projections) | — | verified as missing; existing fenced-delivery seam unacknowledged (TECH-03) |
| 20 | `IDomainQueryHandler`, `IDomainProjectionHandler`, `IReadModelStore` + `ReadModelWritePolicy`, `IQueryCursorCodec`/`QueryCursorScope`, `CachingProjectionActor`, ETag actors, notifiers | l.571, l.671, baseline | All present: `IDomainQueryHandler.cs`, `IDomainProjectionHandler.cs`, `DaprReadModelStore.cs`, `ReadModelWritePolicy.cs`, `IQueryCursorCodec.cs`, `QueryCursorScope.cs`, `CachingProjectionActor.cs`, `ETagActor.cs`, `DaprProjectionChangeNotifier.cs` | — | verified |
| 21 | R11 "EventStore SDK generic gateway client" | AD-20 R11 | `IEventStoreGatewayClient` (EventStore.Client/Gateway) exists and is already what `EventStoreGatewayWorkCommandSubmitter` (`src/Hexalith.Works/Runtime`) consumes | — | verified (exists today) |
| 22 | R6 "EventStore SDK generic durable-reminder + reconciliation seam" | AD-20 R6, AD-25 | **No** reminder abstraction in EventStore (`IDurableReminder*`/`IReminderRegistrar*` = 0 hits; only dead-letter drain reminders inside `AggregateActor`/`Operations`). Reminders live in Works: `DateReminderActor : Actor, IRemindable` (`RegisterReminderAsync`, `DateReminderActor.cs:46`) | — | missing-capability, allocated (TECH-07) |
| 23 | R7 "EventStore SDK checkpointed process-runner seam" | AD-20 R7 | **No** such seam (`ProcessRunner` = 0 hits); `ICascadeCheckpointStore`/`CascadeCheckpoint` are Works-owned (`Recovery/Cascade/CascadeDispatcher.cs`) | — | missing-capability, allocated (TECH-07) |
| 24 | R4 "relationship-aware fan-out seam" | AD-20 R4, AD-22 | **No** fan-out seam (`FanOut` = 0 hits) | — | missing-capability, allocated (TECH-07) |
| 25 | `IWorkCommandSubmitter` / `EventStoreGatewayWorkCommandSubmitter` | R11 | Both exist in `src/Hexalith.Works/Runtime/` | — | verified |
| 26 | `Hexalith.PolymorphicSerializations` for every durable type | AD-01 | ProjectReference + CodeGenerators analyzer in `Hexalith.Works.Contracts.csproj`; Builds pin 1.19.2 | — | verified |
| 27 | `Hexalith.Commons` ID helper, sortable ULIDs | AD-02, AD-03 | `Hexalith.Commons.UniqueIds/UniqueIdHelper.cs` wraps `ByteAether.Ulid` with `MonotonicIncrement` | ByteAether.Ulid 1.4.0 latest (Jul 2026) — https://www.nuget.org/packages/ByteAether.Ulid | verified |
| 28 | Hexalith.Tenants membership as claim→tenant truth | AD-23 | `TenantAggregate.cs`/`TenantState.cs` (members), `GetUserTenantsQueryHandler`, `GetTenantUsersQueryHandler` | — | verified (existence); no membership-lookup API is bound in Works yet — honestly marked Story 4.9 |
| 29 | RFC 9457 ProblemDetails; `AddProblemDetails` not proof | l.684, VAL-M04 | `WorksHost.cs:49 AddProblemDetails()`, `:107 UseStatusCodePages()` | RFC 9457 obsoletes RFC 7807 — https://www.rfc-editor.org/rfc/rfc9457 | verified |
| 30 | Works ships no AppHost/ServiceDefaults (target); both still present until 4.9 | AD-20, l.600, l.636 | `src/Hexalith.Works.AppHost` (Sdk `Aspire.AppHost.Sdk/13.5.3`) and `src/Hexalith.Works.ServiceDefaults` exist; IntegrationTests reference the AppHost | — | verified as honestly stated |
| 31 | `Hexalith.EventStore.Operations` composed by the Works AppHost | R1 (dead-letter) | ProjectReference only; **no** `PackageVersion` for `Hexalith.EventStore.Operations` in Builds props (Admin/Aspire/Client/Contracts/DomainService/Gateway/Server/ServiceDefaults/SignalR/Testing are listed) | — | missing-capability for package-based platform composition (TECH-11) |

## Findings

### TECH-01 — [medium] — Reminder durability is reality-checked only against a single-replica self-hosted Scheduler; HA/backup and the actor-API horizon are unbound

**Issue:** AD-11/AD-25 commit Works' date-resume *and* expiry to Dapr actor reminders. The only live evidence is the Story 4.8 lane against the AppHost's single `daprio/dapr 1.18.3` scheduler container (record superseded 2026-09-08: scheduler-fire resume now passes; the same lane skipped in the plain `dapr init` sandbox for weeks). Nothing has exercised Scheduler HA (3 replicas), restart-with-persisted-etcd, or backup/restore, and the register defers all of it to "R6". Separately, the Dapr .NET SDK's next line (1.19.0-preview.2) is a from-scratch actor rewrite (`Dapr.Actors.Next`, source-generated proxies) while `DateReminderActor` is built on the classic `Actor` + `IRemindable` API; the spine records no watch item for that API shift.

**Divergence/risk:** "registration persists across supported failover" (AD-11) has no failover evidence yet; AD-25 doubles the load on the same unproven mechanism. A future SDK-line move could invalidate the reminder adapter's base-class contract without any register entry flagging it.

**Source:** architecture.md:137-149 (AD-11), 397-416 (AD-25), 265 (R6), 697.

**Corroborating evidence:** `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs:111` (single scheduler container 1.18.3); `src/Hexalith.Works/Reminders/DateReminderActor.cs:17,46`; `_bmad-output/implementation-artifacts/4-8-register-and-reconcile-date-reminders-durably.md:476,691`; https://docs.dapr.io/concepts/dapr-services/scheduler/ (HA = 3 replicas, scaling risks data loss, backup guidance); https://github.com/dapr/dotnet-sdk/releases (1.19.0-preview.2, Dapr.Actors.Next).

**Suggested disposition:** discuss — keep AD-11's conditional wording but add to R6's "proof before removal" an explicit HA/restart/backup scenario, and add an AD-19-style watch note that the reminder adapter targets the classic Dapr actor API (SDK 1.18 line) so the 1.19 rewrite is a tracked migration, not a surprise.

### TECH-02 — [high] — AD-20's "single 13.5.x family" silently rests on a preview-only Dapr Aspire integration and an empty platform host

**Issue:** AD-20 binds `Aspire.AppHost.Sdk` + `Aspire.Hosting*` to one 13.5.x family and says Platform "upgrades from its current 13.4.6 scaffold". Reality: (a) the only Aspire→Dapr sidecar integration the ecosystem uses is `CommunityToolkit.Aspire.Hosting.Dapr`, whose stable line is 13.0.0; only `13.5.0-preview.1.260825-0345` tracks Aspire 13.5, and that is exactly what Works pins — so a fully-stable 13.5 family is not attainable today and the register never names the toolkit or its preview status; (b) `Hexalith.Platform` is a nine-line single-file `apphost.cs` with an empty `DistributedApplication.CreateBuilder(args)` — no Dapr, no packages, no Sentry/placement/scheduler composition, no Works or EventStore reference — and its README asks for "Aspire CLI 13.4.6 or later"; (c) Platform builds on SDK 10.0.302 while Works/Builds build on 10.0.400.

**Divergence/risk:** Story 4.9's R1 "Hexalith.Platform apphost (13.5.x)" must re-create everything `DaprSelfHostedMtls.cs` + `HexalithEventStore.cs` do today (Sentry, mTLS control plane, access-control files, resiliency CRD, Redis) inside a repo that currently has none of it, on a preview dependency the register does not disclose. A reader of AD-20 alone would assume the platform is a near-ready host and that "13.5.x" is a stable graph.

**Source:** architecture.md:238-246 (AD-20 Aspire family + lane), 260 (R1), 1048.

**Corroborating evidence:** `global.json` (`Aspire.AppHost.Sdk: 13.5.3`); `references/Hexalith.Builds/Props/Directory.Packages.props` (`Aspire.Hosting 13.5.3`, `CommunityToolkit.Aspire.Hosting.Dapr 13.5.0-preview.1.260825-0345`); `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj` (references the toolkit); `/home/administrator/projects/hexalith/platform/apphost.cs:1` (`#:sdk Aspire.AppHost.Sdk@13.4.6`, empty builder); `platform/global.json` (10.0.302); `platform/README.md:25`; https://www.nuget.org/packages/CommunityToolkit.Aspire.Hosting.Dapr ; https://www.nuget.org/packages/Aspire.Hosting .

**Suggested disposition:** discuss — amend AD-20 to (i) name `CommunityToolkit.Aspire.Hosting.Dapr` as part of the family and state that on the 13.5 line it is preview-only (with the upgrade-on-stable trigger), (ii) state plainly that the platform host is an empty scaffold with no Dapr composition as of 2026-09-08, and (iii) fold the SDK band into the "move together" rule (see TECH-10).

### TECH-03 — [medium] — AD-16/R4 name only the legacy `/project` route and ignore the fenced `/project/v2` delivery seam that already exists

**Issue:** The register describes projection plumbing as "`/project` endpoint" and says the capture-through-Commit fence "is not yet supplied by the EventStore API". The submodule at `v3.103.0` also maps `/project/v2` and `/project/v2/reconcile`, and its server side already has a fenced-delivery model (`ProjectionDeliveryReservation` with a monotonic `FencingToken`, lease expiry, `ProjectionDeliveryCompletion.Fenced`). The shared-rebuild session already carries an inventory fingerprint (`ExpectedInventoryFingerprint`). None of this is acknowledged, so the "fence protocol" obligation in R4 is framed as greenfield when a partial substrate exists. Works' sandbox access-control policy allows only `/project`, so the v2 path is not even reachable in the current topology.

**Divergence/risk:** Story 4.9 R4 may re-invent a fence that EventStore is already growing under `/project/v2`, or conversely assume `/project` is the long-term seam when the SDK is moving to v2. The register's "Confidence: High in ... EventStore API facts" (l.1161) is overstated for this row.

**Source:** architecture.md:184-194 (AD-16), 263 (R4), 338-340 (AD-22 "single-aggregate `/project` path"), 1161.

**Corroborating evidence:** `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs:202-203,233,242,265,375-380`; `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Projections/ProjectionDeliveryReservation.cs:3-20`; `.../ProjectionDeliveryDiagnostics.cs:39,45`; `.../DomainSharedProjectionRebuildDispatcher.cs:500-501`; `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml` (operations list).

**Suggested disposition:** discuss — add one sentence to AD-16/R4 recording that EventStore v3.103 already exposes `/project/v2(+/reconcile)` with per-delivery fencing tokens and that the R4 fence protocol must be specified against that seam (or explicitly reject it), rather than against `/project`.

### TECH-04 — [medium] — The register violates AD-19 by stating versions as "current", and the Dapr-runtime-pin ownership claim is not yet true

**Issue:** AD-19's Rule: "this document never states a version as 'current'". AD-20 then says Platform "upgrades from its **current** 13.4.6 scaffold", "the mixed 13.4.6/13.5.3 graph", and "13.5.x family" — undated, in the binding register. AD-19 also says the Dapr runtime pin is "owned by the platform host"; today it is three hard-coded literals (`"1.18.3"`) in Works' own AppHost and the platform repo has no Dapr pin anywhere. The prose sections comply (the June table is labelled historical/superseded; l.697's "since Dapr 1.15" is a historical fact).

**Divergence/risk:** The register will rot exactly the way AD-19 was written to prevent (VAL-H05/TECH-01 of the prior gate); an agent reading "13.4.6" or "13.5.3" from the register has no date anchor. The "platform-owned runtime pin" statement misdirects anyone looking for where 1.18.3 is actually set.

**Source:** architecture.md:216-224 (AD-19), 238-242 (AD-20), 620.

**Corroborating evidence:** `src/Hexalith.Works.AppHost/DaprSelfHostedMtls.cs:47,90,111`; `/home/administrator/projects/hexalith/platform/apphost.cs` (no Dapr); `global.json`; prose scan of lines 455-1197 (only historical-labelled numbers found).

**Suggested disposition:** autofix — date-stamp the AD-20 numbers ("as of 2026-09-06: Platform `apphost.cs` = 13.4.6, Works = 13.5.3") and add to AD-19 "until Story 4.9 R1 lands, the Dapr runtime pin lives in `Hexalith.Works.AppHost/DaprSelfHostedMtls.cs`".

### TECH-05 — [low] — Test-tooling claims are accurate but the test projects carry a mixed VSTest/MTP package set

**Issue:** "xUnit v3 + Microsoft.Testing.Platform" and "current v3 line defaults to MTP v2" are verified (`xunit.v3` 4.0.0 requires MTP v2 and dropped MTP v1; `global.json` selects the MTP runner). However every test csproj still references `Microsoft.NET.Test.Sdk` 18.9.0, `xunit.runner.visualstudio`, and `coverlet.collector` (VSTest-era) alongside the MTP runner, and `FsCheck.Xunit.v3` is pinned centrally but PropertyTests reference plain `FsCheck`.

**Divergence/risk:** Not an architecture divergence; a hygiene item that already shows up as "no `dotnet test` in sandbox; run xUnit binaries directly" in the working notes.

**Source:** architecture.md:629, 775-778, 895.

**Corroborating evidence:** `tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj`; `tests/Hexalith.Works.PropertyTests/Hexalith.Works.PropertyTests.csproj`; `global.json`; https://xunit.net/releases/v3/4.0.0 .

**Suggested disposition:** defer — track in the VAL-M01 test-infrastructure story.

### TECH-06 — [low] — Fluent UI Blazor is pinned to a prerelease ecosystem-wide; harmless for headless v1 but the baseline calls it "V5"

**Issue:** The doc correctly marks `5.0.0-rc.3` as historical and Fluent UI as unused in v1. Repo pin is `5.0.0-rc.5-26219.1`; NuGet stable is 4.14.4 and 5.0.0 is not GA. The ecosystem baseline (`hexalith-llm-instructions.md`) states "Microsoft Fluent UI Blazor V5" as the stack.

**Divergence/risk:** None for Works v1 (no UI project references it). Worth one clause so the Theme 3+ horizon does not assume a GA library.

**Source:** architecture.md:631, 693.

**Corroborating evidence:** `references/Hexalith.Builds/Props/Directory.Packages.props` (`Microsoft.FluentUI.AspNetCore.Components 5.0.0-rc.5-26219.1`); https://www.nuget.org/packages/Microsoft.FluentUI.AspNetCore.Components .

**Suggested disposition:** ignore (note only).

### TECH-07 — [medium] — Three AD-20 target seams do not exist in EventStore today and the matrix does not say so row by row

**Issue:** R4 ("relationship-aware fan-out seam"), R6 ("generic durable-reminder + reconciliation seam"), and R7 ("checkpointed process-runner seam") name EventStore SDK capabilities that have zero presence in the submodule at `v3.103.0` (grep: `FanOut` 0, `IDurableReminder*`/`IReminderRegistrar*` 0, `ProcessRunner` 0; reminders/checkpoints are Works-owned types). R11's "generic gateway client" *does* exist (`IEventStoreGatewayClient`) and is already consumed. AD-16 is the only row that says "not yet supplied by the EventStore API"; the matrix's "Target owner" column reads as an allocation but never states current absence, nor cites an EventStore backlog item.

**Divergence/risk:** Story 4.9 acceptance for R4/R6/R7 is gated on another repository shipping three features with no referenced story or version target; "Missing reusable capability is added to the EventStore SDK first, never copied into Works" (AD-20) is honest in principle but unquantified. AD-22's rule ("ordinary delivery path must converge every ancestor without operator action") cannot be met until R4 exists.

**Source:** architecture.md:234-237, 263, 265, 266, 270 (matrix), 336-337 (AD-22 ownership).

**Corroborating evidence:** EventStore grep results (`references/Hexalith.EventStore/src`, no matches for the three seam names); `src/Hexalith.Works/Reminders/DateReminderActor.cs`, `src/Hexalith.Works/Recovery/Cascade/CascadeDispatcher.cs:22-28` (`ICascadeCheckpointStore`); `references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Gateway/IEventStoreGatewayClient.cs`; `src/Hexalith.Works/Runtime/EventStoreGatewayWorkCommandSubmitter.cs:14`.

**Suggested disposition:** discuss — add an "Exists in EventStore today?" column (or a per-row "absent at v3.103.0 — EventStore story ref/TBD") to the migration matrix; mark R11 as "exists" and R4/R6/R7 as "absent".

### TECH-08 — [low] — Dapr semantic claims are correct for the pinned 1.18 line

**Issue:** Verified against docs.dapr.io: at-least-once pub/sub with no ordering guarantee; actor turn-based concurrency; transactional + ETag actor state stores; Scheduler default for reminders since 1.15 with embedded etcd / 3-replica HA / backup guidance; Sentry-issued SPIFFE identity with trust domain + namespace; deny-by-default access-control policies keyed on trustDomain/namespace/appId. The sandbox policy uses Dapr's default `trustDomain: "public"` and `controlPlaneTrustDomain: "localhost"`, correctly labelled dev-only.

**Divergence/risk:** None found. Minor: the register never says which Dapr *runtime* line the semantics were checked against (by design, AD-19), so a future runtime move must re-run this check.

**Source:** architecture.md:105-126 (AD-08/09), 370-395 (AD-24), 697.

**Corroborating evidence:** URLs in claims table rows 4-10; `src/Hexalith.Works.AppHost/DaprComponents/accesscontrol.works.yaml`.

**Suggested disposition:** ignore.

### TECH-09 — [low] — Sibling-module seams (Commons ULID, Tenants membership, PolymorphicSerializations, RFC 9457) verified

**Issue:** `Hexalith.Commons.UniqueIds/UniqueIdHelper` generates monotonic ULIDs via `ByteAether.Ulid` 1.4.0 (AD-02/AD-03 "sortable ULIDs via Hexalith.Commons" — real). Tenants ships tenant-member state and user↔tenant query handlers (AD-23's "membership truth" exists, though no lookup is wired in Works yet — honestly gated to 4.9). PolymorphicSerializations is wired via ProjectReference + source-generator analyzer. RFC 9457 obsoletes 7807; `AddProblemDetails()`/`UseStatusCodePages()` are registered and the doc correctly refuses to treat that as conformance (VAL-M04).

**Divergence/risk:** None.

**Source:** architecture.md:49-55, 62-66, 344-368, 684, 806.

**Corroborating evidence:** `references/Hexalith.Commons/src/libraries/Hexalith.Commons.UniqueIds/UniqueIdHelper.cs:13-29`; `references/Hexalith.Tenants/src/Hexalith.Tenants.Server/Aggregates/TenantAggregate.cs`, `.../Queries/Handlers/GetUserTenantsQueryHandler.cs`; `src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj`; `src/Hexalith.Works/Runtime/WorksHost.cs:49,107`; https://www.rfc-editor.org/rfc/rfc9457 .

**Suggested disposition:** ignore.

### TECH-10 — [medium] — SDK band divergence between Works (10.0.400) and Platform (10.0.302) is outside AD-20's "move together" rule

**Issue:** AD-20's single-family rule covers only Aspire. Builds pins Roslyn 5.9.0 with the comment that tooling "must remain compatible with the compiler host pinned by global.json", and Works/Builds are on the 10.0.400 band; the designated platform host is on 10.0.302. When Platform composes Works, two repos will build the same graph with different SDK bands/analyzer hosts.

**Divergence/risk:** Analyzer/compiler drift (TreatWarningsAsErrors is on) and MTP runner differences between the conformance lane and the Works build; not a runtime risk but a "green in one repo, red in the other" risk for the very lane AD-20 makes the gate.

**Source:** architecture.md:216-224 (AD-19), 238-246 (AD-20), 490.

**Corroborating evidence:** `global.json` (10.0.400); `/home/administrator/projects/hexalith/platform/global.json` (10.0.302); `references/Hexalith.Builds/Props/Directory.Packages.props` (Roslyn 5.9.0 comment, `Microsoft.SourceLink.GitHub 10.0.400`); https://dotnet.microsoft.com/en-us/download/dotnet/10.0 (10.0.400 latest band).

**Suggested disposition:** discuss — extend AD-20's family rule: "`global.json` SDK band and `Hexalith.Builds` submodule commit move together with the Aspire family across Works and Platform".

### TECH-11 — [low] — `Hexalith.EventStore.Operations` has no central package pin, so package-based platform composition of the dead-letter seam is not yet possible

**Issue:** The spine says the external platform host "composes published modules" and R1 lists dead-letter handling among the topology the platform inherits. Works reaches `Hexalith.EventStore.Operations` only by ProjectReference; the Builds central package list pins every other EventStore package but not `Operations`, and Platform's `nuget.config` is nuget.org-only.

**Divergence/risk:** Minor blocker for R1/R8 if Platform composes from packages rather than a submodule checkout.

**Source:** architecture.md:260 (R1 dead-letter), 267 (R8), 976.

**Corroborating evidence:** `references/Hexalith.Builds/Props/Directory.Packages.props` (EventStore package list, no `Hexalith.EventStore.Operations`); `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`; `tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj`; `/home/administrator/projects/hexalith/platform/nuget.config`.

**Suggested disposition:** defer — resolve in Story 4.9 R1 (add the pin in Hexalith.Builds or state that Platform composes EventStore from its submodule).

## Summary by severity

| Severity | Count | IDs |
|---|---|---|
| critical | 0 | — |
| high | 1 | TECH-02 |
| medium | 5 | TECH-01, TECH-03, TECH-04, TECH-07, TECH-10 |
| low | 5 | TECH-05, TECH-06, TECH-08, TECH-09, TECH-11 |

Nothing in the document names a technology that no longer exists, and no pinned version is behind the web as of 2026-09-08 (Dapr runtime 1.18.3 / SDK 1.18.5, Aspire 13.5.3, .NET SDK 10.0.400, xunit.v3 4.0.0, NSubstitute 6.2.0, FsCheck 3.4.0, Shouldly 4.3.0). The concerns are about what the register leaves unsaid: a preview-only Dapr/Aspire dependency, an empty platform host on a different SDK band, three SDK seams that do not yet exist, and version literals stated as "current" against AD-19's own rule.
