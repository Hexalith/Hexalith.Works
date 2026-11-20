# Hexalith.Works

Hexalith.Works is the Hexalith work-item domain module. The repository is an umbrella workspace with root-declared Hexalith submodules and a canonical EventStore domain-service executable. Aspire hosting and Dapr topology belong to Hexalith.Platform. Current Works packages require .NET 11 or later; build them with the SDK selected in `global.json`. Install .NET SDK 10.0.404 alongside it to run the compatibility fixtures and Platform integration checks. Verify both SDKs with `dotnet --list-sdks`.

## Local Debug development

Debug builds consume EventStore and PolymorphicSerializations from sibling source so changes can be debugged across module boundaries. The source graph also needs Builds (central props/tooling), Commons (SDK primitives), McpCli (contract declarations), Memories (Platform preparation contracts), Tenants (SDK build inputs), Parties (client/contracts and the integration authority source fixture), and Platform (source-only Identity and Composition test adapters). AI.Tools supplies the required repository baseline. Initialize these root-declared dependencies:

```bash
git -c submodule.recurse=false submodule update --init references/Hexalith.AI.Tools references/Hexalith.Builds references/Hexalith.Commons references/Hexalith.McpCli references/Hexalith.Memories references/Hexalith.EventStore references/Hexalith.PolymorphicSerializations references/Hexalith.Tenants references/Hexalith.Parties references/Hexalith.Platform
export HexalithBuildsRoot="$PWD/references/Hexalith.Builds"
dotnet restore Hexalith.Works.slnx -p:Configuration=Debug
dotnet build Hexalith.Works.slnx --configuration Debug --no-restore
```

Do not use recursive submodule initialization.

Run the local source verification tier after initializing these dependencies:

```bash
python3 scripts/verify-source-debug.py --artifacts-path /tmp/works-source-debug
```

It checks the reviewed EventStore and PolymorphicSerializations source/Release-version pairs in
`tools/source-debug-dependencies.json`, then restores and compiles the full solution in explicit
Debug/source mode. A changed package pin, gitlink or checkout requires reviewing that pairing; the
gate never updates dependencies. It hashes Git-visible tracked/untracked source edits and checks their contents again after compilation,
so changed inputs cannot retain a passing result. Logs and `result.json` retain failed
commands as well as successful compilation. CI runs the same identity preflight with `--check-only`,
using Release/package evaluation without restoring or compiling the Debug graph. The local tier
complements CI's required Release/package validation; it does not run live Platform parity or authenticate package archives.

The manifest records the exact current EventStore and PolymorphicSerializations source revisions
and their selected Release versions. These development pairings do not claim that the Debug
and Release producers have identical behavior. Re-run source characterization and the local gate
when changing the pairings. See the [development assurance closure](docs/development-assurance.md)
for the historical tooling retirements and validation boundaries.

The deterministic certificate-refusal integration controls require Linux and the `openssl` executable
on `PATH`. Other providers remain inconclusive in the live proof; the native tests explicitly report
unsupported runners, and the repository's `failSkips` setting keeps those runs from qualifying.

## CI Release validation

Pushes and pull requests to `main` run the shared Builds CI workflow. The `after-unit-tests` hook checks the reviewed Debug source identities. After shared CI succeeds, `custom-platform-parity` runs the canonical Platform R1–R11 verifier with the `ci` profile. That job selects the published SDK, exercises the live facts and checks fleet rollback. Inspect both jobs on the commit being qualified. The local equivalent of the parity job is:

```bash
proof_parent="$(mktemp -d)"
bash scripts/verify-platform-parity-ci.sh --artifacts-path "$proof_parent/works-platform-proof"
```

For an independently obtained official SDK source export, add `--runtime-sdk-root /absolute/path/to/export`. The bootstrap copies it into the run-owned workspace and records an unknown checkout commit; the canonical verifier still requires its exact runtime/build inputs to match the selected public package producer and official release tag. This option does not select a different package version or qualify a candidate Builds catalog.

The verifier validates persisted evidence envelopes after every successful producing command, before starting the next command. It permits valid incomplete prefixes and list states; final acceptance still binds every required proof and closes source integrity. Each command's ledger entry records UTC start/end boundaries, monotonic `elapsedSeconds`, output hash, exit status when available, and failures. The ledger and per-command logs are under `$proof_parent/works-platform-proof/gate`.

