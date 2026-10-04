# Story 4.11 review-fix verification — 2026-10-04

The coordinator classifies effect collisions over all valid current intents before collapsing reminder names. The retained same-name witnesses keep `witness-collision` evidence and audit records; a third name sharing either source is quarantined as `effect-collision`.

## Focused checks

- `collisions-red.xml`: before the coordinator correction, all 24 new cases failed on the unintended submission (due cases) or arming (future cases), with zero errors/skips. The cases cover both shared-source variants and all six input orders.
- `coordinator-focused.xml`: after the correction, `ReminderCoordinatorTests` and `ReminderCallbackAdmissionTests` passed 125/125, with zero errors/skips.
- `domainservice-build.log` and `live-build-final.log`: affected Debug projects built with zero warnings/errors, using `UseHexalithProjectReferences=true`, `NuGetAudit=false`, `MinVerVersionOverride=1.0.0`, `-m:1`.
- `live-reminders-container-final.xml`: the existing `RegistrationCallbackRestartReplayAndRearmPersistInRedis` live proof passed 1/1, with zero errors/skips (7.454 seconds). The actual Scheduler delay is checked against the operation's start/end clock interval with ±2 seconds tolerance and an operation duration cap of 90 seconds. The actual repeat period equals `EventStoreReminderOptions.RetryMaxDelay`. Both initial registration and reconciliation re-arm are checked. The parser handles Go duration and Scheduler's `@every <Go duration>` interval representation and rejects unparsed text.
- `git diff --check` passed in EventStore; the deferred-work file's diff check also passed.
- Only the historical AC4 public R6 API availability/package-proof entry was resolved. Its original description is retained; its two relative evidence links exist and the retained XML is one Pass with zero errors/skips.

## Exact successful live command

Working directory: `/home/administrator/projects/hexalith/works/references/Hexalith.EventStore`.

```sh
timeout 240s docker run --rm --network host --mount type=bind,source=/home/administrator/projects/hexalith/works/references/Hexalith.EventStore/tests/Hexalith.EventStore.Server.LiveSidecar.Tests/bin/Debug/net10.0,target=/proof,readonly --mount type=bind,source=/home/administrator/.dapr/bin/daprd,target=/root/.dapr/bin/daprd,readonly --mount type=bind,source=/tmp/story-4-11-review-fixes-20261004-RtXpaNu7,target=/evidence --workdir /proof mcr.microsoft.com/dotnet/aspnet:10.0-alpine dotnet Hexalith.EventStore.Server.LiveSidecar.Tests.dll -class '*ReminderRecoveryLiveSidecarTests' -result-xml /evidence/live-reminders-container-final.xml > /tmp/story-4-11-review-fixes-20261004-RtXpaNu7/live-reminders-container-final.log 2>&1
```

Result: exit 0. Image was already cached; its digest is `mcr.microsoft.com/dotnet/aspnet@sha256:57bd717ac18ff6c8a39cc0ee4a76c1f15adc46df50434c73eff0c3f1df4c88f0` (linux/amd64, .NET and ASP.NET 10.0.9). Test xUnit version is 4.0.1. The read-only mounted sidecar binary is Dapr 1.18.4. The existing `dapr_redis` (`redis:6`, port 6379), `dapr_placement` (`daprio/dapr:1.18.4`, port 50005), and `dapr_scheduler` (`daprio/dapr:1.18.4`, port 50006) containers were reused. The ephemeral test container was removed automatically; no shared containers were restarted and no host configuration or inotify limits were changed.

## Earlier environment failures retained

- Normal host execution failed during fixture initialization because the administrator user's inotify watcher allocation was exhausted (`no space left on device`); disk space was available. See `live-reminders.log`/`.xml`.
- The isolated root host subprocess bypassed that watcher allocation but failed before the test body because Redis's WSL host published-port connection closed with EOF. See `live-reminders-isolated-user.log`/`.xml`.
- The first Docker host-network attempt reached the live test body and failed because the new parser had not yet accepted Scheduler's `@every 15m0s` format. This prompted the explicit prefix handling. See `live-reminders-container.log`/`.xml`.
- Fresh pre-change Aspire baseline attempt: `timeout 150s aspire start --isolated --apphost src/Hexalith.EventStore.AppHost/Hexalith.EventStore.AppHost.csproj --non-interactive --format Json` exited 2; the AppHost crashed with exit 134 because intentionally uninitialized nested `references/Hexalith.Tenants` project files are absent. `aspire describe` and `aspire stop` both exited 0 afterward. Logs: `aspire-start.log`, `aspire-describe.log`, `aspire-stop.log`. No submodule was initialized.

## Remaining scope

The parent coordinator owns broader verification, workflow status, retained repository evidence, and the next named public release. Public EventStore 3.112.0 established the historical API/package availability proof but predates this runtime correction. This report does not claim that the corrected runtime has been publicly released.
