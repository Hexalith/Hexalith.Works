#!/usr/bin/env bash
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
# Compile the selected current source first; stale Release output cannot stand in for this proof.
dotnet build "$repository_root/src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj" --configuration Release -p:NuGetAudit=false
python3 - "$repository_root" "$fixture" <<'PY'
import io, pathlib, re, subprocess, sys, tarfile
root, fixture = map(pathlib.Path, sys.argv[1:])
archive = subprocess.check_output(['git', 'archive', '8679d7d700925a870e9931b3c43c745c063e459b', 'src/Hexalith.Works.Contracts'], cwd=root)
with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
    tar.extractall(fixture / 'baseline', filter='data')
project = fixture / 'baseline/src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj'
project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyVersion>0.0.0.0</AssemblyVersion></PropertyGroup><ItemGroup><PackageReference Include="Hexalith.EventStore.Contracts" Version="3.122.0"/><PackageReference Include="Hexalith.PolymorphicSerializations" Version="1.19.3"/><PackageReference Include="Hexalith.PolymorphicSerializations.CodeGenerators" Version="1.19.3" IncludeAssets="analyzers;build;buildTransitive" ExcludeAssets="compile;runtime;contentFiles;native" PrivateAssets="all"/></ItemGroup></Project>''')
info = (root / 'src/Hexalith.Works.Contracts/obj/Release/net10.0/Hexalith.Works.Contracts.AssemblyInfo.cs').read_text()
version = re.search(r'AssemblyVersionAttribute\("([^"]+)"\)', info).group(1)
project.write_text(project.read_text().replace('<AssemblyVersion>0.0.0.0</AssemblyVersion>', '<AssemblyVersion>' + version + '</AssemblyVersion>'))
(fixture / 'consumer').mkdir()
(fixture / 'consumer/Consumer.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><ProjectReference Include="../baseline/src/Hexalith.Works.Contracts/Hexalith.Works.Contracts.csproj"/></ItemGroup></Project>''')
PY
cat > "$fixture/consumer/Program.cs" <<'CS'
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hexalith.Works.Contracts.Commands;
using Hexalith.Works.Contracts.Events;
using Hexalith.Works.Contracts.ValueObjects;
var tenant = new TenantId("tenant-abi"); var item = new WorkItemId("item-abi"); var schedule = new WorkItemSchedule();
var create = new CreateWorkItem(tenant, item, "proof", null, null, null, null, null, null, 1, 32);
var reschedule = new RescheduleWorkItem(tenant, item, schedule, null);
var expire = new ExpireWorkItem(tenant, item);
var created = new WorkItemCreated(item.Value, 1, tenant, item, new Obligation("proof"), null, null, null, null, null);
var rescheduled = new WorkItemRescheduled(item.Value, 2, tenant, item, schedule, null);
var expired = new WorkItemExpired(item.Value, 3, tenant, item);
var (a,b,c,d,e,f,g,h,i,j,k) = create;
var (l,m,n,o) = reschedule;
var (p,q) = expire;
var (r,s,t,u,v,w,x,y,z,aa) = created;
var (ab,ac,ad,ae,af,ag) = rescheduled;
var (ah,ai,aj,ak) = expired;
if (a != tenant || b != item || c != "proof" || j != 1 || k != 32 || l != tenant || m != item || n != schedule
    || p != tenant || q != item || r != item.Value || s != 1 || t != tenant || u != item || v.Description != "proof"
    || ab != item.Value || ac != 2 || ad != tenant || ae != item || af != schedule || ah != item.Value || ai != 3 || aj != tenant || ak != item)
    throw new Exception("Baseline constructor or deconstruction values changed");
foreach (object payload in new object[]{create,reschedule,expire,created,rescheduled,expired})
{
    string json = JsonSerializer.Serialize(payload, payload.GetType());
    if (json.Contains("Expiry")) throw new Exception("Historical null fields changed");
    Console.WriteLine(payload.GetType().Name + " " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
}
CS
dotnet build "$fixture/consumer/Consumer.csproj" --configuration Release -p:NuGetAudit=false
dotnet "$fixture/consumer/bin/Release/net10.0/Consumer.dll" > "$fixture/baseline.txt"
cp "$repository_root/src/Hexalith.Works.Contracts/bin/Release/net10.0/Hexalith.Works.Contracts.dll" "$fixture/consumer/bin/Release/net10.0/Hexalith.Works.Contracts.dll"
dotnet "$fixture/consumer/bin/Release/net10.0/Consumer.dll" > "$fixture/current.txt"
cmp "$fixture/baseline.txt" "$fixture/current.txt"
cat "$fixture/current.txt"
echo "Unchanged baseline Works Contracts binary passed all six constructors/deconstructions and historical payload hashes."
