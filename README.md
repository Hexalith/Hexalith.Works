# Hexalith.Works

Hexalith.Works is the Hexalith work-item domain module. The repository is an umbrella workspace with root-declared Hexalith submodules and a canonical EventStore domain-service executable. Aspire hosting and Dapr topology belong to Hexalith.Platform.

## Local Debug development

Debug builds consume EventStore and PolymorphicSerializations from sibling source so changes can be debugged across module boundaries. The source graph also needs Builds (central props/tooling), Commons (SDK primitives), Tenants (SDK build inputs), Parties (client/contracts and the integration authority source fixture), and Platform (source-only Identity and Composition test adapters). AI.Tools supplies the required repository baseline. Initialize these root-declared dependencies:

```bash
git submodule update --init references/Hexalith.AI.Tools references/Hexalith.Builds references/Hexalith.Commons references/Hexalith.EventStore references/Hexalith.PolymorphicSerializations references/Hexalith.Tenants references/Hexalith.Parties references/Hexalith.Platform
export HexalithBuildsRoot="$PWD/references/Hexalith.Builds"
dotnet restore Hexalith.Works.slnx -p:Configuration=Debug
dotnet build Hexalith.Works.slnx --configuration Debug --no-restore
```

Do not use recursive submodule initialization.

The deterministic certificate-refusal integration controls require Linux and the `openssl` executable
on `PATH`. Other providers remain inconclusive in the live proof; the native tests explicitly report
unsupported runners, and the repository's `failSkips` setting keeps those runs from qualifying.

## CI Release validation

CI restores and builds Release assets with centrally pinned NuGet references for published external Hexalith libraries. It then packs and validates the Works inventory, builds and executes isolated package-only consumers (including reflected `AssemblyInformationalVersion` checks), executes the release-tooling regression suite, and runs each blocking Microsoft.Testing.Platform project separately. The repository-root `xunit.runner.json` is copied beside every test assembly and sets `failSkips: true`, so a skipped fact fails the same gate as a failed fact.

The blocking `platform-parity` CI job runs Platform's canonical R1–R11 verifier with its `ci` profile: Release/public-package consumer checks and Release Agents probes, every explicit live fact, public producer qualification and complete-fleet rollback. Its bootstrap uses an isolated Platform checkout, an exact public SDK source export, and links to the initialized root dependencies. Missing, skipped, unexecuted or failed required lanes fail the job. The ordinary integration run alone does not qualify parity. Run the same CI profile locally after initializing the dependencies above:

```bash
bash scripts/verify-platform-parity-ci.sh --artifacts-path /tmp/works-platform-proof
```

Required live classes run in that verifier with an explicitly selected, hashed Platform host; an unselected live launch refuses rather than starting a Works-owned topology. Deterministic suites can exclude only the live classes whose required facts run in that same gate. See `docs/platform-hosting-cutover.md` for producer bindings and the historical rollback source archive.

