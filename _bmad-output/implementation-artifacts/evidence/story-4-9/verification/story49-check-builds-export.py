from pathlib import Path
import subprocess,tarfile,io,hashlib,json,datetime
repo=Path('/home/administrator/projects/hexalith/works/references/Hexalith.Builds');export=Path('/tmp/story416-builds-b435f2e-public-source');gate=Path('/tmp/story49-post-cutover-20261020');commit=subprocess.check_output(['git','-C',str(repo),'rev-parse','b435f2e'],text=True).strip()
names=subprocess.check_output(['git','-C',str(repo),'ls-tree','-r','--name-only',commit],text=True).splitlines();folders={'src','Props','Tools','Github','schemas','.config','.github'};names=[n for n in names if len(Path(n).parts)==1 or Path(n).parts[0] in folders]
a=subprocess.check_output(['git','-C',str(repo),'archive',commit,*names]);expected={}
with tarfile.open(fileobj=io.BytesIO(a)) as t:
 for m in t.getmembers():
  if m.isfile() and not {'bin','obj'}.intersection(Path(m.name).parts):expected[m.name]=hashlib.sha256(t.extractfile(m).read()).hexdigest()
actual={n:hashlib.sha256((export/n).read_bytes()).hexdigest() for n in expected}
extra=[str(p.relative_to(export)) for folder in folders for p in (export/folder).rglob('*') if p.is_file() and not {'bin','obj'}.intersection(p.relative_to(export).parts) and str(p.relative_to(export)) not in expected]
if actual!=expected or extra:raise ValueError('Builds export differs from its exact owning Git object: '+str(extra))
record=gate/'builds-source-binding.json';now={'observedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),'gatePhase':json.loads((gate/'ledger.json').read_text())['commands'][-1]['name']}
if record.exists():
 d=json.loads(record.read_text());d['finalObservation']=now|{'unchanged':d['sourceFiles']==actual};record.write_text(json.dumps(d,indent=2)+'\n');assert d['finalObservation']['unchanged']
else:
 record.write_text(json.dumps({'scope':'Independently compare every ordinary root/build/tooling/source/schema input in the selected filesystem Builds export to exact Git archive bytes; no public package claim or revision relabeling. Captured during execution and rechecked at acceptance.','gitObjectRepository':str(repo),'repositoryUrl':'https://github.com/Hexalith/Hexalith.Builds.git','repositoryCommit':commit,'export':str(export),'observation':now,'sourceFiles':actual,'inventorySha256':hashlib.sha256(json.dumps(actual,sort_keys=True).encode()).hexdigest()},indent=2)+'\n')
print(commit,len(actual),'exact unchanged Git source inputs')
