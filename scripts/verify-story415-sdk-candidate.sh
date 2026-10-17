#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
export NUGET_PACKAGES="$fixture/packages"
export NUGET_HTTP_CACHE_PATH="$fixture/http-cache"
sdk_version="$(python3 - "$repository_root/Directory.Packages.props" <<'PY'
import sys
import xml.etree.ElementTree as ET
print(ET.parse(sys.argv[1]).findtext('PropertyGroup/HexalithEventStoreVersion'))
PY
)"
parties_version="$(python3 - "$repository_root/Directory.Packages.props" <<'PY'
import sys
import xml.etree.ElementTree as ET
print(ET.parse(sys.argv[1]).findtext('PropertyGroup/HexalithPartiesVersion'))
PY
)"
verification_feed="$repository_root/.artifacts/story-4-15/feed"
if [[ "$sdk_version" != *-* ]]; then
    verification_feed="$fixture/public-feed"
    mkdir -p "$verification_feed"
fi
cat >"$fixture/Consumer.csproj" <<PROJECT
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Hexalith.EventStore.DomainService" Version="$sdk_version" />
    <PackageReference Include="Hexalith.EventStore.Testing" Version="$sdk_version" />
    <PackageReference Include="Hexalith.Parties.Client" Version="$parties_version" />
  </ItemGroup>
</Project>
PROJECT
python3 - "$repository_root" "$fixture/NuGet.Config" "$sdk_version" <<'CONFIG'
import pathlib, sys
import xml.etree.ElementTree as ET
configuration = ET.Element('configuration')
sources = ET.SubElement(configuration, 'packageSources')
ET.SubElement(sources, 'clear')
if '-' in sys.argv[3]:
    ET.SubElement(sources, 'add', key='candidate', value=str(pathlib.Path(sys.argv[1]) / '.artifacts/story-4-15/feed'))
ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
ET.SubElement(ET.SubElement(configuration, 'fallbackPackageFolders'), 'clear')
mapping = ET.SubElement(configuration, 'packageSourceMapping')
if '-' in sys.argv[3]:
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='candidate'), 'package', pattern='Hexalith.EventStore.*')
ET.SubElement(ET.SubElement(mapping, 'packageSource', key='nuget.org'), 'package', pattern='*')
ET.ElementTree(configuration).write(sys.argv[2], encoding='utf-8', xml_declaration=True)
CONFIG
cat >"$fixture/Program.cs" <<'CS'
using Hexalith.EventStore.Client.Projections;
using Hexalith.EventStore.Client.Processes;
using Hexalith.EventStore.Client.Reminders;
using Hexalith.EventStore.Client.Effects;
using Hexalith.EventStore.Contracts.Effects;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.DomainService;
using Hexalith.EventStore.Testing.Fakes;
using Hexalith.Parties.Client.Abstractions;

var intent = new ReminderIntent("synthetic", "work", "item", DateTimeOffset.UnixEpoch,
    EffectKindCatalog.DateResume, "Resume", [1], "work", "item", 1, 1) { Ordinal = 1, SourceMessageId = "committed-source" };
if (intent.Ordinal != 1 || intent.SourceMessageId != "committed-source"
    || typeof(IReminderTargetCatalog).GetMethod("ListAsync") is null
    || typeof(SharedProjectionEpochCoordinator).GetMethod("GetAvailableControlIndexTenantsAsync") is null
    || typeof(SharedProjectionEpochCoordinator).GetMethod("ReadDiscoveryAsync") is null
    || new SharedProjectionControlIndexReadResult([], true).IsIncomplete != true
    || new ProcessEvidenceUnavailableException(new HttpRequestException()).InnerException is not HttpRequestException
    || typeof(Hexalith.EventStore.Client.Streams.AuthoritativeStreamReadDeadline).GetMethod("ReadWithAdmissionAsync") is null
    || !(new ProcessSourcePage([], null) { IsIncomplete = true }).IsIncomplete
    || typeof(ProcessSourceState).GetProperty("PendingPlan")?.PropertyType != typeof(ProcessEnvelopePlan)
    || typeof(TrustedEffectOrigin).GetProperty("CommandDigest") is null || TrustedEffectOriginScope.Current is not null)
    throw new Exception("Story 4.15 public adapter contracts");
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
var discovery = await coordinator.ReadDiscoveryAsync<string[]>(scope, "queue");
if (!discovery.IsAvailable || !discovery.IsDegraded || discovery.Value![0] != "accepted")
    throw new Exception("bounded selected discovery");
var read = await coordinator.ReadManyAsync<string[]>(scope, ["queue"]);
if (!read.IsAvailable || !read.IsDegraded || read.Value!["queue"]![0] != "accepted"
    || (await coordinator.GetCheckpointAsync(scope, "item"))!.Token[0] != 10
    || !(await coordinator.GetStatusAsync(scope)).IsDegraded) throw new Exception("durable readiness");
