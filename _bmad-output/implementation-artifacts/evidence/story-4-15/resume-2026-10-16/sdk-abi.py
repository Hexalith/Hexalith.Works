from pathlib import Path
import subprocess,zipfile,hashlib
p=Path(__file__).parent
project=p/'sdk-abi/Consumer.csproj'
subprocess.run(['dotnet','build',str(project),'--configuration','Release','-p:NuGetAudit=false'],check=True)
binary=p/'sdk-abi/bin/Release/net10.0/Consumer.dll'
baseline=subprocess.check_output(['dotnet',str(binary)])
archive=Path('/home/administrator/projects/hexalith/works/.artifacts/story-4-15/feed/Hexalith.EventStore.Contracts.3.122.1-story415.20261016.10.nupkg')
with zipfile.ZipFile(archive) as z:
 replacement=z.read('lib/net10.0/Hexalith.EventStore.Contracts.dll')
(p/'sdk-abi/bin/Release/net10.0/Hexalith.EventStore.Contracts.dll').write_bytes(replacement)
current=subprocess.check_output(['dotnet',str(binary)])
assert baseline==current,(baseline,current)
(p/'sdk-abi-baseline.txt').write_bytes(baseline);(p/'sdk-abi-current.txt').write_bytes(current)
print(current.decode(),end='')
print('Unchanged published-baseline binary passed original 11-parameter constructor, 11-value deconstruction and historical identity/JSON hash against selected candidate.')
print('Consumer SHA-256:',hashlib.sha256(binary.read_bytes()).hexdigest())
print('Candidate DLL SHA-256:',hashlib.sha256(replacement).hexdigest())
