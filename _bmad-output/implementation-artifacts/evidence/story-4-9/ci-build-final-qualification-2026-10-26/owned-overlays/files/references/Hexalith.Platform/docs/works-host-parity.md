# Works host parity and rollback

## Canonical local and CI profiles

`eng/verify-works-host.sh --profile full` is the default local gate: source Debug and public-package Release consumer checks, followed by every explicit live R1–R11 proof and complete-fleet rollback. `--profile ci` uses only Release/public-package consumer checks and Release/package Agents scaffold probes, as required by the Hexalith CI baseline. The explicitly accepted test-only source exceptions are unpublished Platform.Identity, Platform.Composition, conditional Platform.Works.Runtime and the eleven linked Parties authority/query sources compiled into Works IntegrationTests. Parties.Client and Parties.Contracts remain catalog-selected public NuGet packages, and `linkedFixtureSources` binds the exact linked authority/query bytes from `HexalithPartiesRoot`; the Composition exception is hash-bound from the Platform tree the consumer actually compiles (`PlatformWorksRuntimeRoot`), including its linked `DaprSelfHostedMtls.cs`, not from the Works dependency copy. Runtime SDK executables still build in Release from independently qualified exact public producer source; their external dependencies use packages.

The Builds catalog (`HexalithDaprRuntimeVersion` 1.18.7, `HexalithDaprCliVersion` 1.18.2) is the single Dapr version authority for the Works fleet: `WorksPreviewModel` composes Sentry, Placement and Scheduler from it through `CompositionToolchainPins`, and `eng/works_toolchain.py` derives the sidecar runtime and CLI from the same catalog, resolved exactly like `HexalithBuildsRoot`. The Platform-owned [Works proof toolchain](../config/works-proof-toolchain.json) no longer names versions of its own: it binds only the exact qualified Linux `daprd` SHA-256 to the catalog runtime it was qualified for, and refuses a catalog runtime change until that hash is requalified. CI reads the derived pins through `eng/works_toolchain.py` for both the parity lane and the ordinary lane (`dapr-version` and `dapr-runtime-version`). The canonical gate checks the installed version and runtime hash, requires that the toolchain came from the same catalog that selects the SDK, and refuses either isolated launcher (live fleet or retained preflight) unless it executes those exact bytes, all before any live lane; the Works CI coordinator independently requires the executed hash to equal the Platform pin. An owned local self-invocation comparison of the exact hosted 1.18.3 binary showed missing caller attribution and preservation of a spoofed `dapr-caller-app-id`; the qualified 1.18.7 binary stamps the actual `works` caller and overwrites the spoof. That probe did not enable mTLS and does not qualify a production deployment. The production profile requires `daprRuntimeVersion >= 1.18.7` as a numeric qualified floor (1.18.8, 1.18.10, 1.19.0 and 2.0.0 admissible; 1.9.0 and 1.18.6 refused), and its JSON schema is tested against the same cases; preview admission remains closed. Hosted run [39674116771](https://github.com/Hexalith/Hexalith.Works/actions/runs/39674116771) passed the Release/package CI profile on the earlier pins; the Builds-derived toolchain, the 3.127.0 producer selection and the changed verifier inputs require fresh complete qualification; the earlier 3.126.1 selection's qualification is recorded with the Works Story 4.9 evidence.

Both profiles execute the same live recovery, callback, historical lifecycle/security, restart, HA, mTLS, shared rebuild and retained-fleet rollback path. Both retain public producer qualification and final source, fixture, archive, runtime and launcher closure. A CI profile does not weaken an R1–R11 proof or satisfy source Debug conformance: its ledger records `profile.name=ci`, only `Release` in consumer configurations/checks/dependency modes, `agentsConfiguration=Release`, and source conformance as not requested. It never records an absent Debug check as passed. `--prepare-only` remains nonqualifying in either profile. Both still require status `passed-disposable-synthetic-parity-and-rollback`, all 44 proof kinds and complete rollback.

Agents Full component probes receive the same selected configuration through `verify-agents-host.sh` and `ext-host-1/verify_full.py`. Their custody and EventStore guard `dotnet test` calls explicitly select that configuration and its matching source/package flags; CI never relies on the implicit Debug default. Component evidence records the actual configuration and dependency mode, and each suite retains its exact `command.json`. Release attempt identity includes that mode, so a retained Debug component attempt cannot qualify a Release request. The default Debug attempt identity remains compatible with historical local attempts. These nested helpers and their regression tests are explicitly hash-bound before execution and checked at final closure.

Fixture binding includes the complete IntegrationTests and Testing project inputs, copied golden resources,
and root runner configuration, excluding generated outputs relative to each owning source tree and refusing
symbolic links. The complete consumer output, including copied resources, runner settings and compiled test
dependencies, is bound before supporting tests execute and re-enumerated at final closure. SDK source
exports separately record the selected Builds catalog location, its resolved target and all Props bytes;
these inputs are checked again during producer qualification and final closure. Final closure executes
regressions for source membership, artifacts, helpers, fixtures and host port reservations. Verifier commands
run in owned POSIX process groups; timeout, cancellation and completion clean surviving members of that group.
Processes that explicitly detach into another session require their caller's separate lifecycle cleanup.
These deterministic controls do not replace a fresh complete live qualification.

The Works-owned CI bootstrap supplies root-declared dependencies and an exact public SDK export to this verifier; it owns no topology or proof implementation. For isolated execution, `works_host_snapshot.create(..., upstream_origin=...)` additionally binds the original owner working overlay to a run-owned Git origin and its execution export. Admission and final closure verify all three ordinary source inventories and selected revisions, while the existing dependency-link/catalog binding remains on the isolated origin/export. Any relevant original-owner, isolated-origin or execution-export drift refuses qualification. Default snapshot callers retain their existing binding contract.

The qualification records below preserve their exact historical revisions and profiles. New source or profile changes require fresh complete runs; these older packets are not relabeled.

Story 4.16 synthetic parity and rollback are **qualified on public EventStore 3.126.0**. The implementation is ready for review.
The accepted 4.15 evidence remains preserved. A passing supporting build or an
individual live scenario does not accept the complete Works gate. The retained
Works AppHost and ServiceDefaults stay usable until 4.16 acceptance and the
subsequent 4.9 cutover.

Public 3.124.0 failed automatic named projection catch-up; its
[public failure record](../../works/_bmad-output/implementation-artifacts/evidence/story-4-16/root-acceptance-2026-10-16/public-3.124.0/public-r4-blocker.json)
is retained. Public EventStore 3.125.1 (tag `v3.125.1`, producer
`55432c50d4b6f1666b4bd6d4a60bfc263b2d0e0b`) contains both producer corrections.
The first canonical 3.125.1 run on 2026-10-18 is
[historical supporting evidence](../../works/_bmad-output/implementation-artifacts/evidence/story-4-16/public-3.125.1-2026-10-18/README.md)
only. Review iteration 1 found that it replaced Platform's own Works hosting
startup, ran at fixture-injected rate limits, declared per-row proofs from phase
names and probed mTLS without a server-side refusal. Only a fresh canonical run
on the corrected graph can qualify this gate.

That fresh run exited 0 on 2026-10-18 with status
`passed-disposable-synthetic-parity-and-rollback`. All eleven rows derived their
four proofs from field-bound phases, with zero skips, nine Platform starts and one
retained-host rollback start; every Platform `works` and `works-ha` host loaded
Platform's hosting startup first. Its
[review iteration 1 result](../../works/_bmad-output/implementation-artifacts/evidence/story-4-16/review1-corrections-2026-10-18/canonical-admission/result.json)
is now historical supporting evidence.

Review iteration 2 found that the live restart and rollback queries passed only
through coordinator authority the fixture added, and that the fixture still set
keys Platform composes. The fresh run on that corrected graph exited 0 on
2026-10-19 with status `passed-disposable-synthetic-parity-and-rollback`: all
eleven rows passed with zero skips, every live query a proof reads was a delegated
Executor query, and no `platform-target` start set a key Platform composed outside
the three permitted shapes. Its
[iteration 2 result](../../works/_bmad-output/implementation-artifacts/evidence/story-4-16/review2-corrections-2026-10-19/canonical-admission/result.json)
is now historical supporting evidence.

Review iteration 3 found three gaps. The EventStore receiver refused Admin.Server,
which the SDK's production template grants. A startup alert point could satisfy
the alert-raise proofs. The composition guard compared only fixture writes
evaluated on an empty environment. The fresh run on that corrected graph exited 0
on 2026-10-19 with status `passed-disposable-synthetic-parity-and-rollback`: all
eleven rows passed with zero skips. Each alert raise followed the phase's observed
Degraded state, and no `platform-target` start removed or changed a key Platform
composed. Its
[iteration 3 result](../../works/_bmad-output/implementation-artifacts/evidence/story-4-16/review3-corrections-2026-10-19/canonical-admission/result.json)
retains the command, ledger, logs and outcome records. Acceptance stays with
independent review.

The owner selected public **3.126.0** for fresh requalification on 2026-10-19. Earlier results above qualify only their recorded 3.125.1 graphs; 3.126.0 acceptance requires a new complete verifier run.

The fresh [3.126.0 packet](../../works/_bmad-output/implementation-artifacts/evidence/story-4-16/public-3.126.0-2026-10-19/README.md) records canonical exit 0, all eleven rows, complete rollback, zero skips, and a separate 4/4 retained-host source Debug pass. Concurrent Platform and Builds edits were preserved by running from the recorded isolated source snapshot. Story 4.9 cutover remains separate.

The review iteration 6 corrections (T213–T219) changed the verifier and its tests,
so the [corrections packet](../../works/_bmad-output/implementation-artifacts/evidence/story-4-16/review6-corrections-2026-10-20/README.md)
requalified the same graph on 2026-10-20 with canonical exit 0, all eleven rows,
complete rollback and zero skips on verifier `add0f90e`. It is the current run; the
2026-10-19 packet binds the earlier verifier.

To repeat that run, build the isolated workspace its
[isolation provenance](../../works/_bmad-output/implementation-artifacts/evidence/story-4-16/review6-corrections-2026-10-20/isolated-platform.json)
records: an ordinary-source export of Platform base
`f0e33167fcdb5f539da159c54f7505b72a65c9b2` plus the four reviewed overlay files
`apphost.cs`, `eng/verify_works_host.py`, `eng/test_verify_works_host.py` and
`docs/works-host-parity.md`, taken from Platform commit
`56e36a269e8296022293f02643aac67e174c1fd0`, whose blobs match the recorded hashes.
The export leaves every root-declared `references/*` path empty. Link Builds to an
ordinary-source export of the bound catalog commit (the live Builds checkout may
have moved since qualification), EventStore to an exact `v3.126.0` export, Works
to the selected Works checkout, and each other reference to the matching
initialized checkout of the Platform source, as the qualified run did; those other
links are not hash-bound. Topology tests also read Works at the workspace's
`../works` sibling before the live verifier applies `--works-root`, so an existing
sibling must resolve to the selected Works checkout.

Save the block as a script and run it with `bash` rather than pasting it into an
interactive shell. `set -euo pipefail` stops at the first failing step, and the
script refuses any export, workspace or evidence path that already exists instead
of extracting over it or linking into it:

```bash
set -euo pipefail
WORKS_PARITY_WORKS_ROOT=/home/administrator/projects/hexalith/works
WORKS_PARITY_PLATFORM_SOURCE=/home/administrator/projects/hexalith/platform
WORKS_PARITY_PLATFORM_ROOT=/absolute/isolated/workspace/platform
WORKS_PARITY_BUILDS_ROOT=/tmp/works-parity-builds-b435f2e-source
WORKS_PARITY_SDK_ROOT=/tmp/works-parity-sdk-3.126.0
WORKS_PARITY_ARTIFACTS=/tmp/works-parity-new-run
for path in "$WORKS_PARITY_PLATFORM_ROOT" "$WORKS_PARITY_BUILDS_ROOT" "$WORKS_PARITY_SDK_ROOT" "$WORKS_PARITY_ARTIFACTS"; do
  if [ -e "$path" ] || [ -L "$path" ]; then echo "Choose a fresh path: $path" >&2; exit 1; fi
done
# Isolated Platform: the base export plus the four reviewed overlay blobs.
mkdir -p "$WORKS_PARITY_PLATFORM_ROOT"
git -C "$WORKS_PARITY_PLATFORM_SOURCE" archive f0e33167fcdb5f539da159c54f7505b72a65c9b2 | tar -x -C "$WORKS_PARITY_PLATFORM_ROOT"
for overlay in apphost.cs eng/verify_works_host.py eng/test_verify_works_host.py docs/works-host-parity.md; do
  git -C "$WORKS_PARITY_PLATFORM_SOURCE" cat-file blob "56e36a269e8296022293f02643aac67e174c1fd0:$overlay" > "$WORKS_PARITY_PLATFORM_ROOT/$overlay"
done
# The bound Builds catalog and the exact SDK tag as ordinary source.
mkdir "$WORKS_PARITY_BUILDS_ROOT" "$WORKS_PARITY_SDK_ROOT"
git -C "$WORKS_PARITY_WORKS_ROOT/references/Hexalith.Builds" archive b435f2e3b3892e03f69322d24de6003b0baf7a7d | tar -x -C "$WORKS_PARITY_BUILDS_ROOT"
git -C "$WORKS_PARITY_WORKS_ROOT/references/Hexalith.EventStore" archive v3.126.0 | tar -x -C "$WORKS_PARITY_SDK_ROOT"
# git archive applies eol attributes; restore the one rewritten SDK blob byte for byte.
git -C "$WORKS_PARITY_WORKS_ROOT/references/Hexalith.EventStore" cat-file blob v3.126.0:global.json > "$WORKS_PARITY_SDK_ROOT/global.json"
# Replace each empty root-declared reference path with its selected source. rmdir refuses a
# non-empty path and ln -T never links into an existing directory; no nested Git checkout is created.
rmdir "$WORKS_PARITY_SDK_ROOT/references/Hexalith.Builds"
ln -sT "$WORKS_PARITY_BUILDS_ROOT" "$WORKS_PARITY_SDK_ROOT/references/Hexalith.Builds"
for reference in "$WORKS_PARITY_PLATFORM_ROOT"/references/*; do
  name="$(basename "$reference")"
  case "$name" in
    Hexalith.Builds) target="$WORKS_PARITY_BUILDS_ROOT" ;;
    Hexalith.EventStore) target="$WORKS_PARITY_SDK_ROOT" ;;
    Hexalith.Works) target="$WORKS_PARITY_WORKS_ROOT" ;;
    *) target="$WORKS_PARITY_PLATFORM_SOURCE/references/$name" ;;
  esac
  rmdir "$reference"
  ln -sT "$target" "$reference"
done
# Required by the Platform topology tests as well as the explicit live selection.
works_sibling="$(dirname "$WORKS_PARITY_PLATFORM_ROOT")/works"
if [ -e "$works_sibling" ] || [ -L "$works_sibling" ]; then
  [ "$(realpath "$works_sibling")" = "$(realpath "$WORKS_PARITY_WORKS_ROOT")" ] || { echo "Not the selected Works checkout: $works_sibling" >&2; exit 1; }
else
  ln -sT "$WORKS_PARITY_WORKS_ROOT" "$works_sibling"
fi
cd "$WORKS_PARITY_PLATFORM_ROOT"
HexalithBuildsRoot="$WORKS_PARITY_BUILDS_ROOT" \
HexalithEventStoreRoot="$WORKS_PARITY_SDK_ROOT" \
bash eng/verify-works-host.sh --works-root "$WORKS_PARITY_WORKS_ROOT" --runtime-sdk-root "$WORKS_PARITY_SDK_ROOT" --artifacts-path "$WORKS_PARITY_ARTIFACTS"
```

`--runtime-sdk-root` is required when the Works EventStore gitlink is not exactly
the selected tag commit: without it the verifier qualifies the gitlink source
against the selected public archives and refuses any differing runtime/build files.
The export must come from the root-declared SDK checkout's object store. The
ordinary-source Builds export pins the qualified catalog and compatible dependency
versions independently of subsequent shared-checkout changes. `HexalithBuildsRoot`
and `HexalithEventStoreRoot` preserve those same selections in child builds. No
shared checkout is mutated.

Choose a fresh, empty artifacts directory for each attempt. The verifier records
each exact command, working directory, exit result and log hash in `ledger.json`,
including the first failing command. It builds the actual `apphost.cs` file entry
and a separate Works qualification consumer. Live tests require that exact host
assembly, its SHA-256 and the selected Platform root. They cannot fall back to the
retained Works host. Only the explicit rollback phase selects that host.

`--runtime-sdk-root /absolute/producer-source` selects an explicit SDK source
directory, including an extracted official producer archive. Admission compares
every `src/**` file except `bin`/`obj` build outputs, plus the three central build
files, by path and Git blob with the official commit trees bound to the restored
public packages and the named `v<version>` tag. Props, targets, JSON and resource
inputs are qualified like C# sources. Modified, missing, extra, linked or foreign
producer source fails; a matching version label alone cannot qualify it.

`--prepare-only` exits successfully when preparation passes, with status
`preparation-passed; parity-incomplete`. Acceptance requires the full command to
exit zero, status `passed-disposable-synthetic-parity-and-rollback`, all eleven
rows passed, complete rollback, all required persisted phases, and executed test
inventories with zero failures, skips, errors or missing methods. An incomplete
ledger remains incomplete even when some supporting checks pass.

## Scope and prerequisites

This gate uses disposable synthetic data, an owned Redis container, a dedicated
Scheduler volume and generated local mTLS certificates. It requires Docker,
the catalog-compatible .NET/Aspire/Dapr tools, restored root-declared dependencies,
and the existing local Dapr prerequisites checked by the harness. The two-host trust
probe also needs the host `openssl` CLI (OpenSSL 1.1.1 or later): the owned Sentry
issues Ed25519 credentials, and .NET cannot sign the probe's client certificate with
them. The verifier also needs outbound HTTPS. It fetches the producer's official
commit, tree and `v<version>` tag receipts from `api.github.com`. The optional
`GITHUB_TOKEN` authenticates only that official repository's Git API requests
to avoid shared anonymous rate limits; it is excluded from redirected requests
and other repository paths. Hosted CI supplies its read-only job token.
The verifier restores any
published EventStore package missing from the local NuGet cache from nuget.org
(`https://api.nuget.org/v3/index.json`), the provenance source every restored
package must record. Without that access the run fails with a recorded failure.
The preview allocates private Sentry, placement and scheduler host ports; the
harness binds its selected control-plane listeners for each owned topology.
Those default to Sentry 50001, placement 51005 and scheduler 51006; set all of
`WORKS_PLATFORM_SENTRY_PORT`, `WORKS_PLATFORM_PLACEMENT_PORT` and
`WORKS_PLATFORM_SCHEDULER_PORT` to select others. Occupied bindings fail startup.
Application and sidecar endpoints are isolated from unrelated running sessions.
Each live start also gives every sidecar a run-owned SQLite name-resolution
registry derived from its exact configuration. Default mDNS discovery would
round-robin app IDs such as `eventstore` with an unrelated local session's
sidecar; on 2026-10-18 that cross-talk failed every other Works invocation with
a TLS handshake error. A direct developer run of the preview outside this fixture
still uses default mDNS discovery. The fixture tears down only its owned resources;
it does not stop foreign hosts.

Debug development consumers use source references. Release consumers restore the
published EventStore graph selected by the Platform Builds catalog, currently
3.126.0. The verifier reads that single stable selection. It refuses a Works
consumer whose default pin in either `Directory.Build.props` or
`Directory.Packages.props` differs, and a file host whose `#:package`
`Hexalith.EventStore.*` directives in `apphost.cs` differ. It binds the explicit producer source to the
official `v<version>` release tag as well as the commit and trees named by the
restored archives. The ledger records package archive hashes and
NuGet provenance, source consumers, host identity and the actual compatible runtime
inventory used during restore. Source-built producer executables retain their own
recorded build identity; their binaries are not relabeled as published archives.
The target qualification consumer selects the Dapr adapter from the current Builds
catalog in isolated output. It preserves the retained Works consumer's pin.
Platform.Identity remains an explicitly identified test-only source dependency.

## Required producer and consumer evidence

Every EventStore package below must qualify at public version **3.126.0**
(tag `v3.126.0`, producer `fcf78d6cafb6c0c9f52dc350a9a09aff54ded288`). The
executable ledger owns exact hashes, commands and outcome records; this table
describes the required evidence rather than asserting that a run has passed.

| Row | Published producer/API | Actual consumer | Required target evidence |
| --- | --- | --- | --- |
| R1 | EventStore.Aspire: `AddHexalithEventStore`, `AddEventStoreDomainModule` | Platform `WorksPreviewModel` | Actual file-host composition, durable lifecycle writes, complete process membership and receiver policies |
| R2 | EventStore.DomainService/ServiceDefaults: canonical host defaults and health | Works `WorksHost`; Platform readiness/runtime adapters | Disabled recovery remains Degraded; actual OTLP recovery alert appears and clears after convergence |
| R3 | EventStore.DomainService: protected subscription/delivery | Works domain-event adapter | Actual ingress redelivery, origin/publish refusal and unchanged accepted family |
| R4 | EventStore.Client: `SharedProjectionEpochCoordinator` | Works shared rebuild handler | Every source history captured (twelve in the recovery lane, eleven in the focused lane); old generation readable during Capture/Stage; fenced delivery; complete Commit and unchanged logical family/checkpoints; the focused two-rebuild automatic catch-up fact that failed on public 3.124.0 |
| R5 | EventStore.DomainService: `IDomainQueryHandler` | Works what's-next and item-detail handlers | Own assignment and queued work visible, foreign assignment hidden, wrong actor/tenant and unavailable identity denied; restart and rollback queries are delegated Executor queries that remain usable; a non-delegated full-tenant query is refused |
| R6 | EventStore.DomainService: `AddEventStoreReminders` | Works recovery domain adapters | Lost firing/reissue, stale callback refusal, two distinct Works hosts/sidecars with primary loss, exactly one logical effect and durable receipt |
| R7 | EventStore.DomainService: `AddEventStoreProcesses` | Works process translator/catalog | Linked candidate pages, complete coverage, independent owner fencing and restart checkpoints |
| R8 | EventStore.DomainService: `ProcessDispositionService`; Client authorizer/audit interfaces | Works protected disposition endpoint; Platform runtime providers | Exact source/operator bindings, durable attributed audit before mutation, audit outage refusal, exported recovery alerts |
| R9 | EventStore.DomainService: `AddEventStoreDomainService`, `UseEventStoreDomainService` | Works executable/domain registrations | Both SDK modes selected, protected callbacks persist SDK state; final executable simplification belongs to 4.9 |
| R10 | EventStore.ServiceDefaults workload/channel authentication | Platform ACLs/profile; Works SDK ingress | mTLS, deny defaults, exact `works` routes and the SDK template's `eventstore-admin` grant on the EventStore receiver, wrong port/caller/tenant/purpose/publish refusal, incomplete or foreign production configuration refused |
| R11 | EventStore.Client: `HttpTrustedEffectSubmitter` | Works process delegation/effect policy | Distinct target receipts, replay returns the original terminal result, no repeated logical resume/cancel/expiry |

The verifier requires fresh persisted phase records from live recovery, historical,
two-host and focused projection-rebuild qualification facts. Supporting in-process SDK/domain suites do not
substitute for those target-host records. Host-start records distinguish
`platform-target` from `retained-rollback`; both bind their actual binary and owned
Scheduler volume. One target start must bind the independent `works-ha` replica
module.

Each row's `persisted`, `negative`, `restart` and `rollback` proof maps to named
phases and the state fields each phase must carry with qualifying values, for
example a `404` legacy-route status or `foreignCaClientRefusedByServer=true`. The
verifier derives `requiredProofs` from that map and fails a row whose phase is
missing, duplicated, lacks a field or carries a non-qualifying value. Cross-phase
checks also bind the restart family digest to the persisted one, and the restored
fleet, runtime binaries and replayed receipt to the Platform-written snapshot.
An alert-clear proof needs a cleared point exported after the last raise. An
alert-raise proof needs a raised point that the collector received after the phase
observed the Degraded state, and the phase records that observation time. The
Platform publisher starts Unavailable and exports `alert=1` before any health
publish, so a startup point proves no reaction to the degradation. Every
live module entry must be one of the prebuilt runtime artifacts with the same
hash. The ledger binds every Works IntegrationTests C# source outside `bin`/`obj`
as a fixture source, and the supporting set includes the telemetry-collector,
identity-fixture, composition-guard, host-selection and package-contract classes.
Once the run has started, any failure, whatever its exception type, is written to
the ledger before the nonzero exit. Network read errors are included. Only a
non-empty artifacts directory is refused without a ledger, because writing one
would overwrite earlier evidence.

The recovery lane keeps eleven sources for the five-child cascade, expiry, date
and child-completion scenarios, all of which end terminal or `InProgress`. The
Executor view shows only its own Assigned and the Queued work, so a twelfth source
stays Assigned to the synthetic Executor for the delegated restart and rollback
queries. The fixture's process profile therefore supports twelve retained sources,
with 1.9-second operations and a 56-second source quantum to satisfy the SDK's
coupled budget validation.

- **R9 negative.** The live Works host answers a fully credentialed request to the
  retired `/project` route with the canonical SDK's named-mode `404`. An
  uncredentialed request gets `401`. Neither changes the accepted family.
- **Delegated queries.** Every live query that a row's proof reads (R1, R3, R4
  and R5) is a delegated Executor query under the host's composed gateway
  workload `eventstore`, resolved through a test-only Parties identity authority.
  Each such phase records `queryIdentity`, the envelope it sent and how many
  resolutions the authority observed, and the verifier requires it. Neither host
  composes `Works:Query:PermittedCoordinatorWorkloads`, the fixture adds none, and
  the restart phase records a non-delegated full-tenant query refused as
  `Forbidden` before any identity resolution.
