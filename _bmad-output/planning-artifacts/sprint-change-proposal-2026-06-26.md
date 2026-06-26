---
title: "Adopt HexalithEventStoreSecurityExtensions in Works AppHost"
status: implemented
created: 2026-06-26
trigger: "Use HexalithEventStoreSecurityExtensions to initialize the shared security service in the Aspire host."
scope: minor
---

# Sprint Change Proposal - Works AppHost Security Initialization

## 1. Issue Summary

The Works Aspire AppHost composed EventStore, EventStore Admin.Server, and the runnable Works domain service without initializing the shared EventStore security service. The EventStore platform now provides `HexalithEventStoreSecurityExtensions` in `Hexalith.EventStore.Aspire`; sibling modules already use `builder.AddHexalithEventStoreSecurity()` as the AppHost-level security entry point.

Trigger: direct user instruction on 2026-06-26 to initialize the security service in the Aspire host through `HexalithEventStoreSecurityExtensions`.

Evidence:

- `Hexalith.EventStore/src/Hexalith.EventStore.Aspire/HexalithEventStoreSecurityExtensions.cs` provides `AddHexalithEventStoreSecurity`, `WithJwtBearerSecurity`, `WithSecurityDependency`, `WithEventStoreClientCredentials`, and `WithOpenIdConnectSecurity`.
- `src/Hexalith.Works.AppHost/Program.cs` already referenced `Hexalith.EventStore.Aspire` but did not call `AddHexalithEventStoreSecurity`.
- The Works recovery runtime submits commands back to the EventStore gateway, so it needs EventStore client credentials when Keycloak-backed security is enabled.
- Targeted build verification exposed that the current `Hexalith.EventStore.Aspire` submodule requires `Aspire.Hosting >= 13.4.6`, while Works was still pinned to `13.4.5`.
- Integration restore exposed that the runnable Works host now consumes EventStore source requiring Dapr `1.18.4` and OpenTelemetry ASP.NET/HTTP instrumentation `1.16.0`, while Works was pinned lower.

## 2. Impact Analysis

Epic impact:

- Affects Epic 4: Shared Work Execution and Builder Runtime Validation.
- Story impact is limited to the Aspire host/runtime validation surface for FR-24 and FR-25.
- No new epic is needed.
- No epic resequencing is needed.

Artifact impact:

- PRD: no change. FR-24 already requires the Aspire host to wire Works and substrate dependencies.
- Architecture: no scope change. The AppHost remains the accepted technical component in this repository and continues to consume EventStore Aspire helpers. Version references are updated to the current `13.4.6` EventStore alignment.
- UX: no change. v1 remains headless and no production UI/security-hardening surface is introduced.
- Tests: update architecture/topology guard coverage so the AppHost cannot regress to direct Keycloak setup or no shared security initialization.

Technical impact:

- Works AppHost initializes `HexalithEventStoreSecurityResources? security = builder.AddHexalithEventStoreSecurity(...)`.
- When security is enabled, EventStore, Admin.Server, and Works receive JWT bearer configuration through `WithJwtBearerSecurity(security)`.
- Works also receives `WithEventStoreClientCredentials(security)` for reminder/cascade command reissue through the secured EventStore gateway.
- `EnableKeycloak=false` remains supported because `AddHexalithEventStoreSecurity()` returns `null` and the AppHost skips security wiring.
- Works aligns Aspire Hosting and AppHost SDK pins to `13.4.6`, Dapr pins to `1.18.4`, and OpenTelemetry ASP.NET/HTTP instrumentation pins to `1.16.0`, matching the checked-out EventStore submodule and sibling AppHost practice.

## 3. Recommended Approach

Selected path: Direct Adjustment.

Rationale:

- The change is AppHost composition only and does not alter the Work Item kernel, contracts, projections, reactor, PRD scope, or UX scope.
- The shared EventStore Aspire helper is the current platform pattern and avoids duplicating Keycloak/JWT wiring.
- Risk is low because the helper is nullable for `EnableKeycloak=false`, which preserves existing smoke-test behavior.
- Timeline impact is low: one AppHost edit, a required Aspire pin reconciliation, and a structural regression assertion.

Alternatives considered:

- Rollback: not viable; no completed story needs to be reverted.
- PRD MVP review: not needed; the MVP remains achievable.
- Copy local Keycloak composition into Works: rejected because it duplicates platform security topology logic already centralized in EventStore Aspire.

## 4. Detailed Change Proposals

### AppHost

File: `src/Hexalith.Works.AppHost/Program.cs`

Old:

```csharp
IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// EventStore command gateway + Admin.Server ...
IResourceBuilder<ProjectResource> eventStore = builder.AddProject<HexalithEventStore>("eventstore");
```

New:

```csharp
IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

HexalithEventStoreSecurityResources? security = builder.AddHexalithEventStoreSecurity(
    new HexalithEventStoreSecurityOptions
    {
        RealmImportPath = ProjectMetadataPaths.GetProjectPath(
            "Hexalith.EventStore",
            "src",
            "Hexalith.EventStore.AppHost",
            "KeycloakRealms"),
    });

IResourceBuilder<ProjectResource> eventStore = builder.AddProject<HexalithEventStore>("eventstore");
```

Rationale: Works should initialize local security through the shared EventStore Aspire helper. The realm import path reuses the canonical EventStore AppHost import rather than duplicating a large JSON realm file in Works.

Additional AppHost wiring:

```csharp
if (security is not null)
{
    _ = eventStore.WithJwtBearerSecurity(security);
    _ = adminServer.WithJwtBearerSecurity(security);
    _ = works
        .WithJwtBearerSecurity(security)
        .WithEventStoreClientCredentials(security);
}
```

Rationale: EventStore/Admin.Server validate JWTs when Keycloak is enabled. Works validates the same JWT settings and can obtain EventStore client credentials for host-edge reminder/cascade command reissue.

### Regression Guard

File: `tests/Hexalith.Works.ArchitectureTests/FitnessTests/RuntimeAdapterGovernanceTests.cs`

Old:

```csharp
program.ShouldContain("AddHexalithEventStore", Case.Sensitive, ...);
program.ShouldContain("AddEventStoreDomainModule", Case.Sensitive, ...);
```

New:

```csharp
program.ShouldContain("AddHexalithEventStore", Case.Sensitive, ...);
program.ShouldContain("AddHexalithEventStoreSecurity", Case.Sensitive, ...);
program.ShouldContain("AddEventStoreDomainModule", Case.Sensitive, ...);
program.ShouldContain("WithJwtBearerSecurity(security)", Case.Sensitive, ...);
program.ShouldContain("WithEventStoreClientCredentials(security)", Case.Sensitive, ...);
program.ShouldNotContain("AddKeycloak(", Case.Sensitive, ...);
```

Rationale: The architecture fitness test now enforces the shared EventStore security helper and rejects hand-rolled Keycloak setup.

### Runtime Version Alignment

Files:

- `Directory.Packages.props`
- `global.json`
- `src/Hexalith.Works.AppHost/Hexalith.Works.AppHost.csproj`
- `tests/Hexalith.Works.ArchitectureTests/FitnessTests/BuildConfigurationTests.cs`
- `docs/eventstore-api-surface-constraints.md`
- `_bmad-output/planning-artifacts/architecture.md`

Old:

```text
Dapr.* = 1.18.2
Aspire.Hosting = 13.4.5
Aspire.AppHost.Sdk = 13.4.5
Aspire.Hosting.Keycloak = 13.4.5-preview.1.26316.12
OpenTelemetry.Instrumentation.AspNetCore = 1.15.2
OpenTelemetry.Instrumentation.Http = 1.15.1
```

New:

```text
Dapr.* = 1.18.4
Aspire.Hosting = 13.4.6
Aspire.AppHost.Sdk = 13.4.6
Aspire.Hosting.Keycloak = 13.4.6-preview.1.26319.6
OpenTelemetry.Instrumentation.AspNetCore = 1.16.0
OpenTelemetry.Instrumentation.Http = 1.16.0
```

Rationale: `Hexalith.EventStore.Aspire` is referenced as source and now requires `Aspire.Hosting >= 13.4.6`. The Works AppHost must align to the checked-out submodule because Hexalith dependencies are project references, not NuGet package references.

## 5. Checklist Outcome

| Item | Status | Notes |
|---|---:|---|
| 1.1 Triggering story | [x] | Runtime validation/AppHost story surface for FR-24/FR-25. |
| 1.2 Core problem | [x] | Technical drift from shared EventStore Aspire security helper. |
| 1.3 Evidence | [x] | Helper exists in EventStore Aspire; Works AppHost omitted it. |
| 2.1 Current epic still valid | [x] | Epic 4 remains valid. |
| 2.2 Epic-level changes | [N/A] | No epic scope change. |
| 2.3 Future epic changes | [N/A] | No future epic impact. |
| 2.4 New/obsolete epics | [N/A] | None. |
| 2.5 Priority/order changes | [N/A] | None. |
| 3.1 PRD conflicts | [N/A] | No PRD change. |
| 3.2 Architecture conflicts | [x] | Aligns with AppHost consuming EventStore Aspire helpers and updates runtime pins to current EventStore-compatible versions. |
| 3.3 UX conflicts | [N/A] | v1 remains headless. |
| 3.4 Secondary artifacts | [x] | Architecture regression test updated. |
| 4.1 Direct adjustment | [x] | Viable, low effort, low risk. |
| 4.2 Rollback | [N/A] | Not useful. |
| 4.3 MVP review | [N/A] | Not needed. |
| 4.4 Recommendation | [x] | Direct AppHost adjustment. |
| 5.1 Issue summary | [x] | Included above. |
| 5.2 Impact summary | [x] | Included above. |
| 5.3 Path forward | [x] | Direct Adjustment. |
| 5.4 MVP/action plan | [x] | MVP unchanged; implement AppHost and guard test. |
| 5.5 Handoff plan | [x] | Developer implementation. |
| 6.1 Checklist completion | [x] | All applicable items addressed. |
| 6.2 Proposal accuracy | [x] | Proposal matches current source inspection. |
| 6.3 User approval | [x] | Treated as approved by direct user instruction to make this correction. |
| 6.4 Sprint status update | [N/A] | No epic/story inventory change required. |

## 6. Implementation Handoff

Scope classification: Minor.

Route to: Developer agent for direct implementation.

Implementation tasks:

1. Update `src/Hexalith.Works.AppHost/Program.cs` to call `builder.AddHexalithEventStoreSecurity(...)`.
2. Wire `WithJwtBearerSecurity(security)` for EventStore, Admin.Server, and Works when security is enabled.
3. Wire `WithEventStoreClientCredentials(security)` for Works so recovery runtime command reissue can use the secured EventStore gateway.
4. Update architecture fitness tests to require the shared security helper and reject direct Keycloak setup.
5. Align Dapr, Aspire Hosting/AppHost SDK, and OpenTelemetry instrumentation pins to satisfy the current EventStore source reference.
6. Run targeted build/test verification.

Success criteria:

- Works AppHost initializes the shared security service through `HexalithEventStoreSecurityExtensions`.
- `EnableKeycloak=false` AppHost topology still builds and remains usable for existing smoke tests.
- Dapr, Aspire packages/AppHost SDK, and OpenTelemetry instrumentation are aligned with the checked-out EventStore submodule.
- Architecture tests fail if the AppHost drops the shared security helper or reintroduces direct `AddKeycloak(...)`.
