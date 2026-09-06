# Review — Reality/Technology Verification Lens (architecture-gate)

- **Target:** `_bmad-output/planning-artifacts/architecture.md` (updated 2026-09-06, AD-01…AD-25 register + edited prose)
- **Lens:** technology/reality verification — every committed claim checked against local checkouts, live code, and the web
- **Reviewer:** reality-update verification subagent
- **Date:** 2026-09-06

## Verdict

**PASS.** Every committed claim checked resolves to a real artifact, a real API, or an accurately
reported external fact. The register consistently distinguishes what exists today from what it
allocates as new work (AD-16 fence, AD-20 matrix, AD-21 registry, AD-22 fan-out seam, AD-25
reminder adapter). Two low-severity residues found; neither invalidates a decision.

## Scope-by-scope findings

### 1. AD-20 — Hexalith.Platform claims vs the local checkout — VERIFIED

Checkout: `/home/administrator/projects/hexalith/platform` (origin
`https://github.com/Hexalith/Hexalith.Platform.git`, single commit `a66cdf3` "scaffold
platform-owned Aspire host for Agents EXT-HOST-1").

| Doc claim (architecture.md:218-243) | Reality | Match |
|---|---|---|
| Designated platform/host repository at github.com/Hexalith/Hexalith.Platform | `git remote -v` confirms | Yes |
| "already carries the identical contract for Hexalith.Agents (EXT-HOST-1)" | `platform/README.md:5-13` — EXT-HOST-1 delivery target; "Domain modules … must **not** ship module-owned AppHost/Aspire/ServiceDefaults" | Yes |
| Accountable owner "Platform Maintainer (Hexalith)" | `platform/README.md` "## Owner — Platform Maintainer (Hexalith)." | Yes, verbatim |
| "upgrades from its **current 13.4.6 scaffold**" | `platform/apphost.cs:1` — `#:sdk Aspire.AppHost.Sdk@13.4.6`; README requires "Aspire CLI 13.4.6 or later" | Yes — doc states current state correctly, does not misstate it as already-13.5 |
| Platform .NET SDK | `platform/global.json` — `10.0.302`, `rollForward: latestPatch` (matches prompt expectation; doc does not assert this number anywhere, consistent with AD-19) | Yes |
| `verify-works-host` lane "mirroring its `verify-agents-host.sh`" | `platform/eng/verify-agents-host.sh` exists (only lane in `eng/`); `verify-works-host` correctly framed as future obligation, not existing | Yes |

### 2. AD-19 + version prose — VERIFIED (one residue)

Authoritative files match expectations exactly:

- `/home/administrator/projects/hexalith/works/global.json` — SDK `10.0.400`, `rollForward: latestPatch`, MTP runner.
- `references/Hexalith.Builds/Props/Directory.Packages.props` — Dapr.* `1.18.5` (lines 139-146), `Aspire.Hosting*` `13.5.3` (lines 113-121), `xunit.v3` `4.0.0` (lines 319-321), `Microsoft.FluentUI.AspNetCore.Components` `5.0.0-rc.5-26219.1` (lines 226-227).

Doc behavior: every stale version literal (`10.0.301`, `1.18.4`, xUnit `3.2.2`, FluentUI `rc.3`)
now lives inside the explicitly dated "historical snapshot (2026-06-14) … historical evidence
only" table (architecture.md:572-588), each row marked "superseded". The doc no longer asserts
stale versions as current — with one exception (Finding F1 below).

Web verification:

- **Aspire 13.5.x is the current line and an AppHost SDK exists in that family.** Aspire 13.5
  released 2026-08-18; `Aspire.AppHost.Sdk` latest is 13.5.1 on NuGet; `Aspire.Hosting*` at 13.5.3.
  AD-20's "single 13.5.x family" is real and installable.
  (https://www.nuget.org/packages/Aspire.AppHost.Sdk, https://devblogs.microsoft.com/aspire/whats-new-aspire-13-5/)
- **Dapr Scheduler-backed actor reminders default since 1.15.** `SchedulerReminders` flag true by
  default in v1.15; reminders migrated from Placement to Scheduler. Matches AD-11/C2 prose
  "Scheduler-backed by default since Dapr 1.15".
  (https://github.com/dapr/dapr/issues/8045, https://blog.dapr.io/posts/2025/02/27/dapr-v1.15-is-now-available/)
- **xUnit v3 4.0.0 (2026-08-14) defaults to Microsoft.Testing.Platform v2** and discontinues MTP
  v1 support — matches the doc's "current v3 line defaults to MTP v2" (architecture.md:583).
  (https://xunit.net/releases/v3/4.0.0, https://www.nuget.org/packages/xunit.v3.core.mtp-v2/)

### 3. AD-08 — turn-lock-primary / ETag-fallback — VERIFIED

- **Dapr docs:** the actor runtime enforces turn-based concurrency via a per-actor lock acquired
  at turn start, released at turn end; no more than one thread active inside an actor at a time.
  The doc's "single-writer actor turn lock serializes same-item commands" is the documented
  behavior. (https://docs.dapr.io/developing-applications/building-blocks/actors/actors-features-concepts/)
- **Bounded conflict retry exists in code:**
  `references/Hexalith.EventStore/src/Hexalith.EventStore.Server/Actors/AggregateActor.cs` —
  `MaxPersistenceConflictRetries` property (line 90-94) sourced from
  `CommandConcurrencyOptions.DefaultMaxPersistenceConflictRetries = 1`
  (`Configuration/CommandConcurrencyOptions.cs:10`), retry loop label
  `RetryAfterPersistenceConflict:` (line 976), `ConcurrencyConflictException` with
  `conflictSource: "StateStore"` on save conflict (lines 1189-1195), retry gated by
  `persistenceConflictRetryCount < maxPersistenceConflictRetries` (line 1219). Matches AD-08's
  "rehydrates and re-handles within its bounded retry; exhaustion surfaces an infrastructure
  ConcurrencyConflict".
- The named normal-loser rejection `WorkItemTransitionRejected` exists at
  `src/Hexalith.Works.Contracts/Events/Rejections/WorkItemTransitionRejected.cs`.

### 4. AD-21/AD-22 — no phantom EventStore API — VERIFIED

- `references/Hexalith.EventStore/src/Hexalith.EventStore.DomainService/EventStoreDomainServiceExtensions.cs`
  exposes `AddEventStoreDomainService`/`UseEventStoreDomainService`/`MapEventStoreDomainService`
  and the canonical routes `/process`, `/replay-state`, `/query`, `/project`, `/project/v2`,
  `/admin/operational-index-metadata` (lines 31-33, 184-185), plus the shared-rebuild route
  referenced by AD-16 (`rebuild/shared` appears in this file). Discovery registers query and
  projection handlers only — **no relationship-lookup, ancestry, or multi-document fan-out seam
  exists** (repo-wide grep for relationship/fan-out/ancestor in EventStore src is negative).
- The doc gets this right: AD-22 says the capability "**lands in** the EventStore SDK first
  (AD-20 matrix R4)" (architecture.md:299-300) and R4 lists it as "**the new**
  relationship-aware fan-out seam" (line 254) — allocated, not asserted to exist. AD-16 likewise
  marks the capture-through-Commit fence "not yet supplied by the EventStore API … open platform
  obligation (VAL-H08)" (lines 183-186). No wording found that implies the seam already ships.
- AD-22's "Prevents" claim about today's behavior is confirmed by
  `src/Hexalith.Works/Projections/WorkItemProjectionDispatcher.cs:34-35` — "Parent rolled totals
  are therefore persisted and exposed as unavailable" pending shared rebuild — exactly the
  contradiction AD-22 says it resolves.

### 5. AD-25 vs ExpireWorkItem — VERIFIED

`src/Hexalith.Works.Contracts/Commands/ExpireWorkItem.cs` — `ExpireWorkItem(TenantId, WorkItemId)`,
doc-comment: "Terminal expiry: accepted from any non-terminal status (→ Expired). The command is
the adapter-fired signal — handling reads no clock … TTL/date sourcing and the scheduled signal
that fires this command are **out of scope here** (Story 4.6)."

AD-25 fills precisely the gap the command declares out of scope (trigger ownership → reminder
adapter; policy → platform host configuration) and contradicts nothing: adapter-fired matches,
no-clock matches (AD-25 Rule), tenant-inclusive reminder identity is supported by the command
carrying `TenantId`, and "idempotent against terminal state per the transition table" is
compatible with "accepted from any non-terminal status" under the authoritative
`docs/lifecycle-transition-matrix.md`.

## Findings

| ID | Severity | Finding | Evidence |
|---|---|---|---|
| F1 | Low | Stale version literal outside the historical table: the project-structure tree annotates `global.json` as "SDK 10.0.301" while the real file pins 10.0.400. This is the one spot that still violates AD-19's own rule ("this document never states a version as 'current'"); the tree section is not marked as a dated snapshot. | architecture.md:849 vs /home/administrator/projects/hexalith/works/global.json:3 |
| F2 | Low | AD-20 declares the mixed 13.4.6/13.5.3 graph "retired, not grandfathered" but only names the platform's 13.4.6 scaffold. Works' **own** `global.json` still carries `"msbuild-sdks": { "Aspire.AppHost.Sdk": "13.4.6" }` — a vestigial pin (Works ships no AppHost) sitting in an AD-19-authoritative file that contradicts the "single 13.5.x family" target; the doc never mentions it, so a reader reconciling AD-19 with AD-20 hits an unexplained 13.4.6. | /home/administrator/projects/hexalith/works/global.json:9-11 vs architecture.md:230-233 |
| F3 | Info | "Aspire.AppHost.Sdk and Aspire.Hosting* packages move together on the 13.5 line" is family-accurate but the exact patch versions differ today (AppHost.Sdk latest 13.5.1; Hosting* pinned 13.5.3). Not a misstatement — the doc commits to the family, not a patch number. | https://www.nuget.org/packages/Aspire.AppHost.Sdk; Directory.Packages.props:113 |

## Sources

- https://www.nuget.org/packages/Aspire.AppHost.Sdk
- https://devblogs.microsoft.com/aspire/whats-new-aspire-13-5/
- https://aspire.dev/whats-new/aspire-13-5/
- https://github.com/dapr/dapr/issues/8045
- https://blog.dapr.io/posts/2025/02/27/dapr-v1.15-is-now-available/
- https://docs.dapr.io/developing-applications/building-blocks/actors/actors-features-concepts/
- https://xunit.net/releases/v3/4.0.0
- https://www.nuget.org/packages/xunit.v3.core.mtp-v2/