- **R10 negative.** The trust probe connects to the Works sidecar's internal mTLS
  gRPC endpoint. A client chained to the owned trust anchors completes HTTP/2
  setup. The sidecar itself refuses a client without a certificate and a client
  certificate from a foreign CA. The probe validates the server's exact SPIFFE
  identity under the configured root, and it rejects the genuine server when it
  expects another application's identity.
- **EventStore receiver callers.** The EventStore receiver
  (`DaprComponents/accesscontrol.yaml`) denies by default and names exactly two
  callers. `works` holds its exact command, query, status, stream-read and
  trusted-effect routes. `eventstore-admin` holds the selected producer's
  production-template grant, unchanged in substance: `/**` with GET, POST and PUT
  (`deploy/dapr/accesscontrol.yaml:34-46` at `v3.126.0`), with this topology's
  `public` trust domain and `default` namespace. Admin.Server invokes EventStore
  through its own sidecar for health, projection and backup operations, which the
  retained host also allows. The wildcard reaches the admin passthrough endpoints,
  and EventStore authorization still protects the command surface, so the grant
  adds no command authority. `WorksDaprPolicyTests` pins both callers and their
  operations, so any other caller or route fails it.

## Readiness, telemetry and privileged operations

Platform preserves the SDK's Healthy, Degraded and Unhealthy readiness meanings
for both its default text writer and Development JSON writer. HTTP 200 alone
cannot promote Degraded to healthy. Operations can start while Works recovery is
degraded, so operators retain diagnostic access.

