"""Run the unchanged token regressions against an official NuGet SDK archive."""
import hashlib
import json
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

if len(sys.argv) not in (3, 4):
    raise SystemExit('usage: verify VERSION EXPECTED_PACKAGE_COMMIT [REGRESSION_SOURCE_COMMIT]')
version, expected_commit = sys.argv[1:3]
test_commit = sys.argv[3] if len(sys.argv) == 4 else expected_commit
owner = Path('/home/administrator/projects/hexalith/works/references/Hexalith.EventStore')
root = Path('/tmp') / ('story49-published-sdk-' + version)
root.mkdir(exist_ok=False)
artifacts = owner / '.artifacts' / ('story49-published-sdk-' + version)
records = {'version': version, 'repositoryCommit': expected_commit, 'packages': {}, 'commands': []}

def save():
    (root / 'verification.json').write_text(json.dumps(records, indent=2) + '\n')

def run(name, command):
    with (root / (name + '.log')).open('w') as stream:
        result = subprocess.run([str(x) for x in command], cwd=owner, stdout=stream, stderr=subprocess.STDOUT)
    records['commands'].append({'name': name, 'command': [str(x) for x in command], 'cwd': str(owner), 'exitCode': result.returncode})
    save()
    if result.returncode:
        print((root / (name + '.log')).read_text()[-4000:])
        raise SystemExit(result.returncode)

for package in ('Hexalith.EventStore.Aspire', 'Hexalith.EventStore.Client', 'Hexalith.EventStore.DomainService'):
    name = package.lower()
    url = f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg'
    target = root / f'{package}.{version}.nupkg'
    with urllib.request.urlopen(url, timeout=120) as response:
        target.write_bytes(response.read())
    with zipfile.ZipFile(target) as archive:
        manifest = ET.fromstring(archive.read(next(n for n in archive.namelist() if n.endswith('.nuspec'))))
        repo = manifest.find('.//{*}repository')
        assert repo is not None and repo.get('commit') == expected_commit, (package, repo.attrib if repo is not None else None)
        assert manifest.findtext('.//{*}version') == version
    records['packages'][package] = {'url': url, 'sha256': hashlib.sha256(target.read_bytes()).hexdigest(), 'repository': repo.attrib}
    save()

source_path = 'tests/Hexalith.EventStore.AppHost.Tests/Configuration/AppChannelTokenAspireExtensionTests.cs'
source = subprocess.check_output(['git', 'show', test_commit + ':' + source_path], cwd=owner)
(root / 'AppChannelTokenAspireExtensionTests.cs').write_bytes(source)
records['testSource'] = {'path': source_path, 'commit': test_commit, 'sha256': hashlib.sha256(source).hexdigest()}
(root / 'NuGet.Config').write_text('<?xml version="1.0"?><configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>\n')
(root / 'PublishedSdk.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><IsTestProject>true</IsTestProject>
    <AssemblyName>Hexalith.EventStore.AppHost.Tests</AssemblyName><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App"/>
    <PackageReference Include="Hexalith.EventStore.Aspire" Version="{version}"/>
    <PackageReference Include="xunit.v3" Version="4.0.2"/>
    <PackageReference Include="Shouldly" Version="4.3.0"/>
    <Using Include="Xunit"/><Using Include="Shouldly"/>
  </ItemGroup>
</Project>\n''')
run('build', ['dotnet', 'build', root / 'PublishedSdk.csproj', '-c', 'Release', '-m:1', '--artifacts-path', artifacts])
binroot = artifacts / 'bin/PublishedSdk/release'
assembly = binroot / 'Hexalith.EventStore.Aspire.dll'
with zipfile.ZipFile(root / f'Hexalith.EventStore.Aspire.{version}.nupkg') as archive:
    package_assembly = archive.read('lib/net10.0/Hexalith.EventStore.Aspire.dll')
assert assembly.read_bytes() == package_assembly, 'Executed SDK differs from the official package'
records['executedSdkSha256'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
deps = json.loads((binroot / 'Hexalith.EventStore.AppHost.Tests.deps.json').read_text())
assert deps['libraries'][f'Hexalith.EventStore.Aspire/{version}']['type'] == 'package'
run('tests', [binroot / 'Hexalith.EventStore.AppHost.Tests', '-noLogo', '-class', 'Hexalith.EventStore.AppHost.Tests.Configuration.AppChannelTokenAspireExtensionTests', '-result-xml', root / 'tests.xml'])
result = ET.parse(root / 'tests.xml').getroot().find('assembly')
assert result is not None
records['tests'] = dict(result.attrib)
assert result.get('total') == '8' and result.get('passed') == '8'
assert all(result.get(k, '0') == '0' for k in ('failed', 'errors', 'skipped', 'not-run'))
records['status'] = 'passed-official-package-token-registration-regressions'
save()
print(json.dumps({'evidence': str(root), 'status': records['status'], 'tests': 8}, indent=2))
