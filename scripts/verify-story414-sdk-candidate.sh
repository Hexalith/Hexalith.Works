#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
export NUGET_PACKAGES="$fixture/packages"
candidate_version="$(python3 - "$repository_root/Directory.Packages.props" <<'PY'
import sys
import xml.etree.ElementTree as ET
print(ET.parse(sys.argv[1]).findtext('PropertyGroup/HexalithEventStoreVersion'))
PY
)"
cat >"$fixture/Consumer.csproj" <<PROJECT
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Hexalith.EventStore.DomainService" Version="$candidate_version" />
    <PackageReference Include="Hexalith.EventStore.Testing" Version="$candidate_version" />
    <PackageReference Include="Hexalith.Parties.Client" Version="1.2.1" />
  </ItemGroup>
</Project>
PROJECT
cat >"$fixture/NuGet.Config" <<CONFIG
<configuration><packageSources><clear /><add key="candidate" value="$repository_root/.artifacts/story-4-14/feed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders><packageSourceMapping><packageSource key="candidate"><package pattern="Hexalith.EventStore.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping></configuration>
CONFIG
cat >"$fixture/Program.cs" <<'CS'
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Parties.Client.Abstractions;

var builder = WebApplication.CreateBuilder();
builder.AddEventStoreDomainService();
_ = typeof(IAsyncDomainSharedProjectionEpochHandler);
_ = typeof(IPartiesIdentityClient);
var store = new InMemoryReadModelStore();
var coordinator = new SharedProjectionEpochCoordinator(store, store);
var scope = new SharedProjectionScope("statestore", "synthetic", "work", "works-runtime-v3", ["writer"]);
var lease = await coordinator.RegisterWriterAsync(scope, "writer");
var accepted = new SharedProjectionDelivery("item", 1, [1], [10]);
if (await coordinator.JournalAsync(scope, lease, accepted) != SharedProjectionJournalResult.Journaled) throw new Exception("journal");
await coordinator.CatchUpAsync(scope, (_, _, _) => Task.FromResult<IReadOnlyList<ReadModelBatchOperation>>([
    ReadModelBatchOperation.Write("queue", new[] { "accepted" }, ReadModelBatchConcurrency.LastWrite)]));
if (await coordinator.JournalAsync(scope, lease, accepted with { CanonicalPayload = [2] }) != SharedProjectionJournalResult.IdentityConflict)
    throw new Exception("conflict");
coordinator = new SharedProjectionEpochCoordinator(store, store);
var read = await coordinator.ReadManyAsync<string[]>(scope, ["queue"]);
if (!read.IsAvailable || !read.IsDegraded || read.Value!["queue"]![0] != "accepted"
    || (await coordinator.GetCheckpointAsync(scope, "item"))!.Token[0] != 10
    || !(await coordinator.GetStatusAsync(scope)).IsDegraded) throw new Exception("durable readiness");
Console.WriteLine("Isolated package-only host consumer: persisted accepted contents/checkpoint and restart-safe conflict readiness passed.");
CS
dotnet run --project "$fixture/Consumer.csproj" --configuration Release -p:NuGetAudit=false

python3 - "$fixture" "$repository_root/.artifacts/story-4-14/feed" "$candidate_version" <<'HASHES'
import hashlib, json, pathlib, sys
fixture, feed = map(pathlib.Path, sys.argv[1:3])
version = sys.argv[3]
assets = json.loads((fixture / 'obj/project.assets.json').read_text())
assert not any(item['type'] == 'project' for item in assets['libraries'].values()), 'Project reference in package-only proof'
matched = []
for identity, item in assets['libraries'].items():
    name, restored_version = identity.split('/')
    if not name.startswith('Hexalith.EventStore.'):
        continue
    assert restored_version == version, identity
    intended = feed / f'{name}.{version}.nupkg'
    restored = fixture / 'packages' / name.lower() / version.lower() / f'{name.lower()}.{version.lower()}.nupkg'
    intended_hash = hashlib.sha256(intended.read_bytes()).hexdigest()
    restored_hash = hashlib.sha256(restored.read_bytes()).hexdigest()
    assert restored_hash == intended_hash, f'Restored archive differs from intended feed: {identity}'
    matched.append({'identity': identity, 'feedSha256': intended_hash, 'isolatedCacheSha256': restored_hash})
assert len(matched) >= 5, 'Incomplete SDK package graph'
print(json.dumps({'isolatedPackageProof': matched}, sort_keys=True))
HASHES