The Platform-owned runtime publishes cached SDK process-health values on the
existing `Hexalith.EventStore.Domain.work` meter. Instruments use prefix
`eventstore.domain.recovery.`: unresolved, pending disposition/publication/
transition/observation counts, retained candidates, completeness, capacity,
availability, readiness and `alert`. `alert=1` denotes degraded, unavailable or
stale recovery; healthy fresh coverage emits zero. Samples older than fifteen
seconds alert. Reason labels come from a fixed allowlist; unknown reasons collapse
to `other`. Tenant IDs, item IDs, payloads and credentials are not metric labels.
The disposable loopback collector verifies actual OTLP HTTP/protobuf exports,
retains only these gauges and bounded reason labels, and rejects oversized or
malformed requests.

`WorksPreviewModel` composes `WORKS_PLATFORM_RUNTIME=1`, the Platform runtime's
`DOTNET_STARTUP_HOOKS` path and `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Hexalith.Platform.Works.Runtime`
on its `works` host. Live parity appends the synthetic proof assembly after those
values with each variable's separator (`:` and `;`); it never replaces them. So
`WorksRecoveryHostingStartup`, behind its `WORKS_PLATFORM_RUNTIME` gate, registers
the recovery telemetry before the proof runs, and the proof's later registration
adds nothing. Each proof host records which hosting startups loaded, whether
Platform's registered telemetry first, and the registration count. The verifier
refuses a `platform-target` start whose `works` or `works-ha` host lacks Platform's
hosting startup, runs it after the proof, or registers the telemetry more than
once. The HA replica copies Platform's composed `works` values, including its
environment; the fixture never re-sets Platform-composed workload callers.

