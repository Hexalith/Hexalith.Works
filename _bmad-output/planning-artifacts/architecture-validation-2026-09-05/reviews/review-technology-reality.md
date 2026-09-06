# Technology / Reality-Check Review — Hexalith.Works Architecture

**Target:** `_bmad-output/planning-artifacts/architecture.md`  
**Intent:** Validate only; the source architecture was not modified.  
**Lens:** Verify committed framework, library, version, API, and runtime-behavior claims against the checked-out root and root-declared sibling repositories, then against current official primary sources where behavior or release state is unstable.  
**Review date:** 2026-09-05  
**Verdict:** **FAIL — changes required.** The EventStore API surface and several conservative design postures are real, but the document's “current sibling pins” spine is stale as of its own update date, its Dapr version conflates runtime and SDK, and two claimed runtime guarantees (shared-rebuild fencing and reminder recovery) are not supplied by the named technology/API as written.

## Date and evidence interpretation

The document was originally completed on 2026-06-14 and updated on 2026-09-05 ([architecture.md:5-6](../../architecture.md)). The version table labels itself a 2026-06-14 verification snapshot ([architecture.md:189-190](../../architecture.md)); those values can be retained as historical evidence only if they are clearly frozen and are not subsequently called “current.” They are currently repeated as active/current constraints at lines 61, 201-202, 453-455, and 600-601. This review therefore does not retroactively judge the June snapshot by September releases; it flags the unqualified “current” claims in a document deliberately updated after the live pins had changed. The root SDK change itself was committed on 2026-08-31 (`0904f06`), while the architecture update was committed on 2026-09-05 (`7e7ef4e`).

## Finding summary

| ID | Severity | Disposition | Finding |
| --- | --- | --- | --- |
| TECH-01 | High | update | The “current sibling pins” matrix is stale and internally contradictory for .NET, Dapr SDK, xUnit, and Fluent UI. |
| TECH-02 | High | discuss | Aspire ownership/version compatibility is asserted, not established; the live tree mixes AppHost SDK 13.4.6 with integration packages 13.5.3. |
| TECH-03 | High | discuss | Dapr Scheduler reminders are durable, but “by construction” recovery overstates the guarantee and the stated Dapr 1.18.4 runtime does not exist as a current runtime pin. |
| TECH-04 | High | discuss | The shared rebuild API provides marker-gated atomic reader visibility, but no capture-through-Commit delivery fence. |
| TECH-05 | Medium | update | “Dapr pub/sub is … not ordered” is too absolute; only at-least-once is portable, while ordering is component/configuration-specific. |
| TECH-06 | Medium | update | Two same-actor claims are turn-serialized by Dapr; an ETag conflict is a fallback failure path, not necessarily the loser mechanism. |
| TECH-07 | Medium | update | `AddProblemDetails` plus middleware does not by itself prove the architecture's RFC 9457 and tenant-context contract. |

## High findings

### TECH-01 — The active version spine no longer matches the live ecosystem

**Severity:** High  
**Disposition:** update