Hosted run [44049423249](https://github.com/Hexalith/Hexalith.Works/actions/runs/44049423249) measured 44m12s for qualification and 8m13s for ordinary compilation/tests. These are the pre-change observations: approximately eight minutes is the selected ordinary feedback target, while new hosted durations remain unmeasured until publication. Recovery budgets and all five recovery topology starts remain unchanged.

Required live classes run in that verifier with an explicitly selected, hashed Platform host; an unselected live launch refuses rather than starting a Works-owned topology. The ordinary integration suite leaves only the explicitly listed live facts to the Platform parity job; those facts must all pass on the exact release source before publication. See `docs/platform-hosting-cutover.md` for producer bindings and the historical rollback source archive.

The [Platform profile contract](references/Hexalith.Platform/docs/works-host-parity.md#canonical-local-and-ci-profiles) also preserves the default `full` local gate, which adds source Debug conformance. CI records that source conformance was not requested; it does not claim a Debug pass. Both profiles require all eleven rows and all 44 proof kinds, with the same strict source/runtime/archive/launcher binding. The bootstrap binds its selected export back to the original Platform working overlay and refuses relevant drift in either source.

```bash
git -c submodule.recurse=false submodule update --init references/Hexalith.AI.Tools references/Hexalith.Builds references/Hexalith.Commons references/Hexalith.McpCli references/Hexalith.Memories references/Hexalith.EventStore references/Hexalith.PolymorphicSerializations references/Hexalith.Tenants references/Hexalith.Parties references/Hexalith.Platform
export HexalithBuildsRoot="$PWD/references/Hexalith.Builds"
dotnet restore Hexalith.Works.slnx -p:Configuration=Release
dotnet build Hexalith.Works.slnx --configuration Release --no-restore -warnaserror -m:1
python3 scripts/pack-release-packages.py ./nupkgs 0.0.0-ci-test
python3 scripts/validate-nuget-packages.py ./nupkgs
python3 scripts/validate-consumer-package-references.py ./nupkgs
python3 -m unittest discover -s scripts/tests

for project in \
  tests/Hexalith.Works.UnitTests \
  tests/Hexalith.Works.ArchitectureTests \
  tests/Hexalith.Works.PropertyTests \
  tests/Hexalith.Works.IntegrationTests; do
  dotnet test --project "$project" --configuration Release --no-build
done
```

NuGet audit is enabled in all restore modes with `NuGetAuditMode=all`, so advisories of every severity are reported in the build log. Per the Hexalith.Builds CI/CD standard, `NU1901`-`NU1904` are exempt from the repository-wide warnings-as-errors policy: an advisory warns and does not fail the build, so a transitive advisory that cannot be upgraded immediately never blocks CI. Acknowledge or waive an individual advisory with `<NuGetAuditSuppress>` rather than disabling the audit.

Release restore and compilation require the committed `HexalithEventStoreVersion` in `Directory.Packages.props`. An ambient environment variable or command-line override selecting a different SDK fails with `HXW0003`. Builds without restore also check resolved SDK versions and reject assets still using the default candidate cache. For isolated candidate verification, pass `-p:WorksAllowLocalEventStoreCandidate=true` explicitly with the intended version and feed configuration. The existing integration contract requires a `-story415.` candidate version; build admission alone does not qualify a producer. The opt-in admits the historical `.artifacts/story-4-14/feed` and `.artifacts/story-4-15/feed` directories when present and defaults to the separate `.artifacts/sdk-candidate-packages` cache. Ordinary restores ignore those feeds and that cache. Explicitly supplied NuGet sources and `RestorePackagesPath` overrides remain the caller's isolation responsibility; admission does not authenticate arbitrary pre-existing global-cache contents. Source Debug builds retain their existing version behavior.

The local Python tooling tests exercise package inventory, restore guards, fixture builds and verifier behavior. Run `python3 -m unittest discover -s scripts/tests` from the repository root with the required .NET SDKs on PATH. For parity, initialize the listed direct dependencies with their Git history so the bootstrap can select the exact SDK release tag.

## Release packages

`tools/release-packages.json` is the authoritative inventory. One semantic version publishes exactly these five packages:

| Package ID | Purpose |
| --- | --- |
| `Hexalith.Works.Contracts` | Commands, events, models, ports, and value objects |
| `Hexalith.Works.Server` | Pure work-item decision core |
| `Hexalith.Works.Projections` | Read-model projection strategies |
| `Hexalith.Works.Reactor` | Pure cascade and resume translators |
| `Hexalith.Works.Testing` | Infrastructure-free test builders and markers |

Hosts, AppHost resources, samples, service defaults, and test runners are never published.

## Release operations

Hexalith.CiCd schedules releases centrally. The module's `.github/workflows/release.yml` accepts a delivered-state SHA and calls the pinned Hexalith.Builds release workflow. When that workflow authorizes publication, it passes the version, SHA, draft ID and artifact ID, digest and run to the module's `publish` job.

Configure the protected `hexalith-release` environment and the `HEXALITH_NUGET_USER` variable for the matching NuGet trusted-publisher identity. The publishing job requires GitHub OIDC. Before approval, compare the candidate SHA and exact CI/parity results with the shared workflow's prepared draft and artifact. The five-package inventory and the local pack/consumer commands above provide a publication-free rehearsal.

If publication fails after any packages appear, retain the Actions run, draft and artifact identity. Reconcile the published IDs, versions and bytes with the prepared inventory using the shared release owner's recovery process before another attempt. Published NuGet versions are immutable.