The verifier builds Platform's file host and the Works consumer with one
deterministic path map, so their `Hexalith.Platform.Works.Runtime.dll` compiles
are byte-identical. It refuses a run whose two compiles differ, and a start whose
loaded hosting startup or first startup hook differs from Platform's host output.
Platform's composed hook value is never replaced to make them match.

On a `platform-target` start, the fixture sets no environment key that Platform
composed for that resource, even to an equal value. The harness evaluates
Platform's own environment callbacks before the fixture configures the builder,
and the fixture's callbacks separately, and refuses the start before any resource
is built when they overlap. It evaluates the fixture's callbacks twice: on an
empty environment, which shows every key they write, and after Platform's values.
The second evaluation catches a callback that removes a composed key, or rewrites
one only where Platform composed it. Three overlaps are permitted, each with a checked
shape: the proof extends the composed hook lists; the captured, restorable
synthetic app-channel secret replaces the per-run secret through the same public
SDK helper; and the telemetry lanes redirect the orchestrator's OTLP export to the
proof's owned loopback collector. Each start records Platform's composed keys, the
fixture's keys, the permitted overlaps, and Platform's composed values for
`EventStore__RateLimiting__*`, `Authentication__Workload__AllowedCallers__*`,
`DOTNET_ENVIRONMENT`, `Works__Recovery__UseSdkRuntime` and
`Works__Projection__UseSdkDelivery`. It also records each module's launched and
in-process values. The verifier recomputes the overlap, requires the composed
values to equal the literals in `WorksPreviewModel.cs`, and requires every
launched and in-process value to equal Platform's composition.

