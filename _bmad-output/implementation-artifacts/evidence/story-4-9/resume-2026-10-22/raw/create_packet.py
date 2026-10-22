from pathlib import Path
import datetime,gzip,hashlib,json,shutil,subprocess,xml.etree.ElementTree as ET
root=Path('/home/administrator/projects/hexalith/works')
packet=root/'_bmad-output/implementation-artifacts/evidence/story-4-9/resume-2026-10-22'
packet.mkdir(parents=True,exist_ok=False)
raw=packet/'raw';raw.mkdir()
source=packet/'sources';source.mkdir()
logs=sorted(Path('/tmp').glob('story49-current-*.log'))+sorted(Path('/tmp').glob('story49-current-*.xml'))
for original in logs:
 (raw/(original.name+'.gz')).write_bytes(gzip.compress(original.read_bytes(),mtime=0))
parent=Path('/tmp/story49-resume-20261022-b5dnz4kb')
for original in parent.iterdir():
 if original.is_file() and original.suffix in ('.json','.py'):
  shutil.copy2(original,raw/original.name)
for original in (Path('/tmp/story49-current-linked-helper-audit.json'),Path('/tmp/story49-current-custody-copy-root.txt')):
 shutil.copy2(original,raw/original.name)
copy=Path('/tmp/story49-current-custody-copy-root.txt').read_text().strip()
shutil.copy2(Path(copy)/'source-copy-binding.json',raw/'custody-owner-copy-binding.json')
owners={'Works':root,'EventStore':root/'references/Hexalith.EventStore','Platform':root/'references/Hexalith.Platform'}
for name,owner in owners.items():
 (source/(name+'.patch')).write_bytes(subprocess.check_output(['git','-C',str(owner),'diff','--no-ext-diff','--ignore-submodules=all']))
changes={}
for name,owner in owners.items():
 files=subprocess.check_output(['git','-C',str(owner),'diff','--name-only','--ignore-submodules=all'],text=True).splitlines()
 changes[name]={p:hashlib.sha256((owner/p).read_bytes()).hexdigest() for p in files if (owner/p).is_file()}
(packet/'source-inputs.json').write_text(json.dumps(changes,indent=2)+'\n')
assemblies={}
for name in ('architecture','integration','eventstore-authority','platform-custody'):
 original=Path('/tmp/story49-current-'+name+'.xml')
 if original.is_file():
  assemblies[name]=[{key:node.get(key) for key in ('total','passed','failed','skipped','not-run','errors','time')} for node in ET.parse(original).getroot().findall('assembly')]
summary={'observedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'nonqualifying; public fixed producer unavailable','tests':assemblies,'sourceMode':'Debug/source; no public-producer qualification claimed','fullAndCiProfilesRun':False,'pending':['Published EventStore producer containing retained-seal fix','Owner commits and dependency adoption as applicable','Fresh full canonical gate followed by actual CI profile on the same unchanged inputs']}
(packet/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
manifest={}
for p in sorted(packet.rglob('*')):
 if p.is_file():
  b=p.read_bytes();entry={'sha256':hashlib.sha256(b).hexdigest(),'bytes':len(b)}
  if p.suffix=='.gz':entry['uncompressedSha256']=hashlib.sha256(gzip.decompress(b)).hexdigest()
  manifest[str(p.relative_to(packet))]=entry
(packet/'manifest.json').write_text(json.dumps({'files':manifest},indent=2)+'\n')
print(packet)
