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

CI restores and builds Release assets with centrally pinned NuGet references for published external Hexalith libraries. It then packs and validates the Works inventory, builds isolated package-only consumers, and runs each blocking Microsoft.Testing.Platform project separately:

```bash
dotnet restore Hexalith.Works.slnx
dotnet build Hexalith.Works.slnx --configuration Release --no-restore -warnaserror -m:1
python3 scripts/pack-release-packages.py ./nupkgs 0.0.0-ci-test
python3 scripts/validate-nuget-packages.py ./nupkgs
python3 scripts/validate-consumer-package-references.py ./nupkgs

for project in \
  tests/Hexalith.Works.UnitTests \
  tests/Hexalith.Works.ArchitectureTests \
  tests/Hexalith.Works.PropertyTests \
  tests/Hexalith.Works.IntegrationTests; do
  dotnet test "$project" --configuration Release --no-build
done
```

NuGet audit is enabled in all restore modes. Advisories remain visible, while `NU1901` through `NU1904` are not promoted by the repository-wide warnings-as-errors policy.

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

As checked against the NuGet.org flat-container indexes on 2026-09-18, all five package IDs are currently unpublished: `Hexalith.Works.Contracts` (HTTP 404), `Hexalith.Works.Server` (HTTP 404), `Hexalith.Works.Projections` (HTTP 404), `Hexalith.Works.Reactor` (HTTP 404), and `Hexalith.Works.Testing` (HTTP 404). Registry absence is status only; it is not publication authority.

## Manual release prerequisites

Release is operator-only through the `Release` workflow. Before an authorized dispatch, repository owners must configure all of the following:

- A protected `production` environment with required reviewers and a `main`-only deployment policy.
- An explicit `NUGET_API_KEY` secret.
- A repository-scoped `HEXALITH_RELEASE_PUBLISH_ENABLED` variable set to the exact string `true`.
- A successful push `CI` run for the exact current `main` commit.

The workflow rejects a non-`main` or stale dispatch before requesting environment approval, fails before any NuGet write if any one of the five ID/version pairs already exists, and verifies that the release tag resolves to the dispatched commit and all five exact packages become readable afterward. Publication is never triggered by a push.
