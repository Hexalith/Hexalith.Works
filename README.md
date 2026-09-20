# Hexalith.Works

Hexalith.Works is the Hexalith work-item domain module. The repository is an umbrella workspace with root-declared Hexalith submodules and a Works-specific Aspire host for local and automated validation.

## Local Debug development

Debug builds consume EventStore and PolymorphicSerializations from sibling source so changes can be debugged across module boundaries. Initialize only the root-declared dependencies needed by Works:

```bash
git submodule update --init references/Hexalith.EventStore references/Hexalith.PolymorphicSerializations references/Hexalith.Tenants
dotnet restore Hexalith.Works.slnx -p:Configuration=Debug
dotnet build Hexalith.Works.slnx --configuration Debug --no-restore
```

Do not use recursive submodule initialization.

## CI Release validation

CI restores and builds Release assets with centrally pinned NuGet references for published external Hexalith libraries. It then packs and validates the Works inventory, builds and executes isolated package-only consumers (including reflected `AssemblyInformationalVersion` checks), executes the release-tooling regression suite, and runs each blocking Microsoft.Testing.Platform project separately. The repository-root `xunit.runner.json` is copied beside every test assembly and sets `failSkips: true`, so a skipped fact fails the same gate as a failed fact.

`Hexalith.EventStore.Operations` is an unpackageable executable topology resource. The AppHost locates it through
project metadata and builds it only when the live topology starts; it is not a compile-time ProjectReference in the
Release graph. The EventStore submodule is therefore still required by the Integration lane:

```bash
git submodule update --init references/Hexalith.EventStore references/Hexalith.PolymorphicSerializations references/Hexalith.Tenants
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
- An explicit `NUGET_API_KEY` secret.
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
after ambiguous transport or server failures. A first-attempt 409 Conflict is always a collision and fails; a 409
is accepted only when that same invocation already sent the same unchanged artifact and received an ambiguous
result, because only then might the prior request have committed. The hosted runner and its candidate ledger are
ephemeral, so retry exhaustion is not recoverable by rerunning the workflow or by reusing a local ledger. Do not
delete published packages or push different content at the incomplete version. Publish a new patch version that
contains all five packages, and leave the incomplete version unlisted so consumers never resolve it.