The document contains three incompatible .NET declarations: SDK `10.0.300` as a hard constraint ([architecture.md:61](../../architecture.md)), `10.0.301` in the “current” table ([architecture.md:194](../../architecture.md)), and `10.0.301` again in the target tree and compatibility conclusion ([architecture.md:453](../../architecture.md), [architecture.md:600-601](../../architecture.md)). The live root pins `10.0.400` ([global.json:2-4](../../../../global.json)), and every checked-out root-declared sibling with a `global.json` also pins `10.0.400`. Microsoft lists SDK 10.0.400 as the current .NET 10 SDK released 2026-08-11 on the [official .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

The same stale-current problem affects the rest of the table:

- The architecture names Dapr `1.18.4` ([architecture.md:195](../../architecture.md)); the shared central pins are the **.NET SDK packages** at `1.18.5` ([Hexalith.Builds/Props/Directory.Packages.props:139-146](../../../../references/Hexalith.Builds/Props/Directory.Packages.props)), matching the official [Dapr .NET SDK v1.18.5 release](https://github.com/dapr/dotnet-sdk/releases). Runtime and SDK versions must be separate fields; see TECH-03.
- The architecture names xUnit v3 `3.2.2` ([architecture.md:197](../../architecture.md)); the shared runner/framework/assert/extensibility pins are `4.0.0` ([Hexalith.Builds/Props/Directory.Packages.props:318-321](../../../../references/Hexalith.Builds/Props/Directory.Packages.props)), and Works consumes them through ordinary unversioned package references ([Hexalith.Works.UnitTests.csproj:12-16](../../../../tests/Hexalith.Works.UnitTests/Hexalith.Works.UnitTests.csproj)). The [official xUnit 4.0.0 release notes](https://xunit.net/releases/v3/4.0.0) also record the change from MTP v1 to MTP v2 by default, so “+ Microsoft.Testing.Platform” alone is no longer enough to describe runner behavior.
- The architecture names Fluent UI Blazor `5.0.0-rc.3` ([architecture.md:199](../../architecture.md)); the live shared pins are `5.0.0-rc.5-26219.1` ([Hexalith.Builds/Props/Directory.Packages.props:226-227](../../../../references/Hexalith.Builds/Props/Directory.Packages.props)). The official [NuGet package history](https://www.nuget.org/packages/Microsoft.FluentUI.AspNetCore.Components) confirms rc.5 was published before this review. Its v1 non-use remains correctly stated, but the asserted ecosystem pin is stale.

The root imports the sibling's central version file ([Directory.Packages.props:5-11](../../../../Directory.Packages.props)), so these are not speculative latest-version suggestions: they are the actual effective ecosystem pins. Replace all active duplicate versions with one authoritative pin table generated from or explicitly subordinate to `global.json` and `Hexalith.Builds/Props/Directory.Packages.props`. If the June values are retained, label them “historical snapshot; not implementation pins” and remove “current” from the September conclusions.

### TECH-02 — Aspire compatibility and ownership are not a coherent pinned contract

**Severity:** High  
**Disposition:** discuss

The table says Aspire is platform-owned and Works carries no AppHost pin ([architecture.md:196](../../architecture.md)), but the validation conclusion assigns Aspire `13.4.6` and calls all versions mutually compatible/current ([architecture.md:597-601](../../architecture.md)). The intended migration is acknowledged as incomplete ([architecture.md:204-207](../../architecture.md), [architecture.md:633-647](../../architecture.md)), yet the checked-out topology currently has:

- `Aspire.AppHost.Sdk` `13.4.6` in `global.json` ([global.json:9-11](../../../../global.json)) and explicitly in the Works AppHost project ([Hexalith.Works.AppHost.csproj:1](../../../../src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj));
- centrally supplied `Aspire.Hosting*` packages at `13.5.3` ([Hexalith.Builds/Props/Directory.Packages.props:109-121](../../../../references/Hexalith.Builds/Props/Directory.Packages.props));
- that AppHost consuming `Aspire.Hosting.Docker`, `Aspire.Hosting.Redis`, and the Dapr toolkit integration ([Hexalith.Works.AppHost.csproj:21-25](../../../../src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj)).

The official [Aspire.Hosting NuGet registry](https://www.nuget.org/packages/Aspire.Hosting/13.5.3) confirms 13.5.3 as the current package line. A mixed 13.4 SDK/13.5 hosting graph may build, but neither the architecture nor the live pin files prove that it is the supported compatibility set; “mutually compatible” is therefore unsupported. Name the platform owner, choose a single Aspire release family (or document a tested mixed-version exception), and make its topology gate authoritative before removing the Works-owned AppHost.

### TECH-03 — Reminder durability is conditional, and runtime/SDK versions are conflated

**Severity:** High  
**Disposition:** discuss

C2 says Scheduler-backed reminders are the default from Dapr 1.15, that Works is on “1.18.4,” and that reminders are “durable across crash/restart by construction,” with reconciliation covering lost firings ([architecture.md:263](../../architecture.md)); the readiness summary repeats “durable Dapr reminders” as solved ([architecture.md:709](../../architecture.md)). The default-selection portion is correct: Dapr's [Scheduler service documentation for 1.15](https://v1-15.docs.dapr.io/concepts/dapr-services/scheduler/) says `SchedulerReminders` became the default and that Scheduler stores jobs in embedded etcd. Current [actor reminder documentation](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-timers-reminders/) confirms persistence across actor deactivation and failover.

The stronger claim is not technology-guaranteed without deployment and failure-policy constraints:

- Official runtime releases currently identify [Dapr Runtime 1.18.2](https://github.com/dapr/dapr/releases) as the latest runtime; `1.18.4` is a Dapr **.NET SDK** release, not the runtime pin stated next to Scheduler behavior. The repository pins only .NET client/actor packages at 1.18.5 and delegates the local runtime to `dapr init`; the installed environment reports CLI 1.18.0/runtime 1.18.1. There is no checked-in production runtime pin proving “Works on 1.18.4.”
- Scheduler durability depends on the Scheduler's etcd persistence/HA/backup. The official Scheduler documentation explicitly discusses data loss during topology changes and recommends backups; the Works AppHost merely injects external placement/Scheduler addresses ([Program.cs:10-14](../../../../src/Hexalith.Works.AppHost/Program.cs), [Program.cs:74-86](../../../../src/Hexalith.Works.AppHost/Program.cs)).
- Current Dapr reminder documentation says the default callback failure policy is only three retries at one-second intervals. Works registers a legacy one-shot reminder without an explicit failure policy ([DateReminderActor.cs:19-20](../../../../src/Hexalith.Works/Reminders/DateReminderActor.cs), [DateReminderActor.cs:43-46](../../../../src/Hexalith.Works/Reminders/DateReminderActor.cs)). Its reconciliation service runs a bounded startup pass, five attempts by default, then returns without an ongoing retry/alert loop ([ReminderReconciliationService.cs:9-16](../../../../src/Hexalith.Works/Reminders/ReminderReconciliationService.cs), [ReminderReconciliationService.cs:37-60](../../../../src/Hexalith.Works/Reminders/ReminderReconciliationService.cs), [WorksRecoveryOptions.cs:19-29](../../../../src/Hexalith.Works/Runtime/WorksRecoveryOptions.cs)). A one-shot callback that exhausts delivery while the host remains alive is therefore not necessarily recovered until another restart.

Split Dapr runtime and .NET SDK pins; bind Scheduler persistence/HA/backup and an explicit reminder failure policy; and either add continuous reconciliation/alerting or narrow the guarantee to “reconciled on a later successful host startup.” “By construction” is only valid for Scheduler-persisted registration across supported failover, not for end-to-end eventual domain resume.

### TECH-04 — The shared rebuild API does not supply the claimed delivery fence

**Severity:** High  
**Disposition:** discuss

The architecture requires ordinary delivery to be quiesced or platform-fenced from sealed inventory capture through Commit and claims readers never observe partial state ([architecture.md:47](../../architecture.md), [architecture.md:105-106](../../architecture.md), [architecture.md:265](../../architecture.md)). It later marks this NFR “all addressed” and a key strength ([architecture.md:623-629](../../architecture.md), [architecture.md:708-710](../../architecture.md)).

The checked-out EventStore API verifies only part of this claim:

- The canonical `/project/rebuild/shared/v1` route exists and dispatches the documented Begin/Accumulate/Finalize/Stage/Commit protocol ([EventStoreDomainServiceExtensions.cs:374-400](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs)).
- `ReadModelBatchProtocol.ResolveVisibleAsync` returns the old complete value until a committed marker makes the candidate visible ([ReadModelBatchProtocol.cs:281-327](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Client/Projections/ReadModelBatchProtocol.cs)); this validates reader-atomic **promotion**.
- The shared dispatcher resolves only the rebuild handler and read-model/session stores, then switches directly over rebuild actions ([DomainSharedProjectionRebuildDispatcher.cs:20-99](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/DomainSharedProjectionRebuildDispatcher.cs)). Commit builds the batch and invokes the staging store ([DomainSharedProjectionRebuildDispatcher.cs:346-403](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/DomainSharedProjectionRebuildDispatcher.cs)). No lifecycle/fence token, writer epoch, capture watermark, or ordinary-delivery coordinator is part of that API.

Thus atomic visibility at Stage/Commit is real; capture-through-Commit exclusion is not. A live delivery after inventory capture but before promotion can still be absent from or overwritten by the staged manifest unless a separately owned fence exists. Bind the external fence protocol and owner (writer set, token/epoch, capture watermark, resume/catch-up behavior) before describing the NFR as addressed.

## Medium findings

### TECH-05 — At-least-once is portable; “not ordered” is not a universal Dapr fact

**Severity:** Medium  
**Disposition:** update

The architecture says “Dapr pub/sub is at-least-once, not ordered” and says this was resolved “by verification” ([architecture.md:252](../../architecture.md), [architecture.md:668-669](../../architecture.md)). Dapr's [pub/sub overview](https://docs.dapr.io/developing-applications/building-blocks/pubsub/pubsub-overview/) does guarantee at-least-once delivery across components. It does not impose a universal unordered semantic; broker/component configuration controls available ordering. For example, the official [GCP Pub/Sub component](https://docs.dapr.io/reference/components-reference/supported-pubsub/setup-gcp-pubsub/) exposes `enableMessageOrdering` and ordering keys.

The design decision is still correct: portable Works logic must not rely on ordering. Change the factual premise to: “Dapr guarantees at-least-once; ordering is component- and configuration-specific and is not a portable Works contract. Therefore projections are duplicate- and reorder-tolerant.”

### TECH-06 — Dapr actor serialization, not ETag conflict, normally decides two claims

**Severity:** Medium  
**Disposition:** update

The architecture says two racing claims yield “exactly one conflict loser” under ETag-backed optimistic concurrency, which is retried and re-handled into a domain rejection ([architecture.md:97](../../architecture.md), [architecture.md:116](../../architecture.md), [architecture.md:251](../../architecture.md), [architecture.md:397-400](../../architecture.md)). Current Dapr actor behavior is more specific: the official [actor runtime concurrency documentation](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-features-concepts/) says a single actor instance processes only one turn at a time under a per-actor lock. Two requests to one Work Item actor are normally serialized; the second observes the first committed state and can be domain-rejected without an ETag conflict.

The EventStore fallback is real: it atomically saves the event batch ([AggregateActor.cs:1024-1029](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs)), classifies save failure as a concurrency conflict, clears cached state, and re-handles up to the configured bound ([AggregateActor.cs:1031-1121](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs)); the default bound is one retry ([CommandConcurrencyOptions.cs:9-15](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Configuration/CommandConcurrencyOptions.cs)). Correct the mechanism and tests: require one accepted claim and one observable domain rejection under same-actor serialization, while separately testing the injected state-store conflict recovery/exhaustion path. Do not require a genuine ETag conflict for every two-request race.

### TECH-07 — The RFC 9457 contract is asserted beyond the configured evidence

**Severity:** Medium  
**Disposition:** update

The architecture repeatedly promises ProblemDetails / RFC 9457 with correlation and tenant context ([architecture.md:49](../../architecture.md), [architecture.md:87](../../architecture.md), [architecture.md:250](../../architecture.md), [architecture.md:367](../../architecture.md), [architecture.md:627-629](../../architecture.md)). The live host registers the framework's default `AddProblemDetails`, `UseExceptionHandler`, and `UseStatusCodePages` only ([WorksHost.cs:45-48](../../../../src/Hexalith.Works/Runtime/WorksHost.cs), [WorksHost.cs:92-96](../../../../src/Hexalith.Works/Runtime/WorksHost.cs)). Its only located test checks the `application/problem+json` media type, not RFC members or the required tenant/correlation extensions ([WorksDomainEventSubscriptionTests.cs:159-166](../../../../tests/Hexalith.Works.IntegrationTests/WorksDomainEventSubscriptionTests.cs)).

RFC 9457 is the current standard and obsoletes RFC 7807 ([RFC Editor](https://www.rfc-editor.org/rfc/rfc9457.html)). Microsoft documents `AddProblemDetails` as registering the default problem-details service and middleware behavior ([ASP.NET Core error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0)), but the ASP.NET Core repository still has an open backlog item to [support/verify the latest RFC 9457 details](https://github.com/dotnet/aspnetcore/issues/52414). Framework registration therefore proves a standard ProblemDetails shape, not this architecture's full RFC/conveyed-context contract. Make RFC 9457 an acceptance contract: configure a customizer/writer for safe correlation and tenant extensions, define which error paths it covers, and add conformance tests for media type, member types/defaults, status consistency, extensions, content negotiation, and no sensitive detail leakage.

## Claims verified against the live repositories

The following named technology/API claims are materially supported and do not require a finding:

- The EventStore domain-service starter API is real: `AddEventStoreDomainService` and `UseEventStoreDomainService` are the documented two-line hosting surface ([EventStoreDomainServiceExtensions.cs:27-43](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs), [EventStoreDomainServiceExtensions.cs:66-95](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs)). Works uses the assembly overload ([WorksHost.cs:38-43](../../../../src/Hexalith.Works/Runtime/WorksHost.cs)).
- EventStore exposes aggregate-local envelope `SequenceNumber` and assigns it during persistence ([EventEnvelope.cs:11-36](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventEnvelope.cs), [EventPersister.cs:97-137](../../../../references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Events/EventPersister.cs)). The architecture's instruction to use envelope sequence rather than pub/sub arrival order is technically grounded.
- The named projection substrate exists: `CachingProjectionActor`, `ETagActor`, and `IProjectionChangeNotifier`/Dapr notifier implementations are present under `references/Hexalith.EventStore/src/Hexalith.EventStore.Server` and `.Client`. The name-level claim at [architecture.md:239](../../architecture.md) is current.
- Dapr Scheduler became the default actor-reminder engine from 1.15, and reminders are persisted across actor deactivation/failover. TECH-03 narrows only the end-to-end durability/recovery promise.
- The root uses `.slnx`, central package management, .NET 10, xUnit v3-family packages with Microsoft Testing Platform, Shouldly, NSubstitute, and FsCheck. The categories are correct; TECH-01 corrects the exact active versions and xUnit/MTP generation.
- Fluent UI is unused by the v1 headless Works kernel. Its inherited pin remains prerelease; only the rc number is stale.

## Gate conclusion

The architecture should not be approved as technology-current until TECH-01 is corrected and TECH-02 through TECH-04 are bound as explicit, testable platform contracts. TECH-05 through TECH-07 are wording/acceptance corrections that prevent downstream teams from building tests against guarantees the frameworks do not actually make. The strongest parts—the EventStore Add/Use starter surface, envelope sequencing, atomic read-model promotion, and the conservative no-order-dependency posture—survive the review once the claims are narrowed to the evidence.

**Counts:** 0 Critical · 4 High · 3 Medium · 0 Low.