Console.WriteLine("Isolated package-only host consumer: persisted accepted contents/checkpoint and restart-safe conflict readiness passed.");
CS
dotnet run --project "$fixture/Consumer.csproj" --configuration Release -p:NuGetAudit=false

python3 - "$fixture" "$verification_feed" "$sdk_version" <<'HASHES'
import hashlib, json, pathlib, sys, urllib.request
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
    if '-' in version:
        archive_source = str(intended)
    else:
        archive_source = f'https://api.nuget.org/v3-flatcontainer/{name.lower()}/{version.lower()}/{name.lower()}.{version.lower()}.nupkg'
        assert not intended.exists(), f'Public verification archive already exists: {intended}'
        with urllib.request.urlopen(archive_source, timeout=30) as response:
            intended.write_bytes(response.read())
    intended_hash = hashlib.sha256(intended.read_bytes()).hexdigest()
    restored_hash = hashlib.sha256(restored.read_bytes()).hexdigest()
    assert restored_hash == intended_hash, f'Restored archive differs from selected source: {identity}'
    matched.append({'identity': identity, 'source': archive_source, 'sourceSha256': intended_hash, 'isolatedCacheSha256': restored_hash})
assert len(matched) >= 5, 'Incomplete SDK package graph'
print(json.dumps({'isolatedPackageProof': matched}, sort_keys=True))
HASHES

# Run the upstream public consumer probes for the three adapter API packages. Stable releases
# use the independently verified public archives above; prereleases keep the immutable candidate feed.
python3 - "$repository_root" "$sdk_version" "$verification_feed" <<'CONSUMERS'
import importlib.util, pathlib, sys
root = pathlib.Path(sys.argv[1])
script = root / 'references/Hexalith.EventStore/scripts/validate-consumer-package-references.py'
spec = importlib.util.spec_from_file_location('story415_public_consumers', script)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
from release_package_contract import read_package_metadata
feed = pathlib.Path(sys.argv[3])
for suffix in ['Contracts', 'Client', 'DomainService']:
    archive = feed / f'Hexalith.EventStore.{suffix}.{sys.argv[2]}.nupkg'
    module.validate_library_package(read_package_metadata(archive), feed)
    print(f'Passed isolated upstream public {suffix} consumer at {sys.argv[2]}', flush=True)
CONSUMERS

# Compile against the published eleven-parameter contract once, then run the unchanged binary
# with only the selected Contracts assembly substituted. This proves CLR constructor and
# Deconstruct compatibility in addition to the current source consumer above.
mkdir -p "$fixture/abi"
cat >"$fixture/abi/Abi.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="Hexalith.EventStore.Contracts" Version="3.122.0" /></ItemGroup>
</Project>
PROJECT
cat >"$fixture/abi/NuGet.Config" <<'CONFIG'
<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping><clear /></packageSourceMapping></configuration>
CONFIG
cat >"$fixture/abi/Program.cs" <<'CS'
using System.Security.Cryptography;
using System.Text.Json;
using Hexalith.EventStore.Contracts.Reminders;
using Hexalith.EventStore.Contracts.Effects;
var value = new ReminderIntent("tenant-a", "widget", "item", DateTimeOffset.UnixEpoch,
    EffectKindCatalog.DateResume, "Resume", [1, 2, 3], "widget", "item", 3, 1);
var (tenant, domain, item, due, kind, type, payload, sourceDomain, sourceItem, sequence, revision) = value;
if (tenant != "tenant-a" || domain != "widget" || item != "item" || due != DateTimeOffset.UnixEpoch
    || kind != EffectKindCatalog.DateResume || type != "Resume" || payload.Length != 3
    || sourceDomain != "widget" || sourceItem != "item" || sequence != 3 || revision != 1)
    throw new Exception("Original eleven-value constructor/deconstruction failed");
Console.WriteLine(ReminderIdentityCodec.ComputeReminderName(value));
Console.WriteLine(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))));
CS
dotnet build "$fixture/abi/Abi.csproj" --configuration Release -p:NuGetAudit=false
dotnet "$fixture/abi/bin/Release/net10.0/Abi.dll" >"$fixture/abi-baseline.txt"
python3 - "$verification_feed/Hexalith.EventStore.Contracts.$sdk_version.nupkg" "$fixture/abi/bin/Release/net10.0/Hexalith.EventStore.Contracts.dll" <<'ASSEMBLY'
import pathlib, sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    pathlib.Path(sys.argv[2]).write_bytes(archive.read('lib/net10.0/Hexalith.EventStore.Contracts.dll'))
ASSEMBLY
dotnet "$fixture/abi/bin/Release/net10.0/Abi.dll" >"$fixture/abi-selected.txt"
cmp "$fixture/abi-baseline.txt" "$fixture/abi-selected.txt"
echo "Unchanged published-contract binary passed selected constructor/deconstruction and historical identity/serialization hash compatibility."