The [Platform profile contract](references/Hexalith.Platform/docs/works-host-parity.md#canonical-local-and-ci-profiles) also preserves the default `full` local gate, which adds source Debug conformance. CI records that source conformance was not requested; it does not claim a Debug pass. Both profiles require all eleven rows and all 44 proof kinds, with the same strict source/runtime/archive/launcher binding. The bootstrap binds its selected export back to the original Platform working overlay and refuses relevant drift in either source.

```bash
git submodule update --init references/Hexalith.AI.Tools references/Hexalith.Builds references/Hexalith.Commons references/Hexalith.EventStore references/Hexalith.PolymorphicSerializations references/Hexalith.Tenants references/Hexalith.Parties references/Hexalith.Platform
export HexalithBuildsRoot="$PWD/references/Hexalith.Builds"
dotnet restore Hexalith.Works.slnx
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
  dotnet test "$project" --configuration Release --no-build
done
```

NuGet audit is enabled in all restore modes with `NuGetAuditMode=all`, so advisories of every severity are reported in the build log. Per the Hexalith.Builds CI/CD standard, `NU1901`-`NU1904` are exempt from the repository-wide warnings-as-errors policy: an advisory warns and does not fail the build, so a transitive advisory that cannot be upgraded immediately never blocks CI. Acknowledge or waive an individual advisory with `<NuGetAuditSuppress>` rather than disabling the audit.

Release restore and compilation require the committed `HexalithEventStoreVersion` in `Directory.Packages.props`. An ambient environment variable or command-line override selecting a different SDK fails with `HXW0003`. Builds without restore also check resolved SDK versions and reject assets still using the default candidate cache. For isolated candidate verification, pass `-p:WorksAllowLocalEventStoreCandidate=true` explicitly with the intended version and feed configuration. The existing integration contract requires a `-story415.` candidate version; build admission alone does not qualify a producer. The opt-in admits the historical `.artifacts/story-4-14/feed` and `.artifacts/story-4-15/feed` directories when present and defaults to the separate `.artifacts/sdk-candidate-packages` cache. Ordinary restores ignore those feeds and that cache. Explicitly supplied NuGet sources and `RestorePackagesPath` overrides remain the caller's isolation responsibility; admission does not authenticate arbitrary pre-existing global-cache contents. Source Debug builds retain their existing version behavior.

The release-tooling tests execute package-free `Build,Pack` solution targets and fresh-cache restore controls. They also compare the effective initialization, SDK selection and restore/build steps from the exact Builds commits selected by CI and release preparation. Initialize the root Builds submodule with its Git history before running `python3 -m unittest discover -s scripts/tests`; missing producer objects fail the compatibility check.

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

As checked against the NuGet.org flat-container indexes on 2026-09-20, all five package IDs are currently unpublished: `Hexalith.Works.Contracts` (HTTP 404), `Hexalith.Works.Server` (HTTP 404), `Hexalith.Works.Projections` (HTTP 404), `Hexalith.Works.Reactor` (HTTP 404), and `Hexalith.Works.Testing` (HTTP 404). Registry absence is status only; it is not publication authority.

## Manual release prerequisites

Release is operator-only through the `Release` workflow. Before an authorized dispatch, repository owners must configure all of the following:

- A protected `production` environment with required reviewers and a `main`-only deployment policy. Both the
  publishing job and the post-publication verification job reference this environment. Verification always runs
  after the release job starts and does not re-read the mutable publication-freeze variable, so a later variable
  edit cannot suppress checks for an already-started publication. With required reviewers configured, expect
  **two** approval prompts in one release run: one before publication and one before verification.
- A repository-scoped `NUGET_USER` variable naming the NuGet.org publishing account, with a NuGet
  trusted-publisher policy matching this GitHub repository, the `release.yml` workflow, and the
  `production` environment. The workflow uses GitHub OIDC through `NuGet/login` to obtain its temporary
  `NUGET_API_KEY`; repository owners do not provision a long-lived API-key secret for this workflow.
- A repository-scoped `HEXALITH_RELEASE_PUBLISH_ENABLED` variable set to the exact string `true`.
- A successful push `CI` run for the exact current `main` commit.

The workflow rejects a non-`main` or stale dispatch before requesting environment approval, fails before any NuGet write if any one of the five ID/version pairs already exists, and verifies that the release tag resolves to the dispatched commit and all five exact packages become readable afterward. Publication is never triggered by a push.

Publication pushes exactly the manifest packages and their `.snupkg` symbol packages through
`scripts/push-release-packages.sh`, never a `*.nupkg` glob, so an unvalidated archive left in the output directory
can never be published. Packing first freezes the SHA-256 of all ten candidate archives in
`nupkgs/release-artifacts.sha256`; publication refuses any changed or extra candidate. `--skip-duplicate` is
deliberately absent: an ordinary version collision must fail loudly.

**Partial-publication recovery.** NuGet has no transaction, so a failure part-way can leave some packages live and
immutable. The publisher retries an unchanged ledger-verified artifact up to three times in the same invocation
only after a fail-closed classifier recognizes a bounded transport failure, HTTP 408/429, or HTTP 5xx response.
Every 409 Conflict is a collision and fails, including one after an ambiguous attempt: local runner history cannot
authenticate the bytes stored remotely. The hosted runner and its candidate ledger are ephemeral, so retry
exhaustion or any 409 is not recoverable by rerunning the workflow or by reusing a local ledger. Do not delete
published packages or push different content at the incomplete version. Publish a new patch version that contains
all five packages, and leave the incomplete version unlisted so consumers never resolve it.