Ordinary Platform runtime injection installs telemetry only. Synthetic disposition
authority additionally requires Development, `WORKS_SDK_PROOF=1`,
`WORKS_PLATFORM_PARITY=1` and 1..64 explicit canonical source grants. There is no
discovered or wildcard authority. Protected HTTP admission independently requires
the receiver's sidecar channel and a fresh signed operator assertion. The provider
then requires the named operator workload, `process:dispose`, exact tenant/domain/
aggregate/purpose bindings and an attributed actor. The ordinary endpoint policy
also requires `domain-service:process`; Dapr grants the operator only the exact
disposition route.

Audit uses the SDK's durable CAS store, with an attributed attempt written before
the SDK prepares any disposition mutation. Audit failure or exhausted bounded
capacity refuses mutation. These disposable providers and the authenticated
synthetic Parties identity service do not qualify production identity, custody,
delegation or audit providers. Do not copy their generated credentials into an
operational deployment.

## Development/test rate limits

EventStore's default limiter (1,000 requests per minute per tenant and 100 per
second per consumer) rejects the preview's own internal actor, projection and
recovery traffic with `429`. A swallowed projection update then leaves the
pending-date index empty, which is an open EventStore producer defect tracked in
the Works deferred-work ledger. `WorksPreviewModel` therefore composes explicit
development/test limits on its `eventstore` host:

| Setting | Value |
| --- | --- |
| `EventStore__RateLimiting__PermitLimit` | 8192 |
| `EventStore__RateLimiting__ConsumerPermitLimit` | 8192 |
| `EventStore__RateLimiting__TenantPermitLimits__anonymous` | 8192 |
| `EventStore__RateLimiting__ConsumerPermitLimits__anonymous` | 8192 |

These values carry the twelve-source parity load and keep both SDK limiters
finite. Parity runs at exactly these Platform-composed limits; the fixture injects
none. The retained Works AppHost keeps its own proof-topology limits (100000).
Production admission stays refused and must size its own limits after the
EventStore defect is fixed.

`WorksPreviewModel` also composes the `configstore` Dapr component
(`DaprComponents/configstore.yaml`, type `configuration.redis`, scoped to
`eventstore`) and references it from the `eventstore` sidecar only. EventStore's
`DaprRateLimitConfigSync` polls that store every 10 seconds. Its
`ratelimit:tenants` and `ratelimit:{tenantId}:permit-limit` keys override
`EventStore:RateLimiting:TenantPermitLimits` at runtime, so a value written there
can change a tenant's limit without a restart. The committed component points at
`localhost:6379`. The proof rebinds `configstore`, like `statestore` and `pubsub`,
to its owned Redis container. That container starts empty and the proof writes no
`ratelimit:*` key, so the run uses the composed limits above.

## Production profile policy bindings

`WorksProductionProfile` requires `brokerPolicySha256` and `networkPolicySha256`
but only checks that each is a 64-character hexadecimal value. No broker or
network policy file exists for these hashes to match yet. The owner deferred that
binding to the AD-28 production gate on 2026-10-18; that gate must supply the
committed policy files. Until then the hashes are format-checked only. Production
admission stays refused regardless, and development values for `trustDomain` and
`namespace` are refused in any letter case.

## Complete fleet rollback

The drill first performs Platform writes and verifies terminal receipts, query
results and complete recovery coverage. It stops the owned topology before
capturing every Redis key/value and original expiry, offline Scheduler contents,
component/receiver/resiliency configuration, certificate/key material and the
compatible runtime deployment inventory. Generated authority and digest-key bytes
remain in the protected transient fixture snapshot; committed evidence contains
only safe hashes and synthetic outcomes.

Restore reproduces the full owned key inventory and values, removes a
post-snapshot sentinel, preserves expiry semantics, and verifies Scheduler,
configuration, credential and runtime bytes against their captured inventories.
The Scheduler inventory binds every entry's type, mode, owner and link target,
including the volume root itself, plus every regular file's content hash. The
rollback start must use the snapshot's own Scheduler volume.
The original Redis serialization supplies restoration bytes. Complete binary
hash fields/values and set members use stable ordering for verification; hash-field
absolute deadlines are included. Other values retain strict serialization checks,
including stream consumer groups and pending deliveries. Startup reuses captured
component files so retained templates cannot overwrite restored configuration.
The retained Works host then starts with that restored compatible fleet. Protected
queries must remain usable, source replay must converge, and reissued trusted
effects must replay their original receipts without another logical effect.
The restore phase records its own re-observations, not the snapshot's variables:
the stopped restored fleet is re-inventoried before the retained start, the runtime
binaries are re-hashed inside it, and counts and flags are the values it observed.
A re-observation cannot reproduce Redis's original DUMP encoding of unordered
hashes and sets, so it omits that capture-only inventory; every other fleet entry,
including the canonical logical value inventory, must equal the snapshot.
Projection-only restore, a changed receiver policy, or a mixed binary/state fleet
cannot qualify this drill.

This synthetic exercise does not admit real data or new catalogs. AD-28 restore
and approval gates still apply before that admission. Production Works preview
composition refuses even a structurally valid profile until the Maintainer's
time-bounded exception and qualified provider bindings exist.

## Agents and cutover boundaries

The user-approved 4.16 contract preserves the default Agents Full refusal:

```text
./eng/verify-agents-host.sh
exit 1: DependencyNotAvailable: EXT-HOST-1
```

`--mode LocalScaffold` must pass. Full Agents qualification remains unavailable
and is not a prerequisite for this independent Works gate. Its refusal is never
recorded as a successful Full qualification. See the
[Agents owner contract](ext-host-1-agents-composition.md).

After independent review accepts every required Works row and rollback, synchronize
the [4.16 story](../../works/_bmad-output/implementation-artifacts/spec-4-16-prove-platform-works-parity-and-rollback.md)
and sprint status. Only then resume
[4.9](../../works/_bmad-output/implementation-artifacts/spec-4-9-migrate-works-hosting-to-the-platform-boundary.md),
which reruns capture-through-Commit, restart, redelivery and security checks at
cutover before removing Works-owned hosting projects.
